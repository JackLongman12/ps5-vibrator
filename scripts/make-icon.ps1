<#
.SYNOPSIS
  Regenerates the app icon (white wave on a PS-blue tile) as a multi-size .ico.
.EXAMPLE
  powershell -File scripts/make-icon.ps1 -Out src/DualSenseStudio.App/Assets/app.ico
#>
param([string]$Out, [string]$PreviewPng)
Add-Type -AssemblyName System.Drawing
$S = 256
$master = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
# PS-blue rounded tile
$p = New-Object System.Drawing.Drawing2D.GraphicsPath; $r = 56; $d = $r*2; $w = 255
$p.AddArc(0,0,$d,$d,180,90); $p.AddArc($w-$d,0,$d,$d,270,90); $p.AddArc($w-$d,$w-$d,$d,$d,0,90); $p.AddArc(0,$w-$d,$d,$d,90,90); $p.CloseFigure()
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,0,112,209))), $p)
# One white vibration wave
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 22
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$pts = @()
for ($i = 0; $i -le 120; $i++) {
  $t = $i / 120
  $x = 48 + 160 * $t
  $y = 128 - [Math]::Sin($t * [Math]::PI * 4) * 52 * [Math]::Sin([Math]::PI * $t)
  $pts += New-Object System.Drawing.PointF ([single]$x), ([single]$y)
}
$g.DrawLines($pen, $pts); $g.Dispose()
if ($PreviewPng) { $master.Save($PreviewPng, [System.Drawing.Imaging.ImageFormat]::Png) }
$sizes = 16,24,32,48,64,256; $pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s; $gg = [System.Drawing.Graphics]::FromImage($bmp)
  $gg.InterpolationMode = 'HighQualityBicubic'; $gg.PixelOffsetMode = 'HighQuality'; $gg.DrawImage($master, 0, 0, $s, $s); $gg.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngs += ,$ms.ToArray(); $bmp.Dispose()
}
$fs = [System.IO.File]::Create($Out); $bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count); $offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dd = $pngs[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$dd.Length); $bw.Write([UInt32]$offset); $offset += $dd.Length
}
foreach ($dd in $pngs) { $bw.Write($dd) }; $bw.Close(); "wrote $Out"
