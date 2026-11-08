using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class OvernightGraphicsChecks
{
    public static int Run()
    {
        string root=Path.Combine(AppContext.BaseDirectory,".overnight-graphics-"+Guid.NewGuid().ToString("N"));
        try{using var game=new CheckGame(root);game.Run();if(!game.Verified)throw new Exception("Graphics checks did not complete.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(Directory.Exists(root)&&Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)))Directory.Delete(root,true);}
    }
    private sealed class CheckGame:Game1
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        public bool Verified {get;private set;}
        public CheckGame(string root):base(new WorldSaveManager(Path.Combine(root,"Worlds")),new SettingsStore(Path.Combine(root,"settings.json")))
        {Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");}
        protected override void Update(GameTime time){}
        private T F<T>(string name)=>(T)typeof(Game1).GetField(name,Flags).GetValue(this);
        private object Call(string name,params object[] args)=>typeof(Game1).GetMethod(name,Flags).Invoke(this,args);
        private GameSession Session=>F<GameSession>("_session");
        private void Input(double seconds=0,MouseState mouse=default,params Keys[] keys)=>Call("ProcessInput",new KeyboardState(keys),mouse,false,new GameTime(TimeSpan.Zero,TimeSpan.FromSeconds(seconds)),Point.Zero);
        private void Key(Keys key){Input();Input(keys:new[]{key});Input();}
        private void Click(Rectangle r,bool right=false)
        {Input();Input(mouse:new(r.Center.X,r.Center.Y,0,right?ButtonState.Released:ButtonState.Pressed,ButtonState.Released,right?ButtonState.Pressed:ButtonState.Released,ButtonState.Released,ButtonState.Released));}
        private Color[] Render(string name,int width=640,int height=480)
        {
            using var target=new RenderTarget2D(GraphicsDevice,width,height,false,SurfaceFormat.Color,DepthFormat.Depth24);
            GraphicsDevice.SetRenderTarget(target);base.Draw(new GameTime());GraphicsDevice.SetRenderTarget(null);
            var pixels=new Color[width*height];target.GetData(pixels);
            using var file=File.Create(Path.Combine(AppContext.BaseDirectory,"overnight-"+name+".png"));target.SaveAsPng(file,width,height);
            return pixels;
        }
        protected override void Draw(GameTime time)
        {
            if(Verified){Exit();return;}
            var store=F<WorldSaveManager>("_saves");var world=PigChecks.Flat();world.SavedGameMode=GameMode.Creative;
            var save=new WorldSave(world,"New graphics",new(25.5f,1,28.5f),0,-.3f,
                Mobs:new[]{new MobState(MobSpecies.Cow,new(22.5f,1,23.5f),0,10,Burning:new(7,.2))});
            string id=store.Create(save);Call("BeginWorld",id,save);Input();
            C(world.PlaceBuilding(new(25,1,25),BlockType.Sign,0,false,Session.Player.Bounds,new(0,-1,0)),"Sign graphics fixture failed.");
            for(byte facing=0;facing<4;facing++)
            {
                world.PlaceBuilding(new(19+facing*3,1,22),BlockType.WoodenStairs,facing,false,Session.Player.Bounds,new(0,-1,0));
                world.PlaceBuilding(new(19+facing*3,3,22),BlockType.CobblestoneStairs,facing,true,Session.Player.Bounds,new(0,-1,0));
            }
            RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);
            var arm=Render("arm");C(arm.Count(c=>c.R>c.G*1.2f&&c.G>c.B*1.15f)>100,"Arm skin faces invisible.");
            Session.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.WoodenPlanks),1));var held=Render("held");C(!arm.AsSpan().SequenceEqual(held),"Occupied slot did not replace arm.");
            Session.Swing.Trigger();Session.Swing.Update(.1f,false);var swing=Render("swing");C(!swing.AsSpan().SequenceEqual(held),"Swing did not render.");Session.Swing.Reset();
            Key(Keys.F1);var hidden=Render("hud-hidden");var repeat=Render("hud-hidden-readback");C(hidden.AsSpan().SequenceEqual(repeat),"Readback changed hidden-HUD world textures.");Key(Keys.F1);
            Key(Keys.E);C(State==GameState.Inventory,"Creative inventory did not open.");
            using(var target=new RenderTarget2D(GraphicsDevice,640,480,false,SurfaceFormat.Color,DepthFormat.Depth24))
            {
                GraphicsDevice.SetRenderTarget(target);Click(InventoryLayout.BrowserSearch(GraphicsDevice.Viewport));
                foreach(char c in "obsidian")Call("TextInput",c);
                C(F<InventoryScreen>("_inventoryScreen").SearchText=="obsidian","Actual search text input failed.");
                Input(keys:new[]{Keys.E});C(State==GameState.Inventory,"Typing E closed focused search.");Input();
                Click(InventoryLayout.BrowserSlot(GraphicsDevice.Viewport,0));
                C(Enumerable.Range(0,36).Any(i=>Items.Name(Session.Inventory[i].Item).Contains("OBSIDIAN")),"Filtered browser granted wrong/missing item.");
                GraphicsDevice.SetRenderTarget(null);
            }
            Render("creative-search");Key(Keys.Escape);
            Session.Inventory.SetSlot(0,default);Click(new(0,0,1,1),true);C(State==GameState.SignEditing,"Actual RMB did not edit sign.");
            foreach(char c in "MDSM")Call("TextInput",c);Key(Keys.Enter);foreach(char c in "TEST SIGN")Call("TextInput",c);Render("sign-editor");Key(Keys.Escape);
            C(world.Signs[new(25,1,25)]=="MDSM\nTEST SIGN\n\n","Actual sign editor lost text.");
            var textFrame=Render("sign-world");C(textFrame.AsSpan().SequenceEqual(Render("sign-world-repeat")),"Text pass/readback altered the next frame.");
            Key(Keys.F1);var textHidden=Render("sign-text-hud-hidden");C(textHidden.AsSpan().SequenceEqual(Render("sign-text-hud-hidden-repeat")),"Text texture corrupted HUD-hidden terrain after readback.");Key(Keys.F1);
            C(typeof(SignEditor).GetField("_world",Flags).GetValue(F<SignEditor>("_signEditor"))==null,"Closed sign editor retained the previous world's blocks.");
            VerifySignScreenshots();VerifySignDirectionsAndBuffers();
            Key(Keys.T);foreach(char c in "/spawn c")Call("TextInput",c);C(F<CommandBar>("_commandBar").Input.Suggestions.Count==2,"Autocomplete has no visible choices.");Render("autocomplete-before");Key(Keys.Tab);
            foreach(var size in new[]{new Point(320,240),new(640,480),new(1280,720)})
            {var choices=Render("autocomplete-"+size.X,size.X,size.Y);C(choices.Count(p=>p.R>230 && p.G>230 && p.B<50)>20,"Highlighted Tab option is not visible.");}
            Key(Keys.Tab);C(F<CommandBar>("_commandBar").Input.SuggestionIndex==1,"Actual Tab did not advance the highlighted choice.");Key(Keys.Escape);
            Key(Keys.T);foreach(char c in "/give @p baked_pot")Call("TextInput",c);Key(Keys.Tab);C(F<CommandBar>("_commandBar").Input.Text=="/give @p baked_potato","Actual Tab completion failed.");Key(Keys.Escape);
            for(int stage=0;stage<7;stage++)
            {
                world.SetBlock(19+stage,0,23,BlockType.FarmlandWet);world.SetBlock(19+stage,1,23,(BlockType)((byte)BlockType.Carrot1+stage));
                world.SetBlock(19+stage,0,21,BlockType.FarmlandWet);world.SetBlock(19+stage,1,21,(BlockType)((byte)BlockType.Potato1+stage));
            }
            world.SetBlock(27,1,24,BlockType.Snow);
            RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);
            var noFire=Render("crops-snow");C(world.Fire.Ignite(new(24,1,24)),"Graphics fire ignition failed.");RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);
            var fire=Render("fire");C(!fire.AsSpan().SequenceEqual(noFire),"Fire model did not render.");
            Session.Inventory.SetSlot(0,new(Items.Snowball,3));
            var noBall=Render("before-snowball");C(Session.Projectiles.ThrowSnowball(Session.Inventory,Session.Camera.Position+Session.Camera.Forward*1.5f,Session.Camera.Forward,true),"Graphics snowball throw failed.");
            var ball=Render("snowball");C(!ball.AsSpan().SequenceEqual(noBall),"Snowball projectile did not render.");Session.Projectiles.RemoveAll();
            world.Weather.Restore(new(300,180,25,25,120,1));var rain=Render("rain");world.Weather.Restore(new(300,0,25,25,120,1));var clear=Render("clear");C(!rain.AsSpan().SequenceEqual(clear),"Rain did not render.");
            world.Particles.Burst(BlockType.Cobblestone,new(25.5f,2,25));Render("break-particles");
            var renderer=F<WorldRenderer>("_renderer");var edits=new double[12];
            for(int i=0;i<edits.Length;i++)
            {
                world.SetBlock(26,2,24,i%2==0?BlockType.Stone:BlockType.Air);
                long start=Stopwatch.GetTimestamp();renderer.Prepare(Session.Camera);edits[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            Console.WriteLine($"EDIT PREPARE (actual GL upload): mean {edits.Average():F2} ms; max {edits.Max():F2} ms; GPU terrain estimate {renderer.BufferBytes/1048576d:F1} MiB.");
            C(renderer.ResidentChunks<=renderer.ResidentLimit,"Unbounded mesh residency.");
            Input();Input(.02,new MouseState(0,0,0,ButtonState.Pressed,ButtonState.Released,ButtonState.Released,ButtonState.Released,ButtonState.Released));
            C(world.GetBlock(25,1,25)==BlockType.Air && Session.Swing.Transform!=Matrix.Identity,"Actual Creative LMB did not mine/swing.");
            bool debris=false;foreach(var p in world.Particles.Active)debris|=p.Life>0;C(debris,"Actual mining produced no particles.");Render("mining-particles");Input();
            Call("SetState",GameState.Paused);double weatherSeconds=world.Weather.Remaining;Input(30);C(world.Weather.Remaining==weatherSeconds,"Pause advanced weather.");
            Call("SetState",GameState.Playing);VerifyDesertAndClouds();
            var natural=World.CreateNew(173,WorldSize.Small);natural.SavedGameMode=GameMode.Creative;natural.AllowCheats=true;
            var naturalSave=new WorldSave(natural,"Natural graphics",natural.Spawn,0,-.1f);Call("BeginWorld",store.Create(naturalSave),naturalSave);
            int bestY=0,bx=0,bz=0;
            for(int z=24;z<natural.Depth-24;z++)for(int x=24;x<natural.Width-24;x++)if(natural.SurfaceY(x,z)>bestY){bestY=natural.SurfaceY(x,z);bx=x;bz=z;}
            Session.Camera.Position=new(bx+35,bestY+12,bz+45);Vector3 direction=new Vector3(bx,bestY,bz)-Session.Camera.Position;
            Session.Camera.RestoreOrientation(MathF.Atan2(direction.X,-direction.Z),MathF.Atan2(direction.Y,new Vector2(direction.X,direction.Z).Length()));
            RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);Render("mountains");
            Console.WriteLine($"NATURAL Small: {natural.GeneratedDungeons.Count} dungeons; highest rendered terrain/tree column Y={bestY}; {natural.GeneratedWaterFeatures}.");
            Key(Keys.E);foreach(Point size in new[]{new Point(320,240),new(640,480),new(1280,720)})
            {
                using var target=new RenderTarget2D(GraphicsDevice,size.X,size.Y,false,SurfaceFormat.Color,DepthFormat.Depth24);
                GraphicsDevice.SetRenderTarget(target);base.Draw(new GameTime());GraphicsDevice.SetRenderTarget(null);using var file=File.Create(Path.Combine(AppContext.BaseDirectory,$"overnight-browser-{size.X}.png"));target.SaveAsPng(file,size.X,size.Y);
            }
            C((bool)Call("SaveCurrentWorld"),"Runtime save failed.");
            Console.WriteLine("PASS: Real DesktopGL arm/items/swing, readback, building models, search grants, sign editing, Tab completion, crops, snow, fire/burning mobs, snowball rendering, weather, particles, pause, natural generation and save input.");
            Verified=true;Exit();
        }
        private static void C(bool ok,string message)=>OvernightChecks.Check(ok,message);
        private void VerifySignScreenshots()
        {
            foreach(bool hidden in new[]{false,true})
            {
                if(hidden)Key(Keys.F1);Key(Keys.F2);GraphicsDevice.SetRenderTarget(null);base.Draw(new GameTime());
                int width=GraphicsDevice.PresentationParameters.BackBufferWidth,height=GraphicsDevice.PresentationParameters.BackBufferHeight;
                var before=new Color[width*height];GraphicsDevice.GetBackBufferData(before);
                string path=Directory.GetFiles(F<ScreenshotCapture>("_screenshots").RootDirectory,"*.png").OrderBy(s=>s,StringComparer.Ordinal).Last();
                var png=ItemAtlasChecks.Read(path);C(png.Width==width && png.Height==height,"Sign screenshot dimensions changed.");
                for(int i=0;i<before.Length;i++)C(before[i]==new Color(png.Pixels[i*4],png.Pixels[i*4+1],png.Pixels[i*4+2],png.Pixels[i*4+3]),"F2 lost pixels after the text pass.");
                base.Draw(new GameTime());var after=new Color[before.Length];GraphicsDevice.GetBackBufferData(after);
                C(before.AsSpan().SequenceEqual(after),"F2 changed world/font textures with a visible sign.");if(hidden)Key(Keys.F1);
            }
        }
        private void VerifySignDirectionsAndBuffers()
        {
            World world=Session.World;Point3 cell=new(40,1,40);Vector3 oldPosition=Session.Camera.Position;float oldYaw=Session.Camera.Yaw,oldPitch=Session.Camera.Pitch;
            for(int octant=0;octant<8;octant++)
            {
                world.SetBlock(cell.X,cell.Y,cell.Z,BlockType.Air);byte facing=(byte)(octant/2+((octant&1)<<3));
                C(world.PlaceBuilding(cell,BlockType.Sign,facing,false,Session.Player.Bounds,new(0,-1,0)),"GPU diagonal sign placement failed.");world.EditSign(cell,"MDSM\nBIGGER\nTEXT");
                Vector3 normal=Vector3.TransformNormal(Vector3.Backward,World.SignTransform(cell,world.Structure(cell)));
                Session.Camera.Position=new Vector3(40.5f,1.75f,40.5f)+normal*3.5f;Vector3 aim=-normal;
                Session.Camera.RestoreOrientation(MathF.Atan2(aim.X,-aim.Z),0);RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);Render("sign-angle-"+octant);
            }
            Session.Camera.Position=new(40.5f,2.6f,44);Vector3 diagonalAim=new Vector3(40.5f,1.75f,40.5f)-Session.Camera.Position;
            Session.Camera.RestoreOrientation(0,MathF.Atan2(diagonalAim.Y,new Vector2(diagonalAim.X,diagonalAim.Z).Length()));Render("sign-diagonal");
            Point3 fireCell=new(55,1,55);
            for(int facing=0;facing<4;facing++)
            {
                Point3 d=World.FacingDirection(facing),fuel=fireCell+d;world.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.WoodLog);C(world.Fire.Ignite(fireCell,d),"GPU wall-fire fixture failed.");
                Session.Camera.Position=new Vector3(55.5f,2.6f,55.5f)-new Vector3(d.X,0,d.Z)*3.5f;
                Session.Camera.RestoreOrientation(MathF.Atan2(d.X,-d.Z),-.2f);Input();RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);Render("wall-fire-"+facing);
                world.SetBlock(fireCell.X,fireCell.Y,fireCell.Z,BlockType.Air);world.SetBlock(fuel.X,fuel.Y,fuel.Z,BlockType.Air);
            }
            Session.Camera.Position=oldPosition;Session.Camera.RestoreOrientation(oldYaw,oldPitch);
            long original=ChunkMesh.LiveBufferBytes;int buffers=ChunkMesh.LiveBuffers;
            var isolated=PigChecks.Flat();isolated.PlaceBuilding(cell,BlockType.Sign,8,false,Session.Player.Bounds,new(0,-1,0));isolated.EditSign(cell,"TEXT");
            using(var mesh=new ChunkMesh(GraphicsDevice,isolated.GetChunk(2,2)))
            {
                var builder=new ChunkMeshBuilder();mesh.Update(isolated,builder);C(mesh.HasText && ChunkMesh.LiveBuffers==buffers+2,"Sign font GPU buffer was not tracked separately.");
                long bytes=mesh.BufferBytes;isolated.EditSign(cell,"");mesh.Update(isolated,builder);
                C(!mesh.HasText && mesh.BufferBytes<bytes && ChunkMesh.LiveBuffers==buffers+1,"Cleared text retained its GPU buffer.");
            }
            C(ChunkMesh.LiveBufferBytes==original && ChunkMesh.LiveBuffers==buffers,"Disposed sign mesh leaked GPU buffers.");
        }
        private void VerifyDesertAndClouds()
        {
            World world=Session.World;for(int x=62;x<=68;x+=3)
            {world.SetBlock(x,0,70,BlockType.Sand);int height=2+(x-62)/3;for(int y=1;y<=height;y++)C(world.SetBlock(x,y,70,BlockType.Cactus),"GPU cactus fixture failed.");}
            world.SetBlock(64,0,72,BlockType.Sand);world.SetBlock(64,1,72,BlockType.DeadBush);
            Session.Camera.Position=new(65,3,78);Session.Camera.RestoreOrientation(0,-.15f);RenderingPerformanceChecks.Warm(F<WorldRenderer>("_renderer"),Session.Camera);Render("cacti-bush");
            Session.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.Cactus),1));Render("held-cactus");Session.Inventory.SetSlot(0,new(Items.FromBlock(BlockType.DeadBush),1));Render("held-bush");
            for(int z=1;z<=4;z++)for(int x=1;x<=4;x++)
            {
                uint hash=unchecked((uint)(x*73856093^z*19349663^world.Seed));hash^=hash>>13;hash*=1274126177;hash^=hash>>16;if(hash%7>2)continue;
                Session.Camera.Position=new(x*32+11,130,z*32+10);Session.Camera.RestoreOrientation(0,Camera.PitchLimit);Render("textured-clouds");
                var sky=typeof(WorldRenderer).GetField("_sky",Flags).GetValue(F<WorldRenderer>("_renderer"));var clouds=(CuboidModelBuilder)typeof(SkyRenderer).GetField("_clouds",Flags).GetValue(sky);
                C(clouds.Count>0 && clouds.Vertices.Take(clouds.Count).All(v=>SignFireChecks.Tile(v)==Blocks.Get(BlockType.Snow).SideTile),"Cloud geometry did not sample Snow.");return;
            }
            throw new Exception("Cloud fixture not found.");
        }
    }
}
