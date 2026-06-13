#define MyAppName "SX3 Scanner"
#define MyAppVersion "4.4.0"
#define MyAppPublisher "JBZVN"
#define MyAppExeName "SX3 SCANER.exe"
#define MySourceDir "..\ReleaseOutput"
#define MyRepository "hieuvipro94x/sx3-scanner-release"

[Setup]
AppId={{A6E812B1-9F30-4D3C-A9E2-0A0B0C0D0E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/{#MyRepository}
AppSupportURL=https://github.com/{#MyRepository}/issues
AppUpdatesURL=https://github.com/{#MyRepository}/releases
DefaultDirName={autopf}\JBZVN\SX3 Scanner
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
OutputDir=..\InstallerOutput
OutputBaseFilename=SX3ScannerSetup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UsedUserAreasWarning=no
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoCopyright=Copyright © 2026 NAM

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Dirs]
Name: "{commonappdata}\JBZVN\SX3 Scanner"; Permissions: users-modify
Name: "{commonappdata}\JBZVN\SX3 Scanner\config"; Permissions: users-modify
Name: "{commonappdata}\JBZVN\SX3 Scanner\cache\updates"; Permissions: users-modify

[InstallDelete]
Type: files; Name: "{userstartup}\SX3 SCANER.lnk"
Type: files; Name: "{commonstartup}\SX3 SCANER.lnk"

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "database.db,product.db,database.db-wal,database.db-shm,product.db-wal,product.db-shm,database.db-journal,product.db-journal"; Flags: ignoreversion
Source: "{#MySourceDir}\x64\*"; DestDir: "{app}\x64"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#MySourceDir}\x86\*"; DestDir: "{app}\x86"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\Sounds\*.wav"; DestDir: "{app}\Sounds"; Flags: ignoreversion

[Icons]
Name: "{group}\SX3 Scanner"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Release note"; Filename: "{app}\release-note.txt"
Name: "{commondesktop}\SX3 Scanner"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""SX3 Scanner"" /TR ""{app}\{#MyAppExeName}"" /SC ONLOGON /RL HIGHEST"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Open SX3 Scanner"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillProcessByName(FileName: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM "' + FileName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup(): Boolean;
begin
  KillProcessByName('SX3 SCANER.exe');
  Sleep(2000);
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillProcessByName('SX3 SCANER.exe');
  Sleep(2000);
  Result := '';
end;
