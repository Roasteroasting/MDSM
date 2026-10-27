using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class LargeLakeChecks
{
    private static readonly Point[] Neighbors = {new(1,0),new(-1,0),new(0,1),new(0,-1)};
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static int Run(){try{Notice();WaterOrdering();Generation();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    public static void Notice()
    {
        Require(WorldCreationLayout.NoticeText.Contains("2048 \u00d7 128 \u00d7 2048"),"Notice dimension symbols were corrupted.");
        foreach(WorldSize size in Enum.GetValues<WorldSize>())foreach(GameState state in Enum.GetValues<GameState>())
            Require(WorldCreationLayout.NoticeVisible(state,size)==(state==GameState.WorldCreation && size==WorldSize.Massive),"Incorrect notice visibility.");
        foreach(Point size in new[]{new Point(320,240),new Point(640,480),new Point(801,603),new Point(1280,720),new Point(1920,1080)})
        {
            var v=new Viewport(0,0,size.X,size.Y);Rectangle panel=WorldCreationLayout.Notice(v);string[] lines=WorldCreationLayout.Lines(v);
            Require(string.Join(' ',lines)==WorldCreationLayout.NoticeText && lines.All(s=>s.Length*6*WorldCreationLayout.NoticeScale(v)<=panel.Width-16),"Notice text was truncated or overflows.");
            Require(panel.Top>WorldCreationLayout.Field(v,2).Bottom && panel.Bottom<WorldCreationLayout.Button(v,0).Top
                && WorldCreationLayout.Button(v,0).Bottom<=size.Y && panel.Left>=0 && panel.Right<=size.X,"Notice overlaps controls or window bounds.");
            for(int i=0;i<3;i++)Require(WorldCreationLayout.Field(v,i).Top>40 && (i==0 || WorldCreationLayout.Field(v,i).Top>WorldCreationLayout.Field(v,i-1).Bottom),"Compact fields overlap.");
        }
    }
    public static void WaterOrdering()
    {
        var random=new Random(173);var keys=new WaterRenderer.WaterSortKey[40000];
        for(int i=0;i<keys.Length;i++)keys[i]=new WaterRenderer.WaterSortKey(i,random.Next(10000));
        Array.Sort(keys);
        for(int i=1;i<keys.Length;i++)Require(keys[i-1].Distance>keys[i].Distance
            || keys[i-1].Distance==keys[i].Distance && keys[i-1].Index<keys[i].Index,"Compact water keys lost back-to-front ordering or stable ties.");
        Require(keys.Select(k=>k.Index).Distinct().Count()==keys.Length,"Sorting duplicated/lost a water face.");
        for(int round=0;round<2;round++)
        {
            for(int i=0;i<keys.Length;i++)keys[i]=keys[i] with {Distance=round==0 ? keys[i].Distance+random.NextSingle()*.1f : random.Next(10000)};
            WaterRenderer.UpdateOrder(keys,keys.Length);
            for(int i=1;i<keys.Length;i++)Require(keys[i-1].CompareTo(keys[i])<0,"Walking repair or large-motion fallback changed distance ordering.");
            Require(keys.Select(k=>k.Index).Distinct().Count()==keys.Length,"Repair/fallback duplicated or lost water faces.");
        }
    }
    public static void Generation()
    {
        foreach(WorldSize size in new[]{WorldSize.Large,WorldSize.Massive})
        foreach(int seed in new[]{173,-907,137}){CheckWorld(size,seed);GC.Collect();GC.WaitForPendingFinalizers();}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckWorld(WorldSize size,int seed)
    {
        var watch=Stopwatch.StartNew();World world=World.CreateNew(seed,size);
        Console.WriteLine($"LAKES {size} seed={seed}: {world.GeneratedWaterFeatures}, generation={watch.Elapsed.TotalSeconds:F2}s, managedMiB={GC.GetTotalMemory(false)/1048576.0:F1}");
        Require(world.GeneratedLakes.Count>=(size==WorldSize.Large?2:6) && world.GeneratedLakes.Count<=LakeGenerator.LakeBudget(size),"Representative seed missed lake population.");
        Require(world.GeneratedWaterFeatures.Ponds==LakeGenerator.MaximumLakes(size) && world.GeneratedWaterFeatures.Rivers==LakeGenerator.RiverBudget(size),"Major lakes displaced pond/river budgets.");
        foreach(NaturalLake lake in world.GeneratedLakes)Measure(world,lake,true);
        Require(world.Fluids.PendingCount==0 && world.Gravity.PendingCount==0,"Generated water/gravity frontier is active.");
        int revision=world.Revision;for(int i=0;i<10;i++)world.Fluids.Tick();
        Require(world.Revision==revision,"Natural lake changed without edits.");
        NaturalLake[] expected=world.GeneratedLakes.ToArray();world=null;GC.Collect();GC.WaitForPendingFinalizers();
        Regenerate(size,seed,expected);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Regenerate(WorldSize size,int seed,NaturalLake[] expected)
    {
        World world=new World(seed,size,false);new TerrainGenerator(seed).Generate(world);
        var generator=new LakeGenerator(seed);generator.Generate(world);
        Require(expected.AsSpan().SequenceEqual(generator.Lakes.ToArray()),"Lake plans are not deterministic.");
        foreach(NaturalLake lake in generator.Lakes)Measure(world,lake,false);
    }
    private static int Ground(World w,int x,int z)
    {
        int y=w.SurfaceY(x,z);
        while(y>0 && w.GetBlock(x,y,z) is not (BlockType.Grass or BlockType.Dirt or BlockType.Stone or BlockType.Gravel or BlockType.Sand or BlockType.Bedrock))y--;
        return y;
    }
    private static void Measure(World world,NaturalLake lake,bool print)
    {
        Rectangle b=lake.Bounds;int count=0,deepest=0,minX=int.MaxValue,minZ=int.MaxValue,maxX=0,maxZ=0;
        Require(lake.WaterY<=TerrainGenerator.MaximumSurface,"Lake rim exceeded the natural land height limit.");
        var wet=new bool[b.Width*b.Height];var seen=new bool[wet.Length];
        Require(b.Left>2 && b.Top>2 && b.Right<world.Width-2 && b.Bottom<world.Depth-2
            && !b.Intersects(new Rectangle(world.Width/2-28,world.Depth/2-28,56,56)),"Lake violated boundaries or spawn.");
        for(int z=b.Top;z<b.Bottom;z++)for(int x=b.Left;x<b.Right;x++)
            wet[(z-b.Top)*b.Width+x-b.Left]=Water.IsWater(world.GetBlock(x,lake.WaterY,z));
        // A separate preserved pond can fit inside a broad cove's bounding rectangle.
        // Measure the actual largest connected body, rather than treating the rectangle as water.
        var queue=new System.Collections.Generic.Queue<int>();
        var largest=new System.Collections.Generic.List<int>();
        for(int start=0;start<wet.Length;start++)
        {
            if(!wet[start]||seen[start])continue;
            var body=new System.Collections.Generic.List<int>();queue.Enqueue(start);seen[start]=true;
            while(queue.TryDequeue(out int cell))
            {
                body.Add(cell);int x=cell%b.Width,z=cell/b.Width;Visit(x-1,z);Visit(x+1,z);Visit(x,z-1);Visit(x,z+1);
                void Visit(int nx,int nz){if((uint)nx>=b.Width||(uint)nz>=b.Height)return;int next=nz*b.Width+nx;if(wet[next]&&!seen[next]){seen[next]=true;queue.Enqueue(next);}}
            }
            if(body.Count>largest.Count)largest=body;
        }
        count=largest.Count;Require(count==lake.Area,"Actual connected lake area differs from metadata.");
        foreach(int cell in largest)
        {
            int x=b.Left+cell%b.Width,z=b.Top+cell/b.Width;
            minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
            int bed=lake.WaterY;while(Water.IsWater(world.GetBlock(x,bed,z))){Require(Water.IsSource(world.GetBlock(x,bed,z)),"Lake contains flowing water at initialization.");bed--;}
            int depth=lake.WaterY-bed;deepest=Math.Max(deepest,depth);
            Require(world.IsSolid(x,bed,z) && world.IsSolid(x,bed-1,z),"Unsupported bed.");
            foreach(Point offset in Neighbors)
            {
                int nx=x+offset.X,nz=z+offset.Y;
                if(Water.IsWater(world.GetBlock(nx,lake.WaterY,nz)))Require(Math.Abs(Ground(world,nx,nz)-bed)<=2,"Abrupt underwater bed.");
                else Require(Ground(world,nx,nz)>=lake.WaterY && Ground(world,nx,nz)<=lake.WaterY+2,"Open/vertical shore.");
            }
        }
        Require(deepest==lake.MaximumDepth,"Actual lake depth differs from metadata.");
        int width=maxX-minX+1,height=maxZ-minZ+1,extent=Math.Max(width,height);
        Require(extent>=100 && extent<=(world.Size==WorldSize.Massive?315:230) && deepest>=(world.Size==WorldSize.Massive?15:12)
            && deepest<=(world.Size==WorldSize.Massive?24:18),"Actual lake dimensions/depth missed policy.");
        Require(count/(float)(width*height)<.88f && count/(float)(width*height)>.3f,"Rectangular/thin lake.");
        for(int z=Math.Max(1,b.Top-30);z<Math.Min(world.Depth-2,b.Bottom+30);z+=3)
        for(int x=Math.Max(1,b.Left-30);x<Math.Min(world.Width-2,b.Right+30);x+=3)
        {
            if(Water.IsWater(world.GetBlock(x,lake.WaterY,z)))continue;int ground=Ground(world,x,z);
            if(!Water.IsWater(world.GetBlock(x+1,lake.WaterY,z)))Require(Math.Abs(ground-Ground(world,x+1,z))<=2,"Dry shoreline cliff X.");
            if(!Water.IsWater(world.GetBlock(x,lake.WaterY,z+1)))Require(Math.Abs(ground-Ground(world,x,z+1))<=2,"Dry shoreline cliff Z.");
        }
        if(print)Console.WriteLine($"  actual {width}x{height}, depth={deepest}, area={count}, level={lake.WaterY}, originalRelief={lake.OriginalRelief}, fill={count/(float)(width*height):F2}");
    }
}
