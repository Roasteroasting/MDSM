using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    private static void WriteGuid(BinaryWriter writer,Guid id){Span<byte> bytes=stackalloc byte[16];id.TryWriteBytes(bytes);writer.Write(bytes);}
    private static Guid ReadGuid(BinaryReader reader){Span<byte> bytes=stackalloc byte[16];reader.BaseStream.ReadExactly(bytes);return new Guid(bytes);}
    internal static void WriteStack(BinaryWriter writer,ItemStack stack)
    {writer.Write(stack.Item.Value);writer.Write((byte)stack.Count);writer.Write(stack.Durability);WriteGuid(writer,stack.InstanceId);}
    internal static ItemStack ReadStack(BinaryReader reader)
    {
        ItemId item=new(reader.ReadUInt16());int count=reader.ReadByte(),durability=reader.ReadInt32();Guid id=ReadGuid(reader);
        if(item.Value==0 && count==0 && durability==0 && id==Guid.Empty)return default;
        int maximum=Items.Get(item).Durability;
        if(maximum>0 ? id==Guid.Empty || durability<1 || durability>maximum : durability!=0 || id!=Guid.Empty)
            throw new InvalidDataException("Invalid saved equipment identity/durability.");
        try{return new(item,count,durability,id);}catch(ArgumentException e){throw new InvalidDataException("Invalid saved stack metadata.",e);}
    }
    private static int EquipmentCount(Inventory inventory,DroppedItemState[] drops)
    {int count=0;for(int i=0;i<36;i++)if(inventory[i].Durability>0)count++;foreach(var drop in drops)if(Items.Get(drop.Item).Durability>0)count++;return count;}
    private static void ValidateOwnership(Inventory inventory,DroppedItemState[] drops,BlockEntityState[] containers)
    {
        var identities=new HashSet<Guid>();
        void Check(ItemStack stack)
        {if(stack.Durability>0 && (stack.InstanceId==Guid.Empty || !identities.Add(stack.InstanceId)))throw new InvalidDataException("Duplicate/missing equipment instance identity.");}
        for(int i=0;i<36;i++)Check(inventory[i]);foreach(var drop in drops)Check(drop.Stack);
        foreach(var container in containers)foreach(var stack in container.Slots)Check(stack);
    }
    private static void WriteSurvival(BinaryWriter writer,WorldSave save,Inventory inventory,DroppedItemState[] drops,BlockEntityState[] states)
    {
        writer.Write(EquipmentCount(inventory,drops));
        for(int i=0;i<36;i++)if(inventory[i].Durability>0)WriteEquipment(0,i,inventory[i]);
        for(int i=0;i<drops.Length;i++)if(drops[i].Durability>0)WriteEquipment(1,i,drops[i].Stack);
        void WriteEquipment(byte owner,int index,ItemStack stack){writer.Write(owner);writer.Write(index);writer.Write(stack.Durability);WriteGuid(writer,stack.InstanceId);}
        writer.Write(states.Length);
        foreach(var state in states)
        {
            writer.Write(state.Cell.X);writer.Write(state.Cell.Y);writer.Write(state.Cell.Z);writer.Write((byte)state.Block);writer.Write(state.Slots.Length);
            foreach(var stack in state.Slots)WriteStack(writer,stack);
            writer.Write(state.Progress);writer.Write(state.Burn);writer.Write(state.BurnTotal);
        }
        ArrowState[] arrows=save.Arrows??Array.Empty<ArrowState>();if(arrows.Length>Projectiles.Maximum)throw new InvalidDataException("Too many arrows.");
        writer.Write(arrows.Length);
        foreach(var arrow in arrows)
        {
            Projectiles.Validate(arrow,save.World);
            writer.Write(arrow.Position.X);writer.Write(arrow.Position.Y);writer.Write(arrow.Position.Z);
            writer.Write(arrow.Velocity.X);writer.Write(arrow.Velocity.Y);writer.Write(arrow.Velocity.Z);
            writer.Write(arrow.Age);writer.Write(arrow.Damage);WriteGuid(writer,arrow.Bow);
        }
    }
    private static ArrowState[] ReadSurvival(BinaryReader reader,World world,Inventory inventory,DroppedItemState[] drops)
    {
        int count=reader.ReadInt32();if(count<0 || count>36+drops.Length || count!=EquipmentCount(inventory,drops))throw new InvalidDataException("Invalid equipment metadata count.");
        var locations=new HashSet<long>();
        for(int i=0;i<count;i++)
        {
            byte owner=reader.ReadByte();int index=reader.ReadInt32(),durability=reader.ReadInt32();Guid id=ReadGuid(reader);
            if(owner>1 || index<0 || index>=(owner==0?36:drops.Length) || !locations.Add(((long)owner<<32)|(uint)index) || id==Guid.Empty)
                throw new InvalidDataException("Invalid equipment metadata location.");
            ItemId type=owner==0?inventory[index].Item:drops[index].Item;
            int maximum=Items.Get(type).Durability;
            if(maximum==0 || durability<1 || durability>maximum)throw new InvalidDataException("Metadata has invalid equipment durability.");
            ItemStack stack;
            try{stack=new(type,1,durability,id);}catch(ArgumentException e){throw new InvalidDataException("Invalid equipment durability.",e);}
            if(owner==0)inventory.SetSlot(index,stack);else drops[index]=drops[index] with {Durability=durability,InstanceId=id};
        }
        foreach(var drop in drops)DroppedItems.Validate(drop,world.Size);
        int containers=reader.ReadInt32();if(containers<0 || containers>BlockEntities.Maximum)throw new InvalidDataException("Invalid container count.");
        var states=new BlockEntityState[containers];
        for(int i=0;i<containers;i++)
        {
            Point3 cell=new(reader.ReadInt32(),reader.ReadInt32(),reader.ReadInt32());BlockType block=(BlockType)reader.ReadByte();int slots=reader.ReadInt32();
            if(!BlockEntities.HasState(block) || slots!=(block==BlockType.Chest?27:3))throw new InvalidDataException("Invalid container layout.");
            var items=new ItemStack[slots];for(int j=0;j<slots;j++)items[j]=ReadStack(reader);
            states[i]=new(cell,block,items,reader.ReadDouble(),reader.ReadDouble(),reader.ReadDouble());
        }
        world.Containers.Restore(states);ValidateOwnership(inventory,drops,states);
        int arrowCount=reader.ReadInt32();if(arrowCount<0 || arrowCount>Projectiles.Maximum)throw new InvalidDataException("Invalid arrow count.");
        var arrows=new ArrowState[arrowCount];
        for(int i=0;i<arrowCount;i++)
        {
            arrows[i]=new(new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),reader.ReadSingle(),reader.ReadInt32(),ReadGuid(reader));
            Projectiles.Validate(arrows[i],world);
        }
        return arrows;
    }
    private sealed class HashedReadStream(Stream source,IncrementalHash hash):Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(Span<byte> buffer){int count=source.Read(buffer);hash.AppendData(buffer[..count]);return count;}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
