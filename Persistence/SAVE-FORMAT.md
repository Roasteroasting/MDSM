# World save container, version 14

The game writes `world.dat` in each existing GUID-based world directory. All
integers and floats are little-endian. Versions 11-14 use **Zstandard level 3** through
[ZstdSharp.Port 0.8.8](https://github.com/oleg-st/ZstdSharp). Brotli quality 3 and
uncompressed records are supported by the same container reader and benchmark
writer. Normal gameplay always selects Zstandard; individual records fall back to
stored bytes if compression would increase their size.

CPU worlds remain fully loaded byte-per-block arrays. This format does not add
chunk streaming, change terrain generation, or reduce the 512 MiB raw block RAM
required by a Massive world.

## Metadata header

The initial metadata layout matches versions 1-10: MDSM magic, version, dimensions,
seed, UTF-8 world name, player position/orientation and block count. The version is
14. This is followed by four Int32 values:

1. Container layout schema: 1.
2. Chunks per block-data group: 32.
3. Maximum decoded record payload: 1,048,576 bytes.
4. Default codec hint: 2 (Zstandard).

A 32-byte SHA-256 covers the metadata and these options. World selection reads
only this bounded header and validates it, without decompressing block arrays.
Seekable inputs also receive a minimum-file-size check; full integrity is checked
when loading.

## Independently compressed records

Each record has a 56-byte header, followed by its encoded payload:

| Offset | Type | Meaning |
| --- | --- | --- |
| 0 | UInt32 | Section type |
| 4 | UInt16 | Section schema version, currently 1 |
| 6 | UInt16 | Flags: bit 0 means required; other bits currently rejected |
| 8 | Byte | Codec: 0 stored, 1 Brotli, 2 Zstandard |
| 9 | 3 bytes | Reserved, must be zero |
| 12 | Int32 | Sequence / first chunk index |
| 16 | Int32 | Decoded length, at most 1 MiB |
| 20 | Int32 | Encoded length, separately bounded |
| 24 | 32 bytes | SHA-256 of decoded payload |

The current required sections are:

| Type | Contents |
| --- | --- |
| 1 | Canonical version-10 core header; must match outer metadata, except for version |
| 2 | 32 complete 16x128x16 chunks (1 MiB), in existing chunk order; sequence identifies the first chunk |
| 3 | Sequential bounded frames containing the existing version-10 gameplay/state tail and its checksum |
| 4 | Version-13 environment (12 bytes): Float64 seconds since sunrise (0 inclusive, 1200 exclusive), Byte game mode (0 Survival, 1 Creative), Byte lighting rules version (1), Byte allow-cheats (0 off, 1 on), Byte difficulty (0 Peaceful, 1 Easy, 2 Normal, 3 Hard) |
| 5 | Version-14 authoritative world features, section schema 1 / payload schema 2 (also reads payload 1), sequential independently compressed frames (at most 16 MiB decoded in total): signs, egg timers, climate/weather, snowballs, entity burning and fire deadlines/attachments |
| 0 | End marker: zero lengths/sequence, stored codec, SHA-256 of empty data |

There is one core header, all chunk groups in order, and at least one state frame.
Versions 12-14 then require exactly one environment record before the end marker.
Version 14 also requires the feature frames after the environment record. The
unchanged core payload is validated first; feature state is restored only after
the complete file and section checksums have passed.
Version 12 uses its original 10-byte payload (seconds, mode 0/1/2, lighting rules).
Version 11 has no environment record. A malformed, repeated, missing, or unsupported
required environment section is rejected, including nonfinite/out-of-range time.
Current sizes use 8/32/128/512 chunk groups for Small/Normal/Large/Massive. The core
serialization is deliberately retained to preserve validated handling of block
bytes, IDs, items/durability/ownership, health, entities, population timers,
containers, fluids, falling blocks, farming, vegetation, structures and projectiles.
The streaming adapters present this payload to the existing semantic reader; they
do not assemble another whole-world byte array.

After the end record, a final 32-byte SHA-256 covers every preceding container
byte, including encoded payloads and record headers. Trailing bytes are rejected.
The inner checksum remains active as an additional complete-state integrity check.

## Extension policy

New features can add section types with their own schema versions. World time and
mode now use the scalar environment section. Sky/block lighting is reconstructed
deterministically from blocks and burning containers in a bounded, lazy CPU cache;
neither stale light arrays nor redundant whole-world light data are stored. Each
cached cell packs two four-bit values. Daylight is applied to sky light at draw/query
time, so day/night transitions require no light-array or terrain-mesh rewrites.
Future authoritative features must define their section sizes and decoded budgets.

Unknown required sections/schemas fail with an unsupported-data error, preventing
an older reader from silently discarding authoritative future state. Unknown
optional sections are checksum-validated, bounded and ignored; only explicitly
discardable/recomputable data should be marked optional. Optional data is not
preserved when this version resaves it. Current world data is always required.

## Memory and validation

Each operation uses approximately 1 MiB of decoded staging plus 1.012 MiB of encoded
staging and one reusable codec context. There is no full-world compression buffer
or CPU mesh cache. The existing world/state allocations remain unchanged. Lengths,
codecs, flags, versions, sequences, group sizes, section counts and total decoded/
encoded bytes are checked before using records. Zstandard's configured decoder
window limit is 1 MiB. The existing semantic and ownership validation still runs.

Legacy uncompressed versions 1-10 and compressed versions 11-13 remain readable.
All pre-13 worlds migrate to cheats enabled, Normal difficulty and Survival mode,
including version-12 worlds previously recorded as Creative. Version-12 world time
is preserved; older formats without time start at morning. Blocks, inventories,
entities and other state remain unchanged. Subsequent version-13 saves retain each
world's selected rules independently, so migration defaults are not reapplied.
New worlds default to cheats off, Normal and Survival; creation selections override
those defaults before the initial save. No world-rule values are stored in the
application settings JSON; old global mode/difficulty fields are ignored.
Bed block IDs 61/62 are foot/head; their paired orientations use the existing
structure metadata layout, validated as one matching two-block structure.
Existing 64-high 256/512-square
worlds retain the established height migration: blocks keep their coordinates and
the added upper half is Air. No terrain or entities regenerate. Loading alone does
not rewrite a save; the next successful save atomically replaces it with v14.

## Version-14 feature payload

Section 5 has the following logical order. Large payloads are split across
sequence-numbered, independently compressed frames without changing this order.
Counts are Int32, points are three Int32 coordinates, booleans are validated
0/1 bytes, and a burning state is two Float64 values (remaining seconds, partial
damage-second accumulator).

1. Int32 feature schema (2; the reader also accepts 1); sign count, then cell and BinaryWriter UTF-8 string
   per sign. Text is at most four lines of 24 printable ASCII characters. Every
   Sign structure must have exactly one text entry, including empty signs.
2. Non-pig passive-mob count, matching the core mob array; Float64 egg countdown
   and burning state for each mob in that array's order.
3. Varied-generation flag, then regional weather: Float64 next-weather and
   remaining-storm timers; Float32 center X, Z and radius; UInt32 reproducible
   weather RNG state; Float64 presentation motion clock.
4. Snowball count; Float32 position XYZ, velocity XYZ and age, then 16-byte
   projectile identity per snowball. The combined Arrow/Snowball limit is 256.
5. Player burning state; pig count and per-pig burning states; hostile count
   and per-hostile burning states, in their existing core-array orders.
6. Fire count; cell, Float64 remaining lifetime and Float64 next-tick delay per
   flame, followed by one attachment byte in schema 2, limited to 4,096 entries
   and matching the world's Fire blocks. Attachments: 0 = automatic inference,
   1 = floor, 2 = north, 3 = east, 4 = south, 5 = west (direction toward support).
   Schema-1 records have no attachment byte; existing valid wall/floor support
   determines their orientation. Ceiling-only support is no longer valid.

Egg, weather and fire deadlines are remaining active-gameplay durations, never
wall-clock timestamps. Fire's temporary spread RNG can restart after loading;
existing flames, burning actors and their timers are restored, not rerolled.
Particles, arm swings and cloud mesh geometry are presentation-only and are not
saved. Slab/stair/sign orientations use the existing structure metadata: low
two facing bits, plus bit 2 for upper slabs/upside-down stairs. Standing Signs use
bit 3 for an additional 45-degree turn; their old unused bit 2 is ignored. Wall
Signs remain cardinal. The existing byte layout and format version are unchanged;
the reader/writer validate the expanded Sign-specific flag range. New block IDs
63-88 and item IDs 328-333 append to the previous registries without renumbering.
Block IDs 87 (Cactus) and 88 (Dead Bush) are also their block-item IDs. The outer
compressed format remains version 14; only the independently versioned feature
payload changed. Existing terrain, climate and entities never regenerate.

Versions 1-13 have no feature section. They load with their original blocks,
inventories, structures, populations and rules under the established migration
policy, default egg timers, no active new fire/snowball effects and temperate
climate. Biomes, mountains, dungeons and crops are never generated retroactively.
Version-13 cheats/difficulty/mode and time are retained exactly. All older save
paths continue through the existing validation and safe replacement machinery.

[WorldSaveManager.cs](./WorldSaveManager.cs) retains the same same-directory
temporary-file, `Flush(true)`, replacement/rename and failed-temp cleanup procedure.
Compression completion and all checksums precede the flush/replacement. A failed
write or replacement leaves the previous valid world file untouched.

## Verification and benchmarks

Run from the project directory:

```powershell
dotnet build
dotnet bin/Debug/net9.0/MDSM.dll --compression-test
dotnet bin/Debug/net9.0/MDSM.dll --self-test
```

The focused command includes all four sizes, both codecs/stored records,
non-seekable input, independent header-only listing, complete v10-payload SHA-256
equality, optional/required extensions, corruption/truncation, legacy migrations,
and atomic failure handling. Existing byte-offset fixtures explicitly use the
unchanged v10 writer, keeping their structural checks effective.

Reproduce the representative benchmark (generated seed 173, edits, inventory,
durability, items and area-scaled mobs; temporary data stays under build output):

```powershell
dotnet bin/Debug/net9.0/MDSM.dll --compression-fixture Massive
dotnet bin/Debug/net9.0/MDSM.dll --compression-benchmark Massive raw
dotnet bin/Debug/net9.0/MDSM.dll --compression-benchmark Massive zstd
dotnet bin/Debug/net9.0/MDSM.dll --compression-benchmark Massive brotli
```

Replace Massive with Small/Normal/Large for other sizes. Each pipeline case should
run in a fresh process. Save timing includes serialization, validation, checksums,
compression, file flush and rename. Load timing includes decompression, semantic
validation and world reconstruction. Source-fixture creation/loading and the
post-load comparison hash are outside those timers. Working-set and managed peaks
are sampled every 10 ms, not guaranteed instantaneous peaks or GPU measurements.

The standalone [codec benchmark](../Tools/SaveCompressionBench/Program.cs) also
compares Zstandard and Brotli using sequential bounded buffers and SHA-256 checks:

```powershell
dotnet run --project Tools/SaveCompressionBench -- bin/Debug/net9.0/compression-benchmark/Massive.raw zstd 1 3
dotnet run --project Tools/SaveCompressionBench -- bin/Debug/net9.0/compression-benchmark/Massive.raw brotli 1 3
```

Its timings measure codec/file/hash work without allocating a game world. Full
pipeline results and measurement limits are recorded in
[CODEX-SUMMARY.md](../Summaries/CODEX-SUMMARY.md).
