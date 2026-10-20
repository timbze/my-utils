<#
Runs qbx.exe inside the logged-on user's desktop session and prints its output.

QuickBooks only accepts SDK connections from the session it runs in, so qbx started
over SSH fails with "Could not start QuickBooks". This registers a one-shot scheduled
task with an interactive logon, waits for it, prints stdout/stderr and returns qbx's
exit code. Relative paths resolve against the current directory.
Set QBX_TIMEOUT (seconds, default 900) for long requests.

  powershell -NoProfile -ExecutionPolicy Bypass -File qbx-desktop.ps1 request.xml
#>
# No param block: every argument goes to qbx as-is, so its -o/-f/-w are not taken as PowerShell parameters.
$QbxArgs = $args
$TimeoutSeconds = if ($env:QBX_TIMEOUT) { [int]$env:QBX_TIMEOUT } else { 900 }

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false

$exe = Join-Path $PSScriptRoot 'qbx.exe'
$id = [guid]::NewGuid().ToString('N')
$work = Join-Path $env:TEMP "qbx-$id"
New-Item -ItemType Directory -Path $work | Out-Null

$stdout = Join-Path $work 'stdout.txt'
$stderr = Join-Path $work 'stderr.txt'
$exitFile = Join-Path $work 'exit.txt'

$quoted = ($QbxArgs | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '
$cmd = "/v:on /c `"`"$exe`" $quoted > `"$stdout`" 2> `"$stderr`" & echo !errorlevel! > `"$exitFile`"`""

$taskName = "qbx-$id"
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument $cmd -WorkingDirectory (Get-Location).Path
$principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Seconds $TimeoutSeconds)

try {
    Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null
    Start-ScheduledTask -TaskName $taskName

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path $exitFile)) {
        if ((Get-Date) -gt $deadline) {
            [Console]::Error.WriteLine("qbx did not finish within $TimeoutSeconds seconds. Is the user logged on to the desktop?")
            exit 1
        }
        Start-Sleep -Milliseconds 300
    }
    # The exit file is written after the redirects close; give cmd a moment to flush it.
    Start-Sleep -Milliseconds 200

    if (Test-Path $stdout) { [Console]::Out.Write([IO.File]::ReadAllText($stdout)) }
    if (Test-Path $stderr) { [Console]::Error.Write([IO.File]::ReadAllText($stderr)) }
    exit [int]([IO.File]::ReadAllText($exitFile).Trim())
}
finally {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
