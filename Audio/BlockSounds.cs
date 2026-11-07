using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Audio;

public static class BlockSounds
{
    public static SoundMaterial Material(BlockType block)=>block switch
    {
        BlockType.Grass=>SoundMaterial.Grass,
        BlockType.Dirt or BlockType.FarmlandDry or BlockType.FarmlandWet=>SoundMaterial.Dirt,
        BlockType.Sand=>SoundMaterial.Sand,BlockType.Gravel=>SoundMaterial.Gravel,
        BlockType.Glass or BlockType.GlassPane=>SoundMaterial.Glass,
        BlockType.IronOre=>SoundMaterial.Metal,BlockType.WhiteWool=>SoundMaterial.Wool,
        BlockType.WoodLog or BlockType.WoodenPlanks or BlockType.CraftingTable or BlockType.Chest or BlockType.Door or BlockType.DoorUpper or BlockType.Fence or BlockType.FenceGate or BlockType.Torch=>SoundMaterial.Wood,
        BlockType.Leaves=>SoundMaterial.Leaves,
        _ when Blocks.Get(block).NeedsGrass=>SoundMaterial.Plant,
        _=>SoundMaterial.Stone
    };
    public static Vector3 Center(Point3 cell)=>new(cell.X+.5f,cell.Y+.5f,cell.Z+.5f);
    public static BlockType Surface(World world,Vector3 feet)
    {
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(int y=(int)MathF.Floor(feet.Y-.01f);y>=Math.Max(0,(int)MathF.Floor(feet.Y)-2);y--)
        for(int z=(int)MathF.Floor(feet.Z-.25f);z<=(int)MathF.Floor(feet.Z+.25f);z++)
        for(int x=(int)MathF.Floor(feet.X-.25f);x<=(int)MathF.Floor(feet.X+.25f);x++)
        {
            Point3 cell=new(x,y,z);int count=world.BlockBoxes(cell,boxes,true);
            for(int i=0;i<count;i++)if(Math.Abs(boxes[i].Max.Y-feet.Y)<.08f
                && boxes[i].Min.X<feet.X+.25f && boxes[i].Max.X>feet.X-.25f
                && boxes[i].Min.Z<feet.Z+.25f && boxes[i].Max.Z>feet.Z-.25f)return world.GetBlock(x,y,z);
        }
        return BlockType.Stone;
    }
}
