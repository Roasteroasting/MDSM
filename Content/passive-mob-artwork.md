# Milestone 9 passive-mob and resource artwork

The final, independently editable assets are [cow.png](./cow.png), [sheep.png](./sheep.png), and [chicken.png](./chicken.png), each **64 x 16 RGBA**, plus [leather.png](./leather.png) and [feather.png](./feather.png), each **16 x 16 RGBA**. White Wool occupies **tile 15 (pixel origin 240, 0)** in [terrain.png](./terrain.png).

These were generated with the built-in `image_gen` tool, not the fallback CLI. Each generated skin was a square 2 x 2 sheet; one-time preparation sampled each quadrant at the centers of a 16 x 16 grid and assembled the four tiles horizontally. Runtime tile order is **0 body, 1 front face, 2 muzzle/skin/beak, 3 leg/hoof**. Cow horns sample a pale 9 x 3 region at (35, 0) within its muzzle tile, so their texture stays separate from the nostrils.

The resource sheet's top-left wool quadrant was sampled into terrain tile 15. Its top-right leather and bottom-left feather quadrants were sampled into separate icons, preserving transparency and thresholding alpha at 128 to exact 0/255. The wool tile and skins are opaque. All terrain pixels outside tile 15 were checked against the original atlas. Existing pig, porkchop and water artwork was not modified.

No artwork generation or preparation runs at build/startup. The existing MonoGame Content Pipeline compiles the editable PNGs without mipmaps/resizing. Rendering uses point filtering and texel-center UVs. Edit, rebuild and restart normally.

## Final generation prompts

### cow

```text
Use case: stylized-concept. Asset type: pixel-art texture sheet for hardcoded cuboid voxel mob, NOT model preview. A square sheet containing exactly FOUR opaque square tiles in a 2x2 grid, edge to edge, no padding, labels, borders or background. Each square is exactly 16x16 logical pixels enlarged with nearest neighbor. Flat unlit chunky early-Minecraft-style pixel art, no perspective, no gradients, text or watermark. Tiles fill canvas. Cow skin: top-left tile seamless dark brown/black and white cowhide with large irregular white patches; top-right tile cow face with two dark eyes above pale muzzle; bottom-left tile pink/tan muzzle with dark nostrils (also useful horn base); bottom-right tile dark brown leg with dark charcoal hoof along bottom 4 logical rows.
```

### sheep

```text
Use case: stylized-concept. Asset type: pixel-art texture sheet for hardcoded cuboid voxel mob, NOT model preview. A square sheet containing exactly FOUR opaque square tiles in a 2x2 grid, edge to edge, no padding, labels, borders or background. Each square is exactly 16x16 logical pixels enlarged with nearest neighbor. Flat unlit chunky early-Minecraft-style pixel art, no perspective, no gradients, text or watermark. Tiles fill canvas. White sheep skin: top-left tile seamless off-white chunky fleece with subtle light-gray wool clusters; top-right tile beige/tan sheep face, dark eyes and pale muzzle; bottom-left tile tan short fur with subtle mottling; bottom-right tile tan leg with dark gray hoof along bottom 4 logical rows. Warm wool white not gray, keep recognizable sheep face.
```

### chicken

```text
Use case: stylized-concept. Asset type: pixel-art texture sheet for hardcoded cuboid voxel mob, NOT model preview. A square sheet containing exactly FOUR opaque square tiles in a 2x2 grid, edge to edge, no padding, labels, borders or background. Each square is exactly 16x16 logical pixels enlarged with nearest neighbor. Flat unlit chunky early-Minecraft-style pixel art, no perspective, no gradients, text or watermark. Tiles fill canvas. White chicken skin: top-left tile white feathers with subtle cream/light gray clusters; top-right tile white chicken face with clear black square eyes; bottom-left tile orange/yellow beak, subtle dark orange line; bottom-right tile golden yellow chicken legs/scales. White feathers, not penguin or duck, no full animal picture.
```

### resources

```text
Use case: stylized-concept. Asset type: pixel-art game resource sheet. Square sheet split exactly into FOUR equal square cells in a 2x2 grid, no gaps or padding. Each cell 16x16 logical pixels enlarged crisply, no smoothing. Top-left: opaque edge-to-edge tile of white wool, off-white chunky curls and subtle very light gray clusters, seamlessly tileable; top-right: one brown leather hide inventory icon with an irregular hide silhouette and lighter brown folds, surrounded by genuinely transparent background; bottom-left: one white feather inventory icon with gray outline, pale quill diagonally upward, genuinely transparent background outside the feather; bottom-right: entirely transparent empty cell. Flat early-Minecraft-inspired chunky pixel art only. No text, no frames, no shadows or perspective. Exactly three assets, kept in their specified separate cells.
```


