using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class HostileModelChecks
{
    private static readonly float[] Headings={0,MathHelper.PiOver2,MathHelper.Pi,-MathHelper.PiOver2,.7f};
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static void Pose(PassiveMob mob,bool walking,float phase)
    {
        typeof(PassiveMob).GetProperty(nameof(PassiveMob.Walking)).SetValue(mob,walking);
        typeof(PassiveMob).GetProperty(nameof(PassiveMob.WalkPhase)).SetValue(mob,phase);
    }
    private static Vector3[] LocalCuboid(CuboidModelBuilder mesh,int cuboid,PassiveMob mob)
    {
        Matrix inverse=Matrix.Invert(Matrix.CreateRotationY(mob.Yaw)*Matrix.CreateTranslation(mob.Position));
        var points=new Vector3[36];for(int i=0;i<36;i++)points[i]=Vector3.Transform(mesh.Vertices[cuboid*36+i].Position,inverse);return points;
    }
    public static void ZombieArms()
    {
        var mesh=new CuboidModelBuilder(64,16);
        foreach(float heading in Headings)for(int frame=0;frame<=32;frame++)
        {
            var mob=new PassiveMob(new(MobSpecies.Zombie,new(12,3,17),heading,20),1);
            Pose(mob,frame>0,frame*MathHelper.TwoPi/32);mesh.Clear();PassiveMobModels.Append(mesh,mob);
            Check(mesh.Count==6*36,"Zombie proportions/cuboid count changed.");
            for(int side=0;side<2;side++)
            {
                Vector3[] arm=LocalCuboid(mesh,side==0?3:5,mob);
                float sign=side==0?-1:1;
                Check(Math.Abs(arm.Min(p=>p.Z)+.65f)<.0001f && Math.Abs(arm.Max(p=>p.Z))<.0001f,"Zombie arm does not extend forward along local -Z.");
                Check(Math.Abs((arm.Min(p=>p.Y)+arm.Max(p=>p.Y))*.5f-1.28f)<.0001f,"Zombie arm is not horizontal at its shoulder.");
                Check(Math.Abs((arm.Min(p=>p.X)+arm.Max(p=>p.X))*.5f-sign*.31f)<.0001f,"Zombie shoulder position/symmetry changed.");
                Check(Math.Abs(arm.Max(p=>p.X)-arm.Min(p=>p.X)-.24f)<.0001f && Math.Abs(arm.Max(p=>p.Y)-arm.Min(p=>p.Y)-.24f)<.0001f,"Zombie arm thickness changed.");
                Vector3 forward=Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(heading));
                Vector3 shoulder=Vector3.Transform(new(sign*.31f,1.28f,0),Matrix.CreateRotationY(heading))+mob.Position;
                Check(mesh.Vertices.Skip((side==0?3:5)*36).Take(36).Max(v=>Vector3.Dot(v.Position-shoulder,forward))>.649f,"Zombie arm failed to follow body facing.");
            }
        }
    }
    public static void SpiderLegs()
    {
        var mesh=new CuboidModelBuilder(64,16);
        foreach(float heading in Headings)for(int frame=0;frame<=64;frame++)
        {
            var mob=new PassiveMob(new(MobSpecies.Spider,new(12,3,17),heading,16),1);
            Pose(mob,frame>0,frame*MathHelper.TwoPi/64);mesh.Clear();PassiveMobModels.Append(mesh,mob);
            Check(mesh.Count==10*36,"Spider lost body parts or legs.");
            for(int leg=0;leg<8;leg++)
            {
                int side=leg%2==0?-1:1;float z=-.25f+leg/2*.2f;
                Vector3[] points=LocalCuboid(mesh,leg+2,mob);Vector3[] tips=points.Where(p=>p.X*side>.5f).ToArray();
                Check(tips.Length>0 && tips.Max(p=>p.Y)<.3f,"Spider outer leg angles upward instead of down.");
                Check(points.Min(p=>p.Y)>=-.0001f && points.Max(p=>p.Y)<.53f,"Animated spider leg rises above its joint or tunnels below the feet.");
                Check(points.Any(p=>Vector3.Distance(p,new(side*.3f,.5f,z))<.06f),"Spider leg no longer attaches at its original joint.");
                Check(Math.Abs((points.Min(p=>p.Z)+points.Max(p=>p.Z))*.5f-z)<.0001f,"Spider front/back leg spacing changed.");
                if(frame==0 && side==1)
                {
                    Vector3[] left=LocalCuboid(mesh,leg+1,mob);
                    Check(points.All(p=>left.Any(q=>Vector3.Distance(p,new(-q.X,q.Y,q.Z))<.0001f)),"Idle spider leg pairs are not mirrored.");
                }
            }
        }
    }
    // Prints an exact before/after comparison for species sharing this model builder.
    public static void UnaffectedFingerprint()
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);var mesh=new CuboidModelBuilder(64,16);
        foreach(MobSpecies species in new[]{MobSpecies.Pig,MobSpecies.Cow,MobSpecies.Sheep,MobSpecies.Chicken,MobSpecies.Skeleton})
        foreach(float heading in Headings)for(int frame=0;frame<4;frame++)
        {
            var mob=new PassiveMob(new(species,new(12,3,17),heading,MobDefinitions.Get(species).MaximumHealth),1);
            Pose(mob,frame>0,frame*1.3f);mesh.Clear();PassiveMobModels.Append(mesh,mob);
            foreach(var vertex in mesh.Vertices.AsSpan(0,mesh.Count))
            {writer.Write(vertex.Position.X);writer.Write(vertex.Position.Y);writer.Write(vertex.Position.Z);writer.Write(vertex.Color.PackedValue);writer.Write(vertex.TextureCoordinate.X);writer.Write(vertex.TextureCoordinate.Y);}
        }
        writer.Flush();Console.WriteLine("  Unaffected species geometry/UV/color SHA-256: "+Convert.ToHexString(SHA256.HashData(stream.ToArray())));
    }
    public static int Run()
    {
        int failed=0;
        foreach(var test in new (string Name,Action Check)[]{("Forward zombie arms and yaw/pivot consistency",ZombieArms),("Downward spider legs throughout the gait and turning",SpiderLegs),("Shared passive/skeleton geometry",()=>{MobChecks.Geometry();UnaffectedFingerprint();})})
            try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e.Message);}
        return failed==0?0:1;
    }
    public static void VerifyGraphics(Game1 game)
    {
        GraphicsDevice device=game.GraphicsDevice;
        var world=new World(173,WorldSize.Small,false);var empty=new PassiveMobPopulation(world);
        using var renderer=new PassiveMobRenderer(device,game.Content);
        using var target=new RenderTarget2D(device,1024,896,false,SurfaceFormat.Color,DepthFormat.Depth24);
        using var sprites=new SpriteBatch(device);using var pixel=new Texture2D(device,1,1);pixel.SetData(new[]{Color.White});
        Color background=new(45,55,70);string[] angles={"FRONT","RIGHT","REAR","LEFT"};
        var originalTargets=device.GetRenderTargets();Viewport originalViewport=device.Viewport;Texture originalTexture=device.Textures[0];
        try
        {
            foreach(MobSpecies species in new[]{MobSpecies.Zombie,MobSpecies.Spider})
            {
                device.SetRenderTarget(target);device.Clear(background);
                for(int row=0;row<4;row++)for(int column=0;column<4;column++)
                {
                    device.Viewport=new Viewport(column*256,row*224,256,224);
                    var population=new HostilePopulation(world,new[]{new MobState(species,new(20,1,20),row>=2?.7f:0,MobDefinitions.Get(species).MaximumHealth)});
                    var mob=population.Entities[0];Pose(mob,row%2==1,row==1?1.1f:4.2f);
                    Vector3 center=mob.Position+Vector3.Up*(species==MobSpecies.Zombie?.9f:.4f);
                    Vector3 offset=column switch{0=>Vector3.Forward,1=>Vector3.Right,2=>Vector3.Backward,_=>Vector3.Left};
                    var camera=new Camera{Position=center+offset*(species==MobSpecies.Zombie?2.4f:1.8f)+Vector3.Up*.25f};
                    Vector3 forward=center-camera.Position;camera.RestoreOrientation(MathF.Atan2(forward.X,-forward.Z),MathF.Atan2(forward.Y,new Vector2(forward.X,forward.Z).Length()));
                    var blend=device.BlendState;var depth=device.DepthStencilState;var raster=device.RasterizerState;var sampler=device.SamplerStates[0];Texture texture=device.Textures[0];
                    renderer.Draw(camera,empty,population);
                    Check(device.Textures[0]==texture && device.BlendState==blend && device.DepthStencilState==depth && device.RasterizerState==raster && device.SamplerStates[0]==sampler,"Hostile model rendering leaked graphics state.");
                }
                device.Viewport=new Viewport(0,0,target.Width,target.Height);
                sprites.Begin(samplerState:SamplerState.PointClamp);
                for(int row=0;row<4;row++)for(int column=0;column<4;column++)
                    PixelFont.Draw(sprites,pixel,$"{angles[column]} / {(row>=2?"TURN ":"")}{(row%2==0?"IDLE":"WALK")}",column*256+8,row*224+8,1,Color.White);
                sprites.End();device.SetRenderTarget(null);
                var pixels=new Color[target.Width*target.Height];target.GetData(pixels);
                for(int row=0;row<4;row++)for(int column=0;column<4;column++)
                {
                    int visible=0;
                    for(int y=row*224+32;y<(row+1)*224;y++)for(int x=column*256;x<(column+1)*256;x++)if(pixels[y*target.Width+x]!=background)visible++;
                    Check(visible>150,$"{species} missing in camera view {column}, pose {row}.");
                }
                string filename=Path.Combine(AppContext.BaseDirectory,$"hostile-model-{species.ToString().ToLowerInvariant()}.png");
                using(var file=File.Create(filename))target.SaveAsPng(file,target.Width,target.Height);
                device.Textures[0]=null;device.Textures[0]=originalTexture;
            }
        }
        finally
        {
            device.SetRenderTargets(originalTargets);device.Viewport=originalViewport;
            device.Textures[0]=null;device.Textures[0]=originalTexture;
        }
        Console.WriteLine("PASS: Zombie/spider GPU front, rear, both sides, idle/walking poses, turning, textures and render-state preservation.");
    }
}
