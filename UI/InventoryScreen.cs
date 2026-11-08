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
    public static int SlotSize(Viewport viewport, bool creative = false) => Math.Clamp(Math.Min((viewport.Width - (creative ? 64 : 40)) / (creative ? 15 : 9), (viewport.Height - 72) / 7), 16, 52);
    public static Rectangle Panel(Viewport viewport, bool creative = false)
    {
        int size = SlotSize(viewport, creative), width = size * 9 + 24, height = size * 7 + 64;
        int groupWidth = width + (creative ? size * 5 + 24 : 0);
        return new Rectangle((viewport.Width - groupWidth) / 2, (viewport.Height - height) / 2, width, height);
    }
    public static Rectangle Slot(Viewport viewport, int slot, bool creative = false)
    {
        Rectangle panel = Panel(viewport, creative);
        int size = SlotSize(viewport, creative);
        int row = slot < Inventory.HotbarSlots ? 3 : (slot - Inventory.HotbarSlots) / 9;
        int column = slot % 9;
        return new Rectangle(panel.X + 12 + column * size, panel.Y + 34 + 3 * size + row * size + (row == 3 ? 10 : 0), size - 2, size - 2);
    }
    public static Rectangle CraftingSlot(Viewport viewport, int width, int slot, bool creative = false)
    {
        Rectangle panel = Panel(viewport, creative); int size = SlotSize(viewport, creative);
        return new Rectangle(panel.X + 12 + size + slot % width * size, panel.Y + 24 + slot / width * size, size - 2, size - 2);
    }
    public static Rectangle Output(Viewport viewport, int width, bool creative = false)
    {
        Rectangle input = CraftingSlot(viewport, width, 0, creative); int size = SlotSize(viewport, creative);
        return new Rectangle(input.X + (width + 2) * size, input.Y + (width - 1) * size / 2, size - 2, size - 2);
    }
    public static Rectangle Arrow(Viewport viewport, int width, bool creative = false)
    {
        Rectangle output = Output(viewport, width, creative); int size = SlotSize(viewport, creative);
        return new Rectangle(output.X - size - size / 2, output.Center.Y - size / 8, size, Math.Max(2, size / 4));
    }
    public static Rectangle ContainerSlot(Viewport viewport,BlockType block,int slot,bool creative=false)
    {
        Rectangle panel=Panel(viewport,creative);int size=SlotSize(viewport,creative);
        if(block==BlockType.Chest)return new(panel.X+12+slot%9*size,panel.Y+24+slot/9*size,size-2,size-2);
        return new(panel.X+12+(slot==2?6:2)*size,panel.Y+24+(slot==0?0:slot==1?2:1)*size,size-2,size-2);
    }
    public static Rectangle HotbarSlot(Viewport viewport, int slot)
    {
        int size = Math.Clamp((viewport.Width - 24) / 9, 16, 58);
        return new Rectangle((viewport.Width - size * 9) / 2 + slot * size, viewport.Height - size - 18, size - 2, size);
    }
    public static Rectangle Equipment(Viewport viewport,int slot,bool creative=false)
    {
        Rectangle panel=Panel(viewport,creative);int size=SlotSize(viewport,creative);
        int small=size*3/4;return new(panel.Right-size-12,panel.Y+24+slot*small,small-2,small-2);
    }
    public static Rectangle Browser(Viewport viewport)
    { Rectangle panel = Panel(viewport,true); return new(panel.Right+8,panel.Y,SlotSize(viewport,true)*5+16,panel.Height); }
    public static Rectangle BrowserSearch(Viewport viewport)
    {Rectangle p=Browser(viewport);return new(p.X+8,p.Y+23,p.Width-16,18);}
    public static int BrowserRows(Viewport viewport) => Math.Max(1,(Browser(viewport).Height-78)/SlotSize(viewport,true));
    public static Rectangle BrowserSlot(Viewport viewport,int visibleSlot)
    { Rectangle panel=Browser(viewport);int size=SlotSize(viewport,true);return new(panel.X+8+visibleSlot%5*size,panel.Y+46+visibleSlot/5*size,size-2,size-2); }
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
    public static void Draw(SpriteBatch sprites, TextureAtlas atlas, Texture2D pixel, ItemStack stack, Rectangle bounds, bool showCount = true)
    {
        if (stack.IsEmpty || !Items.IsDefined(stack.Item)) return;
        int inset = Math.Max(3, bounds.Width / 6);
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
        int scale = bounds.Width >= 42 ? 2 : 1;
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
    public void Open() {Message="";SearchFocused=false;SearchText="";Filter();}
    public void CannotClose() => Message = "MAKE ROOM FOR HELD ITEMS AND INGREDIENTS.";
    public void Update(GameSession session, MouseState mouse, MouseState previous, Viewport viewport, bool shift = false, bool dropPressed = false)
    {
        if (dropPressed)
        {
            bool? dropped = TryDropHovered(session, mouse.Position, viewport, shift);
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
        bool creative = session.Player.Creative;
        if(left)SearchFocused=creative && InventoryLayout.BrowserSearch(viewport).Contains(mouse.Position);
        if(creative && InventoryLayout.BrowserSearch(viewport).Contains(mouse.Position))return;
        if (creative && InventoryLayout.Browser(viewport).Contains(mouse.Position))
        {
            int rows = InventoryLayout.BrowserRows(viewport);
            _creativeScroll = Math.Clamp(_creativeScroll - (mouse.ScrollWheelValue-previous.ScrollWheelValue)/120,0,Math.Max(0,(_filteredCount+4)/5-rows));
            if (left) for (int slot=0;slot<rows*5;slot++)
            {
                int visible=_creativeScroll*5+slot;if(visible>=_filteredCount)break;int index=_filtered[visible];
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
            if (InventoryLayout.Slot(viewport, slot, creative).Contains(mouse.Position))
            {
                var before=interaction.Inventory[slot];var cursor=interaction.Cursor;
                interaction.Click(slot, right && !left);
                if(before!=interaction.Inventory[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);
                Message = "";
                return;
            }
        CraftingGrid crafting = session.Crafting;BlockContainer container=session.ActiveContainer;
        if(container==null && crafting.Width==2)for(int slot=0;slot<4;slot++)if(InventoryLayout.Equipment(viewport,slot,creative).Contains(mouse.Position))
        {var before=interaction.Inventory.Equipped(slot);var cursor=interaction.Cursor;interaction.Inventory.ClickEquipment(slot,interaction,right && !left);
        if(before!=interaction.Inventory.Equipped(slot) || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.Equip);Message="";return;}
        if(container!=null)
        {
            for(int slot=0;slot<container.SlotCount;slot++)if(InventoryLayout.ContainerSlot(viewport,container.Block,slot,creative).Contains(mouse.Position))
            {var before=container[slot];var cursor=interaction.Cursor;container.Click(slot,interaction,right && !left);
            if(before!=container[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);Message="";return;}
        }
        else
        {
        for (int slot = 0; slot < crafting.SlotCount; slot++)
            if (InventoryLayout.CraftingSlot(viewport, crafting.Width, slot, creative).Contains(mouse.Position))
            { var before=crafting[slot];var cursor=interaction.Cursor;crafting.Click(slot, interaction, right && !left);
            if(before!=crafting[slot] || cursor!=interaction.Cursor)session.Sounds.Local(SoundKind.InventoryMove);Message = ""; return; }
        if (InventoryLayout.Output(viewport, crafting.Width, creative).Contains(mouse.Position))
        { if(crafting.TryCraft(interaction))session.Sounds.Local(SoundKind.Craft); Message = ""; return; }
        }
        if (left && !interaction.Cursor.IsEmpty && !InventoryLayout.Panel(viewport, creative).Contains(mouse.Position))
        {bool dropped=session.DropCursor();Message = dropped ? "" : "NO ROOM TO DROP ITEMS.";if(dropped)session.Sounds.Local(SoundKind.Drop);}
    }
    // Hit-test the current mouse/layout, never the previous rendered tooltip.
    // Null means there is no real stack here (including previews/catalog entries).
    internal static bool? TryDropHovered(GameSession session, Point mouse, Viewport viewport, bool wholeStack)
    {
        if (!session.InventoryInteraction.Cursor.IsEmpty) return session.DropCursor(wholeStack);
        bool creative = session.Player.Creative;
        bool? Drop(ItemStack stack, out ItemStack remaining)
        {
            remaining = stack;
            return stack.IsEmpty ? null : session.TryDropStack(stack, wholeStack, out remaining);
        }
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            if (InventoryLayout.Slot(viewport, slot, creative).Contains(mouse))
            {
                bool? result = Drop(session.Inventory[slot], out var remaining);
                if (result == true) session.Inventory.SetSlot(slot, remaining);
                return result;
            }
        BlockContainer container = session.ActiveContainer;
        if (container == null && session.Crafting.Width == 2)
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
                if (InventoryLayout.ContainerSlot(viewport, container.Block, slot, creative).Contains(mouse))
                {
                    bool? result = Drop(container[slot], out var remaining);
                    if (result == true) container.SetSlot(slot, remaining);
                    return result;
                }
        }
        else
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
        bool creative=session.Player.Creative;
        InventoryInteraction interaction = session.InventoryInteraction;
        CraftingGrid crafting = session.Crafting;
        BlockContainer container=session.ActiveContainer;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);
        Rectangle panel = InventoryLayout.Panel(viewport, creative);
        Fill(panel, new Color(150, 141, 125));
        Fill(new Rectangle(panel.X + 3, panel.Y + 3, panel.Width - 6, panel.Height - 6), new Color(51, 48, 43));
        Text(container!=null?Blocks.Get(container.Block).Name:crafting.Width == 3 ? "CRAFTING TABLE" : "CRAFTING", panel.X + 12, panel.Y + 8, 1, Color.White);
        if(container==null)
        {
        for (int slot = 0; slot < crafting.SlotCount; slot++)
            DrawSlot(crafting[slot], InventoryLayout.CraftingSlot(viewport, crafting.Width, slot, creative), mouse, false);
        Rectangle arrow = InventoryLayout.Arrow(viewport, crafting.Width, creative);
        Fill(new Rectangle(arrow.X, arrow.Center.Y - 1, arrow.Width - 3, 3), Color.LightGray);
        for (int i = 0; i < 5; i++) Fill(new Rectangle(arrow.Right - 5 + i, arrow.Center.Y - 4 + i, 1, 9 - i * 2), Color.LightGray);
        DrawSlot(crafting.Preview, InventoryLayout.Output(viewport, crafting.Width, creative), mouse, false);
        if(crafting.Width==2)for(int slot=0;slot<4;slot++)DrawSlot(interaction.Inventory.Equipped(slot),InventoryLayout.Equipment(viewport,slot,creative),mouse,false);
        }
        else
        {
            for(int slot=0;slot<container.SlotCount;slot++)DrawSlot(container[slot],InventoryLayout.ContainerSlot(viewport,container.Block,slot,creative),mouse,false);
            if(Smelting.Machine(container.Block))
            {
                int size=InventoryLayout.SlotSize(viewport, creative);Rectangle input=InventoryLayout.ContainerSlot(viewport,container.Block,0,creative),output=InventoryLayout.ContainerSlot(viewport,container.Block,2,creative);
                var progress=new Rectangle(input.Right+size/2,output.Center.Y-4,Math.Max(4,output.Left-input.Right-size),8);
                Fill(progress,Color.Black);Fill(new Rectangle(progress.X,progress.Y,(int)(progress.Width*container.Progress/Smelting.Seconds),progress.Height),Color.Gold);
                Rectangle fuel=InventoryLayout.ContainerSlot(viewport,container.Block,1,creative);
                var burn=new Rectangle(fuel.Right+4,fuel.Y,6,fuel.Height);Fill(burn,Color.Black);
                int height=(int)(burn.Height*container.Burn/Math.Max(1,container.BurnTotal));
                Fill(new Rectangle(burn.X,burn.Bottom-height,burn.Width,height),Color.OrangeRed);
            }
        }
        Text("INVENTORY", panel.X + 12, InventoryLayout.Slot(viewport, 9, creative).Y - 10, 1, Color.LightGray);
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            Rectangle bounds = InventoryLayout.Slot(viewport, slot, creative);
            DrawSlot(interaction.Inventory[slot], bounds, mouse, slot == interaction.Inventory.SelectedSlot);
        }
        string help = PixelFont.Fit("E / ESC CLOSE   LMB OUTSIDE DROPS HELD STACK", (panel.Width - 24) / 6);
        Text(Message.Length > 0 ? PixelFont.Fit(Message, (panel.Width - 24) / 6) : help,
            panel.X + 12, panel.Bottom - 20, 1, Message.Length > 0 ? Color.Salmon : Color.LightGray);
        if (creative)
        {
            Rectangle browser=InventoryLayout.Browser(viewport);
            Fill(browser,new Color(150,141,125));Fill(new Rectangle(browser.X+3,browser.Y+3,browser.Width-6,browser.Height-6),new Color(51,48,43));
            Text("CREATIVE ITEMS",browser.X+8,browser.Y+9,1,Color.LightGoldenrodYellow);
            Rectangle search=InventoryLayout.BrowserSearch(viewport);Fill(search,SearchFocused?new Color(81,80,70):new Color(24,25,23));
            Text(PixelFont.Fit(SearchText.Length==0?"SEARCH...":SearchText.ToUpperInvariant(),Math.Max(1,(search.Width-6)/6)),search.X+3,search.Y+5,1,Color.White);
            int rows=InventoryLayout.BrowserRows(viewport);
            _creativeScroll=Math.Clamp(_creativeScroll,0,Math.Max(0,(_filteredCount+4)/5-rows));
            for(int slot=0;slot<rows*5;slot++)
            {
                int visible=_creativeScroll*5+slot;if(visible>=_filteredCount)break;int index=_filtered[visible];
                DrawSlot(CatalogIcons[index],InventoryLayout.BrowserSlot(viewport,slot),mouse,false,true);
            }
            Text("WHEEL: SCROLL",browser.X+8,browser.Bottom-23,1,Color.LightGray);
            Text("SHIFT: STACK",browser.X+8,browser.Bottom-13,1,Color.LightGray);
        }
        if(interaction.Cursor.IsEmpty && !_hovered.IsEmpty)
        {
            if(_tooltipStack!=_hovered){_tooltipStack=_hovered;_tooltip=Items.Name(_hovered.Item)+(_hovered.Durability>0?$" {_hovered.Durability}/{Items.Get(_hovered.Item).Durability}":"");}
            int x=Math.Clamp(mouse.X+8,4,Math.Max(4,viewport.Width-_tooltip.Length*6-8)),y=Math.Clamp(mouse.Y-15,2,Math.Max(2,viewport.Height-13));
            Fill(new Rectangle(x-3,y-2,_tooltip.Length*6+6,11),Color.Black*.9f);Text(_tooltip,x,y,1,Color.White);
        }
        ItemIcons.Draw(_sprites, _atlas, _pixel, interaction.Cursor, new Rectangle(mouse.X + 8, mouse.Y + 8, 42, 42));
        _sprites.End();
    }
    private void DrawSlot(ItemStack stack, Rectangle bounds, MouseState mouse, bool selected, bool catalog = false)
    {
        if(bounds.Contains(mouse.Position) && !stack.IsEmpty)_hovered=stack;
        Fill(bounds, selected ? new Color(235, 216, 135) : new Color(125, 119, 107));
        Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), bounds.Contains(mouse.Position)
            ? new Color(83, 82, 73) : new Color(32, 34, 33));
        ItemIcons.Draw(_sprites, _atlas, _pixel, stack, bounds, !catalog);
    }
    private void Text(string text, int x, int y, int scale, Color color) => PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);
    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);
    public void Dispose() { _sprites.Dispose(); _pixel.Dispose(); }
}
