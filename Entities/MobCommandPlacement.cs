using System;
using Microsoft.Xna.Framework;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Entities;

// Explicit summons use local, bounded searches, not natural spawn light/distance rules.
internal static class MobCommandPlacement
{
    public static bool TryFind(GameSession session, MobSpecies species, Random random, out Vector3 feet)
    {
        World world = session.World;
        Vector3 player = session.Player.Position;
        for (int attempt = 0; attempt < 128; attempt++)
        {
            float angle = random.NextSingle() * MathHelper.TwoPi, radius = 3 + random.NextSingle() * 9;
            int x = (int)MathF.Floor(player.X + MathF.Cos(angle) * radius);
            int z = (int)MathF.Floor(player.Z + MathF.Sin(angle) * radius);
            if (x < 2 || z < 2 || x >= world.Width - 2 || z >= world.Depth - 2) continue;
            int preferred = Math.Clamp((int)MathF.Floor(player.Y), 1, World.Height - 1);
            for (int offset = 0; offset <= 16; offset++)
            for (int side = 0; side < (offset == 0 ? 1 : 2); side++)
            {
                int y = preferred + (side == 0 ? -offset : offset);
                if (y < 1 || y + MobDefinitions.Get(species).Height > World.Height || !world.IsSolid(x, y - 1, z)) continue;
                Vector3 candidate = new(x + .5f, y, z + .5f);
                BoundingBox bounds = PassiveMob.BoundsAt(species, candidate);
                if (world.IntersectsSolid(bounds) || !world.IntersectsSolid(new(bounds.Min - Vector3.Up * .05f, bounds.Max - Vector3.Up * .05f))
                    || Water.Intersects(world, bounds) || Lava.Intersects(world, bounds) || World.Overlaps(bounds, session.Player.Bounds)) continue;
                bool occupied = false;
                foreach (PassiveMob mob in session.Mobs.Entities) if (World.Overlaps(bounds, mob.Bounds)) { occupied = true; break; }
                if (!occupied) foreach (PassiveMob mob in session.Hostiles.Entities) if (World.Overlaps(bounds, mob.Bounds)) { occupied = true; break; }
                if (!occupied) { feet = candidate; return true; }
            }
        }
        feet = default;
        return false;
    }
}
