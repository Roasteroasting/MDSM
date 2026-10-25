# MDSM

A small Minecraft Indev-inspired voxel sandbox in C# and MonoGame DesktopGL. The game has finite worlds in three sizes, seeded terrain with trees and vegetation, chunk-based rendering, timed mining, collectible item stacks, a persistent 36-slot inventory, and player health with fall damage, death, and respawning.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/terrain.png](./Content/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting atlas texture is copied into the build output and owned by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Settings and frame rate

Choose **Settings** from the main menu or the pause menu. Both entry points share the same preferences. **Back** or Escape returns to the screen that opened Settings; returning to the pause menu keeps gameplay suspended and the mouse cursor available.

The **FPS Cap** slider supports every whole number from **5 to 360**, defaulting to **60**. Click or drag it, or focus it with Tab/Up/Down and use Left/Right for one-FPS changes; Home/End select the endpoints. The current numerical value is shown above the slider. **Uncapped FPS** defaults off and can be clicked or toggled with Space/Enter when focused. Changes apply immediately in gameplay and every menu. While uncapped, the slider remains editable and retains its value, but does not limit rendering until uncapped is turned off.

Preferences are application-wide in `%LOCALAPPDATA%/MDSM/settings.json` on Windows, alongside the existing `Worlds` directory. The equivalent per-user application-data location is used elsewhere. They are independent of world saves and survive world switches and application restarts. Missing settings use defaults; malformed/oversized files fall back safely, and out-of-range integers are clamped. Changes save after a short idle delay and on Back/exit using a temporary file and atomic replacement. Write failures keep the selected preferences active for the current session and display an error without replacing the previous valid file.

[FrameTiming.cs](./FrameTiming.cs) separates rendering from a **120 Hz fixed simulation clock**. The render limiter uses stopwatch deadlines and sleeping, compensating for small timer overshoots without busy-waiting and rebasing after slow frames. Uncapped mode skips the limiter. MonoGame's outer fixed-step timing and presentation synchronization are disabled so they do not impose the former 60 FPS ceiling; its inactive sleep is also disabled so the same preference applies throughout menus and focus changes. See [MonoGame's timing loop](https://github.com/MonoGame/MonoGame/blob/v3.8.6/MonoGame.Framework/Game.cs) for the outer-loop behavior. There is no additional VSync setting.

The simulation consumes elapsed time in fixed steps, including multiple steps per rendered frame at low FPS. Player physics, fall tracking, mining, item motion, pickup delays, and merging therefore use the same simulation rate at every render cap. Autosave remains based on active elapsed time. Jump presses are retained until the next simulation step; mouse look uses mouse displacement once per rendered frame. Menus clear fractional simulation time without resetting an in-progress fall. MonoGame's existing half-second maximum elapsed-time safeguard remains in place for exceptional stalls. A chosen cap is a rendering target/upper limit, not a promise of hardware performance; operating-system scheduling, drivers, and world complexity affect achieved FPS.

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

The player spawns above the surface near the center, with a clear area around the initial spawn. Horizontal player bounds stop at the finite edges, where terrain ends visibly. Block reads outside the world return air; edits outside it are rejected. Space above Y=63 remains open for movement, with no blocks stored above the limit. Existing void recovery below Y=-16 returns the player above the current center surface, resetting movement/fall tracking while preserving health; it is not an additional damage or healing source.

Terrain generation and initial mesh creation run synchronously on the main thread, so startup includes a short initial build. Each chunk has its own reusable GPU vertex buffer. Opaque neighbors hide cube faces across chunk boundaries; faces needed behind transparent leaf pixels remain visible. Plants use two crossed, double-sided quads in the same chunk buffers. Alpha testing discards pixels with alpha below 128 while remaining pixels write depth normally, without alpha blending or transparency sorting. Chunk bounds are tested against the camera frustum before submitting draws. An edit rebuilds its own mesh and, at a chunk edge, the face-sharing neighbors (at most three meshes for a single block edit). Diagonal chunks are unaffected. Mesh scratch storage and GPU capacity are reused, and all graphics operations stay on the main thread.

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

### Binary format, version 3

[WorldSerializer.cs](./WorldSerializer.cs) uses little-endian fields and no compression. Each save contains 1, 4, or 16 MiB of block data, depending on size, plus its header, inventory, dropped items, and checksum:

| Field | Representation |
| --- | --- |
| Signature | Four ASCII bytes: `MDSM` |
| Format version | 32-bit integer, currently 3; versions 1 and 2 remain readable |
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
| Checksum | SHA-256 of all preceding bytes, 32 bytes |

Loading validates the signature, version, supported dimensions, name, finite player pose, health range, block/item IDs, slot and entity counts, stack quantities, finite bounded item positions/velocities/delays, exact length, and checksum. Pickup delays must be between zero and one second. Allocation follows validated dimensions and bounded entity counts rather than arbitrary file lengths. Stored blocks are copied directly into empty chunk storage; terrain is never regenerated over saved edits. Chunk meshes are rebuilt from restored blocks. Unknown versions, missing files, and corrupt data produce a menu error. Listing obtains size from the unchanged small header without deserializing block or inventory arrays; full checksum validation happens on load.

Milestone 6 writes **format version 3**, appending health after the existing version-2 inventory/item records and before the checksum. Versions 1 and 2 default to 20 health. Version 2 retains its exact inventory and dropped items; it receives no testing-stock refill. Version 1 has the same header and raw block layout, followed immediately by its checksum. Such worlds receive one starter inventory and no dropped items on first load. After their next save (including rename), version 3 persists these normally without replenishment. Milestone 3/4 saves display as Normal; later Small/Large saves retain their own dimensions. All older terrain, seed, names, player pose, canopies, and bottom layers are preserved. Block IDs, GUID directories, and world generation are unchanged. Earlier game builds cannot read version-3 saves.

[WorldSaveManager.cs](./WorldSaveManager.cs) writes `world.dat.<random GUID>.tmp` in the same world directory, flushes it to disk, then atomically replaces `world.dat` (or renames it for the first save). Failed writes leave the previous valid save intact. Temporary files left by an interrupted process are ignored. No background save thread is used: save/load and generation can briefly pause the UI. Player position/orientation, health, all 36 inventory slots, selected hotbar slot, and uncollected items including remaining pickup delays persist per world. Partial mining progress, player velocity/fall tracking, F3 visibility, and cursor UI state are not serialized; cursor items must return safely before saving. Loading a save with zero health respawns the player with full health and its saved empty inventory, retaining the already-saved death drops without creating them again.

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

Every current non-air block has a corresponding item and remains placeable when held in the hotbar. Bedrock is unbreakable through mining. Plants require grass directly below them; mining their grass support also drops the removed plant. Leaves retain solid collision. Sand and gravel do not fall. Planks, cobblestone, sand, and gravel remain available as items without generating naturally. Bedrock generates only at Y=0. No block textures or generation rules changed in Milestones 5 or 6.

## Mining and dropped items

Hold **LMB** on a block within six blocks to mine it. An orange outline and a progress bar below the crosshair indicate progress, without a numerical percentage. Releasing LMB, changing targets or block types, losing reach, or opening a menu discards progress immediately. Holding LMB continues onto the next target after a break. Mining uses elapsed time and these centralized placeholder durations in [Blocks.cs](./Blocks.cs):

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

Press **E** to open the inventory: 27 main slots in three rows above the same nine hotbar slots used by the HUD. Opening it releases the mouse and freezes gameplay; **E** or **Escape** closes it and recaptures the mouse. The layout scales with the viewport. **1-9** or the **mouse wheel** selects the gameplay hotbar slot. Wheel selection wraps in either direction and supports multiple increments; scrolling in menus/inventory does not change it. Tab no longer changes hotbar pages.

Stacks hold 1-64 whole items of one type. Every occupied slot and the cursor-held stack show their quantity. Empty slots contain no item and no quantity. Mouse actions are single clicks, with no drag distribution or shift transfers:

| Click | Empty cursor | Holding a stack |
| --- | --- | --- |
| Left | Pick up the whole slot | Place in an empty slot, merge up to 64 with the same type, or swap with a different type |
| Right | Pick up half, rounded down; pick up the single item when quantity is 1 | Place one into an empty/compatible non-full slot; incompatible/full slots stay unchanged |

**Left-click outside the inventory panel while holding items to drop the whole cursor stack near the player**, preferably in front. The inventory stays open. Empty-cursor clicks and clicks in panel gaps do nothing. Drops require nearby clearance outside the player's body and are removed from the cursor only after spawning succeeds; a failed drop leaves the stack held with a message. Existing slot click behavior is unchanged. Closing first tries to return held items to their original slot, then compatible stacks and empty slots. If a remainder cannot fit, the inventory stays open with a message until the player places or drops it. Saving/exiting uses the same safe-return rule.

**RMB** placement consumes exactly one item from the selected hotbar slot only when placement succeeds. Empty slots cannot place anything. Failed placement, including player overlap, occupied cells, invalid plant support, and world boundaries, consumes nothing. Bedrock remains placeable from inventory.

Each new world starts with exactly **13 stacks of 64**, in the existing palette order: grass, dirt, stone, logs, leaves, planks, cobblestone, sand, gravel in hotbar slots 1-9; bedrock, tall grass, red flowers, yellow flowers in the first four main slots. The other 23 slots start empty. This is temporary testing stock. It is never replenished after world creation, after death, or after a legacy world's inventory has been saved in version 2 or later. Each world has independent contents and hotbar selection.

## Health, death, and movement

The player has **20 whole health points**, shown as ten pixel-art hearts above the hotbar. Full hearts represent two points; half hearts represent one. Damage and health restoration are clamped to 0-20 through centralized methods. There is no natural healing or other healing gameplay yet.

Fall damage is the only damage source. On landing on a solid surface, damage is `max(0, floor(fallDistance - 3))`, with a small floating-point tolerance at integer thresholds. Distances of 3, 4, 5, 13, and 23 blocks cause 0, 1, 2, 10, and 20 damage respectively. Distance is vertical feet descent from the highest point reached during the airborne movement, independent of horizontal motion or impact velocity. Ordinary jumps cause no damage; jumping to a lower surface measures from the apex. Each landing resets tracking, while pausing or opening inventory during a fall preserves it.

At zero health, **You died!** appears with exactly **Respawn** and **Main Menu**. Gameplay input and item simulation stop. Every occupied inventory/hotbar slot and any cursor-held stack drops near death, preserving its type and quantity; one stack creates one entity, rather than one per item. Transfers clear inventory only after spawning succeeds. If the entity limit or lack of clearance prevents a drop, those items are explicitly retained with a message. A retained cursor that cannot fit blocks saving until the player respawns and organizes it.

**Respawn** restores 20 health above the current center surface, clears fall/movement state, and recaptures the mouse. Inventory remains empty after normal death, with no testing-stock refill. Death drops stay at the death location for recovery. **Main Menu** safely saves zero health, empty inventory, and death drops before releasing the session; a save failure leaves the death screen and session available to retry or respawn. Reloading a dead save automatically respawns with full health without repeating its drops or refilling inventory.

Hold **Left Shift** to crouch at 35% walking speed with a lower camera. Normal jumping height and mouse look remain available. Grounded crouch movement checks support under the player's feet along both horizontal axes, including diagonal approaches, allowing a close approach to platform edges without walking off. Jumping intentionally leaves the edge; releasing Shift permits walking off. Removing the supporting block still allows the player to fall. Crouching changes speed and eye height; the standing collision body is retained.

The eye height is 1.62 blocks while standing and **1.50 while crouching**, a subtle 0.12-block reduction instead of the previous 0.37. The existing immediate transition is retained; crouch speed, collision, edge protection, and jump height are unchanged.

## First-person held items and mouse capture

During gameplay, an empty selected hotbar slot renders **nothing**. Occupied slots show the actual selected item: ordinary blocks are textured 3D cubes, while flowers/tall grass use transparent crossed planes. Number keys and the mouse wheel update the model immediately. The empty-hand model and its texture have been removed. There is no separate held-item selection or inventory copy, player body, third-person mode, or animation.

[FirstPersonRenderer.cs](./FirstPersonRenderer.cs) reuses the existing cube/plant geometry and block atlas, caches geometry/uploads until the selected item changes, and draws one small camera-space model after the world and before the HUD. A depth reset keeps nearby terrain from clipping it while preserving the world color; the model uses its own perspective, depth testing, backface culling, alpha testing, and point sampling. Its viewport is restored before the HUD. The existing held-item scale, rotation, perspective, and responsive lower-right placement are unchanged. Models are suppressed in every menu, inventory, and death screen, and whenever the selected slot is empty. The atlas, world generation, world save format, and FPS preferences are unchanged by this correction.

[MouseLookInput.cs](./MouseLookInput.cs) captures native relative motion through **SDL2 already shipped by MonoGame DesktopGL**, without an extra package. The previous loop repeatedly read an absolute OS cursor position, subtracted the window center, and called `Mouse.SetPosition` every frame. [MonoGame 3.8.6's implementation](https://github.com/MonoGame/MonoGame/blob/v3.8.6/MonoGame.Framework/Platform/Input/Mouse.SDL.cs) reads global OS position even after setting its cached window coordinates, leaving cursor warps and subsequent polls as separate operations. A stale or warp-induced non-center sample could therefore be applied as genuine movement at high rendering rates. A baseline run reproduced small rightward jumps at 240/360/uncapped FPS.

Gameplay now uses [SDL_GetRelativeMouseState](https://wiki.libsdl.org/SDL2/SDL_GetRelativeMouseState) once per input update: it returns accumulated motion since the previous read, and repeated reads without new motion return zero. [SDL relative mode](https://wiki.libsdl.org/SDL2/SDL_SetRelativeMouseMode) hides/confines the pointer without the game's per-frame recentering. Menus, inventory, focus loss, death, and exit release it and restore the prior menu cursor position. Capturing/releasing and window resizing discard transition motion; the first subsequent genuine movement is processed immediately. Buttons and scrolling retain MonoGame's existing input path. Angular sensitivity stays at 0.0025 radians per delta unit, with no dead zone, smoothing, elapsed-time multiplier, or FPS restriction.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move forward / left / backward / right |
| Mouse | Look around |
| Space | Jump while on the ground |
| Hold Left Shift | Crouch; slower movement, lower camera, and grounded edge protection |
| Hold left mouse button | Mine the outlined block; release to discard progress |
| Right click | Place one item from the selected hotbar slot |
| 1-9 (or numpad) | Select the corresponding hotbar slot |
| Mouse wheel during gameplay | Select hotbar slots with wrapping in both directions |
| E | Open / close inventory |
| Left / right click in inventory | Move whole stacks / split or place one item |
| Left click outside inventory panel while holding items | Drop the entire held stack; inventory stays open |
| F3 | Toggle player coordinates, chunk coordinates, approximate FPS, seed, chunk count, and dimensions |
| Escape | Open pause / resume; close inventory if its held stack can return safely |
| Pause menu Resume | Resume and capture the mouse |
| Main/pause menu Settings | Open the shared FPS slider and Uncapped FPS toggle |
| Settings Left/Right, Home/End | Change the focused FPS slider by one / select its minimum or maximum |
| F10 or gamepad Back | Save the active world and quit |

Block interaction reaches six blocks from the camera. Each RMB press attempts one placement. Placement requires an empty cell inside the world; solid blocks cannot overlap the player's body. Raycasts target a plant's voxel cell, rather than individual visible texels or crossed planes. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams.

Block edits, health, collected inventory, and uncollected items persist together. Infinite terrain, crafting, tools, armor, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | Lifecycle, state transitions, save/autosave/exit orchestration, input, and renderer lifetime |
| [GameSettings.cs](./GameSettings.cs) | Shared FPS preferences, bounded loading, and atomic application-settings persistence |
| [FrameTiming.cs](./FrameTiming.cs) | Sleeping render limiter and independent fixed simulation clock |
| [SettingsLayout.cs](./SettingsLayout.cs) | Responsive slider/toggle/Back geometry and integer slider conversion |
| [FirstPersonRenderer.cs](./FirstPersonRenderer.cs) | Inventory-driven cube/plant selection, empty-slot suppression, cached geometry, and isolated camera-space rendering |
| [MouseLookInput.cs](./MouseLookInput.cs) | Consumed SDL relative deltas and capture-transition flushing using the existing native SDL library |
| [GameState.cs](./GameState.cs) | Main menu, creation, selection, rename, deletion confirmation, gameplay, inventory, pause, and death states |
| [MenuController.cs](./MenuController.cs) | Atlas dirt background, title, size selector, world management layouts, navigation, and feedback |
| [WorldSave.cs](./WorldSave.cs) | Saved state and isolated session, one-time death transfers, respawn, and manual drop orchestration |
| [WorldSerializer.cs](./WorldSerializer.cs) | Versioned binary format, validation, and checksums |
| [WorldSaveManager.cs](./WorldSaveManager.cs) | Per-user world directories, header listing, safe rename/deletion, folder opening, and atomic replacement |
| [Camera.cs](./Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Player.cs) | Health, player bounds, movement, crouch support checks, gravity, jump, collision, and respawn |
| [FallTracker.cs](./FallTracker.cs) | Vertical apex/landing tracking and distance-only fall damage |
| [HealthHud.cs](./HealthHud.cs) | Pixel-art full/half/empty hearts above the hotbar |
| [Blocks.cs](./Blocks.cs) | Stable IDs, centralized block properties, mining durations, shapes, and face textures |
| [Items.cs](./Items.cs) | Item IDs, block-item mapping, and validated immutable stacks |
| [Inventory.cs](./Inventory.cs) | 36-slot storage, stacking, cursor operations/safe transfers, and accumulated wheel selection |
| [InventoryScreen.cs](./InventoryScreen.cs) | Scaled slot layout, point-sampled icons/counts, and mouse interface |
| [MiningController.cs](./MiningController.cs) | Target-bound elapsed mining, drops, resets, and inventory placement |
| [DroppedItems.cs](./DroppedItems.cs) | Entity state, gravity/collision, pickup delays, spatial merging, insertion remainders, and validation |
| [DropPlacement.cs](./DropPlacement.cs) | Nearby clear spawn positions for deliberate item drops |
| [DroppedItemRenderer.cs](./DroppedItemRenderer.cs) | Batched cube/cutout item rendering with reusable GPU capacity |
| [World.cs](./World.cs) | Finite world, chunk lookup/invalidation, safe reads/edits, placement, and collision queries |
| [WorldSize.cs](./WorldSize.cs) | Supported per-world size options and dimension validation |
| [Chunk.cs](./Chunk.cs) | Compact local block storage, world bounds, and mesh revision |
| [TerrainGenerator.cs](./TerrainGenerator.cs) | Unchanged seeded height generation, grass/dirt/stone layering, and a single bedrock layer |
| [WorldDecorator.cs](./WorldDecorator.cs) | Deterministic trees and plants after base terrain generation |
| [BlockRaycaster.cs](./BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./TextureAtlas.cs) | ContentManager atlas loading, grid tile rectangles, and texel-center UVs |
| [Content/terrain.png](./Content/terrain.png) | Editable 256 x 256 block texture atlas |
| [ChunkMeshBuilder.cs](./ChunkMeshBuilder.cs) | Shared cube/cross geometry for chunks and small items, with chunk opacity checks |
| [ChunkMesh.cs](./ChunkMesh.cs) | Per-chunk GPU buffer, revision tracking, uploads, and disposal |
| [WorldRenderer.cs](./WorldRenderer.cs) | Chunk frustum culling, alpha testing, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, save confirmation, and cached F3 debug text |
| [PixelFont.cs](./PixelFont.cs) | Shared bitmap lettering for the HUD and menus |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |
| [PersistenceChecks.cs](./PersistenceChecks.cs) | Edited-world restoration, corruption validation, safe paths, and failed-save checks |
| [DecorationChecks.cs](./DecorationChecks.cs) | Block definitions, deterministic decorations, bounds, cutout geometry, interactions, and legacy/new save restoration |
| [WorldManagementChecks.cs](./WorldManagementChecks.cs) | All-size generation, indexing, bounds, canopies, restoration, header-only metadata, and safe management |
| [InventoryChecks.cs](./InventoryChecks.cs) | Mining, item physics/pickup, accounting, mouse operations, placement, migration, and persistence checks |
| [SurvivalChecks.cs](./SurvivalChecks.cs) | Health/hearts, fall distances, death accounting, respawn, deliberate drops, delays/merges, v1/v2 compatibility, crouching, and wheel checks |
| [PresentationChecks.cs](./PresentationChecks.cs) | FPS range/override, settings persistence/corruption, render-rate independence, crouch camera, and model geometry/visibility/layout checks |
| [MouseInputChecks.cs](./MouseInputChecks.cs) | Stationary/moving high-frequency input, unchanged sensitivity, consumed deltas, and capture/release resets |

All chunks remain loaded for the active world. There is no chunk streaming, multithreaded generation, water, ores, crafting, tools, armor, tree growth, or vegetation growth.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The **56 check groups** include FPS defaults and all integer slider values, uncapped override/value retention, settings reload/validation/write failures, identical fixed simulation outcomes at 5/30/60/144/240/360/997 rendered FPS, reduced crouch camera height, and selected cube/plant geometry, empty-slot suppression/switching, menu visibility, UVs, and layout. Mouse regressions check zero stationary rotation at 60-2000 updates/second, identical movement/sensitivity across rates, no replayed deltas, one-unit motion without a dead zone, and capture/release/resize flushing without first-sample latency. Health, fall tracking, death/respawn, dropped items, inventory accounting/placement, persistence, v1/v2 compatibility, crouch support/jumps, generation/restoration, geometry, atlas, checksums, atomic saving, management, and world-size checks remain included. A bounded non-seekable stream verifies that metadata reading stops before block data. Persistence fixtures stay under the test build and are cleaned up; they never touch real saves. A failing check returns a nonzero exit code. These checks do not open a window or exercise operating-system input.

A separate scripted DesktopGL run verified actual GPU heart states at 1280x720, 640x480, and 320x240; cube/plant item cutouts and terrain depth; input-path inventory drops and wheel selection; diagonal crouch edges and jumping; fatal falls, death input suspension and cursor transfers; respawn; failed/successful death saves and automatic respawn on reload; a real pre-Milestone-6 v2 save; autosaving health; and world creation, selection, renaming, and confirmed deletion. It used isolated temporary saves and synthetic input, rather than physical keyboard/mouse playtesting.

The FPS update's scripted DesktopGL checks covered Settings entry/Back, every keyboard slider value and mouse dragging/toggling, fresh preference loading, paused-world suspension, input-path movement/mining, foreground-depth obstruction, preserved terrain, and HUD clearance. Short render-only samples measured approximately 60/144/360/5 FPS at those menu caps and above 60 when uncapped. These samples establish limiter behavior, not sustained performance with normal or Large terrain.

The corrective update compared cube/log/leaf/plant renders pixel-for-pixel against pre-correction images at 320x240, 640x480, 1280x720, and 2560x1080. Empty slots draw no pixels, switching back restores the same item render, and all menus suppress models. A native Windows/SDL run verified zero stationary rotation and exact rotation from injected movement at 60, 144, 240, 360, and uncapped (roughly 2800 achieved) FPS. It also rejected 10,000 stale absolute-position samples and exercised inventory, pause, Settings, injected SDL focus events, death, and respawn capture transitions. An isolated run of the previous build reproduced stationary rightward jumps at 240/360/uncapped, while 60/144 stayed still during that sample.

Manual testing is still needed with physical mice at different polling rates/DPI, real Alt-Tab/focus changes and cursor restoration, Linux/macOS relative capture, and sustained high-FPS play in all sizes. Also exercise slider/mouse-wheel controls, held-item alignment on other displays/aspect ratios, irregular crouch edges, falls across menu transitions, death-drop recovery, many simultaneous items, inventory placement/accounting, save-on-window-close, filesystem errors, and folder opening. Scripted injected input is not physical mouse playtesting. Generation, initial mesh construction, and saving remain synchronous, so Large worlds can pause briefly during those operations.

