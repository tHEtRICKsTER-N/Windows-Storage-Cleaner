#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PayloadDir
  #error PayloadDir is required
#endif

[Setup]
AppId={{3BAFAF9C-4AF4-4B67-A255-0B33C7C1C042}
AppName=Windows Storage Cleaner
AppVersion={#AppVersion}
AppPublisher=Windows Storage Cleaner contributors
DefaultDirName={localappdata}\Programs\WindowsStorageCleaner
DefaultGroupName=Windows Storage Cleaner
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
OutputDir={#ReleaseDir}
OutputBaseFilename=WindowsStorageCleaner-{#AppVersion}-Setup-x64
SetupIconFile={#SourceRoot}\assets\app.ico
UninstallDisplayIcon={app}\WindowsStorageCleaner.exe
LicenseFile={#SourceRoot}\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AllowNetworkDrive=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Optional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\Windows Storage Cleaner"; Filename: "{app}\WindowsStorageCleaner.exe"
Name: "{group}\Uninstall Windows Storage Cleaner"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Windows Storage Cleaner"; Filename: "{app}\WindowsStorageCleaner.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\WindowsStorageCleaner.exe"; Description: "Open Windows Storage Cleaner"; Flags: nowait postinstall skipifsilent unchecked runasoriginaluser
