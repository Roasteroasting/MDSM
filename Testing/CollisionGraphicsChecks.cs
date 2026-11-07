using System;
using System.IO;
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

internal static class CollisionGraphicsChecks
{
    public static int Run(bool diagnose = false)
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".collision-graphics-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var game = new CollisionGame(root, diagnose);
            game.Run();
            if (game.Failure != null) throw new InvalidOperationException("Scripted collision check failed.", game.Failure);
            if (!game.Verified) throw new InvalidOperationException("Scripted collision check did not finish.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped test directory.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class CollisionGame : Game1
    {
        private readonly bool _diagnose;
        private int _frame;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public Exception Failure { get; private set; }
        public bool Verified { get; private set; }
        private T Field<T>(string name) => (T)typeof(Game1).GetField(name, Private).GetValue(this);
        private object Call(string name, params object[] args) => typeof(Game1).GetMethod(name, Private).Invoke(this, args);
        public CollisionGame(string root, bool diagnose) : base(new WorldSaveManager(Path.Combine(root, "Worlds")), new SettingsStore(Path.Combine(root, "settings.json")))
        { _diagnose = diagnose; Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content"); }
        protected override void Update(GameTime time) { }
        private void Input(bool right = false, params Keys[] keys) => Call("ProcessInput", new KeyboardState(keys),
            new MouseState(0, 0, 0, ButtonState.Released, ButtonState.Released, right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released),
            false, new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.0 / 60)), Point.Zero);
        protected override void Draw(GameTime time)
        {
            try
            {
                if (_frame++ == 0)
                {
                    Field<GameSettings>("_settings").GameMode = GameMode.Creative;
                    var world = new World(173, WorldSize.Small, false);
                    for (int z = 16; z < 25; z++) for (int x = 16; x < 25; x++) world.SetBlock(x, 0, z, BlockType.Stone);
                    Point3 door = new(20, 1, 20);
                    world.PlaceStructure(door, BlockType.Door, 0, new(0, -1, 0), new Player(new(24, 1, 24)).Bounds);
                    world.Toggle(door);
                    var save = new WorldSave(world, "Collision regression", new(20.35f, 1, 20.14f), MathHelper.PiOver2, 0);
                    string id = Field<WorldSaveManager>("_saves").Create(save);
                    Call("BeginWorld", id, save);
                    Input();
                    var session = Field<GameSession>("_session");
                    if (world.IntersectsSolid(session.Player.Bounds)) throw new InvalidOperationException("Open Door fixture intersects player.");
                    Input(true);
                    if (world.Structure(door).Open) throw new InvalidOperationException("Actual RMB did not close Door.");
                    Vector3 before = session.Player.Position;
                    Input(false, Keys.Space);
                    Vector3 delta = session.Player.Position - before;
                    Console.WriteLine($"Actual Creative RMB-close/Space: {before} -> {session.Player.Position}, delta={delta}, overlapping={world.IntersectsSolid(session.Player.Bounds)}, yaw={session.Camera.Yaw}, pitch={session.Camera.Pitch}");
                    if (!_diagnose && (delta.Y < -.001f || world.IntersectsSolid(session.Player.Bounds))) throw new InvalidOperationException("Door closing still corrupts Creative ascent.");
                    if (!_diagnose)
                    {
                        Vector3 start=session.Player.Position;
                        for(int i=0;i<60;i++)Input(false,Keys.W,Keys.Space);
                        Vector3 travel=session.Player.Position-start;
                        if(Math.Abs(travel.X-Player.MoveSpeed)>.08f || Math.Abs(travel.Y-Player.MoveSpeed)>.08f || Math.Abs(travel.Z)>.001f)
                            throw new InvalidOperationException("W/Space axes or flight speed corrupted after Door recovery.");
                        for(int i=0;i<60;i++)Input(false,Keys.S,Keys.LeftShift);
                        if(Vector3.Distance(session.Player.Position,start)>.1f || world.IntersectsSolid(session.Player.Bounds))
                            throw new InvalidOperationException("S/Shift return flight corrupted after Door recovery.");
                        if(session.Camera.Yaw!=MathHelper.PiOver2 || session.Camera.Pitch!=0)throw new InvalidOperationException("Collision changed camera orientation.");
                        Console.WriteLine("PASS: Actual Creative RMB-close/Space, W/S, ascent/descent, speed and camera orientation after recovery.");
                        VerifyMobPlacement(MobSpecies.Cow);
                        VerifyMobPlacement(MobSpecies.Zombie);
                    }
                }
                base.Draw(time);
                if (_frame > 1) { Verified = true; Exit(); }
            }
            catch (Exception e) { Failure = e; Exit(); }
        }

        private void VerifyMobPlacement(MobSpecies species)
        {
            var world=new World(173,WorldSize.Small,false);
            for(int z=16;z<26;z++)for(int x=16;x<26;x++)world.SetBlock(x,0,z,BlockType.Grass);
            var inventory=new Inventory();inventory.SetSlot(0,new(Items.FromBlock(BlockType.Dirt),2));
            var state=new MobState(species,new(20.5f,1,20.3f),0,MobDefinitions.Get(species).MaximumHealth);
            var save=new WorldSave(world,"Mob placement",new(20.5f,3,20.5f),0,-1.5f,inventory,
                Mobs:species==MobSpecies.Cow?new[]{state}:null,Hostiles:species==MobSpecies.Zombie?new[]{state}:null);
            string id=Field<WorldSaveManager>("_saves").Create(save);Call("BeginWorld",id,save);Input();
            var session=Field<GameSession>("_session");
            var hit=BlockRaycaster.Cast(world,session.Camera.Position,session.Camera.Forward);
            if(hit?.PlacementCell!=new Point3(20,1,20))throw new InvalidOperationException("Scripted placement ray missed fixture.");
            int revision=world.Revision;Input(true);
            if(world.GetBlock(20,1,20)!=BlockType.Air || session.Inventory.Selected.Count!=2 || revision!=world.Revision)
                throw new InvalidOperationException("Actual RMB placed through "+species);
            var mob=species==MobSpecies.Cow?session.Mobs.Entities[0]:session.Hostiles.Entities[0];
            for(int tick=0;tick<480;tick++)mob.Update(world,1f/120,Vector3.UnitX);
            Input();Input(true);
            if(world.GetBlock(20,1,20)!=BlockType.Dirt || session.Inventory.Selected.Count!=2)
                throw new InvalidOperationException("Actual Creative RMB failed after mob moved out of the way.");
            Console.WriteLine("PASS: Actual RMB rejects placement within "+species+" and permits placement after it moves away.");
        }
    }
}
