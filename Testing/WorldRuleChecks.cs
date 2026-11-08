using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldRuleChecks
{
    internal static readonly (string Name,Action Check)[] All =
    {
        ("World-specific cheats/difficulty/mode roundtrips, command gates and locked application", Rules),
        ("Genuine v12/v11/v10 rule migration, exact state preservation and invalid rule bytes", Migration),
        ("World creation rule layouts/defaults and global preference separation", LayoutAndPreferences)
    };
    private static void Check(bool ok,string message)=>CommandChecks.Check(ok,message);
    public static void Rules()
    {
        foreach(bool cheats in new[]{false,true})foreach(Difficulty difficulty in Enum.GetValues<Difficulty>())foreach(GameMode mode in Enum.GetValues<GameMode>())
        {
            var world=PigChecks.Flat();world.AllowCheats=cheats;world.SavedDifficulty=difficulty;world.SavedGameMode=mode;world.Time.Restore(812.75);
            var inventory=new Inventory();inventory.SetSlot(0,new(Items.Apple,17));var save=new WorldSave(world,"Rules",new(40.5f,1,40.5f),.2f,-.1f,inventory);
            using var bytes=new MemoryStream();WorldSerializer.Write(bytes,save);bytes.Position=0;var restored=WorldSerializer.Read(bytes);
            Check(restored.World.AllowCheats==cheats && restored.World.SavedDifficulty==difficulty && restored.World.SavedGameMode==mode
                && restored.Inventory[0].Count==17 && restored.World.Time.Seconds==812.75,"Rule roundtrip lost state.");
            var session=new GameSession("rules",restored);
            Check(session.Player.Creative==(mode==GameMode.Creative) && session.Player.Settings.Difficulty==difficulty,"Session did not initialize its own rules.");
            var attempted=new GameSettings{GameMode=mode==GameMode.Creative?GameMode.Survival:GameMode.Creative,Difficulty=difficulty==Difficulty.Hard?Difficulty.Easy:Difficulty.Hard,FpsCap=144};
            session.ApplySettings(attempted);
            Check(attempted.FpsCap==144,"Locked world prevented graphics changes.");
            if(!cheats)
            {
                Check(attempted.GameMode==mode && attempted.Difficulty==difficulty && session.World.SavedGameMode==mode && session.World.SavedDifficulty==difficulty,"Disabled cheats allowed rule changes.");
                foreach(string command in new[]{"/help","/time set 1","/gamemode creative","/give @p apple","/spawn pig","/kill @e"})
                    Check(!new WorldCommands().Execute(command,session,attempted).Success && session.World.Time.Seconds==812.75 && session.Inventory[0].Count==17 && !session.Player.IsDead,"Locked world executed a command.");
            }
            else Check(session.World.SavedGameMode==attempted.GameMode && session.World.SavedDifficulty==attempted.Difficulty,"Enabled cheats did not update stored world rules.");
        }
    }
    private static int EnvironmentOffset(byte[] bytes)
    {
        int at=52+BitConverter.ToInt32(bytes,24)+48;
        while(BitConverter.ToUInt32(bytes,at)!=WorldSerializer.EnvironmentRecord)at+=56+BitConverter.ToInt32(bytes,at+20);
        return at;
    }
    private static void Rehash(byte[] bytes)
    {
        int start=52+BitConverter.ToInt32(bytes,24)+48;
        SHA256.HashData(bytes.AsSpan(0,start-32)).CopyTo(bytes.AsSpan(start-32,32));
        SHA256.HashData(bytes.AsSpan(0,bytes.Length-32)).CopyTo(bytes.AsSpan(bytes.Length-32));
    }
    public static void Migration()
    {
        var session=CommandChecks.Session();session.World.SavedGameMode=GameMode.Creative;session.World.SavedDifficulty=Difficulty.Hard;session.World.AllowCheats=false;session.World.Time.Restore(876.25);
        session.Inventory.SetSlot(0,new(Items.Tool(ToolTier.Iron,ToolKind.Axe),1,61));session.World.SetBlock(5,127,5,BlockType.Stone);
        var save=session.Capture();byte[] hash=CompressionChecks.RawHash(save);
        using var output=new MemoryStream();WorldSerializer.Write(output,save);byte[] current=output.ToArray();int at=EnvironmentOffset(current);
        Check(current[at+8]==0 && BitConverter.ToInt32(current,at+20)==12,"Rule fixture is not stored twelve-byte environment.");
        foreach(byte oldMode in new byte[]{0,1,2})
        {
            byte[] v12=current[..(at+56+10)].Concat(current[(at+56+12)..]).ToArray();BitConverter.GetBytes(12).CopyTo(v12,4);
            BitConverter.GetBytes(10).CopyTo(v12,at+16);BitConverter.GetBytes(10).CopyTo(v12,at+20);v12[at+56+8]=oldMode;
            SHA256.HashData(v12.AsSpan(at+56,10)).CopyTo(v12.AsSpan(at+24,32));v12=OvernightChecks.WithoutFeatures(v12);Rehash(v12);
            var loaded=WorldSerializer.Read(new MemoryStream(v12));
            Check(loaded.World.AllowCheats && loaded.World.SavedGameMode==GameMode.Survival && loaded.World.SavedDifficulty==Difficulty.Normal && loaded.World.Time.Seconds==876.25
                && hash.AsSpan().SequenceEqual(CompressionChecks.RawHash(loaded)),"Pre-change v12 did not migrate exactly once to allowed/Normal/Survival.");
            var played=new GameSession("old",loaded);played.ApplySettings(new(){GameMode=GameMode.Creative,Difficulty=Difficulty.Hard});
            using var migrated=new MemoryStream();WorldSerializer.Write(migrated,played.Capture());migrated.Position=0;var again=WorldSerializer.Read(migrated);
            Check(again.World.SavedGameMode==GameMode.Creative && again.World.SavedDifficulty==Difficulty.Hard && again.World.AllowCheats,"Migrated world reset its chosen rules on subsequent load.");
        }
        byte[] v11=current[..at].Concat(current[(at+56+12)..]).ToArray();BitConverter.GetBytes(11).CopyTo(v11,4);v11=OvernightChecks.WithoutFeatures(v11);Rehash(v11);
        var legacy=WorldSerializer.Read(new MemoryStream(v11));Check(legacy.World.AllowCheats && legacy.World.SavedDifficulty==Difficulty.Normal && legacy.World.SavedGameMode==GameMode.Survival,"V11 migration failed.");
        using var raw=new MemoryStream();WorldSerializer.WriteLegacy(raw,save);raw.Position=0;legacy=WorldSerializer.Read(raw);
        Check(legacy.World.AllowCheats && legacy.World.SavedDifficulty==Difficulty.Normal && legacy.World.SavedGameMode==GameMode.Survival,"Raw legacy migration failed.");
        foreach(var invalid in new (int Offset,byte Value)[]{(8,2),(10,2),(11,4)})
        {
            byte[] bad=(byte[])current.Clone();bad[at+56+invalid.Offset]=invalid.Value;
            SHA256.HashData(bad.AsSpan(at+56,12)).CopyTo(bad.AsSpan(at+24,32));Rehash(bad);bool rejected=false;
            try{WorldSerializer.Read(new MemoryStream(bad));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Invalid world rules were accepted with valid checksums.");
        }
    }
    public static void LayoutAndPreferences()
    {
        LargeLakeChecks.Notice();
        foreach(Point size in new[]{new Point(320,240),new(640,360),new(800,480),new(1280,720),new(1920,1080)})
        {
            var viewport=new Viewport(0,0,size.X,size.Y);
            for(int i=0;i<3;i++)
            {
                Rectangle option=WorldCreationLayout.Option(viewport,i);
                Check(option.Left>=0 && option.Right<=size.X && option.Bottom<WorldCreationLayout.Notice(viewport).Top,"Rule option leaves window or overlaps notice.");
                for(int j=0;j<i;j++)Check(!option.Intersects(WorldCreationLayout.Option(viewport,j)),"Rule options overlap.");
            }
        }
        string parent=Path.GetFullPath(AppContext.BaseDirectory),root=Path.Combine(parent,".world-rule-prefs-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(new(){GameMode=GameMode.Creative,Difficulty=Difficulty.Hard,FpsCap=144});
            string json=File.ReadAllText(store.FilePath);Check(!json.Contains("GameMode") && !json.Contains("Difficulty") && store.Load().FpsCap==144,"Global settings persist world rules.");
            File.WriteAllText(store.FilePath,"{\"GameMode\":1,\"Difficulty\":3,\"FpsCap\":240}");var settings=store.Load();
            Check(settings.GameMode==GameMode.Survival && settings.Difficulty==Difficulty.Normal && settings.FpsCap==240,"Old global preferences override new world defaults.");
        }
        finally{if(Directory.Exists(root) && Path.GetDirectoryName(Path.GetFullPath(root))==Path.TrimEndingDirectorySeparator(parent))Directory.Delete(root,true);}
    }
    public static int Run()
    {
        int failed=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        return failed==0?0:1;
    }
}
