# Large/Massive performance investigation

## Reproduction and measurements

Tests used the Debug build on an i5-11400, NVIDIA GeForce GTX 970 (4 GiB), and 16 GiB system RAM. The active OpenGL renderer identified itself as `NVIDIA GeForce GTX 970/PCIe/SSE2`. Each comparison used the complete generated world, seed 173, a 1920x1080 client area, uncapped presentation with VSync disabled, the same center camera 12 blocks above spawn, and four directions at two-second intervals. Each isolated stage lasted eight seconds. No terrain was removed or replaced with a small fixture for the benchmarks.

The comparison harness runs terrain alone, terrain plus water, mobs alone, then terrain/water/mobs with the existing 120 Hz mob/fluid/falling simulation. The last stage excludes HUD/player/input costs; separate sustained Game1 measurements cover those. CPU timings are stopwatch measurements, including CPU-side driver stalls. They are **not GPU timer-query measurements**. Frame percentiles include presentation/event-loop costs. Windows background work and thermal state can influence results; these are individual runs, not statistical guarantees.

Vertex-buffer estimates use actual allocated capacities times the vertex stride (24 bytes); geometry is currently non-indexed. These figures exclude textures, driver bookkeeping, shadow copies, and other applications. They are **not driver-reported process VRAM**. A separate `nvidia-smi` snapshot during the old Massive allocation reported about 3.8 GiB of total GPU memory in use across the GPU. That supports the memory-pressure diagnosis but does not attribute that entire number to MDSM. The baseline completed full Massive mesh preparation; the benchmark's safety abort did not trigger.

Full-scene comparison (terrain, water, mobs and fixed simulation):

| Size | FPS before → after | P95 frame ms before → after | Terrain buffers MiB before → after | Resident terrain meshes before → after |
| --- | ---: | ---: | ---: | ---: |
| Small | 2120.0 → 2123.1 | 0.78 → 0.78 | 61.0 → 47.3 | 256 → 256 |
| Normal | 1117.5 → 1676.7 | 1.27 → 0.88 | 197.5 → 152.6 | 1024 → 1011 |
| Large | 360.7 → 1516.0 | 4.35 → 1.40 | 716.1 → 179.6 | 4096 → 1373 |
| Massive | 13.1 → 661.3 | 264.29 → 4.67 | 2738.4 → 181.7 | 16384 → 1373 |

| Size | Mean CPU Update ms before → after | Mean CPU Draw submission ms before → after | Process working set MiB before → after |
| --- | ---: | ---: | ---: |
| Small | 0.008 → 0.004 | 0.204 → 0.218 | 137.9 → 143.2 |
| Normal | 0.052 → 0.016 | 0.700 → 0.465 | 293.5 → 281.3 |
| Large | 0.635 → 0.068 | 2.096 → 0.565 | 840.1 → 404.5 |
| Massive | 71.141 → 0.639 | 5.139 → 0.841 | 3123.9 → 821.1 |

These working sets are actual process measurements, including CPU allocations and driver-related allocations, rather than GPU memory estimates. Small's modest working-set increase despite smaller buffers illustrates why these quantities should not be conflated. The updated comparison stages reported zero managed allocation bytes per sampled frame, outside preparation; that does not imply zero allocations during generation, rebuilding, gameplay HUD work, or diagnostics.

Update timings are per rendered frame, not per 120 Hz simulation tick. Slow rendering requires more fixed steps inside each Update, so the drop from 71.141 to 0.639 ms must not be interpreted as a 111-fold speedup of an individual physics tick. The simulation rate and game-time accounting were preserved.

Isolated stages confirm that the changes address several costs:

| Size | Terrain-only FPS before → after | Terrain + water FPS before → after | Mobs-only FPS before → after |
| --- | ---: | ---: | ---: |
| Small | 2188.7 → 2190.2 | 2167.5 → 2181.8 | 5108.1 → 5721.6 |
| Normal | 1166.2 → 1676.8 | 1160.1 → 1713.1 | 2857.1 → 4995.7 |
| Large | 595.9 → 1755.4 | 588.3 → 1783.9 | 1158.4 → 4609.5 |
| Massive | 413.9 → 1802.1 | 215.2 → 1843.1 | 790.1 → 2392.6 |

In the sampled Massive direction, terrain submissions decreased from about 5.4 million vertices/1063 chunks to 1.2 million/234 chunks. The final full-scene run averaged 239 world draw calls and about 396,000 triangles per frame. The GPU budget includes only 1,373 resident terrain buffers, versus the original 16,384. Water retained 2,480 nearby faces and a 144 KiB vertex buffer. All 2,048 mobs remained present and updated; eight seconds of measured gameplay simulation advanced eight seconds at 120 Hz.

Separate **30-second actual Game1 runs**, with the normal HUD, held item, player physics, combat targeting and other updates, produced:

| Size / 15-second phase | FPS | P95 frame ms | Mean Update / Draw CPU ms | Resident chunks | Terrain buffer MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Large, stationary turns | 995.7 | 1.81 | 0.151 / 0.822 | 1373 | 179.6 |
| Large, walking turns | 854.8 | 2.26 | 0.168 / 0.970 | 1482 | 193.8 |
| Massive, stationary turns | 389.2 | 5.44 | 1.329 / 1.198 | 1373 | 181.7 |
| Massive, walking turns | 328.8 | 5.81 | 1.538 / 1.460 | 1482 | 195.8 |

Walking crossed chunk boundaries and ended 18.5 blocks from the phase start after turning; normal terrain collisions remained active. Massive's walking working set was about 843 MiB. The extra retained chunks demonstrate the hysteresis margin without growth toward the map's 16,384 chunks. These full-game figures have no equivalent pre-change full-game run and must not be presented as an identical before/after comparison. Native window inspection confirmed a 1920x1080 rendered client area.

The baseline reproduced severe Massive slowdown (about 13 FPS in this scenario), rather than exactly reproducing the reported 3-5 FPS. Seed, viewpoint, other GPU memory users, and operating-system/driver state can affect the point at which memory pressure becomes severe.

## Findings and fixes

* The old [WorldRenderer](./Rendering/WorldRenderer.cs) created and uploaded all chunks before play, retained their buffers until world closure, and checked every chunk every frame. Massive held 88.3 million terrain vertices in 16,384 chunk meshes. Their allocated capacities totalled 2,871,459,840 bytes. Frustum culling reduced draw calls but did not release those allocations.
* Power-of-two vertex capacities amplified retention. They reserved about 35.5% more terrain buffer space than the actual Massive vertex payload. Empty chunks retained old buffers, and greatly reduced meshes retained peak capacity.
* The old [WaterRenderer](./Rendering/WaterRenderer.cs) maintained face lists for all wet chunks and scanned the entire map each frame. Its transparent pass became more expensive under Massive's memory pressure.
* All 2,048 Massive mobs remain active at 120 Hz. Repeated nearby voxel collision queries across a large block working set contributed enough CPU work to make simulation catch-up costly. Reducing GPU residency alone improved the same Massive scenario to about 90 FPS, but simulation still consumed about 10.2 ms per rendered frame. This justified a separate collision-query optimization.

[ChunkRegion](./Rendering/ChunkRegion.cs) now manages a camera-centered disk independently of CPU world storage. Its radius conservatively encloses the far frustum at every camera orientation and elevation, plus two prefetch rings. Two further retention rings provide hysteresis. At 1080p, the initial disk has 1,373 chunks; gradual movement may retain more within the fixed margin. Map edges reduce the count. Aspect-ratio changes recompute coverage. The region is refreshed on chunk/aspect changes, and only resident entries are examined during normal draws. Small worlds can still fit entirely in the region; Massive does not allocate geometry for the entire map.

Build priority favors existing edited meshes, nearby chunks, and newly visible chunks. At most four chunk operations run per frame, with a soft three-millisecond budget checked after each complete chunk. One unusually expensive chunk can exceed that time budget; builds are not interrupted midway. Old edited meshes remain drawable until replacement. Terrain uploads and water face construction share the budget. GPU allocation/disposal remains on the graphics thread.

Leaving the retention disk disposes terrain buffers and removes water face lists. There is no replacement full-world CPU terrain-mesh cache. Chunk revisions and the original block data remain authoritative: non-resident edits reconstruct correctly on return. Initial creation and loading prepare nearby geometry incrementally behind progress feedback, rather than uploading the entire map. Disk deserialization now runs in the existing background preparation workflow.

The shared camera far plane is 160 view-space blocks; fog reaches the sky color between 80 and 144 blocks. Terrain, water, mobs, falling blocks, and item renderers use this same projection/fog transition. This is an intentional reduction in viewing range, accompanied by prefetch coverage in every direction. Dynamic entities still simulate outside the visible region. Their renderer buffers grow with visible geometry rather than one retained mesh per world entity or chunk.

[ChunkMesh](./Rendering/ChunkMesh.cs) rounds capacities to 1,024 vertices, shrinks substantially oversized buffers, and releases empty buffers. Live buffer counters permit lifecycle checks against the actual allocations, including after eviction and world switches.

[PassiveMob](./Entities/PassiveMob.cs) caches a small neighborhood of solid cells per mob, refreshing when movement leaves the cached bounds or a world edit changes the revision. Species dimensions, AI/random decisions, collision resolution, damage, animation and 120 Hz updates are unchanged. [Water.Intersects](./Worlds/Water.cs) first rejects entirely dry chunks, then executes the original detailed water intersection logic when needed. Regression checks compare cached movement with freshly queried voxel data through wandering, edits, knockback and water.

## Diagnostics and reproduction commands

F3 retains the previous information and adds meshed/GPU/drawn chunk counts, estimated live buffer MiB, submitted draw calls/triangles, smoothed CPU Update/Draw submission time, active entity counts, allocation bytes and cached water faces. Formatted strings refresh five times per second only while debug is visible. Normal gameplay performs no diagnostic readbacks or file logging. The allocation counter reports the preceding completed update/draw sampling, not a heap-size or VRAM measurement.

```powershell
dotnet build
dotnet bin/Debug/net9.0/MDSM.dll --self-test
dotnet bin/Debug/net9.0/MDSM.dll --graphics-test
dotnet bin/Debug/net9.0/MDSM.dll --performance-test Small
dotnet bin/Debug/net9.0/MDSM.dll --performance-test Normal
dotnet bin/Debug/net9.0/MDSM.dll --performance-test Large
dotnet bin/Debug/net9.0/MDSM.dll --performance-test Massive
dotnet bin/Debug/net9.0/MDSM.dll --gameplay-performance-test Massive
dotnet publish --no-restore -c Release
```

Run benchmarks sequentially without competing test/build workloads. The gameplay benchmark creates a full world in an isolated test session, prepares the ordinary residency region, and uses the actual Game1 input/update/draw path with synthetic input: 15 seconds of stationary turning, then 15 seconds of walking/turning. It includes normal HUD/held-item rendering, player physics, raycasting, all mobs, fluid simulation and falling blocks. It saves on exit to its temporary test directory and removes only that directory. It does not access player saves/settings. These are scripted tests, not physical-input or human visual playtesting.

## Verification and remaining limits

`dotnet build`: zero warnings/errors. All **100/100** headless groups passed, including every previous check. The complete five-part graphics suite passed. Release publish succeeded and included the existing content assets.

[RenderingPerformanceChecks](./Testing/RenderingPerformanceChecks.cs) adds all-size region/frustum-volume coverage, aspect/edge/elevation behavior, hysteresis and fixed-clock checks, and exact cached-versus-fresh collision trajectories. [WorldExpansionGraphicsChecks](./Testing/WorldExpansionGraphicsChecks.cs) now additionally uses the real WorldRenderer on fully generated Large/Massive worlds, with actual global live-buffer accounting. It exercises bounded uploads/residency, turning, rapid moves, high cameras, world edges, neighboring chunk edits, eviction and distant edits/re-entry, water cache release, world switching, buffer disposal/shrinking, and empty meshes. Existing pixel/state tests continue covering opaque/cutout/water rendering, falling blocks, mobs and repeated F1/F2/F3 screenshots. [MobGraphicsChecks](./Testing/MobGraphicsChecks.cs) also tests the actual Save/Return → world selection → asynchronous Load path with saved edits and completed nearby preparation. Existing pause/save/load/legacy migration checks remain intact. Pixel fixtures wait for the ordinary bounded residency region before comparing frames; they do not populate an entire Massive map with GPU geometry.

The opt-in [PerformanceChecks](./Testing/PerformanceChecks.cs) and [GameplayPerformanceChecks](./Testing/GameplayPerformanceChecks.cs) generate complete worlds and report measurements only after preparation. No test-only reduction in world storage, population, terrain, water or view distance is used. Benchmarks and graphical checks used isolated data and did not change player saves or preferences.

## Files changed for this performance update

* Added [ChunkRegion.cs](./Rendering/ChunkRegion.cs) and [FrameDiagnostics.cs](./Core/FrameDiagnostics.cs).
* Updated [WorldRenderer.cs](./Rendering/WorldRenderer.cs), [ChunkMesh.cs](./Rendering/ChunkMesh.cs), [WaterRenderer.cs](./Rendering/WaterRenderer.cs), [Camera.cs](./Gameplay/Camera.cs), [DroppedItemRenderer.cs](./Rendering/DroppedItemRenderer.cs), [FallingBlockRenderer.cs](./Rendering/FallingBlockRenderer.cs), and [PassiveMobRenderer.cs](./Rendering/PassiveMobRenderer.cs): residency, capacity/disposal, shared projection/fog and buffer accounting.
* Updated [PassiveMob.cs](./Entities/PassiveMob.cs), [Water.cs](./Worlds/Water.cs), [Game1.cs](./Core/Game1.cs), and [Hud.cs](./UI/Hud.cs): collision cache, dry-chunk rejection, initial/loading preparation and diagnostics.
* Added [RenderingPerformanceChecks.cs](./Testing/RenderingPerformanceChecks.cs), [PerformanceChecks.cs](./Testing/PerformanceChecks.cs), and [GameplayPerformanceChecks.cs](./Testing/GameplayPerformanceChecks.cs). Updated [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [WorldExpansionGraphicsChecks.cs](./Testing/WorldExpansionGraphicsChecks.cs), [InterfaceGraphicsChecks.cs](./Testing/InterfaceGraphicsChecks.cs), [PigGraphicsChecks.cs](./Testing/PigGraphicsChecks.cs), [WaterGraphicsChecks.cs](./Testing/WaterGraphicsChecks.cs), [MobGraphicsChecks.cs](./Testing/MobGraphicsChecks.cs) and [Program.cs](./Program.cs) for coverage, initial readiness and opt-in benchmark entry points.
* Updated [README.md](./README.md) and added this report. Assets, project dependencies, frame limiter, terrain generation and serialization were not changed by this performance update.

All four dimensions, generation algorithms, persistent populations, respawn timers, block storage, 120 Hz simulation clock, FPS settings, and version-7 save layout remain unchanged. Existing legacy-height migration remains covered by the complete save tests. No new dependencies, terrain simplification, population reduction, world streaming, greedy meshing, or full-world CPU geometry caches were introduced.

The original 512 MiB raw Massive block storage remains necessary. Initial terrain generation, loading and saving still have costs proportional to world area. Mob simulation remains proportional to population, although repeated collision queries are cheaper. Dense edited scenes can still cost more than the tested natural terrain. Rapid teleports can require bounded rebuilding over several frames; ordinary movement has prefetch coverage. A single complex chunk can exceed the soft build-time budget. Future profiling could consider indexed geometry, water sorting, or more local collision-cache invalidation if those become limiting.

Manual checks remain: exploration for longer sessions and other seeds, assessment of the shorter fog/view distance, unusual edited shores and dense vegetation, physical mouse/keyboard behavior, screenshot appearance, driver configurations, and concurrent VRAM-heavy applications. Passing scripted tests and measured FPS here are not a guarantee of a particular FPS in every world or machine state.

## Final survival content profiling — 2026-11-03

This section measures the caves/Lava/farming/breeding/hostile/equipment update described in [FINAL-SURVIVAL.md](./FINAL-SURVIVAL.md). Historical measurements above remain historical; no view-distance/fog reduction or population reduction was used for this update.

### Controlled gameplay frames

Existing actual-Game1 benchmark, seed 173, 1920×1080, uncapped, sequential runs, 15 seconds each of stationary quarter-turns and W/turning. All finite CPU blocks and original passive populations remain loaded. The walking path can encounter terrain; travel is recorded rather than assumed. These are short scripted samples on this environment, not physical playtesting or hardware-independent FPS promises.

| Size | FPS turning / walking | p95 ms turning / walking | CPU Update ms | CPU Draw ms | Resident turning / walking | Passive population |
| --- | --- | --- | --- | --- | --- | --- |
| Small | 1791 / 1764 | 0.85 / 0.90 | 0.013 / 0.013 | 0.445 / 0.333 | 256 / 256 | 32 |
| Normal | 1238 / 1195 | 1.11 / 1.18 | 0.025 / 0.024 | 0.640 / 0.614 | 1011 / 1019 | 128 |
| Large | 1254 / 1086 | 1.07 / 1.20 | 0.063 / 0.066 | 0.703 / 0.822 | 1373 / 1487 | 512 |
| Massive | 757 / 689 | 1.73 / 1.88 | 0.256 / 0.261 | 1.026 / 1.152 | 1373 / 1482 | 2048 |

Massive's terrain-buffer estimates were **279,281,664 / 302,014,464 bytes** (approximately 266/288 MiB), versus roughly 180/195 MiB before caves. Added underground faces account for additional geometry, while residency remains bounded rather than scaling with all 16,384 chunks. Process working set was approximately 957/978 MiB. These are **estimated buffer bytes and process RAM**, not driver-reported VRAM. Driver memory limits on other GPUs were not independently reproduced.

Historical Milestone-12 actual-game samples were roughly 420/371 FPS and 5.39/5.48 ms p95 on Massive. Current samples are substantially faster despite cave geometry. The main CPU change is explicit 32-block behavioral-AI activation, while long-term animal clocks and natural replacement scheduling remain active. Changed terrain and ordinary timing variation prevent treating this as an exact identical-geometry experiment.

The natural-cave mode uses the complete generated Massive map, its 2,048 passives, eight hostile fixtures on valid nearby cave floors, natural Lava, and periodic Water/Lava contact. Test-only Obsidian Armor/health restoration prevents measuring the death menu. It does not remove terrain, lower populations, shrink the view, or bypass residency. The selected natural pool is near a finite-world edge; residency is accordingly smaller than the central surface sample.

| Cave phase | FPS | p95 frame | Update / Draw CPU | Resident | Terrain-buffer estimate | Hostiles |
| --- | --- | --- | --- | --- | --- | --- |
| Turning | 1041 | 1.36 ms | 0.240 / 0.684 ms | 708 | 148,242,432 bytes | 8 |
| W / turning | 1056 | 1.34 ms | 0.237 / 0.676 ms | 708 | 148,242,432 bytes | 8 |

The cave walking sample traveled 2.8 blocks through constrained terrain. Both fluid frontiers settled to zero, and the last sampled frame required no rebuild. Existing graphics lifecycle checks verify the four-build/3 ms budget, movement/hysteresis, edits/re-entry, frustum/elevation/edges, Water eviction and complete GPU cleanup on both Large and Massive. This cave scenario complements, rather than replaces, the larger-residency surface test.

### Generation, CPU systems and full persistence

Isolated `--completion-performance-test`, seed 173, one world at a time. Generation is measured before diagnostic edits. A small constructed underground chamber then exercises all retained passive clocks, two fed parents/one baby, eight active hostiles, 24 crops, hydration changes, Torch suppression, player/projectile physics and Water/Lava contact. The 10 simulated seconds below are **CPU work**, not rendered FPS. An isolated 2,400-second crop-clock advance measures growth processing without a 40-minute real-time wait. Health restoration is a diagnostic control. Save/load covers the complete world and verifies a full-block SHA-256 plus entity/crop counts; no reduced-world stand-in is used.

| Size | Generation s | Cave pass ms | Cave systems / carved cells | Lava basins | Bonus ore cells | Managed MiB |
| --- | --- | --- | --- | --- | --- | --- |
| Small | 0.362 | 44 | 41 / 47,414 | 12 | 762 | 8.8 |
| Normal | 1.389 | 247 | 193 / 280,109 | 73 | 3,981 | 34.2 |
| Large | 5.703 | 1,023 | 744 / 1,162,052 | 256 | 16,219 | 136.0 |
| Massive | 22.783 | 4,206 | 2930 / 4,848,431 | 1111 | 66,743 | 544.4 |

Surface opening-cell counts were 0/145/840/3,581; these count removed surface cells, not distinct entrances. Small seed 173 has no entrance after shoreline protection; additional-seed checks verify occasional openings. Cavities occupy under one percent of total block volume on this Massive seed. All four worlds initialize with **zero Lava frontier work**, and generated Sand/Gravel support and existing water features remain protected.

| Size | CPU ms for 10 simulated seconds | Growth batch ms | Save s | Load s | File MiB | Passives / hostiles / crops |
| --- | --- | --- | --- | --- | --- | --- |
| Small | 100.97 | 1.445 | 0.032 | 0.083 | 8.05 | 33 / 8 / 24 |
| Normal | 75.21 | 0.332 | 0.045 | 0.305 | 32.18 | 129 / 8 / 24 |
| Large | 95.20 | 0.299 | 0.187 | 1.233 | 128.67 | 513 / 8 / 24 |
| Massive | 150.10 | 0.302 | 6.412 | 5.331 | 514.72 | 2049 / 8 / 24 |

The extra passive in this table is the intentionally bred child, not a changed initial target. The stress case recorded 456 localized block edits and approximately 11/13/19/43 KiB of total allocations over the ten-second simulation, including initial queues, offspring and projectiles. Fluid budgets stayed within 2,048 Water / 512 Lava updates per tick; farming stayed within bounded hydration/growth batches. The deliberately edited Water frontier remained at 30, while Lava was idle. Spawn sampling is bounded to 24 attempts, and the hostile count never exceeded eight.

### Investigation-driven fixes

- Initial additional per-cell restore classification made Massive loading roughly **14.9 seconds** (Large 3.7). One histogram pass per chunk plus top-down solid-column searches reduced that to **5.3 seconds** (Large 1.2), without relaxing validation or changing the binary block stream. Block-ID validation now uses a vectorized range check.
- Early pool carving could leave unsupported floors or later tunnels beneath existing pools. Strict floor/wall checks and fluid-protected carving now leave natural Lava idle at initialization on all sizes. Stable pools create no perpetual frontier work.
- New partial shapes retain efficient shared block-box queries. Animal body footprints fit open Gates; larger render envelopes prevent decorative heads/noses from being incorrectly culled. Existing collision-cache comparisons continue to check edits, fluids and fresh-query equivalence.
- All timers are gameplay based. Crops, structures and Torches use sparse registries; no extra per-voxel state array is retained. The two fluid frontiers use sparse queued sets. No full-world CPU mesh cache or eager GPU allocation was introduced.

Massive saves remain roughly 515 MiB and uncompressed. Storage throughput still matters; synchronous saving can briefly pause gameplay. No particular speed is promised on other disks. Long interactive survival sessions, intricate navigation/builds, and physical-input checks remain manual.

Reproduce:

```powershell
dotnet build
dotnet run --no-build -- --completion-test
dotnet run --no-build -- --completion-performance-test
dotnet run --no-build -- --gameplay-performance-test Small
dotnet run --no-build -- --gameplay-performance-test Normal
dotnet run --no-build -- --gameplay-performance-test Large
dotnet run --no-build -- --gameplay-performance-test Massive
dotnet run --no-build -- --gameplay-performance-test Massive --cave
dotnet run --no-build -- --self-test
dotnet run --no-build -- --graphics-test
```