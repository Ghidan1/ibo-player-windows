; IBO Player v1.0 — Inno Setup installer script.
; Built by GitHub Actions (choco install innosetup, then ISCC.exe).
; Per-user install, no admin needed: the player only needs to read the
; user's own playlists and play video.

#define AppVersion "1.0"
#define AppExe "publish\IBOPlayer.exe"

[Setup]
AppName=IBO Player
AppVersion={#AppVersion}
AppPublisher=IBO Player
DefaultDirName={localappdata}\Programs\IBO Player
DefaultGroupName=IBO Player
OutputDir=installer
OutputBaseFilename=IBOPlayer-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\IBOPlayer.exe
WizardStyle=modern

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\IBO Player"; Filename: "{app}\IBOPlayer.exe"
Name: "{userdesktop}\IBO Player"; Filename: "{app}\IBOPlayer.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\IBOPlayer.exe"; Description: "Launch IBO Player"; Flags: nowait postinstall skipifsilent
