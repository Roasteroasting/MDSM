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

## 2026-11-28 · Grounded cables and live interfaces

Copper wire now runs along the bottom of its voxel at 1/8-block thickness instead
of floating at the voxel centre. Horizontal connections meet low solar-panel edges;
connections to full-block electrical terminals rise against their faces. Vertical
runs remain continuous. A panel with a top-connected cable draws a cable lead from
its low plate to the next voxel boundary. That lead is targetable but does not add
solid collision above the panel. Thin copper wire above a panel no longer counts
as a complete sky obstruction; ordinary roofs/glass/snow retain their prior rules.
Network topology, power allocation, item IDs and save layouts are unchanged.

World meshes and targeting share the same cable boxes, with all connections and
terminal risers fitting the existing nine-box shape budget. Meshing clips buried
joint faces and omits caps against connected neighbors. Cable UVs sample a narrow
band of the existing copper artwork, rotate it along each run, and retain block
texel density rather than compressing a whole 16x16 tile onto each thin face.
Panel sides also use appropriately cropped UVs. No atlas artwork was generated,
redrawn, or changed.

Inventory, crafting, chests, furnaces, industrial panels and other stations now
use the ordinary fixed-step world simulation with neutral player controls.
Day/night, weather, solar wattage, machines, fluids, farming, gravity, mobs,
projectiles, pickups and player hazards remain live. Keyboard/mouse UI actions do
not become movement, jumping, flight toggles, hotbar changes, mining or attacks.
Command and sign interfaces likewise keep the world running. Streaming continues
while interfaces are open, including main-thread chunk publication and reframing.
Physics still waits for safe nearby terrain if streaming is not ready.

The actual pause menu and its settings screens freeze simulation. Escape closes
an open interface as before; Escape during gameplay opens the pause menu. Losing
window focus opens the pause menu, including from inventory, and resuming restores
that inventory with its cursor/crafting ownership intact. Death and environmental
damage work while an interface is open. Weather presentation and game audio also
continue in live interfaces.

Autosave elapsed time advances with live simulation. A due autosave waits while
the cursor or crafting slots contain stacks, avoiding silently clearing an active
recipe or returning/dropping the held stack. Saving resumes when those transient
items are returned; explicit save/exit retains existing item-return behavior.

Regression coverage now exercises all cable masks, floor/solar contact, terminal
risers, the nine-box budget, UV density, top-panel wiring, and Infinite seams.
Scripted Game1 input covers inventory and all placed stations, changing solar
wattage, fuel, time/weather, neutral controls, gravity, damage/death, pause/resume,
and autosave-owned stacks. An Infinite fixture opens inventory before chunk
publication and verifies live cross-seam access-panel monitoring afterward. The
existing mob graphics test now expects replacement timers to advance in inventory
and remain frozen in the pause menu. Updated world screenshots show cables beside
panels and terminals at actual ground level in both lighting modes.

Verification: `dotnet build` passed with zero warnings and errors. All 278/278
Infinite/shared CPU regressions passed. The focused electrical checks passed again
after the final connected-cap adjustment. All ten suites in the final private
WSL/Xvfb `--graphics-test` run passed, including the new live-interface and streamed
panel checks and the updated mob-timer check. The earlier graphics run exposed the
old expectation that inventory freezes replacement timers; that regression now
checks the requested live behavior and passes. `git diff --check` passed.

Logs: `bin/wire-live-self-test.log`, `bin/wire-live-graphics-final.log`; current wire
views and station screenshots are in `bin/headless-graphics-artifacts`.
No outstanding failures. README.md and texture atlas pixels were not changed.
No publishing, commits, tags or releases were performed.

## 2026-11-28 · Industrialization milestone 3: electric processing

Added electric furnace (block 153), electric glass kiln (154), and electric cooker
(155), using the existing `IElectricalConsumer`, output-panel routes, five-machine
switches, proportional allocator, electrical cadence and simulation window. There
is no parallel power system. Machines register when placed/published/restored and
unregister on removal/retirement; snapshot wire ports include them.

`Worlds/AdvancedProcessing.cs` centralizes ratings, recipe definitions, durations,
inventory sizes and product names. Nominal ratings are 2,000 W / 2,500 W / 400 W.
The furnace processes all ordinary-furnace recipes in 2.5 seconds at full power
(four times the ordinary furnace speed), with four input and four output slots.
Inputs may hold different smelting materials, processed in slot order. Cooker
simple dishes take two seconds; vegetable stew takes four. The fuel-based furnace
still works without infrastructure, while ceramic pots still require the primitive
brick kiln. No new generic components or raw ingredients were added.

All machines show rated power, current demand, allocated power and a progress bar.
Kiln/cooker arrows select products and show ingredient/output requirements.
Selections remain fixed during partial work; finishing the work or removing its
ingredients allows reselection. Work scales with delivered/nominal watts.
Disconnected, shaded or switched-off machines preserve partial work; blocked
outputs pause without drawing power. Ingredients are retained until an entire
transaction can complete, then consumed together with output insertion. Output
slots only allow withdrawal. Mixed batches use all compatible output slots.

### Recipes and products

All three machine recipes use the existing Metalworking Bench. Rows below use
I=iron ingot, C=copper ingot, W=copper wire, P=ceramic pot, F=furnace, K=brick kiln,
S=smoker, G=ordinary glass, B=brick item.

- Electric furnace: IPI / WFW / ICI.
- Electric glass kiln: IGI / WKW / BCB.
- Electric cooker: IPI / WSW / ICI.

The glass kiln accepts clear glass or any existing stained-glass block and
preserves its color. Its selectable products are:

- 1 glass -> 2 slabs, four seconds.
- 3 glass -> 4 stairs, six seconds.
- 3 glass -> 8 panes, four seconds.
- 1 glass -> 1 decorative tile block, four seconds.
- 2 glass -> 1 flower vase, five seconds.

Slabs, stairs, decorative glass and vases each have clear plus sixteen stained
variants. Existing panes and their dye recipes are reused. Eight clear new shapes
around an existing dye yield eight colored shapes at the crafting table; all 64
new staining recipes are covered. Slabs join into matching double slabs, which
drop their two original slabs. Stairs reuse existing orientation, inverted
placement and corner logic. New glass mesh faces merge half-cell regions and
cull matching-color shared surfaces, including partial slab/stair intersections;
collision/targeting use the existing oriented building boxes. Decorative tiles
are full transparent glass blocks. Dye tint works in world, inventory, held and
dropped meshes without new procedural artwork.

Vases are hollow five-part glass models with matching collision and flower
targeting. Right-click with any existing flower to insert one; right-click with
an empty hand to retrieve it when inventory has space. Contents survive finite
and Infinite saves and are included in asynchronous mesh snapshots. Survival
mining drops the vase and its flower through the existing container-drop path.

Cooker choices are cooked pork, baked potato, roasted carrot, fried egg, and
vegetable stew. Stew uses one carrot, one potato and one ceramic pot; eating it
returns the pot. The three new food items (IDs 360–362, sprites 106–108) heal
4 / 4 / 8 and follow existing single-serving food conventions. No new ingredients
are needed. Existing fuel-based food recipes remain available.

### Integration and persistence

Block IDs 156–240 append five contiguous 17-color families (slab, stairs,
decorative tile, vase, joined slab). Joined slabs have no standalone inventory
item. All obtainable new blocks/items have Creative entries, atlas-backed held
and dropped models, crafting/mining integration and save validation.

`BlockEntityState` adds optional recipe-selection and work-item metadata.
Infinite JSON metadata saves it through the existing resident-container pipeline.
Finite binary records append those two fields only for the three new machine
IDs; old container records keep their layout. Power allocations are transient and
recomputed after load. Corrected `Chunk.RestoreBlocks` to count all registered
oriented building shapes, rather than just the old fixed slab/stair ID range,
so newly saved shaped glass restores its orientation metadata correctly.

### Artwork

Used the imagegen skill for original textures, retained the source sheets as
`Content/Blocks/advanced-processing-source.png` and
`Content/Items/advanced-food-source.png`, and imported them with
`Tools/ImportAdvancedTextures.ps1`. The importer only crops/nearest-samples
generated artwork into 16x16 atlas tiles; it does not synthesize pixels. It
preserves unassociated transparent RGB by cloning the existing atlas bitmap.

Terrain slots 112–119 hold three machine fronts, shared side/top, decorative
glass, vase glass and a reserved glass-edge variant. Item slots 106–108 hold the
three new foods. Every pixel outside those assigned slots matches the pre-task
atlases exactly, including transparent pixels: zero unexpected changes in either
PNG. Existing artwork, including the previously baked seasonal tiles, is intact.
UI remains code-drawn.

Exact block-sheet imagegen prompt:

> Production texture atlas sheet for original retro voxel survival game MDSM. Exactly FOUR columns and TWO rows of square tiles, flush edge-to-edge, NO margins, gutters, labels, captions, text, perspective or 3D scene. Crisp 16x16-style pixel art with muted weathered iron, copper and cream ceramic, consistent industrial palette. Order row-major: 1 electric furnace front, square insulated oven door with orange warm window and tiny green power lamp, no burning coal; 2 electric glass kiln front, deep dark crucible window and amber heat gauge, pale ceramic insulation; 3 electric cooker front, small oven hatch with dark glass window, copper knobs and tiny green lamp; 4 shared machine side, riveted grey iron with neat dark cooling vents; 5 shared machine top, dark iron rim and pale ceramic centre inset; 6 DECORATIVE GLASS TILE block-face texture: very thin neutral WHITE/PALE GRAY square outer border and fine white diamond/diagonal etched lines, truly TRANSPARENT empty interior, no colored backdrop; 7 GLASS VASE wall texture: delicate WHITE/pale gray rim and clear glass highlight streaks, truly TRANSPARENT interior, no colored backdrop, a texture face not an illustration of a whole vase; 8 GLASS EDGE texture: thin pale white glass border and small diagonal highlight on truly transparent interior. Tiles 1-5 must be fully opaque square block faces. Tiles 6-8 must have genuine alpha transparency throughout their interiors, suitable for a voxel cutout glass renderer and neutral enough for sixteen dye tints. No checkerboard pixels or background color baked into transparent regions. One coherent texture-sheet asset. All designs original.

Exact food-sheet imagegen prompt:

> A single game food sprite-sheet asset, exactly THREE equal square tiles in ONE horizontal row, aspect ratio 3:1, flush edge-to-edge no gutters or outer margins, each sprite centred with at least one logical transparent pixel around it. Crisp original 16x16-style pixel art, warm earthy palette, readable retro voxel survival inventory style. Left: one roasted carrot, orange-gold carrot with browned ridges and a tiny green stem. Centre: one fried egg, creamy irregular white with bright golden yolk and a brown cooked edge. Right: a small reddish-brown ceramic pot full of vegetable stew, orange carrot and pale potato chunks visible at its open top, no cutlery. Genuine fully transparent background outside each silhouette, no plate, no cast shadow, no checkerboard, no captions or text. No perspective scene; these are small clear inventory sprites. One coherent sprite sheet.

### Verification

- `dotnet build`: passed with zero warnings/errors.
- `dotnet run --no-build -- --self-test`: 283/283 Infinite/shared checks passed.
- Final `--advanced-processing-test`: all five focused checks passed, including
  all kiln colors/products, machine and dye recipes, full and partial power,
  atomic batches, output blocking, source/switch interruptions, finite work saves,
  and repeated Infinite chunk retirement/reload of work, completed products,
  input reserves, saved-off switches and vase flowers across signed chunk seams.
- `--crafting-test`: passed; catalog now has 236 recipes.
- All ten suites in the private WSL/Xvfb `--graphics-test` aggregate passed.
  Final `--collision-graphics-test` and `--door-graphics-test` runs also passed
  after sharing exposed-face glass geometry with inventory/held models.
- Final focused `--industrial-test` and `--electrical-test` checks passed.
- Atlas pixel preservation and `git diff --check`: passed.

The broad graphics run exposed a fixed sprite-count assertion (105) that needed
the three new food sprites (108); the check now also reports its actual count.
Graphics checks exercise actual RMB station opening, selector/input/output clicks,
live production, vase flower insertion/removal, 320x240 and 1280x720 interfaces,
all obtainable held models, and stained shapes in both lighting modes. Screenshots
are in `bin/headless-graphics-artifacts`; logs are
`bin/milestone3-self-test.log`, `bin/milestone3-processing-test.log` and
`bin/milestone3-graphics-test.log`. Final geometry/input logs are
`bin/milestone3-collision-final.log` and `bin/milestone3-held-final.log`.

Resident machines intentionally stop outside the simulation window and resume
when active again; no offline catch-up is introduced. No publishing, commits,
tags or releases were performed. README.md is unchanged.

## 2026-11-28 · Wiki catch-up for electrical infrastructure and machinery

Updated the local `Wiki/` site for industrialization milestones 2 and 3, grounded
cables, baked flower artwork and live interfaces. The gameplay guide now explains
solar/coal generation, panel direction, wiring, five-machine switches, proportional
power sharing, monitoring, fuel use, machine inventories/timings, interruptions,
chunk/save behavior, all glass products and dyes, vase flowers, cooker recipes,
food healing and ceramic-pot return. Homepage links and catalog descriptions point
to the new content; obsolete "wire reserved for future use" text is removed.

Extended `Tools/WikiExport/Program.cs` to export electric-furnace smelting times,
all 85 clear/stained kiln recipes, five cooker recipes, electrical balance values,
machine input/output sizes and glass-family metadata from the existing game APIs.
Refreshed `Wiki/data.js` and its atlas copies: 241 block IDs, 265 inventory entries
(105 standalone items), 236 crafting patterns, eight smelting recipes and 90
additional electric processing recipes. Repeated export is byte-identical.

Recipe browsing now filters electric machines, glass kilns and cookers, searches
ingredients/results/machines, displays power and full-power timings, and groups
the stained variants in expandable tables. Item and machine pages link their
electric production/use recipes. The canonical wire item (339) now resolves its
properties and crafting links correctly from block 147. New shaped glass has
correct drop/interaction descriptions rather than the old no-drop glass fallback.
Peony and violet icons use the atlas artwork instead of placeholders. Updated
global search and guide anchors connect the new topics.

For future registry/recipe changes, run
`dotnet run --project Tools/WikiExport -- .`, then update the player-facing
`Wiki/guide.html` and `Wiki/notes.js` descriptions as needed. The export refreshes
`Wiki/data.js` and copies the current PNG atlases; no publication is performed.

Verification: `dotnet build` and `dotnet build Tools/WikiExport/WikiExport.csproj`
passed with zero warnings/errors. The reusable
`Tools/WikiExport/CheckWiki.html` checker passed in headless Edge with a separate
profile and `--allow-file-access-from-files`: 362 offline pages, 5,191 item/block
links, all catalog entries, new recipes/filters, canonical wire identity, baked
flower icons, new food notes, guide/search integration and 390/1280-pixel layouts.
Static checks passed for all six wiki HTML pages and 127 local file/fragment/asset
links. Both wiki atlas PNGs exactly match their current Content sources.
Desktop and narrow-window screenshots were reviewed. `git diff --check` passed.
No gameplay source changed, so the full gameplay regression suite was not rerun.

Artifacts: `bin/wiki-check-dom.html`, `bin/wiki-cooker-desktop.png` and
`bin/wiki-glass-mobile.png`. No outstanding failures. README.md is unchanged;
no publishing, Git commits, tags or releases were performed.

## 2026-11-28 · GitHub Pages deployment for the static wiki

Added `.github/workflows/deploy-wiki.yml`. Pushes to `main` affecting `Wiki/**`
or the workflow itself deploy the existing `Wiki/` contents directly through
GitHub's configure/upload/deploy Pages actions. Manual dispatch is available,
with the deploy job restricted to `main`. The workflow has read-only repository
contents permission, Pages/OIDC write permissions, the `github-pages` environment
and deployment URL, and serialized deployments without canceling an active run.
Its artifact root is `Wiki`, not the repository, so the site is served at the
repository Pages base path without an extra `/Wiki/` directory. No game build,
wiki export, package installation or game publishing occurs in the workflow.

The current HTML navigation, CSS references, JavaScript-generated links and atlas
URLs are already relative and work unchanged at `/MDSM/`. Kept all wiki files in
their current directory. Extended `Tools/WikiExport/CheckWiki.html` with a `site`
query parameter for HTTP/project-site testing, resource/subpath assertions and
full-page navigation checks, while preserving its default offline-file mode.

Verification: workflow YAML parsed and its branch/path filters, permissions,
environment, action sequence and artifact scope passed focused assertions.
Headless Edge passed all 362 pages and 8,087 navigation/item/block links both
over HTTP under `/MDSM/` and as offline files, including CSS/JS/assets, search,
processing filters, registry entries and 390/1280-pixel layouts. The HTTP fixture
served only `Wiki/` under `/MDSM/`; domain-root assets returned 404. All 129
static HTML/CSS references resolved successfully inside the subpath.
`dotnet build` passed with zero warnings/errors; `git diff --check` passed.
Reports are `bin/wiki-pages-check-dom.html` and
`bin/wiki-pages-offline-check-dom.html`.

Repository setup still requires Settings > Pages > Build and deployment > Source
to be GitHub Actions. No remote settings were changed and no live Actions run or
deployment was attempted: the requested files remain local for the owner to
commit and push. After that setup, the expected project URL is
`https://roasteroasting.github.io/MDSM/` (unless a custom domain is configured).
No game source or README changes, commits, pushes, game builds published, or
releases were made.
