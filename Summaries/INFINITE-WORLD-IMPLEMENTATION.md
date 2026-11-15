# Infinite-world implementation and verification

Status: Infinite is selectable and playable through the normal Create/Load World
flow. This supersedes the earlier foundation-only status. Finite worlds retain
their existing dimensions, generation pipeline, compressed v14 serializer and
legacy readers; they are not converted to Infinite.

## Integrated architecture

- `WorldSize.Infinite`, menu cycling, list metadata, creation progress and the
  save manager now route Infinite worlds through their streamed implementation.
  New worlds retain the existing Survival/cheats/difficulty choices and empty
  starting inventory. Existing finite-world choices remain available.
- `World` retains its finite `Chunk[]` path and adds sparse resident storage.
  Native `Chunk` objects share the lifecycle's block arrays. Int64 block/chunk
  identities use signed floor division/modulo; no rectangular Infinite allocation
  or cache of every explored chunk is retained.
- Default distances: load 12 chunks, render 10, simulate 6. Storage is bounded by
  625 resident chunks. Rendering admits the 21x21 inner square and retains one
  additional ring for boundary reversals (maximum 529 meshes). Loading includes
  an additional margin. Distances remain separate implementation constants.
- Generation, indexed-region I/O/compression and CPU meshes run on workers.
  Terrain reads/generation and retirement writes each have at most two requests
  in flight; CPU lighting/meshing has at most two.
  Gameplay uploads at most one completed mesh per frame. Nearby work takes
  priority, obsolete requests are cancelled or their revision-tagged results
  discarded, and all GPU operations stay on the graphics thread.
- Mesh workers receive copied nine-chunk halos and immutable structure/emitter
  data, calculate lighting asynchronously with pooled scratch arrays, then use
  the established terrain/water builders, with chunk-local float
  vertices. Drawing subtracts Int64 origins before converting translations to
  float. No full-world CPU mesh cache is introduced.
- Player, mob and dropped-item positions have authoritative double coordinates;
  projectile records retain a double position across frame changes. Existing
  collision/movement math runs in a small moving local float frame. Physics,
  targeting, placements, special collision boxes, sounds, bobbing, particles,
  weather, clouds, commands and renderer bounds use that frame consistently.
  The simulation remains 120 Hz. This is not a rewrite of every physics
  calculation into double arithmetic.
- Missing chunks are blocking terrain for collision and cannot be edited. The
  player waits for a surrounding 3x3 neighborhood during creation/teleportation.
  Extreme teleport coordinates are parsed as doubles and loaded before use;
  unsafe destinations leave the actual player position unchanged.
  `/tp [@p] x y z` also completes terrain loading while the command screen
  remains open, without advancing gameplay time. Airborne Y is preserved.
- Sparse simulation state and entity ownership are captured per chunk. Crops,
  hydration, natural leaves/decay, Saplings, fire, fluid frontiers, falling blocks,
  animals/replacement timers, hostiles, dropped stacks, projectiles, containers,
  signs and orientation data detach and restore with their terrain. Dormant crop/
  fire/leaf ownership is reconciled when a neighbor edit removes its block, and
  Peaceful clears dormant loaded hostiles as well as active ones. Outside the
  simulation square, active behavior is suspended rather than simulating the
  entire explored map. Collision-cache revisions change when terrain arrives or
  retires; an origin shift does not turn into walking bobbing or knockback.

## Generation and Far Lands

`BetaDensityGenerator` uses lower/upper/selector fields and broad elevation/relief
controls, sampled at 4x8x4 intervals with trilinear interpolation. The version-1
Infinite feature pipeline uses absolute-coordinate ownership and bounded halos
for biome surfaces, caves/lava, deposits and ores, ponds/rivers/lakes, trees,
vegetation, desert plants, snow cover, cave-connected dungeons/chest loot and a
modest shared four-species passive population. Structures write their own slice
into each intersected chunk, without mutating another loaded chunk. Natural
population targets derive from surviving natural animals plus pending replacements
in those records; loading does not repopulate killed animals or reset timers.
Finite generation is not replaced by these Infinite-specific feature passes.

Perlin explicitly implements Java-compatible Int32 saturation and the unchecked
negative floor correction. It preserves out-of-range interpolation remainders;
there is no Far Lands wall, coordinate normalization or density clamp. The main
high-frequency fields advance 171.103 noise units per horizontal block. Raw seed
173 density at Y=64 changes from about 3.05 at X=12,550,823 to 317,211 at
12,550,824 and 3.82e11 at 12,550,825. Negative-axis and corner behavior differ.
Further finite samples continue at 25,101,648 and 1e12; some extreme chunks become
largely solid, without resetting to ordinary terrain.

Primary research references:

- [Java narrowing conversions](https://docs.oracle.com/javase/specs/jls/se21/html/jls-5.html#jls-5.1.3)
- [Beta Perlin implementation](https://github.com/Spottedleaf/OldGenerator/blob/master/src/main/java/ca/spottedleaf/oldgenerator/generator/b173/noise/NoiseGeneratorPerlin173.java)
- [Beta noise notes](https://github.com/OpenBetaGroup/beta-wiki/blob/main/technical/noise.md)

## Persistence and compatibility

Infinite worlds keep `world.dat` as a checksummed version-1 manifest and use
`regions/r.<signed-x>.<signed-z>.mdr`. Regions contain 32x32 indexed chunks, with
independently Zstandard-level-3-compressed records. Dual checksummed indexes commit
only after data is flushed. Compaction reclaims obsolete records without replacing
valid data before its replacement is ready. Chunk snapshot schema 2 stores block
bytes plus versioned gameplay sections; the schema-1 block-only reader remains.
Generator identity is recorded in the manifest and every chunk.

The manifest preserves player position/orientation, exact health, inventory,
equipment identities/durability, selected slot, world options, spawn, world time
and weather. Reconstructible packed lighting pages are recalculated, not duplicated
on disk. Normal chunk arrivals/edits invalidate the appropriate bounded light and
mesh neighborhoods; time-of-day changes continue to use shader uniforms.

A prepared checkpoint contains immutable chunk snapshots, per-record checksums
and a validated manifest. Its atomically published plan is replayed idempotently
on load after an interrupted commit. Failed commits preserve the prior manifest
and retain live state for retry. The game captures ownership on its main thread,
then performs checkpoint writing/compression in the background. Streaming writes
are held during that transaction. Save and Return/exit wait for success; if an
older autosave was already running they request a fresh final snapshot, preserving
changes made since that autosave began. Existing safe finite saves remain on their
original code path.

Checkpoint records share one durably flushed staging journal; region records are
committed in bounded batches of at most 64, sharing data/index flushes per region.
Recovery also accepts the original per-chunk staging journals. A completed final
checkpoint is reused at shutdown instead of writing the same resident state again.
The manifest, compressed region and finite-world save formats are unchanged.

## Verification

The new checks exercise actual sparse World queries and mutations, signed/high
coordinates, physics/raycasting/rebasing, ordinary session creation, streamed
unload/reload, block and gameplay ownership, containers/equipment, signs/Doors,
falling objects, drops, Saplings, pending replacements, checksummed manifests,
failed commit recovery, async snapshot isolation, corruption detection, rename,
deterministic generation and block support/ore bounds. Existing checks remain;
finite-only dimension loops explicitly cover the same four finite sizes, while
new checks cover Infinite. The old menu test now expects five selectable sizes.

The scripted DesktopGL test uses the real creation menu, Game1, native streamed
renderer, screenshots, commands, Save and Return, reload and GPU disposal. It
created a default-distance world, explored, and rendered positive/negative Corner
Far Lands and coordinates at 1e12. It checked final-frame PNG pixels and hidden-HUD
texture restoration. No physical input or subjective manual playtesting was used.

The earlier pre-correction Release run on the NVIDIA GeForce GTX 970 reported 625 loaded chunks,
441 initial meshes and 134,774,784 estimated terrain-buffer bytes after warming
that entire nearby region (19.19 seconds). Its 20-second, 1920x1080 scripted
Creative-flight route covered 184.6 blocks over 37,316 frames: mean frame interval
0.54 ms, 1% low 288.2 FPS and 0.1% low 114.4 FPS; managed memory sample 71,984,792 bytes.
These figures describe that route, not a universal FPS guarantee or a driver VRAM
measurement. The route's maximum estimated terrain/item/falling/water buffers were
138,289,152 bytes (about 132 MiB); textures, backbuffers and driver overhead are
not included. That historical run includes hysteresis and collision-cache adjustments.

The subsequent corrective run exercised real command-bar teleport submission,
matched F3 to `(12550824, 128, 0)`, tested visual-only legacy snapping and checked
20-second airborne flights at 60/144 caps without loading-paused frames. It
covered approximately 258 blocks per flight, retained at most 625 chunks, and
saved/reloaded successfully with batched checkpoint journals. The current full
regression count is 259/259. See the final corrective entry in
[CODEX-SUMMARY.md](./CODEX-SUMMARY.md) for measured frame-time lows, save timings,
root causes and remaining performance limits. The old route advanced only 184.6
blocks, so its FPS is not directly comparable to runs that simulate the full flight.

Final Debug and Release builds passed with zero warnings/errors; the final Release
regression suite passed 258/258. The full existing graphics aggregate and the
lighting, command, Bed-sleep, world-rules, item-drop, mob-movement, overnight and
density-probe graphics suites passed. The menu fixtures were updated to include
the fifth size without weakening their original world-rule/population assertions.
The native graphics suite also reopened the saved Infinite world through the real
world-selection/loading menu. git diff --check passed.

## Explicit limits and manual checks

- Coordinates are deliberately bounded at approximately +/-2^40 blocks for
  meaningful double sub-block precision and safe indexing; the world does not end
  at the Far Lands. `/precision legacy` enables optional visual camera snapping;
  `/precision modern` restores the default. This application preference persists,
  but never narrows authoritative positions, physics, targeting, or noise inputs.
  It approximates legacy camera jitter, not every historical vertex artifact.
- Infinite generation is Beta-inspired, not an exact Minecraft seed reproduction.
  Its feature algorithms adapt existing content/style to coordinate ownership;
  they are not the finite generator applied to an ever-growing rectangle.
- Simulation timers freeze outside simulation distance; there is no offline or
  distant catch-up ecology. Entity selection and dungeon lookup operate on the
  currently available area, not a scan of every explored region on disk.
- Chunk records enforce a 1 MiB decoded payload budget. Exceptionally dense
  per-chunk container/entity metadata can exceed it and produces a save error,
  rather than being silently truncated. Existing gameplay entity/container limits
  remain. Ordinary blocks occupy 32 KiB per chunk.
- Initial exploration may wait for nearby terrain, and snapshot capture still
  does bounded main-thread state copying. The scripted run is not an hours-long
  endurance test or a benchmark of every terrain/AI/weather configuration.
- Recommended manual playtesting: ordinary Survival progression across region
  boundaries; wide-angle horizon/water/lighting inspection while turning; Doors,
  Beds and containers across seams; walking/mining inside Edge/Corner Far Lands;
  long exploration with dense farms, animals and fluid/fire edits; and exit/retry
  behavior under real storage failures.

README.md, dependency versions and artwork are unchanged. No publishing or Git
commit commands were used.
