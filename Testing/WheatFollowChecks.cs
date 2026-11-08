using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WheatFollowChecks
{
    private const float Dt=(float)SimulationClock.StepSeconds;
    private static readonly Vector3 Start=new(40.5f,1,40.5f),Target=new(48.5f,1,40.5f);
    internal static readonly (string Name,Action Check)[] All=
    {
        ("All passive species/babies follow Wheat, face correctly and stop without crowding",Following),
        ("Inclusive 10-block 3D range, selected Wheat only, removal and ordinary AI recovery",Conditions),
        ("Wheat steering preserves collision, one-block hopping, breeding and fixed-step rates",Movement),
        ("Following preserves item/population/save state and avoids per-tick allocations",StateAndBudget)
    };
    private static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static PassiveMobPopulation Population(World world,MobSpecies species=MobSpecies.Pig,Vector3? position=null,bool baby=false)
        =>new(world,new[]{new MobState(species,position??Start,MathHelper.PiOver2,MobDefinitions.Get(species).MaximumHealth,BabySeconds:baby?900:0)});
    public static void Following()
    {
        var world=PigChecks.Flat();
        for(int species=0;species<MobDefinitions.SpeciesCount;species++)foreach(bool baby in new[]{false,true})
        {
            var population=Population(world,(MobSpecies)species,baby:baby);var mob=population.Entities[0];
            population.Update(Dt,Target,heldItem:Items.Wheat);
            Check(mob.Position.X>Start.X && Math.Abs(mob.Position.Z-Start.Z)<.00001f && mob.Walking,"Species did not follow Wheat: "+mob.Species);
            Check(Math.Abs(mob.Yaw+MathHelper.PiOver2)<.00001f,"Follower faces away from its movement.");
            for(int i=0;i<2400;i++)population.Update(Dt,Target,heldItem:Items.Wheat);
            float distance=Vector3.Distance(mob.Position,Target);
            Check(distance>=1.98f && distance<=2.01f,"Follower failed to stop at a safe distance: "+mob.Species);
            Vector3 stopped=mob.Position;float phase=mob.WalkPhase;
            for(int i=0;i<1200;i++)population.Update(Dt,Target,heldItem:Items.Wheat);
            Check(mob.Position==stopped && !mob.Walking && mob.WalkPhase==phase,"Nearby follower wandered or kept animating.");
            Check(mob.Health==mob.Definition.MaximumHealth && population.Entities.Count==1,"Following changed health or population.");
        }
    }
    public static void Conditions()
    {
        var world=PigChecks.Flat();
        foreach(Vector3 delta in new[]{new Vector3(10,0,0),new Vector3(8,6,0),new Vector3(0,0,10)})
        {
            var population=Population(world);population.Update(Dt,Start+delta,heldItem:Items.Wheat);
            Check(Vector2.Distance(new(Start.X,Start.Z),new(population.Entities[0].Position.X,population.Entities[0].Position.Z))>0,"Exact ten-block boundary was excluded.");
        }
        foreach(Vector3 delta in new[]{new Vector3(10.01f,0,0),new Vector3(8,6.01f,0),new Vector3(0,0,10.01f)})
        {
            var population=Population(world);population.Update(Dt,Start+delta,heldItem:Items.Wheat);
            Check(population.Entities[0].Position==Start,"Out-of-range or vertically distant mob followed.");
        }
        foreach(ItemId item in new[]{default(ItemId),Items.WheatSeeds,Items.Bread,Items.FromBlock(BlockType.TallGrass)})
        {
            var population=Population(world);population.Update(Dt,Target,heldItem:item);
            Check(population.Entities[0].Position==Start,"Non-Wheat item attracted a mob.");
        }
        var noPlayer=Population(world);noPlayer.Update(Dt,heldItem:Items.Wheat);Check(noPlayer.Entities[0].Position==Start,"Missing player created a lure target.");
        // Identical AI state must resume exactly the ordinary behavior when Wheat
        // is switched away, rather than retaining a stored pursuit target.
        var changed=Population(world);var control=Population(world);
        for(int i=0;i<120;i++){changed.Update(Dt,Target,heldItem:Items.Wheat);control.Update(Dt,Target,heldItem:Items.Wheat);}
        for(int i=0;i<600;i++)
        {changed.Update(Dt,Target,heldItem:Items.WheatSeeds);control.Update(Dt,Target);Check(changed.Entities[0].CaptureState()==control.Entities[0].CaptureState(),"Lure state remained after changing selected item.");}
        var outside=Population(world);control=Population(world);
        for(int i=0;i<120;i++){outside.Update(Dt,Target,heldItem:Items.Wheat);control.Update(Dt,Target,heldItem:Items.Wheat);}
        Vector3 far=Target+Vector3.Right*20;
        for(int i=0;i<120;i++){outside.Update(Dt,far,heldItem:Items.Wheat);control.Update(Dt,far);}
        Check(outside.Entities[0].CaptureState()==control.Entities[0].CaptureState(),"Mob retained a lure beyond ten blocks.");
    }
    public static void Movement()
    {
        var wall=PigChecks.Flat();for(int y=1;y<=5;y++)for(int z=35;z<=45;z++)wall.SetBlock(44,y,z,BlockType.Stone);
        for(int species=0;species<MobDefinitions.SpeciesCount;species++)
        {
            var population=Population(wall,(MobSpecies)species);var mob=population.Entities[0];
            for(int i=0;i<1800;i++)
            {population.Update(Dt,Target,heldItem:Items.Wheat);Check(!wall.IntersectsSolid(mob.Bounds) && mob.Bounds.Max.X<=44.001f,"Following passed through a wall.");}
        }
        var step=PigChecks.Flat();for(int z=35;z<=45;z++)step.SetBlock(44,1,z,BlockType.Stone);
        var jumper=Population(step);for(int i=0;i<1800;i++)jumper.Update(Dt,Target,heldItem:Items.Wheat);
        Check(jumper.Entities[0].Position.X>45 && !step.IntersectsSolid(jumper.Entities[0].Bounds),"Existing steering could not hop a one-block obstacle.");
        Vector3? reference=null;
        foreach(int fps in new[]{30,60,144,360})
        {
            var population=Population(step);var clock=new SimulationClock();
            for(int frame=0;frame<fps*4;frame++)for(int tick=clock.Advance(1d/fps);tick>0;tick--)population.Update(Dt,Target,heldItem:Items.Wheat);
            reference??=population.Entities[0].Position;Check(Vector3.Distance(reference.Value,population.Entities[0].Position)<.00001f,"Following depends on rendering frame rate.");
        }
        var breeding= new PassiveMobPopulation(step,new[]{new MobState(MobSpecies.Pig,Start,0,10,LoveSeconds:60),new MobState(MobSpecies.Pig,Start+new Vector3(0,0,6),0,10,LoveSeconds:60)});
        breeding.Update(.5f,Target);Vector3 before=breeding.Entities[0].Position;
        breeding.Update(Dt,Target,heldItem:Items.Wheat);
        Check(breeding.Entities[0].Position.X>before.X && Math.Abs(breeding.Entities[0].Position.Z-before.Z)<.00001f,"Breeding steering overrode the nearby Wheat lure.");
    }
    public static void StateAndBudget()
    {
        var world=PigChecks.Flat();var population=Population(world);
        var session=new GameSession("wheat",new(world,"Wheat",Target,0,0,Pigs:population.CapturePigs()));
        session.Inventory.SetSlot(0,new(Items.Wheat,17));session.Inventory.SetSlot(1,new(Items.WheatSeeds,9));
        var targets=session.Mobs.CapturePopulation().Targets;
        for(int i=0;i<1200;i++)session.Mobs.Update(Dt,session.Player.Position,session.Player.Bounds,session.Items,session.Inventory.Selected.Item);
        Check(session.Inventory[0].Count==17 && session.Inventory[1].Count==9 && session.Mobs.Entities[0].LoveSeconds==0,"Luring consumed Wheat or fed/bred the mob.");
        Check(session.Mobs.CapturePopulation().Targets.AsSpan().SequenceEqual(targets) && session.Mobs.PendingRespawns.Count==0 && world.Revision==0,"Luring changed population targets or terrain.");
        var before=session.Mobs.Entities[0].CaptureState();session.Mobs.Update(0,Target,heldItem:Items.Wheat);session.Mobs.Update(float.NaN,Target,heldItem:Items.Wheat);
        Check(session.Mobs.Entities[0].CaptureState()==before,"Invalid/paused time advanced following.");
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)session.Mobs.Update(Dt,Target,heldItem:Items.Wheat);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Following allocates every simulation tick.");
        before=session.Mobs.Entities[0].CaptureState();using var stream=new MemoryStream();WorldSerializer.Write(stream,session.Capture());stream.Position=0;
        var restored=new GameSession("restored",WorldSerializer.Read(stream));
        Check(restored.Mobs.Entities[0].CaptureState()==before && restored.Inventory[0].Count==17,"Followed mob or Wheat changed during saving/loading.");
        restored.Inventory.SelectedSlot=1;Vector3 position=restored.Mobs.Entities[0].Position;restored.Mobs.Update(Dt,Target,heldItem:restored.Inventory.Selected.Item);
        Check(restored.Mobs.Entities[0].Position==position,"A persisted lure ignored the selected item after loading.");
    }
    public static int Run()
    {
        var checks=All.Concat(new (string Name,Action Check)[]{("Existing species AI/collision/damage",MobChecks.DefinitionsAndMotion),
            ("Existing passive combat and drops",MobChecks.CombatAndDrops),("Existing population/respawn targets",WorldUpdateChecks.TargetsAndTimers),
            ("Existing feeding and breeding",CompletionChecks.Animals)}).ToArray();
        int failures=0;foreach(var check in checks)try{check.Check();Console.WriteLine("PASS: "+check.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+check.Name+": "+e);}
        Console.WriteLine($"{checks.Length-failures}/{checks.Length} Wheat following checks passed.");return failures==0?0:1;
    }
    public static int RunInput()
    {
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".wheat-input-"+Guid.NewGuid().ToString("N"));
        try{using var game=new InputGame(root);game.Run();Check(game.Verified,"Wheat input check did not complete.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent))Directory.Delete(root,true);}
    }
    private sealed class InputGame : Game1
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        public bool Verified { get; private set; }
        public InputGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time) { }
        private T Field<T>(string name)=>(T)typeof(Game1).GetField(name,Flags).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Flags).Invoke(this,args);
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            Check(IsActive,"Wheat input window is not active.");
            Vector3 player=Start;
            var world=PigChecks.Flat();var inventory=new Inventory();inventory.SetSlot(0,new(Items.Wheat,17));inventory.SetSlot(1,new(Items.WheatSeeds,9));inventory.SelectedSlot=1;
            var states=new[]{new MobState(MobSpecies.Pig,player+Vector3.Right*8,0,10),new MobState(MobSpecies.Cow,player+Vector3.Backward*8,0,10),
                new MobState(MobSpecies.Sheep,player+Vector3.Left*6,0,10),new MobState(MobSpecies.Chicken,player+Vector3.Forward*7,0,6)};
            var population=new PassiveMobPopulation(world,states);var save=new WorldSave(world,"Wheat input",player,0,0,inventory,Pigs:population.CapturePigs(),Mobs:population.CaptureOthers());
            var store=Field<WorldSaveManager>("_saves");Call("BeginWorld",store.Create(save),save);var session=Field<GameSession>("_session");
            void Input(double elapsed,params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),default(MouseState),false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(elapsed)),Point.Zero);
            Vector3[] before=session.Mobs.Entities.Select(m=>m.Position).ToArray();Input(.25,Keys.D2);
            Check(session.Mobs.Entities.Select(m=>m.Position).SequenceEqual(before),"Selected Seeds attracted passive mobs.");
            Input(.25,Keys.D1);
            for(int i=0;i<before.Length;i++)Check(Vector3.Distance(session.Mobs.Entities[i].Position,session.Player.Position)<Vector3.Distance(before[i],player),"Actual selected-Wheat input failed for "+session.Mobs.Entities[i].Species);
            Check(session.Inventory.SelectedSlot==0 && session.Inventory[0].Count==17 && session.Player.Position==player,"Following consumed Wheat or moved the player.");
            before=session.Mobs.Entities.Select(m=>m.Position).ToArray();Call("SetState",GameState.Paused);Input(2);
            Check(session.Mobs.Entities.Select(m=>m.Position).SequenceEqual(before),"Paused gameplay advanced followers.");
            Verified=true;Exit();Console.WriteLine("PASS: Real Game1 number-key selection distinguishes Wheat/Seeds, all four species follow without consuming items, and pause stops following (synthetic input).");
        }
    }
}
