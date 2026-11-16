using System;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;

namespace MDSM.Testing;

internal static class ClassicBobbingChecks
{
    internal static int Run()
    {
        try{RefinementChecks.Movement();RefinementChecks.BobbingPacing();Reference();Console.WriteLine("PASS: Classic bob formula, camera-local order, interpolation, rotations, fades and logical aim.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    internal static void Reference()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        var bob=new ViewBobbing();const float dt=1f/120;bob.UpdatePose(Vector3.Zero,true,true,dt);
        for(int i=1;i<=1215;i++)bob.UpdatePose(new(i*ViewBobbing.DistancePerCycle/120,0,0),true,true,dt);
        float phase=MathHelper.PiOver4,amplitude=ViewBobbing.VerticalAmplitude*ViewBobbing.DistancePerCycle/Player.MoveSpeed;
        var expected=Matrix.CreateRotationX(MathHelper.ToRadians(MathF.Abs(MathF.Cos(phase-.2f))*amplitude*5))
            *Matrix.CreateRotationZ(MathHelper.ToRadians(MathF.Sin(phase)*amplitude*3))
            *Matrix.CreateTranslation(MathF.Sin(phase)*amplitude*.5f,-MathF.Abs(MathF.Cos(phase))*amplitude,0);
        Matrix actual=bob.Transform(1);
        Check(Near(actual,expected,.0001f),"Classic synchronized transform differs from the reference formula.");
        Check(actual.M43==0&&actual.M42<0&&Math.Abs(actual.M12)>0&&Math.Abs(actual.M23)>0,"Bob has forward travel, upward orbit, or missing roll/pitch.");
        var camera=new Camera{Position=new(12,70,-9)};camera.RestoreOrientation(1.1f,.7f);
        Matrix unmodified=camera.View;Vector3 forward=camera.Forward,position=camera.Position;
        camera.PresentationTransform=actual;
        Check(Near(camera.View,unmodified*actual,.00001f)&&camera.Forward==forward&&camera.Position==position,"Presentation changed aim/position or used world-axis transforms.");
        Check(!Near(bob.Transform(0),bob.Transform(1),.00001f)&&!Near(bob.Transform(.5f),bob.Transform(1),.00001f),"Fixed-step presentation is not interpolated.");
        for(int i=0;i<1200;i++)
        {
            bob.UpdatePose(new(30.375f+i*.025f,0,0),true,true,dt);
            Check(bob.Transform(.5f).M42<=0&&bob.Transform(.5f).M43==0,"Walking traces an orbit or moves forward/backward.");
        }
        bob.UpdatePose(new(60.375f,0,0),false,false,dt);
        Check(bob.Transform(0)==Matrix.Identity&&bob.Transform(.5f)==Matrix.Identity&&bob.Transform(1)==Matrix.Identity,"Disabled bob leaves interpolated rotation/translation.");
        bob.Reset();Check(bob.Transform(.5f)==Matrix.Identity,"Reset retained presentation history.");
        Matrix? reference=null;
        foreach(int fps in new[]{30,60,144,360})
        {
            var clock=new MDSM.Core.SimulationClock();var sampled=new ViewBobbing();int steps=0;sampled.UpdatePose(Vector3.Zero,true,true,dt);
            for(int frame=0;frame<fps*2;frame++)
            {
                int count=clock.Advance(1.0/fps);
                for(int i=0;i<count;i++)sampled.UpdatePose(new(++steps*.025f,0,0),true,true,dt);
            }
            Matrix result=sampled.Transform(clock.Fraction);
            Check(steps==240&&(!reference.HasValue||Near(reference.Value,result,.0001f)),"Fixed simulation/render interpolation changed with frame rate.");reference=result;
        }
        var world=new MDSM.Worlds.World(173,MDSM.Worlds.WorldSize.Small,false);
        for(int z=6;z<=10;z++)for(int x=6;x<=10;x++)world.SetGeneratedBlock(x,0,z,MDSM.Worlds.BlockType.Stone);
        var player=new Player(new(8,1,8));player.Update(world,Vector3.Zero,false,dt);Check(player.Grounded,"Water eligibility fixture was not grounded.");
        foreach(bool creative in new[]{false,true})
        {
            player.Settings.GameMode=creative?MDSM.Core.GameMode.Creative:MDSM.Core.GameMode.Survival;
            world.SetGeneratedBlock(8,1,8,creative?MDSM.Worlds.BlockType.Air:MDSM.Worlds.BlockType.WaterSource);
            var suppressed=new ViewBobbing();suppressed.UpdatePose(player.Position-Vector3.Right*.43f,true,true,dt);
            suppressed.Update(player,true,.1f,world);
            Check(suppressed.Offset==Vector2.Zero&&suppressed.Transform(1)==Matrix.Identity,"Swimming or Creative flight started a walking bob.");
        }
    }
    private static bool Near(Matrix a,Matrix b,float e)=>Math.Abs(a.M11-b.M11)<e&&Math.Abs(a.M12-b.M12)<e&&Math.Abs(a.M13-b.M13)<e&&Math.Abs(a.M21-b.M21)<e&&Math.Abs(a.M22-b.M22)<e&&Math.Abs(a.M23-b.M23)<e&&Math.Abs(a.M31-b.M31)<e&&Math.Abs(a.M32-b.M32)<e&&Math.Abs(a.M33-b.M33)<e&&Math.Abs(a.M41-b.M41)<e&&Math.Abs(a.M42-b.M42)<e&&Math.Abs(a.M43-b.M43)<e;
}
