param([Parameter(Mandatory=$true)][string]$Source)
# Offline nearest-neighbor packing only; existing atlas tiles are never redrawn.
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$inputImage = [Drawing.Bitmap]::new([IO.Path]::GetFullPath($Source))
$sheet = [Drawing.Bitmap]::new(64,32)
for ($y=0;$y -lt 32;$y++) { for ($x=0;$x -lt 64;$x++) {
    $sx=[Math]::Min($inputImage.Width-1,[int][Math]::Floor(($x+.5)*$inputImage.Width/64))
    $sy=[Math]::Min($inputImage.Height-1,[int][Math]::Floor(($y+.5)*$inputImage.Height/32))
    $color=$inputImage.GetPixel($sx,$sy)
    # The game uses binary cutouts. Quantize only the new sheet's generated alpha.
    $tile=[int][Math]::Floor($y/16)*4+[int][Math]::Floor($x/16)
    if($tile -in @(0,1,6)){$color=[Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B)}
    elseif($color.A -lt 250){$color=[Drawing.Color]::Transparent}
    $sheet.SetPixel($x,$y,$color)
} }
$inputImage.Dispose()
$sheet.Save((Join-Path $root 'Content/Artwork/bed.png'),[Drawing.Imaging.ImageFormat]::Png)
foreach ($entry in @(@('Blocks/terrain.png',53,7,0),@('Items/items.png',73,1,7))) {
    $path=Join-Path $root ('Content/'+$entry[0]);$atlas=[Drawing.Bitmap]::new($path)
    for($tile=0;$tile -lt $entry[2];$tile++) {
        $sourceTile=$tile+$entry[3];$targetTile=$tile+$entry[1]
        for($y=0;$y -lt 16;$y++) {for($x=0;$x -lt 16;$x++) {
            $atlas.SetPixel(($targetTile%16)*16+$x,[int][Math]::Floor($targetTile/16)*16+$y,$sheet.GetPixel(($sourceTile%4)*16+$x,[int][Math]::Floor($sourceTile/4)*16+$y))
        }}
    }
    $encoded=[IO.MemoryStream]::new();$atlas.Save($encoded,[Drawing.Imaging.ImageFormat]::Png);$atlas.Dispose()
    [IO.File]::WriteAllBytes($path,$encoded.ToArray());$encoded.Dispose()
}
$sheet.Dispose()
