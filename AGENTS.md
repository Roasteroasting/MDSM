# MDSM Agent Development Instructions

## Project overview

MDSM (Mojang Don't Sue Me) is an original, open-source voxel survival sandbox inspired by classic Minecraft, particularly Beta 1.7.3, with its own evolving gameplay and identity. It uses C# with .NET 9 and MonoGame DesktopGL. It includes Survival and Creative modes, infinite procedural worlds, authentic Far Lands, lighting, fluids, entities, crafting, and farming.

## Development targets

- Infinite is the development target. Implement new features for Infinite worlds and exercise them through the streamed chunk lifecycle.
- Legacy is the existing 1024×1024 world option. Limit Legacy changes to basic bug fixes unless the user asks for broader coverage. Use `dotnet run -- --legacy-smoke-test` only for basic Legacy bug triage.
- Small, Normal, and Massive are retired. Keep them out of generation runs, benchmarks, and regression matrices. Compact, hand-built in-memory maps can still serve as fixtures for shared gameplay, rendering, and save-code tests.

## Development principles

- Inspect the relevant code first. Treat the current source as authoritative and verify older documentation against it.
- Reuse existing architecture, utilities, and conventions. Follow project naming, directory organization, rendering style, and UI design. Prefer straightforward, maintainable, performant solutions and avoid unnecessary abstractions or large dependencies.
- Complete requested features and their integrations. Preserve existing behavior unless the request changes it, and avoid unrelated refactoring.
- Keep work within scope. Avoid inventing gameplay content, crafting materials, placeholder items, or mechanics. Make routine implementation decisions independently and ask for clarification when a major gameplay or architecture decision cannot reasonably be inferred.

## World generation and simulation

- Preserve deterministic terrain generation, existing biome behavior, and authentic Far Lands unless the request changes them. Maintain accuracy at large coordinates with double-precision calculations and camera-relative rendering where appropriate.
- Work within the existing chunk loading, meshing, lighting, fluid simulation, and persistence architecture. Account for chunk boundaries, asynchronous loading, and unloaded neighbors.
- Avoid unbounded memory growth, excessive remeshing, unnecessary allocations, and expensive per-frame world scans. Keep visual behavior separate from gameplay simulation where appropriate.

## Gameplay and compatibility

- Preserve compatibility with existing worlds and saves wherever possible. Respect gameplay settings and world-specific rules.
- Give new items, blocks, and mechanics complete, meaningful behavior. Integrate them with crafting, inventory, Creative mode, drops, rendering, lighting, collision, and persistence as applicable.
- Check progression for circular dependencies. Players must be able to obtain a required component before using machinery that depends on it.

## Graphics and UI

- Maintain the retro voxel aesthetic and match existing menus, inventory interfaces, fonts, controls, and visual conventions.
- Use the existing rendering paths for world blocks, item sprites, 3D inventory models, and held items. Check lighting, transparency, texture atlases, and seasonal coloration where applicable.
- Use the imagegen skill to create new block and item textures. Add block artwork to `Content/Blocks/terrain.png` and item artwork to `Content/Items/items.png`.
- Never draw or synthesize block/item texture artwork procedurally in code; load it from the editable PNG atlases. When migrating existing generated artwork, copy its exact pixels instead of redesigning it.
- UI remains drawn in code using the existing UI rendering paths.
- Preserve rendering performance in dense chunks and complex terrain.

## Testing and verification

- Run `dotnet build` and focused checks for the change. Add regression coverage for substantial changes when practical. Cover relevant edge cases, especially chunk boundaries, save/load, world generation, and interactions between gameplay systems.
- Use `dotnet run -- --self-test` for routine CPU regression, `dotnet run -- --graphics-test` for graphics regression, and `dotnet run -- --infinite-graphics-test` for sustained live Infinite profiling.
- Claim completion or verification only when the relevant checks have succeeded. Report build or test failures, unresolved problems, and important limitations.

## Workflow and Git attribution

- Keep edits within this project unless explicitly authorized to work elsewhere, including in the separate Rust rewrite or any other repository.
- Leave `README.md` for the project author. Record development notes in `Summaries/CODEX-SUMMARY.md`. Summarize completed changes, verification, and outstanding issues at the end of the task.
- The project owner handles publishing and distribution manually. Do not run `dotnet publish`. Create Git commits, tags, or releases only when explicitly requested.
- For an explicitly requested Codex-assisted commit, preserve the human developer as primary author and include this trailer.

```text
Co-authored-by: Codex <267193182+codex@users.noreply.github.com>
```
