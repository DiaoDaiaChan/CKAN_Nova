<#
.SYNOPSIS
    真机驻留与空转采样：启动 CKAN-UI Release 可执行文件，等它挂稳后采样空闲 CPU / 工作集 / 线程 / 句柄。

.DESCRIPTION
    无头探针（perf-probe --mem）量的是托管堆与 Lab 环境下的帧成本，量不到真实进程的
    WorkingSet、线程数与"桌面挂机时到底烧多少 CPU"。本脚本补这一段：
    1. 启动 exe -> 先静置 WarmupSeconds 秒等启动瞬态结束（JIT/首屏/资源加载/实例扫描尾账）；
    2. 之后在 IdleSeconds 秒窗口内按 TotalProcessorTime 差值算单核占用率；
    3. 同一窗口取 WorkingSet64 / PrivateMemorySize64 / Threads / HandleCount 的峰值与末值；
    4. 采样结束强制结束进程（脚本自己拉起，不碰用户已开的实例）。
#>
[CmdletBinding()]
param(
    [string]$Exe = "src/CkanUI.App/bin/Release/net10.0/CkanUI.exe",
    # 启动瞬态实测能持续 20 秒以上（CPU 从 400%+ 一路衰减到 9-19%）。静置不足就把加载当空转，
    # 同一份代码能测出 5 倍差异——所以默认值取 45 秒，而不是以往的 8 秒。
    [int]$WarmupSeconds = 45,
    [int]$IdleSeconds = 20,
    [int]$SampleIntervalMs = 250,
    [string]$Out = "artifacts/perf/live-profile.json",
    # 传 -NoForeground 可测"窗口不在前台"的另一种空转口径；默认前台，保证与基线可比。
    [switch]$NoForeground,
    # 采样窗口内持续重申前台（别的进程/agent 弹窗会偷走焦点，焦点漂移会让空转 CPU 直接换量级）。
    [bool]$KeepForeground = $true
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path $Exe)) { $Exe = Join-Path $root $Exe }
if (-not (Test-Path $Exe)) { throw "exe not found: $Exe" }

$tempRoot = Join-Path $root "artifacts/tmp/live-temp"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
$env:TEMP = $tempRoot
$env:TMP = $tempRoot

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Focus {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool fAttach);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
  // Windows 的前台锁会拒绝普通进程的 SetForegroundWindow（实测 40 个采样点里 37 次抢不到）。
  // 先 AttachThreadInput 把本线程挂到当前前台线程的输入队列，再抢前台/置顶，才拿得到稳定的空转口径。
  public static bool ForceForeground(IntPtr hWnd) {
    uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
    uint myThread = GetCurrentThreadId();
    bool attached = false;
    if (fgThread != 0 && fgThread != myThread) { attached = AttachThreadInput(myThread, fgThread, true); }
    ShowWindow(hWnd, 9);
    BringWindowToTop(hWnd);
    bool ok = SetForegroundWindow(hWnd);
    if (attached) { AttachThreadInput(myThread, fgThread, false); }
    return ok;
  }
}
"@

Write-Host ("[live] start " + $Exe)
$proc = Start-Process -FilePath $Exe -PassThru
$deadline = (Get-Date).AddSeconds(12)
while ($proc.MainWindowHandle -eq [IntPtr]::Zero -and -not $proc.HasExited -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 150 }
Start-Sleep -Milliseconds 500
$proc.Refresh()
$hwnd = $proc.MainWindowHandle
$centerX = 0
$centerY = 0
if (-not $NoForeground -and $hwnd -ne [IntPtr]::Zero) {
    # 采样口径必须固定：默认把窗口提到前台、光标停到窗口中心，
    # 让「是否前台」「光标是否悬停」这两个变量不再随机漂移 —— 否则同一份代码能测出 12% 和 58% 两种空转。
    [Win32Focus]::ForceForeground($hwnd) | Out-Null
    $rect = New-Object Win32Focus+RECT
    if ([Win32Focus]::GetWindowRect($hwnd, [ref]$rect)) {
        $centerX = [int](($rect.Left + $rect.Right) / 2)
        $centerY = [int](($rect.Top + $rect.Bottom) / 2)
        [Win32Focus]::SetCursorPos($centerX, $centerY) | Out-Null
    }
    Write-Host "[live] window foregrounded, cursor parked at center"
}
Start-Sleep -Seconds ([Math]::Max(0, $WarmupSeconds - 2))
$proc.Refresh()
if ($proc.HasExited) { throw "process exited early: $Exe" }

$foregroundAtStart = ($hwnd -ne [IntPtr]::Zero) -and ([Win32Focus]::GetForegroundWindow() -eq $hwnd)

$cores = [Environment]::ProcessorCount
$cpuStart = $proc.TotalProcessorTime
$sw = [Diagnostics.Stopwatch]::StartNew()

$wsSamples = New-Object System.Collections.Generic.List[long]
$privSamples = New-Object System.Collections.Generic.List[long]
$threadSamples = New-Object System.Collections.Generic.List[int]
$handleSamples = New-Object System.Collections.Generic.List[int]

$focusLostCount = 0
while ($sw.Elapsed.TotalSeconds -lt $IdleSeconds) {
    if ($KeepForeground -and -not $NoForeground -and $hwnd -ne [IntPtr]::Zero) {
        if ([Win32Focus]::GetForegroundWindow() -ne $hwnd) {
            $focusLostCount++
            [Win32Focus]::ForceForeground($hwnd) | Out-Null
            [Win32Focus]::SetCursorPos($centerX, $centerY) | Out-Null
        }
    }
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
    foregroundDuringSample = $foregroundAtStart
    cursorParked          = (-not $NoForeground)
    keepForeground        = ($KeepForeground -and -not $NoForeground)
    focusLostSamples      = $focusLostCount
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

Write-Host ("[live] idle cpu one-core%=" + $oneCorePercent + "  machine%=" + $totalMachinePercent + "  cores=" + $cores + "  foreground=" + $foregroundAtStart + "  focusLost=" + $focusLostCount)
Write-Host ("[live] workingSet avgMb=" + $result.workingSetMbAvg + " maxMb=" + $result.workingSetMbMax + "  private avgMb=" + $result.privateMbAvg + " maxMb=" + $result.privateMbMax)
Write-Host ("[live] threadsMax=" + $result.threadsMax + "  handlesMax=" + $result.handlesMax)
Write-Host ("[live] json: " + $outPath)

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
