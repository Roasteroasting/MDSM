# MDSM

A small Minecraft Indev-inspired voxel sandbox in C# and MonoGame DesktopGL. The game has finite worlds in three sizes, seeded terrain with trees, vegetation and small lakes, flowing translucent water and swimming, chunk-based rendering, timed mining, collectible item stacks, a persistent 36-slot inventory, player health with fall damage/death/respawning, passive pigs/cows/sheep/chickens, punching, White Wool and resource drops, and raw porkchops for healing.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting atlas texture is copied into the build output and owned by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Settings and frame rate

Choose **Settings** from the main menu or the pause menu. Both entry points share the same preferences. **Back** or Escape returns to the screen that opened Settings; returning to the pause menu keeps gameplay suspended and the mouse cursor available.

Settings also contains **Controls** and **About**. Controls lists the implemented gameplay, inventory, and menu bindings; it does not change them. About identifies MDSM (Mojang Don't Sue Me), its author Mark Arnold (Roasteroasting), and explicitly acknowledges that the codebase was written using GPT-6.1 Sol and the game textures were AI-generated. Each screen's Back button or Escape returns to Settings, preserving its main-menu or pause-menu origin and all FPS preferences. Text wraps with the window; use the mouse wheel, Up/Down, Page Up/Page Down, or Home/End to scroll longer information screens.

The **FPS Cap** slider supports every whole number from **5 to 360**, defaulting to **60**. Click or drag it, or focus it with Tab/Up/Down and use Left/Right for one-FPS changes; Home/End select the endpoints. The current numerical value is shown above the slider. **Uncapped FPS** defaults off and can be clicked or toggled with Space/Enter when focused. Changes apply immediately in gameplay and every menu. While uncapped, the slider remains editable and retains its value, but does not limit rendering until uncapped is turned off.

Preferences are application-wide in `%LOCALAPPDATA%/MDSM/settings.json` on Windows, alongside the existing `Worlds` directory. The equivalent per-user application-data location is used elsewhere. They are independent of world saves and survive world switches and application restarts. Missing settings use defaults; malformed/oversized files fall back safely, and out-of-range integers are clamped. Changes save after a short idle delay and on Back/exit using a temporary file and atomic replacement. Write failures keep the selected preferences active for the current session and display an error without replacing the previous valid file.

[FrameTiming.cs](./Core/FrameTiming.cs) separates rendering from a **120 Hz fixed simulation clock**. The render limiter uses stopwatch deadlines and sleeping, compensating for small timer overshoots without busy-waiting and rebasing after slow frames. Uncapped mode skips the limiter. MonoGame's outer fixed-step timing and presentation synchronization are disabled so they do not impose the former 60 FPS ceiling; its inactive sleep is also disabled so the same preference applies throughout menus and focus changes. See [MonoGame's timing loop](https://github.com/MonoGame/MonoGame/blob/v3.8.6/MonoGame.Framework/Game.cs) for the outer-loop behavior. There is no additional VSync setting.

The simulation consumes elapsed time in fixed steps, including multiple steps per rendered frame at low FPS. Player physics, fall tracking, mining, item motion, pickup delays, and merging therefore use the same simulation rate at every render cap. Autosave remains based on active elapsed time. Jump presses are retained until the next simulation step; mouse look uses mouse displacement once per rendered frame. Menus clear fractional simulation time without resetting an in-progress fall. MonoGame's existing half-second maximum elapsed-time safeguard remains in place for exceptional stalls. A chosen cap is a rendering target/upper limit, not a promise of hardware performance; operating-system scheduling, drivers, and world complexity affect achieved FPS.

## Screenshots and HUD visibility

Press **F2** once to save a lossless PNG of the final composed frame, including whichever world, held item, HUD, debug overlay, inventory, or menu is visible. Holding F2 does not repeat captures. Screenshots use the actual rendered client-area resolution, without scaling, cropping, filtering, or color changes. Capture runs after all draw passes, before presentation, using MonoGame's backbuffer readback and PNG encoder. It does not reconstruct the scene or temporarily enable hidden HUD elements.

Screenshots are stored in **`%LOCALAPPDATA%/MDSM/screenshots/`** on Windows, alongside `Worlds` and `settings.json`; the equivalent per-user application-data location is used elsewhere. The directory is created on the first capture. Filenames use `yyyy-MM-dd_HH-mm-ss_fff.png`, with numbered suffixes for collisions and exclusive file creation to prevent overwriting. Write failures display an error using the existing menu/gameplay notification and leave gameplay running; an incomplete file from a failed encoding is removed. GPU readback and PNG writing are synchronous only when requested, so taking a screenshot can briefly pause rendering at large resolutions.

Press **F1** to hide or restore the gameplay HUD: hotbar, selected-item name, hearts, crosshair, targeting/mining feedback, held-item model, notifications, and debug overlay. This changes rendering only; movement, mining, placement, and hotbar selection remain active. Inventory and menu interfaces remain usable and visible. HUD visibility resets to visible on application launch and is not saved.

**F3** retains its independent debug toggle. Hiding the HUD preserves the F3 state; restoring it reveals debug again if enabled. F3 can also change that state while the HUD is hidden. The former permanent upper-left controls panel is removed; bindings are available in **Settings → Controls** instead.

## Finite world and seed

Each world keeps its own size, selected during creation. **Normal** is the default. Click the size field to cycle options, or focus it with Tab and use Left/Right or Enter. All worlds remain entirely in memory. Chunks are always 16 x 64 x 16 blocks, with one byte per block; metadata and render buffers require additional memory.

| Size | Dimensions (X x Y x Z) | Chunk grid | Total chunks | Raw block bytes |
| --- | --- | --- | --- | --- |
| Small | 128 x 64 x 128 | 8 x 8 | 64 | 1,048,576 (1 MiB) |
| Normal | 256 x 64 x 256 | 16 x 16 | 256 | 4,194,304 (4 MiB) |
| Large | 512 x 64 x 512 | 32 x 32 | 1,024 | 16,777,216 (16 MiB) |

Valid X/Z coordinates run from zero through the selected width/depth minus one. Y remains 0-63 for every size. Storage, player bounds, spawn, decorations, rendering, and F3 dimensions/chunk counts use the active world's size, rather than a global selection.

Choose **Create New World** from the main menu, enter a name, and optionally enter a signed 32-bit integer seed. Leaving the seed blank chooses a random seed. The same seed and size produce the same complete world. The code-level `World` constructor defaults to seed 173 and Normal size; the creation menu passes its selections explicitly. [TerrainGenerator.cs](./Worlds/TerrainGenerator.cs) combines coherent 2D gradient noise at several scales using global coordinates, so hills and valleys continue across chunk boundaries. Surfaces stay within Y 16-44, with grass on top, 3-5 dirt blocks below, and stone underneath. The height-generation algorithm remains unchanged from Milestone 2. New worlds replace stone at Y=0 with exactly one complete bedrock layer, without adding bedrock above it.

The creation menu uses `World.CreateNew`: unchanged base terrain, local lake basins, then [WorldDecorator.cs](./Worlds/WorldDecorator.cs) for oak-style trees and sparse tall grass and red/yellow flowers. The original constructor remains available for base-terrain/decorated fixtures; loading restores saved bytes without running generation. Seeded world-coordinate hashes and slowly varying density noise create open areas and wooded patches, with density scaling across the selected area. Trunks vary from 4 to 6 blocks high, with layered canopies and varying top shapes. Leaves extend at most two blocks from the trunk on either horizontal axis, within a 5 x 5 footprint. Candidates require grass, gentle slopes, sufficient clearance, and spacing between trunks. Canopies can cross chunk boundaries but never world boundaries. Decorations are written before meshes are built, without repeated mesh invalidation. Existing saves keep their original trees and bottom layer; generation corrections apply only to new worlds.

The player spawns above the surface near the center, with a clear area around the initial spawn. Horizontal player bounds stop at the finite edges, where terrain ends visibly. Block reads outside the world return air; edits outside it are rejected. Space above Y=63 remains open for movement, with no blocks stored above the limit. Existing void recovery below Y=-16 returns the player above the current center surface, resetting movement/fall tracking while preserving health; it is not an additional damage or healing source.

Terrain generation and initial mesh creation run synchronously on the main thread, so startup includes a short initial build. Each chunk has its own reusable GPU vertex buffer. Opaque neighbors hide cube faces across chunk boundaries; faces needed behind transparent leaf pixels remain visible. Plants use two crossed, double-sided quads in the same chunk buffers. Alpha testing discards pixels with alpha below 128 while remaining pixels write depth normally. Water uses a separate translucent pass described below. Chunk bounds are tested against the camera frustum before submitting draws. A solid edit rebuilds its own terrain mesh and, at a chunk edge, the face-sharing neighbors (at most three terrain meshes for a single block edit). Water corners additionally invalidate diagonal water meshes at chunk corners. Mesh scratch storage and GPU capacity are reused, and all graphics operations stay on the main thread.

## Water and natural lakes

New worlds contain restrained, isolated freshwater lakes. [LakeGenerator.cs](./Worlds/LakeGenerator.cs) uses a seed-separated deterministic sequence, irregular stretched outlines, and gently sloped sites. Basins are one to three blocks deep, with gravel/dirt beds, occasional sand, and patchy sand/gravel/dirt/grass shores. Small/Normal/Large worlds allow up to 2/4/12 lakes with maximum horizontal radii 6/7/10 blocks; most are small. Lakes stay away from finite edges and a protected area around the center spawn. Trees and vegetation run afterward using their existing rules, so plants do not appear underwater. No lakes are added to loaded worlds.

Water is non-solid, cannot be mined or held in inventory, and does not obstruct targeting blocks or mobs behind it. Place a solid block into water to displace it. Digging into a bank or lake bed opens a path for water. Sources continuously supply downward flow first, and supported water spreads horizontally with levels 1-7, losing one level per horizontal step. Falling columns reach their floor before spreading sideways. Water routes around obstacles within that reach, never travels upward, and flowing water recedes when no valid supply remains. Two adjacent horizontal sources can regenerate a source only over solid support or another source; ordinary spreading never creates sources on its own. Original lake basins are sealed and source-filled, so they remain stable until edited.

[FluidSimulation.cs](./Worlds/FluidSimulation.cs) maintains a deterministic, deduplicated frontier with a **0.2-second water tick** and a **2,048-cell budget per tick**. Decisions use a snapshot and apply together, preventing update order from accelerating flow. Creation/loading scans water-bearing chunks once to schedule active flow and exposed frontiers; stable lakes then do no work. The queue uses one bit per block only when needed. Updates pause with gameplay. Fluid-only changes rebuild affected water geometry, without rebuilding opaque terrain or scanning the entire world each frame.

[WaterRenderer.cs](./Rendering/WaterRenderer.cs) caches water faces per chunk, frustum-culls chunks, sorts visible faces back-to-front, and batches them into a reusable GPU buffer. Shared corner heights join differing flow levels across chunk boundaries; sources are substantially full, weak flows are shallower, and falling columns join vertically. Internal water faces and faces against opaque terrain are omitted. The pass runs after opaque terrain/items/mobs and before held items/HUD, with point sampling, alpha blending, depth testing and **no depth writes**. It restores texture bindings and graphics states, retaining the screenshot binding fix. [Content/water.png](./Content/water.png) is a separate editable 16 x 16 tile; existing terrain artwork is unchanged. Edit, rebuild and restart to change it. Its built-in image-generation prompt and one-time preparation are recorded in [Content/water-artwork.md](./Content/water-artwork.md).

Entering water reduces horizontal movement and falling speed and cancels accumulated fall damage. **Hold Space to swim upward**; normal jumping outside water is unchanged. Passive mobs slow down, float, and can hop against a shoreline while wandering to avoid being trapped. There are no water currents, drowning, oxygen, underwater effects, buckets, rivers, or oceans.

## Worlds and saving

MDSM starts at the main menu, with a large centered title and a tiled dirt background sampled directly from the editable atlas. The background repeats at 64 screen pixels per tile using point sampling and adapts to the window size. Other menu screens share this background; pause and death retain the darkened gameplay view.

**Create New World** generates and immediately saves a new world. **Select World** lists existing world names, sizes, seeds, and local last-modified times. Mouse clicks select buttons and list rows. Up/Down and Tab move keyboard focus, Enter activates an option, and Escape returns or resumes. In creation/rename fields, type text, use Backspace to erase, and Ctrl+A to replace the field. The world list supports mouse-wheel scrolling. The built-in pixel font displays basic Latin characters and punctuation; other characters are preserved in saves but may appear as question marks.

Select a world, then choose **Rename World** to edit its display name. Saving the name uses the same atomic replacement as gameplay saves, retaining block bytes, seed, size, player pose, health, inventory, dropped items, and the GUID directory. **Delete World** opens a separate confirmation naming the world and explaining that deletion cannot be undone. Cancel has initial keyboard focus; only **Delete Permanently** removes the selected directory. Cancel or Escape returns to the list. The list refreshes after either operation. A loaded world's ID is protected from rename/deletion. Filesystem errors remain visible in the menu.

**Open Saves Folder** creates and opens the save manager's actual root directory in File Explorer on Windows, `open` on macOS, or `xdg-open` on Linux. Unsupported platforms or launch failures produce a menu error.

Escape during gameplay opens a pause menu with **Resume**, **Save World**, and **Save and Main Menu**. Movement, mining, item physics/pickup, and block placement stop while paused or in the inventory. Saving displays confirmation; save-and-return releases the world's GPU buffers after saving succeeds. Autosave runs every **five minutes of active gameplay**. F10, gamepad Back, and closing the window also attempt a save. Saving first returns any cursor-held stack; if there is insufficient room, closing/exiting is canceled and the inventory remains open until the stack is placed. If a write fails, the world stays open and the pause menu shows an error. Losing focus pauses gameplay; an already-open inventory stays suspended. Resuming consumes held mouse buttons until released, preventing an accidental mining or placement action.

Production saves use the per-user `Environment.SpecialFolder.LocalApplicationData` directory, with a home-directory `.local/share` fallback where needed. On Windows the location is:

```text
%LOCALAPPDATA%/MDSM/Worlds/
  <32-character world GUID>/
    world.dat
```

Each world has an independent generated directory ID. Display names are metadata, never paths; blank names, control characters, path separators, and invalid path characters are rejected. Names can contain up to 48 UTF-16 characters. Two worlds may share a display name. Saves are kept outside the source repository and build output. Management validates GUIDs and direct-child paths; symbolic links/junctions in save paths are rejected, and deletion checks descendants without following links. Directories are never renamed or migrated. No duplication or import/export UI is implemented.

### Binary format, version 7

[WorldSerializer.cs](./Persistence/WorldSerializer.cs) uses little-endian fields and no compression. Each save contains 1, 4, or 16 MiB of block data, depending on size, plus its header, inventory, dropped items, and checksum:

| Field | Representation |
| --- | --- |
| Signature | Four ASCII bytes: `MDSM` |
| Format version | 32-bit integer, currently 7; versions 1-6 remain readable |
| Dimensions | Three 32-bit integers: X/Z=128, 256, or 512; Y=64 |
| World seed | Signed 32-bit integer |
| World name | 32-bit UTF-8 byte length, then UTF-8 bytes |
| Player feet position | X/Y/Z, three 32-bit floats |
| Camera orientation | Yaw/pitch, two 32-bit floats in radians |
| Block byte count | 32-bit integer, exactly X * Y * Z |
| Block data | One byte per block, all chunks in chunk-Z/chunk-X order; within each chunk X varies fastest, then Z, then Y |
| Selected hotbar slot | 32-bit integer, 0-8 |
| Inventory slot count | 32-bit integer, exactly 36 |
| Inventory slots | 36 entries in hotbar-first order, each a 16-bit unsigned item ID and 8-bit quantity; empty is ID 0 / quantity 0, otherwise quantity 1-64 |
| Dropped-item count | 32-bit integer, 0-1,000,000 |
| Dropped items | Each entry: 16-bit item ID, 8-bit quantity, three 32-bit position floats, 32-bit vertical-velocity float, 32-bit remaining pickup-delay float (23 bytes per entity) |
| Player health | 32-bit integer, 0-20; added in version 3 |
| Passive mob count | 32-bit integer, 0-256 combined across species |
| Passive mobs | Each entry: 8-bit species (0 pig, 1 cow, 2 sheep, 3 chicken), three 32-bit feet-position floats, 32-bit yaw float, 32-bit health integer; 21 bytes per mob |
| Population targets | Four 32-bit counts in pig/cow/sheep/chicken order; living plus pending must equal each target |
| Pending respawn count | 32-bit integer, 0-256 |
| Pending respawns | Each entry: 8-bit species and 64-bit remaining gameplay seconds, finite and between 0 and 60 (9 bytes) |
| Pending gravity column count | 32-bit integer, 0 to X * Z |
| Pending gravity columns | Unique 32-bit column indices, Z * width + X, in processing order |
| Active falling-block count | 32-bit integer, 0-4096; added in version 7 |
| Active falling blocks | Each entry: 8-bit block ID (Sand/Gravel only), 32-bit X and Z, 64-bit feet Y, 64-bit downward speed (25 bytes); source voxel is already empty |
| Checksum | SHA-256 of all preceding bytes, 32 bytes |

Loading validates the signature, version, supported dimensions, name, finite player pose, health range, block/item IDs, slot and entity counts, stack quantities, finite bounded item positions/velocities/delays, exact length, and checksum. Pickup delays must be between zero and one second. Allocation follows validated dimensions and bounded entity counts rather than arbitrary file lengths. Stored blocks are copied directly into empty chunk storage; terrain is never regenerated over saved edits. Chunk meshes are rebuilt from restored blocks. Unknown versions, missing files, and corrupt data produce a menu error. Listing obtains size from the unchanged small header without deserializing block or inventory arrays; full checksum validation happens on load.

Milestone 7 writes **format version 4**, appending a pig count and pig records after the existing version-3 health integer and before the checksum. Versions 1-3 remain readable. Versions 1 and 2 default to 20 health; versions 2 and 3 retain their exact inventory and dropped items without a stock refill. Version 1 has the same header and raw block layout, followed immediately by its checksum, and receives the existing 13-block starter inventory on first load. **Older worlds receive no pigs and no starting porkchops.** An empty pig list is saved on upgrade, so later loads never silently repopulate them. Milestone 3/4 saves display as Normal; later Small/Large saves retain their own dimensions. All older terrain, seed, names, player pose, canopies, and bottom layers are preserved. Block IDs, GUID directories, atomic replacement, and world generation are unchanged. Earlier game builds cannot read version-4 saves.

[WorldSaveManager.cs](./Persistence/WorldSaveManager.cs) writes `world.dat.<random GUID>.tmp` in the same world directory, flushes it to disk, then atomically replaces `world.dat` (or renames it for the first save). Failed writes leave the previous valid save intact. Temporary files left by an interrupted process are ignored. No background save thread is used: save/load and generation can briefly pause the UI. Player position/orientation, health, all 36 inventory slots, selected hotbar slot, and uncollected items including remaining pickup delays persist per world. Partial mining progress, player velocity/fall tracking, F3 visibility, and cursor UI state are not serialized; cursor items must return safely before saving. Loading a save with zero health respawns the player with full health and its saved empty inventory, retaining the already-saved death drops without creating them again.

Milestone 8 keeps **version 4 and the exact binary layout**. New block bytes 14-22 encode all water states directly; no extra fluid array/tail is required. Loading schedules the restored fluid frontier once and resumes simulation without creating lakes or sources through generation. Existing v1-v4 saves retain terrain and all player/entity data. Water-free saves retain their block bytes exactly. Earlier game builds do not recognize water IDs and cannot load worlds containing them. Queue order, fractional tick time and temporary mesh data are not saved; the saved fluid configuration settles to its valid equilibrium after loading.

Milestone 9 writes **version 5**, adding a species byte to each former pig record and using one combined mob count. Version-4 records remain 20-byte pig records and load as pigs without changing pose or health. Versions 1-3 still load with no mobs. Loading any older world never adds cows/sheep/chickens or replenishes inventory, including White Wool: existing raw porkchops and all other slots remain intact. Existing water bytes, terrain, player health/pose, dropped items and metadata retain their layouts. Species IDs and species-specific health/bounds are validated; the combined count is limited to 256. Older builds cannot read version-5 saves. GUID paths, atomic replacement, and storage locations are unchanged.

The population/gravity update writes **version 6**, appending per-species targets, independent remaining respawn delays, and pending gravity columns after the unchanged version-5 mob records. Saves made before this update remain readable: each species' surviving count becomes its initial target once, with no pending replacements inferred for historical deaths. Thus pig-only worlds stay pig-only, and empty older worlds stay empty. Saving persists that baseline for future deaths; loading never recalculates a version-6 target from survivors alone. Pending timers resume without advancing while the game is closed or paused. Legacy terrain is restored exactly without activating a global gravity scan; later support edits enable gravity in their affected columns. New queued gravity edits survive saving before settlement. The old header-only metadata reader, GUID directories, checksum, and temporary-file/atomic replacement strategy remain intact. Earlier builds cannot read version-6 saves.

Smooth falling writes **version 7**, appending active Sand/Gravel types, columns, continuous heights, and velocities after the version-6 gravity queue. Loading resumes those objects without recreating their source blocks, changing their types, settling them prematurely, or creating item drops. The reader rejects invalid counts, block types, non-finite/out-of-range values, overlapping falling objects, and intersections with stored solid terrain. Version-6 queues now launch animated falls; versions 1-5 retain the conservative unchanged-terrain policy above. All existing world/player/mob/inventory/water fields and paths remain intact, and atomic replacement is unchanged. Earlier game builds cannot read version-7 saves.

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
| 15 | White Wool | (240, 0) |
| 16-255 | Unused, transparent | `(index % 16 * 16, index / 16 * 16)` using integer division |

The first four tiles were exported once from the original seed-173 algorithm and remain pixel-for-pixel unchanged. Milestone 4 artwork occupies tiles 4-14; its generation prompt and one-time preparation are recorded in [Content/texture-artwork.md](./Content/texture-artwork.md). White Wool occupies tile 15, documented in [Content/passive-mob-artwork.md](./Content/passive-mob-artwork.md); all other existing atlas pixels are unchanged. The PNG is the source artwork; builds and game launches never regenerate or overwrite it. Edit a tile, save the PNG, then run `dotnet build` followed by `dotnet run --no-build` (or simply `dotnet run`, which builds first). Restarting an existing build without rebuilding will continue to use its previously compiled texture. Replacing existing textures requires no C# changes.

For a future material, draw its texture in an unused slot and assign that index in the centralized [block definitions](./Worlds/Blocks.cs). Each definition owns its name, collision solidity, opacity, mesh shape, placement eligibility, and side/top/bottom tiles. Grass retains its existing face mappings; logs use end grain on top/bottom and bark on their sides. Both world rendering and the hotbar use point sampling, with mipmaps disabled and world UVs inset to texel centers to prevent tile bleeding. For leaves and plants, use alpha 0 for holes/backgrounds and alpha 255 for visible pixels; intermediate alpha is treated as a binary cutout rather than translucency.

## Block identifiers

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
| 14 | Water source | Separate water tile | Non-solid / translucent, not an item |
| 15-21 | Flowing water levels 1-7 | Separate water tile | Non-solid / translucent, not items |
| 22 | Falling water | Separate water tile | Non-solid / translucent, not an item |
| 23 | White Wool | 15 | Solid / opaque, mineable and placeable |

Blocks 1-13 and 23 have corresponding items and remain placeable when held in the hotbar. Water states are not inventory items. Bedrock is unbreakable through mining. Plants require grass directly below them; mining their grass support also drops the removed plant. Leaves retain solid collision. Sand and gravel generate locally in lake beds/shorelines and now fall when unsupported. Planks and cobblestone remain available as items without generating naturally. White Wool does not generate as terrain; sheep drop it. It uses 1.25-second placeholder mining and drops itself. Bedrock generates only at Y=0. No block textures or generation rules changed in Milestones 5 or 6.

[FallingBlockSimulation.cs](./Worlds/FallingBlockSimulation.cs) queues only changed horizontal columns, deduplicates them, and checks at most 32 columns every 0.05 gameplay seconds. Each bounded bottom-up pass removes unsupported Sand/Gravel from storage and transfers it into a [FallingBlock](./Worlds/FallingBlock.cs) with continuous height and downward velocity. Blocks accelerate at 24 blocks/second squared, capped at 20 blocks/second. Swept collision finds the first solid surface even during a large elapsed-time step; falling stacks also support each other without passing through or changing order. Landing restores exactly one ordinary block of the same type, without creating item drops. Up to 4096 falling objects can be active; excess unsupported blocks stay queued in storage until capacity becomes available.

[FallingBlockRenderer.cs](./Rendering/FallingBlockRenderer.cs) translates cached full-size Sand/Gravel cube geometry into a reusable batch, using the existing atlas, face shading, point sampling, depth testing and fog. Render interpolation between the 120 Hz simulation steps keeps high-FPS movement smooth. Only launch/landing changes terrain geometry; animation does not rebuild chunk meshes. The pass runs before translucent water and restores graphics/texture state, including DesktopGL binding-cache invalidation. Menus freeze physics and the interpolated view. Saving and loading retains active motion; no new textures are needed.

Water does not support falling material: intermediate water cells remain intact and only the final destination displaces water, notifying the existing fluid queue. Placement into a moving cube is rejected without consuming an inventory item; placement below it can create new support. The finite Y=0 plane catches blocks if a fixture or edit removes bottom support. Generated Sand/Gravel still requires solid support at placement; lake shapes and shore materials remain unchanged, with no initial falling cascade or global activation of old terrain.

## Mining and dropped items

Hold **LMB** on a block within six blocks to mine it. An orange outline and a progress bar below the crosshair indicate progress, without a numerical percentage. Releasing LMB, changing targets or block types, losing reach, or opening a menu discards progress immediately. Holding LMB continues onto the next target after a break. Mining uses elapsed time and these centralized placeholder durations in [Blocks.cs](./Worlds/Blocks.cs):

| Block | Seconds |
| --- | --- |
| Tall grass, red/yellow flowers | Instant |
| Grass block, dirt | 1.25 |
| Stone, cobblestone | 3 |
| Wood log | 2.5 |
| Leaves | 0.4 |
| Wooden planks | 1.5 |
| Sand, gravel | 1 |
| Bedrock | Unbreakable |

Each mined block drops one item of its own type. Solid items render as small atlas-textured cubes; plants use transparent crossed quads. Items fall with gravity and settle on solid blocks, using substeps to prevent tunneling. They are drawn together through a reusable dynamic vertex buffer with point sampling, alpha testing, depth writes, and frustum checks. Item movement never invalidates chunk meshes.

After a short 0.35-second pickup delay for mined items, approaching within 1.25 blocks of an item's center from the player's collision box collects it automatically. Manual drops and death drops instead have a one-second delay. Timers use elapsed active gameplay time and stop with the rest of gameplay in menus. Compatible inventory stacks fill before empty slots. Any quantity that does not fit stays in the world and can be collected later. Dead players cannot collect items.

Nearby same-type entities merge within 0.75 blocks, up to 64 items per stack; 40 + 40 becomes 64 + 16. Both resulting stacks keep the longer remaining pickup delay, preventing merges from bypassing it. A reusable spatial index checks nearby cells every quarter-second, without comparing every entity pair each frame. Entities do not expire. Items falling through a hole below the world remain at their original X/Z at the lower simulation limit (Y=-15.875), rather than teleporting to spawn or disappearing. A defensive one-million-entity save/allocation bound prevents further mining at that limit without removing blocks or items.

## Inventory

Press **E** to open the inventory: 27 main slots in three rows above the same nine hotbar slots used by the HUD. Opening it releases the mouse and freezes gameplay; **E** or **Escape** closes it and recaptures the mouse. The layout scales with the viewport. **1-9** or the **mouse wheel** selects the gameplay hotbar slot. **Scroll down moves right (1 toward 9); scroll up moves left (9 toward 1)**, wrapping at both ends and supporting multiple increments. Scrolling in menus/inventory does not change it. Tab no longer changes hotbar pages.

Stacks hold 1-64 whole items of one type. Every occupied slot and the cursor-held stack show their quantity. Empty slots contain no item and no quantity. Mouse actions are single clicks, with no drag distribution or shift transfers:

| Click | Empty cursor | Holding a stack |
| --- | --- | --- |
| Left | Pick up the whole slot | Place in an empty slot, merge up to 64 with the same type, or swap with a different type |
| Right | Pick up half, rounded down; pick up the single item when quantity is 1 | Place one into an empty/compatible non-full slot; incompatible/full slots stay unchanged |

**Left-click outside the inventory panel while holding items to drop the whole cursor stack near the player**, preferably in front. The inventory stays open. Empty-cursor clicks and clicks in panel gaps do nothing. Drops require nearby clearance outside the player's body and are removed from the cursor only after spawning succeeds; a failed drop leaves the stack held with a message. Existing slot click behavior is unchanged. Closing first tries to return held items to their original slot, then compatible stacks and empty slots. If a remainder cannot fit, the inventory stays open with a message until the player places or drops it. Saving/exiting uses the same safe-return rule.

**RMB** placement consumes exactly one item from the selected hotbar slot only when placement succeeds. Empty slots cannot place anything. Failed placement, including player overlap, occupied cells, invalid plant support, and world boundaries, consumes nothing. Bedrock remains placeable from inventory.

Each new world starts with **14 stacks of 64 placeable blocks**: the existing 13 block stacks in their original palette order, followed by White Wool. Grass, dirt, stone, logs, leaves, planks, cobblestone, sand, and gravel occupy hotbar slots 1-9; bedrock, tall grass, red flowers, yellow flowers, and White Wool occupy the first five main slots. The other 22 slots start empty. **No raw porkchops, Leather, or Feathers are granted.** This is temporary testing stock, never replenished after world creation or respawn. Existing saved inventories are not changed. The pre-existing version-1 inventory migration still grants only the original 13 block stacks. Each world has independent contents and hotbar selection.

## Passive mobs, resources, and raw porkchops

New Small, Normal, and Large worlds attempt to spawn **8, 32, and 128 passive mobs total**, respectively, sharing that budget across all four species. A separate seeded species sequence allows small variations while keeping a balanced mixture. Seeded positions require clear, level grass, no water, spacing between mobs, room for their species-specific bounds, and at least 12 horizontal blocks of clearance from the player's initial spawn. Extremely obstructed terrain can yield fewer mobs. Initial spawning runs once after terrain/decorations, without modifying terrain or rebuilding meshes for individual mobs. The actual initial counts become independent per-species population targets, rather than applying a new budget after deaths. The defensive runtime/save limit is **256 combined**, preserving valid older pig populations. There is no breeding, despawning, or population growth beyond these targets.

Each killed animal schedules exactly one same-species replacement after an independent random **30-60 seconds of active gameplay**. Replacement candidates reuse the grass, water, clearance, and species-bound checks and exclude other mobs, dropped items, and the player's body. The first candidates prefer at least 24 horizontal blocks from the player, relaxing to 12 for later attempts. Work is bounded to 32 candidate positions per simulation update across all ready replacements. A failed search waits five gameplay seconds before retrying; pending animals are never abandoned and cannot exceed their species' saved target. Pausing, inventory, death, and other menus suspend these timers. Saving preserves targets and remaining delays, so loading neither loses a replacement nor creates it twice.

| Species / saved ID | Maximum health | Walking speed | Death drop |
| --- | --- | --- | --- |
| Pig / 0 | 10 | 0.8 blocks/second | 1-2 Raw Porkchops |
| Cow / 1 | 10 | 0.7 blocks/second | 1-4 Leather |
| Sheep / 2 | 10 | 0.75 blocks/second | 1-4 White Wool |
| Chicken / 3 | 6 | 0.9 blocks/second | 1-4 Feathers |

All four species share the original pig idle/wander, gravity, terrain collision, water flotation/shore hopping, and damage logic in [PassiveMob.cs](./Entities/PassiveMob.cs). Their health, dimensions, speeds, textures and drops are centralized in [MobDefinitions.cs](./Entities/MobDefinitions.cs). They stop at obstacles and choose a later direction, without pathfinding, pursuing, fleeing, attacking, or automatic land-block climbing. Damage does not change their AI decision. The original seven-cuboid pig model/texture/animation remains intact. Cows have a larger body, head, muzzle, horns and four legs; sheep have a fleece body, head/cap and four legs; chickens have a small body/head, beak, red wattle, wings and two legs. Distance-driven leg swings (and chicken wing motion) stop while idle. [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs) frustum-culls and batches dynamic cuboids by species, issuing at most four mob draws and reusing GPU capacity without touching chunk meshes. It restores texture/sampler/blend/depth/culling state, and the F1/F2 binding regression remains covered.

Hold **LMB** while aiming at a mob within the normal six-block reach to punch it. Each successful punch randomly deals **1 or 2 health**, with a **0.4-second cooldown**, a brief red flash, and modest upward/backward knockback. Solid terrain occludes entity targeting; non-solid plants and water do not. The nearest valid mob takes priority over mining the block behind it, using its species-specific bounds. When it leaves the ray or dies, holding LMB continues ordinary block mining. Death creates one stack of the designated resource and quantity, exactly once. Like mining, a fatal transfer waits if the defensive dropped-entity capacity limit prevents creating its drop. Mobs never flee or retaliate.

**Leather (item ID 257)** and **Feather (item ID 258)** are non-placeable, non-food items. **White Wool (block/item ID 23)** is a normal solid opaque cube using atlas tile 15. All stack to 64 and support the existing inventory clicks, splitting, cursor/manual/death drops, pickup, stacking, and per-world persistence. Leather/Feather use cutout held/drop sprites and separate editable 16 x 16 icons; Wool uses textured cubes in the world, hotbar/inventory, held view, and dropped entities. No shearing, colored wool or crafting is implemented.

**Raw Porkchop uses item ID 256**, outside the existing byte-valued block IDs. It is non-placeable and stacks to 64. Existing inventory clicks, merging, splitting, manual/death drops, pickup, and per-world persistence apply. Its cutout icon also appears as a flat held item and camera-facing dropped sprite. Select it in the hotbar and **press RMB to consume one and restore 3 health**, capped at 20. Full-health/dead players consume nothing. Holding RMB does not repeat eating; release and press again. Ordinary block placement still uses RMB for placeable items. There is no hunger, regeneration, eating animation, cooking, or consumption timer.

Living mob species, positions, yaw, and remaining health persist with each world, together with population targets and pending replacement delays. AI timers, walk phase, knockback velocity, and hurt flash are temporary. Killed mobs are removed before saving; loading or player respawn never restores the dead entity, and its scheduled replacement still waits for the remaining gameplay delay. Mobs and dropped resources freeze while gameplay is paused or inventory/menus are open. Editable [pig.png](./Content/pig.png) and [raw-porkchop.png](./Content/raw-porkchop.png) remain unchanged. The new [cow.png](./Content/cow.png), [sheep.png](./Content/sheep.png), [chicken.png](./Content/chicken.png), [leather.png](./Content/leather.png) and [feather.png](./Content/feather.png) use the existing Content pipeline. [Artwork notes](./Content/passive-mob-artwork.md) record their built-in image-generation prompts and one-time preparation. Only previously unused terrain atlas tile 15 was filled; world/lake generation and water mechanics are unchanged.

## Health, death, and movement

The player has **20 whole health points**, shown as ten pixel-art hearts above the hotbar. Full hearts represent two points; half hearts represent one. Damage and health restoration are clamped to 0-20 through centralized methods. Raw porkchops provide direct healing; there is no natural regeneration.

Fall damage is the only damage source. On landing on a solid surface, damage is `max(0, floor(fallDistance - 3))`, with a small floating-point tolerance at integer thresholds. Distances of 3, 4, 5, 13, and 23 blocks cause 0, 1, 2, 10, and 20 damage respectively. Distance is vertical feet descent from the highest point reached during the airborne movement, independent of horizontal motion or impact velocity. Ordinary jumps cause no damage; jumping to a lower surface measures from the apex. Each landing resets tracking, while pausing or opening inventory during a fall preserves it.

At zero health, **You died!** appears with exactly **Respawn** and **Main Menu**. Gameplay input and item simulation stop. Every occupied inventory/hotbar slot and any cursor-held stack drops near death, preserving its type and quantity; one stack creates one entity, rather than one per item. Transfers clear inventory only after spawning succeeds. If the entity limit or lack of clearance prevents a drop, those items are explicitly retained with a message. A retained cursor that cannot fit blocks saving until the player respawns and organizes it.

**Respawn** restores 20 health above the current center surface, clears fall/movement state, and recaptures the mouse. Inventory remains empty after normal death, with no testing-stock refill. Death drops stay at the death location for recovery. **Main Menu** safely saves zero health, empty inventory, and death drops before releasing the session; a save failure leaves the death screen and session available to retry or respawn. Reloading a dead save automatically respawns with full health without repeating its drops or refilling inventory.

Hold **Left Shift** to crouch at 35% walking speed with a lower camera. Normal jumping height and mouse look remain available. Grounded crouch movement checks support under the player's feet along both horizontal axes, including diagonal approaches, allowing a close approach to platform edges without walking off. Jumping intentionally leaves the edge; releasing Shift permits walking off. Removing the supporting block still allows the player to fall. Crouching changes speed and eye height; the standing collision body is retained.

The eye height is 1.62 blocks while standing and **1.50 while crouching**, a subtle 0.12-block reduction instead of the previous 0.37. The existing immediate transition is retained; crouch speed, collision, edge protection, and jump height are unchanged.

## First-person held items and mouse capture

During gameplay, an empty selected hotbar slot renders **nothing**. Occupied slots show the actual selected item: ordinary blocks are textured 3D cubes, while flowers/tall grass use transparent crossed planes. Number keys and the mouse wheel update the model immediately. The empty-hand model and its texture have been removed. There is no separate held-item selection or inventory copy, player body, third-person mode, or animation.

[FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs) reuses the existing cube/plant geometry and block atlas, caches geometry/uploads until the selected item changes, and draws one small camera-space model after the world and before the HUD. A depth reset keeps nearby terrain from clipping it while preserving the world color; the model uses its own perspective, depth testing, backface culling, alpha testing, and point sampling. Its viewport is restored before the HUD. The existing held-item scale, rotation, perspective, and responsive lower-right placement are unchanged. Models are suppressed in every menu, inventory, and death screen, whenever the selected slot is empty, and when F1 hides the HUD. The atlas, world generation, world save format, and FPS preferences are unchanged by this update.

[MouseLookInput.cs](./Input/MouseLookInput.cs) captures native relative motion through **SDL2 already shipped by MonoGame DesktopGL**, without an extra package. The previous loop repeatedly read an absolute OS cursor position, subtracted the window center, and called `Mouse.SetPosition` every frame. [MonoGame 3.8.6's implementation](https://github.com/MonoGame/MonoGame/blob/v3.8.6/MonoGame.Framework/Platform/Input/Mouse.SDL.cs) reads global OS position even after setting its cached window coordinates, leaving cursor warps and subsequent polls as separate operations. A stale or warp-induced non-center sample could therefore be applied as genuine movement at high rendering rates. A baseline run reproduced small rightward jumps at 240/360/uncapped FPS.

Gameplay now uses [SDL_GetRelativeMouseState](https://wiki.libsdl.org/SDL2/SDL_GetRelativeMouseState) once per input update: it returns accumulated motion since the previous read, and repeated reads without new motion return zero. [SDL relative mode](https://wiki.libsdl.org/SDL2/SDL_SetRelativeMouseMode) hides/confines the pointer without the game's per-frame recentering. Menus, inventory, focus loss, death, and exit release it and restore the prior menu cursor position. Capturing/releasing and window resizing discard transition motion; the first subsequent genuine movement is processed immediately. Buttons and scrolling retain MonoGame's existing input path. Angular sensitivity stays at 0.0025 radians per delta unit, with no dead zone, smoothing, elapsed-time multiplier, or FPS restriction.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move forward / left / backward / right |
| Mouse | Look around |
| Space | Jump while on the ground / hold to swim upward in water |
| Hold Left Shift | Crouch; slower movement, lower camera, and grounded edge protection |
| Hold left mouse button | Punch a targeted passive mob, or mine the outlined block; release to discard mining progress |
| Right click | Place one selected block, or consume one raw porkchop to heal 3 health when below full health |
| 1-9 (or numpad) | Select the corresponding hotbar slot |
| Mouse wheel during gameplay | Down: select right / Up: select left; wraps at slots 1 and 9 |
| E | Open / close inventory |
| Left / right click in inventory | Move whole stacks / split or place one item |
| Left click outside inventory panel while holding items | Drop the entire held stack; inventory stays open |
| F1 | Hide / restore the gameplay HUD and held item, preserving the F3 state |
| F2 | Save the final rendered frame as a PNG in the application's screenshots directory |
| F3 | Toggle player coordinates, chunk coordinates, approximate FPS, seed, chunk count, and dimensions |
| Escape | Open pause / resume; close inventory if its held stack can return safely |
| Pause menu Resume | Resume and capture the mouse |
| Main/pause menu Settings | Open the shared FPS preferences, Controls, and About options |
| Controls / About wheel, Up/Down, Page Up/Down, Home/End | Scroll informational text; Back or Escape returns to Settings |
| Settings Left/Right, Home/End | Change the focused FPS slider by one / select its minimum or maximum |
| F10 or gamepad Back | Save the active world and quit |

Block interaction reaches six blocks from the camera. Each RMB press attempts one placement. Placement requires an empty cell inside the world; solid blocks cannot overlap the player's body. Raycasts target a plant's voxel cell, rather than individual visible texels or crossed planes. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams.

Block edits, health, collected inventory, and uncollected items persist together. Infinite terrain, crafting, tools, armor, and advanced lighting are outside this milestone.

## Code layout

Source files use file-scoped namespaces matching their directories, rooted at `MDSM`. The plural `Worlds` and `Inventories` and the `Gameplay` grouping avoid collisions with the existing `World`, `Inventory`, and `Player` class names. Existing companion types remain together in their original files.

| Directory / namespace | Responsibility |
| --- | --- |
| [Core/](./Core/) — `MDSM.Core` | Game coordination, application state, frame timing, and application settings |
| [Worlds/](./Worlds/) — `MDSM.Worlds` | World/chunk storage, block definitions, sizes, terrain, and decoration |
| [Rendering/](./Rendering/) — `MDSM.Rendering` | World/item rendering, meshes, atlas loading, held models, and screenshots |
| [Gameplay/](./Gameplay/) — `MDSM.Gameplay` | Player/camera state, movement, fall tracking, mining, placement, and raycasting |
| [Inventories/](./Inventories/) — `MDSM.Inventories` | Item definitions, stacks, inventory operations, and hotbar selection |
| [Entities/](./Entities/) — `MDSM.Entities` | Dropped-item state/physics and drop-position checks |
| [UI/](./UI/) — `MDSM.UI` | HUD, inventory interface, menus, layouts, and pixel font |
| [Input/](./Input/) — `MDSM.Input` | Relative mouse capture and presentation shortcuts |
| [Persistence/](./Persistence/) — `MDSM.Persistence` | World serialization, save management, and saved/session state |
| [Testing/](./Testing/) — `MDSM.Testing` | All existing headless checks and optional DesktopGL regression checks |

[Program.cs](./Program.cs) remains at the project root as the application/check-runner entry point, alongside [MDSM.csproj](./MDSM.csproj) and the existing configuration, documentation, and icon files. SDK recursive source inclusion compiles the organized directories without explicit compile entries. [Content/](./Content/), content asset names, application-data locations, and save/settings formats are unchanged. Build and check commands still run from the project root.

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Core/Game1.cs) | Lifecycle, state transitions, save/autosave/exit orchestration, input, and renderer lifetime |
| [GameSettings.cs](./Core/GameSettings.cs) | Shared FPS preferences, bounded loading, and atomic application-settings persistence |
| [FrameTiming.cs](./Core/FrameTiming.cs) | Sleeping render limiter and independent fixed simulation clock |
| [SettingsLayout.cs](./UI/SettingsLayout.cs) | Responsive slider/toggle/Back geometry and integer slider conversion |
| [PresentationInput.cs](./Input/PresentationInput.cs) | Independent F1/F3 visibility and single-press F2 requests |
| [ScreenshotCapture.cs](./Rendering/ScreenshotCapture.cs) | Final backbuffer PNG capture, exclusive filenames, and failed-write cleanup |
| [MenuInformation.cs](./UI/MenuInformation.cs) | Controls/About text, responsive wrapping, and scroll layout |
| [FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs) | Inventory-driven cube/plant selection, empty-slot suppression, cached geometry, and isolated camera-space rendering |
| [MouseLookInput.cs](./Input/MouseLookInput.cs) | Consumed SDL relative deltas and capture-transition flushing using the existing native SDL library |
| [GameState.cs](./Core/GameState.cs) | Main menu, world management, gameplay, inventory, pause, death, Settings, Controls, and About states |
| [MenuController.cs](./UI/MenuController.cs) | Atlas dirt background, title, size selector, world management layouts, navigation, and feedback |
| [WorldSave.cs](./Persistence/WorldSave.cs) | Saved state and isolated session, one-time death transfers, respawn, and manual drop orchestration |
| [WorldSerializer.cs](./Persistence/WorldSerializer.cs) | Versioned binary format, validation, and checksums |
| [WorldSaveManager.cs](./Persistence/WorldSaveManager.cs) | Per-user world directories, header listing, safe rename/deletion, folder opening, and atomic replacement |
| [Camera.cs](./Gameplay/Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Gameplay/Player.cs) | Health, player bounds, movement, crouch support checks, gravity, jump, collision, and respawn |
| [FallTracker.cs](./Gameplay/FallTracker.cs) | Vertical apex/landing tracking and distance-only fall damage |
| [HealthHud.cs](./UI/HealthHud.cs) | Pixel-art full/half/empty hearts above the hotbar |
| [Blocks.cs](./Worlds/Blocks.cs) | Stable IDs, centralized block properties, mining durations, shapes, and face textures |
| [Items.cs](./Inventories/Items.cs) | Item IDs, block-item mapping, and validated immutable stacks |
| [Inventory.cs](./Inventories/Inventory.cs) | 36-slot storage, stacking, cursor operations/safe transfers, and accumulated wheel selection |
| [InventoryScreen.cs](./UI/InventoryScreen.cs) | Scaled slot layout, point-sampled icons/counts, and mouse interface |
| [MiningController.cs](./Gameplay/MiningController.cs) | Target-bound elapsed mining, drops, resets, and inventory placement |
| [DroppedItems.cs](./Entities/DroppedItems.cs) | Entity state, gravity/collision, pickup delays, spatial merging, insertion remainders, and validation |
| [DropPlacement.cs](./Entities/DropPlacement.cs) | Nearby clear spawn positions for deliberate item drops |
| [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs) | Batched cube/cutout item rendering with reusable GPU capacity |
| [World.cs](./Worlds/World.cs) | Finite world, chunk lookup/invalidation, safe reads/edits, placement, and collision queries |
| [WorldSize.cs](./Worlds/WorldSize.cs) | Supported per-world size options and dimension validation |
| [Chunk.cs](./Worlds/Chunk.cs) | Compact local block storage, world bounds, and mesh revision |
| [TerrainGenerator.cs](./Worlds/TerrainGenerator.cs) | Unchanged seeded height generation, grass/dirt/stone layering, and a single bedrock layer |
| [WorldDecorator.cs](./Worlds/WorldDecorator.cs) | Deterministic trees and plants after base terrain generation |
| [BlockRaycaster.cs](./Gameplay/BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./Rendering/TextureAtlas.cs) | ContentManager atlas loading, grid tile rectangles, and texel-center UVs |
| [Content/terrain.png](./Content/terrain.png) | Editable 256 x 256 block texture atlas |
| [ChunkMeshBuilder.cs](./Rendering/ChunkMeshBuilder.cs) | Shared cube/cross geometry for chunks and small items, with chunk opacity checks |
| [ChunkMesh.cs](./Rendering/ChunkMesh.cs) | Per-chunk GPU buffer, revision tracking, uploads, and disposal |
| [WorldRenderer.cs](./Rendering/WorldRenderer.cs) | Chunk frustum culling, alpha testing, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./UI/Hud.cs) | Visibility-aware crosshair, hotbar, health, notifications, and cached F3 debug text |
| [PixelFont.cs](./UI/PixelFont.cs) | Shared bitmap lettering for the HUD and menus |
| [PrototypeChecks.cs](./Testing/PrototypeChecks.cs) | Automated checks that run without a graphics device |
| [PersistenceChecks.cs](./Testing/PersistenceChecks.cs) | Edited-world restoration, corruption validation, safe paths, and failed-save checks |
| [DecorationChecks.cs](./Testing/DecorationChecks.cs) | Block definitions, deterministic decorations, bounds, cutout geometry, interactions, and legacy/new save restoration |
| [WorldManagementChecks.cs](./Testing/WorldManagementChecks.cs) | All-size generation, indexing, bounds, canopies, restoration, header-only metadata, and safe management |
| [InventoryChecks.cs](./Testing/InventoryChecks.cs) | Mining, item physics/pickup, accounting, mouse operations, placement, migration, and persistence checks |
| [SurvivalChecks.cs](./Testing/SurvivalChecks.cs) | Health/hearts, fall distances, death accounting, respawn, deliberate drops, delays/merges, v1/v2 compatibility, crouching, and wheel checks |
| [PresentationChecks.cs](./Testing/PresentationChecks.cs) | FPS range/override, settings persistence/corruption, render-rate independence, crouch camera, and model geometry/visibility/layout checks |
| [MouseInputChecks.cs](./Testing/MouseInputChecks.cs) | Stationary/moving high-frequency input, unchanged sensitivity, consumed deltas, and capture/release resets |
| [Pig.cs](./Entities/Pig.cs), [PigPopulation.cs](./Entities/PigPopulation.cs) | Thin pig compatibility facades over shared passive-mob behavior/population for older state and regression fixtures |
| [CombatController.cs](./Gameplay/CombatController.cs), [ItemUse.cs](./Gameplay/ItemUse.cs) | Entity ray targeting/occlusion, attack cooldown/mining priority, and press-edge food consumption |
| [CuboidModelBuilder.cs](./Rendering/CuboidModelBuilder.cs), [PigRenderer.cs](./Rendering/PigRenderer.cs) | Reusable textured cuboids/UV regions and the preserved original pig model/animation |
| [ItemSpriteGeometry.cs](./Rendering/ItemSpriteGeometry.cs) | Shared cutout geometry for held and dropped non-block items |
| [PigChecks.cs](./Testing/PigChecks.cs), [PigGraphicsChecks.cs](./Testing/PigGraphicsChecks.cs) | Pig/food simulation, persistence, input paths, and actual GPU regressions |
| [InterfaceChecks.cs](./Testing/InterfaceChecks.cs) | F1/F3/F2 input edges, screenshot storage/failures, and information text/layout checks |
| [InterfaceGraphicsChecks.cs](./Testing/InterfaceGraphicsChecks.cs) | Optional real DesktopGL PNG pixel comparisons, HUD states, resizing, menu navigation, and failure recovery |

All chunks remain loaded for the active world. There is no chunk streaming, multithreaded generation, ores, crafting, tools, armor, tree growth, or vegetation growth.

The water update adds [Water.cs](./Worlds/Water.cs) for fluid-state/height queries, [FluidSimulation.cs](./Worlds/FluidSimulation.cs) for scheduled flow, [LakeGenerator.cs](./Worlds/LakeGenerator.cs) for new-world basins, [WaterMeshBuilder.cs](./Rendering/WaterMeshBuilder.cs) and [WaterRenderer.cs](./Rendering/WaterRenderer.cs) for translucent geometry, and [WaterChecks.cs](./Testing/WaterChecks.cs) / [WaterGraphicsChecks.cs](./Testing/WaterGraphicsChecks.cs) for regression coverage. Integration changes are limited to existing world/chunk storage, creation/load orchestration, rendering, targeting/placement, player/pig water motion, and the Controls text.

Milestone 8 changes 26 files (9 added, 17 modified):

| System | Added | Modified |
| --- | --- | --- |
| World and simulation | [Water.cs](./Worlds/Water.cs), [FluidSimulation.cs](./Worlds/FluidSimulation.cs), [LakeGenerator.cs](./Worlds/LakeGenerator.cs) | [World.cs](./Worlds/World.cs), [Chunk.cs](./Worlds/Chunk.cs), [Blocks.cs](./Worlds/Blocks.cs) |
| Rendering | [WaterMeshBuilder.cs](./Rendering/WaterMeshBuilder.cs), [WaterRenderer.cs](./Rendering/WaterRenderer.cs) | [WorldRenderer.cs](./Rendering/WorldRenderer.cs), [ChunkMeshBuilder.cs](./Rendering/ChunkMeshBuilder.cs), [TextureAtlas.cs](./Rendering/TextureAtlas.cs) |
| Integration and movement | None | [Game1.cs](./Core/Game1.cs), [Player.cs](./Gameplay/Player.cs), [BlockRaycaster.cs](./Gameplay/BlockRaycaster.cs), [Pig.cs](./Entities/Pig.cs), [PigPopulation.cs](./Entities/PigPopulation.cs), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Persistence | None | [WorldSerializer.cs](./Persistence/WorldSerializer.cs) |
| Assets | [water.png](./Content/water.png), [water-artwork.md](./Content/water-artwork.md) | [Content.mgcb](./Content/Content.mgcb) |
| Checks | [WaterChecks.cs](./Testing/WaterChecks.cs), [WaterGraphicsChecks.cs](./Testing/WaterGraphicsChecks.cs) | [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [Program.cs](./Program.cs) |
| Documentation | None | This README |

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The **95 check groups** include independent F1/F3 state, repeated F2 input without duplicate captures, screenshot directory creation, collision-safe filenames and write failures, accurate Controls/About acknowledgements, and responsive information layouts. Existing coverage includes FPS defaults and all integer slider values, uncapped override/value retention, settings reload/validation/write failures, identical fixed simulation outcomes at 5/30/60/144/240/360/997 rendered FPS, reduced crouch camera height, and selected cube/plant geometry, empty-slot suppression/switching, menu visibility, UVs, and layout. Mouse regressions check zero stationary rotation at 60-2000 updates/second, identical movement/sensitivity across rates, no replayed deltas, one-unit motion without a dead zone, and capture/release/resize flushing without first-sample latency. Health, fall tracking, death/respawn, dropped items, inventory accounting/placement, persistence, v1/v2 compatibility, crouch support/jumps, generation/restoration, geometry, atlas, checksums, atomic saving, management, and world-size checks remain included. A bounded non-seekable stream verifies that metadata reading stops before block data. Persistence fixtures stay under the test build and are cleaned up; they never touch real saves. A failing check returns a nonzero exit code. These checks do not open a window or exercise operating-system input.

Optional graphics verification opens a real DesktopGL window and requires focus and an OpenGL-capable desktop:

```powershell
dotnet run --no-build -- --graphics-test
```

This check uses synthetic input and temporary saves/preferences/screenshots under the build directory, cleaned up afterward. It verified 13 PNG captures at 1280x720, 801x603, and 320x240 against the final backbuffer, pixel-for-pixel. Asymmetric corners and a final-pass frame marker detect inverted/aligned rows, stale frames, missing draw passes, and changed pixels. Coverage includes HUD shown/hidden with independent debug states, occupied/empty selected slots, resizing, inventory/death screens, main/pause Settings navigation through Controls/About, scrolling, persistent FPS preferences, captures at 60/144/240/360/uncapped configurations, and an injected screenshot directory failure followed by recovery. It also checks removal of the permanent controls panel and hotbar selection while the HUD is hidden. It does not measure sustained FPS or exercise physical keys/mice.

An additional screenshot regression compares consecutive frames before and after repeated captures, alternating HUD visibility, resizing, and a failed write. It disables the test's SpriteBatch markers and defers PNG decoding until after the comparisons: both operations change texture bindings and could mask the black-texture bug. Before the correction, the first hidden-HUD frame after capture failed; with the correction, world pixels remain identical and all eight additional PNGs match their original resolution and pixel data. [ScreenshotCapture.cs](./Rendering/ScreenshotCapture.cs) restores and invalidates the previous texture binding in a `finally` block so DesktopGL's temporary screenshot texture cannot leave MonoGame's binding cache stale, including when writing fails.

A separate scripted DesktopGL run verified actual GPU heart states at 1280x720, 640x480, and 320x240; cube/plant item cutouts and terrain depth; input-path inventory drops and wheel selection; diagonal crouch edges and jumping; fatal falls, death input suspension and cursor transfers; respawn; failed/successful death saves and automatic respawn on reload; a real pre-Milestone-6 v2 save; autosaving health; and world creation, selection, renaming, and confirmed deletion. It used isolated temporary saves and synthetic input, rather than physical keyboard/mouse playtesting.

The six pig/food headless groups cover deterministic all-size populations, gravity/collisions, passive AI, render-independent motion/combat, knockback, health/death/drop accounting, terrain-occluded targeting, cooldown/mining priority, RMB healing edges/limits, inventory operations/full-inventory remainders, new/old-world stock, all-size persistence/corruption, and model geometry/animation. All original 59 groups remain. The graphics command also runs pig/food checks: textured pink cuboids, red hurt tint, foreground-depth occlusion, restored graphics state, held/dropped cutout food, three additional screenshots and unmodified following F1-hidden frames, real input-path punching/eating/cooldowns, saved kills/drops/health, empty respawn inventory, and actual Create World menu stock/population. It uses synthetic input and isolated data, not physical-input or visual playtesting.

The FPS update's scripted DesktopGL checks covered Settings entry/Back, every keyboard slider value and mouse dragging/toggling, fresh preference loading, paused-world suspension, input-path movement/mining, foreground-depth obstruction, preserved terrain, and HUD clearance. Short render-only samples measured approximately 60/144/360/5 FPS at those menu caps and above 60 when uncapped. These samples establish limiter behavior, not sustained performance with normal or Large terrain.

The corrective update compared cube/log/leaf/plant renders pixel-for-pixel against pre-correction images at 320x240, 640x480, 1280x720, and 2560x1080. Empty slots draw no pixels, switching back restores the same item render, and all menus suppress models. A native Windows/SDL run verified zero stationary rotation and exact rotation from injected movement at 60, 144, 240, 360, and uncapped (roughly 2800 achieved) FPS. It also rejected 10,000 stale absolute-position samples and exercised inventory, pause, Settings, injected SDL focus events, death, and respawn capture transitions. An isolated run of the previous build reproduced stationary rightward jumps at 240/360/uncapped, while 60/144 stayed still during that sample.

Manual testing is still needed for physical F1/F2/F3 presses and menu scrolling, screenshots on other drivers/platforms and high-DPI displays, physical mice at different polling rates/DPI, real Alt-Tab/focus changes and cursor restoration, Linux/macOS relative capture, and sustained high-FPS play in all sizes. Also exercise slider/mouse-wheel controls, held-item alignment on other displays/aspect ratios, irregular crouch edges, falls across menu transitions, death-drop recovery, many simultaneous items, inventory placement/accounting, save-on-window-close, filesystem errors, and folder opening. Scripted injected input is not physical mouse playtesting. Generation, initial mesh construction, and saving remain synchronous, so Large worlds can pause briefly during those operations.


The nine water groups preserve all 65 previous groups. They verify source/falling/level bytes, non-inventory water, placement displacement, downward priority, seven-block horizontal reach, obstacle routing, supported source regeneration, complete recession and remaining supplies, cross-chunk/world boundaries, bounded/idle queues and render-independent ticking, shared surface heights and face visibility, swimming/fall resistance, pig flotation and shoreline escape, deterministic/stable all-size lakes with restrained sizes and patchy beds/beaches, extreme seeds, actual bank/bed excavation, all-size active-flow round trips, and unchanged v4 compatibility. Existing tests continue exercising older v1-v3 readers and all previous systems.

The graphics command additionally checks water on a real DesktopGL device: blue alpha blending over different backgrounds, visible lake beds, foreground terrain depth, absence of water depth writes, restored blend/depth/raster/sampler/texture state, repeated HUD-hidden captures and following unchanged frames, F1/F3 independence, exact PNG pixels at 1280x720 and 801x603, held-Space swimming through the real input path, and a rendered/saved newly generated lake. These are scripted pixel/input checks; physical controls, shoreline appearance while exploring, unusual waterfalls, other graphics drivers, and sustained Large-world fluid/rendering performance still require manual playtesting.

## Milestone 9 source changes and verification

37 files changed: 13 added and 24 modified. Existing classes were extended/extracted rather than introducing a new entity framework; all previous check groups remain enabled.

| System | Added | Modified |
| --- | --- | --- |
| Passive behavior/population | [MobDefinitions.cs](./Entities/MobDefinitions.cs), [PassiveMob.cs](./Entities/PassiveMob.cs), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) | [Pig.cs](./Entities/Pig.cs), [PigPopulation.cs](./Entities/PigPopulation.cs) |
| Rendering | [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs), [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs) | [PigRenderer.cs](./Rendering/PigRenderer.cs), [CuboidModelBuilder.cs](./Rendering/CuboidModelBuilder.cs), [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs), [FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs), [TextureAtlas.cs](./Rendering/TextureAtlas.cs) |
| Integration, items and saves | None | [Game1.cs](./Core/Game1.cs), [CombatController.cs](./Gameplay/CombatController.cs), [Items.cs](./Inventories/Items.cs), [Inventory.cs](./Inventories/Inventory.cs), [Blocks.cs](./Worlds/Blocks.cs), [WorldSave.cs](./Persistence/WorldSave.cs), [WorldSerializer.cs](./Persistence/WorldSerializer.cs), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Assets | [cow.png](./Content/cow.png), [sheep.png](./Content/sheep.png), [chicken.png](./Content/chicken.png), [leather.png](./Content/leather.png), [feather.png](./Content/feather.png), [passive-mob-artwork.md](./Content/passive-mob-artwork.md) | [terrain.png](./Content/terrain.png) (tile 15 only), [Content.mgcb](./Content/Content.mgcb) |
| Checks | [MobChecks.cs](./Testing/MobChecks.cs), [MobGraphicsChecks.cs](./Testing/MobGraphicsChecks.cs) | [DecorationChecks.cs](./Testing/DecorationChecks.cs), [PigChecks.cs](./Testing/PigChecks.cs), [PigGraphicsChecks.cs](./Testing/PigGraphicsChecks.cs), [WaterChecks.cs](./Testing/WaterChecks.cs), [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [Program.cs](./Program.cs) |
| Documentation | None | This README |

The eight new headless groups cover all species' health/AI/physics/knockback, rendered-frame independence, finite boundaries and water flotation; deterministic balanced shared populations across all three generated world sizes; per-species ray bounds/occlusion/cooldowns/mining priority; exact single death drops and all quantity ranges; Leather/Feather stack operations, partial pickup and non-placeable/non-food behavior; White Wool timing/placement/self-drops/UVs; revised new-world stock and empty respawn; all-size mixed-entity/resource/water saves; killed-entity restoration; independent version-4 pig/food fixtures; and invalid species, health, coordinates and combined counts. Older check groups were updated only for the explicitly revised starting stock, version number, species-byte offsets, and shared APIs. The water group's legacy fixture now independently encodes the actual version-4 layout.

The full DesktopGL command also runs [MobGraphicsChecks.cs](./Testing/MobGraphicsChecks.cs). Scripted checks render all four models and their walking/hurt states, test foreground depth and restored graphics state, display held/dropped Leather/Feather/Wool, render a placed wool block, compare six full-frame PNGs including water and HUD-hidden consecutive frames, punch/kill each new species through the real game input path, save/reload its death/resources while retaining existing porkchops, and exercise actual new-world menu population/stock. The previous interface, pig/food, water and screenshot regressions remain included. Test data stays isolated under temporary test-build directories and is cleaned up; no real player saves/preferences are used.

Physical-input playtesting, model proportions/orientation while exploring normal terrain, crowded mixed mobs around irregular shores, resource recovery during longer play, other GPU drivers, and sustained Large-world rendering still require manual verification. The checks do not claim human visual review or extended gameplay.

## Population, falling blocks, and scroll correction

This intermediate update adds four source files and extends the existing systems:

| System | Files |
| --- | --- |
| Population targets, delays, valid bounded replacement attempts | Added [MobPopulationState.cs](./Entities/MobPopulationState.cs); extended [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) |
| Queued column gravity and generation support guard | Added [FallingBlockSimulation.cs](./Worlds/FallingBlockSimulation.cs); extended [World.cs](./Worlds/World.cs), [Blocks.cs](./Worlds/Blocks.cs) |
| Gameplay ticking and new-world population capture | [Game1.cs](./Core/Game1.cs) |
| Version-6 persistence and backward-compatible population baseline | [WorldSave.cs](./Persistence/WorldSave.cs), [WorldSerializer.cs](./Persistence/WorldSerializer.cs) |
| Wheel direction and accurate control text | [Inventory.cs](./Inventories/Inventory.cs), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Regression checks and legacy binary offsets | Added [WorldUpdateChecks.cs](./Testing/WorldUpdateChecks.cs), [SaveLayout.cs](./Testing/SaveLayout.cs); extended [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [MobGraphicsChecks.cs](./Testing/MobGraphicsChecks.cs), [MobChecks.cs](./Testing/MobChecks.cs), [PigChecks.cs](./Testing/PigChecks.cs), [DecorationChecks.cs](./Testing/DecorationChecks.cs), [WaterChecks.cs](./Testing/WaterChecks.cs), [SurvivalChecks.cs](./Testing/SurvivalChecks.cs) |
| Documentation | This README |

`dotnet build` completes with zero warnings/errors; all **90/90 headless groups** pass, including every previous group. Eight additional groups cover independent species targets and 30-60-second delays, quantity-preserving deaths, bounded failed-spawn retries, water/player/entity exclusion, exact all-size population/timer restoration, independent version-4/5 compatibility fixtures and corrupt metadata, stationary/support-removed/mixed stacked gravity at chunk and world edges, water displacement and preserved intermediate sources, queued-gravity restoration and unchanged legacy terrain, all-size generated Sand/Gravel support, work budgets, render-rate independence, and explicit wheel direction/wraparound. Existing assertions changed only for the intentional save-version/scroll-direction changes and independent binary-record offsets.

The complete `--graphics-test` suite also passes. Its added scripted input checks exercise paused and inventory-suspended respawn timers, resumed fixed-step stacked gravity, down/right and up/left wheel wrapping, preserved number keys, suppressed inventory scrolling, menu-return save/load of pending replacements, and exactly one subsequent replacement through the actual game loop. Existing mixed-mob, water, depth/state, screenshot/HUD-hidden consecutive-frame and exact PNG regressions remain passing. These checks use isolated temporary saves and injected input; they are not physical-input or visual playtesting.

Manual playtesting remains appropriate for physical scrolling, watching replenishment on irregular natural terrain over longer sessions, support removal around lakes and edited structures, and sustained Large-world performance. The population/scroll update did not change artwork, AI behavior, lake generation, fluid rules, inventory contents, rendering passes, or save locations; smooth falling adds the dynamic pass described below.

## Smooth falling-block refinement

Added [FallingBlock.cs](./Worlds/FallingBlock.cs), [FallingBlockRenderer.cs](./Rendering/FallingBlockRenderer.cs), and [FallingBlockChecks.cs](./Testing/FallingBlockChecks.cs). Extended [FallingBlockSimulation.cs](./Worlds/FallingBlockSimulation.cs), [World.cs](./Worlds/World.cs) (placement clearance), [ChunkMeshBuilder.cs](./Rendering/ChunkMeshBuilder.cs) (unit cube template), [WorldRenderer.cs](./Rendering/WorldRenderer.cs), [Game1.cs](./Core/Game1.cs), [FrameTiming.cs](./Core/FrameTiming.cs) (interpolation fraction), and [WorldSerializer.cs](./Persistence/WorldSerializer.cs). Updated [WorldUpdateChecks.cs](./Testing/WorldUpdateChecks.cs), [MobGraphicsChecks.cs](./Testing/MobGraphicsChecks.cs), [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [DecorationChecks.cs](./Testing/DecorationChecks.cs), [WaterChecks.cs](./Testing/WaterChecks.cs), and [MobChecks.cs](./Testing/MobChecks.cs), plus this README. Existing settlement assertions now wait for animation completion while retaining all material-count, support, water, boundary, mesh and save guarantees.

The headless suite includes five added groups for continuous/accelerated/interpolated motion, terminal velocity, first-surface swept collision with unusually large steps, mixed and staggered stacks, identical half-second positions at 30/60/144/360 FPS, placement accounting/new support, all-size exact mid-fall save/load, independent version-6 queue compatibility, invalid serialized states, and a 4200-block case that exercises the active cap, deferred stacks, persistence, and exact final type/order/counts. No previous group was disabled.

Verification: `dotnet build` succeeds with zero warnings/errors; **95/95 headless check groups** and the complete `--graphics-test` suite pass.

The complete DesktopGL suite checks full-size Sand/Gravel at distinct fractional heights, interpolation pixels, foreground depth, unchanged terrain revisions during flight, and restored graphics/texture state. Repeated full-frame F1/F2 regressions now also include active falling cubes alongside water and mobs. Injected gameplay input verifies that pause freezes an active fall, safe saving/loading retains its exact state, and resumed gameplay settles it once. These are scripted GPU/input checks, not physical-input or human visual playtesting. Manually assess animation feel while removing supports, tall mixed stacks and unusual edited lake shores, plus sustained performance with many simultaneous falls on other hardware. No new assets, dependencies, terrain algorithms, fluid rules, or mob mechanics were introduced.
