# Registers PrintShelf to start at logon with Task Scheduler.
# Run once from an elevated-or-normal PowerShell for the current user:
#   powershell -ExecutionPolicy Bypass -File deploy\install-task.ps1
[CmdletBinding()]
param(
    [string]$TaskName = 'PrintShelf',
    [string]$PublishDir
)

$ErrorActionPreference = 'Stop'
if (-not $PublishDir) { $PublishDir = Join-Path $PSScriptRoot '..\publish' }
$runScript = Join-Path $PSScriptRoot 'run-printshelf.ps1'
if (-not (Test-Path (Join-Path $PublishDir 'PrintShelf.Web.dll'))) {
    throw "Publish the app first (see run-printshelf.ps1)."
}

$action = New-ScheduledTaskAction `
    -Execute 'powershell.exe' `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$runScript`"" `
    -WorkingDirectory $PublishDir

$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'PrintShelf local 3D print catalogue (http://localhost:5100)' -Force | Out-Null

Write-Host "Registered scheduled task '$TaskName' (at logon)."
Write-Host "Start now:  Start-ScheduledTask -TaskName $TaskName"
Write-Host "Remove:     Unregister-ScheduledTask -TaskName $TaskName -Confirm:`$false"
