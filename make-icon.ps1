# Draws icon.ico: a dark rounded tile holding a lighter card with a title bar and a 2×2 grid of coloured shortcut squares.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Png([int]$s) {
    $b = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $m = [Math]::Max(0.5, $s * 0.06); $w = $s - 2 * $m; $r = $s * 0.2
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 34, 40))), (RoundRect $m $m $w $w $r))
    $cx = $s * 0.17; $cy = $s * 0.2; $cw = $s * 0.66; $ch = $s * 0.62
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 58, 62, 78))), (RoundRect $cx $cy $cw $ch ($s * 0.08)))
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 206, 220))), (RoundRect ($cx + $s * 0.07) ($cy + $s * 0.06) ($cw * 0.45) ([Math]::Max(1, $s * 0.05)) ([Math]::Max(0.5, $s * 0.025))))
    $cols = @(@(77, 166, 255), @(240, 88, 88), @(40, 190, 200), @(255, 170, 60))
    $t = $s * 0.17; $gap = $s * 0.06; $x0 = $cx + ($cw - 2 * $t - $gap) / 2; $y0 = $cy + $s * 0.17
    for ($i = 0; $i -lt 4; $i++) {
        $x = $x0 + ($i % 2) * ($t + $gap); $y = $y0 + [Math]::Floor($i / 2) * ($t + $gap); $c = $cols[$i]
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))), (RoundRect $x $y $t $t ($t * 0.28)))
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$imgs = $sizes | ForEach-Object { , (Png $_) }
$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $imgs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $imgs) { $bw.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())
Write-Output "Wrote icon.ico"

