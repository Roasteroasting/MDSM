using System;
using MDSM.Worlds.Streaming;

namespace MDSM.Rendering;

// Stationary, absolute-coordinate coverage; only the cloud layer's translation
// changes with time. Smooth value noise produces broad connected blocky islands.
internal static class CloudPattern
{
    internal const int CellSize=8;
    internal static bool Occupied(long x,long z,int seed)=>Noise(x,z,10,seed)*.75+Noise(x,z,4,seed^0x4c4f5544)*.25>.51;
    private static double Noise(long x,long z,int scale,int seed)
    {
        long ix=Coordinates.FloorDiv(x,scale),iz=Coordinates.FloorDiv(z,scale);
        double fx=(double)Coordinates.Mod(x,scale)/scale,fz=(double)Coordinates.Mod(z,scale)/scale;
        fx=fx*fx*(3-2*fx);fz=fz*fz*(3-2*fz);
        double a=Hash(ix,iz,seed),b=Hash(ix+1,iz,seed),c=Hash(ix,iz+1,seed),d=Hash(ix+1,iz+1,seed);
        return (a+(b-a)*fx)*(1-fz)+(c+(d-c)*fx)*fz;
    }
    private static double Hash(long x,long z,int seed)
    {
        ulong n=unchecked((ulong)x*0x9e3779b185ebca87UL^(ulong)z*0xc2b2ae3d27d4eb4fUL^(uint)seed);
        n^=n>>30;n=unchecked(n*0xbf58476d1ce4e5b9UL);n^=n>>27;n=unchecked(n*0x94d049bb133111ebUL);n^=n>>31;
        return (n>>11)*(1.0/9007199254740992.0);
    }
}
