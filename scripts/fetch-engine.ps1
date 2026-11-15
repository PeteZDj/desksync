$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = '2.1.7'
$expected = '3ce7da770f546a8d02b65b2e7ee31f2a3b8b7c3f990ede045590cca4172ecfe1'
$stage = Join-Path $root 'artifacts\engine-download'
New-Item -ItemType Directory -Path $stage,(Join-Path $root vendor) -Force | Out-Null
$archive = Join-Path $stage syncthing.zip
Invoke-WebRequest -Uri "https://github.com/syncthing/syncthing/releases/download/v$version/syncthing-windows-amd64-v$version.zip" -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Engine checksum mismatch. No binary installed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $stage -Force
$source = Join-Path $stage "syncthing-windows-amd64-v$version"
Copy-Item -LiteralPath (Join-Path $source syncthing.exe) -Destination (Join-Path $root 'vendor\syncthing.exe') -Force
Copy-Item -LiteralPath (Join-Path $source LICENSE.txt) -Destination (Join-Path $root 'vendor\SYNCTHING-LICENSE.txt') -Force
Write-Output "Verified Syncthing $version."
