using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
using MDSM.Persistence;
using MDSM.Inventories;
using MDSM.Entities;
using MDSM.Core;
using System.Linq;

namespace MDSM.Testing;

internal static class InfiniteIntegrationChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Live sparse World block mutations, negative/high coordinates, physics and frame-relative targeting",WorldAccess),
        ("Infinite creation, real session unload/reload, containers, timers, mobs, drops and checkpoint persistence",SessionLifecycle),
        ("Infinite prepared-checkpoint recovery, failed saves, corruption and independent async snapshots",CheckpointSafety),
        ("Complete Infinite generation is deterministic, biome-aware, supported and bounded",CompleteGeneration),
        ("Infinite exposed soil, exact airborne teleports and lighting progress during streaming",StreamingCorrections)};
    public static int Run(){try{foreach(var check in All){check.Check();Console.WriteLine("PASS: "+check.Name);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static void C(bool condition,string message)=>CommandChecks.Check(condition,message);
    private static void StreamingCorrections()
    {
        var generator=new BetaDensityGenerator(173);int grass=0,stone=0;
        foreach(var address in new[]{new ChunkAddress(0,0),new(4,-3),new(-8,7),new(784426,0)})
        {
            byte[] raw=generator.Generate(address);var complete=generator.GenerateComplete(address).Blocks;
            for(int z=0;z<16;z++)for(int x=0;x<16;x++)
            {
                bool covered=false;
                for(int y=127;y>0;y--)
                {
                    var block=(BlockType)raw[Coordinates.Index(x,y,z)];
                    if(block==BlockType.Grass){C(!covered,"Buried/underwater/overhang Grass was painted underground.");grass++;}
                    if(Blocks.IsOpaque(block)||Water.IsWater(block))covered=true;
                    if(block==BlockType.Stone)stone++;
                    if(y<127&&complete[Coordinates.Index(x,y,z)]==(byte)BlockType.Grass)C(!Blocks.IsOpaque((BlockType)complete[Coordinates.Index(x,y+1,z)]),"A feature covered Grass without replacing it with Dirt.");
                }
            }
        }
        C(grass>0&&stone>20000,"Surface painting lost Grass or underground Stone.");
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".streaming-corrections-"+Guid.NewGuid().ToString("N"));
        var world=World.OpenStreamed(173,root,2,0,0);GameSession session=null;
        try
        {
            world.AllowCheats=true;world.SavedGameMode=GameMode.Creative;
            session=new("corrections",new WorldSave(world,"Corrections",new(8.5f,128,8.5f),0,0));
            foreach(string command in new[]{"/tp @p 12550824 128 0","/tp -12550824 128 0","/tp @p 0 140 -12550824","/tp 12550824 128 12550824"})
            {
                C(new WorldCommands().Execute(command,session,new GameSettings()).Success,"Exact or optional-selector teleport rejected: "+command);
                var expected=session.PendingTeleport.Value;var timer=Stopwatch.StartNew();
                while(!session.AdvanceStreaming(out _)){if(world.ChunkLifecycle.Error!=null)throw world.ChunkLifecycle.Error;if(timer.Elapsed.TotalSeconds>30)throw new Exception("Exact teleport stalled.");Thread.Sleep(1);}
                C(session.Player.AbsolutePosition==expected,"Teleport snapped a requested airborne Y to terrain.");
                for(int i=0;i<120;i++)session.Player.Update(world,Vector3.Zero,false,1f/120);
                C(session.Player.AbsolutePosition==expected,"Creative physics/F3 origin conversion corrupted a teleport.");
                Console.WriteLine($"Exact teleport {expected}: {timer.Elapsed.TotalMilliseconds:F0} ms, local={session.Player.Position}.");
            }
            long cx=session.Player.AbsolutePosition.Chunk.X,cz=session.Player.AbsolutePosition.Chunk.Z;
            bool prepared=false;
            for(int i=0;i<2000&&!prepared;i++)
            {
                world.Lighting.Invalidate(new((cx+100)*16,64,(cz+100)*16));
                world.Lighting.Invalidate(new((cx+2)*16,64,cz*16));
                prepared=world.Lighting.PreparePage(cx,cz,.05);
            }
            C(prepared,"Unrelated streaming arrivals starved incremental lighting.");
            var source=world.GetChunk(cx,cz);var torch=new Point3(cx*16+8,125,cz*16+8);var furnace=torch+new Point3(2,0,0);
            world.SetBlock(torch.X,torch.Y,torch.Z,BlockType.Torch);world.SetBlock(furnace.X,furnace.Y,furnace.Z,BlockType.Furnace);
            world.Containers.Get(furnace).Restore(new BlockEntityState(furnace,BlockType.Furnace,new ItemStack[3],Burn:40,BurnTotal:80));world.Lighting.Invalidate(furnace);
            byte[] expectedLight=(byte[])world.Lighting.GetPage(cx,cz).Data.Clone();
            for(int repeat=0;repeat<3;repeat++)
            {
                var snapshot=world.MeshSnapshot(source,false);
                byte[] workerLight=System.Threading.Tasks.Task.Run(()=>
                {try{return (byte[])snapshot.Lighting.GetPage(0,0).Data.Clone();}finally{snapshot.Lighting.ReleaseWorkerScratch();}}).GetAwaiter().GetResult();
                C(workerLight.SequenceEqual(expectedLight),"Asynchronous/pooled lighting changed sunlight, Torch or burning-Furnace illumination.");
            }
            var settings=new GameSettings();var commands=new WorldCommands();var position=session.Player.AbsolutePosition;
            C(!settings.LegacyVisualPrecision&&Camera.PrecisionOffset(position,false)==Vector3.Zero,"Modern rendering lost its precision default.");
            C(commands.Execute("/precision legacy",session,settings).Success&&settings.LegacyVisualPrecision,"Legacy visual mode was inaccessible.");
            var eye=position with{X=12550824.25,Y=129.62,Z=-12550824.25};Vector3 offset=Camera.PrecisionOffset(eye,true);
            C(Math.Abs(offset.X+.25f)<.00001&&Math.Abs(offset.Z-.25f)<.00001,"Legacy camera narrowing did not snap at the expected float boundary.");
            C(session.Player.AbsolutePosition==position,"Legacy presentation altered simulation coordinates.");
            var store=new SettingsStore(Path.Combine(root,"precision-settings.json"));store.Save(settings);C(store.Load().LegacyVisualPrecision,"Legacy precision preference did not persist.");
            C(commands.Execute("/precision modern",session,settings).Success&&!settings.LegacyVisualPrecision,"Precise rendering could not be restored.");
        }
        finally
        {
            if(session!=null)session.CloseStreaming();else world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(Path.GetDirectoryName(Path.GetFullPath(root))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe correction cleanup.");if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
    private static void CheckpointSafety()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".infinite-recovery-"+Guid.NewGuid().ToString("N"));GameSession session=null;string blocked=null;
        Directory.CreateDirectory(root);
        try
        {
            var manager=new WorldSaveManager(root);var made=manager.CreateInfinite(42,"Recovery",true,Difficulty.Hard,GameMode.Creative);session=new(made.Id,made.Save);
            Pump(session.World,new(0,0),()=>session.World.IsLoaded(new(-16,0,-16))&&session.World.IsLoaded(new(16,0,16)));
            session.World.SetBlock(1,125,1,BlockType.Glass);
            session.Inventory.SetSlot(0,new(Items.IronIngot,17));var checkpoint=manager.BeginInfiniteSave(made.Id,session.Capture());
            session.Inventory.SetSlot(0,new(Items.IronIngot,19));checkpoint.GetAwaiter().GetResult();session.World.ChunkLifecycle.FinishCheckpoint(true);
            var snapshot=manager.Load(made.Id);C(snapshot.Inventory[0].Count==17,"Background save observed mutable inventory after capture.");snapshot.World.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            string directory=Path.Combine(root,made.Id),file=Path.Combine(directory,WorldSaveManager.FileName);byte[] original=File.ReadAllBytes(file);
            blocked=file+".tmp";Directory.CreateDirectory(blocked);bool failed=false;
            try{manager.Save(made.Id,session.Capture());}catch(Exception e)when(e is IOException or UnauthorizedAccessException){failed=true;}
            C(failed&&File.ReadAllBytes(file).SequenceEqual(original),"Failed checkpoint replaced the previously valid manifest.");
            C(File.Exists(Path.Combine(directory,"checkpoint.pending","plan")),"Failed commit lost its recoverable checkpoint plan.");
            Directory.Delete(blocked,false);blocked=null;
            string journal=Path.Combine(directory,"checkpoint.pending","chunks");byte[] journalBytes=File.ReadAllBytes(journal);C(journalBytes.Length>32768,"Checkpoint did not journal actual chunk records.");
            var damaged=(byte[])journalBytes.Clone();damaged[^1]^=1;File.WriteAllBytes(journal,damaged);bool journalRejected=false;
            try{manager.Load(made.Id);}catch(InvalidDataException){journalRejected=true;}
            C(journalRejected&&File.ReadAllBytes(file).SequenceEqual(original),"Corrupt checkpoint journal was accepted or changed the manifest.");File.WriteAllBytes(journal,journalBytes);
            var recovered=manager.Load(made.Id);C(recovered.Inventory[0].Count==19&&recovered.World.SavedDifficulty==Difficulty.Hard,"Prepared checkpoint recovery lost state.");recovered.World.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            C(!File.Exists(Path.Combine(directory,"checkpoint.pending","plan")),"Recovered checkpoint was left active for duplicate replay.");
            blocked=file+".tmp";Directory.CreateDirectory(blocked);
            try{manager.Save(made.Id,session.Capture());}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
            Directory.Delete(blocked,false);blocked=null;
            string pending=Path.Combine(directory,"checkpoint.pending"),plan=Path.Combine(pending,"plan");journalBytes=File.ReadAllBytes(Path.Combine(pending,"chunks"));
            using(var document=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(plan)))
            {
                var legacy=new System.Collections.Generic.List<object>();int index=0;
                foreach(var entry in document.RootElement.EnumerateArray())
                {
                    string name=$"legacy-{index++}.chunk";int offset=checked((int)entry.GetProperty("Offset").GetInt64()),length=entry.GetProperty("Length").GetInt32();
                    byte[] bytes=journalBytes.AsSpan(offset,length).ToArray();File.WriteAllBytes(Path.Combine(pending,name),bytes);
                    legacy.Add(new{File=name,Hash=System.Security.Cryptography.SHA256.HashData(bytes)});
                }
                File.WriteAllBytes(plan,System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(legacy));
            }
            var legacyRecovered=manager.Load(made.Id);Pump(legacyRecovered.World,new(0,0),()=>legacyRecovered.World.IsLoaded(new(1,125,1)));
            C(legacyRecovered.World.GetBlock(1,125,1)==BlockType.Glass&&legacyRecovered.Inventory[0].Count==19,"Legacy per-chunk recovery journal lost blocks or inventory.");legacyRecovered.World.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            manager.Save(made.Id,session.Capture());session.CloseStreaming();session=null;
            byte[] good=File.ReadAllBytes(file),bad=(byte[])good.Clone();bad[40]^=1;File.WriteAllBytes(file,bad);bool corrupt=false;
            try{manager.Load(made.Id);}catch(InvalidDataException){corrupt=true;}
            C(corrupt&&manager.ListWorlds().Single().Error!=null,"Corrupt Infinite manifest was accepted or crashed world listing.");File.WriteAllBytes(file,good);
            manager.Rename(made.Id,"Recovered Infinite");C(manager.ListWorlds().Single().Name=="Recovered Infinite","Infinite world rename lost its metadata path.");
            var empty=new ChunkSnapshot(new(-1,-1),BetaDensityGenerator.Version,new byte[32768]);C(ChunkSnapshot.Decode(empty.Address,empty.Encode()).Metadata==null,"Block-only snapshot gained an invalid empty metadata record.");
            C(new ChunkAddress(-100,500).ToString()=="-100, 500"&&new WorldPosition(1e12+.5,132,-1e12-.5).ToString().Length<100,"Coordinate diagnostics recursively formatted derived properties.");
            var bob=new ViewBobbing();var reference=new ViewBobbing();bob.UpdatePose(new(63,1,0),true,true,.1f);reference.UpdatePose(new(63,1,0),true,true,.1f);bob.Reframe(new(-64,0,0));bob.UpdatePose(new(-.5f,1,0),true,true,.1f);reference.UpdatePose(new(63.5f,1,0),true,true,.1f);C(bob.Offset==reference.Offset,"Floating-origin rebasing changed the walking bob cycle.");
            var invalid=new ChunkGameplay{Water=null};bool rejected=false;try{ChunkGameplay.Validate(new(0,0),invalid);}catch(InvalidDataException){rejected=true;}C(rejected,"Null streamed sections bypassed validation.");
        }
        finally{if(blocked!=null&&Directory.Exists(blocked))Directory.Delete(blocked,false);session?.CloseStreaming();if(Path.GetDirectoryName(Path.GetFullPath(root))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe recovery cleanup.");Directory.Delete(root,true);}
    }
    private static void Pump(World world,ChunkAddress center,Func<bool> done)
    {var timer=Stopwatch.StartNew();do{world.UpdateStreaming(center);if(world.ChunkLifecycle.Error!=null)throw world.ChunkLifecycle.Error;if(timer.Elapsed.TotalSeconds>60)throw new Exception("Streamed gameplay lifecycle stalled.");Thread.Sleep(1);}while(!done());}
    private static void SessionLifecycle()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),path=Path.Combine(parent,".infinite-session-"+Guid.NewGuid().ToString("N"));GameSession session=null;
        Directory.CreateDirectory(path);
        try
        {
            var manager=new WorldSaveManager(path);var made=manager.CreateInfinite(173,"Infinite lifecycle",true,Difficulty.Normal,GameMode.Creative);
            session=new GameSession(made.Id,made.Save);var world=session.World;
            Pump(world,new(0,0),()=>world.IsLoaded(new(16,0,16))&&world.IsLoaded(new(-16,0,-16)));
            for(int z=2;z<=13;z++)for(int x=2;x<=13;x++)for(int y=110;y<127;y++)world.SetBlock(x,y,z,y==110?BlockType.Stone:BlockType.Air);
            var chest=new Point3(4,111,4);C(world.SetBlock(chest.X,chest.Y,chest.Z,BlockType.Chest),"Chest fixture failed.");
            var tool=new ItemStack(Items.Tool(ToolTier.Iron,ToolKind.Pickaxe),1,117);world.Containers.Get(chest).SetSlot(0,tool);world.Containers.Get(chest).SetSlot(1,new(Items.IronIngot,23));
            world.SetBlock(6,111,4,BlockType.FarmlandWet);world.SetBlock(6,112,4,BlockType.Wheat1);
            world.SetBlock(7,110,4,BlockType.Dirt);world.SetBlock(7,111,4,BlockType.Sapling);
            world.SetBlock(11,112,4,BlockType.Leaves);world.Vegetation.MarkNatural(new(11,112,4));
            C(world.PlaceStructure(new(8,111,4),BlockType.Torch,0,new(0,-1,0),session.Player.Bounds),"Torch placement failed.");
            C(world.PlaceStructure(new(12,111,4),BlockType.Door,3,new(0,-1,0),session.Player.Bounds),"Door placement failed.");
            C(world.PlaceBuilding(new(9,111,4),BlockType.Sign,8,false,session.Player.Bounds,new(0,-1,0))&&world.EditSign(new(9,111,4),"Streamed text"),"Sign metadata fixture failed.");
            world.SetBlock(10,119,4,BlockType.Sand);for(int i=0;i<16&&world.Gravity.Active.Count==0;i++)world.Gravity.Tick();C(world.Gravity.Active.Count>0,"Falling object did not start.");
            C(session.Items.TrySpawn(new(Items.IronIngot,13),new(5.5f,111,9.5f),.9f),"Dropped stack fixture failed.");
            session.Mobs.AttachChunk(new[]{new LocatedMob(new(MobSpecies.Pig,Vector3.Zero,1,7),new(10.5,111,10.5))},Array.Empty<MobRespawnState>());
            var pig=session.Mobs.Entities.Last(m=>m.Species==MobSpecies.Pig);C(session.Mobs.Hit(pig,7,session.Player.Position,session.Items),"Pending replacement fixture failed.");
            int pending=session.Mobs.PendingRespawns.Count;C(pending>0,"Natural death did not schedule a replacement.");
            session.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.WoodenPlanks),17));session.Inventory.SetSlot(5,tool);session.Inventory.SelectedSlot=5;
            session.Player.SetAbsolute(new(8.5,111,12.5));session.Player.RestoreFraction(17.25);world.Time.Restore(471);
            double growth=world.CaptureGameplay(new(0,0)).Vegetation.Saplings.Single().Remaining;
            int totalDrops=session.Items.Entities.Sum(i=>i.Count);
            C(session.Hostiles.TrySummon(MobSpecies.Zombie,new(4.5f,111,10.5f)),"Dormant hostile fixture failed.");
            world.UpdateStreaming(new(7,0));C(world.IsLoaded(new(6,112,4)),"Dormant boundary fixture retired too soon.");
            C(world.SetBlock(6,112,4,BlockType.Air),"Dormant neighbor edit failed.");
            session.ApplySettings(new GameSettings{GameMode=GameMode.Creative,Difficulty=Difficulty.Peaceful});
            world.UpdateStreaming(new(0,0));C(world.Farming.Count==0&&session.Hostiles.Entities.Count==0,"Dormant crop/hostile state was resurrected after an edit or Peaceful switch.");
            var far=new ChunkAddress(50,-50);Pump(world,far,()=>!world.IsLoaded(chest)&&world.IsLoaded(new(far.BlockX,0,far.BlockZ)));
            C(world.Containers.Get(chest)==null&&session.Items.Entities.Count==0,"Retired objects retained live ownership.");
            Pump(world,new(0,0),()=>world.IsLoaded(chest)&&world.IsLoaded(new(-16,0,-16))&&world.IsLoaded(new(16,0,16)));
            C(world.Containers.Get(chest)[0]==tool&&world.Containers.Get(chest)[1].Count==23,"Container contents lost on unload.");
            C(session.Items.Entities.Sum(i=>i.Count)==totalDrops&&session.Mobs.PendingRespawns.Count==pending,"Unload duplicated/lost entities or pending respawns.");
            C(Math.Abs(world.CaptureGameplay(new(0,0)).Vegetation.Saplings.Single().Remaining-growth)<1e-9,"Dormant growth timer advanced.");
            C(world.Signs[new(9,111,4)]=="Streamed text"&&world.Structure(new(12,111,4)).Facing%4==3&&world.Gravity.Active.Count==1,"Sign, orientation or active fall lost.");
            manager.Save(made.Id,session.Capture());session.CloseStreaming();session=null;
            C(manager.ListWorlds().Single().Size==WorldSize.Infinite,"World selection did not recognize Infinite metadata.");
            var save=manager.Load(made.Id);session=new GameSession(made.Id,save);world=session.World;Pump(world,new(0,0),()=>world.IsLoaded(chest)&&world.IsLoaded(new(-16,0,-16))&&world.IsLoaded(new(16,0,16)));
            C(session.Inventory[0].Count==17&&session.Inventory[5]==tool&&session.Inventory.SelectedSlot==5&&session.Player.HealthValue==17.25&&world.Time.Seconds==471,"Manifest lost player/time/item metadata.");
            C(world.Containers.Get(chest)[0]==tool&&session.Items.Entities.Sum(i=>i.Count)==totalDrops&&world.Gravity.Active.Count==1&&session.Mobs.PendingRespawns.Count==pending,"Checkpoint lost gameplay state.");
            var commands=new WorldCommands();C(commands.Execute("/tp @p 12550824.5 132 -12550824.5",session,new GameSettings{GameMode=GameMode.Creative}).Success,"Wide teleport command rejected.");
            var watch=Stopwatch.StartNew();while(!session.AdvanceStreaming(out _)){if(watch.Elapsed.TotalSeconds>60)throw new Exception("Teleport streaming stalled: "+session.StreamingNotice);Thread.Sleep(1);}
            C(session.Player.AbsolutePosition==new WorldPosition(12550824.5,132,-12550824.5),"Teleport rounded absolute coordinates.");
            C(world.ChunkCount<=world.ChunkLifecycle.MaximumResident,"Extended session exceeded its resident bound.");
            manager.Save(made.Id,session.Capture());session.CloseStreaming();session=null;
            save=manager.Load(made.Id);C(save.ExactPosition==new WorldPosition(12550824.5,132,-12550824.5),"Manifest lost Far Lands position.");
            session=new(made.Id,save);var loadClock=Stopwatch.StartNew();while(!session.AdvanceStreaming(out _)){if(loadClock.Elapsed.TotalSeconds>60)throw new Exception("Death fixture could not load.");Thread.Sleep(1);}
            C(session.Player.Kill()&&session.HandleDeath()&&session.Items.Entities.Sum(i=>i.Count)==18,"Infinite death did not preserve the inventory quantity at its distant location.");
            manager.Save(made.Id,session.Capture());session.CloseStreaming();session=null;save=manager.Load(made.Id);session=new(made.Id,save);
            C(session.Player.Health==20&&session.Player.AbsolutePosition==session.World.InfiniteSpawn&&Enumerable.Range(0,36).All(i=>session.Inventory[i].IsEmpty),"Dead Infinite save did not respawn safely with an empty inventory.");
            C(session.RequestTeleport(new(12550824.5,132,-12550824.5)),"Return to death location failed.");loadClock.Restart();while(!session.AdvanceStreaming(out _)){if(loadClock.Elapsed.TotalSeconds>60)throw new Exception("Death drop reload stalled.");Thread.Sleep(1);}
            C(session.Items.Entities.Sum(i=>i.Count)==18,"Death drops vanished or duplicated on respawn/reload.");
        }
        finally{session?.CloseStreaming();if(Path.GetDirectoryName(Path.GetFullPath(path))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe session cleanup.");Directory.Delete(path,true);}
    }
    private static void CompleteGeneration()
    {
        var generator=new BetaDensityGenerator(173);var clock=Stopwatch.StartNew();int ores=0,plants=0;
        foreach(var a in new[]{new ChunkAddress(0,0),new(-1,0),new(7,11),new(-15,-22),new(784426,-784427)})
        {
            var first=generator.GenerateComplete(a);var second=new BetaDensityGenerator(173).GenerateComplete(a);
            C(first.Blocks.SequenceEqual(second.Blocks)&&first.Metadata.SequenceEqual(second.Metadata),"Feature generation changed with request order.");
            var state=StreamingJson.Decode<ChunkGameplay>(first.Metadata);ChunkGameplay.Validate(a,state);
            for(int z=0;z<16;z++)for(int x=0;x<16;x++)for(int y=1;y<128;y++)
            {var b=(BlockType)first.Blocks[Coordinates.Index(x,y,z)];if(Blocks.Falls(b))C(Blocks.IsSolid((BlockType)first.Blocks[Coordinates.Index(x,y-1,z)]),"Generated gravity block lacks support.");if(b is BlockType.CoalOre or BlockType.IronOre)C(y>=4&&y<=60,"Ore escaped its Y range.");if(b==BlockType.DiamondOre)C(y>=4&&y<=20,"Diamond escaped Y range.");if(b is BlockType.CoalOre or BlockType.IronOre or BlockType.DiamondOre)ores++;if(b is BlockType.Leaves or BlockType.TallGrass or BlockType.Snow or BlockType.Cactus)plants++;}
        }
        C(ores>0&&plants>0,"Infinite feature passes produced no ore/vegetation.");Console.WriteLine($"Complete generation: 10 chunk builds {clock.Elapsed.TotalMilliseconds:F0} ms; {ores} sampled ore cells, {plants} vegetation cells.");
    }
    private static void WorldAccess()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),path=Path.Combine(parent,".infinite-integration-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            foreach(long location in new[]{-12550824L,12550824L,1000000000000L,Coordinates.MaximumBlock-8,-Coordinates.MaximumBlock+8})
            {
                var world=World.OpenStreamed(173,Path.Combine(path,location.ToString(System.Globalization.CultureInfo.InvariantCulture)),2,0,0);
                var address=ChunkAddress.At(location,location);long bx=address.BlockX,bz=address.BlockZ;world.SetOrigin(bx,bz);
                var timer=Stopwatch.StartNew();
                while(!world.IsLoaded(new(bx+8,125,bz+8)))
                {world.UpdateStreaming(address);if(timer.Elapsed.TotalSeconds>20)throw new Exception("Live World did not receive its chunk.");Thread.Sleep(1);}
                C(world.IsInfinite&&world.ChunkCount<=25,"Sparse world allocated a finite array or unbounded storage.");
                C(world.Revision>0,"Publishing terrain did not invalidate cached mob collision queries.");
                for(int z=3;z<=13;z++)for(int x=3;x<=13;x++)for(int y=124;y<128;y++)world.SetBlock(bx+x,y,bz+z,y==125?BlockType.Stone:BlockType.Air);
                world.SetBlock(bx+8,127,bz+5,BlockType.Glass);
                var player=new Player(new(8.5f,126,8.5f));player.Bind(world);
                for(int i=0;i<120;i++)player.Update(world,Vector3.Zero,false,1f/120);
                C(player.Grounded && Math.Abs(player.AbsolutePosition.X-(bx+8.5))<.001 && player.Health==20,"High-coordinate standing/collision drifted.");
                var hit=BlockRaycaster.Cast(world,player.EyePosition,-Vector3.UnitZ);
                C(hit.HasValue && hit.Value.Cell.X==bx+8 && hit.Value.Cell.Z==bz+5,"Ray targeting used lossy absolute float coordinates.");
                WorldPosition before=player.AbsolutePosition;world.SetOrigin(bx+16,bz+16);
                C(player.AbsolutePosition==before && player.Position.X<0,"Reframing moved the authoritative actor position.");
                for(int i=0;i<60;i++)player.Update(world,Vector3.Zero,false,1f/120);
                C(player.Health==20&&player.Grounded&&player.AbsolutePosition==before,"Negative local-frame collision changed position/health.");
                long away=location>0?-1000:1000;var distant=new Point3(bx+away,120,bz+away);C(world.GetBlock(distant.X,distant.Y,distant.Z)==BlockType.Bedrock && !world.SetBlock(distant.X,distant.Y,distant.Z,BlockType.Dirt),"Unloaded terrain acted like writable Air.");
                world.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
                var restored=World.OpenStreamed(173,Path.Combine(path,location.ToString(System.Globalization.CultureInfo.InvariantCulture)),2,0,0);timer.Restart();
                while(!restored.IsLoaded(new(bx+8,127,bz+5)))
                {restored.UpdateStreaming(address);if(timer.Elapsed.TotalSeconds>20)throw new Exception("Live World reload stalled.");Thread.Sleep(1);}
                C(restored.GetBlock(bx+8,127,bz+5)==BlockType.Glass,"Live World mutation was not persisted by the shared backing store.");
                restored.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally{if(Path.GetDirectoryName(Path.GetFullPath(path))!=Path.TrimEndingDirectorySeparator(parent))throw new Exception("Unsafe integration cleanup.");Directory.Delete(path,true);}
    }
}
