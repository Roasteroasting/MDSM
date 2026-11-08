using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Audio;

namespace MDSM.Worlds;

public readonly record struct CropState(Point3 Cell,double Remaining,double StageSeconds);
public sealed class FarmingSimulation(World world)
{
    public const int MaximumCrops=65536,Budget=256;
    private sealed class Crop { public double Remaining,Interval,Due;public int Version;public bool Wet; }
    private readonly Dictionary<Point3,Crop> _crops=new();
    private readonly PriorityQueue<(Point3 Cell,int Version),double> _growth=new();
    private readonly Queue<Point3> _hydration=new();private readonly HashSet<Point3> _queued=new();
    private readonly Random _random=new(unchecked(world.Seed^0x43524f50));
    private double _time,_tick;
    public int LastProcessed { get; private set; }
    public int Count=>_crops.Count;
    internal bool CanPlant=>_crops.Count<MaximumCrops;
    public bool Hydrated(Point3 p)
    {
        for(int y=p.Y-1;y<=p.Y+1;y++)for(int z=p.Z-2;z<=p.Z+2;z++)for(int x=p.X-2;x<=p.X+2;x++)if(Water.IsWater(world.GetBlock(x,y,z)))return true;
        return false;
    }
    private void Queue(Point3 p){if(_queued.Add(p))_hydration.Enqueue(p);}
    internal void Changed(Point3 p,BlockType old,BlockType next)
    {
        if(Blocks.Soil(next))Queue(p);
        if(Water.IsWater(old)!=Water.IsWater(next))
            for(int y=p.Y-1;y<=p.Y+1;y++)for(int z=p.Z-2;z<=p.Z+2;z++)for(int x=p.X-2;x<=p.X+2;x++)
                if(Blocks.Soil(world.GetBlock(x,y,z)))Queue(new(x,y,z));
        if(Blocks.Crop(old) && !Blocks.Crop(next))
        {
            _crops.Remove(p);
        }
        if(Blocks.Crop(next) && !_crops.ContainsKey(p))
        {
            double interval=(600+_random.NextDouble()*1800)/6;var crop=new Crop{Remaining=interval,Interval=interval};_crops.Add(p,crop);Wet(p,crop,world.GetBlock(p.X,p.Y-1,p.Z)==BlockType.FarmlandWet);
        }
        if(Blocks.Soil(old) && !Blocks.Soil(next) && Blocks.Crop(world.GetBlock(p.X,p.Y+1,p.Z)))world.SetBlock(p.X,p.Y+1,p.Z,BlockType.Air);
        if(Blocks.Soil(old) && Blocks.Soil(next) && _crops.TryGetValue(p+new Point3(0,1,0),out var plant))Wet(p+new Point3(0,1,0),plant,next==BlockType.FarmlandWet);
    }
    private void Wet(Point3 p,Crop crop,bool wet)
    {
        if(crop.Wet==wet)return;
        if(crop.Wet)crop.Remaining=Math.Max(0,crop.Due-_time);
        crop.Wet=wet;crop.Version++;
        if(wet && !Blocks.MatureCrop(world.GetBlock(p.X,p.Y,p.Z))){crop.Due=_time+crop.Remaining;_growth.Enqueue((p,crop.Version),crop.Due);}
    }
    public bool Till(Point3 p,Inventory inventory,bool creative=false)
    {
        if(Items.Get(inventory.Selected.Item).Tool!=ToolKind.Hoe || world.GetBlock(p.X,p.Y,p.Z) is not (BlockType.Dirt or BlockType.Grass)
            || world.GetBlock(p.X,p.Y+1,p.Z)!=BlockType.Air)return false;
        if(!world.SetBlock(p.X,p.Y,p.Z,Hydrated(p)?BlockType.FarmlandWet:BlockType.FarmlandDry))return false;
        world.Sounds?.At(SoundKind.Till,BlockSounds.Center(p));if(!creative)inventory.WearSelected();return true;
    }
    public bool Plant(Point3 soil,Inventory inventory,bool creative=false)
    {
        ItemId item=inventory.Selected.Item;BlockType crop=item==Items.WheatSeeds?BlockType.Wheat1:item==Items.Carrot?BlockType.Carrot1:item==Items.Potato?BlockType.Potato1:BlockType.Air;
        if(crop==BlockType.Air || _crops.Count>=MaximumCrops || !Blocks.Soil(world.GetBlock(soil.X,soil.Y,soil.Z))
            || world.GetBlock(soil.X,soil.Y+1,soil.Z)!=BlockType.Air || !world.Contains(soil.X,soil.Y+1,soil.Z))return false;
        if(!world.SetBlock(soil.X,soil.Y+1,soil.Z,crop))return false;
        world.Sounds?.At(SoundKind.Plant,BlockSounds.Center(soil));if(!creative)inventory.ConsumeSelected();return true;
    }
    public void Update(double elapsed)
    {
        LastProcessed=0;if(!double.IsFinite(elapsed)||elapsed<=0)return;_time+=elapsed;_tick+=elapsed;if(_tick<1)return;_tick%=1;
        int hydration=Math.Min(Budget,_hydration.Count);
        for(int i=0;i<hydration;i++)
        {
            Point3 p=_hydration.Dequeue();_queued.Remove(p);LastProcessed++;
            BlockType block=world.GetBlock(p.X,p.Y,p.Z);if(Blocks.Soil(block))world.SetBlock(p.X,p.Y,p.Z,Hydrated(p)?BlockType.FarmlandWet:BlockType.FarmlandDry);
        }
        while(LastProcessed<Budget*2 && _growth.TryPeek(out var entry,out double due) && due<=_time)
        {
            _growth.Dequeue();LastProcessed++;
            if(!_crops.TryGetValue(entry.Cell,out var crop) || !crop.Wet || crop.Version!=entry.Version)continue;
            Point3 p=entry.Cell;BlockType stage=world.GetBlock(p.X,p.Y,p.Z);
            if(!Blocks.Crop(stage)||Blocks.MatureCrop(stage))continue;
            world.SetBlock(p.X,p.Y,p.Z,stage+1);crop.Remaining=crop.Interval;crop.Due=due+crop.Interval;_growth.Enqueue((p,crop.Version),crop.Due);
        }
    }
    public CropState[] Capture()
    {
        var result=new CropState[_crops.Count];int i=0;foreach(var pair in _crops)result[i++]=new(pair.Key,pair.Value.Wet?Math.Max(0,pair.Value.Due-_time):pair.Value.Remaining,pair.Value.Interval);
        Array.Sort(result,(a,b)=>a.Cell.Y!=b.Cell.Y?a.Cell.Y.CompareTo(b.Cell.Y):a.Cell.Z!=b.Cell.Z?a.Cell.Z.CompareTo(b.Cell.Z):a.Cell.X.CompareTo(b.Cell.X));return result;
    }
    public Point3[] CaptureHydration()
    {var result=new List<Point3>();foreach(Point3 p in _hydration)if(Blocks.Soil(world.GetBlock(p.X,p.Y,p.Z)))result.Add(p);return result.ToArray();}
    public void RestoreHydration(Point3[] pending)
    {
        var seen=new HashSet<Point3>();foreach(Point3 p in pending)
        {if(!world.Contains(p.X,p.Y,p.Z)||!Blocks.Soil(world.GetBlock(p.X,p.Y,p.Z))||!seen.Add(p))throw new InvalidDataException("Invalid pending hydration.");Queue(p);}
    }
    public void Restore(CropState[] states)
    {
        if(states.Length>MaximumCrops)throw new InvalidDataException("Too many crops.");
        foreach(var state in states)
        {
            Point3 p=state.Cell;if(!world.Contains(p.X,p.Y,p.Z)||!Blocks.Crop(world.GetBlock(p.X,p.Y,p.Z)) || !Blocks.Soil(world.GetBlock(p.X,p.Y-1,p.Z))
                || !double.IsFinite(state.Remaining)||state.Remaining<0||state.Remaining>400||!double.IsFinite(state.StageSeconds)||state.StageSeconds<100||state.StageSeconds>400||_crops.ContainsKey(p))throw new InvalidDataException("Invalid crop state.");
            var crop=new Crop{Remaining=state.Remaining,Interval=state.StageSeconds};_crops.Add(p,crop);Wet(p,crop,world.GetBlock(p.X,p.Y-1,p.Z)==BlockType.FarmlandWet);
        }
    }
}
