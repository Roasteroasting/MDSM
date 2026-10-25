using System;

using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Inventories;

public sealed class Inventory
{
    public const int HotbarSlots = 9;
    public const int SlotCount = 36;
    private readonly ItemStack[] _slots = new ItemStack[SlotCount];
    private int _selected;
    public int SelectedSlot
    {
        get => _selected;
        set => _selected = value >= 0 && value < HotbarSlots ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
    public ItemStack Selected => _slots[_selected];
    public ItemStack this[int slot] => _slots[slot];
    public void SetSlot(int slot, ItemStack stack) => _slots[slot] = stack;
    public void ConsumeSelected() => _slots[_selected] = Selected.WithCount(Selected.Count - 1);

    // Returns the uninserted count. Compatible stacks always take priority over empties.
    public int Add(ItemId item, int count)
    {
        if (!Items.IsDefined(item) || count < 0) throw new ArgumentException("Invalid inventory insertion.");
        for (int i = 0; i < SlotCount && count > 0; i++)
            if (!_slots[i].IsEmpty && _slots[i].Item == item)
                count = Insert(i, item, count);
        for (int i = 0; i < SlotCount && count > 0; i++)
            if (_slots[i].IsEmpty) count = Insert(i, item, count);
        return count;
    }

    internal int Insert(int slot, ItemId item, int count)
    {
        ItemStack existing = _slots[slot];
        if (!existing.IsEmpty && existing.Item != item) return count;
        int accepted = Math.Min(ItemStack.Maximum - existing.Count, count);
        if (accepted > 0) _slots[slot] = new ItemStack(item, existing.Count + accepted);
        return count - accepted;
    }

    public Inventory Clone()
    {
        var copy = new Inventory { SelectedSlot = SelectedSlot };
        _slots.CopyTo(copy._slots, 0);
        return copy;
    }

    public static Inventory CreateStarter()
    {
        var inventory = new Inventory();
        int slot = 0;
        foreach (BlockType block in Blocks.Selection)
            inventory._slots[slot++] = new ItemStack(Items.FromBlock(block), ItemStack.Maximum);
        return inventory;
    }
}

// No graphics or input-device dependency: mouse clicks are atomic stack operations.
public sealed class InventoryInteraction(Inventory inventory)
{
    public Inventory Inventory { get; } = inventory;
    public ItemStack Cursor { get; private set; }
    private int _originalSlot = -1;

    public void Click(int slot, bool right)
    {
        if (slot < 0 || slot >= Inventory.SlotCount) return; // Outside clicks never delete items.
        ItemStack existing = Inventory[slot];
        if (Cursor.IsEmpty)
        {
            if (existing.IsEmpty) return;
            int count = right ? Math.Max(1, existing.Count / 2) : existing.Count;
            Cursor = new ItemStack(existing.Item, count);
            Inventory.SetSlot(slot, existing.WithCount(existing.Count - count));
            _originalSlot = slot;
        }
        else if (right)
        {
            int remainder = Inventory.Insert(slot, Cursor.Item, 1);
            if (remainder == 0) Cursor = Cursor.WithCount(Cursor.Count - 1);
        }
        else if (existing.IsEmpty || existing.Item == Cursor.Item)
            Cursor = Cursor.WithCount(Inventory.Insert(slot, Cursor.Item, Cursor.Count));
        else
        {
            Inventory.SetSlot(slot, Cursor);
            Cursor = existing;
        }
    }

    public bool TryReturnCursor()
    {
        if (Cursor.IsEmpty) return true;
        int remaining = Cursor.Count;
        if (_originalSlot >= 0) remaining = Inventory.Insert(_originalSlot, Cursor.Item, remaining);
        remaining = Inventory.Add(Cursor.Item, remaining);
        Cursor = Cursor.WithCount(remaining);
        if (!Cursor.IsEmpty) return false;
        _originalSlot = -1;
        return true;
    }

    public bool TryDropCursor(DroppedItems items, Microsoft.Xna.Framework.Vector3 position, float delay)
    {
        if (Cursor.IsEmpty || !items.TrySpawn(Cursor, position, delay)) return false;
        Cursor = default;
        _originalSlot = -1;
        return true;
    }
}

public sealed class HotbarScroll
{
    private long _remainder;
    public void Reset() => _remainder = 0;
    public void Apply(Inventory inventory, long delta)
    {
        long total = _remainder + delta;
        long steps = total / 120;
        _remainder = total % 120;
        inventory.SelectedSlot = (int)((inventory.SelectedSlot + steps % Inventory.HotbarSlots + Inventory.HotbarSlots) % Inventory.HotbarSlots);
    }
}
