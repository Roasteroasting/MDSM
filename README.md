# MDSM

A small Minecraft Indev-inspired voxel sandbox in C# and MonoGame DesktopGL. The game has finite worlds in three sizes, seeded terrain with trees and vegetation, chunk-based rendering, and persistent worlds managed through a main menu.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting `Content/terrain.xnb` is copied into the build output and loaded by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Finite world and seed

Each world keeps its own size, selected during creation. **Normal** is the default. Click the size field to cycle options, or focus it with Tab and use Left/Right or Enter. All worlds remain entirely in memory. Chunks are always 16 x 64 x 16 blocks, with one byte per block; metadata and render buffers require additional memory.

| Size | Dimensions (X x Y x Z) | Chunk grid | Total chunks | Raw block bytes |
| --- | --- | --- | --- | --- |
| Small | 128 x 64 x 128 | 8 x 8 | 64 | 1,048,576 (1 MiB) |
| Normal | 256 x 64 x 256 | 16 x 16 | 256 | 4,194,304 (4 MiB) |
| Large | 512 x 64 x 512 | 32 x 32 | 1,024 | 16,777,216 (16 MiB) |

Valid X/Z coordinates run from zero through the selected width/depth minus one. Y remains 0-63 for every size. Storage, player bounds, spawn, decorations, rendering, and F3 dimensions/chunk counts use the active world's size, rather than a global selection.

Choose **Create New World** from the main menu, enter a name, and optionally enter a signed 32-bit integer seed. Leaving the seed blank chooses a random seed. The same seed and size produce the same complete world. The code-level `World` constructor defaults to seed 173 and Normal size; the creation menu passes its selections explicitly. [TerrainGenerator.cs](./TerrainGenerator.cs) combines coherent 2D gradient noise at several scales using global coordinates, so hills and valleys continue across chunk boundaries. Surfaces stay within Y 16-44, with grass on top, 3-5 dirt blocks below, and stone underneath. The height-generation algorithm remains unchanged from Milestone 2. New worlds replace stone at Y=0 with exactly one complete bedrock layer, without adding bedrock above it.

After base terrain generation, [WorldDecorator.cs](./WorldDecorator.cs) places oak-style trees, then sparse tall grass and red/yellow flowers. Seeded world-coordinate hashes and slowly varying density noise create open areas and wooded patches, with density scaling across the selected area. Trunks vary from 4 to 6 blocks high, with layered canopies and varying top shapes. Leaves extend at most two blocks from the trunk on either horizontal axis, within a 5 x 5 footprint. Candidates require grass, gentle slopes, sufficient clearance, and spacing between trunks. Canopies can cross chunk boundaries but never world boundaries. Decorations are written before meshes are built, without repeated mesh invalidation. Existing saves keep their original trees and bottom layer; generation corrections apply only to new worlds.

The player spawns above the surface near the center, with a clear area around the initial spawn. Horizontal player bounds stop at the finite edges, where terrain ends visibly. Block reads outside the world return air; edits outside it are rejected. Space above Y=63 remains open for movement, with no blocks stored above the limit. Mining through the bottom can cause a fall; falling below Y=-16 respawns the player above the current surface at the center.

Terrain generation and initial mesh creation run synchronously on the main thread, so startup includes a short initial build. Each chunk has its own reusable GPU vertex buffer. Opaque neighbors hide cube faces across chunk boundaries; faces needed behind transparent leaf pixels remain visible. Plants use two crossed, double-sided quads in the same chunk buffers. Alpha testing discards pixels with alpha below 128 while remaining pixels write depth normally, without alpha blending or transparency sorting. Chunk bounds are tested against the camera frustum before submitting draws. An edit rebuilds its own mesh and, at a chunk edge, the face-sharing neighbors (at most three meshes for a single block edit). Diagonal chunks are unaffected. Mesh scratch storage and GPU capacity are reused, and all graphics operations stay on the main thread.

## Worlds and saving

MDSM starts at the main menu, with a large centered title and a tiled dirt background sampled directly from the editable atlas. The background repeats at 64 screen pixels per tile using point sampling and adapts to the window size. Other menu screens share this background; pause retains the darkened gameplay view.

**Create New World** generates and immediately saves a new world. **Select World** lists existing world names, sizes, seeds, and local last-modified times. Mouse clicks select buttons and list rows. Up/Down and Tab move keyboard focus, Enter activates an option, and Escape returns or resumes. In creation/rename fields, type text, use Backspace to erase, and Ctrl+A to replace the field. The world list supports mouse-wheel scrolling. The built-in pixel font displays basic Latin characters and punctuation; other characters are preserved in saves but may appear as question marks.

Select a world, then choose **Rename World** to edit its display name. Saving the name uses the same atomic replacement as gameplay saves, retaining block bytes, seed, size, player pose, and the GUID directory. **Delete World** opens a separate confirmation naming the world and explaining that deletion cannot be undone. Cancel has initial keyboard focus; only **Delete Permanently** removes the selected directory. Cancel or Escape returns to the list. The list refreshes after either operation. A loaded world's ID is protected from rename/deletion. Filesystem errors remain visible in the menu.

**Open Saves Folder** creates and opens the save manager's actual root directory in File Explorer on Windows, `open` on macOS, or `xdg-open` on Linux. Unsupported platforms or launch failures produce a menu error.

Escape during gameplay opens a pause menu with **Resume**, **Save World**, and **Save and Main Menu**. Movement and block edits stop while paused. Saving displays confirmation; save-and-return releases the world's GPU buffers after saving succeeds. Autosave runs every **five minutes of active gameplay**. F10, gamepad Back, and closing the window also attempt a save. If saving fails, the world stays open and the pause menu shows an error; quitting is canceled so saving can be retried. Losing focus opens the pause menu. Resuming consumes the activating click, preventing an accidental block break.

Production saves use the per-user `Environment.SpecialFolder.LocalApplicationData` directory, with a home-directory `.local/share` fallback where needed. On Windows the location is:

```text
%LOCALAPPDATA%/MDSM/Worlds/
  <32-character world GUID>/
    world.dat
```

Each world has an independent generated directory ID. Display names are metadata, never paths; blank names, control characters, path separators, and invalid path characters are rejected. Names can contain up to 48 UTF-16 characters. Two worlds may share a display name. Saves are kept outside the source repository and build output. Management validates GUIDs and direct-child paths; symbolic links/junctions in save paths are rejected, and deletion checks descendants without following links. Directories are never renamed or migrated. No duplication or import/export UI is implemented.

### Binary format, version 1

[WorldSerializer.cs](./WorldSerializer.cs) uses little-endian fields and no compression. Each save contains 1, 4, or 16 MiB of block data, depending on size, plus its small header and checksum:

| Field | Representation |
| --- | --- |
| Signature | Four ASCII bytes: `MDSM` |
| Format version | 32-bit integer, currently 1 |
| Dimensions | Three 32-bit integers: X/Z=128, 256, or 512; Y=64 |
| World seed | Signed 32-bit integer |
| World name | 32-bit UTF-8 byte length, then UTF-8 bytes |
| Player feet position | X/Y/Z, three 32-bit floats |
| Camera orientation | Yaw/pitch, two 32-bit floats in radians |
| Block byte count | 32-bit integer, exactly X * Y * Z |
| Block data | One byte per block, all chunks in chunk-Z/chunk-X order; within each chunk X varies fastest, then Z, then Y |
| Checksum | SHA-256 of all preceding bytes, 32 bytes |

Loading validates the signature, version, one of the three supported dimensions, name, finite player pose within that size, block IDs, byte count, exact file length, and checksum. Payload allocation follows validated dimensions rather than trusting arbitrary file lengths. Stored blocks are copied directly into empty chunk storage; terrain is never regenerated over saved edits. Chunk meshes are rebuilt from the restored blocks. Unknown versions, missing files, and corrupt data produce a menu error. Listing obtains size from the small header without deserializing block arrays; full checksum validation happens on load.

This update retains **format version 1**: dimensions were already present in the Milestone 3/4 header, so no layout change or migration is needed. All those older worlds explicitly contain 256 x 64 x 256 dimensions and display as Normal, preserving their blocks, metadata, player pose, old canopies, and original bottom layers. Loading does not depend on the most recent creation-menu selection. Block IDs are unchanged. Older game versions reject Small/Large saves; Milestone 3 also cannot read the expanded Milestone 4 block IDs.

[WorldSaveManager.cs](./WorldSaveManager.cs) writes `world.dat.<random GUID>.tmp` in the same world directory, flushes it to disk, then atomically replaces `world.dat` (or renames it for the first save). Failed writes leave the previous valid save intact. Temporary files left by an interrupted process are ignored. No background save thread is used: save/load and generation can briefly pause the UI. Player velocity, hotbar selection, and F3 visibility are session state; position and orientation are persisted.

## Editing block textures

Open [Content/terrain.png](./Content/terrain.png) in an ordinary image editor. Keep the image at **256 x 256 pixels**, arranged as a **16 x 16 grid of 16 x 16 pixel tiles**. Tiles are numbered from 0 to 255 in row-major order: left to right, then top to bottom. Tile 16 begins the second row, and tile 255 is at the bottom right.

| Tile index | Texture | Pixel origin (x, y) |
| --- | --- | --- |
| 0 | Grass top | (0, 0) |
| 1 | Grass side | (16, 0) |
| 2 | Dirt / grass bottom | (32, 0) |
| 3 | Stone | (48, 0) |
| 4 | Log bark | (64, 0) |
| 5 | Log end grain | (80, 0) |
| 6 | Leaves, with transparent holes | (96, 0) |
| 7 | Wooden planks | (112, 0) |
| 8 | Cobblestone | (128, 0) |
| 9 | Sand | (144, 0) |
| 10 | Gravel | (160, 0) |
| 11 | Bedrock | (176, 0) |
| 12 | Tall grass | (192, 0) |
| 13 | Red flower | (208, 0) |
| 14 | Yellow flower | (224, 0) |
| 15-255 | Unused, transparent | `(index % 16 * 16, index / 16 * 16)` using integer division |

The first four tiles were exported once from the original seed-173 algorithm and remain pixel-for-pixel unchanged. New artwork occupies tiles 4-14; its generation prompt and one-time preparation are recorded in [Content/texture-artwork.md](./Content/texture-artwork.md). The PNG is the source artwork; builds and game launches never regenerate or overwrite it. Edit a tile, save the PNG, then run `dotnet build` followed by `dotnet run --no-build` (or simply `dotnet run`, which builds first). Restarting an existing build without rebuilding will continue to use its previously compiled texture. Replacing existing textures requires no C# changes.

For a future material, draw its texture in an unused slot and assign that index in the centralized [block definitions](./Blocks.cs). Each definition owns its name, collision solidity, opacity, mesh shape, placement eligibility, and side/top/bottom tiles. Grass retains its existing face mappings; logs use end grain on top/bottom and bark on their sides. Both world rendering and the hotbar use point sampling, with mipmaps disabled and world UVs inset to texel centers to prevent tile bleeding. For leaves and plants, use alpha 0 for holes/backgrounds and alpha 255 for visible pixels; intermediate alpha is treated as a binary cutout rather than translucency.

## Block identifiers and testing palette

These numeric IDs are persistent save data and must not be reordered or reused:

| ID | Block | Atlas tiles | Collision / opacity |
| --- | --- | --- | --- |
| 0 | Air | None | Non-solid / invisible |
| 1 | Grass | 0 top, 1 sides, 2 bottom | Solid / opaque |
| 2 | Dirt | 2 | Solid / opaque |
| 3 | Stone | 3 | Solid / opaque |
| 4 | Wood log | 4 sides, 5 ends | Solid / opaque |
| 5 | Leaves | 6 | Solid / cutout |
| 6 | Wooden planks | 7 | Solid / opaque |
| 7 | Cobblestone | 8 | Solid / opaque |
| 8 | Sand | 9 | Solid / opaque |
| 9 | Gravel | 10 | Solid / opaque |
| 10 | Bedrock | 11 | Solid / opaque |
| 11 | Tall grass | 12 | Non-solid / cutout cross |
| 12 | Red flower | 13 | Non-solid / cutout cross |
| 13 | Yellow flower | 14 | Non-solid / cutout cross |

The nine-slot hotbar has two pages, switched with **Tab** during gameplay. Page 1 contains grass, dirt, stone, logs, leaves, planks, cobblestone, sand, and gravel. Page 2 contains bedrock, tall grass, and both flowers; unused slots are disabled. **1-9** selects a slot on the current page. All non-air blocks are placeable and breakable with unlimited quantities. Plants require grass directly below them and disappear when their supporting grass is removed. Leaves retain solid collision. Sand and gravel do not fall, and bedrock has no mining restriction. Planks, cobblestone, sand, and gravel remain available for placement without generating naturally. Only bedrock is newly added to base terrain, at Y=0.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move forward / left / backward / right |
| Mouse | Look around |
| Space | Jump while on the ground |
| Left click | Break the outlined block |
| Right click | Place the selected block against the outlined face |
| 1-9 (or numpad) | Select a slot on the current hotbar page |
| Tab during gameplay | Switch hotbar page |
| F3 | Toggle player coordinates, chunk coordinates, approximate FPS, seed, chunk count, and dimensions |
| Escape | Open the pause menu / resume from pause |
| Pause menu Resume | Resume and capture the mouse |
| F10 or gamepad Back | Save the active world and quit |

Block interaction reaches six blocks from the camera. Each click performs one edit. Placement requires an empty cell inside the world; solid blocks cannot overlap the player's body. Raycasts target a plant's voxel cell, rather than individual visible texels or crossed planes. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams.

The hotbar provides unlimited blocks. Block edits persist when saved. Infinite terrain, crafting, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | Lifecycle, state transitions, save/autosave/exit orchestration, input, and renderer lifetime |
| [GameState.cs](./GameState.cs) | Main menu, creation, selection, rename, deletion confirmation, gameplay, and pause states |
| [MenuController.cs](./MenuController.cs) | Atlas dirt background, title, size selector, world management layouts, navigation, and feedback |
| [WorldSave.cs](./WorldSave.cs) | Saved metadata and active world/player/camera session |
| [WorldSerializer.cs](./WorldSerializer.cs) | Versioned binary format, validation, and checksums |
| [WorldSaveManager.cs](./WorldSaveManager.cs) | Per-user world directories, header listing, safe rename/deletion, folder opening, and atomic replacement |
| [Camera.cs](./Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Player.cs) | Player bounds, movement, gravity, jump, collision, and respawn |
| [Blocks.cs](./Blocks.cs) | Stable IDs, centralized block properties, shapes, and face textures |
| [BlockPalette.cs](./BlockPalette.cs) | Nine-slot paged testing selection |
| [World.cs](./World.cs) | Finite world, chunk lookup/invalidation, safe reads/edits, placement, and collision queries |
| [WorldSize.cs](./WorldSize.cs) | Supported per-world size options and dimension validation |
| [Chunk.cs](./Chunk.cs) | Compact local block storage, world bounds, and mesh revision |
| [TerrainGenerator.cs](./TerrainGenerator.cs) | Unchanged seeded height generation, grass/dirt/stone layering, and a single bedrock layer |
| [WorldDecorator.cs](./WorldDecorator.cs) | Deterministic trees and plants after base terrain generation |
| [BlockRaycaster.cs](./BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./TextureAtlas.cs) | ContentManager atlas loading, grid tile rectangles, and texel-center UVs |
| [Content/terrain.png](./Content/terrain.png) | Editable 256 x 256 block texture atlas |
| [ChunkMeshBuilder.cs](./ChunkMeshBuilder.cs) | Cube and crossed-plant geometry with cross-chunk opacity checks |
| [ChunkMesh.cs](./ChunkMesh.cs) | Per-chunk GPU buffer, revision tracking, uploads, and disposal |
| [WorldRenderer.cs](./WorldRenderer.cs) | Chunk frustum culling, alpha testing, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, save confirmation, and cached F3 debug text |
| [PixelFont.cs](./PixelFont.cs) | Shared bitmap lettering for the HUD and menus |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |
| [PersistenceChecks.cs](./PersistenceChecks.cs) | Edited-world restoration, corruption validation, safe paths, and failed-save checks |
| [DecorationChecks.cs](./DecorationChecks.cs) | Block definitions, deterministic decorations, bounds, cutout geometry, interactions, and legacy/new save restoration |
| [WorldManagementChecks.cs](./WorldManagementChecks.cs) | All-size generation, indexing, bounds, canopies, restoration, header-only metadata, and safe management |

All chunks remain loaded for the active world. There is no chunk streaming, multithreaded generation, water, ores, crafting, drops, tree growth, or vegetation growth.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The 31 check groups cover deterministic terrain/decorations for all sizes, one-byte storage and indexing, chunk invalidation/seams/frustum bounds, the single bedrock layer, maximum canopy extent and variation, spawn clearance, cutout mesh geometry, collision, targeting plants, both palette pages, original gameplay, atlas UVs, heavily edited world restoration, every block ID, independent legacy-v1 restoration, exact player pose, save checksums/headers/lengths, safe names/paths, missing/corrupt saves, atomic replacement failures, persistent rename/deletion, and active-world protection. A bounded non-seekable stream verifies that metadata reading stops before block data. Controlled fixtures isolate physics and file tests. Persistence test directories are temporary fixtures under the test build and are cleaned up; they never touch the user's real saves. A failing check returns a nonzero exit code. These checks do not open a window and cannot verify GPU output or operating-system input behavior.

For a manual playtest, create each size with explicit and random seeds; inspect tree spacing, canopy variation, leaf holes from above/below, and plants from both sides. Walk through plants and collide with leaves. Select and place blocks across chunk seams, then save and reopen worlds in a different size order. Check dimensions/F3, blocks, position, orientation, and older worlds without generation changes. Rename a disposable world, cancel deletion, then confirm deletion and verify other worlds remain intact. Test Open Saves Folder, mouse/keyboard navigation, scrolling, text entry, resizing/focus, corrupt-save errors, five-minute autosave, and sustained Large-world rendering performance. Generation, mesh construction, and saving remain synchronous, so Large worlds can cause a longer initial pause.

