# MDSM

A small Minecraft Indev-inspired voxel sandbox in C# and MonoGame DesktopGL. Milestone 2 provides a finite **256 x 64 x 256** world (X, Y, Z), gentle seeded terrain, and chunk-based rendering.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting `Content/terrain.xnb` is copied into the build output and loaded by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Finite world and seed

Valid block coordinates are X/Z 0-255 and Y 0-63. The complete world stays in memory as **256 chunks**, arranged 16 x 16. Each chunk spans 16 x 64 x 16 blocks and stores one byte per block: **4,194,304 bytes (4 MiB)** of raw block data in total, plus chunk metadata and render buffers.

The configurable default seed is `World.DefaultSeed` in [World.cs](./World.cs), currently **173**. Change that constant to choose a different default, or pass an integer to the `World` constructor. The same seed produces the same world. [TerrainGenerator.cs](./TerrainGenerator.cs) combines coherent 2D gradient noise at several scales using global coordinates, so hills and valleys continue across chunk boundaries. Surfaces stay within Y 16-44, with grass on top, 3-5 dirt blocks below, and stone underneath. There are no caves or other terrain features in this milestone.

The player spawns above the surface near the center. Horizontal player bounds stop at the finite edges, where terrain ends visibly. Block reads outside the world return air; edits outside it are rejected. Space above Y=63 remains open for movement, with no blocks stored above the limit. Mining through the bottom can cause a fall; falling below Y=-16 respawns the player above the current surface at the center.

Terrain generation and initial mesh creation run synchronously on the main thread, so startup includes a short initial build. Each chunk has its own reusable GPU vertex buffer, containing only exposed faces. Neighbor queries cross chunk boundaries, hiding interior seam faces. Chunk bounds are tested against the camera frustum before submitting draws. An edit rebuilds its own mesh and, at a chunk edge, the face-sharing neighbors (at most three meshes for a single block edit). Diagonal chunks are unaffected. Mesh scratch storage and GPU capacity are reused, and all graphics operations stay on the main thread.

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
| F3 | Toggle player coordinates, chunk coordinates, approximate FPS, seed, chunk count, and dimensions |
| Escape | Pause and release the mouse |
| Left click while paused | Resume and capture the mouse |
| F10 or gamepad Back | Quit |

Block interaction reaches six blocks from the camera. Each click performs one edit. Placement requires an empty cell inside the world and cannot overlap the player's body. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams. Losing window focus pauses the game and releases the mouse; the resume click does not break a block.

The hotbar provides unlimited blocks. World edits last for the current session; saving, infinite terrain, crafting, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | MonoGame lifecycle, input, mouse capture, and gameplay coordination |
| [Camera.cs](./Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Player.cs) | Player bounds, movement, gravity, jump, collision, and respawn |
| [Blocks.cs](./Blocks.cs) | Block types and atlas tile selection |
| [World.cs](./World.cs) | Finite world, chunk lookup/invalidation, safe reads/edits, placement, and collision queries |
| [Chunk.cs](./Chunk.cs) | Compact local block storage, world bounds, and mesh revision |
| [TerrainGenerator.cs](./TerrainGenerator.cs) | Seeded coherent height generation and grass/dirt/stone layering |
| [BlockRaycaster.cs](./BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./TextureAtlas.cs) | ContentManager atlas loading, grid tile rectangles, and texel-center UVs |
| [Content/terrain.png](./Content/terrain.png) | Editable 256 x 256 block texture atlas |
| [ChunkMeshBuilder.cs](./ChunkMeshBuilder.cs) | CPU exposed-face geometry with cross-chunk neighbor checks |
| [ChunkMesh.cs](./ChunkMesh.cs) | Per-chunk GPU buffer, revision tracking, uploads, and disposal |
| [WorldRenderer.cs](./WorldRenderer.cs) | Chunk frustum culling, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, pause overlay, and cached F3 debug text |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |

All chunks remain loaded. There is no chunk streaming, multithreaded generation, saving/loading, water, trees, ores, or other gameplay expansion.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The 16 check groups cover deterministic seeds, generated layers and headroom, 4 MiB storage and coordinate conversion, safe out-of-bounds access, exact chunk invalidation, seam/edge face counts, frustum tests, cross-chunk collision and raycasting, horizontal boundaries, atlas UVs, and the original movement/placement checks. Controlled flat fixtures isolate physics tests from terrain generation. A failing check returns a nonzero exit code. These checks do not open a window and cannot verify content loading, GPU output, or operating-system input behavior.

For a manual playtest, inspect hills from different directions, toggle F3, and cross the chunk boundaries indicated by its chunk coordinates. Break/place blocks on chunk seams and at world edges; check neighboring faces, collision, and selection. Walk toward all four horizontal edges, dig through the bottom, and check safe respawn. Confirm all three hotbar blocks and the original jump, pause/resume, mouse-look, resizing, and focus behavior. Check responsiveness while turning across a wide view of the world.

