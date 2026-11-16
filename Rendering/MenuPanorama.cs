using System;
using Microsoft.Xna.Framework;
using System.IO;
using Microsoft.Xna.Framework.Graphics;
namespace MDSM.Rendering;

// Six photographs of one generated landscape. No menu-time terrain or simulation.
internal sealed class MenuPanorama : IDisposable
{
    internal static readonly Vector3[] Directions = { Vector3.Forward, Vector3.Right, Vector3.Backward, Vector3.Left, Vector3.Up, Vector3.Down };
    internal static Vector3 Up(int face) => face == 4 ? Vector3.Backward : face == 5 ? Vector3.Forward : Vector3.Up;
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly Texture2D[] _faces = new Texture2D[6];
    private readonly VertexPositionTexture[][] _vertices = new VertexPositionTexture[6][];
    private double _seconds;
    internal float Angle => (float)(_seconds * .018);
    internal void Update(double elapsed) { if(double.IsFinite(elapsed) && elapsed>0) _seconds=(_seconds+elapsed)%(Math.PI*2/.018); }
    internal MenuPanorama(GraphicsDevice device)
    {
        _device=device;
        _effect=new BasicEffect(device){TextureEnabled=true,World=Matrix.Identity};
        try { for(int i=0;i<6;i++)
        {
            using(var file=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Content","UI","Panorama",i+".png"))) _faces[i]=Texture2D.FromStream(device,file);
            Vector3 f=Directions[i], up=Up(i), right=Vector3.Cross(f,up);
            Vector3 a=f-right+up,b=f+right+up,c=f+right-up,d=f-right-up;
            _vertices[i]=new[]{new VertexPositionTexture(a,new(0,0)),new(b,new(1,0)),new(c,new(1,1)),new(a,new(0,0)),new(c,new(1,1)),new(d,new(0,1))};
        } }
        catch { Dispose(); throw; }
    }
    internal void Draw(Viewport viewport)
    {
        // This is a background-only pass, followed by SpriteBatch; never touch depth contents.
        _device.BlendState=BlendState.Opaque;_device.DepthStencilState=DepthStencilState.None;
        _device.RasterizerState=RasterizerState.CullNone;_device.SamplerStates[0]=SamplerState.LinearClamp;
        Vector3 look=new(MathF.Sin(Angle),-.075f,-MathF.Cos(Angle));
        _effect.View=Matrix.CreateLookAt(Vector3.Zero,look,Vector3.Up);
        _effect.Projection=Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(70),viewport.AspectRatio,.01f,10);
        for(int i=0;i<6;i++){_effect.Texture=_faces[i];foreach(var pass in _effect.CurrentTechnique.Passes){pass.Apply();_device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices[i],0,2);}}
    }
    public void Dispose(){_effect.Dispose();foreach(var face in _faces)face?.Dispose();}
}

