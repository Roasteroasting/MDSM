using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal sealed class LeashRenderer(GraphicsDevice device) : IDisposable
{
    private const int Segments=12;
    private static readonly Color Rope=new(132,87,48);
    private static readonly Color Knot=new(170,117,66);
    private readonly BasicEffect _effect=new(device){VertexColorEnabled=true};
    private readonly HashSet<Point3> _knots=new();
    private VertexPositionColor[] _vertices=new VertexPositionColor[512];
    private int _count;

    internal void Draw(Camera camera,PassiveMobPopulation population,World world,Vector3? player)
    {
        _count=0;_knots.Clear();
        foreach(var mob in population.Entities)
        {
            if(!mob.Leash.Active || mob.IsDead)continue;
            Vector3 anchor;
            if(mob.Leash.Anchor==LeashAnchor.Fence)
            {
                Point3 fence=mob.Leash.Fence;
                if(!world.IsLoaded(fence) || world.GetBlock(fence.X,fence.Y,fence.Z)!=BlockType.Fence)continue;
                anchor=PassiveMobPopulation.FenceTip(world,fence);
                if(Vector3.DistanceSquared(mob.Position,anchor)>121)continue;
                if(_knots.Add(fence))DrawKnot(anchor,camera);
            }
            else
            {
                if(!player.HasValue)continue;
                anchor=player.Value;
            }
            Vector3 start=mob.Position+Vector3.Up*(mob.Bounds.Max.Y-mob.Position.Y)*.73f;
            if(Vector3.DistanceSquared(start,anchor)>121)continue;
            Vector3 previous=start;
            for(int i=1;i<=Segments;i++)
            {
                Vector3 next=RopePoint(start,anchor,i/(float)Segments);
                Ribbon(previous,next,camera,.014f,Rope);
                previous=next;
            }
        }
        if(_count==0)return;
        var raster=device.RasterizerState;
        try
        {
            device.RasterizerState=RasterizerState.CullNone;
            _effect.View=camera.View;_effect.Projection=camera.Projection(device.Viewport.AspectRatio);
            if(world!=null)UnderwaterView.Fog(_effect,camera,world.Time.Sky);
            foreach(var pass in _effect.CurrentTechnique.Passes)
            {pass.Apply();device.DrawUserPrimitives(PrimitiveType.TriangleList,_vertices,0,_count/3);}
        }
        finally {device.RasterizerState=raster;}
    }

    internal static Vector3 RopePoint(Vector3 start,Vector3 end,float t)
    {
        float sag=Math.Clamp((PassiveMobPopulation.LeashLength-Vector3.Distance(start,end))*.13f,.065f,.67f);
        return Vector3.Lerp(start,end,t)-Vector3.Up*(sag*4*t*(1-t));
    }

    private void DrawKnot(Vector3 center,Camera camera)
    {
        // The crossing turns on the central post read as a small tied knot from any side.
        for(int i=0;i<4;i++)
        {
            Vector3 a=Corner(i),b=Corner((i+1)%4);
            Ribbon(a,b,camera,.027f,Knot);
            Ribbon(a-Vector3.Up*.055f,b-Vector3.Up*.055f,camera,.027f,Rope);
        }
        Ribbon(center+new Vector3(-.055f,.08f,-.16f),center+new Vector3(.055f,-.075f,-.16f),camera,.031f,Knot);
        Vector3 Corner(int i)=>center+new Vector3(i<2?-.143f:.143f,0,i is 0 or 3?-.143f:.143f);
    }

    private void Ribbon(Vector3 a,Vector3 b,Camera camera,float thickness,Color color)
    {
        Vector3 side=Vector3.Cross(b-a,camera.Position-(a+b)*.5f);
        if(side.LengthSquared()<.0000001f)side=camera.Right;
        side=Vector3.Normalize(side)*thickness;
        if(_count+6>_vertices.Length)Array.Resize(ref _vertices,_vertices.Length*2);
        _vertices[_count++]=new(a-side,color);_vertices[_count++]=new(a+side,color);_vertices[_count++]=new(b+side,color);
        _vertices[_count++]=new(a-side,color);_vertices[_count++]=new(b+side,color);_vertices[_count++]=new(b-side,color);
    }

    public void Dispose()=>_effect.Dispose();
}
