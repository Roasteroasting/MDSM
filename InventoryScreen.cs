using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MDSM;

internal static class InventoryLayout
{
    public static int SlotSize(Viewport viewport) => Math.Clamp(Math.Min((viewport.Width - 40) / 9, (viewport.Height - 100) / 4), 16, 52);
    public static Rectangle Panel(Viewport viewport)
    {
        int size = SlotSize(viewport), width = size * 9 + 24, height = size * 4 + 88;
        return new Rectangle((viewport.Width - width) / 2, (viewport.Height - height) / 2, width, height);
    }
    public static Rectangle Slot(Viewport viewport, int slot)
    {
        Rectangle panel = Panel(viewport);
        int size = SlotSize(viewport);
        int row = slot < Inventory.HotbarSlots ? 3 : (slot - Inventory.HotbarSlots) / 9;
        int column = slot % 9;
        return new Rectangle(panel.X + 12 + column * size, panel.Y + 32 + row * size + (row == 3 ? 14 : 0), size - 2, size - 2);
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
        if (stack.IsEmpty || !Items.TryGetBlock(stack.Item, out BlockType block)) return;
        int inset = Math.Max(3, bounds.Width / 6);
        sprites.Draw(atlas.Texture, new Rectangle(bounds.X + inset, bounds.Y + inset, bounds.Width - inset * 2, bounds.Height - inset * 2),
            TextureAtlas.Source(Blocks.Tile(block, Vector3.Up)), Color.White);
        string count = Counts[stack.Count];
        int scale = bounds.Width >= 42 ? 2 : 1;
        int x = bounds.Right - 3 - count.Length * 6 * scale, y = bounds.Bottom - 3 - 7 * scale;
        PixelFont.Draw(sprites, pixel, count, x + 1, y + 1, scale, Color.Black);
        PixelFont.Draw(sprites, pixel, count, x, y, scale, Color.White);
    }
}

internal sealed class InventoryScreen : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    public string Message { get; private set; } = "";
    public InventoryScreen(GraphicsDevice device, TextureAtlas atlas)
    {
        _atlas = atlas;
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }
    public void Open() => Message = "";
    public void CannotClose() => Message = "PLACE THE HELD STACK BEFORE CLOSING.";
    public void Update(InventoryInteraction interaction, MouseState mouse, MouseState previous, Viewport viewport)
    {
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
    }
    public void Draw(InventoryInteraction interaction, MouseState mouse, Viewport viewport)
    {
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.6f);
        Rectangle panel = InventoryLayout.Panel(viewport);
        Fill(panel, new Color(150, 141, 125));
        Fill(new Rectangle(panel.X + 3, panel.Y + 3, panel.Width - 6, panel.Height - 6), new Color(51, 48, 43));
        Text("INVENTORY", panel.X + 12, panel.Y + 12, 1, Color.White);
        for (int slot = 0; slot < Inventory.SlotCount; slot++)
        {
            Rectangle bounds = InventoryLayout.Slot(viewport, slot);
            Fill(bounds, slot == interaction.Inventory.SelectedSlot ? new Color(235, 216, 135) : new Color(125, 119, 107));
            Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), bounds.Contains(mouse.Position)
                ? new Color(83, 82, 73) : new Color(32, 34, 33));
            ItemIcons.Draw(_sprites, _atlas, _pixel, interaction.Inventory[slot], bounds);
        }
        string help = PixelFont.Fit("E / ESC CLOSE   LMB MOVE   RMB SPLIT / PLACE ONE", (panel.Width - 24) / 6);
        Text(help, panel.X + 12, panel.Bottom - 20, 1, Color.LightGray);
        if (Message.Length > 0) Text(Message, Math.Max(8, (viewport.Width - Message.Length * 6) / 2), panel.Bottom + 6, 1, Color.Salmon);
        ItemIcons.Draw(_sprites, _atlas, _pixel, interaction.Cursor, new Rectangle(mouse.X + 8, mouse.Y + 8, 42, 42));
        _sprites.End();
    }
    private void Text(string text, int x, int y, int scale, Color color) => PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);
    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);
    public void Dispose() { _sprites.Dispose(); _pixel.Dispose(); }
}
