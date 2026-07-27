[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$assetDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$iconSizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$renderScale = 4

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-UsageIconBitmap {
    param([int]$Size)

    $canvasSize = $Size * $renderScale
    $bitmap = New-Object System.Drawing.Bitmap($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)

    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = [float]$canvasSize / 256.0
    $inset = 8 * $scale
    $tileSize = $canvasSize - (2 * $inset)
    $cornerRadius = 50 * $scale

    $tilePath = New-RoundedRectanglePath -X $inset -Y $inset -Width $tileSize -Height $tileSize -Radius $cornerRadius
    $tileBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        ([System.Drawing.PointF]::new(0, 0)),
        ([System.Drawing.PointF]::new($canvasSize, $canvasSize)),
        [System.Drawing.Color]::FromArgb(255, 14, 25, 43),
        [System.Drawing.Color]::FromArgb(255, 3, 7, 15)
    )
    $graphics.FillPath($tileBrush, $tilePath)

    # A restrained cyan rim keeps the silhouette readable on dark and light desktops.
    $rimWidth = [Math]::Max(1.25 * $renderScale, 5.0 * $scale)
    $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 42, 224, 255), $rimWidth)
    $rimPen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Inset
    $graphics.DrawPath($rimPen, $tilePath)

    # The broken ring is the quota meter: cyan is remaining capacity, magenta is consumed.
    $meterInset = 43 * $scale
    $meterDiameter = $canvasSize - (2 * $meterInset)
    $meterRect = [System.Drawing.RectangleF]::new($meterInset, $meterInset, $meterDiameter, $meterDiameter)
    $meterWidth = [Math]::Max(2.0 * $renderScale, 19.0 * $scale)

    $trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 39, 62, 81), $meterWidth)
    $trackPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $trackPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($trackPen, $meterRect, -214, 320)

    $remainingPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 40, 232, 255), $meterWidth)
    $remainingPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $remainingPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($remainingPen, $meterRect, -214, 224)

    $usedPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 54, 190), $meterWidth)
    $usedPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $usedPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawArc($usedPen, $meterRect, 27, 79)

    # A hand-drawn terminal prompt links the meter to coding without copying any brand mark.
    $promptWidth = [Math]::Max(1.5 * $renderScale, 13.0 * $scale)
    $promptPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 226, 250, 255), $promptWidth)
    $promptPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $promptPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $promptPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    [System.Drawing.PointF[]]$chevronPoints = @(
        [System.Drawing.PointF]::new(101 * $scale, 96 * $scale),
        [System.Drawing.PointF]::new(132 * $scale, 128 * $scale),
        [System.Drawing.PointF]::new(101 * $scale, 160 * $scale)
    )
    $graphics.DrawLines($promptPen, $chevronPoints)

    $cursorPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 54, 190), $promptWidth)
    $cursorPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cursorPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($cursorPen, 142 * $scale, 158 * $scale, 176 * $scale, 158 * $scale)

    $cursorPen.Dispose()
    $promptPen.Dispose()
    $usedPen.Dispose()
    $remainingPen.Dispose()
    $trackPen.Dispose()
    $rimPen.Dispose()
    $tileBrush.Dispose()
    $tilePath.Dispose()
    $graphics.Dispose()

    if ($renderScale -eq 1) {
        return $bitmap
    }

    $result = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $result.SetResolution(96, 96)
    $downsample = [System.Drawing.Graphics]::FromImage($result)
    $downsample.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $downsample.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $downsample.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $downsample.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $downsample.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $downsample.DrawImage(
        $bitmap,
        ([System.Drawing.Rectangle]::new(0, 0, $Size, $Size)),
        0,
        0,
        $canvasSize,
        $canvasSize,
        [System.Drawing.GraphicsUnit]::Pixel
    )
    $downsample.Dispose()
    $bitmap.Dispose()
    return $result
}

$pngPaths = @()
foreach ($size in $iconSizes) {
    $bitmap = New-UsageIconBitmap -Size $size
    $pngPath = Join-Path $assetDirectory ("CodexUsageMonitor-{0}.png" -f $size)
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $pngPaths += $pngPath
}

Copy-Item -LiteralPath (Join-Path $assetDirectory 'CodexUsageMonitor-256.png') `
    -Destination (Join-Path $assetDirectory 'CodexUsageMonitor.png') -Force

# Store frames as 32-bit DIB data instead of PNG-compressed ICO frames. This is
# slightly larger, but remains compatible with legacy System.Drawing consumers
# used by .NET Framework tray icons as well as modern Windows Explorer.
function Convert-PngToIconDib {
    param([string]$PngPath)

    $bitmap = [System.Drawing.Bitmap]::FromFile($PngPath)
    $memoryStream = New-Object System.IO.MemoryStream
    $dibWriter = New-Object System.IO.BinaryWriter($memoryStream)
    try {
        $width = $bitmap.Width
        $height = $bitmap.Height
        $maskStride = [int](([Math]::Floor(($width + 31) / 32)) * 4)
        $xorBytes = $width * $height * 4

        $dibWriter.Write([UInt32]40)
        $dibWriter.Write([Int32]$width)
        $dibWriter.Write([Int32]($height * 2))
        $dibWriter.Write([UInt16]1)
        $dibWriter.Write([UInt16]32)
        $dibWriter.Write([UInt32]0)
        $dibWriter.Write([UInt32]$xorBytes)
        $dibWriter.Write([Int32]0)
        $dibWriter.Write([Int32]0)
        $dibWriter.Write([UInt32]0)
        $dibWriter.Write([UInt32]0)

        for ($y = $height - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $dibWriter.Write([Byte]$pixel.B)
                $dibWriter.Write([Byte]$pixel.G)
                $dibWriter.Write([Byte]$pixel.R)
                $dibWriter.Write([Byte]$pixel.A)
            }
        }

        for ($y = $height - 1; $y -ge 0; $y--) {
            $maskRow = New-Object Byte[] $maskStride
            for ($x = 0; $x -lt $width; $x++) {
                if ($bitmap.GetPixel($x, $y).A -eq 0) {
                    $byteIndex = [int][Math]::Floor($x / 8)
                    $bitIndex = 7 - ($x % 8)
                    $maskRow[$byteIndex] = $maskRow[$byteIndex] -bor (1 -shl $bitIndex)
                }
            }
            $dibWriter.Write($maskRow)
        }

        $dibWriter.Flush()
        return ,$memoryStream.ToArray()
    }
    finally {
        $dibWriter.Dispose()
        $memoryStream.Dispose()
        $bitmap.Dispose()
    }
}

$icoPath = Join-Path $assetDirectory 'CodexUsageMonitor.ico'
$iconStreams = @()
for ($index = 0; $index -lt $pngPaths.Count; $index++) {
    if ($iconSizes[$index] -eq 256) {
        # Windows recommends PNG compression for the 256px frame.
        $iconStreams += ,([System.IO.File]::ReadAllBytes($pngPaths[$index]))
    }
    else {
        $iconStreams += ,(Convert-PngToIconDib -PngPath $pngPaths[$index])
    }
}

$fileStream = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter($fileStream)
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$iconSizes.Count)

    $offset = 6 + (16 * $iconSizes.Count)
    for ($index = 0; $index -lt $iconSizes.Count; $index++) {
        $size = $iconSizes[$index]
        $dimensionByte = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([Byte]$dimensionByte)
        $writer.Write([Byte]$dimensionByte)
        $writer.Write([Byte]0)
        $writer.Write([Byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$iconStreams[$index].Length)
        $writer.Write([UInt32]$offset)
        $offset += $iconStreams[$index].Length
    }

    foreach ($iconStream in $iconStreams) {
        $writer.Write($iconStream)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

Write-Host ("Generated {0} PNG sizes and {1}" -f $iconSizes.Count, $icoPath)
