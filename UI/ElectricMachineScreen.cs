using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Worlds;
using MDSM.Inventories;

namespace MDSM.UI;

internal sealed partial class InventoryScreen
{
    private void DrawElectricMachine(BlockContainer machine,Viewport viewport)
    {
        bool furnace=machine.Block==BlockType.ElectricFurnace;
        int left=furnace?49:29,width=furnace?78:116;
        void Label(string text,int y,Color color)
        {var r=InventoryLayout.At(viewport,false,left,y,width,8);Text(text,r.X,r.Y,_menuScale,color);}
        if(!furnace)
        {
            for(int direction=-1;direction<=1;direction+=2)
            {
                var button=InventoryLayout.ElectricRecipeButton(viewport,direction);
                MdsmUiStyle.Frame(_sprites,_pixel,button,_menuScale);
                Text(direction<0?"<":">",button.X+3*_menuScale,button.Y+2*_menuScale,_menuScale,MdsmUiStyle.Ink);
            }
            var name=InventoryLayout.At(viewport,false,26,18,124,8);
            string product=AdvancedProcessing.ProductName(machine.Block,machine.SelectedRecipe);
            Text(PixelFont.Fit(product,20),name.X,name.Y,_menuScale,MdsmUiStyle.Ink);
            var recipe=machine.PreviewRecipe;
            string material=machine.Block==BlockType.ElectricGlassKiln?$"{recipe.Amount} GLASS > {recipe.Quantity}":
                machine.SelectedRecipe==4?"CARROT+POTATO+POT":machine.SelectedRecipe==0?"1 RAW PORK":machine.SelectedRecipe==1?"1 POTATO":machine.SelectedRecipe==2?"1 CARROT":"1 EGG";
            Label(material,28,MdsmUiStyle.Ink);
            Label("RATED "+ElectricalBalance.Power(machine.NominalWatts),38,MdsmUiStyle.Light);
            Label("NEED "+ElectricalBalance.Power(machine.RequestedWatts),48,MdsmUiStyle.Ink);
            Label("HAVE "+ElectricalBalance.Power(machine.AllocatedWatts),58,MdsmUiStyle.Accent);

        }
        else
        {
            Label("RATED "+ElectricalBalance.Power(machine.NominalWatts),18,MdsmUiStyle.Light);
            Label("NEED "+ElectricalBalance.Power(machine.RequestedWatts),29,MdsmUiStyle.Ink);
            Label("HAVE "+ElectricalBalance.Power(machine.AllocatedWatts),40,MdsmUiStyle.Accent);
            Label($"{100*machine.Progress/Math.Max(.001,machine.ProcessingSeconds):0}%",59,MdsmUiStyle.Ink);
        }
        int y=furnace?52:69;
        Fill(InventoryLayout.At(viewport,false,left,y,width,3),MdsmUiStyle.Inset);
        int filled=(int)Math.Clamp(width*machine.Progress/Math.Max(.001,machine.ProcessingSeconds),0,width);
        if(filled>0)Fill(InventoryLayout.At(viewport,false,left,y,filled,3),MdsmUiStyle.Accent);
    }
}
