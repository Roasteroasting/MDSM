using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public enum Biome { Temperate, Desert, SnowyForest }
public sealed class Climate(int seed)
{
    // Older saves keep their original landscape and temperate weather identity.
    public bool VariedGeneration {get;internal set;}
    public Biome At(int x,int z)=>!VariedGeneration?Biome.Temperate:Temperature(seed,x,z) switch{> .32f=>Biome.Desert,< -.30f=>Biome.SnowyForest,_=>Biome.Temperate};
    internal bool WaterCandidate(int x,int z)
        =>At(x,z)!=Biome.Desert || DesertVegetation.Hash(seed^0x57415452,x,z)%100<15;
    internal static float Temperature(int seed,int x,int z)
    {
        float fx=x/180f,fz=z/180f;int ix=(int)MathF.Floor(fx),iz=(int)MathF.Floor(fz);fx-=ix;fz-=iz;
        fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
        float H(int a,int b){uint h=unchecked((uint)(a*73856093^b*19349663^seed^0x42494f4d));h=(h^(h>>16))*0x7feb352d;h^=h>>15;return(h&65535)/32767.5f-1;}
        return MathHelper.Lerp(MathHelper.Lerp(H(ix,iz),H(ix+1,iz),fx),MathHelper.Lerp(H(ix,iz+1),H(ix+1,iz+1),fx),fz);
    }
    internal void SnowCover(World world)
    {
        if(!VariedGeneration)return;
        for(int z=0;z<world.Depth;z++)for(int x=0;x<world.Width;x++)
        {
            if(At(x,z)!=Biome.SnowyForest)continue;int y=world.SurfaceY(x,z);
            if(y>=0 && y+1<World.Height && world.GetBlock(x,y+1,z)==BlockType.Air && !Lava.Fluid(world.GetBlock(x,y,z)))world.SetGeneratedBlock(x,y+1,z,BlockType.Snow);
        }
    }
}

public readonly record struct WeatherState(double UntilNext,double Remaining,float X,float Z,float Radius,uint RandomState,double MotionSeconds=0);
public sealed class Weather
{
    private readonly World _world;
    private uint _random;
    private double _next,_remaining,_snowTick;
    private float _x,_z,_radius;
    public double MotionSeconds {get;private set;}
    public bool Active=>_remaining>0;
    public double Remaining=>_remaining;
    public Weather(World world){_world=world;_random=unchecked((uint)world.Seed^0x57454154);if(_random==0)_random=1;_next=90+Next()*150;}
    private float Next(){_random^=_random<<13;_random^=_random>>17;_random^=_random<<5;return(_random&0xffffff)/16777216f;}
    public bool Precipitation(int x,int z)=>Active && _world.Climate.At(x,z)!=Biome.Desert && (x-_x)*(x-_x)+(z-_z)*(z-_z)<_radius*_radius;
    public bool RainAt(int x,int z)=>Precipitation(x,z) && _world.Climate.At(x,z)!=Biome.SnowyForest;
    public bool ExposedRain(Vector3 p)
    {int x=(int)MathF.Floor(p.X),z=(int)MathF.Floor(p.Z);return RainAt(x,z) && _world.SurfaceY(x,z)<p.Y;}
    public void Update(double dt,Vector3 player)
    {
        if(!double.IsFinite(dt)||dt<=0)return;
        MotionSeconds+=dt;
        if(_remaining>0)_remaining=Math.Max(0,_remaining-dt);
        else if((_next-=dt)<=0)
        {
            _next=180+Next()*300;
            if(Next()<.7f){_remaining=120+Next()*120;_x=Next()*_world.Width;_z=Next()*_world.Depth;_radius=Math.Max(72,_world.Width*(.2f+Next()*.15f));}
        }
        // Only a handful of exposed nearby columns accumulate snow per second.
        // Remote precipitation is visual/climate state, not a full-world block scan.
        _snowTick+=dt;if(_snowTick<1||!Active)return;_snowTick%=1;
        for(int i=0;i<8;i++)
        {
            int x=(int)player.X+(int)(Next()*65)-32,z=(int)player.Z+(int)(Next()*65)-32;
            if(!_world.Contains(x,0,z)||!Precipitation(x,z)||_world.Climate.At(x,z)!=Biome.SnowyForest)continue;
            int y=_world.SurfaceY(x,z);if(y>=0 && y+1<World.Height && _world.GetBlock(x,y+1,z)==BlockType.Air)_world.SetBlock(x,y+1,z,BlockType.Snow);
        }
    }
    public WeatherState Capture()=>new(_next,_remaining,_x,_z,_radius,_random,MotionSeconds);
    public void Restore(WeatherState s)
    {
        if(!double.IsFinite(s.UntilNext)||s.UntilNext<0||s.UntilNext>480||!double.IsFinite(s.Remaining)||s.Remaining<0||s.Remaining>240
            ||!float.IsFinite(s.X)||!float.IsFinite(s.Z)||!float.IsFinite(s.Radius)||s.X<0||s.X>_world.Width||s.Z<0||s.Z>_world.Depth||s.Radius<0||s.Radius>_world.Width||s.RandomState==0||!double.IsFinite(s.MotionSeconds)||s.MotionSeconds<0||s.MotionSeconds>1e12)throw new System.IO.InvalidDataException("Invalid weather state.");
        _next=s.UntilNext;_remaining=s.Remaining;_x=s.X;_z=s.Z;_radius=s.Radius;_random=s.RandomState;MotionSeconds=s.MotionSeconds;
    }
}
