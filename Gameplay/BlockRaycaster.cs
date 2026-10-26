using System;
using Microsoft.Xna.Framework;

using MDSM.Worlds;

namespace MDSM.Gameplay;

public readonly record struct BlockHit(Point3 Cell, Point3 Normal, float Distance)
{
    public Point3 PlacementCell => Cell + Normal;
    public bool HasPlacementFace => Normal != new Point3(0, 0, 0);
}

public static class BlockRaycaster
{
    public const float Reach = 6f;
    // Amanatides-Woo grid traversal visits every voxel crossed by the ray.
    public static BlockHit? Cast(World world, Vector3 origin, Vector3 direction, float reach = Reach, bool solidOnly = false)
    {
        if (direction.LengthSquared() < 0.000001f || reach < 0)
            return null;
        direction.Normalize();

        int x = (int)MathF.Floor(origin.X);
        int y = (int)MathF.Floor(origin.Y);
        int z = (int)MathF.Floor(origin.Z);
        int sx = Math.Sign(direction.X), sy = Math.Sign(direction.Y), sz = Math.Sign(direction.Z);
        float dx = Delta(direction.X), dy = Delta(direction.Y), dz = Delta(direction.Z);
        float tx = FirstBoundary(origin.X, x, direction.X);
        float ty = FirstBoundary(origin.Y, y, direction.Y);
        float tz = FirstBoundary(origin.Z, z, direction.Z);
        float distance = 0;
        Point3 normal = default;

        while (distance <= reach)
        {
            if (solidOnly ? world.IsSolid(x, y, z) : (Blocks.IsRenderable(world.GetBlock(x, y, z)) && !Water.IsWater(world.GetBlock(x, y, z))))
                return new BlockHit(new Point3(x, y, z), normal, distance);

            if (tx <= ty && tx <= tz)
            {
                x += sx;
                distance = tx;
                tx += dx;
                normal = new Point3(-sx, 0, 0);
            }
            else if (ty <= tz)
            {
                y += sy;
                distance = ty;
                ty += dy;
                normal = new Point3(0, -sy, 0);
            }
            else
            {
                z += sz;
                distance = tz;
                tz += dz;
                normal = new Point3(0, 0, -sz);
            }
        }

        return null;
    }

    private static float Delta(float direction) => direction == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction);

    private static float FirstBoundary(float position, int cell, float direction) =>
        direction == 0 ? float.PositiveInfinity : ((direction > 0 ? cell + 1 : cell) - position) / direction;
}
