using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Fixed-capacity presentation pool. No entities, saves or chunk mesh updates.
public sealed class BlockParticles
{
    public struct Particle { public Vector3 Position,Velocity; public float Life,Size; public int Tile,Patch; }
    public const int Capacity=384;
    private readonly Particle[] _particles=new Particle[Capacity];
    private int _cursor;
    public ReadOnlySpan<Particle> Active=>_particles;
    public void Burst(BlockType block,Vector3 position,bool landing=false)
    {
        if(!Blocks.IsRenderable(block)||Lava.Fluid(block))return;
        int count=landing?7:24;
        for(int i=0;i<count;i++)
        {
            float x=Random.Shared.NextSingle()-.5f,z=Random.Shared.NextSingle()-.5f;
            _particles[_cursor++%Capacity]=new(){Position=position+new Vector3(x,landing?.03f:Random.Shared.NextSingle()-.5f,z),
                Velocity=new(x*3,landing?1+Random.Shared.NextSingle():1+Random.Shared.NextSingle()*3,z*3),
                Life=landing?.28f:.55f+Random.Shared.NextSingle()*.3f,Size=landing?.045f:.065f,
                Tile=Blocks.Tile(block,Vector3.Up),Patch=Random.Shared.Next(64)};
        }
    }
    public void Update(World world,float dt)
    {
        if(!float.IsFinite(dt)||dt<=0)return;
        for(int i=0;i<_particles.Length;i++)
        {
            ref Particle p=ref _particles[i];if(p.Life<=0)continue;
            p.Life=Math.Max(0,p.Life-dt);p.Velocity.Y-=12*dt;
            Vector3 next=p.Position+p.Velocity*dt;
            if(world.IsSolid((int)MathF.Floor(next.X),(int)MathF.Floor(next.Y),(int)MathF.Floor(next.Z)))
            {p.Velocity*=.2f;p.Velocity.Y=0;}
            else p.Position=next;
        }
    }
}
