# qbx for AI agents

How to read from and write to QuickBooks Desktop with qbx without guessing. The README covers
building and installing.

## Set up (first time)

Skip this if `./qbx-remote doctor` already passes, or if qbx.exe is already on the Windows machine.

1. **Ask the human** which QuickBooks Desktop year they run (Help > About QuickBooks), and
   whether you run on that Windows machine or reach it over SSH (and its SSH host name).
2. **Pick the exe.** QuickBooks 2022 and newer: `qbx-*-win-x64-qb2022-and-newer.exe`. 2021 and
   older: `qbx-*-win-x86-qb2021-and-older.exe`. Both are in the release's `qbx.zip`
   (`https://github.com/timbze/my-utils/releases/latest/download/qbx.zip`). A wrong pick fails
   with "QBXMLRP2 is not registered for this ...-bit process"; install the other one.
3. **Over SSH** (you run on Linux or macOS): the human sets up OpenSSH on Windows
   (README.md, "Set up a new machine", step 2). Then run
   `QBX_HOST=<host> ./qbx-remote install <exe>`, which copies it and runs `doctor`. Export
   `QBX_HOST` for every later `qbx-remote` command too.
4. **On the Windows machine itself:** copy the exe to a folder as `qbx.exe` and run it from
   there (`qbx.exe company`). `qbx-validate` and `qbx-remote` are bash; use `qbx --check`
   instead. `qbx-skeleton` needs Python 3.

## Check the setup first

- Remote host: `./qbx-remote doctor`. It checks SSH, the installed files, the desktop session,
  QuickBooks, authorization and the qbXML versions, and names the step that fails. Most fixes
  need the human (log on to Windows, open QuickBooks, answer the authorization prompt).
- On the Windows machine itself: `qbx company` prints the open company file's name.

## Write a request

1. **Start from [examples/](examples/)** when one fits. Its README lists them.
2. Otherwise **get the shape**: `./qbx-skeleton --list bill` finds the message name,
   `./qbx-skeleton BillAddRq` prints every element in the required order with notes (optional,
   repeating, alternatives, allowed values, max lengths). Responses work too: `./qbx-skeleton BillRet`.
3. **Order is strict.** Keep elements in the skeleton's order; leave optional ones out rather
   than sending them empty.
4. Header `<?qbxml version="13.0"?>`. Amounts like `12.50`, dates `YYYY-MM-DD`, escape `&` and `<`.
   Use plain ASCII punctuation in text: QuickBooks rejects the whole request over a `–` or `’`.
5. **Check it**: `./qbx-validate request.xml`. `qbx-remote` and `qbx` do this before every send
   too, and refuse a request that does not match the schema.
6. **Names must exist.** Account, vendor, customer and item refs are `FullName`s already in the
   company file (subaccounts as `Parent:Child`). Look them up with `examples/query/accounts.xml`
   and `examples/query/names.xml`. Do not invent them, and ask before adding new ones.

## Send it

- Read-only by default. Pass `--write` only for writes the human approved.
- Pass `--expect-company "<name>"` on every run. qbx then refuses to run if QuickBooks has a
  different company file open. Confirm the name with the human once.
- Each run opens a QuickBooks session, which takes seconds (about 8 over SSH). Put many messages
  in one request, each with its own `requestID`, under `onError="continueOnError"`. They are not
  a transaction: messages before a failure stay written.
- Exit codes: 0 ok, 1 refused or not sent (usage, schema, connection, wrong company), 2 QuickBooks
  returned an error status. Non-zero statuses are listed on stderr; the codes you will usually
  see are in [examples/README.md](examples/README.md).

## Before and after writing

- Ask the human before the first write, and make sure a backup of the company file exists
  (File > Back Up Company).
- Start with a small batch and let the human check it in QuickBooks before sending more.
- Start every memo with a batch tag such as `[import 2026-01a]`, keep the request and response
  files, and record the TxnID of each new entry (in the `*Ret` of its response). A bad batch can
  then be removed with `TxnDelRq` (`examples/change/delete.xml`).
- Do not modify or delete entries you did not create unless the human says so.
- Requests and responses contain company data. Keep them out of public repositories
  (this repository ignores `*.response.xml`).

## Read large or awkward data

- Long lists: add `<MaxReturned>500</MaxReturned>` and `iterator="Start"` to the query, then
  repeat it with `iterator="Continue" iteratorID="..."` from the response until its
  `iteratorRemainingCount` is 0.
- A register with cleared status: `examples/query/register.xml` (a custom report; columns are
  matched to `ColDesc` by `colID`).
- `IncludeRetElement` limits a query's response to the fields you name, which keeps it small.
  It only takes top-level fields of the response's `*Ret`, and they differ by type: vendors and
  other names have `Name` but no `FullName`, customers and accounts have both. QuickBooks silently
  leaves out a name it does not know (or a nested one like `City`), so check with
  `./qbx-skeleton VendorRet`; `qbx-validate` flags wrong names.

## What the SDK cannot do

- Reconcile an account, or tell you the last reconciled date. The latest line with
  `ClearedStatus` Cleared is the closest you get; reconciling is done in QuickBooks.
- Change company preferences such as the closing date.
