#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif

#if AppArch == "x64"
  #define ArchitectureName "x64"
#elif AppArch == "arm64"
  #define ArchitectureName "arm64"
#else
  #define ArchitectureName "x86"
#endif

[Setup]
AppId={{9B078E63-3A65-4AC8-B922-9FFCB3BE9C0A}
AppName=ntfy-windows
AppVersion={#AppVersion}
AppVerName=ntfy-windows
AppPublisher=ntfy-windows
DefaultDirName={autopf}\ntfy-windows
UsePreviousAppDir=no
DefaultGroupName=ntfy-windows
DisableProgramGroupPage=yes
PrivilegesRequired=admin
UsedUserAreasWarning=no
OutputDir=..\artifacts
OutputBaseFilename=ntfy-windows-win-{#ArchitectureName}-setup
SetupIconFile=..\Assets\Icons\ntfy.ico
UninstallDisplayIcon={app}\ntfy-windows.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
#if AppArch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif AppArch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ntfy-windows"; Filename: "{app}\ntfy-windows.exe"
Name: "{autodesktop}\ntfy-windows"; Filename: "{app}\ntfy-windows.exe"; Tasks: desktopicon

[InstallDelete]
Type: filesandordirs; Name: "{localappdata}\Programs\ntfy-windows"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "ntfy-windows"; Flags: uninsdeletevalue

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM ntfy-windows.exe /F >nul 2>&1"; Flags: runhidden waituntilterminated; RunOnceId: "StopNtfyWindows"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\NtfyWindows"

[Run]
Filename: "{app}\ntfy-windows.exe"; Description: "Launch ntfy-windows"; Flags: nowait postinstall skipifsilent

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'),
    '/C taskkill /IM ntfy-windows.exe /F >nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
