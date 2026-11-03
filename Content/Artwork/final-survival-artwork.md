# Final survival artwork

Only new atlas positions were written. Existing terrain tiles 0–28 and item tiles 0–31 retain their original RGBA values. Runtime artwork remains [terrain.png](../Blocks/terrain.png), [items.png](../Items/items.png), and 64×16 skins under [Mobs](../Mobs/). MGCB builds those files normally; no artwork is generated at build/launch.

| Terrain tile | Content |
| --- | --- |
| 29 | Obsidian |
| 30–31 | Dry / wet tilled soil |
| 32–38 | Wheat stages 1–7 |
| 39–40 | Fence / Gate |
| 41–42 | Door bottom / top |
| 43 | Torch |
| 44–46 | Smoker top / side / front |
| 47–49 | Blast Furnace top / side / front |
| 50 | Opaque Lava |

| Item tile | Content |
| --- | --- |
| 32–39 | Seeds, Wheat, Bread, Rotten Flesh, Bone, Empty/Water/Lava Buckets |
| 40–44 | Wood, Stone, Iron, Diamond, Obsidian Hoes |
| 45–48 | Obsidian Pickaxe, Axe, Sword, Shovel |
| 49–72 | Leather/Wood/Stone/Iron/Diamond/Obsidian Armor, Helmet/Chest/Legs/Boots order |

Tile numbers are independent of persistent block/item IDs. New terrain tiles use row-major atlas coordinates with 16-pixel boundaries; item positions use the same convention. Previous sampling/filtering/content settings are unchanged.

The following three source sheets were generated with the built-in imagegen tool and retained here. One-time import sampled centers of each 16×16 cell. Magenta was keyed only in new terrain art. The generated item sheet had a light background despite the transparent request; a tile-local edge-connected neutral-background mask removed that background while retaining enclosed white/silver details. No existing artwork was resampled, masked or regenerated. The mob sheet was split into three 64×16 skins.

## Item source prompt

[final-items-source.png](./final-items-source.png):

> A game item atlas extension for MDSM, original early Minecraft-style pixel artwork, NOT copied assets. EXACT 8 columns by 6 rows of equal square cells, no margins/gutters, no text. Every cell a crisp 16x16 logical pixel icon enlarged evenly with nearest neighbor squares, transparent background. Row-major content: Row1 wheat seeds, wheat bundle, bread loaf, rotten flesh, bone, empty iron bucket, blue water bucket, orange lava bucket. Row2 wooden hoe, stone hoe, iron hoe, diamond hoe, obsidian hoe, obsidian pickaxe, obsidian axe, obsidian sword. Row3 obsidian shovel, leather helmet, leather chestplate, leather leggings, leather boots, wooden helmet, wooden chestplate, wooden leggings. Row4 wooden boots, stone helmet, stone chestplate, stone leggings, stone boots, iron helmet, iron chestplate, iron leggings. Row5 iron boots, diamond helmet, diamond chestplate, diamond leggings, diamond boots, obsidian helmet, obsidian chestplate, obsidian leggings. Row6 obsidian boots then seven empty transparent cells. Armor distinct recognizable silhouettes with small limited palettes: brown leather, tan plank wood, gray stone, silver iron, cyan diamond, near-black purple obsidian. Icons centered with 1 logical pixel transparent padding. No antialiasing, vector curves, labels, cast shadows, watermark or UI. All 41 icons distinct.

## Block source prompt

[final-blocks-source.png](./final-blocks-source.png):

> Original pixel-art terrain texture atlas extension for MDSM early voxel sandbox, exact 8 columns by 3 rows equal square cells, no margins/gutters, no text or grid outlines. Each cell seamless 16x16 logical pixels, uniformly enlarged nearest neighbor crisp squares, flat orthographic texture. Row1: dark purple-black obsidian rocky surface; dry tan-brown tilled farmland parallel furrows; dark moist tilled farmland furrows; wheat stage1 tiny green shoots on pure magenta background; wheat stage2 green sprouts on pure magenta; wheat stage3 green blades on pure magenta; wheat stage4 taller green stems on pure magenta; wheat stage5 half golden heads on pure magenta. Row2: wheat stage6 golden stalks on pure magenta; wheat stage7 mature golden wheat on pure magenta; wooden fence planks; wooden fence gate plank design; wooden door bottom wooden panels; wooden door top with small window filled pure magenta; torch stick and orange flame on pure magenta; smoker top. Row3: smoker side wooden slats stone rim; smoker front dark opening with cooking grate; blast furnace top iron grate; blast furnace side dark stone iron straps; blast furnace front with iron furnace opening; orange-red lava seamless opaque pixel mottles; empty pure magenta; empty pure magenta. Strict limited pixel palettes, no gradients, shadows or lighting, no labels. Opaque block faces full tile; plant/torch magenta pixels will be removed for transparency. Do not use copyrighted textures.

## Mob source prompt

[final-mobs-source.png](./final-mobs-source.png):

> Original texture atlas sheet for three hard-coded cuboid monsters in an early voxel sandbox game MDSM. EXACT 4 columns x 3 rows equal square cells, no gutters, no text. Every tile 16x16 logical pixels uniformly enlarged nearest neighbor. Row1 zombie: green skin mottles; green zombie face looking forward dark two eyes and mouth; teal ragged shirt fabric; purple trouser fabric. Row2 skeleton: ivory bone mottles; ivory skull face with black eye sockets and mouth; rib cage pattern pale bones against dark gray; pale joint bones gray shadows. Row3 spider: dark brown-black mottles; spider face with eight glowing red pixel eyes; dark brown abdomen with subtle red markings; dark brown-black leg striations. Opaque full tile skins, crisp pixel squares, limited palettes, no smooth gradients, no perspective, no borders, no labels. Do not copy Minecraft textures.
