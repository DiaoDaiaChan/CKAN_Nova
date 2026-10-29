#Requires -Version 5.1
<#
.SYNOPSIS
    CKAN UI 每用户安装器：只写 HKCU，全程不请求管理员权限。

.DESCRIPTION
    把解压好的应用目录（便携包解压出来的那一坨）安装成"已安装的应用"：

        <InstallDir>\app\<版本>\      程序本体（来自 -SourceDir 的整份拷贝）
        <InstallDir>\current\version.txt   当前生效的版本号（唯一的"指针"）
        <InstallDir>\uninstall.ps1    卸载入口（UninstallString 指向它）
        <InstallDir>\LICENSE.md       SourceDir 里有时才复制
        <InstallDir>\THIRD-PARTY-NOTICES.txt   同上

    为什么用文本指针而不是 directory junction：junction 要额外权限、杀软与备份工具对它
    的处理不一致、卸载后容易留悬空链接；一个文本文件谁都能看、能改、能回滚。

    安装器只做四件事：文件落位、快捷方式、HKCU 卸载项、App Paths。
    ckan:// 协议注册不在本脚本里——应用自己在运行期注册（19 篇 §6/§12）。

    失败会回滚：已复制的目录、已写的注册表键、已建的快捷方式都按逆序撤销。

.PARAMETER SourceDir
    解压后的应用目录（必填）。要求目录里能找到主程序 exe。

.PARAMETER InstallDir
    安装根目录，默认 %LocalAppData%\CKAN UI。安装器不写 HKLM，所以不需要管理员。

.PARAMETER ExeName
    主程序文件名，默认 CkanUI.exe。主程序名正在从 CkanUI.App.exe 改成 CkanUI.exe，
    所以：不显式指定本参数时两个名字都接受（先找 CkanUI.exe，再回退 CkanUI.App.exe）；
    显式指定时只认指定名。日志里会打印实际用了哪个名字。

.PARAMETER NoShortcuts
    不创建开始菜单与桌面快捷方式。

.PARAMETER NoLaunch
    装完不启动应用。

.PARAMETER Force
    检测到同名进程正在运行时不再中止，改为警告后继续（文件可能被占用而使安装失败）。
    注意：本安装器**从不**强制结束进程，-Force 也不是"强杀"。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\install\install.ps1 -SourceDir D:\tmp\ckan-ui-0.1.0

.EXAMPLE
    # 探针/自测：装到临时目录，不建快捷方式也不启动
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\install\install.ps1 -SourceDir artifacts\install-probe\stage -InstallDir artifacts\install-probe\root -NoShortcuts -NoLaunch
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $SourceDir,

    [string] $InstallDir = '',

    [string] $ExeName = 'CkanUI.exe',

    [switch] $NoShortcuts,

    [switch] $NoLaunch,

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 控制台按 UTF-8 输出：脚本里的中文在 bash / CI 日志里才不会变成乱码。
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:StepNo = 0
$script:Rollback = New-Object System.Collections.ArrayList
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:Utf8Bom = New-Object System.Text.UTF8Encoding($true)

function Write-Step {
    param([string] $Message)
    $script:StepNo++
    Write-Host ''
    Write-Host ('[{0}] {1}' -f $script:StepNo, $Message) -ForegroundColor Cyan
}

function Write-Info {
    param([string] $Message)
    Write-Host ('    ' + $Message)
}

function Write-Note {
    param([string] $Message)
    Write-Host ('    注意：' + $Message) -ForegroundColor Yellow
}

function Add-Rollback {
    param([scriptblock] $Action)
    [void]$script:Rollback.Add($Action)
}

function Exit-WithError {
    param([string] $Message, [int] $Code = 1)
    Write-Host ''
    Write-Host ('失败：' + $Message) -ForegroundColor Red
    exit $Code
}

function Resolve-AbsolutePath {
    param([string] $Path)
    $trimmed = $Path.Trim().Trim('"')
    if ([System.IO.Path]::IsPathRooted($trimmed)) {
        return [System.IO.Path]::GetFullPath($trimmed).TrimEnd('\')
    }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $trimmed)).TrimEnd('\')
}

# 整棵树复制：用 .NET 而不用 Copy-Item，一是快（几百 MB 的 Debug 输出），
# 二是能自己建空目录、自己算字节数，出错信息也更贴近文件。
function Copy-Tree {
    param([string] $From, [string] $To)

    $sourceRoot = (Resolve-Path -LiteralPath $From).ProviderPath.TrimEnd('\')
    [void][System.IO.Directory]::CreateDirectory($To)

    foreach ($dir in [System.IO.Directory]::GetDirectories($sourceRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $rel = $dir.Substring($sourceRoot.Length).TrimStart('\')
        [void][System.IO.Directory]::CreateDirectory((Join-Path $To $rel))
    }

    $count = 0
    $bytes = 0L
    foreach ($file in [System.IO.Directory]::GetFiles($sourceRoot, '*', [System.IO.SearchOption]::AllDirectories)) {
        $rel = $file.Substring($sourceRoot.Length).TrimStart('\')
        $dest = Join-Path $To $rel
        $parent = [System.IO.Path]::GetDirectoryName($dest)
        if (-not [System.IO.Directory]::Exists($parent)) {
            [void][System.IO.Directory]::CreateDirectory($parent)
        }
        [System.IO.File]::Copy($file, $dest, $true)
        $count++
        $bytes += ([System.IO.FileInfo]::new($file)).Length
    }

    return [pscustomobject]@{ Files = $count; Bytes = $bytes }
}

function Get-DirectorySize {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 0L }
    $sum = 0L
    foreach ($file in [System.IO.Directory]::GetFiles($Path, '*', [System.IO.SearchOption]::AllDirectories)) {
        $sum += ([System.IO.FileInfo]::new($file)).Length
    }
    return $sum
}

function New-Shortcut {
    param(
        [string] $Path,
        [string] $Target,
        [string] $WorkingDirectory,
        [string] $Description = ''
    )

    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($Path)
        $link.TargetPath = $Target
        $link.WorkingDirectory = $WorkingDirectory
        $link.IconLocation = ($Target + ',0')
        if ($Description) { $link.Description = $Description }
        $link.Save()
    }
    finally {
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

# 逆序回滚：后登记的动作先撤销，天然处理嵌套（先删 stage，再删 app\，最后删 InstallDir）。
function Invoke-Rollback {
    param([string] $Reason)

    Write-Host ''
    Write-Host ('安装失败，开始回滚：' + $Reason) -ForegroundColor Yellow
    for ($i = $script:Rollback.Count - 1; $i -ge 0; $i--) {
        try {
            & $script:Rollback[$i]
        }
        catch {
            Write-Host ('    回滚步骤跳过：' + $_.Exception.Message) -ForegroundColor Yellow
        }
    }
    Write-Host '回滚完成，台面上不留半装状态。' -ForegroundColor Yellow
}

# ---- 1. 校验参数、定位主程序 ------------------------------------------------

Write-Step '校验参数与源目录'

$sourcePath = Resolve-AbsolutePath $SourceDir
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    Exit-WithError ('源目录不存在或不是目录：' + $sourcePath)
}
Write-Info ('源目录 ' + $sourcePath)

if (-not $InstallDir) {
    if (-not $env:LOCALAPPDATA) {
        Exit-WithError '当前账户没有 LOCALAPPDATA 环境变量，请显式指定 -InstallDir。'
    }
    $InstallDir = Join-Path $env:LOCALAPPDATA 'CKAN UI'
}
$installPath = Resolve-AbsolutePath $InstallDir
if ([System.IO.Path]::GetPathRoot($installPath) -eq $installPath) {
    Exit-WithError ('安装目录不能是盘符根：' + $installPath)
}
Write-Info ('安装目录 ' + $installPath)

# 主程序名：不显式指定 -ExeName 时接受改名前后的两个名字。
if ($PSBoundParameters.ContainsKey('ExeName') -and $ExeName -ne 'CkanUI.exe') {
    $exeCandidates = @($ExeName)
}
else {
    $exeCandidates = @('CkanUI.exe', 'CkanUI.App.exe')
}

$sourceExeName = ''
$sourceExePath = ''
foreach ($candidate in $exeCandidates) {
    $probe = Join-Path $sourcePath $candidate
    if (Test-Path -LiteralPath $probe -PathType Leaf) {
        $sourceExeName = $candidate
        $sourceExePath = $probe
        break
    }
}
if (-not $sourceExeName) {
    Exit-WithError ('源目录里找不到主程序，试过：' + ($exeCandidates -join '、') + '（目录 ' + $sourcePath + '）')
}
Write-Info ('主程序 ' + $sourceExeName + '（候选：' + ($exeCandidates -join '、') + '）')

$version = ''
try {
    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sourceExePath).FileVersion
}
catch {
    $version = ''
}
if (-not $version) { $version = '0.0.0' }
# 版本号要当目录名用：把非法字符换掉，免得一条脏版本号把安装目录搞成非法路径。
$version = ($version -replace '[^0-9A-Za-z.\-_+]', '-').Trim()
if (-not $version) { $version = '0.0.0' }
Write-Info ('版本 ' + $version + '（读自 ' + $sourceExeName + ' 的 FileVersion）')

# ---- 2. 检测同名进程 --------------------------------------------------------

Write-Step '检测正在运行的进程'

$processNames = @($exeCandidates | ForEach-Object { $_ -replace '\.exe$', '' })
$running = @()
foreach ($name in $processNames) {
    foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        $procPath = ''
        try { $procPath = $proc.Path } catch { $procPath = '' }
        $running += [pscustomobject]@{ Id = $proc.Id; Name = $proc.ProcessName; Path = $procPath }
    }
}

if ($running.Count -gt 0) {
    foreach ($item in $running) {
        $where = if ($item.Path) { $item.Path } else { '（路径不可读）' }
        Write-Info ('运行中：' + $item.Name + ' PID ' + $item.Id + ' ' + $where)
    }
    if (-not $Force) {
        Write-Host ''
        Write-Host '请先关闭 CKAN UI 再重新运行安装器。' -ForegroundColor Red
        Write-Host '（本安装器不会强制结束进程；确实要带锁安装可加 -Force，但可能装到一半失败。）' -ForegroundColor Yellow
        exit 2
    }
    Write-Note '检测到运行中的进程，-Force 下继续安装（不结束进程），文件被占用时会失败并回滚。'
}
else {
    Write-Info '没有同名进程在运行。'
}

# ---- 3. 预检：记录既有状态，准备回滚 ----------------------------------------

Write-Step '预检目标目录'

$appRoot = Join-Path $installPath 'app'
$targetDir = Join-Path $appRoot $version
$currentDir = Join-Path $installPath 'current'

$installExisted = Test-Path -LiteralPath $installPath
$appRootExisted = Test-Path -LiteralPath $appRoot
$targetExisted = Test-Path -LiteralPath $targetDir

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI'
$appPathsRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths'
$appPathsKey = Join-Path $appPathsRoot $sourceExeName
$uninstallKeyExisted = Test-Path -LiteralPath $uninstallKey
$appPathsKeyExisted = Test-Path -LiteralPath $appPathsKey

$startMenuDir = [Environment]::GetFolderPath('Programs')
$desktopDir = [Environment]::GetFolderPath('Desktop')
$startMenuLink = Join-Path $startMenuDir 'CKAN UI.lnk'
$desktopLink = Join-Path $desktopDir 'CKAN UI.lnk'

# 回滚顺序的登记方式：先登记的（最外层目录）最后撤销。
if ($targetExisted) {
    Write-Info ('目标版本目录已存在，将整体替换：' + $targetDir)
}
else {
    Write-Info ('目标版本目录：' + $targetDir)
}
if (-not $installExisted) {
    $createdInstallRoot = $installPath
    Add-Rollback { if (Test-Path -LiteralPath $createdInstallRoot) { Remove-Item -LiteralPath $createdInstallRoot -Recurse -Force -ErrorAction SilentlyContinue } }.GetNewClosure()
}

# ---- 4. 复制到 app\<版本>（先落 staging，再原子切换） -----------------------

Write-Step '复制应用文件'

[void][System.IO.Directory]::CreateDirectory($appRoot)

$stageDir = Join-Path $appRoot ('.staging-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
$stageCleanup = $stageDir
Add-Rollback { if (Test-Path -LiteralPath $stageCleanup) { Remove-Item -LiteralPath $stageCleanup -Recurse -Force -ErrorAction SilentlyContinue } }.GetNewClosure()

try {
    $copy = Copy-Tree -From $sourcePath -To $stageDir
}
catch {
    Invoke-Rollback ('复制文件失败：' + $_.Exception.Message)
    exit 3
}

$copiedExe = Join-Path $stageDir $sourceExeName
if (-not (Test-Path -LiteralPath $copiedExe -PathType Leaf)) {
    Invoke-Rollback ('复制后找不到主程序 ' + $copiedExe)
    exit 3
}
Write-Info ('已复制 ' + $copy.Files + ' 个文件，' + [math]::Round($copy.Bytes / 1MB, 1) + ' MB')

$backupDir = ''
if ([System.IO.Directory]::Exists($targetDir)) {
    $backupDir = Join-Path $appRoot ('.backup-' + $version + '-' + (Get-Date -Format 'yyyyMMddHHmmss'))
    try {
        [System.IO.Directory]::Move($targetDir, $backupDir)
        $restoreFrom = $backupDir
        $restoreTo = $targetDir
        Add-Rollback { if ((Test-Path -LiteralPath $restoreFrom) -and -not (Test-Path -LiteralPath $restoreTo)) { [System.IO.Directory]::Move($restoreFrom, $restoreTo) } }.GetNewClosure()
    }
    catch {
        Invoke-Rollback ('无法替换已存在的版本目录（可能有文件被占用）：' + $_.Exception.Message)
        exit 3
    }
}

try {
    [System.IO.Directory]::Move($stageDir, $targetDir)
}
catch {
    Invoke-Rollback ('无法就位到 ' + $targetDir + '：' + $_.Exception.Message)
    exit 3
}

if ($backupDir) {
    Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
}

$appExePath = Join-Path $targetDir $sourceExeName
Write-Info ('程序就位：' + $appExePath)

# ---- 5. current 指针 --------------------------------------------------------

Write-Step '更新 current 指针'

$currentExisted = Test-Path -LiteralPath $currentDir
[void][System.IO.Directory]::CreateDirectory($currentDir)
if (-not $currentExisted) {
    $createdCurrent = $currentDir
    Add-Rollback { if (Test-Path -LiteralPath $createdCurrent) { Remove-Item -LiteralPath $createdCurrent -Recurse -Force -ErrorAction SilentlyContinue } }.GetNewClosure()
}

$versionFile = Join-Path $currentDir 'version.txt'
[System.IO.File]::WriteAllText($versionFile, ($version + [Environment]::NewLine), $script:Utf8NoBom)

$currentReadme = @(
    '本目录是「当前版本」指针，不含程序文件。',
    '',
    '  version.txt   当前生效的版本号',
    '  程序本体      ..\app\<版本号>\',
    '  卸载入口      ..\uninstall.ps1',
    '',
    '升级时安装器只改 version.txt 的内容，旧版本目录保留在 ..\app\ 下，',
    '确认新版本可用后可以手工删除旧目录。卸载脚本会整个删掉 ..\app\。',
    ''
) -join [Environment]::NewLine
[System.IO.File]::WriteAllText((Join-Path $currentDir 'README.txt'), $currentReadme, $script:Utf8Bom)

Write-Info ('current 指向 ' + $version)

# ---- 6. 许可证与卸载入口 ----------------------------------------------------

Write-Step '复制许可证与卸载入口'

foreach ($notice in @('LICENSE.md', 'THIRD-PARTY-NOTICES.txt')) {
    $noticeSource = Join-Path $sourcePath $notice
    if (Test-Path -LiteralPath $noticeSource -PathType Leaf) {
        Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $installPath $notice) -Force
        Write-Info ('已复制 ' + $notice)
    }
    else {
        Write-Info ('源目录没有 ' + $notice + '，跳过')
    }
}

$uninstallSource = Join-Path $PSScriptRoot 'uninstall.ps1'
$uninstallTarget = Join-Path $installPath 'uninstall.ps1'
if (Test-Path -LiteralPath $uninstallSource -PathType Leaf) {
    Copy-Item -LiteralPath $uninstallSource -Destination $uninstallTarget -Force
    Write-Info ('已复制 uninstall.ps1 → ' + $uninstallTarget)
}
else {
    Write-Note ('找不到 ' + $uninstallSource + '，卸载项会指向不存在的脚本。')
}

# ---- 7. 快捷方式 ------------------------------------------------------------

Write-Step '创建快捷方式'

if ($NoShortcuts) {
    Write-Info '按 -NoShortcuts 跳过。'
}
else {
    $shortcutTargets = @(
        [pscustomobject]@{ Path = $startMenuLink; Label = '开始菜单' },
        [pscustomobject]@{ Path = $desktopLink; Label = '桌面' }
    )
    foreach ($item in $shortcutTargets) {
        $linkPath = $item.Path
        if (-not (Test-Path -LiteralPath ([System.IO.Path]::GetDirectoryName($linkPath)))) {
            Write-Note ($item.Label + '目录不存在，跳过：' + $linkPath)
            continue
        }
        $existedBefore = Test-Path -LiteralPath $linkPath
        try {
            New-Shortcut -Path $linkPath -Target $appExePath -WorkingDirectory $targetDir -Description 'CKAN UI'
            if (-not $existedBefore) {
                $createdLink = $linkPath
                Add-Rollback { if (Test-Path -LiteralPath $createdLink) { Remove-Item -LiteralPath $createdLink -Force -ErrorAction SilentlyContinue } }.GetNewClosure()
            }
            Write-Info ($item.Label + '快捷方式：' + $linkPath)
        }
        catch {
            Write-Note ('创建' + $item.Label + '快捷方式失败（不影响安装）：' + $_.Exception.Message)
        }
    }
}

# ---- 8. 注册表：HKCU 卸载项 + App Paths ------------------------------------

Write-Step '写 HKCU 注册项（不碰 HKLM）'

$psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$uninstallCommand = '"' + $psExe + '" -NoProfile -ExecutionPolicy Bypass -File "' + $uninstallTarget + '"'
$quietUninstallCommand = $uninstallCommand + ' -Force'
$estimatedSizeKb = [int][math]::Min([math]::Round((Get-DirectorySize $appRoot) / 1KB), [uint32]::MaxValue)

try {
    if (-not $uninstallKeyExisted) {
        [void](New-Item -Path $uninstallKey -Force)
        Add-Rollback { if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue } }
    }
    if (-not $appPathsKeyExisted) {
        [void](New-Item -Path $appPathsKey -Force)
        Add-Rollback { if (Test-Path -LiteralPath $appPathsKey) { Remove-Item -LiteralPath $appPathsKey -Recurse -Force -ErrorAction SilentlyContinue } }
    }

    $stringValues = [ordered]@{
        DisplayName          = 'CKAN UI'
        DisplayVersion       = $version
        Publisher            = 'CKAN UI Project'
        DisplayIcon          = ($appExePath + ',0')
        InstallLocation      = $installPath
        UninstallString      = $uninstallCommand
        QuietUninstallString = $quietUninstallCommand
    }
    foreach ($name in $stringValues.Keys) {
        New-ItemProperty -LiteralPath $uninstallKey -Name $name -Value $stringValues[$name] -PropertyType String -Force | Out-Null
    }

    New-ItemProperty -LiteralPath $uninstallKey -Name 'EstimatedSize' -Value $estimatedSizeKb -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
    Write-Info ('HKCU 卸载项：' + $uninstallKey.Substring(5) + '（EstimatedSize ' + $estimatedSizeKb + ' KB）')

    # App Paths 的键名必须等于真实 exe 名，Win+R 才找得到我们。
    Set-Item -LiteralPath $appPathsKey -Value $appExePath -Force
    New-ItemProperty -LiteralPath $appPathsKey -Name 'Path' -Value $targetDir -PropertyType String -Force | Out-Null
    Write-Info ('App Paths：' + $sourceExeName + ' → ' + $appExePath)

    # 主程序改过名时，清掉指向本安装目录的旧键；指向别处的一律不动。
    foreach ($stale in $exeCandidates) {
        if ($stale -eq $sourceExeName) { continue }
        $staleKey = Join-Path $appPathsRoot $stale
        if (-not (Test-Path -LiteralPath $staleKey)) { continue }
        $staleValue = (Get-Item -LiteralPath $staleKey).GetValue('')
        if ($staleValue -and ($staleValue -like ($installPath + '\*'))) {
            Remove-Item -LiteralPath $staleKey -Recurse -Force
            Write-Info ('清理改名前的 App Paths 键：' + $stale)
        }
    }
}
catch {
    Invoke-Rollback ('写注册表失败：' + $_.Exception.Message)
    exit 3
}

# ---- 9. 完成 ----------------------------------------------------------------

Write-Step '完成'

if ($NoLaunch) {
    Write-Info '按 -NoLaunch 不启动应用。'
}
else {
    try {
        Start-Process -FilePath $appExePath -WorkingDirectory $targetDir
        Write-Info '已启动 ' + $sourceExeName
    }
    catch {
        Write-Note ('启动失败（不影响安装）：' + $_.Exception.Message)
    }
}

Write-Host ''
Write-Host '安装完成。' -ForegroundColor Green
Write-Info ('版本     ' + $version)
Write-Info ('程序     ' + $appExePath)
Write-Info ('安装目录 ' + $installPath)
Write-Info ('卸载     ' + $uninstallTarget)
Write-Info '卸载项在「设置 → 应用 → 已安装的应用」里显示为 CKAN UI。'
exit 0
