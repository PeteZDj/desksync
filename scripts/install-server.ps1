param([Parameter(Mandatory=$true)][string]$NssmPath)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run server setup from an elevated PowerShell window.' }
if (-not (Test-Path -LiteralPath $NssmPath -PathType Leaf)) { throw 'NSSM was not found at the supplied path.' }
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'artifacts\app'
if (-not (Test-Path -LiteralPath (Join-Path $app Desksync.exe))) { throw 'Build the application first using scripts\build.ps1.' }
if (Get-Service -Name DesksyncSync -ErrorAction SilentlyContinue) { throw 'DesksyncSync already exists. Stop and update that installation explicitly.' }
$install = 'C:\Program Files\Afrosoftware\Desksync'
$data = 'C:\ProgramData\Desksync'
$newFolder = -not (Test-Path -LiteralPath 'C:\Desksync')
New-Item -ItemType Directory -Path $install,$data,'C:\Desksync' -Force | Out-Null
if ($newFolder) {
    $folderAcl = [Security.AccessControl.DirectorySecurity]::new()
    $folderAcl.SetAccessRuleProtection($true,$false)
    foreach ($sid in @($identity.User,[Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::LocalSystemSid,$null),[Security.Principal.SecurityIdentifier]::new([Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,$null))) {
        $folderAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    }
    Set-Acl -LiteralPath 'C:\Desksync' -AclObject $folderAcl
}
Copy-Item -Path (Join-Path $app '*') -Destination $install -Recurse -Force
$initialize = Start-Process -FilePath (Join-Path $install Desksync.exe) -ArgumentList @('--initialize','--data',$data,'--folder','C:\Desksync') -WindowStyle Hidden -PassThru
$initialize.WaitForExit()
if ($initialize.ExitCode -ne 0) { throw 'Engine initialization failed. No service was installed.' }
[xml]$configuration = Get-Content -LiteralPath (Join-Path $data 'engine\config.xml')
$headers = @{'X-API-Key' = [string]$configuration.configuration.gui.apikey}
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5391/rest/system/shutdown' -Headers $headers | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do { Start-Sleep -Milliseconds 250; $listener = Get-NetTCPConnection -LocalPort 5391 -State Listen -ErrorAction SilentlyContinue } while ($listener -and [DateTime]::UtcNow -lt $deadline)
if ($listener) { throw 'The initialization engine did not stop. No service was installed.' }
& $NssmPath install DesksyncSync (Join-Path $install syncthing.exe)
if ($LASTEXITCODE) { throw 'Service registration failed.' }
& $NssmPath set DesksyncSync AppDirectory $install | Out-Null
& $NssmPath set DesksyncSync AppParameters 'serve --home=C:\ProgramData\Desksync\engine --no-browser --no-upgrade --no-restart --log-file=C:\ProgramData\Desksync\engine.log --log-max-size=5242880 --log-max-old-files=2' | Out-Null
& $NssmPath set DesksyncSync Start SERVICE_AUTO_START | Out-Null
& $NssmPath set DesksyncSync DisplayName 'Desksync encrypted file synchronization' | Out-Null
& "$env:SystemRoot\System32\sc.exe" sidtype DesksyncSync unrestricted | Out-Null
$service = Get-CimInstance Win32_Service -Filter "Name='DesksyncSync'"
$accountChange = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments @{StartName='NT AUTHORITY\LocalService';StartPassword=$null}
if ($accountChange.ReturnValue -ne 0) { throw 'Unable to configure the restricted service account. The service was not started.' }
$serviceSid = [Security.Principal.NTAccount]::new('NT SERVICE\DesksyncSync').Translate([Security.Principal.SecurityIdentifier])
foreach ($directory in @($data,'C:\Desksync')) {
    $acl=Get-Acl -LiteralPath $directory
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($serviceSid,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
    Set-Acl -LiteralPath $directory -AclObject $acl
}
foreach ($protocol in @('TCP','UDP')) {
    $ruleName = 'Desksync-Sync-' + $protocol
    if (-not (Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -Name $ruleName -DisplayName ('Desksync encrypted sync ' + $protocol) -Group Desksync -Direction Inbound -Action Allow -Protocol $protocol -LocalPort 22000 -Program (Join-Path $install syncthing.exe) -Profile Any | Out-Null
    }
}
Start-Service -Name DesksyncSync
Write-Output 'DesksyncSync installed. C:\Desksync is ready; no remote device has been approved.'
