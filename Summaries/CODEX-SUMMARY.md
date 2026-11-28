## 2026-11-23 · Workbench face textures

The crafting table and metalworking bench now use their dedicated textures on all four vertical faces. Both retain their original top and bottom tiles and saved placement orientation. The furnace, smoker, blast furnace, and brick kiln retain their directional face mappings.

The placement regression checks all four lateral faces and verifies both benches' full face mappings after an Infinite chunk unload and reload. `dotnet build`, `--placement-update-test`, `--crafting-test`, and `--interface-graphics-test` passed.

Run CPU regressions in the background and keep graphics tests off the active Windows desktop. All 273 checks in `dotnet run --no-build -- --self-test` passed in the background. On this host, SDL's Windows offscreen driver could not initialize OpenGL, and several graphics checks require a focused window.

## Background graphics runner

The Windows checkout builds the game and MonoGame content with `dotnet build`. [RunHeadlessGraphics.sh](../Tools/RunHeadlessGraphics.sh) copies that build to a temporary Linux directory so WSL reads an independent copy of the assembly. It uses .NET 10 through the project's major roll-forward setting, starts a private Xvfb display with Openbox, and runs the existing graphics suite with Mesa llvmpipe and a silent audio device. Exported PNGs go to `bin/headless-graphics-artifacts`.

From an administrator PowerShell, install Ubuntu 24.04 on WSL and restart Windows if prompted.

```powershell
wsl --install --distribution Ubuntu-24.04 --no-launch
```

Install the runner dependencies as root in Ubuntu, then create an unprivileged account for the graphics runs if the distro has no regular account.

```bash
apt-get update
apt-get install --no-install-recommends dotnet-runtime-10.0 xvfb xauth openbox x11-utils mesa-utils libgl1-mesa-dri libglx-mesa0
/usr/sbin/useradd --create-home --shell /bin/bash mdsm-runner
```

From the repository in PowerShell, run the default graphics suite or pass a focused test switch such as `--interface-graphics-test`.

```powershell
wsl --distribution Ubuntu-24.04 --user mdsm-runner --exec bash Tools/RunHeadlessGraphics.sh
```

The Linux aggregate runs each graphics suite in a fresh process because the in-process aggregate crashed after the seasonal suite. Interface, water, and mob screenshot checks let the SDL/X11 drawable resize complete before comparing exact pixels. The runner is installed on Ubuntu 24.04 under the unprivileged `mdsm-runner` account.

`dotnet build`, Bash syntax validation, ShellCheck, the focused water and mob movement checks, and all ten suites in the headless graphics aggregate passed. The aggregate log is in `bin/headless-graphics.log`.

## 2026-11-28 · Industrialization milestone 2: electricity

Implemented placeable copper wire, solar panels, coal generators, full-block access
and output panels, and right-click power switches. New block IDs 147–152 are
appended; wire retains its original item ID 339, sprite and bench recipe. There is
no duplicate wire item. The other five blocks have inventory/held/drop models,
Creative entries, mining rules, recipes and persisted state. README.md is untouched.

### Building and operation

Wire is a thin, non-colliding cable with a targetable central joint and automatic
arms in all six directions. It can run vertically. Solar panels are low plates;
access/output panels and generators/switches occupy full blocks. Output panels
and switches face the player on placement. Their marked rear is the input, their
front is the output, and side faces do not connect. Other electrical blocks accept
connections on all faces. Place wire alongside the intended ports.

Solar panels need an access panel on their supply circuit to generate. Feed that
circuit into the rear of an output panel; connect consumers to its front, optionally
through switches. Coal generators feed output panels without an access panel.
Multiple generators, access panels and output panels share their actual supply;
loops and parallel paths do not create additional electricity.

Right-click access/output panels for production, consumption, free power, source,
output and consumer totals. Right-click a generator for its coal/charcoal intake,
remaining fuel seconds, active-fuel bar, production, consumption and free power.
Generator consumption is attributed to that generator; the access panel reports
its supply circuit. No consumers have been added, so zero consumption and positive
free power are meaningful. Free power is unused instantaneous output, not stored
energy. Generators burn fuel while idle, including when disconnected. Burning
generators use the existing ignition sound and level-13 furnace lighting.

Right-click a switch to open/close its route; its front artwork changes between
red and green. Each switch supports five reachable machines, chosen in stable
coordinate order if more are attached. Additional machines can use another
switch or output branch. A separately wired live route can still power a machine
when another route is switched off.

All five new block recipes use the Metalworking Bench. Rows below use I=iron
ingot, C=copper ingot, W=existing copper wire, G=glass, B=black dye, S=stick,
F=ordinary furnace. Only existing obtainable materials are used.

- Solar panel: GGG / BCB / IWI.
- Coal generator: III / WFW / ICI.
- Access panel: IGI / WCW.
- Output panel: IWI / WCW.
- Power switch: ISI / WCW.

### Network and extension contract

`Worlds/ElectricalBalance.cs` centralizes the 750 W solar maximum, 6,000 W coal
maximum, 80 active seconds per coal/charcoal, 30% overcast output, 0.25-second
network cadence and five-machine switch limit. Solar output follows the existing
sun direction/daylight and regional precipitation, with cached direct sky
exposure. Any occupied cell above a solar panel blocks it, including glass and
snow. Column edits invalidate only affected solar exposure entries.

`World.Electricity` indexes resident electrical cells as chunks publish or blocks
change. Ordinary terrain is rejected using a vectorized byte-range check during
publication. Terrain is never scanned during an electrical tick. Cached topology
is rebuilt after electrical edits, registration changes, residency changes or
simulation-window movement. Wires and supply blocks form undirected buses;
output panels and switches have separate input/output ports and directed routes.

`ElectricalAllocation` allocates each unique source's watts once and each unique
consumer's request once. It progressively fills requested-power fractions using
residual max-flow cuts, so proportional shortages respect restricted source
reachability while unrelated circuits can use their spare power. No voltage,
resistance, loss, capacity limits, batteries, or fictional consumer blocks were
introduced. Quiet networks keep their topology; circuits with no demand skip the
allocation solver.

Future real machines implement `IElectricalConsumer` with an absolute `Point3`
cell, a finite requested wattage, and `ReceivePower(watts, elapsedSeconds)`.
Register after placement/restoration on the world thread and unregister when
ownership ends. One machine endpoint is allowed per cell. Scale actual machine
work by the delivered/requested fraction, and do not accumulate work during a
zero-elapsed reset callback. Block replacement and owner-chunk retirement reset
and remove registered endpoints. Asynchronous mesh snapshots copy only terminal
geometry, not live consumer objects. Future machine persistence remains the
responsibility of the owning machine's chunk state.

Only resident, simulated circuits operate. Unloaded/dormant wires interrupt the
route; dormant sources consume no fuel. Reentry, publication, cancelled
retirement, splitting and merging rebuild the network from actual loaded blocks.
Generator stacks/burn reserves use existing block-container records, panel state
uses empty containers, and switch orientation/open state uses existing structure
records. Existing save versions and IDs remain readable; no format bump is needed.

### Original artwork

The built-in imagegen skill generated `Content/Blocks/electrical-source.png`, an
original 4×3 texture sheet. `Tools/ImportElectricalTextures.ps1` imports its tiles
with nearest sampling into terrain tiles 100–111. The editable terrain atlas is
the runtime source. Pixel comparison verified all 3,072 new texels and every
unchanged texel outside those tiles, including partially transparent glass RGB.
The importer clones bitmap data and saves through a temporary PNG to preserve it.

Final imagegen prompt (built-in tool):

Create ONE production game texture sprite sheet for an original retro voxel survival
game, flat orthographic pixel artwork, NOT a rendered scene. Exactly 4 columns by 3 rows
of equally sized square tiles, flush edge-to-edge, NO gutters, no captions or text, no
perspective, no outer margin. Canvas aspect ratio 4:3. Each tile is a single 16x16 pixel
art block face scaled up with nearest neighbor crisp pixels, muted classic earthy game
palette, restrained weathered industrial look, designs original, no logos. Tile order
row-major: 1) bright copper cable surface, warm orange brown with longitudinal
highlights and dark sheath edges; 2) blue-black solar photovoltaic panel top with silver
frame and fine copper cell grid; 3) solar panel rim and underside, dark steel ribbed
edge; 4) coal generator side, weathered gray iron with small dark vents; 5) steel
machine top with corner rivets and brushed metal; 6) coal generator front, small coal-
burning furnace intake at bottom and bronze rotor/vent at top; 7) electrical panel side
with weathered gray metal and one copper rear input socket; 8) access panel front with
dark green CRT display showing tiny abstract mint bars, no text, inset bronze bezel; 9)
output panel front with prominent copper output socket and small amber arrow shape; 10)
power switch front ON, dark metal plate with central bronze lever raised and green
indicator; 11) power switch front OFF same plate lever lowered and red indicator; 12)
electrical panel REAR input, prominent copper recessed socket and blue inward arrow. All
are opaque textures. Fill each whole tile square with artwork. One coherent atlas sheet.

### Verification

Focused checks are available with `--electrical-test` and included in `--self-test`.
They cover proportional allocation, restricted/overlapping sources, solar weather,
night and roofs, access requirements, switch limits/toggles, parallel output paths,
splits/merges, canonical wire placement/drops, recipes, fuel exhaustion and restored
validation, targeting/collision and inventory geometry, signed Infinite seams,
simulation-window dormancy, chunk retirement and reload, switch orientation and
fuel reserves, plus immutable worker mesh construction.

Electrical UI and right-click checks are part of `--collision-graphics-test`.
Screenshots at 320×240 and 1280×720 and world views in both lighting modes are
exported into `bin/headless-graphics-artifacts`. The private WSL runner also accepts
`MDSM_GRAPHICS_BUILD_DIR` for a separate validation build, avoiding Windows locks
while an existing test executable is running.

Final verification:

- `dotnet build`: passed, zero warnings and errors.
- `--self-test`: all 277/277 Infinite and shared CPU checks passed on the final build.
- `--graphics-test`: all ten suites passed on the private WSL/Xvfb display.
- Final `--collision-graphics-test`: passed after generator-lighting and lossless-atlas
  changes, including fueled generator statistics and responsive electrical screens.
- Focused `--electrical-test`, `--lighting-test` (6/6), `--completion-test` (12/12)
  and `--crafting-test` passed. Shell syntax and `git diff --check` passed.

The first broad run exposed three exact recipe-count assertions, the obsolete
unused-atlas range, and a duplicate raw wire item identity. These were corrected;
the final CPU run has no failures. Intermediate overlapping build/test runs hit
Windows executable/DLL copy locks; the final standard build succeeded after the
running tests released their files. No outstanding failures remain. Circuits
intentionally stop at the simulation boundary and resume when the chunks become
active again.
No publishing, commits, tags or releases were performed.

## 2026-11-28 · Baked seasonal block artwork

The three formerly blank terrain tiles at (64,80), (80,80), and (96,80) are
snowy grass sides (84), peonies (85), and violets (86). Captured the original
runtime atlas before changing the loader, copied those exact RGBA pixels into
`Content/Blocks/terrain.png`, and removed `SeasonalArtwork.Extend`, its flower
painting routine, and the loader call. These tiles now come directly from the
editable PNG. Existing seasonal color tinting and code-drawn UI remain intact.

All 768 tile pixels, including transparent pixels, match the captured originals.
Every pixel outside the three tiles matches the atlas from the start of this task;
384 formerly transparent texels changed. Frozen SHA-256 checks verify the source
PNG and the compiled GPU atlas against the original runtime pixels. No migration
capture code or runtime sprite generator remains.

`AGENTS.md` now requires the imagegen skill for new block/item artwork, block
textures in `Content/Blocks/terrain.png`, item textures in `Content/Items/items.png`,
and no procedural painting of block/item artwork. Existing generated artwork must
be migrated pixel-exactly. UI remains drawn in code.

Verification: `dotnet build` passed with zero warnings/errors. All eight focused
`--season-lead-test` CPU checks passed, including the new exact-artwork check.
`--season-lead-graphics-test` passed on the private WSL/Xvfb display, including
exact compiled pixel hashes and existing seasonal tint/snow-white checks.
No outstanding issues; no publishing or Git commits performed.
