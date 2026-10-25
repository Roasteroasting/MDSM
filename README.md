# MDSM

A small Minecraft Indev-inspired voxel sandbox in C# and MonoGame DesktopGL. The game has a finite **256 x 64 x 256** world (X, Y, Z), seeded terrain, chunk-based rendering, and persistent worlds managed through a main menu.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting `Content/terrain.xnb` is copied into the build output and loaded by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Finite world and seed

Valid block coordinates are X/Z 0-255 and Y 0-63. The complete world stays in memory as **256 chunks**, arranged 16 x 16. Each chunk spans 16 x 64 x 16 blocks and stores one byte per block: **4,194,304 bytes (4 MiB)** of raw block data in total, plus chunk metadata and render buffers.

Choose **Create New World** from the main menu, enter a name, and optionally enter a signed 32-bit integer seed. Leaving the seed blank chooses a random seed. The same seed produces the same terrain. The code-level `World` constructor still defaults to `World.DefaultSeed` (173); the creation menu passes its selected seed explicitly. [TerrainGenerator.cs](./TerrainGenerator.cs) combines coherent 2D gradient noise at several scales using global coordinates, so hills and valleys continue across chunk boundaries. Surfaces stay within Y 16-44, with grass on top, 3-5 dirt blocks below, and stone underneath. Terrain generation is unchanged from Milestone 2.

The player spawns above the surface near the center. Horizontal player bounds stop at the finite edges, where terrain ends visibly. Block reads outside the world return air; edits outside it are rejected. Space above Y=63 remains open for movement, with no blocks stored above the limit. Mining through the bottom can cause a fall; falling below Y=-16 respawns the player above the current surface at the center.

Terrain generation and initial mesh creation run synchronously on the main thread, so startup includes a short initial build. Each chunk has its own reusable GPU vertex buffer, containing only exposed faces. Neighbor queries cross chunk boundaries, hiding interior seam faces. Chunk bounds are tested against the camera frustum before submitting draws. An edit rebuilds its own mesh and, at a chunk edge, the face-sharing neighbors (at most three meshes for a single block edit). Diagonal chunks are unaffected. Mesh scratch storage and GPU capacity are reused, and all graphics operations stay on the main thread.

## Worlds and saving

MDSM starts at the main menu. **Create New World** generates and immediately saves a new world. **Select World** lists existing world names, seeds, and local last-modified times. Mouse clicks select buttons and list rows. Up/Down and Tab move keyboard focus, Enter activates an option, and Escape returns or resumes. In creation fields, type text, use Backspace to erase, and Ctrl+A to replace the field. The world list supports mouse-wheel scrolling. The built-in pixel font displays basic Latin characters and punctuation; other characters are preserved in saves but may appear as question marks.

Escape during gameplay opens a pause menu with **Resume**, **Save World**, and **Save and Main Menu**. Movement and block edits stop while paused. Saving displays confirmation; save-and-return releases the world's GPU buffers after saving succeeds. Autosave runs every **five minutes of active gameplay**. F10, gamepad Back, and closing the window also attempt a save. If saving fails, the world stays open and the pause menu shows an error; quitting is canceled so saving can be retried. Losing focus opens the pause menu. Resuming consumes the activating click, preventing an accidental block break.

Production saves use the per-user `Environment.SpecialFolder.LocalApplicationData` directory, with a home-directory `.local/share` fallback where needed. On Windows the location is:

```text
%LOCALAPPDATA%/MDSM/Worlds/
  <32-character world GUID>/
    world.dat
```

Each world has an independent generated directory ID. Display names are metadata, never paths; blank names, control characters, path separators, and invalid path characters are rejected. Names can contain up to 48 UTF-16 characters. Two worlds may share a display name. Saves are kept outside the source repository and build output. No deletion, renaming, duplication, or import/export UI is implemented.

### Binary format, version 1

[WorldSerializer.cs](./WorldSerializer.cs) uses little-endian fields and no compression. Each save is approximately 4 MiB plus its small header and checksum:

| Field | Representation |
| --- | --- |
| Signature | Four ASCII bytes: `MDSM` |
| Format version | 32-bit integer, currently 1 |
| Dimensions | Three 32-bit integers: X=256, Y=64, Z=256 |
| World seed | Signed 32-bit integer |
| World name | 32-bit UTF-8 byte length, then UTF-8 bytes |
| Player feet position | X/Y/Z, three 32-bit floats |
| Camera orientation | Yaw/pitch, two 32-bit floats in radians |
| Block byte count | 32-bit integer, exactly 4,194,304 |
| Block data | One byte per block, all 256 chunks in chunk-Z/chunk-X order; within each chunk X varies fastest, then Z, then Y |
| Checksum | SHA-256 of all preceding bytes, 32 bytes |

Loading validates the signature, version, dimensions, name, finite player pose, block IDs, byte count, exact file length, and checksum. Stored blocks are copied directly into empty chunk storage; terrain is never regenerated over saved edits. Chunk meshes are rebuilt from the restored blocks. Unknown versions, missing files, and corrupt data produce a menu error. Listing reads only headers for speed; full checksum validation happens on load.

[WorldSaveManager.cs](./WorldSaveManager.cs) writes `world.dat.<random GUID>.tmp` in the same world directory, flushes it to disk, then atomically replaces `world.dat` (or renames it for the first save). Failed writes leave the previous valid save intact. Temporary files left by an interrupted process are ignored. No background save thread is used: save/load and generation can briefly pause the UI. Player velocity, hotbar selection, and F3 visibility are session state; position and orientation are persisted.

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
| Escape | Open the pause menu / resume from pause |
| Pause menu Resume | Resume and capture the mouse |
| F10 or gamepad Back | Save the active world and quit |

Block interaction reaches six blocks from the camera. Each click performs one edit. Placement requires an empty cell inside the world and cannot overlap the player's body. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams.

The hotbar provides unlimited blocks. Block edits persist when saved. Infinite terrain, crafting, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | Lifecycle, state transitions, save/autosave/exit orchestration, input, and renderer lifetime |
| [GameState.cs](./GameState.cs) | Main menu, creation, selection, gameplay, and pause states |
| [MenuController.cs](./MenuController.cs) | Menu layouts, text fields, world list, navigation, and feedback |
| [WorldSave.cs](./WorldSave.cs) | Saved metadata and active world/player/camera session |
| [WorldSerializer.cs](./WorldSerializer.cs) | Versioned binary format, validation, and checksums |
| [WorldSaveManager.cs](./WorldSaveManager.cs) | Per-user world directories, listing, and atomic file replacement |
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
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, save confirmation, and cached F3 debug text |
| [PixelFont.cs](./PixelFont.cs) | Shared bitmap lettering for the HUD and menus |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |
| [PersistenceChecks.cs](./PersistenceChecks.cs) | Edited-world restoration, corruption validation, safe paths, and failed-save checks |

All chunks remain loaded for the active world. There is no chunk streaming, multithreaded generation, water, trees, ores, or other gameplay expansion.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The 20 check groups cover deterministic terrain, storage, chunk invalidation and seams, culling, collision, original gameplay, atlas UVs, heavily edited world restoration, exact player pose, save checksums/headers/lengths, safe directory names, missing/corrupt saves, and atomic replacement failures. Controlled fixtures isolate physics and file tests. Persistence test directories are temporary fixtures under the test build and are cleaned up; they never touch the user's real saves. A failing check returns a nonzero exit code. These checks do not open a window and cannot verify GPU output or operating-system input behavior.

For a manual playtest, create named worlds with explicit and random seeds; edit blocks across chunk seams, move and turn, then save and return to the menu. Reopen each world and check blocks, position, and orientation. Check the five-minute autosave, exit saving, pause/resume input, world-list scrolling, text entry, corrupt-save errors, window resizing/focus, and extended play. The existing terrain, movement, hotbar, and F3 controls should behave as before.

