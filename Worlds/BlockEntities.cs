using System;
using System.Collections.Generic;
using System.IO;
using MDSM.Inventories;

namespace MDSM.Worlds;

public readonly record struct SmeltingRecipe(ItemId Input,ItemId Output);
public static class Smelting
{
    public const double Seconds=10;
    public const double MaximumBurnSeconds=80;
    public static readonly SmeltingRecipe[] Recipes={new(Items.FromBlock(BlockType.Sand),Items.FromBlock(BlockType.Glass)),
        new(Items.FromBlock(BlockType.IronOre),Items.IronIngot),new(Items.FromBlock(BlockType.WoodLog),Items.Charcoal)};
    public static ItemId Result(ItemId input){foreach(var recipe in Recipes)if(recipe.Input==input)return recipe.Output;return default;}
    public static bool CoalLike(ItemId item)=>item==Items.Coal || item==Items.Charcoal;
    public static double Fuel(ItemId item)=>CoalLike(item)?MaximumBurnSeconds:Items.TryGetBlock(item,out var block)&&block is BlockType.WoodLog or BlockType.WoodenPlanks?15:0;
}

public sealed record BlockEntityState(Point3 Cell,BlockType Block,ItemStack[] Slots,double Progress=0,double Burn=0,double BurnTotal=0);
public sealed class BlockContainer
{
    private readonly ItemStack[] _slots;
    private readonly Action _wake;
    public Point3 Cell { get; }
    public BlockType Block { get; }
    public int SlotCount=>_slots.Length;
    public ItemStack this[int slot]=>_slots[slot];
    public double Progress { get; private set; }
    public double Burn { get; private set; }
    public double BurnTotal { get; private set; }
    public BlockContainer(Point3 cell,BlockType block,Action wake)
    {Cell=cell;Block=block;_slots=new ItemStack[block==BlockType.Chest?27:3];_wake=wake;}
    public void SetSlot(int slot,ItemStack stack)
    {
        if(Block==BlockType.Furnace && slot==0 && (stack.IsEmpty || stack.Item!=_slots[0].Item))Progress=0;
        _slots[slot]=stack;_wake?.Invoke();
    }
    public bool Empty { get {foreach(var stack in _slots)if(!stack.IsEmpty)return false;return true;} }
    public bool Active=>Block==BlockType.Furnace && (Burn>0 || CanSmelt && Smelting.Fuel(_slots[1].Item)>0);
    private bool CanSmelt => !_slots[0].IsEmpty && Smelting.Result(_slots[0].Item).Value!=0
        && (_slots[2].IsEmpty || _slots[2].Item==Smelting.Result(_slots[0].Item) && _slots[2].Count<_slots[2].Limit);
    public void Click(int slot,InventoryInteraction interaction,bool right)
    {
        if(Block==BlockType.Furnace)
        {
            if(slot==2){SetSlot(2,interaction.TakeStack(_slots[2],right));return;}
            if(!interaction.Cursor.IsEmpty && (slot==0?Smelting.Result(interaction.Cursor.Item).Value==0:Smelting.Fuel(interaction.Cursor.Item)<=0))return;
        }
        SetSlot(slot,interaction.ClickStack(_slots[slot],right));
    }
    internal void Update(double elapsed)
    {
        if(Block!=BlockType.Furnace || !double.IsFinite(elapsed) || elapsed<=0)return;
        double time=elapsed;
        // A stack has at most 64 inputs; even a very large time step has bounded transitions.
        for(int eventCount=0;eventCount<256 && time>1e-9;eventCount++)
        {
            bool ready=CanSmelt;
            if(Burn<=1e-8)
            {
                Burn=0;if(!ready || _slots[1].IsEmpty || Smelting.Fuel(_slots[1].Item)<=0)break;
                Burn=BurnTotal=Smelting.Fuel(_slots[1].Item);_slots[1]=_slots[1].WithCount(_slots[1].Count-1);
            }
            double step=Math.Min(time,Burn);if(ready)step=Math.Min(step,Smelting.Seconds-Progress);
            Burn=Math.Max(0,Burn-step);time-=step;
            if(!ready)continue;
            Progress+=step;
            if(Progress+1e-8>=Smelting.Seconds)
            {
                ItemId result=Smelting.Result(_slots[0].Item);
                _slots[0]=_slots[0].WithCount(_slots[0].Count-1);
                _slots[2]=new(result,_slots[2].Count+1);Progress=0;
            }
        }
    }
    public bool Wear(Guid id)
    {for(int i=0;i<_slots.Length;i++)if(id!=Guid.Empty && _slots[i].InstanceId==id){_slots[i]=_slots[i].Wear();return true;}return false;}
    public BlockEntityState Capture()=>new(Cell,Block,(ItemStack[])_slots.Clone(),Progress,Burn,BurnTotal);
    internal void Restore(BlockEntityState state)
    {
        if(state.Slots.Length!=SlotCount || !double.IsFinite(state.Progress) || state.Progress<0 || state.Progress>=Smelting.Seconds
            || !double.IsFinite(state.Burn) || state.Burn<0 || state.Burn>Smelting.MaximumBurnSeconds || !double.IsFinite(state.BurnTotal) || state.BurnTotal<state.Burn || state.BurnTotal>Smelting.MaximumBurnSeconds)
            throw new InvalidDataException("Invalid block container state.");
        if(Block==BlockType.Chest && (state.Progress!=0 || state.Burn!=0 || state.BurnTotal!=0))throw new InvalidDataException("Chest contains furnace timers.");
        if(Block==BlockType.Furnace && ((!state.Slots[0].IsEmpty && Smelting.Result(state.Slots[0].Item).Value==0)
            || (!state.Slots[1].IsEmpty && Smelting.Fuel(state.Slots[1].Item)<=0)
            || (!state.Slots[2].IsEmpty && !Array.Exists(Smelting.Recipes,r=>r.Output==state.Slots[2].Item))
            || state.Progress>0 && state.Slots[0].IsEmpty))throw new InvalidDataException("Invalid furnace contents/progress.");
        state.Slots.CopyTo(_slots,0);Progress=state.Progress;Burn=state.Burn;BurnTotal=state.BurnTotal;_wake?.Invoke();
    }
}

public sealed class BlockEntities(World world)
{
    public const int Maximum=65536;
    private readonly Dictionary<Point3,BlockContainer> _entries=new();
    private readonly HashSet<Point3> _active=new();
    private readonly List<Point3> _sleep=new();
    public IReadOnlyDictionary<Point3,BlockContainer> Entries=>_entries;
    public int LastProcessed { get; private set; }
    public static bool HasState(BlockType block)=>block is BlockType.Chest or BlockType.Furnace;
    public BlockContainer Get(Point3 cell)
    {
        BlockType block=world.GetBlock(cell.X,cell.Y,cell.Z);if(!HasState(block))return null;
        if(_entries.TryGetValue(cell,out var entry))return entry;
        if(_entries.Count>=Maximum)throw new InvalidOperationException("Block container limit reached.");
        entry=new(cell,block,()=>{if(block==BlockType.Furnace)_active.Add(cell);});_entries.Add(cell,entry);return entry;
    }
    internal bool CanChange(Point3 cell,BlockType next)
        => (!_entries.TryGetValue(cell,out var existing) || existing.Empty)
        && (!HasState(next) || _entries.ContainsKey(cell) || _entries.Count<Maximum);
    internal void Changed(Point3 cell,BlockType next)
    {
        _entries.Remove(cell);_active.Remove(cell);if(HasState(next))Get(cell);
    }
    public BlockContainer Detach(Point3 cell)
    {if(!_entries.Remove(cell,out var entry))return null;_active.Remove(cell);return entry;}
    public void Reattach(BlockContainer entry)
    {if(entry==null)return;_entries.Add(entry.Cell,entry);if(entry.Active)_active.Add(entry.Cell);}
    public void Update(double elapsed)
    {
        LastProcessed=0;_sleep.Clear();
        foreach(Point3 cell in _active)
        {
            if(!_entries.TryGetValue(cell,out var entry)){_sleep.Add(cell);continue;}
            entry.Update(elapsed);LastProcessed++;if(!entry.Active)_sleep.Add(cell);
        }
        foreach(Point3 cell in _sleep)_active.Remove(cell);
    }
    public BlockEntityState[] Capture()
    {
        var result=new BlockEntityState[_entries.Count];int i=0;foreach(var entry in _entries.Values)result[i++]=entry.Capture();
        Array.Sort(result,(a,b)=>a.Cell.Y!=b.Cell.Y?a.Cell.Y.CompareTo(b.Cell.Y):a.Cell.Z!=b.Cell.Z?a.Cell.Z.CompareTo(b.Cell.Z):a.Cell.X.CompareTo(b.Cell.X));return result;
    }
    public void Restore(BlockEntityState[] states)
    {
        if(states.Length>Maximum)throw new InvalidDataException("Too many block entities.");
        foreach(var state in states)
        {
            if(!world.Contains(state.Cell.X,state.Cell.Y,state.Cell.Z) || !HasState(state.Block)
                || world.GetBlock(state.Cell.X,state.Cell.Y,state.Cell.Z)!=state.Block || _entries.ContainsKey(state.Cell))throw new InvalidDataException("Invalid/duplicate container position.");
            Get(state.Cell).Restore(state);
        }
    }
}
