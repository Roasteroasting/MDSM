# Final major survival content milestone

This update adds caves, Lava/Obsidian, farming, breeding and babies, building structures, Armor and Obsidian equipment, underground hostiles, Torches, Buckets, Smokers and Blast Furnaces. Existing finite dimensions, height migration, world management, mining, persistence, inventory and bounded GPU residency remain.

## Recipes

Patterns below are rows separated by `/`; `.` means an empty slot. Patterns translate within a larger grid. Mirrored patterns are registered only where stated. Result previews do not consume ingredients; taking one result performs one atomic operation. Equipment results are fresh, distinct durable items.

| Output | Pattern / ingredients | Result |
| --- | --- | --- |
| Bread | `WWW`, W=Wheat | 1 |
| White Wool | `SS/SS`, S=String | 1; replaces the removed Wool → String recipe |
| Fence | `PSP/PSP`, P=Planks, S=Stick | 3 |
| Fence Gate | `SPS/SPS` | 1 |
| Wooden Door | `PP/PP/PP` | 3 |
| Torch | Coal or Charcoal above Stick | 4 |
| Empty Bucket | `I.I/.I.`, I=Iron Ingot | 1 |
| Smoker | `.L./LFL/.L.`, L=Log, F=Furnace | 1 |
| Blast Furnace | `III/IFI/CCC`, C=Cobblestone | 1 |
| Hoe, all five tool tiers | `MM/.S/.S`, or mirror `MM/S./S.` | 1 |
| Obsidian Pickaxe | `MMM/.S./.S.` | 1 |
| Obsidian Axe | `MM/MS/.S`, or mirror `MM/SM/S.` | 1 |
| Obsidian Sword | `M/M/S` | 1 |
| Obsidian Shovel | `M/S/S` | 1 |
| Helmet, all six Armor tiers | `MMM/M.M` | 1 |
| Chestplate, all Armor tiers | `M.M/MMM/MMM` | 1 |
| Leggings, all Armor tiers | `MMM/M.M/M.M` | 1 |
| Boots, all Armor tiers | `M.M/M.M` | 1 |

Tool materials are Planks, Cobblestone, Iron Ingots, Diamonds, Obsidian. Armor adds Leather before those five. **Every Obsidian material position needs and consumes six blocks**; Sticks still need one. Costs: Pickaxe/Axe 18, Sword/Hoe 12, Shovel 6, Helmet 30, Chestplate 48, Leggings 42, Boots 24. Five per slot cannot substitute for six. There are **79 registered shaped patterns**, including existing recipes and explicit mirrors. No resource shortcuts were added for Seeds, Wheat, Bones, Rotten Flesh or Obsidian.

## Farming and food

Grass/Dirt retain IDs 1/2; dry/wet tilled soil uses compatible block representations and one shared farming system. A Hoe tills Grass or Dirt only when the cell above is clear, costing one durability only on success. All Hoe tiers till identically. Farmland has a 15/16-high mesh and collision surface.

Source or flowing Water within X/Z ±2 and Y ±1 hydrates soil; removing Water dries it. Only nearby soil is queued when Water changes. Seeds plant only above tilled soil and consume one. Crops progress through all seven distinct stages over six equal, individually paused intervals, with a seeded random total of 600–2,400 active seconds. Only wet crops advance. Immature harvest guarantees one Seed; mature harvest guarantees one Wheat and 1–4 Seeds. Breaking support safely resolves the crop too. Sparse deadlines and bounded hydration batches avoid per-frame crop/world scans. Growth progress and pending hydration, including unplanted soil, persist.

Food heals directly: Raw Porkchop 2, Cooked Porkchop 8, Apple 4, Bread 5. Full health consumes nothing; every use needs a new RMB press. Rotten Flesh instead inflicts exactly one health point, bypassing Armor and Armor wear. There is no hunger, poisoning, saturation or regeneration.

## Animals and populations

Wheat puts an eligible adult into breeding mode for 60 active seconds. A matching adult within eight blocks is sought through a small spatial grid and ordinary collision/jump steering. Meeting produces one child; both parents leave breeding mode and receive a 300-second cooldown. Unpaired Wheat is not refunded. Babies cannot breed or be sheared and drop nothing. Their bodies/legs are half scale while heads remain adult-sized; collision bounds include those heads. Maturation takes 900–2,700 active seconds, then normal adult appearance, loot and eligibility return. Baby Sheep mature with fleece intact.

Original species targets are preserved. Bred animals are tagged separately and do not cause additional natural respawn scheduling. A shared live-plus-pending allowance is twice the original total (minimum 32, maximum 4,096). Pending natural replacements reserve space. No bred animals are arbitrarily despawned. Beyond 32 blocks from the player, passive behavior/physics sleep, while age, breeding expiration/cooldown, wool regrowth and other long-term clocks continue. Returning nearby resumes ordinary behavior.

## Armor and tools

Four dedicated inventory slots accept only their matching Helmet, Chestplate, Leggings and Boots. Pieces remain nonstackable with individual GUID/durability, and transfer through the existing cursor rules. Death drops worn equipment along with storage/cursor contents; respawn grants no supplies. The Armor bar hides with no equipment, uses ten half/full icons through 20 points, and shows up to three additional icons above for 26.

| Material | Helmet / Chest / Legs / Boots points | Total | Piece durability in that order |
| --- | --- | --- | --- |
| Leather | 1 / 2 / 2 / 1 | 6 | 55 / 80 / 75 / 50 |
| Wood | 1 / 3 / 3 / 1 | 8 | 80 / 120 / 110 / 75 |
| Stone | 2 / 4 / 3 / 2 | 11 | 150 / 220 / 200 / 140 |
| Iron | 2 / 6 / 5 / 2 | 15 | 180 / 250 / 225 / 165 |
| Diamond | 3 / 8 / 6 / 3 | 20 | 380 / 550 / 500 / 350 |
| Obsidian | 4 / 10 / 8 / 4 | 26 | 2,800 / 4,000 / 3,600 / 2,500 |

Mixed pieces sum fixed points, independent of remaining durability. Reduction is 3% per point, capped at 78%; fractional health persists so small hits remain damaging. The existing integer hearts display uses the ceiling of precise health. Accepted ordinary damage wears each equipped piece once; zero/rejected hits do not. A shared half-second invulnerability window prevents tick/contact damage multiplication. Broken pieces immediately disappear and protection recalculates. Rotten Flesh bypasses reduction, wear and that rejection window.

Wood/Stone/Iron/Diamond Hoes use 59/131/250/1,561 durability. All five Obsidian tools use 10,000. Obsidian efficiency is 10 versus Diamond's 8; its Sword damage is 9 versus 7, other appropriate melee tools 4 versus 3. Obsidian hardness is 50; Diamond/Obsidian Pickaxes harvest it, while lower tiers yield nothing. Matching Diamond/Obsidian mining takes approximately 15/12 seconds, versus 0.45/0.36 seconds on Stone. Existing other-tier mining balance remains.

## Caves and fluids

New-world order is base terrain → preserved ponds/rivers/lakes → caves/Lava → trees/plants → underground Sand/Gravel → ordinary ores → cave-wall bonus ores → fluid initialization. Cave walks branch, twist and vary vertically, with typical diameters around 3–5 blocks and occasional intersections approaching six. They protect Bedrock, world edges, the spawn region, water and existing pools, and supported Sand/Gravel. Occasional rising ends form entrances. No giant cave map or global per-frame cave work is allocated.

Bonus veins start on cave walls, contain 2–8 face-connected cells, avoid merging same-type veins, and retain Coal/Iron Y=4–60 and Diamond Y=4–20. Existing vein proposal rarity and tier requirements remain; deposits cannot displace ores. Low supported 3×3 Lava basins appear at Y=3–18 and initialize with an idle frontier. Shallow cave/entrance carving reserves generated water-feature footprints and shore blending margins, and retains the soil/stone roof beneath intact Grass. Existing saved terrain is never carved or repopulated by generation.

Lava uses source/flow byte states with the existing frontier algorithm, at **0.8 seconds** and at most **512 updates/tick**, versus Water's 0.2 seconds/2,048. Downward flow has priority; horizontal Lava reach is three blocks, without source regeneration. The two simulations use sparse queued-cell sets instead of another world-sized flag array. Water contact converts affected source or flowing Lava to Obsidian, keeping Water; changes remain localized. Lava is rendered opaque in ordinary chunk buffers with appropriate fluid heights and no internal faces. Water retains its translucent pass.

Lava contact causes two player damage per active second, respecting Armor and accepted-hit cooldowns. Ordinary dropped items are immediately destroyed; Obsidian block items and all Obsidian tools/Armor survive. Safe inventory contents are unaffected. Buckets collect sources only and exchange exactly one item/source; full-inventory pickup fails without consuming either. Filled buckets place a source only at a valid target and return one Empty Bucket. Water's qualifying two-source replenishment remains; Lava never makes a new source merely from adjacent sources. Entity feeding/source collection takes priority over a container behind the target.

## Building and Torches

Fences use connected posts/rails, one-block visual/targeting height, and 1.5-block collision barriers for players/mobs. Gates connect and open on RMB, preserving facing/state; open gates remove their collision barrier. Adult passive collision widths are 0.9 blocks (Pig/Cow/Sheep) and 0.6 (Chicken), allowing the one-block gate opening; larger render envelopes retain heads/noses during culling. Baby body collisions are smaller, with adult-sized head clearance. Shared AI jumps clear ordinary one-block steps without clearing 1.5-block fences. Doors need two clear cells and support, use facing/hinge/open state, and remove both halves with exactly one collected Door. Serialization rejects missing metadata/orphaned halves. Changes dirty nearby chunks only.

Torches have small targeting boxes, supported floor/wall placements, drop on support removal, and a 16-block spatial-cell index. They prevent hostile spawning within a 15-block **3D** radius through walls. They emit **no light**; ambient rendering is unchanged.

## Hostiles

| Species | Health | Ordinary speed | Attack | Guaranteed death loot |
| --- | --- | --- | --- | --- |
| Zombie | 20 | 1.6 blocks/s | 3 melee damage | 1–4 Rotten Flesh |
| Skeleton | 20 | 1.5 blocks/s | 2-damage gravity arrows; 1 weak melee | 1–4 Bones |
| Spider | 16 | 2 blocks/s | 2 contact damage; climbing and short lunges | 1–4 String |

Sampling attempts are bounded to 24 at a randomized 4–9-second cadence, globally capped at eight hostiles. Candidates are clear, supported, dry, below Y=50, 24–128 blocks from the player, and outside Torch protection. They do not overlap existing entities. Line of sight is not a spawn prerequisite. Distances beyond 128 immediately despawn regardless of AI; behavioral AI sleeps beyond 32. Visible targets update at a modest cadence, with up to three seconds of last-seen memory after losing sight. Attack-time line of sight is rechecked; contact attacks have a 1.2-second cooldown.

Skeletons retreat inside six blocks, prefer a moderate shooting distance and fire roughly every 2.5 seconds. They aim at a 0.5–1-second-old position with random error and gravity compensation, without leading moving targets. Projectile ownership separates player targets from mob targets. Skeleton loot independently adds 1–4 Arrows at 25% and a damaged Bow at 5%; Bow durability is biased low and persists. Spiders climb blocked walls and lunge on a two-second cadence nearby, below normal player escape speed. Models reuse the existing cuboid builder and dynamic buffers; no terrain remesh occurs on movement.

## Machines

The normal Furnace retains all recipes. Smokers accept food cooking only (currently Raw → Cooked Porkchop); Blast Furnaces accept ore smelting only (currently Iron Ore → Ingot). Sand → Glass and Log → Charcoal remain normal-Furnace recipes.

Specialized machines process twice as fast **and consume burn capacity twice as fast**, preserving item yield per fuel. Progress and burn are stored in normal-recipe units, so existing UI/progress persistence is reused. Coal/Charcoal still power eight operations. A Lava Bucket provides 1,000 base burn seconds, exactly 100 operations in any eligible machine, and leaves an Empty Bucket in the fuel slot. Invalid input does not start fuel or consume ingredients. Taking the empty fuel Bucket is safe mid-burn. All variants preserve slots, progress and fuel independently and drop their unprocessed contents when mined.

## Persistence, assets and architecture

Save **version 10** retains versions 1–9 and the supported legacy height migration. Its appended state contains precise health, four full equipment stacks, sparse structure orientation/open/support records, crop deadlines and pending hydration, animal age/love/cooldown/bred records, hostile poses/health and hostile-arrow ownership indices. IDs 0–31 and 256–286 remain unchanged. Block states still occupy one byte; no full-world soil/structure/crop metadata arrays were introduced. GUID directories, per-user locations, streaming checksums and atomic replacement are unchanged. Old worlds gain no caves/Lava or altered inventories; new gameplay can be used in them normally.

New block IDs: 32 Obsidian; 33/34 dry/wet Farmland; 35–41 Wheat stages; 42 Fence; 43 Gate; 44/45 Door halves; 46 Torch; 47 Smoker; 48 Blast Furnace; 49 Lava source; 50–56 Lava flow; 57 falling Lava. New item IDs: 287 Seeds, 288 Wheat, 289 Bread, 290 Rotten Flesh, 291 Bone, 292–294 Buckets, 295–298 conventional Hoes, 299–303 Obsidian Pickaxe/Axe/Sword/Shovel/Hoe, 304–327 six Armor tiers in Helmet/Chest/Legs/Boots order.

New item art occupies tiles 32–72 of the existing editable [items.png](./Content/Items/items.png); new terrain art uses tiles 29–50. Previous pixels are preserved. Three 64×16 mob skins are loaded through the existing content pipeline. [Artwork notes](./Content/Artwork/final-survival-artwork.md) document source sheets and imports. Runtime assets remain ordinary PNGs and are never regenerated at launch/build.

Main additions are [CaveGenerator](./Worlds/CaveGenerator.cs), [Lava](./Worlds/Lava.cs), [FarmingSimulation](./Worlds/FarmingSimulation.cs), [world details](./Worlds/WorldDetails.cs), [HostilePopulation](./Entities/HostilePopulation.cs), [SurvivalInteractions](./Gameplay/SurvivalInteractions.cs), [ArmorHud](./UI/ArmorHud.cs), and [CompletionSerialization](./Persistence/CompletionSerialization.cs). Existing block/item/recipe, NPC physics/models, player damage, projectile ownership, inventory, chunk meshing, ray/collision and machine classes are extended rather than replaced. Chunk restore now uses one histogram and top-down column searches; block-ID validation is vectorized. See [PERFORMANCE.md](./PERFORMANCE.md) for measured profiling and [CompletionChecks](./Testing/CompletionChecks.cs), [graphics checks](./Testing/CompletionGraphicsChecks.cs), [CPU/state profiling](./Testing/CompletionPerformanceChecks.cs) for verification scenarios.

## Verification and manual checks

Verification completed:

- `dotnet build --no-restore`: zero warnings/errors.
- Complete `--self-test`: **143/143 groups passed**, retaining every earlier group. Changes to earlier assertions reflect the explicitly replaced Wool/String and Tall Grass rules, expanded registries/save version, valid two-half Door fixtures, and distinct render/collision envelopes. Shoreline and intact-soil checks were retained and generation fixed to satisfy them.
- Focused `--completion-test`: **11/11 passed**; prior `--refinement-test`: **15/15 passed**. The new groups exercise every recipe/offset and Obsidian quantity, Armor/fractional damage/wear/death, fluid/source/bucket/accounting rules, farming stages/harvest/hydration, structures/Torches, breeding/age/caps, hostile AI/combat/loot, machine speed/fuel, persistence/version-9 fixtures, and seeded cave/lava/ore constraints. Pending unplanted-soil hydration is explicitly saved/reloaded.
- Full `--graphics-test`: passed, including Small gameplay/menu scenarios and Large/Massive generation/residency/eviction/edits/cleanup. Final focused `--interface-graphics-test` also passed with the added immediate Rotten Flesh death check. Synthetic input exercised tilling, planting, equipment transfers and saving. New models, crop stages, Lava, building geometry, all 73 sprite items, and the 26-point HUD were rendered; existing F1/F2, exact PNG pixels/resolutions, depth/texture-state and deliberate failed-capture regressions remain passing.
- Existing survival walkthrough still starts empty and reaches Diamond tools using mined/crafted/smelted materials.
- `dotnet publish -c Release --no-restore -o bin/Release/final-survival-publish`: passed. Ten runtime texture assets are present; the published Release executable's 11 focused completion groups also pass.
- Existing atlas RGBA checks verified **8,192 item pixels** and **7,424 terrain pixels** unchanged against the pre-update source. Rendered world/model, item and Armor snapshots were inspected.

Controlled profiling generated, simulated, saved and fully restored **all four sizes**, with matching full-block hashes and entity/crop counts. Massive generation was 22.8 seconds, managed world/state memory about 544 MiB, save 6.4 seconds and load 5.3 seconds for a 514.7 MiB file. A 10-simulation-second constructed stress case retained 2,049 passives (including one child), eight hostiles and 24 crops, using 150 ms total CPU work; an isolated growth batch took 0.30 ms. These are CPU diagnostics, not rendered FPS.

Actual 1080p uncapped surface gameplay samples measured approximately **757 FPS turning / 689 walking** on Massive, compared with the historical Milestone-12 420/371 sample. Resident terrain-buffer estimates remained around **266–288 MiB**, with 1,373–1,482 chunks, rather than all 16,384. A separate natural Lava-cave sample with eight hostile fixtures measured about 1,041–1,056 FPS near a finite-world edge, where residency was 708. These short scripts, their diagnostic controls, all-size figures and limitations are detailed in [PERFORMANCE.md](./PERFORMANCE.md). Buffer estimates are not driver-reported VRAM.

Scripted checks do not constitute physical-input or extended playtesting. No specific FPS is promised on other hardware/scenes.

Manual scenarios worth checking: a complete resource/crafting loop from an empty world; caves/entrances and Lava excavation; source collection with a full inventory; Water/Lava mining; wet/dry farming through save/reload; animal pens, pairing, baby maturation and wool; Door/Gate approaches and high-speed collisions; mixed/breaking Armor and fractional damage; Skeleton retreat/aim and Spider walls; Torch exclusion near a cave base; saving mid-burn/growth/breeding/combat; travel, edits and world switching in Large/Massive.

Navigation intentionally remains simple steering/jumps, not a general maze pathfinder. Undergrounds use ambient brightness; Torches do not illuminate. Save files remain uncompressed and Massive IO is correspondingly substantial. Short controlled benchmarks are not guarantees for every scene or hardware configuration.
