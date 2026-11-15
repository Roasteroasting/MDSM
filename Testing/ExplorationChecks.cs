using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class ExplorationChecks
{
    internal static readonly (string Name,Action Check)[] All={
        ("Creative pick-block mapping, exact stack preservation, capacity and Survival isolation",Pick),
        ("Safe absolute/relative teleport, hazards, movement reset, permissions and persistence",Teleport),
        ("Sparse nearest-dungeon lookup, older saves, edited rooms, permissions and completion",Locate)};
    public static int Run(){foreach(var test in All){test.Check();Console.WriteLine("PASS: "+test.Name);}return 0;}
    private static void C(bool value,string message)=>CommandChecks.Check(value,message);
    private static CommandResult Run(GameSession s,string text)=>new WorldCommands().Execute(text,s,s.Player.Settings);
    private static void Pick()
    {
        var s=CommandChecks.Session();var cell=new Point3(20,1,20);s.World.SetBlock(20,1,20,BlockType.Stone);var hit=new BlockHit(cell,new(0,1,0),1);
        C(!ExplorationTools.Pick(s,hit) && s.Inventory.Selected.IsEmpty,"Survival gained Creative items.");s.Player.Settings.GameMode=GameMode.Creative;
        C(ExplorationTools.Pick(s,hit) && s.Inventory.Selected.Item==Items.FromBlock(BlockType.Stone),"Stone was picked as its mining drop instead of Stone.");
        var stack=new ItemStack(Items.Bow,1,21);s.Inventory.SetSlot(0,stack);s.Inventory.SetSlot(12,new(Items.FromBlock(BlockType.Stone),17));
        C(ExplorationTools.Pick(s,hit) && s.Inventory.Selected.Count==17 && s.Inventory[12]==stack,"Main-inventory swap lost quantity or equipment identity.");
        s.Inventory.SetSlot(8,new(Items.FromBlock(BlockType.Stone),9));C(ExplorationTools.Pick(s,hit)&&s.Inventory.SelectedSlot==0,"Existing first matching hotbar slot was not selected.");
        for(int i=0;i<Inventory.SlotCount;i++)s.Inventory.SetSlot(i,new(Items.Stick,64));
        C(!ExplorationTools.Pick(s,hit) && Enumerable.Range(0,36).All(i=>s.Inventory[i].Item==Items.Stick && s.Inventory[i].Count==64),"Full inventory lost items.");
        s.Inventory.SetSlot(22,default);C(ExplorationTools.Pick(s,hit)&&s.Inventory[22].Count==64 && s.Inventory.Selected.Item==Items.FromBlock(BlockType.Stone),"Safe relocation of occupied hotbar stack failed.");
        foreach(var pair in new[]{(BlockType.WaterSource,Items.WaterBucket),(BlockType.DoorUpper,Items.FromBlock(BlockType.Door)),(BlockType.Wheat7,Items.WheatSeeds),(BlockType.Fire,default(ItemId))})
        {
            // Loading fixtures may contain multipart blocks; no placement rules are bypassed in gameplay.
            s.World.SetGeneratedBlock(20,1,20,pair.Item1);for(int i=0;i<36;i++)s.Inventory.SetSlot(i,default);
            bool picked=ExplorationTools.Pick(s,hit);C(pair.Item2==default?!picked:picked && s.Inventory.Selected.Item==pair.Item2,"Special block picked the wrong registered item.");
        }
    }
    private static void Teleport()
    {
        var s=CommandChecks.Session();s.Player.TakeDamage(3);int health=s.Player.Health;float yaw=s.Camera.Yaw;
        C(Run(s,"/tp @p 50.5 1 50.5").Success && s.Player.Position==new Vector3(50.5f,1,50.5f) && s.Camera.Position==s.Player.EyePosition,"Absolute teleport/camera failed.");
        C(Run(s,"/tp @p ~2 ~ ~-3").Success && s.Player.Position==new Vector3(52.5f,1,47.5f),"Relative coordinates failed.");
        C(s.Player.Health==health && s.Camera.Yaw==yaw,"Teleport changed health/facing.");
        s.World.SetBlock(60,1,60,BlockType.Stone);Vector3 before=s.Player.Position;
        foreach(string text in new[]{"/tp @p NaN 1 2","/tp @p Infinity 1 2","/tp @p 1e99 1 2","/tp @p -1 1 2","/tp @p 60.5 1 60.5","/tp @e 1 1 1","/tp @p 1 1","/tp @p 1 127 1"})
            C(!Run(s,text).Success && s.Player.Position==before,"Invalid teleport moved player: "+text);
        s.World.SetBlock(61,1,61,BlockType.WaterSource);C(!Run(s,"/tp @p 61.5 1 61.5").Success,"Teleport accepted water.");
        s.Mobs.TrySummon(MDSM.Entities.MobSpecies.Cow,new(62.5f,1,62.5f));C(!Run(s,"/tp @p 62.5 1 62.5").Success,"Teleport intersected a mob.");
        s.World.AllowCheats=false;C(!Run(s,"/tp @p 70.5 1 70.5").Success,"Teleport bypassed world permissions.");s.World.AllowCheats=true;
        s.Player.Teleport(new(80.5f,50,80.5f));s.Player.Update(s.World,Vector3.Zero,false,.5f);
        C(Run(s,"/tp @p 70.5 1 70.5").Success,"Safe teleport failed.");
        s.Player.Update(s.World,Vector3.Zero,false,1f/120);C(s.Player.Health==health,"Teleport retained stale fall damage.");
        using var data=new MemoryStream();WorldSerializer.Write(data,s.Capture());data.Position=0;C(WorldSerializer.Read(data).PlayerPosition==s.Player.Position,"Teleport position did not persist.");
    }
    private static void Locate()
    {
        var s=CommandChecks.Session();C(!Run(s,"/locate dungeon").Success,"Empty world reported a dungeon.");
        for(int y=0;y<=35;y++)for(int z=10;z<=30;z++)for(int x=10;x<=30;x++)s.World.SetGeneratedBlock(x,y,z,BlockType.Stone);
        for(int y=10;y<=14;y++)for(int z=18;z<=22;z++)for(int x=16;x<=22;x++)s.World.SetGeneratedBlock(x,y,z,BlockType.Air);
        C(DungeonGenerator.TryRoom(s.World,new(20,10,20),new Random(1)),"Fixture room rejected.");
        C(ExplorationTools.NearestDungeon(s.World,s.Player.Position)==new Point3(20,10,20),"Dungeon center not recognized.");
        for(int y=0;y<=35;y++)for(int z=70;z<=90;z++)for(int x=70;x<=90;x++)s.World.SetGeneratedBlock(x,y,z,BlockType.Stone);
        for(int y=10;y<=14;y++)for(int z=78;z<=82;z++)for(int x=76;x<=82;x++)s.World.SetGeneratedBlock(x,y,z,BlockType.Air);
        C(DungeonGenerator.TryRoom(s.World,new(80,10,80),new Random(2)),"Second room fixture rejected.");
        C(ExplorationTools.NearestDungeon(s.World,new(85,10,85))==new Point3(80,10,80),"Locator did not choose the nearest room.");
        C(Run(s,"/locate dungeon").Message.Contains("/tp @p 20.5 10 20.5"),"Locator omitted usable coordinates.");
        using var data=new MemoryStream();WorldSerializer.Write(data,s.Capture());data.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(data));
        C(loaded.World.GeneratedDungeons.Count==0 && Run(loaded,"/locate dungeon").Success,"Lookup depended on unpersisted generation metadata.");
        C(Run(loaded,"/tp @p 20.5 10 20.5").Success,"Reported room center is not a clear destination.");
        s.World.SetBlock(20,9,20,BlockType.Air);C(ExplorationTools.NearestDungeon(s.World,s.Player.Position)==new Point3(80,10,80),"Altered non-intact room was still advertised.");
        loaded.World.AllowCheats=false;C(!Run(loaded,"/locate dungeon").Success,"Locator bypassed cheats restriction.");
        foreach(var pair in new[]{("/lo","/locate"),("/locate ","dungeon"),("/tp ","@p"),("/tp @p ","~")})
            C(CommandCompletion.Candidates(pair.Item1,pair.Item1.Length,out _,out _).Contains(pair.Item2),"New command completion missing.");
    }
}
