$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outPath = Join-Path $PSScriptRoot '..\src\app.ico'
$sizes = @(256, 128, 64, 48, 32, 16)
$images = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.Rectangle $pad, $pad, ($size - $pad * 2), ($size - $pad * 2)
    $radius = [int]($size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 11, 40, 68)), ([System.Drawing.Color]::FromArgb(255, 30, 110, 160)), 45
    $g.FillPath($brush, $path)

    $inner = New-Object System.Drawing.Rectangle ($rect.X + [int]($size * 0.08)), ($rect.Y + [int]($size * 0.08)), ($rect.Width - [int]($size * 0.16)), ($rect.Height - [int]($size * 0.16))
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(55, 255, 255, 255)), ([Math]::Max(1, $size * 0.012))
    $g.DrawPath($pen, $path)

    $fontSize = [float]($size * 0.36)
    $font = New-Object System.Drawing.Font 'Microsoft YaHei UI', $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF 0, (-$size * 0.03), $size, $size
    $g.DrawString('PC', $font, [System.Drawing.Brushes]::White, $textRect, $sf)

    $dot = [Math]::Max(2, [int]($size * 0.15))
    $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 74, 222, 128))
    $g.FillEllipse($dotBrush, ($size - $dot - [int]($size * 0.10)), ($size - $dot - [int]($size * 0.10)), $dot, $dot)
    $g.Dispose()
    $font.Dispose()
    $brush.Dispose()
    $pen.Dispose()
    $dotBrush.Dispose()
    $path.Dispose()
    $sf.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $images += ,@($size, $ms.ToArray())
    $ms.Dispose()
}

$fs = New-Object System.IO.FileStream $outPath, ([System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)
$offset = 6 + $images.Count * 16
foreach ($item in $images) {
    $size = [int]$item[0]
    $bytes = [byte[]]$item[1]
    $bw.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([Byte]($(if ($size -ge 256) { 0 } else { $size })))
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($item in $images) {
    $bytes = [byte[]]$item[1]
    $bw.Write($bytes)
}
$bw.Flush()
$bw.Close()
$fs.Close()
Get-Item $outPath | Select-Object FullName, Length
