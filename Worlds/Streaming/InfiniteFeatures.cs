using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Inventories;
using MDSM.Persistence;

namespace MDSM.Worlds.Streaming;

// Coordinate-owned feature blueprints. Each request writes only its own 16x128x16
// array, evaluates the same neighboring anchors, and never edits another resident
// chunk. The bounded base-terrain halo is discarded when the worker completes.
internal sealed class InfiniteFeatures(BetaDensityGenerator terrain,int seed,ChunkAddress address,CancellationToken cancellation)
{
    private readonly Dictionary<ChunkAddress,byte[]> _base=new();
    private readonly List<Point3> _leaves=new();
    private readonly List<BlockEntityState> _chests=new();
    private readonly List<LocatedMob> _animals=new();
    private readonly Dictionary<(long X,long Z),Column> _profiles=new();
    private readonly Dictionary<(long X,long Z),int> _surfaces=new();
    private InfiniteWaterTerrain _water;
    private InfiniteWaterTerrain WaterTerrain=>_water??=new(seed,Surface,terrain.PlanSurface,Raw,terrain.WaterPlans,cancellation);
    private byte[] _blocks;
    private byte[] _boundary;
    private readonly Climate _climate=new(seed){VariedGeneration=true};
    private static readonly Point3[] Directions={new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1)};
    internal static ulong Hash(int seed,long x,long y,long z)
    {
        ulong value=unchecked((ulong)x*0x9e3779b97f4a7c15UL^(ulong)z*0xbf58476d1ce4e5b9UL^(ulong)y*0x94d049bb133111ebUL^(uint)seed);
        value=(value^(value>>30))*0xbf58476d1ce4e5b9UL;value=(value^(value>>27))*0x94d049bb133111ebUL;return value^(value>>31);
    }
    private static double Unit(ulong value)=>(value>>11)*(1.0/(1UL<<53));
    internal static double Noise(int seed,double x,double y,double z)
    {
        long ix=(long)Math.Floor(x),iy=(long)Math.Floor(y),iz=(long)Math.Floor(z);double fx=x-ix,fy=y-iy,fz=z-iz;
        fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);fz=fz*fz*(3-2*fz);
        double At(int dx,int dy,int dz)=>Unit(Hash(seed,ix+dx,iy+dy,iz+dz))*2-1;
        double P(int dy)=>LegacyPerlin.Lerp(LegacyPerlin.Lerp(At(0,dy,0),At(1,dy,0),fx),LegacyPerlin.Lerp(At(0,dy,1),At(1,dy,1),fx),fz);
        return LegacyPerlin.Lerp(P(0),P(1),fy);
    }
    private byte[] Base(ChunkAddress at)
    {
        if(!Coordinates.Valid(at.BlockX,at.BlockZ)||!Coordinates.Valid(at.BlockX+15,at.BlockZ+15))
        {if(_boundary==null){_boundary=new byte[32768];Array.Fill(_boundary,(byte)BlockType.Bedrock);}return _boundary;}
        if(!_base.TryGetValue(at,out var blocks)){blocks=terrain.TerrainForFeatures(at,cancellation);_base.Add(at,blocks);}return blocks;
    }
    private BlockType Raw(long x,int y,long z)
    {if(y<0||y>=128)return BlockType.Air;return (BlockType)Base(ChunkAddress.At(x,z))[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))];}
    private int Surface(long x,long z)
    {
        if(_surfaces.TryGetValue((x,z),out int cached))return cached;
        var data=Base(ChunkAddress.At(x,z));int lx=Coordinates.Mod(x,16),lz=Coordinates.Mod(z,16),top=-1;
        for(int y=127;y>=0;y--)if(Blocks.IsSolid((BlockType)data[Coordinates.Index(lx,y,lz)])){top=y;break;}
        _surfaces.Add((x,z),top);return top;
    }
    private readonly Dictionary<ChunkAddress,InfiniteCaves> _caves=new();
    private bool Cave(long x,int y,long z)
    {
        if(terrain.GenerationVersion==1)return y>=5&&y<=58&&Raw(x,y+5,z)!=BlockType.Air&&Math.Abs(Noise(seed^0x43415645,x/24.0,y/18.0,z/24.0))<.13&&Math.Abs(Noise(seed^0x54554e4c,x/33.0,y/21.0,z/33.0))<.16;
        if(y<1||y>119)return false;
        var at=ChunkAddress.At(x,z);
        if(!_caves.TryGetValue(at,out var caves))
        {caves=terrain.CavesForFeatures(at,(px,py,pz)=>TerrainAt(px,py,pz,Profile(px,pz),false),cancellation);_caves.Add(at,caves);}
        return caves.Mask[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))]!=0;
    }
    private readonly record struct Column(int Surface,int Bed,int Water,double Edge,int Shore=-1);
    private Column Profile(long x,long z)
    {if(!_profiles.TryGetValue((x,z),out var column)){column=ComputeProfile(x,z);_profiles.Add((x,z),column);}return column;}
    private Column ComputeProfile(long x,long z)
    {
        int top=Surface(x,z);if(terrain.GenerationVersion>=3||top<57||top>85)return new(top,top,-1,0);
        Column? lake=LakeColumn(x,z,top);if(lake.HasValue)return lake.Value;
        // A 128-block anchor lattice owns ponds. Only the nearest anchors can
        // affect a column, so broad water features cost no world-sized masks.
        double influence=0;long ax=Coordinates.FloorDiv(x,128),az=Coordinates.FloorDiv(z,128);
        for(long dz=az-1;dz<=az+1;dz++)for(long dx=ax-1;dx<=ax+1;dx++)
        {
            ulong h=Hash(seed^0x504f4e44,dx,0,dz);if(h%100>=65)continue;
            long cx=dx*128+32+(long)(h>>8&63),cz=dz*128+32+(long)(h>>16&63);
            if(_climate.At(cx,cz)==Biome.Desert&&h%7!=0)continue;
            double rx=8+(h>>24&7),rz=7+(h>>32&7),xx=(x-cx)/rx,zz=(z-cz)/rz;
            double q=1-xx*xx-zz*zz+.17*Noise(seed^0x42414e4b,x/8.0,0,z/8.0);if(q>influence)influence=q;
        }
        // Valley rivers follow a smooth, coordinate-continuous field. They use
        // one level and close naturally where the land is unsuitable.
        double river=Math.Abs(Noise(seed^0x52495652,x/180.0,0,z/180.0));
        if(top<=71&&_climate.At(x,z)!=Biome.Desert)influence=Math.Max(influence,(.025-river)*15);
        if(influence<=0)return new(top,top,-1,0);
        int level=63,depth=Math.Clamp((int)Math.Ceiling(influence*4),1,5);
        if(top>level+8||top<level-5)return new(top,top,-1,0);
        return new(top,Math.Min(top,level-depth),level,influence);
    }
    private readonly Dictionary<(long X,long Z),(long X,long Z,int Level,double Rx,double Rz,bool Valid)> _lakes=new();
    private Column? LakeColumn(long x,long z,int top)
    {
        long ax=Coordinates.FloorDiv(x,512),az=Coordinates.FloorDiv(z,512);
        for(long sz=az-1;sz<=az+1;sz++)for(long sx=ax-1;sx<=ax+1;sx++)
        {
            ulong h=Hash(seed^0x4c414b45,sx,0,sz);if(h%100>=55)continue;
            long cx=sx*512+128+(long)(h>>8&255),cz=sz*512+128+(long)(h>>16&255);
            double rx=42+(h>>24&15),rz=35+(h>>32&31);if(Math.Abs(x-cx)>rx*1.4||Math.Abs(z-cz)>rz*1.4)continue;
            if(!_lakes.TryGetValue((sx,sz),out var plan))
            {
                int level=Surface(cx,cz)-2,min=128,max=0;
                for(int i=0;i<8;i++){double angle=i*Math.PI/4;int y=Surface(cx+(long)(Math.Cos(angle)*rx),cz+(long)(Math.Sin(angle)*rz));min=Math.Min(min,y);max=Math.Max(max,y);}
                bool valid=level is >=63 and <=82 && max-min<=18 && _climate.At(cx,cz)!=Biome.Desert && (Math.Abs(cx)>100||Math.Abs(cz)>100);
                plan=(cx,cz,level,rx,rz,valid);_lakes.Add((sx,sz),plan);
            }
            if(!plan.Valid)continue;
            double dx=(x-plan.X)/plan.Rx,dz=(z-plan.Z)/plan.Rz;
            double q=1-dx*dx-dz*dz+.22*Noise(seed^0x53484f52,x/25.0,0,z/25.0)+.12*Noise(seed^0x42415953,x/47.0,0,z/47.0);
            if(q<-.35)continue;
            if(q<=.08)
            {double t=Math.Clamp((q+.35)/.43,0,1);t=t*t*(3-2*t);int shore=(int)Math.Round(top*(1-t)+(plan.Level+1)*t);return new(top,Math.Min(top,shore),-1,q,Math.Min(top,shore));}
            int depth=1+(int)Math.Round(Math.Min(1,q)*9);return new(top,Math.Min(top,plan.Level-depth),plan.Level,q);
        }
        return null;
    }
    private BlockType TerrainAt(long x,int y,long z,Column column,bool carve=true)
    {
        if(y<0||y>=128)return BlockType.Air;
        BlockType original=Raw(x,y,z);if(y==0)return BlockType.Bedrock;
        if(terrain.GenerationVersion>=3)
        {
            BlockType shaped=WaterTerrain.At(x,y,z,original,column.Surface);
            if(carve && shaped is (BlockType.Stone or BlockType.Dirt or BlockType.Grass) && Cave(x,y,z))return y<10?BlockType.LavaSource:BlockType.Air;
            return shaped;
        }
        if(column.Shore>=0)
        {
            if(y>column.Shore&&y<=column.Surface)return BlockType.Air;
            if(y==column.Shore&&Blocks.IsSolid(original))return Hash(seed,x,0,z)%3==0?BlockType.Sand:BlockType.Gravel;
        }
        if(column.Water>=0)
        {
            if(y>column.Bed&&y<=column.Surface)return y<=column.Water?BlockType.WaterSource:BlockType.Air;
            if(y>column.Surface&&y<=column.Water)return BlockType.WaterSource;
            if(y==column.Bed&&Blocks.IsSolid(original))return (Hash(seed,x,0,z)%5) switch{0=>BlockType.Sand,1=>BlockType.Dirt,_=>BlockType.Gravel};
        }
        if(carve && original is BlockType.Stone or BlockType.Dirt or BlockType.Grass&&Cave(x,y,z))return y<10?BlockType.LavaSource:BlockType.Air;
        if(y<=column.Surface&&y>column.Surface-3&&(original is BlockType.Grass or BlockType.Dirt)&&_climate.At(x,z)==Biome.Desert)return BlockType.Sand;
        return original;
    }
    private BlockType TerrainAt(long x,int y,long z)=>TerrainAt(x,y,z,Profile(x,z));
    internal WaterColumn WaterProfile(long x,long z)=>WaterTerrain.Column(x,z);
    private BlockType At(long x,int y,long z)
    {if(y<0||y>=128)return BlockType.Air;if(ChunkAddress.At(x,z)==address)return (BlockType)_blocks[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))];return TerrainAt(x,y,z);}
    private void Write(long x,int y,long z,BlockType block)
    {if(y>0&&y<128&&ChunkAddress.At(x,z)==address)_blocks[Coordinates.Index(Coordinates.Mod(x,16),y,Coordinates.Mod(z,16))]=(byte)block;}
    internal ChunkSnapshot Generate()
    {
        GenerateTerrain();
        DepositsAndOres();Dungeons();TreesAndPlants();Animals();
        // Exact support safeguard after every feature pass. Fill only cavities
        // directly below generated gravity blocks; never create initial falls.
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)for(int y=1;y<128;y++)
        {
            int index=Coordinates.Index(x,y,z);
            if(Blocks.Falls((BlockType)_blocks[index])&&!Blocks.IsSolid((BlockType)_blocks[Coordinates.Index(x,y-1,z)]))_blocks[index]=(byte)BlockType.Dirt;
            if((BlockType)_blocks[index]==BlockType.Grass&&y<127&&Blocks.IsOpaque((BlockType)_blocks[Coordinates.Index(x,y+1,z)]))_blocks[index]=(byte)BlockType.Dirt;
        }
        var metadata=new ChunkGameplay{Vegetation=new(_leaves.ToArray(),Array.Empty<CellTimer>(),Array.Empty<CellTimer>()),Containers=_chests.ToArray(),Passive=_animals.ToArray()};
        return new(address,terrain.GenerationVersion,_blocks,Metadata:StreamingJson.Encode(metadata));
    }
    // Same production pipeline, exposed before decoration for deterministic
    // diagnostics that distinguish density voids from actual cave carving.
    internal byte[] GenerateTerrain(bool carve=true)
    {
        _blocks=(byte[])Base(address).Clone();
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            cancellation.ThrowIfCancellationRequested();long wx=address.BlockX+x,wz=address.BlockZ+z;Column column=Profile(wx,wz);
            for(int y=1;y<128;y++)_blocks[Coordinates.Index(x,y,z)]=(byte)TerrainAt(wx,y,wz,column,carve);
        }
        return _blocks;
    }
    private void DepositsAndOres()
    {
        // Every vein is owned by one absolute chunk and clips into each neighbor.
        // The same nine owners are evaluated in the same order for either side.
        for(long cz=address.Z-1;cz<=address.Z+1;cz++)for(long cx=address.X-1;cx<=address.X+1;cx++)
        {
            var random=new Random(unchecked((int)Hash(seed^0x53414e44,cx,0,cz)));
            if(random.Next(100)<35)
            {
                Point3 root=new(cx*16+random.Next(16),random.Next(8,54),cz*16+random.Next(16));var cells=new List<Point3>(30);int target=random.Next(10,31);
                for(int attempt=0;attempt<180&&cells.Count<target;attempt++)
                {
                    Point3 p=cells.Count==0?root:cells[random.Next(cells.Count)]+Directions[random.Next(6)];if(p.Y<5||p.Y>56||Math.Abs(p.X-root.X)>5||Math.Abs(p.Z-root.Z)>5||cells.Contains(p)||TerrainAt(p.X,p.Y,p.Z)!=BlockType.Stone)continue;
                    bool covered=true;for(int y=-1;y<=4;y++)if(!Blocks.IsSolid(TerrainAt(p.X,p.Y+y,p.Z))){covered=false;break;}if(covered)cells.Add(p);
                }
                BlockType material=random.Next(2)==0?BlockType.Sand:BlockType.Gravel;if(cells.Count>=10)foreach(var p in cells)Write(p.X,p.Y,p.Z,material);
            }
        }
        for(long cz=address.Z-1;cz<=address.Z+1;cz++)for(long cx=address.X-1;cx<=address.X+1;cx++)
        {
            var random=new Random(unchecked((int)Hash(seed^0x4f524553,cx,0,cz)));
            for(int material=0;material<3;material++)for(int vein=0;vein<(material==0?18:material==1?10:1);vein++)
            {
                int max=material==2?20:60,target=random.Next(2,9);Point3 root=new(cx*16+random.Next(16),random.Next(4,max+1),cz*16+random.Next(16)),p=root;var cells=new List<Point3>(8);
                for(int step=0;step<32&&cells.Count<target;step++)
                {if(p.Y>=4&&p.Y<=max&&Math.Abs(p.X-root.X)<=7&&Math.Abs(p.Z-root.Z)<=7&&TerrainAt(p.X,p.Y,p.Z)==BlockType.Stone&&!cells.Contains(p))cells.Add(p);p=(cells.Count>0?cells[random.Next(cells.Count)]:root)+Directions[random.Next(6)];}
                if(cells.Count<2)continue;foreach(var point in cells)Write(point.X,point.Y,point.Z,material==0?BlockType.CoalOre:material==1?BlockType.IronOre:BlockType.DiamondOre);
            }
        }
    }
    private void Dungeons()
    {
        Span<byte> equipmentId=stackalloc byte[16];
        long ax=Coordinates.FloorDiv(address.BlockX,32),az=Coordinates.FloorDiv(address.BlockZ,32);
        for(long sz=az-1;sz<=az+1;sz++)for(long sx=ax-1;sx<=ax+1;sx++)
        {
            var random=new Random(unchecked((int)Hash(seed^0x44554e47,sx,0,sz)));if(random.NextDouble()>.5)continue;
            long x=sx*32+8+random.Next(16),z=sz*32+8+random.Next(16);if(Math.Abs(x-address.BlockX)>20||Math.Abs(z-address.BlockZ)>20||Math.Abs(x)<20&&Math.Abs(z)<20)continue;
            for(int attempt=0;attempt<20;attempt++)
            {
                int y=random.Next(12,49),air=0;bool valid=true;Point3 opening=default;
                for(int dz=-3;dz<=3&&valid;dz++)for(int dx=-3;dx<=3&&valid;dx++)for(int dy=-1;dy<=5;dy++)
                {var b=TerrainAt(x+dx,y+dy,z+dz);if(Lava.Fluid(b)||b==BlockType.Bedrock){valid=false;break;}if(Math.Abs(dx)<=2&&Math.Abs(dz)<=2&&dy>=0&&dy<5&&b==BlockType.Air)air++;}
                if(!valid||air<30||!Blocks.IsSolid(TerrainAt(x,y-2,z))||!Blocks.IsSolid(TerrainAt(x,y+6,z)))continue;
                bool accessible=false;for(int facing=0;facing<4;facing++){var d=World.FacingDirection(facing);if(TerrainAt(x+d.X*4,y,z+d.Z*4)==BlockType.Air&&TerrainAt(x+d.X*4,y+1,z+d.Z*4)==BlockType.Air){opening=new(d.X*3,0,d.Z*3);accessible=true;break;}}if(!accessible)continue;
                for(int dy=-1;dy<=5;dy++)for(int dz=-3;dz<=3;dz++)for(int dx=-3;dx<=3;dx++)
                {bool wall=dy==-1||dy==5||Math.Abs(dx)==3||Math.Abs(dz)==3,door=dx==opening.X&&dz==opening.Z&&dy is 0 or 1;var b=wall&&!door?(random.NextDouble()<.4?BlockType.MossyCobblestone:BlockType.Cobblestone):BlockType.Air;Write(x+dx,y+dy,z+dz,b);}
                int chests=random.Next(1,4),offset=random.Next(4);Point3[] corners={new(-2,0,-2),new(2,0,2),new(2,0,-2),new(-2,0,2)};
                for(int i=0;i<chests;i++)
                {
                    Point3 p=new(x+corners[(offset+i)%4].X,y,z+corners[(offset+i)%4].Z);var slots=new ItemStack[27];int rolls=random.Next(4,9);
                    for(int roll=0;roll<rolls;roll++)
                    {
                        ItemId item=random.Next(10) switch{0=>Items.Armor(ArmorMaterial.Iron,(ArmorSlot)random.Next(4)),1=>Items.IronIngot,2=>Items.String,3=>Items.Bread,4=>Items.Potato,5=>Items.Carrot,6=>Items.Arrow,7=>Items.Coal,8=>Items.Apple,_=>Items.Stick};
                        Guid instance=default;if(Items.Get(item).Durability>0){System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(equipmentId,Hash(seed,p.X,roll,p.Z));System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(equipmentId[8..],Hash(seed^0x47554944,p.X,p.Y+roll,p.Z));instance=new Guid(equipmentId);}
                        slots[roll]=new(item,Items.StackLimit(item)==1?1:random.Next(1,6),instanceId:instance);
                    }
                    Write(p.X,p.Y,p.Z,BlockType.Chest);if(ChunkGameplay.Owns(address,p))_chests.Add(new(p,BlockType.Chest,slots));
                }
                break;
            }
        }
    }
    private void TreesAndPlants()
    {
        long ax=Coordinates.FloorDiv(address.BlockX-2,8),az=Coordinates.FloorDiv(address.BlockZ-2,8);
        for(long sz=az;sz<=Coordinates.FloorDiv(address.BlockZ+17,8);sz++)for(long sx=ax;sx<=Coordinates.FloorDiv(address.BlockX+17,8);sx++)
        {
            ulong h=Hash(seed^0x54524545,sx,0,sz);long x=sx*8+2+(long)(h>>8&3),z=sz*8+2+(long)(h>>16&3);var biome=_climate.At(x,z);
            if(biome==Biome.Desert||h%100>=(biome==Biome.SnowyForest?65UL:35UL)||Math.Abs(x)<7&&Math.Abs(z)<7)continue;
            int ground=Surface(x,z),height=4+(int)((h>>24)%3),top=ground+height;if(top+1>=128||TerrainAt(x,ground,z)!=BlockType.Grass)continue;
            bool clear=true;for(int dy=1;dy<=height+1&&clear;dy++)for(int dz=-2;dz<=2&&clear;dz++)for(int dx=-2;dx<=2;dx++)if(TerrainAt(x+dx,ground+dy,z+dz)!=BlockType.Air){clear=false;break;}if(!clear)continue;
            for(int y=ground+1;y<=top;y++)Write(x,y,z,BlockType.WoodLog);
            for(int layer=-2;layer<=1;layer++)for(int dz=-2;dz<=2;dz++)for(int dx=-2;dx<=2;dx++)
            {int width=layer<0?2:1;if(Math.Abs(dx)>width||Math.Abs(dz)>width||Math.Abs(dx)+Math.Abs(dz)>(layer==1&&(h&16)==0?width:width+1)||dx==0&&dz==0&&layer<=0)continue;Point3 p=new(x+dx,top+layer,z+dz);Write(p.X,p.Y,p.Z,BlockType.Leaves);if(ChunkGameplay.Owns(address,p))_leaves.Add(p);}
        }
        for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            long wx=address.BlockX+x,wz=address.BlockZ+z;int ground=-1;for(int y=126;y>0;y--)if(Blocks.IsSolid(At(wx,y,wz))){ground=y;break;}
            if(ground<1||At(wx,ground+1,wz)!=BlockType.Air)continue;ulong h=Hash(seed^0x504c414e,wx,0,wz);var biome=_climate.At(wx,wz);
            if(biome==Biome.Desert&&At(wx,ground,wz)==BlockType.Sand)
            {
                if(h%80==0&&ground+4<128){int height=2+(int)((h>>8)%3);bool clear=true;for(int y=1;y<=height&&clear;y++)foreach(var d in new[]{new Point3(1,0,0),new(-1,0,0),new(0,0,1),new(0,0,-1)})if(TerrainAt(wx+d.X,ground+y,wz+d.Z)!=BlockType.Air)clear=false;if(clear)for(int y=1;y<=height;y++)Write(wx,ground+y,wz,BlockType.Cactus);}
                else if(h%25==0)Write(wx,ground+1,wz,BlockType.DeadBush);continue;
            }
            if(At(wx,ground,wz)!=BlockType.Grass)continue;
            if(biome==Biome.SnowyForest)Write(wx,ground+1,wz,BlockType.Snow);
            else if(h%100<36)
            {
                bool doublePlant=h%13==0&&ground+2<128&&At(wx,ground+2,wz)==BlockType.Air;
                Write(wx,ground+1,wz,doublePlant?BlockType.DoubleTallGrassLower:h%23==0?BlockType.RedFlower:h%29==0?BlockType.YellowFlower:BlockType.TallGrass);
                if(doublePlant)Write(wx,ground+2,wz,BlockType.DoubleTallGrassUpper);
            }
        }
    }
    private void Animals()
    {
        ulong h=Hash(seed^0x53504157,address.X,0,address.Z);if(h%8!=0)return;var random=new Random(unchecked((int)h));
        MobSpecies species=(MobSpecies)(h>>12&3);var def=MobDefinitions.Get(species);
        for(int attempt=0;attempt<16;attempt++)
        {
            long x=address.BlockX+random.Next(2,14),z=address.BlockZ+random.Next(2,14);int y=Surface(x,z);if(Math.Abs(x)<12&&Math.Abs(z)<12||y+3>=128||At(x,y,z)!=BlockType.Grass)continue;
            bool clear=true;for(int dy=1;dy<=2;dy++)if(Blocks.IsSolid(At(x,y+dy,z))||Lava.Fluid(At(x,y+dy,z)))clear=false;if(!clear)continue;
            _animals.Add(new(new(species,Vector3.Zero,random.NextSingle()*MathHelper.TwoPi-MathHelper.Pi,def.MaximumHealth),new(x+.5,y+1,z+.5)));break;
        }
    }
}
