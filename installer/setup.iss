[Setup]
AppName=StorkDrop
AppVersion={#AppVersion}
AppPublisher=StorkDrop
AppPublisherURL=https://github.com/chiouyazo/StorkDrop
DefaultDirName={autopf}\StorkDrop
DefaultGroupName=StorkDrop
OutputBaseFilename=StorkDrop-{#AppVersion}-Setup
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\StorkDrop.App.exe
UninstallDisplayName=StorkDrop
SetupIconFile=..\assets\stork.ico

[Files]
Source: "..\publish\StorkDrop.App.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\StorkDrop"; Filename: "{app}\StorkDrop.App.exe"
Name: "{commondesktop}\StorkDrop"; Filename: "{app}\StorkDrop.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\StorkDrop.App.exe"; Description: "Launch StorkDrop"; Flags: nowait postinstall skipifsilent
