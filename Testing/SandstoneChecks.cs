using System;
using System.Linq;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;
using MDSM.Inventories;
using MDSM.Gameplay;
using MDSM.Persistence;
namespace MDSM.Testing;
internal static class SandstoneChecks
{
 internal static readonly (string Name,Action Check)[] All={("Sandstone registry, crafting, harvest and natural filler",CheckAll)};
 internal static int Run(){try{CheckAll();Console.WriteLine("PASS: Sandstone registry, recipe, natural filler, seams and persistence.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
 private static void C(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void CheckAll()
 {
  var id=Items.FromBlock(BlockType.Sandstone);C(id.Value==89&&Items.Obtainable.Contains(id)&&Items.StackLimit(id)==64&&!Blocks.Falls(BlockType.Sandstone),"Sandstone registry/stack/gravity incorrect.");
  var def=Blocks.Get(BlockType.Sandstone);C(def.SideTile==81&&def.TopTile==82&&def.BottomTile==83&&Blocks.IsSolid(BlockType.Sandstone),"Sandstone face maps incorrect.");
  var pick=new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),1);
  C(MiningRules.Seconds(BlockType.Sandstone,default)>0&&MiningRules.Drop(BlockType.Sandstone,default,new Random(1))==null,"Hand breaking must be possible without a sandstone drop.");
  C(MiningRules.Profile(BlockType.Sandstone).Hardness==.8f&&MiningRules.Drop(BlockType.Sandstone,pick,new Random(1))==id&&!MiningRules.Harvests(BlockType.Sandstone,default),"Sandstone harvest requirements incorrect.");
  foreach(int width in new[]{2,3})for(int oz=0;oz<=width-2;oz++)for(int ox=0;ox<=width-2;ox++)
  {
   var grid=new CraftingGrid(width);foreach(int dz in new[]{0,1})foreach(int dx in new[]{0,1})grid.SetSlot((oz+dz)*width+ox+dx,new(Items.FromBlock(BlockType.Sand),2));
   C(grid.Preview==new ItemStack(id,1)&&grid[(oz*width)+ox].Count==2,"Sandstone preview consumed ingredients.");
   var cursor=new InventoryInteraction(new Inventory());C(grid.TryCraft(cursor)&&cursor.Cursor==new ItemStack(id,1)&&grid.Preview==new ItemStack(id,1),"Sandstone crafting transaction failed.");
  }
  int[] counts=new int[4];var world=new World(173,WorldSize.Small,false);
  for(int x=1;x<65;x++)
  {
   int n=SandstoneSurface.Thickness(173,x,16);counts[n]++;
   for(int y=1;y<=65;y++)world.SetGeneratedBlock(x,y,16,y>=63?BlockType.Sand:BlockType.Stone);
   SandstoneSurface.UnderSand(world,x,65,16);
   for(int depth=0;depth<4;depth++)C(world.GetBlock(x,62-depth,16)==(depth<n?BlockType.Sandstone:BlockType.Stone),"Natural 0..3 layer thickness wrong.");
   C(world.GetBlock(x,63,16)==BlockType.Sand,"Natural filler replaced sand.");
  }
  C(counts.All(n=>n>0),"Filler never varies through all 0..3 thicknesses.");
  world.SetBlock(10,80,10,BlockType.Sandstone);var inventory=new Inventory();inventory.SetSlot(0,new(id,64));
  using(var stream=new System.IO.MemoryStream())
  {WorldSerializer.Write(stream,new(world,"Sandstone",new Microsoft.Xna.Framework.Vector3(10,82,10),0,0,inventory));stream.Position=0;var restored=WorldSerializer.Read(stream);C(restored.World.GetBlock(10,80,10)==BlockType.Sandstone&&restored.Inventory[0]==new ItemStack(id,64),"World sandstone/inventory roundtrip failed.");}
  int Height(long x,long z)=>70;
  BlockType Raw(long x,int y,long z)=>y<=70?BlockType.Stone:BlockType.Air;
  var climate=new Climate(173){VariedGeneration=true};int sandColumns=0;
  var water=new InfiniteWaterTerrain(173,Height,Height,Raw,new WaterFeaturePlans(),default);
  int cx=0,cz=0;bool found=false;for(int z=-1024;z<=1024&&!found;z+=64)for(int x=-1024;x<=1024&&!found;x+=64)if(climate.At(x,z)==Biome.Desert){cx=x;cz=z;found=true;}
  C(found,"Could not find a desert fixture.");
  for(int z=cz;z<cz+32;z++)for(int x=cx;x<cx+32;x++)
  {
   if(climate.At(x,z)!=Biome.Desert)continue;var col=water.Column(x,z);if(col.Wet||col.Floor!=70)continue;
   sandColumns++;int n=SandstoneSurface.Thickness(173,x,z);
   for(int d=0;d<=6;d++)C(water.At(x,70-d,z,BlockType.Stone)==(d<3?BlockType.Sand:d<3+n?BlockType.Sandstone:BlockType.Stone),"Desert sandstone filler incorrect.");
  }
  // Exercise real biome classification without depending on origin biome.
  C(sandColumns>100,"Too few desert columns were tested.");
  var gen=new BetaDensityGenerator(173);foreach(var a in new[]{new ChunkAddress(-1,0),new(0,0),new(784426,0)})
  {var snapshot=gen.GenerateComplete(a);C(ChunkSnapshot.Decode(a,snapshot.Encode()).Blocks.SequenceEqual(snapshot.Blocks),"Sandstone snapshot lost data.");}
  Console.WriteLine($"Sandstone thickness samples: {string.Join(',',counts)}; desert columns tested={sandColumns}.");
 }
}
