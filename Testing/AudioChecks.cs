using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Audio;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class AudioChecks
{
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Audio asset coverage, WAV signatures and unchanged build content copies",Assets),
        ("Bounded allocation-free audio queue, elapsed rate limits and positional category mixing",Mixing),
        ("Successful mining/placement, paired structures, health, mobs, pickup and weapon sound triggers",Triggers),
        ("Distance-based footsteps, wall/air silence, jump/landing, swimming and FPS independence",Movement),
        ("Persistent audio volumes, old/invalid settings, responsive Sounds sliders and save isolation",Settings)
    };
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static SoundEvent[] Drain(SoundEvents events)
    {var sounds=new List<SoundEvent>();while(events.TryDequeue(out var sound))sounds.Add(sound);return sounds.ToArray();}
    private static int Count(SoundEvents events,SoundKind kind)=>Drain(events).Count(s=>SoundCatalog.Groups[s.Group].Kind==kind);
    private static GameSession Session()
    {
        var world=new World(173,WorldSize.Small,false);
        for(int z=0;z<52;z++)for(int x=0;x<52;x++)world.SetGeneratedBlock(x,0,z,BlockType.Grass);
        return new("audio",new(world,"Audio checks",new(40,1,40),0,0));
    }
    public static int Run()
    {
        int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failures}/{All.Length} audio checks passed.");return failures==0?0:1;
    }
    public static void Assets()
    {
        string root=AppContext.BaseDirectory;while(!File.Exists(Path.Combine(root,"MDSM.csproj")))root=Directory.GetParent(root).FullName;
        string mgcb=File.ReadAllText(Path.Combine(root,"Content/Content.mgcb"));int total=0;long size=0;
        foreach(var group in SoundCatalog.Groups)for(int variant=0;variant<group.Variations;variant++)
        {
            string asset=group.Asset(variant);byte[] source=File.ReadAllBytes(Path.Combine(root,"Content",asset));
            byte[] built=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Content",asset));
            Check(source.AsSpan().SequenceEqual(built) && mgcb.Contains("/copy:"+asset),"WAV not copied unchanged: "+asset);
            // Replacement recordings may use different encodings, metadata chunks,
            // envelopes and levels, or reuse an asset across multiple events. The
            // runtime checks validate decoding/playback through MonoGame itself.
            Check(source.Length>=12 && System.Text.Encoding.ASCII.GetString(source,0,4)=="RIFF"
                && System.Text.Encoding.ASCII.GetString(source,8,4)=="WAVE","Invalid WAV signature: "+asset);
            total++;size+=source.Length;
        }
        foreach(BlockType block in Enum.GetValues<BlockType>())if(Blocks.IsRenderable(block) && !Lava.Fluid(block))
            foreach(SoundKind kind in new[]{SoundKind.Hit,SoundKind.Break,SoundKind.Place,SoundKind.Step,SoundKind.Land})
                Check(SoundCatalog.Groups[SoundCatalog.Find(kind,BlockSounds.Material(block))].Variations>=2,"Uncovered block material.");
        foreach(MobSpecies species in Enum.GetValues<MobSpecies>())foreach(SoundKind kind in new[]{SoundKind.MobIdle,SoundKind.MobHurt,SoundKind.MobDeath})
            Check(SoundCatalog.Groups[SoundCatalog.Find(kind,species:(int)species)].Species==(int)species,"Uncovered species sound.");
        Console.WriteLine($"  {total} WAV assets, {size:N0} bytes; {SoundCatalog.Groups.Length} groups; all source/build copies match.");
    }
    public static void Mixing()
    {
        var settings=new GameSettings();var step=new SoundEvent(SoundCatalog.Find(SoundKind.Step),new(3,0,0),true);
        var right=SoundSpatial.Calculate(step,settings,Vector3.Zero,Vector3.Right);
        var left=SoundSpatial.Calculate(step with {Position=new(-3,0,0)},settings,Vector3.Zero,Vector3.Right);
        var far=SoundSpatial.Calculate(step with {Position=new(100,0,0)},settings,Vector3.Zero,Vector3.Right);
        Check(right.Volume>0 && right.Pan>0 && left.Pan<0 && left.Volume==right.Volume && far.Volume==0,"Spatial pan/range incorrect.");
        Check(SoundSpatial.Calculate(step with{Position=new(10,0,0)},settings,Vector3.Zero,Vector3.Right).Volume<right.Volume,"Distance attenuation missing.");
        settings.EffectsVolume=0;Check(SoundSpatial.Calculate(step,settings,Vector3.Zero,Vector3.Right).Volume==0,"Effect category mute ignored.");
        var ui=new SoundEvent(SoundCatalog.Find(SoundKind.Click),default,false);
        Check(SoundSpatial.Calculate(ui,settings,new(100,100,100),Vector3.Right).Volume>0,"Effects mute also muted UI.");
        settings.MasterVolume=0;Check(SoundSpatial.Calculate(ui,settings,default,Vector3.Right).Volume==0,"Master mute ignored.");
        var events=new SoundEvents();events.SetListener(Vector3.Zero);
        for(int i=0;i<1000;i++)events.Block(SoundKind.Step,BlockType.Stone,Vector3.Zero);
        Check(events.Count==SoundEvents.Capacity && events.Discarded==1000-SoundEvents.Capacity,"Audio queue unbounded.");events.Local(SoundKind.PlayerDeath);
        Check(Drain(events).Any(s=>SoundCatalog.Groups[s.Group].Kind==SoundKind.PlayerDeath),"Critical sound lost to full step queue.");
        events.Block(SoundKind.Step,BlockType.Stone,new(1000,0,0));Check(events.Count==0,"Distant event queued.");
        for(int i=0;i<100;i++){events.Local(SoundKind.Click);events.TryDequeue(out _);}
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++){events.Local(SoundKind.Click);events.TryDequeue(out _);}
        Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"Audio requests allocate per update.");
        int? expected=null;
        foreach(int fps in new[]{30,60,144,360,2000})
        {
            var limiter=new SoundRateLimiter();var clock=new SimulationClock();int accepted=0;
            for(int frame=0;frame<fps;frame++)for(int tick=clock.Advance(1d/fps);tick>0;tick--)
            {limiter.Advance(SimulationClock.StepSeconds);if(limiter.Accept(step.Group))accepted++;}
            expected??=accepted;Check(accepted==expected && accepted<30,"Audio cadence depends on render FPS.");
        }
    }
    public static void Triggers()
    {
        var s=Session();Point3 support=new(20,1,20),torch=new(20,2,20);s.World.SetBlock(20,1,20,BlockType.Dirt);
        s.World.PlaceStructure(torch,BlockType.Torch,0,new(0,-1,0),s.Player.Bounds);s.Sounds.Clear();
        Check(s.Mining.Update(s.World,s.Items,new(support,default,1),true,2,s.Inventory),"Mining fixture failed.");
        var sounds=Drain(s.Sounds);Check(sounds.Count(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Break)==2 && sounds.Count(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Hit)==1,"Break/mining/support sounds missing or duplicated.");
        s.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.Dirt),2));
        Check(!BlockPlacement.TryPlace(s.World,s.Inventory,new Point3(40,1,40),s.Player.Bounds) && s.Sounds.Count==0,"Rejected placement made sound.");
        Check(BlockPlacement.TryPlace(s.World,s.Inventory,support,s.Player.Bounds) && Count(s.Sounds,SoundKind.Place)==1,"Successful placement lacked sound.");
        s.World.PlaceStructure(torch,BlockType.Door,0,new(0,-1,0),s.Player.Bounds);s.Sounds.Clear();
        s.World.Toggle(new(20,3,20));Check(Count(s.Sounds,SoundKind.DoorOpen)==1,"Upper Door open sound duplicated.");
        s.World.Toggle(torch);Check(Count(s.Sounds,SoundKind.DoorClose)==1,"Door close sound missing.");
        Check(new MiningController().Update(s.World,s.Items,new(new(20,3,20),default,1),true,5,s.Inventory),"Upper Door mining failed.");
        Check(Count(s.Sounds,SoundKind.Break)==1,"Paired Door emitted two break sounds.");
        s.Player.TakeDamage(1);s.Player.TakeDamage(1);Check(Count(s.Sounds,SoundKind.PlayerHurt)==1,"Rejected damage produced sound.");
        s.Player.TakeDamage(100,false);s.Player.TakeDamage(100,false);Check(Count(s.Sounds,SoundKind.PlayerDeath)==1,"Repeated death sound.");
        foreach(MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            var definition=MobDefinitions.Get(species);var state=new MobState(species,new(25,1,25),0,definition.MaximumHealth);
            var mob=new PassiveMob(state,17);var control=new PassiveMob(state,17);var w=Session().World;var quiet=new World(173,WorldSize.Small,false);
            for(int z=0;z<52;z++)for(int x=0;x<52;x++)quiet.SetGeneratedBlock(x,0,z,BlockType.Grass);
            w.Sounds.SetListener(state.Position);for(int tick=0;tick<1500;tick++){mob.Update(w,1f/120);control.Update(quiet,1f/120);}
            Check(mob.Position==control.Position && mob.Yaw==control.Yaw,"Audio altered mob AI randomness or movement.");
            Check(Drain(w.Sounds).Any(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.MobIdle && SoundCatalog.Groups[e.Group].Species==(int)species),"Species idle sound missing.");
            var population=(int)species<4?new PassiveMobPopulation(w,new[]{state}):null;
            var hostiles=(int)species>=4?new HostilePopulation(w,new[]{state}):null;
            var target=population?.Entities[0]??hostiles.Entities[0];var drops=new DroppedItems(w);
            bool Hit(int damage)=>population!=null?population.Hit(target,damage,Vector3.Zero,drops):hostiles.Hit(target,damage,Vector3.Zero,drops);
            Check(Hit(1) && Count(w.Sounds,SoundKind.MobHurt)==1,"Species hurt sound missing.");
            Check(Hit(100) && !Hit(100) && Count(w.Sounds,SoundKind.MobDeath)==1,"Species death sound duplicated.");
        }
        s=Session();s.Items.Spawn(Items.Coal,2,s.Player.Position+Vector3.Up,0);s.Items.Spawn(Items.IronIngot,1,s.Player.Position+Vector3.Up,0);
        s.Items.Update(s.Player,s.Inventory,.01f);Check(Count(s.Sounds,SoundKind.Pickup)==1,"Pickup burst was duplicated or silent.");
        for(int slot=0;slot<36;slot++)s.Inventory.SetSlot(slot,new(Items.Coal,64));s.Items.Spawn(Items.IronIngot,1,s.Player.Position+Vector3.Up,0);
        s.Items.Update(s.Player,s.Inventory,.01f);Check(Count(s.Sounds,SoundKind.Pickup)==0,"Full inventory generated pickup sound.");
        s=Session();s.Inventory.SetSlot(0,new(Items.Bow,1));s.Bow.Update(s.Inventory,s.Projectiles,new(20,2.5f,20.5f),Vector3.Right,true,.2f);
        Check(s.Sounds.Count==0,"Empty bow made draw sound.");s.Inventory.SetSlot(1,new(Items.Arrow,2));
        s.Bow.Update(s.Inventory,s.Projectiles,new(20,2.5f,20.5f),Vector3.Right,true,.5f);s.Bow.Update(s.Inventory,s.Projectiles,new(20,2.5f,20.5f),Vector3.Right,true,.5f);
        Check(Count(s.Sounds,SoundKind.BowDraw)==1,"Held bow repeated draw sound.");s.Bow.Update(s.Inventory,s.Projectiles,new(20,2.5f,20.5f),Vector3.Right,false,0);
        Check(Count(s.Sounds,SoundKind.BowShoot)==1,"Successful shot made no sound.");s.World.SetBlock(25,2,20,BlockType.Stone);
        s.Projectiles.Update(.2f,s.Mobs,s.Items,_=>{});Check(Count(s.Sounds,SoundKind.ArrowHit)==1,"Arrow impact sound missing.");
        s=Session();s.World.SetBlock(20,1,20,BlockType.Bedrock);var blocked=new MiningController();
        for(int tick=0;tick<120;tick++)blocked.Update(s.World,s.Items,new(new(20,1,20),default,1),true,1f/120);
        Check(Count(s.Sounds,SoundKind.Hit) is >=4 and <=5 && s.World.GetBlock(20,1,20)==BlockType.Bedrock && s.Items.Entities.Count==0,"Unbreakable hitting feedback changed mining or flooded sounds.");
        s.Inventory.SetSlot(0,new(Items.Apple,1));Check(!ItemUse.TryConsume(s.Player,s.Inventory,true,false) && s.Sounds.Count==0,"Full-health food made eating sound.");
        s.Player.TakeDamage(5,false);s.Sounds.Clear();Check(ItemUse.TryConsume(s.Player,s.Inventory,true,false) && Count(s.Sounds,SoundKind.Eat)==1,"Successful eating sound missing.");
        s.Inventory.SetSlot(0,new(Items.Tool(ToolTier.Wood,ToolKind.Hoe),1));Check(s.World.Farming.Till(new(25,0,25),s.Inventory) && Count(s.Sounds,SoundKind.Till)==1,"Tilling sound missing.");
        s.Inventory.SetSlot(0,new(Items.WheatSeeds,1));Check(s.World.Farming.Plant(new(25,0,25),s.Inventory) && Count(s.Sounds,SoundKind.Plant)==1,"Planting sound missing.");
        s.World.SetBlock(25,1,23,BlockType.Chest);Check(s.TryOpenContainer(new(new(25,1,23),default,1)) && Count(s.Sounds,SoundKind.ChestOpen)==1,"Chest opening sound missing.");
        s.CloseInterface();Check(Count(s.Sounds,SoundKind.ChestClose)==1,"Chest closing sound missing.");
        s.Inventory.SetSlot(0,new(Items.Tool(ToolTier.Wood,ToolKind.Axe),1,1));s.Inventory.WearSelected();Check(Count(s.Sounds,SoundKind.ToolBreak)==1,"Final tool wear sound missing.");
        s.World.SetBlock(25,1,25,BlockType.Furnace);var furnace=s.World.Containers.Get(new(25,1,25));furnace.SetSlot(0,new(Items.FromBlock(BlockType.Sand),1));furnace.SetSlot(1,new(Items.Coal,1));
        s.World.Containers.Update(.1);Check(Count(s.Sounds,SoundKind.Ignite)==1,"Furnace ignition missing.");s.World.Containers.Update(10);Check(Count(s.Sounds,SoundKind.Smelt)==1,"Smelting completion missing.");
        s.World.SetBlock(30,1,30,BlockType.WaterSource);s.World.SetBlock(31,1,30,BlockType.LavaSource);Check(Count(s.Sounds,SoundKind.Fizz)==1,"Fluid contact did not hiss exactly once.");
    }
    public static void Movement()
    {
        int? expected=null;
        foreach(int fps in new[]{30,60,144,360,2000})
        {
            var s=Session();var player=new Player(new(10,1,10)){Sounds=s.Sounds};var motion=new PlayerAudio();motion.Update(player,s.World);var clock=new SimulationClock();
            for(int frame=0;frame<fps*2;frame++)for(int tick=clock.Advance(1d/fps);tick>0;tick--){player.Update(s.World,Vector3.Right,false,1f/120);motion.Update(player,s.World);}
            var sounds=Drain(s.Sounds);int steps=sounds.Count(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Step);expected??=steps;
            Check(steps==expected && steps is >=4 and <=5 && sounds.Where(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Step).All(e=>SoundCatalog.Groups[e.Group].Material==SoundMaterial.Grass),"Distance/material footstep cadence differs across FPS.");
            for(int tick=0;tick<120;tick++){player.Update(s.World,Vector3.Zero,false,1f/120);motion.Update(player,s.World);}s.Sounds.Clear();
            player.Update(s.World,Vector3.Zero,true,1f/120);motion.Update(player,s.World);
            for(int tick=0;tick<240;tick++){player.Update(s.World,Vector3.Zero,false,1f/120);motion.Update(player,s.World);}
            sounds=Drain(s.Sounds);Check(sounds.Count(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Jump)==1 && sounds.Count(e=>SoundCatalog.Groups[e.Group].Kind==SoundKind.Land)==1 && sounds.All(e=>SoundCatalog.Groups[e.Group].Kind!=SoundKind.Step),"Jump/landing audio or airborne silence incorrect.");
        }
        var water=Session();var swimmer=new Player(new(20.5f,1,20.5f)){Sounds=water.Sounds};var swim=new PlayerAudio();swim.Update(swimmer,water.World);
        for(int y=1;y<4;y++)for(int z=19;z<23;z++)for(int x=19;x<26;x++)water.World.SetBlock(x,y,z,BlockType.WaterSource);
        swim.Update(swimmer,water.World);Check(Count(water.Sounds,SoundKind.WaterEnter)==1,"Water entering missing.");
        for(int tick=0;tick<120;tick++){swimmer.Update(water.World,Vector3.Right,false,1f/120);swim.Update(swimmer,water.World);}
        Check(Count(water.Sounds,SoundKind.Swim)>0,"Actual swim movement made no sound.");
        for(int y=1;y<4;y++)for(int z=19;z<23;z++)for(int x=19;x<26;x++)water.World.SetBlock(x,y,z,BlockType.Air);
        swim.Update(swimmer,water.World);Check(Count(water.Sounds,SoundKind.WaterExit)==1,"Water exiting missing.");
        var wall=Session();for(int y=1;y<4;y++)wall.World.SetBlock(21,y,20,BlockType.Stone);
        var stuck=new Player(new(20.7f,1,20.5f));var observer=new PlayerAudio();observer.Update(stuck,wall.World);
        for(int tick=0;tick<120;tick++){stuck.Update(wall.World,Vector3.Right,false,1f/120);observer.Update(stuck,wall.World);}
        Check(Count(wall.Sounds,SoundKind.Step)==0,"Keyboard against a wall made footsteps.");
    }
    public static void Settings()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".audio-settings-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));var settings=store.Load();Check(settings.MasterVolume==.8f && settings.MobVolume==1,"Volume defaults incorrect.");
            settings.FpsCap=240;settings.MasterVolume=.2f;settings.EffectsVolume=.3f;settings.MobVolume=.4f;settings.InterfaceVolume=.5f;store.Save(settings);
            var loaded=store.Load();Check(loaded.MasterVolume==.2f && loaded.EffectsVolume==.3f && loaded.MobVolume==.4f && loaded.InterfaceVolume==.5f && loaded.FpsCap==240,"Volumes did not persist independently.");
            File.WriteAllText(store.FilePath,"{\"MasterVolume\":\"invalid\",\"EffectsVolume\":5,\"MobVolume\":-2,\"InterfaceVolume\":1e400}");loaded=store.Load();
            Check(loaded.MasterVolume==.8f && loaded.EffectsVolume==1 && loaded.MobVolume==0 && loaded.InterfaceVolume==1,"Invalid volume values unsafe.");
            File.WriteAllText(store.FilePath,"{\"FpsCap\":144}");Check(store.Load().MasterVolume==.8f && store.Load().FpsCap==144,"Old settings incompatible.");
            foreach(Point size in new[]{new Point(320,240),new(480,300),new(1280,720),new(1920,1080)})
            {
                var viewport=new Viewport(0,0,size.X,size.Y);Rectangle last=default;
                for(int row=0;row<4;row++)
                {var slider=SoundSettingsLayout.Slider(viewport,row);Check(slider.Left>=0 && slider.Right<=size.X && (row%2==0?slider.Top>last.Bottom:slider.Left>last.Right && slider.Y==last.Y),"Volume sliders overlap or escape viewport.");
                    Check(SoundSettingsLayout.ValueAt(slider,slider.Left)==0 && SoundSettingsLayout.ValueAt(slider,slider.Right)==1,"Slider endpoints wrong.");last=slider;}
                Check(SoundSettingsLayout.Back(viewport).Bottom<=size.Y && SoundSettingsLayout.Back(viewport).Top>last.Bottom,"Volume Back clipped.");
            }
            var session=Session();using var a=new MemoryStream();WorldSerializer.Write(a,session.Capture());session.Sounds.Local(SoundKind.PlayerHurt);using var b=new MemoryStream();WorldSerializer.Write(b,session.Capture());
            Check(a.ToArray().AsSpan().SequenceEqual(b.ToArray()),"Transient audio entered world saves.");
        }
        finally{Check(Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent),"Unsafe test cleanup.");Directory.Delete(root,true);}
        using var missing=new AudioManager("missing",new GameSettings(),_=>throw new FileNotFoundException());
        Check(missing.LoadedClips==0 && missing.MissingClips==SoundCatalog.Groups.Sum(g=>g.Variations) && !missing.Play(new(SoundCatalog.Find(SoundKind.Click),default,false),default,Vector3.Right),"Missing assets crashed or played.");
    }
}
