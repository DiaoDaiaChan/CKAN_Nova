<#
.SYNOPSIS
    One-click build + launch for CKAN UI.

.DESCRIPTION
    Builds ckan-ui.sln in Release, stops any running CkanUI.exe (the build
    fails on locked output files otherwise), starts the app, and tries to
    bring its window to the foreground.

    The foreground step simulates a click on the title bar first: Windows
    refuses SetForegroundWindow from a process that does not own the
    current foreground window, and simulated input releases that lock.

.PARAMETER NoBuild
    Skip the build and launch the existing binary.

.PARAMETER StayAlive
    Block while the app runs. Useful when the caller expects the script to
    own the child process lifetime; a double-click does not need it.

.EXAMPLE
    scripts\run-gui.cmd
    scripts\run-gui.cmd -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$StayAlive
)

$ErrorActionPreference = 'Stop'

# The script lives in tools/run-gui, so the repository root is two levels up.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$solution = Join-Path $root 'ckan-ui.sln'
$exe = Join-Path $root 'src/CkanUI.App/bin/Release/net10.0/CkanUI.exe'
$logDir = Join-Path $root 'artifacts/tmp'
$stdoutLog = Join-Path $logDir 'gui-launch.log'
$stderrLog = Join-Path $logDir 'gui-launch.log.err'

if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class CkanWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

function Stop-RunningApp {
    $running = Get-Process -Name 'CkanUI' -ErrorAction SilentlyContinue
    if (-not $running) {
        return 0
    }

    foreach ($process in $running) {
        Write-Host ("[stop ] CkanUI.exe PID " + $process.Id + " - the build would fail on a locked output file")
    }

    Stop-Process -Id $running.Id -Force
    Start-Sleep -Milliseconds 400
    return $running.Count
}

function Wait-MainWindow([System.Diagnostics.Process]$process, [int]$timeoutMilliseconds) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            return [IntPtr]::Zero
        }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            return $process.MainWindowHandle
        }
        Start-Sleep -Milliseconds 200
    }
    return [IntPtr]::Zero
}

function Focus-Window([IntPtr]$handle) {
    if ([CkanWindow]::IsIconic($handle)) {
        [CkanWindow]::ShowWindow($handle, 9) | Out-Null   # SW_RESTORE
    }

    [CkanWindow]::ShowWindow($handle, 5) | Out-Null       # SW_SHOW
    [CkanWindow]::BringWindowToTop($handle) | Out-Null
    [CkanWindow]::SetForegroundWindow($handle) | Out-Null
    if ([CkanWindow]::GetForegroundWindow() -eq $handle) {
        return $true
    }

    $rect = New-Object CkanWindow+RECT
    [CkanWindow]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $centerX = [int](($rect.Left + $rect.Right) / 2)
    $titleY = $rect.Top + 14
    [CkanWindow]::SetCursorPos($centerX, $titleY) | Out-Null
    Start-Sleep -Milliseconds 120
    [CkanWindow]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # LEFTDOWN
    Start-Sleep -Milliseconds 60
    [CkanWindow]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # LEFTUP
    Start-Sleep -Milliseconds 300
    [CkanWindow]::SetForegroundWindow($handle) | Out-Null
    return ([CkanWindow]::GetForegroundWindow() -eq $handle)
}

$stopped = Stop-RunningApp

if (-not $NoBuild) {
    Write-Host "[build] dotnet build ckan-ui.sln -c Release"
    $buildLog = & dotnet build $solution -c Release -p:UseSharedCompilation=false -p:AvaloniaTelemetryOptOut=true -m:1 2>&1
    $buildExit = $LASTEXITCODE
    $buildLog | Select-Object -Last 6 | ForEach-Object { Write-Host ("        " + $_) }
    if ($buildExit -ne 0) {
        Write-Host "[build] FAILED - exit code $buildExit" -ForegroundColor Red
        exit $buildExit
    }
    Write-Host "[build] OK"
}

if (-not (Test-Path $exe)) {
    Write-Host "[error] Missing binary: $exe" -ForegroundColor Red
    exit 2
}

Write-Host "[start] $exe"
$app = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
$handle = Wait-MainWindow $app 25000
if ($app.HasExited) {
    Write-Host ("[error] CkanUI.exe exited with code " + $app.ExitCode + " - see " + $stderrLog) -ForegroundColor Red
    exit 3
}
if ($handle -eq [IntPtr]::Zero) {
    Write-Host "[warn ] process is running but no window handle showed up in 25 s"
} else {
    $foreground = Focus-Window $handle
    $rect = New-Object CkanWindow+RECT
    [CkanWindow]::GetWindowRect($handle, [ref]$rect) | Out-Null
    Write-Host ("[ready] PID " + $app.Id + "  hwnd " + $handle + "  rect " + $rect.Left + "," + $rect.Top + " " + ($rect.Right - $rect.Left) + "x" + ($rect.Bottom - $rect.Top) + "  foreground=" + $foreground)
    if (-not $foreground) {
        Write-Host "[warn ] window is visible but another window owns the foreground; click it once" -ForegroundColor Yellow
    }
}

if ($StayAlive) {
    $app.WaitForExit()
    Write-Host ("[exit ] CkanUI.exe exited with code " + $app.ExitCode)
}
