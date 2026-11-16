using System;
using System.Threading;
namespace MDSM.Worlds.Streaming;

// Beta-style seeded tunnel walks. Neighboring chunks replay the same owners;
// clipping changes writes only, never the random walk or branching decisions.
internal sealed class InfiniteCaves
{
    // Java's compact 48-bit LCG avoids System.Random's per-seed table allocation
    // when replaying hundreds of neighboring owners. It is local to each walk.
    private struct CaveRandom
    {
        private ulong _state;
        internal CaveRandom(int seed)=>_state=((ulong)(long)seed^0x5DEECE66DUL)&((1UL<<48)-1);
        private int Bits(int bits){_state=(_state*0x5DEECE66DUL+11)&((1UL<<48)-1);return (int)(_state>>(48-bits));}
        internal int Next()=>Bits(31);
        internal int Next(int bound)
        {
            if(bound<=0)throw new ArgumentOutOfRangeException(nameof(bound));
            if((bound&-bound)==bound)return (int)((bound*(long)Bits(31))>>31);
            int bits,value;do{bits=Bits(31);value=bits%bound;}while(unchecked(bits-value+(bound-1))<0);return value;
        }
        internal double NextDouble()=>(((long)Bits(26)<<27)+Bits(27))*(1.0/(1L<<53));
    }
    internal readonly byte[] Mask=new byte[32768];
    internal int CarvedCells {get;private set;}
    internal InfiniteCaves(int seed,ChunkAddress chunk,Func<long,int,long,BlockType> terrain,CancellationToken token)
    {
        for(long cz=chunk.Z-8;cz<=chunk.Z+8;cz++)for(long cx=chunk.X-8;cx<=chunk.X+8;cx++)
        {
            token.ThrowIfCancellationRequested();
            var r=new CaveRandom(unchecked((int)InfiniteFeatures.Hash(seed^0x43415645,cx,0,cz)));
            int count=r.Next(r.Next(r.Next(40)+1)+1);if(r.Next(15)!=0)continue;
            for(int i=0;i<count;i++)
            {
                long x=cx*16+r.Next(16),z=cz*16+r.Next(16);double y=r.Next(r.Next(120)+8);int tunnels=1;
                if(r.Next(4)==0){Sphere(x,y,z,1+r.NextDouble()*5,.5);tunnels+=r.Next(4);}
                for(int j=0;j<tunnels;j++)Walk(x,y,z,r.NextDouble()*Math.PI*2,(r.NextDouble()-.5)*.25,r.NextDouble()*2+r.NextDouble(),0,85+r.Next(28),r.Next(),true);
            }
        }
        void Walk(double x,double y,double z,double yaw,double pitch,double size,int start,int length,int seedValue,bool branch)
        {
            var r=new CaveRandom(seedValue);double dyaw=0,dpitch=0;int split=r.Next(length/2)+length/4;bool steep=r.Next(6)==0;
            for(int step=start;step<length;step++)
            {
                double radius=1.5+Math.Sin(step*Math.PI/length)*size;
                x+=Math.Cos(yaw)*Math.Cos(pitch);y+=Math.Sin(pitch);z+=Math.Sin(yaw)*Math.Cos(pitch);
                pitch*=steep?.92:.7;pitch+=dpitch*.1;yaw+=dyaw*.1;dpitch*=.9;dyaw*=.75;
                dpitch+=(r.NextDouble()-r.NextDouble())*r.NextDouble()*2;dyaw+=(r.NextDouble()-r.NextDouble())*r.NextDouble()*4;
                if(branch&&step==split&&size>1)
                {
                    Walk(x,y,z,yaw-Math.PI/2,pitch/3,.5+r.NextDouble()*.5,step,length,r.Next(),false);
                    Walk(x,y,z,yaw+Math.PI/2,pitch/3,.5+r.NextDouble()*.5,step,length,r.Next(),false);return;
                }
                if(r.Next(4)!=0)Sphere(x,y,z,radius,1);
            }
        }
        void Sphere(double x,double y,double z,double radius,double verticalScale)
        {
            // Compare relative doubles; absolute positions remain precise within
            // the engine's supported coordinate range. Never scan remote cells.
            if(x+radius<chunk.BlockX||x-radius>chunk.BlockX+16||z+radius<chunk.BlockZ||z-radius>chunk.BlockZ+16)return;
            int loY=Math.Max(1,(int)Math.Floor(y-radius*verticalScale)),hiY=Math.Min(119,(int)Math.Ceiling(y+radius*verticalScale));
            if(loY>hiY)return;
            long loX=(long)Math.Floor(x-radius),hiX=(long)Math.Ceiling(x+radius),loZ=(long)Math.Floor(z-radius),hiZ=(long)Math.Ceiling(z+radius);
            // Check the whole section (not merely this chunk's clipped part) so
            // water rejection agrees on both sides of every chunk boundary.
            for(long pz=loZ-1;pz<=hiZ+1;pz++)for(long px=loX-1;px<=hiX+1;px++)
            {
                if(Water.IsWater(terrain(px,loY-1,pz))||Water.IsWater(terrain(px,hiY+1,pz)))return;
                if(px!=loX-1&&px!=hiX+1&&pz!=loZ-1&&pz!=hiZ+1)continue;
                for(int py=loY;py<=hiY;py++)if(Water.IsWater(terrain(px,py,pz)))return;
            }
            for(long pz=Math.Max(loZ,chunk.BlockZ);pz<=Math.Min(hiZ,chunk.BlockZ+15);pz++)
            for(long px=Math.Max(loX,chunk.BlockX);px<=Math.Min(hiX,chunk.BlockX+15);px++)for(int py=loY;py<=hiY;py++)
            {
                double dx=(px+.5-x)/radius,dz=(pz+.5-z)/radius,dy=(py+.5-y)/(radius*verticalScale);
                if(dy<=-.7||dx*dx+dy*dy+dz*dz>=1)continue;
                int index=Coordinates.Index((int)(px-chunk.BlockX),py,(int)(pz-chunk.BlockZ));
                if(Mask[index]==0){Mask[index]=1;CarvedCells++;}
            }
        }
    }
}
