#Requires -Version 5.1
<#
.SYNOPSIS
    一键出安装包：双击即可产出 CkanUISetup.exe（含便携 zip 与校验和）。

.DESCRIPTION
    这是 tools/release/release.ps1 的"一键"包装壳，只加三件事，不重复打包逻辑：
      1) 开工前把前置条件验干净（.NET SDK 主版本、Native AOT 需要的 MSVC 链接器、仓库完整性），
         缺什么直接说清怎么补，而不是让 dotnet publish 在十几步之后炸出来；
      2) 用子进程调 release.ps1，参数原样透传，日志实时刷屏；
      3) 收工后自检产物：安装器用 /? 真跑一次（证明这台的 AOT 单文件能启动）、
         逐个打印体积与 sha256，可选直接拉起安装向导。

    默认产出（artifacts/release/）：
        ckan-ui-<ver>-win-x64-setup-unsigned.exe          单文件原生安装向导（内嵌便携包）
        ckan-ui-<ver>-win-x64-portable-unsigned.zip       解压即用，自带运行时
        ckan-ui-<ver>-win-x64-framework-dependent-*.zip   需 .NET 10 Desktop Runtime
        SHA256SUMS.txt / manifest.json                    校验和与构建清单

.PARAMETER ZipOnly
    只出 zip，不编译 Native AOT 安装器。没有 VS C++ 工具链时用这个。

.PARAMETER Clean
    开工前清空 artifacts/release（只动产物目录，不碰 bin/obj 缓存）。

.PARAMETER RunSetup
    构建成功后直接启动安装向导，人工过一遍真实安装体验。

.PARAMETER OpenFolder
    构建成功后打开产物目录。

.PARAMETER Extra
    其余参数原样转给 release.ps1，例如 -SmokeTest、-KeepWork、-NoReadyToRun、
    -Version 0.2.0、-PfxPath x.pfx -PfxPassword *** 、-Thumbprint <指纹>。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Build-Setup.ps1

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Build-Setup.ps1 -Clean -RunSetup

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/Build-Setup.ps1 -ZipOnly -Version 0.2.0
#>
[CmdletBinding()]
param(
    [switch] $ZipOnly,
    [switch] $Clean,
    [switch] $RunSetup,
    [switch] $OpenFolder,
    [Parameter(ValueFromRemainingArguments = $true)] [string[]] $Extra = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Failures = @()

function Write-Head {
    param([string] $Message)
    Write-Host ''
    Write-Host ('== ' + $Message) -ForegroundColor Cyan
}

function Write-Ok {
    param([string] $Message)
    Write-Host ('   [OK]   ' + $Message) -ForegroundColor Green
}

function Write-Bad {
    param([string] $Message, [string] $Fix = '')
    Write-Host ('   [缺]   ' + $Message) -ForegroundColor Red
    if ($Fix -ne '') { Write-Host ('          补法：' + $Fix) -ForegroundColor Yellow }
}

function Write-Note {
    param([string] $Message)
    Write-Host ('   ' + $Message) -ForegroundColor DarkGray
}

$startedAt = Get-Date
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseScript = Join-Path $PSScriptRoot 'release.ps1'
$outDir = Join-Path $repoRoot 'artifacts\release'

try { $mv = [System.Environment]::OSVersion.Version } catch { $mv = $null }

# ---- 1. 前置条件 -----------------------------------------------------------

Write-Head '前置检查'

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'ckan-ui.sln'))) {
    Write-Bad ('当前目录不像仓库根：' + $repoRoot)
    exit 1
}
if (-not (Test-Path -LiteralPath $releaseScript)) {
    Write-Bad ('找不到打包脚本 ' + $releaseScript)
    exit 1
}
Write-Ok ('仓库根 ' + $repoRoot)

$dotnetExe = (Get-Command dotnet -ErrorAction SilentlyContinue)
if (-not $dotnetExe) {
    Write-Bad 'PATH 里没有 dotnet。' '装 .NET 10 SDK：winget install Microsoft.DotNet.SDK.10'
    exit 1
}
$sdkVersion = (& dotnet --version) 2>$null
if ($LASTEXITCODE -ne 0 -or -not $sdkVersion) {
    Write-Bad 'dotnet --version 跑不通。' '重装 .NET 10 SDK 或检查 global.json 的 SDK 版本是否存在'
    exit 1
}
$sdkMajor = 0
try { $sdkMajor = [int] ($sdkVersion.Trim().Split('.')[0]) } catch { $sdkMajor = 0 }
if ($sdkMajor -lt 10) {
    Write-Bad ('SDK 版本 ' + $sdkVersion + ' 太旧，本项目要 10.x。') 'winget install Microsoft.DotNet.SDK.10'
    exit 1
}
Write-Ok ('dotnet SDK ' + $sdkVersion)

if (-not $ZipOnly) {
    # Native AOT 最终链接要 MSVC 的 link.exe，没有它 publish 会在最后一公里失败。
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msvcRoot = $null
    if (Test-Path -LiteralPath $vswhere) {
        $msvcRoot = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath) 2>$null
    }
    if ($msvcRoot) {
        Write-Ok ('MSVC 工具链 ' + $msvcRoot)
    } else {
        Write-Bad '没找到 VS 的 C++ 生成工具（Native AOT 必需）。' '装 VS Build Tools 勾选「使用 C++ 的桌面开发」：winget install Microsoft.VisualStudio.2022.BuildTools'
        Write-Note '只想拿 zip 不要安装器：加 -ZipOnly 重跑。'
        exit 1
    }
} else {
    Write-Note '跳过 MSVC 检查（-ZipOnly：不出 Native AOT 安装器）'
}

# 7z 只影响压缩率，缺了会自动退回 .NET 自带 ZipArchive。
$has7z = $true
if (-not (Get-Command 7z -ErrorAction SilentlyContinue) -and -not (Test-Path 'C:\Program Files\7-Zip\7z.exe')) {
    $has7z = $false
    Write-Note '没有 7z：zip 用 .NET 默认压缩，体积会大几个 MB（可选装：winget install 7zip.7zip）'
} else {
    Write-Ok '7-Zip 可用（zip 走 -mx=9 极限压缩）'
}

# ---- 2. 清产物 -------------------------------------------------------------

if ($Clean -and (Test-Path -LiteralPath $outDir)) {
    Write-Head ('清理 ' + $outDir)
    Get-ChildItem -LiteralPath $outDir -Force | Remove-Item -Recurse -Force
    Write-Ok '已清空'
}

# ---- 3. 调 release.ps1（子进程，参数透传） ---------------------------------

Write-Head '开始构建（release.ps1）'
$releaseArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $releaseScript)
if ($ZipOnly) { $releaseArgs += '-NoInstaller' }
foreach ($item in $Extra) { $releaseArgs += $item }
Write-Note ('powershell ' + ($releaseArgs -join ' '))
Write-Host ''

$hostExe = (Get-Process -Id $PID).Path
if (-not $hostExe) { $hostExe = 'powershell.exe' }
& $hostExe @releaseArgs
$releaseExit = $LASTEXITCODE

if ($releaseExit -ne 0) {
    Write-Host ''
    Write-Host ('构建失败，release.ps1 退出码 ' + $releaseExit) -ForegroundColor Red
    Write-Note '中间产物在 artifacts/release/_work（加 -KeepWork 可保留更多日志）'
    exit $releaseExit
}

# ---- 4. 产物自检 -----------------------------------------------------------

Write-Head '产物自检'
# -ZipOnly 时目录里可能躺着上一次的安装器：那不是本轮的产物，既不进表也不做自检。
$setupExe = $null
if ($ZipOnly) {
    $stale = Get-ChildItem -LiteralPath $outDir -Filter '*-setup*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($stale) { Write-Note ('本轮 -ZipOnly 未重编安装器；目录里的 ' + $stale.Name + ' 是旧产物，未做自检。') }
} else {
    $setupExe = Get-ChildItem -LiteralPath $outDir -Filter '*-setup*.exe' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $setupExe) {
        Write-Bad '没找到 setup exe：release.ps1 说自己成功了，但产物不见踪影。'
        $script:Failures += 'setup-missing'
    }
}
$zips = Get-ChildItem -LiteralPath $outDir -Filter '*.zip' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
$sumsPath = Join-Path $outDir 'SHA256SUMS.txt'
$manifestPath = Join-Path $outDir 'manifest.json'

if (-not $zips -and -not $setupExe -and -not $ZipOnly) {
    Write-Bad '产物目录里既没有 zip 也没有安装器。'
    exit 1
}

if ($setupExe) {
    # 【自检】用 /? 真跑一次：能返回 0 就说明这台机器上 AOT 单文件能起来（缺依赖会直接崩）。
    $probe = & $setupExe.FullName '/?' 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Ok ('安装器可执行自检通过（/? 返回 0）：' + $setupExe.Name)
    } else {
        Write-Bad ('安装器 /? 自检退出码 ' + $LASTEXITCODE + '，单文件可能缺依赖：' + $setupExe.Name)
        $script:Failures += 'setup-probe'
    }
}

if (Test-Path -LiteralPath $sumsPath) {
    Write-Ok 'SHA256SUMS.txt 已生成'
} else {
    Write-Bad '缺 SHA256SUMS.txt'
    $script:Failures += 'sums'
}
if (Test-Path -LiteralPath $manifestPath) {
    Write-Ok 'manifest.json 已生成'
} else {
    Write-Bad '缺 manifest.json'
    $script:Failures += 'manifest'
}

# 体积表直接从磁盘取，避免和 manifest 里的口径打架。
$rows = @()
foreach ($f in (@($setupExe) + @($zips) | Where-Object { $_ })) {
    $rows += [pscustomobject]@{
        产物 = $f.Name
        MB   = [math]::Round($f.Length / 1MB, 2)
        修改 = $f.LastWriteTime.ToString('HH:mm:ss')
    }
}
if ($rows.Count -gt 0) {
    Write-Host ''
    $rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
}

$elapsed = [int] ((Get-Date) - $startedAt).TotalSeconds

# ---- 5. 收尾 ---------------------------------------------------------------

Write-Head '完成'
Write-Note ('产物目录 ' + $outDir)
Write-Note ('总耗时 ' + [int]($elapsed / 60) + ' 分 ' + ($elapsed % 60) + ' 秒')
if (-not $has7z) { Write-Note '提示：装 7-Zip 后重跑，zip 还能再小一点。' }

if ($script:Failures.Count -gt 0) {
    Write-Host ''
    Write-Host ('自检未全过：' + ($script:Failures -join ', ')) -ForegroundColor Red
    exit 1
}

if ($OpenFolder) {
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + $outDir + '"')
}

if ($RunSetup -and $setupExe) {
    Write-Note ('启动安装向导：' + $setupExe.Name)
    Start-Process -FilePath $setupExe.FullName
}

Write-Host ''
Write-Host '出包成功。' -ForegroundColor Green
exit 0
