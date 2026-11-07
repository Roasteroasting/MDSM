using System;
using System.Reflection;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CollisionUpdateChecks
{
    internal static readonly (string Name, Action Check)[] All =
    {
        ("Creative/Survival closed-Door penetration recovery, all facings/hinges and saved intersections", DoorRecovery),
        ("Flight walls/floor/ceiling, stable vectors/speeds, modes and fixed-step render rates", Flight),
        ("Overlapping solids, world edges and bounded recovery without inverted motion", SolidRecovery),
        ("All passive/hostile bounds reject placement atomically in both modes", MobPlacement),
        ("Prospective Door/Gate/Pane/Fence shapes and neighboring connections protect mobs", SpecializedPlacement)
    };
    private static void Check(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static World Flat()
    {
        var world=new World(173,WorldSize.Small,false);
        for(int z=0;z<65;z++)for(int x=0;x<65;x++)world.SetBlock(x,0,z,BlockType.Grass);
        return world;
    }
    private static Player Flyer(Vector3 feet)=>new(feet){Settings=new(){GameMode=GameMode.Creative}};
    public static int Run()
    {
        int failed=0;
        foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failed}/{All.Length} collision update checks passed.");return failed==0?0:1;
    }
    public static void DoorRecovery()
    {
        var world=Flat();Point3 cell=new(20,1,20);Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        Check(world.PlaceStructure(cell,BlockType.Door,0,new(0,-1,0),new Player(new(40,1,40)).Bounds),"Door fixture failed.");
        foreach(GameMode mode in Enum.GetValues<GameMode>())
        for(byte facing=0;facing<4;facing++)for(int hinge=0;hinge<2;hinge++)
        {
            world.Structures[cell]=new(cell,(byte)(facing|hinge*4),false,new(0,-1,0));world.BlockBoxes(cell,boxes,true);
            Vector3 center=(boxes[0].Min+boxes[0].Max)*.5f;center.Y=1;
            world.Toggle(cell);
            var player=new Player(center){Settings=new(){GameMode=mode}};
            player.Update(world,Vector3.Zero,false,1f/120);
            Check(!world.IntersectsSolid(player.Bounds),"Open Door fixture intersects player.");
            world.Toggle(cell);Check(world.IntersectsSolid(player.Bounds),"Closing Door failed to reproduce overlap.");
            float y=player.Position.Y;player.Update(world,Vector3.Zero,false,1f/120);
            Check(!world.IntersectsSolid(player.Bounds) && Math.Abs(player.Position.Y-y)<.001f,"Door did not resolve to the nearby horizontal exit.");
            Vector3 before=player.Position;player.Update(world,Vector3.Zero,true,1f/120,swimUp:true);
            Check(player.Position.Y>before.Y && player.Position.Y-before.Y<.1f,"Space inverted or caused excessive displacement after recovery.");
            if(mode==GameMode.Creative)
            {
                var inventory=new Inventory();using var stream=new MemoryStream();
                WorldSerializer.Write(stream,new(world,"Embedded",center,0,0,inventory));stream.Position=0;
                var loaded=new GameSession("test",WorldSerializer.Read(stream));loaded.ApplySettings(new(){GameMode=mode});
                loaded.Player.Update(loaded.World,Vector3.Zero,false,1f/120);
                Check(!loaded.World.IntersectsSolid(loaded.Player.Bounds) && loaded.Player.Health==20,"Restored intersection was not safely recovered.");
            }
        }
    }
    public static void Flight()
    {
        var world=Flat();
        for(int y=1;y<9;y++)for(int z=16;z<25;z++)world.SetBlock(24,y,z,BlockType.Stone);
        for(int z=16;z<25;z++)for(int x=16;x<24;x++)world.SetBlock(x,9,z,BlockType.Stone);
        foreach(int fps in new[]{5,30,60,144,360,2000})
        {
            var player=Flyer(new(20,3,20));var clock=new SimulationClock();
            void Second(Vector3 move,bool up=false,bool down=false,bool boost=false)
            {
                for(int frame=0;frame<fps;frame++)for(int ticks=clock.Advance(1d/fps);ticks>0;ticks--)
                {player.Update(world,move,false,(float)SimulationClock.StepSeconds,down,up,boost);Check(!world.IntersectsSolid(player.Bounds),"Flight penetrated terrain at FPS "+fps);}
            }
            Second(Vector3.UnitX,boost:true);Check(Math.Abs(player.Position.X-(24-Player.HalfWidth))<.001f,"Wall collision failed.");
            float x=player.Position.X;Second(-Vector3.UnitX);
            Check(Math.Abs(player.Position.X-(x-Player.MoveSpeed))<.002f,"Horizontal flight inverted or changed speed after wall collision.");
            Second(Vector3.Zero,true,boost:true);Check(Math.Abs(player.Position.Y-(9-Player.BodyHeight))<.001f,"Ceiling collision failed.");
            Second(Vector3.Zero,down:true,boost:true);Check(Math.Abs(player.Position.Y-1)<.001f,"Floor collision failed.");
            Second(Vector3.Zero,true);Check(Math.Abs(player.Position.Y-1-Player.MoveSpeed)<.002f,"Ascent changed speed after collision.");
            float height=player.Position.Y;Second(Vector3.Zero);Check(player.Position.Y==height,"Creative acquired gravity or residual velocity.");
            player.Settings.GameMode=GameMode.Survival;player.Update(world,Vector3.Zero,false,1f/120);
            Check(player.Position.Y<height && player.Health==20,"Survival switch lost gravity or introduced fall damage.");
            player.Settings.GameMode=GameMode.Creative;player.Update(world,Vector3.Zero,false,1f/120);height=player.Position.Y;
            Second(Vector3.Zero);Check(player.Position.Y==height,"Survival momentum leaked into Creative.");
        }
        // Movement basis remains camera-relative at arbitrary headings after recovering.
        foreach(float yaw in new[]{0f,.6f,-1.7f,MathHelper.Pi})
        {
            var camera=new Camera();camera.RestoreOrientation(yaw,.4f);
            foreach(Vector3 move in new[]{camera.HorizontalForward,-camera.HorizontalForward,camera.Right,-camera.Right,Vector3.Normalize(camera.Right+camera.HorizontalForward)})
            {
                var player=Flyer(new(40,10,40));Vector3 start=player.Position;
                for(int tick=0;tick<120;tick++)player.Update(world,move,false,1f/120);
                Check(Vector3.Distance(player.Position,start+move*Player.MoveSpeed)<.003f && camera.Yaw==yaw && camera.Pitch==.4f,"Camera-relative axes or diagonal speed changed.");
            }
        }
    }
    public static void SolidRecovery()
    {
        var world=Flat();world.SetBlock(20,1,20,BlockType.Stone);world.SetBlock(20,2,20,BlockType.Stone);
        var player=Flyer(new(20.5f,1,20.5f));player.Update(world,Vector3.Zero,false,1f/120);
        Check(!world.IntersectsSolid(player.Bounds),"Full-block penetration not resolved without input.");
        Vector3 resolved=player.Position;player.Update(world,Vector3.Zero,false,1f/120);Check(player.Position==resolved,"Stationary recovery jittered.");
        // Recovery must respect a world-edge obstruction instead of exiting the world.
        world.SetBlock(0,1,20,BlockType.Stone);var edge=Flyer(new(.3f,1,20.5f));edge.Update(world,Vector3.Zero,false,1f/120);
        Check(edge.Position.X>=Player.HalfWidth && !world.IntersectsSolid(edge.Bounds),"Recovery escaped the finite world.");
        // A mode switch from the Survival void cannot sweep backward through Y=0.
        var below=Flyer(new(30,-2,30));below.Update(world,Vector3.Zero,false,1f/120,swimUp:true);
        Check(below.Position.Y>=1 && !world.IntersectsSolid(below.Bounds),"Below-world Creative entry corrupted collision.");
        var enclosed=new World(173,WorldSize.Small,false);
        for(int z=0;z<100;z++)for(int y=0;y<100;y++)for(int x=0;x<100;x++)enclosed.SetGeneratedBlock(x,y,z,BlockType.Stone);
        var trapped=Flyer(new(50,50,50));
        for(int tick=0;tick<10;tick++)trapped.Update(enclosed,Vector3.UnitX,true,1f/120,swimUp:true,fastFlight:true);
        Check(trapped.Position==new Vector3(50,50,50),"Unresolvable overlap became runaway movement.");
        // Ordinary axis collision must still slide rather than freezing both axes.
        for(int y=1;y<5;y++)for(int z=25;z<40;z++)world.SetBlock(32,y,z,BlockType.Stone);
        var sliding=Flyer(new(31.6f,1,28));for(int tick=0;tick<120;tick++)sliding.Update(world,Vector3.Normalize(new Vector3(1,0,1)),false,1f/120);
        Check(sliding.Position.X<=31.7001f && sliding.Position.Z>30 && !world.IntersectsSolid(sliding.Bounds),"Flight wall sliding regressed.");
    }
    private static GameSession Session(World world,MobSpecies species,Vector3 position)
    {
        int health=MobDefinitions.Get(species).MaximumHealth;
        var save=new WorldSave(world,"Placement",new(45,1,45),0,0,
            Pigs:species==MobSpecies.Pig?new[]{new PigState(position,0,health)}:null,
            Mobs:species!=MobSpecies.Pig && (int)species<4?new[]{new MobState(species,position,0,health)}:null,
            Hostiles:(int)species>=4?new[]{new MobState(species,position,0,health)}:null);
        return new GameSession("test",save);
    }
    public static void MobPlacement()
    {
        var world=Flat();Point3 cell=new(20,1,20);
        foreach(MobSpecies species in Enum.GetValues<MobSpecies>())foreach(bool creative in new[]{false,true})
        {
            // Standing, fractional horizontal movement, and airborne collision boxes.
            foreach(Vector3 feet in new[]{new Vector3(20.5f,1,20.5f),new Vector3(20.95f,1,20.5f),new Vector3(20.5f,1.7f,20.5f)})
            {
                var session=Session(world,species,feet);session.ApplySettings(new(){GameMode=creative?GameMode.Creative:GameMode.Survival});
                var mob=(int)species<4?session.Mobs.Entities[0]:session.Hostiles.Entities[0];
                session.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.Dirt),2));int revision=world.Revision;
                Check(!BlockPlacement.TryPlace(world,session.Inventory,cell,session.Player.Bounds,creative),"Placed solid through "+species);
                Check(session.Inventory.Selected.Count==2 && world.GetBlock(20,1,20)==BlockType.Air && world.Revision==revision && mob.Position==feet,"Rejected placement changed items/world/mob.");
                Point3 adjacent=new(23,1,20);Check(BlockPlacement.TryPlace(world,session.Inventory,adjacent,session.Player.Bounds,creative),"Valid adjacent placement was rejected.");
                Check(session.Inventory.Selected.Count==(creative?2:1),"Successful placement accounting changed.");world.SetBlock(23,1,20,BlockType.Air);
            }
        }
        var moving=Session(world,MobSpecies.Cow,new(20.5f,1,20.5f));moving.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.Stone),2));
        var animal=moving.Mobs.Entities[0];
        for(int tick=0;tick<180;tick++)animal.Update(world,1f/120,Vector3.UnitX);
        Check(!World.Overlaps(World.Bounds(cell),animal.Bounds) && BlockPlacement.TryPlace(world,moving.Inventory,cell,moving.Player.Bounds),"Placement did not become valid after a live mob moved away.");
        world.SetBlock(20,1,20,BlockType.Air);
        var plants=Session(world,MobSpecies.Pig,new(20.5f,1,20.5f));plants.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.RedFlower),2));
        Check(BlockPlacement.TryPlace(world,plants.Inventory,cell,plants.Player.Bounds),"Non-solid plant placement within a mob was changed.");
    }
    public static void SpecializedPlacement()
    {
        var world=Flat();Point3 cell=new(20,1,20);
        foreach(bool creative in new[]{false,true})
        {
            void Rejected(BlockType block,Vector3 feet)
            {
                var session=Session(world,MobSpecies.Chicken,feet);session.Inventory.SetSlot(0,new(Items.FromBlock(block),2));
                session.ApplySettings(new(){GameMode=creative?GameMode.Creative:GameMode.Survival});
                int revision=world.Revision,structures=world.Structures.Count;
                bool placed=block is BlockType.Door or BlockType.FenceGate
                    ? SurvivalInteractions.Use(session,new BlockHit(new(20,0,20),new(0,1,0),1))
                    : BlockPlacement.TryPlace(world,session.Inventory,cell,session.Player.Bounds,creative);
                // Use reports handled even when a special placement is rejected.
                Check((block is BlockType.Door or BlockType.FenceGate || !placed) && world.GetBlock(20,1,20)==BlockType.Air && world.GetBlock(20,2,20)==BlockType.Air
                    && revision==world.Revision && structures==world.Structures.Count && session.Inventory.Selected.Count==2,"Special shape placement was not atomic: "+block);
            }
            Rejected(BlockType.GlassPane,new(20.5f,1,20.5f));
            Rejected(BlockType.Fence,new(20.5f,2.2f,20.5f)); // fence collision extends above its cell
            Rejected(BlockType.FenceGate,new(20.5f,2.2f,20.5f));
            Rejected(BlockType.Door,new(20.5f,2.2f,20.1f)); // upper half
        }
        // True thin geometry: mobs may occupy a free part of a Pane/Door cell.
        foreach(BlockType block in new[]{BlockType.GlassPane,BlockType.Door,BlockType.FenceGate})
        {
            var session=Session(world,MobSpecies.Chicken,new(20.02f,1,20.98f));session.Inventory.SetSlot(0,new(Items.FromBlock(block),2));
            Check(BlockPlacement.TryPlace(world,session.Inventory,cell,session.Player.Bounds),"Thin block rejected nonintersecting mob: "+block);
            world.SetBlock(20,1,20,BlockType.Air);
        }
        // Facing-specific Door strips, both halves, all four directions.
        Span<BoundingBox> boxes=stackalloc BoundingBox[9];
        for(byte facing=0;facing<4;facing++)
        {
            world.PlaceStructure(cell,BlockType.Door,facing,new(0,-1,0),new Player(new(45,1,45)).Bounds);world.BlockBoxes(cell,boxes,true);
            Vector3 center=(boxes[0].Min+boxes[0].Max)*.5f;center.Y=2.3f;world.SetBlock(20,1,20,BlockType.Air);
            var session=Session(world,MobSpecies.Chicken,center);int revision=world.Revision;
            Check(!world.PlaceStructure(cell,BlockType.Door,facing,new(0,-1,0),session.Player.Bounds) && revision==world.Revision,"Facing-specific upper Door collision missed mob.");
            world.LiveMobs=null;world.LiveHostiles=null;
        }
        // A new neighbor can extend an existing Pane's arm into a mob, including at a chunk seam.
        Point3 pane=new(15,1,20),support=new(16,1,20);world.SetBlock(pane.X,pane.Y,pane.Z,BlockType.GlassPane);
        var neighbor=Session(world,MobSpecies.Chicken,new(15.8f,1,20.5f));
        // Baby bounds leave room between the center post and the next cell.
        world.LiveMobs=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Chicken,new(15.8f,1,20.5f),0,6,BabySeconds:100)});
        neighbor.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.Stone),2));int old=world.Revision;
        Check(!world.IntersectsSolid(world.LiveMobs.Entities[0].Bounds),"Neighbor-connection fixture already intersects Pane.");
        Check(!BlockPlacement.TryPlace(world,neighbor.Inventory,support,neighbor.Player.Bounds) && old==world.Revision && neighbor.Inventory.Selected.Count==2,"New Pane arm displaced mob across a chunk seam.");
        world.SetBlock(pane.X,pane.Y,pane.Z,BlockType.Fence);
        world.LiveMobs=new PassiveMobPopulation(world,new[]{new MobState(MobSpecies.Chicken,new(15.95f,2.2f,20.5f),0,6,BabySeconds:100)});
        old=world.Revision;
        Check(!world.IntersectsSolid(world.LiveMobs.Entities[0].Bounds) && !BlockPlacement.TryPlace(world,neighbor.Inventory,support,neighbor.Player.Bounds)
            && world.Revision==old,"New Fence arm above the placed cell displaced a mob.");
    }
    internal static void Diagnose()
    {
        foreach (GameMode mode in Enum.GetValues<GameMode>())
        {
            var world = new World(173, WorldSize.Small, false);
            for (int z = 16; z < 25; z++) for (int x = 16; x < 25; x++) world.SetBlock(x, 0, z, BlockType.Stone);
            Point3 door = new(20, 1, 20);
            world.PlaceStructure(door, BlockType.Door, 0, new(0, -1, 0), new Player(new(24, 1, 24)).Bounds);
            world.Toggle(door);
            var player = new Player(new(20.35f, 1, 20.14f)) { Settings = new() { GameMode = mode } };
            player.Update(world, Vector3.Zero, false, 1f / 120);
            Console.WriteLine($"{mode} open: {player.Position}, overlap={world.IntersectsSolid(player.Bounds)}");
            world.Toggle(door);
            player.Update(world, Vector3.Zero, false, 1f / 120);
            Console.WriteLine($"{mode} closed idle: {player.Position}, overlap={world.IntersectsSolid(player.Bounds)}");
            for (int tick = 0; tick < 3; tick++)
            {
                Vector3 before = player.Position;
                player.Update(world, Vector3.Zero, true, 1f / 120, swimUp: true);
                Console.WriteLine($"{mode} Space: {player.Position}, delta={player.Position-before}, overlap={world.IntersectsSolid(player.Bounds)}");
            }
            Vector3 previous = player.Position;
            player.Update(world, Vector3.UnitX, false, 1f / 120);
            Console.WriteLine($"{mode} +X: {player.Position}, delta={player.Position-previous}, vertical={typeof(Player).GetField("_verticalSpeed", BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player)}");
        }
    }
}
