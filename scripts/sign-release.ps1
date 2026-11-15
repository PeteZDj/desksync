param([string]$KeyPath='C:\ProgramData\DesksyncRelease\update-signing.pem')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
[xml]$project=Get-Content -LiteralPath (Join-Path $root 'src\Desksync\Desksync.csproj')
$version=[string]$project.Project.PropertyGroup.Version | Where-Object {$_}
$file=Join-Path $root "artifacts\Desksync-update-$version-win-x64.zip"
$json=[ordered]@{version=$version;url="https://github.com/PeteZDj/desksync/releases/download/v$version/Desksync-update-$version-win-x64.zip";sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $file).Length;channel='preview'} | ConvertTo-Json -Compress
$bytes=[Text.Encoding]::UTF8.GetBytes($json)
$rsa=[Security.Cryptography.RSA]::Create();$rsa.ImportFromPem((Get-Content -LiteralPath $KeyPath -Raw))
$verify=[Security.Cryptography.RSA]::Create();$verify.ImportFromPem((Get-Content -LiteralPath (Join-Path $root 'src\Shared\UpdatePublicKey.pem') -Raw))
if([Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo()) -ne [Convert]::ToBase64String($verify.ExportSubjectPublicKeyInfo())){throw 'Release key does not match the public key embedded in the application'}
$signature=$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
[IO.File]::WriteAllBytes((Join-Path $root 'artifacts\update.json'),$bytes)
[IO.File]::WriteAllText((Join-Path $root 'artifacts\update.json.sig'),[Convert]::ToBase64String($signature))
$rsa.Dispose();$verify.Dispose();Write-Output 'Release metadata signed. No private key is copied into the package.'
