using MDSM.Inventories;

namespace MDSM.Gameplay;

public static class ItemUse
{
    public static bool TryConsume(Player player, Inventory inventory, bool rightDown, bool previousRightDown)
    {
        if (!rightDown || previousRightDown || player.IsDead || player.Health == Player.MaximumHealth
            || inventory.Selected.IsEmpty) return false;
        int healing = inventory.Selected.Item==Items.RawPorkchop?2:inventory.Selected.Item==Items.CookedPorkchop?8:inventory.Selected.Item==Items.Apple?4:0;
        if(healing==0)return false;
        player.RestoreHealth(healing);
        inventory.ConsumeSelected();
        return true;
    }
}
