using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds.Streaming;

public readonly record struct ChunkAddress(long X,long Z)
{
    public override string ToString()=>FormattableString.Invariant($"{X}, {Z}");
    public const int Side=16,RegionSide=32;
    public long BlockX=>checked(X*Side);
    public long BlockZ=>checked(Z*Side);
    public ChunkAddress Region=>new(Coordinates.FloorDiv(X,RegionSide),Coordinates.FloorDiv(Z,RegionSide));
    public int RegionSlot=>Coordinates.Mod(Z,RegionSide)*RegionSide+Coordinates.Mod(X,RegionSide);
    public static ChunkAddress At(long x,long z)=>new(Coordinates.FloorDiv(x,Side),Coordinates.FloorDiv(z,Side));
}

public static class Coordinates
{
    // Leave enough double mantissa bits for meaningful sub-block movement and
    // enough Int64 headroom for generation halos and region indexing.
    public const long MaximumBlock=1L<<40;
    public static long FloorDiv(long value,int divisor)
    {if(divisor<=0)throw new ArgumentOutOfRangeException(nameof(divisor));long q=value/divisor;return value%divisor<0?q-1:q;}
    public static int Mod(long value,int divisor)
    {if(divisor<=0)throw new ArgumentOutOfRangeException(nameof(divisor));long r=value%divisor;return (int)(r<0?r+divisor:r);}
    public static bool Valid(long x,long z)=>x>=-MaximumBlock && x<=MaximumBlock && z>=-MaximumBlock && z<=MaximumBlock;
    public static void Validate(ChunkAddress address)
    {if(!Valid(address.BlockX,address.BlockZ)||!Valid(checked(address.BlockX+15),checked(address.BlockZ+15)))throw new ArgumentOutOfRangeException(nameof(address));}
    public static int Index(int x,int y,int z)
    {if((uint)x>=16 || (uint)y>=128 || (uint)z>=16)throw new ArgumentOutOfRangeException(nameof(x));return(y*16+z)*16+x;}
}

public readonly record struct WorldPosition(double X,double Y,double Z)
{
    public override string ToString()=>FormattableString.Invariant($"{X:R}, {Y:R}, {Z:R}");
    public bool Valid=>double.IsFinite(X)&&double.IsFinite(Y)&&double.IsFinite(Z)&&Math.Abs(X)<=Coordinates.MaximumBlock&&Math.Abs(Z)<=Coordinates.MaximumBlock;
    public ChunkAddress Chunk=>ChunkAddress.At(checked((long)Math.Floor(X)),checked((long)Math.Floor(Z)));
    // Subtract before narrowing: GPU positions are always small local offsets.
    public Vector3 RelativeTo(WorldPosition origin)=>new((float)(X-origin.X),(float)(Y-origin.Y),(float)(Z-origin.Z));
    public static WorldPosition operator +(WorldPosition a,WorldPosition b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
}
