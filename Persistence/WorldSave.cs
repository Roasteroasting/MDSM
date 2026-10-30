using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public sealed record WorldSave(World World, string Name, Vector3 PlayerPosition, float Yaw, float Pitch,
    Inventory Inventory = null, DroppedItemState[] DroppedItems = null, int Health = Player.MaximumHealth, PigState[] Pigs = null,
    MobState[] Mobs = null, MobPopulationState Population = null); // Mobs contains non-pig species.

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
    public MiningController Mining { get; } = new();
    public CombatController Combat { get; } = new();
    public PassiveMobPopulation Mobs { get; }
    private bool _deathHandled;
    public bool UndroppedDeathItems { get; private set; }

    public GameSession(string id, WorldSave save)
    {
        Id = id;
        Name = save.Name;
        World = save.World;
        Inventory = save.Inventory?.Clone() ?? Inventory.CreateNewWorld();
        InventoryInteraction = new InventoryInteraction(Inventory);
        Items = new DroppedItems(World, save.DroppedItems);
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
            Mobs.CapturePigs(), Mobs.CaptureOthers(), Mobs.CapturePopulation());
    }

    public bool TryOpenCrafting(int width)
    {
        if (Player.IsDead || width is not (2 or 3) || !TryReturnInterfaceItems()) return false;
        Crafting = new CraftingGrid(width);
        return true;
    }

    public bool TryOpenCraftingTable(BlockHit? hit) => hit is BlockHit target && target.Distance >= 0
        && target.Distance <= BlockRaycaster.Reach && World.GetBlock(target.Cell.X, target.Cell.Y, target.Cell.Z) == BlockType.CraftingTable && TryOpenCrafting(3);

    // Inputs and cursor remain owned until inserted or spawned successfully. On an
    // exceptional clearance/entity-limit failure, keep the interface/session open.
    public bool TryReturnInterfaceItems()
    {
        if (!InventoryInteraction.TryReturnCursor() && DropPlacement.TryFind(World, Player, Camera.HorizontalForward, !Player.IsDead, out Vector3 cursorPosition))
            InventoryInteraction.TryDropCursor(Items, cursorPosition, DroppedItems.DeliberatePickupDelay);
        for (int i = 0; i < Crafting.SlotCount; i++)
        {
            ItemStack stack = Crafting[i]; if (stack.IsEmpty) continue;
            int remaining = Inventory.Add(stack.Item, stack.Count);
            Crafting.SetSlot(i, stack.WithCount(remaining));
            if (remaining > 0 && DropPlacement.TryFind(World, Player, Camera.HorizontalForward, !Player.IsDead, out Vector3 position)
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
