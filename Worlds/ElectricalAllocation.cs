using System;
using System.Collections.Generic;
using System.Linq;

namespace MDSM.Worlds;

/// <summary>Proportional, progressively filled allocation over unique sources and
/// consumers. A residual min-cut freezes bottlenecked loads while other circuits
/// continue filling. Parallel routes never multiply either supply or demand.</summary>
internal static class ElectricalAllocation
{
    internal sealed record Result(double[] Delivered,double[] Used);
    internal static Result Solve(double[] production,int[][] reachable,double[] requested)
    {
        var delivered=new double[requested.Length];var active=requested.Select(w=>w>0).ToArray();
        if(!active.Contains(true)||production.All(w=>w<=0))return new(delivered,new double[production.Length]);
        Flow final=null;
        var full=Build(1);
        if(full.Satisfied)
            return new(full.Demand.Select(e=>e.Initial-e.Remaining).ToArray(),full.Supply.Select(e=>e.Initial-e.Remaining).ToArray());
        for(int round=0;round<=requested.Length;round++)
        {
            double lo=0,hi=1;
            for(int step=0;step<36;step++)
            {
                double ratio=(lo+hi)/2;var trial=Build(ratio);
                if(trial.Satisfied)lo=ratio;else hi=ratio;
            }
            final=Build(lo);
            for(int i=0;i<requested.Length;i++)if(active[i])delivered[i]=requested[i]*lo;
            if(lo>1-1e-8||!active.Contains(true))break;
            var cut=Build(Math.Min(1,lo+1e-6));var fromSource=cut.Reachable();bool frozen=false;
            for(int i=0;i<requested.Length;i++)if(active[i]&&!fromSource[1+production.Length+i]){active[i]=false;frozen=true;}
            if(!frozen)break;
        }
        final=Build(0);
        var used=new double[production.Length];for(int i=0;i<used.Length;i++)used[i]=production[i]-final.Supply[i].Remaining;
        // Read actual sink flows, avoiding even numerical creation of a watt.
        for(int i=0;i<delivered.Length;i++)delivered[i]=final.Demand[i].Initial-final.Demand[i].Remaining;
        return new(delivered,used);

        Flow Build(double ratio)
        {
            var f=new Flow(2+production.Length+requested.Length,production.Length,requested.Length);
            for(int i=0;i<production.Length;i++)
            {
                f.Supply[i]=f.Add(0,1+i,production[i]);
                foreach(int load in reachable[i])f.Add(1+i,1+production.Length+load,1e20);
            }
            for(int i=0;i<requested.Length;i++)
            {
                double demand=active[i]?Math.Max(delivered[i],requested[i]*ratio):delivered[i];
                f.Demand[i]=f.Add(1+production.Length+i,f.Sink,demand);f.Required+=demand;
            }
            f.Run();return f;
        }
    }
    private sealed class Edge(int to,int reverse,double capacity)
    {public readonly int To=to,Reverse=reverse;public readonly double Initial=capacity;public double Remaining=capacity;}
    private sealed class Flow
    {
        private readonly List<Edge>[] _edges;
        private readonly int[] _level,_next,_queue;
        public readonly Edge[] Supply,Demand;
        public readonly int Sink;
        public double Required,Total;
        public bool Satisfied=>Total+1e-7>=Required;
        public Flow(int count,int sources,int loads)
        {
            Sink=count-1;_edges=new List<Edge>[count];for(int i=0;i<count;i++)_edges[i]=new();
            _level=new int[count];_next=new int[count];_queue=new int[count];Supply=new Edge[sources];Demand=new Edge[loads];
        }
        public Edge Add(int from,int to,double capacity)
        {
            var e=new Edge(to,_edges[to].Count,capacity);var reverse=new Edge(from,_edges[from].Count,0);
            _edges[from].Add(e);_edges[to].Add(reverse);return e;
        }
        public bool[] Reachable()
        {
            var seen=new bool[_edges.Length];int head=0,tail=0;_queue[tail++]=0;seen[0]=true;
            while(head<tail)foreach(var e in _edges[_queue[head++]])if(e.Remaining>1e-8&&!seen[e.To]){seen[e.To]=true;_queue[tail++]=e.To;}
            return seen;
        }
        public void Run()
        {
            while(true)
            {
                Array.Fill(_level,-1);int head=0,tail=0;_queue[tail++]=0;_level[0]=0;
                while(head<tail)
                {
                    int from=_queue[head++];foreach(var e in _edges[from])if(e.Remaining>1e-8&&_level[e.To]<0){_level[e.To]=_level[from]+1;_queue[tail++]=e.To;}
                }
                if(_level[Sink]<0)return;Array.Clear(_next);
                double pushed;while((pushed=Push(0,1e20))>1e-8)Total+=pushed;
            }
        }
        private double Push(int from,double amount)
        {
            if(from==Sink)return amount;
            for(;_next[from]<_edges[from].Count;_next[from]++)
            {
                var e=_edges[from][_next[from]];if(e.Remaining<=1e-8||_level[e.To]!=_level[from]+1)continue;
                double pushed=Push(e.To,Math.Min(amount,e.Remaining));if(pushed<=1e-8)continue;
                e.Remaining-=pushed;_edges[e.To][e.Reverse].Remaining+=pushed;return pushed;
            }
            return 0;
        }
    }
}
