using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class KillCommandChecks
{
    private static void Check(bool condition,string message)=>CommandChecks.Check(condition,message);
    private static CommandResult Execute(GameSession session,string text)=>new WorldCommands().Execute(text,session,session.Player.Settings);
    private static GameSession Populated()
    {
        var session=CommandChecks.Session();
        for(int i=0;i<7;i++)
        {
            MobSpecies species=(MobSpecies)i;Vector3 feet=new(20+i*3,1,20);
            if(i<4)session.Mobs.TrySummon(species,feet);else session.Hostiles.TrySummon(species,feet);
        }
        session.Items.Spawn(Items.Apple,7,new(25,2,25));
        session.Projectiles.ShootHostile(new(25,3,25),Vector3.Right*5);
        session.World.Gravity.Restore(Array.Empty<int>(),new[]{new FallingBlockState(BlockType.Sand,30,30,5.5,2),new(BlockType.Gravel,31,30,6.5,3)});
        return session;
    }
    public static void Selectors()
    {
        foreach(string text in new[]{"/kill","/kill @a","/kill @p type=pig","/kill @e pig","/kill @e type=","/kill @e type=bogus","/kill @e type=pig type=cow","/kill @e type=pig,cow","/kill @e type=7"})
        {
            var session=Populated();int revision=session.World.Revision;
            Check(!Execute(session,text).Success && !session.Player.IsDead && session.Mobs.Entities.Count==4 && session.Hostiles.Entities.Count==3
                && session.Items.Entities.Count==1 && session.Projectiles.Arrows.Count==1 && session.World.Gravity.Active.Count==2 && session.World.Revision==revision,"Invalid selector mutated state: "+text);
        }
        for(int i=0;i<7;i++)
        {
            var session=Populated();var mob=session.Mobs.Entities.Concat(session.Hostiles.Entities).Single(m=>(int)m.Species==i);
            var result=Execute(session,"/KiLl @E TyPe="+(i%2==0?i.ToString():mob.Species.ToString().ToUpperInvariant()));
            Check(result.Success && mob.IsDead && session.Mobs.Entities.Count+session.Hostiles.Entities.Count==6 && !session.Player.IsDead,"Species filter selected incorrect entities.");
            Check(session.Items.Entities.Count>1 && session.Projectiles.Arrows.Count==1 && session.World.Gravity.Active.Count==2,"Species kill lost loot or selected unrelated entities.");
        }
        foreach(string type in new[]{"mob","passive","hostile"})
        {
            var session=Populated();Check(Execute(session,"/kill @e type="+type).Success,"Group selector failed.");
            Check(session.Mobs.Entities.Count==(type=="hostile"?4:0) && session.Hostiles.Entities.Count==(type=="passive"?3:0) && !session.Player.IsDead,"Group filter selected wrong population/player.");
            Check(Execute(session,"/kill @e type="+type).Message.Contains("0 entities"),"Repeated group kill duplicated death handling.");
        }
    }
    public static void PlayerDeath()
    {
        foreach(GameMode mode in Enum.GetValues<GameMode>())
        {
            var session=CommandChecks.Session();session.ApplySettings(new(){GameMode=mode,Difficulty=Difficulty.Peaceful});
            var armor=new ItemStack(Items.Armor(ArmorMaterial.Obsidian,ArmorSlot.Chestplate),1);
            session.Inventory.SetEquipment((int)ArmorSlot.Chestplate,armor);
            session.Inventory.SetSlot(0,new(Items.Apple,7));session.InventoryInteraction.Click(0,false);
            session.Inventory.SetSlot(4,new(Items.Leather,12));session.Crafting.SetSlot(0,new(Items.FromBlock(BlockType.WoodenPlanks),4));
            Check(Execute(session,"/kill @p").Success && session.Player.IsDead && session.Player.HealthValue==0,"Player command failed through Creative/difficulty/armor.");
            Check(Enumerable.Range(0,Inventory.SlotCount).All(i=>session.Inventory[i].IsEmpty) && session.Inventory.Equipped(1).IsEmpty
                && session.InventoryInteraction.Cursor.IsEmpty && session.Crafting[0].IsEmpty,"Command death failed to transfer held/inventory/equipment/crafting items.");
            Check(session.Items.Entities.Sum(i=>i.Count)==24 && session.Items.Entities.Single(i=>i.InstanceId==armor.InstanceId).Durability==armor.Durability,"Player command damaged armor or lost/duplicated death items.");
            int count=session.Items.Entities.Count;session.HandleDeath();Check(session.Items.Entities.Count==count,"Repeated command death duplicated drops.");
            using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(data));
            Check(!loaded.Player.IsDead && loaded.Player.Health==20 && loaded.Items.Entities.Sum(i=>i.Count)==24 && loaded.Inventory.Selected.IsEmpty,"Dead command save did not auto-respawn safely.");
            session.Respawn();Check(session.Player.Health==20 && session.Inventory.Selected.IsEmpty && session.Items.Entities.Count==count,"Command respawn lost drops or granted items.");
        }
        var filtered=CommandChecks.Session();Check(Execute(filtered,"/kill @e type=player").Success && filtered.Player.IsDead,"Player type filter failed.");
        SurvivalChecks.DeathAndRespawn(); // Preserve the pre-existing ordinary death/respawn resource loop.
    }
    public static void TransientEntities()
    {
        foreach(string type in new[]{"item","arrow","falling_block"})
        {
            var session=Populated();session.Inventory.SetSlot(0,new(Items.Apple,3));
            Check(Execute(session,"/kill @e type="+type).Success,"Transient selector failed.");
            Check(session.Items.Entities.Count==(type=="item"?0:1) && session.Projectiles.Arrows.Count==(type=="arrow"?0:1)
                && session.World.Gravity.Active.Count==(type=="falling_block"?0:2) && session.Mobs.Entities.Count==4 && session.Hostiles.Entities.Count==3
                && !session.Player.IsDead && session.Inventory[0].Count==3,"Transient filter affected unrelated state.");
            if(type=="falling_block")
            {
                session.World.Gravity.Update(1);Check(session.World.GetBlock(30,1,30)==BlockType.Air && !session.World.Gravity.Intersects(new(new Vector3(30,1,30),new Vector3(31,8,31))),"Removed falling cubes settled or left stale collision columns.");
            }
            if(type=="item")
            {
                session.Items.Spawn(Items.Apple,3,session.Player.Position+Vector3.Up*.5f,0);
                session.Items.Update(session.Player,session.Inventory,.01f);Check(session.Inventory[0].Count==6,"Clearing drops corrupted later insertion/pickup.");
            }
        }
    }
    public static void AllAndPersistence()
    {
        var fixture=Populated();var states=fixture.Mobs.CaptureAll().Select(m=>m with {Bred=false}).ToArray();
        var session=new GameSession("all",new(fixture.World,"All",fixture.Player.Position,0,0,Mobs:states.Where(m=>m.Species!=MobSpecies.Pig).ToArray(),Pigs:new[]{new PigState(states[0].Position,0,10)},Hostiles:fixture.Hostiles.Capture()));
        session.ApplySettings(new(){GameMode=GameMode.Creative});session.Inventory.SetSlot(0,new(Items.Apple,6));
        session.Items.Spawn(Items.Leather,3,new(25,2,25));session.Projectiles.ShootHostile(new(25,3,25),Vector3.Right*5);
        var result=Execute(session,"/kill @e");
        Check(result.Success && result.Message.Contains("12 entities") && session.Player.IsDead && session.Mobs.Entities.Count==0 && session.Hostiles.Entities.Count==0
            && session.Items.Entities.Count==0 && session.Projectiles.Arrows.Count==0 && session.World.Gravity.Active.Count==0,"All-entity wipe left entities or death loot, or counted incorrectly.");
        Check(session.Mobs.PendingRespawns.Count==4 && session.Mobs.CapturePopulation().Targets.All(t=>t==1),"All-entity kill corrupted natural population accounting.");
        session.HandleDeath();Check(session.Items.Entities.Count==0,"Re-entering death regenerated cleared loot.");
        using var data=new MemoryStream();WorldSerializer.Write(data,session.Capture());data.Position=0;var loaded=new GameSession("loaded",WorldSerializer.Read(data));
        Check(!loaded.Player.IsDead && loaded.Inventory.Selected.IsEmpty && loaded.Items.Entities.Count==0 && loaded.Mobs.Entities.Count==0 && loaded.Hostiles.Entities.Count==0
            && loaded.Projectiles.Arrows.Count==0 && loaded.World.Gravity.Active.Count==0 && loaded.Mobs.PendingRespawns.Count==4,"Kill-all state was duplicated or lost on reload.");
        for(int i=0;i<4;i++)Check(loaded.Mobs.PendingRespawns[i]==session.Mobs.PendingRespawns[i],"Pending natural respawn changed during save/load.");
        Check(Execute(loaded,"/help kill").Success,"Kill help missing.");
    }
}
