$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$testRoot=Join-Path $root ('artifacts\updater-qa-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$rsa=[Security.Cryptography.RSA]::Create(2048)
$public=Join-Path $testRoot public.pem
[IO.File]::WriteAllText($public,$rsa.ExportSubjectPublicKeyInfoPem())
$helper=Join-Path $testRoot helper
dotnet publish (Join-Path $root 'src\Desksync.Updater\Desksync.Updater.csproj') -c Release -r win-x64 -o $helper -p:EnableCompressionInSingleFile=true "-p:UpdateVerificationKey=$public" -v:q
if($LASTEXITCODE){throw 'QA updater build failed'}
$stub=Join-Path $testRoot stub
dotnet publish (Join-Path $root 'tests\UpdateStub\UpdateStub.csproj') -c Release -r win-x64 -o $stub -p:EnableCompressionInSingleFile=true -v:q
if($LASTEXITCODE){throw 'QA application fixture build failed'}
foreach($mode in @('success','rollback')) {
 $case=Join-Path $testRoot $mode
 $install=Join-Path $case 'Programs\Afrosoftware\Desksync'
 $testProfile=Join-Path $case profile
 $staging=Join-Path $case payload
 New-Item -ItemType Directory -Path $install,$testProfile,$staging,(Join-Path $testProfile updates) -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $stub Desksync.exe) -Destination $install
 Copy-Item -LiteralPath (Join-Path $stub Desksync.exe) -Destination $staging
 [IO.File]::WriteAllBytes((Join-Path $staging syncthing.exe),[byte[]]@(0))
 Set-Content -LiteralPath (Join-Path $install version.txt) -Value 'old version'
 Set-Content -LiteralPath (Join-Path $staging version.txt) -Value 'new version'
 if($mode -eq 'rollback'){Set-Content -LiteralPath (Join-Path $staging FAIL_START) -Value 'deliberate fixture crash'}
 $archive=Join-Path $case update.zip
 Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive
 $manifest=Join-Path $case update.json
 $json=@{version='0.2.1';url='https://afrosoftware.com/downloads/desksync/test.zip';sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $archive).Length;channel='preview'} | ConvertTo-Json -Compress
 $bytes=[Text.Encoding]::UTF8.GetBytes($json);[IO.File]::WriteAllBytes($manifest,$bytes)
 $signature=Join-Path $case update.sig
 [IO.File]::WriteAllBytes($signature,$rsa.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss))
 $info=[Diagnostics.ProcessStartInfo]::new((Join-Path $helper Desksync.Updater.exe));$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.Environment['DESKSYNC_UPDATER_TEST_HOME']=$testProfile
 foreach($arg in @('--pid','2147483646','--install',$install,'--home',$testProfile,'--manifest',$manifest,'--signature',$signature,'--archive',$archive,'--desktop')){$info.ArgumentList.Add($arg)}
 $process=[Diagnostics.Process]::Start($info)
 if(-not $process.WaitForExit(90000)){$process.Kill($true);throw 'QA updater timed out'}
 $expected=if($mode -eq 'success'){'new version'}else{'old version'}
 if((Get-Content -LiteralPath (Join-Path $install version.txt) -Raw).Trim() -ne $expected){throw "Wrong application files after $mode"}
 if($mode -eq 'success' -and $process.ExitCode -ne 0){throw 'Healthy update did not complete'}
 if($mode -eq 'rollback' -and ((Test-Path -LiteralPath (Join-Path $install FAIL_START)) -or -not (Test-Path -LiteralPath (Join-Path $testProfile 'updates\blocked-version.txt')))){throw 'Rollback did not remove new files or prevent an automatic retry loop'}
 Write-Output "PASS real updater $mode transaction with isolated application fixtures"
 $process.Dispose()
}
$rsa.Dispose()
Write-Output 'Updater tests use their own ephemeral signing key and temporary install directories; no running Desksync service or user files are changed.'
