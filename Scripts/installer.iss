#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
[Setup]
AppId={{C0B638D0-1D7B-49F9-95A7-ADE4C5E8CE22}
AppName=BorisRay
AppVersion={#AppVersion}
AppPublisher=BorisRay
AppPublisherURL=https://github.com/nikokoi90/borisray-windows
DefaultDirName={autopf}\BorisRay
DefaultGroupName=BorisRay
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
PrivilegesRequired=admin
OutputDir=..\artifacts
OutputBaseFilename=BorisRay-Windows-Setup-x64-{#AppVersion}
SetupIconFile=..\assets\BorisRay.ico
UninstallDisplayIcon={app}\BorisRay.exe
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=BorisRay.exe
RestartApplications=no
SetupLogging=yes
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\BorisRay"; Filename: "{app}\BorisRay.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\BorisRay"; Filename: "{app}\BorisRay.exe"; Tasks: desktopicon; WorkingDir: "{app}"
[Run]
Filename: "{app}\BorisRay.exe"; Description: "{cm:LaunchProgram,BorisRay}"; Flags: nowait postinstall skipifsilent runasoriginaluser
