#Requires -Version 5.1
<#
.SYNOPSIS
    用 signtool 给发布产物做 Authenticode 签名（SHA256 + RFC 3161 时间戳）。

.DESCRIPTION
    两种证书来源：
      -Thumbprint   证书在证书存储里（推荐：CI 上由 SignPath / 云签名 / 机器证书提供）
      -PfxPath      .pfx 文件 + -PfxPassword（本地自签测试证书走这条）

    本机（开发机）没有代码签名证书，也没有 Windows SDK 的 signtool，所以本脚本的
    核心交付是 -DryRun：只打印将要执行的完整 signtool 命令行，退出码 0，不碰任何文件。

    signtool 查找顺序：PATH → %ProgramFiles(x86)%\Windows Kits\10\bin\<ver>\x64\signtool.exe
    → %ProgramFiles%\Windows Kits\10\bin\<ver>\signtool.exe（取版本号最大的一个）。

    找不到 signtool 时：真实签名打印可操作说明并以非零退出；-DryRun 仍以 0 退出
    （预览命令行不需要 signtool 存在）。

.PARAMETER Path
    要签名的文件，必填，可多个。文件不存在直接失败，不跳过
    （发布流水线里静默跳过比失败更危险）。

.PARAMETER Thumbprint
    证书指纹（不带空格，40 位十六进制）。与 -PfxPath 二选一；同时给以指纹为准。

.PARAMETER PfxPath
    .pfx 文件路径。tools/release/New-DevCertificate.ps1 生成的自签证书就在这里。

.PARAMETER PfxPassword
    .pfx 的口令。日志里一律打码；只影响打印，不影响实际调用。

.PARAMETER TimestampUrl
    RFC 3161 时间戳服务，默认 http://timestamp.digicert.com。
    传空字符串表示不盖时间戳（证书过期后旧签名即失效，发布物不要这么做）。

.PARAMETER DryRun
    只打印命令行，不执行签名。即使没有 signtool 也返回 0。

.EXAMPLE
    powershell -NoProfile -File tools\release\Sign-Artifacts.ps1 -Path .\artifacts\release\CkanUISetup-0.1.0-x64.exe -Thumbprint A1B2C3 -DryRun

.EXAMPLE
    # 本地自签证书试签（证书由 New-DevCertificate.ps1 生成）
    powershell -NoProfile -File tools\release\Sign-Artifacts.ps1 -Path .\x.exe -PfxPath .\artifacts\certs\dev-code-signing.pfx -PfxPassword ckanui-dev
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]] $Path,

    [string] $Thumbprint = '',

    [string] $PfxPath = '',

    [string] $PfxPassword = '',

    [string] $TimestampUrl = 'http://timestamp.digicert.com',

    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

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

function Quote-Argument {
    param([string] $Value)
    if ($Value -match '[\s"]') { return ('"' + $Value + '"') }
    return $Value
}

# ---- 1. 定位 signtool -------------------------------------------------------

function Find-SignTool {
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) {
        return [pscustomobject]@{ Path = $onPath.Source; Source = 'PATH' }
    }

    $kitsRoots = @()
    $x86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    $x64 = [Environment]::GetEnvironmentVariable('ProgramFiles')
    if ($x86) { $kitsRoots += (Join-Path $x86 'Windows Kits\10\bin') }
    if ($x64) { $kitsRoots += (Join-Path $x64 'Windows Kits\10\bin') }

    $found = @()
    foreach ($root in $kitsRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($versionDir in @(Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $parsed = [version]'0.0'
            try { $parsed = [version]$versionDir.Name } catch { $parsed = [version]'0.0' }
            foreach ($arch in @('x64', 'x86', 'arm64')) {
                $candidate = Join-Path $versionDir.FullName (Join-Path $arch 'signtool.exe')
                if (Test-Path -LiteralPath $candidate) {
                    $found += [pscustomobject]@{ Path = $candidate; Version = $parsed; Arch = $arch; Dir = $versionDir.Name }
                }
            }
        }
    }

    if ($found.Count -eq 0) { return $null }

    $best = $found |
        Sort-Object -Property @(
            @{ Expression = 'Version'; Descending = $true },
            @{ Expression = { if ($_.Arch -eq 'x64') { 0 } else { 1 } }; Descending = $false }
        ) |
        Select-Object -First 1
    return [pscustomobject]@{ Path = $best.Path; Source = ('Windows Kits ' + $best.Dir + ' ' + $best.Arch) }
}

$signTool = Find-SignTool

Write-Host ''
Write-Host '[1] 定位 signtool' -ForegroundColor Cyan
if ($signTool) {
    Write-Info ($signTool.Path + '（来源：' + $signTool.Source + '）')
}
elseif ($DryRun) {
    Write-Note '本机没有 signtool.exe：-DryRun 只预览命令行，所以继续（真实签名会失败）。'
}
else {
    Write-Host ''
    Write-Host '失败：找不到 signtool.exe。' -ForegroundColor Red
    Write-Host ''
    Write-Host '三条可操作的路，任选一条：' -ForegroundColor Yellow
    Write-Host '  1) 装 Windows SDK，只勾「Windows SDK Signing Tools for Desktop Apps」：'
    Write-Host '     https://developer.microsoft.com/windows/downloads/windows-sdk/'
    Write-Host '     装完 signtool.exe 位于 %ProgramFiles(x86)%\Windows Kits\10\bin\<版本>\x64\signtool.exe，本脚本会自动找到。'
    Write-Host '  2) 先预览将要执行的命令行、不签名：加 -DryRun（不需要 signtool）。'
    Write-Host '  3) 证书在证书存储里时用 -Thumbprint 指定，但 signtool 本体仍然要装。'
    Write-Host '  若最终走 SignPath / 云签名，则本脚本不参与签名，改在 CI 里按厂商流程做（见 docs/ops/install.md）。'
    exit 1
}

# ---- 2. 校验参数 ------------------------------------------------------------

Write-Host ''
Write-Host '[2] 校验参数' -ForegroundColor Cyan

$files = @()
foreach ($item in $Path) {
    $full = $item
    if (-not [System.IO.Path]::IsPathRooted($full)) {
        $full = Join-Path (Get-Location).Path $full
    }
    $full = [System.IO.Path]::GetFullPath($full)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        Exit-WithError ('要签名的文件不存在：' + $full)
    }
    $files += $full
}
foreach ($file in $files) {
    Write-Info ('待签名 ' + $file)
}

$useThumbprint = [bool]$Thumbprint
$usePfx = [bool]$PfxPath

if ($useThumbprint -and $usePfx) {
    Write-Note '同时给了 -Thumbprint 与 -PfxPath，以 -Thumbprint 为准。'
    $usePfx = $false
}
if (-not $useThumbprint -and -not $usePfx) {
    if (-not $DryRun) {
        Exit-WithError '必须给 -Thumbprint 或 -PfxPath 之一（本机没有可用于签名的证书）。'
    }
    Write-Note '没有给 -Thumbprint 或 -PfxPath：下面按"证书未指定"打印占位命令行。'
}

$pfxFull = ''
if ($usePfx) {
    $pfxFull = $PfxPath
    if (-not [System.IO.Path]::IsPathRooted($pfxFull)) {
        $pfxFull = Join-Path (Get-Location).Path $pfxFull
    }
    $pfxFull = [System.IO.Path]::GetFullPath($pfxFull)
    if (-not (Test-Path -LiteralPath $pfxFull -PathType Leaf)) {
        Exit-WithError ('找不到证书文件：' + $pfxFull)
    }
    Write-Info ('证书文件 ' + $pfxFull)
}

# ---- 3. 组装命令 ------------------------------------------------------------

Write-Host ''
Write-Host '[3] 组装 signtool 命令' -ForegroundColor Cyan

$signArgs = @('sign', '/a', '/fd', 'sha256')
if ($TimestampUrl) {
    $signArgs += @('/tr', $TimestampUrl, '/td', 'sha256')
    Write-Info ('时间戳 RFC3161 ' + $TimestampUrl)
}
else {
    Write-Note '没有时间戳（-TimestampUrl 为空）：证书过期后旧签名即失效，发布物不要这么做。'
}

if ($useThumbprint) {
    $signArgs += @('/sha1', $Thumbprint)
    Write-Info ('按证书指纹选证书 ' + $Thumbprint)
}
elseif ($usePfx) {
    $signArgs += @('/f', $pfxFull, '/p', $PfxPassword)
    Write-Info '按 .pfx 选证书（口令在日志里打码）'
}
else {
    $signArgs += @('/sha1', '<证书指纹>')
}

# 预演用的参数副本：pfx 口令一律换成 ***，别把明文口令打进日志。
$previewArgs = @()
foreach ($arg in $signArgs) { $previewArgs += $arg }
if ($usePfx -and $PfxPassword) {
    $passwordIndex = [array]::IndexOf($previewArgs, '/p')
    if ($passwordIndex -ge 0 -and ($passwordIndex + 1) -lt $previewArgs.Count) {
        $previewArgs[$passwordIndex + 1] = '***'
    }
}

function Format-SignToolCommand {
    param([string] $ToolName, [string[]] $Arguments, [string] $File)
    $pieces = @()
    $pieces += (Quote-Argument $ToolName)
    foreach ($a in $Arguments) { $pieces += (Quote-Argument $a) }
    $pieces += (Quote-Argument $File)
    return ($pieces -join ' ')
}

$toolLabel = if ($signTool) { $signTool.Path } else { 'signtool.exe' }

# ---- 4. 执行（或预览） ------------------------------------------------------

Write-Host ''
if ($DryRun) {
    Write-Host '[4] 预演（-DryRun，不执行签名）' -ForegroundColor Cyan
    foreach ($file in $files) {
        Write-Host ''
        Write-Host (Format-SignToolCommand -ToolName $toolLabel -Arguments $previewArgs -File $file)
    }
    if ($usePfx -and $PfxPassword) {
        Write-Host ''
        Write-Info '口令在打印里显示为 ***：实际执行用 -PfxPassword 的值，别把明文口令写进日志或 CI 配置。'
    }
    Write-Host ''
    Write-Host ('预演完成：' + $files.Count + ' 个文件，未改动任何文件。') -ForegroundColor Green
    exit 0
}

Write-Host '[4] 执行签名' -ForegroundColor Cyan
if (-not $signTool) {
    Exit-WithError '找不到 signtool.exe，无法签名（加 -DryRun 可只看命令行）。'
}
foreach ($file in $files) {
    $arguments = @()
    foreach ($a in $signArgs) { $arguments += $a }
    $arguments += $file

    Write-Info ('签名 ' + [System.IO.Path]::GetFileName($file))
    & $signTool.Path @arguments
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Exit-WithError ('signtool 退出码 ' + $code + '，已停止（后续文件不再签名）：' + $file)
    }
}

# ---- 5. 复核 ----------------------------------------------------------------

Write-Host ''
Write-Host '[5] 复核签名' -ForegroundColor Cyan
$bad = 0
foreach ($file in $files) {
    $sig = Get-AuthenticodeSignature -LiteralPath $file
    $stamp = if ($sig.TimeStamperCertificate) { $sig.TimeStamperCertificate.Subject } else { '（无时间戳）' }
    Write-Info ([System.IO.Path]::GetFileName($file) + '：' + $sig.Status + ' / ' + $stamp)
    if ($sig.Status -ne 'Valid') { $bad++ }
}

Write-Host ''
if ($bad -gt 0) {
    Write-Note '有文件状态不是 Valid：用自签证书或证书链不受信任时这是预期结果，发布前请换受信任证书重签。'
    Write-Host ('签名完成（' + $files.Count + ' 个文件，其中 ' + $bad + ' 个未通过链校验）。') -ForegroundColor Yellow
    exit 0
}
Write-Host ('签名完成：' + $files.Count + ' 个文件全部 Valid。') -ForegroundColor Green
exit 0
