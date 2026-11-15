using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Rendering;

// The worker sees a small immutable halo, never live gameplay storage. Coordinates
// are translated in integer space before producing any float vertex positions.
internal sealed record StreamedMeshResult(ChunkMeshBuilder Terrain,List<WaterFace> Water,byte[] Light);
internal sealed record StreamedMeshJob(Chunk Source,int Revision,int WaterRevision,Task<StreamedMeshResult> Task);

