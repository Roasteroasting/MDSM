# One-time, lossless migration of the existing sign glyph tiles. No resampling.
Add-Type -AssemblyName System.Drawing
$projectRoot=Split-Path -Parent $PSScriptRoot
$terrainPath=Join-Path $projectRoot 'Content/Blocks/terrain.png'
$textPath=Join-Path $projectRoot 'Content/UI/text.png'
if(Test-Path -LiteralPath $textPath){throw 'text.png already exists; refusing to overwrite edited font artwork.'}
[IO.Directory]::CreateDirectory((Split-Path -Parent $textPath)) | Out-Null
$terrain=[Drawing.Bitmap]::new($terrainPath)
$text=[Drawing.Bitmap]::new(256,256,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$before=[int[]]::new(256*256)
$originalGlyphs=[IO.MemoryStream]::new()
$writer=[IO.BinaryWriter]::new($originalGlyphs)
try {
    if($terrain.Width -ne 256 -or $terrain.Height -ne 256){throw 'Unexpected terrain dimensions.'}
    for($y=0;$y -lt 256;$y++){for($x=0;$x -lt 256;$x++){$before[$y*256+$x]=$terrain.GetPixel($x,$y).ToArgb()}}
    for($tile=80;$tile -le 174;$tile++) {
        $left=($tile%16)*16;$top=[int][Math]::Floor($tile/16)*16
        for($y=0;$y -lt 16;$y++){for($x=0;$x -lt 16;$x++) {
            $color=$terrain.GetPixel($left+$x,$top+$y)
            $writer.Write($color.ToArgb())
            $text.SetPixel($left+$x,$top+$y,$color)
            $terrain.SetPixel($left+$x,$top+$y,[Drawing.Color]::FromArgb(0,0,0,0))
        }}
    }
    $terrain.Save($terrainPath+'.extract.png',[Drawing.Imaging.ImageFormat]::Png)
    $text.Save($textPath+'.extract.png',[Drawing.Imaging.ImageFormat]::Png)
    $savedTerrain=[Drawing.Bitmap]::new($terrainPath+'.extract.png')
    $savedText=[Drawing.Bitmap]::new($textPath+'.extract.png')
    try {
        for($y=0;$y -lt 256;$y++){for($x=0;$x -lt 256;$x++) {
            $tile=[int][Math]::Floor($y/16)*16+[int][Math]::Floor($x/16)
            if($tile -ge 80 -and $tile -le 174) {
                if($savedText.GetPixel($x,$y).ToArgb() -ne $before[$y*256+$x]){throw 'Glyph RGBA changed during migration.'}
                if($savedTerrain.GetPixel($x,$y).A -ne 0){throw 'Terrain still contains a glyph.'}
            } elseif($savedTerrain.GetPixel($x,$y).ToArgb() -ne $before[$y*256+$x]){throw 'Unrelated terrain pixel changed.'}
        }}
        $writer.Flush();$hash=[Security.Cryptography.SHA256]::Create()
        try {$digest=([BitConverter]::ToString($hash.ComputeHash($originalGlyphs.ToArray()))).Replace('-','').ToLowerInvariant()} finally {$hash.Dispose()}
        Write-Output ('Verified 95 tiles / 24,320 pixels, exact RGBA including transparent RGB. Original/migrated glyph SHA-256: '+$digest)
        Write-Output 'Verified: zero terrain changes outside the glyph tiles.'
    } finally {$savedTerrain.Dispose();$savedText.Dispose()}
} finally {$writer.Dispose();$originalGlyphs.Dispose();$terrain.Dispose();$text.Dispose()}
Move-Item -LiteralPath ($textPath+'.extract.png') -Destination $textPath
Move-Item -LiteralPath ($terrainPath+'.extract.png') -Destination $terrainPath -Force
