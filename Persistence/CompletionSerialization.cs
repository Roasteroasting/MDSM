using System;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    private static void WritePoint(BinaryWriter writer,Point3 p){writer.Write(p.X);writer.Write(p.Y);writer.Write(p.Z);}
    private static Point3 ReadPoint(BinaryReader reader)=>new(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());
    private static void WriteCompletion(BinaryWriter writer,WorldSave save,Inventory inventory,PigState[] pigs,MobState[] others)
    {
        double exact=save.ExactHealth<0?save.Health:save.ExactHealth;
        if(!double.IsFinite(exact)||exact<0||exact>20||Math.Ceiling(exact)!=save.Health)throw new InvalidDataException("Invalid precise health.");writer.Write(exact);
        for(int i=0;i<4;i++)WriteStack(writer,inventory.Equipped(i));
        ValidateStructures(save.World);writer.Write(save.World.Structures.Count);
        var structures=new StructureState[save.World.Structures.Count];save.World.Structures.Values.CopyTo(structures,0);
        Array.Sort(structures,(a,b)=>a.Cell.Y!=b.Cell.Y?a.Cell.Y.CompareTo(b.Cell.Y):a.Cell.Z!=b.Cell.Z?a.Cell.Z.CompareTo(b.Cell.Z):a.Cell.X.CompareTo(b.Cell.X));
        foreach(var state in structures){WritePoint(writer,state.Cell);writer.Write(state.Facing);writer.Write(state.Open);WritePoint(writer,state.Support);}
        CropState[] crops=save.World.Farming.Capture();int cropBlocks=0;foreach(var chunk in save.World.Chunks)cropBlocks+=chunk.CropCount;if(cropBlocks!=crops.Length)throw new InvalidDataException("Missing crop timers.");writer.Write(crops.Length);
        foreach(var state in crops){WritePoint(writer,state.Cell);writer.Write(state.Remaining);writer.Write(state.StageSeconds);}
        Point3[] hydration=save.World.Farming.CaptureHydration();if(hydration.Length>1000000)throw new InvalidDataException("Too many pending hydration updates.");writer.Write(hydration.Length);foreach(Point3 p in hydration)WritePoint(writer,p);
        writer.Write(pigs.Length+others.Length);
        void Life(MobState state){writer.Write(state.BabySeconds);writer.Write(state.LoveSeconds);writer.Write(state.BreedCooldown);writer.Write(state.Bred);}
        foreach(var pig in pigs)Life(pig.ToMobState());foreach(var mob in others)Life(mob);
        MobState[] hostiles=save.Hostiles??Array.Empty<MobState>();if(hostiles.Length>HostilePopulation.Maximum)throw new InvalidDataException("Too many hostiles.");writer.Write(hostiles.Length);
        foreach(var state in hostiles)
        {
            HostilePopulation.Validate(state,save.World);writer.Write((byte)state.Species);writer.Write(state.Position.X);writer.Write(state.Position.Y);writer.Write(state.Position.Z);writer.Write(state.Yaw);writer.Write(state.Health);
        }
        ArrowState[] arrows=save.Arrows??Array.Empty<ArrowState>();int count=0;foreach(var arrow in arrows)if(arrow.Hostile)count++;writer.Write(count);
        for(int i=0;i<arrows.Length;i++)if(arrows[i].Hostile)writer.Write(i);
    }
    private static void ReadCompletion(BinaryReader reader,World world,Inventory inventory,PigState[] pigs,MobState[] others,ArrowState[] arrows,int health,out double exact,out MobState[] hostiles)
    {
        int Count(int maximum){int count=reader.ReadInt32();if(count<0||count>maximum)throw new InvalidDataException("Invalid completion state count.");return count;}
        exact=reader.ReadDouble();if(!double.IsFinite(exact)||exact<0||exact>20||Math.Ceiling(exact)!=health)throw new InvalidDataException("Invalid precise health.");
        for(int i=0;i<4;i++){var stack=ReadStack(reader);try{inventory.SetEquipment(i,stack);}catch(ArgumentException e){throw new InvalidDataException("Invalid armor slot.",e);}}
        int structureCount=Count(65536);
        for(int i=0;i<structureCount;i++)
        {
            Point3 p=ReadPoint(reader);byte facing=reader.ReadByte();bool open=reader.ReadBoolean();Point3 support=ReadPoint(reader);
            if(facing>(world.GetBlock(p.X,p.Y,p.Z)==BlockType.Door?7:3)||!world.Contains(p.X,p.Y,p.Z)||Math.Abs((long)support.X)+Math.Abs((long)support.Y)+Math.Abs((long)support.Z)!=1
                ||world.GetBlock(p.X,p.Y,p.Z) is not (BlockType.Torch or BlockType.Door or BlockType.FenceGate)||!world.Structures.TryAdd(p,new(p,facing,open,support)))throw new InvalidDataException("Invalid structure state.");
            if(world.GetBlock(p.X,p.Y,p.Z)==BlockType.Torch)world.Torches.Add(p);
        }
        ValidateStructures(world);
        var crops=new CropState[Count(FarmingSimulation.MaximumCrops)];for(int i=0;i<crops.Length;i++)crops[i]=new(ReadPoint(reader),reader.ReadDouble(),reader.ReadDouble());world.Farming.Restore(crops);
        int cropBlocks=0;foreach(var chunk in world.Chunks)cropBlocks+=chunk.CropCount;if(cropBlocks!=crops.Length)throw new InvalidDataException("Missing crop timers.");
        var hydration=new Point3[Count(1000000)];for(int i=0;i<hydration.Length;i++)hydration[i]=ReadPoint(reader);world.Farming.RestoreHydration(hydration);
        int animals=Count(PassiveMobPopulation.MaximumMobs);if(animals!=pigs.Length+others.Length)throw new InvalidDataException("Animal life count mismatch.");
        for(int i=0;i<animals;i++)
        {
            double baby=reader.ReadDouble(),love=reader.ReadDouble(),cooldown=reader.ReadDouble();bool bred=reader.ReadBoolean();
            if(i<pigs.Length){pigs[i]=pigs[i] with {BabySeconds=baby,LoveSeconds=love,BreedCooldown=cooldown,Bred=bred};PassiveMobPopulation.Validate(pigs[i].ToMobState(),world.Size);}
            else {int index=i-pigs.Length;others[index]=others[index] with {BabySeconds=baby,LoveSeconds=love,BreedCooldown=cooldown,Bred=bred};PassiveMobPopulation.Validate(others[index],world.Size);}
        }
        hostiles=new MobState[Count(HostilePopulation.Maximum)];for(int i=0;i<hostiles.Length;i++){hostiles[i]=new((MobSpecies)reader.ReadByte(),new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),reader.ReadSingle(),reader.ReadInt32());HostilePopulation.Validate(hostiles[i],world);}
        int flags=Count(arrows.Length);var seen=new System.Collections.Generic.HashSet<int>();for(int i=0;i<flags;i++){int index=reader.ReadInt32();if(index<0||index>=arrows.Length||!seen.Add(index))throw new InvalidDataException("Invalid hostile projectile.");arrows[index]=arrows[index] with {Hostile=true};}
    }
    private static void ValidateStructures(World world)
    {
        if(world.Structures.Count>65536)throw new InvalidDataException("Too many structures.");int expected=world.Structures.Count;
        foreach(var state in world.Structures.Values)
        {
            Point3 p=state.Cell;BlockType block=world.GetBlock(p.X,p.Y,p.Z);
            if(state.Facing>(block==BlockType.Door?7:3)||Math.Abs((long)state.Support.X)+Math.Abs((long)state.Support.Y)+Math.Abs((long)state.Support.Z)!=1)throw new InvalidDataException("Invalid structure orientation/support.");
            if(block==BlockType.Door){expected++;if(world.GetBlock(p.X,p.Y+1,p.Z)!=BlockType.DoorUpper)throw new InvalidDataException("Orphaned door.");}
            else if(block is not (BlockType.FenceGate or BlockType.Torch))throw new InvalidDataException("Structure/block mismatch.");
        }
        int actual=0;foreach(var chunk in world.Chunks)actual+=chunk.StructureCount;if(actual!=expected)throw new InvalidDataException("Missing structure metadata or orphaned door halves.");
    }
}
