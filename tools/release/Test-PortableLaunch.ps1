#Requires -Version 5.1
<#
.SYNOPSIS
    便携包启动冒烟：解压 zip、真启动、等主窗口出现，然后关掉。

.DESCRIPTION
    为什么以"主窗口出现"作为通过标准：只看进程存活会假通过——Avalonia 初始化失败、
    缺少运行库、本地化包缺失时，WinExe 也可能存活好几秒（弹对话框或正在写日志）。
    主窗口标题出现说明 XAML 加载、字体、语言包与 CKAN Core 的初始化都过了。

    单实例守卫：程序启动时会先抢单实例锁，已有实例在跑时新进程会转发参数后自己退出。
    因此本脚本检测到已有 CKAN UI 进程时直接返回退出码 2（跳过），而不是判失败。

.PARAMETER ZipPath
    要冒烟的便携 zip 路径（通常是 artifacts/release 下的自包含版）。

.PARAMETER TimeoutSeconds
    等主窗口的上限秒数，默认 90。

.PARAMETER ExpectedTitle
    期望的主窗口标题片段，默认 CKAN UI。

.OUTPUTS
    退出码 0 = 看到主窗口；1 = 启动失败 / 提前退出 / 超时；2 = 跳过（已有实例在跑）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $ZipPath,
    [string] $ExeName = 'CkanUI.exe',
    [int] $TimeoutSeconds = 90,
    [string] $ExpectedTitle = 'CKAN UI',
    [switch] $KeepDir
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 默认不加载压缩程序集：ZipArchive 在 System.IO.Compression，
# ZipFile 在 System.IO.Compression.FileSystem，两个都要显式加载（pwsh 7 上重复加载也无害）。
try { Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop } catch { }
try { Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop } catch { }

if (-not (Test-Path -LiteralPath $ZipPath)) {
    Write-Output ('FAIL: 找不到 zip ' + $ZipPath)
    exit 1
}

$existing = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'CkanUI*' })
if ($existing.Count -gt 0) {
    $pids = ($existing | ForEach-Object { $_.Id.ToString() }) -join ','
    Write-Output ('SKIP: 已有 ' + $existing.Count + ' 个 CKAN UI 进程在运行（pid ' + $pids + '），单实例守卫会让新进程转发后退出。')
    exit 2
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ckan-ui-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[System.IO.Compression.ZipFile]::ExtractToDirectory($ZipPath, $tempRoot)
Write-Output ('INFO: 解压到 ' + $tempRoot)

$exePath = Join-Path $tempRoot $ExeName
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Output ('FAIL: 解压后没有 ' + $ExeName)
    try { Remove-Item -LiteralPath $tempRoot -Recurse -Force } catch { }
    exit 1
}

$exitCode = 1
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$process = $null
try {
    $process = Start-Process -FilePath $exePath -WorkingDirectory $tempRoot -PassThru
    $title = ''
    $elapsedMs = -1
    while ($stopwatch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        Start-Sleep -Milliseconds 250
        if ($process.HasExited) { break }
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle) {
            $title = $process.MainWindowTitle
            $elapsedMs = [int] $stopwatch.Elapsed.TotalMilliseconds
            break
        }
    }

    if ($process.HasExited) {
        $exitCode = 1
        Write-Output ('FAIL: 进程提前退出，ExitCode=' + $process.ExitCode + '，耗时 ' + [int] $stopwatch.Elapsed.TotalMilliseconds + ' ms')
    } elseif ($title) {
        Write-Output ('RESULT: window up in ' + $elapsedMs + ' ms title=[' + $title + '] pid=' + $process.Id)
        if ($ExpectedTitle -and $title -notlike ('*' + $ExpectedTitle + '*')) {
            Write-Output ('FAIL: 主窗口标题不含 "' + $ExpectedTitle + '"')
            $exitCode = 1
        } else {
            $exitCode = 0
        }
    } else {
        $exitCode = 1
        Write-Output ('FAIL: ' + $TimeoutSeconds + ' 秒内没出现主窗口，进程仍在运行=' + (-not $process.HasExited))
    }
} finally {
    $stopwatch.Stop()
    try {
        if ($process -and -not $process.HasExited) {
            $process.CloseMainWindow() | Out-Null
            if (-not $process.WaitForExit(5000)) { $process.Kill($true) }
            Write-Output 'INFO: 已关闭冒烟进程'
        }
    } catch {
        Write-Output ('INFO: 关闭进程失败：' + $_.Exception.Message)
    }
    if ($KeepDir) {
        Write-Output ('INFO: 保留解压目录 ' + $tempRoot)
    } else {
        try { Remove-Item -LiteralPath $tempRoot -Recurse -Force } catch { }
    }
}

if ($exitCode -ne 0) {
    Write-Output '提示：程序日志在 %LocalAppData%\CKAN\logs\（该目录不可写时会退回到程序目录）。'
}
exit $exitCode
