using Microsoft.Xna.Framework;
using System;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public sealed record WorldSave(World World, string Name, Vector3 PlayerPosition, float Yaw, float Pitch,
    Inventory Inventory = null, DroppedItemState[] DroppedItems = null, int Health = Player.MaximumHealth, PigState[] Pigs = null,
    MobState[] Mobs = null, MobPopulationState Population = null,ArrowState[] Arrows=null); // Mobs contains non-pig species.

public sealed record WorldMetadata(string Name, int Seed, Vector3 PlayerPosition, float Yaw, float Pitch,
    WorldSize Size = WorldSize.Normal, int Version = 1, int StoredHeight = World.Height)
{
    public int StoredBlockCount => checked(Size.Width() * StoredHeight * Size.Width());
    public bool ExpandsHeight => StoredHeight != World.Height;
}

// A session has no GPU resources. The renderer is replaced/disposed by Game1.
public sealed class GameSession
{
    public string Id { get; }
    public string Name { get; }
    public World World { get; }
    public Player Player { get; }
    public Camera Camera { get; }
    public Inventory Inventory { get; }
    public InventoryInteraction InventoryInteraction { get; }
    public CraftingGrid Crafting { get; private set; } = new(2);
    public DroppedItems Items { get; }
    public Projectiles Projectiles { get; }
    public BowController Bow { get; }=new();
    public ViewBobbing Bobbing { get; }=new();
    internal Action<Guid> ProjectileWear { get; }
    public Point3? ContainerCell { get; private set; }
    public BlockContainer ActiveContainer=>ContainerCell.HasValue?World.Containers.Get(ContainerCell.Value):null;
    public MiningController Mining { get; } = new();
    public CombatController Combat { get; } = new();
    public PassiveMobPopulation Mobs { get; }
    private bool _deathHandled;
    public bool UndroppedDeathItems { get; private set; }
    private string _equipmentNotice;
    public string TakeEquipmentNotice(){string notice=_equipmentNotice;_equipmentNotice=null;return notice;}

    public GameSession(string id, WorldSave save)
    {
        Id = id;
        ProjectileWear=WearEquipment;
        Name = save.Name;
        World = save.World;
        Inventory = save.Inventory?.Clone() ?? Inventory.CreateNewWorld();
        Inventory.EquipmentBroken+=item=>_equipmentNotice=MDSM.Inventories.Items.Name(item)+" BROKE";
        InventoryInteraction = new InventoryInteraction(Inventory);
        Items = new DroppedItems(World, save.DroppedItems);
        Projectiles=new Projectiles(World,save.Arrows);
        Mobs = new PassiveMobPopulation(World, save.Pigs, save.Mobs, save.Population);
        Player = new Player(save.PlayerPosition, save.Health);
        if (Player.IsDead) Player.Respawn(World); // Saved drops already exist; never drop again on load.
        Camera = new Camera { Position = Player.EyePosition };
        Camera.RestoreOrientation(save.Yaw, save.Pitch);
    }

    public WorldSave Capture()
    {
        if (!TryReturnInterfaceItems())
            throw new System.InvalidOperationException("Make room for held items and crafting ingredients before saving.");
        return new(World, Name, Player.Position, Camera.Yaw, Camera.Pitch, Inventory.Clone(), Items.Capture(), Player.Health,
            Mobs.CapturePigs(), Mobs.CaptureOthers(), Mobs.CapturePopulation(),Projectiles.Capture());
    }

    public bool TryOpenCrafting(int width)
    {
        if (Player.IsDead || width is not (2 or 3) || !TryReturnInterfaceItems()) return false;
        ContainerCell=null;
        Crafting = new CraftingGrid(width);
        return true;
    }

    public bool TryOpenCraftingTable(BlockHit? hit) => hit is BlockHit target && target.Distance >= 0
        && target.Distance <= BlockRaycaster.Reach && World.GetBlock(target.Cell.X, target.Cell.Y, target.Cell.Z) == BlockType.CraftingTable && TryOpenCrafting(3);

    public bool TryOpenContainer(BlockHit hit)
    {
        if(Player.IsDead || !float.IsFinite(hit.Distance) || hit.Distance<0 || hit.Distance>BlockRaycaster.Reach
            || !BlockEntities.HasState(World.GetBlock(hit.Cell.X,hit.Cell.Y,hit.Cell.Z)) || !TryReturnInterfaceItems())return false;
        ContainerCell=hit.Cell;World.Containers.Get(hit.Cell);return true;
    }
    public void CloseInterface()=>ContainerCell=null;
    public void WearEquipment(Guid id)
    {
        if(id==Guid.Empty)return;
        if(Inventory.Wear(id) || InventoryInteraction.Wear(id) || Items.Wear(id))return;
        for(int i=0;i<Crafting.SlotCount;i++)if(Crafting[i].InstanceId==id){Crafting.SetSlot(i,Crafting[i].Wear());return;}
        foreach(var entry in World.Containers.Entries.Values)if(entry.Wear(id))return;
    }

    // Inputs and cursor remain owned until inserted or spawned successfully. On an
    // exceptional clearance/entity-limit failure, keep the interface/session open.
    public bool TryReturnInterfaceItems()
    {
        if (!InventoryInteraction.TryReturnCursor() && DropPlacement.TryFind(World, Player, Camera.HorizontalForward, !Player.IsDead, out Vector3 cursorPosition))
            InventoryInteraction.TryDropCursor(Items, cursorPosition, DroppedItems.DeliberatePickupDelay);
        for (int i = 0; i < Crafting.SlotCount; i++)
        {
            ItemStack stack = Crafting[i]; if (stack.IsEmpty) continue;
            ItemStack remaining = Inventory.Add(stack);
            Crafting.SetSlot(i, remaining);
            if (!remaining.IsEmpty && DropPlacement.TryFind(World, Player, Camera.HorizontalForward, !Player.IsDead, out Vector3 position)
                && Items.TrySpawn(Crafting[i], position, DroppedItems.DeliberatePickupDelay)) Crafting.SetSlot(i, default);
        }
        if (!InventoryInteraction.Cursor.IsEmpty) return false;
        for (int i = 0; i < Crafting.SlotCount; i++) if (!Crafting[i].IsEmpty) return false;
        return true;
    }

    public bool HandleDeath()
    {
        if (!Player.IsDead || _deathHandled) return false;
        _deathHandled = true;
        ContainerCell=null;Bow.Cancel();
        UndroppedDeathItems = false;
        Mining.Reset(); Player.StopMovement();
        bool hasPosition = DropPlacement.TryFind(World, Player, Camera.HorizontalForward, false, out Vector3 position);
        if (!InventoryInteraction.Cursor.IsEmpty && (!hasPosition
            || !InventoryInteraction.TryDropCursor(Items, position, DroppedItems.DeliberatePickupDelay))) UndroppedDeathItems = true;
        for (int slot = 0; slot < Crafting.SlotCount; slot++)
        {
            ItemStack stack = Crafting[slot]; if (stack.IsEmpty) continue;
            if (hasPosition && Items.TrySpawn(stack, position, DroppedItems.DeliberatePickupDelay)) Crafting.SetSlot(slot, default);
            else UndroppedDeathItems = true;
        }
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            ItemStack stack = Inventory[slot];
            if (stack.IsEmpty) continue;
            if (hasPosition && Items.TrySpawn(stack, position, DroppedItems.DeliberatePickupDelay)) Inventory.SetSlot(slot, default);
            else UndroppedDeathItems = true; // Explicit capacity/clearance failure retains ownership.
        }
        Camera.Position = Player.EyePosition;
        return true;
    }

    public void Respawn()
    {
        Player.Respawn(World); Mining.Reset(); _deathHandled = false;
        Camera.Position = Player.EyePosition; Camera.RestoreOrientation(0, 0);
    }

    public bool DropCursor()
    {
        return !Player.IsDead && DropPlacement.TryFind(World, Player, Camera.HorizontalForward, true, out Vector3 position)
            && InventoryInteraction.TryDropCursor(Items, position, DroppedItems.DeliberatePickupDelay);
    }
}
