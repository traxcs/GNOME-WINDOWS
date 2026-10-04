Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\GnomeWin\Assets\GnomeWin.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [single]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $s = [single]($size - 1)
    $path.AddArc(0, 0, $r * 2, $r * 2, 180, 90)
    $path.AddArc($s - $r * 2, 0, $r * 2, $r * 2, 270, 90)
    $path.AddArc($s - $r * 2, $s - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc(0, $s - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $size), ([System.Drawing.Color]::FromArgb(255, 98, 160, 234)), ([System.Drawing.Color]::FromArgb(255, 26, 95, 180))
    $g.FillPath($brush, $path)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    $u = $size / 16.0
    $g.FillEllipse($white, [single](4.2 * $u), [single](6.6 * $u), [single](7.6 * $u), [single](7.0 * $u))
    $g.FillEllipse($white, [single](3.0 * $u), [single](3.4 * $u), [single](2.2 * $u), [single](2.6 * $u))
    $g.FillEllipse($white, [single](5.6 * $u), [single](2.0 * $u), [single](2.2 * $u), [single](2.6 * $u))
    $g.FillEllipse($white, [single](8.3 * $u), [single](2.0 * $u), [single](2.2 * $u), [single](2.6 * $u))
    $g.FillEllipse($white, [single](10.9 * $u), [single](3.4 * $u), [single](2.2 * $u), [single](2.6 * $u))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @(); foreach ($sz in $sizes) { $images += ,(New-IconPng $sz) }
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $images[$i]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $images) { $bw.Write($data) }
$bw.Close()
Write-Host "Icon written to $out"
