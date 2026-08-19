#define MyAppName "一梦江湖登录修复"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "YiMengFix"
#define MyAppExeName "YiMengFix.exe"

[Setup]
AppId={{B9F3F4A8-6072-4BA8-A4BB-2403D27C2A47}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\YiMengFix
DefaultGroupName={#MyAppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=YiMengFix-Setup-{#MyAppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes
SetupIconFile=..\src\YiMengFix.UI\Assets\app-icon.ico

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\artifacts\portable-single\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
