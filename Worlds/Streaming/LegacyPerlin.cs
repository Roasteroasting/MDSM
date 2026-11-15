using System;

namespace MDSM.Worlds.Streaming;

// Original implementation of improved gradient noise. Intentionally retains
// Beta's unbounded fractional remainders; never wrap input coordinates here.
internal sealed class LegacyPerlin
{
    private readonly int[] _permutation=new int[512];
    private readonly double _x,_y,_z;
    internal LegacyPerlin(Random random)
    {
        _x=random.NextDouble()*256;_y=random.NextDouble()*256;_z=random.NextDouble()*256;
        for(int i=0;i<256;i++)_permutation[i]=i;
        for(int i=0;i<256;i++){int j=random.Next(i,256);(_permutation[i],_permutation[j])=(_permutation[j],_permutation[i]);}
        for(int i=0;i<256;i++)_permutation[i+256]=_permutation[i];
    }
    internal static int JavaInt(double value)=>double.IsNaN(value)?0:value>=int.MaxValue?int.MaxValue:value<=int.MinValue?int.MinValue:(int)value;
    internal static int Lattice(double value)
    {int integer=JavaInt(value);return value<integer?unchecked(integer-1):integer;}
    internal static double Fade(double t)=>t*t*t*(t*(t*6-15)+10);
    internal static double Lerp(double a,double b,double t)=>a+t*(b-a);
    private static double Gradient(int hash,double x,double y,double z)
    {int h=hash&15;double a=h<8?x:y,b=h<4?y:h is 12 or 14?x:z;return((h&1)==0?a:-a)+((h&2)==0?b:-b);}
    internal double Sample(double x,double y,double z)
    {
        x+=_x;y+=_y;z+=_z;int ix=Lattice(x),iy=Lattice(y),iz=Lattice(z);
        x-=ix;y-=iy;z-=iz;
        int X=ix&255,Y=iy&255,Z=iz&255;
        double u=Fade(x),v=Fade(y),w=Fade(z);
        int a=_permutation[X]+Y,b=_permutation[X+1]+Y;
        int aa=_permutation[a]+Z,ab=_permutation[a+1]+Z,ba=_permutation[b]+Z,bb=_permutation[b+1]+Z;
        double first=Lerp(Lerp(Gradient(_permutation[aa],x,y,z),Gradient(_permutation[ba],x-1,y,z),u),
            Lerp(Gradient(_permutation[ab],x,y-1,z),Gradient(_permutation[bb],x-1,y-1,z),u),v);
        double second=Lerp(Lerp(Gradient(_permutation[aa+1],x,y,z-1),Gradient(_permutation[ba+1],x-1,y,z-1),u),
            Lerp(Gradient(_permutation[ab+1],x,y-1,z-1),Gradient(_permutation[bb+1],x-1,y-1,z-1),u),v);
        double result=Lerp(first,second,w);
        if(!double.IsFinite(result))throw new ArithmeticException("Noise exceeded the supported finite numerical range.");
        return result;
    }
}

internal sealed class LegacyOctaves
{
    private readonly LegacyPerlin[] _layers;
    internal LegacyOctaves(Random random,int count)
    {_layers=new LegacyPerlin[count];for(int i=0;i<count;i++)_layers[i]=new(random);}
    internal double Sample(double x,double y,double z)
    {
        double frequency=1,total=0;
        foreach(var layer in _layers){total+=layer.Sample(x*frequency,y*frequency,z*frequency)/frequency;frequency*=.5;}
        if(!double.IsFinite(total))throw new ArithmeticException("Octave accumulation exceeded finite range.");return total;
    }
}
