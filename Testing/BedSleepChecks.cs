using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class BedSleepChecks
{
    internal static readonly Point3 Foot=new(20,1,20);
    internal static readonly (string Name,Action Check)[] All=
    {
        ("Sleep eligibility at the actual three-level sunlight threshold and dawn",Threshold),
        ("Four-way pillow camera, delayed smooth fade, exact return pose and fixed-time completion",Transition),
        ("Ten-block spherical monster exclusion across both Bed halves, species and obstacles",Monsters),
        ("Cancel, removed Bed, death, save/reload, invalid input and bounded allocation-free sleep",Safety)
    };
    internal static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    internal static GameSession Session(byte facing=0,MobState[] monsters=null)
    {
        var world=PigChecks.Flat();Vector3 player=new(23.5f,1,22.5f);
        Check(world.PlaceBed(Foot,facing,new Player(player).Bounds),"Sleep fixture Bed placement failed.");world.Time.Restore(600);
        var inventory=new Inventory();inventory.SetSlot(0,new(Items.Wheat,7));
        var session=new GameSession("sleep",new(world,"Sleep",player,.3f,-.45f,inventory,Hostiles:monsters));
        return session;
    }
    public static void Threshold()
    {
        var time=new WorldTime();Check(time.SunlightLevel==14 && !time.CanSleep,"Full daylight permitted sleep.");
        foreach(double seconds in new[]{540d,550,557}){time.Restore(seconds);Check(time.SunlightLevel>=12 && !time.CanSleep,"Sleep began before three light levels were lost.");}
        time.Restore(557.3);Check(time.SunlightLevel==11 && time.CanSleep && !time.IsNight,"Earlier level-eleven sleep failed.");
        foreach(double seconds in new[]{558d,600,660,900,1140,1180}){time.Restore(seconds);Check(time.CanSleep,"Dark-enough dusk/night/dawn was rejected.");}
        time.Restore(1195);Check(time.SunlightLevel>=12 && !time.CanSleep,"Bright dawn permitted sleep.");
        var session=Session();session.World.Time.Restore(WorldTime.Morning);var original=session.Camera.Position;
        Check(SurvivalInteractions.Use(session,new(Foot,default,1)) && !session.Sleep.IsSleeping && session.Camera.Position==original && session.World.Time.Seconds==WorldTime.Morning,"Daytime Bed use skipped time or moved the camera.");
        foreach(Point size in new[]{new Point(320,240),new(640,480),new(1280,720),new(2560,1080)})
        {
            Rectangle button=MenuController.SleepButtonBounds(new Viewport(0,0,size.X,size.Y));
            Check(button.Left>=0 && button.Right<=size.X && button.Top>=0 && button.Bottom<size.Y,"Leave Bed button escapes the window.");
        }
    }
    public static void Transition()
    {
        for(byte facing=0;facing<4;facing++)for(int half=0;half<2;half++)
        {
            var session=Session(facing);Point3 direction=World.BedDirection(facing),head=Foot+direction;
            Vector3 original=session.Camera.Position,body=session.Player.Position;float yaw=session.Camera.Yaw,pitch=session.Camera.Pitch;
            Check(SurvivalInteractions.Use(session,new(half==0?Foot:head,default,1)) && session.Sleep.IsSleeping,"Either Bed half failed to start sleeping.");
            Check(session.World.Time.Seconds==600 && session.Sleep.Fade==0 && session.Player.Position==body,"Bed use immediately changed time or body position.");
            Check(session.Camera.Position==new Vector3(head.X+.5f,head.Y+9f/16+.3f,head.Z+.5f),"Sleeping camera is not over the pillow.");
            Check(Vector3.Dot(session.Camera.HorizontalForward,new Vector3(-direction.X,0,-direction.Z))>.999f,"Sleeping camera faces away from the foot.");
            session.Sleep.Update(session,1);float alpha=session.Sleep.Fade;
            Check(alpha>0 && alpha<.5f && session.World.Time.Seconds==600,"Sleep did not darken gradually before skipping time.");
            session.Sleep.Update(session,2.99);Check(session.Sleep.IsSleeping && session.Sleep.Fade>alpha && session.Sleep.Fade>.99f,"Sleep ended before its fade completed.");
            session.Sleep.Update(session,.01);Check(!session.Sleep.IsSleeping && session.World.Time.Seconds==WorldTime.Morning && session.Player.Position==body
                && session.Camera.Position==original && session.Camera.Yaw==yaw && session.Camera.Pitch==pitch,"Waking changed the original body/camera pose.");
            session.Sleep.Update(session,10);Check(session.World.Time.Seconds==WorldTime.Morning,"Finished sleep skipped time repeatedly.");
        }
        foreach(int fps in new[]{5,30,60,144,360})
        {
            var session=Session();session.Sleep.TryBegin(session,new(Foot,default,1));float last=0;
            for(int i=0;i<fps*4-1;i++){session.Sleep.Update(session,1d/fps);Check(session.Sleep.IsSleeping && session.Sleep.Fade>=last,"Fade depended on frame rate or moved backward.");last=session.Sleep.Fade;}
            session.Sleep.Update(session,1d/fps);Check(!session.Sleep.IsSleeping && session.World.Time.Seconds==WorldTime.Morning,"Sleep completion depends on rendered FPS.");
        }
    }
    public static void Monsters()
    {
        foreach(MobSpecies species in new[]{MobSpecies.Zombie,MobSpecies.Skeleton,MobSpecies.Spider})
        {
            float half=MobDefinitions.Get(species).HalfWidth;
            foreach(Vector3 feet in new[]{new Vector3(31+half,1,20.5f),new Vector3(20.5f,1,9-half),new Vector3(20.5f,1,31+half),new Vector3(20.5f,11+9f/16,20.5f),new Vector3(27+half,1,29+half)})
            {
                var session=Session(monsters:new[]{new MobState(species,feet,0,MobDefinitions.Get(species).MaximumHealth)});
                for(int y=1;y<=4;y++)session.World.SetBlock(25,y,20,BlockType.Stone); // Solid cover does not exempt nearby monsters.
                Vector3 camera=session.Camera.Position;int revision=session.World.Revision;
                Check(!session.Sleep.TryBegin(session,new(Foot,default,1)) && session.Sleep.TakeNotice()==BedSleepController.MonsterNotice
                    && session.Camera.Position==camera && session.World.Time.Seconds==600 && session.World.Revision==revision,"Nearby/occluded monster did not reject sleeping safely.");
            }
            foreach(Vector3 feet in new[]{new Vector3(31.01f+half,1,20.5f),new Vector3(20.5f,11.01f+9f/16,20.5f),new Vector3(29+half,1,29+half)})
            {
                var session=Session(monsters:new[]{new MobState(species,feet,0,MobDefinitions.Get(species).MaximumHealth)});
                Check(session.Sleep.TryBegin(session,new(Foot,default,1)),"Monster outside the ten-block sphere prevented sleeping.");
            }
        }
        var removed=Session(monsters:new[]{new MobState(MobSpecies.Zombie,new(25,1,20),0,20)});
        removed.Hostiles.Hit(removed.Hostiles.Entities[0],100,removed.Player.Position,removed.Items);
        Check(removed.Sleep.TryBegin(removed,new(Foot,default,1)),"Killed monster continued to block sleeping.");
        var arriving=Session(monsters:new[]{new MobState(MobSpecies.Zombie,new(32,1,20.5f),0,20)});
        Check(arriving.Sleep.TryBegin(arriving,new(Foot,default,1)),"Distant monster incorrectly blocked entry.");
        for(int i=0;i<10;i++)arriving.Hostiles.Entities[0].Update(arriving.World,.1f,Vector3.Left);
        arriving.Sleep.Update(arriving,.1);Check(!arriving.Sleep.IsSleeping && arriving.World.Time.Seconds==600 && arriving.Sleep.TakeNotice()==BedSleepController.MonsterNotice,"Newly nearby monster did not safely cancel sleep.");
    }
    public static void Safety()
    {
        var session=Session();Vector3 original=session.Camera.Position;float yaw=session.Camera.Yaw,pitch=session.Camera.Pitch;
        Check(session.Sleep.TryBegin(session,new(Foot,default,1)),"Cancel fixture failed.");session.Sleep.Update(session,2);session.Sleep.Cancel(session.Camera);session.Sleep.Cancel(session.Camera);
        Check(session.World.Time.Seconds==600 && session.Camera.Position==original && session.Camera.Yaw==yaw && session.Camera.Pitch==pitch,"Leaving Bed altered time or original view.");
        Check(session.Sleep.TryBegin(session,new(Foot,default,1)) && session.Sleep.Fade==0,"Reentry retained old fade progress.");
        var capture=session.Capture();Check(capture.PlayerPosition==session.Player.Position && capture.Yaw==yaw && capture.Pitch==pitch,"Saving captured the temporary Bed orientation.");
        using(var stream=new MemoryStream())
        {WorldSerializer.Write(stream,capture);stream.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(stream));Check(!loaded.Sleep.IsSleeping && loaded.Camera.Position==original && loaded.Camera.Yaw==yaw && loaded.Inventory[0].Count==7 && loaded.World.Time.Seconds==600,"Saving mid-fade changed player/inventory/time on reload.");}
        session.World.SetBlock(Foot.X,Foot.Y,Foot.Z,BlockType.Air);session.Sleep.Update(session,.1);
        Check(!session.Sleep.IsSleeping && session.Camera.Position==original && session.World.Time.Seconds==600,"Removed Bed left a stale sleeping camera.");
        foreach(float distance in new[]{-1,float.NaN,BlockRaycaster.Reach+.01f})
        {session=Session();Check(!session.Sleep.TryBegin(session,new(Foot,default,distance)),"Invalid interaction reach started sleep.");}
        session=Session();session.Sleep.TryBegin(session,new(Foot,default,1));session.Player.TakeDamage(100,false);session.Sleep.Update(session,.1);
        Check(!session.Sleep.IsSleeping && session.World.Time.Seconds==600,"Death completed the night skip.");
        session=Session(monsters:new[]{new MobState(MobSpecies.Zombie,new(45,1,20),0,20)});session.Sleep.TryBegin(session,new(Foot,default,1));
        session.Sleep.Update(session,0);session.Sleep.Update(session,double.NaN);session.Sleep.Update(session,-1);Check(session.Sleep.Elapsed==0,"Invalid/paused elapsed time advanced sleeping.");
        for(int i=0;i<100;i++)session.Sleep.Update(session,.000001);
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)session.Sleep.Update(session,.000001);
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"Sleeping allocates or scans world storage every frame.");
    }
    public static int Run()
    {
        var checks=All.Concat(new (string Name,Action Check)[]{("Existing Bed geometry/drop/save checks",LightingChecks.Beds),("Existing sunlight/time/brightness",LightingChecks.TimeAndBrightness)}).ToArray();
        int failed=0;foreach(var test in checks)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} Bed sleep checks passed.");return failed==0?0:1;
    }
}
