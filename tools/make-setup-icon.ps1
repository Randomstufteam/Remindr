Add-Type -AssemblyName System.Drawing
function New-LogoPng([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size,$size,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $r = [int]($size * 0.22); $d = $r * 2
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc(0,0,$d,$d,180,90); $path.AddArc($size-$d-1,0,$d,$d,270,90)
  $path.AddArc($size-$d-1,$size-$d-1,$d,$d,0,90); $path.AddArc(0,$size-$d-1,$d,$d,90,90); $path.CloseFigure()
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0,0)),([System.Drawing.Point]::new($size,$size)),([System.Drawing.ColorTranslator]::FromHtml('#3B82F6')),([System.Drawing.ColorTranslator]::FromHtml('#8B5CF6'))
  $g.FillPath($br,$path)
  # bell, drawn on a 32x32 grid
  $s = $size / 32.0
  $bell = New-Object System.Drawing.Drawing2D.GraphicsPath
  $bell.AddBezier(16*$s,5*$s, 11*$s,5*$s, 8.5*$s,9*$s, 8.5*$s,13.5*$s)
  $bell.AddLine(8.5*$s,13.5*$s, 8.5*$s,19*$s); $bell.AddLine(8.5*$s,19*$s, 6*$s,22.5*$s); $bell.AddLine(6*$s,22.5*$s, 26*$s,22.5*$s)
  $bell.AddLine(26*$s,22.5*$s, 23.5*$s,19*$s); $bell.AddLine(23.5*$s,19*$s, 23.5*$s,13.5*$s)
  $bell.AddBezier(23.5*$s,13.5*$s, 23.5*$s,9*$s, 21*$s,5*$s, 16*$s,5*$s); $bell.CloseFigure()
  $g.FillPath([System.Drawing.Brushes]::White,$bell)
  $g.FillEllipse([System.Drawing.Brushes]::White, 13*$s, 24*$s, 6*$s, 4*$s)
  $g.FillEllipse([System.Drawing.Brushes]::White, 14.6*$s, 3*$s, 2.8*$s, 2.8*$s)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  ,$ms.ToArray()
}
$sizes = 16,24,32,48,64,128,256
$pngs = foreach ($z in $sizes) { ,(New-LogoPng $z) }
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
  $z = $sizes[$i]; $b = if ($z -ge 256) {0} else {$z}
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset); $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $PSScriptRoot) 'installer\Remindr.Setup\Assets\remindr.ico'), $out.ToArray())
