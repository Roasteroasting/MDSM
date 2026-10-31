# Milestone 11 implementation and verification

Dependency order: shared item metadata/transactions; recipes and harvesting rules;
coordinate-keyed chest/furnace state and save version 8; deterministic ores;
melee and swept bow projectiles; shared interfaces and original artwork; integration tests.

Persistent block IDs 0-24 and item IDs 256-258 remain unchanged. New blocks:
25 Glass, 26 Iron Ore, 27 Coal Ore, 28 Diamond Ore, 29 Furnace, 30 Chest.
Materials 259-266: Iron Ingot, Coal, Flint, Diamond, Stick, Arrow, String, Charcoal.
Tools 267-282 are four contiguous tiers (wood/stone/iron/diamond), each ordered
pickaxe, axe, sword, shovel. Bow is 283.

Equipment carries a stable instance GUID and remaining durability, with quantity one.
Ordinary stacks retain quantity limits of 64. All stores use the same stack operations.
Version 8 adds equipment metadata, coordinate-keyed block entities and active arrows;
versions 1-7 remain readable without regenerating terrain or changing saved inventories.

Bow ammunition is consumed on release; durability is charged only for a successful mob
hit, locating the original bow by its instance GUID across player/transient inputs,
dropped items and block containers. A destroyed/missing bow cannot be charged twice
or accidentally replaced with a different bow. Arrows do not target their shooter.

Coal/charcoal burn for 80 seconds; logs/planks for 15 seconds; smelting takes 10 seconds.
Input is consumed only at completion. Existing fuel burns down while output is blocked,
but new fuel is never consumed without a valid operation. Processing pauses with the
game, continues in inventory-style interfaces, and never advances offline.

## Registered content

| Block ID | Block | Terrain atlas tiles | Properties / drop |
| --- | --- | --- | --- |
| 25 | Glass | 18 | Solid, clear cutout; no block drop |
| 26 | Iron Ore | 19 | Stone-tier Pickaxe harvests the Ore block |
| 27 | Coal Ore | 20 | Wooden-tier Pickaxe harvests Coal |
| 28 | Diamond Ore | 21 | Iron-tier Pickaxe harvests Diamond |
| 29 | Furnace | 22 top/bottom, 23 side, 24 front | Wood-tier Pickaxe; drops itself and real stored stacks |
| 30 | Chest | 25 top/bottom, 26 side, 27 front | Hand/wooden-category mining; drops itself and real stored stacks |

| Item ID | Item |
| --- | --- |
| 259 | Iron Ingot |
| 260 | Coal |
| 261 | Flint |
| 262 | Diamond |
| 263 | Stick |
| 264 | Arrow |
| 265 | String |
| 266 | Charcoal |
| 267 / 268 / 269 / 270 | Wooden Pickaxe / Axe / Sword / Shovel |
| 271 / 272 / 273 / 274 | Stone Pickaxe / Axe / Sword / Shovel |
| 275 / 276 / 277 / 278 | Iron Pickaxe / Axe / Sword / Shovel |
| 279 / 280 / 281 / 282 | Diamond Pickaxe / Axe / Sword / Shovel |
| 283 | Bow |

Placeable items use their block IDs, as before. Raw Porkchop 256, Leather 257,
Feather 258 and all earlier blocks retain their identities. New worlds receive no
items; old inventories, health, actors, populations, water, falls and terrain persist.

## All crafting registrations

There are **29 patterns / 24 distinct outputs**. Each recipe translates wherever its
rectangle fits. All unspecified cells must be empty. Only the two Axes and Bow
orientations explicitly registered below may mirror. Equipment results are quantity
one with fresh identity and full durability. Merely displaying a result owns no item.
The entire result must fit the cursor before any ingredient is consumed.

`M` = Planks / Cobblestone / Iron Ingot / Diamond for the four tiers;
`S` = Stick, `T` = String, `.` = required empty cell. Rows below are separated by `/`.

| Result | Pattern / ingredients | Output |
| --- | --- | ---: |
| Wooden Planks | One Log (1x1) | 4 |
| Sticks | `P / P`, P = Planks | 4 |
| Crafting Table (unchanged) | `PP / PP` | 1 |
| Chest | `PPP / P.P / PPP` | 1 |
| Furnace | `CCC / C.C / CCC`, C = Cobblestone | 1 |
| Pickaxe, every tier | `MMM / .S. / .S.` | 1 |
| Axe, every tier | `MM / MS / .S`, or `MM / SM / S.` | 1 |
| Sword, every tier | `M / M / S` | 1 |
| Shovel, every tier | `M / S / S` | 1 |
| Arrows | Flint / Stick / Feather | 4 |
| String | One White Wool (1x1) | 2 |
| Bow | `.ST / S.T / .ST`, or `TS. / T.S / TS.` | 1 |

Planks, Sticks, String and Tables fit the personal grid. Three-row recipes require a
table. There is no recipe book, shift crafting, shapeless rearrangement or free supply.
The automated walkthrough acquires its logs and ores from a real generated world,
collects actual mined drops, moves ingredients with the shared cursor, and reaches
a Diamond Pickaxe from an empty inventory. It repositions the test player between
sites and feeds mining durations; it is not physical-input/manual walking verification.

## Mining, durability and damage

[MiningRules.cs](./Gameplay/MiningRules.cs) owns categories/hardness/harvest tiers.
Pickaxes match minerals, Axes match wood, Shovels match earth. Swords are combat
weapons. Suitable efficiency is Wood 2x, Stone 4x, Iron 6x, Diamond 8x. Time is
`hardness * 1.5 / efficiency`, with a 4x penalty for an under-tier Pickaxe.
Stone hardness is 1.5, Cobblestone 2, Ores 3 and Furnace 3.5. Existing wood/earth
base durations remain. Grass and Dirt have equal hand timing; plants remain instant.

Hands and wrong categories cannot break minerals. Pickaxes can slowly destroy
under-tier Ores but cannot obtain their drops. Wood or better harvests Stone,
Cobblestone and Coal Ore; Stone or better harvests Iron; Iron or better harvests
Diamond. Bedrock remains unbreakable. Stone yields Cobblestone. Gravel yields one
Flint with 10% probability, otherwise one Gravel, never both. Coal and Charcoal
remain distinct. Other ordinary blocks retain their own drops, except new Glass,
which breaks without a block drop.

| Tier | Uses (all four tools) | Sword damage | Other tool damage |
| --- | ---: | ---: | ---: |
| Wooden | 59 | 4 | 1 |
| Stone | 131 | 5 | 2 |
| Iron | 250 | 6 | 2 |
| Diamond | 1561 | 7 | 3 |

Tiered tools wear once per successful break/hit, including physically allowed
off-category mining. Misses, interrupted mining, invalid reach and blocked transactions
do not wear. Switching slot, instance, tool state or target resets mining progress.
Last use removes the item; carried equipment posts a brief break notice. Damaged
equipment has a durability bar and a concise hover label. Full equipment omits the bar.
Fists retain random 1-2 damage, 0.4-second cooldown and existing passive-mob feedback.

## Containers and processing

[BlockEntities.cs](./Worlds/BlockEntities.cs) is a sparse position/block-identity registry,
not chunk/GPU state. Chests have 27 slots; Furnaces have input/fuel/output, progress,
remaining burn and original burn duration. Closing returns only player cursor/crafting
inputs; container contents remain in place. Shared transactions handle transfers,
half splitting, swaps, stack limits and equipment identity. Output slots reject insertion.

Smelting recipes: Sand -> Glass, Iron Ore -> Iron Ingot, Log -> Charcoal, all 1:1.
Each takes 10 seconds; input remains until completion. Coal/Charcoal each burn 80
seconds (8 operations); Logs and Planks burn 15 seconds (two can power 3 operations).
Lit fuel burns while output is blocked but input/progress wait. New fuel starts only
for a valid operation. Partial progress survives fuel starvation and saving; removing or
changing input resets it. Contents and timers are independent for each Furnace.

Only an active hash set ticks. Inventory-style interfaces keep Furnaces processing
while suspending existing player/combat/mob/item motion. True Pause, Settings,
death/main menus and closed application stop processing. No offline processing occurs.
Destroying a container preflights drop capacity, detaches its state, removes the block,
then releases exactly the block and real stored stacks. Unfinished smelting gives no output.

## Arrows

RMB holds/draws up to one second and releases one Arrow if available. Minimum draw
is 0.1 seconds. Power determines speed (12-30 blocks/second) and damage (1-5).
Gravity is 18 blocks/second squared. Swept voxel/entity checks, with bounded curved
substeps, stop on the nearest solid surface or mob. Collision never queries the player.
Only accepted mob hits wear the originating Bow, looked up by GUID across player
slots, cursor, crafting inputs, drops and block containers. Damage/removal occur once.
Bow capacity is 384. Misses, drawing and release do not wear it. An already broken
or missing Bow cannot cause another instance to wear.

Flights persist with position, velocity, age, damage and originating identity. Arrows
despawn on block impact, finite horizontal bounds, below Y=0, above Y=192 or at
12 gameplay seconds. At most 256 exist. Menus cancel unfinished drawing and freeze
flights. Rendering is a small dynamic shaft/feather pass with restored graphics state,
not terrain meshing. No retrieval, critical hits or alternate arrows are implemented.

## Measured ore generation

Generation replaces only Stone after terrain/water/decorations are complete, using
world-coordinate compact branching walks and deterministic per-chunk randomness.
Touching deposits of the same type are rejected to keep connected veins at 2-8 cells.
No ordinary frame scans the world for Ores; no old save is regenerated.
Coal and Iron use Y=4-60; Diamond Y=4-20. Horizontal chunk proposal budgets are
18 Coal, 10 Iron and one Diamond vein. These are practical tunnel-accessible bands,
not modern Minecraft distributions. Grass/Dirt, Bedrock, shore beds and water remain intact.

Full generated seed-173 worlds on an i5-11400, Debug build (counts are actual placed cells):

| Size | Coal | Iron | Diamond | Connected veins | Ore phase |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | 22,181 | 12,497 | 1,243 | 7,211 | ~19 ms |
| Normal | 88,685 | 50,410 | 5,166 | 28,874 | ~81 ms |
| Large | 354,897 | 200,459 | 20,631 | 115,514 | ~312 ms |
| Massive | 1,421,564 | 800,703 | 82,362 | 462,235 | ~1,179 ms |

Small seed -907 independently measured 22,050 / 12,438 / 1,318 cells and 7,221 veins.
Independent flood fills checked sizes and found 977 / 934 crossing-chunk veins in
Small seeds 173 / -907. Flint measured **987/10,000** with a deterministic RNG.
Full-world hashes/regeneration cover all sizes. Ore generation uses an eight-cell
scratch span and bounded proposals rather than another full-size block/height array.

## Save version 8

All prior header/raw blocks/inventory/drop/mob/population/falling fields retain their
prefix layout. After version-7 data, the SHA-256-covered extension writes:

1. Sparse equipment metadata: count and 25-byte records (owner byte, 32-bit slot/drop
   index, 32-bit remaining durability, 16-byte GUID). Plain inventory/drop prefix entries
   remain their existing 3/23 bytes.
2. Container count and coordinate/block/slot records. Each stack is 23 bytes (ID, count,
   durability, GUID); three double timers follow each container.
3. Arrow count and 48-byte flight records (position/velocity floats, age, damage, GUID).

Counts, bounds, stack limits, positive durability, duplicate equipment identities,
container/block correspondence, fuel/progress and arrow values are validated. Saved
negative sentinel durability cannot silently refill equipment. Versions 1-7 still read
their original layouts and have no new block-entity or arrow state. Version-1 inventory
migration retains its former once-only behavior; stored inventories are never refilled.
Legacy height migration and all four dimensions remain supported. Atomic temporary-file
replacement, checksum checking, GUID save directories and app-data paths are unchanged.
Older executables cannot read version 8. Container data is independent of mesh residency.

## Rendering and performance

Terrain keeps its bounded residency, far plane, fog and incremental upload budget.
Ores are opaque cubes and change no initial interior geometry. Glass is clear binary
cutout with opaque frame/glints, depth testing, and hidden Glass/Glass faces; it does
not require translucent sorting or modify the Water pass. New sprites share one
128x64 Content texture; drop rendering groups them into one batch rather than one
draw per item. First-person geometry uses per-item atlas rectangles and existing
camera-space presentation. No new dependencies were introduced.

GTX 970, 1920x1080, uncapped, seed 173, controlled full-scene + 120 Hz simulation:

| Scenario | FPS | CPU update / draw | p95 frame | Resident chunks | Estimated terrain buffers |
| --- | ---: | --- | ---: | ---: | ---: |
| Large fixed camera | 1,534 | 0.068 / 0.557 ms | 1.41 ms | 1,373 | 179.6 MiB |
| Massive fixed camera | 642 | 0.675 / 0.848 ms | 4.83 ms | 1,373 | 180.4 MiB |
| Massive actual Game1, turning | 426 | 1.232 / 1.068 ms | 5.36 ms | 1,373 | 180.4 MiB |
| Massive actual Game1, walking/turning | 374 | 1.375 / 1.248 ms | 5.39 ms | 1,482 (hysteresis) | 194.6 MiB |

The controlled prior values were approximately 1,496/648 FPS. These runs show no
material regression in those scenarios, not a universal FPS guarantee. Actual Game1
walking covered 18.5 blocks with all 2,048 mobs; working set was about 844 MiB.
Controlled stages measured zero managed bytes/frame; actual walking averaged nine
bytes/frame including periodic rebuild/diagnostic work. These are application buffer
estimates, not driver-reported VRAM. No CPU tests ran concurrently with measurements.
Benchmarks do not certify extreme numbers of active Furnaces/arrows or all hardware.

## Verification

- `dotnet build`: succeeded, zero warnings/errors.
- Focused `--survival-test`: all nine groups passed, including the empty-start resource walkthrough.
- Complete `--self-test`: **117/117 groups passed** on the final code.
- Complete `--graphics-test`: passed all interface, pig, mixed-mob, water and full-size terrain/resource-lifecycle runners, including new survival checks and strict repeated F1/F2/F3 PNG/state comparisons.
- `dotnet publish --no-restore -c Release`: succeeded; all ten compiled texture assets are present. The Debug/published survival texture hashes match.
- `--performance-test Large`, `--performance-test Massive` and `--gameplay-performance-test Massive`: completed sequentially; results above.
- `git diff --check`: clean. No new dependencies or storage paths.

Existing checks are retained. Superseded assumptions were updated explicitly:
mineral mining now uses a valid Pickaxe; drop checks use harvesting rules; recipe
count covers the new registrations; legacy fixtures stop before the version-8 extension.
The old prefix offsets remain unchanged. Graphics checks include real input-path
Chest/Furnace transfers and observed smelting, all new held sprites/icons, durability
bars, clear Glass, world textures, arrows, pause, saving and repeated F1/F2/F3 frames.
Representative render snapshots were inspected. No physical-input/manual playtesting
or extended survival session is claimed.

## Files and architecture

- [Items.cs](./Inventories/Items.cs), [Inventory.cs](./Inventories/Inventory.cs),
  [Crafting.cs](./Inventories/Crafting.cs): registry, equipment instances and shared transactions/recipes.
- [MiningRules.cs](./Gameplay/MiningRules.cs), [MiningController.cs](./Gameplay/MiningController.cs),
  [CombatController.cs](./Gameplay/CombatController.cs), [PassiveMobPopulation.cs](./Entities/PassiveMobPopulation.cs): harvesting, durability and shared damage.
- [Blocks.cs](./Worlds/Blocks.cs), [World.cs](./Worlds/World.cs),
  [OreGenerator.cs](./Worlds/OreGenerator.cs), [BlockEntities.cs](./Worlds/BlockEntities.cs): definitions, ore creation and sparse persistent processing state.
- [DroppedItems.cs](./Entities/DroppedItems.cs), [Projectiles.cs](./Entities/Projectiles.cs),
  [WorldSave.cs](./Persistence/WorldSave.cs), [WorldSerializer.cs](./Persistence/WorldSerializer.cs),
  [SurvivalSerialization.cs](./Persistence/SurvivalSerialization.cs): item identity, arrows and versioned persistence.
- [Game1.cs](./Core/Game1.cs), [InventoryScreen.cs](./UI/InventoryScreen.cs),
  [Hud.cs](./UI/Hud.cs), [MenuInformation.cs](./UI/MenuInformation.cs): interaction, active GUI processing and feedback.
- [TextureAtlas.cs](./Rendering/TextureAtlas.cs), [ChunkMeshBuilder.cs](./Rendering/ChunkMeshBuilder.cs),
  [ItemSpriteGeometry.cs](./Rendering/ItemSpriteGeometry.cs), [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs),
  [FirstPersonRenderer.cs](./Rendering/FirstPersonRenderer.cs), [ProjectileRenderer.cs](./Rendering/ProjectileRenderer.cs): existing atlas/meshing extension and isolated dynamic rendering.
- [ProgressionChecks.cs](./Testing/ProgressionChecks.cs), [ProgressionGraphicsChecks.cs](./Testing/ProgressionGraphicsChecks.cs),
  existing [Testing/](./Testing/), [README.md](./README.md): expanded tests, migration fixtures and documentation.
- [terrain.png](./Content/Blocks/terrain.png), [survival.png](./Content/Items/survival.png),
  [Content.mgcb](./Content/Content.mgcb), [artwork prompts](./Content/Artwork/survival-artwork.md): original built-in image generation and one-time pixel-center preparation; prior tiles 0-17 unchanged.

## Limits and manual checks

Front faces currently point North; there is no placement-facing selection, double Chest
or animated lid. Furnace feedback is in its UI; no world lighting or smoke was added.
Glass is clear cutout rather than tinted/refraction-based transparency. Arrows despawn
instead of embedding/retrieval. These are deliberate simple implementations of this scope.

Manual testing remains for physical draw/release, unusual inventory transfers during
flight, long tunneling/resource balance, storage destruction/recovery under crowded
conditions, many simultaneous active Furnaces/arrows, other drivers/resolutions and
extended Large/Massive play. Old worlds deliberately receive no natural Ores; full
mineral progression requires a newly generated world (or already placed new resources).
No caves, lava, hostile mobs, armor, enchanting, torches, food cooking or unrelated systems
were introduced.
