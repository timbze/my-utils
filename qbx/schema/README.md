# qbXML 13.0 schema

Intuit's XML Schema for qbXML 13.0, the version these tools send. `qbx-validate` and `qbx.exe`
check every request against it before it goes to QuickBooks, so a wrong element order or a bad
value is reported right away with its line number, instead of as QuickBooks' generic parse error.

| File | What |
|------|------|
| `qbxmlops130.xsd` | Entry point: every request and response message |
| `qbxml130.xsd` | Element and aggregate definitions |
| `qbxmltypes130.xsd` | Simple types (amounts, dates, IDs) |
| `qbxmlso130.xsd` | Subscription and event messages |

To check a request by hand:

```sh
xmllint --noout --schema schema/qbxmlops130.xsd request.xml
```

## Source and license

The files are © 2013 Intuit Inc. and ship with the QuickBooks Desktop SDK under the qbXML
Developer Agreement (see the header of each file). They are not covered by this repository's
GPL license.

Copied from [jsgoupil/quickbooks-sync](https://github.com/jsgoupil/quickbooks-sync/tree/d35c33107dbcfaf2d0b542c2be3a620da4bfe500/src/QbXml/Schemas)
at commit `d35c331`. That project uses a patched `qbxml130_modified.xsd`; here `qbxmlops130.xsd`
includes the original `qbxml130.xsd` again, which is the only change.

Requests for an older version (`<?qbxml version="12.0"?>` and below) are checked against 13.0 too.
That catches order and type errors, but not an element newer than the version the request asks for.
Requests for 14.0 and up are not checked; a request with no `<?qbxml version?>` line is refused.
