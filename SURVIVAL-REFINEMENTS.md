# Milestone 12 — survival refinements

## Implemented mechanics

- Underground Sand/Gravel deposits use an independent seeded, connected growth pass. Each accepted clump contains 10–30 cells, replaces Stone only, has a solid roof/support, and stays at Y=5–56. A 35% per-chunk proposal keeps them moderately uncommon while scaling with horizontal area. Ore generation follows and may replace Stone/Sand/Gravel. Its seeds, proposal counts, vein sizes, vertical bands and tier restrictions are unchanged.
- Tall Grass drops nothing. Ordinary Leaf breaks and natural decay independently roll Sapling 5%, Stick 2%, Apple 0.5%; multiple drops are possible. Shears instead collect the Leaf block.
- Natural/grown Leaves use a sparse set of integer cell keys. Player-placed Leaves are absent from that set and cannot decay. Removing Logs/Leaves schedules nearby natural Leaves for checks after 20–45 active seconds. Support is a face-connected Leaf path to a Log, at most four steps. Each search visits at most the 129 positions within that Manhattan radius. Leaf and growth queues share a 32-entry budget per simulation update. Stable worlds do not scan all Leaves.
- Saplings accept clear cells above Grass or Dirt. Breaking returns the Sapling. Growth first attempts after 60–1,800 active seconds, using the existing height-4–6, maximum-5×5 tree style. The complete trunk/canopy is checked before writes; obstruction retries after 30–60 seconds without deleting the plant or overwriting structures. Growing trees use normal chunk invalidation and mark their new Leaves natural. Sapling timers are sparse and bounded to 65,536 planted cells.
- Shears are nonstackable, have 238 uses, and craft from two diagonal Iron Ingots in either orientation, with valid 3×3 offsets. A successful RMB shear yields 1–4 White Wool, leaves the Sheep alive, removes its visible fleece, and costs one use. Regrowth takes 180–360 active seconds. A sheared Sheep drops no additional Wool on death before regrowth. Leaf collection takes 0.10 seconds and costs one use. Interrupted/failed interactions do not wear Shears. Sheep targeting retains reach and terrain occlusion; ordinary container interaction remains available when holding Shears.
- Apples restore 4 health, Raw Porkchops restore 2, and Cooked Porkchops restore 8, capped at 20. Full health consumes nothing, and each consumption requires a fresh RMB press. Porkchops of both kinds have stack limit one; Apples retain a 64-item limit. Furnaces cook Raw Porkchop → Cooked Porkchop 1:1 in the existing 10-second operation, respecting blocked/nonstackable output and existing fuels.
- Bows cannot begin or continue drawing without an Arrow in the inventory. Their existing valid-shot behavior, ammunition use, durability and projectiles are preserved.
- Save/exit's menu notification expires after eight seconds of elapsed menu-update time. Errors retain their existing persistent presentation.

New worlds and respawns still receive no starter supplies. No terrain/decorations are generated during loading.

## Movement and balance

The existing 120 Hz clock, axis collision/substeps, vertical-distance fall tracking, swimming and crouch support checks remain. Walking now accelerates toward 4.3 blocks/second, with ground acceleration 60 and air acceleration 12 blocks/second². No-input drag is exponential: 24/second on ground, 2/second in air, 8/second in water. Water acceleration is 12; existing 45% swimming speed and upward controls remain. Crouch retains 35% walking speed, standing/crouch eye heights 1.62/1.50, and the same jump impulse.

Gravity is 28 blocks/second²; jump impulse 8.4 blocks/second; terminal fall speed 60. The fixed-step jump rises approximately 1.23 blocks. Collision blocks the appropriate velocity component, retaining wall sliding. Movement state resets on death/respawn as before.

View Bobbing defaults on and persists in the application settings file. It responds to actual grounded travel, fades while stopped, and is suppressed in menus. One smooth sine/cosine cycle spans three blocks of actual horizontal travel (about 1.43 cycles/second at normal walking speed). Maximum local offsets are 0.060 horizontally and 0.075 vertically, scaled by speed. Phase stops advancing when stationary or airborne; the effect fades completely to zero, and disabling the setting clears it immediately. Only the render view is offset: logical eye position, forward direction, block/combat rays, projectile origin/direction and collision are unchanged. No hand/swing animations or new camera controls were added.

Matching tiered tools take 1.6× their former mining duration. Hand/mismatched-category timing, hardness, tier ratios, harvesting, combat and durability rules remain unchanged.

| Block / matching tool | Wooden before → after | Stone | Iron | Diamond |
| --- | --- | --- | --- | --- |
| Stone / Pickaxe | 1.125 → 1.800 s | 0.5625 → 0.900 | 0.375 → 0.600 | 0.28125 → 0.450 |
| Log / Axe | 1.250 → 2.000 | 0.625 → 1.000 | 0.4167 → 0.6667 | 0.3125 → 0.500 |
| Dirt / Shovel | 0.625 → 1.000 | 0.3125 → 0.500 | 0.2083 → 0.3333 | 0.15625 → 0.250 |

## Atlas and IDs

[Content/Items/items.png](./Content/Items/items.png) is the runtime 256×256 RGBA item atlas, with 256 row-major 16×16 slots. [Artwork notes](./Content/Artwork/refinements-artwork.md) document every slot, the four new icons' built-in imagegen prompt and one-time import. The original sheets remain uncompiled historical references for exact pixel checks.

Tiles 0–2 contain the original Porkchop/Leather/Feather; 3–27 contain all 25 former survival-sheet tiles. New tiles are 28 Sapling, 29 Apple, 30 Shears, 31 Cooked Porkchop. The Sapling's world cross uses terrain tile 28. The registry maps these independently from persistent IDs: Sapling block/item **31**, Apple **284**, Shears **285**, Cooked Porkchop **286**. No previous ID changed.

All 7,168 migrated source pixels are tested channel-for-channel, including RGB under alpha zero. An additional comparison against the pre-update terrain image verified that all 65,280 pixels outside the new Sapling tile are unchanged. Runtime sprites use one shared atlas batch; block cubes retain their existing terrain texture path. ContentManager owns both atlases. Filtering, texel centers, depth/cutout rendering, and graphics-state restoration are preserved.

## Persistence and compatibility

Save format **9** retains the complete version-8 prefix and appends:

1. Natural Leaf count and sparse 32-bit cell keys, `(Y × depth + Z) × width + X`.
2. Counted pending Leaf-decay and Sapling-growth entries, each cell key plus 64-bit remaining gameplay seconds.
3. Counted sheared-Sheep entries, each index into the non-pig mob array plus 64-bit remaining regrowth seconds.

The existing block stream, containers, equipment identity/durability, player state, populations, respawn timers, falling blocks, arrows, checksum and atomic replacement retain their layouts. No full-world mesh or additional per-block flag array is stored. Counts, positions, block identity, finite timers and Sheep species are validated. Versions 1–8 remain readable, including the existing supported 64→128 height migration. Earlier application builds cannot read version 9.

Older saves have no reliable Leaf provenance: their existing Leaves conservatively remain non-decaying. Newly grown trees in those worlds acquire normal natural provenance. This protects unknown player builds without scanning/reclassifying old terrain.

Old stacked Raw Porkchops are read through a migration-only representation, then split before a world is returned to gameplay. Their original slots keep one, empty slots receive singles, and overflow becomes delayed dropped items near the player/container. Existing world drops are split into singles with their positions/velocity/delay preserved. Equipment metadata is restored before redistribution so its indices/identity remain intact. This migration does not grant items and does not repeat after a version-9 save. A defensive overflow beyond the existing one-million dropped-entity bound fails loading with an error and leaves the original file intact; it never truncates quantity.

View Bobbing joins FPS preferences in the existing application settings JSON; missing/invalid values safely retain the enabled default. No save/settings/screenshot locations changed.

## Files

| Area | Added / changed |
| --- | --- |
| World generation and vegetation | Added [UndergroundDeposits.cs](./Worlds/UndergroundDeposits.cs), [VegetationSimulation.cs](./Worlds/VegetationSimulation.cs); extended [World.cs](./Worlds/World.cs), [WorldDecorator.cs](./Worlds/WorldDecorator.cs), [OreGenerator.cs](./Worlds/OreGenerator.cs), [Blocks.cs](./Worlds/Blocks.cs) |
| Item storage/crafting/food | [Items.cs](./Inventories/Items.cs), [Inventory.cs](./Inventories/Inventory.cs), [Crafting.cs](./Inventories/Crafting.cs), [ItemUse.cs](./Gameplay/ItemUse.cs), [BlockEntities.cs](./Worlds/BlockEntities.cs) |
| Mining and movement | [MiningRules.cs](./Gameplay/MiningRules.cs), [MiningController.cs](./Gameplay/MiningController.cs), [Player.cs](./Gameplay/Player.cs), [Camera.cs](./Gameplay/Camera.cs); added [ViewBobbing.cs](./Gameplay/ViewBobbing.cs) |
| Entities | [MobDefinitions.cs](./Entities/MobDefinitions.cs), [PassiveMob.cs](./Entities/PassiveMob.cs), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs), [DroppedItems.cs](./Entities/DroppedItems.cs), [Projectiles.cs](./Entities/Projectiles.cs) |
| Save/session | Added [RefinementSerialization.cs](./Persistence/RefinementSerialization.cs); extended [WorldSerializer.cs](./Persistence/WorldSerializer.cs), [SurvivalSerialization.cs](./Persistence/SurvivalSerialization.cs), [WorldSave.cs](./Persistence/WorldSave.cs) |
| Rendering | [TextureAtlas.cs](./Rendering/TextureAtlas.cs), [ItemSpriteGeometry.cs](./Rendering/ItemSpriteGeometry.cs), [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs), [FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs), [PassiveMobModels.cs](./Rendering/PassiveMobModels.cs) |
| Coordination/settings/UI | [Game1.cs](./Core/Game1.cs), [GameSettings.cs](./Core/GameSettings.cs), [MenuController.cs](./UI/MenuController.cs), [SettingsLayout.cs](./UI/SettingsLayout.cs), [MenuInformation.cs](./UI/MenuInformation.cs) |
| Assets | Added [items.png](./Content/Items/items.png), [refinements-source.png](./Content/Artwork/refinements-source.png), [artwork notes](./Content/Artwork/refinements-artwork.md); only terrain tile 28 and [Content.mgcb](./Content/Content.mgcb) changed |
| Regression checks | Added [RefinementChecks.cs](./Testing/RefinementChecks.cs), [ItemAtlasChecks.cs](./Testing/ItemAtlasChecks.cs), [RefinementGraphicsChecks.cs](./Testing/RefinementGraphicsChecks.cs); updated existing crafting, inventory, persistence, food/mob, physics, atlas and graphics fixtures for the intentional policies, plus [PrototypeChecks.cs](./Testing/PrototypeChecks.cs) and [Program.cs](./Program.cs) |
| Documentation | [README.md](./README.md), this report and the artwork notes |

Existing check groups remain enabled. Tests that previously assumed stacked Porkchops now check single-item accounting; independent old-format fixtures continue to test stacked-food migration. Lake-bed classification accepts exposed ores, and movement fixtures measure steady speed after acceleration rather than assuming the old instantaneous 5-block/second motion.

## Verification

- `dotnet build --no-restore`: passed, **zero warnings/errors**.
- Full `--self-test`: **130/130 groups passed**. All four sizes were generated, checked and streamed through save/load; legacy migration, old gameplay and bounded rendering checks remain included.
- Focused `--refinement-test`: **13/13 passed**, including the additional zero-allocation assertion across **10,000 idle vegetation ticks**.
- Full `--graphics-test`: passed (exit 0). Includes real scripted RMB shearing, no-ammunition Bow input, paused timers, wool/state saving, Bobbing settings input/persistence/layout, eight-second save/exit expiry, new held sprites, sheared geometry, all previous inventories/menus/entities/water, repeated F1/F2 screenshots, exact PNG pixels/resolution and forced write failures, plus Large/Massive residency/edits/cleanup. The deliberate screenshot-write failure prints an expected error without failing the suite.
- `dotnet publish -c Release --no-restore -o bin/Release/milestone12-publish`: passed. Published output contains the seven runtime textures: terrain, water, unified items, and four mob skins. No fragmented item sheets are built.
- Rendered item-sheet and sheared-Sheep snapshots were inspected. These are scripted graphics checks, not physical-input playtesting.

Measured new Small-world underground deposits: seed 173 **1,737 blocks**, seed −907 **1,834**, seed 137 **1,931**. Every ore coordinate and type matched the deposit-free reference in these tests. Across 200,000 Leaf loot trials: **10,022 Saplings**, **4,007 Sticks**, **989 Apples**, with **259 multiple-drop rolls**.

The unchanged Large/Massive rendering architecture retained **1,373** nearby resident chunks in the graphics checks, with approximately **181.6 / 180.7 MiB** estimated terrain buffers. These are buffer estimates, not driver-reported VRAM.

An isolated actual-Game1 benchmark used a fully generated Massive world, seed 173, 1920×1080 uncapped, 2,048 mobs, and 15 seconds each of turning and walking/turning:

| Phase | FPS | p95 frame | CPU Update / Draw | Resident | Terrain-buffer estimate | Process working set |
| --- | --- | --- | --- | --- | --- | --- |
| Stationary turns | 419.8 | 5.39 ms | 1.249 / 1.083 ms | 1,373 | 180.4 MiB | 866.0 MiB |
| Walking turns | 370.5 | 5.48 ms | 1.393 / 1.258 ms | 1,482 (hysteresis) | 194.6 MiB | 879.5 MiB |

The recorded allocation averages were 115 and 9 bytes/rendered frame, including scene/resource preparation. They are not proof of a recurring per-tick allocation: the isolated new idle vegetation loop allocates zero. Historical Milestone-11 results were approximately 426/374 FPS; this short run remains comparable, though changed movement and ordinary timing variation prevent a strict identical-path comparison. No new renderer, view distance, fog setting or population reduction was introduced.

Remaining manual checks: movement/air-control feel, bobbing comfort, keyboard/mouse interaction over a longer survival session, and long-running tree/decay/regrowth behavior in player-built structures. The automated long timers use simulated active time; no physical-input or extended real-time growth session is claimed. The benchmark is a short synthetic sample, not a guarantee for every scene/hardware configuration.

## Bobbing and decay pacing refinement

The targeted follow-up keeps the same support/loot/provenance rules, queue budget and save format. New Leaf deadlines are randomized over 20–45 seconds; saved pending deadlines retain their exact remaining times, including shorter timers from earlier version-9 saves. The focused suite now has **15/15 passing groups**. Measured bobbing cadence at 30/60/144/360 updates per second was 1.429/1.431/1.433/1.433 Hz. Three 57-leaf fixtures (seeds 173, −907, 137) started decaying at 21.0/20.4/20.6 seconds and finished at 44.7/43.9/44.8 seconds, with exact saved countdowns and protected player-placed Leaves. Build passed with zero warnings/errors. Perceptual walking feel still needs physical playtesting.
