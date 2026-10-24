using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class Hud : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly TextureAtlas _atlas;
    private static readonly BlockType[] Slots = { BlockType.Grass, BlockType.Dirt, BlockType.Stone };

    // A tiny built-in font keeps the prototype independent of installed system fonts.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = "01110/10001/10001/11111/10001/10001/10001",
        ['B'] = "11110/10001/10001/11110/10001/10001/11110",
        ['C'] = "01111/10000/10000/10000/10000/10000/01111",
        ['D'] = "11110/10001/10001/10001/10001/10001/11110",
        ['E'] = "11111/10000/10000/11110/10000/10000/11111",
        ['F'] = "11111/10000/10000/11110/10000/10000/10000",
        ['G'] = "01111/10000/10000/10111/10001/10001/01111",
        ['H'] = "10001/10001/10001/11111/10001/10001/10001",
        ['I'] = "11111/00100/00100/00100/00100/00100/11111",
        ['J'] = "00111/00010/00010/00010/10010/10010/01100",
        ['K'] = "10001/10010/10100/11000/10100/10010/10001",
        ['L'] = "10000/10000/10000/10000/10000/10000/11111",
        ['M'] = "10001/11011/10101/10101/10001/10001/10001",
        ['N'] = "10001/11001/10101/10011/10001/10001/10001",
        ['O'] = "01110/10001/10001/10001/10001/10001/01110",
        ['P'] = "11110/10001/10001/11110/10000/10000/10000",
        ['Q'] = "01110/10001/10001/10001/10101/10010/01101",
        ['R'] = "11110/10001/10001/11110/10100/10010/10001",
        ['S'] = "01111/10000/10000/01110/00001/00001/11110",
        ['T'] = "11111/00100/00100/00100/00100/00100/00100",
        ['U'] = "10001/10001/10001/10001/10001/10001/01110",
        ['V'] = "10001/10001/10001/10001/10001/01010/00100",
        ['W'] = "10001/10001/10001/10101/10101/10101/01010",
        ['X'] = "10001/10001/01010/00100/01010/10001/10001",
        ['Y'] = "10001/10001/01010/00100/00100/00100/00100",
        ['Z'] = "11111/00001/00010/00100/01000/10000/11111",
        ['0'] = "01110/10001/10011/10101/11001/10001/01110",
        ['1'] = "00100/01100/00100/00100/00100/00100/01110",
        ['2'] = "01110/10001/00001/00010/00100/01000/11111",
        ['3'] = "11110/00001/00001/01110/00001/00001/11110",
        ['4'] = "00010/00110/01010/10010/11111/00010/00010",
        ['5'] = "11111/10000/10000/11110/00001/00001/11110",
        ['6'] = "01110/10000/10000/11110/10001/10001/01110",
        ['7'] = "11111/00001/00010/00100/01000/01000/01000",
        ['8'] = "01110/10001/10001/01110/10001/10001/01110",
        ['9'] = "01110/10001/10001/01111/00001/00001/01110",
        ['-'] = "00000/00000/00000/11111/00000/00000/00000",
        ['/'] = "00001/00001/00010/00100/01000/10000/10000"
    };

    public Hud(GraphicsDevice device, TextureAtlas atlas)
    {
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _atlas = atlas;
    }

    public void Draw(Viewport viewport, int selectedSlot, bool captured)
    {
        int width = viewport.Width, height = viewport.Height;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);

        int textScale = width >= 900 ? 2 : 1;
        Fill(new Rectangle(8, 8, 294 * textScale, 38 * textScale), Color.Black * 0.55f);
        Text("MDSM - VOXEL PROTOTYPE", 14, 14, textScale, Color.White);
        Text("WASD MOVE / MOUSE LOOK / SPACE JUMP", 14, 14 + 11 * textScale, textScale, Color.White);
        Text("LMB BREAK / RMB PLACE / 1-3 BLOCK / ESC PAUSE", 14, 14 + 22 * textScale, textScale, Color.White);

        const int slotSize = 58;
        int start = (width - Slots.Length * slotSize) / 2;
        int top = height - slotSize - 18;
        for (int i = 0; i < Slots.Length; i++)
        {
            Fill(new Rectangle(start + i * slotSize, top, slotSize - 2, slotSize), i == selectedSlot ? new Color(235, 216, 135) : new Color(74, 80, 89));
            Fill(new Rectangle(start + i * slotSize + 3, top + 3, slotSize - 8, slotSize - 6), new Color(31, 36, 44));
            _sprites.Draw(_atlas.Texture, new Rectangle(start + i * slotSize + 14, top + 9, 30, 30),
                TextureAtlas.Source(Blocks.Tile(Slots[i], Vector3.Up)), Color.White);
            Text((i + 1).ToString(), start + i * slotSize + 6, top + 43, 1, Color.White);
        }
        string name = Slots[selectedSlot].ToString().ToUpperInvariant();
        Text(name, (width - name.Length * 12) / 2, top - 24, 2, Color.White);

        if (captured)
        {
            Fill(new Rectangle(width / 2 - 6, height / 2 - 1, 13, 3), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 1, height / 2 - 6, 3, 13), Color.Black * 0.7f);
            Fill(new Rectangle(width / 2 - 5, height / 2, 11, 1), Color.White);
            Fill(new Rectangle(width / 2, height / 2 - 5, 1, 11), Color.White);
        }
        else
        {
            Fill(new Rectangle(0, 0, width, height), Color.Black * 0.45f);
            CenterText("PAUSED", width, height / 2 - 44, 3);
            CenterText("CLICK TO RESUME", width, height / 2, 2);
            CenterText("F10 TO QUIT", width, height / 2 + 30, 2);
        }

        _sprites.End();
    }

    private void Fill(Rectangle rectangle, Color color) => _sprites.Draw(_pixel, rectangle, color);

    private void CenterText(string text, int width, int y, int scale) =>
        Text(text, (width - text.Length * 6 * scale) / 2, y, scale, Color.White);

    private void Text(string text, int x, int y, int scale, Color color)
    {
        foreach (char character in text)
        {
            if (Glyphs.TryGetValue(character, out string glyph))
                for (int row = 0; row < 7; row++)
                for (int column = 0; column < 5; column++)
                    if (glyph[row * 6 + column] == '1')
                        Fill(new Rectangle(x + column * scale, y + row * scale, scale, scale), color);
            x += 6 * scale;
        }
    }

    public void Dispose()
    {
        _sprites.Dispose();
        _pixel.Dispose();
    }
}
