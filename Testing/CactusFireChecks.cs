using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Audio;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CactusFireChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static World World()
    {
        var world=new World(173,WorldSize.Small,false);
        for(int z=16;z<=25;z++)for(int x=16;x<=25;x++){world.SetGeneratedBlock(x,0,z,BlockType.Stone);world.SetGeneratedBlock(x,1,z,BlockType.Sand);}
        return world;
    }
    internal static void Cpu()
    {
        foreach(int fps in new[]{30,60,144,360})foreach(var position in new[]{new Vector3(19.7625f,2,20.5f),new(21.2375f,2,20.5f),new(20.5f,2,19.7625f),new(20.5f,2,21.2375f)})
        {
            var world=World();world.SetBlock(20,2,20,BlockType.Cactus);var session=new GameSession("cactus",new(world,"Cactus",position,0,0));
            Check(EnvironmentalHazards.CactusSideContact(world,session.Player.Bounds),"A cactus side missed actual player contact.");
            for(int i=0;i<fps*3;i++)session.Hazards.Update(session,1f/fps);
            Check(session.Player.HealthValue==17,"Cactus contact is not one HP per gameplay second.");
        }
        var w=World();w.SetBlock(20,2,20,BlockType.Cactus);
        Check(!EnvironmentalHazards.CactusSideContact(w,new(new(20.2f,3,20.2f),new(20.8f,4.8f,20.8f))),"Cactus top causes contact damage.");
        Check(!EnvironmentalHazards.CactusSideContact(w,new(new(20.2f,.2f,20.2f),new(20.8f,2,20.8f))),"Cactus bottom causes contact damage.");
        Check(!EnvironmentalHazards.CactusSideContact(w,new(new(19,2,20.2f),new(20,3.8f,20.8f))),"Cactus damage reaches across its inset gap.");
        var s=new GameSession("cactus",new(w,"Cactus",new(19.7625f,2,20.5f),0,0));s.Hazards.Update(s,.75f);s.Player.Teleport(new(18,2,20.5f));s.Hazards.Update(s,.1f);s.Player.Teleport(new(19.7625f,2,20.5f));s.Hazards.Update(s,.75f);Check(s.Player.Health==20,"Leaving cactus contact retained a partial damage tick.");
        s.ApplySettings(new(){GameMode=GameMode.Creative});s.Hazards.Update(s,5);Check(s.Player.Health==20,"Creative cactus contact bypassed invulnerability.");
        var walking=new GameSession("walking",new(w,"Cactus",new(18.8f,2,20.5f),0,0));walking.ApplySettings(new(){GameMode=GameMode.Survival});
        for(int i=0;i<360;i++){walking.Player.Update(w,Vector3.Right,false,1f/120);walking.Hazards.Update(walking,1f/120);}
        Check(walking.Player.Position.X<=19.763f&&walking.Player.Health==18,"Real wall collision does not trigger sustained cactus side contact.");
        var atop=new GameSession("top",new(w,"Cactus",new(20.5f,3,20.5f),0,0));
        for(int i=0;i<240;i++){atop.Player.Update(w,Vector3.Zero,false,1f/120);atop.Hazards.Update(atop,1f/120);}
        Check(atop.Player.Health==20&&Math.Abs(atop.Player.Position.Y-3)<.001f,"Standing on a cactus top is unsafe.");
        var fire=World();fire.Sounds=new SoundEvents();fire.Sounds.SetListener(new(20.5f,3,20.5f));Check(fire.Fire.Ignite(new(20,2,20)),"Fire fixture failed.");var before=fire.Fire.Capture();
        for(int i=0;i<120*20;i++){fire.Particles.Update(fire,1f/120);fire.Fire.UpdatePresentation(new(20.5f,3,20.5f),1f/120);}
        Check(fire.Fire.Capture().SequenceEqual(before),"Presentation changed fire simulation timers.");Check(fire.Particles.Active.ToArray().Any(p=>p.Spark),"Burning fire never emitted a spark.");
        int count=0;while(fire.Sounds.TryDequeue(out var sound)){Check(SoundCatalog.Groups[sound.Group].Kind==SoundKind.FireCrackle&&sound.Positional,"Fire emitted the wrong ambient request.");count++;}
        Check(count is >=70 and <=81,"Fire ambient heartbeat is not gameplay-time bounded.");fire.Fire.UpdatePresentation(new(200,2,200),1);Check(fire.Sounds.Count==0,"Distant fire creates ambient requests.");
        var pool=fire.Particles.Active.ToArray();fire.Fire.UpdatePresentation(new(20.5f,3,20.5f),0);Check(pool.SequenceEqual(fire.Particles.Active.ToArray()),"Paused presentation changed sparks.");
        fire.SetBlock(20,2,20,BlockType.Air);fire.Sounds.Clear();fire.Fire.UpdatePresentation(new(20.5f,3,20.5f),1);Check(fire.Sounds.Count==0,"Extinguished fire keeps emitting ambience.");
        Console.WriteLine("PASS: Four cactus sides at 30/60/144/360 Hz, top/bottom safety, inset gap, contact reset/Creative, bounded fire heartbeat/sparks, pause and extinguishing.");
    }
    internal static int Run(){try{Cpu();using var game=new Preview();game.Run();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private sealed class Preview:Game
    {
        internal Preview(){_=new GraphicsDeviceManager(this){PreferredBackBufferWidth=640,PreferredBackBufferHeight=360,SynchronizeWithVerticalRetrace=false};Content.RootDirectory=Path.Combine(AppContext.BaseDirectory,"Content");IsFixedTimeStep=false;}
        protected override void Draw(GameTime time)
        {
            var settings=new GameSettings{MasterVolume=.08f};using var audio=new AudioManager(Path.Combine(AppContext.BaseDirectory,"Content"),settings);Check(audio.Available&&audio.MissingClips==0,"Sound assets failed to load.");
            var world=World();world.Sounds=new SoundEvents();var listener=new Vector3(19,2.5f,20.5f);world.Sounds.SetListener(listener);world.Fire.Ignite(new(20,2,20));
            void Pump(){world.Fire.UpdatePresentation(listener,.25f);audio.Update(.25,null,world.Sounds,listener,Vector3.Right,true);}
            Pump();Check(audio.ActiveVoices==1,"Fire ambience did not start.");
            Array voices=(Array)typeof(AudioManager).GetField("_active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(audio);var voice=voices.GetValue(0);var instance=(SoundEffectInstance)voice.GetType().GetProperty("Instance").GetValue(voice);
            Check(instance.IsLooped&&instance.Pan>0&&instance.Volume>0&&instance.Volume<.02f,"Fire loop is not quiet/positional.");int played=audio.Played;
            for(int i=0;i<20;i++)Pump();Check(audio.ActiveVoices==1&&audio.Played==played,"Ambient heartbeats stack overlapping loops.");
            audio.Update(.7,null,null,listener,Vector3.Right,true);Check(audio.ActiveVoices==0,"Stale fire ambience did not stop.");Pump();audio.Update(0,null,null,listener,Vector3.Right,false);Check(audio.ActiveVoices==0,"Fire ambience plays while paused.");
            Pump();settings.EffectsVolume=0;audio.Update(0,null,null,listener,Vector3.Right,true);Check(audio.ActiveVoices==0,"Fire loop ignores live volume mute.");
            world.SetBlock(20,2,20,BlockType.Air);world.Particles.Spark(new(20.5f,2.7f,20.5f));world.Particles.Burst(BlockType.Dirt,new(19.7f,2.5f,20.5f));
            using var atlas=new TextureAtlas(Content);using var renderer=new WorldRenderer(GraphicsDevice,atlas,world);using var particles=new ParticleRenderer(GraphicsDevice,atlas.Texture);
            var camera=new Camera{Position=new(20.5f,3,23)};camera.RestoreOrientation(0,-.1f);RenderingPerformanceChecks.Warm(renderer,camera);using var target=new RenderTarget2D(GraphicsDevice,640,360,false,SurfaceFormat.Color,DepthFormat.Depth24);
            GraphicsDevice.SetRenderTarget(target);GraphicsDevice.Clear(world.Time.Sky);renderer.Draw(camera,null);
            var texture=GraphicsDevice.Textures[0];var blend=GraphicsDevice.BlendState;var depth=GraphicsDevice.DepthStencilState;var raster=GraphicsDevice.RasterizerState;var sampler=GraphicsDevice.SamplerStates[0];particles.Draw(camera,world,.5f);
            Check(GraphicsDevice.Textures[0]==texture&&GraphicsDevice.BlendState==blend&&GraphicsDevice.DepthStencilState==depth&&GraphicsDevice.RasterizerState==raster&&GraphicsDevice.SamplerStates[0]==sampler,"Mixed spark/debris pass leaks graphics state.");
            GraphicsDevice.SetRenderTarget(null);var pixels=new Color[640*360];target.GetData(pixels);Check(pixels.Any(p=>p.R>200&&p.G>100&&p.G<210&&p.B<100),"GPU sparks are not visible/emissive.");using(var file=File.Create(Path.Combine(AppContext.BaseDirectory,"fire-sparks.png")))target.SaveAsPng(file,640,360);
            Console.WriteLine("PASS: Actual MonoGame fire loop playback/heartbeat/pan/mute/pause, single-voice bound and mixed spark/debris GPU/state checks. Listening remains manual.");Exit();
        }
    }
}
