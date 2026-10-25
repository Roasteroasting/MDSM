using System;
using Microsoft.Xna.Framework;

namespace MDSM;

// Run with --self-test. These checks need no window, graphics device, or test packages.
internal static class PrototypeChecks
{
    public static int Run()
    {
        (string Name, Action Check)[] checks =
        {
            ("Terrain layers, bounds, and edit revisions", CheckWorld),
            ("Camera direction and pitch limits", CheckCamera),
            ("Atlas rows, texel centers, orientation, and index bounds", CheckAtlas),
            ("Exposed-face mesh, winding, atlas coordinates, and edit rebuilds", CheckMesh),
            ("Raycast faces, distance, boundaries, and misses", CheckRaycast),
            ("Safe placement and block breaking", CheckPlacement),
            ("Ground contact, jump, gravity, and landing", CheckJump),
            ("Wall collision, sliding, and diagonal speed", CheckMovement),
            ("Ceiling collision and fast falling", CheckVerticalCollision),
            ("Falling off the world and safe respawn", CheckRespawn)
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
        var world = new World();
        for (int x = 0; x < World.Width; x++)
        for (int z = 0; z < World.Depth; z++)
        {
            Require(world.GetBlock(x, 4, z) == BlockType.Grass, "Grass surface missing.");
            Require(world.GetBlock(x, 3, z) == BlockType.Dirt && world.GetBlock(x, 2, z) == BlockType.Dirt, "Dirt layers missing.");
            Require(world.GetBlock(x, 1, z) == BlockType.Stone && world.GetBlock(x, 0, z) == BlockType.Stone, "Stone layers missing.");
            Require(world.GetBlock(x, 5, z) == BlockType.Air, "Space above terrain must be air.");
        }
        Require(world.GetBlock(-1, 4, 0) == BlockType.Air, "Outside the world must be empty.");
        Require(!world.SetBlock(World.Width, 5, 0, BlockType.Stone), "Out-of-bounds edit accepted.");
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
        var world = new World();
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
        var world = new World();
        var vertices = WorldRenderer.BuildMesh(world);
        int expectedFaces = 2 * (World.Width * World.Depth + World.Width * World.GroundHeight + World.Depth * World.GroundHeight);
        Require(vertices.Length == expectedFaces * 6, "Hidden block faces were emitted or exterior faces are missing.");
        Vector3 center = new(World.Width / 2f, World.GroundHeight / 2f, World.Depth / 2f);
        for (int i = 0; i < vertices.Length; i += 3)
        {
            Vector3 normal = Vector3.Cross(vertices[i + 1].Position - vertices[i].Position, vertices[i + 2].Position - vertices[i].Position);
            // XNA's clockwise front faces have a geometric cross product pointing inward.
            Require(Vector3.Dot(normal, vertices[i].Position - center) < 0, "Triangle winding is inconsistent with backface culling.");
        }
        foreach (var vertex in vertices)
            Require(vertex.TextureCoordinate.X > 0 && vertex.TextureCoordinate.X < 1
                && vertex.TextureCoordinate.Y > 0 && vertex.TextureCoordinate.Y < 1, "Atlas coordinate touches or exceeds texture boundary.");

        world.SetBlock(10, 4, 10, BlockType.Air);
        Require(WorldRenderer.BuildMesh(world).Length == vertices.Length + 24, "Breaking surface block did not expose its neighbors.");
        world.SetBlock(10, 8, 10, BlockType.Stone);
        Require(WorldRenderer.BuildMesh(world).Length == vertices.Length + 24 + 36, "Isolated placed block must expose six faces.");
        Require(Blocks.Tile(BlockType.Grass, Vector3.Up) == 0 && Blocks.Tile(BlockType.Grass, Vector3.Right) == 1
            && Blocks.Tile(BlockType.Grass, Vector3.Down) == 2, "Grass face textures are incorrect.");
    }

    private static void CheckPlacement()
    {
        var world = new World();
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
        var world = new World();
        var player = new Player(world.Spawn);
        Step(player, world, 10);
        Require(player.Grounded && Near(player.Position.Y, World.GroundHeight), "Player did not settle on grass.");
        player.Update(world, Vector3.Zero, true, 1f / 60);
        Require(!player.Grounded && player.Position.Y > World.GroundHeight, "Jump did not leave the ground.");
        float peak = player.Position.Y;
        for (int i = 0; i < 100; i++)
        {
            player.Update(world, Vector3.Zero, false, 1f / 60);
            peak = MathF.Max(peak, player.Position.Y);
            Require(!world.IntersectsSolid(player.Bounds), "Player penetrated terrain during jump.");
        }
        Require(peak > World.GroundHeight + 1 && peak < World.GroundHeight + 1.5f, "Unexpected jump height.");
        Require(player.Grounded && Near(player.Position.Y, World.GroundHeight), "Player did not land.");
        // Removing the supporting block must immediately allow a fall.
        world.SetBlock((int)player.Position.X, 4, (int)player.Position.Z, BlockType.Air);
        Step(player, world, 45);
        Require(player.Grounded && Near(player.Position.Y, 4), "Player did not fall into the excavated cell.");
    }

    private static void CheckMovement()
    {
        var world = new World();
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
        var world = new World();
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
        Require(falling.Grounded && Near(falling.Position.Y, 5), "Fast fall failed to land.");
    }

    private static void CheckRespawn()
    {
        var world = new World();
        Vector3 spawn = world.Spawn;
        world.SetBlock((int)spawn.X, (int)spawn.Y, (int)spawn.Z, BlockType.Stone);
        var player = new Player(new Vector3(-3, 5, -3));
        Step(player, world, 150);
        Require(Near(player.Position.X, spawn.X) && Near(player.Position.Z, spawn.Z), "Void fall did not respawn.");
        Require(player.Grounded && Near(player.Position.Y, 6) && !world.IntersectsSolid(player.Bounds), "Respawn did not find safe space above edited terrain.");
    }

    private static void Step(Player player, World world, int count)
    {
        for (int i = 0; i < count; i++)
            player.Update(world, Vector3.Zero, false, 1f / 60);
    }

    private static bool Near(float actual, float expected) => MathF.Abs(actual - expected) < 0.001f;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
