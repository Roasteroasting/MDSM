using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

public sealed partial class WorldRenderer
{
    private readonly List<StreamedMeshJob> _jobs=new(2);
    private void ApplyChunkTransform(ChunkMesh mesh,Camera camera,EffectPass pass)
    {
        // Subtract Int64 origins before float conversion, independently for every chunk.
        var translation=Matrix.CreateTranslation(mesh.Chunk.OriginX-_world.OriginX,0,mesh.Chunk.OriginZ-_world.OriginZ);
        _terrainEffect.Parameters["View"].SetValue(translation*camera.View);
        _terrainEffect.Parameters["ViewProjection"].SetValue(translation*camera.View*camera.Projection(_device.Viewport.AspectRatio));pass.Apply();
    }
    private void PrepareStreamed(Camera camera,int budget)
    {
        long started=Stopwatch.GetTimestamp();RebuiltChunks=0;
        _frustum.Matrix=camera.View*camera.Projection(_device.Viewport.AspectRatio);
        if(_region.Update(camera.Position,_device.Viewport.AspectRatio))foreach(int i in _region.Removed){_meshes[i]?.Dispose();_meshes[i]=null;_water.Evict(i);}
        for(int i=_jobs.Count-1;i>=0;i--)
        {
            var job=_jobs[i];if(!job.Task.IsCompleted)continue;
            var result=job.Task.GetAwaiter().GetResult();_jobs.RemoveAt(i);
            int slot=job.Source.Slot;
            if(_world.ChunkAtSlot(slot)!=job.Source||!_region.Contains(slot)||job.Source.Revision!=job.Revision||job.Source.WaterRevision!=job.WaterRevision)continue;
            _world.Lighting.AcceptPage(job.Source.X,job.Source.Z,result.Light);
            _meshes[slot]??=new ChunkMesh(_device,job.Source);_meshes[slot].UploadPrepared(result.Terrain,job.Revision);_water.UploadPrepared(slot,result.Water,job.WaterRevision);
            RebuiltChunks++;PreparedChunks++;if(RebuiltChunks>=budget)break;
        }
        if(_jobs.Count<2)
        {
            Chunk next=null;float priority=float.MaxValue;
            foreach(int i in _region.Indices)
            {
                var chunk=_world.ChunkAtSlot(i);if(chunk==null||_meshes[i]!=null&&!_meshes[i].NeedsBuild&&!_water.NeedsBuild(i))continue;
                bool building=false;foreach(var job in _jobs)if(job.Source==chunk){building=true;break;}if(building)continue;
                float dx=chunk.OriginX-_world.OriginX+8-camera.Position.X,dz=chunk.OriginZ-_world.OriginZ+8-camera.Position.Z;
                float score=dx*dx+dz*dz;if(_meshes[i]?.NeedsBuild==true)score-=1000000;
                else if(!_frustum.Intersects(_world.RelativeBounds(chunk))&&score>1024)score+=100000;
                if(score<priority){next=chunk;priority=score;}
            }
            if(next!=null)
            {
                // Lighting and CPU geometry use the same immutable halo on the
                // worker. A tiny main-thread light budget starved this pipeline
                // at capped FPS, especially above terrain where no foreground
                // light query happened to finish a page as a side effect.
                World snapshot=_world.MeshSnapshot(next,false);int revision=next.Revision,waterRevision=next.WaterRevision;
                var task=Task.Run(()=>
                {
                    try
                    {
                        var center=snapshot.GetChunk(0L,0L);center.RestoreBlocks(center.Blocks);
                        var builder=new ChunkMeshBuilder();builder.Build(snapshot,center);
                        var faces=new List<WaterFace>();WaterMeshBuilder.Build(snapshot,center,faces);
                        return new StreamedMeshResult(builder,faces,snapshot.Lighting.GetPage(0,0).Data);
                    }
                    finally{snapshot.Lighting.ReleaseWorkerScratch();}
                });
                _jobs.Add(new(next,revision,waterRevision,task));
            }
        }
        MeshedChunks=GpuChunks=PendingChunks=0;BufferBytes=VertexCount=0;
        foreach(int i in _region.Indices){var mesh=_meshes[i];if(mesh==null||mesh.NeedsBuild||_water.NeedsBuild(i))PendingChunks++;if(mesh==null)continue;MeshedChunks++;if(mesh.BufferBytes>0)GpuChunks++;BufferBytes+=mesh.BufferBytes;VertexCount+=mesh.VertexCount;}
        PrepareMilliseconds=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}
