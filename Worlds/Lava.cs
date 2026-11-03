using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public static class Lava
{
    public static bool IsLava(BlockType block)=>block is >=BlockType.LavaSource and <=BlockType.LavaFalling;
    public static bool Fluid(BlockType block)=>Water.IsWater(block)||IsLava(block);
    public static float Height(BlockType block)=>block==BlockType.LavaFalling?1:IsLava(block)?(8-(int)block+(int)BlockType.LavaSource)/9f:0;
    public static float CornerHeight(World world,int x,int y,int z)
    {
        float sum=0;int weights=0;
        for(int pz=z-1;pz<=z;pz++)for(int px=x-1;px<=x;px++)
        {
            BlockType block=world.GetBlock(px,y,pz);if(!IsLava(block))continue;
            if(IsLava(world.GetBlock(px,y+1,pz)))return 1;
            int weight=block==BlockType.LavaSource?4:1;sum+=Height(block)*weight;weights+=weight;
        }
        return weights==0?0:sum/weights;
    }
    public static bool Intersects(World world,BoundingBox bounds)
    {
        for(int cz=Math.Max(0,(int)bounds.Min.Z/16);cz<=Math.Min(world.ChunksZ-1,(int)bounds.Max.Z/16);cz++)
        for(int cx=Math.Max(0,(int)bounds.Min.X/16);cx<=Math.Min(world.ChunksX-1,(int)bounds.Max.X/16);cx++)
        {
            if(world.GetChunk(cx,cz).LavaCount==0)continue;
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
