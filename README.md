# MDSM

A small Minecraft Indev-inspired voxel sandbox prototype in C# and MonoGame DesktopGL. The world is a 32 x 16 x 32 block volume with a flat grass surface, two dirt layers, and two stone layers.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting `Content/terrain.xnb` is copied into the build output and loaded by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Editing block textures

Open [Content/terrain.png](./Content/terrain.png) in an ordinary image editor. Keep the image at **256 x 256 pixels**, arranged as a **16 x 16 grid of 16 x 16 pixel tiles**. Tiles are numbered from 0 to 255 in row-major order: left to right, then top to bottom. Tile 16 begins the second row, and tile 255 is at the bottom right.

| Tile index | Texture | Pixel origin (x, y) |
| --- | --- | --- |
| 0 | Grass top | (0, 0) |
| 1 | Grass side | (16, 0) |
| 2 | Dirt / grass bottom | (32, 0) |
| 3 | Stone | (48, 0) |
| 4–255 | Unused, transparent | `(index % 16 * 16, index / 16 * 16)` using integer division |

The first four tiles were exported once from the original seed-173 algorithm. The PNG is the source artwork; builds and game launches never regenerate or overwrite it. Edit a tile, save the PNG, then run `dotnet build` followed by `dotnet run --no-build` (or simply `dotnet run`, which builds first). Restarting an existing build without rebuilding will continue to use its previously compiled texture. Replacing existing textures requires no C# changes.

For a future material, draw its texture in an unused slot and assign that index in the centralized [Blocks.Tile mapping](./Blocks.cs). No atlas renderer changes are needed. Grass still uses its top, side, and dirt-bottom textures; dirt and stone use one texture on all faces. Both world rendering and the hotbar use point sampling, with mipmaps disabled and world UVs inset to texel centers to prevent tile bleeding. Unused transparent slots are placeholders; transparent-block rendering is outside this refactor.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move forward / left / backward / right |
| Mouse | Look around |
| Space | Jump while on the ground |
| Left click | Break the outlined block |
| Right click | Place the selected block against the outlined face |
| 1 / 2 / 3 (or numpad) | Select grass / dirt / stone |
| Escape | Pause and release the mouse |
| Left click while paused | Resume and capture the mouse |
| F10 or gamepad Back | Quit |

Block interaction reaches six blocks from the camera. Each click performs one edit. Placement requires an empty cell inside the world and cannot overlap the player's body. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Falling off the terrain respawns the player above the original spawn column, accounting for blocks placed there. Losing window focus pauses the game and releases the mouse; the resume click does not break a block.

The hotbar provides unlimited blocks. World edits last for the current session; saving, infinite terrain, crafting, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | MonoGame lifecycle, input, mouse capture, and gameplay coordination |
| [Camera.cs](./Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Player.cs) | Player bounds, movement, gravity, jump, collision, and respawn |
| [Blocks.cs](./Blocks.cs) | Block types and atlas tile selection |
| [World.cs](./World.cs) | Flat terrain, safe reads/edits, placement, and collision queries |
| [BlockRaycaster.cs](./BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./TextureAtlas.cs) | ContentManager atlas loading, grid tile rectangles, and texel-center UVs |
| [Content/terrain.png](./Content/terrain.png) | Editable 256 x 256 block texture atlas |
| [WorldRenderer.cs](./WorldRenderer.cs) | Exposed-face mesh, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, and pause overlay |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |

Only exposed faces are uploaded to the GPU. A world revision triggers a mesh rebuild after an edit; the mesh is reused between edits. Rebuilding this small world's mesh keeps editing straightforward without adding a chunk streaming system.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The checks cover terrain and edit bounds, camera orientation, mesh face counts and winding, atlas rows and texel-center coordinates, raycast hits and reach, placement safety, jumping and landing, wall sliding, diagonal speed, ceilings, fast falls, and safe respawn. A failing check returns a nonzero exit code. These checks do not open a window and cannot verify content loading, GPU output, or operating-system input behavior.

For a manual playtest, confirm the textured terrain, crosshair, hotbar, and block outline render correctly; walk and jump into placed walls; break the floor and place all three block types; verify placement never traps the player; then test Escape, click-to-resume, window resizing, and switching focus.

