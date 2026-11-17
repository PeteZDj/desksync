param([ValidateSet('x64','arm64')][string[]]$Architecture=@('x64','arm64'))
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$version='0.3.2'
$hashes=@{x64='2edc416e7ed136b7c858ed5f3f02a3b8316589605e54c380228ceea7acbf0142';arm64='48e456690cd2b97e7d26b02b5ac35f62a3b36172267b144cfeaf02c993ab955c'}
foreach($arch in $Architecture){
 $rid="linux-$arch"; $engineArch=if($arch -eq 'x64'){'amd64'}else{'arm64'}
 $stage=Join-Path $root "artifacts\linux-$arch-build"; $package=Join-Path $stage "desksync-$version-$rid"
 New-Item -ItemType Directory -Path $stage,$package -Force | Out-Null
 $archive=Join-Path $stage 'engine.tar.gz'
 Invoke-WebRequest "https://github.com/syncthing/syncthing/releases/download/v2.1.7/syncthing-linux-$engineArch-v2.1.7.tar.gz" -OutFile $archive
 if((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashes[$arch]){throw 'Syncthing checksum mismatch.'}
 tar -xzf $archive -C $stage
 if($LASTEXITCODE -ne 0){throw 'Engine extraction failed.'}
 dotnet publish (Join-Path $root 'src\Desksync.Linux\Desksync.Linux.csproj') -c Release -r $rid --self-contained true -o $package
 if($LASTEXITCODE -ne 0){throw 'Linux build failed.'}
 $deps=Get-Content (Join-Path $root "src\Desksync.Linux\bin\Release\net10.0\$rid\desksync.deps.json") -Raw | ConvertFrom-Json
 foreach($library in $deps.libraries.psobject.Properties.Name | Where-Object {$_ -like 'runtimepack.*'}){
  $parts=$library.Split('/');$packageName=$parts[0].Substring('runtimepack.'.Length).ToLowerInvariant()
  $packageRoot=Join-Path ([Environment]::GetFolderPath('UserProfile')) ".nuget\packages\$packageName\$($parts[1])"
  Copy-Item (Join-Path $packageRoot 'LICENSE.TXT') (Join-Path $package "$packageName-LICENSE.txt") -Force
  Copy-Item (Join-Path $packageRoot 'THIRD-PARTY-NOTICES.TXT') (Join-Path $package "$packageName-THIRD-PARTY-NOTICES.txt") -Force
 }
 Copy-Item (Join-Path $stage "syncthing-linux-$engineArch-v2.1.7\syncthing") $package -Force
 Copy-Item (Join-Path $stage "syncthing-linux-$engineArch-v2.1.7\LICENSE.txt") (Join-Path $package 'SYNCTHING-LICENSE.txt') -Force
 Copy-Item (Join-Path $root 'LICENSE') (Join-Path $package 'LICENSE.txt') -Force
 Copy-Item (Join-Path $root 'docs\linux.md') (Join-Path $package 'LINUX-SETUP.md') -Force
 Copy-Item (Join-Path $root 'scripts\linux\install.sh') $package -Force
 Copy-Item (Join-Path $root 'scripts\linux\open.sh') $package -Force
 Copy-Item (Join-Path $root 'scripts\linux\diagnose.sh') $package -Force
 Copy-Item (Join-Path $root 'assets\desksync.ico') $package -Force
 Copy-Item (Join-Path $root 'assets\desksync.svg') $package -Force
 $output=Join-Path $root "artifacts\Desksync-$version-$rid.tar.gz"
 tar -czf $output -C $stage "desksync-$version-$rid"
 if($LASTEXITCODE -ne 0){throw 'Linux packaging failed.'}
 $hash=(Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
 "$hash  $([IO.Path]::GetFileName($output))" | Set-Content -Encoding ascii -LiteralPath "$output.sha256"
 Write-Output "Built $output ($hash)."
}
