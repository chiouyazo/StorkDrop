[Setup]
; White-label support: ship a "whitelabel" folder (whitelabel.json + logo) next to Setup.exe, or
; pass /WHITELABELDIR=<folder>. The edition prefix is read from whitelabel.json's "prefix" and drives
; the install dir, executable name, shortcuts and uninstall entry automatically - no prefix to type.
; /PREFIX=<code> can still override it (used by self-update). With no white-label payload the
; installer behaves exactly like a plain StorkDrop setup.
AppId={code:GetAppId}
UsePreviousLanguage=no
AppName={code:GetAppName}
AppVersion={#AppVersion}
AppVerName={code:GetAppName} {#AppVersion}
AppPublisher=StorkDrop
AppPublisherURL=https://github.com/chiouyazo/StorkDrop
AppSupportURL=https://github.com/chiouyazo/StorkDrop/issues
AppUpdatesURL=https://github.com/chiouyazo/StorkDrop/releases
DefaultDirName={code:GetInstallDir}
DefaultGroupName={code:GetAppName}
OutputBaseFilename=StorkDrop-{#AppVersion}-Setup
OutputDir=Output
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
ChangesEnvironment=yes
UninstallDisplayIcon={app}\{code:GetExeName}
UninstallDisplayName={code:GetAppName}
SetupIconFile=..\assets\stork.ico

[Files]
Source: "..\publish\StorkDrop.App.exe"; DestDir: "{app}"; DestName: "{code:GetExeName}"; Flags: ignoreversion
; Optional white-label payload (whitelabel.json + logo), copied verbatim into the install dir.
Source: "{code:GetWhitelabelSource}"; DestDir: "{app}"; Flags: external recursesubdirs createallsubdirs ignoreversion skipifsourcedoesntexist; Check: HasWhitelabel

[Icons]
Name: "{group}\{code:GetAppName}"; Filename: "{app}\{code:GetExeName}"
Name: "{commondesktop}\{code:GetAppName}"; Filename: "{app}\{code:GetExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "addtopath"; Description: "Add to PATH (enables CLI usage from any terminal)"; GroupDescription: "Additional options:"; Flags: checkedonce
Name: "machinedata"; Description: "Store application data for all users (machine-wide - recommended for terminal servers and shared PCs)"; GroupDescription: "Additional options:"; Flags: unchecked

[Dirs]
; Machine-wide data: create %ProgramData%\<AppFolder> and grant Users modify so non-elevated sessions can
; write config/state there (StorkPaths reads the location from the DataLocation registry value below).
Name: "{commonappdata}\{code:GetAppName}"; Permissions: users-modify; Check: IsMachineDataLocation

[Registry]
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Tasks: addtopath; Check: NeedsAddPath('{app}')
; Where StorkPaths stores state: "Machine" (%ProgramData%) or "User" (%AppData%). Read by StorkPaths at runtime.
Root: HKLM; Subkey: "SOFTWARE\{code:GetAppName}"; ValueType: string; ValueName: "DataLocation"; ValueData: "{code:GetDataLocation}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\{code:GetExeName}"; Description: "Launch {code:GetAppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  CachedPrefix: String;
  PrefixResolved: Boolean;

// The white-label payload folder: an explicit /WHITELABELDIR, otherwise a "whitelabel" folder next
// to Setup.exe. It must contain whitelabel.json (and any logo the config references).
function WhitelabelSourceDir(): String;
begin
  Result := ExpandConstant('{param:whitelabeldir|}');
  if Result = '' then
    Result := ExpandConstant('{src}') + '\whitelabel';
end;

// Minimal extractor for a top-level "key": "value" string from a small JSON file.
function ExtractJsonString(Content, Key: String): String;
var
  s: String;
  i: Integer;
begin
  Result := '';
  i := Pos(LowerCase('"' + Key + '"'), LowerCase(Content));
  if i = 0 then
    exit;
  s := Copy(Content, i + Length(Key) + 2, Length(Content));
  i := Pos(':', s);
  if i = 0 then
    exit;
  s := Copy(s, i + 1, Length(s));
  i := Pos('"', s);
  if i = 0 then
    exit;
  s := Copy(s, i + 1, Length(s));
  i := Pos('"', s);
  if i = 0 then
    exit;
  Result := Copy(s, 1, i - 1);
end;

// Edition prefix: /PREFIX wins, otherwise "prefix" from whitelabel.json. Resolved once at startup
// from a file (before the wizard), so AppId, install dir, exe name and shortcuts are all correct
// with zero user input.
function ResolvePrefix(): String;
var
  jsonPath: String;
  content: AnsiString;
begin
  if PrefixResolved then
  begin
    Result := CachedPrefix;
    exit;
  end;

  Result := ExpandConstant('{param:prefix|}');
  if Result = '' then
  begin
    jsonPath := WhitelabelSourceDir() + '\whitelabel.json';
    if FileExists(jsonPath) and LoadStringFromFile(jsonPath, content) then
      Result := Trim(ExtractJsonString(String(content), 'prefix'));
  end;

  CachedPrefix := Result;
  PrefixResolved := True;
end;

function AppFolder(): String;
begin
  if ResolvePrefix() = '' then
    Result := 'StorkDrop'
  else
    Result := ResolvePrefix() + '-StorkDrop';
end;

function GetAppName(Param: String): String;
begin
  Result := AppFolder();
end;

function GetInstallDir(Param: String): String;
begin
  Result := ExpandConstant('{autopf}') + '\' + AppFolder();
end;

function GetExeName(Param: String): String;
begin
  Result := AppFolder() + '.exe';
end;

function GetAppId(Param: String): String;
begin
  Result := 'StorkDrop-' + AppFolder();
end;

// "Machine" when the machine-wide task is selected, or when a previous install already chose it (so a
// silent self-update preserves the choice); otherwise "User". StorkPaths reads this value at runtime.
function GetDataLocation(Param: String): String;
var
  existing: String;
begin
  if IsTaskSelected('machinedata') then
    Result := 'Machine'
  else if RegQueryStringValue(HKEY_LOCAL_MACHINE, 'SOFTWARE\' + AppFolder(), 'DataLocation', existing)
          and (CompareText(existing, 'Machine') = 0) then
    Result := 'Machine'
  else
    Result := 'User';
end;

function IsMachineDataLocation(): Boolean;
begin
  Result := CompareText(GetDataLocation(''), 'Machine') = 0;
end;

function HasWhitelabel(): Boolean;
begin
  Result := FileExists(WhitelabelSourceDir() + '\whitelabel.json');
end;

function GetWhitelabelSource(Param: String): String;
begin
  Result := WhitelabelSourceDir() + '\*';
end;

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
