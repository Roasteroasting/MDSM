using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Audio;

namespace MDSM.UI;

internal static class InventoryLayout
{
    private static float Scale(Viewport v,bool creative,int rows=0)
    {float value=Math.Min((v.Width-16f)/(creative?195:176),(v.Height-16f)/(creative?192:(rows>0?167:166)+Math.Max(0,rows-3)*18));return value>=1?Math.Min(ClassicHudLayout.Scale(v),MathF.Floor(value)):Math.Max(.5f,value);}
    public static int SlotSize(Viewport v,bool creative=false,int rows=0)=>(int)MathF.Round(18*Scale(v,creative,rows));
    public static Rectangle Panel(Viewport v,bool creative=false,int rows=0)
    {float s=Scale(v,creative,rows);int w=(int)(s*(creative?195:176)),h=(int)(s*(creative?192:(rows>0?167:166)+Math.Max(0,rows-3)*18));return new(v.X+(v.Width-w)/2,v.Y+(v.Height-h)/2,w,h);}
    internal static Rectangle At(Viewport v,bool creative,int x,int y,int w,int h,int rows=0)
    {var p=Panel(v,creative,rows);float s=Scale(v,creative,rows);return new(p.X+(int)MathF.Round(x*s),p.Y+(int)MathF.Round(y*s),(int)MathF.Round(w*s),(int)MathF.Round(h*s));}
    public static Rectangle Slot(Viewport v,int slot,bool creative=false,int rows=0)
    {int extra=Math.Max(0,rows-3)*18;return At(v,creative,(creative?8:7)+slot%9*18,slot<9?(creative?139:rows>0?142:141)+extra:(creative?81:rows>0?84:83)+(slot-9)/9*18+extra,18,18,rows);}
    public static Rectangle CraftingSlot(Viewport v,int width,int slot,bool creative=false)=>At(v,creative,(width==2?90:29)+slot%width*18,(width==2?28:16)+slot/width*18,18,18);
    public static Rectangle Output(Viewport v,int width,bool creative=false)=>At(v,creative,width==2?146:119,width==2?38:30,width==2?18:26,width==2?18:26);
    public static Rectangle Arrow(Viewport v,int width,bool creative=false)=>At(v,creative,width==2?128:90,width==2?40:35,width==2?16:22,14);
    public static Rectangle ContainerSlot(Viewport v,BlockType block,int slot,bool creative=false,int rows=0)
        =>block==BlockType.Chest?At(v,creative,7+slot%9*18,17+slot/9*18,18,18,rows):At(v,creative,slot==2?111:55,slot==0?16:slot==1?52:30,slot==2?26:18,slot==2?26:18);
    public static Rectangle HotbarSlot(Viewport viewport, int slot)
    {
        return ClassicHudLayout.Slot(viewport,slot);
    }
    public static Rectangle Equipment(Viewport v,int slot,bool creative=false)=>creative?At(v,true,slot<2?53:107,slot%2==0?33:60,18,18):At(v,false,7,7+slot*18,18,18);
    public static Rectangle Browser(Viewport v)=>Panel(v,true);
    public static Rectangle BrowserSearch(Viewport v)=>At(v,true,80,32,89,11);
    public static int BrowserRows(Viewport v)=>5;
    public static Rectangle BrowserSlot(Viewport v,int slot)=>At(v,true,8+slot%9*18,45+slot/9*18,18,18);
    internal static Rectangle CreativeTab(Viewport v,bool items)=>At(v,true,170,items?2:162,24,29);

}

internal static class ItemIcons
{
    private static readonly string[] Counts = CreateCounts();
    private static string[] CreateCounts()
    {
        var values = new string[65];
        for (int i = 1; i < values.Length; i++) values[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return values;
    }
    public static void Draw(SpriteBatch sprites, TextureAtlas atlas, Texture2D pixel, ItemStack stack, Rectangle bounds, bool showCount = true,int? guiScale=null,int? iconInset=null)
    {
        if (stack.IsEmpty || !Items.IsDefined(stack.Item)) return;
        int inset = iconInset??(guiScale.HasValue?2*guiScale.Value:Math.Max(3, bounds.Width / 6));
        Rectangle icon=new(bounds.X+inset,bounds.Y+inset,bounds.Width-inset*2,bounds.Height-inset*2),source=atlas.ItemSource(stack.Item);
        BlockShape shape=Items.TryGetBlock(stack.Item,out var block)?Blocks.Get(block).Shape:BlockShape.None;
        if(shape is BlockShape.Slab or BlockShape.Stairs)
        {
            sprites.Draw(atlas.Texture,new Rectangle(icon.X,icon.Y+icon.Height/2,icon.Width,icon.Height-icon.Height/2),new Rectangle(source.X,source.Y+8,16,8),Color.White);
            if(shape==BlockShape.Stairs)sprites.Draw(atlas.Texture,new Rectangle(icon.X,icon.Y,icon.Width/2,icon.Height/2),new Rectangle(source.X,source.Y,8,8),Color.White);
        }
        else if(shape==BlockShape.Sign)
        {
            sprites.Draw(atlas.Texture,new Rectangle(icon.X,icon.Y,icon.Width,icon.Height/2),new Rectangle(source.X,source.Y,16,8),Color.White);
            sprites.Draw(atlas.Texture,new Rectangle(icon.X+icon.Width*7/16,icon.Y+icon.Height/2,Math.Max(1,icon.Width/8),icon.Height/2),new Rectangle(source.X+7,source.Y+8,2,8),Color.White);
        }
        else sprites.Draw(atlas.ItemTexture(stack.Item),icon,source,Color.White);
        string count = Counts[stack.Count];
        int scale = guiScale??(bounds.Width >= 42 ? 2 : 1);
        int x = bounds.Right - 3 - count.Length * 6 * scale, y = bounds.Bottom - 3 - 7 * scale;
        if(showCount && stack.Limit>1)
        {
            PixelFont.Draw(sprites, pixel, count, x + 1, y + 1, scale, Color.Black);
            PixelFont.Draw(sprites, pixel, count, x, y, scale, Color.White);
        }
        int maximum=Items.Get(stack.Item).Durability;
        if(maximum>0 && stack.Durability<maximum)
        {
            float fraction=stack.Durability/(float)maximum;var bar=new Rectangle(bounds.X+3,bounds.Bottom-4,bounds.Width-6,3);
            sprites.Draw(pixel,bar,Color.Black);
            sprites.Draw(pixel,new Rectangle(bar.X,bar.Y,Math.Max(1,(int)(bar.Width*fraction)),2),new Color(1-fraction,fraction,0));
        }
    }
}

internal sealed class InventoryScreen : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    public string Message { get; private set; } = "";
    private ItemStack _hovered,_tooltipStack;
    private string _tooltip="";
    private int _creativeScroll;
    private bool _creativeItems=true;
    private int _menuScale=1;
    private bool _dragScroll;
    private readonly int[] _filtered=new int[CreativeCatalog.All.Length];
    private int _filteredCount;
    public bool SearchFocused {get;private set;}
    public string SearchText {get;private set;}="";
    public void TextInput(char c)
    {
        if(!SearchFocused)return;
        if(c=='\b'){if(SearchText.Length>0)SearchText=SearchText[..^1];}
        else if(c>=' ' && c<='~' && SearchText.Length<32)SearchText+=c;
        else return;
        Filter();
    }
    private void Filter()
    {
        _filteredCount=0;_creativeScroll=0;
        for(int i=0;i<CatalogIcons.Length;i++)if(CreativeCatalog.Matches(CatalogIcons[i].Item,SearchText))_filtered[_filteredCount++]=i;
    }
    private static readonly ItemStack[] CatalogIcons = CreateCatalogIcons();
    private static ItemStack[] CreateCatalogIcons()
    { var icons=new ItemStack[CreativeCatalog.All.Length]; for(int i=0;i<icons.Length;i++)icons[i]=new(CreativeCatalog.All[i],1);return icons; }
    public InventoryScreen(GraphicsDevice device, TextureAtlas atlas)
    {
        _atlas = atlas;
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
        Filter();
    }
    public void Open() {_creativeItems=true;Message="";SearchFocused=false;SearchText="";Filter();}
    public void CannotClose() => Message = "MAKE ROOM FOR HELD ITEMS AND INGREDIENTS.";
    public void Update(GameSession session, MouseState mouse, MouseState previous, Viewport viewport, bool shift = false, bool dropPressed = false)
    {
        if (dropPressed)
        {
            bool? dropped = TryDropHovered(session, mouse.Position, viewport, shift,_creativeItems);
            if (dropped.HasValue)
            {
                Message = dropped.Value ? "" : "NO ROOM TO DROP ITEMS.";
                if (dropped.Value) session.Sounds.Local(SoundKind.Drop);
            }
            return; // One input action; simultaneous clicks must not transfer/drop twice.
        }
        InventoryInteraction interaction = session.InventoryInteraction;
        bool left = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        bool right = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        bool creative = session.Player.Creative && session.ActiveContainer==null && session.Crafting.Width==2;
        int rows=session.ActiveContainer?.Block==BlockType.Chest?session.ActiveContainer.SlotCount/9:0;
        if(left && creative && (InventoryLayout.CreativeTab(viewport,true).Contains(mouse.Position)||InventoryLayout.CreativeTab(viewport,false).Contains(mouse.Position))){_creativeItems=InventoryLayout.CreativeTab(viewport,true).Contains(mouse.Position);SearchFocused=false;return;}
        if(left)SearchFocused=creative && _creativeItems && InventoryLayout.BrowserSearch(viewport).Contains(mouse.Position);
        if(creative && _creativeItems && InventoryLayout.BrowserSearch(viewport).Contains(mouse.Position))return;
        if (creative && _creativeItems && InventoryLayout.Browser(viewport).Contains(mouse.Position) && mouse.Y<InventoryLayout.Slot(viewport,0,true).Y)
        {
            int browserRows = InventoryLayout.BrowserRows(viewport);
            var track=InventoryLayout.At(viewport,true,174,45,12,112);
            if(mouse.LeftButton==ButtonState.Released)_dragScroll=false;
            if(left&&track.Contains(mouse.Position))_dragScroll=true;
            if(_dragScroll){_creativeScroll=(int)Math.Round(Math.Clamp((mouse.Y-track.Y)/(double)Math.Max(1,track.Height),0,1)*Math.Max(0,(_filteredCount+8)/9-browserRows));return;}
            _creativeScroll = Math.Clamp(_creativeScroll - (mouse.ScrollWheelValue-previous.ScrollWheelValue)/120,0,Math.Max(0,(_filteredCount+8)/9-browserRows));
            if (left) for (int slot=0;slot<browserRows*9;slot++)
            {
                int visible=_creativeScroll*9+slot;if(visible>=_filteredCount)break;int index=_filtered[visible];
                if(!InventoryLayout.BrowserSlot(viewport,slot).Contains(mouse.Position))continue;
                int granted=CreativeCatalog.Grant(interaction.Inventory,CatalogIcons[index].Item,shift);
                if(granted>0)session.Sounds.Local(SoundKind.InventoryMove);
                Message=granted==0?"INVENTORY FULL.":$"GRANTED {granted} {Items.Name(CatalogIcons[index].Item)}";
                return;
            }
            // Browser clicks never fall through to cursor dropping or ordinary slots.
            return;
        }
        if (!left && !right) return;
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            if ((!creative||!_creativeItems||slot<9) && InventoryLayout.Slot(viewport, slot, creative,rows).Contains(mouse.Position))
            {
                var before=interaction.Inventory[slot];var cursor=interaction.Cursor;
                interaction.Click(slot, right && !left);
                if(before!=interaction.Inventory[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);
                Message = "";
                return;
            }
        CraftingGrid crafting = session.Crafting;BlockContainer container=session.ActiveContainer;
        if(container==null && crafting.Width==2 && (!creative||!_creativeItems))for(int slot=0;slot<4;slot++)if(InventoryLayout.Equipment(viewport,slot,creative).Contains(mouse.Position))
        {var before=interaction.Inventory.Equipped(slot);var cursor=interaction.Cursor;interaction.Inventory.ClickEquipment(slot,interaction,right && !left);
        if(before!=interaction.Inventory.Equipped(slot) || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.Equip);Message="";return;}
        if(container!=null)
        {
            for(int slot=0;slot<container.SlotCount;slot++)if(InventoryLayout.ContainerSlot(viewport,container.Block,slot,creative,rows).Contains(mouse.Position))
            {var before=container[slot];var cursor=interaction.Cursor;container.Click(slot,interaction,right && !left);
            if(before!=container[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);Message="";return;}
        }
        else if(!creative)
        {
        for (int slot = 0; slot < crafting.SlotCount; slot++)
            if (InventoryLayout.CraftingSlot(viewport, crafting.Width, slot, creative).Contains(mouse.Position))
            { var before=crafting[slot];var cursor=interaction.Cursor;crafting.Click(slot, interaction, right && !left);
            if(before!=crafting[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);Message = ""; return; }
        if (InventoryLayout.Output(viewport, crafting.Width, creative).Contains(mouse.Position))
        { if(crafting.TryCraft(interaction))session.Sounds.Local(SoundKind.Craft); Message = ""; return; }
        }
        if (left && !interaction.Cursor.IsEmpty && !InventoryLayout.Panel(viewport, creative,rows).Contains(mouse.Position))
        {bool dropped=session.DropCursor();Message = dropped ? "" : "NO ROOM TO DROP ITEMS.";if(dropped)session.Sounds.Local(SoundKind.Drop);}
    }
    // Hit-test the current mouse/layout, never the previous rendered tooltip.
    // Null means there is no real stack here (including previews/catalog entries).
    internal static bool? TryDropHovered(GameSession session, Point mouse, Viewport viewport, bool wholeStack,bool creativeItems=false)
    {
        if (!session.InventoryInteraction.Cursor.IsEmpty) return session.DropCursor(wholeStack);
        bool creative = session.Player.Creative && session.ActiveContainer==null && session.Crafting.Width==2;
        int rows=session.ActiveContainer?.Block==BlockType.Chest?session.ActiveContainer.SlotCount/9:0;
        bool? Drop(ItemStack stack, out ItemStack remaining)
        {
            remaining = stack;
            return stack.IsEmpty ? null : session.TryDropStack(stack, wholeStack, out remaining);
        }
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            if ((!creative||!creativeItems||slot<9)&&InventoryLayout.Slot(viewport, slot, creative,rows).Contains(mouse))
            {
                bool? result = Drop(session.Inventory[slot], out var remaining);
                if (result == true) session.Inventory.SetSlot(slot, remaining);
                return result;
            }
        BlockContainer container = session.ActiveContainer;
        if (container == null && session.Crafting.Width == 2&&(!creative||!creativeItems))
            for (int slot = 0; slot < 4; slot++)
                if (InventoryLayout.Equipment(viewport, slot, creative).Contains(mouse))
                {
                    bool? result = Drop(session.Inventory.Equipped(slot), out var remaining);
                    if (result == true) session.Inventory.SetEquipment(slot, remaining);
                    return result;
                }
        if (container != null)
        {
            for (int slot = 0; slot < container.SlotCount; slot++)
                if (InventoryLayout.ContainerSlot(viewport, container.Block, slot, creative,rows).Contains(mouse))
                {
                    bool? result = Drop(container[slot], out var remaining);
                    if (result == true) container.SetSlot(slot, remaining);
                    return result;
                }
        }
        else if(!creative)
            for (int slot = 0; slot < session.Crafting.SlotCount; slot++)
                if (InventoryLayout.CraftingSlot(viewport, session.Crafting.Width, slot, creative).Contains(mouse))
                {
                    bool? result = Drop(session.Crafting[slot], out var remaining);
                    if (result == true) session.Crafting.SetSlot(slot, remaining);
                    return result;
                }
        return null;
    }
    public void Draw(GameSession session, MouseState mouse, Viewport viewport)
    {
        _hovered=default;
        bool creative=session.Player.Creative&&session.ActiveContainer==null&&session.Crafting.Width==2;
        int rows=session.ActiveContainer?.Block==BlockType.Chest?session.ActiveContainer.SlotCount/9:0;
        InventoryInteraction interaction = session.InventoryInteraction;
        CraftingGrid crafting = session.Crafting;
        BlockContainer container=session.ActiveContainer;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);
        _menuScale=Math.Max(1,(int)Math.Round(InventoryLayout.SlotSize(viewport,creative,rows)/18.0));
        Rectangle panel=InventoryLayout.Panel(viewport,creative,rows);
        if(creative)
        {
            MdsmUiStyle.Frame(_sprites,_pixel,InventoryLayout.At(viewport,true,0,29,195,133),_menuScale);
            for(int tabIndex=0;tabIndex<2;tabIndex++)
            {
                bool items=tabIndex==0;var tab=InventoryLayout.CreativeTab(viewport,items);MdsmUiStyle.Frame(_sprites,_pixel,tab,_menuScale);
                if(items==_creativeItems)Fill(new(tab.X+2*_menuScale,tab.Bottom-3*_menuScale,tab.Width-4*_menuScale,_menuScale),MdsmUiStyle.Accent);
                if(items)for(int y=0;y<3;y++)for(int x=0;x<3;x++)Fill(InventoryLayout.At(viewport,true,174+x*5,10+y*5,3,3),MdsmUiStyle.Ink);
                else {Fill(InventoryLayout.At(viewport,true,176,169,12,15),MdsmUiStyle.Light);Fill(InventoryLayout.At(viewport,true,179,173,6,3),MdsmUiStyle.Accent);}
            }
            var title=InventoryLayout.At(viewport,true,8,33,64,9);Text(_creativeItems?"ITEMS":"PACK",title.X,title.Y,_menuScale,MdsmUiStyle.Ink);
        }
        else
        {
            MdsmUiStyle.Frame(_sprites,_pixel,panel,_menuScale);
            string title=container!=null?(container.Block==BlockType.Chest?(rows==6?"LARGE CHEST":"CHEST"):Blocks.Get(container.Block).Name):crafting.Width==3?"WORKBENCH":"PACK";
            var heading=InventoryLayout.At(viewport,false,container==null&&crafting.Width==2?31:7,5,140,9,rows);Text(title,heading.X,heading.Y,_menuScale,MdsmUiStyle.Ink);
            if(container==null){var caption=InventoryLayout.At(viewport,false,crafting.Width==2?90:29,crafting.Width==2?17:6,80,8);if(crafting.Width==2)Text("ASSEMBLE",caption.X,caption.Y,_menuScale,MdsmUiStyle.Accent);MdsmUiStyle.Arrow(_sprites,_pixel,InventoryLayout.Arrow(viewport,crafting.Width),MdsmUiStyle.Accent);}
            else if(Smelting.Machine(container.Block))MdsmUiStyle.Arrow(_sprites,_pixel,InventoryLayout.At(viewport,false,79,35,24,17),MdsmUiStyle.Light);
            if(container!=null||crafting.Width==3){var captionPack=InventoryLayout.At(viewport,false,7,(rows>0?74+(rows-3)*18:73),100,8,rows);Text("INVENTORY",captionPack.X,captionPack.Y,_menuScale,MdsmUiStyle.Light);}
        }
        if(!creative&&container==null)
        {
            for(int slot=0;slot<crafting.SlotCount;slot++)DrawSlot(crafting[slot],InventoryLayout.CraftingSlot(viewport,crafting.Width,slot),mouse,false);
            DrawSlot(crafting.Preview,InventoryLayout.Output(viewport,crafting.Width),mouse,false);
        }
        if(container==null&&(!creative||!_creativeItems)&&crafting.Width==2)
            for(int slot=0;slot<4;slot++)
            {
                var bounds=InventoryLayout.Equipment(viewport,slot,creative);var stack=interaction.Inventory.Equipped(slot);
                DrawSlot(stack,bounds,mouse,false);
                if(stack.IsEmpty)Text(slot switch{0=>"H",1=>"C",2=>"L",_=>"B"},bounds.Center.X-3*_menuScale,bounds.Center.Y-3*_menuScale,_menuScale,MdsmUiStyle.Light);
            }
        if(container!=null)
        {
            for(int slot=0;slot<container.SlotCount;slot++)DrawSlot(container[slot],InventoryLayout.ContainerSlot(viewport,container.Block,slot,false,rows),mouse,false);
            if(Smelting.Machine(container.Block))
            {
                int progress=(int)(24*container.Progress/Smelting.Seconds);
                for(int x=0;x<progress;x++)for(int y=0;y<17;y++)
                    if(x<15?y>=6&&y<=10:Math.Abs(y-8)<=23-x)Fill(InventoryLayout.At(viewport,false,79+x,35+y,1,1),Color.White);
                int lit=(int)(12*container.Burn/Math.Max(1,container.BurnTotal));
                for(int y=12-lit;y<12;y++)for(int x=0;x<12;x++)
                    if(Math.Abs(x-5.5f)<(y<5?y*.65f+1:5.5f))Fill(InventoryLayout.At(viewport,false,56+x,38+y,1,1),y>7&&x>3&&x<8?Color.Yellow:Color.OrangeRed);

            }
        }
        for(int slot=0;slot<36;slot++)if(!creative||!_creativeItems||slot<9)
            DrawSlot(interaction.Inventory[slot],InventoryLayout.Slot(viewport,slot,creative,rows),mouse,false);
        if(creative)
        {
            // Both tabs show only real game data; no reference sprite decorations.

            if(_creativeItems)
            {
                Rectangle search=InventoryLayout.BrowserSearch(viewport);Fill(search,SearchFocused?MdsmUiStyle.Light:MdsmUiStyle.Inset);
                Text(PixelFont.Fit(SearchText.Length==0?"SEARCH...":SearchText.ToUpperInvariant(),Math.Max(1,(search.Width-4)/(6*_menuScale))),search.X+2,search.Y+2,_menuScale,Color.White);
                int browserRows=InventoryLayout.BrowserRows(viewport);_creativeScroll=Math.Clamp(_creativeScroll,0,Math.Max(0,(_filteredCount+8)/9-browserRows));
                var track=InventoryLayout.At(viewport,true,174,45,12,112);Fill(track,MdsmUiStyle.Inset);
                int maxScroll=Math.Max(0,(_filteredCount+8)/9-browserRows);int thumbHeight=Math.Max(8,track.Height*browserRows/Math.Max(browserRows,(_filteredCount+8)/9));
                Fill(new(track.X,track.Y+(maxScroll==0?0:(track.Height-thumbHeight)*_creativeScroll/maxScroll),track.Width,thumbHeight),MdsmUiStyle.Accent);
                for(int slot=0;slot<browserRows*9;slot++)
                {
                    int visible=_creativeScroll*9+slot;var bounds=InventoryLayout.BrowserSlot(viewport,slot);
                    Fill(bounds,MdsmUiStyle.Inset);
                    DrawSlot(visible<_filteredCount?CatalogIcons[_filtered[visible]]:default,bounds,mouse,false,true);
                }
            }
        }
        if(Message.Length>0)Text(PixelFont.Fit(Message,Math.Max(1,(viewport.Width-16)/6)),8,viewport.Height-12,1,Color.Salmon);
        if(interaction.Cursor.IsEmpty && !_hovered.IsEmpty)
        {
            if(_tooltipStack!=_hovered){_tooltipStack=_hovered;_tooltip=Items.Name(_hovered.Item)+(_hovered.Durability>0?$" {_hovered.Durability}/{Items.Get(_hovered.Item).Durability}":"");}
            int x=Math.Clamp(mouse.X+8,4,Math.Max(4,viewport.Width-_tooltip.Length*6-8)),y=Math.Clamp(mouse.Y-15,2,Math.Max(2,viewport.Height-13));
            Fill(new Rectangle(x-3,y-2,_tooltip.Length*6+6,11),Color.Black*.9f);Text(_tooltip,x,y,1,Color.White);
        }
        ItemIcons.Draw(_sprites, _atlas, _pixel, interaction.Cursor, new Rectangle(mouse.X + 8, mouse.Y + 8, 42, 42));
        _sprites.End();
    }
    private void DrawSlot(ItemStack stack, Rectangle bounds, MouseState mouse, bool selected, bool catalog = false,bool frame=true)
    {
        if(bounds.Contains(mouse.Position) && !stack.IsEmpty)_hovered=stack;
        if(frame)
        {
            Fill(bounds,MdsmUiStyle.Edge);int edge=_menuScale;
            Fill(new(bounds.X+edge,bounds.Y+edge,bounds.Width-edge,bounds.Height-edge),MdsmUiStyle.Light);
            Fill(new(bounds.X+edge,bounds.Y+edge,bounds.Width-edge*2,bounds.Height-edge*2),MdsmUiStyle.Inset);
        }
        if(bounds.Contains(mouse.Position))Fill(bounds,Color.White*.22f);
        ItemIcons.Draw(_sprites, _atlas, _pixel, stack, bounds, !catalog&&stack.Count>1,_menuScale,Math.Max(1,(bounds.Width-16*_menuScale)/2));
    }
    private void Text(string text, int x, int y, int scale, Color color) => PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);
    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);
    public void Dispose() { _sprites.Dispose(); _pixel.Dispose(); }
}
