param([string]$AppExe=(Join-Path $env:ProgramFiles 'Afrosoftware\Desksync\Desksync.exe'))
$ErrorActionPreference='Stop'
$expected=Join-Path $env:ProgramFiles 'Afrosoftware\Desksync\Desksync.exe'
if([IO.Path]::GetFullPath($AppExe) -ne $expected -or -not (Test-Path -LiteralPath $AppExe)){throw 'Install the server application in its protected Program Files directory first'}
$owner=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$action=New-ScheduledTaskAction -Execute $AppExe -Argument '--background'
$trigger=New-ScheduledTaskTrigger -AtLogOn -User $owner
$principal=New-ScheduledTaskPrincipal -UserId $owner -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([timespan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName 'Afrosoftware Desksync Tray' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Desksync dashboard and tray for the signed-in server owner. Background synchronization is a separate low-privilege service.' -Force | Out-Null
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if(Test-Path -LiteralPath $run){Remove-ItemProperty -LiteralPath $run -Name 'Afrosoftware Desksync' -ErrorAction SilentlyContinue}
Write-Output 'Server tray starts when its owner signs in; no password is stored. Sync remains a separate always-on service.'
