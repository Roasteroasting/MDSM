using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.UI;

internal static class InventoryLayout
{
    public static int SlotSize(Viewport viewport) => Math.Clamp(Math.Min((viewport.Width - 40) / 9, (viewport.Height - 72) / 7), 16, 52);
    public static Rectangle Panel(Viewport viewport)
    {
        int size = SlotSize(viewport), width = size * 9 + 24, height = size * 7 + 64;
        return new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
    }
    public static Rectangle Slot(Viewport viewport, int slot)
    {
        Rectangle panel = Panel(viewport);
        int size = SlotSize(viewport);
        int row = slot < Inventory.HotbarSlots ? 3 : (slot - Inventory.HotbarSlots) / 9;
        int column = slot % 9;
        return new Rectangle(panel.X + 12 + column * size, panel.Y + 34 + 3 * size + row * size + (row == 3 ? 10 : 0), size - 2, size - 2);
    }
    public static Rectangle CraftingSlot(Viewport viewport, int width, int slot)
    {
        Rectangle panel = Panel(viewport); int size = SlotSize(viewport);
        return new Rectangle(panel.X + 12 + size + slot % width * size, panel.Y + 24 + slot / width * size, size - 2, size - 2);
    }
    public static Rectangle Output(Viewport viewport, int width)
    {
        Rectangle input = CraftingSlot(viewport, width, 0); int size = SlotSize(viewport);
        return new Rectangle(input.X + (width + 2) * size, input.Y + (width - 1) * size / 2, size - 2, size - 2);
    }
    public static Rectangle Arrow(Viewport viewport, int width)
    {
        Rectangle output = Output(viewport, width); int size = SlotSize(viewport);
        return new Rectangle(output.X - size - size / 2, output.Center.Y - size / 8, size, Math.Max(2, size / 4));
    }
    public static Rectangle ContainerSlot(Viewport viewport,BlockType block,int slot)
    {
        Rectangle panel=Panel(viewport);int size=SlotSize(viewport);
        if(block==BlockType.Chest)return new(panel.X+12+slot%9*size,panel.Y+24+slot/9*size,size-2,size-2);
        return new(panel.X+12+(slot==2?6:2)*size,panel.Y+24+(slot==0?0:slot==1?2:1)*size,size-2,size-2);
    }
    public static Rectangle HotbarSlot(Viewport viewport, int slot)
    {
        int size = Math.Clamp((viewport.Width - 24) / 9, 16, 58);
        return new Rectangle((viewport.Width - size * 9) / 2 + slot * size, viewport.Height - size - 18, size - 2, size);
    }
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
    public static void Draw(SpriteBatch sprites, TextureAtlas atlas, Texture2D pixel, ItemStack stack, Rectangle bounds)
    {
        if (stack.IsEmpty || !Items.IsDefined(stack.Item)) return;
        int inset = Math.Max(3, bounds.Width / 6);
        sprites.Draw(atlas.ItemTexture(stack.Item), new Rectangle(bounds.X + inset, bounds.Y + inset, bounds.Width - inset * 2, bounds.Height - inset * 2),
            atlas.ItemSource(stack.Item), Color.White);
        string count = Counts[stack.Count];
        int scale = bounds.Width >= 42 ? 2 : 1;
        int x = bounds.Right - 3 - count.Length * 6 * scale, y = bounds.Bottom - 3 - 7 * scale;
        if(stack.Limit>1)
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
    public InventoryScreen(GraphicsDevice device, TextureAtlas atlas)
    {
        _atlas = atlas;
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }
    public void Open() => Message = "";
    public void CannotClose() => Message = "MAKE ROOM FOR HELD ITEMS AND INGREDIENTS.";
    public void Update(GameSession session, MouseState mouse, MouseState previous, Viewport viewport)
    {
        InventoryInteraction interaction = session.InventoryInteraction;
        bool left = mouse.LeftButton == ButtonState.Pressed && previous.LeftButton == ButtonState.Released;
        bool right = mouse.RightButton == ButtonState.Pressed && previous.RightButton == ButtonState.Released;
        if (!left && !right) return;
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
            if (InventoryLayout.Slot(viewport, slot).Contains(mouse.Position))
            {
                interaction.Click(slot, right && !left);
                Message = "";
                return;
            }
        CraftingGrid crafting = session.Crafting;BlockContainer container=session.ActiveContainer;
        if(container!=null)
        {
            for(int slot=0;slot<container.SlotCount;slot++)if(InventoryLayout.ContainerSlot(viewport,container.Block,slot).Contains(mouse.Position))
            {container.Click(slot,interaction,right && !left);Message="";return;}
        }
        else
        {
        for (int slot = 0; slot < crafting.SlotCount; slot++)
            if (InventoryLayout.CraftingSlot(viewport, crafting.Width, slot).Contains(mouse.Position))
            { crafting.Click(slot, interaction, right && !left); Message = ""; return; }
        if (InventoryLayout.Output(viewport, crafting.Width).Contains(mouse.Position))
        { crafting.TryCraft(interaction); Message = ""; return; }
        }
        if (left && !interaction.Cursor.IsEmpty && !InventoryLayout.Panel(viewport).Contains(mouse.Position))
            Message = session.DropCursor() ? "" : "NO ROOM TO DROP ITEMS.";
    }
    public void Draw(GameSession session, MouseState mouse, Viewport viewport)
    {
        _hovered=default;
        InventoryInteraction interaction = session.InventoryInteraction;
        CraftingGrid crafting = session.Crafting;
        BlockContainer container=session.ActiveContainer;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);
        Rectangle panel = InventoryLayout.Panel(viewport);
        Fill(panel, new Color(150, 141, 125));
        Fill(new Rectangle(panel.X + 3, panel.Y + 3, panel.Width - 6, panel.Height - 6), new Color(51, 48, 43));
        Text(container!=null?Blocks.Get(container.Block).Name:crafting.Width == 3 ? "CRAFTING TABLE" : "CRAFTING", panel.X + 12, panel.Y + 8, 1, Color.White);
        if(container==null)
        {
        for (int slot = 0; slot < crafting.SlotCount; slot++)
            DrawSlot(crafting[slot], InventoryLayout.CraftingSlot(viewport, crafting.Width, slot), mouse, false);
        Rectangle arrow = InventoryLayout.Arrow(viewport, crafting.Width);
        Fill(new Rectangle(arrow.X, arrow.Center.Y - 1, arrow.Width - 3, 3), Color.LightGray);
        for (int i = 0; i < 5; i++) Fill(new Rectangle(arrow.Right - 5 + i, arrow.Center.Y - 4 + i, 1, 9 - i * 2), Color.LightGray);
        DrawSlot(crafting.Preview, InventoryLayout.Output(viewport, crafting.Width), mouse, false);
        }
        else
        {
            for(int slot=0;slot<container.SlotCount;slot++)DrawSlot(container[slot],InventoryLayout.ContainerSlot(viewport,container.Block,slot),mouse,false);
            if(container.Block==BlockType.Furnace)
            {
                int size=InventoryLayout.SlotSize(viewport);Rectangle input=InventoryLayout.ContainerSlot(viewport,container.Block,0),output=InventoryLayout.ContainerSlot(viewport,container.Block,2);
                var progress=new Rectangle(input.Right+size/2,output.Center.Y-4,Math.Max(4,output.Left-input.Right-size),8);
                Fill(progress,Color.Black);Fill(new Rectangle(progress.X,progress.Y,(int)(progress.Width*container.Progress/Smelting.Seconds),progress.Height),Color.Gold);
                Rectangle fuel=InventoryLayout.ContainerSlot(viewport,container.Block,1);
                var burn=new Rectangle(fuel.Right+4,fuel.Y,6,fuel.Height);Fill(burn,Color.Black);
                int height=(int)(burn.Height*container.Burn/Math.Max(1,container.BurnTotal));
                Fill(new Rectangle(burn.X,burn.Bottom-height,burn.Width,height),Color.OrangeRed);
            }
        }
        Text("INVENTORY", panel.X + 12, InventoryLayout.Slot(viewport, 9).Y - 10, 1, Color.LightGray);
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            Rectangle bounds = InventoryLayout.Slot(viewport, slot);
            DrawSlot(interaction.Inventory[slot], bounds, mouse, slot == interaction.Inventory.SelectedSlot);
        }
        string help = PixelFont.Fit("E / ESC CLOSE   LMB OUTSIDE DROPS HELD STACK", (panel.Width - 24) / 6);
        Text(Message.Length > 0 ? PixelFont.Fit(Message, (panel.Width - 24) / 6) : help,
            panel.X + 12, panel.Bottom - 20, 1, Message.Length > 0 ? Color.Salmon : Color.LightGray);
        if(interaction.Cursor.IsEmpty && _hovered.Durability>0)
        {
            if(_tooltipStack!=_hovered){_tooltipStack=_hovered;_tooltip=$"{Items.Name(_hovered.Item)} {_hovered.Durability}/{Items.Get(_hovered.Item).Durability}";}
            int x=Math.Clamp(mouse.X+8,4,Math.Max(4,viewport.Width-_tooltip.Length*6-8)),y=Math.Clamp(mouse.Y-15,2,Math.Max(2,viewport.Height-13));
            Fill(new Rectangle(x-3,y-2,_tooltip.Length*6+6,11),Color.Black*.9f);Text(_tooltip,x,y,1,Color.White);
        }
        ItemIcons.Draw(_sprites, _atlas, _pixel, interaction.Cursor, new Rectangle(mouse.X + 8, mouse.Y + 8, 42, 42));
        _sprites.End();
    }
    private void DrawSlot(ItemStack stack, Rectangle bounds, MouseState mouse, bool selected)
    {
        if(bounds.Contains(mouse.Position) && !stack.IsEmpty)_hovered=stack;
        Fill(bounds, selected ? new Color(235, 216, 135) : new Color(125, 119, 107));
        Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), bounds.Contains(mouse.Position)
            ? new Color(83, 82, 73) : new Color(32, 34, 33));
        ItemIcons.Draw(_sprites, _atlas, _pixel, stack, bounds);
    }
    private void Text(string text, int x, int y, int scale, Color color) => PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);
    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);
    public void Dispose() { _sprites.Dispose(); _pixel.Dispose(); }
}
