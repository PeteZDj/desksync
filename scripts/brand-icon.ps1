Add-Type -AssemblyName System.Drawing
$root=Split-Path -Parent $PSScriptRoot
$bitmap=[Drawing.Bitmap]::new(64,64);$g=[Drawing.Graphics]::FromImage($bitmap);$g.SmoothingMode='AntiAlias';$g.Clear([Drawing.Color]::Transparent)
$brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(50,105,236));$shape=[Drawing.Drawing2D.GraphicsPath]::new()
$shape.AddArc(2,2,28,28,180,90);$shape.AddArc(34,2,28,28,270,90);$shape.AddArc(34,34,28,28,0,90);$shape.AddArc(2,34,28,28,90,90);$shape.CloseFigure();$g.FillPath($brush,$shape)
$pen=[Drawing.Pen]::new([Drawing.Color]::White,5);$pen.StartCap='Round';$pen.EndCap='Round';$g.DrawArc($pen,15,15,34,34,205,125);$g.DrawArc($pen,15,15,34,34,25,125)
$g.DrawLines($pen,[Drawing.Point[]]@([Drawing.Point]::new(37,13),[Drawing.Point]::new(48,17),[Drawing.Point]::new(45,29)));$g.DrawLines($pen,[Drawing.Point[]]@([Drawing.Point]::new(27,51),[Drawing.Point]::new(16,47),[Drawing.Point]::new(19,35)))
$icon=[Drawing.Icon]::FromHandle($bitmap.GetHicon());$file=[IO.File]::Create((Join-Path $root 'assets\desksync.ico'));$icon.Save($file);$file.Dispose();$icon.Dispose();$pen.Dispose();$brush.Dispose();$shape.Dispose();$g.Dispose();$bitmap.Dispose()
