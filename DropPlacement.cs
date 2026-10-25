using System;
using Microsoft.Xna.Framework;

namespace MDSM;

public static class DropPlacement
{
    // Prefer the viewing direction, then nearby sides and overhead clearance.
    public static bool TryFind(World world, Player player, Vector3 forward, bool avoidPlayer, out Vector3 position)
    {
        forward.Y = 0;
        if (forward.LengthSquared() < 0.001f) forward = Vector3.Forward;
        else forward.Normalize();
        Vector3 right = new(-forward.Z, 0, forward.X);
        for (int level = 0; level < 4; level++)
        for (int choice = 0; choice < 9; choice++)
        {
            Vector3 offset = choice switch
            {
                0 => forward, 1 => right, 2 => -right, 3 => -forward,
                4 => (forward + right) * 0.7f, 5 => (forward - right) * 0.7f,
                6 => (-forward + right) * 0.7f, 7 => (-forward - right) * 0.7f, _ => Vector3.Zero
            };
            Vector3 candidate = player.Position + offset * 1.1f + new Vector3(0, 0.6f + level * 0.5f, 0);
            candidate.X = Math.Clamp(candidate.X, DroppedItem.Radius, world.Width - DroppedItem.Radius);
            candidate.Z = Math.Clamp(candidate.Z, DroppedItem.Radius, world.Depth - DroppedItem.Radius);
            candidate.Y = Math.Clamp(candidate.Y, -16 + DroppedItem.Radius, World.Height + 16);
            var bounds = new BoundingBox(candidate - new Vector3(DroppedItem.Radius), candidate + new Vector3(DroppedItem.Radius));
            if (world.IntersectsSolid(bounds) || (avoidPlayer && World.Overlaps(bounds, player.Bounds))) continue;
            position = candidate;
            return true;
        }
        position = default;
        return false;
    }
}
