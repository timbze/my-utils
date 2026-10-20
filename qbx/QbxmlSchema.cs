using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Qbx;

/// <summary>
/// Checks requests against Intuit's qbXML schema, which is embedded from schema/*.xsd.
/// QuickBooks only says "error parsing the XML text stream"; the schema names the line and what it expected.
/// </summary>
internal static partial class QbxmlSchema
{
    public const string Version = "13.0";

    /// <summary>Returns the schema errors in the request (empty when it is valid), or null when it asks for a qbXML version newer than the schema.
    /// Older requests are checked against 13.0, which catches order and type errors but not elements newer than the version asked for.</summary>
    public static List<string>? Validate(XDocument doc, string request)
    {
        var version = RequestVersion(doc);
        if (version == null)
            return ["no <?qbxml version=\"...\"?> line; QuickBooks needs one"];
        if (version > decimal.Parse(Version, CultureInfo.InvariantCulture))
            return null;

        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = Load() };
        settings.ValidationEventHandler += (_, e) => errors.Add($"line {e.Exception.LineNumber}: {e.Message}");

        using var reader = XmlReader.Create(new StringReader(request), settings);
        while (reader.Read())
        {
        }

        return errors;
    }

    /// <summary>The version in the request's &lt;?qbxml version="..."?&gt; line, or null when there is none.</summary>
    private static decimal? RequestVersion(XDocument doc)
    {
        var pi = doc.Nodes().OfType<XProcessingInstruction>().FirstOrDefault(p => p.Target == "qbxml");
        var match = pi == null ? null : VersionRegex().Match(pi.Data);
        return match is { Success: true } && decimal.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static XmlSchemaSet Load()
    {
        var set = new XmlSchemaSet { XmlResolver = new ResourceResolver() };
        using var main = Open("qbxmlops130.xsd");
        set.Add(null, XmlReader.Create(main, null, "res:///qbxmlops130.xsd"));
        set.Compile();
        return set;
    }

    private static Stream Open(string name) =>
        typeof(QbxmlSchema).Assembly.GetManifestResourceStream("schema/" + name)
        ?? throw new FileNotFoundException($"Embedded schema file {name} is missing.");

    [GeneratedRegex(@"version\s*=\s*""([0-9.]+)""")]
    private static partial Regex VersionRegex();

    /// <summary>Resolves the schema's xsd:include files from the embedded resources.</summary>
    private sealed class ResourceResolver : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            Open(Path.GetFileName(absoluteUri.AbsolutePath));
    }
}
