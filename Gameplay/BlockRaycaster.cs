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
    public static BlockHit? Cast(World world, Vector3 origin, Vector3 direction, float reach = Reach, bool solidOnly = false,bool includeFluids=false)
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
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];var ray=new Ray(origin,direction);

        while (distance <= reach)
        {
            BlockType block=world.GetBlock(x,y,z);
            if (solidOnly ? world.IsSolid(x, y, z) : (Blocks.IsRenderable(block) && (includeFluids || !Lava.Fluid(block))))
            {
                Point3 cell=new(x,y,z);int count=world.BlockBoxes(cell,boxes);float closest=float.MaxValue;BoundingBox chosen=default;
                for(int i=0;i<count;i++){float? hit=ray.Intersects(boxes[i]);if(hit.HasValue && hit.Value>=distance-.0001f && hit.Value<closest){closest=hit.Value;chosen=boxes[i];}}
                if(closest<=reach)
                {
                    Vector3 p=origin+direction*closest;Point3 face=normal;
                    if(closest>distance+.0001f)
                        face=Math.Abs(p.X-chosen.Min.X)<.001f?new(-1,0,0):Math.Abs(p.X-chosen.Max.X)<.001f?new(1,0,0):Math.Abs(p.Y-chosen.Min.Y)<.001f?new(0,-1,0):Math.Abs(p.Y-chosen.Max.Y)<.001f?new(0,1,0):Math.Abs(p.Z-chosen.Min.Z)<.001f?new(0,0,-1):new(0,0,1);
                    return new BlockHit(cell,face,closest);
                }
            }

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
