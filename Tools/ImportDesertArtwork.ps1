Add-Type -AssemblyName System.Drawing
$root=Split-Path -Parent $PSScriptRoot
$path=Join-Path $root 'Content/Blocks/terrain.png'
$atlas=[Drawing.Bitmap]::new($path)
$cactus=[Drawing.Bitmap]::new((Join-Path $root 'Content/Artwork/cactus-source.png'))
$bush=[Drawing.Bitmap]::new((Join-Path $root 'Content/Artwork/dead-bush-source.png'))
try {
    for($tile=77;$tile -le 80;$tile++) {
        for($y=0;$y -lt 16;$y++){for($x=0;$x -lt 16;$x++) {
            if($tile -eq 80) {
                $color=$bush.GetPixel([int][Math]::Floor(($x+.5)*$bush.Width/16),[int][Math]::Floor(($y+.5)*$bush.Height/16))
                $color=if($color.A -lt 128){[Drawing.Color]::FromArgb(0,0,0,0)}else{[Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B)}
            } else {
                $color=$cactus.GetPixel([int][Math]::Floor((($tile-77)+($x+.5)/16)*$cactus.Width/3),[int][Math]::Floor(($y+.5)*$cactus.Height/16))
                $color=[Drawing.Color]::FromArgb(255,$color.R,$color.G,$color.B)
            }
            $atlas.SetPixel(($tile%16)*16+$x,[int][Math]::Floor($tile/16)*16+$y,$color)
        }}
    }
    $atlas.Save($path+'.import.png',[Drawing.Imaging.ImageFormat]::Png)
} finally {$atlas.Dispose();$cactus.Dispose();$bush.Dispose()}
Move-Item -LiteralPath ($path+'.import.png') -Destination $path -Force
