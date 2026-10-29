#Requires -Version 5.1
<#
.SYNOPSIS
    卸载 CKAN UI：删文件、删快捷方式、删 HKCU 注册项，默认保留用户数据。

.DESCRIPTION
    与 install.ps1 对称：只动 HKCU 与本程序的安装目录，不碰 HKLM。

    默认保留（-KeepUserData 默认 $true）：
        %LocalAppData%\CKAN\config.json    与 ckan.exe / CLI 共享，删了等于"卸载 mod 数据"
        <GameDir>\CKAN\registry.json       实例的已装 mod 记录
        %LocalAppData%\CKAN\repos\         仓库缓存（重装省一次几十 MB 下载）
        %LocalAppData%\CKAN\CKAN.log       排障日志（可能是 ckan.exe 写的）

    -RemoveUserData 只删"本程序独占"的那几件：ckan-ui.json、ckan-ui.secrets、
    ckanui-<日期>.log。共享文件一律不动——这是 19 篇 §8 / R-65 的硬约束。

    误删守卫（仿 release.ps1 的 _work 守卫）：安装目录的叶子名不能是系统目录名，
    路径必须能解析出父目录，且目录里要能看到 app\ 或 current\ 或 uninstall.ps1
    这类"我们自己的东西"，否则拒绝删除。

.PARAMETER InstallDir
    安装根目录，默认 %LocalAppData%\CKAN UI（与 install.ps1 的默认值一致）。

.PARAMETER KeepUserData
    是否保留用户数据，默认 $true。显式关闭：-KeepUserData:$false。

.PARAMETER RemoveUserData
    删除本程序独占的用户数据（等价于 -KeepUserData:$false，但只删我们自己的文件）。

.PARAMETER Force
    不再询问，直接结束运行中的进程并静默卸载（UninstallString 的静默形态）。

.PARAMETER ExeName
    额外的 exe 名候选。默认会把 CkanUI.exe 与 CkanUI.App.exe 两个 App Paths 键都清掉，
    以覆盖"主程序改名前装过"的机器。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\install\uninstall.ps1

.EXAMPLE
    # 探针/自测
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release\install\uninstall.ps1 -InstallDir artifacts\install-probe\root -Force
#>
[CmdletBinding()]
param(
    [string] $InstallDir = '',

    [bool] $KeepUserData = $true,

    [switch] $RemoveUserData,

    [switch] $Force,

    [string[]] $ExeName = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:StepNo = 0

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

# ---- 1. 解析并守卫安装目录 --------------------------------------------------

Write-Step '解析安装目录'

if (-not $InstallDir) {
    # 从「已安装的副本」运行时（<InstallDir>\uninstall.ps1，安装器拷贝过去的那份），
    # 用脚本自己所在目录当安装根：UninstallString 调它时并不带 -InstallDir，
    # 若这里死退回默认路径，装在非默认位置（例如探针目录）的安装就会卸错地方。
    $ownDir = $PSScriptRoot
    if ($ownDir -and ((Test-Path -LiteralPath (Join-Path $ownDir 'app')) -or (Test-Path -LiteralPath (Join-Path $ownDir 'current')))) {
        $InstallDir = $ownDir
        Write-Info ('未给 -InstallDir，按脚本所在目录推断：' + $ownDir)
    }
    else {
        if (-not $env:LOCALAPPDATA) {
            Exit-WithError '当前账户没有 LOCALAPPDATA 环境变量，请显式指定 -InstallDir。'
        }
        $InstallDir = Join-Path $env:LOCALAPPDATA 'CKAN UI'
    }
}
$installPath = Resolve-AbsolutePath $InstallDir
Write-Info ('安装目录 ' + $installPath)

if ([System.IO.Path]::GetPathRoot($installPath) -eq $installPath) {
    Exit-WithError ('拒绝删除盘符根目录：' + $installPath) 3
}
$leaf = [System.IO.Path]::GetFileName($installPath)
$parent = [System.IO.Path]::GetDirectoryName($installPath)
if (-not $leaf) {
    Exit-WithError ('无法解析安装目录的叶子名，拒绝删除：' + $installPath) 3
}
if (-not $parent) {
    Exit-WithError ('安装目录没有父目录，拒绝删除：' + $installPath) 3
}

$forbiddenLeaves = @(
    'windows', 'program files', 'program files (x86)', 'programdata', 'users',
    'appdata', 'localappdata', 'roaming', 'system32', 'syswow64', 'temp', 'tmp',
    'downloads', 'documents', 'desktop', 'start menu', 'ckan'
)
if ($forbiddenLeaves -contains $leaf.ToLowerInvariant()) {
    Exit-WithError ('拒绝删除系统或共享目录：' + $installPath) 3
}

$dirExists = Test-Path -LiteralPath $installPath -PathType Container
if ($dirExists) {
    # 只认"看起来是我们装的"目录：有 app\ 或 current\ 或 uninstall.ps1。
    $looksOurs = $false
    foreach ($marker in @('app', 'current', 'uninstall.ps1')) {
        if (Test-Path -LiteralPath (Join-Path $installPath $marker)) { $looksOurs = $true; break }
    }
    if (-not $looksOurs) {
        Exit-WithError ('目录里没有 app\、current\ 或 uninstall.ps1，不像 CKAN UI 的安装目录，拒绝删除：' + $installPath + '（确认要删就先手工清空该目录）') 3
    }
    Write-Info '目录存在，且带 CKAN UI 的安装标记。'
}
else {
    Write-Info '目录不存在，只清理注册表与快捷方式。'
}

# ---- 2. 结束运行中的进程 ----------------------------------------------------

Write-Step '检测并结束运行中的进程'

if ($ExeName.Count -eq 0) {
    $exeCandidates = @('CkanUI.exe', 'CkanUI.App.exe')
}
else {
    $exeCandidates = @($ExeName)
}
$processNames = @($exeCandidates | ForEach-Object { $_ -replace '\.exe$', '' } | Select-Object -Unique)

$running = @()
foreach ($name in $processNames) {
    foreach ($proc in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        $procPath = ''
        try { $procPath = $proc.Path } catch { $procPath = '' }
        $running += [pscustomobject]@{ Id = $proc.Id; Name = $proc.ProcessName; Path = $procPath }
    }
}

if ($running.Count -eq 0) {
    Write-Info '没有同名进程在运行。'
}
else {
    foreach ($item in $running) {
        $where = if ($item.Path) { $item.Path } else { '（路径不可读）' }
        Write-Info ('运行中：' + $item.Name + ' PID ' + $item.Id + ' ' + $where)
    }

    $shouldStop = [bool]$Force
    if (-not $shouldStop) {
        $answer = Read-Host '是否结束这些进程后继续卸载？(y/N)'
        if ($answer -match '^(y|yes|是|Y)$') { $shouldStop = $true }
    }

    if (-not $shouldStop) {
        Write-Host ''
        Write-Host '已取消：请先关闭 CKAN UI 再卸载。' -ForegroundColor Yellow
        exit 2
    }

    foreach ($item in $running) {
        try {
            Stop-Process -Id $item.Id -Force -ErrorAction Stop
            Write-Info ('已结束 PID ' + $item.Id)
        }
        catch {
            Write-Note ('结束 PID ' + $item.Id + ' 失败：' + $_.Exception.Message)
        }
    }
    Start-Sleep -Milliseconds 500
}

# ---- 3. 删除快捷方式 --------------------------------------------------------

Write-Step '删除快捷方式'

$shortcutPaths = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'CKAN UI.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'CKAN UI.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'CKAN UI\卸载 CKAN UI.lnk')
)
foreach ($link in $shortcutPaths) {
    if ($link -and (Test-Path -LiteralPath $link)) {
        Remove-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
        Write-Info ('已删除 ' + $link)
    }
}

# ---- 4. 删除 HKCU 注册项 ----------------------------------------------------

Write-Step '删除 HKCU 注册项（不碰 HKLM）'

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CkanUI'
if (-not (Test-Path -LiteralPath $uninstallKey)) {
    Write-Info 'HKCU 卸载项本来就不存在。'
}
else {
    # 只删「属于自己」的卸载项：InstallLocation 指向别处时留着，
    # 否则在 A 安装目录跑卸载脚本会把 B 安装目录的登记一起抹掉。
    $registeredLocation = ''
    try {
        $registeredLocation = [string](Get-ItemProperty -LiteralPath $uninstallKey -Name 'InstallLocation' -ErrorAction Stop).InstallLocation
    }
    catch {
        $registeredLocation = ''
    }
    if ((-not $registeredLocation) -or ($registeredLocation.TrimEnd('\') -ieq $installPath)) {
        Remove-Item -LiteralPath $uninstallKey -Recurse -Force
        Write-Info ('已删除 ' + $uninstallKey.Substring(5))
    }
    else {
        Write-Note ('保留 ' + $uninstallKey.Substring(5) + '（登记的是另一个安装位置：' + $registeredLocation + '）')
    }
}

$appPathsRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths'
foreach ($name in $exeCandidates) {
    $keyPath = Join-Path $appPathsRoot $name
    if (-not (Test-Path -LiteralPath $keyPath)) { continue }
    # 只删指向本安装目录的键；指向别处（另一个 CKAN UI 安装）的不动。
    $value = ''
    try { $value = (Get-Item -LiteralPath $keyPath).GetValue('') } catch { $value = '' }
    if ($value -and ($value -like ($installPath + '\*'))) {
        Remove-Item -LiteralPath $keyPath -Recurse -Force
        Write-Info ('已删除 App Paths\' + $name)
    }
    elseif (-not $value) {
        Remove-Item -LiteralPath $keyPath -Recurse -Force
        Write-Info ('已删除空的 App Paths\' + $name)
    }
    else {
        Write-Note ('保留 App Paths\' + $name + '（指向别处：' + $value + '）')
    }
}

# ---- 5. 删除安装目录 --------------------------------------------------------

Write-Step '删除安装目录'

if ($dirExists) {
    $attempt = 0
    $removed = $false
    while ($attempt -lt 3 -and -not $removed) {
        $attempt++
        try {
            Remove-Item -LiteralPath $installPath -Recurse -Force -ErrorAction Stop
            $removed = $true
        }
        catch {
            if ($attempt -lt 3) {
                Write-Note ('删除失败（第 ' + $attempt + ' 次），1 秒后重试：' + $_.Exception.Message)
                Start-Sleep -Seconds 1
            }
            else {
                Exit-WithError ('无法删除 ' + $installPath + '：' + $_.Exception.Message + '（可能有文件仍被占用）') 1
            }
        }
    }
    if (Test-Path -LiteralPath $installPath) {
        Exit-WithError ('删除后目录仍然存在：' + $installPath) 1
    }
    Write-Info ('已删除 ' + $installPath)
}
else {
    Write-Info '目录不存在，无需删除。'
}

# ---- 6. 用户数据 ------------------------------------------------------------

Write-Step '处理用户数据'

if ($RemoveUserData) {
    $KeepUserData = $false
}

$dataDir = ''
if ($env:LOCALAPPDATA) {
    $dataDir = Join-Path $env:LOCALAPPDATA 'CKAN'
}

if ($KeepUserData) {
    Write-Info '按默认（-KeepUserData）保留用户数据：'
}
elseif (-not $dataDir -or -not (Test-Path -LiteralPath $dataDir)) {
    Write-Info '没有找到 %LocalAppData%\CKAN，无需处理。'
}
else {
    # 只删本程序独占的文件；config.json / repos / CKAN.log / registry.json 一律不动。
    $owned = @('ckan-ui.json', 'ckan-ui.secrets', 'ckan-ui.json.bak')
    foreach ($name in $owned) {
        $path = Join-Path $dataDir $name
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            Write-Info ('已删除 ' + $path)
        }
    }
    foreach ($log in @(Get-ChildItem -LiteralPath $dataDir -Filter 'ckanui-*.log' -File -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $log.FullName -Force -ErrorAction SilentlyContinue
        Write-Info ('已删除 ' + $log.FullName)
    }
    Write-Info '保留（与 ckan.exe 共享）：config.json、repos\、CKAN.log'
}

if ($dataDir -and (Test-Path -LiteralPath $dataDir)) {
    Write-Info ('用户数据目录 ' + $dataDir + ' 保留。')
}

# ---- 7. 完成 ----------------------------------------------------------------

Write-Host ''
Write-Host '卸载完成。' -ForegroundColor Green
Write-Info '「设置 → 应用 → 已安装的应用」里的 CKAN UI 条目已移除。'
Write-Info 'ckan:// 协议与 .ckan 关联由应用运行期注册，随本程序目录删除后不再生效；'
Write-Info '如需彻底清掉协议键，可在应用外手工删除 HKCU\Software\Classes\ckan。'
exit 0
