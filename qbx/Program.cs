using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Qbx;

/// <summary>
/// Sends one qbXML request file to QuickBooks Desktop and prints the response.
/// Talks to the SDK's QBXMLRP2 COM object by late binding, so no Intuit interop DLL is needed.
/// </summary>
public static class Program
{
    private const string Usage = """
        qbx - send qbXML to QuickBooks Desktop

        Usage:
          qbx [options] <request.xml | ->   send a request, print the response
          qbx [options] versions            print the qbXML versions QuickBooks supports
          qbx [options] company             print the name of the open company

        Options:
          -f, --file <path>      company file (default: the one open in QuickBooks)
          -o, --out <path>       write the response here instead of stdout
          -w, --write            allow requests other than *QueryRq (add, mod, delete, void)
          -a, --app-name <name>  name shown in the QuickBooks authorization prompt (default: qbx)
          -e, --expect-company <name>
                                 refuse to run unless the open company has this name
          -c, --check            only check the request against the qbXML schema; QuickBooks is not needed
              --no-validate      send without checking the request against the schema

        Read-only by default: a request is refused unless every message ends in QueryRq.
        Requests for qbXML 13.0 and older are checked against the 13.0 schema before they are sent.
        Exit codes: 0 ok, 1 usage, schema, connection or wrong-company error, 2 QuickBooks returned an error status.
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        // Company data is often non-ASCII; keep redirected output UTF-8 rather than the OEM code page.
        Console.OutputEncoding = new UTF8Encoding(false);

        string companyFile = "";
        string? outPath = null;
        string appName = "qbx";
        string? expectCompany = null;
        var allowWrite = false;
        var checkOnly = false;
        var validate = true;
        string? input = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-f" or "--file" when i + 1 < args.Length:
                    companyFile = args[++i];
                    break;
                case "-o" or "--out" when i + 1 < args.Length:
                    outPath = args[++i];
                    break;
                case "-a" or "--app-name" when i + 1 < args.Length:
                    appName = args[++i];
                    break;
                case "-e" or "--expect-company" when i + 1 < args.Length:
                    expectCompany = args[++i];
                    break;
                case "-w" or "--write":
                    allowWrite = true;
                    break;
                case "-c" or "--check":
                    checkOnly = true;
                    break;
                case "--no-validate":
                    validate = false;
                    break;
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    if (input != null || (args[i].StartsWith('-') && args[i] != "-"))
                    {
                        Console.Error.WriteLine(Usage);
                        return 1;
                    }

                    input = args[i];
                    break;
            }
        }

        if (input == null)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string? request = null;
        if (input is not ("versions" or "company"))
        {
            request = input == "-" ? Console.In.ReadToEnd() : File.ReadAllText(input);

            XDocument doc;
            try
            {
                doc = XDocument.Parse(request);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Request is not valid XML: {ex.Message}");
                return 1;
            }

            if (validate || checkOnly)
            {
                var errors = QbxmlSchema.Validate(doc, request);
                if (errors == null)
                {
                    Console.Error.WriteLine($"Not checked: the schema here is qbXML {QbxmlSchema.Version}, the request asks for a newer version.");
                }
                else if (errors.Count > 0)
                {
                    Console.Error.WriteLine($"Request does not match the qbXML {QbxmlSchema.Version} schema:");
                    errors.ForEach(e => Console.Error.WriteLine("  " + e));
                    return 1;
                }
            }

            if (checkOnly)
                return 0;

            var refusal = CheckReadOnly(doc, allowWrite);
            if (refusal != null)
            {
                Console.Error.WriteLine(refusal);
                return 1;
            }
        }
        else if (checkOnly)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        try
        {
            using var qb = new QbSession(appName, companyFile);

            if (expectCompany != null || input == "company")
            {
                var company = qb.CompanyName();
                if (input == "company")
                    Console.WriteLine(company);
                if (expectCompany != null && company != expectCompany)
                {
                    Console.Error.WriteLine($"Refused: QuickBooks has \"{company}\" open, expected \"{expectCompany}\".");
                    return 1;
                }
            }

            if (request == null)
            {
                if (input == "versions")
                    Console.WriteLine(string.Join(Environment.NewLine, qb.SupportedVersions()));
                return 0;
            }

            var response = qb.ProcessRequest(request);
            if (outPath != null)
                File.WriteAllText(outPath, response, new UTF8Encoding(false));
            else
                Console.WriteLine(response);

            return ReportStatus(response);
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException { InnerException: { } innerEx } ? innerEx : ex;
            Console.Error.WriteLine($"QuickBooks error: {inner.Message}");
            if (inner is COMException com)
                Console.Error.WriteLine($"HRESULT 0x{com.HResult:X8}");
            return 1;
        }
    }

    /// <summary>Returns a refusal message, or null when the request may be sent.</summary>
    private static string? CheckReadOnly(XDocument doc, bool allowWrite)
    {
        var messages = doc.Root?.Element("QBXMLMsgsRq")?.Elements().ToList();
        if (messages == null || messages.Count == 0)
            return "Request has no messages under QBXML/QBXMLMsgsRq.";

        if (allowWrite)
            return null;

        var writes = messages
            .Select(m => m.Name.LocalName)
            .Where(name => !name.EndsWith("QueryRq", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        return writes.Count == 0
            ? null
            : $"Refused: {string.Join(", ", writes)} would change data. Pass --write to allow it.";
    }

    /// <summary>Prints one line per non-zero status to stderr; returns 2 if any is an error.</summary>
    private static int ReportStatus(string response)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(response);
        }
        catch
        {
            return 0;
        }

        var hasError = false;
        foreach (var rs in doc.Descendants().Where(e => e.Attribute("statusCode") != null))
        {
            if (rs.Attribute("statusCode")!.Value == "0")
                continue;

            var severity = rs.Attribute("statusSeverity")?.Value ?? "";
            hasError |= severity == "Error";
            var requestId = rs.Attribute("requestID")?.Value;
            Console.Error.WriteLine(
                $"{rs.Name.LocalName}{(requestId != null ? $" [{requestId}]" : "")}: "
                + $"{severity} {rs.Attribute("statusCode")!.Value} {rs.Attribute("statusMessage")?.Value}");
        }

        return hasError ? 2 : 0;
    }
}

internal sealed class QbSession : IDisposable
{
    // QBXMLRPConnectionType.localQBD
    private const int LocalQbd = 1;

    // QBFileMode.qbFileOpenDoNotCare
    private const int FileOpenDoNotCare = 2;

    private readonly object _rp;
    private readonly string? _ticket;
    private bool _connected;

    public QbSession(string appName, string companyFile)
    {
        var type = Type.GetTypeFromProgID("QBXMLRP2.RequestProcessor")
            ?? throw new InvalidOperationException(
                $"QuickBooks SDK (QBXMLRP2) is not registered for this {(Environment.Is64BitProcess ? "64" : "32")}-bit process. "
                + "QuickBooks before 2022 needs the win-x86 build; 2022 and later need win-x64.");

        _rp = Activator.CreateInstance(type)!;
        try
        {
            Call("OpenConnection2", "", appName, LocalQbd);
            _connected = true;
            _ticket = (string)Call("BeginSession", companyFile, FileOpenDoNotCare)!;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string ProcessRequest(string request) => (string)Call("ProcessRequest", _ticket!, request)!;

    public string CompanyName()
    {
        var response = ProcessRequest($"""
            <?xml version="1.0" encoding="utf-8"?>
            <?qbxml version="{QbxmlSchema.Version}"?>
            <QBXML>
              <QBXMLMsgsRq onError="stopOnError">
                <CompanyQueryRq><IncludeRetElement>CompanyName</IncludeRetElement></CompanyQueryRq>
              </QBXMLMsgsRq>
            </QBXML>
            """);
        return XDocument.Parse(response).Descendants("CompanyName").FirstOrDefault()?.Value ?? "";
    }

    public IEnumerable<string> SupportedVersions()
    {
        var versions = _rp.GetType().InvokeMember(
            "QBXMLVersionsForSession", BindingFlags.GetProperty, null, _rp, [_ticket!]);
        return versions is Array array ? array.Cast<object>().Select(v => v.ToString()!) : [];
    }

    public void Dispose()
    {
        try
        {
            if (_ticket != null)
                Call("EndSession", _ticket);
            if (_connected)
                Call("CloseConnection");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: closing QuickBooks session failed: {ex.Message}");
        }
        finally
        {
            Marshal.FinalReleaseComObject(_rp);
        }
    }

    private object? Call(string method, params object[] args) =>
        _rp.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, _rp, args);
}
