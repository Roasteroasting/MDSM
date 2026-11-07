# Procedural sound assets

These are original synthesized effects, not Minecraft recordings or downloaded audio.
The game loads the WAVs once through MonoGame `SoundEffect.FromStream`; generation
never runs when building or launching the game. MGCB copies the WAV files unchanged
into the output Content directory, using the same content deployment mechanism as
the existing assets. No external audio service, encoder, or new package is required.

There are **279** mono, 22,050 Hz, 16-bit PCM WAVs (**3,205,644 bytes**) in **112** groups:

| Directory | Coverage |
| --- | --- |
| Blocks/ | Stone, Dirt, Grass, Wood, Sand, Gravel, Glass, Metal, Wool, Leaves and Plant; three variations each for hitting, breaking, placing, stepping and landing |
| Mobs/ | Pig, Cow, Sheep, Chicken, Zombie, Skeleton and Spider; two variations each for idle, hurt and death |
| Player/ | Damage, death, pickups, jumping and eating |
| Water/ | Entering, exiting, swimming, nearby active flow, pouring and scooping |
| Interface/ | Clicks, inventory opening/closing/movement, crafting, equipment and manual drops |
| Interactions/ | Doors, Gates, Chests, tools breaking, bow draw/fire, arrow impacts, melee swings, shearing, tilling, planting, Furnace ignition/results, feeding and fluid-contact fizz |

The shared [catalog](../../Audio/SoundCatalog.cs) defines names, variants, material
families, categories, ranges, gains, repetition intervals and priorities. Sound
indices are transient and have no relationship to persistent block/item IDs.

## Regenerating and checking

From the project directory:

```powershell
dotnet run --project Tools/SoundGenerator -- .
dotnet build
```

The separate [generator](../../Tools/SoundGenerator/Program.cs) uses deterministic
per-path/per-variation seeds. Tune [Synthesizer.cs](../../Tools/SoundGenerator/Synthesizer.cs)
to change oscillator frequencies, harmonic/formant content, filtered noise, grain
envelopes, modulation, attack/tail or timing. Generated clips have zero-valued
endpoints, DC correction, soft saturation and approximately 0.68 peak headroom.
Regeneration updates only the marked sound-copy section of the content pipeline.

Check byte-for-byte reproducibility without rewriting assets:

```powershell
dotnet run --project Tools/SoundGenerator -- . --verify
dotnet bin/Debug/net9.0/MDSM.dll --audio-test
dotnet bin/Debug/net9.0/MDSM.dll --audio-graphics-test
```

The latter opens a scripted MonoGame window and plays quiet representative sounds;
it requires an audio device and graphical desktop. It does not replace human
listening. Volume and character should be assessed on speakers/headphones before
final sound-design approval. There is no music or continuously looping ambience.

## Playback and settings

Settings > Sounds offers Master, Effects, Mobs and Interface sliders (0–100%,
one-percent increments). Master defaults to 80%; the three categories default to
100%. Preferences share the existing application settings file, not world saves.

Playback uses a bounded 256-event queue per world, two pre-created instances per
clip, at most 32 active voices, per-group rate limits and priority-based eviction.
Consecutive variants differ and pitch varies subtly. World events use stereo pan
and distance attenuation (typically 16–36 blocks); these do not simulate acoustic
occlusion or Doppler effects. Playback updates positional gain/pan as the camera
moves. UI remains audible in menus, while world sounds stop with paused gameplay;
Chest interactions, processing Furnace feedback and the death cue can finish in
their interfaces. Muting discards events rather than accumulating a replay backlog.

Missing/corrupt clips are skipped with one startup summary. If audio initialization
or playback is unavailable, the game continues silently and reports the problem to
the console. Effects and pooled instances are owned/disposed by the audio manager;
they do not use GPU buffers, textures, chunk meshes, or world-save storage.
