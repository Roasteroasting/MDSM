# MDSM

A finite-world, early-Minecraft-inspired survival sandbox in C# and MonoGame DesktopGL. The game has finite worlds in four sizes, terrain/decorations/water, bounded chunk rendering, persistent inventories, shaped crafting, tiered tools and durability, underground ores, chests, furnaces, bows/arrows, passive mobs, health/death/respawning and food for healing, leaf decay, sapling growth, and sheep shearing. New worlds start with empty inventories. Caves, Lava/Obsidian, farming, breeding and babies, underground hostile mobs, Armor, building structures, Buckets and specialized Furnaces are implemented. See [FINAL-SURVIVAL.md](./FINAL-SURVIVAL.md) for current recipes, balance, IDs, persistence and verification. See [SURVIVAL-PROGRESSION.md](./SURVIVAL-PROGRESSION.md) for the complete Milestone 11 recipes, IDs, mechanics, measurements and verification report.

Terrain and water geometry now use bounded camera-centered residency with prefetching, hysteresis and incremental rebuilds. The complete finite world stays in CPU memory. A shared 160-block view-space far plane and 80-144-block fog transition apply to terrain and dynamic entities. F3 includes buffer estimates, rendering counts and CPU timing diagnostics. See [PERFORMANCE.md](./PERFORMANCE.md) for the investigation, measured GTX 970 results, regression coverage and reproducible benchmark commands.

## Build and run

Current survival controls: **E** opens inventory/crafting and four matching Armor slots. **RMB** with a Hoe tills clear Dirt/Grass, Seeds plant tilled soil, Wheat feeds adult animals for breeding, an Empty Bucket collects a fluid source, and a filled Bucket places a source. RMB opens/closes Doors and Gates. Torches prevent nearby hostile spawning; they do **not** emit light. The existing movement, combat, mouse, hotbar, HUD, screenshots and Settings controls remain.

Farmland hydrates within horizontal radius two and vertical distance one of source/flowing Water. Wheat has seven stages and takes 10–40 wet gameplay minutes to mature; dry soil pauses growth. Tall Grass drops a Seed 10% of the time. Immature crops return one Seed; mature crops guarantee one Wheat plus 1–4 Seeds. Bread heals five health; Rotten Flesh instead causes one Armor-bypassing damage.

Animals fed Wheat seek a same-species mate within eight blocks, produce one baby, then wait five minutes before breeding again. Unpaired breeding mode expires after one minute. Babies have small bodies/adult-sized heads, no drops or breeding/shearing, and mature after 15–45 active minutes. Breeding has a bounded extra population allowance and reserves pending natural replacements.

Zombies (20 HP), Skeletons (20 HP) and Spiders (16 HP) spawn in clear spaces below Y=50, 24–128 blocks from the player, with a global cap of eight. Torches exclude spawning within 15 blocks in 3D. Distant behavioral AI stops beyond 32 blocks; hostile despawning beyond 128 remains immediate. Lava causes two damage per second and destroys ordinary dropped items. Water/Lava contact makes Obsidian; dropped Obsidian blocks/tools/Armor survive Lava. Armor reduces damage by 3% per point, up to 78%, and wears on accepted damage events.

See [FINAL-SURVIVAL.md](./FINAL-SURVIVAL.md) for all recipes, Armor values, machine rules and limitations. New checks run with `dotnet run --no-build -- --completion-test`; controlled CPU/state profiling uses `--completion-performance-test`. The existing `--self-test`, `--graphics-test` and gameplay benchmarks include the new systems.

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The MonoGame build task restores the MGCB tool from the existing local tool manifest and compiles [Content/Blocks/terrain.png](./Content/Blocks/terrain.png) through [Content/Content.mgcb](./Content/Content.mgcb). The resulting atlas texture is copied into the build output and owned by the game's ContentManager. The small UI font remains generated in code and needs no installed system fonts.

## Placement and building
Six Glass blocks arranged in two rows of three craft 16 Glass Panes. Panes stack to 64, are available in Creative, and connect horizontally to other panes and solid full blocks. Their collision and targeting follow the thin connected shape. Like ordinary Glass, panes drop nothing when mined.
Torches, Tall Grass and Flowers cannot be placed against block undersides. Valid floor and wall placement remains available under the existing support rules. Door edges use narrow strips of the existing artwork, while front/back textures remain unchanged.
Both Survival and Creative apply a 150 ms cooldown after each successful block break. Holding LMB continues mining/breaking after the cooldown; Creative's first break is still instant when ready. Other mining times and tool rules are unchanged.


## Settings and frame rate

Settings now also includes **Difficulty** (Peaceful, Easy, Normal, Hard) and **Game Mode** (Survival, Creative). Both are application-wide preferences saved alongside FPS settings and apply immediately to the active world. Normal/Survival are the defaults. Fall, Lava, hostile attacks and drowning use 0.5× damage on Peaceful/Easy, 1× on Normal and 1.5× on Hard, applied before unchanged Armor protection. Fractional health is retained. Rotten Flesh still causes exactly one Armor-bypassing damage in Survival. Peaceful immediately removes all hostiles and prevents their spawning; passive populations are unchanged.

Creative enables collision-aware flight: **WASD** moves horizontally, **Space** ascends and **Left Shift** descends. Hold either **Left Ctrl** or **Right Ctrl** for 3x flight speed (12.9 blocks/second along one axis); releasing Ctrl restores normal flight speed. Ctrl does not increase movement speed in Survival. Health is retained but hidden; damage, drowning and damage-related Armor wear are disabled. Mining is instant, including Bedrock, with no block drops or mining durability cost; block placement does not consume items. Chests, Furnaces, crafting, Doors and Gates retain their interactions. Hostiles can spawn outside Peaceful but do not pursue or attack a Creative player. Switching back restores Survival behavior without changing inventory or equipped Armor.

In Creative, **E** shows a separate item browser beside the ordinary inventory. Use the wheel over that panel to scroll, **LMB** to grant one item and **Shift+LMB** to grant its actual maximum stack. Equipment is fresh and fully durable; full slots are never overwritten. All granted items remain ordinary usable items after switching to Survival and persist in that world's inventory. New worlds still start empty.

Underwater breathing begins only when the logical head position is below the Water surface. Ten bubbles appear above Armor (or above the health row without Armor), deplete over 15 gameplay seconds, then drowning causes two health points per second on Normal before Armor. Surfacing immediately restores air and hides the meter; paused gameplay does not advance it. Creative does not consume breath.

Wheat uses four vertical cutout planes in a **#** pattern, with seven stage-dependent heights. Flowers, Tall Grass, Saplings and Wheat have smaller selection bounds and remain non-solid. New worlds have abundant, patchy Tall Grass and occasional two-block plants: either half breaks the whole plant and rolls once for Seeds (20%, versus ordinary grass's 10%). Both halves persist as blocks; existing worlds are not regenerated. Walking automatically steps up ledges up to 0.6 blocks, including Farmland's 1/16-block lip, when headroom permits. Full-block walls and 1.5-block Fences/Gates remain barriers.

These changes retain world save format 10 and existing block/item IDs; double-height grass adds block IDs 58/59 and terrain atlas tiles 51/52. Difficulty and Game Mode use the existing application settings file, not a new world-save extension. Infinite worlds are not implemented.

**View Bobbing** is enabled by default. Toggle it in Settings to disable the walking-only camera motion. Bobbing uses a smooth three-block travel cycle (about 1.43 cycles/second at normal speed), broader horizontal/vertical sway, and fades fully to rest when stopping or airborne. It shares the existing application settings file; logical aiming, collision and projectile rays do not bob.

Choose **Settings** from the main menu or the pause menu. Both entry points share the same preferences. **Back** or Escape returns to the screen that opened Settings; returning to the pause menu keeps gameplay suspended and the mouse cursor available.

Settings also contains **Controls** and **About**. Controls lists the implemented gameplay, inventory, and menu bindings; it does not change them. About identifies MDSM (Mojang Don't Sue Me), its author Mark Arnold (Roasteroasting), and explicitly acknowledges that the codebase was written using GPT-6.1 Sol and the game textures were AI-generated. Each screen's Back button or Escape returns to Settings, preserving its main-menu or pause-menu origin and all FPS preferences. Text wraps with the window; use the mouse wheel, Up/Down, Page Up/Page Down, or Home/End to scroll longer information screens.

The **FPS Cap** slider supports every whole number from **5 to 360**, defaulting to **60**. Click or drag it, or focus it with Tab/Up/Down and use Left/Right for one-FPS changes; Home/End select the endpoints. The current numerical value is shown above the slider. **Uncapped FPS** defaults off and can be clicked or toggled with Space/Enter when focused. Changes apply immediately in gameplay and every menu. While uncapped, the slider remains editable and retains its value, but does not limit rendering until uncapped is turned off.

Preferences are application-wide in `%LOCALAPPDATA%/MDSM/settings.json` on Windows, alongside the existing `Worlds` directory. The equivalent per-user application-data location is used elsewhere. They are independent of world saves and survive world switches and application restarts. Missing settings use defaults; malformed/oversized files fall back safely, and out-of-range integers are clamped. Changes save after a short idle delay and on Back/exit using a temporary file and atomic replacement. Write failures keep the selected preferences active for the current session and display an error without replacing the previous valid file.

[FrameTiming.cs](./Core/FrameTiming.cs) separates rendering from a **120 Hz fixed simulation clock**. The render limiter uses stopwatch deadlines and sleeping, compensating for small timer overshoots without busy-waiting and rebasing after slow frames. Uncapped mode skips the limiter. MonoGame's outer fixed-step timing and presentation synchronization are disabled so they do not impose the former 60 FPS ceiling; its inactive sleep is also disabled so the same preference applies throughout menus and focus changes. See [MonoGame's timing loop](https://github.com/MonoGame/MonoGame/blob/v3.8.6/MonoGame.Framework/Game.cs) for the outer-loop behavior. There is no additional VSync setting.

The simulation consumes elapsed time in fixed steps, including multiple steps per rendered frame at low FPS. Player physics, fall tracking, mining, item motion, pickup delays, and merging therefore use the same simulation rate at every render cap. Autosave remains based on active elapsed time. Jump presses are retained until the next simulation step; mouse look uses mouse displacement once per rendered frame. Menus clear fractional simulation time without resetting an in-progress fall. MonoGame's existing half-second maximum elapsed-time safeguard remains in place for exceptional stalls. A chosen cap is a rendering target/upper limit, not a promise of hardware performance; operating-system scheduling, drivers, and world complexity affect achieved FPS.

## Screenshots and HUD visibility

Press **F2** once to save a lossless PNG of the final composed frame, including whichever world, held item, HUD, debug overlay, inventory, or menu is visible. Holding F2 does not repeat captures. Screenshots use the actual rendered client-area resolution, without scaling, cropping, filtering, or color changes. Capture runs after all draw passes, before presentation, using MonoGame's backbuffer readback and PNG encoder. It does not reconstruct the scene or temporarily enable hidden HUD elements.

Screenshots are stored in **`%LOCALAPPDATA%/MDSM/screenshots/`** on Windows, alongside `Worlds` and `settings.json`; the equivalent per-user application-data location is used elsewhere. The directory is created on the first capture. Filenames use `yyyy-MM-dd_HH-mm-ss_fff.png`, with numbered suffixes for collisions and exclusive file creation to prevent overwriting. Write failures display an error using the existing menu/gameplay notification and leave gameplay running; an incomplete file from a failed encoding is removed. GPU readback and PNG writing are synchronous only when requested, so taking a screenshot can briefly pause rendering at large resolutions.

Press **F1** to hide or restore the gameplay HUD: hotbar, selected-item name, hearts, Armor, breath bubbles, crosshair, targeting/mining feedback, held-item model, notifications, and debug overlay. This changes rendering only; movement, mining, placement, and hotbar selection remain active. Inventory and menu interfaces remain usable and visible. HUD visibility resets to visible on application launch and is not saved.

**F3** retains its independent debug toggle. Hiding the HUD preserves the F3 state; restoring it reveals debug again if enabled. F3 can also change that state while the HUD is hidden. The former permanent upper-left controls panel is removed; bindings are available in **Settings → Controls** instead.

## Finite world and seed

Each world retains its creation size. **Normal** is the default; click the size field or use Left/Right/Enter while focused to cycle all four options. Worlds stay entirely in memory. Chunks are **16 x 128 x 16**, with one byte per block; metadata and GPU buffers require additional memory.

Selecting **Massive** displays a compact performance notice on the creation screen. It is informational: creation requires no extra confirmation. The notice hides immediately when another size is selected.

| Size | Dimensions (X x Y x Z) | Chunk grid | Total chunks | Raw block storage |
| --- | --- | --- | --- | --- |
| Small | 256 x 128 x 256 | 16 x 16 | 256 | 8 MiB |
| Normal | 512 x 128 x 512 | 32 x 32 | 1,024 | 32 MiB |
| Large | 1024 x 128 x 1024 | 64 x 64 | 4,096 | 128 MiB |
| Massive | 2048 x 128 x 2048 | 128 x 128 | 16,384 | 512 MiB |

Valid Y coordinates are 0-127; X/Z are zero through width/depth minus one. All indexing, boundaries, chunk bounds, metadata and F3 values use these dimensions. Massive worlds require substantially more RAM/GPU memory than raw storage alone; use a 64-bit environment. There is no chunk streaming or new render-distance setting.

Choose **Create New World**, enter a name and optionally a signed 32-bit seed. Blank seeds are randomized. The same seed/size produces the same complete world. [TerrainGenerator.cs](./Worlds/TerrainGenerator.cs) combines broad 280-block hills, 110-block variation and restrained 44-block detail in global coordinates. Natural land heights are clamped to **Y=63-114**, with most terrain near Y=88 and gentle neighboring gradients. Grass covers 3-5 dirt layers, deeper stone and one complete bedrock layer at Y=0. Water beds may extend below 63. New worlds include branching narrow caves, occasional entrances, protected low Lava pools and a modest exposed-ore bonus; existing saved terrain is never regenerated.

[World.CreateNew](./Worlds/World.cs) generates terrain, water features, then [WorldDecorator.cs](./Worlds/WorldDecorator.cs) adds trees and vegetation. Their existing per-area seeded density, varying 4-6-block trunks, 5 x 5 canopy limits, clearance checks and spawn clearing scale across all sizes. Submerged beds do not support vegetation. All generation changes apply only to new worlds. Loading restores saved bytes without running generation.

The player spawns safely near the center with a dry, unobstructed neighborhood. Blocks outside the finite world return air; edits are rejected. No blocks are stored above Y=127. Existing void recovery below Y=-16 resets movement/fall tracking while preserving health.

New-world CPU generation and the initial safe save run on a worker task while the menu displays phase progress. Input is suspended during creation; closing waits for creation to finish. Initial chunk meshes are prepared in batches with a visible counter on the main thread before gameplay. The existing GPU buffers, exposed-face meshing, alpha-tested vegetation, frustum culling, water pass and selective edit rebuilds remain. All graphics operations stay on the main thread. Loading, manual saving and autosaving remain synchronous. A one-byte-per-column solid-height cache avoids repeated vertical surface scans and is maintained by edits/restoration.

### Existing-world height expansion

Legacy **256 x 64 x 256** worlds become **Small**, and **512 x 64 x 512** become **Normal**. Each stored chunk's lower 64 layers is restored at exactly the original coordinates; Y=64-127 is initialized to air. Terrain/decorations are never regenerated or shifted. Player pose, inventories, health, water, mobs, original population targets, outstanding respawn delays, dropped items and falling objects remain intact according to their save version. The next save writes height 128. Header-only listings identify the mapped size without reading blocks.

The obsolete **128 x 64 x 128 (Legacy Small)** size is unsupported and appears unavailable with a load error. It is not offered for creation. Other malformed dimensions are rejected. The existing version-7 format already stores dimensions explicitly, so no version/layout change was needed. Older game builds do not understand the new dimensions.

## Ponds, rivers and lakes

All sizes generate ponds. **Large and Massive additionally generate rivers and rare large lakes**. [LakeGenerator.cs](./Worlds/LakeGenerator.cs) uses deterministic, bounded attempts and validates entire footprints before carving:

| Feature | Small | Normal | Large | Massive | Shape/depth |
| --- | --- | --- | --- | --- | --- |
| Ponds | 4 | 16 | 64 | 256 | Irregular stretched basins, radius 3-7 (up to 9 in larger sizes), depth 1-3 |
| Rivers | 0 | 0 | 4 | 12 | Curving routes roughly 90-220 blocks long, varying widths, depth 1-3 |
| Large lakes | 0 | 0 | 2-3 | 6-9 | Compound organic basins; typical spans about 50 / 100 blocks, rare exceptional spans about 100 / 150; depth scales with width |

Pond and river algorithms, dimensions and density budgets are unchanged. Major lakes use a separate compound-basin generator: overlapping smooth lobes, broad coves, gentle coordinate warping, graded shallows and deeper interiors. Exceptional dimensions have a **1-in-64 chance per lake**, so most worlds contain none. Typical depths are about 4-7 blocks in Large and 9-13 in Massive; exceptional lakes are proportionally deeper. Deterministic ranked candidates favor broad lowlands; terrain-aware excavation and bounded shoreline grading replace the old five-block whole-footprint relief restriction. Lake counts are actual population targets, with bounded candidate searches; genuinely unsuitable terrain can still produce fewer. Reservations follow modified terrain rather than oversized bounding rectangles, preserving room for ponds and rivers. Features avoid finite edges and the protected spawn area. Beds use gravel/dirt and occasional sand, with mixed beaches and grassy banks. Sand/Gravel has solid support, and decoration runs afterward so plants cannot appear underwater. See [LAKES.md](./LAKES.md) for size/rarity measurements, implementation details and verification results.

Basins have solid rims/beds and are filled with source blocks, making them stable initially. Dry air above water is excluded from the initial frontier because water cannot flow upward; flow rules are otherwise unchanged. Existing worlds receive no new water features.

Water is non-solid, cannot be mined or held in inventory, and does not obstruct targeting blocks or mobs behind it. Place a solid block into water to displace it. Digging into a bank or lake bed opens a path for water. Sources continuously supply downward flow first, and supported water spreads horizontally with levels 1-7, losing one level per horizontal step. Falling columns reach their floor before spreading sideways. Water routes around obstacles within that reach, never travels upward, and flowing water recedes when no valid supply remains. Two adjacent horizontal sources can regenerate a source only over solid support or another source; ordinary spreading never creates sources on its own. Original lake basins are sealed and source-filled, so they remain stable until edited.

[FluidSimulation.cs](./Worlds/FluidSimulation.cs) maintains a deterministic, deduplicated frontier with a **0.2-second water tick** and a **2,048-cell budget per tick**. Decisions use a snapshot and apply together, preventing update order from accelerating flow. Creation/loading scans water-bearing chunks once to schedule active flow and exposed frontiers; stable lakes then do no work. The queue uses one bit per block only when needed. Updates pause with gameplay. Fluid-only changes rebuild affected water geometry, without rebuilding opaque terrain or scanning the entire world each frame.

[WaterRenderer.cs](./Rendering/WaterRenderer.cs) caches water faces per chunk, frustum-culls chunks, sorts visible faces back-to-front, and batches them into a reusable GPU buffer. Shared corner heights join differing flow levels across chunk boundaries; sources are substantially full, weak flows are shallower, and falling columns join vertically. Internal water faces and faces against opaque terrain are omitted. The pass runs after opaque terrain/items/mobs and before held items/HUD, with point sampling, alpha blending, depth testing and **no depth writes**. It restores texture bindings and graphics states, retaining the screenshot binding fix. [Content/Blocks/water.png](./Content/Blocks/water.png) is a separate editable 16 x 16 tile; existing terrain artwork is unchanged. Edit, rebuild and restart to change it. Its built-in image-generation prompt and one-time preparation are recorded in [Content/Artwork/water-artwork.md](./Content/Artwork/water-artwork.md).

Entering water reduces horizontal movement and falling speed and cancels accumulated fall damage. **Hold Space to swim upward**; normal jumping outside water is unchanged. Passive mobs slow down, float, and can hop against a shoreline while wandering to avoid being trapped. There are no water currents, drowning, oxygen, underwater effects, buckets, or oceans.

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

### Binary format, version 10

[WorldSerializer.cs](./Persistence/WorldSerializer.cs) uses little-endian fields and no compression. Each save contains 8, 32, 128, or 512 MiB of block data, depending on size, plus its header, inventory, dropped items, and checksum:

| Field | Representation |
| --- | --- |
| Signature | Four ASCII bytes: `MDSM` |
| Format version | 32-bit integer, currently 10; versions 1-9 remain readable |
| Dimensions | Three 32-bit integers: X/Z=256, 512, 1024, or 2048; Y=128. Legacy Y=64 is supported only for widths 256/512 |
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
| Passive mob count | 32-bit integer, 0-4096 combined across species |
| Passive mobs | Each entry: 8-bit species (0 pig, 1 cow, 2 sheep, 3 chicken), three 32-bit feet-position floats, 32-bit yaw float, 32-bit health integer; 21 bytes per mob |
| Population targets | Four 32-bit counts in pig/cow/sheep/chicken order; living plus pending must equal each target |
| Pending respawn count | 32-bit integer, 0-4096 |
| Pending respawns | Each entry: 8-bit species and 64-bit remaining gameplay seconds, finite and between 0 and 60 (9 bytes) |
| Pending gravity column count | 32-bit integer, 0 to X * Z |
| Pending gravity columns | Unique 32-bit column indices, Z * width + X, in processing order |
| Active falling-block count | 32-bit integer, 0-4096; added in version 7 |
| Active falling blocks | Each entry: 8-bit block ID (Sand/Gravel only), 32-bit X and Z, 64-bit feet Y, 64-bit downward speed (25 bytes); source voxel is already empty |
| Equipment metadata | 32-bit count, then owner byte (0 inventory / 1 dropped item), 32-bit slot/entity index, 32-bit remaining durability, 16-byte instance GUID (25 bytes per equipment record) |
| Block containers | 32-bit count, then X/Y/Z, block ID, slot count, stacks and three 64-bit progress/burn/total-burn timers; each stack is item ID + quantity + durability + GUID (23 bytes) |
| Active arrows | 32-bit count (0-256), then position/velocity, age, damage and originating Bow GUID (48 bytes per arrow) |
| Natural leaves | 32-bit count and sparse 32-bit cell keys; player-placed Leaves are absent |
| Vegetation timers | Two counted lists (leaf decay, saplings), each entry a 32-bit cell key and 64-bit remaining gameplay seconds |
| Sheared sheep | Count, then non-pig mob-array index and 64-bit remaining wool-regrowth seconds |
| Version-10 survival state | Precise health; four Armor stacks; sparse structure orientation/open/support data; crop timers and pending hydration; animal age/love/cooldowns/bred flags; hostile poses/health; hostile-projectile ownership indices |
| Checksum | SHA-256 of all preceding bytes, 32 bytes |

Version 9 adds sparse natural-Leaf ownership, pending decay/growth deadlines, and sheared-Sheep regrowth state after the unchanged version-8 extension. All block/item IDs remain stable: new Sapling block/item=31, Apple=284, Shears=285, Cooked Porkchop=286. Older formats 1–8 and supported legacy height migration remain readable. Leaves in older saves are conservatively treated as player-placed/non-decaying because provenance was not stored; no terrain is changed or regenerated. Their inventories retain all item quantities, including stacked-food migration. Save paths, streamed block IO, checksums, atomic replacement, and GPU mesh residency are unchanged. Save/exit's **World Saved** menu message now expires after **8 seconds** of menu update time.

Loading validates the signature, version, supported dimensions, name, finite player pose, health range, block/item IDs, slot and entity counts, stack quantities, finite bounded item positions/velocities/delays, exact length, and checksum. Pickup delays must be between zero and one second. Allocation follows validated dimensions and bounded entity counts rather than arbitrary file lengths. Chunk blocks are streamed through incremental SHA-256 on writing/reading; no additional full-world payload array is staged. Migration reads only each stored 64-layer chunk and zero-fills its upper half. Stored blocks are copied directly into empty chunk storage; terrain is never regenerated over saved edits. Chunk meshes are rebuilt from restored blocks. Unknown versions, missing files, and corrupt data produce a menu error. Listing obtains size from the unchanged small header without deserializing block or inventory arrays; full checksum validation happens on load.

Milestone 7 writes **format version 4**, appending a pig count and pig records after the existing version-3 health integer and before the checksum. Versions 1-3 remain readable. Versions 1 and 2 default to 20 health; versions 2 and 3 retain their exact inventory and dropped items without a stock refill. Version 1 has the same header and raw block layout, followed immediately by its checksum, and receives the existing 13-block starter inventory on first load. **Older worlds receive no pigs and no starting porkchops.** An empty pig list is saved on upgrade, so later loads never silently repopulate them. Milestone 3/4 saves display as Normal; later Small/Large saves retain their own dimensions. All older terrain, seed, names, player pose, canopies, and bottom layers are preserved. Block IDs, GUID directories, atomic replacement, and world generation are unchanged. Earlier game builds cannot read version-4 saves.

[WorldSaveManager.cs](./Persistence/WorldSaveManager.cs) writes `world.dat.<random GUID>.tmp` in the same world directory, flushes it to disk, then atomically replaces `world.dat` (or renames it for the first save). Failed writes leave the previous valid save intact. Temporary files left by an interrupted process are ignored. No background save thread is used: save/load can briefly pause the UI; new-world generation uses the progress workflow above. Player position/orientation, health, all 36 inventory slots, selected hotbar slot, and uncollected items including remaining pickup delays persist per world. Partial mining progress, player velocity/fall tracking, F3 visibility, and cursor UI state are not serialized; cursor items must return safely before saving. Loading a save with zero health respawns the player with full health and its saved empty inventory, retaining the already-saved death drops without creating them again.

Milestone 8 keeps **version 4 and the exact binary layout**. New block bytes 14-22 encode all water states directly; no extra fluid array/tail is required. Loading schedules the restored fluid frontier once and resumes simulation without creating lakes or sources through generation. Existing v1-v4 saves retain terrain and all player/entity data. Water-free saves retain their block bytes exactly. Earlier game builds do not recognize water IDs and cannot load worlds containing them. Queue order, fractional tick time and temporary mesh data are not saved; the saved fluid configuration settles to its valid equilibrium after loading.

Milestone 9 writes **version 5**, adding a species byte to each former pig record and using one combined mob count. Version-4 records remain 20-byte pig records and load as pigs without changing pose or health. Versions 1-3 still load with no mobs. Loading any older world never adds cows/sheep/chickens or replenishes inventory, including White Wool: existing raw porkchops and all other slots remain intact. Existing water bytes, terrain, player health/pose, dropped items and metadata retain their layouts. Species IDs and species-specific health/bounds are validated; the combined count is limited to 256. Older builds cannot read version-5 saves. GUID paths, atomic replacement, and storage locations are unchanged.

The population/gravity update writes **version 6**, appending per-species targets, independent remaining respawn delays, and pending gravity columns after the unchanged version-5 mob records. Saves made before this update remain readable: each species' surviving count becomes its initial target once, with no pending replacements inferred for historical deaths. Thus pig-only worlds stay pig-only, and empty older worlds stay empty. Saving persists that baseline for future deaths; loading never recalculates a version-6 target from survivors alone. Pending timers resume without advancing while the game is closed or paused. Legacy terrain is restored exactly without activating a global gravity scan; later support edits enable gravity in their affected columns. New queued gravity edits survive saving before settlement. The old header-only metadata reader, GUID directories, checksum, and temporary-file/atomic replacement strategy remain intact. Earlier builds cannot read version-6 saves.

Smooth falling writes **version 7**, appending active Sand/Gravel types, columns, continuous heights, and velocities after the version-6 gravity queue. Loading resumes those objects without recreating their source blocks, changing their types, settling them prematurely, or creating item drops. The reader rejects invalid counts, block types, non-finite/out-of-range values, overlapping falling objects, and intersections with stored solid terrain. Version-6 queues now launch animated falls; versions 1-5 retain the conservative unchanged-terrain policy above. All existing world/player/mob/inventory/water fields and paths remain intact, and atomic replacement is unchanged. Earlier game builds cannot read version-7 saves.

## Editing block textures

All non-block item artwork and the Sapling icon now use [Content/Items/items.png](./Content/Items/items.png): a **256×256** editable PNG, with a **16×16 grid of 16×16 tiles**. Item tile indices are distinct from persistent item IDs. Tiles 0–27 contain the previous artwork copied pixel-exactly; 28=Sapling, 29=Apple, 30=Shears, 31=Cooked Porkchop. [The artwork notes](./Content/Artwork/refinements-artwork.md) list every tile and the built-in imagegen prompt for the four new icons. The former sheets remain uncompiled historical references. Edit the new atlas and rebuild normally; no runtime artwork generation occurs.

Open [Content/Blocks/terrain.png](./Content/Blocks/terrain.png) in an ordinary image editor. Keep the image at **256 x 256 pixels**, arranged as a **16 x 16 grid of 16 x 16 pixel tiles**. Tiles are numbered from 0 to 255 in row-major order: left to right, then top to bottom. Tile 16 begins the second row, and tile 255 is at the bottom right.

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
| 16 | Crafting Table top | (0, 16) |
| 17 | Crafting Table side | (16, 16) |
| 18 | Glass | (32, 16) |
| 19-21 | Iron / Coal / Diamond Ore | (48 / 64 / 80, 16) |
| 22-24 | Furnace top / side / front | (96 / 112 / 128, 16) |
| 25-27 | Chest top / side / front | (144 / 160 / 176, 16) |
| 28-255 | Unused, transparent | `(index % 16 * 16, index / 16 * 16)` using integer division |

The first four tiles were exported once from the original seed-173 algorithm and remain pixel-for-pixel unchanged. Milestone 4 artwork occupies tiles 4-14; its generation prompt and one-time preparation are recorded in [Content/Artwork/texture-artwork.md](./Content/Artwork/texture-artwork.md). White Wool occupies tile 15, documented in [Content/Artwork/passive-mob-artwork.md](./Content/Artwork/passive-mob-artwork.md); all other existing atlas pixels are unchanged. The PNG is the source artwork; builds and game launches never regenerate or overwrite it. Edit a tile, save the PNG, then run `dotnet build` followed by `dotnet run --no-build` (or simply `dotnet run`, which builds first). Restarting an existing build without rebuilding will continue to use its previously compiled texture. Replacing existing textures requires no C# changes.

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
| 24 | Crafting Table | 16 top, 17 sides, 7 bottom | Solid / opaque, mineable and placeable |
| 25 | Glass | 18 | Solid / clear cutout, no block drop |
| 26 | Iron Ore | 19 | Solid / opaque, requires Stone Pickaxe to harvest |
| 27 | Coal Ore | 20 | Solid / opaque, requires Wooden Pickaxe to harvest |
| 28 | Diamond Ore | 21 | Solid / opaque, requires Iron Pickaxe to harvest |
| 29 | Furnace | 22 top/bottom, 23 sides, 24 front | Solid / opaque, persistent processing inventory |
| 30 | Chest | 25 top/bottom, 26 sides, 27 front | Solid / opaque, persistent 27-slot inventory |
| 31 | Sapling | 28 (item icon also uses items tile 28) | Non-solid / cutout cross, Grass or Dirt support |

Blocks 1-13 and 23-31 have corresponding items and remain placeable when held in the hotbar. Water states are not inventory items. Bedrock is unbreakable in Survival through mining. Flowers/Tall Grass require Grass; Saplings also accept Dirt. Support removal returns flowers/Saplings, while Tall Grass drops nothing. Leaves retain solid collision. Sand and Gravel generate in lake beds/shorelines and underground deposits and fall when unsupported. Planks and Cobblestone remain available as items without generating naturally. White Wool does not generate as terrain; sheep provide it. It uses 1.25-second placeholder hand mining and drops itself. Crafting Tables take 2.5 seconds to mine by hand and drop themselves. Bedrock generates only at Y=0.

[FallingBlockSimulation.cs](./Worlds/FallingBlockSimulation.cs) queues only changed horizontal columns, deduplicates them, and checks at most 32 columns every 0.05 gameplay seconds. Each bounded bottom-up pass removes unsupported Sand/Gravel from storage and transfers it into a [FallingBlock](./Worlds/FallingBlock.cs) with continuous height and downward velocity. Blocks accelerate at 24 blocks/second squared, capped at 20 blocks/second. Swept collision finds the first solid surface even during a large elapsed-time step; falling stacks also support each other without passing through or changing order. Landing restores exactly one ordinary block of the same type, without creating item drops. Up to 4096 falling objects can be active; excess unsupported blocks stay queued in storage until capacity becomes available.

[FallingBlockRenderer.cs](./Rendering/FallingBlockRenderer.cs) translates cached full-size Sand/Gravel cube geometry into a reusable batch, using the existing atlas, face shading, point sampling, depth testing and fog. Render interpolation between the 120 Hz simulation steps keeps high-FPS movement smooth. Only launch/landing changes terrain geometry; animation does not rebuild chunk meshes. The pass runs before translucent water and restores graphics/texture state, including DesktopGL binding-cache invalidation. Menus freeze physics and the interpolated view. Saving and loading retains active motion; no new textures are needed.

Water does not support falling material: intermediate water cells remain intact and only the final destination displaces water, notifying the existing fluid queue. Placement into a moving cube is rejected without consuming an inventory item; placement below it can create new support. The finite Y=0 plane catches blocks if a fixture or edit removes bottom support. Generated Sand/Gravel still requires solid support at placement; lake shapes and shore materials remain unchanged, with no initial falling cascade or global activation of old terrain.

## Mining and dropped items

Tall Grass now drops nothing. Leaves normally roll independent drops: **5% Sapling, 2% Stick, 0.5% Apple**; multiple drops are possible. Shears collect the Leaf block instead. Naturally generated/grown Leaves gradually decay over randomized 20–45-second gameplay timers when no Log is reachable through at most four face-neighbor steps. Existing saved countdowns retain their remaining time. Player-placed Leaves never decay. Unsupported branches are checked through localized, budgeted gameplay timers.

Hold **LMB** on a block within six blocks to mine it. The outline/progress bar shows progress. Releasing LMB, changing targets, switching tools, losing reach or opening a menu discards progress. Holding LMB continues onto subsequent targets. [MiningRules.cs](./Gameplay/MiningRules.cs) centralizes categories, hardness, harvesting and drops.

Hands can break wood, earth, leaves, plants and Glass; they cannot break Stone, Cobblestone, Ores or Furnaces. Bedrock remains unbreakable in Survival. Pickaxes work on mineral blocks, axes on wooden blocks, and shovels on earth. Suitable efficiencies are Wood 2x, Stone 4x, Iron 6x, Diamond 8x. Matching tiered tools now take `hardness * 1.5 * 1.6 / efficiency`, with the existing 4x penalty for under-tier pickaxes. Hand and mismatched-tool base times remain unchanged. Mineral hardness is Stone 1.5, Cobblestone 2, Ores 3, Furnace 3.5. Plants are instant and Grass/Dirt retain matching hand durations.

Representative matching-tool mining seconds, before → after (all tiers take 1.6× longer):

| Block / tool | Wood | Stone | Iron | Diamond |
| --- | --- | --- | --- | --- |
| Stone / Pickaxe | 1.125 → 1.800 | 0.563 → 0.900 | 0.375 → 0.600 | 0.281 → 0.450 |
| Log / Axe | 1.250 → 2.000 | 0.625 → 1.000 | 0.417 → 0.667 | 0.313 → 0.500 |
| Dirt / Shovel | 0.625 → 1.000 | 0.313 → 0.500 | 0.208 → 0.333 | 0.156 → 0.250 |

Wooden Pickaxe harvests Stone/Cobblestone/Coal Ore; Stone or better harvests Iron Ore; Iron or better harvests Diamond Ore. An under-tier Pickaxe can slowly destroy a block but grants no valuable drop. Wrong tool categories cannot bypass mineral restrictions. Stone drops Cobblestone; Coal Ore drops Coal; Iron Ore drops the Ore block; Diamond Ore drops Diamond. Gravel drops exactly one Flint 10% of the time, otherwise exactly one Gravel. Glass breaks without a block drop. Other blocks keep their own drops, and breaking filled Chests/Furnaces also releases their actual stored stacks.

A tiered tool loses one use per successfully broken block or successful mob hit, including mining with an unsuitable tool where physically allowed. Interruptions, misses, invalid targets and blocked transactions do not wear it. Wood/Stone/Iron/Diamond have 59/131/250/1561 uses; Bows have 384. Last use removes the tool, with a short break notice for carried equipment. Swords deal 4/5/6/7 damage by tier; other tools deal 1-3 and fists retain random 1-2 damage. The shared 0.4-second attack cooldown, red flashes, knockback and passive behavior remain.

Dropped blocks render as small atlas cubes; plants use cutout quads; materials/tools use the new point-sampled sprite sheet. Gravity, terrain collision, depth testing and chunk-independent dynamic rendering remain unchanged. Dropped equipment retains its instance identity and damage.
After a short 0.35-second pickup delay for mined items, approaching within 1.25 blocks of an item's center from the player's collision box collects it automatically. Manual drops and death drops instead have a one-second delay. Timers use elapsed active gameplay time and stop with the rest of gameplay in menus. Compatible inventory stacks fill before empty slots. Any quantity that does not fit stays in the world and can be collected later. Dead players cannot collect items.

Nearby compatible stackable entities merge within 0.75 blocks, up to 64 items per stack; 40 + 40 becomes 64 + 16. Both resulting stacks keep the longer remaining pickup delay, preventing merges from bypassing it. A reusable spatial index checks nearby cells every quarter-second, without comparing every entity pair each frame. Entities do not expire. Items falling through a hole below the world remain at their original X/Z at the lower simulation limit (Y=-15.875), rather than teleporting to spawn or disappearing. A defensive one-million-entity save/allocation bound prevents further mining at that limit without removing blocks or items.

## Inventory

**Saplings** may be planted on Grass or Dirt in a clear cell. They return themselves when broken. After **1–30 active minutes**, a Sapling attempts to grow the existing oak-style tree. Blocked growth waits **30–60 seconds** and retries without overwriting nearby blocks. Growth/decay and Sheep regrowth pause with gameplay and survive saving/loading.

**Apples** restore **4 health**, **Raw Porkchops** restore **2**, and Furnace-cooked **Cooked Porkchops** restore **8**. Press RMB once per item; holding does not repeatedly eat, and full-health players consume nothing. Both kinds of Porkchop occupy one slot per item. Old stacked Raw Porkchops are split on load without losing quantity: free inventory/chest slots are filled, and overflow becomes saved dropped items nearby. New worlds and respawns still receive no starting supplies.

Press **E** to open the inventory: 27 main slots in three rows above the same nine hotbar slots used by the HUD. Opening it releases the mouse and freezes player/world motion; Furnaces continue processing; **E** or **Escape** closes it and recaptures the mouse. The layout scales with the viewport. **1-9** or the **mouse wheel** selects the gameplay hotbar slot. **Scroll down moves right (1 toward 9); scroll up moves left (9 toward 1)**, wrapping at both ends and supporting multiple increments. Scrolling in menus/inventory does not change it. Tab no longer changes hotbar pages.

Materials/blocks and Apples stack to 64. Raw and Cooked Porkchops are nonstackable. Tools, Shears and Bows are individual nonstackable items, with their own persistent identity and durability. Stackable items show quantities; damaged equipment shows a green-to-red durability bar, with a concise name/remaining-uses hover label. Empty slots contain no item and no quantity. Mouse actions are single clicks, with no drag distribution or shift transfers:

| Click | Empty cursor | Holding a stack |
| --- | --- | --- |
| Left | Pick up the whole slot | Place in an empty slot, merge up to 64 with the same type, or swap with a different type |
| Right | Pick up half, rounded down; pick up the single item when quantity is 1 | Place one into an empty/compatible non-full slot; incompatible/full slots stay unchanged |

**Left-click outside the inventory panel while holding items to drop the whole cursor stack near the player**, preferably in front. The inventory stays open. Empty-cursor clicks and clicks in panel gaps do nothing. Drops require nearby clearance outside the player's body and are removed from the cursor only after spawning succeeds; a failed drop leaves the stack held with a message. Closing returns cursor-held items and crafting inputs to compatible stacks/empty inventory slots, preferring the cursor's original inventory slot. Overflow is dropped nearby with the existing pickup delay. If clearance or the entity limit prevents a safe drop, the items remain owned and the interface stays open for recovery. Saving/exiting uses the same safe-return rule.

**RMB** placement consumes exactly one item from the selected hotbar slot only when placement succeeds. Empty slots cannot place anything. Failed placement, including player overlap, occupied cells, invalid plant support, and world boundaries, consumes nothing. Bedrock remains placeable from inventory.

Every newly created world now starts with **all 36 inventory/hotbar slots empty**, in every world size. No blocks, Crafting Tables, food or resources are granted. Acquire items by mining and collecting drops. Respawning never grants supplies. Existing saved inventories are preserved exactly, including former testing stacks. The pre-existing version-1 compatibility migration still grants its original 13 stacks only when reading a world with no inventory data; subsequent saves preserve that inventory normally. Each world has independent contents and hotbar selection.

## Crafting

**Shears** use two Iron Ingots diagonally in either orientation in a 2×2 pattern (valid offsets also work in a 3×3 table). They have **238 uses**. RMB an unsheared Sheep to obtain 1–4 White Wool without killing it; its fleece disappears and regrows after **3–6 active minutes**. Killing a sheared Sheep yields no additional Wool until regrowth. One use is deducted per successful shearing or collected Leaf; failed interactions do not wear Shears. Shears take **0.10 seconds** to collect Leaves, bypassing normal leaf loot.

Press **E** for the personal **2x2 crafting grid**, shown above the existing 27 main slots and nine-slot hotbar. **Right-click a placed Crafting Table** within six blocks to use a **3x3 grid**, even when holding a block or food; table interaction takes priority. Both interfaces pause gameplay and use the same cursor and existing left/right stack operations. **E or Escape** closes them and returns unused ingredients/cursor items safely.

Recipes now cover planks (1 Log -> 4), sticks (2 vertical Planks -> 4), the existing four-Plank Crafting Table, an eight-Plank Chest, an eight-Cobblestone Furnace, all 16 tools, wool-to-string (1 -> 2), bows and arrows (Flint/Stick/Feather vertical -> 4). Tool and Bow recipes use their classic shaped arrangements; axes and bows explicitly support horizontal mirrors. Required empty cells and all cells outside a translated pattern must be empty. Small recipes work anywhere they fit in the 2x2 inventory grid; taller/wider recipes require a table. Full patterns and IDs are listed in [SURVIVAL-PROGRESSION.md](./SURVIVAL-PROGRESSION.md).

The output is only a preview. Clicking it with either mouse button performs exactly one operation, consuming the registered ingredient quantities and transferring the complete result to an empty/compatible cursor. An incompatible/full cursor prevents the operation; holding the button does not repeat it. Crafted equipment receives a fresh identity and full durability and never stacks. Releasing/clicking again crafts another result if ingredients remain. [Crafting.cs](./Inventories/Crafting.cs) centralizes shaped recipes, quantities, output and translated matching.

Crafting grids are transient. Closing, saving and exiting return their real inputs, dropping only overflow; previews are never saved, returned or dropped. Death drops crafting ingredients/cursor items alongside ordinary inventory exactly once, retaining equipment metadata. Chest/Furnace contents stay in their world-coordinate registry. Version **8** preserves the older prefix and adds validated equipment, container and projectile state; old worlds are never regenerated or given new items. Original artwork prompts/preparation are in [crafting-table-artwork.md](./Content/Artwork/crafting-table-artwork.md) and [survival-artwork.md](./Content/Artwork/survival-artwork.md); existing atlas pixels 0-17 are preserved.

The five crafting check groups cover strict patterns/quantities/offsets, preview safety, atomic outputs, stack limits, mixed inventory/grid accounting, closing/overflow/failure recovery, death, all-size table persistence and empty starts, and responsive layouts. These original crafting checks remain in the expanded survival suite. Scripted graphics checks cover RMB table priority over food/placement, single-click output edges, 2x2/3x3 input/output rendering, a 320x240 recovery message, placed-table top/side pixels, save/return with held inputs, and the existing screenshot/state regressions. UI and table-render snapshots were inspected. Build and Release publish completed with zero warnings/errors. Physical keyboard/mouse use and extended survival sessions still require manual playtesting.

## Ores and survival progression

New worlds also contain moderately uncommon, irregular underground Sand/Gravel deposits of **10–30 blocks**. They replace covered Stone only, with solid support. Deposits run before ores, so ores take priority and can replace Stone, Sand or Gravel within their existing Y bands. Existing saved terrain is never regenerated.

Start empty: collect Logs, craft Planks/Sticks and a table, make a Wooden Pickaxe, obtain Cobblestone, then craft a Stone Pickaxe and Furnace. Find Coal and Iron Ore, smelt ingots and make an Iron Pickaxe, then search deep underground for Diamonds. All sixteen tools and the Bow are available through shaped crafting. The full recipe/ID tables are in [SURVIVAL-PROGRESSION.md](./SURVIVAL-PROGRESSION.md).

[OreGenerator.cs](./Worlds/OreGenerator.cs) replaces underground Stone, Sand and Gravel in new worlds with compact irregular connected veins of 2-8 cells, including across chunk seams. Coal and Iron use Y=4-60; Diamond uses Y=4-20. Per horizontal chunk, generation proposes 18 Coal veins, 10 Iron and one Diamond vein, rejecting invalid/non-replaceable/adjacent deposits rather than producing merged oversized veins. No Bedrock, surface profile, water, decorations or old saved terrain is overwritten. Ores are not retroactively added to existing worlds. Generation visits bounded proposals rather than rescanning all blocks; no ore-generation work occurs during normal gameplay.

## Chests, Furnaces, and smelting

**RMB** a Chest or Furnace within six blocks to open it. A Chest has 27 private storage slots above the same player inventory/hotbar. All transfers use the existing whole/half/single-item cursor rules and preserve damaged equipment. Closing leaves stored items in place. Chests are independent single containers; there is no double-chest joining.

A Furnace has input (upper left), fuel (lower left), output (right), a progress bar and burn indicator. Only valid smelting ingredients/fuels can be inserted, and output can only be taken. One Sand produces one Glass, one Iron Ore produces one Iron Ingot, one Log produces one Charcoal, and one Raw Porkchop produces one Cooked Porkchop. Each operation takes **10 active seconds**. Input is consumed only at completion. Coal and Charcoal are distinct items with identical **80-second** fuel value (eight operations); Logs/Planks burn **15 seconds**, allowing Charcoal to be made before finding Coal. Remaining fuel powers subsequent operations. Full/incompatible output pauses processing; already-lit fuel continues burning, but new fuel is not consumed without a valid operation. Starved progress waits for fuel; changing/removing the input resets progress.

Furnaces continue while any inventory-style interface is open, including when their chunk has no resident GPU mesh. Player movement/combat, mobs and item motion retain their menu suspension. Actual Pause/Settings/death/main-menu states stop processing. There is no offline/wall-clock smelting. Save/load retains all contents, partial progress and remaining burn without restarting fuel. Breaking a Chest/Furnace drops the block plus each stored stack; incomplete smelting creates no free product. A sparse [block-entity registry](./Worlds/BlockEntities.cs) owns this state by world coordinates; only active Furnaces tick.

## Bow and arrows

Hold **RMB** with a Bow selected to draw (up to one second), then release to fire. The HUD shows draw power. A valid release consumes exactly one Arrow from the inventory/hotbar; no ammunition means no drawing and no shot. Arrow crafting uses Flint above Stick above Feather to make four; Spiders provide String; four String in a 2×2 square craft White Wool. The former Wool-to-String recipe is removed. Bows are nonstackable, with 384 uses.

Arrows follow the camera aim and gravity, use swept block/mob collision, and deal 1-5 damage depending on power. They never target the player. A mob impact removes the projectile and charges exactly one use to the originating Bow's stable identity, even after storage/drop/slot changes; a missing/broken Bow is never replaced with another instance. Misses and releases do not wear the Bow. Arrows despawn on block impact, finite bounds or after 12 gameplay seconds, with at most 256 active. Active flights persist through saving/loading. Menus cancel unfinished drawing; player combat and arrow motion remain suspended there. No retrieval, critical hits or special arrows are added.

## Passive mobs, resources, and raw porkchops

New Small, Normal, Large, and Massive worlds attempt to spawn **32, 128, 512, and 2048 passive mobs total**, respectively, sharing that budget across all four species. A separate seeded species sequence allows small variations while keeping a balanced mixture. Seeded positions require clear, level grass, no water, spacing between mobs, room for their species-specific bounds, and at least 12 horizontal blocks of clearance from the player's initial spawn. Extremely obstructed terrain can yield fewer mobs. Initial spawning runs once after terrain/decorations, without modifying terrain or rebuilding meshes for individual mobs. The actual initial counts become independent per-species population targets, rather than applying a new budget after deaths. The defensive runtime/save limit is **4096 combined**, preserving valid older pig populations. There is no breeding, despawning, or population growth beyond these targets.

Each killed animal schedules exactly one same-species replacement after an independent random **30-60 seconds of active gameplay**. Replacement candidates reuse the grass, water, clearance, and species-bound checks and exclude other mobs, dropped items, and the player's body. The first candidates prefer at least 24 horizontal blocks from the player, relaxing to 12 for later attempts. Work is bounded to 32 candidate positions per simulation update across all ready replacements. A failed search waits five gameplay seconds before retrying; pending animals are never abandoned and cannot exceed their species' saved target. Pausing, inventory, death, and other menus suspend these timers. Saving preserves targets and remaining delays, so loading neither loses a replacement nor creates it twice.

| Species / saved ID | Maximum health | Walking speed | Death drop |
| --- | --- | --- | --- |
| Pig / 0 | 10 | 0.8 blocks/second | 1-2 Raw Porkchops |
| Cow / 1 | 10 | 0.7 blocks/second | 1-4 Leather |
| Sheep / 2 | 10 | 0.75 blocks/second | 1-4 White Wool |
| Chicken / 3 | 6 | 0.9 blocks/second | 1-4 Feathers |

All four species share the original pig idle/wander, gravity, terrain collision, water flotation/shore hopping, and damage logic in [PassiveMob.cs](./Entities/PassiveMob.cs). Their health, dimensions, speeds, textures and drops are centralized in [MobDefinitions.cs](./Entities/MobDefinitions.cs). They stop at obstacles and choose a later direction, without pathfinding, pursuing, fleeing, attacking, or automatic land-block climbing. Damage does not change their AI decision. The original seven-cuboid pig model/texture/animation remains intact. Cows have a larger body, head, muzzle, horns and four legs; sheep have a fleece body, head/cap and four legs; chickens have a small body/head, beak, red wattle, wings and two legs. Distance-driven leg swings (and chicken wing motion) stop while idle. [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs) frustum-culls and batches dynamic cuboids by species, issuing at most four mob draws and reusing GPU capacity without touching chunk meshes. It restores texture/sampler/blend/depth/culling state, and the F1/F2 binding regression remains covered.

Hold **LMB** while aiming at a mob within the normal six-block reach to punch it. Each successful punch randomly deals **1 or 2 health**, with a **0.4-second cooldown**, a brief red flash, and modest upward/backward knockback. Solid terrain occludes entity targeting; non-solid plants and water do not. The nearest valid mob takes priority over mining the block behind it, using its species-specific bounds. When it leaves the ray or dies, holding LMB continues ordinary block mining. Death creates one stack of the designated resource and quantity, exactly once. Like mining, a fatal transfer waits if the defensive dropped-entity capacity limit prevents creating its drop. Mobs never flee or retaliate.

**Leather (item ID 257)** and **Feather (item ID 258)** are non-placeable, non-food items. **White Wool (block/item ID 23)** is a normal solid opaque cube using atlas tile 15. All stack to 64 and support the existing inventory clicks, splitting, cursor/manual/death drops, pickup, stacking, and per-world persistence. Leather/Feather use cutout held/drop sprites and separate editable 16 x 16 icons; Wool uses textured cubes in the world, hotbar/inventory, held view, and dropped entities. No shearing or colored wool is implemented.

**Raw Porkchop uses item ID 256**, outside the existing byte-valued block IDs. It is non-placeable and nonstackable. Existing inventory clicks, merging, splitting, manual/death drops, pickup, and per-world persistence apply. Its cutout icon also appears as a flat held item and camera-facing dropped sprite. Select it in the hotbar and **press RMB to consume one and restore 2 health**, capped at 20. Full-health/dead players consume nothing. Holding RMB does not repeat eating; release and press again. Ordinary block placement still uses RMB for placeable items. Cooked Porkchops restore 8 health and Apples restore 4. There is no hunger, regeneration, eating animation, or consumption timer.

Living mob species, positions, yaw, and remaining health persist with each world, together with population targets and pending replacement delays. AI timers, walk phase, knockback velocity, and hurt flash are temporary. Killed mobs are removed before saving; loading or player respawn never restores the dead entity, and its scheduled replacement still waits for the remaining gameplay delay. Mobs and dropped resources freeze while gameplay is paused or inventory/menus are open. Editable [pig.png](./Content/Mobs/pig.png) and [raw-porkchop.png](./Content/Items/raw-porkchop.png) remain unchanged. The new [cow.png](./Content/Mobs/cow.png), [sheep.png](./Content/Mobs/sheep.png), [chicken.png](./Content/Mobs/chicken.png), [leather.png](./Content/Items/leather.png) and [feather.png](./Content/Items/feather.png) use the existing Content pipeline. [Artwork notes](./Content/Artwork/passive-mob-artwork.md) record their built-in image-generation prompts and one-time preparation. Only previously unused terrain atlas tile 15 was filled; world/lake generation and water mechanics are unchanged.

## Health, death, and movement

Movement now uses 4.3 blocks/second walking speed, responsive ground acceleration/friction, lower air control, gravity 28 blocks/second², an 8.4 blocks/second jump impulse and a 60 blocks/second terminal fall speed. Jump height is about 1.23 blocks; crouching retains 35% walking speed and the same jump impulse. Swimming, support-based crouch edges, collisions and vertical-distance fall damage remain in effect. All simulation uses the existing 120 Hz clock.

The player has a maximum of **20 health points**, shown as ten pixel-art hearts above the hotbar in Survival. Fractional damage from Difficulty and Armor is preserved internally. Full hearts represent two points; half hearts represent one. Damage and health restoration are clamped to 0-20 through centralized methods. Raw porkchops provide direct healing; there is no natural regeneration.

Survival damage sources include falls, Lava, hostile attacks, drowning, and Rotten Flesh. On landing on a solid surface, base fall damage before Difficulty and Armor is `max(0, floor(fallDistance - 3))`, with a small floating-point tolerance at integer thresholds. Distances of 3, 4, 5, 13, and 23 blocks cause 0, 1, 2, 10, and 20 damage respectively. Distance is vertical feet descent from the highest point reached during the airborne movement, independent of horizontal motion or impact velocity. Ordinary jumps cause no damage; jumping to a lower surface measures from the apex. Each landing resets tracking, while pausing or opening inventory during a fall preserves it.

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
| Right click | Place/interact with blocks, shear a Sheep with Shears, or eat selected food when below full health |
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

Block interaction reaches six blocks from the camera. Each RMB press attempts one placement. Placement requires an empty cell inside the world; solid blocks cannot overlap the player's body. Raycasts use compact vegetation selection bounds and stage-sized crop bounds; individual transparent texels are not tested. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Collision and raycasts operate in world coordinates across chunk seams.

Block edits, health, collected inventory, and uncollected items persist together. Infinite terrain and a lighting engine are not implemented.

## Code layout

Source files use file-scoped namespaces matching their directories, rooted at `MDSM`. The plural `Worlds` and `Inventories` and the `Gameplay` grouping avoid collisions with the existing `World`, `Inventory`, and `Player` class names. Existing companion types remain together in their original files.

| Directory / namespace | Responsibility |
| --- | --- |
| [Core/](./Core/) — `MDSM.Core` | Game coordination, application state, frame timing, and application settings |
| [Worlds/](./Worlds/) — `MDSM.Worlds` | World/chunk storage, block definitions, sizes, terrain, and decoration |
| [Rendering/](./Rendering/) — `MDSM.Rendering` | World/item rendering, meshes, atlas loading, held models, and screenshots |
| [Gameplay/](./Gameplay/) — `MDSM.Gameplay` | Player/camera state, movement, fall tracking, mining, placement, and raycasting |
| [Inventories/](./Inventories/) — `MDSM.Inventories` | Item definitions, stacks, inventory operations, and hotbar selection |
| [Entities/](./Entities/) — `MDSM.Entities` | Passive-mob definitions, shared behavior/populations, respawn metadata, dropped-item physics, and drop-position checks |
| [UI/](./UI/) — `MDSM.UI` | HUD, inventory interface, menus, layouts, and pixel font |
| [Input/](./Input/) — `MDSM.Input` | Relative mouse capture and presentation shortcuts |
| [Persistence/](./Persistence/) — `MDSM.Persistence` | World serialization, save management, and saved/session state |
| [Testing/](./Testing/) — `MDSM.Testing` | All existing headless checks and optional DesktopGL regression checks |

[Program.cs](./Program.cs) remains at the project root as the application/check-runner entry point, alongside [MDSM.csproj](./MDSM.csproj) and the existing configuration, documentation, and icon files. SDK recursive source inclusion compiles the organized directories without explicit compile entries. Build and check commands still run from the project root. Application-data locations and save/settings formats are unchanged.

Source assets under [Content/](./Content/) are grouped into [Blocks/](./Content/Blocks/) (terrain atlas and water), [Mobs/](./Content/Mobs/) (four animal skins), [Items/](./Content/Items/) (unified editable item atlas, with former sheets retained as pixel-exact references), and [Artwork/](./Content/Artwork/) (existing creation notes). [Content.mgcb](./Content/Content.mgcb) remains at the Content root and builds matching grouped content keys, such as `Blocks/terrain`, `Mobs/pig`, and `Items/items`. The item atlas consolidates former sheets without changing their source pixels; point filtering and content processing are preserved. Normal builds and publish include the grouped compiled assets. Generated content directories are managed by MonoGame.

The targeted cleanup removed the old Pig and PigPopulation inheritance wrappers: all species now use [PassiveMob.cs](./Entities/PassiveMob.cs) and [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) directly. The legacy `PigState` record remains in [MobDefinitions.cs](./Entities/MobDefinitions.cs), preserving its fields and the existing binary readers/writers. [MobPopulationState.cs](./Entities/MobPopulationState.cs) retains its distinct population-target/timer validation role. [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs) owns all four species' unchanged geometry and shared transform/tint/leg setup; [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs) continues to own GPU batching, culling and graphics-state restoration. The former PigRenderer was a geometry helper, not a separate rendering pass. No save identifiers, layouts or version numbers changed.

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
| [Crafting.cs](./Inventories/Crafting.cs) | Shaped recipe definitions/matching, real input stacks, preview and atomic output transfers |
| [InventoryScreen.cs](./UI/InventoryScreen.cs) | Shared 2x2/3x3 crafting layouts, inventory/hotbar slots, point-sampled icons/counts and mouse interface |
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
| [Content/Blocks/terrain.png](./Content/Blocks/terrain.png) | Editable 256 x 256 block texture atlas |
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
| [MobDefinitions.cs](./Entities/MobDefinitions.cs), [PassiveMob.cs](./Entities/PassiveMob.cs), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) | Central species properties and legacy save record, shared simulation, spawning, combat and replenishment |
| [CombatController.cs](./Gameplay/CombatController.cs), [ItemUse.cs](./Gameplay/ItemUse.cs) | Entity ray targeting/occlusion, attack cooldown/mining priority, and press-edge food consumption |
| [CuboidModelBuilder.cs](./Rendering/CuboidModelBuilder.cs), [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs) | Reusable textured cuboids/UV regions and unchanged species models/animation |
| [ItemSpriteGeometry.cs](./Rendering/ItemSpriteGeometry.cs) | Shared cutout geometry for held and dropped non-block items |
| [PigChecks.cs](./Testing/PigChecks.cs), [PigGraphicsChecks.cs](./Testing/PigGraphicsChecks.cs) | Pig/food simulation, persistence, input paths, and actual GPU regressions |
| [InterfaceChecks.cs](./Testing/InterfaceChecks.cs) | F1/F3/F2 input edges, screenshot storage/failures, and information text/layout checks |
| [InterfaceGraphicsChecks.cs](./Testing/InterfaceGraphicsChecks.cs) | Optional real DesktopGL PNG pixel comparisons, HUD states, resizing, menu navigation, and failure recovery |

All chunks remain loaded for the active world. There is no chunk streaming, multithreaded generation, armor, tree growth, or vegetation growth.

The water update adds [Water.cs](./Worlds/Water.cs) for fluid-state/height queries, [FluidSimulation.cs](./Worlds/FluidSimulation.cs) for scheduled flow, [LakeGenerator.cs](./Worlds/LakeGenerator.cs) for new-world basins, [WaterMeshBuilder.cs](./Rendering/WaterMeshBuilder.cs) and [WaterRenderer.cs](./Rendering/WaterRenderer.cs) for translucent geometry, and [WaterChecks.cs](./Testing/WaterChecks.cs) / [WaterGraphicsChecks.cs](./Testing/WaterGraphicsChecks.cs) for regression coverage. Integration changes are limited to existing world/chunk storage, creation/load orchestration, rendering, targeting/placement, player/pig water motion, and the Controls text.

Milestone 8 changes 26 files (9 added, 17 modified):

| System | Added | Modified |
| --- | --- | --- |
| World and simulation | [Water.cs](./Worlds/Water.cs), [FluidSimulation.cs](./Worlds/FluidSimulation.cs), [LakeGenerator.cs](./Worlds/LakeGenerator.cs) | [World.cs](./Worlds/World.cs), [Chunk.cs](./Worlds/Chunk.cs), [Blocks.cs](./Worlds/Blocks.cs) |
| Rendering | [WaterMeshBuilder.cs](./Rendering/WaterMeshBuilder.cs), [WaterRenderer.cs](./Rendering/WaterRenderer.cs) | [WorldRenderer.cs](./Rendering/WorldRenderer.cs), [ChunkMeshBuilder.cs](./Rendering/ChunkMeshBuilder.cs), [TextureAtlas.cs](./Rendering/TextureAtlas.cs) |
| Integration and movement | None | [Game1.cs](./Core/Game1.cs), [Player.cs](./Gameplay/Player.cs), [BlockRaycaster.cs](./Gameplay/BlockRaycaster.cs), `Pig.cs` (legacy wrapper, now consolidated), `PigPopulation.cs` (legacy wrapper, now consolidated), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Persistence | None | [WorldSerializer.cs](./Persistence/WorldSerializer.cs) |
| Assets | [water.png](./Content/Blocks/water.png), [water-artwork.md](./Content/Artwork/water-artwork.md) | [Content.mgcb](./Content/Content.mgcb) |
| Checks | [WaterChecks.cs](./Testing/WaterChecks.cs), [WaterGraphicsChecks.cs](./Testing/WaterGraphicsChecks.cs) | [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [Program.cs](./Program.cs) |
| Documentation | None | This README |

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The **117 check groups** include independent F1/F3 state, repeated F2 input without duplicate captures, screenshot directory creation, collision-safe filenames and write failures, accurate Controls/About acknowledgements, and responsive information layouts. Existing coverage includes FPS defaults and all integer slider values, uncapped override/value retention, settings reload/validation/write failures, identical fixed simulation outcomes at 5/30/60/144/240/360/997 rendered FPS, reduced crouch camera height, and selected cube/plant geometry, empty-slot suppression/switching, menu visibility, UVs, and layout. Mouse regressions check zero stationary rotation at 60-2000 updates/second, identical movement/sensitivity across rates, no replayed deltas, one-unit motion without a dead zone, and capture/release/resize flushing without first-sample latency. Health, fall tracking, death/respawn, dropped items, inventory accounting/placement, persistence, v1/v2 compatibility, crouch support/jumps, generation/restoration, geometry, atlas, checksums, atomic saving, management, and world-size checks remain included. A bounded non-seekable stream verifies that metadata reading stops before block data. Persistence fixtures stay under the test build and are cleaned up; they never touch real saves. A failing check returns a nonzero exit code. These checks do not open a window or exercise operating-system input.

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

Manual testing is still needed for physical F1/F2/F3 presses and menu scrolling, screenshots on other drivers/platforms and high-DPI displays, physical mice at different polling rates/DPI, real Alt-Tab/focus changes and cursor restoration, Linux/macOS relative capture, and sustained high-FPS play in all sizes. Also exercise slider/mouse-wheel controls, held-item alignment on other displays/aspect ratios, irregular crouch edges, falls across menu transitions, death-drop recovery, many simultaneous items, inventory placement/accounting, save-on-window-close, filesystem errors, and folder opening. Scripted injected input is not physical mouse playtesting. Save/load remain synchronous; new-world generation displays phase progress and prepares initial meshes in batches.


The nine water groups preserve all 65 previous groups. They verify source/falling/level bytes, non-inventory water, placement displacement, downward priority, seven-block horizontal reach, obstacle routing, supported source regeneration, complete recession and remaining supplies, cross-chunk/world boundaries, bounded/idle queues and render-independent ticking, shared surface heights and face visibility, swimming/fall resistance, pig flotation and shoreline escape, deterministic/stable all-size lakes with restrained sizes and patchy beds/beaches, extreme seeds, actual bank/bed excavation, all-size active-flow round trips, and unchanged v4 compatibility. Existing tests continue exercising older v1-v3 readers and all previous systems.

The graphics command additionally checks water on a real DesktopGL device: blue alpha blending over different backgrounds, visible lake beds, foreground terrain depth, absence of water depth writes, restored blend/depth/raster/sampler/texture state, repeated HUD-hidden captures and following unchanged frames, F1/F3 independence, exact PNG pixels at 1280x720 and 801x603, held-Space swimming through the real input path, and a rendered/saved newly generated lake. These are scripted pixel/input checks; physical controls, shoreline appearance while exploring, unusual waterfalls, other graphics drivers, and sustained Large-world fluid/rendering performance still require manual playtesting.

## Milestone 9 source changes and verification

37 files changed: 13 added and 24 modified. Existing classes were extended/extracted rather than introducing a new entity framework; all previous check groups remain enabled.

| System | Added | Modified |
| --- | --- | --- |
| Passive behavior/population | [MobDefinitions.cs](./Entities/MobDefinitions.cs), [PassiveMob.cs](./Entities/PassiveMob.cs), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) | `Pig.cs` (legacy wrapper, now consolidated), `PigPopulation.cs` (legacy wrapper, now consolidated) |
| Rendering | [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs), [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs) | `PigRenderer.cs` (geometry now in [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs)), [CuboidModelBuilder.cs](./Rendering/CuboidModelBuilder.cs), [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs), [FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs), [TextureAtlas.cs](./Rendering/TextureAtlas.cs) |
| Integration, items and saves | None | [Game1.cs](./Core/Game1.cs), [CombatController.cs](./Gameplay/CombatController.cs), [Items.cs](./Inventories/Items.cs), [Inventory.cs](./Inventories/Inventory.cs), [Blocks.cs](./Worlds/Blocks.cs), [WorldSave.cs](./Persistence/WorldSave.cs), [WorldSerializer.cs](./Persistence/WorldSerializer.cs), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Assets | [cow.png](./Content/Mobs/cow.png), [sheep.png](./Content/Mobs/sheep.png), [chicken.png](./Content/Mobs/chicken.png), [leather.png](./Content/Items/leather.png), [feather.png](./Content/Items/feather.png), [passive-mob-artwork.md](./Content/Artwork/passive-mob-artwork.md) | [terrain.png](./Content/Blocks/terrain.png) (tile 15 only), [Content.mgcb](./Content/Content.mgcb) |
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

## Expanded world generation verification

The height/dimension update changes [WorldSize.cs](./Worlds/WorldSize.cs), [World.cs](./Worlds/World.cs), [Chunk.cs](./Worlds/Chunk.cs), [TerrainGenerator.cs](./Worlds/TerrainGenerator.cs), [LakeGenerator.cs](./Worlds/LakeGenerator.cs), [FluidSimulation.cs](./Worlds/FluidSimulation.cs) (initial frontier only), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs) (area budgets/shared safety cap), [WorldSave.cs](./Persistence/WorldSave.cs), [WorldSerializer.cs](./Persistence/WorldSerializer.cs), [MenuController.cs](./UI/MenuController.cs), [Game1.cs](./Core/Game1.cs), and [WorldRenderer.cs](./Rendering/WorldRenderer.cs) (initial preparation batches). Content assets and normal rendering algorithms are unchanged. Existing checks were adjusted for intentionally revised dimensions, limits and asynchronous creation; none of the original 95 check groups was removed.

[WorldExpansionChecks.cs](./Testing/WorldExpansionChecks.cs) adds three groups. Every size gets full storage allocation, chunk/index/boundary checks, high-Y placement/raycasting and height-cache invalidation; terrain strips cover multiple seeds and full-resolution chunk seams. Independent 64-layer fixtures exercise both supported legacy widths across versions 1-7, checking exact block hashes, upper-air layers, pose, inventory, health, entities, targets/timers, queued/active gravity, and subsequent 128-layer saves. Standard per-mechanic fixtures continue exercising the two supported legacy horizontal extents, 256 and 512; the obsolete 128 extent is intentionally rejected. Large/Massive integration uses one world at a time and streamed temporary files rather than keeping several large memory-stream copies.

The complete integration matrix generates **all four full worlds**, checks natural material support, strata, spawn, tree/plant/population scaling and stable source water, and independently classifies connected pond/river/lake shapes, water levels, depth, elongation, original site slopes and dry bank gradients. It regenerates each complete world and compares SHA-256 hashes. It then saves/restores all block bytes, inventory slots, health/pose, mobs, pending respawns, dropped items and falling objects with exact hashes/state comparisons. Metadata remains header-only. Temporary test files stay under the test build and never touch real player data.

Historical height-expansion verification (before the major-lake update documented in [LAKES.md](./LAKES.md)): `dotnet build` completes with zero warnings/errors, all **98/98** headless groups pass, Release publish succeeds, and the complete graphics suite passes. Seed 173 produces 4/16/64/256 ponds, 0/0/4/12 rivers and 0/0/2/5 lakes. The tested Large/Massive worlds have 3065/12615 trees and 512/2048 passive mobs respectively. Timings depend on hardware; the Massive generated/validated/initial-save portion took about 26 seconds on the test machine, excluding full-world GPU preparation and extended gameplay.

The graphics command also runs [WorldExpansionGraphicsChecks.cs](./Testing/WorldExpansionGraphicsChecks.cs): fully generated Large and Massive worlds provide representative 25/20-chunk natural terrain-and-water GPU views. Existing checks exercise all four menu size selections, new-world progress/completion, prepared Small-world meshes, screenshots, water, mobs, physics, inventory and persistence. Representative GPU draws are **not** verification of all-world GPU allocation or sustained Large/Massive gameplay. Physical-input playtesting, landscape exploration, unusual edited banks, and full-world GPU memory/performance on target hardware remain manual checks. Large/Massive creation can take appreciable time; phase/counter feedback remains visible. Saving remains synchronous; loading now uses background CPU preparation and incremental nearby meshes (see the performance update). No streaming, caves, ores, new blocks or gameplay systems were introduced.

## Creative collision and mob-aware placement correction

The Creative Door failure was reproduced before the fix using scripted RMB and Space through the actual DesktopGL game loop. Closing an open Door around a stationary player left the player intersecting its new collision geometry. Creative flight skipped zero-motion axes, unlike Survival's always-active gravity. The directional collision solver assumed a clear starting position and used stale bounds; when already embedded, its face corrections could reverse requested movement and exceed the requested displacement. In the reproduced sequence, Space moved the player from Y=1 to Y=-1.8 in one rendered frame, without changing camera yaw or pitch. This placed the player beneath the floor rather than applying ordinary flight.

[Player.cs](../Gameplay/Player.cs) now resolves existing intersections before either movement mode runs. A bounded six-direction search selects the closest clear collision-face exit, refreshes bounds as it encounters obstacles, respects finite-world limits, and resets movement/fall tracking after external displacement. If an unusually deep solid volume has no exit within the bounded search, movement remains suspended rather than becoming inverted or allowing noclip. Ordinary axis sweeps refresh their bounds and keep their result within the requested movement interval. Flight controls/speeds, Ctrl boost, gravity, crouching, stepping, and Survival balance values are unchanged.

Placement previously checked only player and falling-block overlap. [PlacementCollision.cs](../Worlds/PlacementCollision.cs) now checks the proposed solid geometry against living passive and hostile mob bounds before any edit or item consumption. It uses the same shape definitions as movement, including both Door halves, facing-specific Gates, connected Panes, and 1.5-block Fence collision. It also previews neighboring Pane/Fence arms that a new block would connect, including across chunk boundaries. Non-solid decorations retain their previous placement behavior. The population checks run only on placement attempts, without per-frame population scans or changes to mob physics/AI.

Files changed:

- [Player.cs](../Gameplay/Player.cs): penetration recovery and stable axis sweeps.
- [World.cs](../Worlds/World.cs), [WorldDetails.cs](../Worlds/WorldDetails.cs), [PaneGeometry.cs](../Worlds/PaneGeometry.cs), and new [PlacementCollision.cs](../Worlds/PlacementCollision.cs): shared geometry previews and atomic mob-aware placement validation.
- [WorldSave.cs](../Persistence/WorldSave.cs): bind the session's existing populations to runtime-only world placement context. Binary serialization, format version 10, IDs, and save locations are unchanged.
- New [CollisionUpdateChecks.cs](../Testing/CollisionUpdateChecks.cs) and [CollisionGraphicsChecks.cs](../Testing/CollisionGraphicsChecks.cs), plus [PrototypeChecks.cs](../Testing/PrototypeChecks.cs) and [Program.cs](../Program.cs): five new headless regression groups, actual game-input checks, and test/diagnostic commands.

Focused verification passes all five new groups, the ten existing gameplay-refinement groups, and the ten placement-update checks. Coverage includes all Door facings/hinges, saved intersections, embedded solids/world edges, bounded failure, walls/floors/ceilings, sliding, camera-relative axes and flight speed, mode switching, 5/30/60/144/360/2000 rendered FPS with the existing fixed simulation, all seven mob species, stationary/fractional/airborne bounds, live movement out of a placement cell, thin shapes, and neighboring connection growth. Rejected placements preserve blocks, revisions, metadata, inventory quantities, and mob positions.

The same scripted Door-close/Space sequence after the fix moves sideways out of the Door and upward at normal flight speed: Y=1 to approximately Y=1.07167, with unchanged camera orientation. Actual W/S and Space/Shift inputs retain normal directions/speeds afterward. Actual RMB rejects placement through a Cow and a Zombie and succeeds after each walks away. These are automated input and GPU checks, not physical-input or extended manual playtesting.

Verification: `dotnet build` succeeds with zero warnings/errors. The full headless suite reports **167/168 passing**; its sole failure is the pre-existing historical item-atlas RGBA comparison at tile 0, pixel (0,0), involving RGB under a fully transparent pixel. The original and consolidated artwork hashes remain unchanged, and this check was not modified or bypassed. All five new groups pass. The complete `--graphics-test` suite passes with confirmed native exit code 0, including the new live Door/mob-placement input sequences, existing inventory/menus, water, screenshots/F1/F2, and Large/Massive bounded mesh residency, edits/re-entry, and GPU cleanup. PowerShell initially interpreted the deliberately triggered screenshot-write failure's stderr as a command error; a complete rerun with native redirection confirmed the suite's successful exit.

Physical-input playtesting remains appropriate for crowded Door/Gate structures, unusual edited/enclosed spaces, and longer Creative/Survival sessions. No publishing or distribution commands were run, and README.md was not edited.

## Support-dependent block drop correction

The original bug was reproduced before the fix: Survival mining removed Dirt and its supported Torch but produced only the Dirt item. Mining used a global harvest-suppression flag throughout recursive neighbor removal, then manually recreated only its own loot and the plant immediately above. Torch/Door support callbacks therefore never emitted their items. Flowers, Saplings, and single/double Tall Grass also lacked a shared loot path when support changed through ordinary neighbor updates rather than mining. Crop drops already had a separate callback and needed to retain their existing rules without being duplicated.

New [SupportDrops.cs](../Worlds/SupportDrops.cs) supplies one context for each outer block change. It previews the local removal chain, visits each cell once, treats Door/paired-plant cleanup as one logical harvest, rolls the existing [MiningRules.cs](../Gameplay/MiningRules.cs) tables, and reserves space for both collateral loot and the caller's directly harvested items before changing blocks. It emits dependent loot only after successful removal. No loot tables, item/block IDs, placement requirements, or save layouts changed.

- Survival support loss yields one item per Torch and complete Door. Flowers/Saplings return themselves; Tall Grass and double-height grass retain their existing 10%/20% seed chances and no-drop outcomes; immature Wheat returns one Seed and mature Wheat returns one Wheat plus 1-4 Seeds.
- Creative mode suppresses collateral drops, including ambient support updates. Changing back to Survival restores them immediately.
- Generation and loading have no live drop context and cannot create unintended attachment drops. Direct mining still owns its original block/container loot, mining duration, tool wear, and cooldown.
- Door partner cleanup now guards the specific counterpart rather than suppressing every nested Door cleanup, preventing orphaned segments in chained/stacked Door removals.
- Falling-block startup adds its temporary entity only after voxel removal succeeds and retries failed removals, preventing duplication if support loot has no available entity capacity. Leaf decay also reserves its own loot together with attachment drops and retries a blocked removal; its timing/support/loot rules remain unchanged.

Runtime files changed: new [SupportDrops.cs](../Worlds/SupportDrops.cs), [World.cs](../Worlds/World.cs), [WorldDetails.cs](../Worlds/WorldDetails.cs), [MiningController.cs](../Gameplay/MiningController.cs), [FarmingSimulation.cs](../Worlds/FarmingSimulation.cs), [FallingBlockSimulation.cs](../Worlds/FallingBlockSimulation.cs), [VegetationSimulation.cs](../Worlds/VegetationSimulation.cs), and [WorldSave.cs](../Persistence/WorldSave.cs) (runtime mode context only). Added [SupportDropChecks.cs](../Testing/SupportDropChecks.cs) and registered its six groups in [PrototypeChecks.cs](../Testing/PrototypeChecks.cs) and [Program.cs](../Program.cs), including the focused `--support-drops-test` command. Previous uncommitted collision changes were preserved.

Focused verification: all six new groups pass, including all five Torch attachments, one-per-Door drops, appropriate positions and automatic collection, all Wheat stages, 2,000 single/double grass support removals, chained Doors/Torches, repeated neighbor updates, both modes, direct breaking/no-drop Glass, exact save/load restoration, generation/load safety, and full-capacity atomic rejection/retry with container contents and falling blocks. The existing completion (13 groups), gameplay-refinement (10 groups), and placement-update (10 checks) suites pass. The DesktopGL interface/interaction graphics suite passes with native exit code 0, including existing direct mining/structures, inventories, menus, water and F1/F2 screenshot-state regressions. No physical-input or extended manual playtesting is claimed.

Final verification: `dotnet build` completes with zero warnings/errors. The full `--self-test` suite reports **173/174 passing**, including all six new support-drop groups and every previous gameplay/world/save check. Its sole failure is the previously documented historical item-atlas comparison at tile 0, pixel (0,0); neither its test nor its artwork was changed. The interface graphics suite passes. Physical-input checks of unusual player-built attachment chains, farming supports, and falling supports remain appropriate; no manual playtesting is claimed. No publishing commands were run; README.md and texture assets were not edited.

## Editable artwork test cleanup

Removed obsolete pixel-equality assertions from [ItemAtlasChecks.cs](../Testing/ItemAtlasChecks.cs): the consolidated item atlas no longer needs to reproduce the historical Raw Porkchop, Leather, Feather, or survival-sheet pixels. Also removed the same group's requirement that the Sapling's block and inventory artwork be byte-identical; these are independently editable textures, and matching their pixels is not a rendering requirement. No artwork or runtime behavior was changed.

Retained the functional checks for a 256x256 atlas, valid tile indices, and opaque/transparent pixels in all 73 registered item sprite tiles. Renamed the check and its [RefinementChecks.cs](../Testing/RefinementChecks.cs) registration to describe these checks accurately, including the dynamic sprite count instead of the outdated count of 32.

Verification: `dotnet build` succeeds with zero warnings/errors; `--refinement-test` passes all 15/15 groups, including the updated atlas check. The complete long-running regression and graphics suites were not rerun for this test-only change. No texture assets or README.md were edited, and no publishing commands were run.

## Procedural sound system

Added a centralized [Audio/](../Audio/) subsystem: [SoundCatalog.cs](../Audio/SoundCatalog.cs) defines 112 reusable groups, [BlockSounds.cs](../Audio/BlockSounds.cs) maps existing blocks to eleven material families, [SoundEvents.cs](../Audio/SoundEvents.cs) provides bounded transient requests, [PlayerAudio.cs](../Audio/PlayerAudio.cs) derives footsteps/landing/swimming from actual movement, and [AudioManager.cs](../Audio/AudioManager.cs) owns MonoGame SoundEffect resources, pre-created playback instances and mixing.

The independent [Tools/SoundGenerator/](../Tools/SoundGenerator/) .NET utility generated 279 original WAVs under [Content/Sounds/](../Content/Sounds/), grouped by Blocks, Mobs, Player, Water, Interface and Interactions. They total 3,205,644 bytes (about 3.06 MiB), use mono PCM16 at 22,050 Hz, have clean endpoints/DC correction and conservative normalized peak headroom. Synthesis combines oscillators, harmonics/formants, filtered noise, grain envelopes and modulation; seeds are stable per path/variation. The runtime never synthesizes or overwrites assets. The main project excludes tool sources, and [Content.mgcb](../Content/Content.mgcb) copies WAVs unchanged into the regular build/deployment output. No packages or external services were added.

Material-specific hit, break, place, step and land effects cover Stone, Dirt, Grass, Wood, Sand, Gravel, Glass, Metal, Wool, Leaves and Plants. Every current mob species has distinct idle/hurt/death effects. Other cues cover player damage/death, pickups, jumping, swimming and water transitions/flow, Doors/Gates/Chests, inventories/crafting/equipment, eating/manual drops, tool breakage, bows/arrows/melee, shearing/feeding, tilling/planting, buckets, Furnace processing and water/Lava contact. Trigger hooks run at existing successful actions; failed placements/crafts/damage/pickups remain silent. Unbreakable or unharvestable solids can make throttled hit sounds without changing mining rules. Paired structures emit one break per logical object, and bulk pickups coalesce to one pickup event per update.

Playback is capped at 32 voices and 256 queued world events, with two pre-created instances per clip, per-group cooldowns, priority eviction, nonrepeating variants and small pitch variation. Positional events use stereo pan and squared range attenuation; listener motion updates existing voices. Nearby mob sounds reuse existing active updates with their own audio randomness, preserving AI randomness and avoiding a separate full-world entity scan. Idle queues/mixer produce no per-update managed allocations in the focused checks. World playback stops on pause/focus loss, while UI/death/container-processing cues use appropriate interface exceptions. Missing clips are skipped; unavailable audio hardware disables playback gracefully. Resources are reused across worlds and disposed on shutdown.

Added Settings > Sounds with Master, Effects, Mobs and Interface volume sliders, including mouse dragging and keyboard one-percent steps/Home/End. Existing Controls/About/Back entry indices and both Main Menu/Pause navigation origins remain supported. Audio defaults are Master 80%, category volumes 100%; preferences persist in the existing application-wide settings file and take effect immediately. Existing settings without volume fields retain their other preferences and receive these defaults. World format 10, block/item identifiers, terrain, artwork and gameplay balance remain unchanged; audio queues/state are never serialized.

Integration extends existing game/session coordination, mining and successful placement/support removal, player damage/jumps, dropped-item pickup, mob events, projectiles/bows, inventory interactions, farming/containers/fluids, leaf decay and falling-block settlement. Those systems retain ownership of gameplay decisions; audio follows their results. [AudioChecks.cs](../Testing/AudioChecks.cs) adds five headless groups and [AudioGraphicsChecks.cs](../Testing/AudioGraphicsChecks.cs) adds real MonoGame sound/input checks, registered through [Program.cs](../Program.cs) and the existing suites. [SoundSettingsLayout.cs](../UI/SoundSettingsLayout.cs), [SettingsLayout.cs](../UI/SettingsLayout.cs), [MenuController.cs](../UI/MenuController.cs), [GameSettings.cs](../Core/GameSettings.cs) and [GameState.cs](../Core/GameState.cs) provide the settings interface. Detailed category, architecture and regeneration instructions are in [SOUNDS.md](../Content/Sounds/SOUNDS.md).

Focused verification passes 5/5 audio groups: all 279 source/build WAV copies, format/length, zero endpoints, clipping/DC/silence bounds, distinct hashes, material/species coverage, event conditions and duplicate prevention, unbreakable hits, tool/food/Furnace feedback, distance-driven movement and 30–2000 render-FPS independence, category/pan/range calculations, bounded allocation-free queues, settings persistence/layout and unchanged save bytes. The generator's `--verify` confirms every WAV byte-for-byte without rewriting it. Real DesktopGL checks loaded all 279 assets and started all 112 groups, checked pooled variation/pitch/pan/volume/mute, 32-voice high-water, pause/focus handling, no idle mixer allocations, graphics-state preservation, both Sounds navigation origins and actual mining/crafting input feedback. These tests establish playback and numerical behavior, not human listening or physical-input playtesting.

Final verification: `dotnet build` succeeds with zero warnings/errors. The complete `--self-test` suite passes **179/179 groups**, including all prior checks and the five new audio groups. The complete `--graphics-test` suite passes with native exit code 0, including the audio playback/input checks, prior menus/inventory/world interactions, exact screenshot/F1/F2 regressions, water and entities, and Large/Massive bounded mesh residency, movement/re-entry, edits and GPU cleanup. All 279 WAVs are present unchanged in the development build output, and standalone generation verification passes. These representative graphics/performance checks are not a sustained real-world hardware benchmark.

Remaining manual work is listening on speakers/headphones to assess timbre, relative loudness, many nearby entities/edits, motion/spatial balance and other sound devices/drivers. No human listening or physical-input playtesting is claimed. No publishing commands ran, and README.md was not edited.

## Lossless compressed world saves (version 11)

Added [SaveContainer.cs](../Persistence/SaveContainer.cs), [CompressedSaveStreams.cs](../Persistence/CompressedSaveStreams.cs) and [SaveCompression.cs](../Persistence/SaveCompression.cs). [WorldSerializer.cs](../Persistence/WorldSerializer.cs) now writes compressed version-11 containers by default and dispatches both new and legacy inputs transparently. Zstandard level 3 is implemented through the pinned managed [ZstdSharp.Port 0.8.8](https://github.com/oleg-st/ZstdSharp) dependency in [MDSM.csproj](../MDSM.csproj). Brotli quality 3 and stored records remain supported for testing/format flexibility; normal gameplay writes Zstandard and uses stored bytes if a record would not shrink.

Compression is independent per 32 full-height chunks (1 MiB); core metadata remains directly readable for world listings. Tagged sections have independent versions, required/optional flags, sequences, codec/length fields and decoded SHA-256 checksums. A metadata checksum and complete-container checksum protect headers and encoded data, and the existing v10 checksum/semantic validation remain active. The current validated v10 payload is retained behind streaming adapters, preserving every existing field without a serialization rewrite. Future lighting, sunlight or world-time sections can be added with their own schemas and budgets; none of these features or placeholder arrays were implemented. Unknown required data is rejected rather than discarded; explicitly ignorable optional records are validated and skipped and are not preserved on resaving. Full layout details: [SAVE-FORMAT.md](../Persistence/SAVE-FORMAT.md).

Legacy uncompressed versions 1-10 remain supported, including the existing 64-to-128 height migration for supported older dimensions. Loading does not regenerate terrain, repopulate entities, modify inventories or rewrite files. The next successful save uses v11. GUID directories, world.dat names, application settings, block/item IDs and gameplay systems remain unchanged. [WorldSaveManager.cs](../Persistence/WorldSaveManager.cs) retains its existing same-directory temporary-file, durable flush and atomic replacement/rename behavior; compression/checksums complete before replacement. No current world data is optional.

Writers/readers use approximately two MiB of bounded staging plus reusable codec workspace, with decoded/encoded lengths, counts, order, schemas and window limits validated. There is no second 512 MiB world buffer. Actual CPU world storage remains one byte per block (512 MiB for Massive), and rendering/simulation are unchanged.

### Measurements

Fresh-process, single-pass pipeline runs on this Windows x64 i5-11400 (11th Gen, 2.60 GHz), .NET 10 hosting the net9.0 application. All cases use the same seed-173 fully generated world per size, scattered deterministic block edits, inventory/tool durability, dropped items and normal area-scaled passive populations. Every compressed result was restored and compared by complete v10 serialized-payload SHA-256. Files were cached/local; these are not cold-storage guarantees or sustained gameplay benchmarks. Save time includes validation, hashing, compression, flush and rename; load time includes decoding, validation and world reconstruction. Source-fixture preparation/loading and the final comparison hash are excluded.

| World | Storage | File MiB | Save s | Load s | Write peak working-set MiB | Read peak working-set MiB |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Small | Original raw v10 | 8.051 | 0.175 | 0.074 | 37.3 | 39.3 |
| Small | Zstandard 3 | 0.210 | 0.154 | 0.120 | 44.8 | 46.4 |
| Small | Brotli 3 | 0.197 | 0.087 | 0.091 | 40.1 | 40.9 |
| Normal | Original raw v10 | 32.177 | 0.376 | 0.310 | 63.8 | 67.3 |
| Normal | Zstandard 3 | 0.864 | 0.245 | 0.398 | 70.6 | 72.3 |
| Normal | Brotli 3 | 0.819 | 0.155 | 0.369 | 65.8 | 67.7 |
| Large | Original raw v10 | 128.683 | 1.180 | 1.258 | 168.7 | 168.5 |
| Large | Zstandard 3 | 3.467 | 0.437 | 1.429 | 175.1 | 176.3 |
| Large | Brotli 3 | 3.329 | 0.590 | 1.508 | 170.5 | 170.8 |
| Massive | Original raw v10 | 514.775 | 3.711 | 5.272 | 590.6 | 597.4 |
| Massive | Zstandard 3 | 13.950 | 1.440 | 5.916 | 597.1 | 606.8 |
| Massive | Brotli 3 | 13.358 | 1.950 | 6.090 | 592.4 | 599.8 |

Peak figures are process working sets sampled every 10 ms, including the world and runtime; brief peaks may be missed. They are not raw block-array sizes or VRAM. Massive pre-write working sets were about 589.3-589.4 MiB in all modes. Sampled managed write/read peaks were 544.3/558.2 MiB raw, 545.9/567.0 MiB Zstandard, and 546.0/567.0 MiB Brotli. Thus the compression increment is small and bounded, rather than another full-world copy.

Zstandard reduced the representative Massive file by about 97.3%. Brotli yielded roughly 4.2% smaller files there, but Zstandard used about 26% less save time and loaded about 3% faster than Brotli; it also won the Large pipeline comparison. Brotli was faster and smaller for Small/Normal in these runs. Given the requested preference and Large/Massive responsiveness, Zstandard 3 was selected globally. Compression adds some load CPU time relative to raw (Massive 5.27 to 5.92 s); it does not reduce block RAM.

The standalone codec comparison on the same Massive data used 13.937 MiB / 1.349 s encode / 0.819 s decode for Zstandard 3 versus 13.351 MiB / 1.690 s / 1.015 s for Brotli 3. These timings include file/hash work but exclude world reconstruction and therefore must not be confused with the pipeline table. Large 4 MiB frames provided no useful improvement over 1 MiB (Zstandard 3.524 vs 3.465 MiB; Brotli 3.327 vs 3.321 MiB) and required more scratch memory, supporting the smaller independent-group choice.

Added [CompressionChecks.cs](../Testing/CompressionChecks.cs), [CompressionBenchmarks.cs](../Testing/CompressionBenchmarks.cs) and the standalone [Tools/SaveCompressionBench/](../Tools/SaveCompressionBench/) project. Existing fixed-offset corruption/legacy fixture encoders now explicitly use the unchanged v10 writer, while current-format direct calls and the new checks exercise v11. The bounded metadata test includes the new options/checksum bytes while still rejecting any payload read. No prior check group was removed or bypassed.

Verification: `dotnet build` completes with zero warnings/errors. All **9/9** focused compression/persistence checks pass, including all four sizes, complete-state hashes, Zstandard/Brotli/stored frames, non-seekable reads, header-only listings, corrupted/truncated data, future-section policy, v1-v10 migration, interrupted temporary files and failed write/replacement safety. The full regression suite reports **182/183 passing**, including all four new compression groups and every existing world/persistence check. The sole failure is unrelated: the unchanged audio asset check requires 22,050 Hz but the current unchanged `Content/Sounds/Mobs/pig/idle-0.wav` is mono PCM16 at 44,100 Hz. Both test and clip were verified unchanged from HEAD; neither was altered to make the suite pass.

The existing DesktopGL interface suite passes with native exit code 0 using compressed creation/save/load/menu paths and preserving screenshots/render state. No physical-input or extended gameplay testing, forced power-cut test, or cold-disk performance claim is made. Manual verification can include opening a personal legacy world, checking modifications/entities, saving, closing and reloading. Large temporary benchmark worlds/codecs were removed after measurement; commands regenerate them on demand, while the small benchmark logs/results remain. No publishing commands were run and README.md was not edited.


## Lighting, day/night, Beds and Settings update (2026-11-06)

Implemented on top of the existing compressed-save changes; no previous work was reset, no new dependency was added, and README.md remains unchanged.

### Lighting and world time

- Added [BlockLighting.cs](../Worlds/BlockLighting.cs): separate integer sky/block light channels, each 0-14, packed into one byte per cached cell. Torches and Lava emit 14; burning Furnaces/Smokers/Blast Furnaces emit 13. Opaque blocks block propagation, Leaves/Water attenuate it, and Glass/thin geometry transmit it. Artificial light loses one level per air-cell step. Full vertical sunlight stays at 14 until obstructed/attenuated, then spreads with normal attenuation.
- Lighting is reconstructible from existing blocks/container burn state. There is no full-world light array or startup world scan. Each queried chunk is solved with a 15-cell horizontal halo and a one-cell sampling border; a bounded queue propagates the two channels. Shared source removal, roof edits, fluids and source changes invalidate only the local chunk neighborhood, including boundaries. The CPU cache holds at most 1,536 pages (60.75 MiB of packed light), enough for the normal 1080p mesh region plus a margin; scratch storage is reused. It is independent of total world size and contains no CPU terrain mesh cache.
- Added [WorldTime.cs](../Worlds/WorldTime.cs): a 1,200-second active-gameplay day, new-world daytime at 60 seconds, continuous sky/celestial motion, dusk/dawn transitions and a night-only skip to morning. Normal pause/menu behavior stops this clock.
- Added [BlockLighting.fx](../Content/Effects/BlockLighting.fx), [FlatLighting.cs](../Rendering/FlatLighting.cs), and [SkyRenderer.cs](../Rendering/SkyRenderer.cs). Terrain and water faces encode flat light and existing directional shade in their unchanged vertex layout. Daylight and Brightness are shader uniforms, so time/brightness changes rebuild neither light pages nor chunk meshes. No smooth lighting or AO is introduced. The minimum visual intensity ranges from .025 to .145 across Brightness settings; actual light/spawn eligibility is unaffected.
- Terrain/water retain alpha cutouts/translucency, depth behavior, atlas sampling, fog distances and bounded GPU mesh residency. Moving mobs/items/falling blocks/projectiles and held items receive corresponding local light tint. Sun and Moon are simple moving, untextured squares. Texture bindings are explicitly invalidated/rebound to preserve the DesktopGL screenshot fix.
- Hostiles now require effective light **exactly 0** at their spawn position. Removed the low-Y cutoff and fixed-radius Torch test from spawn eligibility. The bounded search covers the full height, including night surfaces and dark structures. The 36 cap, 2-4-second cycles, 128 candidate columns, player distances, collision/fluid safeguards, Peaceful behavior, AI suspension/despawning and existing mobs remain unchanged. The existing diagnostic `Torch` rejection field now counts light rejection generally.

### Beds, settings and interface behavior

- Added [Beds.cs](../Worlds/Beds.cs). Persistent IDs **61 Bed foot / 62 Bed head** append to existing IDs. Four orientations, both supports/clearances, player/mob-aware placement, shared 9/16-height collision/targeting, paired removal, one logical item/drop and one logical break/place sound are handled through the existing structure/support systems. Bed items stack to one. Creative placement/breaking uses existing consumption/drop rules.
- Added the 3-Wool-over-3-Planks recipe and the registered Creative item. Bed interaction takes priority over item placement/food and skips only at night. Respawning deliberately continues to use the existing default world spawn.
- Added original Bed artwork, with terrain tiles **53-59** and item tile **73**. The editable source, prompt/packing details and per-face regions are documented in [bed-artwork.md](../Content/Artwork/bed-artwork.md); [Pack.ps1](../Tools/BedArtwork/Pack.ps1) repacks offline. Mattress/frame sides use six-pixel strips, top/end/bottom faces are distinct, legs use wood subregions, and internal joining faces are omitted. All pre-existing atlas pixels outside these new tiles were verified unchanged.
- New worlds explicitly start Survival. Active mode changes persist per-world without altering inventory/Armor/health. Older saves, which lacked a per-world mode, retain the previous application-preference inheritance policy until played. Merely renaming an old save preserves this inheritance instead of forcing a new mode.
- Brightness (0-100%, default 50%) persists through the existing settings store. Master/Effects/Mobs/Interface volume sliders now live directly in Settings; the separate Sound screen/action is removed, with preferences and audio behavior retained. FPS, uncapped rendering, view bobbing, difficulty, game mode and information-menu navigation remain available. The compact two-column layout is checked at 320x240 and larger sizes.
- Both HUD/menu World Saved notices now expire after three elapsed seconds, including while paused. Repeated saves refresh the timer. Storage errors retain their existing behavior.

### Save compatibility

[The format documentation](../Persistence/SAVE-FORMAT.md) now describes **compressed version 12**. It retains Zstandard level 3, independently compressed chunk groups, the v10 core payload, checksums, bounded buffers and safe temporary-file/atomic replacement. One required, schema-versioned ten-byte environment record stores Float64 world time, mode (Survival/Creative/legacy inheritance), and lighting-rules version. Packed lighting is deterministically reconstructed, avoiding redundant disk/RAM data. Bed orientations use the existing validated structure metadata layout. Uncompressed versions 1-10 and compressed v11 remain readable, including existing height migration, inventories, containers, entities and pending simulation state. Old terrain is not regenerated; unsupported/corrupt/missing required environment data is rejected.

### Measured performance

Isolated Windows x64 measurements on an i5-11400 with DesktopGL reporting **NVIDIA GeForce GTX 970/PCIe/SSE2**. Seed 173, complete generated worlds, existing populations, 1920x1080 configured backbuffer, uncapped rendering. Each gameplay phase lasts 15 seconds with synthetic input and begins after initial mesh preparation. These are short representative samples, not prolonged physical playtesting or guarantees for every scene/seed.

| World | Stationary turns FPS / p95 frame ms | Walking turns FPS / p95 frame ms | Estimated terrain buffers, stationary / walking | Process working set, stationary / walking |
| --- | --- | --- | --- | --- |
| Large | 920.0 / 1.54 | 704.0 / 1.76 | 327.2 / 355.3 MiB | 645.5 / 681.2 MiB |
| Massive | 584.3 / 2.22 | 491.7 / 2.44 | 329.1 / 357.1 MiB | 1083.2 / 1112.1 MiB |

Both use 1,373 resident chunks initially and 1,487 during the walking sample; populations remain 512/2,048. Buffer estimates are application buffer-capacity sums, **not driver-reported VRAM**. Walking travel was 15.5/16.4 blocks while turning; this does not represent rapid cross-map travel.

Separate isolated CPU checks generated Large/Massive in 6.21/24.75 seconds. Preparing 64 light pages took 504.31/549.51 ms total and 2.53 MiB of cached light. A complete nine-chunk local Torch relight took 52.90/55.37 ms total; ordinary rendering distributes these builds across frames using the existing maximum-four/soft-three-millisecond build budget. One completed page can exceed that soft budget. Advancing 1,000 simulated seconds took 1.92/1.85 ms and rebuilt nothing. Incremental allocations for the 64-page/relight scenario were 4.38 MiB. Scanning contiguous block rows and reusing opacity/source/queue scratch data reduced the initial measured 64-page cost from approximately 660/685 ms. Initial world/mesh preparation remains a noticeable load-time cost.

### Verification

- `dotnet build`: zero warnings/errors.
- Focused lighting checks: **6/6 passed**, including packed propagation/removal, opacity/transmission, sunlight shafts, Furnace/Lava sources, cross-chunk/finite bounds, clock/brightness, four-way Beds/recipe/collision/drops/sounds, v11/v10 compatibility, required environment validation, legacy mode inheritance, zero-versus-one spawning, layouts/settings, cache eviction/distant edits/local invalidation and allocation-free repeated clock/light queries.
- Compression/persistence checks: **9/9 passed** with native exit 0, including all four sizes, full payload hashes, legacy schemas/migration, metadata-only listing, corrupted/truncated files, non-seekable streams, failed writes/replacements and interrupted temporary files.
- Complete existing regression suite: **188/189 passed**. The sole unchanged failure is the procedural-WAV assertion requiring 22,050 Hz while the existing Pig idle clip is 44,100 Hz. That clip was verified unchanged; runtime audio loading/playback graphics checks pass. No existing check group was removed or disabled; recipe/palette/icon counts and UI fixtures were updated for the explicitly added content and layout.
- Focused real DesktopGL lighting checks: passed with native exit 0. Verified darkness versus maximum Brightness, unchanged actual light/mesh counts, Torch removal and readback binding restoration, visible Sun/Moon, all four textured Bed orientations, combined Settings mouse/layout behavior, paused clocks and refreshed three-second messages. Generated images were inspected. Physical input was not tested.

The final full `--graphics-test` rerun passed with native exit 0: real audio, Creative collision/mob-aware placement, Door acquisition, all held items, inventory/crafting/settings, F1/F2/F3 exact PNG/readback regressions, mobs, water, interpolated gravity, and generated Large/Massive terrain/lake rendering. Large/Massive residency tests verified turning/elevation/edges, edits/re-entry, water eviction and GPU cleanup. Screenshot-error messages in this suite are intentional write-failure injections. After the final bottom-height spawn correction, the focused lighting suite passed 6/6 again and the existing completion suite passed 13/13, including its generated-cave spawn/cap/occlusion checks. No check was weakened to bypass the unrelated WAV failure. Manual follow-up should include ordinary cave exploration with Torch/roof edits, a full-length day/night transition, nighttime Bed use in all orientations, and extended movement through Large/Massive worlds. No publishing/distribution commands were run.


## Audio sample-rate assertion removal (2026-11-06)

Removed only the hardcoded 22,050 Hz condition from [AudioChecks.cs](../Testing/AudioChecks.cs), allowing the replacement Pig recording's 44,100 Hz rate. Audio assets and playback code are unchanged; existing PCM, content-copy, envelope, variation and interaction checks remain in place.

Verification: `dotnet build --no-restore` passed with zero warnings/errors. `--audio-test` reports **4/5 passing**. After removing the rate condition, the asset group reaches a separate pre-existing length/envelope assertion. The replacement Pig idle recording contains a valid 33,506-byte PCM data chunk followed by LIST/ID3 metadata; the test assumes the complete WAV ends immediately after its data chunk and has zero-valued endpoint samples. The file is 33,896 bytes, while that assertion expects 33,550. This independent assertion was inspected but left unchanged to keep the edit limited to the requested sample-rate requirement. README.md was not edited; no publishing commands were run.


## Replacement-audio test cleanup (2026-11-06)

Updated [AudioChecks.cs](../Testing/AudioChecks.cs) at the user's request to remove assumptions specific to the original synthesized recordings: canonical header/chunk offsets, mono PCM16 encoding, minimum sample count, metadata-free file length, zero endpoint samples, fixed peak/RMS/DC ranges, and unique file hashes across variations/materials. The fixed sample-rate requirement was already removed. Replacement recordings may have metadata, different levels/envelopes or supported encodings, and reused sound files without failing these generator-specific assertions.

Retained WAV signatures, required asset/material/species coverage, byte-identical source/build copies, content-pipeline inclusion, queue/rate/spatial/mixing tests, gameplay triggers, movement timing, settings persistence and missing-asset handling. Existing real-device checks still require every clip to decode through MonoGame and every sound group to play. No audio files or runtime playback code changed.

Verification: `dotnet build --no-restore` passed with zero warnings/errors; `--audio-test` passed **5/5**, native exit 0. `--audio-graphics-test` passed with native exit 0, loading all **279** clips and playing all **112** groups, including voice limits, positional mixing, volume/mute, pause, UI, mining and crafting. The missing-asset warning in the CPU suite is its intentional injected-loader failure test, not a failure to load the actual assets. The full non-audio regression suite was not rerun for this test-only cleanup. README.md was unchanged and no publishing commands were run.


## Passive mobs following held Wheat (2026-11-07)

[PassiveMobPopulation.cs](../Entities/PassiveMobPopulation.cs) now steers Pigs, Cows, Sheep and Chickens (including babies) toward a player holding selected Wheat within an inclusive 10-block, three-dimensional range. They use their existing species speeds, movement animation, gravity, swimming and collision/obstacle-hopping code, stopping within approximately two horizontal blocks rather than crowding the player. Held Wheat takes steering priority while in range; switching away or leaving range immediately restores ordinary idle/wander/breeding steering. Attraction neither consumes Wheat nor puts animals into breeding mode. Feeding, breeding, health, drops, population targets, respawn timers, distant suspension and persistence layouts remain unchanged.

[Game1.cs](../Core/Game1.cs) passes the actual selected hotbar item into the existing fixed-step population update. No additional world scans, mesh rebuilds, persistent pursuit state or per-tick allocations are introduced.

Added [WheatFollowChecks.cs](../Testing/WheatFollowChecks.cs), registered its four groups in the existing full regression harness, and added `--wheat-test` / `--wheat-input-test` runners. Verification: `dotnet build --no-restore` passed with zero warnings/errors; **8/8** focused/new-and-existing checks passed. Coverage includes all species and babies, exact/outside/vertical range boundaries, Wheat versus Seeds/other items, switching/range loss, stopping and animations, walls, one-block hopping, breeding priority, identical fixed-step outcomes at 30/60/144/360 rendered FPS, item/population/terrain accounting, pause/invalid time, save/load, allocation-free updates, and existing AI/combat/drops/respawning/breeding checks. The scripted DesktopGL `Game1` input test also passed: real number-key selection distinguishes Wheat from Seeds, all four species approach without consuming Wheat, and pausing suspends following. Physical playtesting and the complete non-targeted regression suite were not performed for this focused change. No publishing commands were run; README.md was unchanged.


## Bed sleep presentation and monster safety (2026-11-07)

Replaced the immediate RMB time jump with [BedSleepController.cs](../Gameplay/BedSleepController.cs), a transient four-second, smoothstep fade managed through a new Sleeping game state. Using either half resolves the complete Bed and its facing, puts the camera 0.3 blocks above the head/pillow surface looking toward the foot, and suspends ordinary gameplay/world simulation. The physical player remains at the original position. Completing the fade skips to morning once and restores the original camera position, yaw and pitch; cancelling restores the view without changing world time. Movement/look/mining/placement/inventory input is suspended, and captured mouse motion/held clicks cannot leak into gameplay when waking.

The existing menu renderer draws an always-visible **Leave bed** button after the dark overlay, sharing the same button rendering/style as every other menu. Mouse click, Enter, Escape or E can leave. The ordinary HUD/held model is hidden during sleep independently of F1/F3 state; screenshots still capture the final composed sleeping frame. [WorldTime.cs](../Worlds/WorldTime.cs) now exposes the actual discrete sunlight level, shared with gameplay lighting; sleeping becomes available at level **11 or lower**, once at least three levels have been lost from maximum 14. Brightness and artificial light do not affect this threshold.

All living hostile entities are checked, including behind walls, against a three-dimensional ten-block radius around the entire two-half Bed (nearest collision-box separation). Passive animals do not block sleep. A denied attempt displays exactly `you may not sleep now; there are monsters nearby` for three elapsed seconds; repeated attempts refresh it. A nearby monster or removed/invalid Bed also safely interrupts an ongoing transition. Added the semicolon font glyph and allocation-free notification wrapping for readable status text at smaller sizes.

Focus loss, saving/exiting, death, respawn and world switching restore/cancel the transient camera state. Capturing a session during sleep serializes the original view, and loading resumes normal gameplay at the original player location with the unskipped time. Existing save format 12, Bed IDs/textures/crafting/drops, inventory, health and default respawning remain unchanged. The transition uses elapsed time with bounded checks over at most 36 hostiles and no full-world scans or extra chunk-light/mesh invalidation. Prior Wheat-following changes were preserved. No assets, dependencies or publication outputs were added.

Main files: [Core/Game1.cs](../Core/Game1.cs), [Core/GameState.cs](../Core/GameState.cs), [Gameplay/SurvivalInteractions.cs](../Gameplay/SurvivalInteractions.cs), [Persistence/WorldSave.cs](../Persistence/WorldSave.cs), [Worlds/WorldTime.cs](../Worlds/WorldTime.cs), [Worlds/BlockLighting.cs](../Worlds/BlockLighting.cs), [Worlds/Beds.cs](../Worlds/Beds.cs), and the existing MenuController/Hud/PixelFont/MenuInformation UI components. Added [BedSleepChecks.cs](../Testing/BedSleepChecks.cs) and [BedSleepGraphicsChecks.cs](../Testing/BedSleepGraphicsChecks.cs), registered the four new groups in the existing full harness, and added `--bed-sleep-test` / `--bed-sleep-graphics-test` commands. The earlier lighting Bed-interaction check now verifies the transition rather than requiring an immediate time jump.

Verification: final `dotnet build --no-restore` passes with zero warnings/errors. **6/6 Bed checks**, **8/8 Wheat checks**, and **6/6 lighting checks** passed. CPU checks cover the precise dusk/dawn threshold, all four orientations and both halves, fade progression/completion at 5/30/60/144/360 FPS, exact restoration, cancellation, the spherical ten-block boundary across species/axes/halves, occluded/removed/arriving monsters, saving mid-fade, death, missing Beds, and allocation-free repeated updates. The dedicated real DesktopGL input/graphics check passed with native exit 0: actual RMB entry, blocked gameplay inputs, Leave Bed/Escape/E/Enter, cursor/capture restoration, no click-through mining, visible fade/button at 320x240 and 1280x720, exact sleeping PNG pixels with F1 hidden, morning/return, focus/death/save interruption, and refreshed three-second monster notices. The existing full interface graphics suite also passed with native exit 0, including menus, crafting/inventory, held models, F1/F2/F3, resizing and screenshot texture-binding regressions. Generated fade and status images were inspected; physical-input or extended playtesting was not performed. The complete non-targeted CPU suite was not rerun for this focused update. README.md was not edited; no publishing commands were run.

## In-world command bar (2026-11-07)

Press **T** during ordinary gameplay to open an initially empty command line. Type a slash command and press **Enter** to run it; the bar remains open for subsequent commands. **Escape** returns to gameplay. **Up/Down** recall up to 32 submitted commands; Left/Right/Home/End, Backspace/Delete and Ctrl+A edit the line. Text uses the existing pixel font, wraps help/errors to the viewport, scrolls long input horizontally around the caret, and is capped at 256 characters. Small tinted responses distinguish errors from success without modal dialogs. The interface remains available with F1-hidden HUD and is included in final-frame F2 screenshots. Gameplay simulation, movement, look, interactions, hotbar changes and autosave timers pause while typing; focus loss enters the existing pause menu. Mouse capture and held-button suppression resume safely on closing. Command history/results are transient and reset when another world begins.

Implemented only five commands through [WorldCommands.cs](../Gameplay/WorldCommands.cs):

| Syntax | Behavior |
| --- | --- |
| `/help [give\|spawn\|time\|gamemode\|items [page]\|mobs]` | Syntax/examples, paginated complete obtainable-item names/IDs, and all mob names/IDs. |
| `/give @p <item> [quantity]` | Grant the local player 1–2304 items (default 1). Accept the existing persistent item ID, registry name with underscores, or placeable-block enum name, case-insensitively. Fill compatible stacks first; respect actual stack limits. Preflight all capacity and reject the entire operation without modifying inventory if it cannot fit. Durability-bearing items receive independent identities and full durability. |
| `/spawn <mob> [quantity]` | Spawn 1–32 mobs (default 1), using species names or existing IDs: Pig 0, Cow 1, Sheep 2, Chicken 3, Zombie 4, Skeleton 5, Spider 6. Search safe ground within roughly 3–12 horizontal blocks and 16 vertical blocks of the player; avoid solid/fluid/player/mob intersections and finite boundaries. Search is bounded, stops on failure/cap, and reports the exact number spawned. Hostiles respect the existing 36-mob cap and Peaceful prohibition; passive summons respect the existing 4096-entity safety limit, including reserved respawns. Explicit summons bypass natural darkness/minimum-distance rules. |
| `/time set <0-23999\|sunrise\|day\|noon\|sunset\|night\|midnight>` | Set exact time of day. Native world clock remains gameplay seconds; numeric command ticks map 20:1 to seconds, preserving the 1200-second day. Presets: sunrise 0, day 1000, noon 6000, sunset 12000, night 14000, midnight 18000. Updates lighting/sky through existing dynamic time logic without advancing other simulation. |
| `/time query` | Report the current numeric time of day. |
| `/gamemode <survival\|creative>` | Use the same settings/session pathway as the existing menu, preserving player inventory/equipment and persisting preferences normally. |

Commands are available in either game mode in this single-player game. Unknown commands, malformed arguments, invalid IDs, out-of-range quantities/times, full inventories and unavailable summon positions produce readable errors rather than silent changes. Summoned passive animals use the existing persisted bred/additional-animal flag: they retain normal adult behavior/drops but do not increase natural population targets or schedule replacement on death. Existing naturally generated populations and respawn queues are unchanged.

Added [CommandLineInput.cs](../Input/CommandLineInput.cs), [CommandBar.cs](../UI/CommandBar.cs), and [MobCommandPlacement.cs](../Entities/MobCommandPlacement.cs). Integration changes are limited to [Game1.cs](../Core/Game1.cs), [GameState.cs](../Core/GameState.cs), small internal insertion methods in the existing passive/hostile population owners, [PixelFont.cs](../UI/PixelFont.cs) punctuation, and [MenuInformation.cs](../UI/MenuInformation.cs) controls. Commands reuse the obtainable-item registry, stack insertion, species configuration and existing session/settings/save systems. No new dependencies, persistent IDs, terrain-generation changes, GPU residency changes, or save-format changes: format 12 already preserves granted items/equipment, summoned mobs, population metadata, world time and game mode.

Added [CommandChecks.cs](../Testing/CommandChecks.cs) (seven groups, registered in the full regression suite) and [CommandGraphicsChecks.cs](../Testing/CommandGraphicsChecks.cs), with `--command-test` and `--command-graphics-test` runners in [Program.cs](../Program.cs). Focused checks cover validation without mutation, every obtainable item name/ID, atomic capacity rejection, stack limits/food, independent full-durability tools, numeric/named time, all seven species, collision/fluids/headroom/bounds, failed bounded searches, caps/Peaceful, unchanged respawn targets, persistence and editor behavior. Scripted real DesktopGL verification passed for actual Game1 text/keyboard routing, all commands, paused simulation, capture, held Enter, error pixels, 320x240/800x600/1280x720 layouts, exact composed F2 pixels, saved settings, session save/load and world switching. Saved help images at 320 and 1280 widths were inspected. These use synthetic input, not physical-input playtesting.

Final verification: `dotnet build --no-restore` passed with **zero warnings/errors**; `--command-test` passed **7/7**; `--command-graphics-test` and the existing `--interface-graphics-test` both passed with native exit **0**. The **complete `--self-test` suite passed 204/204**, including the prior Wheat-following and Bed-sleep groups, all four complete generated-world/save/restore cases, compressed and legacy saves, lighting, water, entities, survival/Creative, movement, crafting, inventory, audio and rendering-geometry checks. Expected injected screenshot-file errors in the interface test are intentional failure-path coverage, not actual capture failures. Physical keyboard/mouse playtesting and extended gameplay were not performed. README.md was unchanged; no publishing commands or distribution builds were run.

## Kill command and entity filters (2026-11-07)

Extended [WorldCommands.cs](../Gameplay/WorldCommands.cs) with `/kill @p`, `/kill @e`, and `/kill @e type=<entity>`, plus `/help kill`. Selectors/filter names are case-insensitive. Filters support `mob` (all seven species), `passive`, `hostile`, `player`, `item` (dropped stacks), `arrow`, `falling_block`, and individual species names or existing numeric species IDs: `pig`/0, `cow`/1, `sheep`/2, `chicken`/3, `zombie`/4, `skeleton`/5, `spider`/6. Examples: `/kill @e type=pig`, `/kill @e type=mob`, `/kill @e type=item`. Invalid selectors, types, duplicate/trailing arguments and malformed filters fail before any state changes. Feedback reports affected entity counts (an item stack counts as one entity).

`/kill @p` and `type=player` explicitly bypass Creative immunity, armor, difficulty and damage invulnerability, without wearing armor or changing ordinary damage rules. [Player.cs](../Gameplay/Player.cs) provides a small internal command-death operation; the existing idempotent session death path handles cursor, crafting inputs, inventory, armor, drops and movement. [Game1.cs](../Core/Game1.cs) immediately transitions to the Death screen after execution and preserves exceptional undropped-item warnings even when death was already handled synchronously. Ordinary respawn/dead-save automatic respawn behavior remains intact; no starter items are granted.

Filtered mob kills go through the existing passive/hostile `Hit` paths, preserving species loot, sheared/baby exceptions, sounds, removal and natural 30–60-second replacement scheduling. Explicitly summoned/bred animals still do not create natural replacement targets. If an exceptional item-entity limit prevents normal loot insertion, those mobs remain alive and the command reports the failed count instead of silently deleting loot or claiming every target died.

Unfiltered `/kill @e` is intentionally a complete entity wipe: clear existing drops first, kill mobs/player through their normal death bookkeeping, then clear newly produced death loot as well. All living mobs, the player, dropped item entities, arrows and active falling Sand/Gravel are removed. Natural population targets/pending respawns remain valid, so naturally killed animals can be replenished after gameplay resumes. `type=item`, `type=arrow` and `type=falling_block` remove only those transient entity collections through small internal owner APIs in [DroppedItems.cs](../Entities/DroppedItems.cs), [Projectiles.cs](../Entities/Projectiles.cs) and [FallingBlockSimulation.cs](../Worlds/FallingBlockSimulation.cs). Falling-entity removal intentionally deletes their carried blocks; it clears active collision-column indexes so they cannot later settle or reappear. It does not delete placed terrain or pending terrain-update checks. No full-world block scans, mesh changes, asset changes, new IDs or save-format changes were added.

Added [KillCommandChecks.cs](../Testing/KillCommandChecks.cs) and registered four additional groups through the existing command/full-suite harness. Extended [CommandGraphicsChecks.cs](../Testing/CommandGraphicsChecks.cs) to exercise actual command routing for sheep-only kills, Creative kill-all, immediate Death/cursor state, respawning without regenerated loot, player-only normal death drops and saving from the death screen. Input is synthetic; no physical-input playtesting was performed.

Verification: final `dotnet build --no-restore` passed with **zero warnings/errors**. `--command-test` passed **11/11**, including the existing ordinary death/respawn check invoked by the new player-death tests. `--command-graphics-test` passed with native exit **0**, covering the actual synthetic Game1 acquisition/command/death/respawn path and composed screenshots. Existing `--survival-test` passed all **9** groups (equipment, recipes, mining, containers, melee, projectiles, persistence, ores and progression); `--wheat-test` passed **8/8**, including passive combat/drops and original population/respawn/breeding behavior. `git diff --check` passed. The full non-targeted suite was not rerun for this extension; its earlier **204/204** result above predates `/kill`. No README.md edits, publishing commands or distribution builds were made. Manual typing/playtesting remains recommended.

## Q / Shift+Q item dropping (2026-11-07)

Added **Q** to drop exactly one item and **Left or Right Shift+Q** to drop the entire source stack. During gameplay the source is the actual selected hotbar slot. In an inventory/container interface, a nonempty cursor-held stack takes priority; otherwise the current mouse position selects a real inventory/hotbar, armor, crafting-input, Chest or Furnace slot. Real Furnace outputs may be dropped, while crafting output previews and Creative browser catalog entries are excluded: Q does not craft, grant catalog items, or create copies of previews. Empty slots/outside areas do nothing. The inventory stays open after dropping. Holding Q does not auto-repeat; each release/press performs one action, independently of simulation/render frame rates. Q typed in the command bar and Q in paused/menu states do not drop gameplay items.

[Game1.cs](../Core/Game1.cs) handles the key edge in gameplay or passes it to [InventoryScreen.cs](../UI/InventoryScreen.cs) with the existing Shift state. The interface hit-tests the current layout rather than a stale rendered hover/tooltip; a Q action takes priority over simultaneous mouse transfers to prevent double operations. Successful gameplay drops cancel pending mining/bow use and play the existing Drop sound; interface drops use the same existing sound and failure notice. No movement, selection, placement, mining, or ordinary mouse-click conventions were changed.

[WorldSave.cs](../Persistence/WorldSave.cs) reuses safe nearby [DropPlacement.cs](../Entities/DropPlacement.cs), `DroppedItems.TrySpawn` and the existing one-second deliberate pickup delay. Source contents change only after successful item creation. [Inventory.cs](../Inventories/Inventory.cs) now supports a single-item cursor drop while preserving the cursor's original return slot until empty. The existing outside-left-click drop still defaults to the whole cursor stack. All paths preserve stack limits, worn durability and equipment instance IDs; unstackable items drop once under either shortcut. No save format, item IDs, assets or world simulation changes were needed. Updated the existing Controls information in [MenuInformation.cs](../UI/MenuInformation.cs).

Added [DropControlChecks.cs](../Testing/DropControlChecks.cs), four groups registered in the complete regression harness, and [DropControlGraphicsChecks.cs](../Testing/DropControlGraphicsChecks.cs). New runners: `--drop-control-test` and `--drop-control-graphics-test`. CPU coverage includes selected/cursor quantities, Survival/Creative, worn equipment identity, cursor return, hovered selection across 320x240/800x600/1280x720 layouts, armor/crafting/Chest/Furnace contents, preview/browser exclusion, blocked/empty/dead-source safety and save/load quantities/metadata/pickup delays. The scripted DesktopGL test passed with native exit 0: actual Game1 Q and both Shift paths, no held-key repeat at 30/60/144/360 render rates, hovered/cursor priority, interface staying open, command/pause isolation, nonstackable items, interface rendering and saved item accounting. Input was synthetic; physical-input playtesting was not performed.

Verification: final `dotnet build --no-restore` passed with **zero warnings/errors**. `--drop-control-test` passed **4/4**, including the pre-existing full-cursor manual-drop/pickup-delay regression; `--drop-control-graphics-test` passed with native exit **0**. The existing `--crafting-test` passed, and `--command-test` passed **11/11**, including kill/death/cursor/crafting item accounting. `git diff --check` passed. The complete non-targeted suite was not rerun for this focused control addition. No README.md edits, publishing commands or distribution builds were made.

## World creation rules and locked cheats (2026-11-07)

Added three creation controls: **Allow Cheats ON/OFF**, **Difficulty Peaceful/Easy/Normal/Hard**, and **Gamemode Survival/Creative**. New-world defaults are cheats **OFF**, **Normal**, **Survival**. All three are selectable before creation, including choosing Creative or Peaceful with cheats off; those selections become that world's fixed rules afterward. Controls support mouse clicks and keyboard navigation/Left/Right/Space/Enter. The generation task captures the selections before starting and saves them in the initial world. [WorldCreationLayout.cs](../UI/WorldCreationLayout.cs) adapts compact layouts with side-by-side name/seed fields and a row of three labeled choices; the Massive-only performance notice and Create/Back buttons remain visible without overlap at 320x240 and larger sizes.

[World.cs](../Worlds/World.cs) owns AllowCheats, SavedDifficulty and SavedGameMode. [Game1.cs](../Core/Game1.cs) restores all active-world values on entry, independently of the previously played world's rules, and resets inactive menu defaults when returning to the main menu. [MenuController.cs](../UI/MenuController.cs) keeps the existing Settings difficulty/mode controls present but grey and noninteractive when no world is active or its cheats are off. Mouse, Left/Right, Space and Enter cannot activate disabled choices; FPS, brightness, bobbing, audio and informational menus remain usable. Cheats-on worlds can change difficulty/mode immediately using the existing settings/session pathway, including Peaceful hostile removal and Creative behavior. There is no post-creation cheats toggle.

Enforcement is also below the UI: [WorldCommands.cs](../Gameplay/WorldCommands.cs) rejects every command for cheats-off worlds, and pressing T in ordinary gameplay displays a short disabled notice rather than opening command entry. [GameSession.ApplySettings](../Persistence/WorldSave.cs) restores locked world values before applying attempted settings, preventing an indirect mode/difficulty change while still allowing presentation settings changes. Mode/difficulty remain active-world runtime values but are no longer read from or written to application-wide settings JSON by [GameSettings.cs](../Core/GameSettings.cs). Existing global FPS/audio/brightness/View Bobbing preferences and their storage path are preserved. Switching worlds cannot transfer mode/difficulty to the next save.

Compressed save **format 13** extends the existing required environment record from 10 to **12 bytes**, retaining time, mode and lighting-rules bytes and appending one validated allow-cheats byte (0/1) and one difficulty byte (0–3). Existing chunk compression, checksums, bounds, temporary-file/atomic replacement, block/entity IDs and every other gameplay payload remain unchanged. All pre-13 saves (raw v1–10, compressed v11 and v12) default to **cheats ON, Normal, Survival**, even if a v12 file previously stored Creative. V12 time is retained; formats lacking time retain the existing morning default. No terrain, inventory, player pose, health, entities, population targets/timers or other state is regenerated. Once saved as v13, a world's chosen rules persist normally instead of being reset again. Updated [SAVE-FORMAT.md](../Persistence/SAVE-FORMAT.md); an old world's rename/resave also retains the migrated rules.

Added [WorldRuleChecks.cs](../Testing/WorldRuleChecks.cs), three groups registered in the full existing harness, and [WorldRuleGraphicsChecks.cs](../Testing/WorldRuleGraphicsChecks.cs), with `--world-rules-test` and `--world-rules-graphics-test` runners. CPU checks cover all 16 combinations of cheats/difficulty/mode, exact save/load, command gating, backend locks, global preference separation, genuine generated v12 fixtures with all old mode markers, v11/v10 migration, exact legacy payload hashes, one-time migration and corruption with repaired checksums. Existing version/legacy/settings assertions were updated for the explicitly requested new behavior, keeping their original terrain/state/integrity checks. Scripted DesktopGL testing actually creates a Small world with cheats off/Hard/Creative and verifies the asynchronous creation selections, empty inventory, greyed Settings, mouse/keyboard locks, other graphics controls, T denial, live allowed-world changes, isolated world switching, saving/loading and inactive main-menu Settings. Creation images at 320x240, 640x480 and 1280x720 and locked Settings images at 320x240 and 1280x720 were generated; compact creation/locked screenshots were inspected. Input was synthetic, not physical playtesting.

Final verification: `dotnet build --no-restore` passed with **zero warnings/errors**; all **215/215** complete `--self-test` regression groups passed, including all-size generation/streamed restoration, compressed saves, older formats, height migration, lighting, prior command/kill/drop controls and gameplay. All three focused world-rule groups passed again after the final layout adjustment preserving the original wide-screen notice font. `--world-rules-graphics-test`, the existing `--interface-graphics-test` and `--command-graphics-test` each passed with native exit **0**. These cover actual synthetic creation/locked/enabled interactions, independent rules, commands and deaths, menu/inventory/rendering behavior, and exact composed F1/F2 screenshot pixels. The interface suite's injected screenshot-write errors are expected failure-path tests. Final responsive creation images were inspected; `git diff --check` passed. Physical-input or extended manual playtesting was not performed. README.md remained unchanged; no publishing commands or distribution builds were run.
