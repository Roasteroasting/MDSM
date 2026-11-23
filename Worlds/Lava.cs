using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public static class Lava
{
    public static bool IsLava(BlockType block)=>block is >=BlockType.LavaSource and <=BlockType.LavaFalling
        or >=BlockType.LavaFalling1 and <=BlockType.LavaFalling7;
    public static bool Fluid(BlockType block)=>Water.IsWater(block)||IsLava(block);
    internal static int Decay(BlockType block)=>block>=BlockType.LavaFalling1
        ? 9 + (int)block - (int)BlockType.LavaFalling1 : (int)block - (int)BlockType.LavaSource;
    public static float Height(BlockType block)=>IsLava(block)?(8-(Decay(block)>=8?0:Decay(block)))/9f:0;
    public static float CornerHeight(World world,int x,int y,int z)
        => Water.LiquidCornerHeight(world,world.OriginX+x,y,world.OriginZ+z,true);
    public static bool Intersects(World world,BoundingBox bounds)
    {
        for(int cz=Math.Max(world.LocalMinZ/16,(int)MathF.Floor(bounds.Min.Z/16));cz<=Math.Min((world.LocalMaxZ-1)/16,(int)MathF.Floor(bounds.Max.Z/16));cz++)
        for(int cx=Math.Max(world.LocalMinX/16,(int)MathF.Floor(bounds.Min.X/16));cx<=Math.Min((world.LocalMaxX-1)/16,(int)MathF.Floor(bounds.Max.X/16));cx++)
        {
            if(!world.TryGetLocalChunk(cx,cz,out var chunk)||chunk.LavaCount==0)continue;
            for(int y=Math.Max(0,(int)MathF.Floor(bounds.Min.Y));y<=Math.Min(World.Height-1,(int)bounds.Max.Y);y++)
            for(int z=Math.Max(cz*16,(int)MathF.Floor(bounds.Min.Z));z<=Math.Min(cz*16+15,(int)bounds.Max.Z);z++)
            for(int x=Math.Max(cx*16,(int)MathF.Floor(bounds.Min.X));x<=Math.Min(cx*16+15,(int)bounds.Max.X);x++)
            {
                BlockType block=world.GetBlock(x,y,z);if(!IsLava(block))continue;
                float height=(CornerHeight(world,x,y,z)+CornerHeight(world,x+1,y,z)+CornerHeight(world,x,y,z+1)+CornerHeight(world,x+1,y,z+1))*.25f;
                if(World.Overlaps(bounds,new(new Vector3(x,y,z),new Vector3(x+1,y+height,z+1))))return true;
            }
        }
        return false;
    }
}
