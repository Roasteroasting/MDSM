# Overnight survival additions artwork

Editable runtime assets remain [terrain.png](../Blocks/terrain.png) and
[items.png](../Items/items.png), both 256 by 256 RGBA atlases with 16-pixel tiles.
Sign glyphs have their own [text.png](../UI/text.png) atlas with the same grid.
The original generated reference is
[survival-additions-source.png](./survival-additions-source.png). It was created
with the built-in image-generation tool, without external Minecraft artwork.

The requested design was an original retro voxel-game sprite sheet on a
transparent background, arranged as eight columns and four rows: Egg, Carrot,
Potato, Baked Potato, Flint and Steel, Snowball, Mossy Cobblestone and Snow in
the first row; Fire and seven Carrot growth stages in the second; seven Potato
growth stages in the third; unused cells transparent. The brief specified
16-pixel logical cells, limited flat colors, crisp pixel edges, no text, no
frames, and recognizable progressive crop heights. Moss and snow are tileable
block surfaces; vegetation, fire and item silhouettes use transparency.

[ImportSurvivalArtwork.ps1](../../Tools/ImportSurvivalArtwork.ps1) samples cell
centers with nearest-neighbor selection, writes only the assignments below,
and converts cutout alpha to 0/255. Existing artwork is not resampled or redrawn.
The importer does not write font data. Sign text uses a separate chunk-mesh batch
and the independent text atlas, with one quad per character per board side.
The tool is offline only: normal builds and gameplay load the existing MGCB
atlas entries, without generating textures at startup.

| Atlas | Tile IDs | Artwork |
| --- | --- | --- |
| Items | 74-79 | Egg, Carrot, Potato, Baked Potato, Flint and Steel, Snowball |
| Terrain | 60 | Mossy Cobblestone |
| Terrain | 61 | Snow |
| Terrain | 62 | Fire |
| Terrain | 63-69 | Carrot stages 1-7 |
| Terrain | 70-76 | Potato stages 1-7 |
| Text | 80-174 | Existing pixel-font ASCII 32-126, first 5 by 7 pixels of each tile |

The original glyphs were moved, not regenerated, using
[ExtractSignText.ps1](../../Tools/ExtractSignText.ps1). All 95 full tiles retain
their exact RGBA pixels and grid positions, including transparent RGB values.
Their concatenated little-endian ARGB pixel SHA-256 is
`08f9b8a85e39d46a6273e95a2d27461250934f617232586dc7e344a3224f7c14`.
The migration verified the saved PNGs pixel-by-pixel and confirmed zero terrain
changes outside those tiles. Terrain positions 81-174 remain transparent;
tile 80 now contains the new Dead Bush, documented in [desert-artwork.md](./desert-artwork.md).
The one-time extractor refuses to overwrite
an existing text atlas; ordinary builds do not run it.

Signs, wooden/cobblestone slabs and stairs reuse existing block textures with
cropped per-face UV coordinates. The arm uses colored 3D skin/sleeve cuboids;
clouds reuse the existing Snow tile (61); precipitation uses geometry and vertex colors. No new texture assets
are required for those models. Persistent item/block IDs are independent of
these atlas tile numbers.
