using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Gameplay;
using MDSM.Core;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.Worlds;
using MDSM.Worlds.Streaming;

namespace MDSM.UI;

public sealed class Hud : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    private readonly ClassicHudGraphics _graphics;
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
        _graphics=new(device);
    }

    public void InvalidateDebug() => _debugElapsed = 0.2;

    public void Draw(Viewport viewport, Inventory inventory, bool captured, bool showDebug,
        Vector3 playerPosition, World world, GameTime gameTime, string notification = null, float miningProgress = 0,
        int health = Player.MaximumHealth, bool visible = true, WorldRenderer renderer = null, FrameDiagnostics diagnostics = null,float bowPower=0,bool creative=false,BreathMeter breath=null,WorldPosition? exactPosition=null)
    {
        UpdateDebug(showDebug && visible, exactPosition??world.AbsolutePosition(playerPosition), world, gameTime.ElapsedGameTime.TotalSeconds, renderer, diagnostics);
        if (!visible) return;
        int width = viewport.Width, height = viewport.Height;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);

        int textScale = width >= 900 ? 2 : 1;
        int scale=ClassicHudLayout.Scale(viewport);
        _sprites.Draw(_graphics.Bar,ClassicHudLayout.Bar(viewport),Color.White);
        _sprites.Draw(_graphics.Selection,ClassicHudLayout.Selection(viewport,inventory.SelectedSlot),Color.White);
        for (int i = 0; i < Inventory.HotbarSlots; i++)
        {
            Rectangle bounds = InventoryLayout.HotbarSlot(viewport, i);
            ItemIcons.Draw(_sprites, _atlas, _pixel, inventory[i], bounds,inventory[i].Count>1,scale);
        }
        if (!creative) HealthHud.Draw(_sprites, _pixel, viewport, health);
        ArmorHud.Draw(_sprites,_pixel,viewport,inventory.ArmorPoints);
        if (!creative && breath?.Submerged == true) BreathHud.Draw(_sprites, _pixel, viewport, inventory.ArmorPoints, breath.Bubbles);

        if (captured)
        {
            if(bowPower>0)
            {
                Fill(new Rectangle(width/2-34,height/2+17,68,8),Color.Black);
                Fill(new Rectangle(width/2-32,height/2+19,(int)(64*bowPower),4),Color.Gold);
            }
            // World crack overlays provide mining feedback, as in Beta.
        }

        if (!string.IsNullOrEmpty(notification))
        {
            ReadOnlySpan<char> remaining=notification.AsSpan();int max=Math.Max(1,(width-28)/(6*textScale)),y=18+38*textScale;
            while(!remaining.IsEmpty)
            {
                int count=Math.Min(max,remaining.Length);
                if(count<remaining.Length){int space=remaining[..count].LastIndexOf(' ');if(space>0)count=space;}
                PixelFont.Draw(_sprites,_pixel,remaining[..count],14,y,textScale,Color.White);
                remaining=remaining[count..].TrimStart();y+=10*textScale;
            }
        }

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
        if(captured)
        {
            _sprites.Begin(blendState:_graphics.CrosshairBlend,samplerState:SamplerState.PointClamp,depthStencilState:DepthStencilState.None,rasterizerState:RasterizerState.CullNone);
            int cx=viewport.X+width/2,cy=viewport.Y+height/2;
            Fill(new(cx-4*scale,cy,9*scale,scale),Color.White);
            Fill(new(cx,cy-4*scale,scale,4*scale),Color.White);
            Fill(new(cx,cy+scale,scale,4*scale),Color.White);
            _sprites.End();
        }
    }

    private void UpdateDebug(bool visible, WorldPosition position, World world, double elapsed, WorldRenderer renderer, FrameDiagnostics diagnostics)
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
        _debugLines[1] = $"CHUNK {ChunkAddress.At((long)Math.Floor(position.X),(long)Math.Floor(position.Z))}";
        _debugLines[2] = $"FPS {_fps}";
        _debugLines[3] = $"SEED {world.Seed}";
        _debugLines[4] = $"CHUNKS {world.ChunkCount}";
        _debugLines[5] = world.Size.Dimensions();
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
        _graphics.Dispose();
        _pixel.Dispose();
    }
}
