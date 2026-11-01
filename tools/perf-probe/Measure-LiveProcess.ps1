<#
.SYNOPSIS
    真机驻留与空转采样：启动 CKAN-UI Release 可执行文件，等它挂稳后采样空闲 CPU / 工作集 / 线程 / 句柄。

.DESCRIPTION
    无头探针（perf-probe --mem）量的是托管堆与 Lab 环境下的帧成本，量不到真实进程的
    WorkingSet、线程数与"桌面挂机时到底烧多少 CPU"。本脚本补这一段：
    1. 启动 exe -> 忽略前 WarmupSeconds 秒（JIT/首屏/资源加载尾账）；
    2. 之后在 IdleSeconds 秒窗口内按 TotalProcessorTime 差值算单核占用率；
    3. 同一窗口取 WorkingSet64 / PrivateMemorySize64 / Threads / HandleCount 的峰值与末值；
    4. 采样结束强制结束进程（脚本自己拉起，不碰用户已开的实例）。
#>
[CmdletBinding()]
param(
    [string]$Exe = "src/CkanUI.App/bin/Release/net10.0/CkanUI.exe",
    [int]$WarmupSeconds = 8,
    [int]$IdleSeconds = 10,
    [int]$SampleIntervalMs = 250,
    [string]$Out = "artifacts/perf/live-profile.json"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path $Exe)) { $Exe = Join-Path $root $Exe }
if (-not (Test-Path $Exe)) { throw "exe not found: $Exe" }

$tempRoot = Join-Path $root "artifacts/tmp/live-temp"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

Write-Host ("[live] start " + $Exe)
$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds $WarmupSeconds
$proc.Refresh()
if ($proc.HasExited) { throw "process exited early: $Exe" }

$cores = [Environment]::ProcessorCount
$cpuStart = $proc.TotalProcessorTime
$sw = [Diagnostics.Stopwatch]::StartNew()

$wsSamples = New-Object System.Collections.Generic.List[long]
$privSamples = New-Object System.Collections.Generic.List[long]
$threadSamples = New-Object System.Collections.Generic.List[int]
$handleSamples = New-Object System.Collections.Generic.List[int]

while ($sw.Elapsed.TotalSeconds -lt $IdleSeconds) {
    $proc.Refresh()
    $wsSamples.Add($proc.WorkingSet64)
    $privSamples.Add($proc.PrivateMemorySize64)
    $threadSamples.Add($proc.Threads.Count)
    $handleSamples.Add($proc.HandleCount)
    Start-Sleep -Milliseconds $SampleIntervalMs
}

$sw.Stop()
$proc.Refresh()
$cpuDelta = ($proc.TotalProcessorTime - $cpuStart).TotalMilliseconds
$wallMs = $sw.Elapsed.TotalMilliseconds
$oneCorePercent = [math]::Round($cpuDelta / $wallMs * 100.0, 2)
$totalMachinePercent = [math]::Round($oneCorePercent / $cores, 2)

$result = [ordered]@{
    measuredAt            = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    exe                   = $Exe
    warmupSeconds         = $WarmupSeconds
    idleSeconds           = $IdleSeconds
    cores                 = $cores
    idleCpuOneCorePercent = $oneCorePercent
    idleCpuMachinePercent = $totalMachinePercent
    workingSetMbAvg       = [math]::Round(($wsSamples | Measure-Object -Average).Average / 1MB, 1)
    workingSetMbMax       = [math]::Round(($wsSamples | Measure-Object -Maximum).Maximum / 1MB, 1)
    privateMbAvg          = [math]::Round(($privSamples | Measure-Object -Average).Average / 1MB, 1)
    privateMbMax          = [math]::Round(($privSamples | Measure-Object -Maximum).Maximum / 1MB, 1)
    threadsMax            = ($threadSamples | Measure-Object -Maximum).Maximum
    handlesMax            = ($handleSamples | Measure-Object -Maximum).Maximum
}

$outPath = if ([IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $root $Out }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outPath) | Out-Null
$result | ConvertTo-Json | Set-Content -Path $outPath -Encoding UTF8

Write-Host ("[live] idle cpu one-core%=" + $oneCorePercent + "  machine%=" + $totalMachinePercent + "  cores=" + $cores)
Write-Host ("[live] workingSet avgMb=" + $result.workingSetMbAvg + " maxMb=" + $result.workingSetMbMax + "  private avgMb=" + $result.privateMbAvg + " maxMb=" + $result.privateMbMax)
Write-Host ("[live] threadsMax=" + $result.threadsMax + "  handlesMax=" + $result.handlesMax)
Write-Host ("[live] json: " + $outPath)

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
