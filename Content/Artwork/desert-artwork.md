# Desert vegetation artwork

The editable runtime artwork is in [terrain.png](../Blocks/terrain.png). Existing
tiles are unchanged by [ImportDesertArtwork.ps1](../../Tools/ImportDesertArtwork.ps1),
which writes only these four 16x16 positions:

| Tile | Use |
| --- | --- |
| 77 | Cactus side, cropped to the 14-pixel-wide trunk faces |
| 78 | Cactus top |
| 79 | Cactus bottom |
| 80 | Transparent leafless Dead Bush |

The original references are [cactus-source.png](./cactus-source.png) and
[dead-bush-source.png](./dead-bush-source.png), created with the built-in image
generation tool. No existing artwork was regenerated. The cactus brief requested
three horizontal original retro voxel-game pixel-art tiles: a seamless muted-green
ridged side with sparse pale needles, a distinct ribbed top and darker bottom,
with crisp limited colors, no text, no borders and no external game artwork.
The bush brief requested a single leafless dry brown bush on transparency, a
short trunk and angular branching twigs, no leaves, ground, shadows or text.

The offline importer samples cell centers using nearest-neighbor selection.
Cactus pixels are opaque; bush alpha is thresholded at 128 to retain crisp cutouts.
It never runs during builds or gameplay. Both blocks use the established terrain
content-pipeline entry, point filtering and block-item registry. Persistent block
IDs 87/88 are independent of texture tile indices. The separate sign-font atlas
in `Content/UI/text.png` remains unchanged. Clouds reuse the existing Snow tile 61.
