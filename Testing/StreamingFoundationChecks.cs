using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MDSM.Persistence;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class StreamingFoundationChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Int64 negative chunk/region indexing and double camera-relative coordinates",CoordinatesCheck),
        ("Java saturation, negative overflow, unbounded noise and 3D density chunks",Noise),
        ("Independent compressed region records, checksums and interrupted index recovery",Regions),
        ("Bounded asynchronous chunk requests, cancellation, edits, unload and reload",Lifecycle)};
    public static int Run(){int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine(e);}return failures==0?0:1;}
    private static void C(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void CoordinatesCheck()
    {
        foreach(long x in new[]{long.MinValue,-12550825L,-513,-512,-17,-16,-1,0,15,16,512,12550825,long.MaxValue})
        {
            long q=Coordinates.FloorDiv(x,16);int r=Coordinates.Mod(x,16);
            C((System.Numerics.BigInteger)q*16+r==x && r is >=0 and <16,"Signed floor/modulo failed.");
        }
        C(new ChunkAddress(-1,-1).Region==new ChunkAddress(-1,-1)&&new ChunkAddress(-1,-1).RegionSlot==1023,"Negative region slot collision.");
        var at=new WorldPosition(1000000000000.25,64, -1000000000000.25);var origin=new WorldPosition(1000000000000,64,-1000000000000);
        C(at.RelativeTo(origin)==new Microsoft.Xna.Framework.Vector3(.25f,0,-.25f),"Absolute coordinates narrowed before subtraction.");
    }
    private static void Noise()
    {
        C(LegacyPerlin.JavaInt(double.NaN)==0 && LegacyPerlin.JavaInt(double.PositiveInfinity)==int.MaxValue && LegacyPerlin.JavaInt(double.NegativeInfinity)==int.MinValue,"Java narrowing mismatch.");
        C(LegacyPerlin.Lattice(-.25)==-1 && LegacyPerlin.Lattice(2147483648.0)==int.MaxValue && LegacyPerlin.Lattice(-2147483649.0)==int.MaxValue,"Negative floor correction did not retain Java integer wrapping.");
        C(Math.Abs(LegacyPerlin.Fade(2))>1,"Out-of-range interpolation was clamped.");
        var generator=new BetaDensityGenerator(173);var watch=Stopwatch.StartNew();
        foreach(long x in new[]{0,12550821,12550822,12550823,12550824,12550825,-12550821,-12550825,25101648,1000000000000})
        foreach(long z in new[]{0,x})
        {
            var address=ChunkAddress.At(x,z);byte[] first=generator.Generate(address),second=new BetaDensityGenerator(173).Generate(address);
            C(first.SequenceEqual(second),"Chunk generation depends on request order.");
            for(int i=0;i<256;i++)C(first[i]==(byte)BlockType.Bedrock,"Bedrock layer lost.");
            C(first.All(b=>Blocks.IsDefined((BlockType)b)),"Unrenderable block ID.");
            Console.WriteLine($"DENSITY {x},{z}: sample={generator.Density(x,64,z):G6}; solid={first.Count(b=>Blocks.IsSolid((BlockType)b))}");
        }
        C(watch.Elapsed.TotalSeconds<30,"Coarse density generation is unbounded.");
        double a=generator.Density(12550825,64,0),b=generator.Density(-12550825,64,0);
        C(Math.Abs(a)>100 || Math.Abs(b)>100,"Expected noise-generated Far Lands extrapolation is missing.");
        Console.WriteLine($"Density probes: {watch.Elapsed.TotalMilliseconds:F1} ms (CPU only, not rendering).");
    }
    private static void Temporary(Action<string> action)
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),path=Path.Combine(parent,".stream-check-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);try{action(path);}finally{if(Path.GetDirectoryName(Path.GetFullPath(path))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe cleanup.");Directory.Delete(path,true);}
    }
    private static void Regions()=>Temporary(path=>
    {
        using var store=new ChunkRegionStore(path);var address=new ChunkAddress(-1,-1);var other=new ChunkAddress(-2,-1);
        byte[] original=Enumerable.Range(0,32768).Select(i=>(byte)(i%89)).ToArray();
        store.WriteAsync(address,original).GetAwaiter().GetResult();store.WriteAsync(other,new byte[]{9,8,7}).GetAwaiter().GetResult();
        C(store.ReadAsync(address).GetAwaiter().GetResult().SequenceEqual(original),"Unrelated region update lost data.");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var changed=(System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();changed.NumberFormat.NegativeSign="minus";System.Globalization.CultureInfo.CurrentCulture=changed;
            C(store.ReadAsync(address).GetAwaiter().GetResult().SequenceEqual(original),"Region filenames depend on locale.");
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        store.WriteAsync(address,new byte[]{4,5,6}).GetAwaiter().GetResult();
        string file=Directory.GetFiles(path,"*.mdr").Single();
        long before=new FileInfo(file).Length;store.CompactAsync(address.Region).GetAwaiter().GetResult();
        C(new FileInfo(file).Length<before && store.ReadAsync(address).GetAwaiter().GetResult().SequenceEqual(new byte[]{4,5,6}),"Region compaction lost latest records or retained dead payloads.");
        // Make three more commits after compaction; page 1 is newest again.
        store.WriteAsync(address,original).GetAwaiter().GetResult();store.WriteAsync(address,original).GetAwaiter().GetResult();store.WriteAsync(address,new byte[]{4,5,6}).GetAwaiter().GetResult();
        // The newest commit lives in index page 1; damage it to emulate a torn
        // commit. Page 0 still references the first chunk and second-slot write.
        using(var stream=File.Open(file,FileMode.Open,FileAccess.Write)){stream.Position=32+(8+1024*12+32);stream.WriteByte(255);}
        C(store.ReadAsync(address).GetAwaiter().GetResult().SequenceEqual(original),"Torn index did not recover the previous committed record.");
        C(store.ReadAsync(other).GetAwaiter().GetResult().SequenceEqual(new byte[]{9,8,7}),"Recovery lost independently committed neighbor.");
        using(var stream=File.Open(file,FileMode.Open,FileAccess.Write)){stream.Position=32;stream.WriteByte(255);}
        bool corrupt=false;try{store.ReadAsync(address).GetAwaiter().GetResult();}catch(InvalidDataException){corrupt=true;}C(corrupt,"Corrupt indexes were treated as missing terrain.");
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();bool cancelled=false;
        try{store.WriteAsync(new(100,100),original,cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}C(cancelled,"Cancelled region write still committed.");
        using var batches=new ChunkRegionStore(Path.Combine(path,"batches"));
        var records=Enumerable.Range(0,16).Select(i=>(Address:new ChunkAddress(i<8?-i-1:32+i,0),Payload:new byte[]{(byte)i,42})).ToArray();
        batches.WriteBatchAsync(records).GetAwaiter().GetResult();
        foreach(var record in records)C(batches.ReadAsync(record.Address).GetAwaiter().GetResult().SequenceEqual(record.Payload),"Batched multi-region checkpoint changed a chunk.");
        bool oversized=false;try{batches.WriteBatchAsync(Enumerable.Repeat(records[0],ChunkRegionStore.MaximumWriteBatch+1).ToArray()).GetAwaiter().GetResult();}catch(ArgumentException){oversized=true;}
        C(oversized,"Unbounded checkpoint write batch accepted.");
    });
    private static void Lifecycle()=>Temporary(path=>
    {
        var manager=new StreamingChunks(173,path,2,0,0);
        void Pump(ChunkAddress center,Func<bool> done)
        {
            var timer=Stopwatch.StartNew();do{manager.Update(center);C(manager.Count<=manager.MaximumResident && manager.LoadingCount<=StreamingChunks.MaximumWorkers && manager.SavingCount<=StreamingChunks.MaximumSaveWorkers,"Unbounded residency/work.");if(timer.Elapsed.TotalSeconds>25)throw new Exception("Chunk lifecycle stalled.");Thread.Sleep(1);}while(!done());
        }
        C(manager.Solid(-1,64,-1),"Missing chunk collision treated as Air.");
        Pump(new(-1,-1),()=>manager.Ready(-1,-1));C(manager.Set(-1,120,-1,BlockType.Glass),"Loaded chunk edit failed.");
        for(int i=0;i<30;i++)manager.Update(new(i*100,-i*100));
        Pump(new(1000,1000),()=>!manager.TryGet(new(-1,-1),out _) && manager.Ready(16000,16000));
        Pump(new(-1,-1),()=>manager.TryGet(new(-1,-1),out var chunk) && chunk.Blocks[Coordinates.Index(15,120,15)]==(byte)BlockType.Glass);
        C(manager.Error==null,"Background job failed.");manager.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var restored=new StreamingChunks(173,path,2,0,0);var timer=Stopwatch.StartNew();
        while(!restored.Ready(-1,-1)){restored.Update(new(-1,-1));if(timer.Elapsed.TotalSeconds>25)throw new Exception("Reload stalled.");Thread.Sleep(1);}
        C(restored.TryGet(new(-1,-1),out var saved)&&saved.Blocks[Coordinates.Index(15,120,15)]==(byte)BlockType.Glass,"Shutdown lost edited chunk.");restored.DisposeAsync().AsTask().GetAwaiter().GetResult();
        string blocked=Path.Combine(path,"blocked");File.WriteAllText(blocked,"not a directory");var failing=new StreamingChunks(173,blocked,2,0,0);
        timer.Restart();while(!failing.Ready(0,0)){failing.Update(new(0,0));if(timer.Elapsed.TotalSeconds>25)throw new Exception("Failure fixture load stalled.");Thread.Sleep(1);}
        failing.Set(0,120,0,BlockType.Glass);timer.Restart();
        while(failing.Error==null){failing.Update(new(1000,1000));if(timer.Elapsed.TotalSeconds>25)throw new Exception("Save failure was not surfaced.");Thread.Sleep(1);}
        C(failing.TryGet(new(0,0),out var retained)&&retained.Dirty,"Failed save discarded modified terrain.");
        bool reported=false;try{failing.DisposeAsync().AsTask().GetAwaiter().GetResult();}catch(AggregateException){reported=true;}C(reported,"Shutdown falsely reported persistence success.");
        C(failing.TryGet(new(0,0),out retained)&&retained.Dirty,"Failed shutdown discarded the recoverable session.");
        File.Delete(blocked);Directory.CreateDirectory(blocked);failing.RetryFailures();failing.DisposeAsync().AsTask().GetAwaiter().GetResult();
        using var recoveredStore=new ChunkRegionStore(blocked);var recovered=ChunkSnapshot.Decode(new(0,0),recoveredStore.ReadAsync(new(0,0)).GetAwaiter().GetResult());
        C(recovered.Blocks[Coordinates.Index(0,120,0)]==(byte)BlockType.Glass,"Retried shutdown lost the retained edit.");
    });
}
