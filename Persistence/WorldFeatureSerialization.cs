using System;
using System.IO;
using MDSM.Worlds;
using MDSM.Gameplay;
using MDSM.Entities;

namespace MDSM.Persistence;

public static partial class WorldSerializer
{
    private static byte[] WriteFeatures(WorldSave save)
    {
        using var data=new MemoryStream();using var writer=new BinaryWriter(data,Utf8,true);
        int expectedSigns=0;foreach(var pair in save.World.Structures)if(save.World.GetBlock(pair.Key.X,pair.Key.Y,pair.Key.Z)==BlockType.Sign)expectedSigns++;
        if(expectedSigns!=save.World.Signs.Count)throw new InvalidDataException("Missing sign text.");
        writer.Write(2);writer.Write(save.World.Signs.Count);
        foreach(var pair in save.World.Signs)
        {if(save.World.GetBlock(pair.Key.X,pair.Key.Y,pair.Key.Z)!=BlockType.Sign||!World.ValidSignText(pair.Value))throw new InvalidDataException("Invalid sign text.");WritePoint(writer,pair.Key);writer.Write(pair.Value);}
        var mobs=save.Mobs??Array.Empty<MDSM.Entities.MobState>();writer.Write(mobs.Length);
        foreach(var mob in mobs){writer.Write(mob.EggSeconds);WriteBurn(writer,mob.Burning);}
        writer.Write(save.World.Climate.VariedGeneration);
        var weather=save.World.Weather.Capture();writer.Write(weather.UntilNext);writer.Write(weather.Remaining);writer.Write(weather.X);writer.Write(weather.Z);writer.Write(weather.Radius);writer.Write(weather.RandomState);writer.Write(weather.MotionSeconds);
        var balls=save.Snowballs??Array.Empty<MDSM.Entities.ArrowState>();writer.Write(balls.Length);
        if(balls.Length+(save.Arrows?.Length??0)>MDSM.Entities.Projectiles.Maximum)throw new InvalidDataException("Too many projectiles.");
        foreach(var ball in balls)
        {
            MDSM.Entities.Projectiles.Validate(ball,save.World);if(ball.Kind!=MDSM.Entities.ProjectileKind.Snowball)throw new InvalidDataException("Invalid snowball.");
            writer.Write(ball.Position.X);writer.Write(ball.Position.Y);writer.Write(ball.Position.Z);writer.Write(ball.Velocity.X);writer.Write(ball.Velocity.Y);writer.Write(ball.Velocity.Z);writer.Write(ball.Age);writer.Write(ball.Bow.ToByteArray());
        }
        WriteBurn(writer,save.PlayerFire);
        var pigs=save.Pigs??Array.Empty<PigState>();writer.Write(pigs.Length);foreach(var pig in pigs)WriteBurn(writer,pig.Burning);
        var hostile=save.Hostiles??Array.Empty<MobState>();writer.Write(hostile.Length);foreach(var mob in hostile)WriteBurn(writer,mob.Burning);
        var fires=save.World.Fire.Capture();writer.Write(fires.Length);
        int expectedFires=0;foreach(var chunk in save.World.Chunks)expectedFires+=chunk.FireCount;if(expectedFires!=fires.Length)throw new InvalidDataException("Missing fire timers.");
        foreach(var f in fires){WritePoint(writer,f.Cell);writer.Write(f.Remaining);writer.Write(f.NextTick);writer.Write((byte)f.Attachment);}
        return data.ToArray();
    }
    private static WorldSave ReadFeatures(WorldSave save,byte[] bytes)
    {
        using var data=new MemoryStream(bytes,false);using var reader=new BinaryReader(data,Utf8);
        int schema=reader.ReadInt32();if(schema is <1 or >2)throw new InvalidDataException("Unsupported world feature schema.");
        int count=reader.ReadInt32();if(count<0||count>65536)throw new InvalidDataException("Invalid sign count.");
        for(int i=0;i<count;i++)
        {Point3 cell=ReadPoint(reader);string text=reader.ReadString();if(save.World.Signs.ContainsKey(cell)||!save.World.EditSign(cell,text))throw new InvalidDataException("Invalid/duplicate sign.");}
        int expected=0;foreach(var pair in save.World.Structures)if(save.World.GetBlock(pair.Key.X,pair.Key.Y,pair.Key.Z)==BlockType.Sign)expected++;
        if(expected!=count)throw new InvalidDataException("Missing sign text.");
        var mobs=save.Mobs??Array.Empty<MDSM.Entities.MobState>();if(reader.ReadInt32()!=mobs.Length)throw new InvalidDataException("Mob feature count mismatch.");
        for(int i=0;i<mobs.Length;i++)
        {double egg=reader.ReadDouble();if(!double.IsFinite(egg)||egg<0||egg>330)throw new InvalidDataException("Invalid egg timer.");mobs[i]=mobs[i] with {EggSeconds=egg,Burning=ReadBurn(reader)};}
        byte varied=reader.ReadByte();if(varied>1)throw new InvalidDataException("Invalid climate generation flag.");
        save.World.Climate.VariedGeneration=varied==1;
        save.World.Weather.Restore(new(reader.ReadDouble(),reader.ReadDouble(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadUInt32(),reader.ReadDouble()));
        int ballCount=reader.ReadInt32();if(ballCount<0||ballCount+(save.Arrows?.Length??0)>MDSM.Entities.Projectiles.Maximum)throw new InvalidDataException("Invalid snowball count.");
        var balls=new MDSM.Entities.ArrowState[ballCount];
        for(int i=0;i<balls.Length;i++)
        {
            balls[i]=new(new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle()),reader.ReadSingle(),0,new Guid(reader.ReadBytes(16)),Kind:MDSM.Entities.ProjectileKind.Snowball);
            MDSM.Entities.Projectiles.Validate(balls[i],save.World);
        }
        BurningState playerFire=ReadBurn(reader);
        var pigs=save.Pigs??Array.Empty<PigState>();if(reader.ReadInt32()!=pigs.Length)throw new InvalidDataException("Pig feature count mismatch.");
        for(int i=0;i<pigs.Length;i++)pigs[i]=pigs[i] with {Burning=ReadBurn(reader)};
        var hostile=save.Hostiles??Array.Empty<MobState>();if(reader.ReadInt32()!=hostile.Length)throw new InvalidDataException("Hostile feature count mismatch.");
        for(int i=0;i<hostile.Length;i++)hostile[i]=hostile[i] with {Burning=ReadBurn(reader)};
        int fireCount=reader.ReadInt32();if(fireCount<0||fireCount>FireSimulation.Maximum)throw new InvalidDataException("Invalid fire count.");
        var fires=new FireState[fireCount];for(int i=0;i<fireCount;i++)fires[i]=new(ReadPoint(reader),reader.ReadDouble(),reader.ReadDouble(),schema>=2?(FireAttachment)reader.ReadByte():FireAttachment.Automatic);save.World.Fire.Restore(fires);
        int expectedFires=0;foreach(var chunk in save.World.Chunks)expectedFires+=chunk.FireCount;if(expectedFires!=fireCount)throw new InvalidDataException("Missing fire timers.");
        if(data.Position!=data.Length)throw new InvalidDataException("Unexpected feature data.");
        return save with {Snowballs=balls,PlayerFire=playerFire};
    }
    private static BurningState ReadBurn(BinaryReader reader)
    {var s=new BurningState(reader.ReadDouble(),reader.ReadDouble());ValidateBurn(s);return s;}
    private static void WriteBurn(BinaryWriter writer,BurningState s)
    {ValidateBurn(s);writer.Write(s.Remaining);writer.Write(s.DamageClock);}
    private static void ValidateBurn(BurningState s)
    {if(!double.IsFinite(s.Remaining)||s.Remaining<0||s.Remaining>9||!double.IsFinite(s.DamageClock)||s.DamageClock<0||s.DamageClock>=1)throw new InvalidDataException("Invalid burning state.");}
}
