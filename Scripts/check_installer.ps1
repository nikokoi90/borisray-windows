param([Parameter(Mandatory=$true)][string]$Setup)
$ErrorActionPreference = 'Stop'
if ($env:CI -ne 'true') { throw 'Installer smoke test is restricted to the disposable CI runner' }
$installed = Join-Path $env:ProgramFiles 'BorisRay'
if (Test-Path $installed) { throw 'Refusing to replace an existing installation' }
$markerRoot = Join-Path $env:LOCALAPPDATA 'BorisRay'
New-Item -ItemType Directory -Force $markerRoot | Out-Null
$marker = Join-Path $markerRoot 'installer-preservation.fixture'
Set-Content $marker 'synthetic local data must survive uninstall'
$install = Start-Process (Resolve-Path $Setup) -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=$((Join-Path $PWD 'artifacts/install.log'))" -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Installer exit $($install.ExitCode)" }
foreach ($file in @('BorisRay.exe','YamlDotNet.dll','core/sing-box.exe','core/libcronet.dll','licenses/YamlDotNet-LICENSE.txt')) {
    $expected = (Get-FileHash (Join-Path 'artifacts/app' $file) -Algorithm SHA256).Hash
    if ((Get-FileHash (Join-Path $installed $file) -Algorithm SHA256).Hash -ne $expected) { throw "Installed file mismatch: $file" }
}
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'BorisRay.lnk'
if (-not (Test-Path $shortcut)) { throw 'Start menu shortcut missing' }
$uninstall = Start-Process (Join-Path $installed 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
if ($uninstall.ExitCode -ne 0 -or (Test-Path (Join-Path $installed 'BorisRay.exe')) -or (Test-Path $shortcut)) { throw 'Uninstaller did not remove application and shortcut' }
if ((Get-Content $marker) -ne 'synthetic local data must survive uninstall') { throw 'Local user data changed during uninstall' }
Remove-Item $marker
@{ install = 'passed'; installedFileHashes = 'passed'; startMenuShortcut = 'passed'; uninstall = 'passed'; localDataPreservation = 'passed'; vpnStarted = $false; os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription } | ConvertTo-Json | Set-Content 'artifacts/installer-check.json' -Encoding utf8NoBOM
