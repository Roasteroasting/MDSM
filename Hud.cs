using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class Hud : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    private static readonly string[] SlotNumbers = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
    private static readonly string[] PageLabels = CreatePageLabels();
    private readonly string[] _debugLines = new string[6];
    private double _fpsElapsed;
    private double _debugElapsed;
    private int _frameCount;
    private int _fps;

    public Hud(GraphicsDevice device, TextureAtlas atlas)
    {
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _atlas = atlas;
    }

    public void InvalidateDebug() => _debugElapsed = 0.2;

    public void Draw(Viewport viewport, BlockPalette palette, bool captured, bool showDebug,
        Vector3 playerPosition, World world, GameTime gameTime, string notification = null)
    {
        UpdateDebug(showDebug, playerPosition, world, gameTime.ElapsedGameTime.TotalSeconds);
        int width = viewport.Width, height = viewport.Height;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);

        int textScale = width >= 900 ? 2 : 1;
        Fill(new Rectangle(8, 8, 294 * textScale, 38 * textScale), Color.Black * 0.55f);
        Text("MDSM / F3 DEBUG / TAB BLOCK PAGE", 14, 14, textScale, Color.White);
        Text("WASD MOVE / MOUSE LOOK / SPACE JUMP", 14, 14 + 11 * textScale, textScale, Color.White);
        Text("LMB BREAK / RMB PLACE / 1-9 BLOCK / ESC PAUSE", 14, 14 + 22 * textScale, textScale, Color.White);

        const int slotSize = 58;
        int start = (width - BlockPalette.SlotsPerPage * slotSize) / 2;
        int top = height - slotSize - 18;
        for (int i = 0; i < BlockPalette.SlotsPerPage; i++)
        {
            BlockType block = palette.AtSlot(i);
            Fill(new Rectangle(start + i * slotSize, top, slotSize - 2, slotSize), i == palette.SelectedSlot ? new Color(235, 216, 135) : new Color(74, 80, 89));
            Fill(new Rectangle(start + i * slotSize + 3, top + 3, slotSize - 8, slotSize - 6), new Color(31, 36, 44));
            if (block != BlockType.Air)
                _sprites.Draw(_atlas.Texture, new Rectangle(start + i * slotSize + 14, top + 9, 30, 30),
                    TextureAtlas.Source(Blocks.Tile(block, Vector3.Up)), Color.White);
            Text(SlotNumbers[i], start + i * slotSize + 6, top + 43, 1, block == BlockType.Air ? Color.Gray : Color.White);
        }
        string name = Blocks.Get(palette.Selected).Name;
        Text(name, (width - name.Length * 12) / 2, top - 24, 2, Color.White);
        string page = PageLabels[palette.Page];
        Text(page, (width - page.Length * 6) / 2, top - 40, 1, Color.White);

        if (captured)
        {
            Fill(new Rectangle(width / 2 - 6, height / 2 - 1, 13, 3), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 1, height / 2 - 6, 3, 13), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 5, height / 2, 11, 1), Color.White);
            Fill(new Rectangle(width / 2, height / 2 - 5, 1, 11), Color.White);
        }

        if (!string.IsNullOrEmpty(notification))
            Text(notification, 14, 18 + 38 * textScale, textScale, Color.White);

        if (showDebug)
        {
            int panelWidth = 180 * textScale;
            int panelX = Math.Max(8, width - panelWidth - 8);
            int panelY = width >= 900 ? 8 : 56;
            Fill(new Rectangle(panelX, panelY, panelWidth, 72 * textScale), Color.Black * 0.65f);
            for (int i = 0; i < _debugLines.Length; i++)
                Text(_debugLines[i], panelX + 6, panelY + 6 + i * 11 * textScale, textScale, Color.White);
        }

        _sprites.End();
    }

    private void UpdateDebug(bool visible, Vector3 position, World world, double elapsed)
    {
        _frameCount++;
        _fpsElapsed += elapsed;
        _debugElapsed += elapsed;
        if (_fpsElapsed >= 0.5)
        {
            _fps = (int)Math.Round(_frameCount / _fpsElapsed);
            _fpsElapsed = 0;
            _frameCount = 0;
        }

        // Cache formatted strings: normal frames do not allocate HUD text.
        if (!visible || (_debugLines[0] != null && _debugElapsed < 0.2))
            return;
        _debugElapsed = 0;
        _debugLines[0] = FormattableString.Invariant($"X {position.X:0.0} Y {position.Y:0.0} Z {position.Z:0.0}");
        _debugLines[1] = $"CHUNK {(int)MathF.Floor(position.X) / Chunk.Size} {(int)MathF.Floor(position.Z) / Chunk.Size}";
        _debugLines[2] = $"FPS {_fps}";
        _debugLines[3] = $"SEED {world.Seed}";
        _debugLines[4] = $"CHUNKS {world.ChunkCount}";
        _debugLines[5] = $"WORLD {world.Width} X {World.Height} X {world.Depth}";
    }

    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);

    private static string[] CreatePageLabels()
    {
        int count = (Blocks.Selection.Length + BlockPalette.SlotsPerPage - 1) / BlockPalette.SlotsPerPage;
        var labels = new string[count];
        for (int i = 0; i < count; i++) labels[i] = $"BLOCKS {i + 1}/{count} - TAB PAGE";
        return labels;
    }

    private void Text(string text, int x, int y, int scale, Color color) =>
        PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);

    public void Dispose()
    {
        _sprites.Dispose();
        _pixel.Dispose();
    }
}
