using System;
using System.IO;
using Microsoft.Xna.Framework;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Persistence;

namespace MDSM.Testing;

internal static class RunningBootsChecks
{
    private static void Check(bool condition,string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Boots()
    {
        var grid=new CraftingGrid(3);
        ItemId[] ingredients={Items.Feather,Items.Leather,Items.IronIngot};
        for(int row=0;row<3;row++)
        {
            grid.SetSlot(row*3,new(ingredients[row],1));
            grid.SetSlot(row*3+2,new(ingredients[row],1));
        }
        Check(grid.Preview.Item==Items.RunningBoots,"Running Boots recipe did not match.");
        grid.SetSlot(4,new(Items.Leather,1));
        Check(grid.Preview.IsEmpty,"Running Boots recipe accepted a filled center column.");
        grid.SetSlot(4,default);
        var inventory=new Inventory();
        var cursor=new InventoryInteraction(inventory);
        Check(grid.TryCraft(cursor) && cursor.Cursor.Item==Items.RunningBoots && grid.Preview.IsEmpty,"Crafting did not consume exactly one recipe.");
        int bootSlot=(int)ArmorSlot.Boots;
        inventory.SetEquipment(bootSlot,cursor.Cursor);
        var iron=Items.Get(Items.Armor(ArmorMaterial.Iron,ArmorSlot.Boots));
        Check(inventory.ArmorPoints==iron.ArmorPoints && cursor.Cursor.Durability==iron.Durability && cursor.Cursor.Limit==1,
            "Running Boots did not inherit iron boots protection, durability and stack size.");

        var world=WaterChecks.Floor();
        VerifySprintJumps(inventory,world);
        var player=new Player(new(20.5f,1,20.5f),equipment:inventory);
        float Move(bool boostedFlight=false)
        {
            for (int i=0;i<30;i++) player.Update(world,Vector3.UnitX,false,1f/120,fastFlight:boostedFlight);
            float start=player.Position.X;
            for (int i=0;i<120;i++) player.Update(world,Vector3.UnitX,false,1f/120,fastFlight:boostedFlight);
            return player.Position.X-start;
        }
        bool Matches(float actual,float expected)=>Math.Abs(actual-expected)<.005f;
        Check(player.BootSprintActive && Matches(Move(),Player.MoveSpeed*1.3f),"Survival sprint speed was incorrect.");
        player.ToggleBootSprint();
        Check(!player.BootSprintActive && Matches(Move(),Player.MoveSpeed),"Left Alt's toggle did not restore walk speed.");
        player.ToggleBootSprint();
        player.Settings.GameMode=GameMode.Creative;
        Check(!player.BootSprintActive && Matches(Move(),Player.MoveSpeed) && Matches(Move(true),Player.MoveSpeed*3),
            "Running Boots changed Creative flight.");
        player.SetFlightEnabled(false);
        Check(player.BootSprintActive && Matches(Move(),Player.MoveSpeed*1.3f),"Creative walking did not use Running Boots.");
        var stack=inventory.Equipped(bootSlot);
        using var data=new MemoryStream();
        WorldSerializer.Write(data,new(world,"Boots",player.Position,0,0,inventory));
        data.Position=0;
        Check(WorldSerializer.Read(data).Inventory.Equipped(bootSlot)==stack,"Equipment did not round-trip through the save.");
        inventory.SetEquipment(bootSlot,default);
        Check(!player.BootSprintActive && Matches(Move(),Player.MoveSpeed),"Unequipping boots retained the speed boost.");
    }

    private static void VerifySprintJumps(Inventory inventory,Worlds.World world)
    {
        var player=new Player(new(20.5f,1,20.5f),equipment:inventory);
        const float dt=1f/120;
        player.Update(world,Vector3.Zero,false,dt);
        Check(player.Grounded,"Sprint jump fixture was not grounded.");
        Vector3 direction=Vector3.Normalize(Vector3.UnitX+Vector3.UnitZ);
        float expected=Player.MoveSpeed*Player.SprintMultiplier*dt;
        int takeoffs=0;
        for(int tick=0;tick<360;tick++)
        {
            bool grounded=player.Grounded;
            Vector3 before=player.Position;
            player.Update(world,direction,true,dt);
            Vector3 travel=player.Position-before;
            if(grounded && travel.Y>0) takeoffs++;
            travel.Y=0;
            Check(Vector3.Distance(travel,direction*expected)<.0001f,
                $"Sprint jump lost speed on step {tick}: expected {expected/dt:F2}, got {travel.Length()/dt:F2} blocks/s.");
        }
        Check(takeoffs>=4,"The sprint-jump fixture did not exercise repeated landings and takeoffs.");
    }
}
