$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'artifacts\app'
$setup = Join-Path $root 'artifacts\setup'
dotnet publish (Join-Path $root 'src\Desksync\Desksync.csproj') -c Release -r win-x64 -o $app -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE) { throw 'Application build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'vendor\syncthing.exe') -Destination $app -Force
foreach ($file in @('README.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $app -Force }
Copy-Item -LiteralPath (Join-Path $root 'vendor\SYNCTHING-LICENSE.txt') -Destination $app -Force
foreach ($notice in @('DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $root "vendor\$notice") -Destination $app -Force }
$payload = Join-Path $root 'artifacts\payload.zip'
Compress-Archive -LiteralPath @((Get-ChildItem -LiteralPath $app -File | Where-Object {$_.Extension -ne '.pdb'}).FullName) -DestinationPath $payload -Force
dotnet publish (Join-Path $root 'src\Desksync.Setup\Desksync.Setup.csproj') -c Release -r win-x64 -o $setup -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE) { throw 'Installer build failed.' }
$output = Join-Path $root 'artifacts\Desksync-Setup-0.1.0-win-x64.exe'
Copy-Item -LiteralPath (Join-Path $setup Desksync-Setup.exe) -Destination $output -Force
$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $output -Leaf)" | Set-Content -LiteralPath "$output.sha256" -Encoding utf8
Get-Item -LiteralPath $output | Select-Object Name,Length
