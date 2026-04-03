[Setup]
AppName=StorkDrop
AppVersion={#AppVersion}
AppVerName=StorkDrop {#AppVersion}
AppPublisher=StorkDrop
AppPublisherURL=https://github.com/chiouyazo/StorkDrop
AppSupportURL=https://github.com/chiouyazo/StorkDrop/issues
AppUpdatesURL=https://github.com/chiouyazo/StorkDrop/releases
DefaultDirName={autopf}\StorkDrop
DefaultGroupName=StorkDrop
OutputBaseFilename=StorkDrop-{#AppVersion}-Setup
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ChangesEnvironment=yes
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
Name: "addtopath"; Description: "Add to PATH (enables CLI usage from any terminal)"; GroupDescription: "Additional options:"; Flags: checkedonce

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath('{app}')

[Run]
Filename: "{app}\StorkDrop.App.exe"; Description: "Launch StorkDrop"; Flags: nowait postinstall skipifsilent

[Code]
function NeedsAddPath(Param: string): Boolean;
var
  Path: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', Path)
  then begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + UpperCase(Param) + ';', ';' + UpperCase(Path) + ';') = 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Path: string;
begin
  if CurUninstallStep <> usPostUninstall then
    exit;
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', Path)
  then
    exit;
  StringChangeEx(Path, ';' + ExpandConstant('{app}'), '', True);
  StringChangeEx(Path, ExpandConstant('{app}') + ';', '', True);
  StringChangeEx(Path, ExpandConstant('{app}'), '', True);
  RegWriteExpandStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Control\Session Manager\Environment',
    'Path', Path);
end;
