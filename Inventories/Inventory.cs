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
    public event Action<ItemId> EquipmentBroken;
    public ItemStack this[int slot] => _slots[slot];
    public void SetSlot(int slot, ItemStack stack) => _slots[slot] = stack;
    public void ConsumeSelected() => _slots[_selected] = Selected.WithCount(Selected.Count - 1);
    public void WearSelected()
    {ItemStack before=Selected;_slots[_selected]=before.Wear();if(before.Durability==1)EquipmentBroken?.Invoke(before.Item);}
    public bool Consume(ItemId item)
    {
        for(int i=0;i<SlotCount;i++)if(!_slots[i].IsEmpty && _slots[i].Item==item){_slots[i]=_slots[i].WithCount(_slots[i].Count-1);return true;}
        return false;
    }
    public bool Wear(Guid instance)
    {
        for(int i=0;i<SlotCount;i++)if(instance!=Guid.Empty && _slots[i].InstanceId==instance){ItemStack before=_slots[i];_slots[i]=before.Wear();if(before.Durability==1)EquipmentBroken?.Invoke(before.Item);return true;}
        return false;
    }

    // Returns the uninserted count. Compatible stacks always take priority over empties.
    public int Add(ItemId item, int count)
    {
        if (!Items.IsDefined(item) || count < 0) throw new ArgumentException("Invalid inventory insertion.");
        if(Items.StackLimit(item)==1)
        {
            for(int i=0;i<SlotCount && count>0;i++)if(_slots[i].IsEmpty){_slots[i]=new ItemStack(item,1);count--;}
            return count;
        }

        for (int i = 0; i < SlotCount && count > 0; i++)
            if (!_slots[i].IsEmpty && _slots[i].Item == item)
                count = Insert(i, item, count);
        for (int i = 0; i < SlotCount && count > 0; i++)
            if (_slots[i].IsEmpty) count = Insert(i, item, count);
        return count;
    }
    public ItemStack Add(ItemStack stack)
    {
        for(int i=0;i<SlotCount && !stack.IsEmpty;i++)if(_slots[i].Compatible(stack))stack=Insert(i,stack);
        for(int i=0;i<SlotCount && !stack.IsEmpty;i++)if(_slots[i].IsEmpty)stack=Insert(i,stack);
        return stack;
    }
    internal ItemStack Insert(int slot,ItemStack stack)
    {
        if(stack.IsEmpty || (!_slots[slot].IsEmpty && !_slots[slot].Compatible(stack)))return stack;
        int accepted=Math.Min(stack.Limit-_slots[slot].Count,stack.Count);
        if(accepted>0)_slots[slot]=_slots[slot].IsEmpty?stack.WithCount(accepted):_slots[slot].WithCount(_slots[slot].Count+accepted);
        return stack.WithCount(stack.Count-accepted);
    }

    internal int Insert(int slot, ItemId item, int count)
    {
        ItemStack existing = _slots[slot];
        if (!existing.IsEmpty && existing.Item != item) return count;
        int accepted = Math.Min(Items.StackLimit(item) - existing.Count, count);
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
        // Pre-inventory saves migrate with exactly the original thirteen stacks.
        foreach (BlockType block in Blocks.Selection[..13])
            inventory._slots[slot++] = new ItemStack(Items.FromBlock(block), ItemStack.Maximum);
        return inventory;
    }

    public static Inventory CreateNewWorld() => new();
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
        bool pickingUp = Cursor.IsEmpty && !Inventory[slot].IsEmpty;
        Inventory.SetSlot(slot, ClickStack(Inventory[slot], right));
        if (pickingUp) _originalSlot = slot;
    }

    // The same cursor and mouse rules serve real crafting inputs and inventory slots.
    public ItemStack ClickStack(ItemStack existing, bool right)
    {
        if (Cursor.IsEmpty)
        {
            if (existing.IsEmpty) return existing;
            int count = right ? Math.Max(1, existing.Count / 2) : existing.Count;
            Cursor = existing.WithCount(count);
            _originalSlot = -1;
            return existing.WithCount(existing.Count - count);
        }
        else if (right)
        {
            if ((existing.IsEmpty || existing.Compatible(Cursor)) && existing.Count < Cursor.Limit)
            {
                existing = existing.IsEmpty ? Cursor.WithCount(1) : existing.WithCount(existing.Count+1);
                Cursor = Cursor.WithCount(Cursor.Count - 1);
            }
        }
        else if (existing.IsEmpty || existing.Compatible(Cursor))
        {
            int accepted = Math.Min(Cursor.Limit - existing.Count, Cursor.Count);
            existing = existing.IsEmpty ? Cursor.WithCount(accepted) : existing.WithCount(existing.Count+accepted);
            Cursor = Cursor.WithCount(Cursor.Count - accepted);
        }
        else
        {
            (existing, Cursor) = (Cursor, existing);
        }
        return existing;
    }

    // Crafting is all-or-nothing: a partial output must never consume a whole recipe.
    public bool TryReceive(ItemStack output)
    {
        if (output.IsEmpty || (!Cursor.IsEmpty && (!Cursor.Compatible(output) || Cursor.Count + output.Count > output.Limit))) return false;
        if (Cursor.IsEmpty) _originalSlot = -1;
        Cursor = Cursor.IsEmpty?output:Cursor.WithCount(Cursor.Count+output.Count);
        return true;
    }

    public bool TryReturnCursor()
    {
        if (Cursor.IsEmpty) return true;
        if (_originalSlot >= 0) Cursor = Inventory.Insert(_originalSlot, Cursor);
        Cursor = Inventory.Add(Cursor);
        if (!Cursor.IsEmpty) return false;
        _originalSlot = -1;
        return true;
    }
    public bool Wear(Guid id) { if(id==Guid.Empty || Cursor.InstanceId!=id)return false;Cursor=Cursor.Wear();return true; }
    public ItemStack TakeStack(ItemStack existing,bool right)
    {
        if(existing.IsEmpty || (!Cursor.IsEmpty && !Cursor.Compatible(existing)))return existing;
        int take=Math.Min(Cursor.IsEmpty?existing.Limit:Cursor.Limit-Cursor.Count,right?Math.Max(1,existing.Count/2):existing.Count);
        if(take<=0)return existing;
        TryReceive(existing.WithCount(take));return existing.WithCount(existing.Count-take);
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
        inventory.SelectedSlot = (int)((inventory.SelectedSlot - steps % Inventory.HotbarSlots + Inventory.HotbarSlots) % Inventory.HotbarSlots);
    }
}
