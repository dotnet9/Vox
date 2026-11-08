; Vox Windows 安装入口；产品安装行为与品牌外观分开维护，参照 Vex。
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
AppSupportURL=https://github.com/dotnet9/Vox/issues
LicenseFile=..\LICENSE
SetupIconFile=..\logo.ico
UninstallDisplayIcon={app}\Vox.exe
DefaultDirName={localappdata}\Programs\Vox
DefaultGroupName=Vox
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Vox-v{#AppVersion}-win-x64-setup
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2/max
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
CloseApplicationsFilter=Vox.exe
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "installer\Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.OpenWithVox=Open with Vox
chinesesimplified.OpenWithVox=用 Vox 打开
english.MarkdownAssociation=Register Vox as an opening option for Markdown files
chinesesimplified.MarkdownAssociation=注册 Vox 为 Markdown 文件的打开方式候选
english.FileAssociations=File associations:
chinesesimplified.FileAssociations=文件关联：

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "markdownassociation"; Description: "{cm:MarkdownAssociation}"; GroupDescription: "{cm:FileAssociations}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.dbg,Vox.dll.config,Vox.exe.config"; Flags: ignoreversion recursesubdirs createallsubdirs
; 保留已安装用户的配置，升级与卸载都不删除。
Source: "{#SourceDir}\Vox.dll.config"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall skipifsourcedoesntexist
Source: "{#SourceDir}\Vox.exe.config"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\Vox"; Filename: "{app}\Vox.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Vox"; Filename: "{app}\Vox.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; 只注册候选和右键入口，默认打开方式仍由用户选择。
Root: HKCU; Subkey: "Software\Classes\Vox.Markdown"; ValueType: string; ValueName: ""; ValueData: "Markdown"; Flags: uninsdeletekey; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Vox.Markdown\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"",0"; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Vox.Markdown\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"" ""%1"""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Vox"; Flags: uninsdeletekey; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"",0"; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"" ""%1"""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\Applications\Vox.exe\SupportedTypes"; ValueType: string; ValueName: ".mdown"; ValueData: ""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Vox.Markdown"; ValueData: ""; Flags: uninsdeletevalue; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Vox.Markdown"; ValueData: ""; Flags: uninsdeletevalue; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\.mdown\OpenWithProgids"; ValueType: string; ValueName: "Vox.Markdown"; ValueData: ""; Flags: uninsdeletevalue; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\OpenWithVox"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWithVox}"; Flags: uninsdeletekey; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\OpenWithVox"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Vox.exe"",0"; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.md\shell\OpenWithVox\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"" ""%1"""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\OpenWithVox"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWithVox}"; Flags: uninsdeletekey; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\OpenWithVox"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Vox.exe"",0"; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.markdown\shell\OpenWithVox\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"" ""%1"""; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mdown\shell\OpenWithVox"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWithVox}"; Flags: uninsdeletekey; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mdown\shell\OpenWithVox"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Vox.exe"",0"; Tasks: markdownassociation
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mdown\shell\OpenWithVox\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Vox.exe"" ""%1"""; Tasks: markdownassociation

[Run]
Filename: "{app}\Vox.exe"; WorkingDir: "{app}"; Description: "{cm:VStartWriting}"; Flags: runasoriginaluser nowait postinstall skipifsilent

#include "installer\Vox.Setup.iss"
