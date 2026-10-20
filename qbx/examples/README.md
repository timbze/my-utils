# qbXML examples

Complete requests for the common bookkeeping jobs. Copy one, replace the placeholder names
(`Checking`, `Sample Vendor`, `Office Supplies`, ...) with the FullNames from your company file,
and send it. Every file passes the qbXML 13.0 schema (`../qbx-validate examples/*/*.xml`).

Element order matters: qbXML is defined by an XML Schema sequence, so elements must come in
the order shown here. Leaving optional elements out is fine; reordering them is not. For
anything not covered here, `../schema/qbxmlops130.xsd` is the full reference: search for
`<xsd:element name="CheckAdd">` and the like to see the allowed children in order.

## Read (no `--write` needed)

| File | What |
|------|------|
| [query/company.xml](query/company.xml) | Name of the open company file. Check it before writing. |
| [query/accounts.xml](query/accounts.xml) | Chart of accounts with types and balances |
| [query/names.xml](query/names.xml) | Vendors, customers and other names, as three messages in one request |
| [query/register.xml](query/register.xml) | One account's register with cleared status, via a custom report |
| [query/transactions.xml](query/transactions.xml) | Every transaction on one account in a date range |
| [query/open-bills.xml](query/open-bills.xml) | A vendor's unpaid bills and their TxnIDs |
| [query/journal-entries.xml](query/journal-entries.xml) | Journal entries by reference number |
| [query/one-transaction.xml](query/one-transaction.xml) | One transaction by TxnID, with lines and EditSequence |

All of these were sent to QuickBooks Desktop 2019 as they are.

## Write (`--write`)

| File | What |
|------|------|
| [add/check.xml](add/check.xml) | Payment from a bank account, split over expense accounts |
| [add/deposit.xml](add/deposit.xml) | Money into a bank account that is not an invoice payment |
| [add/bill.xml](add/bill.xml) | Vendor bill |
| [add/vendor-credit.xml](add/vendor-credit.xml) | Credit from a vendor |
| [add/bill-payment-check.xml](add/bill-payment-check.xml) | Pay open bills from a bank account, using a vendor credit |
| [add/journal-entry.xml](add/journal-entry.xml) | General journal entry |
| [add/account.xml](add/account.xml) | New (sub)account |
| [add/transfer.xml](add/transfer.xml) | Transfer between two bank accounts *(schema-checked only)* |
| [add/credit-card-charge.xml](add/credit-card-charge.xml) | Purchase on a credit card account *(schema-checked only)* |
| [add/vendor.xml](add/vendor.xml) | New vendor *(schema-checked only)* |
| [change/check-mod.xml](change/check-mod.xml) | Change a check; how line edits work *(schema-checked only)* |
| [change/delete.xml](change/delete.xml) | Delete a transaction by TxnID *(schema-checked only)* |

The ones not marked follow the shape of entries QuickBooks Desktop 2019 accepted.

## Habits that pay off

- **Batch.** Each run of qbx opens a QuickBooks session, which takes seconds. Put many messages
  in one `QBXMLMsgsRq` and give each its own `requestID`; every response echoes it.
  `onError="continueOnError"` reports all failures instead of stopping at the first one.
  Messages are not a transaction: those before a failure stay written.
- **Tag what you write.** Start every memo with a batch tag such as `[import 2026-01a]`. Then you
  can find a batch again in QuickBooks, and remove a bad one with `change/delete.xml` by the
  TxnIDs from the response.
- **Refer to existing names.** `FullName` refs must match an account, vendor, customer or item
  that already exists (subaccounts as `Parent:Child`). Look them up with `query/accounts.xml`
  and `query/names.xml` rather than guessing.
- **Amounts** have at most two decimals (`12.50`, not `12.5` or `12.505`) and dates are `YYYY-MM-DD`.
  Escape `&` and `<` in text as `&amp;` and `&lt;`.

## Status codes you will see

Each response element has `statusCode`, `statusSeverity` and `statusMessage`.

| Code | Meaning |
|------|---------|
| 0 | OK |
| 1 | Query found nothing (Info) |
| 500 | Query ran, but a name or ID in its filter does not exist (Warn) |
| 3100 | The name is already in use (adding a duplicate) |
| 3120 | An object named in the request does not exist |
| 3140 | A reference (account, payee, ...) in an add or mod does not exist |
| 3200 | The EditSequence is out of date: query the record again and retry the mod |
