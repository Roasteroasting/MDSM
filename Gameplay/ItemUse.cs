using MDSM.Inventories;

namespace MDSM.Gameplay;

public static class ItemUse
{
    public static bool TryConsume(Player player, Inventory inventory, bool rightDown, bool previousRightDown)
    {
        if (!rightDown || previousRightDown || player.IsDead || player.Health == Player.MaximumHealth
            || inventory.Selected.IsEmpty || inventory.Selected.Item != Items.RawPorkchop) return false;
        player.RestoreHealth(3);
        inventory.ConsumeSelected();
        return true;
    }
}
