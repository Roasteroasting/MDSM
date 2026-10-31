using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;

namespace MDSM.Worlds;

public readonly record struct VegetationTimer(int Cell,double Remaining);
public sealed record VegetationState(int[] NaturalLeaves,VegetationTimer[] Decay,VegetationTimer[] Saplings);

// Sparse provenance, deadline queues and bounded connectivity. No per-world block-sized arrays.
public sealed class VegetationSimulation(World world)
{
    public const int SupportDistance=4, WorkBudget=32, MaximumSaplings=65536;
    public const double MinimumGrowth=60,MaximumGrowth=1800, GrowthRetry=30;
    public const double MinimumDecay=20,MaximumDecay=45;
    private readonly HashSet<int> _natural=new();
    private readonly Dictionary<int,double> _decay=new(),_saplings=new();
    private readonly PriorityQueue<int,double> _leafQueue=new(),_growthQueue=new();
    private readonly Random _random=new(unchecked(world.Seed^0x4c454146));
    private double _time;
    public int NaturalCount=>_natural.Count;
    public int PendingDecay=>_decay.Count;
    public int LastProcessed { get; private set; }
    internal bool CanPlant=>_saplings.Count<MaximumSaplings;
    private static readonly Point3[] Neighbors={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    internal int Key(Point3 p)=>(p.Y*world.Depth+p.Z)*world.Width+p.X;
    internal Point3 Cell(int key)=>new(key%world.Width,key/(world.Width*world.Depth),key/world.Width%world.Depth);
    public bool IsNatural(Point3 p)=>world.Contains(p.X,p.Y,p.Z) && _natural.Contains(Key(p));
    internal void MarkNatural(Point3 p)=>_natural.Add(Key(p));
    internal void Changed(Point3 p,BlockType previous,BlockType next)
    {
        int key=Key(p);_natural.Remove(key);_saplings.Remove(key);_decay.Remove(key);
        if(next==BlockType.Sapling)Schedule(_saplings,_growthQueue,key,MinimumGrowth+_random.NextDouble()*(MaximumGrowth-MinimumGrowth));
        if(previous is not (BlockType.WoodLog or BlockType.Leaves))return;
        for(int dy=-4;dy<=4;dy++)for(int dz=-4;dz<=4;dz++)for(int dx=-4;dx<=4;dx++)
        {
            if(Math.Abs(dx)+Math.Abs(dy)+Math.Abs(dz)>4)continue;
            Point3 n=p+new Point3(dx,dy,dz);if(!IsNatural(n))continue;
            int neighbor=Key(n);if(!_decay.ContainsKey(neighbor))Schedule(_decay,_leafQueue,neighbor,MinimumDecay+_random.NextDouble()*(MaximumDecay-MinimumDecay));
        }
    }
    private void Schedule(Dictionary<int,double> deadlines,PriorityQueue<int,double> queue,int cell,double delay)
    {double due=_time+delay;deadlines[cell]=due;queue.Enqueue(cell,due);}
    public bool Supported(Point3 origin)
    {
        // Manhattan radius four contains 129 cells. Each position is visited at most once.
        Span<Point3> cells=stackalloc Point3[129];Span<byte> distances=stackalloc byte[129];
        int count=1;cells[0]=origin;distances[0]=0;
        for(int i=0;i<count;i++)
        {
            if(distances[i]>=SupportDistance)continue;
            foreach(Point3 offset in Neighbors)
            {
                Point3 next=cells[i]+offset;BlockType block=world.GetBlock(next.X,next.Y,next.Z);
                if(block==BlockType.WoodLog)return true;
                if(block==BlockType.Leaves && !cells[..count].Contains(next))
                {cells[count]=next;distances[count++]=(byte)(distances[i]+1);}
            }
        }
        return false;
    }
    public void Update(double elapsed,DroppedItems items)
    {
        LastProcessed=0;if(!double.IsFinite(elapsed) || elapsed<=0)return;_time+=elapsed;
        Process(_leafQueue,_decay,false);Process(_growthQueue,_saplings,true);
        void Process(PriorityQueue<int,double> queue,Dictionary<int,double> deadlines,bool growth)
        {
            Span<ItemId> loot=stackalloc ItemId[3];
            while(LastProcessed<WorkBudget && queue.TryPeek(out int key,out double due) && due<=_time)
            {
                queue.Dequeue();LastProcessed++;
                if(!deadlines.TryGetValue(key,out double expected) || expected!=due)continue;
                deadlines.Remove(key);Point3 p=Cell(key);
                if(growth)
                {
                    if(world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Sapling)continue;
                    uint shape=(uint)_random.Next();
                    if(!WorldDecorator.TryTree(world,p.X,p.Y-1,p.Z,4+(int)(shape%3),2,shape,true))
                        Schedule(_saplings,_growthQueue,key,GrowthRetry+_random.NextDouble()*30);
                }
                else if(IsNatural(p) && !Supported(p))
                {
                    // Reserve worst-case three independent drops before changing the block.
                    if(items==null || !items.CanSpawn(3)){Schedule(_decay,_leafQueue,key,5);continue;}
                    int count=LeafLoot.Roll(_random,loot);
                    if(world.SetBlock(p.X,p.Y,p.Z,BlockType.Air))for(int i=0;i<count;i++)items.Spawn(loot[i],1,new Vector3(p.X+.5f,p.Y+.5f,p.Z+.5f));
                }
            }
        }
    }
    public VegetationState Capture()
    {
        // Bulk chunk restoration is used by loaders/fixtures; never serialize stale ownership.
        _natural.RemoveWhere(key=>{Point3 p=Cell(key);return world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Leaves;});
        foreach(int key in new List<int>(_decay.Keys))if(!_natural.Contains(key))_decay.Remove(key);
        foreach(int key in new List<int>(_saplings.Keys)){Point3 p=Cell(key);if(world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Sapling)_saplings.Remove(key);}
        int[] leaves=new int[_natural.Count];_natural.CopyTo(leaves);Array.Sort(leaves);
        VegetationTimer[] Timers(Dictionary<int,double> entries)
        {var result=new VegetationTimer[entries.Count];int i=0;foreach(var pair in entries)result[i++]=new(pair.Key,Math.Max(0,pair.Value-_time));Array.Sort(result,(a,b)=>a.Cell.CompareTo(b.Cell));return result;}
        return new(leaves,Timers(_decay),Timers(_saplings));
    }
    public void Restore(VegetationState state)
    {
        foreach(int key in state.NaturalLeaves)
        {Point3 p=Cell(key);if(key<0 || key>=world.BlockCount || world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Leaves || !_natural.Add(key))throw new InvalidDataException("Invalid natural leaf provenance.");}
        RestoreTimers(state.Decay,_decay,_leafQueue,false);RestoreTimers(state.Saplings,_saplings,_growthQueue,true);
        void RestoreTimers(VegetationTimer[] timers,Dictionary<int,double> deadlines,PriorityQueue<int,double> queue,bool growth)
        {
            foreach(var timer in timers)
            {
                Point3 p=Cell(timer.Cell);
                if(timer.Cell<0 || timer.Cell>=world.BlockCount || !double.IsFinite(timer.Remaining) || timer.Remaining<0 || timer.Remaining>(growth?MaximumGrowth:MaximumDecay)
                    || (growth?world.GetBlock(p.X,p.Y,p.Z)!=BlockType.Sapling:!_natural.Contains(timer.Cell)) || deadlines.ContainsKey(timer.Cell))throw new InvalidDataException("Invalid vegetation timer.");
                Schedule(deadlines,queue,timer.Cell,timer.Remaining);
            }
        }
    }
}

public static class LeafLoot
{
    public static int Roll(Random random,Span<ItemId> output)
    {
        int count=0;if(random.NextDouble()<.05)output[count++]=Items.Sapling;
        if(random.NextDouble()<.02)output[count++]=Items.Stick;
        if(random.NextDouble()<.005)output[count++]=Items.Apple;
        return count;
    }
}
