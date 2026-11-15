$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src\Desksync\Desksync.csproj')
$version=[string]$project.Project.PropertyGroup.Version | Where-Object {$_}
$app=Join-Path $root 'artifacts\app-v02'
& (Join-Path $PSScriptRoot brand-icon.ps1)
dotnet publish (Join-Path $root 'src\Desksync\Desksync.csproj') -c Release -r win-x64 -o $app -p:EnableCompressionInSingleFile=true -v:q
if($LASTEXITCODE){throw 'Desktop application build failed'}
foreach($component in @('Desksync.Service','Desksync.Updater')) {
 $output=Join-Path $root ('artifacts\'+$component)
 dotnet publish (Join-Path $root "src\$component\$component.csproj") -c Release -r win-x64 -o $output -p:EnableCompressionInSingleFile=true -v:q
 if($LASTEXITCODE){throw "$component build failed"}
 Copy-Item -LiteralPath (Join-Path $output "$component.exe") -Destination $app -Force
}
Copy-Item -LiteralPath (Join-Path $root 'vendor\syncthing.exe') -Destination $app -Force
foreach($file in @('README.md','LICENSE')){Copy-Item -LiteralPath (Join-Path $root $file) -Destination $app -Force}
foreach($notice in @('SYNCTHING-LICENSE.txt','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','WEBVIEW2-LICENSE.txt','WEBVIEW2-NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $root "vendor\$notice") -Destination $app -Force}
$files=@((Get-ChildItem -LiteralPath $app -File | Where-Object {$_.Extension -notin '.pdb','.xml'}).FullName)
$payload=Join-Path $root 'artifacts\payload.zip'
Compress-Archive -LiteralPath $files -DestinationPath $payload -Force
$update=Join-Path $root "artifacts\Desksync-update-$version-win-x64.zip"
Copy-Item -LiteralPath $payload -Destination $update -Force
$setup=Join-Path $root 'artifacts\setup-v02'
dotnet publish (Join-Path $root 'src\Desksync.Setup\Desksync.Setup.csproj') -c Release -r win-x64 -o $setup -p:EnableCompressionInSingleFile=true -v:q
if($LASTEXITCODE){throw 'Installer build failed'}
$installer=Join-Path $root "artifacts\Desksync-Setup-$version-win-x64.exe"
Copy-Item -LiteralPath (Join-Path $setup Desksync-Setup.exe) -Destination $installer -Force
foreach($file in @($installer,$update)){[IO.File]::WriteAllText("$file.sha256",(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path $file -Leaf)+"`n")}
Get-Item -LiteralPath $installer,$update | Select-Object Name,Length
