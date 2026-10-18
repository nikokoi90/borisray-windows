param([switch]$Capture)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
function Check-Exit { if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code $LASTEXITCODE" } }
$version = '0.1.0'
$coreVersion = '1.14.3'
$zipHash = 'd8b86ef58936dba9230ec6a57ffa4243933f3c14ef4809828b6c553440ed1518'
$exeHash = '5c19bb3f8c12d4e85b3d47fe63123a7624b314b58dd7a8e32f51004f1c4d2faf'
$dllHash = '3217c6260fbca5f16072e0b79735742f40109a63bb0ff88fd6b96dd6b54a2928'
$vendor = Join-Path $root 'vendor/sing-box'
if (-not (Test-Path "$vendor/sing-box.exe")) {
    New-Item -ItemType Directory -Force 'vendor' | Out-Null
    $archive = Join-Path $root 'vendor/sing-box.zip'
    Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$coreVersion/sing-box-$coreVersion-windows-amd64.zip" -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $zipHash) { throw 'Upstream archive digest mismatch' }
    Expand-Archive $archive 'vendor/extracted' -Force
    $found = Get-ChildItem 'vendor/extracted' -Filter 'sing-box.exe' -Recurse | Select-Object -First 1
    if (-not $found) { throw 'Upstream core executable missing' }
    New-Item -ItemType Directory -Force $vendor | Out-Null
    Copy-Item "$($found.DirectoryName)/*" $vendor -Recurse -Force
}
if ((Get-FileHash "$vendor/sing-box.exe" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $exeHash) { throw 'Core executable digest mismatch' }
if ((Get-FileHash "$vendor/libcronet.dll" -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dllHash) { throw 'Cronet digest mismatch' }
New-Item -ItemType Directory -Force 'artifacts/config-checks', 'artifacts/review' | Out-Null
dotnet run --project tests/BorisRay.Tests.csproj -c Release -- 'artifacts/config-checks'; Check-Exit
foreach ($config in Get-ChildItem 'artifacts/config-checks' -Filter '*.json') {
    & "$vendor/sing-box.exe" check -c $config.FullName; Check-Exit
}
dotnet publish src/BorisRay.Windows/BorisRay.Windows.csproj -c Release -r win-x64 --self-contained true -o artifacts/app; Check-Exit
New-Item -ItemType Directory -Force 'artifacts/app/core' | Out-Null
Copy-Item "$vendor/*" 'artifacts/app/core' -Recurse -Force
Copy-Item 'README.txt', 'LICENSES.txt', 'LICENSE' 'artifacts/app'
Copy-Item 'licenses' 'artifacts/app/licenses' -Recurse -Force
$archive = "artifacts/BorisRay-Windows-x64-$version.zip"
if (Test-Path $archive) { Remove-Item $archive }
Compress-Archive -Path 'artifacts/app/*' -DestinationPath $archive -CompressionLevel Optimal
if ($Capture) {
    dotnet publish tests/BorisRay.Capture/BorisRay.Capture.csproj -c Release -r win-x64 --self-contained true -o artifacts/capture; Check-Exit
    New-Item -ItemType Directory -Force 'artifacts/capture/core' | Out-Null
    Copy-Item "$vendor/*" 'artifacts/capture/core' -Recurse -Force
    & './artifacts/capture/BorisRay.Capture.exe' (Join-Path $root 'artifacts/review'); Check-Exit
}
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $archive -Leaf)" | Set-Content 'artifacts/SHA256SUMS' -Encoding utf8NoBOM
@{ version = $version; platform = 'win-x64'; core = $coreVersion; sha256 = $hash; size = (Get-Item $archive).Length; commit = $env:GITHUB_SHA; os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription } | ConvertTo-Json | Set-Content 'artifacts/build.json' -Encoding utf8NoBOM
