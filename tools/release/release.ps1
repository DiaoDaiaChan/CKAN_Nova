#Requires -Version 5.1
<#
.SYNOPSIS
    CKAN UI 一条命令出包：Windows x64 双发布包 + 校验和 + 清单。

.DESCRIPTION
    从当前工作树直接产出两个"解压即用"的便携 zip：
      ckan-ui-<ver>-win-x64-portable-unsigned.zip             自包含（自带 .NET 运行时）
      ckan-ui-<ver>-win-x64-framework-dependent-unsigned.zip  框架依赖（需 .NET 10 Desktop Runtime）
    另产出 SHA256SUMS.txt 与 manifest.json（版本、提交、构建参数、逐包哈希、冒烟结果）。

    为什么两个包都要：06 篇 PKG-01 要求两种配置都能出包。内网 / 无外网机器用自包含版，
    能自己装运行时的用户用体积小一半的框架依赖版。两种都用 PublishReadyToRun=true：
    实测冷启动从 1.58 s 降到 0.81 s；不做 PublishTrimmed（Avalonia XAML 绑定与 CKAN Core
    的反射不是 trim-safe，裁了就不能保证"干净机器能跑"）。

.PARAMETER Version
    覆盖版本号。默认从 src/CkanUI.App/Version.props 的 CkanUiVersion 读取——
    那是版本号的唯一手改点，本脚本不重复定义版本。

.PARAMETER Runtime
    目标 RID，默认 win-x64（M6 只做这一档）。

.PARAMETER OutDir
    产物目录，默认 <仓库根>/artifacts/release（artifacts/ 已在 .gitignore 内）。

.PARAMETER RequireClean
    工作树脏就中止。默认只把 dirty 标记写进 manifest——并行开发期工作树常是脏的。

.PARAMETER NoReadyToRun
    不做 ReadyToRun 预编译。默认做（冷启动 1.58 s → 0.81 s），代价是体积：实测自包含版解压
    152 MB → 119 MB（-34 MB）、框架依赖版 73 MB → 41 MB。磁盘紧张或要与上游体积对齐时用它。

.PARAMETER PfxPath
    .pfx 代码签名证书路径。与本参数或 -Thumbprint 一起给时，脚本会在打包前调用
    tools/release/Sign-Artifacts.ps1 给 stage 里的主程序签名。本机没有证书，默认不签名，
    包名带 -unsigned；签名后请在发布页同时给出 Authenticode 指纹。

.PARAMETER PfxPassword
    .pfx 口令。只在内存里传，日志里一律打码。

.PARAMETER Thumbprint
    证书存储里的证书指纹。与 -PfxPath 同时给时以指纹为准。

.PARAMETER TimestampUrl
    RFC 3161 时间戳服务，默认 http://timestamp.digicert.com（证书过期后旧签名仍有效）。

.PARAMETER SignDryRun
    只打印 signtool 命令行、不真签，用于验证签名接线。产物仍是未签名包，包名不改。

.PARAMETER SmokeTest
    打包后把 zip 解压到临时目录、真启动一次、等主窗口出现再关掉，结果写进 manifest。
    没有桌面会话的 CI 环境不要开（会等到超时后判失败）。

.PARAMETER KeepWork
    保留中间目录 _work（publish 输出、发布日志、stage 内容）便于排查。

.EXAMPLE
    powershell -NoProfile -File tools/release/release.ps1

.EXAMPLE
    powershell -NoProfile -File tools/release/release.ps1 -SmokeTest -RequireClean
#>
[CmdletBinding()]
param(
    [string] $Version = '',
    [string] $Runtime = 'win-x64',
    [string] $OutDir = '',
    [switch] $RequireClean,
    [switch] $SmokeTest,
    [switch] $KeepWork,
    [switch] $NoReadyToRun,
    [string] $PfxPath = '',
    [string] $PfxPassword = '',
    [string] $Thumbprint = '',
    [string] $TimestampUrl = 'http://timestamp.digicert.com',
    [switch] $SignDryRun,
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# 控制台按 UTF-8 输出：脚本里的中文在 bash / CI 日志里才不会变成乱码。
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
# Windows PowerShell 5.1 默认不加载压缩程序集：ZipArchive 在 System.IO.Compression，
# ZipFile 在 System.IO.Compression.FileSystem，两个都要显式加载（pwsh 7 上重复加载也无害）。
try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

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

function Stop-WithError {
    param([string] $Message)
    Write-Host ''
    Write-Host ('失败：' + $Message) -ForegroundColor Red
    exit 1
}

# ---- 0. 路径与版本来源 -----------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$solutionPath = Join-Path $repoRoot 'ckan-ui.sln'
$appProject = Join-Path $repoRoot 'src\CkanUI.App\CkanUI.App.csproj'
$versionPropsPath = Join-Path $repoRoot 'src\CkanUI.App\Version.props'
$packagingDir = Join-Path $PSScriptRoot 'package'
$smokeScript = Join-Path $PSScriptRoot 'Test-PortableLaunch.ps1'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    Stop-WithError ('找不到仓库根（期望 ' + $solutionPath + '）。本脚本必须放在 <仓库根>/tools/release/ 下。')
}

Write-Step '解析版本与源码信息'

if (-not $Version) {
    if (-not (Test-Path -LiteralPath $versionPropsPath)) {
        Stop-WithError ('找不到版本来源文件 ' + $versionPropsPath)
    }
    $propsText = Get-Content -LiteralPath $versionPropsPath -Raw
    $versionMatch = [regex]::Match($propsText, '<CkanUiVersion[^>]*>\s*([^<]+?)\s*</CkanUiVersion>')
    if (-not $versionMatch.Success) {
        Stop-WithError ('Version.props 里没有 <CkanUiVersion> 属性：' + $versionPropsPath)
    }
    $Version = $versionMatch.Groups[1].Value
}
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.\-]+)?$') {
    Stop-WithError ('版本号必须是 x.y.z 或 x.y.z-后缀 的形式，实际为：' + $Version)
}
Write-Info ('版本 ' + $Version + '（来源 src/CkanUI.App/Version.props）')

$commitCount = 0
$shortSha = 'nogit'
$dirty = $false
if (Get-Command git -ErrorAction SilentlyContinue) {
    Push-Location -LiteralPath $repoRoot
    try {
        $countText = (& git rev-list --count HEAD 2>$null) | Select-Object -First 1
        if ($countText -and $countText.Trim() -match '^[0-9]+$') { $commitCount = [int]$countText.Trim() }
        $shaText = (& git rev-parse --short HEAD 2>$null) | Select-Object -First 1
        if ($shaText) { $shortSha = $shaText.Trim() }
        $statusText = & git status --porcelain 2>$null
        if ($statusText) { $dirty = $true }
    } finally {
        Pop-Location
    }
} else {
    Write-Info '未找到 git：提交数与哈希记为 unknown。'
}
Write-Info ('提交 ' + $shortSha + '，次数 ' + $commitCount + '，工作树' + $(if ($dirty) { '脏（dirty，会记入 manifest）' } else { '干净' }))

if ($dirty) {
    if ($RequireClean) {
        Stop-WithError '工作树不干净，而指定了 -RequireClean。请先提交或 stash 后再出包。'
    }
    Write-Host '    提示：工作树不干净，出包仍继续；manifest.json 里 source.dirty=true 可追溯。' -ForegroundColor Yellow
}

# ---- 1. 环境自检 -----------------------------------------------------------

Write-Step '环境自检'

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCommand) {
    Stop-WithError '找不到 dotnet。请按 docs/ops/build-environment.md 安装 .NET 10 SDK。'
}
$sdkVersion = (& dotnet --version) | Select-Object -First 1
Write-Info ('dotnet ' + $sdkVersion + ' / ' + [System.Environment]::OSVersion.VersionString)

$coreProject = Join-Path $repoRoot 'external\CKAN\Core\CKAN-core.csproj'
if (-not (Test-Path -LiteralPath $coreProject)) {
    Stop-WithError ('找不到 CKAN Core：' + $coreProject + '。请先按 docs/ops/build-environment.md 准备 external/CKAN。')
}
Write-Info 'CKAN Core 存在'

# ---- 2. 产物目录与中间目录 -------------------------------------------------

if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'artifacts\release' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path -LiteralPath $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}
Write-Step ('输出目录 ' + $OutDir)

$workDir = Join-Path $OutDir '_work'
if ((Split-Path -Leaf $workDir) -ne '_work') {
    Stop-WithError ('中间目录名必须是 _work，实际为：' + $workDir)
}
if (Test-Path -LiteralPath $workDir) {
    if ($KeepWork) {
        Write-Info '保留上次的 _work 目录（-KeepWork）'
    } else {
        # 只清理自己产出的固定名字的中间目录；绝不碰 bin/ obj/ 与 external/_build。
        Remove-Item -LiteralPath $workDir -Recurse -Force
    }
}
if (-not (Test-Path -LiteralPath $workDir)) {
    New-Item -ItemType Directory -Path $workDir -Force | Out-Null
}

# ---- 3. 发布函数 -----------------------------------------------------------

function Invoke-Publish {
    param(
        [Parameter(Mandatory = $true)] [string] $Flavor,
        [Parameter(Mandatory = $true)] [bool] $SelfContained
    )

    $publishDir = Join-Path $workDir ('publish-' + $Flavor)
    $logPath = Join-Path $workDir ('publish-' + $Flavor + '.log')
    $selfContainedFlag = 'false'
    if ($SelfContained) { $selfContainedFlag = 'true' }

    $dotnetArgs = @(
        'publish', $appProject,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $selfContainedFlag,
        '-o', $publishDir,
        ('-p:Version=' + $Version),
        ('-p:CkanUiVersion=' + $Version),
        ('-p:CkanUiCommitCount=' + $commitCount),
        ('-p:CkanUiCommitSha=' + $shortSha),
        ('-p:PublishReadyToRun=' + $(if ($NoReadyToRun) { 'false' } else { 'true' })),
        '-p:PublishTrimmed=false',
        '-p:PublishSingleFile=false',
        '--nologo',
        '-v', 'm',
        '/nodeReuse:false'
    )
    Write-Info ('dotnet ' + ($dotnetArgs -join ' '))

    $output = & dotnet @dotnetArgs 2>&1
    $exitCode = $LASTEXITCODE
    $output | Set-Content -LiteralPath $logPath -Encoding UTF8

    $warnings = @($output | Where-Object { $_ -match ': warning ' })
    foreach ($warningLine in $warnings) {
        Write-Host ('    [warn] ' + $warningLine) -ForegroundColor Yellow
    }
    Write-Info ('发布输出 ' + @($output).Count + ' 行，警告 ' + $warnings.Count + ' 条，日志 ' + $logPath)

    if ($exitCode -ne 0) {
        Write-Host '    ---- 发布日志尾部 ----' -ForegroundColor Yellow
        $output | Select-Object -Last 30 | ForEach-Object { Write-Host ('    ' + $_) }
        Stop-WithError ('dotnet publish 失败（' + $Flavor + '，退出码 ' + $exitCode + '）。若出现 NU1301 说明 NuGet 源不可用，见 docs/ops/build-environment.md；不要改 NuGet 配置或包版本。')
    }

    $exePath = Join-Path $publishDir 'CkanUI.exe'
    if (-not (Test-Path -LiteralPath $exePath)) {
        Stop-WithError ('发布输出里没有 CkanUI.exe：' + $publishDir)
    }
    return $publishDir
}

# ---- 4. 组装 stage 目录 ----------------------------------------------------

function New-Stage {
    param(
        [Parameter(Mandatory = $true)] [string] $Flavor,
        [Parameter(Mandatory = $true)] [string] $PublishDir,
        [Parameter(Mandatory = $true)] [bool] $SelfContained
    )

    $stageDir = Join-Path $workDir ('stage-' + $Flavor)
    if (Test-Path -LiteralPath $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

    $payloadFiles = 0
    $payloadBytes = [long] 0
    foreach ($file in Get-ChildItem -LiteralPath $PublishDir -Recurse -File -Force) {
        # 调试符号不进发布包：实测 14 个 pdb 共 101 MB（libSkiaSharp.pdb 单个 84 MB）。
        if ($file.Extension -ieq '.pdb') { continue }
        $relative = $file.FullName.Substring($PublishDir.Length).TrimStart('\', '/')
        $target = Join-Path $stageDir $relative
        $targetParent = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetParent)) {
            New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        $payloadFiles++
        $payloadBytes += $file.Length
    }

    $flavorTitle = '自包含版（self-contained）'
    $runtimeNote = '本包自带 .NET 运行时，不需要另外安装 .NET，解压后直接双击运行。'
    if (-not $SelfContained) {
        $flavorTitle = '框架依赖版（framework-dependent）'
        $runtimeNote = '本包不自带运行时：请先安装 .NET 10 Desktop Runtime（x64），否则启动会提示缺少运行库。'
    }
    $sizeNote = '解压后约 {0} MB（{1} 个文件）' -f [int]($payloadBytes / 1MB), $payloadFiles
    Write-Info ($flavorTitle + '：' + $payloadFiles + ' 个文件 / ' + [int]($payloadBytes / 1MB) + ' MB（已排除 pdb）')

    $readmeTemplate = Get-Content -LiteralPath (Join-Path $packagingDir 'README-portable.template.txt') -Raw
    $readmeText = $readmeTemplate.Replace('{{VERSION}}', $Version).Replace('{{FLAVOR_NAME}}', $flavorTitle).Replace('{{RUNTIME_NOTE}}', $runtimeNote).Replace('{{SIZE_NOTE}}', $sizeNote)
    # 不用 Set-Content -Encoding UTF8：Windows PowerShell 5.1 会给文件加 BOM，
    # 解压后 README.txt 首行带 U+FEFF，记事本看不出但 bash 里看很别扭。
    [System.IO.File]::WriteAllText((Join-Path $stageDir 'README.txt'), $readmeText, [System.Text.UTF8Encoding]::new($false))

    $noticesTemplate = Get-Content -LiteralPath (Join-Path $packagingDir 'THIRD-PARTY-NOTICES.template.txt') -Raw
    $noticesText = $noticesTemplate.Replace('{{VERSION}}', $Version).Replace('{{FLAVOR_NAME}}', $flavorTitle).Replace('{{RUNTIME_NOTE}}', $runtimeNote)
    [System.IO.File]::WriteAllText((Join-Path $stageDir 'THIRD-PARTY-NOTICES.txt'), $noticesText, [System.Text.UTF8Encoding]::new($false))

    $licenseDir = Join-Path $stageDir 'licenses'
    New-Item -ItemType Directory -Path $licenseDir -Force | Out-Null
    # 本项目自身的许可证（GPL-3.0 全文）随包分发：用户在解压目录里就能读到，不必回仓库找。
    $ownLicense = Join-Path $repoRoot 'LICENSE.md'
    if (Test-Path -LiteralPath $ownLicense) {
        Copy-Item -LiteralPath $ownLicense -Destination (Join-Path $stageDir 'LICENSE.md') -Force
    } else {
        Stop-WithError '缺少仓库根的 LICENSE.md：发布包必须带上项目自身的许可证原文。'
    }
    $ckanLicense = Join-Path $repoRoot 'external\CKAN\LICENSE.md'
    if (Test-Path -LiteralPath $ckanLicense) {
        Copy-Item -LiteralPath $ckanLicense -Destination (Join-Path $licenseDir 'CKAN-LICENSE.md') -Force
    } else {
        Write-Host '    警告：external/CKAN/LICENSE.md 不存在，包里将缺少 CKAN 许可证原文。' -ForegroundColor Yellow
    }

    # 便携包的"安装器"：解压后跑 install\install.ps1 即可 per-user 安装（写 HKCU、建快捷方式、登记卸载），
    # 不需要管理员，也不需要 Inno Setup。脚本随包分发，用户不用回仓库找。
    $installScriptDir = Join-Path $stageDir 'install'
    New-Item -ItemType Directory -Path $installScriptDir -Force | Out-Null
    foreach ($scriptName in @('install.ps1', 'uninstall.ps1')) {
        $scriptSource = Join-Path (Join-Path $PSScriptRoot 'install') $scriptName
        if (-not (Test-Path -LiteralPath $scriptSource)) {
            Stop-WithError ('缺少安装脚本 tools/release/install/' + $scriptName)
        }
        Copy-Item -LiteralPath $scriptSource -Destination (Join-Path $installScriptDir $scriptName) -Force
    }
    $installReadme = @(
        'CKAN UI 安装脚本（per-user，不需要管理员权限）'
        ''
        '用法（在解压出来的这个目录里打开 PowerShell）：'
        '    powershell -ExecutionPolicy Bypass -File .\install\install.ps1 -SourceDir .'
        ''
        '装到 %LocalAppData%\CKAN UI，建开始菜单与桌面快捷方式，并在 HKCU 登记卸载项。'
        '卸载：powershell -ExecutionPolicy Bypass -File .\install\uninstall.ps1'
        '（卸载默认保留 %LocalAppData%\CKAN 里的设置、日志与缓存）'
        ''
        '不想装也可以直接双击同目录的 CkanUI.exe：这就是便携版。'
        '详细说明见仓库 docs/ops/install.md。'
    ) -join [Environment]::NewLine
    [System.IO.File]::WriteAllText((Join-Path $installScriptDir 'README.txt'), $installReadme + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

    $finalMeasure = Get-ChildItem -LiteralPath $stageDir -Recurse -File -Force | Measure-Object -Property Length -Sum

    $finalMeasure = Get-ChildItem -LiteralPath $stageDir -Recurse -File -Force | Measure-Object -Property Length -Sum
    return [pscustomobject]@{
        Directory = $stageDir
        FileCount = [int] $finalMeasure.Count
        UncompressedBytes = [long] $finalMeasure.Sum
    }
}

# ---- 5. 确定性 zip ---------------------------------------------------------

function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)] [string] $SourceDir,
        [Parameter(Mandatory = $true)] [string] $ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }
    # 时间戳统一成 zip 纪元（1980-01-01）：同样的文件内容得到同样的 sha256，
    # 这样"包有没有变"可以只看哈希，不用逐字节比对。
    $fixedTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $files = Get-ChildItem -LiteralPath $SourceDir -Recurse -File -Force | Sort-Object FullName

    $stream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $files) {
                $relative = $file.FullName.Substring($SourceDir.Length).TrimStart('\', '/')
                # zip 规范要求 '/' 分隔；写成反斜杠会让 Linux unzip / 7-Zip 平铺出一堆怪文件名。
                $entryName = $relative.Replace('\', '/')
                $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
                try { $entry.LastWriteTime = $fixedTime } catch { }
                $entryStream = $entry.Open()
                try {
                    $inputStream = [System.IO.File]::OpenRead($file.FullName)
                    try { $inputStream.CopyTo($entryStream) } finally { $inputStream.Dispose() }
                } finally {
                    $entryStream.Dispose()
                }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
    return $files.Count
}

# ---- 6. zip 内容自检 -------------------------------------------------------

function Test-ZipContent {
    param(
        [Parameter(Mandatory = $true)] [string] $ZipPath,
        [Parameter(Mandatory = $true)] [string[]] $RequiredEntries
    )

    $names = @{}
    $pdbCount = 0
    $entryCount = 0
    $stream = [System.IO.File]::OpenRead($ZipPath)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            foreach ($entry in $archive.Entries) {
                $names[$entry.FullName.ToLowerInvariant()] = $true
                $entryCount++
                if ($entry.FullName -like '*.pdb') { $pdbCount++ }
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $stream.Dispose()
    }

    $missing = @()
    foreach ($required in $RequiredEntries) {
        $key = $required.Replace('\', '/').ToLowerInvariant()
        if (-not $names.ContainsKey($key)) { $missing += $required }
    }
    return [pscustomobject]@{
        EntryCount = $entryCount
        PdbCount = $pdbCount
        Missing = $missing
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---- 7. 主流程 -------------------------------------------------------------

$requiredEntries = @(
    'CkanUI.exe',
    'CKAN.dll',
    'log4net.xml',
    'LICENSE.md',
    'README.txt',
    'THIRD-PARTY-NOTICES.txt',
    'licenses/CKAN-LICENSE.md',
    'install/install.ps1',
    'install/uninstall.ps1',
    'install/README.txt',
    'i18n/packs/en-US.json',
    'i18n/packs/zh-CN.json',
    'Assets/ckan.ico'
)

$flavors = @(
    [pscustomobject]@{ Name = 'portable'; SelfContained = $true; Suffix = 'portable'; Description = '自包含（自带运行时）' },
    [pscustomobject]@{ Name = 'framework-dependent'; SelfContained = $false; Suffix = 'framework-dependent'; Description = '框架依赖（需 .NET 10 Desktop Runtime）' }
)

# 主程序名与签名状态：脚本里只此一处写死 exe 名（与 CkanUI.App.csproj 的 AssemblyName 一致）。
$exeName = 'CkanUI.exe'
$signRequested = ($PfxPath -ne '') -or ($Thumbprint -ne '') -or $SignDryRun
$signedApplied = (($PfxPath -ne '') -or ($Thumbprint -ne '')) -and (-not $SignDryRun)

$artifacts = @()
$smokeResult = [ordered]@{
    performed = $false
    result = 'not-run'
    reason = '未指定 -SmokeTest'
}

foreach ($flavor in $flavors) {
    Write-Step ('发布 ' + $flavor.Name + '（' + $flavor.Description + '）')
    $publishDir = Invoke-Publish -Flavor $flavor.Name -SelfContained $flavor.SelfContained

    Write-Step ('组装 ' + $flavor.Name + ' 便携目录')
    $stage = New-Stage -Flavor $flavor.Name -PublishDir $publishDir -SelfContained $flavor.SelfContained

    if ($signRequested) {
        Write-Step ('签名 ' + $flavor.Name + ' 的主程序')
        $signScript = Join-Path $PSScriptRoot 'Sign-Artifacts.ps1'
        if (-not (Test-Path -LiteralPath $signScript)) {
            Stop-WithError ('找不到签名脚本 tools/release/Sign-Artifacts.ps1，去掉签名参数或补上该脚本。')
        }
        $stageExe = Join-Path $stage.Directory ($exeName)
        if (-not (Test-Path -LiteralPath $stageExe)) {
            Stop-WithError ('stage 里没有主程序 ' + $exeName + '：' + $stage.Directory)
        }
        $signArgs = @('-Path', $stageExe)
        if ($Thumbprint -ne '') { $signArgs += @('-Thumbprint', $Thumbprint) }
        if ($PfxPath -ne '') { $signArgs += @('-PfxPath', $PfxPath); $signArgs += @('-PfxPassword', $PfxPassword) }
        if ($TimestampUrl -ne '') { $signArgs += @('-TimestampUrl', $TimestampUrl) }
        if ($SignDryRun) { $signArgs += '-DryRun' }
        $signHost = (Get-Process -Id $PID).Path
        if (-not $signHost) { $signHost = 'powershell.exe' }
        $signOutput = & $signHost -NoProfile -ExecutionPolicy Bypass -File $signScript @signArgs 2>&1
        $signExit = $LASTEXITCODE
        $signOutput | ForEach-Object { Write-Host ('    ' + $_) }
        if ($signExit -ne 0) {
            Stop-WithError ('签名失败（' + $flavor.Name + '，退出码 ' + $signExit + '）。没有证书就不要传签名参数；只想验证接线用 -SignDryRun。')
        }
    }

    Write-Step ('打包 ' + $flavor.Name + ' zip')
    $zipName = 'ckan-ui-' + $Version + '-' + $Runtime + '-' + $flavor.Suffix + '-unsigned.zip'
    $zipPath = Join-Path $OutDir $zipName
    $zipFileCount = New-DeterministicZip -SourceDir $stage.Directory -ZipPath $zipPath
    $verify = Test-ZipContent -ZipPath $zipPath -RequiredEntries $requiredEntries
    if ($verify.Missing.Count -gt 0) {
        Stop-WithError ('zip 缺少必需条目：' + ($verify.Missing -join ', '))
    }
    if ($verify.PdbCount -gt 0) {
        Stop-WithError ('zip 里混进了 ' + $verify.PdbCount + ' 个 pdb，检查排除规则。')
    }
    $zipItem = Get-Item -LiteralPath $zipPath
    Write-Info ($zipName + '：' + [int]($zipItem.Length / 1MB) + ' MB（' + $verify.EntryCount + ' 个条目），必需条目齐全、无 pdb')

    $artifacts += [ordered]@{
        file = $zipName
        selfContained = $flavor.SelfContained
        sizeBytes = [long] $zipItem.Length
        sha256 = (Get-Sha256 -Path $zipPath)
        fileCount = $zipFileCount
        uncompressedBytes = $stage.UncompressedBytes
    }
}

# ---- 8. 可选冒烟 -----------------------------------------------------------

if ($SmokeTest) {
    Write-Step '冒烟：解压自包含 zip 并真启动到主窗口'
    if (-not (Test-Path -LiteralPath $smokeScript)) {
        Stop-WithError ('找不到冒烟脚本 ' + $smokeScript)
    }
    $smokeZip = Join-Path $OutDir $artifacts[0].file
    $smokeLogPath = Join-Path $workDir 'smoke-portable.log'
    $hostExe = (Get-Process -Id $PID).Path
    if (-not $hostExe) { $hostExe = 'powershell.exe' }
    $smokeOutput = & $hostExe -NoProfile -ExecutionPolicy Bypass -File $smokeScript -ZipPath $smokeZip -TimeoutSeconds 90 2>&1
    $smokeExit = $LASTEXITCODE
    $smokeOutput | Set-Content -LiteralPath $smokeLogPath -Encoding UTF8
    $smokeOutput | ForEach-Object { Write-Host ('    ' + $_) }

    if ($smokeExit -eq 0) {
        $smokeResult = [ordered]@{ performed = $true; result = 'main-window-shown'; reason = '自包含 zip 解压后启动到主窗口'; log = 'artifacts/release/_work/smoke-portable.log' }
    } elseif ($smokeExit -eq 2) {
        $smokeResult = [ordered]@{ performed = $false; result = 'skipped'; reason = '已有 CKAN UI 实例在运行（单实例守卫会转发退出），冒烟未执行'; log = 'artifacts/release/_work/smoke-portable.log' }
        Write-Host '    冒烟跳过：请关掉正在运行的 CKAN UI 后重跑。' -ForegroundColor Yellow
    } else {
        $smokeResult = [ordered]@{ performed = $true; result = 'failed'; reason = ('冒烟退出码 ' + $smokeExit); log = 'artifacts/release/_work/smoke-portable.log' }
        Stop-WithError ('冒烟失败，见 ' + $smokeLogPath)
    }
}

# ---- 9. 校验和与清单 -------------------------------------------------------

Write-Step '写 SHA256SUMS.txt 与 manifest.json'

$sumsLines = @()
foreach ($artifact in $artifacts) {
    $sumsLines += ($artifact.sha256 + '  ' + $artifact.file)
}
$sumsPath = Join-Path $OutDir 'SHA256SUMS.txt'
# 用 LF + 无 BOM 写：GNU 的 sha256sum -c 会把行尾的 \r 当成文件名的一部分而报「找不到文件」，
# 而 Windows PowerShell 的 Set-Content 固定写 CRLF，所以这里自己拼字符串落盘。
[System.IO.File]::WriteAllText($sumsPath, (($sumsLines -join "`n") + "`n"), [System.Text.Encoding]::ASCII)

$manifest = [ordered]@{
    schemaVersion = 1
    product = 'CKAN UI'
    version = $Version
    runtime = $Runtime
    configuration = $Configuration
    signed = $signedApplied
    builtAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    versionSource = 'src/CkanUI.App/Version.props'
    source = [ordered]@{
        commitCount = $commitCount
        commitSha = $shortSha
        dirty = $dirty
    }
    toolchain = [ordered]@{
        dotnetSdk = $sdkVersion
        os = [System.Environment]::OSVersion.VersionString
        powershell = $PSVersionTable.PSVersion.ToString()
    }
    publishOptions = [ordered]@{
        publishReadyToRun = (-not $NoReadyToRun)
        publishTrimmed = $false
        publishSingleFile = $false
        excludedPatterns = @('*.pdb')
    }
    artifacts = $artifacts
    signing = [ordered]@{
        requested = $signRequested
        applied = $signedApplied
        method = $(if ($Thumbprint -ne '') { 'thumbprint' } elseif ($PfxPath -ne '') { 'pfx' } else { 'none' })
        timestampUrl = $TimestampUrl
    }
    smokeTest = $smokeResult
}

$manifestPath = Join-Path $OutDir 'manifest.json'
$manifestJson = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $manifestJson + [System.Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

# ---- 10. 汇总 --------------------------------------------------------------

Write-Step '完成'
foreach ($artifact in $artifacts) {
    $sizeMb = [math]::Round($artifact.sizeBytes / 1MB, 1)
    Write-Host ('    ' + $artifact.file)
    Write-Host ('      大小 ' + $sizeMb + ' MB，解压后 ' + [int]($artifact.uncompressedBytes / 1MB) + ' MB / ' + $artifact.fileCount + ' 个文件')
    Write-Host ('      sha256 ' + $artifact.sha256)
}
Write-Host ''
Write-Host ('    产物目录 ' + $OutDir)
Write-Host ('    未签名：发布页需同时给出 SHA256SUMS.txt 的值。中间目录可用 -KeepWork 保留，否则下次运行会清掉。')

exit 0
