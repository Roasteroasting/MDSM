# MDSM

A small Minecraft Beta 1.7.3-inspired voxel sandbox prototype in C# and MonoGame DesktopGL. The world is a 32 x 16 x 32 block volume with a flat grass surface, two dirt layers, and two stone layers.

## Build and run

Install a .NET SDK that supports the project's existing `net9.0` target and a graphics driver with OpenGL support. The project retains its original MonoGame DesktopGL packages and allows major runtime roll-forward.

```powershell
dotnet build
dotnet run --no-build
```

The pixel texture atlas and small UI font are generated in code, so no external texture files, installed fonts, or additional content pipeline tools are needed for this prototype.

## Controls

| Input | Action |
| --- | --- |
| W / A / S / D | Move forward / left / backward / right |
| Mouse | Look around |
| Space | Jump while on the ground |
| Left click | Break the outlined block |
| Right click | Place the selected block against the outlined face |
| 1 / 2 / 3 (or numpad) | Select grass / dirt / stone |
| Escape | Pause and release the mouse |
| Left click while paused | Resume and capture the mouse |
| F10 or gamepad Back | Quit |

Block interaction reaches six blocks from the camera. Each click performs one edit. Placement requires an empty cell inside the world and cannot overlap the player's body. Movement includes gravity, solid-block collision, and sliding along walls; diagonal movement has the same speed as forward movement. Falling off the terrain respawns the player above the original spawn column, accounting for blocks placed there. Losing window focus pauses the game and releases the mouse; the resume click does not break a block.

The hotbar provides unlimited blocks. World edits last for the current session; saving, infinite terrain, crafting, and advanced lighting are outside this milestone.

## Code layout

| File | Responsibility |
| --- | --- |
| [Game1.cs](./Game1.cs) | MonoGame lifecycle, input, mouse capture, and gameplay coordination |
| [Camera.cs](./Camera.cs) | First-person orientation, view, and perspective projection |
| [Player.cs](./Player.cs) | Player bounds, movement, gravity, jump, collision, and respawn |
| [Blocks.cs](./Blocks.cs) | Block types and atlas tile selection |
| [World.cs](./World.cs) | Flat terrain, safe reads/edits, placement, and collision queries |
| [BlockRaycaster.cs](./BlockRaycaster.cs) | Voxel grid traversal and hit-face selection |
| [TextureAtlas.cs](./TextureAtlas.cs) | Deterministic 16-pixel grass, dirt, and stone textures |
| [WorldRenderer.cs](./WorldRenderer.cs) | Exposed-face mesh, depth testing, backface culling, fog, and selection outline |
| [Hud.cs](./Hud.cs) | Crosshair, hotbar, controls, and pause overlay |
| [PrototypeChecks.cs](./PrototypeChecks.cs) | Automated checks that run without a graphics device |

Only exposed faces are uploaded to the GPU. A world revision triggers a mesh rebuild after an edit; the mesh is reused between edits. Rebuilding this small world's mesh keeps editing straightforward without adding a chunk streaming system.

## Verification

```powershell
dotnet run --no-build -- --self-test
```

The checks cover terrain and edit bounds, camera orientation, mesh face counts and winding, atlas coordinates, raycast hits and reach, placement safety, jumping and landing, wall sliding, diagonal speed, ceilings, fast falls, and safe respawn. A failing check returns a nonzero exit code. These checks do not open a window and cannot verify GPU output or operating-system input behavior.

For a manual playtest, confirm the textured terrain, crosshair, hotbar, and block outline render correctly; walk and jump into placed walls; break the floor and place all three block types; verify placement never traps the player; then test Escape, click-to-resume, window resizing, and switching focus.

