param(
    [Parameter(Mandatory = $true)][string]$A,
    [Parameter(Mandatory = $true)][string]$B,
    [int]$Tol = 12
)
# 逐像素比对两张 PNG（视觉回归取证用）：输出差异像素数、百分比、包围盒与按行分带。
# 用法：pwsh -NoProfile -File tools/perf-probe/diff-png.ps1 -A a.png -B b.png
Add-Type -AssemblyName System.Drawing
$i1 = [System.Drawing.Bitmap]::FromFile($A)
$i2 = [System.Drawing.Bitmap]::FromFile($B)
if ($i1.Width -ne $i2.Width -or $i1.Height -ne $i2.Height) {
    "SIZE_MISMATCH $($i1.Width)x$($i1.Height) vs $($i2.Width)x$($i2.Height)"
    exit 1
}
$w = $i1.Width; $h = $i1.Height
$rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
$d1 = $i1.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$d2 = $i2.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$len = $w * $h * 4
$b1 = New-Object byte[] $len; $b2 = New-Object byte[] $len
[System.Runtime.InteropServices.Marshal]::Copy($d1.Scan0, $b1, 0, $len)
[System.Runtime.InteropServices.Marshal]::Copy($d2.Scan0, $b2, 0, $len)
$i1.UnlockBits($d1); $i2.UnlockBits($d2)

$diff = 0; $minX = $w; $minY = $h; $maxX = -1; $maxY = -1
$bands = New-Object System.Collections.Generic.List[string]
$bandStart = -1; $bandEnd = -1
for ($y = 0; $y -lt $h; $y++) {
    $rowBase = $y * $w * 4
    $rowDiff = 0
    for ($x = 0; $x -lt $w; $x++) {
        $o = $rowBase + $x * 4
        $d = [Math]::Abs($b1[$o] - $b2[$o]) + [Math]::Abs($b1[$o + 1] - $b2[$o + 1]) + [Math]::Abs($b1[$o + 2] - $b2[$o + 2])
        if ($d -gt $Tol) {
            $diff++; $rowDiff++
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
        }
    }
    if ($rowDiff -gt 0) {
        if ($bandStart -lt 0) { $bandStart = $y; $bandEnd = $y }
        elseif ($y -le $bandEnd + 2) { $bandEnd = $y }
        else { $bands.Add("band y=$bandStart..$bandEnd"); $bandStart = $y; $bandEnd = $y }
    }
}
if ($bandStart -ge 0) { $bands.Add("band y=$bandStart..$bandEnd") }

"diffPixels=$diff total=$($w * $h) pct=$([Math]::Round(100.0 * $diff / ($w * $h), 3))%"
if ($diff -gt 0) { "bbox=($minX,$minY)-($maxX,$maxY)" }
$bands | Select-Object -First 20
$i1.Dispose(); $i2.Dispose()
