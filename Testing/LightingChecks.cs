using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.Testing;

internal static class LightingChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Packed sunlight/block lighting, attenuation, opacity, sources, removal and seams",Propagation),
        ("Twenty-minute clock, sky/celestial alignment, sleep, visual-only brightness and no remeshing",TimeAndBrightness),
        ("Beds: four orientations, recipe, paired geometry/collision, support and exact drops",Beds),
        ("Versioned compressed environment, legacy compatibility, Bed metadata and corruption",Persistence),
        ("Exactly-zero hostile spawning on surfaces/structures, artificial light, Peaceful and settings",SpawningAndSettings),
        ("Bounded light-cache eviction, distant edits, local invalidation and allocation-free clocks",Budgets)
    };
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static int Block(World w,int x,int y,int z)=>w.Lighting.Packed(new(x,y,z))&15;
    private static int Sky(World w,int x,int y,int z)=>w.Lighting.Packed(new(x,y,z))>>4;
    private static World Chamber()
    {
        var w=new World(173,WorldSize.Small,false);
        for(int z=0;z<80;z++)for(int x=0;x<80;x++)
        {w.SetGeneratedBlock(x,0,z,BlockType.Stone);w.SetGeneratedBlock(x,30,z,BlockType.Stone);}
        return w;
    }
    public static void Propagation()
    {
        var w=Chamber();Check(Sky(w,31,5,31)==0 && Sky(w,31,31,31)==14,"Roof/exposed sunlight failed.");
        w.SetBlock(31,5,31,BlockType.Torch);
        Check(Block(w,31,5,31)==14 && Block(w,32,5,31)==13 && Block(w,44,5,31)==1 && Block(w,45,5,31)==0,"Torch/seam attenuation failed.");
        Check(w.Lighting.CachedChunks<20,"A local query scanned every world chunk.");
        w.SetBlock(31,5,31,BlockType.Air);Check(Block(w,32,5,31)==0,"Removed Torch left ghost light.");
        w.SetBlock(31,5,31,BlockType.LavaSource);Check(Block(w,32,5,31)==13,"Lava emission failed.");
        w.SetBlock(31,5,31,BlockType.Air);
        for(int z=0;z<80;z++)for(int y=1;y<30;y++)w.SetBlock(32,y,z,BlockType.Stone);
        w.SetBlock(31,5,31,BlockType.Torch);Check(Block(w,33,5,31)==0,"Opaque barrier transmitted light.");
        w.SetBlock(32,5,31,BlockType.Glass);Check(Block(w,33,5,31)==12,"Glass did not transmit light.");
        w.SetBlock(32,5,31,BlockType.Leaves);Check(Block(w,33,5,31)==12,"Leaf attenuation failed.");
        w.SetBlock(31,5,31,BlockType.Air);w.SetBlock(31,30,31,BlockType.Air);
        Check(Sky(w,31,5,31)==14 && Sky(w,30,5,31)==13,"Vertical sunlight shaft/lateral attenuation failed.");
        w.SetBlock(31,30,31,BlockType.Leaves);Check(Sky(w,31,30,31)==13 && Sky(w,31,29,31)==12,"Leaves did not attenuate direct sky.");
        w.SetBlock(31,30,31,BlockType.WaterSource);Check(Sky(w,31,30,31)==13 && Sky(w,31,29,31)==12,"Water did not attenuate direct sky.");
        w.SetBlock(31,30,31,BlockType.Glass);Check(Sky(w,31,29,31)==14,"Glass incorrectly blocks direct sky.");
        w.SetBlock(31,30,31,BlockType.Stone);Check(Sky(w,31,5,31)==0,"Closing sunlight shaft left stale sky.");
        w.SetBlock(50,5,50,BlockType.Furnace);var f=w.Containers.Get(new(50,5,50));
        f.SetSlot(0,new(Items.FromBlock(BlockType.Sand),1));f.SetSlot(1,new(Items.Coal,1));w.Containers.Update(.1);
        Check(Block(w,51,5,50)==12,"Burning Furnace failed to emit level thirteen.");
        w.Containers.Update(90);Check(Block(w,51,5,50)==0,"Extinguished Furnace left stale light.");
        w.SetBlock(255,127,255,BlockType.Torch);Check(Block(w,254,127,255)==13,"Finite upper/outer boundary failed.");
        var mesh=new ChunkMeshBuilder();mesh.Build(w,w.GetChunk(1,1));
        Check(mesh.Vertices.AsSpan(0,mesh.VertexCount).ToArray().All(v=>v.Color.G<=14 && v.Color.B<=14 && v.Color.A==255),"Terrain light encoding corrupted alpha/range.");
    }
    public static void TimeAndBrightness()
    {
        var w=Chamber();var t=w.Time;Check(t.Daylight==14 && !t.IsNight && !t.Sleep(),"New-world daytime/sleep failed.");
        double original=t.Seconds;foreach(int fps in new[]{30,60,144,360})
        {t.Restore(original);for(int i=0;i<fps*20;i++)t.Advance(1d/fps);Check(Math.Abs(t.Seconds-original-20)<1e-7,"Clock depends on frames.");}
        t.Restore(300);Check(t.SunDirection.Y>.99f && t.MoonDirection.Y<-.99f,"Noon celestial position failed.");
        t.Restore(900);Check(t.Daylight==0 && t.SunDirection.Y<-.99f && t.MoonDirection.Y>.99f,"Midnight alignment failed.");
        int pages=w.Lighting.BuiltPages;int[] revisions=w.Chunks.ToArray().Select(c=>c.Revision).ToArray();
        for(int i=0;i<12000;i++)t.Advance(.1);
        Check(Math.Abs(t.Seconds-900)<1e-7 && w.Lighting.BuiltPages==pages && w.Chunks.ToArray().Select(c=>c.Revision).SequenceEqual(revisions),"Day/night recalculated lighting or geometry.");
        Check(t.Sleep() && t.Seconds==WorldTime.Morning && t.Daylight==14 && !t.Sleep(),"Night skip failed.");
        foreach(float b in new[]{0,.5f,1})
        {Check(BlockLighting.Visibility(0,b)>0 && BlockLighting.Visibility(0,b)<.2f && BlockLighting.Visibility(14,b)==1,"Brightness erased darkness or made black voids.");}
        var settings=new GameSettings();settings.Brightness=9;Check(settings.Brightness==1,"Brightness upper bound failed.");settings.Brightness=float.NaN;Check(settings.Brightness==.5f,"Invalid brightness failed fallback.");
        t.Restore(600);Color dusk=t.Sky;t.Restore(300);Color day=t.Sky;t.Restore(900);Check(dusk!=day && t.Sky!=day && t.Sky!=dusk,"Sky palette is static.");
    }
    public static void Beds()
    {
        var grid=new CraftingGrid(3);for(int i=0;i<3;i++){grid.SetSlot(i+3,new(Items.FromBlock(BlockType.WhiteWool),1));grid.SetSlot(i+6,new(Items.FromBlock(BlockType.WoodenPlanks),1));}
        Check(grid.Preview.Item==Items.FromBlock(BlockType.Bed),"Translated Bed recipe failed.");
        var interaction=new InventoryInteraction(new Inventory());Check(grid.TryCraft(interaction) && interaction.Cursor.Count==1 && grid.Preview.IsEmpty,"Bed crafting accounting failed.");
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(byte facing=0;facing<4;facing++)for(int half=0;half<2;half++)
        {
            var w=PigChecks.Flat();var session=new GameSession("bed",new(w,"Bed",new(30,1,30),0,0));Point3 p=new(15,1,15),q=p+World.BedDirection(facing);
            Check(w.PlaceBed(p,facing,session.Player.Bounds),"Bed placement failed.");
            Check(session.Sounds.Count==1,"Bed placement must make exactly one sound.");session.Sounds.Clear();
            Check(w.BlockBoxes(p,boxes,true)==1 && boxes[0].Max.Y==1+9f/16,"Bed collision height wrong.");
            var builder=new ChunkMeshBuilder();builder.Build(w,w.GetChunk(p.X/16,p.Z/16));
            Rectangle top=TextureAtlas.Source(54);Check(builder.Vertices.AsSpan(0,builder.VertexCount).ToArray().Any(v=>v.Position.Y==1+9f/16 && v.TextureCoordinate.Y>top.Y/256f && v.TextureCoordinate.Y<(top.Y+16)/256f),"Bed top geometry/UVs missing.");
            var mining=new MiningController(1);Point3 target=half==0?p:q;
            Check(mining.Update(w,session.Items,new(target,default,1),true,1,session.Inventory),"Bed mining failed.");
            Check(w.GetBlock(p.X,p.Y,p.Z)==BlockType.Air && w.GetBlock(q.X,q.Y,q.Z)==BlockType.Air && session.Items.Entities.Sum(i=>i.Count)==1 && session.Items.Entities[0].Item==Items.FromBlock(BlockType.Bed),"Paired Bed duplicated/lost drops.");
            int hits=0,breaks=0;while(session.Sounds.TryDequeue(out var sound))
            {SoundKind kind=SoundCatalog.Groups[sound.Group].Kind;if(kind==SoundKind.Hit)hits++;if(kind==SoundKind.Break)breaks++;}
            Check(hits==1 && breaks==1,"Paired Bed emitted duplicate hit/break sounds.");
            Check(w.PlaceBed(p,facing,session.Player.Bounds),"Bed replacement failed.");w.SetBlock(p.X,p.Y-1,p.Z,BlockType.Air);
            Check(w.GetBlock(q.X,q.Y,q.Z)==BlockType.Air && session.Items.Entities.Sum(i=>i.Count)==2,"Support loss lost/duplicated Bed.");
        }
        var blocked=PigChecks.Flat();Point3 foot=new(20,1,20),head=foot+World.BedDirection(0);
        blocked.SetBlock(head.X,head.Y,head.Z,BlockType.Stone);Check(!blocked.PlaceBed(foot,0,new Player(new(40,1,40)).Bounds) && blocked.GetBlock(20,1,20)==BlockType.Air,"Partial obstructed Bed placement modified world.");
        blocked.SetBlock(head.X,head.Y,head.Z,BlockType.Air);Check(!blocked.PlaceBed(foot,0,new Player(new(20.5f,1,20.5f)).Bounds),"Bed placed through player.");
        var creative=new GameSession("creative",new(PigChecks.Flat(),"Creative",new(30,1,30),0,0));creative.ApplySettings(new(){GameMode=GameMode.Creative});
        creative.World.PlaceBed(foot,0,creative.Player.Bounds);creative.Mining.Update(creative.World,creative.Items,new(foot,default,1),true,0,creative.Inventory,true);
        Check(creative.Items.Entities.Count==0 && creative.World.Structures.Count==0,"Creative Bed produced loot/orphan state.");
        creative.World.PlaceBed(foot,0,creative.Player.Bounds);creative.World.Time.Restore(900);
        Check(SurvivalInteractions.Use(creative,new BlockHit(foot,default,1)) && creative.Sleep.IsSleeping && creative.World.Time.Daylight==0,"Bed interaction skipped the sleep transition.");
        creative.Sleep.Update(creative,BedSleepController.FadeSeconds);
        Check(!creative.Sleep.IsSleeping && creative.World.Time.Daylight==14,"Completed Bed transition did not skip to morning.");
        double morning=creative.World.Time.Seconds;Check(SurvivalInteractions.Use(creative,new BlockHit(foot,default,1)) && creative.World.Time.Seconds==morning,"Daytime Bed interaction skipped time.");
    }
    public static void Persistence()
    {
        var w=PigChecks.Flat();w.Time.Restore(873.125);w.SavedGameMode=GameMode.Creative;
        for(byte i=0;i<4;i++)Check(w.PlaceBed(new(30+i*4,1,30),i,new Player(new(50,1,50)).Bounds),"Bed save fixture failed.");
        Check(w.PlaceStructure(new(31,1,31),BlockType.Torch,0,new(0,-1,0),new Player(new(50,1,50)).Bounds),"Torch save fixture failed.");
        var save=new WorldSave(w,"Environment",new(50,1,50),0,0);byte[] before=CompressionChecks.RawHash(save);
        using var stream=new MemoryStream();WorldSerializer.Write(stream,save);stream.Position=0;var loaded=WorldSerializer.Read(stream);
        Check(loaded.World.Time.Seconds==873.125 && loaded.World.SavedGameMode==GameMode.Creative && before.AsSpan().SequenceEqual(CompressionChecks.RawHash(loaded)),"Environment/Bed state changed.");
        Check(loaded.World.Lighting.CachedChunks==0 && loaded.World.Lighting.Effective(new Point3(32,1,31))==13 && loaded.World.Lighting.Effective(new Point3(32,1,31))==w.Lighting.Effective(new Point3(32,1,31)),"Loaded lights are missing or stored redundantly.");
        var session=new GameSession("loaded",loaded);Check(session.Player.Creative,"Saved mode not restored.");
        // Build an actual v11 container by removing only the v12 environment record.
        var legacyWorld=PigChecks.Flat();legacyWorld.SetBlock(31,1,31,BlockType.Torch);
        var legacySave=new WorldSave(legacyWorld,"Legacy environment",new(50,1,50),0,0);
        using var legacyContainer=new MemoryStream();WorldSerializer.Write(legacyContainer,legacySave);
        byte[] bytes=legacyContainer.ToArray();int start=52+BitConverter.ToInt32(bytes,24)+48,at=start;
        while(BitConverter.ToUInt32(bytes,at)!=WorldSerializer.EnvironmentRecord)at+=56+BitConverter.ToInt32(bytes,at+20);
        Check(bytes[at+8]==0,"Environment corruption fixture requires stored scalar payload.");
        void RejectEnvironment(Action<byte[]> corrupt)
        {
            var bad=(byte[])bytes.Clone();corrupt(bad);
            SHA256.HashData(bad.AsSpan(at+56,BitConverter.ToInt32(bad,at+16))).CopyTo(bad.AsSpan(at+24,32));SHA256.HashData(bad.AsSpan(0,bad.Length-32)).CopyTo(bad.AsSpan(bad.Length-32));
            bool invalid=false;try{WorldSerializer.Read(new MemoryStream(bad));}catch(InvalidDataException){invalid=true;}Check(invalid,"Invalid environment state was accepted after repaired checksums.");
        }
        RejectEnvironment(b=>BitConverter.GetBytes(double.NaN).CopyTo(b,at+56));
        RejectEnvironment(b=>b[at+56+8]=3);RejectEnvironment(b=>b[at+56+9]=2);
        int length=56+BitConverter.ToInt32(bytes,at+20);byte[] v11=bytes[..at].Concat(bytes[(at+length)..]).ToArray();BitConverter.GetBytes(11).CopyTo(v11,4);
        SHA256.HashData(v11.AsSpan(0,start-32)).CopyTo(v11.AsSpan(start-32,32));SHA256.HashData(v11.AsSpan(0,v11.Length-32)).CopyTo(v11.AsSpan(v11.Length-32));
        var old=WorldSerializer.Read(new MemoryStream(v11));Check(old.World.Time.Seconds==WorldTime.Morning && old.World.SavedGameMode==GameMode.Survival && old.World.SavedDifficulty==Difficulty.Normal && old.World.AllowCheats,"v11 default world-rule migration failed.");
        using(var resaved=new MemoryStream())
        {WorldSerializer.Write(resaved,old with{Name="Renamed legacy"});resaved.Position=0;Check(WorldSerializer.Read(resaved).World.SavedGameMode==GameMode.Survival,"Renaming an old world lost its migrated mode.");}
        var migratedSession=new GameSession("old",old);migratedSession.ApplySettings(new(){GameMode=GameMode.Creative});
        using(var played=new MemoryStream())
        {WorldSerializer.Write(played,migratedSession.Capture());played.Position=0;Check(WorldSerializer.Read(played).World.SavedGameMode==GameMode.Creative,"Migrated active mode failed to persist.");}
        using var raw=new MemoryStream();WorldSerializer.WriteLegacy(raw,legacySave);raw.Position=0;Check(WorldSerializer.Read(raw).World.Time.Seconds==WorldTime.Morning,"v10 load failed.");
        // Missing required environment cannot silently reset a modern world's time.
        BitConverter.GetBytes(12).CopyTo(v11,4);SHA256.HashData(v11.AsSpan(0,start-32)).CopyTo(v11.AsSpan(start-32,32));SHA256.HashData(v11.AsSpan(0,v11.Length-32)).CopyTo(v11.AsSpan(v11.Length-32));
        bool rejected=false;try{WorldSerializer.Read(new MemoryStream(v11));}catch(InvalidDataException){rejected=true;}Check(rejected,"v12 missing world time accepted.");
    }
    public static void SpawningAndSettings()
    {
        var w=new World(1,WorldSize.Small,false);for(int z=60;z<110;z++)for(int x=60;x<110;x++)w.SetGeneratedBlock(x,79,z,BlockType.Grass);
        var mobs=new HostilePopulation(w);Vector3 feet=new(80.5f,80,80.5f),player=new(110.5f,80,80.5f);
        Check(!mobs.Suitable(MobSpecies.Zombie,feet,player),"Daylight surface spawn accepted.");w.Time.Restore(800);
        Check(mobs.Suitable(MobSpecies.Zombie,feet,player),"Dark high-Y surface spawn rejected.");
        w.SetBlock(93,80,80,BlockType.Torch);Check(w.Lighting.Effective(feet)==1 && !mobs.Suitable(MobSpecies.Zombie,feet,player),"Light one allowed hostile spawn.");
        w.SetBlock(93,80,80,BlockType.Air);Check(mobs.Suitable(MobSpecies.Zombie,feet,player),"Light zero rejected after Torch removal.");
        w.SetGeneratedBlock(80,0,80,BlockType.Stone);
        Check(mobs.Suitable(MobSpecies.Zombie,new(80.5f,1,80.5f),new(110.5f,1,80.5f)),"Suitable dark ground at the lowest standing cell was rejected.");
        w.Time.Restore(300);for(int z=60;z<110;z++)for(int x=60;x<110;x++)w.SetBlock(x,83,z,BlockType.Stone);
        Check(mobs.Suitable(MobSpecies.Zombie,feet,player),"Daytime enclosed dark structure spawn rejected.");
        var populated=new HostilePopulation(w,new[]{new MobState(MobSpecies.Zombie,feet,0,20)});populated.ApplySettings(new(){Difficulty=Difficulty.Peaceful});Check(populated.Entities.Count==0,"Peaceful failed immediate removal.");
        foreach(Point size in new[]{new Point(320,240),new(640,480),new(1280,720)})
        {
            var v=new Viewport(0,0,size.X,size.Y);Rectangle[] controls={SettingsLayout.Slider(v),SettingsLayout.Brightness(v),SettingsLayout.Volume(v,0),SettingsLayout.Volume(v,1),SettingsLayout.Volume(v,2),SettingsLayout.Volume(v,3),SettingsLayout.Toggle(v),SettingsLayout.Bobbing(v),SettingsLayout.Choice(v,0),SettingsLayout.Choice(v,1),SettingsLayout.Information(v,0),SettingsLayout.Information(v,1),SettingsLayout.Back(v)};
            for(int i=0;i<controls.Length;i++){Check(controls[i].Top>=30 && controls[i].Bottom<=v.Height && controls[i].Left>=0 && controls[i].Right<=v.Width,"Settings leaves viewport.");for(int j=0;j<i;j++)Check(!controls[i].Intersects(controls[j]),"Integrated settings overlap.");}
        }
        Check(new GameSettings().GameMode==GameMode.Survival,"Default mode is not Survival.");
        string path=Path.Combine(AppContext.BaseDirectory,".lighting-settings-"+Guid.NewGuid().ToString("N")+".json");
        try{var store=new SettingsStore(path);store.Save(new(){Brightness=.85f,MasterVolume=.2f,FpsCap=240});var settings=store.Load();Check(settings.Brightness==.85f && settings.MasterVolume==.2f && settings.FpsCap==240,"New settings reset existing preferences.");}finally{if(File.Exists(path))File.Delete(path);}
    }
    public static int Run()
    {int fail=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){fail++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}Console.WriteLine($"{All.Length-fail}/{All.Length} lighting checks passed.");return fail==0?0:1;}
    public static void Budgets()
    {
        var w=new World(173,WorldSize.Large,false);w.Time.Restore(900);w.SetBlock(1,5,1,BlockType.Torch);
        Check(Block(w,2,5,1)==13,"Cache fixture failed.");
        for(int i=1;i<BlockLighting.MaximumCachedChunks+32;i++)w.Lighting.GetPage(i%w.ChunksX,i/w.ChunksX);
        Check(w.Lighting.CachedChunks==BlockLighting.MaximumCachedChunks && w.Lighting.CachedBytes<64*1048576L,"Light cache scales with world dimensions.");
        w.SetBlock(1,5,1,BlockType.Air);Check(Block(w,2,5,1)==0,"Evicted page reconstructed stale source.");
        int[] revisions=w.Chunks.ToArray().Select(c=>c.Revision).ToArray();w.SetBlock(31,50,31,BlockType.Stone);
        Check(w.Chunks.ToArray().Where((c,i)=>c.Revision!=revisions[i]).Count()<=9,"Local edit invalidated the entire world.");
        var p=new Point3(2,5,1);w.Lighting.Effective(p);int pages=w.Lighting.BuiltPages;
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++){w.Time.Advance(1d/120);w.Lighting.Effective(p);}
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated && w.Lighting.BuiltPages==pages,"Idle lighting/time queries allocate or rebuild each tick.");
    }
    public static int Performance(WorldSize size)
    {
        var watch=Stopwatch.StartNew();var w=World.CreateNew(173,size);double generation=watch.Elapsed.TotalSeconds;long allocated=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
        int cx=w.ChunksX/2,cz=w.ChunksZ/2;
        for(int z=cz-4;z<cz+4;z++)for(int x=cx-4;x<cx+4;x++)w.Lighting.GetPage(x,z);
        double initial=watch.Elapsed.TotalMilliseconds;int built=w.Lighting.BuiltPages;watch.Restart();
        for(int i=0;i<120000;i++)w.Time.Advance(1d/120);
        Check(w.Lighting.BuiltPages==built,"Time change rebuilt lighting.");
        double clock=watch.Elapsed.TotalMilliseconds;watch.Restart();var torch=new Point3(w.Width/2,w.SurfaceY(w.Width/2,w.Depth/2)+1,w.Depth/2);w.SetBlock(torch.X,torch.Y,torch.Z,BlockType.Torch);
        int changed=w.Lighting.Effective(torch);for(int z=cz-1;z<=cz+1;z++)for(int x=cx-1;x<=cx+1;x++)w.Lighting.GetPage(x,z);
        Console.WriteLine($"LIGHTING {size}: generation={generation:F2}s; 64 light pages={initial:F2}ms; local Torch relight={watch.Elapsed.TotalMilliseconds:F2}ms; 1000s clock={clock:F2}ms; cache={w.Lighting.CachedChunks} pages/{w.Lighting.CachedBytes/1048576d:F2}MiB; incremental allocations={(GC.GetAllocatedBytesForCurrentThread()-allocated)/1048576d:F2}MiB; process WS={Process.GetCurrentProcess().WorkingSet64/1048576d:F1}MiB; emitted={changed}.");return 0;
    }
}
