using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CompressionBenchmarks
{
    public static int Pipeline(WorldSize size,string mode)
    {
        if(mode is not ("raw" or "zstd" or "brotli"))throw new ArgumentException("Expected raw, zstd or brotli.");
        string directory=Path.Combine(AppContext.BaseDirectory,"compression-benchmark"),input=Path.Combine(directory,size+".raw"),output=Path.Combine(directory,size+"."+mode+".dat");
        if(!File.Exists(input))Fixture(size);
        byte[] expected=WriteMeasured(input,output,mode,out double saving,out double writePeak,out double writeManaged,out double writeBaseline);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        ReadMeasured(output,expected,out double loading,out double readPeak,out double readManaged);
        Console.WriteLine($"PIPELINE {size} {mode}: sizeMiB={new FileInfo(output).Length/1048576d:F3} saveSeconds={saving:F3} loadSeconds={loading:F3} writePeakWSMiB={writePeak:F1} writeBaselineWSMiB={writeBaseline:F1} readPeakWSMiB={readPeak:F1} writePeakManagedMiB={writeManaged:F1} readPeakManagedMiB={readManaged:F1} payloadSHA256=PASS");return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] WriteMeasured(string input,string output,string mode,out double elapsed,out double peak,out double managed,out double baseline)
    {
        WorldSave save;using(var source=File.OpenRead(input))save=WorldSerializer.Read(source);
        byte[] hash=CompressionChecks.RawHash(save);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
        baseline=Process.GetCurrentProcess().WorkingSet64/1048576d;
        using var memory=new PeakMemory();var watch=Stopwatch.StartNew();string temporary=output+".tmp";
        using(var target=File.Create(temporary))
        {
            if(mode=="raw")WorldSerializer.WriteLegacy(target,save);
            else WorldSerializer.WriteCompressed(target,save,mode=="zstd"?SaveCodec.Zstandard:SaveCodec.Brotli);
            target.Flush(true);
        }
        File.Move(temporary,output,true);elapsed=watch.Elapsed.TotalSeconds;memory.Stop();peak=memory.WorkingSet/1048576d;managed=memory.Managed/1048576d;
        return hash;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadMeasured(string path,byte[] expected,out double elapsed,out double peak,out double managed)
    {
        using var memory=new PeakMemory();var watch=Stopwatch.StartNew();WorldSave save;using(var source=File.OpenRead(path))save=WorldSerializer.Read(source);
        elapsed=watch.Elapsed.TotalSeconds;memory.Stop();peak=memory.WorkingSet/1048576d;managed=memory.Managed/1048576d;
        if(!CompressionChecks.RawHash(save).AsSpan().SequenceEqual(expected))throw new InvalidDataException("Benchmark roundtrip changed world data.");
    }
    // Ten-millisecond sampled process RSS/working set and managed live/garbage bytes.
    // Reported as samples, not driver memory or a guaranteed instantaneous maximum.
    private sealed class PeakMemory : IDisposable
    {
        private readonly object _gate=new();private readonly Process _process=Process.GetCurrentProcess();private readonly Timer _timer;
        private bool _stopped;public long WorkingSet {get;private set;}public long Managed {get;private set;}
        public PeakMemory(){Sample(null);_timer=new Timer(Sample,null,10,10);}
        private void Sample(object state){lock(_gate){if(_stopped)return;_process.Refresh();WorkingSet=Math.Max(WorkingSet,_process.WorkingSet64);Managed=Math.Max(Managed,GC.GetTotalMemory(false));}}
        public void Stop(){Sample(null);lock(_gate){_stopped=true;_timer.Change(Timeout.Infinite,Timeout.Infinite);}}
        public void Dispose(){Stop();_timer.Dispose();_process.Dispose();}
    }
    public static int Fixture(WorldSize size)
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"compression-benchmark");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,size+".raw");
        if(File.Exists(path)){Console.WriteLine("Existing benchmark fixture: "+path);return 0;}
        var watch=System.Diagnostics.Stopwatch.StartNew();var world=World.CreateNew(173,size);
        var random=new Random(907);BlockType[] edits={BlockType.Dirt,BlockType.Stone,BlockType.WoodenPlanks,BlockType.WhiteWool,BlockType.Glass};
        for(int i=0;i<world.Width*world.Depth/256;i++)world.SetBlock(random.Next(world.Width),random.Next(116,128),random.Next(world.Depth),edits[random.Next(edits.Length)]);
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1,123));inventory.SetSlot(8,new(Items.Coal,47));inventory.SelectedSlot=8;
        inventory.SetEquipment(0,new(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Helmet),1));
        var mobs=new PassiveMobPopulation(world,PassiveMobPopulation.Generate(world));
        var save=new WorldSave(world,"Compression benchmark",world.Spawn,.3f,-.2f,inventory,
            new[]{new DroppedItemState(Items.Feather,17,world.Spawn,0,.5f)},Health:17,Pigs:mobs.CapturePigs(),Mobs:mobs.CaptureOthers(),Population:mobs.CapturePopulation(),ExactHealth:16.5);
        using(var file=File.Create(path))WorldSerializer.WriteLegacy(file,save);
        Console.WriteLine($"Created {size} representative edited save: {new FileInfo(path).Length/1048576d:F3} MiB in {watch.Elapsed.TotalSeconds:F2}s: {path}");return 0;
    }
}
