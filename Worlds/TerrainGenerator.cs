using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

public sealed class TerrainGenerator
{
    public const int MinimumSurface = 63, MaximumSurface = 114;
    public const int MountainMaximum=121;
    private readonly int _seed;
    private readonly bool _varied;
    private readonly int _width;

    public TerrainGenerator(int seed,bool varied=false,int width=0) { _seed=seed;_varied=varied;_width=width; }

    public int SurfaceY(int x, int z)
    {
        // Broad hills modulated by a slower field leave some areas relatively flat.
        float flatness = 0.8f + Noise(x / 320f + 31, z / 320f - 17) * 0.2f;
        float hills = Noise(x / 280f, z / 280f) * 34f
            + Noise(x / 110f + 7, z / 110f + 13) * 10f
            + Noise(x / 44f - 11, z / 44f + 5) * 2.5f;
        float height=88+hills*flatness;
        if(_varied)
        {
            float mountain=MountainStrength(x,z);
            if(mountain>0)
            {
            // Low/high octave fields blended by a third field create alternating
            // ridges, valleys and steep rock faces, rather than one smooth cone.
            float low=91+Noise(x/68f+37,z/68f-21)*18+Noise(x/23f-19,z/23f+11)*8;
            float ridge=1-MathF.Abs(Noise(x/27f+53,z/27f-49));
            float high=112+ridge*8+Noise(x/11f-31,z/11f+17)*7+Noise(x/5.5f+13,z/5.5f-7)*3;
            float blend=Math.Clamp((Noise(x/47f+73,z/47f-63)*3+.5f-.25f)/.5f,0,1);blend=blend*blend*(3-2*blend);
            float mask=Math.Clamp(mountain/.55f,0,1);mask=mask*mask*(3-2*mask);
            height=Lerp(height,Lerp(low,high,blend),mask);
            }
        }
        return Math.Clamp((int)MathF.Round(height),MinimumSurface,_varied?MountainMaximum:MaximumSurface);
    }
    internal float MountainStrength(int x,int z)
    {
        const int spacing=384;int cx=(int)MathF.Floor(x/(float)spacing),cz=(int)MathF.Floor(z/(float)spacing);float highest=0;
        for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
        {
            if(!Mountain(cx+dx,cz+dz,out Vector2 peak,out Vector2 radius))continue;
            float nx=(x-peak.X)/radius.X,nz=(z-peak.Y)/radius.Y,d=nx*nx+nz*nz;if(d>1.2f)continue;
            float edge=1+Noise(x/68f+19,z/68f-27)*.12f;
            float shoulder=Math.Max(0,1-MathF.Sqrt(d)/edge);
            highest=Math.Max(highest,MathF.Pow(shoulder,.65f));
        }
        if(_width>0)
        {float dx=x-_width/2f,dz=z-_width/2f;highest*=Math.Clamp((MathF.Sqrt(dx*dx+dz*dz)-24)/32,0,1);}
        return highest;
    }
    private bool Mountain(int gx,int gz,out Vector2 peak,out Vector2 radius)
    {
        uint h=unchecked((uint)(gx*73856093^gz*19349663^_seed^0x4d4f554e));h=(h^(h>>16))*0x7feb352d;h^=h>>15;
        peak=new(gx*384+128+(h>>2)%129,gz*384+128+(h>>10)%129);radius=new(80+(h>>18)%41,75+(h>>24)%46);
        if(_width>0 && Math.Abs(peak.X-_width/2f)<72 && Math.Abs(peak.Y-_width/2f)<72)peak.X+=peak.X<_width/2f?-96:96;
        return(h&3)!=0;
    }
    internal bool MountainIntersects(Rectangle bounds,int margin)
    {
        if(!_varied)return false;bounds.Inflate(margin,margin);
        for(int z=(int)MathF.Floor(bounds.Top/384f)-1;z<=(int)MathF.Floor(bounds.Bottom/384f)+1;z++)
        for(int x=(int)MathF.Floor(bounds.Left/384f)-1;x<=(int)MathF.Floor(bounds.Right/384f)+1;x++)
        {
            if(!Mountain(x,z,out var peak,out var r))continue;r*=1.12f;
            float nx=(Math.Clamp(peak.X,bounds.Left,bounds.Right)-peak.X)/r.X,nz=(Math.Clamp(peak.Y,bounds.Top,bounds.Bottom)-peak.Y)/r.Y;
            if(nx*nx+nz*nz<=1)return true;
        }
        return false;
    }

    public int DirtDepth(int x, int z) =>
        Math.Clamp(4 + (int)MathF.Round(Noise(x / 30f + 17, z / 30f - 23) * 2), 3, 5);

    internal void Generate(World world)
    {
        var heights=new int[world.Width*world.Depth];
        for(int z=0;z<world.Depth;z++)for(int x=0;x<world.Width;x++)heights[z*world.Width+x]=SurfaceY(x,z);
        foreach (Chunk chunk in world.Chunks)
        for (int z = 0; z < Chunk.Size; z++)
        for (int x = 0; x < Chunk.Size; x++)
        {
            int worldX = chunk.OriginX + x, worldZ = chunk.OriginZ + z;
            int top = heights[worldZ*world.Width+worldX];
            int dirt = DirtDepth(worldX, worldZ);
            bool desert=_varied && world.Climate.At(worldX,worldZ)==Biome.Desert;
            bool rock=_varied && MountainStrength(worldX,worldZ)>.3f && (top>112
                || Math.Abs(heights[worldZ*world.Width+Math.Min(world.Width-1,worldX+1)]-heights[worldZ*world.Width+Math.Max(0,worldX-1)])>=4
                || Math.Abs(heights[Math.Min(world.Depth-1,worldZ+1)*world.Width+worldX]-heights[Math.Max(0,worldZ-1)*world.Width+worldX])>=4);
            for (int y = 0; y <= top; y++)
                chunk.SetBlock(x, y, z, y == 0 ? BlockType.Bedrock : rock?BlockType.Stone:desert && y>=top-3?BlockType.Sand:y == top ? BlockType.Grass
                    : y >= top - dirt ? BlockType.Dirt : BlockType.Stone);
        }
    }

    // Seeded 2D gradient noise with Perlin's quintic interpolation. Hashing lattice
    // coordinates avoids permutation tables and is deterministic across chunk order.
    private float Noise(float x, float z)
    {
        int ix = (int)MathF.Floor(x), iz = (int)MathF.Floor(z);
        float fx = x - ix, fz = z - iz;
        float u = Fade(fx), v = Fade(fz);
        float bottom = Lerp(Gradient(ix, iz, fx, fz), Gradient(ix + 1, iz, fx - 1, fz), u);
        float top = Lerp(Gradient(ix, iz + 1, fx, fz - 1), Gradient(ix + 1, iz + 1, fx - 1, fz - 1), u);
        return Lerp(bottom, top, v);
    }

    private float Gradient(int x, int z, float dx, float dz)
    {
        uint hash;
        unchecked
        {
            hash = (uint)x * 0x8da6b343u ^ (uint)z * 0xd8163841u ^ (uint)_seed * 0xcb1ab31fu;
            hash = (hash ^ (hash >> 16)) * 0x7feb352du;
            hash = (hash ^ (hash >> 15)) * 0x846ca68bu;
            hash ^= hash >> 16;
        }
        return (hash & 7) switch
        {
            0 => dx,
            1 => -dx,
            2 => dz,
            3 => -dz,
            4 => (dx + dz) * 0.70710678f,
            5 => (dx - dz) * 0.70710678f,
            6 => (-dx + dz) * 0.70710678f,
            _ => (-dx - dz) * 0.70710678f
        };
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
