# Generates ETLCrop.ico from scratch using System.Drawing.
# Design: rounded-square blue gradient (matching the ETWSpy palette), "ETL" wordmark,
# and crop corner-brackets overlaid in the lower-right to convey "crop".
# Produces a multi-resolution .ico (16, 32, 48, 64, 128, 256) with PNG-compressed frames.

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'
$outIco = Join-Path $PSScriptRoot 'ETLCrop.ico'

# Palette sampled from the ETWSpy icon (light/mid blue) with a deeper blue for depth.
$blueLight = [System.Drawing.Color]::FromArgb(255, 120, 178, 214)
$blueMid   = [System.Drawing.Color]::FromArgb(255, 44, 120, 200)
$blueDark  = [System.Drawing.Color]::FromArgb(255, 20, 78, 150)
$white     = [System.Drawing.Color]::White
$cropColor = [System.Drawing.Color]::FromArgb(255, 255, 214, 64)   # amber crop marks for contrast

function New-Frame([int]$size) {
	$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
	$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
	$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
	$g.Clear([System.Drawing.Color]::Transparent)

	$s = [double]$size
	$pad = [Math]::Max(1.0, $s * 0.06)
	[float]$rx = $pad
	[float]$ry = $pad
	[float]$rw = $s - (2.0 * $pad)
	[float]$rh = $s - (2.0 * $pad)
	$rect = New-Object System.Drawing.RectangleF($rx, $ry, $rw, $rh)
	$radius = $s * 0.20

	# Rounded-square path.
	$path = New-Object System.Drawing.Drawing2D.GraphicsPath
	[float]$d = $radius * 2.0
	$path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
	$path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
	$path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
	$path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
	$path.CloseFigure()

	# Diagonal gradient fill.
	$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $blueLight, $blueDark, 55.0)
	$g.FillPath($brush, $path)

	# Subtle top highlight.
	[float]$hlX = $rect.X
	[float]$hlY = $rect.Y
	[float]$hlW = $rect.Width
	[float]$hlH = $rect.Height * 0.5
	$hlRect = New-Object System.Drawing.RectangleF($hlX, $hlY, $hlW, $hlH)
	$hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush($hlRect, [System.Drawing.Color]::FromArgb(70, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255), 90.0)
	$g.FillPath($hl, $path)
	$hl.Dispose()

	# "ETL" wordmark, centered slightly above middle.
	$fontSize = $s * 0.34
	$font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
	$fmt = New-Object System.Drawing.StringFormat
	$fmt.Alignment = [System.Drawing.StringAlignment]::Center
	$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
	[float]$txtY = -$s * 0.08
	$textRect = New-Object System.Drawing.RectangleF([float]0, $txtY, [float]$s, [float]$s)
	# soft shadow
	$shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 0, 0, 0))
	[float]$shX = $s * 0.02
	[float]$shY = $txtY + ($s * 0.02)
	$shRect = New-Object System.Drawing.RectangleF($shX, $shY, [float]$s, [float]$s)
	$g.DrawString("ETL", $font, $shadow, $shRect, $fmt)
	$textBrush = New-Object System.Drawing.SolidBrush($white)
	$g.DrawString("ETL", $font, $textBrush, $textRect, $fmt)

	# Crop corner-brackets in the lower area (top-left and bottom-right corners of a frame).
	$penW = [Math]::Max(1.5, $s * 0.035)
	$pen = New-Object System.Drawing.Pen($cropColor, $penW)
	$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
	$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
	[float]$arm = $s * 0.16
	[float]$cx0 = $s * 0.30
	[float]$cy0 = $s * 0.62
	[float]$cx1 = $s * 0.70
	[float]$cy1 = $s * 0.82
	# top-left bracket
	$g.DrawLine($pen, $cx0, $cy0, [float]($cx0 + $arm), $cy0)
	$g.DrawLine($pen, $cx0, $cy0, $cx0, [float]($cy0 + $arm))
	# bottom-right bracket
	$g.DrawLine($pen, $cx1, $cy1, [float]($cx1 - $arm), $cy1)
	$g.DrawLine($pen, $cx1, $cy1, $cx1, [float]($cy1 - $arm))

	$pen.Dispose(); $shadow.Dispose(); $textBrush.Dispose(); $font.Dispose(); $fmt.Dispose()
	$brush.Dispose(); $path.Dispose(); $g.Dispose()
	return $bmp
}

# Encodes a 32bpp bitmap as a classic ICO "BMP/DIB" frame: a BITMAPINFOHEADER whose height is
# doubled (XOR color image + AND mask), bottom-up BGRA pixels, then a 1bpp AND mask. Windows
# requires this format for sub-256px icon frames; PNG-compressed small frames render blank.
function ConvertTo-IcoDib([System.Drawing.Bitmap]$bmp) {
	$w = $bmp.Width; $h = $bmp.Height
	$rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
	$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$stride = $data.Stride
	$buf = New-Object byte[] ($stride * $h)
	[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
	$bmp.UnlockBits($data)

	$ms = New-Object System.IO.MemoryStream
	$bw = New-Object System.IO.BinaryWriter($ms)
	# BITMAPINFOHEADER (40 bytes); biHeight is doubled to include the AND mask.
	$bw.Write([UInt32]40)
	$bw.Write([Int32]$w)
	$bw.Write([Int32]($h * 2))
	$bw.Write([UInt16]1)
	$bw.Write([UInt16]32)
	$bw.Write([UInt32]0)   # BI_RGB
	$bw.Write([UInt32]0)   # image size (0 ok for BI_RGB)
	$bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)

	# XOR color data: bottom-up rows, BGRA.
	for ($y = $h - 1; $y -ge 0; $y--) {
		$row = $y * $stride
		for ($x = 0; $x -lt $w; $x++) {
			$p = $row + $x * 4
			$bw.Write($buf[$p]); $bw.Write($buf[$p + 1]); $bw.Write($buf[$p + 2]); $bw.Write($buf[$p + 3])
		}
	}

	# AND mask: 1bpp, bottom-up, rows padded to 32 bits. 0 = opaque (alpha drives transparency).
	$maskStride = [Math]::Floor(($w + 31) / 32) * 4
	for ($y = $h - 1; $y -ge 0; $y--) {
		$mrow = New-Object byte[] $maskStride
		for ($x = 0; $x -lt $w; $x++) {
			$a = $buf[$y * $stride + $x * 4 + 3]
			if ($a -lt 128) { $mrow[[Math]::Floor($x / 8)] = $mrow[[Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
		}
		$bw.Write($mrow)
	}

	$bw.Flush()
	$bytes = $ms.ToArray()
	$bw.Dispose(); $ms.Dispose()
	# Force a single byte[] result; the unary comma prevents PowerShell from enumerating the array
	# (which would turn it into Object[] and break the BinaryWriter.Write(byte[]) overload).
	return , [byte[]]$bytes
}

$sizes = 16, 32, 48, 64, 128, 256
$frames = @()
foreach ($sz in $sizes) {
	$frame = New-Frame $sz
	if ($sz -ge 256) {
		# 256px stays PNG-compressed (smaller, and Windows supports PNG only at this size).
		$ms = New-Object System.IO.MemoryStream
		$frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
		$frames += , [pscustomobject]@{ Size = $sz; Png = $true; Data = $ms.ToArray() }
		$ms.Dispose()
	}
	else {
		# Smaller frames use BMP/DIB so Explorer, the taskbar and the title bar render them.
		$frames += , [pscustomobject]@{ Size = $sz; Png = $false; Data = (ConvertTo-IcoDib $frame) }
	}
	$frame.Dispose()
}

# Assemble the .ico container.
$fs = [System.IO.File]::Create($outIco)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
	$sz = $f.Size
	$bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # width
	$bw.Write([Byte]($(if ($sz -ge 256) { 0 } else { $sz })))   # height
	$bw.Write([Byte]0)    # palette
	$bw.Write([Byte]0)    # reserved
	$bw.Write([UInt16]1)  # color planes
	$bw.Write([UInt16]32) # bpp
	$bw.Write([UInt32]$f.Data.Length)
	$bw.Write([UInt32]$offset)
	$offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data) }
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "Wrote $outIco ($((Get-Item $outIco).Length) bytes, $($frames.Count) frames; 256px PNG, rest BMP/DIB)."
