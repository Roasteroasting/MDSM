using System;
using System.IO;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class LegacySmokeChecks
{
    internal static int Run()
    {
        try
        {
            var world=World.CreateNew(173,WorldSize.Large);
            var spawn=world.Spawn;
            var edit=new Point3((long)spawn.X,World.Height-1,(long)spawn.Z);
            if(world.Width!=1024 || world.Depth!=1024 || !world.SetBlock(edit.X,edit.Y,edit.Z,BlockType.Cobblestone))
                throw new InvalidOperationException("Legacy generation or basic block editing failed.");

            using var data=new MemoryStream();
            WorldSerializer.Write(data,new(world,"Legacy smoke",spawn,0,0));
            data.Position=0;
            var loaded=WorldSerializer.Read(data);
            if(loaded.World.Size!=WorldSize.Large || loaded.PlayerPosition!=spawn ||
                loaded.World.GetBlock(edit.X,edit.Y,edit.Z)!=BlockType.Cobblestone)
                throw new InvalidDataException("Legacy reload lost the spawn or edited block.");

            var mesh=new ChunkMeshBuilder();
            mesh.Build(loaded.World,loaded.World.GetChunk(edit.X/Chunk.Size,edit.Z/Chunk.Size));
            if(mesh.VertexCount==0)throw new InvalidOperationException("Legacy spawn terrain did not produce a mesh.");
            Console.WriteLine("PASS: Legacy (1024x1024) generation, block edit, save/load and spawn mesh.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
