using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class MobMovementChecks
{
    internal static readonly (string Name,Action Check)[] All =
    {
        ("All mob walking speeds are measured at 1.5 blocks per second", Speeds),
        ("Passive wandering makes varied one-block jump/turn decisions without penetration", Obstacles),
        ("Passive wandering respects tall walls, low ceilings, fences and finite edges", Clearance),
        ("Obstacle decisions and motion remain deterministic across render rates", FrameRates)
    };
    private const float Dt=(float)SimulationClock.StepSeconds;
    private static void Check(bool condition,string message)=>CommandChecks.Check(condition,message);
    private static PassiveMob Walker(MobSpecies species,int seed,Vector3? feet=null,bool baby=false)
    {
        var mob=new PassiveMob(new(species,feet??new(30-MobDefinitions.Get(species).HalfWidth-.01f,1,30.5f),-MathHelper.PiOver2,MobDefinitions.Get(species).MaximumHealth,BabySeconds:baby?100:0),seed);
        // Start a real idle/wander encounter with a known heading. No production test hooks.
        typeof(PassiveMob).GetField("_wandering",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mob,true);
        typeof(PassiveMob).GetField("_decisionSeconds",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(mob,20f);
        return mob;
    }
    private static World Obstacle(int height=1,BlockType block=BlockType.Stone)
    {
        var world=PigChecks.Flat();
        for(int y=1;y<=height;y++)for(int z=24;z<=36;z++)world.SetBlock(30,y,z,block);
        return world;
    }
    public static void Speeds()
    {
        var world=PigChecks.Flat();
        foreach(MobSpecies species in Enum.GetValues<MobSpecies>())
        {
            float speed=MobDefinitions.Get(species).WalkSpeed;
            Check(speed==1.5f && speed<Player.MoveSpeed,"Mob walking speed is not the requested 1.5 blocks/second: "+species);
            var mob=Walker(species,17,new Vector3(60,1,60));
            for(int i=0;i<120;i++)mob.Update(world,Dt,Vector3.Right);
            Check(MathF.Abs(mob.Position.X-60-speed)<.002f && MathF.Abs(mob.Position.Z-60)<.0001f,"Actual velocity differs from registry speed: "+species);
        }
    }
    public static void Obstacles()
    {
        foreach(BlockType block in new[]{BlockType.Stone,BlockType.FarmlandDry})
        {
            var world=Obstacle(block:block);
            for(int species=0;species<4;species++)foreach(bool baby in new[]{false,true})
            {
                int jumps=0,turns=0;
                for(int seed=0;seed<48;seed++)
                {
                    var mob=Walker((MobSpecies)species,seed,baby:baby);float peak=1;bool turned=false;
                    for(int i=0;i<180;i++)
                    {
                        mob.Update(world,Dt);peak=Math.Max(peak,mob.Position.Y);
                        turned|=MathF.Cos(mob.Yaw+MathHelper.PiOver2)<0;
                        Check(!world.IntersectsSolid(mob.Bounds),"Unsteered jump/turn penetrated an obstacle.");
                    }
                    if(peak>1.1f)
                    {jumps++;Check(mob.Position.X>31 && !turned,"Chosen jump stopped/turned instead of crossing the one-block obstacle.");}
                    else {turns++;Check(turned && mob.Position.X<29,"Rejected jump did not choose an away heading and walk.");}
                }
                Check(jumps>=12 && turns>=12,"Wandering did not offer both random decisions for species "+species+" baby="+baby);
                Console.WriteLine($"  {(MobSpecies)species} {(baby?"baby":"adult")} {block}: {jumps} jumps / {turns} turns across 48 seeds.");
            }
        }
    }
    public static void Clearance()
    {
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken})
        foreach(string kind in new[]{"wall","ceiling","fence","edge"})
        {
            var world=kind=="edge"?PigChecks.Flat():Obstacle(kind=="wall"?2:1,kind=="fence"?BlockType.Fence:BlockType.Stone);
            if(kind=="ceiling")
            {
                int roof=(int)MathF.Ceiling(1+MobDefinitions.Get(species).Height);
                for(int x=26;x<=32;x++)for(int z=27;z<=33;z++)world.SetBlock(x,roof,z,BlockType.Stone);
            }
            for(int seed=0;seed<8;seed++)
            {
                var mob=Walker(species,seed,kind=="edge"?new Vector3(world.Width-MobDefinitions.Get(species).HalfWidth-.01f,1,30.5f):null);
                float peak=1;
                for(int i=0;i<120;i++)
                {
                    mob.Update(world,Dt);peak=Math.Max(peak,mob.Position.Y);
                    Check(!world.IntersectsSolid(mob.Bounds) && mob.Bounds.Min.X>=0 && mob.Bounds.Max.X<=world.Width,"Mob crossed a forbidden barrier or finite edge.");
                }
                Check(peak<=1.001f && MathF.Cos(mob.Yaw+MathHelper.PiOver2)<0,"Mob tried hopping an invalid obstacle: "+species+" "+kind);
            }
        }
        var enclosed=PigChecks.Flat();
        for(int x=29;x<=31;x++)for(int z=29;z<=31;z++)if(x!=30 || z!=30)
            for(int y=1;y<=3;y++)enclosed.SetBlock(x,y,z,BlockType.Stone);
        var trapped=Walker(MobSpecies.Pig,17,new Vector3(30.5f,1,30.5f));int choices=0;float yaw=trapped.Yaw;
        for(int i=0;i<120;i++){trapped.Update(enclosed,Dt);if(trapped.Yaw!=yaw)choices++;yaw=trapped.Yaw;}
        Check(choices is >=1 and <=3 && !enclosed.IntersectsSolid(trapped.Bounds),"Enclosed mob rerolled obstacle decisions every frame.");
    }
    public static void FrameRates()
    {
        var world=Obstacle();
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken})for(int seed=0;seed<4;seed++)
        {
            MobState? reference=null;
            foreach(int fps in new[]{30,60,144,360,997})
            {
                var mob=Walker(species,seed);var clock=new SimulationClock();
                for(int frame=0;frame<fps*2;frame++)for(int steps=clock.Advance(1d/fps);steps>0;steps--)mob.Update(world,Dt);
                Check(reference==null || reference.Value==mob.CaptureState(),"Jump/turn result depended on render rate.");reference=mob.CaptureState();
            }
        }
    }
    public static int Run()
    {
        var checks=All.Concat(new (string Name,Action Check)[]{("Existing species movement/damage/water/collision",MobChecks.DefinitionsAndMotion),
            ("Existing Pig movement/damage/finite boundaries",PigChecks.MovementAndDamage),("Existing cached collision remains equivalent",RenderingPerformanceChecks.CollisionCache),
            ("Existing held-Wheat steering, obstacle hopping and fixed steps",WheatFollowChecks.Movement),
            ("Existing two-block Wheat stopping distance at increased speeds",WheatFollowChecks.Following),
            ("Existing natural population and respawn accounting",WorldUpdateChecks.TargetsAndTimers)}).ToArray();
        int failed=0;foreach(var test in checks)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{checks.Length-failed}/{checks.Length} mob movement checks passed.");return failed==0?0:1;
    }
}
