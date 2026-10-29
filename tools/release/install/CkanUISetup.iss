; ============================================================================
; CKAN UI 安装脚本模板（Inno Setup 6）
;
; ⚠ 本机（交付这台机器）没有安装 Inno Setup，**本模板未实测构建**。
;    它只是把 install.ps1 的那套布局与注册项翻译成 Inno 的写法，供有 Inno 的机器/CI 使用。
;    用法、参数与两种安装方式的取舍见 docs/ops/install.md。
;
; 定位：per-user、免提权、不写 HKLM，与 tools/release/install/install.ps1 等价：
;     {localappdata}\CKAN UI\app\<版本>\   程序本体（[Files] 从 {#StageDir} 整份拷入）
;     {localappdata}\CKAN UI\current\version.txt   当前版本指针
;     HKCU\...\Uninstall\{AppId}_is1       Inno 自动生成（我们不重复写）
;     HKCU\...\App Paths\CkanUI.exe        [Registry] 里补（Inno 不管这块）
;
; 构建（在装了 Inno Setup 6.3+ 的机器上）：
;     iscc.exe /DStageDir="D:\build\stage-win-x64-selfcontained" ^
;              /DAppVersion=0.1.0 tools\release\install\CkanUISetup.iss
;     不给 /DStageDir 时用下面的默认值（release.ps1 的 _work 目录）。
;
; 与 install.ps1 的差异（有意为之，别"修"）：
;   * 卸载由 Inno 自己的 uninst000.exe 负责，不需要 uninstall.ps1；
;   * UninstallString / DisplayName / DisplayVersion / Publisher / EstimatedSize 由 Inno
;     依据 [Setup] 里的 AppName / AppVersion / AppPublisher 自动写，重复写会打架；
;   * 运行中进程检测由 CloseApplications 交给 Restart Manager，install.ps1 才是自己查的。
; ============================================================================

#define AppName "CKAN UI"
#define AppPublisher "CKAN UI Project"
#define AppExeName "CkanUI.exe"

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#ifndef StageDir
  #define StageDir "..\..\..\artifacts\release\_work\stage-win-x64-selfcontained"
#endif

[Setup]
; AppId 一旦发布就不能改：它决定卸载项的键名与升级识别。{{ 是 Inno 里转义的花括号。
AppId={{8F3D2A51-6C4B-4E27-9B7A-2D5E8C1F4A93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} 安装程序
VersionInfoCompany={#AppPublisher}

; per-user：装到 %LocalAppData%，全程不弹 UAC（对应 19 篇 I-01 / Q1）。
DefaultDirName={localappdata}\CKAN UI
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; x64compatible 需要 Inno Setup 6.3+；更老的版本改成 x64。
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Win10 1607 下限（11 篇 §7）：低于此版本在 Setup 第一步就明确拒绝。
MinVersion=10.0.14393

OutputDir=..\..\..\artifacts\release
OutputBaseFilename=CkanUISetup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 未签名构建：安装器自己的文件名会带 -unsigned（见 docs/ops/install.md §签名）。
; 有证书时在构建后用 tools/release/Sign-Artifacts.ps1 签 CkanUISetup-*.exe。

; 卸载项图标与名称指向实际程序（19 篇 I-02：控制面板里图标/名称要正确）。
UninstallDisplayIcon={app}\app\{#AppVersion}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}

; 应用在跑时由 Restart Manager 提示关闭（Inno 6.3+）；我们不替用户强杀。
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 整份 stage 进 app\<版本>\：与 install.ps1 的 <InstallDir>\app\<版本> 一一对应。
; 不带 unins000.exe 之类的 Inno 文件，卸载器在安装根目录。
Source: "{#StageDir}\*"; DestDir: "{app}\app\{#AppVersion}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 版本指针 current\version.txt 由下面的 [Code] 段写入（不额外要求旁边放一个 version.txt），
; 这样 App 侧可以只用一套路径解析逻辑：读 current\version.txt 找 app\<版本>。

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\app\{#AppVersion}\{#AppExeName}"; WorkingDir: "{app}\app\{#AppVersion}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\app\{#AppVersion}\{#AppExeName}"; WorkingDir: "{app}\app\{#AppVersion}"; Tasks: desktopicon

[Registry]
; App Paths：让 Win+R / start 直接找到主程序（HKCU，卸载时由 uninsdeletekey 删）。
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: ""; ValueData: "{app}\app\{#AppVersion}\{#AppExeName}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\{#AppExeName}"; ValueType: string; ValueName: "Path"; ValueData: "{app}\app\{#AppVersion}"; Flags: uninsdeletekey

; ckan:// 协议与 .ckan 关联**不在这里写**：应用运行期自己注册（19 篇 §6/§12），
; 安装器重复注册会在"设置 → 集成"里出现两份真相，卸载后也清不干净。

[Run]
Filename: "{app}\app\{#AppVersion}\{#AppExeName}"; WorkingDir: "{app}\app\{#AppVersion}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
{ 写 current\version.txt：install.ps1 是直接 WriteAllText，这里用 Inno 的等价物。 }
procedure CurStepChanged(CurStep: TSetupStep);
var
  CurrentDir: String;
  VersionFile: String;
begin
  if CurStep = ssPostInstall then
  begin
    CurrentDir := ExpandConstant('{app}\current');
    ForceDirectories(CurrentDir);
    VersionFile := CurrentDir + '\version.txt';
    SaveStringToFile(VersionFile, '{#AppVersion}' + #13#10, False);
  end;
end;

[UninstallDelete]
; 旧版本目录（app\ 下可能留着装过的多个版本）与版本指针都随卸载清掉；
; 用户数据在 %LocalAppData%\CKAN（与 ckan.exe 共享），不在这里、也不能删。
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\current"
