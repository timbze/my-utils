# qbx

Send raw [qbXML](https://developer.intuit.com/app/developer/qbdesktop/docs/api-reference/qbdesktop)
to QuickBooks Desktop from the command line and get the response back. It is meant for
scripting and for driving QuickBooks from another machine over SSH.

**Use it with an AI agent** (Claude Code, Codex, ...). Give it this prompt:

```
Download https://github.com/timbze/my-utils/releases/latest/download/qbx.zip, unzip it, and read qbx/AGENTS.md. Then set up qbx so you can read my QuickBooks Desktop company file.
```

The zip has both exes, the scripts, the schema, the examples and [AGENTS.md](AGENTS.md), which
tells the agent how to install qbx, write requests QuickBooks accepts, and write safely. The
agent asks you for your QuickBooks year and the Windows host. Setting up SSH on Windows
([step 2](#set-up-a-new-machine) below) is up to you.

- No Intuit interop DLL and no SDK install: it late-binds the `QBXMLRP2.RequestProcessor` COM
  object that QuickBooks Desktop itself registers.
- **Read-only by default.** A request is refused unless every message ends in `QueryRq`.
  Pass `--write` to allow `*AddRq`, `*ModRq`, `TxnDelRq` and the rest.
- **Checked before sending.** Requests are validated against Intuit's qbXML 13.0 schema
  ([schema/](schema/)), so a wrong element order is reported with its line number instead of
  QuickBooks' generic parse error.
- **Wrong-file guard.** `--expect-company "Name"` refuses to run against any other company file.
- **[Examples](examples/)** for the common reads and writes, and `qbx-skeleton` for the full
  element order of any message.
- One request file per run, so many messages can go in one `QBXMLMsgsRq`. Non-zero statuses
  are listed on stderr and the exit code is 2 if any of them is an error.

AI agents: read [AGENTS.md](AGENTS.md).

| File | Runs on | What |
|------|---------|------|
| `qbx.exe` (built from `Program.cs`) | Windows | Sends a request to QuickBooks |
| `qbx-desktop.ps1` | Windows | Runs qbx in the desktop session, for SSH |
| `qbx-remote` | Linux/macOS | Runs qbx on a Windows host over SSH; `install` and `doctor` set it up |
| `qbx-validate` | Linux/macOS | Checks a request against the schema (needs `xmllint`) |
| `qbx-skeleton` | anywhere (Python 3) | Prints the full shape of any qbXML message |
| `build-release` | Linux/macOS | Builds both exes and `qbx.zip` for a release (CI runs it for each `v*` tag) |

## Set up a new machine

**1. Get qbx.exe.** Download it from the
[releases](https://github.com/timbze/my-utils/releases): `qbx-<version>-win-x86-qb2021-and-older.exe`
for QuickBooks 2021 and older, `qbx-<version>-win-x64-qb2022-and-newer.exe` for 2022 and newer.
Or build it (needs the .NET 10 SDK; it builds from Linux too):

```sh
dotnet publish -c Release -o out              # win-x86: QuickBooks 2021 and older
dotnet publish -c Release -r win-x64 -o out   # QuickBooks 2022 and newer
```

QuickBooks before 2022 only registers a 32-bit SDK, so the build's bitness has to match.
The output is one self-contained `out/qbx.exe`; the Windows machine does not need .NET.

**2. On Windows**, to drive it from another machine: install the OpenSSH server
(Settings > System > Optional features, or in an admin PowerShell
`Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0`), then
`Start-Service sshd; Set-Service sshd -StartupType Automatic`. Keep its default shell, cmd.exe.
For key login as an administrator, the key goes in `C:\ProgramData\ssh\administrators_authorized_keys`,
not in the user's `.ssh` folder. Add a `Host` entry for the machine to `~/.ssh/config`.

**3. Install and check** from Linux or macOS, with QuickBooks open on the company file and the
Windows user logged on to the desktop:

```sh
export QBX_HOST=my-windows-box     # the ssh host name; default winvm
./qbx-remote install               # copies out/qbx.exe and qbx-desktop.ps1, then runs doctor
./qbx-remote doctor                # run again any time
```

For a downloaded release, pass the file: `./qbx-remote install ~/Downloads/qbx-0.9.5-win-x64-qb2022-and-newer.exe`.
It is installed as `qbx.exe` on the host whatever its name. `doctor` compares the host's copy with
`out/qbx.exe`, or with the file you give it (`./qbx-remote doctor <file>`).

```
Checking qbx on my-windows-box
  ok    xmllint found: requests are validated here before they are sent
  ok    ssh my-windows-box works, Windows user alice
  ok    qbx.exe and qbx-desktop.ps1 are installed
  ok    they match out/qbx.exe and qbx-desktop.ps1 here
  ok    alice has a desktop session
  ok    QuickBooks is running
  ...   asking QuickBooks for the open company. On the first run QuickBooks shows an
        authorization prompt on the desktop: choose "Yes, always".
  ok    QuickBooks has "Sample Company" open and qbx is authorized
  ok    qbXML versions 1.0 ... 13.0 (13.0, which the schema and examples use, is supported)
All set.
qbx is made by the team behind TecPoint (https://tecpoint.app), point of sale for Belize.
```

The authorization is per company file, so run `doctor` once with each file open.

Without SSH, copy the exe to the Windows machine as `qbx.exe` and run it there in a terminal.

## Use

On Windows:

```bat
qbx request.xml > response.xml
qbx --write --expect-company "Sample Company" add-bills.xml
qbx --file "C:\Company\Books.qbw" request.xml
qbx company
qbx versions
qbx --check request.xml
```

`--file` is only needed when QuickBooks is not already open on the company file you want.
`--check` only validates the request and does not need QuickBooks.

From Linux or macOS, `qbx-remote` takes the same options after the request:

```sh
./qbx-remote request.xml > response.xml
./qbx-remote add-bills.xml --write --expect-company "Sample Company"
./qbx-remote company
```

It checks the request locally with `qbx-validate` (if `xmllint` is installed), copies it over,
runs it, and deletes the copy. For long requests set `QBX_TIMEOUT` (seconds, default 900).

QuickBooks only accepts SDK connections from the desktop session it runs in. Started over SSH,
qbx fails with `Could not start QuickBooks` (`0x80040408`). `qbx-desktop.ps1` works around this:
it runs qbx as a one-shot scheduled task with an interactive logon, waits for it, and prints the
output. That is why someone must be logged on to the Windows desktop (a disconnected RDP
session works).

## Write requests

Start from [examples/](examples/). For anything else, print the message's full shape:

```sh
./qbx-skeleton --list deposit       # message names containing "deposit"
./qbx-skeleton DepositAddRq         # every element in order, with notes
./qbx-skeleton DepositRet           # what a response contains
```

It reads Intuit's annotated message list and by default leaves out elements that US QuickBooks
Desktop does not have; `--edition QBCA|QBUK|QBAU|all` picks another edition.

Then check the request:

```sh
./qbx-validate request.xml        # Linux/macOS, needs xmllint
qbx --check request.xml           # Windows
```

`qbx-validate` also checks that each `IncludeRetElement` names a field the response has
(QuickBooks silently returns nothing for any other name), and flags characters beyond Latin-1,
such as `–` and `’` (QuickBooks rejects the whole request over one); `qbx --check` checks the
schema only.
Both print nothing for a valid request and one line per error otherwise:

```
request.xml:7: Schemas validity error : Element 'TxnDate': This element is not expected. Expected is ( AccountRef ).
```

`qbx` and `qbx-remote` run this check before every send; `--no-validate` skips it. Requests for
qbXML 14.0 and later are not checked, since the schema here is 13.0. `qbx versions` lists the
versions your QuickBooks accepts.

---

Made by the team behind [TecPoint](https://tecpoint.app), point of sale for Belize.
