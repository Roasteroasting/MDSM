using System;
using System.Collections.Generic;
using System.Linq;
using MDSM.Audio;
using MDSM.Worlds.Streaming;

namespace MDSM.Worlds;

/// <summary>Implement on a real machine; register while its owning chunk is resident.
/// Requests are watts. ReceivePower is called at the electrical cadence, including zero
/// after disconnection. Machines must scale their work by delivered/requested watts.</summary>
public interface IElectricalConsumer
{
    Point3 Cell { get; }
    double RequestedWatts { get; }
    void ReceivePower(double watts, double elapsedSeconds);
}

public readonly record struct ElectricalStatistics(double Production, double Requested, double Consumption,
    double Available, int Sources, int Outputs, int Consumers, int Attached = 0);

/// <summary>Main-thread, resident-only topology. Edits/publication invalidate the cached
/// graph; a quiet world never scans terrain. Gates have separate rear and front ports,
/// so cycles and parallel paths cannot manufacture another copy of a source.</summary>
public sealed class ElectricalNetwork(World world)
{
    public static readonly Point3[] Directions = { new(-1,0,0),new(1,0,0),new(0,-1,0),new(0,1,0),new(0,0,-1),new(0,0,1) };
    private readonly Dictionary<Point3, BlockType> _nodes = new();
    private readonly Dictionary<ChunkAddress, HashSet<Point3>> _chunks = new();
    private readonly Dictionary<Point3, IElectricalConsumer> _consumers = new();
    private HashSet<Point3> _meshConsumerPorts;
    private readonly Dictionary<Point3, ElectricalStatistics> _statistics = new();
    private readonly Dictionary<Point3, bool> _sky = new();
    private readonly Dictionary<(long X,long Z), HashSet<Point3>> _solarColumns = new();
    private readonly List<Bus> _buses = new();
    private readonly List<Gate> _gates = new();
    private readonly Dictionary<Point3, int> _busAt = new();
    private readonly Dictionary<Point3, int> _gateAt = new();
    private readonly List<Source> _sources = new();
    private readonly Dictionary<int,int[]> _busSources = new();
    private readonly List<(Point3 Cell,int Bus)> _accessPanels = new();
    private IElectricalConsumer[] _loads = Array.Empty<IElectricalConsumer>();
    private bool _dirty = true;
    private double _elapsed;
    private ChunkAddress? _center;
    public int TopologyBuilds { get; private set; }
    public int ResidentNodeCount => _nodes.Count;
    private sealed class Bus
    {
        public readonly List<int> Outgoing = new();
        public bool Access;
    }
    private sealed class Gate(Point3 cell, int input, int output, bool enabled, bool isSwitch)
    {
        public readonly Point3 Cell=cell;
        public readonly int Input=input, Output=output;
        public readonly bool Enabled=enabled, IsSwitch=isSwitch;
        public readonly HashSet<int> Allowed=new();
        public readonly HashSet<int> Downstream=new();
        public int[] Producers=Array.Empty<int>();
    }
    private sealed class Source(Point3 cell, int bus, int[] reachable, HashSet<int> gates)
    {
        public readonly Point3 Cell=cell;
        public readonly int Bus=bus;
        public readonly int[] Reachable=reachable;
        public readonly HashSet<int> Gates=gates;
        public double Watts;
    }
    public static bool IsBlock(BlockType b) => b is >= BlockType.CopperWire and <= BlockType.PowerSwitch;
    public static bool IsGate(BlockType b) => b is BlockType.OutputPanel or BlockType.PowerSwitch;
    public ElectricalStatistics Statistics(Point3 cell) => _statistics.GetValueOrDefault(cell);
    internal void CopyConsumerGeometry(ElectricalNetwork source,Point3 origin)
    {
        _meshConsumerPorts=new(source._consumers.Keys.Where(p=>Math.Abs(p.X-origin.X)<=32&&Math.Abs(p.Z-origin.Z)<=32)
            .Select(p=>new Point3(p.X-origin.X,p.Y,p.Z-origin.Z)));
    }
    internal void Invalidate() => _dirty=true;
    public void Register(IElectricalConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if(!world.IsLoaded(consumer.Cell))throw new InvalidOperationException("Electrical consumers require a resident owner chunk.");
        if(!_consumers.TryAdd(consumer.Cell,consumer))throw new InvalidOperationException("Duplicate electrical consumer.");
        _dirty=true;
    }
    public void Unregister(IElectricalConsumer consumer)
    {
        if(_consumers.TryGetValue(consumer.Cell,out var found)&&ReferenceEquals(found,consumer))
        {_consumers.Remove(consumer.Cell);consumer.ReceivePower(0,0);_dirty=true;}
    }
    internal void Changed(Point3 cell, BlockType before, BlockType after)
    {
        if(_solarColumns.TryGetValue((cell.X,cell.Z),out var column))foreach(var solar in column)_sky.Remove(solar);
        if(IsBlock(before))Remove(cell);
        if(IsBlock(after))Add(cell,after);
        if(_consumers.Remove(cell,out var removed)){removed.ReceivePower(0,0);_dirty=true;}
    }
    private void Add(Point3 cell,BlockType block)
    {
        _nodes[cell]=block;
        var address=ChunkAddress.At(cell.X,cell.Z);
        if(!_chunks.TryGetValue(address,out var cells))_chunks[address]=cells=new();
        cells.Add(cell);
        if(block==BlockType.SolarPanel)
        {
            if(!_solarColumns.TryGetValue((cell.X,cell.Z),out var column))_solarColumns[(cell.X,cell.Z)]=column=new();
            column.Add(cell);
        }
        _dirty=true;
    }
    private void Remove(Point3 cell)
    {
        _nodes.Remove(cell);_statistics.Remove(cell);_sky.Remove(cell);
        var address=ChunkAddress.At(cell.X,cell.Z);
        if(_chunks.TryGetValue(address,out var cells)&&cells.Remove(cell)&&cells.Count==0)_chunks.Remove(address);
        if(_solarColumns.TryGetValue((cell.X,cell.Z),out var column)&&column.Remove(cell)&&column.Count==0)_solarColumns.Remove((cell.X,cell.Z));
        _dirty=true;
    }
    internal void Publish(Chunk chunk)
    {
        // One vectorized rejection for ordinary terrain; inspect cells only in chunks
        // containing player-built electrical blocks. Never scan chunks on a tick.
        if(!chunk.Blocks.ContainsAnyInRange((byte)BlockType.CopperWire,(byte)BlockType.PowerSwitch))return;
        for(int y=0;y<World.Height;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {var b=chunk.GetBlock(x,y,z);if(IsBlock(b))Add(new(chunk.OriginX+x,y,chunk.OriginZ+z),b);}
    }
    internal void Detach(ChunkAddress address)
    {
        if(_chunks.TryGetValue(address,out var cells))foreach(var p in cells.ToArray())Remove(p);
        foreach(var pair in _consumers.Where(p=>ChunkAddress.At(p.Key.X,p.Key.Z)==address).ToArray())Unregister(pair.Value);
    }
    internal bool Connects(Point3 cell, Point3 side)
    {
        var b=world.GetBlock(cell.X,cell.Y,cell.Z);
        if(IsGate(b))
        {var front=World.FacingDirection(world.Structure(cell).Facing);return side==front||side==new Point3(-front.X,0,-front.Z);}
        return IsBlock(b)||_consumers.ContainsKey(cell)||_meshConsumerPorts?.Contains(cell)==true;
    }
    private void Rebuild()
    {
        _dirty=false;TopologyBuilds++;_buses.Clear();_gates.Clear();_busAt.Clear();_gateAt.Clear();_sources.Clear();_statistics.Clear();_busSources.Clear();_accessPanels.Clear();
        _loads=_consumers.Values.Where(c=>world.IsSimulated(c.Cell)).OrderBy(c=>c.Cell.X).ThenBy(c=>c.Cell.Y).ThenBy(c=>c.Cell.Z).ToArray();
        var cells=new HashSet<Point3>(_nodes.Where(p=>!IsGate(p.Value)&&world.IsSimulated(p.Key)).Select(p=>p.Key));
        foreach(var load in _loads)cells.Add(load.Cell);
        var queue=new Queue<Point3>();
        foreach(var start in cells)
        {
            if(_busAt.ContainsKey(start))continue;
            int index=_buses.Count;var bus=new Bus();_buses.Add(bus);_busAt[start]=index;queue.Enqueue(start);
            while(queue.TryDequeue(out var p))
            {
                if(_nodes.GetValueOrDefault(p)==BlockType.AccessPanel)bus.Access=true;
                foreach(var d in Directions){var n=p+d;if(cells.Contains(n)&&_busAt.TryAdd(n,index))queue.Enqueue(n);}
            }
        }
        // Each gate owns two separate vertices, including when gates touch directly.
        var port=new Dictionary<(Point3, bool),int>();
        foreach(var pair in _nodes.Where(p=>IsGate(p.Value)&&world.IsSimulated(p.Key)))
        {
            int input=_buses.Count;_buses.Add(new());int output=_buses.Count;_buses.Add(new());
            port[(pair.Key,false)]=input;port[(pair.Key,true)]=output;
            _gateAt[pair.Key]=_gates.Count;
            _gates.Add(new(pair.Key,input,output,!world.Structure(pair.Key).Open,pair.Value==BlockType.PowerSwitch));
        }
        var joins=new List<int>[_buses.Count];for(int i=0;i<joins.Length;i++)joins[i]=new();
        foreach(var gate in _gates)
        {
            var front=World.FacingDirection(world.Structure(gate.Cell).Facing);
            Join(gate.Input,new(-front.X,0,-front.Z));Join(gate.Output,front);
            void Join(int from,Point3 d)
            {
                var n=gate.Cell+d;int to;
                if(!_busAt.TryGetValue(n,out to))
                {
                    if(!_gateAt.ContainsKey(n))return;
                    var nf=World.FacingDirection(world.Structure(n).Facing);
                    var toward=new Point3(-d.X,0,-d.Z);
                    if(toward!=nf && toward!=new Point3(-nf.X,0,-nf.Z))return;
                    to=port[(n,toward==nf)];
                }
                joins[from].Add(to);joins[to].Add(from);
            }
        }
        // Collapse undirected port joins; directed gate traversal remains separate.
        var parent=Enumerable.Range(0,_buses.Count).ToArray();
        int Root(int n){while(parent[n]!=n){parent[n]=parent[parent[n]];n=parent[n];}return n;}
        for(int i=0;i<joins.Length;i++)foreach(int j in joins[i])parent[Root(j)]=Root(i);
        foreach(var p in _busAt.Keys.ToArray())_busAt[p]=Root(_busAt[p]);
        for(int i=0;i<_buses.Count;i++)if(Root(i)!=i)_buses[Root(i)].Access|=_buses[i].Access;
        for(int i=0;i<_gates.Count;i++)
        {
            var old=_gates[i];_gates[i]=new(old.Cell,Root(old.Input),Root(old.Output),old.Enabled,old.IsSwitch);
            _buses[Root(old.Input)].Outgoing.Add(i);
        }
        foreach(var gate in _gates)
        {
            var buses=Reach(gate.Output,false,-1,out _);
            for(int i=0;i<_loads.Length;i++)if(buses.Contains(_busAt[_loads[i].Cell]))gate.Downstream.Add(i);
            if(gate.IsSwitch)foreach(int i in gate.Downstream.OrderBy(i=>i).Take(ElectricalBalance.SwitchMachineLimit))gate.Allowed.Add(i);
        }
        foreach(var pair in _nodes.Where(p=>p.Value is BlockType.SolarPanel or BlockType.CoalGenerator))
        {
            if(!_busAt.TryGetValue(pair.Key,out int bus))continue;
            var reachable=new List<int>();var gates=new HashSet<int>();
            for(int i=0;i<_loads.Length;i++)
            {
                var powered=Reach(bus,true,i,out var traversed);gates.UnionWith(traversed);
                if(powered.Contains(_busAt[_loads[i].Cell]))reachable.Add(i);
            }
            // Panels must report sources even while no real machines exist.
            Reach(bus,true,-1,out var visible);gates.UnionWith(visible);
            _sources.Add(new(pair.Key,bus,reachable.ToArray(),gates));
        }
        foreach(var group in Enumerable.Range(0,_sources.Count).GroupBy(i=>_sources[i].Bus))_busSources[group.Key]=group.ToArray();
        foreach(var pair in _nodes)if(pair.Value==BlockType.AccessPanel&&_busAt.TryGetValue(pair.Key,out int bus))_accessPanels.Add((pair.Key,bus));
        for(int g=0;g<_gates.Count;g++){int gate=g;_gates[g].Producers=Enumerable.Range(0,_sources.Count).Where(i=>_sources[i].Gates.Contains(gate)).ToArray();}
    }
    private HashSet<int> Reach(int start,bool requireOutput,int consumer,out HashSet<int> traversed)
    {
        var seen=new HashSet<(int Bus,bool Distributed)>();var result=new HashSet<int>();traversed=new();
        var queue=new Queue<(int Bus,bool Distributed)>();queue.Enqueue((start,!requireOutput));
        while(queue.TryDequeue(out var next))
        {
            if(!seen.Add(next))continue;
            if(next.Distributed)result.Add(next.Bus);
            foreach(int index in _buses[next.Bus].Outgoing)
            {
                var gate=_gates[index];traversed.Add(index);
                if(gate.IsSwitch && (!next.Distributed||!gate.Enabled||consumer>=0&&!gate.Allowed.Contains(consumer)))continue;
                queue.Enqueue((gate.Output,next.Distributed||!gate.IsSwitch));
            }
        }
        return result;
    }
    public double SolarOutput(Point3 cell)
    {
        if(!world.IsSimulated(cell))return 0;
        if(!_sky.TryGetValue(cell,out bool exposed))
        {
            exposed=true;
            for(int y=cell.Y+1;y<World.Height;y++)
                if(world.GetBlock(cell.X,y,cell.Z)!=BlockType.Air){exposed=false;break;}
            _sky[cell]=exposed;
        }
        if(!exposed)return 0;
        double day=Math.Clamp(world.Time.SunDirection.Y,0,1)*world.Time.Daylight/14;
        int x=checked((int)(cell.X-world.OriginX)),z=checked((int)(cell.Z-world.OriginZ));
        return ElectricalBalance.SolarWatts*day*(world.Weather.Precipitation(x,z)?ElectricalBalance.OvercastMultiplier:1);
    }
    public void Update(double elapsed)
    {
        if(!double.IsFinite(elapsed)||elapsed<=0)return;
        _elapsed+=elapsed;if(_elapsed+1e-9<ElectricalBalance.UpdateSeconds)return;
        double dt=_elapsed;_elapsed=0;
        if(world.IsInfinite && _center!=world.ChunkLifecycle?.Center){_center=world.ChunkLifecycle?.Center;_dirty=true;}
        if(_dirty)Rebuild();
        var production=new double[_sources.Count];
        for(int i=0;i<_sources.Count;i++)
        {
            var s=_sources[i];
            if(_nodes[s.Cell]==BlockType.SolarPanel)s.Watts=_buses[s.Bus].Access?SolarOutput(s.Cell):0;
            else
            {
                var generator=world.Containers.Get(s.Cell);double before=generator.Burn;
                s.Watts=generator.Generate(dt);
                if((before>0)!=(generator.Burn>0))world.Lighting.Invalidate(s.Cell);
                if(before<=0&&s.Watts>0)world.Sounds?.At(SoundKind.Ignite,world.Center(s.Cell));
            }
            production[i]=s.Watts;
        }
        var requested=new double[_loads.Length];
        for(int i=0;i<_loads.Length;i++){double w=_loads[i].RequestedWatts;requested[i]=double.IsFinite(w)?Math.Clamp(w,0,1e12):0;}
        var allocation=ElectricalAllocation.Solve(production,_sources.Select(s=>s.Reachable).ToArray(),requested);
        for(int i=0;i<_loads.Length;i++)_loads[i].ReceivePower(allocation.Delivered[i],dt);
        foreach(var c in _consumers.Values)if(!world.IsSimulated(c.Cell))c.ReceivePower(0,dt);
        _statistics.Clear();
        // Tick only endpoints. In particular, neither a long wire run nor many
        // panels on one bus causes repeated source searches or wire-cell scans.
        for(int i=0;i<_sources.Count;i++)
        {
            var source=_sources[i];double demand=0;foreach(int load in source.Reachable)demand+=requested[load];
            _statistics[source.Cell]=new(production[i],demand,allocation.Used[i],Math.Max(0,production[i]-allocation.Used[i]),1,
                source.Gates.Count(g=>!_gates[g].IsSwitch),source.Reachable.Length);
        }
        var busStats=new Dictionary<int,ElectricalStatistics>();
        foreach(var panel in _accessPanels)
        {
            if(!busStats.TryGetValue(panel.Bus,out var stats))
                busStats[panel.Bus]=stats=Stats(_busSources.GetValueOrDefault(panel.Bus)??Array.Empty<int>(),null,production,requested,allocation,true);
            _statistics[panel.Cell]=stats;
        }
        for(int g=0;g<_gates.Count;g++)
        {
            var gate=_gates[g];
            var relevant=gate.Producers;
            var visible=new HashSet<int>(gate.Downstream);
            if(gate.IsSwitch)visible.IntersectWith(gate.Enabled?gate.Allowed:new HashSet<int>());
            _statistics[gate.Cell]=Stats(relevant,visible,production,requested,allocation) with{Attached=gate.IsSwitch?gate.Downstream.Count:0};
        }
    }
    private ElectricalStatistics Stats(int[] producers,HashSet<int> visible,double[] power,double[] demand,ElectricalAllocation.Result allocation,bool attributable=false)
    {
        var loads=new HashSet<int>();var gates=new HashSet<int>();double generated=0;
        foreach(int i in producers){generated+=power[i];loads.UnionWith(_sources[i].Reachable);gates.UnionWith(_sources[i].Gates);}
        if(visible!=null)loads.IntersectWith(visible);
        double requested=0,used=0;foreach(int i in loads){requested+=demand[i];used+=allocation.Delivered[i];}
        if(attributable)used=producers.Sum(i=>allocation.Used[i]);
        return new(generated,requested,used,Math.Max(0,generated-used),producers.Length,gates.Count(i=>!_gates[i].IsSwitch),loads.Count);
    }
}
