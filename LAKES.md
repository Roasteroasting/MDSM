# Major lakes and Massive creation notice

The creation screen shows the requested performance notice only while Massive is selected. It uses the existing pixel font, including a multiplication-sign glyph, and a subdued warm-colored panel. Text wraps without truncation. Fields, notice and buttons have separate bounds down to 320x240, with immediate visibility changes and no confirmation or creation restriction. Other menu screens retain their layouts.

## Generation

The previous large-lake path reused the small pond outline and rejected more than five blocks of relief across the entire lake. At landmark-sized footprints, that restriction rejects too many otherwise plausible valley sites. Simply increasing its radius would also preserve the recognizable pond silhouette.

[MajorLakeGenerator.cs](./Worlds/MajorLakeGenerator.cs) instead combines three smoothly joined elliptical lobes, two broad subtractive coves, variable proportions/orientation and gentle non-radial coordinate warping. The center-connected outline is retained if a cutout separates a fragment. The result is a continuous body with broad bays and peninsulas, rather than a scaled circle or repetitive radial ripples. Islands are not added.

A mixed seed selects 2-3 target lakes for Large and 6-9 for Massive. Each slot receives 256 bounded, deterministic candidate positions, ranked by elevation, broad relief and central basin suitability. Full footprints are then checked before writing any blocks. Sites reject hilltops/excessive relief, finite edges and the spawn neighborhood; major lakes maintain generous separation. Search exhaustion skips an unsuitable slot rather than forcing a basin into mountains. Ordinary tested seeds met their full targets.

Water sits at one level near the lowest original land in the footprint. Deepest water ranges from 12-18 blocks in Large and 15-24 in Massive, with a smooth depth profile from shallow shores toward the interior. Shoreline grading cuts or gently fills the immediate rim and blends into the original heightmap, capped at a 56-column search apron; unchanged outer land is left alone. Actual modified terrain, not that entire apron rectangle, is reserved against subsequent ponds/rivers. Beds and beaches use existing Sand, Gravel, Dirt and Grass with solid support. The regular decoration stage runs afterward.

Pond radius/depth/density and river routing/width/length/depth/budgets remain unchanged. Their positions in newly generated Large/Massive worlds can differ as feature layout changes. Small/Normal take the original pond-only path. Existing saves never run this generation stage.

## Measured complete worlds

Debug build, i5-11400 and 16 GB RAM. Each row comes from a complete generated world, not configured radii or a cropped terrain fixture. Entries list **actual water-column bounding width x depth / maximum water depth**, all in blocks. Each major lake's largest connected source body was measured independently; a separate pond can legitimately occupy a cove's bounding rectangle and is excluded from that measurement.

| World / seed | Actual major lake count | Actual dimensions / depth |
| --- | ---: | --- |
| Large / 173 | 3 | 173x152 / 12; 122x152 / 18; 134x150 / 13 |
| Large / -907 | 3 | 194x158 / 18; 107x159 / 16; 125x168 / 12 |
| Large / 137 | 2 | 125x163 / 18; 138x175 / 17 |
| Massive / 173 | 8 | 241x206 / 15; 162x203 / 24; 173x199 / 16; 206x190 / 18; 141x227 / 19; 131x160 / 17; 221x162 / 20; 107x165 / 21 |
| Massive / -907 | 8 | 262x221 / 23; 139x206 / 21; 168x226 / 16; 165x115 / 19; 182x130 / 20; 220x161 / 18; 124x164 / 18; 267x200 / 20 |
| Massive / 137 | 6 | 166x217 / 24; 185x236 / 23; 147x181 / 22; 151x148 / 24; 177x237 / 23; 268x203 / 22 |

All Large samples retained **64 ponds and 4 rivers**; all Massive samples retained **256 ponds and 12 rivers**. Water bodies were connected, level and source-filled, with supported beds, graded shores, protected spawn/boundaries and zero initial fluid/gravity frontier work. Idle fluid ticks left their world revisions unchanged. Original land relief within these lakes was 7-20 blocks. Outline occupancy was roughly 62-78% of the bounding rectangle, with broad connected bodies rather than thin channels.

Pure full-world generation took **3.14-3.31 seconds for Large** and **13.16-13.41 seconds for Massive** in the final headless run. Managed-memory snapshots were approximately 136-137 and 546-547 MiB respectively, including the complete block arrays and temporary generation allocations. Timings and memory snapshots depend on hardware/GC state. Local masks/distance fields are temporary; only small measured summaries remain attached to a new world. No additional full-world block copies or per-frame generation scans were introduced.

## Water rendering safeguards

The larger lakes exposed the old transparent-face sort's CPU cost during camera rotation: it repeatedly recomputed four-corner centers and moved 52-byte face records in comparisons. A focused continuous-camera, offscreen 1920x1080 test initially measured 24.61 ms CPU draw for Large and 42.45 ms for Massive on a GTX 970.

[WaterRenderer.cs](./Rendering/WaterRenderer.cs) now sorts reusable compact index/distance keys, calculating each distance once. It preserves the existing back-to-front distance rule and deterministic ties. Camera rotation reuses the uploaded batch when the visible chunk set, geometry and camera position are unchanged. For translation within the same visible geometry, it repairs the previous order with a bounded insertion pass, falling back to the full sort after large changes. Frustum culling, bounded residency, blending, depth reads and explicit texture/state restoration remain intact. [WaterMeshBuilder.cs](./Rendering/WaterMeshBuilder.cs) skips corner-height work for completely enclosed water cells, while retaining exactly the same exposed-face geometry.

The final GTX 970 run measured:

| Actual major-lake view, 1920x1080 | Large | Massive |
| --- | ---: | ---: |
| Rotation: CPU draw before the sort changes | 24.61 ms | 42.45 ms |
| Rotation: CPU draw after | 0.39 ms | 0.59 ms |
| Rotation: offscreen frames/second after | 1,722 | 1,307 |
| Rotation plus translation: CPU draw after | 3.23 ms | 5.87 ms |
| Rotation plus translation: offscreen frames/second after | 305 | 169 |
| Resident terrain chunks | 1,226 | 1,373 |
| Estimated terrain buffers | 165.0 MiB | 176.1 MiB |
| Estimated water buffers | 4.80 MiB | 7.91 MiB |
| Cached water faces | 20,753 | 35,346 |

The existing fixed-camera full-scene benchmark, including water, mobs and 120 Hz simulation, also ran at 1920x1080: Large measured 1,496 FPS (CPU update/draw 0.070/0.573 ms, p95 frame 1.39 ms); Massive measured 648 FPS (0.660/0.850 ms, p95 4.74 ms). The previous recorded values were 1,516/661 FPS. This small difference is not evidence of a material regression; that scenario does not traverse the new lakes. The explicit moving-camera lake phase above covers the newly enlarged water bodies. No graphics or headless suites ran concurrently with these benchmarks.

These are five-second scripted render phases with continuous camera rotation, followed by rotation plus translation over actual major lakes; offscreen FPS is not a gameplay FPS guarantee. They perform one final readback for pixel verification, outside the measured interval. Existing screenshot regressions continue comparing consecutive frames and exact PNG pixels. Buffer bytes are estimates from live application buffers, not driver-reported total VRAM. Viewing distance and fog settings were not changed.

## Verification and compatibility

`dotnet build` and `dotnet publish --no-restore -c Release` completed with zero warnings/errors; all nine texture assets are present in the published Content tree. The complete graphics suite passed, including the actual 320x240 notice/input path, all four mob models, water/terrain depth and graphics-state restoration, falling blocks, inventory and exact screenshot pixels. Repeated/alternating HUD-hidden and HUD-visible captures, resize and failed-save regressions remained intact. Actual Large/Massive WorldRenderer lifecycle checks exercised bounded residency, frustum/elevation/edges, turns/movement, edits/re-entry, water eviction and GPU cleanup. Both representative lake PNGs and the compact notice PNG were visually inspected.

All **103/103 headless check groups passed** on the final code, preserving the previous 100 groups. New checks cover exact Massive-only notice text/layout/visibility, full sorting plus bounded-repair/fallback correctness without lost faces, and the six-world measured generation/determinism matrix above. The complete integration group also regenerates all four sizes, compares full-world hashes, and saves/restores complete terrain and gameplay state. Save versions 1-7 and supported legacy-height migrations still pass. `git diff --check` is clean. No save-format, asset, dependency, render-distance or fog changes were needed.

The version-7 save layout, block IDs, legacy height migration, player/entities/inventory, fluid rules, rendering distance/fog and assets remain unchanged. Generation-only lake summaries are not serialized or reconstructed on load. No lakes or other terrain are added to existing worlds.

Added [MajorLakeGenerator.cs](./Worlds/MajorLakeGenerator.cs), [WorldCreationLayout.cs](./UI/WorldCreationLayout.cs), and [LargeLakeChecks.cs](./Testing/LargeLakeChecks.cs). Updated [LakeGenerator.cs](./Worlds/LakeGenerator.cs), [World.cs](./Worlds/World.cs), [MenuController.cs](./UI/MenuController.cs), [PixelFont.cs](./UI/PixelFont.cs), the two water rendering files above, [PrototypeChecks.cs](./Testing/PrototypeChecks.cs), [WorldExpansionChecks.cs](./Testing/WorldExpansionChecks.cs), [WorldExpansionGraphicsChecks.cs](./Testing/WorldExpansionGraphicsChecks.cs), [InterfaceGraphicsChecks.cs](./Testing/InterfaceGraphicsChecks.cs), [Program.cs](./Program.cs) and [README.md](./README.md).

Physical-input exploration, longer sessions, other seeds and hardware, shoreline aesthetics from additional viewpoints and heavy player edits still warrant manual testing. The six-world matrix (three seeds in both sizes) and representative GPU views do not exhaust all possible worlds.

```powershell
dotnet build
dotnet bin/Debug/net9.0/MDSM.dll --lake-test
dotnet bin/Debug/net9.0/MDSM.dll --self-test
dotnet bin/Debug/net9.0/MDSM.dll --graphics-test
dotnet publish --no-restore -c Release
```
