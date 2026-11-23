using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.Testing;

internal static class CubePlacementChecks
{
    private static readonly BlockType[] Stations =
    {
        BlockType.CraftingTable, BlockType.Furnace, BlockType.Smoker, BlockType.BlastFurnace,
        BlockType.BrickKiln, BlockType.MetalworkingBench, BlockType.Chest
    };
    private static readonly Point3[] Normals =
    {
        new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1)
    };

    internal static void Verify()
    {
        var world=new World(173,WorldSize.Small,false);
        var inventory=new Inventory();
        var farAway=new BoundingBox(new(64,5,64),new(65,7,65));
        var builder=new ChunkMeshBuilder();
        int x=3;
        foreach(BlockType station in Stations)
        {
            for(int facing=0;facing<4;facing++,x+=2)
            {
                var support=new Point3(x,5,16);
                world.SetBlock(support.X,support.Y,support.Z,BlockType.Stone);
                var hit=new BlockHit(support,new(0,1,0),2);
                float yaw=(facing-2)*MathHelper.PiOver2;
                inventory.SetSlot(0,new(Items.FromBlock(station),2));
                bool creative=facing%2==0;
                Require(BlockPlacement.TryPlace(world,inventory,hit,farAway,creative,yaw)
                    && inventory[0].Count==(creative?2:1),"Station placement or inventory accounting failed.");
                Point3 cell=hit.PlacementCell;
                Require(world.Structure(cell).Facing==facing,"Station did not face the player's yaw.");
                var definition=Blocks.Get(station);
                for(int side=0;side<4;side++)
                    Require(FaceTile(world,builder,cell,World.FacingDirection(side))==(side==facing?definition.FrontTile:definition.SideTile),
                        "World mesh used the wrong lateral tile.");
            }
        }

        for(int i=0;i<Normals.Length;i++)
        {
            var support=new Point3(4+i*4,7,26);
            Point3 normal=Normals[i];
            world.SetBlock(support.X,support.Y,support.Z,BlockType.Stone);
            var hit=new BlockHit(support,normal,2);
            inventory.SetSlot(0,new(Items.FromBlock(BlockType.WoodLog),2));
            Require(BlockPlacement.TryPlace(world,inventory,hit,farAway,false,MathHelper.PiOver2)
                && inventory[0].Count==1,"Log placement failed.");
            Point3 cell=hit.PlacementCell;
            Require(FaceTile(world,builder,cell,normal)==5,"Log end did not follow the clicked face.");
            Require(FaceTile(world,builder,cell,normal.Y==0 ? new(0,1,0) : new(1,0,0))==4,
                "A bark face became an end cap.");
            Require(world.Structures.ContainsKey(cell)==(normal.Y==0),"A default log needlessly stored metadata.");
        }

        var oldLog=new Point3(12,8,29);
        var oldChest=new Point3(15,8,29);
        world.SetGeneratedBlock(oldLog.X,oldLog.Y,oldLog.Z,BlockType.WoodLog);
        world.SetGeneratedBlock(oldChest.X,oldChest.Y,oldChest.Z,BlockType.Chest);
        Require(FaceTile(world,builder,oldLog,new(0,1,0))==5
            && FaceTile(world,builder,oldChest,new(0,0,-1))==27,"Old blocks lost their default appearance.");

        using var save=new MemoryStream();
        WorldSerializer.Write(save,new(world,"Cube facing",new(60,8,24),0,0));
        save.Position=0;
        var restored=WorldSerializer.Read(save).World;
        Require(restored.Structures.Count==world.Structures.Count,"Finite persistence changed metadata count.");
        foreach(var state in world.Structures)
            Require(restored.Structure(state.Key)==state.Value,"Finite persistence changed the placed orientation.");
        var firstLog=new Point3(5,7,26);
        Require(restored.SetBlock(firstLog.X,firstLog.Y,firstLog.Z,BlockType.Air)
            && !restored.Structures.ContainsKey(firstLog),"Removing a block retained stale orientation.");
        VerifyStreamed(farAway,inventory,builder);
    }

    private static int FaceTile(World world,ChunkMeshBuilder builder,Point3 cell,Point3 normal)
    {
        builder.BuildBlock(world,cell);
        Vector3 center=world.LocalPosition(cell)+new Vector3(.5f);
        Vector3 direction=new(normal.X,normal.Y,normal.Z);
        for(int index=0;index<builder.VertexCount;index++)
        {
            var vertex=builder.Vertices[index];
            // Only accept an entire triangle on the face; corners are shared.
            if(index%3!=0 || Vector3.Dot(vertex.Position-center,direction)!=.5f
                || Vector3.Dot(builder.Vertices[index+1].Position-center,direction)!=.5f
                || Vector3.Dot(builder.Vertices[index+2].Position-center,direction)!=.5f)continue;
            return (int)(vertex.TextureCoordinate.Y*16)*16+(int)(vertex.TextureCoordinate.X*16);
        }
        return -1;
    }

    private static void VerifyStreamed(BoundingBox player,Inventory inventory,ChunkMeshBuilder builder)
    {
        string root=Path.GetFullPath(AppContext.BaseDirectory);
        string region=Path.Combine(root,".cube-facing-"+Guid.NewGuid().ToString("N"));
        var home=new ChunkAddress(-1,0);
        var away=new ChunkAddress(6,0);
        var log=new Point3(-1,70,6);
        var furnace=new Point3(-2,70,10);
        var workbenches=new[]
        {
            (Cell: new Point3(-2,70,2), Block: BlockType.CraftingTable, Side: 17, Top: 16),
            (Cell: new Point3(-2,70,14), Block: BlockType.MetalworkingBench, Side: 94, Top: 95)
        };
        World live=null;
        try
        {
            using(var store=new ChunkRegionStore(region))
            {
                var blocks=new byte[Chunk.BlockCount];
                blocks[Coordinates.Index(14,70,6)]=(byte)BlockType.Stone;
                blocks[Coordinates.Index(14,69,10)]=(byte)BlockType.Stone;
                foreach(var bench in workbenches)
                    blocks[Coordinates.Index(14,69,(int)bench.Cell.Z)]=(byte)BlockType.Stone;
                store.WriteAsync(home,new ChunkSnapshot(home,BetaDensityGenerator.Version,blocks).Encode()).GetAwaiter().GetResult();
                var neighbor=new ChunkAddress(0,0);
                store.WriteAsync(neighbor,new ChunkSnapshot(neighbor,BetaDensityGenerator.Version,new byte[Chunk.BlockCount]).Encode()).GetAwaiter().GetResult();
                store.WriteAsync(away,new ChunkSnapshot(away,BetaDensityGenerator.Version,new byte[Chunk.BlockCount]).Encode()).GetAwaiter().GetResult();
            }
            live=World.OpenStreamed(173,region,2,0,0);
            Pump(home,()=>live.IsLoaded(log)&&live.IsLoaded(new(0,70,6)));
            inventory.SetSlot(0,new(Items.FromBlock(BlockType.WoodLog),1));
            Require(BlockPlacement.TryPlace(live,inventory,new BlockHit(new(-2,70,6),new(1,0,0),1),player),"Streamed log placement failed.");
            inventory.SetSlot(0,new(Items.FromBlock(BlockType.Furnace),1));
            Require(BlockPlacement.TryPlace(live,inventory,new BlockHit(new(-2,69,10),new(0,1,0),1),player,yaw:MathHelper.PiOver2),
                "Streamed furnace placement failed.");
            foreach(var bench in workbenches)
            {
                var support=new Point3(bench.Cell.X,bench.Cell.Y-1,bench.Cell.Z);
                inventory.SetSlot(0,new(Items.FromBlock(bench.Block),1));
                Require(BlockPlacement.TryPlace(live,inventory,new BlockHit(support,new(0,1,0),1),player,yaw:MathHelper.PiOver2),
                    "Streamed workbench placement failed.");
                live.SetBlock(support.X,support.Y,support.Z,BlockType.Air);
            }
            var meshWorld=live.MeshSnapshot(live.GetChunk(home.X,home.Z),includeLighting:false);
            Require(FaceTile(meshWorld,builder,new(15,70,6),new(1,0,0))==5
                && FaceTile(meshWorld,builder,new(14,70,10),new(-1,0,0))==24,"Worker mesh snapshot lost orientation.");
            Pump(away,()=>!live.IsLoaded(log));
            live.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            live=World.OpenStreamed(173,region,2,0,0);
            Pump(home,()=>live.IsLoaded(log));
            Require(live.Structure(log).Facing==1 && live.Structure(furnace).Facing==3
                && live.Containers.Get(furnace)?.Block==BlockType.Furnace,"Streamed reload lost orientation or furnace state.");
            meshWorld=live.MeshSnapshot(live.GetChunk(home.X,home.Z),includeLighting:false);
            foreach(var bench in workbenches)
            {
                var cell=new Point3(14,bench.Cell.Y,bench.Cell.Z);
                Require(live.GetBlock(bench.Cell.X,bench.Cell.Y,bench.Cell.Z)==bench.Block && live.Structure(bench.Cell).Facing==3,
                    "Streamed reload lost the workbench or its orientation.");
                for(int side=0;side<4;side++)
                    Require(FaceTile(meshWorld,builder,cell,World.FacingDirection(side))==bench.Side,
                        "Streamed workbench used the wrong lateral tile.");
                Require(FaceTile(meshWorld,builder,cell,new(0,1,0))==bench.Top && FaceTile(meshWorld,builder,cell,new(0,-1,0))==7,
                    "Streamed workbench used the wrong top or bottom tile.");
            }
        }
        finally
        {
            live?.ChunkLifecycle.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if(Directory.Exists(region) && Path.GetDirectoryName(region)==Path.TrimEndingDirectorySeparator(root))
                Directory.Delete(region,true);
        }

        void Pump(ChunkAddress center,Func<bool> ready)
        {
            var time=Stopwatch.StartNew();
            do
            {
                live.UpdateStreaming(center);
                if(live.ChunkLifecycle.Error is Exception error)throw error;
                if(time.Elapsed.TotalSeconds>20)throw new TimeoutException("Orientation streaming fixture did not finish.");
                Thread.Sleep(1);
            }while(!ready());
        }
    }

    private static void Require(bool value,string message)
    {
        if(!value)throw new InvalidOperationException(message);
    }
}
