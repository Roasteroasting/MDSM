$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
# Import the checked-in original sheet into terrain tiles 100-111. Run from the project root.
$source = [System.Drawing.Bitmap]::new((Resolve-Path 'Content/Blocks/electrical-source.png').Path)
$original = [System.Drawing.Bitmap]::new((Resolve-Path 'Content/Blocks/terrain.png').Path)
$atlas = $original.Clone() # Preserve unassociated RGB on partially transparent glass texels.
$original.Dispose()
for ($tile = 0; $tile -lt 12; $tile++) {
    $left = ($tile % 4) * ($source.Width / 4)
    $top = [Math]::Floor($tile / 4) * ($source.Height / 3)
    $target = 100 + $tile
    for ($y = 0; $y -lt 16; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            $sx = [int][Math]::Floor($left + ($x + 0.5) * $source.Width / 64)
            $sy = [int][Math]::Floor($top + ($y + 0.5) * $source.Height / 48)
            $atlas.SetPixel(($target % 16) * 16 + $x, [Math]::Floor($target / 16) * 16 + $y, $source.GetPixel($sx, $sy))
        }
    }
}
$atlas.Save((Join-Path (Get-Location) 'Content/Blocks/terrain-electrical-import.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$atlas.Dispose()
$source.Dispose()
Move-Item -LiteralPath Content/Blocks/terrain-electrical-import.png -Destination Content/Blocks/terrain.png -Force
