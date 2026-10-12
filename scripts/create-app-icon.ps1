param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    $brushes = @('#183D3B','#A7BDA7','#EDF0DC','#DDBB76') | ForEach-Object { [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($_)) }
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 256.0, $size / 256.0)
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.FillEllipse($brushes[0], 3, 3, 250, 250)
        $graphics.FillEllipse($brushes[3], 159, 48, 35, 35)
        $graphics.FillPolygon($brushes[1], [Drawing.PointF[]]@([Drawing.PointF]::new(28,181),[Drawing.PointF]::new(84,96),[Drawing.PointF]::new(144,181)))
        $graphics.FillPolygon($brushes[2], [Drawing.PointF[]]@([Drawing.PointF]::new(74,188),[Drawing.PointF]::new(132,72),[Drawing.PointF]::new(200,188)))
        $graphics.FillPolygon($brushes[1], [Drawing.PointF[]]@([Drawing.PointF]::new(132,72),[Drawing.PointF]::new(154,170),[Drawing.PointF]::new(200,188)))
        $graphics.FillRectangle($brushes[3], 63, 203, 130, 5)
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose(); foreach ($brush in $brushes) { $brush.Dispose() } }
}
$file = [IO.File]::Create($OutputPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
