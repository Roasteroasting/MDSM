using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class CommandChecks
{
    internal static readonly (string Name, Action Check)[] All =
    {
        ("Command validation, help pages and complete registry name/ID resolution", Parsing),
        ("Atomic command grants, stack limits and fresh equipment metadata", Giving),
        ("Numeric and named world-time commands with no simulation catch-up", Time),
        ("Safe species summons, shared limits and unchanged natural respawn targets", Spawning),
        ("Blocked summon search, fluids, headroom, edges and Peaceful", SpawnSafety),
        ("Command mode, time, inventory and summoned entity save/load", Persistence),
        ("Command text editing, opening-key isolation and bounded history", Editor),
        ("Kill syntax, species/ID/group filters and idempotent mob deaths", KillCommandChecks.Selectors),
        ("Kill player bypasses Creative/armor safely and preserves death/respawn accounting", KillCommandChecks.PlayerDeath),
        ("Filtered item/projectile/falling-entity removal preserves other state", KillCommandChecks.TransientEntities),
        ("All-entity removal, death-loot cleanup, population accounting and persistence", KillCommandChecks.AllAndPersistence)
    };
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static GameSession Session() => new("commands", new(PigChecks.Flat(), "Commands", new Vector3(40.5f, 1, 40.5f), 0, 0));
    private static CommandResult Run(WorldCommands commands, GameSession session, string text) => commands.Execute(text, session, session.Player.Settings);
    private static void Good(WorldCommands commands, GameSession session, string text) => Check(Run(commands,session,text).Success, "Command failed: " + text);
    private static int Count(Inventory inventory, ItemId item) => Enumerable.Range(0, Inventory.SlotCount).Sum(i => inventory[i].Item == item ? inventory[i].Count : 0);
    public static void Parsing()
    {
        var commands = new WorldCommands(); var session = Session();
        foreach (string text in new[] { "", "give @p stone", "/", "/unknown", "/give", "/give @a stone", "/give @p 65535", "/give @p air", "/give @p stone 0", "/give @p stone -1", "/give @p stone 1.5", "/give @p stone 2147483648", "/spawn", "/spawn 7", "/spawn pig,cow", "/spawn zombie 33", "/spawn pig -1", "/time set NaN", "/time set 24000", "/time set -1", "/time set 1.5", "/gamemode 2", "/help items 0", "/help items 999", "/help time extra", "/help unknown" })
        { int revision = session.World.Revision; double clock = session.World.Time.Seconds; Check(!Run(commands,session,text).Success && session.Inventory.Selected.IsEmpty && session.World.Revision == revision && session.World.Time.Seconds == clock && session.Mobs.Entities.Count == 0, "Invalid command mutated world state: " + text); }
        Check(!commands.Execute("/time set 1",null,new()).Success,"Menu command accessed missing session.");
        Check(!Run(commands,session,"/" + new string('x', 256)).Success,"Unbounded command input accepted.");
        foreach (ItemId item in Items.Obtainable)
        {
            Check(WorldCommands.TryItem(item.Value.ToString(),out var resolved) && resolved == item,"Registry ID not resolved.");
            Check(WorldCommands.TryItem(WorldCommands.Key(Items.Name(item)),out resolved) && resolved == item,"Registry name not resolved uniquely: " + Items.Name(item));
        }
        foreach (string text in new[] { "/help", "/help give", "/help spawn", "/help time", "/help gamemode", "/help mobs", "/help items", "/help items 2" }) Good(commands,session,text);
        Good(commands,session," /GIVE   @P   StOnE   2 "); Check(Count(session.Inventory,Items.FromBlock(BlockType.Stone)) == 2,"Case/whitespace handling failed.");
    }
    public static void Giving()
    {
        var commands = new WorldCommands(); var session = Session(); var stone = Items.FromBlock(BlockType.Stone);
        session.Inventory.SetSlot(0,new(stone,60)); Good(commands,session,"/give @p stone 10");
        Check(session.Inventory[0].Count==64 && session.Inventory[1].Count==6,"Give did not merge first/overflow correctly.");
        Good(commands,session,"/give @p diamond_pickaxe 2");
        Check(session.Inventory[2].Durability==Items.Get(session.Inventory[2].Item).Durability && session.Inventory[2].InstanceId!=session.Inventory[3].InstanceId,"Granted tools lacked independent full durability.");
        Good(commands,session,"/give @p raw_porkchop 2"); Check(session.Inventory[4].Count==1 && session.Inventory[5].Count==1,"Nonstackable food stacked.");
        for(int i=0;i<Inventory.SlotCount;i++) session.Inventory.SetSlot(i,new(stone,64));
        session.Inventory.SetSlot(0,new(stone,63)); var before=session.Inventory.Clone();
        Check(!Run(commands,session,"/give @p stone 2").Success,"Overflowing give was accepted.");
        for(int i=0;i<Inventory.SlotCount;i++) Check(before[i]==session.Inventory[i],"Rejected give partially changed inventory.");
        Good(commands,session,"/give @p stone"); Check(session.Inventory[0].Count==64,"Default give quantity is not one.");
    }
    public static void Time()
    {
        var commands=new WorldCommands();var session=Session();
        foreach(int tick in new[]{0,1,1000,6000,12000,14000,18000,23999})
        { Good(commands,session,$"/time set {tick}"); Check(Math.Abs(session.World.Time.Seconds-tick*.05)<.000001,"Numeric ticks do not map to exact game clock."); Check(Run(commands,session,"/time query").Message.StartsWith($"Time: {tick} /"),"Time query rounded to the wrong tick."); }
        foreach(var preset in new[]{("sunrise",0),("day",1000),("noon",6000),("sunset",12000),("night",14000),("midnight",18000)})
        { Good(commands,session,"/time set "+preset.Item1); Check(Math.Abs(session.World.Time.Seconds-preset.Item2*.05)<.000001,"Preset is inconsistent with numeric time."); }
        Check(session.World.Time.SunlightLevel==0,"Midnight retained daylight."); Good(commands,session,"/time set day");Check(session.World.Time.SunlightLevel==14,"Day did not restore effective sunlight.");
    }
    public static void Spawning()
    {
        var commands=new WorldCommands();var session=Session();var targets=session.Mobs.CapturePopulation().Targets;
        foreach(var species in Enum.GetValues<MobSpecies>()) Good(commands,session,"/spawn "+species+" 2");
        Check(session.Mobs.Entities.Count==8 && session.Hostiles.Entities.Count==6,"Wrong summoned species quantities.");
        Check(session.Mobs.CapturePopulation().Targets.SequenceEqual(targets) && session.Mobs.Entities.All(m=>m.Bred),"Summons altered original population targets.");
        var all=session.Mobs.Entities.Concat(session.Hostiles.Entities).ToArray();
        for(int i=0;i<all.Length;i++)
        {
            var mob=all[i]; Check(mob.Health==mob.Definition.MaximumHealth && !session.World.IntersectsSolid(mob.Bounds) && !World.Overlaps(mob.Bounds,session.Player.Bounds),"Summon spawned embedded or unhealthy.");
            for(int j=i+1;j<all.Length;j++) Check(!World.Overlaps(mob.Bounds,all[j].Bounds),"Summons intersected one another.");
        }
        var pig=session.Mobs.Entities.First();session.Mobs.Hit(pig,100,session.Player.Position,session.Items);
        Check(session.Mobs.PendingRespawns.Count==0,"Explicit mob death increased natural replenishment.");
        Good(commands,session,"/spawn 4 30"); Check(session.Hostiles.Entities.Count==HostilePopulation.Maximum,"Hostile cap is not respected.");
        Check(!Run(commands,session,"/spawn zombie").Success && session.Hostiles.Entities.Count==HostilePopulation.Maximum,"Full population spawned another mob.");
    }
    public static void SpawnSafety()
    {
        var commands=new WorldCommands();var session=Session();session.Player.Settings.Difficulty=Difficulty.Peaceful;
        Check(!Run(commands,session,"/spawn zombie").Success && session.Hostiles.Entities.Count==0,"Peaceful allowed hostile spawn.");
        Good(commands,session,"/spawn chicken");
        var empty=new World(4,WorldSize.Small,false);session=new("empty",new(empty,"Empty",new Vector3(1,127,1),0,0));
        long start=System.Diagnostics.Stopwatch.GetTimestamp();Check(!Run(commands,session,"/spawn pig 32").Success,"Summon floated without support.");
        Check(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds<2,"Failed summon performed an unbounded search.");
        var world=PigChecks.Flat();for(int x=25;x<=55;x++)for(int z=25;z<=55;z++)for(int y=1;y<=3;y++)world.SetBlock(x,y,z,BlockType.WaterSource);
        session=new("water",new(world,"Water",new Vector3(40.5f,1,40.5f),0,0));
        Check(!Run(commands,session,"/spawn cow").Success && session.Mobs.Entities.Count==0,"Summon spawned inside water.");
        for(int x=25;x<=55;x++)for(int z=25;z<=55;z++)world.SetBlock(x,3,z,BlockType.Stone);
        Good(commands,session,"/spawn cow");Check(session.Mobs.Entities[0].Position.Y==4,"Summon did not search above nearby obstacles.");
    }
    public static void Persistence()
    {
        var commands=new WorldCommands();var session=Session();
        Good(commands,session,"/give @p door 12");Good(commands,session,"/give @p iron_chestplate");Good(commands,session,"/spawn cow");Good(commands,session,"/spawn skeleton");Good(commands,session,"/time set 17345");
        var targets=session.Mobs.CapturePopulation().Targets;
        Check(Run(commands,session,"/gamemode creative").SettingsChanged && session.Player.Creative,"Mode command did not use existing settings pathway.");
        using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(data));
        Check(loaded.Player.Creative && loaded.Inventory[0].Count==12 && loaded.Inventory[1]==session.Inventory[1] && loaded.Mobs.Entities.Count==1 && loaded.Hostiles.Entities.Count==1
            && loaded.Mobs.Entities[0].Bred && loaded.Mobs.CapturePopulation().Targets.SequenceEqual(targets) && loaded.World.Time.Seconds==session.World.Time.Seconds,"Commands did not persist through existing format.");
        Good(commands,loaded,"/gamemode survival");Check(!loaded.Player.Creative && loaded.Inventory[0].Count==12,"Mode change deleted command-obtained items.");
    }
    public static void Editor()
    {
        var editor=new CommandLineInput();editor.Open();editor.TextInput('t');Check(editor.Text=="","Opening key entered command text.");
        editor.Update(default,default);foreach(char c in "/time set 6000")editor.TextInput(c);
        Check(editor.Submit()=="/time set 6000" && editor.Text=="","Submit did not reset/return exact command.");
        editor.Update(new(Keys.Up),default);Check(editor.Text=="/time set 6000","Up did not recall command.");
        editor.Update(new(Keys.Home),default);editor.Update(new(Keys.Right),default);editor.TextInput('x');editor.TextInput('\b');Check(editor.Text=="/time set 6000","Caret insertion/backspace failed.");
        editor.Update(new(Keys.LeftControl,Keys.A),default);editor.TextInput('/');foreach(char c in "give @p apple 1")editor.TextInput(c);Check(editor.Submit()=="/give @p apple 1","Ctrl+A replacement failed.");
        editor.Update(new(Keys.Up),default);editor.Update(new(Keys.Up),default); // Held Up must not execute repeated actions without another key press.
        Check(editor.Text=="/time set 6000","History key transition failed.");
        editor.ClearHistory();editor.Update(default,default);editor.Update(new(Keys.Up),default);Check(editor.Text=="","World switch retained another world's history.");
        for(int i=0;i<300;i++)editor.TextInput('a');Check(editor.Text.Length==CommandLineInput.MaximumLength,"Editor allowed unbounded input.");
        editor.Update(new(Keys.LeftControl,Keys.A),default);editor.Update(new(Keys.Delete),default);Check(editor.Text=="","Delete selection failed.");
    }
    public static int Run()
    {
        int failures=0;foreach(var test in All)try{test.Check();Console.WriteLine("PASS: "+test.Name);}catch(Exception e){failures++;Console.Error.WriteLine("FAIL: "+test.Name+": "+e);}
        Console.WriteLine($"{All.Length-failures}/{All.Length} command checks passed.");return failures==0?0:1;
    }
}
