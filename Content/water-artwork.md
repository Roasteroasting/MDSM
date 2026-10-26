# Milestone 8 water artwork

[water.png](./water.png) is an independently editable **16 x 16 opaque RGBA** water tile, created with the built-in `image_gen` tool (not the fallback CLI). The renderer supplies translucency. The existing [terrain.png](./terrain.png), pig skin and porkchop artwork are unchanged.

One-time preparation sampled the generated square at the centers of a 16 x 16 grid with nearest-neighbor sampling and set alpha to 255. This produced the final tile directly in the Content directory. No generation/preparation code runs at build or startup. The ordinary MonoGame Content Pipeline compiles the PNG with no mipmaps or resizing; gameplay samples texel centers with point filtering. Edit, rebuild and restart normally.

Final generation prompt:

```text
Use case: stylized-concept. Asset type: seamless square pixel-art water texture for a Minecraft Indev-inspired voxel sandbox. Primary request: flat top-down tile of medium cobalt blue water with subtle lighter blue ripples, low contrast clusters, recognizable classic 16x16 pixel-art style enlarged with nearest neighbor. Composition: fill entire square edge to edge, seamless tiling in both axes, no border, no scene, no objects, no text, no perspective or lighting gradient. Palette: 4-6 blue shades. Opaque artwork; translucency will be applied by game renderer. Intended final asset 16x16 pixels.
```
