using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Input;

namespace MDSM.UI;

internal sealed class CommandBar : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private readonly List<(string Text, Color Color)> _lines = new();
    private CommandResult? _result;
    private int _width;
    public CommandLineInput Input { get; } = new();
    public CommandBar(GraphicsDevice device)
    { _sprites = new(device); _pixel = new(device, 1, 1); _pixel.SetData(new[] { Color.White }); }
    public void Reset() { Input.ClearHistory(); _result = null; _lines.Clear(); }
    public void Open() { Input.Open(); }
    public void Show(CommandResult result) { _result = result; _width = 0; }
    internal static int Scale(Viewport viewport) => viewport.Width >= 900 && viewport.Height >= 480 ? 2 : 1;
    internal static Rectangle Bounds(Viewport viewport)
    { int scale = Scale(viewport); return new(8, Math.Max(0, viewport.Height - 24 * scale - 8), Math.Max(1, viewport.Width - 16), 24 * scale); }
    public void Draw(Viewport viewport)
    {
        int scale = Scale(viewport), charWidth = 6 * scale, lineHeight = 10 * scale;
        Rectangle bar = Bounds(viewport);
        int columns = Math.Max(1, (bar.Width - 16) / charWidth);
        if (_width != columns)
        {
            _width = columns; _lines.Clear();
            if (_result is CommandResult result)
                foreach (string paragraph in result.Message.Split('\n'))
                {
                    string remaining = paragraph;
                    while (remaining.Length > columns)
                    {
                        int split = remaining.LastIndexOf(' ', columns - 1, columns);
                        if (split <= 0) split = columns;
                        _lines.Add((remaining[..split], result.Success ? new Color(215,235,215) : new Color(255,175,165)));
                        remaining = remaining[split..].TrimStart();
                    }
                    _lines.Add((remaining, result.Success ? new Color(215,235,215) : new Color(255,175,165)));
                }
        }
        _sprites.Begin(samplerState: SamplerState.PointClamp);
        _sprites.Draw(_pixel, bar, new Color(15,18,22,235));
        // Long commands scroll horizontally so the caret always remains visible.
        int start = Math.Max(0, Input.Caret - columns + 1), length = Math.Min(columns, Input.Text.Length - start);
        if (Input.Selected) _sprites.Draw(_pixel, new Rectangle(bar.X + 8, bar.Y + 5 * scale, Math.Max(1,length * charWidth), 9 * scale), new Color(65,85,120));
        PixelFont.Draw(_sprites, _pixel, Input.Text.AsSpan(start, length), bar.X + 8, bar.Y + 5 * scale, scale, Color.White);
        _sprites.Draw(_pixel, new Rectangle(bar.X + 8 + (Input.Caret - start) * charWidth, bar.Y + 4 * scale, scale, 9 * scale), Color.White);
        string hint = "ENTER: RUN  ESC: CLOSE  TAB: COMPLETE  UP/DOWN: HISTORY";
        PixelFont.Draw(_sprites,_pixel, PixelFont.Fit(hint,columns),bar.X+8,bar.Y-12*scale,scale,new Color(215,215,215));
        var choices=Input.Suggestions;int choiceRows=Math.Min(6,Math.Min(choices.Count,Math.Max(0,(bar.Y-30*scale)/lineHeight)));
        int choiceTop=bar.Y-16*scale-choiceRows*lineHeight;
        if(choiceRows>0)
        {
            int first=Math.Clamp(Input.SuggestionIndex-choiceRows+1,0,Math.Max(0,choices.Count-choiceRows));
            int width=Math.Min(bar.Width,Math.Max(140*scale,Math.Min(columns,40)*charWidth+16));
            _sprites.Draw(_pixel,new Rectangle(bar.X,choiceTop-3*scale,width,choiceRows*lineHeight+6*scale),new Color(15,18,22,240));
            for(int row=0;row<choiceRows;row++)
            {
                int index=first+row,y=choiceTop+row*lineHeight;
                if(index==Input.SuggestionIndex)_sprites.Draw(_pixel,new Rectangle(bar.X+3,y-2*scale,width-6,lineHeight),new Color(60,80,110));
                string label=choices[index]+(index==choices.Count-1?"":row==choiceRows-1?" ...":"");
                PixelFont.Draw(_sprites,_pixel,PixelFont.Fit(label,Math.Max(1,(width-16)/charWidth)),bar.X+8,y,scale,index==Input.SuggestionIndex?Color.Yellow:Color.LightGray);
            }
        }
        int resultBottom=choiceRows>0?choiceTop-8*scale:bar.Y-18*scale;
        int visible = Math.Min(_lines.Count, Math.Max(0, resultBottom / lineHeight));
        if (visible > 0)
        {
            int top = resultBottom - visible * lineHeight;
            _sprites.Draw(_pixel,new Rectangle(bar.X,top-3*scale,bar.Width,visible*lineHeight+6*scale),new Color(15,18,22,215));
            for (int i = 0; i < visible; i++)
                PixelFont.Draw(_sprites,_pixel,_lines[i].Text,bar.X+8,top+i*lineHeight,scale,_lines[i].Color);
        }
        _sprites.End();
    }
    public void Dispose() { _sprites.Dispose(); _pixel.Dispose(); }
}
