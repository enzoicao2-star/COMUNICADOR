param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\Comunicador\Assets\Comunicador.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [System.Drawing.RectangleF]$Rectangle,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($Size * 0.0625)
    $radius = [float]($Size * 0.22)
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, $Size - 2 * $pad, $Size - 2 * $pad)
    $backgroundPath = New-RoundedRectanglePath -Rectangle $rect -Radius $radius
    $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 78, 151, 255),
        [System.Drawing.Color]::FromArgb(255, 155, 87, 241),
        50)
    $graphics.FillPath($gradient, $backgroundPath)

    $left = [float]($Size * 0.205)
    $top = [float]($Size * 0.275)
    $width = [float]($Size * 0.61)
    $height = [float]($Size * 0.43)
    $bubbleRadius = [float]($Size * 0.07)
    $bubbleRect = [System.Drawing.RectangleF]::new($left, $top, $width, $height)
    $bubblePath = New-RoundedRectanglePath -Rectangle $bubbleRect -Radius $bubbleRadius
    $tail = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $tail.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float]($Size * 0.36), [float]($Size * 0.68)),
        [System.Drawing.PointF]::new([float]($Size * 0.36), [float]($Size * 0.81)),
        [System.Drawing.PointF]::new([float]($Size * 0.49), [float]($Size * 0.70))
    ))
    $strokeWidth = [Math]::Max(1.35, $Size * 0.047)
    $whitePen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [float]$strokeWidth)
    $whitePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($whitePen, $bubblePath)
    $graphics.DrawPath($whitePen, $tail)

    $dotRadius = [Math]::Max(1.05, $Size * 0.035)
    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    foreach ($xRatio in 0.35, 0.50, 0.65) {
        $centerX = $Size * $xRatio
        $centerY = $Size * 0.49
        $graphics.FillEllipse(
            $whiteBrush,
            [float]($centerX - $dotRadius),
            [float]($centerY - $dotRadius),
            [float]($dotRadius * 2),
            [float]($dotRadius * 2))
    }

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()

    $stream.Dispose()
    $whiteBrush.Dispose()
    $whitePen.Dispose()
    $tail.Dispose()
    $bubblePath.Dispose()
    $gradient.Dispose()
    $backgroundPath.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return $bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = foreach ($size in $sizes) {
    [pscustomobject]@{ Size = $size; Bytes = (New-IconPng -Size $size) }
}

$directory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) {
        $writer.Write([byte[]]$image.Bytes)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Icone gerado em: $OutputPath"
