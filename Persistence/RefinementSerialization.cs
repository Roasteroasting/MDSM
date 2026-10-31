using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    // Version 9 appends sparse vegetation state and per-sheep state to the unchanged v8 prefix.
    private static void WriteRefinements(BinaryWriter writer,VegetationState vegetation,MobState[] mobs)
    {
        writer.Write(vegetation.NaturalLeaves.Length);foreach(int cell in vegetation.NaturalLeaves)writer.Write(cell);
        void Timers(VegetationTimer[] entries){writer.Write(entries.Length);foreach(var entry in entries){writer.Write(entry.Cell);writer.Write(entry.Remaining);}}
        Timers(vegetation.Decay);Timers(vegetation.Saplings);
        int count=0;foreach(var mob in mobs)if(mob.Sheared)count++;
        writer.Write(count);for(int i=0;i<mobs.Length;i++)if(mobs[i].Sheared){writer.Write(i);writer.Write(mobs[i].WoolSeconds);}
    }
    private static void ReadRefinements(BinaryReader reader,World world,MobState[] mobs)
    {
        // Sparse state is bounded independently of total world volume to reject hostile allocations.
        int Count(int maximum){int count=reader.ReadInt32();if(count<0 || count>maximum)throw new InvalidDataException("Invalid refinement state count.");return count;}
        int[] leaves=new int[Count(Math.Min(world.BlockCount,8_000_000))];for(int i=0;i<leaves.Length;i++)leaves[i]=reader.ReadInt32();
        VegetationTimer[] Timers(int maximum){var result=new VegetationTimer[Count(maximum)];for(int i=0;i<result.Length;i++)result[i]=new(reader.ReadInt32(),reader.ReadDouble());return result;}
        var state=new VegetationState(leaves,Timers(leaves.Length),Timers(65536));world.Vegetation.Restore(state);
        int sheared=Count(mobs.Length);var seen=new HashSet<int>();
        for(int i=0;i<sheared;i++)
        {
            int index=reader.ReadInt32();double remaining=reader.ReadDouble();
            if(index<0 || index>=mobs.Length || !seen.Add(index) || mobs[index].Species!=MobSpecies.Sheep)throw new InvalidDataException("Invalid sheared sheep identity.");
            mobs[index]=mobs[index] with {Sheared=true,WoolSeconds=remaining};PassiveMobPopulation.Validate(mobs[index],world.Size);
        }
    }
    private static void ValidateLoadedDrop(DroppedItemState drop,WorldSize size,bool legacy)
    {
        if(legacy && drop.Item==Items.RawPorkchop)
        {if(drop.Count<1 || drop.Count>64)throw new InvalidDataException("Invalid legacy food quantity.");DroppedItems.Validate(drop with {Count=1},size);}
        else DroppedItems.Validate(drop,size);
    }
    private static DroppedItemState[] MigrateFood(World world,Inventory inventory,DroppedItemState[] saved,Vector3 player)
    {
        var drops=new List<DroppedItemState>(saved.Length);
        void Add(DroppedItemState state)
        {if(drops.Count>=DroppedItems.MaximumEntities)throw new InvalidDataException("Legacy food overflow exceeds the safe dropped-item limit. Original save is unchanged.");drops.Add(state);}
        void Overflow(int quantity,Vector3 position)
        {
            position.X=Math.Clamp(position.X,DroppedItem.Radius,world.Width-DroppedItem.Radius);
            position.Z=Math.Clamp(position.Z,DroppedItem.Radius,world.Depth-DroppedItem.Radius);position.Y=Math.Clamp(position.Y,-16,World.Height+16);
            for(int i=0;i<quantity;i++)Add(new(Items.RawPorkchop,1,position,0,1));
        }
        foreach(var drop in saved)
        {if(drop.Item==Items.RawPorkchop)for(int i=0;i<drop.Count;i++)Add(drop with {Count=1});else Add(drop);}
        for(int slot=0;slot<Inventory.SlotCount;slot++)
        {
            ItemStack stack=inventory[slot];if(stack.Item!=Items.RawPorkchop || stack.Count<=1)continue;
            inventory.SetSlot(slot,new(Items.RawPorkchop,1));Overflow(inventory.Add(Items.RawPorkchop,stack.Count-1),player+Vector3.Up*.3f);
        }
        foreach(var entry in world.Containers.Entries.Values)
        for(int slot=0;slot<entry.SlotCount;slot++)
        {
            ItemStack stack=entry[slot];if(stack.Item!=Items.RawPorkchop || stack.Count<=1)continue;
            entry.SetSlot(slot,new(Items.RawPorkchop,1));int remaining=stack.Count-1;
            if(entry.Block==BlockType.Chest)for(int i=0;i<entry.SlotCount && remaining>0;i++)if(entry[i].IsEmpty){entry.SetSlot(i,new(Items.RawPorkchop,1));remaining--;}
            Overflow(remaining,new Vector3(entry.Cell.X+.5f,entry.Cell.Y+1.2f,entry.Cell.Z+.5f));
        }
        return drops.ToArray();
    }
}
