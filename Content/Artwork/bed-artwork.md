# Original Bed artwork

The original pixel-art source sheet is [bed.png](./bed.png), 64x32 pixels, with
eight 16x16 cells. It was created with the built-in image-generation tool, then
nearest-neighbor sampled and packed offline using [Pack.ps1](../../Tools/BedArtwork/Pack.ps1).
The source sheet remains directly editable. Existing terrain/item tiles were
verified pixel-identical outside the new assignments.

The generation prompt requested an original red blanket, cream pillow, wooden
frame and legs, with a flat 4x2 sheet: head top, foot top, head side, foot side;
head end, foot end, wooden underside, and isometric item icon. A subsequent edit
requested transparent backgrounds around the legs and icon while preserving
opaque pillow artwork. The packing step converts the new sheet's generated alpha
to binary cutouts and keeps the three full top/bottom texture tiles opaque.
No Minecraft artwork was imported.

Terrain atlas assignments: 53 head top, 54 foot top, 55 head side, 56 foot side,
57 head end, 58 foot end, 59 underside/leg wood. Item atlas tile 73 is the Bed icon.
All earlier block/item identifiers and atlas assignments remain unchanged.

Placed geometry rotates as a complete local model around each half's center.
The mattress/frame is six pixels high, from Y=3/16 to 9/16; narrow side/end
faces sample six-pixel strips rather than compressing whole square textures.
Four three-pixel corner legs extend to ground level. The two internal end faces
are omitted. Inventory, held and dropped Bed items use the dedicated cutout icon.

To repack a manually edited source sheet from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/BedArtwork/Pack.ps1 -Source Content/Artwork/bed.png
```
