using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Gameplay;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.UI;

public sealed class Hud : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    private static readonly string[] SlotNumbers = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
    private readonly string[] _debugLines = new string[12];
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

    public void Draw(Viewport viewport, Inventory inventory, bool captured, bool showDebug,
        Vector3 playerPosition, World world, GameTime gameTime, string notification = null, float miningProgress = 0,
        int health = Player.MaximumHealth, bool visible = true, WorldRenderer renderer = null, FrameDiagnostics diagnostics = null)
    {
        UpdateDebug(showDebug && visible, playerPosition, world, gameTime.ElapsedGameTime.TotalSeconds, renderer, diagnostics);
        if (!visible) return;
        int width = viewport.Width, height = viewport.Height;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);

        int textScale = width >= 900 ? 2 : 1;
        int top = InventoryLayout.HotbarSlot(viewport, 0).Top;
        for (int i = 0; i < Inventory.HotbarSlots; i++)
        {
            Rectangle bounds = InventoryLayout.HotbarSlot(viewport, i);
            Fill(bounds, i == inventory.SelectedSlot ? new Color(235, 216, 135) : new Color(74, 80, 89));
            Fill(new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), new Color(31, 36, 44));
            ItemIcons.Draw(_sprites, _atlas, _pixel, inventory[i], bounds);
            Text(SlotNumbers[i], bounds.X + 4, bounds.Y + 4, 1, Color.LightGray);
        }
        string name = Items.Name(inventory.Selected.Item);
        Text(name, (width - name.Length * 12) / 2, top - 46, 2, Color.White);
        HealthHud.Draw(_sprites, _pixel, viewport, health);

        if (captured)
        {
            Fill(new Rectangle(width / 2 - 6, height / 2 - 1, 13, 3), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 1, height / 2 - 6, 3, 13), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 5, height / 2, 11, 1), Color.White);
            Fill(new Rectangle(width / 2, height / 2 - 5, 1, 11), Color.White);
            if (miningProgress > 0)
            {
                Fill(new Rectangle(width / 2 - 34, height / 2 + 17, 68, 10), Color.Black * 0.8f);
                Fill(new Rectangle(width / 2 - 32, height / 2 + 19, (int)(64 * miningProgress), 6), Color.Orange);
                Text("MINING", width / 2 - 18, height / 2 + 31, 1, Color.White);
            }
        }

        if (!string.IsNullOrEmpty(notification))
            Text(notification, 14, 18 + 38 * textScale, textScale, Color.White);

        if (showDebug)
        {
            int panelWidth = 260 * textScale;
            int panelX = Math.Max(8, width - panelWidth - 8);
            int panelY = width >= 900 ? 8 : 56;
            Fill(new Rectangle(panelX, panelY, panelWidth, (10 + 11 * _debugLines.Length) * textScale), Color.Black * 0.65f);
            for (int i = 0; i < _debugLines.Length; i++)
                Text(_debugLines[i], panelX + 6, panelY + 6 + i * 11 * textScale, textScale, Color.White);
        }

        _sprites.End();
    }

    private void UpdateDebug(bool visible, Vector3 position, World world, double elapsed, WorldRenderer renderer, FrameDiagnostics diagnostics)
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
        _debugLines[6] = $"MESH {renderer?.MeshedChunks ?? 0} GPU {renderer?.GpuChunks ?? 0} DRAW {renderer?.VisibleChunks ?? 0}";
        _debugLines[7] = $"EST VB {((renderer?.BufferBytes ?? 0) + (diagnostics?.DynamicBufferBytes ?? 0)) / 1048576.0:0.0} MIB";
        _debugLines[8] = $"DRAWS {diagnostics?.DrawCalls ?? 0} TRIS {diagnostics?.Triangles ?? 0}";
        _debugLines[9] = $"CPU U {diagnostics?.UpdateMilliseconds ?? 0:0.0} D {diagnostics?.DrawMilliseconds ?? 0:0.0} MS";
        _debugLines[10] = $"MOBS {diagnostics?.Mobs ?? 0} ITEMS {diagnostics?.Items ?? 0} FALL {diagnostics?.FallingBlocks ?? 0}";
        _debugLines[11] = $"ALLOC {diagnostics?.AllocatedBytes ?? 0} B WATER {renderer?.Water.CachedFaces ?? 0} FACES";
    }

    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);

    private void Text(string text, int x, int y, int scale, Color color) =>
        PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);

    public void Dispose()
    {
        _sprites.Dispose();
        _pixel.Dispose();
    }
}
