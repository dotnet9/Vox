; Vox Windows 安装包（Inno Setup 6）。
; 从仓库根目录编译：ISCC /DAppVersion=x.y.z scripts/installer/Vox.Setup.iss

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif

#ifndef SourceDir
#define SourceDir "..\artifacts\publish\Vox.App-win-x64"
#endif

#ifndef OutputDir
#define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{0D240AA3-DEBB-4F65-BEE5-CD2FDEE72227}
AppName=Vox
AppVersion={#AppVersion}
AppPublisher=CodeWF
SetupIconFile=..\..\logo.ico
AppSupportURL=https://github.com/dotnet9/Vox/issues
DefaultDirName={localappdata}\Programs\Vox
DefaultGroupName=Vox
DisableProgramGroupPage=yes
; 极简流（对齐原型）：跳过欢迎页与目录页（固定按用户目录），流程=语言 → 选项 → 安装 → 完成。
DisableWelcomePage=yes
DisableDirPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Vox-v{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 按用户安装：无需管理员权限（决策 D5/D11：内置 git、凭据均按用户落地）。
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=Vox.exe
RestartApplications=no
UninstallDisplayIcon={app}\Vox.exe
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Vox"; Filename: "{app}\Vox.exe"
Name: "{autodesktop}\Vox"; Filename: "{app}\Vox.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Vox.exe"; Description: "{cm:LaunchProgram,Vox}"; Flags: nowait postinstall skipifsilent
