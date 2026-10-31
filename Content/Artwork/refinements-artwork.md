# Survival refinement artwork / unified item atlas

Runtime item artwork is [items.png](../Items/items.png), 256×256 RGBA, with 256 row-major 16×16 slots. Unused slots are transparent. Edit it normally and rebuild; no source-code art generation occurs at launch/build.

| Tiles | Contents |
| --- | --- |
| 0–2 | Raw Porkchop, Leather, Feather |
| 3–10 | Iron Ingot, Coal, Flint, Diamond, Stick, Arrow, String, Charcoal |
| 11–14 | Wooden Pickaxe, Axe, Sword, Shovel |
| 15–18 | Stone Pickaxe, Axe, Sword, Shovel |
| 19–22 | Iron Pickaxe, Axe, Sword, Shovel |
| 23–26 | Diamond Pickaxe, Axe, Sword, Shovel |
| 27 | Bow |
| 28–31 | Sapling, Apple, Shears, Cooked Porkchop |

Persistent item IDs are independent of tile indices. Sapling uses block/item ID 31; Apple 284, Shears 285, Cooked Porkchop 286. All prior IDs are unchanged. Sapling's world cross uses terrain tile 28, copied from the same new icon.

The former [survival.png](../Items/survival.png), [raw-porkchop.png](../Items/raw-porkchop.png), [leather.png](../Items/leather.png), and [feather.png](../Items/feather.png) remain as historical source references for pixel-exact regression checks. They are not built or loaded by the game. Their 7,168 RGBA pixels were copied with integer tile coordinates, without scaling, filtering, color conversion or redraw. The test-only PNG decoder verifies every channel, including hidden RGB under transparency.

Only the four new icons were generated with the built-in imagegen tool, using this prompt:

> Asset type: a single four-tile sprite strip for MDSM, an early Minecraft Indev-inspired voxel game. Exactly four equally sized square tiles in one horizontal row, no gutters, no labels, no text. Each tile is artwork authored on a strict 16 by 16 logical pixel grid, uniformly enlarged with nearest-neighbor crisp square pixels. Left to right: 1 oak sapling with little brown stem and leafy green crown, 2 red apple with brown stem green leaf, 3 gray iron shears with two distinct open blades and small dark handles diagonal, 4 browned cooked porkchop with tan fat edge. Each symbol centered within its tile with transparent background; substantial recognizable silhouettes using about 8 colors per icon. No antialias, shadows outside silhouettes, gradients, bevels, photorealism, borders, or UI. Leave approximately one logical pixel transparent padding at tile edges. This is new original pixel artwork; do not copy any existing game assets.

[refinements-source.png](./refinements-source.png) preserves the generated strip. One-time import sampled centers of a 16×16 grid in each quarter, preserving sampled RGB, with binary alpha at 128. This preparation applies only to new artwork. No old artwork was resampled. The existing terrain tiles 0–27 remain unchanged.
