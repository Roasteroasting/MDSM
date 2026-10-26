# Milestone 7 artwork

The editable final assets are [pig.png](../Mobs/pig.png), **64 x 16 RGBA**, and [raw-porkchop.png](../Items/raw-porkchop.png), **16 x 16 RGBA**. They were generated with the built-in `image_gen` tool, not the fallback CLI. No generation or preparation runs at build/startup. The existing [terrain.png](../Blocks/terrain.png) is unchanged.

Pig tiles are a single horizontal row of four 16 x 16 squares: **0 skin, 1 face, 2 snout front, 3 leg/hoof**. During one-time preparation the generated sheet's white top/bottom margins were cropped away and its four equal-width tiles sampled with nearest-neighbor scaling into the final 64 x 16 PNG. The transparent porkchop sprite was nearest-neighbor sampled into 16 x 16; alpha was thresholded at 128 to exact 0/255 for crisp binary transparency. No existing block artwork was replaced.

These PNGs are ordinary directly editable assets, compiled by the existing Content pipeline with point sampling, no mipmaps/resizing, and alpha enabled. Edit them, rebuild, and relaunch. The pig's seven cuboids share this skin; the porkchop icon is shared by inventory, held, and dropped representations.

## Final pig generation prompt

```text
Use case: stylized-concept. Asset type: game texture sheet, NOT a model preview. Create a strictly pixel-art texture sheet for a simple early-Minecraft-inspired pink pig assembled from cuboids. Exact layout: one horizontal strip with FOUR equal square tiles edge-to-edge, no padding, no labels. Treat each tile as exactly 16 by 16 chunky pixels (64 by 16 logical pixels across the entire image). Tile 0 on far left: seamless light pink pig skin with restrained darker pink square mottling, full opaque square. Tile 1: pig face, pale pink full square, two clear black square eyes near upper left/right, no projecting snout drawn because model has separate snout cuboid. Tile 2: snout front, medium-pink full square with two dark square nostrils in center. Tile 3 on far right: pink leg texture, bottom four logical pixel rows dark brown-pink hoof, full opaque square. Flat orthographic unlit texture art only, crisp square grid, no gradients, no perspective, no 3D, no outlines around tiles, no text, no watermarks. Tiles fill entire 4:1 canvas.
```

## Final porkchop generation prompt

```text
Use case: stylized-concept. Asset type: transparent 16x16 logical pixel-art inventory sprite for an early Minecraft inspired voxel game. One recognizable uncooked raw porkchop, pink/red meat with cream fat rim and a small cream bone detail. Large chunky square pixel clusters on a strict 16 by 16 logical pixel grid. Single cutout centered, icon occupies about 14 by 13 logical pixels with a transparent margin. No plate, no background, no shadow, no text, no gradients, no smoothing, no watermark. Flat sprite, genuinely transparent background outside the porkchop silhouette.
```
