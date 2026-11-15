using System;
using System.Linq;
using MDSM.Entities;
using MDSM.Persistence;

namespace MDSM.Worlds.Streaming;

internal readonly record struct CellTimer(Point3 Cell,double Remaining);
internal sealed record ChunkVegetation(Point3[] Leaves,CellTimer[] Decay,CellTimer[] Saplings);
internal readonly record struct LocatedMob(MobState State,WorldPosition Position);
internal readonly record struct LocatedDrop(DroppedItemState State,WorldPosition Position);
internal readonly record struct LocatedArrow(ArrowState State,WorldPosition Position);
internal readonly record struct SignState(Point3 Cell,string Text);
internal sealed record ChunkGameplay
{
    public int Version {get;init;}=1;
    public StructureState[] Structures {get;init;}=Array.Empty<StructureState>();
    public SignState[] Signs {get;init;}=Array.Empty<SignState>();
    public BlockEntityState[] Containers {get;init;}=Array.Empty<BlockEntityState>();
    public ChunkVegetation Vegetation {get;init;}=new(Array.Empty<Point3>(),Array.Empty<CellTimer>(),Array.Empty<CellTimer>());
    public CropState[] Crops {get;init;}=Array.Empty<CropState>();
    public Point3[] Hydration {get;init;}=Array.Empty<Point3>();
    public FireState[] Fire {get;init;}=Array.Empty<FireState>();
    public Point3[] Water {get;init;}=Array.Empty<Point3>();
    public Point3[] Lava {get;init;}=Array.Empty<Point3>();
    public Point3[] Gravity {get;init;}=Array.Empty<Point3>();
    public FallingBlockState[] Falling {get;init;}=Array.Empty<FallingBlockState>();
    public LocatedMob[] Passive {get;init;}=Array.Empty<LocatedMob>();
    public LocatedMob[] Hostile {get;init;}=Array.Empty<LocatedMob>();
    public LocatedDrop[] Drops {get;init;}=Array.Empty<LocatedDrop>();
    public LocatedArrow[] Arrows {get;init;}=Array.Empty<LocatedArrow>();
    public MobRespawnState[] Respawns {get;init;}=Array.Empty<MobRespawnState>();
    // Only the complete owner chunk is persisted; replicas used by mesh workers are read-only.
    internal static bool Owns(ChunkAddress a,Point3 p)=>ChunkAddress.At(p.X,p.Z)==a;
    internal static bool Owns(ChunkAddress a,WorldPosition p)=>ChunkAddress.At((long)Math.Floor(p.X),(long)Math.Floor(p.Z))==a;
    internal static void Validate(ChunkAddress a,ChunkGameplay state)
    {
        if(state.Version!=1)throw new System.IO.InvalidDataException("Unsupported chunk gameplay version.");
        if(state.Structures==null||state.Signs==null||state.Containers==null||state.Vegetation==null||state.Vegetation.Leaves==null||state.Vegetation.Decay==null||state.Vegetation.Saplings==null
            ||state.Crops==null||state.Hydration==null||state.Fire==null||state.Water==null||state.Lava==null||state.Gravity==null||state.Falling==null||state.Passive==null||state.Hostile==null||state.Drops==null||state.Arrows==null||state.Respawns==null)
            throw new System.IO.InvalidDataException("Missing chunk gameplay sections.");
        foreach(var p in state.Structures.Select(s=>s.Cell).Concat(state.Signs.Select(s=>s.Cell)).Concat(state.Containers.Select(s=>s.Cell))
            .Concat(state.Vegetation.Leaves).Concat(state.Vegetation.Decay.Select(s=>s.Cell)).Concat(state.Vegetation.Saplings.Select(s=>s.Cell))
            .Concat(state.Crops.Select(s=>s.Cell)).Concat(state.Hydration).Concat(state.Fire.Select(s=>s.Cell)).Concat(state.Water).Concat(state.Lava).Concat(state.Gravity))
            if(!Owns(a,p)||p.Y<0||p.Y>=128)throw new System.IO.InvalidDataException("Chunk metadata escapes its owner.");
        foreach(var p in state.Passive.Select(s=>s.Position).Concat(state.Hostile.Select(s=>s.Position)).Concat(state.Drops.Select(s=>s.Position)).Concat(state.Arrows.Select(s=>s.Position)))
            if(!Owns(a,p)||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||!double.IsFinite(p.Z))throw new System.IO.InvalidDataException("Invalid entity owner/position.");
        foreach(var falling in state.Falling)if(ChunkAddress.At(falling.X,falling.Z)!=a)throw new System.IO.InvalidDataException("Falling block escapes its owner.");
        foreach(var pending in state.Respawns)if(pending.Home is not Point3 home||!Owns(a,home))throw new System.IO.InvalidDataException("Replacement escapes its owner.");
    }
}
