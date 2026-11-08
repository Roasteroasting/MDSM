Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot 'Content/Artwork/survival-additions-source.png'
$source = [Drawing.Bitmap]::new($sourcePath)
try {
    foreach ($atlasName in @('Items/items.png','Blocks/terrain.png')) {
        $atlasPath = Join-Path $projectRoot ('Content/' + $atlasName)
        $atlas = [Drawing.Bitmap]::new($atlasPath)
        try {
            $entries = @()
            if ($atlasName -eq 'Items/items.png') {
                for ($i=0; $i -lt 6; $i++) { $entries += @{ Tile=74+$i; X=$i; Y=0 } }
            } else {
                $entries += @{Tile=60; X=6; Y=0}, @{Tile=61; X=7; Y=0}, @{Tile=62; X=0; Y=1}
                for ($i=0; $i -lt 7; $i++) {
                    $entries += @{Tile=63+$i; X=1+$i; Y=1}, @{Tile=70+$i; X=$i; Y=2}
                }
            }
            foreach ($entry in $entries) {
                for ($y=0; $y -lt 16; $y++) { for ($x=0; $x -lt 16; $x++) {
                    $sx=[int][Math]::Floor(($entry.X+($x+0.5)/16)*$source.Width/8)
                    $sy=[int][Math]::Floor(($entry.Y+($y+0.5)/16)*$source.Height/4)
                    $color=$source.GetPixel($sx,$sy)
                    # Match the game's binary cutouts; preserve opaque sprite colors.
                    if ($color.A -lt 245 -and -not ($atlasName -eq 'Blocks/terrain.png' -and $entry.Tile -in @(60,61))) { $color=[Drawing.Color]::Transparent }
                    else { $color=[Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B) }
                    $atlas.SetPixel(($entry.Tile%16)*16+$x,[int][Math]::Floor($entry.Tile/16)*16+$y,$color)
                } }
            }
            $temporary = $atlasPath + '.import.png'
            $atlas.Save($temporary,[Drawing.Imaging.ImageFormat]::Png)
        } finally { $atlas.Dispose() }
        Move-Item -LiteralPath $temporary -Destination $atlasPath -Force
    }
} finally { $source.Dispose() }
