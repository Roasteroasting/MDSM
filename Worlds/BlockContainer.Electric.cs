using System;
using System.IO;
using MDSM.Inventories;

namespace MDSM.Worlds;

public sealed partial class BlockContainer : IElectricalConsumer
{
    public int SelectedRecipe {get;private set;}
    private ItemId _workItem;
    public double AllocatedWatts {get;private set;}
    public double NominalWatts=>AdvancedProcessing.Machine(Block)?AdvancedProcessing.Watts(Block):0;
    public double RequestedWatts=>AdvancedProcessing.Machine(Block)&&TryJob(out _,out _)?NominalWatts:0;
    public ProcessingRecipe PreviewRecipe
    {
        get
        {
            ItemId item=Block==BlockType.ElectricGlassKiln?Items.FromBlock(BlockType.Glass):default;
            for(int i=0;i<AdvancedProcessing.Inputs(Block);i++)if(!_slots[i].IsEmpty){item=_slots[i].Item;break;}
            return AdvancedProcessing.Recipe(Block,SelectedRecipe,item);
        }
    }
    public double ProcessingSeconds=>TryJob(out var recipe,out _)?recipe.Seconds:
        Block==BlockType.ElectricFurnace?AdvancedProcessing.FurnaceSeconds:AdvancedProcessing.Recipe(Block,SelectedRecipe,Items.FromBlock(BlockType.Glass)).Seconds;
    public bool SelectRecipe(int choice)
    {
        if(!AdvancedProcessing.Machine(Block)||choice<0||choice>=AdvancedProcessing.Choices(Block)||Progress>1e-8)return false;
        SelectedRecipe=choice;_workItem=default;_wake?.Invoke();return true;
    }
    private int CountInput(ItemId item)
    {
        int count=0;for(int i=0;i<AdvancedProcessing.Inputs(Block);i++)if(_slots[i].Item==item)count+=_slots[i].Count;return count;
    }
    private bool OutputFits(ItemId item,int amount)
    {
        if(item==default)return false;
        for(int i=AdvancedProcessing.Inputs(Block);i<SlotCount;i++)
        {var s=_slots[i];if(s.IsEmpty)amount-=Items.StackLimit(item);else if(s.Item==item)amount-=s.Limit-s.Count;}
        return amount<=0;
    }
    private bool TryJob(out ProcessingRecipe recipe,out int slot)
    {
        recipe=default;slot=-1;if(!AdvancedProcessing.Machine(Block))return false;
        int inputs=AdvancedProcessing.Inputs(Block);
        for(int i=0;i<inputs;i++)
        {
            if(_slots[i].IsEmpty||Progress>0&&_workItem!=default&&_slots[i].Item!=_workItem)continue;
            var candidate=AdvancedProcessing.Recipe(Block,SelectedRecipe,_slots[i].Item);
            if(candidate.Output==default||candidate.Input!=_slots[i].Item||CountInput(candidate.Input)<candidate.Amount
                ||candidate.Second!=default&&CountInput(candidate.Second)<1||candidate.Vessel!=default&&CountInput(candidate.Vessel)<1
                ||!OutputFits(candidate.Output,candidate.Quantity))continue;
            recipe=candidate;slot=i;return true;
        }
        return false;
    }
    private void Consume(ItemId item,int amount)
    {
        for(int i=0;i<AdvancedProcessing.Inputs(Block)&&amount>0;i++)if(_slots[i].Item==item)
        {int take=Math.Min(amount,_slots[i].Count);_slots[i]=_slots[i].WithCount(_slots[i].Count-take);amount-=take;}
    }
    private void Produce(ItemId item,int amount)
    {
        for(int pass=0;pass<2;pass++)for(int i=AdvancedProcessing.Inputs(Block);i<SlotCount&&amount>0;i++)
        {
            var stack=_slots[i];if(pass==0?(stack.IsEmpty||stack.Item!=item):!stack.IsEmpty)continue;
            int add=Math.Min(amount,Items.StackLimit(item)-stack.Count);_slots[i]=new(item,stack.Count+add);amount-=add;
        }
    }
    private void ElectricSlotChanged(int slot)
    {
        if(AdvancedProcessing.Machine(Block)&&slot<AdvancedProcessing.Inputs(Block)&&Progress>0)
        {
            var job=AdvancedProcessing.Recipe(Block,SelectedRecipe,_workItem);
            if(CountInput(job.Input)<job.Amount||job.Second!=default&&CountInput(job.Second)<1||job.Vessel!=default&&CountInput(job.Vessel)<1)
            {Progress=0;_workItem=default;}
        }
    }
    public void ReceivePower(double watts,double elapsedSeconds)
    {
        AllocatedWatts=double.IsFinite(watts)?Math.Clamp(watts,0,NominalWatts):0;
        if(!double.IsFinite(elapsedSeconds)||elapsedSeconds<=0||AllocatedWatts<=0)return;
        double work=elapsedSeconds*AllocatedWatts/NominalWatts;
        for(int events=0;events<1024&&work>1e-9&&TryJob(out var recipe,out _);events++)
        {
            _workItem=recipe.Input;double step=Math.Min(work,recipe.Seconds-Progress);Progress+=step;work-=step;
            if(Progress+1e-8<recipe.Seconds)break;
            Consume(recipe.Input,recipe.Amount);if(recipe.Second!=default)Consume(recipe.Second,1);if(recipe.Vessel!=default)Consume(recipe.Vessel,1);
            Produce(recipe.Output,recipe.Quantity);Progress=0;_workItem=default;_processed?.Invoke();
        }
        _wake?.Invoke();
    }
    private void RestoreElectric(BlockEntityState state)
    {
        if(state.Recipe<0||state.Recipe>=AdvancedProcessing.Choices(Block)||state.Burn!=0||state.BurnTotal!=0)
            throw new InvalidDataException("Invalid electric machine selection/timers.");
        SelectedRecipe=state.Recipe;_workItem=state.WorkItem;
        for(int i=0;i<AdvancedProcessing.Inputs(Block);i++)if(!state.Slots[i].IsEmpty&&!AdvancedProcessing.Input(Block,state.Slots[i].Item))
            throw new InvalidDataException("Invalid electric machine input.");
        for(int i=AdvancedProcessing.Inputs(Block);i<SlotCount;i++)if(!state.Slots[i].IsEmpty)
        {
            ItemId item=state.Slots[i].Item;
            bool valid=Block==BlockType.ElectricFurnace?Smelting.Output(item,BlockType.Furnace):Block==BlockType.ElectricGlassKiln?
                Items.TryGetBlock(item,out var glass)&&(GlassProcessing.IsGlass(glass)&&!GlassProcessing.IsDouble(glass)||Blocks.GlassPane(glass)):
                item==Items.CookedPorkchop||item==Items.BakedPotato||item==Items.RoastedCarrot||item==Items.FriedEgg||item==Items.VegetableStew;
            if(!valid)throw new InvalidDataException("Invalid electric machine output.");
        }
        if(state.Progress>0)
        {
            var recipe=AdvancedProcessing.Recipe(Block,state.Recipe,state.WorkItem);
            if(state.WorkItem==default||recipe.Output==default||state.Progress>=recipe.Seconds
                ||CountInput(recipe.Input)<recipe.Amount||recipe.Second!=default&&CountInput(recipe.Second)<1||recipe.Vessel!=default&&CountInput(recipe.Vessel)<1)
                throw new InvalidDataException("Electric work has no matching ingredients.");
        }
        else if(state.WorkItem!=default)throw new InvalidDataException("Idle machine retained a work item.");
    }
}
