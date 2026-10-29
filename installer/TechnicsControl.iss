; Technics Control インストーラー (Inno Setup 6)
; ビルド例:
;   dotnet publish src/TechnicsControl.App/TechnicsControl.App.csproj -c Release -p:Platform=x64 -o artifacts/publish
;   ISCC.exe /DAppVersion=1.0.0 /DAppVersionNumeric=1.0.0.0 installer/TechnicsControl.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppVersionNumeric
  #define AppVersionNumeric "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Technics Control"
#define AppExeName "TechnicsControl.exe"

[Setup]
AppId={{FB3A4F92-DD54-4B02-87C5-1C78F6E8335B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Fuku856
AppPublisherURL=https://github.com/Fuku856/TechnicsControl
AppSupportURL=https://github.com/Fuku856/TechnicsControl/issues
VersionInfoVersion={#AppVersionNumeric}
; 自動起動は HKCU の Run キーを使うので、ユーザー単位 (%LOCALAPPDATA%\Programs) にインストールする
PrivilegesRequired=lowest
DefaultDirName={autopf}\TechnicsControl
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=TechnicsControl-{#AppVersion}-setup-x64
SetupIconFile=..\src\TechnicsControl.App\Assets\Official\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; StartupService が登録する自動起動エントリを、アンインストール時に削除する
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TechnicsControl"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
