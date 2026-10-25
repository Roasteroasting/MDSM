using Microsoft.Xna.Framework;

using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Persistence;

public sealed record WorldSave(World World, string Name, Vector3 PlayerPosition, float Yaw, float Pitch,
    Inventory Inventory = null, DroppedItemState[] DroppedItems = null, int Health = Player.MaximumHealth);

public sealed record WorldMetadata(string Name, int Seed, Vector3 PlayerPosition, float Yaw, float Pitch,
    WorldSize Size = WorldSize.Normal, int Version = 1);

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
    public DroppedItems Items { get; }
    public MiningController Mining { get; } = new();
    private bool _deathHandled;
    public bool UndroppedDeathItems { get; private set; }

    public GameSession(string id, WorldSave save)
    {
        Id = id;
        Name = save.Name;
        World = save.World;
        Inventory = save.Inventory?.Clone() ?? Inventory.CreateStarter();
        InventoryInteraction = new InventoryInteraction(Inventory);
        Items = new DroppedItems(World, save.DroppedItems);
        Player = new Player(save.PlayerPosition, save.Health);
        if (Player.IsDead) Player.Respawn(World); // Saved drops already exist; never drop again on load.
        Camera = new Camera { Position = Player.EyePosition };
        Camera.RestoreOrientation(save.Yaw, save.Pitch);
    }

    public WorldSave Capture()
    {
        if (!InventoryInteraction.Cursor.IsEmpty)
            throw new System.InvalidOperationException("Return cursor-held items before saving.");
        return new(World, Name, Player.Position, Camera.Yaw, Camera.Pitch, Inventory.Clone(), Items.Capture(), Player.Health);
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
