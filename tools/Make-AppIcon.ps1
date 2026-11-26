param([string]$OutFile, [string]$PreviewDir)
# Draws the ceramic-puck app icon at every Windows icon size and packs them into one .ico.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
if (!$OutFile) { $OutFile = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Assets\joystick.ico' }

function New-Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Freeze(); $b }
function New-Pen($brush, [double]$width) { $p = New-Object System.Windows.Media.Pen $brush, $width; $p.StartLineCap = 'Round'; $p.EndLineCap = 'Round'; $p.Freeze(); $p }
function C([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }

function Render-Icon([int]$size) {
    $s = $size / 256.0
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $center = New-Object System.Windows.Point (128 * $s), (122 * $s)
    $radius = 106 * $s
    if ($size -ge 32) {
        # Soft contact shadow under the puck.
        $shadow = New-Object System.Windows.Media.RadialGradientBrush
        $shadow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C '#553C2D1E'), 0.55))
        $shadow.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C '#003C2D1E'), 1.0))
        $dc.DrawEllipse($shadow, $null, (New-Object System.Windows.Point (128 * $s), (134 * $s)), (120 * $s), (118 * $s))
    }
    $body = New-Object System.Windows.Media.RadialGradientBrush
    $body.GradientOrigin = New-Object System.Windows.Point 0.36, 0.26
    $body.Center = New-Object System.Windows.Point 0.42, 0.34
    $body.RadiusX = 0.8; $body.RadiusY = 0.8
    $body.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C '#FFFFFF'), 0))
    $body.GradientStops.Add((New-Object System.Windows.Media.GradientStop (C '#E2DBD0'), 1))
    $rimWidth = [Math]::Max(1.0, 3.5 * $s)
    $dc.DrawEllipse($body, (New-Pen (New-Brush '#BDB4A6') $rimWidth), $center, ($radius - $rimWidth / 2), ($radius - $rimWidth / 2))
    if ($size -ge 40) {
        $dc.DrawEllipse($null, (New-Pen (New-Brush '#DDD5C9') ([Math]::Max(1.0, 4 * $s))), $center, (60 * $s), (60 * $s))
    }
    if ($size -ge 24) {
        $graphite = New-Brush '#2B2A27'
        $keyWidth = [Math]::Max(1.6, 15 * $s)
        $keyPen = New-Pen $graphite $keyWidth
        $keyRadius = 88 * $s
        $half = [Math]::Max(1.2, 13 * $s)
        foreach ($deg in 215, 325, 145, 35) {
            $a = $deg * [Math]::PI / 180
            $px = $center.X + $keyRadius * [Math]::Cos($a); $py = $center.Y + $keyRadius * [Math]::Sin($a)
            $tx = -[Math]::Sin($a); $ty = [Math]::Cos($a)
            $dc.DrawLine($keyPen, (New-Object System.Windows.Point ($px - $tx * $half), ($py - $ty * $half)),
                (New-Object System.Windows.Point ($px + $tx * $half), ($py + $ty * $half)))
        }
    }
    $core = New-Object System.Windows.Media.LinearGradientBrush (C '#FF7A33'), (C '#DC5410'), 90
    $coreRadius = if ($size -le 20) { 4.2 * $size / 16 } else { 26 * $s }
    $dc.DrawEllipse($core, $null, $center, $coreRadius, $coreRadius)
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = New-Object System.IO.MemoryStream
    $encoder.Save($memory)
    if ($PreviewDir) {
        New-Item -ItemType Directory -Force -Path $PreviewDir | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $PreviewDir ("icon-$size.png")), $memory.ToArray())
    }
    , $memory.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($size in $sizes) { $images += , (Render-Icon $size) }
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes($OutFile, $out.ToArray())
Write-Host ("{0} ({1} bytes, {2} sizes)" -f $OutFile, $out.Length, $sizes.Count)
