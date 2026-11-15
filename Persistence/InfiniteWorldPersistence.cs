using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Persistence;

internal sealed record InfiniteManifest
{
    public int Version {get;init;}=1;
    public int Generator {get;init;}=BetaDensityGenerator.Version;
    public string Name {get;init;}
    public int Seed {get;init;}
    public long OriginX {get;init;}
    public long OriginZ {get;init;}
    public WorldPosition Position {get;init;}
    public WorldPosition Spawn {get;init;}
    public float Yaw {get;init;}
    public float Pitch {get;init;}
    public double Health {get;init;}=20;
    public BurningState Burning {get;init;}
    public ItemStack[] Inventory {get;init;}=new ItemStack[36];
    public ItemStack[] Armor {get;init;}=new ItemStack[4];
    public int Selected {get;init;}
    public bool Cheats {get;init;}
    public GameMode Mode {get;init;}
    public Difficulty Difficulty {get;init;}=Difficulty.Normal;
    public double Time {get;init;}
    public WeatherState Weather {get;init;}
    public WorldPosition WeatherCenter {get;init;}
}

// The existing finite serializer is unchanged. Infinite manifests point to indexed,
// independently compressed records. A prepared checkpoint is replayed idempotently
// before opening the world, so a partial multi-record save is recoverable.
internal static class InfiniteWorldPersistence
{
    // Offset=-1 reads the original per-chunk recovery journals too.
    private sealed record PreparedChunk(string File,byte[] Hash,long Offset=-1,int Length=0);
    private static readonly byte[] Magic=Encoding.ASCII.GetBytes("MDSM-INF");
    internal static bool IsInfinite(Stream stream)
    {long at=stream.Position;Span<byte> magic=stackalloc byte[8];int n=stream.Read(magic);stream.Position=at;return n==8&&magic.SequenceEqual(Magic);}
    private static byte[] Envelope(InfiniteManifest manifest)
    {byte[] body=StreamingJson.Encode(manifest);using var m=new MemoryStream();m.Write(Magic);m.Write(SHA256.HashData(body));m.Write(body);return m.ToArray();}
    private static InfiniteManifest Read(string path)
    {
        byte[] bytes=File.ReadAllBytes(path);if(bytes.Length<41||bytes.Length>1048576||!bytes.AsSpan(0,8).SequenceEqual(Magic)||!CryptographicOperations.FixedTimeEquals(bytes.AsSpan(8,32),SHA256.HashData(bytes.AsSpan(40))))throw new InvalidDataException("Invalid Infinite world manifest/checksum.");
        var s=StreamingJson.Decode<InfiniteManifest>(bytes[40..]);
        try{WorldSerializer.ValidateName(s.Name);}catch(ArgumentException e){throw new InvalidDataException("Invalid Infinite world name.",e);}
        if(s.Version!=1||s.Generator!=BetaDensityGenerator.Version||!s.Position.Valid||!s.Spawn.Valid||s.Position.X>=Coordinates.MaximumBlock||s.Position.Z>=Coordinates.MaximumBlock||s.Spawn.X>=Coordinates.MaximumBlock||s.Spawn.Z>=Coordinates.MaximumBlock||s.Spawn.Y<0||s.Spawn.Y>144
            ||!double.IsFinite(s.Position.X)||!double.IsFinite(s.Position.Y)||!double.IsFinite(s.Position.Z)||s.Position.Y< -16||s.Position.Y>256
            ||!double.IsFinite(s.Health)||s.Health<0||s.Health>20||!float.IsFinite(s.Yaw)||!float.IsFinite(s.Pitch)||s.Inventory?.Length!=36||s.Armor?.Length!=4||s.Selected is <0 or >8
            ||!Enum.IsDefined(s.Mode)||!Enum.IsDefined(s.Difficulty)||s.OriginX%16!=0||s.OriginZ%16!=0||!Coordinates.Valid(s.OriginX,s.OriginZ))throw new InvalidDataException("Invalid Infinite world/player data.");
        return s;
    }
    internal static WorldMetadata Metadata(string directory)
    {var s=Read(Path.Combine(directory,WorldSaveManager.FileName));return new(s.Name,s.Seed,Vector3.Zero,s.Yaw,s.Pitch,WorldSize.Infinite);}
    internal static WorldSave Load(string directory)
    {
        Recover(directory);var s=Read(Path.Combine(directory,WorldSaveManager.FileName));var world=World.OpenStreamed(s.Seed,Path.Combine(directory,"regions"));
        var focus=s.Health==0?s.Spawn:s.Position;world.SetOrigin(focus.Chunk.BlockX,focus.Chunk.BlockZ);world.InfiniteSpawn=s.Spawn;world.AllowCheats=s.Cheats;world.SavedDifficulty=s.Difficulty;world.SavedGameMode=s.Mode;world.Climate.VariedGeneration=true;
        world.Time.Restore(s.Time);world.Weather.Restore(s.Weather);world.Weather.RestoreCenter(s.WeatherCenter);
        var inventory=new Inventory{SelectedSlot=s.Selected};for(int i=0;i<36;i++)inventory.SetSlot(i,s.Inventory[i]);for(int i=0;i<4;i++)inventory.SetEquipment(i,s.Armor[i]);
        return new(world,s.Name,world.LocalPosition(s.Position),s.Yaw,s.Pitch,inventory,Health:(int)Math.Ceiling(s.Health),ExactHealth:s.Health,PlayerFire:s.Burning,ExactPosition:s.Position);
    }
    internal static void Save(string directory,WorldSave save)
    {bool success=false;try{BeginSave(directory,save).GetAwaiter().GetResult();success=true;}finally{save.World.ChunkLifecycle.FinishCheckpoint(success);}}
    internal static System.Threading.Tasks.Task BeginSave(string directory,WorldSave save)
    {
        WorldSerializer.ValidateName(save.Name);
        var w=save.World;var inventory=save.Inventory??new Inventory();var s=new InfiniteManifest{Name=save.Name,Seed=w.Seed,OriginX=w.OriginX,OriginZ=w.OriginZ,
            Position=save.ExactPosition??w.AbsolutePosition(save.PlayerPosition),Spawn=w.InfiniteSpawn,Yaw=save.Yaw,Pitch=save.Pitch,Health=save.ExactHealth<0?save.Health:save.ExactHealth,Burning=save.PlayerFire,
            Inventory=Enumerable.Range(0,36).Select(i=>inventory[i]).ToArray(),Armor=Enumerable.Range(0,4).Select(i=>inventory.Equipped(i)).ToArray(),Selected=inventory.SelectedSlot,
            Cheats=w.AllowCheats,Mode=w.SavedGameMode??GameMode.Survival,Difficulty=w.SavedDifficulty,Time=w.Time.Seconds,Weather=w.Weather.Capture(),WeatherCenter=w.Weather.Center};
        var chunks=w.ChunkLifecycle.CaptureCheckpoint();byte[] manifest=Envelope(s);
        return System.Threading.Tasks.Task.Run(()=>WritePrepared(directory,w,chunks,manifest));
    }
    private static void WritePrepared(string directory,World world,List<ChunkSnapshot> chunks,byte[] manifest)
    {
        world.ChunkLifecycle.WaitWritesAsync().GetAwaiter().GetResult();
        Directory.CreateDirectory(directory);Recover(directory);string pending=Path.Combine(directory,"checkpoint.pending");Directory.CreateDirectory(pending);
        // Only our uncommitted staging records are eligible for cleanup.
        foreach(string file in Directory.EnumerateFiles(pending,"*.chunk"))File.Delete(file);
        var entries=new List<PreparedChunk>(chunks.Count);
        // One durable journal flush, rather than hundreds of separately flushed
        // tiny files. The checksummed plan still commits only after all bytes.
        using(var journal=new FileStream(Path.Combine(pending,"chunks"),FileMode.Create,FileAccess.Write,FileShare.None))
        {
            foreach(var chunk in chunks)
            {byte[] bytes=chunk.Encode();long offset=journal.Position;journal.Write(bytes);entries.Add(new("chunks",SHA256.HashData(bytes),offset,bytes.Length));}
            journal.Flush(true);
        }
        DurableWrite(Path.Combine(pending,"manifest"),manifest);
        // A complete checksum-protected plan is the commit intent. Ignore incomplete staging.
        string plan=Path.Combine(pending,"plan");DurableWrite(plan+".tmp",StreamingJson.Encode(entries.ToArray()));File.Move(plan+".tmp",plan,true);
        world.ChunkLifecycle.WriteCheckpointAsync(chunks).GetAwaiter().GetResult();
        AtomicManifest(directory,manifest);ClearPrepared(pending,entries.Select(e=>e.File));
    }
    private static void Recover(string directory)
    {
        string pending=Path.Combine(directory,"checkpoint.pending"),plan=Path.Combine(pending,"plan");if(!File.Exists(plan))return;
        PreparedChunk[] entries=StreamingJson.Decode<PreparedChunk[]>(File.ReadAllBytes(plan));if(entries.Length>4225)throw new InvalidDataException("Oversized checkpoint.");
        _=Read(Path.Combine(pending,"manifest"));
        using var regions=new ChunkRegionStore(Path.Combine(directory,"regions"));
        var batch=new List<(ChunkAddress Address,byte[] Payload)>(ChunkRegionStore.MaximumWriteBatch);
        foreach(var record in entries)
        {
            string entry=record.File;
            if(Path.GetFileName(entry)!=entry)throw new InvalidDataException("Invalid checkpoint path.");
            byte[] bytes;
            if(record.Offset==-1)bytes=File.ReadAllBytes(Path.Combine(pending,entry));
            else
            {
                if(entry!="chunks"||record.Offset<0||record.Length is <1 or >ChunkRegionStore.MaximumPayload)throw new InvalidDataException("Invalid prepared chunk range.");
                using var journal=File.OpenRead(Path.Combine(pending,entry));
                if(record.Offset>journal.Length-record.Length)throw new InvalidDataException("Truncated checkpoint journal.");
                journal.Position=record.Offset;bytes=new byte[record.Length];journal.ReadExactly(bytes);
            }
            if(record.Hash?.Length!=32||!CryptographicOperations.FixedTimeEquals(record.Hash,SHA256.HashData(bytes)))throw new InvalidDataException("Prepared chunk checksum mismatch.");
            using var r=new BinaryReader(new MemoryStream(bytes));r.ReadInt32();r.ReadInt32();var a=new ChunkAddress(r.ReadInt64(),r.ReadInt64());var snapshot=ChunkSnapshot.Decode(a,bytes);
            batch.Add((a,bytes));if(batch.Count==ChunkRegionStore.MaximumWriteBatch){regions.WriteBatchAsync(batch.ToArray()).GetAwaiter().GetResult();batch.Clear();}
        }
        if(batch.Count>0)regions.WriteBatchAsync(batch.ToArray()).GetAwaiter().GetResult();
        AtomicManifest(directory,File.ReadAllBytes(Path.Combine(pending,"manifest")));ClearPrepared(pending,entries.Select(e=>e.File));
    }
    private static void ClearPrepared(string directory,IEnumerable<string> entries)
    {File.Delete(Path.Combine(directory,"plan"));foreach(string entry in entries.Distinct())File.Delete(Path.Combine(directory,entry));File.Delete(Path.Combine(directory,"chunks"));File.Delete(Path.Combine(directory,"manifest"));}
    private static void DurableWrite(string path,byte[] bytes)
    {using var file=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);file.Write(bytes);file.Flush(true);}
    private static void AtomicManifest(string directory,byte[] bytes)
    {string path=Path.Combine(directory,WorldSaveManager.FileName),temporary=path+".tmp";DurableWrite(temporary,bytes);if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
}
