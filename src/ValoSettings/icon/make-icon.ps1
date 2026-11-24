# Génère icon\app.ico à partir de icon.svg (48 px et plus) et icon-small.svg (16 à 32 px).
# Rendu SVG via Edge headless (fond transparent), réduction via System.Drawing, puis assemblage ICO (entrées PNG).
param([string]$Out = "$PSScriptRoot\app.ico")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("valoicon-" + [guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null

function Render-Svg([string]$svg, [string]$png) {
    $html = Join-Path $tmp ([IO.Path]::GetFileNameWithoutExtension($png) + ".html")
    $data = [Convert]::ToBase64String([IO.File]::ReadAllBytes($svg))
    Set-Content $html "<html><body style='margin:0;background:transparent'><img src='data:image/svg+xml;base64,$data' width='256' height='256' style='display:block'></body></html>"
    & $edge --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1 --default-background-color=00000000 `
        --window-size=256,256 "--user-data-dir=$tmp\edge" "--screenshot=$png" ("file:///" + $html.Replace('\', '/')) 2>&1 | Out-Null
    if (-not (Test-Path $png)) { throw "Rendu impossible : $svg" }
}

$big = Join-Path $tmp "big.png"; $small = Join-Path $tmp "small.png"
Render-Svg "$PSScriptRoot\icon.svg" $big
Render-Svg "$PSScriptRoot\icon-small.svg" $small

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $src = [Drawing.Image]::FromFile($(if ($s -le 32) { $small } else { $big }))
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose(); $src.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save("$PSScriptRoot\icon-256.png", [Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}

# Format ICO : en-tête 6 octets, 16 octets par entrée, puis les PNG.
$fs = [IO.File]::Create($Out)
$w = New-Object IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $len = $img[1].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $w.Write($img[1]) }
$w.Close()
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
"OK : $Out ($($images.Count) tailles)"
