$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function Import-Tiles($atlasPath,$sourcePath,$columns,$rows,$first,$count) {
    $old = [System.Drawing.Bitmap]::new((Resolve-Path $atlasPath).Path)
    $atlas = $old.Clone()
    $old.Dispose()
    $source = [System.Drawing.Bitmap]::new((Resolve-Path $sourcePath).Path)
    for ($tile=0; $tile -lt $count; $tile++) {
        $target=$first+$tile
        for ($y=0; $y -lt 16; $y++) { for ($x=0; $x -lt 16; $x++) {
            $sx=[int][Math]::Floor((($tile % $columns)+($x+.5)/16)*$source.Width/$columns)
            $sy=[int][Math]::Floor(([Math]::Floor($tile/$columns)+($y+.5)/16)*$source.Height/$rows)
            $atlas.SetPixel(($target%16)*16+$x,[Math]::Floor($target/16)*16+$y,$source.GetPixel($sx,$sy))
        }}
    }
    $temporary = $atlasPath + '.import.png'
    $atlas.Save((Join-Path (Get-Location) $temporary),[System.Drawing.Imaging.ImageFormat]::Png)
    $atlas.Dispose();$source.Dispose()
    Move-Item -LiteralPath $temporary -Destination $atlasPath -Force
}
Import-Tiles 'Content/Blocks/terrain.png' 'Content/Blocks/advanced-processing-source.png' 4 2 112 8
Import-Tiles 'Content/Items/items.png' 'Content/Items/advanced-food-source.png' 3 1 106 3
