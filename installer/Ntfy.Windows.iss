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
AppPublisher=ntfy-windows
DefaultDirName={localappdata}\Programs\ntfy-windows
DefaultGroupName=ntfy-windows
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
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

[Run]
Filename: "{app}\ntfy-windows.exe"; Description: "Launch ntfy-windows"; Flags: nowait postinstall skipifsilent
