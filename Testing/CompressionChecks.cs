using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CompressionChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("V11 compressed state/codec roundtrips, header-only listings and nonseekable input",RoundTrips),
        ("Compressed checksums, truncation, record schemas/order, limits and optional extensions",Corruption),
        ("Legacy v10 migration, compressed atomic replacement and interrupted-save safety",MigrationAndAtomic),
        ("All four sizes: streamed lossless compressed block/state restoration",AllSizes)
    };
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static WorldSave Fixture(WorldSize size=WorldSize.Small,bool terrain=false)
    {
        var world=new World(173,size,false);if(terrain)new TerrainGenerator(173).Generate(world);
        for(int z=10;z<45;z++)for(int x=10;x<45;x++)world.SetGeneratedBlock(x,0,z,BlockType.Grass);
        world.SetBlock(15,127,16,BlockType.GlassPane);world.SetBlock(world.Width-1,127,world.Depth-1,BlockType.Cobblestone);
        world.SetBlock(20,1,20,BlockType.WaterSource);world.SetBlock(21,1,20,Water.Flow(3));world.SetBlock(24,1,20,BlockType.LavaSource);
        world.SetBlock(25,5,25,BlockType.Gravel);world.Gravity.Update(.1);world.SetGeneratedBlock(28,6,28,BlockType.Leaves);
        world.SetBlock(28,5,28,BlockType.WoodLog);world.SetBlock(28,5,28,BlockType.Air);
        world.SetBlock(30,1,30,BlockType.Sapling);world.SetBlock(31,0,30,BlockType.FarmlandWet);world.SetBlock(31,1,30,BlockType.Wheat4);
        world.PlaceStructure(new(33,1,30),BlockType.Door,1,new(0,-1,0),new(new(40,1,40),new(41,3,41)));world.Toggle(new(33,1,30));
        world.SetBlock(34,1,30,BlockType.Smoker);var furnace=world.Containers.Get(new(34,1,30));furnace.SetSlot(0,new(Items.RawPorkchop,1));furnace.SetSlot(1,new(Items.Coal,1));world.Containers.Update(1);
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.Bow,1,123));inventory.SetSlot(17,new(Items.Coal,47));inventory.SelectedSlot=7;
        inventory.SetEquipment(0,new(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet),1));
        return new(world,"Compression",new(40,3,40),.2f,-.1f,inventory,
            new[]{new DroppedItemState(Items.Feather,17,new(35,3,35),-1,.7f)},17,
            new[]{new PigState(new(36,1,36),.2f,7)},new[]{new MobState(MobSpecies.Sheep,new(38,1,38),1,8,true,92)},
            new(new[]{2,0,1,0},new[]{new MobRespawnState(MobSpecies.Pig,37)}),
            new[]{new ArrowState(new(40,4,40),Vector3.Forward*10,.3f,2,inventory[0].InstanceId)},
            new[]{new MobState(MobSpecies.Zombie,new(43,1,43),-.2f,13)},16.7);
    }
    private static byte[] Encode(WorldSave save,SaveCodec codec=SaveCodec.Zstandard)
    {using var stream=new MemoryStream();WorldSerializer.WriteCompressed(stream,save,codec);return stream.ToArray();}
    internal static byte[] RawHash(WorldSave save)
    {using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var sink=new HashStream(hash);WorldSerializer.WriteLegacy(sink,save);return hash.GetHashAndReset();}
    public static void RoundTrips()
    {
        var save=Fixture();byte[] expected=RawHash(save);
        foreach(SaveCodec codec in Enum.GetValues<SaveCodec>())
        {
            byte[] bytes=Encode(save,codec);using var metadata=new MemoryStream(bytes);var info=WorldSerializer.ReadMetadata(metadata);
            Check(info.Version==12 && info.Size==WorldSize.Small && metadata.Position<256,"Listing decompressed data or lost metadata.");
            Check(codec==SaveCodec.Stored || bytes.Length<save.World.BlockCount/10,"Save did not compress.");
            foreach(bool pipe in new[]{false,true})
            {
                using Stream input=pipe?new Pipe(bytes):new MemoryStream(bytes);WorldSave loaded=WorldSerializer.Read(input);
                Check(expected.AsSpan().SequenceEqual(RawHash(loaded)),"Compressed blocks or gameplay state changed.");
                Check(bytes.AsSpan().SequenceEqual(Encode(loaded,codec)),"Compressed serialization is not deterministic.");
                using Stream listing=pipe?new Pipe(bytes):new MemoryStream(bytes);Check(WorldSerializer.ReadMetadata(listing).Seed==173,"Pipe metadata failed.");
            }
        }
        // Ownership of caller streams is unchanged.
        using var stream=new MemoryStream(Encode(save));WorldSerializer.Read(stream);Check(stream.CanRead,"Loading closed the caller stream.");
    }
    private static int HeaderEnd(byte[] data)=>52+BitConverter.ToInt32(data,24)+48;
    private static int Next(byte[] data,int record)=>record+56+BitConverter.ToInt32(data,record+20);
    private static void Sign(byte[] data)=>SHA256.HashData(data.AsSpan(0,data.Length-32)).CopyTo(data.AsSpan(data.Length-32));
    private static void Reject(byte[] data)
    {try{WorldSerializer.Read(new MemoryStream(data));}catch(InvalidDataException){return;}throw new InvalidOperationException("Corrupt compressed file accepted.");}
    public static void Corruption()
    {
        byte[] original=Encode(Fixture());int first=HeaderEnd(original),blocks=Next(original,first);
        bool rejected=false;try{WorldSerializer.ReadMetadata(new MemoryStream(original[..first]));}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Header-only listing accepted a file missing all payload records.");
        foreach(int length in new[]{0,4,8,27,first-1,first+20,blocks+57,original.Length-1,original.Length-32})Reject(original[..length]);
        void Bad(int offset,int value,bool sign=true)
        {byte[] data=(byte[])original.Clone();BitConverter.GetBytes(value).CopyTo(data,offset);if(sign)Sign(data);Reject(data);}
        Bad(4,99);Bad(24,int.MaxValue);Bad(8,128);Bad(12,64);Bad(first-48,99); // schema
        Bad(blocks+16,int.MaxValue);Bad(blocks+20,int.MaxValue);Bad(blocks+12,32);Bad(first,2);Bad(blocks,0);Bad(blocks+4,2);
        byte[] changed=(byte[])original.Clone();changed[blocks+8]=255;Sign(changed);Reject(changed);
        changed=(byte[])original.Clone();changed[blocks+56]^=16;Sign(changed);Reject(changed); // compressed payload, outer checksum repaired
        changed=(byte[])original.Clone();changed[blocks+24]^=1;Sign(changed);Reject(changed); // record digest
        changed=(byte[])original.Clone();changed[^1]^=1;Reject(changed);Reject(original.Concat(new byte[]{0}).ToArray());
        // Explicitly ignorable extensions do not enter the reconstructed v10 stream.
        byte[] optional=new byte[56+8];BitConverter.GetBytes(0x100u).CopyTo(optional,0);optional[4]=1;optional[16]=8;optional[20]=8;
        BitConverter.GetBytes(12345L).CopyTo(optional,56);SHA256.HashData(optional.AsSpan(56)).CopyTo(optional.AsSpan(24));
        changed=new byte[original.Length+optional.Length];original.AsSpan(0,first).CopyTo(changed);optional.CopyTo(changed,first);original.AsSpan(first).CopyTo(changed.AsSpan(first+optional.Length));Sign(changed);
        Check(RawHash(WorldSerializer.Read(new MemoryStream(changed))).AsSpan().SequenceEqual(RawHash(WorldSerializer.Read(new MemoryStream(original)))),"Optional extension corrupted current state.");
        changed[first+6]=1;Sign(changed);Reject(changed); // future required data must never be silently discarded
        byte[] wrongInner=(byte[])original.Clone();wrongInner[first+56+20]^=1;SHA256.HashData(wrongInner.AsSpan(first+56,BitConverter.ToInt32(wrongInner,first+16))).CopyTo(wrongInner.AsSpan(first+24));Sign(wrongInner);Reject(wrongInner);
    }
    public static void MigrationAndAtomic()
    {
        var save=Fixture();using var legacy=new MemoryStream();WorldSerializer.WriteLegacy(legacy,save);legacy.Position=0;
        Check(WorldSerializer.ReadMetadata(legacy).Version==10,"Legacy fixture is not v10.");legacy.Position=0;var migrated=WorldSerializer.Read(legacy);
        Check(RawHash(migrated).AsSpan().SequenceEqual(RawHash(save)),"Legacy data changed on compression migration.");
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".compression-atomic-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new WorldSaveManager(root);string id=store.Create(save),path=Path.Combine(root,id,WorldSaveManager.FileName);
            File.WriteAllBytes(path,legacy.ToArray());store.Save(id,store.Load(id));
            using(var listing=File.OpenRead(path))Check(WorldSerializer.ReadMetadata(listing).Version==12,"Subsequent save did not migrate storage.");
            byte[] previous=File.ReadAllBytes(path);save.World.GetChunk(0,0).SetBlock(0,0,0,(BlockType)255);
            bool failed=false;try{store.Save(id,save);}catch(InvalidDataException){failed=true;}
            Check(failed && previous.AsSpan().SequenceEqual(File.ReadAllBytes(path)),"Failed compressed write replaced the valid save.");
            save.World.GetChunk(0,0).SetBlock(0,0,0,BlockType.Air);
            if(OperatingSystem.IsWindows())
            {using var locked=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read);failed=false;try{store.Save(id,save);}catch(IOException){failed=true;}Check(failed && previous.AsSpan().SequenceEqual(File.ReadAllBytes(path)),"Failed replacement damaged the previous compressed save.");}
            Check(Directory.GetFiles(Path.GetDirectoryName(path),"*.tmp").Length==0,"Failed compression left temp files.");
            File.WriteAllBytes(path+".interrupted.tmp",previous[..100]);Check(RawHash(store.Load(id)).AsSpan().SequenceEqual(RawHash(migrated)),"Interrupted temp file replaced valid world.");
            store.Rename(id,"Renamed compressed");Check(store.Load(id).Name=="Renamed compressed" && store.ListWorlds().Single().Size==WorldSize.Small,"Compressed rename/list failed.");
        }
        finally{if(Directory.Exists(root)){Check(Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent),"Unsafe cleanup.");Directory.Delete(root,true);}}
    }
    public static void AllSizes()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,".compression-roundtrip-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            foreach(WorldSize size in Enum.GetValues<WorldSize>())
            {
                string path=Path.Combine(directory,size+".dat");byte[] expected=WriteSize(size,path);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                ReadSize(path,expected);Console.WriteLine($"  {size} compressed all-state roundtrip: {new FileInfo(path).Length/1048576d:F3} MiB, complete v10 payload SHA-256 equality.");
            }
        }
        finally{string parent=Path.GetFullPath(AppContext.BaseDirectory);Check(Path.GetDirectoryName(Path.GetFullPath(directory))==Path.TrimEndingDirectorySeparator(parent),"Unsafe cleanup.");Directory.Delete(directory,true);}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]private static byte[] WriteSize(WorldSize size,string path)
    {var save=Fixture(size,true);byte[] hash=RawHash(save);using var file=File.Create(path);WorldSerializer.Write(file,save);return hash;}
    [MethodImpl(MethodImplOptions.NoInlining)]private static void ReadSize(string path,byte[] expected)
    {using var file=File.OpenRead(path);Check(RawHash(WorldSerializer.Read(file)).AsSpan().SequenceEqual(expected),"All-size compressed payload changed.");}
    public static int Run()
    {
        var checks=All.Concat(new (string Name,Action Check)[]{
            ("Existing raw checksums/schema/corruption",PersistenceChecks.CorruptData),
            ("Existing compressed header-only world-size metadata contract",WorldManagementChecks.PersistenceAndMetadata),
            ("Existing v1-v7 legacy height migration",WorldExpansionChecks.Migration),
            ("Existing atomic filesystem failures",PersistenceChecks.AtomicSaving),
            ("Existing inventory/drop data validation",InventoryChecks.Validation)}).ToArray();
        int failures=0;foreach(var check in checks)try{check.Check();Console.WriteLine("PASS: "+check.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+check.Name+": "+e);}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} compression/persistence checks passed.");return failures==0?0:1;
    }
    private sealed class Pipe(byte[] data) : Stream
    {
        private readonly MemoryStream _source=new(data,false);
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] b,int o,int n)=>_source.Read(b,o,Math.Min(71,n));public override int Read(Span<byte> b)=>_source.Read(b[..Math.Min(71,b.Length)]);
        public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
    internal sealed class HashStream(IncrementalHash hash) : Stream
    {
        public override bool CanRead=>false;public override bool CanWrite=>true;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Write(byte[] b,int o,int n)=>hash.AppendData(b.AsSpan(o,n));public override void Write(ReadOnlySpan<byte> b)=>hash.AppendData(b);
        public override void Flush(){}public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
}
