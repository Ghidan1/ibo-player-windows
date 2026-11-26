; Zaghlol Player v2.0.3 — Inno Setup installer script.
; Built by GitHub Actions (choco install innosetup, then ISCC.exe).
; Per-user install, no admin needed: the player only reads the
; user's own playlists and plays video.

#define AppVersion "2.0.3"
#define AppExe "publish\ZaghlolPlayer.exe"

[Setup]
AppName=Zaghlol Player
AppVersion={#AppVersion}
AppPublisher=Zaghlol Player
DefaultDirName={localappdata}\Programs\Zaghlol Player
DefaultGroupName=Zaghlol Player
OutputDir=installer
OutputBaseFilename=ZaghlolPlayer-Setup-{#AppVersion}
SetupIconFile=z.ico
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\ZaghlolPlayer.exe
WizardStyle=modern

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Zaghlol Player"; Filename: "{app}\ZaghlolPlayer.exe"
Name: "{userdesktop}\Zaghlol Player"; Filename: "{app}\ZaghlolPlayer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\ZaghlolPlayer.exe"; Description: "Launch Zaghlol Player"; Flags: nowait postinstall skipifsilent
