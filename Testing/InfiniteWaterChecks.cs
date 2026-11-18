using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
using MDSM.Persistence;
namespace MDSM.Testing;
internal static class InfiniteWaterChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Isolated shoreline distance has no axis-aligned underwater pillars",ShoreArtifacts),
        ("Exposed sea-level soil is grass rather than inherited dirt",SeaLevelGrass),
        ("Infinite water basins, coherent sediments, bounded rivers and material seams",Profiles),
        ("Infinite water production determinism, density preservation, versioning and planning cost",Production),
        ("Natural seeded basin shores and containment beyond flat fixtures",NaturalBasins)};
    private static void C(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    internal static int Run(){try{foreach(var t in All){t.Check();Console.WriteLine("PASS: "+t.Name);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static InfiniteWaterTerrain Fixture(int seed,Func<long,long,int> height)
    {
        BlockType Raw(long x,int y,long z)=>y==0?BlockType.Bedrock:y<=height(x,z)?(y==height(x,z)?BlockType.Grass:BlockType.Stone):y<=InfiniteWaterTerrain.Sea?BlockType.WaterSource:BlockType.Air;
        return new(seed,height,height,Raw,new WaterFeaturePlans(),default);
    }
    private static void ShoreArtifacts()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            const int cx=1024,cz=1024;
            var water=Fixture(seed,(x,z)=>x==cx&&z==cz?62:58);
            int largestDifference=0;
            foreach(int r in new[]{8,12,16,24,32})
            {
                int d=(int)Math.Round(r/Math.Sqrt(2));
                int axis=water.Column(cx+r,cz).Floor,diagonal=water.Column(cx+d,cz+d).Floor;
                largestDifference=Math.Max(largestDifference,Math.Abs(axis-diagonal));
                C(Math.Abs(axis-diagonal)<=1,$"Cross-shaped seabed ridge: seed={seed}, radius={r}, axial floor={axis}, diagonal floor={diagonal}.");
            }
            Console.WriteLine($"Isolated island seed {seed}: maximum axial/diagonal floor difference={largestDifference} blocks.");
        }
        var shores=new[]{(X:-17L,Z:15L),(X:19L,Z:-3L),(X:34L,Z:33L)};
        BlockType Land(long x,int y,long z)=>shores.Any(p=>p.X==x&&p.Z==z)?BlockType.Stone:BlockType.WaterSource;
        var plans=new WaterFeaturePlans();
        for(int z=65;z>=-65;z-=3)for(int x=65;x>=-65;x-=3)
        {
            double expected=Math.Min(36,Math.Sqrt(shores.Min(p=>(x-p.X)*(double)(x-p.X)+(z-p.Z)*(double)(z-p.Z))));
            C(Math.Abs(plans.CoastDistance(x,z,Land)-expected)<.00001,"Coast distance is incorrect at a signed chunk boundary or after cache reuse.");
        }
        var floating=new WaterFeaturePlans();
        C(floating.CoastDistance(0,0,(x,y,z)=>y>62?BlockType.Stone:BlockType.WaterSource)==36,"Floating formations were mistaken for sea-level shores.");
    }
    private static void SeaLevelGrass()
    {
        int checkedSoil=0;var climate=new Climate(173){VariedGeneration=true};
        int Height(long x,long z)=>62;
        BlockType Raw(long x,int y,long z)=>y<62?BlockType.Stone:y==62?BlockType.Dirt:BlockType.Air;
        var water=new InfiniteWaterTerrain(173,Height,Height,Raw,new WaterFeaturePlans(),default);
        for(int z=0;z<64;z++)for(int x=0;x<64;x++)
        {
            var c=water.Column(x,z);if(c.Wet||c.Kind!=NaturalWater.None||climate.At(x,z)==Biome.Desert)continue;
            if(water.Column(x-3,z).Wet||water.Column(x+3,z).Wet||water.Column(x,z-3).Wet||water.Column(x,z+3).Wet)continue;
            checkedSoil++;C(water.At(x,62,z,BlockType.Dirt)==BlockType.Grass,"Dry, exposed Y=62 soil retained its inherited Dirt surface.");
        }
        C(checkedSoil>100,"Sea-level soil regression fixture covered too few dry columns.");
    }
    private static void Profiles()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            var ocean=Fixture(seed,(_,_)=>56);var center=ocean.Column(1000,1000);
            C(center.Wet&&center.Level==62&&center.Level-center.Floor>=12,"Open sea is a shallow flooded plain.");
            var flooded=Fixture(seed,(_,_)=>61).Column(1000,1000);
            C(flooded.Level-flooded.Floor>=8,"Broad one-block flooding was retained instead of a basin.");
            var coast=Fixture(seed,(x,_)=>x<0?Math.Max(52,61+(int)Math.Floor(x/6.0)):Math.Min(70,62+(int)(x/6)));
            int maxJump=0,maxAt=0,last=coast.Column(-60,500).Floor,sand=0,gravel=0,dirt=0,patchChanges=0;BlockType previous=default;
            for(int x=-59;x<=40;x++)
            {
                var c=coast.Column(x,500);if(Math.Abs(last-c.Floor)>maxJump){maxJump=Math.Abs(last-c.Floor);maxAt=x;}last=c.Floor;
                var material=coast.At(x,c.Floor,500,BlockType.Stone);
                if(material==BlockType.Sand)sand++;if(material==BlockType.Gravel)gravel++;if(material==BlockType.Dirt)dirt++;
                if(x< -30&&previous!=default&&material!=previous)patchChanges++;previous=material;
                C(c.Floor>=40,"Water excavated into deep underground caves.");
                if(!c.Wet&&c.Floor>65)C(material is BlockType.Grass or BlockType.Sand,$"Exposed low-gradient land was left bare: seed {seed}, x={x}, {material}.");
                C(coast.At(x,20,500,BlockType.Stone)==BlockType.Stone,"Water opened deep rock.");
            }
            C(maxJump<=3&&sand>=3&&patchChanges<=3,$"Shore/beds are abrupt or random patchwork: seed {seed}, step={maxJump} at x={maxAt}, {coast.Column(maxAt-1,500)} to {coast.Column(maxAt,500)}, sand={sand}, changes={patchChanges}.");
            var flat=Fixture(seed,(_,_)=>64);int lake=0,pond=0,river=0,maxDepth=0,maxRiverCut=0,unsupported=0;
            long riverX=0,riverZ=0;
            // Actual column geometry, not just configured radii. Continuous
            // sampling is repeated in reverse order with a fresh planner.
            for(int z=0;z<512;z+=2)for(int x=0;x<512;x+=2)
            {
                var c=flat.Column(x,z);if(!c.Wet)continue;
                int depth=c.Level-c.Floor;maxDepth=Math.Max(maxDepth,depth);
                if(c.Kind==NaturalWater.Lake)lake++;if(c.Kind==NaturalWater.Pond)pond++;
                if(c.Kind==NaturalWater.River){river++;maxRiverCut=Math.Max(maxRiverCut,c.Original-c.Floor);riverX=x;riverZ=z;}
                if(flat.At(x,c.Floor-1,z,BlockType.Stone)==BlockType.Air)unsupported++;
                foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                {
                    var n=flat.Column(x+d.Item1,z+d.Item2);
                    C(n.Floor>=c.Level||n.Level>=c.Level,$"Uncontained feature water: seed {seed}, {x},{z}, {c.Kind} level={c.Level}, neighbor={n}.");
                }
            }
            C(lake>0&&pond>0&&river>0&&maxDepth>=7&&maxRiverCut<=5&&unsupported==0,"Distinct basins/ponds/rivers, depths or support failed.");
            var reverse=Fixture(seed,(_,_)=>64);
            for(int z=31;z>=-16;z--)for(int x=31;x>=-16;x--)
            {
                var a=flat.Column(x+riverX,z+riverZ);var b=reverse.Column(x+riverX,z+riverZ);
                C(a==b,"Water profiles depend on chunk/request order.");
                for(int y=a.Floor;y<=Math.Max(a.Level,a.Floor)+1;y++)C(flat.At(x+riverX,y,z+riverZ,BlockType.Stone)==reverse.At(x+riverX,y,z+riverZ,BlockType.Stone),"Material seams cross chunk boundaries.");
            }
            var hills=Fixture(seed,(x,_)=>x is >=50 and <=110?90:64);
            for(int x=60;x<=100;x+=4){var c=hills.Column(x,80);C(!c.Wet&&c.Floor==90,"River/lake cut a trench through a hill.");}
            Console.WriteLine($"Water fixture seed {seed}: sampled wet columns lake={lake}, pond={pond}, river={river}; max depth={maxDepth}, river cut={maxRiverCut}; coast max step={maxJump}, sediment transitions={patchChanges}.");
        }
        // Explicit rocky coast, deterministic desert sand and lake containment.
        var rock=Fixture(173,(x,_)=>x<0?57:80);
        C(rock.At(-1,rock.Column(-1,500).Floor,500,BlockType.Stone)==BlockType.Stone,"Steep coast was blanketed with sand.");
    }
    private static void Production()
    {
        foreach(int seed in new[]{173,-907,137})
        {
            var g=new BetaDensityGenerator(seed);var prior=new BetaDensityGenerator(seed,2);var clock=Stopwatch.StartNew();int water=0,sand=0;
            foreach(var a in new[]{new ChunkAddress(0,0),new(8,-9),new(-24,18),new(50,35)})
            {
                byte[] basis=g.Generate(a),oldBasis=prior.Generate(a);
                for(int i=0;i<basis.Length;i++)if(basis[i]!=oldBasis[i])
                    C(i/256==InfiniteWaterTerrain.Sea && basis[i]==(byte)BlockType.Grass && oldBasis[i]==(byte)BlockType.Dirt
                        && basis[i+256]==(byte)BlockType.Air,"Water update altered density terrain beyond the exposed sea-level grass correction.");
                var features=new InfiniteFeatures(g,seed,a,default);
                var first=features.Generate();var second=new BetaDensityGenerator(seed).GenerateComplete(a);
                C(first.Blocks.SequenceEqual(second.Blocks)&&first.Metadata.SequenceEqual(second.Metadata),"Water features changed with request order/cache state.");
                for(int z=0;z<16;z++)for(int x=0;x<16;x++)
                {
                    long wx=a.BlockX+x,wz=a.BlockZ+z;var c=features.WaterProfile(wx,wz);
                    if(!c.Wet)continue;
                    C(c.Original-c.Floor<=16,"Water exceeded its bounded surface excavation cap.");
                    foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    {
                        var n=features.WaterProfile(wx+d.Item1,wz+d.Item2);
                        C(n.Floor>=c.Level||n.Level>=c.Level,$"Natural water is uncontained: seed={seed}, {wx},{wz}, {c}, neighbor={n}.");
                    }
                }
                for(int z=0;z<16;z+=3)for(int x=0;x<16;x+=3)
                {
                    int top=127;while(top>0&&!Blocks.IsSolid((BlockType)basis[Coordinates.Index(x,top,z)]))top--;
                    C(g.PlanSurface(a.BlockX+x,a.BlockZ+z)==top,"Fast planning sampler disagrees with actual interpolated terrain.");
                }
                for(int z=0;z<16;z++)for(int x=0;x<16;x++)for(int y=1;y<128;y++)
                {
                    BlockType block=(BlockType)first.Blocks[Coordinates.Index(x,y,z)];if(Water.IsWater(block))water++;if(block==BlockType.Sand)sand++;
                    if(Blocks.Falls(block))C(Blocks.IsSolid((BlockType)first.Blocks[Coordinates.Index(x,y-1,z)]),"Generated shoreline sediment lacks support.");
                }
                var restored=ChunkSnapshot.Decode(a,first.Encode());C(restored.GeneratorVersion==BetaDensityGenerator.Version&&restored.Blocks.SequenceEqual(first.Blocks),"New water snapshot changed on persistence.");
                var legacy=prior.GenerateComplete(a);C(ChunkSnapshot.Decode(a,legacy.Encode()).GeneratorVersion==2,"Version-2 snapshots no longer load.");
            }
            C(water>0&&sand>0&&clock.Elapsed.TotalSeconds<45,"Natural water/sediments absent or generation work unbounded.");
            Console.WriteLine($"Natural seed {seed}: {water} water cells, {sand} sand cells in 4 chunks; 8 full builds + density/version checks in {clock.Elapsed.TotalSeconds:F2}s.");
        }
    }
    private static void NaturalBasins()
    {
        int totalLake=0;
        foreach(int seed in new[]{173,-907,137})
        {
            var g=new BetaDensityGenerator(seed);var heights=new Dictionary<(long,long),int>();
            int Height(long x,long z){if(!heights.TryGetValue((x,z),out int y)){y=g.PlanSurface(x,z);heights.Add((x,z),y);}return y;}
            BlockType Raw(long x,int y,long z)=>y<0||y>=128?BlockType.Air:(BlockType)g.TerrainForFeatures(ChunkAddress.At(x,z),default)[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))];
            int lakes=0,ponds=0,seas=0,rivers=0,maxDepth=0;
            for(int az=-1;az<=1;az++)for(int ax=-1;ax<=1;ax++)
            {
                var h=InfiniteFeatures.Hash(seed^0x4c414b45,ax,0,az);if(h%100>=55)continue;
                long cx=ax*512+128+(long)((h>>8)%256),cz=az*512+128+(long)((h>>20)%256);
                var water=new InfiniteWaterTerrain(seed,Height,Height,Raw,g.WaterPlans,default);
                for(int dz=-64;dz<=64;dz+=4)for(int dx=-64;dx<=64;dx+=4)
                {
                    long x=cx+dx,z=cz+dz;var c=water.Column(x,z);if(!c.Wet)continue;
                    if(c.Kind==NaturalWater.Lake)lakes++;if(c.Kind==NaturalWater.Pond)ponds++;
                    if(c.Kind==NaturalWater.River)rivers++;if(c.Kind==NaturalWater.Sea)seas++;
                    maxDepth=Math.Max(maxDepth,c.Level-c.Floor);
                    C(c.Original-c.Floor<=16,"Natural basin excavation exceeded cap.");
                    foreach(var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    {var n=water.Column(x+d.Item1,z+d.Item2);C(n.Floor>=c.Level||n.Level>=c.Level,$"Natural basin leaked: seed {seed} at {x},{z}, {c}; neighbor {n}.");}
                }
            }
            totalLake+=lakes;Console.WriteLine($"Natural basin probes seed {seed}: lake={lakes}, pond={ponds}, sea={seas}, river={rivers}, max depth={maxDepth} (4-block sample grid, not feature counts).");
        }
        C(totalLake>0,"Natural terrain suitability rejected every sampled lake.");
    }
}
