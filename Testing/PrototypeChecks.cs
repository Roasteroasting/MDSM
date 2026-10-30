using System;
using Microsoft.Xna.Framework;

using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

// Run with --self-test. These checks need no window, graphics device, or test packages.
internal static class PrototypeChecks
{
    private const int TestGroundHeight = 5;

    public static int Run()
    {
        (string Name, Action Check)[] checks =
        {
            ("Shaped crafting, quantities, offsets, strict formations and preview safety", CraftingChecks.Recipes),
            ("Atomic crafting outputs, stack limits and mixed mouse accounting", CraftingChecks.Transactions),
            ("Crafting close/overflow, blocked recovery and death item ownership", CraftingChecks.ClosingAndDeath),
            ("Crafting table placement/mining/persistence and all-size empty starts", CraftingChecks.BlocksAndPersistence),
            ("Responsive 2x2/3x3 crafting, inventory, hotbar and arrow layouts", CraftingChecks.Layout),
            ("Terrain layers, bounds, and edit revisions", CheckWorld),
            ("Compact storage and world/chunk coordinate conversion", CheckCoordinates),
            ("Seed determinism, terrain layers, and continuous hills", CheckTerrain),
            ("Only face-sharing chunk meshes are invalidated", CheckChunkRevisions),
            ("Chunk frustum culling", CheckFrustum),
            ("Massive-only notice text, responsive layout and visibility", LargeLakeChecks.Notice),
            ("Large water batches preserve distance ordering and every face", LargeLakeChecks.WaterOrdering),
            ("Large/Massive multi-seed measured lakes, shores, counts and determinism", LargeLakeChecks.Generation),
            ("All-size bounded residency, frustum coverage, edges and hysteresis", RenderingPerformanceChecks.Regions),
            ("Cached mob collision matches fresh queries, edits, knockback and water", RenderingPerformanceChecks.CollisionCache),
            ("Collision and raycasting across chunk seams", CheckChunkGameplay),
            ("Horizontal player bounds and vertical world limits", CheckBoundaries),
            ("Camera direction and pitch limits", CheckCamera),
            ("Atlas rows, texel centers, orientation, and index bounds", CheckAtlas),
            ("Exposed-face mesh, winding, atlas coordinates, and edit rebuilds", CheckMesh),
            ("Raycast faces, distance, boundaries, and misses", CheckRaycast),
            ("Safe placement and block breaking", CheckPlacement),
            ("Ground contact, jump, gravity, and landing", CheckJump),
            ("Wall collision, sliding, and diagonal speed", CheckMovement),
            ("Ceiling collision and fast falling", CheckVerticalCollision),
            ("Falling off the world and safe respawn", CheckRespawn),
            ("Edited world and player pose serialization round-trip", PersistenceChecks.RoundTrip),
            ("Save checksums, headers, dimensions, and corrupt data", PersistenceChecks.CorruptData),
            ("Multiple world directories, safe names, and unreadable saves", PersistenceChecks.WorldDirectories),
            ("Atomic saving and preservation after write/replace failures", PersistenceChecks.AtomicSaving),
            ("Stable expanded block definitions and starter palette order", DecorationChecks.Definitions),
            ("Deterministic trees/plants with unchanged base terrain", DecorationChecks.Determinism),
            ("Tree boundary validation and cross-chunk canopies", DecorationChecks.TreeBoundaries),
            ("Leaf occlusion, cutout vertex alpha, and plant quads", DecorationChecks.Meshes),
            ("New block placement, vegetation targeting, and support", DecorationChecks.Interaction),
            ("Legacy v1 compatibility and expanded block restoration", DecorationChecks.Persistence),
            ("All world sizes, indexing, boundaries, seams, and frustum bounds", WorldManagementChecks.SizesAndBoundaries),
            ("All sizes: deterministic decorations, spawn, and single bedrock layer", WorldManagementChecks.Generation),
            ("Maximum 5x5 canopies with height and shape variation", WorldManagementChecks.Canopies),
            ("All sizes: complete save restoration and header-only metadata", WorldManagementChecks.PersistenceAndMetadata),
            ("Safe rename/delete, active-world guards, and filesystem failures", WorldManagementChecks.Management),
            ("Starter inventory, compatible stacks, overflow, and 64-item limits", InventoryChecks.Stacking),
            ("Left/right inventory clicks, odd splitting, merging, and swapping", InventoryChecks.MouseOperations),
            ("5000 cursor operations conserve items and blocked closing is safe", InventoryChecks.AccountingAndClosing),
            ("Timed/instant mining, resets, continuous mining, bedrock, and drops", InventoryChecks.Mining),
            ("Item gravity, terrain collision, pickup, and full-inventory remainders", InventoryChecks.PhysicsAndPickup),
            ("Inventory-based placement and independent sessions across all sizes", InventoryChecks.PlacementAndIsolation),
            ("All-size inventory/drop persistence and one-time legacy inventory", InventoryChecks.Persistence),
            ("Invalid inventory/drop data is rejected safely", InventoryChecks.Validation),
            ("Health bounds and full/half/empty heart HUD mapping", SurvivalChecks.HealthAndHearts),
            ("Vertical fall distance, thresholds, horizontal and velocity independence", SurvivalChecks.FallDistances),
            ("Fall apex tracking, jumps, and independent landings", SurvivalChecks.JumpAndLanding),
            ("Death stack accounting, idempotence, respawn, and recovery", SurvivalChecks.DeathAndRespawn),
            ("Atomic manual drops, clear positions, and elapsed pickup delay", SurvivalChecks.ManualDropAndDelay),
            ("Spatial item merging, overflow, unrelated types, and delay preservation", SurvivalChecks.Merging),
            ("All-size health persistence, dead load, and v1/v2 compatibility", SurvivalChecks.HealthPersistence),
            ("Atomic death saving, failure preservation, rename, and reload", SurvivalChecks.ManagedDeathSave),
            ("Crouch speed/camera and unchanged one-block jump height", SurvivalChecks.CrouchMovement),
            ("Crouch edge/corner support, jumping off, removal, and wall sliding", SurvivalChecks.CrouchEdges),
            ("Wheel wrapping, multi/partial steps, reset, and inventory preservation", SurvivalChecks.Scrolling),
            ("FPS defaults, integer slider range, uncapped override, and layouts", PresentationChecks.SettingsValues),
            ("Application settings restart, invalid files, and atomic write failures", PresentationChecks.SettingsPersistence),
            ("Render-independent physics/mining/item clocks, jumps, mouse look, and crouch camera", PresentationChecks.SimulationRates),
            ("Empty-slot suppression, selected cube/plant geometry, menu visibility, and HUD clearance", PresentationChecks.ViewModels),
            ("Stationary and moving relative mouse input at 60-2000 updates/second", MouseInputChecks.Motion),
            ("Mouse capture/release flushes transition motion without delaying real input", MouseInputChecks.CaptureTransitions),
            ("F1/F3 independence and F2 press edges at high update rates", InterfaceChecks.PresentationKeys),
            ("Screenshot directory, PNG bytes, unique names, and write failures", InterfaceChecks.ScreenshotFiles),
            ("Accurate Controls/About information and responsive menu layouts", InterfaceChecks.InformationAndLayouts),
            ("Seeded pig populations, suitable terrain, finite sizes, and no regeneration", PigChecks.Spawning),
            ("Pig gravity/collisions, wandering, health/knockback, and render-rate independence", PigChecks.MovementAndDamage),
            ("Pig targeting/occlusion, punching cooldown, death drops, and continuous mining", PigChecks.Combat),
            ("Porkchop accounting, RMB edges/healing, new-world stock, death/respawn, and pickup", PigChecks.Food),
            ("Pig/food persistence across all sizes, corruption, and legacy world policy", PigChecks.Persistence),
            ("Cuboid pig models, cutout food geometry, hurt tint, and animation", PigChecks.Geometry),
            ("Water byte states, non-solid/non-item rules, targeting and displacement", WaterChecks.StatesAndPlacement),
            ("Downward priority, finite horizontal reach, and obstacle/corner flow", WaterChecks.Propagation),
            ("Source regeneration, removal, remaining supply, and complete flow recession", WaterChecks.RecessionAndRegeneration),
            ("Fluid boundaries, chunk invalidation, clocks, and bounded scheduling", WaterChecks.BoundariesAndScheduling),
            ("Water surface levels, shared corners, transparent faces, and terrain culling", WaterChecks.Geometry),
            ("Player swimming/fall resistance and passive pig water collisions", WaterChecks.Swimming),
            ("Deterministic all-size lakes, restrained basins, beaches, spawn and stability", WaterChecks.Lakes),
            ("Extreme seeds, sealed lakes, digging shore/bottom, and solid displacement", WaterChecks.LakeEditsAndSeeds),
            ("All-size active water round trips, resumed flow, and saved terrain preservation", WaterChecks.Persistence),
            ("All species health, passive AI, collisions, knockback, and frame independence", MobChecks.DefinitionsAndMotion),
            ("Shared all-size spawn budgets, balanced species, grass/water safety, and no replenishment", MobChecks.Spawning),
            ("Species targeting, terrain occlusion, cooldown, exact drops, and mining fallback", MobChecks.CombatAndDrops),
            ("Leather/feather stacks and pickup, non-food resources, and wool placement/mining/UVs", MobChecks.ResourcesAndWool),
            ("Empty survival starts, preserved legacy inventory and empty respawn", MobChecks.StartingAndRespawn),
            ("All-size mixed mobs/resources/water persistence and saved kills", MobChecks.Persistence),
            ("Independent v4 pig/food migration and invalid species/health/count rejection", MobChecks.BackwardCompatibilityAndValidation),
            ("Species cuboid proportions, cutout UVs, animation, and hurt tint", MobChecks.Geometry),
            ("Species targets, independent 30-60 second delays, caps, and single replacements", WorldUpdateChecks.TargetsAndTimers),
            ("Bounded respawn retries, recovery, player/entity/water clearance", WorldUpdateChecks.RetryAndClearance),
            ("All-size target and outstanding timer save/load without duplicate mobs", WorldUpdateChecks.PopulationPersistence),
            ("Conservative v4/v5 population migration and corrupt scheduling data", WorldUpdateChecks.LegacyPopulationAndCorruption),
            ("Sand/gravel support removal, stacks, chunk edges, and finite bottom", WorldUpdateChecks.GravityStacksAndBoundaries),
            ("Gravity through water, queued-save restoration, and unchanged legacy terrain", WorldUpdateChecks.GravityWaterAndPersistence),
            ("Supported natural shores/beds and bounded gravity column processing", WorldUpdateChecks.GenerationAndBudgets),
            ("Render-independent respawns/gravity and corrected up/down hotbar direction", WorldUpdateChecks.TimingAndScroll),
            ("Continuous falling acceleration, interpolation, full cube geometry, and idle terrain meshes", FallingBlockChecks.ContinuousMotion),
            ("Swept low-FPS falling collision, terminal speed, and ordered active stacks", FallingBlockChecks.SweepsAndStacks),
            ("Identical falls at 30/60/144/360 FPS and safe placement around falling cubes", FallingBlockChecks.FrameRatesAndPlacement),
            ("All-size mid-fall persistence, exact resumption, v6 queues, and corrupt active states", FallingBlockChecks.Persistence),
            ("Bounded active falls, deferred stacks, save/load, and exact material accounting", FallingBlockChecks.CapacityAndAccounting),
            ("Four expanded dimensions, high-Y indexing, terrain limits and smooth gradients", WorldExpansionChecks.DimensionsAndTerrain),
            ("Legacy 64-high Normal/Large migration for save versions 1-7", WorldExpansionChecks.Migration),
            ("All four complete generated worlds, water features, and streamed restoration", WorldExpansionChecks.GenerationAndStreaming)
        };

        int failures = 0;
        foreach (var check in checks)
        {
            try
            {
                check.Check();
                Console.WriteLine($"PASS: {check.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {check.Name}: {exception.Message}");
            }
        }
        Console.WriteLine($"{checks.Length - failures}/{checks.Length} prototype checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckWorld()
    {
        var world = FlatWorld();
        for (int x = 0; x < world.Width; x++)
        for (int z = 0; z < world.Depth; z++)
        {
            Require(world.GetBlock(x, 4, z) == BlockType.Grass, "Grass surface missing.");
            Require(world.GetBlock(x, 3, z) == BlockType.Dirt && world.GetBlock(x, 2, z) == BlockType.Dirt, "Dirt layers missing.");
            Require(world.GetBlock(x, 1, z) == BlockType.Stone && world.GetBlock(x, 0, z) == BlockType.Stone, "Stone layers missing.");
            Require(world.GetBlock(x, 5, z) == BlockType.Air, "Space above terrain must be air.");
        }
        Require(world.GetBlock(-1, 4, 0) == BlockType.Air, "Outside the world must be empty.");
        Require(!world.SetBlock(world.Width, 5, 0, BlockType.Stone), "Out-of-bounds edit accepted.");
        Require(!world.SetBlock(0, 5, 0, (BlockType)99), "Invalid block accepted.");
        Require(world.SetBlock(0, 5, 0, BlockType.Stone) && world.Revision == 1, "Edit revision not updated.");
        Require(!world.SetBlock(0, 5, 0, BlockType.Stone) && world.Revision == 1, "Unchanged edit modified revision.");
    }

    private static void CheckCamera()
    {
        var camera = new Camera();
        Require(Vector3.Distance(camera.HorizontalForward, Vector3.Forward) < 0.001f, "Initial forward direction incorrect.");
        camera.Look(200, 0);
        Require(camera.Forward.X > 0 && camera.Right.Z > 0, "Mouse right must turn right.");
        camera.Look(0, -100000);
        Require(camera.Forward.Y > 0.99f && camera.HorizontalForward.Y == 0, "Pitch limit or flat walking direction incorrect.");
        camera.Look(0, 100000);
        Require(camera.Forward.Y < -0.99f, "Downward pitch limit incorrect.");
        Require(MathF.Abs(Vector3.Dot(camera.HorizontalForward, camera.Right)) < 0.001f, "Walking basis is not orthogonal.");
    }

    private static void CheckRaycast()
    {
        var world = FlatWorld();
        BlockHit? top = BlockRaycaster.Cast(world, new Vector3(10.5f, 8, 10.5f), new Vector3(0, -4, 0));
        Require(top.HasValue && top.Value.Cell == new Point3(10, 4, 10), "Vertical ray missed grass.");
        Require(top.Value.Normal == new Point3(0, 1, 0) && Near(top.Value.Distance, 3), "Surface normal or normalized distance incorrect.");
        Require(BlockRaycaster.Cast(world, new Vector3(10.5f, 8, 10.5f), Vector3.Down, 2.99f) == null, "Ray exceeded reach.");
        Require(BlockRaycaster.Cast(world, new Vector3(10.5f, 8, 10.5f), Vector3.Down, 3f).HasValue, "Exact reach must hit.");
        Require(BlockRaycaster.Cast(world, new Vector3(10, 8, 10), Vector3.Up) == null, "Sky ray should miss.");
        Require(BlockRaycaster.Cast(world, Vector3.Zero, Vector3.Zero) == null, "Zero direction should miss.");

        world.SetBlock(12, 8, 12, BlockType.Stone);
        Vector3 center = new(12.5f, 8.5f, 12.5f);
        Vector3[] directions = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Forward, Vector3.Backward };
        foreach (Vector3 outward in directions)
        {
            BlockHit? hit = BlockRaycaster.Cast(world, center + outward * 2, -outward);
            Require(hit.HasValue && hit.Value.Cell == new Point3(12, 8, 12), "Axis-aligned face ray missed.");
            Require(hit.Value.Normal == new Point3((int)outward.X, (int)outward.Y, (int)outward.Z), "Incorrect placement face.");
            Require(Near(hit.Value.Distance, 1.5f), "Incorrect face distance.");
        }

        BlockHit? boundary = BlockRaycaster.Cast(world, new Vector3(13, 8.5f, 12.5f), Vector3.Left);
        Require(boundary.HasValue && boundary.Value.Cell.X == 12 && Near(boundary.Value.Distance, 0), "Negative ray on a cell boundary failed.");
        BlockHit? inside = BlockRaycaster.Cast(world, center, Vector3.Right);
        Require(inside.HasValue && !inside.Value.HasPlacementFace && inside.Value.Distance == 0, "Ray starting inside a block must not invent a placement face.");
        BlockHit? diagonal = BlockRaycaster.Cast(world, new Vector3(10.5f, 10.5f, 10.5f), new Vector3(2, -2, 2));
        Require(diagonal.HasValue && diagonal.Value.Cell == new Point3(12, 8, 12), "Diagonal grid traversal missed block.");
        BlockHit? outside = BlockRaycaster.Cast(world, new Vector3(-2, 4.5f, 10.5f), Vector3.Right);
        Require(outside.HasValue && outside.Value.Cell.X == 0 && Near(outside.Value.Distance, 2), "Ray entering world failed.");
    }

    private static void CheckAtlas()
    {
        Require(TextureAtlas.TileCount == 256 && TextureAtlas.AtlasSize == 256, "Atlas must provide 256 slots in a 256x256 image.");
        Require(TextureAtlas.Source(0) == new Rectangle(0, 0, 16, 16), "First tile is not at the upper left.");
        Require(TextureAtlas.Source(3) == new Rectangle(48, 0, 16, 16), "Existing stone tile moved.");
        Require(TextureAtlas.Source(15) == new Rectangle(240, 0, 16, 16), "Last tile in first row is incorrect.");
        Require(TextureAtlas.Source(16) == new Rectangle(0, 16, 16, 16), "Tile indices do not wrap to the next row.");
        Require(TextureAtlas.Source(17) == new Rectangle(16, 16, 16, 16), "Second row column is incorrect.");
        Require(TextureAtlas.Source(255) == new Rectangle(240, 240, 16, 16), "Last tile is incorrect.");

        Require(Vector2.Distance(TextureAtlas.Uv(0, 0, 0), new Vector2(0.5f / 256, 0.5f / 256)) < 0.000001f, "First texel center is incorrect.");
        Require(Vector2.Distance(TextureAtlas.Uv(17, 1, 0), new Vector2(31.5f / 256, 16.5f / 256)) < 0.000001f, "Local upper-right UV orientation changed.");
        Require(Vector2.Distance(TextureAtlas.Uv(17, 0, 1), new Vector2(16.5f / 256, 31.5f / 256)) < 0.000001f, "Local lower-left UV orientation changed.");
        Require(Vector2.Distance(TextureAtlas.Uv(255, 1, 1), new Vector2(255.5f / 256)) < 0.000001f, "Last texel center is incorrect.");

        foreach (int invalid in new[] { -1, 256 })
        {
            bool rejected = false;
            try { TextureAtlas.Source(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Out-of-range tile index accepted.");
        }
    }

    private static void CheckMesh()
    {
        var world = new World(0, WorldSize.Small, false);
        var builder = new ChunkMeshBuilder();
        world.SetBlock(15, 10, 8, BlockType.Grass);
        builder.Build(world, world.GetChunk(0, 0));
        Require(builder.VertexCount == 36, "Isolated block must expose six faces.");
        Vector3 center = new(15.5f, 10.5f, 8.5f);
        for (int i = 0; i < builder.VertexCount; i += 3)
        {
            var vertices = builder.Vertices;
            Vector3 inward = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position);
            Require(Vector3.Dot(inward, vertices[i].Position - center) < 0, "Triangle winding is inconsistent with backface culling.");
            Rectangle source = TextureAtlas.Source(Blocks.Tile(BlockType.Grass, -inward));
            for (int j = i; j < i + 3; j++)
            {
                Vector2 pixel = vertices[j].TextureCoordinate * TextureAtlas.AtlasSize;
                Require(pixel.X >= source.Left + 0.5f && pixel.X <= source.Right - 0.5f
                    && pixel.Y >= source.Top + 0.5f && pixel.Y <= source.Bottom - 0.5f, "Face uses the wrong atlas tile.");
            }
        }

        world.SetBlock(16, 10, 8, BlockType.Stone);
        builder.Build(world, world.GetChunk(0, 0));
        Require(builder.VertexCount == 30, "X seam emitted an internal face.");
        builder.Build(world, world.GetChunk(1, 0));
        Require(builder.VertexCount == 30, "Opposite side of X seam emitted an internal face.");
        world.SetBlock(15, 10, 8, BlockType.Air);
        builder.Build(world, world.GetChunk(1, 0));
        Require(builder.VertexCount == 36, "Boundary edit failed to expose neighboring block.");
        builder.Build(world, world.GetChunk(0, 0));
        Require(builder.VertexCount == 0, "Empty chunk retained geometry.");

        world.SetBlock(8, 10, 15, BlockType.Stone);
        world.SetBlock(8, 10, 16, BlockType.Stone);
        builder.Build(world, world.GetChunk(0, 0));
        Require(builder.VertexCount == 30, "Z seam emitted an internal face.");
        builder.Build(world, world.GetChunk(0, 1));
        Require(builder.VertexCount == 30, "Opposite side of Z seam emitted an internal face.");
        world.SetBlock(255, 63, 255, BlockType.Stone);
        builder.Build(world, world.GetChunk(15, 15));
        Require(builder.VertexCount == 36, "Finite outer/top boundaries must expose faces.");
        world.SetBlock(0, 0, 0, BlockType.Stone);
        builder.Build(world, world.GetChunk(0, 0));
        Require(builder.VertexCount == 66, "Finite lower boundaries must expose faces.");
    }

    private static void CheckPlacement()
    {
        var world = FlatWorld();
        var player = new Player(world.Spawn);
        Point3 feet = new((int)player.Position.X, (int)player.Position.Y, (int)player.Position.Z);
        Require(!world.TryPlace(feet, BlockType.Stone, player.Bounds), "Placement inside player accepted.");
        Require(!world.TryPlace(feet + new Point3(0, 1, 0), BlockType.Stone, player.Bounds), "Placement inside player head accepted.");
        Require(!world.TryPlace(new Point3(-1, 5, 1), BlockType.Stone, player.Bounds), "Outside placement accepted.");
        Require(!world.TryPlace(new Point3(1, 4, 1), BlockType.Stone, player.Bounds), "Occupied cell overwritten.");
        Point3 adjacent = feet + new Point3(1, 0, 0);
        Require(world.TryPlace(adjacent, BlockType.Dirt, player.Bounds), "Valid adjacent placement rejected.");
        Require(world.SetBlock(adjacent.X, adjacent.Y, adjacent.Z, BlockType.Air), "Breaking failed.");
        Require(!world.IsSolid(adjacent.X, adjacent.Y, adjacent.Z), "Broken block still collides.");
        // A block may touch the feet without overlapping the player's body.
        world.SetBlock(feet.X, feet.Y - 1, feet.Z, BlockType.Air);
        Require(world.TryPlace(feet + new Point3(0, -1, 0), BlockType.Grass, player.Bounds), "Placement touching feet rejected.");
    }

    private static void CheckJump()
    {
        var world = FlatWorld();
        var player = new Player(world.Spawn);
        Step(player, world, 10);
        Require(player.Grounded && Near(player.Position.Y, TestGroundHeight), "Player did not settle on grass.");
        player.Update(world, Vector3.Zero, true, 1f / 60);
        Require(!player.Grounded && player.Position.Y > TestGroundHeight, "Jump did not leave the ground.");
        float peak = player.Position.Y;
        for (int i = 0; i < 100; i++)
        {
            player.Update(world, Vector3.Zero, false, 1f / 60);
            peak = MathF.Max(peak, player.Position.Y);
            Require(!world.IntersectsSolid(player.Bounds), "Player penetrated terrain during jump.");
        }
        Require(peak > TestGroundHeight + 1 && peak < TestGroundHeight + 1.5f, "Unexpected jump height.");
        Require(player.Grounded && Near(player.Position.Y, TestGroundHeight), "Player did not land.");
        // Removing the supporting block must immediately allow a fall.
        world.SetBlock((int)player.Position.X, 4, (int)player.Position.Z, BlockType.Air);
        Step(player, world, 45);
        Require(player.Grounded && Near(player.Position.Y, 4), "Player did not fall into the excavated cell.");
    }

    private static void CheckMovement()
    {
        var world = FlatWorld();
        for (int z = 5; z <= 25; z++)
        for (int y = 5; y < 9; y++)
            world.SetBlock(12, y, z, BlockType.Stone);
        var player = new Player(new Vector3(10.5f, 5, 10.5f));
        for (int i = 0; i < 60; i++)
            player.Update(world, new Vector3(1, 0, 1), false, 1f / 60);
        Require(Near(player.Position.X, 12 - Player.HalfWidth) && player.Position.Z > 13.5f, "Wall collision or sliding failed.");
        Require(!world.IntersectsSolid(player.Bounds), "Player penetrated wall.");

        var straight = new Player(new Vector3(20.5f, 5, 10.5f));
        var diagonal = new Player(straight.Position);
        Vector3 start = straight.Position;
        for (int i = 0; i < 30; i++)
        {
            straight.Update(world, Vector3.Right, false, 1f / 60);
            diagonal.Update(world, new Vector3(1, 0, 1), false, 1f / 60);
        }
        Require(Near(Vector3.Distance(straight.Position, start), Vector3.Distance(diagonal.Position, start)), "Diagonal movement is faster than straight movement.");
    }

    private static void CheckVerticalCollision()
    {
        var world = FlatWorld();
        var player = new Player(new Vector3(10.5f, 5, 10.5f));
        world.SetBlock(10, 7, 10, BlockType.Stone);
        Step(player, world, 1);
        player.Update(world, Vector3.Zero, true, 1f / 60);
        for (int i = 0; i < 60; i++)
        {
            player.Update(world, Vector3.Zero, false, 1f / 60);
            Require(player.Bounds.Max.Y <= 7.001f && !world.IntersectsSolid(player.Bounds), "Player jumped through ceiling.");
        }
        Require(player.Grounded, "Player failed to settle after ceiling impact.");

        var falling = new Player(new Vector3(20.5f, 40, 20.5f));
        for (int i = 0; i < 50; i++)
        {
            falling.Update(world, Vector3.Zero, false, 0.1f);
            Require(!world.IntersectsSolid(falling.Bounds), "Fast fall tunneled into terrain.");
        }
        Require(falling.IsDead && Near(falling.Position.Y, 5) && !world.IntersectsSolid(falling.Bounds), "Fatal fast fall failed to land safely.");
    }

    private static void CheckRespawn()
    {
        var world = FlatWorld();
        Vector3 spawn = world.Spawn;
        world.SetBlock((int)spawn.X, (int)spawn.Y, (int)spawn.Z, BlockType.Stone);
        var player = new Player(new Vector3(spawn.X, -20, spawn.Z));
        Step(player, world, 150);
        Require(Near(player.Position.X, spawn.X) && Near(player.Position.Z, spawn.Z), "Void fall did not respawn.");
        Require(player.Grounded && Near(player.Position.Y, 6) && !world.IntersectsSolid(player.Bounds), "Respawn did not find safe space above edited terrain.");
    }

    private static void Step(Player player, World world, int count)
    {
        for (int i = 0; i < count; i++)
            player.Update(world, Vector3.Zero, false, 1f / 60);
    }

    private static void CheckCoordinates()
    {
        var world = new World(0, WorldSize.Small, false);
        Require(world.Width == 256 && World.Height == 128 && world.Depth == 256, "World dimensions changed.");
        Require(world.Chunks.Length == 256, "Expected exactly 256 chunks.");
        int storedBytes = 0;
        foreach (Chunk chunk in world.Chunks)
        {
            storedBytes += chunk.Blocks.Length;
            Require(ReferenceEquals(world.GetChunk(chunk.X, chunk.Z), chunk), "Chunk lookup order is inconsistent.");
            foreach (int local in new[] { 0, 15 })
            foreach (int y in new[] { 0, 63 })
            {
                int x = chunk.OriginX + local, z = chunk.OriginZ + local;
                Require(world.SetBlock(x, y, z, BlockType.Dirt), "Valid extreme coordinate rejected.");
                Require(world.GetBlock(x, y, z) == BlockType.Dirt && chunk.GetBlock(local, y, local) == BlockType.Dirt,
                    "World-to-local coordinate conversion failed.");
            }
        }
        Require(storedBytes == 8 * 1024 * 1024 && storedBytes == world.BlockCount, "Raw storage must be exactly 8 MiB.");
        Require(world.GetChunk(15, 15).Bounds.Max == new Vector3(256, 128, 256), "Last chunk bounds are incorrect.");
        Point3[] invalid = { new(-1, 0, 0), new(256, 0, 0), new(0, -1, 0), new(0, 128, 0),
            new(0, 0, -1), new(0, 0, 256), new(int.MinValue, 0, 0), new(0, int.MaxValue, 0) };
        int revision = world.Revision;
        foreach (Point3 cell in invalid)
        {
            Require(world.GetBlock(cell.X, cell.Y, cell.Z) == BlockType.Air, "Out-of-bounds read must return air.");
            Require(!world.SetBlock(cell.X, cell.Y, cell.Z, BlockType.Stone), "Out-of-bounds write succeeded.");
            Require(!world.TryPlace(cell, BlockType.Stone, new BoundingBox()), "Out-of-bounds placement succeeded.");
        }
        Require(world.Revision == revision, "Invalid writes dirtied the world.");
    }

    private static void CheckTerrain()
    {
        var world = BaseTerrain(173);
        var same = BaseTerrain(173);
        var other = BaseTerrain(174);
        bool seedChangedTerrain = false;
        for (int i = 0; i < world.ChunkCount; i++)
        {
            Require(world.Chunks[i].Blocks.SequenceEqual(same.Chunks[i].Blocks), "Identical seeds produced different terrain.");
            seedChangedTerrain |= !world.Chunks[i].Blocks.SequenceEqual(other.Chunks[i].Blocks);
        }
        Require(seedChangedTerrain, "Changing seed had no effect.");

        var generator = new TerrainGenerator(173);
        int min = 128, max = 0;
        var heights = new int[world.Width * world.Depth];
        for (int z = 0; z < world.Depth; z++)
        for (int x = 0; x < world.Width; x++)
        {
            int top = world.SurfaceY(x, z), dirt = generator.DirtDepth(x, z);
            Require(top == generator.SurfaceY(x, z) && top >= 63 && top <= 114, "Surface height is unsafe or differs from generator.");
            Require(dirt >= 3 && dirt <= 5, "Dirt thickness must be 3-5 blocks.");
            for (int y = 0; y < World.Height; y++)
            {
                BlockType expected = y == 0 ? BlockType.Bedrock : y > top ? BlockType.Air : y == top ? BlockType.Grass
                    : y >= top - dirt ? BlockType.Dirt : BlockType.Stone;
                Require(world.GetBlock(x, y, z) == expected, "Generated column has incorrect layers or holes.");
            }
            heights[z * world.Width + x] = top;
            if (x > 0) Require(Math.Abs(top - heights[z * world.Width + x - 1]) <= 2, "Discontinuity along X, including chunk seams.");
            if (z > 0) Require(Math.Abs(top - heights[(z - 1) * world.Width + x]) <= 2, "Discontinuity along Z, including chunk seams.");
            min = Math.Min(min, top);
            max = Math.Max(max, top);
        }
        Require(max - min >= 4, "Terrain is effectively flat.");
        var player = new Player(world.Spawn);
        Require(!world.IntersectsSolid(player.Bounds) && player.Bounds.Max.Y < World.Height, "Generated spawn is obstructed or too high.");
        Step(player, world, 90);
        Require(player.Grounded && !world.IntersectsSolid(player.Bounds), "Player failed to land at generated spawn.");
        Console.WriteLine($"  Seed 173 surface range: Y {min}-{max}; raw storage {world.BlockCount} bytes.");
    }

    private static void CheckChunkRevisions()
    {
        var world = new World(0, WorldSize.Small, false);
        CheckEdit(world, new Point3(8, 10, 8), (0, 0));
        CheckEdit(world, new Point3(15, 10, 8), (0, 0), (1, 0));
        CheckEdit(world, new Point3(16, 10, 8), (1, 0), (0, 0));
        CheckEdit(world, new Point3(8, 10, 15), (0, 0), (0, 1));
        CheckEdit(world, new Point3(16, 10, 16), (1, 1), (0, 1), (1, 0));
        CheckEdit(world, new Point3(255, 63, 255), (15, 15));
        CheckEdit(world, new Point3(0, 0, 0), (0, 0));
    }

    private static void CheckEdit(World world, Point3 cell, params (int X, int Z)[] expected)
    {
        var before = new int[world.ChunkCount];
        for (int i = 0; i < before.Length; i++)
            before[i] = world.Chunks[i].Revision;
        Require(world.SetBlock(cell.X, cell.Y, cell.Z, BlockType.Stone), "Test edit failed.");
        for (int i = 0; i < before.Length; i++)
        {
            Chunk chunk = world.Chunks[i];
            bool dirty = false;
            foreach (var target in expected)
                dirty |= chunk.X == target.X && chunk.Z == target.Z;
            Require(chunk.Revision == before[i] + (dirty ? 1 : 0), "Edit dirtied an unrelated chunk or missed a face-sharing neighbor.");
        }
        int revision = world.Revision;
        Require(!world.SetBlock(cell.X, cell.Y, cell.Z, BlockType.Stone) && world.Revision == revision, "Unchanged edit dirtied meshes.");
    }

    private static void CheckFrustum()
    {
        var world = new World(0, WorldSize.Small, false);
        var camera = new Camera { Position = new Vector3(128.5f, 40, 128.5f) };
        var frustum = new BoundingFrustum(camera.View * camera.Projection(16f / 9));
        Require(frustum.Intersects(world.GetChunk(8, 4).Bounds), "Forward chunk was culled.");
        Require(!frustum.Intersects(world.GetChunk(8, 15).Bounds), "Chunk behind camera was not culled.");
        Require(!frustum.Intersects(new BoundingBox(new Vector3(128, 0, -500), new Vector3(144, 64, -484))), "Chunk beyond far plane was not culled.");
    }

    private static void CheckChunkGameplay()
    {
        var world = FlatWorld();
        world.SetBlock(16, 8, 16, BlockType.Stone);
        BlockHit? hit = BlockRaycaster.Cast(world, new Vector3(14.5f, 8.5f, 16.5f), Vector3.Right);
        Require(hit.HasValue && hit.Value.Cell == new Point3(16, 8, 16) && hit.Value.PlacementCell == new Point3(15, 8, 16), "Raycast or placement face failed across seam.");
        var player = new Player(new Vector3(14.5f, 5, 10.5f));
        for (int y = 5; y < 9; y++)
            world.SetBlock(16, y, 10, BlockType.Stone);
        for (int i = 0; i < 60; i++)
            player.Update(world, Vector3.Right, false, 1f / 60);
        Require(Near(player.Position.X, 16 - Player.HalfWidth) && !world.IntersectsSolid(player.Bounds), "Cross-chunk wall collision failed.");
        for (int y = 5; y < 9; y++)
            world.SetBlock(16, y, 10, BlockType.Air);
        for (int i = 0; i < 30; i++)
            player.Update(world, Vector3.Right, false, 1f / 60);
        Require(player.Position.X > 17 && !world.IntersectsSolid(player.Bounds), "Player failed to cross edited chunk seam.");
    }

    private static void CheckBoundaries()
    {
        var world = FlatWorld();
        (Vector3 Start, Vector3 Direction)[] edges =
        {
            (new(0.5f, 5, 10.5f), Vector3.Left),
            (new(255.5f, 5, 10.5f), Vector3.Right),
            (new(10.5f, 5, 0.5f), Vector3.Forward),
            (new(10.5f, 5, 255.5f), Vector3.Backward)
        };
        foreach (var edge in edges)
        {
            var player = new Player(edge.Start);
            for (int i = 0; i < 120; i++)
                player.Update(world, edge.Direction, false, 1f / 60);
            Require(player.Bounds.Min.X >= -0.0001f && player.Bounds.Min.Z >= -0.0001f
                && player.Bounds.Max.X <= world.Width + 0.0001f && player.Bounds.Max.Z <= world.Depth + 0.0001f,
                "Player crossed a horizontal world boundary.");
        }
        Require(world.SetBlock(10, 127, 10, BlockType.Stone), "Top valid block rejected.");
        Require(!world.SetBlock(10, 128, 10, BlockType.Stone) && !world.SetBlock(10, -1, 10, BlockType.Air), "Invalid vertical edit accepted.");
        Require(BlockRaycaster.Cast(world, new Vector3(10.5f, 130, 10.5f), Vector3.Down).Value.Cell.Y == 127, "Ray from above world failed.");
        var below = new Player(new Vector3(10.5f, -20, 10.5f));
        Step(below, world, 1);
        Require(below.Position.Y >= 5 && !world.IntersectsSolid(below.Bounds), "Bottom-boundary respawn failed.");
    }

    private static World FlatWorld()
    {
        var world = new World(0, WorldSize.Small, false);
        foreach (Chunk chunk in world.Chunks)
        for (int y = 0; y < TestGroundHeight; y++)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
            chunk.SetBlock(x, y, z, y == TestGroundHeight - 1 ? BlockType.Grass
                : y >= TestGroundHeight - 3 ? BlockType.Dirt : BlockType.Stone);
        return world;
    }

    private static World BaseTerrain(int seed)
    {
        var world = new World(seed, WorldSize.Small, false);
        new TerrainGenerator(seed).Generate(world);
        return world;
    }

    private static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 0.001f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
