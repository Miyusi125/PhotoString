$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetFolder = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $assetFolder -Force | Out-Null
$bitmap = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$resources = New-Object 'System.Collections.Generic.List[System.IDisposable]'
function Get-IconBrush([string]$color) { $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($color)); $resources.Add($brush); return $brush }
$shape = New-Object System.Drawing.Drawing2D.GraphicsPath
$shape.AddArc(4, 4, 56, 56, 180, 90)
$shape.AddArc(196, 4, 56, 56, 270, 90)
$shape.AddArc(196, 196, 56, 56, 0, 90)
$shape.AddArc(4, 196, 56, 56, 90, 90)
$shape.CloseFigure()
$graphics.FillPath((Get-IconBrush '#6e8064'), $shape)
$rope = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml('#d8c49e')), 5
$resources.Add($rope)
$graphics.DrawBezier($rope, 28, 65, 76, 104, 183, 104, 228, 60)
$paper = Get-IconBrush '#fffbed'
$graphics.FillRectangle($paper, 49, 90, 76, 102)
$graphics.FillRectangle($paper, 144, 83, 63, 93)
$graphics.FillRectangle((Get-IconBrush '#b5c9be'), 55, 97, 64, 72)
$graphics.FillRectangle((Get-IconBrush '#d5bd9f'), 150, 90, 51, 64)
$points = [System.Drawing.Point[]]@([System.Drawing.Point]::new(55,169), [System.Drawing.Point]::new(87,125), [System.Drawing.Point]::new(119,169))
$graphics.FillPolygon((Get-IconBrush '#597664'), $points)
$graphics.FillEllipse((Get-IconBrush '#f1dfb9'), 93, 105, 16, 16)
$graphics.FillRectangle((Get-IconBrush '#789382'), 160, 109, 18, 45)
$graphics.FillRectangle((Get-IconBrush '#dec7a2'), 81, 74, 10, 35)
$graphics.FillRectangle((Get-IconBrush '#dec7a2'), 171, 67, 10, 35)
$graphics.FillEllipse((Get-IconBrush '#dec7a2'), 21, 57, 15, 15)
$graphics.FillEllipse((Get-IconBrush '#dec7a2'), 220, 52, 15, 15)
$stream = New-Object System.IO.MemoryStream
$bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$file = [System.IO.File]::Create((Join-Path $assetFolder 'PhotoString.ico'))
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]1)
$writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([Byte]0); $writer.Write([Byte]0)
$writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$png.Length); $writer.Write([UInt32]22)
$writer.Write([Byte[]]$png)
$writer.Dispose(); $stream.Dispose(); $graphics.Dispose(); $shape.Dispose(); $bitmap.Dispose()
foreach ($resource in $resources) { $resource.Dispose() }
