using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

public sealed partial class World
{
    internal World MeshSnapshot(Chunk center,bool includeLighting=true)
    {
        var copy=new World(Seed,WorldSize.Infinite,false);
        for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)
        {
            byte[] bytes;
            if(TryGetChunkAt(center.OriginX+dx*16,center.OriginZ+dz*16,out var source))bytes=source.Blocks.ToArray();
            else {bytes=new byte[32768];Array.Fill(bytes,(byte)BlockType.Bedrock);}
            var chunk=new Chunk(dx,dz,bytes);
            copy._resident.Add(new(dx,dz),chunk);
        }
        copy.RefreshResidents();
        Point3 Relative(Point3 p)=>new(p.X-center.OriginX,p.Y,p.Z-center.OriginZ);
        foreach(var pair in Structures)if(Math.Abs(pair.Key.X-center.OriginX)<=32 && Math.Abs(pair.Key.Z-center.OriginZ)<=32)
        {var p=Relative(pair.Key);copy.Structures[p]=pair.Value with{Cell=p};}
        foreach(var pair in Signs)if(Math.Abs(pair.Key.X-center.OriginX)<=32 && Math.Abs(pair.Key.Z-center.OriginZ)<=32)copy.Signs[Relative(pair.Key)]=pair.Value;
        foreach(var state in Fire.Capture())if(ChunkAddress.At(state.Cell.X,state.Cell.Z)==new ChunkAddress(center.X,center.Z))copy.Fire.Restore(new[]{state with{Cell=Relative(state.Cell)}});
        foreach(var entry in Containers.Entries.Values)
            if(entry.Burn>0&&entry.Cell.X>=center.OriginX-16&&entry.Cell.X<center.OriginX+32&&entry.Cell.Z>=center.OriginZ-16&&entry.Cell.Z<center.OriginZ+32)
                copy.Containers.Restore(new[]{entry.Capture() with{Cell=Relative(entry.Cell)}});
        if(includeLighting)copy.Lighting.SetSnapshot((byte[])Lighting.GetPage(center.X,center.Z).Data.Clone());
        else copy.Lighting.UseWorkerScratch();
        return copy;
    }
}
