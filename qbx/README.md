# qbx

Send raw [qbXML](https://developer.intuit.com/app/developer/qbdesktop/docs/api-reference/qbdesktop)
to QuickBooks Desktop from the command line and get the response back. It is meant for
scripting and for driving QuickBooks from another machine over SSH.

- No Intuit interop DLL: it late-binds the SDK's `QBXMLRP2.RequestProcessor` COM object.
- **Read-only by default.** A request is refused unless every message ends in `QueryRq`.
  Pass `--write` to allow `*AddRq`, `*ModRq`, `TxnDelRq` and the rest.
- One request file per run, so many messages can go in one `QBXMLMsgsRq`. Non-zero statuses
  are listed on stderr and the exit code is 2 if any of them is an error.
- **Checked before sending.** Requests are validated against Intuit's qbXML 13.0 schema
  ([schema/](schema/)), so a wrong element order is reported with its line number instead of
  QuickBooks' generic parse error.
- **[Examples](examples/)** for the common reads and writes: checks, deposits, bills, bill
  payments, journal entries, registers with cleared status, and more.

## Build

Needs the .NET 10 SDK. It builds from Linux too.

```sh
dotnet publish -c Release -o out              # win-x86: QuickBooks 2021 and older
dotnet publish -c Release -r win-x64 -o out   # QuickBooks 2022 and newer
```

QuickBooks before 2022 only registers a 32-bit SDK, so the build's bitness has to match.
The output is one self-contained `qbx.exe`; the target machine does not need .NET installed.

## Use

```bat
qbx request.xml > response.xml
qbx --write add-bills.xml
qbx --file "C:\Company\Books.qbw" request.xml
qbx versions
```

`--file` is only needed when QuickBooks is not already open on the company file you want.
`--check` only validates the request and exits; it does not need QuickBooks.
The first run shows an authorization prompt in QuickBooks. Pick "Yes, always" to allow
later runs without it.

## Over SSH

QuickBooks only accepts SDK connections from the desktop session it runs in. Started over SSH,
qbx fails with `Could not start QuickBooks` (`0x80040408`). `qbx-desktop.ps1` works around this:
it runs qbx as a one-shot scheduled task with an interactive logon, waits for it, and prints the
output. Someone must be logged on to the Windows desktop (a disconnected RDP session works).

Put `qbx.exe` and `qbx-desktop.ps1` in `%USERPROFILE%\qbx` on the Windows machine, then from
Linux or macOS:

```sh
QBX_HOST=my-windows-box ./qbx-remote request.xml > response.xml
QBX_HOST=my-windows-box ./qbx-remote add-bills.xml --write
```

`qbx-remote` checks the request locally with `qbx-validate` (if `xmllint` is installed),
copies it over, runs it, and deletes the copy. `QBX_HOST` defaults to
`winvm`. For long requests set `QBX_TIMEOUT` (seconds, default 900) on the Windows side.

## Validate without QuickBooks

```sh
./qbx-validate request.xml        # Linux/macOS, needs xmllint
qbx --check request.xml           # Windows
```

Both print nothing for a valid request and one line per error otherwise:

```
request.xml:7: Schemas validity error : Element 'TxnDate': This element is not expected. Expected is ( AccountRef ).
```

Pass `--no-validate` to `qbx` or `qbx-remote` to skip the check. Requests for qbXML 14.0 and
later are not checked, since the schema here is 13.0.

## Example request

```xml
<?xml version="1.0" encoding="utf-8"?>
<?qbxml version="13.0"?>
<QBXML>
  <QBXMLMsgsRq onError="continueOnError">
    <AccountQueryRq requestID="1" />
  </QBXMLMsgsRq>
</QBXML>
```

Run `qbx versions` to see which qbXML versions your QuickBooks accepts.
