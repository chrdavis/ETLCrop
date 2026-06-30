# Generates the WiX UI bitmaps (banner.bmp 493x58, dialog.bmp 493x312) for the ETWCrop
# installer. The artwork composites the real application icon (ETLCrop.ico) onto a blue
# gradient so the installer always matches the app's branding exactly.

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$blueLight = [System.Drawing.Color]::FromArgb(255, 120, 178, 214)
$blueDark  = [System.Drawing.Color]::FromArgb(255, 20, 78, 150)

$iconPath = Join-Path $PSScriptRoot 'ETLCrop.ico'

# Load the largest, crispest frame of the app icon (prefer the PNG-encoded 256px frame).
function Get-IconBitmap {
	$bytes = [System.IO.File]::ReadAllBytes($iconPath)
	$count = [BitConverter]::ToUInt16($bytes, 4)
	$bestOff = 0; $bestSize = 0; $bestW = -1
	for ($i = 0; $i -lt $count; $i++) {
		$d = 6 + $i * 16
		$w = $bytes[$d]; if ($w -eq 0) { $w = 256 }
		$sz = [BitConverter]::ToUInt32($bytes, $d + 8)
		$off = [BitConverter]::ToUInt32($bytes, $d + 12)
		if ($bytes[$off] -eq 0x89 -and $w -ge $bestW) { $bestW = $w; $bestOff = $off; $bestSize = $sz }
	}
	if ($bestW -gt 0) {
		$ms = New-Object System.IO.MemoryStream($bytes, $bestOff, $bestSize)
		return [System.Drawing.Bitmap]::FromStream($ms)
	}
	$fs = [System.IO.File]::OpenRead($iconPath)
	$ico = New-Object System.Drawing.Icon($fs, 256, 256)
	$bmp = $ico.ToBitmap()
	$ico.Dispose(); $fs.Close()
	return $bmp
}

$iconBmp = Get-IconBitmap

function New-Bmp([int]$w, [int]$h, [string]$path, [bool]$banner) {
	$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
	$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
	$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

	if ($banner) {
		# Banner: white background (WiX overlays its dark title text on the left), with the app
		# icon shown at the right edge.
		$g.Clear([System.Drawing.Color]::White)
		[int]$isz = 40
		[int]$ix = $w - $isz - 10
		[int]$iy = [int](($h - $isz) / 2)
		$g.DrawImage($iconBmp, $ix, $iy, $isz, $isz)
	}
	else {
		# Dialog: WiX overlays its (dark) welcome text on the right two-thirds, so keep that area
		# white for legibility and confine the branded blue art + icon to the left column. This
		# reads cleanly regardless of the OS theme (the MSI UI itself is always light-chrome).
		$g.Clear([System.Drawing.Color]::White)
		[int]$colW = 164
		$colRect = New-Object System.Drawing.RectangleF([float]0, [float]0, [float]$colW, [float]$h)
		$br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($colRect, $blueLight, $blueDark, 55.0)
		$g.FillRectangle($br, $colRect)
		$br.Dispose()

		[int]$isz = 120
		[int]$ix = [int](($colW - $isz) / 2)   # centered within the art column
		[int]$iy = [int](($h - $isz) / 2)
		$g.DrawImage($iconBmp, $ix, $iy, $isz, $isz)
	}

	$g.Dispose()
	$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
	$bmp.Dispose()
	Write-Host "Wrote $path ($w x $h)."
}

New-Bmp 493 58  (Join-Path $PSScriptRoot '..\ETWCropInstaller\banner.bmp') $true
New-Bmp 493 312 (Join-Path $PSScriptRoot '..\ETWCropInstaller\dialog.bmp') $false

$iconBmp.Dispose()
