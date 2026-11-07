using System.Text;
using MDSM.Audio;

internal static class Synthesizer
{
    public const int SampleRate=22050;
    public static byte[] Generate(SoundGroup group,int variation)
    {
        uint state=2166136261;foreach(char c in group.Path)state=(state^c)*16777619;state^=(uint)(variation+1)*0x9e3779b9;
        double duration=group.Kind<=SoundKind.Land?group.Kind switch{SoundKind.Break=>.29,SoundKind.Land=>.18,SoundKind.Step=>.105,_=>.14}
            :group.Kind is >=SoundKind.MobIdle and <=SoundKind.MobDeath?group.Kind==SoundKind.MobDeath?.8:group.Species==3?.34:.65
            :group.Kind switch{SoundKind.PlayerDeath=>.7,SoundKind.BowDraw=>.3,SoundKind.Eat=>.26,SoundKind.WaterEnter or SoundKind.WaterExit or SoundKind.WaterFlow or SoundKind.Pour or SoundKind.Scoop=>.4,_=>.17};
        int count=(int)(duration*SampleRate);var samples=new double[count];double low=0,slow=0,phase=0,peak=0;
        double detune=1+(variation-(group.Variations-1)*.5)*.055;
        for(int i=0;i<count;i++)
        {
            double t=i/(double)SampleRate,u=t/duration;
            state^=state<<13;state^=state>>17;state^=state<<5;double noise=state/(double)uint.MaxValue*2-1;
            low+=.17*(noise-low);slow+=.027*(noise-slow);
            double envelope=Math.Min(1,t/.004)*Math.Pow(1-u,1.6),value;
            if(group.Kind<=SoundKind.Land)
            {
                double frequency=group.Material switch{SoundMaterial.Stone=>245,SoundMaterial.Dirt=>95,SoundMaterial.Grass=>130,SoundMaterial.Wood=>170,
                    SoundMaterial.Sand=>80,SoundMaterial.Gravel=>350,SoundMaterial.Glass=>2100,SoundMaterial.Metal=>690,SoundMaterial.Wool=>65,SoundMaterial.Leaves=>190,_=>360};
                phase+=Math.Tau*frequency*detune/SampleRate;
                double ring=Math.Sin(phase)*Math.Exp(-t*32)+.35*Math.Sin(phase*1.91)*Math.Exp(-t*24);
                value=group.Material switch
                {
                    SoundMaterial.Stone=>low*.8+ring*.4+noise*.12,
                    SoundMaterial.Dirt=>slow*2.5+ring*.25,
                    SoundMaterial.Grass=>low*.8+(noise-low)*.28+ring*.2,
                    SoundMaterial.Wood=>ring*.65+Math.Sin(phase*.43)*Math.Exp(-t*27)*.25+low*.55,
                    SoundMaterial.Sand=>low*.55+(noise-low)*.35+slow*.8,
                    SoundMaterial.Gravel=>(noise-low)*(.15+.55*Math.Pow(.5+.5*Math.Sin(t*220+variation),5))+ring*.2,
                    SoundMaterial.Glass=>Math.Sin(phase)*Math.Exp(-t*12)*.35+Math.Sin(phase*1.47)*Math.Exp(-t*18)*.25+noise*.1,
                    SoundMaterial.Metal=>(Math.Sin(phase)+.5*Math.Sin(phase*2.37)+.3*Math.Sin(phase*3.69))*Math.Exp(-t*14)*.35+low*.2,
                    SoundMaterial.Wool=>slow*2+low*.35+ring*.12,
                    SoundMaterial.Leaves=>(noise-low)*(.25+.25*Math.Sin(t*145)*Math.Sin(t*145))+low*.5,
                    _=>low*.5+(noise-low)*.25
                };
                value*=group.Kind switch{SoundKind.Hit=>Math.Exp(-t*12),SoundKind.Place=>Math.Exp(-t*22)*(1+.35*Math.Sin(t*90)),SoundKind.Step=>Math.Exp(-t*25),SoundKind.Land=>1+.5*Math.Exp(-t*18),_=>.5+Math.Abs(Math.Sin(t*47+variation))};
            }
            else if(group.Kind is >=SoundKind.MobIdle and <=SoundKind.MobDeath)
            {
                double f=group.Species switch{0=>130,1=>68,2=>190,3=>620,4=>90,5=>1100,_=>850};
                f*=detune*(group.Kind==SoundKind.MobHurt?1.25:group.Kind==SoundKind.MobDeath?1.1-u*.65:1);
                f*=1+.06*Math.Sin(t*(group.Species==2?48:19));phase+=Math.Tau*f/SampleRate;
                double voice=Math.Sin(phase)+.45*Math.Sin(phase*2)+.22*Math.Sin(phase*3);
                double pulses=.6+.4*Math.Sin(t*(group.Species==3?63:group.Species==2?42:21));
                value=group.Species switch
                {
                    0=>(voice*.45+low*.8)*pulses,
                    1=>voice*.55*(.75+.25*Math.Sin(Math.PI*u)),
                    2=>voice*.5*pulses+low*.12,
                    3=>(Math.Sin(phase)*.55+noise*.1)*Math.Pow(pulses,3),
                    4=>(voice*.25+slow*1.5+low*.65)*pulses,
                    5=>(Math.Sin(phase)*.35*Math.Exp(-t*15)+noise*.16)*Math.Pow(.5+.5*Math.Cos(t*60),5),
                    _=>(noise-low)*.45*pulses+Math.Sin(phase)*.13*Math.Pow(pulses,4)
                };
                envelope=Math.Min(1,t/.018)*Math.Pow(Math.Sin(Math.PI*u),.7);
            }
            else
            {
                double frequency=group.Kind switch{SoundKind.Pickup=>850+u*1200,SoundKind.Craft=>t<.07?730:1100,
                    SoundKind.PlayerHurt=>150-u*60,SoundKind.PlayerDeath=>110-u*65,SoundKind.Eat=>140,
                    SoundKind.BowDraw=>180+u*500,SoundKind.BowShoot=>650-u*440,SoundKind.Equip=>1200,
                    SoundKind.DoorOpen or SoundKind.ChestOpen=>110+60*Math.Sin(t*28),SoundKind.GateOpen=>220,
                    SoundKind.ToolBreak=>1500-u*900,SoundKind.Click=>760,SoundKind.InventoryMove=>520,
                    SoundKind.Shear=>2200,SoundKind.Smelt=>1000+u*700,_=>210};
                phase+=Math.Tau*frequency*detune/SampleRate;
                bool water=group.Kind is SoundKind.WaterEnter or SoundKind.WaterExit or SoundKind.Swim or SoundKind.WaterFlow or SoundKind.Pour or SoundKind.Scoop;
                if(water)value=slow*2.2+low*(.6+.35*Math.Sin(t*110))+Math.Sin(phase*(1+Math.Sin(t*14)*.1))*.15*Math.Pow(.5+.5*Math.Sin(t*47),4);
                else value=group.Kind switch
                {
                    SoundKind.PlayerHurt or SoundKind.PlayerDeath=>Math.Sin(phase)*.45+Math.Sin(phase*2)*.2+low*.6,
                    SoundKind.DoorOpen or SoundKind.GateOpen or SoundKind.ChestOpen=>Math.Sin(phase)*.4+Math.Sin(phase*3)*.2+low*.3,
                    SoundKind.DoorClose or SoundKind.GateClose or SoundKind.ChestClose or SoundKind.Land=>slow*2+Math.Sin(phase)*Math.Exp(-t*35)*.7+low*.4,
                    SoundKind.BowDraw=>Math.Sin(phase)*.4+low*.1,
                    SoundKind.BowShoot or SoundKind.Swing=>low*.7+(noise-low)*.2+Math.Sin(phase)*Math.Exp(-t*30)*.2,
                    SoundKind.Eat=>low*(.8+.8*Math.Pow(.5+.5*Math.Sin(t*80),4))+slow*.5,
                    SoundKind.ToolBreak or SoundKind.Shear or SoundKind.ArrowHit=>noise*.22+Math.Sin(phase)*Math.Exp(-t*26)*.45,
                    SoundKind.Ignite or SoundKind.Fizz=>low*.5+noise*.3*Math.Pow(.5+.5*Math.Sin(t*130),2),
                    SoundKind.Till or SoundKind.Plant=>low*.8+slow*.8,
                    _=>Math.Sin(phase)*.55*Math.Exp(-t*24)+low*.12
                };
            }
            // Smooth attack/tail, gentle saturation, DC filtering and conservative peak headroom.
            samples[i]=Math.Tanh(value*envelope);peak=Math.Max(peak,Math.Abs(samples[i]));
        }
        double mean=samples.Average(),scale=.68/Math.Max(.001,peak+Math.Abs(mean));
        using var output=new MemoryStream();using var writer=new BinaryWriter(output,Encoding.ASCII,true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(SampleRate);writer.Write(SampleRate*2);writer.Write((short)2);writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++)
        {double edge=Math.Min(1,Math.Min(i,count-1-i)/64d);writer.Write((short)Math.Round((samples[i]-mean)*scale*edge*short.MaxValue));}
        return output.ToArray();
    }
}
