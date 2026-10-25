using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MDSM;

internal sealed class MenuController : IDisposable
{
    private readonly SpriteBatch _sprites;
    private readonly Texture2D _pixel;
    private GameState _screen;
    private int _focus;
    private int _selected;
    private int _scroll;
    private bool _selectAll;
    private Point _mouse;
    private IReadOnlyList<WorldEntry> _worlds = Array.Empty<WorldEntry>();
    private string[] _worldDetails = Array.Empty<string>();
    private string _message = "";
    private bool _messageIsError;
    public string NameText { get; private set; } = "";
    public string SeedText { get; private set; } = "";
    public WorldEntry SelectedWorld => _worlds.Count == 0 ? null : _worlds[_selected];

    private readonly record struct Button(string Text, MenuAction Action);
    private static readonly Button[] MainButtons =
    {
        new("CREATE NEW WORLD", MenuAction.NewWorld), new("SELECT WORLD", MenuAction.SelectWorld), new("EXIT", MenuAction.Exit)
    };
    private static readonly Button[] PauseButtons =
    {
        new("RESUME", MenuAction.Resume), new("SAVE WORLD", MenuAction.Save), new("SAVE AND MAIN MENU", MenuAction.SaveAndReturn)
    };
    private static readonly Button[] CreateButtons = { new("CREATE", MenuAction.CreateWorld), new("BACK", MenuAction.Back) };
    private static readonly Button[] SelectButtons = { new("LOAD WORLD", MenuAction.LoadWorld), new("BACK", MenuAction.Back) };
    private Button[] Buttons => _screen switch
    {
        GameState.Paused => PauseButtons,
        GameState.WorldCreation => CreateButtons,
        GameState.WorldSelection => SelectButtons,
        _ => MainButtons
    };
    private int FocusOffset => _screen == GameState.WorldCreation ? 2 : _screen == GameState.WorldSelection ? 1 : 0;

    public MenuController(GraphicsDevice device)
    {
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Open(GameState screen)
    {
        _screen = screen;
        _focus = 0;
        _selectAll = false;
        _message = "";
        if (screen == GameState.WorldCreation)
        {
            NameText = "";
            SeedText = "";
        }
    }

    public void SetWorlds(IReadOnlyList<WorldEntry> worlds)
    {
        _worlds = worlds;
        _worldDetails = new string[worlds.Count];
        _selected = _scroll = 0;
        for (int i = 0; i < worlds.Count; i++)
            _worldDetails[i] = worlds[i].Error == null
                ? $"SEED {worlds[i].Seed} / {worlds[i].ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
                : "UNAVAILABLE - SELECT LOAD FOR DETAILS";
    }

    public void Message(string text, bool error = false)
    {
        _message = Wrap(text, 56);
        _messageIsError = error;
    }

    public void TextInput(char character)
    {
        if (_screen != GameState.WorldCreation || _focus > 1)
            return;
        string current = _focus == 0 ? NameText : SeedText;
        if (character == '\b')
            current = _selectAll ? "" : current.Length > 0 ? current[..^1] : "";
        else
        {
            if (char.IsControl(character)) return;
            if (_focus == 1 && !(character >= '0' && character <= '9') && character != '-') return;
            if (_selectAll) current = "";
            if (current.Length >= (_focus == 0 ? WorldSerializer.MaxNameLength : 11)) return;
            current += character;
        }
        if (_focus == 0) NameText = current;
        else SeedText = current;
        _selectAll = false;
        _message = "";
    }

    public MenuAction Update(KeyboardState keyboard, KeyboardState previousKeyboard,
        MouseState mouse, MouseState previousMouse, Viewport viewport)
    {
        _mouse = mouse.Position;
        bool Pressed(Keys key) => keyboard.IsKeyDown(key) && previousKeyboard.IsKeyUp(key);
        bool click = mouse.LeftButton == ButtonState.Pressed && previousMouse.LeftButton == ButtonState.Released;
        if (Pressed(Keys.Escape))
            return _screen == GameState.Paused ? MenuAction.Resume : _screen == GameState.MainMenu ? MenuAction.None : MenuAction.Back;
        if (_screen == GameState.WorldCreation && _focus < 2 && Pressed(Keys.A)
            && (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)))
            _selectAll = true;

        int direction = Pressed(Keys.Up) ? -1 : Pressed(Keys.Down) ? 1 : 0;
        if (Pressed(Keys.Tab))
            direction = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? -1 : 1;
        if (direction != 0)
        {
            if (_screen == GameState.WorldSelection && _focus == 0 && !Pressed(Keys.Tab) && _worlds.Count > 0)
            {
                _selected = Math.Clamp(_selected + direction, 0, _worlds.Count - 1);
                EnsureVisible(viewport);
            }
            else
                _focus = (_focus + direction + Buttons.Length + FocusOffset) % (Buttons.Length + FocusOffset);
            _selectAll = false;
        }

        if (_screen == GameState.WorldCreation && click)
            for (int i = 0; i < 2; i++)
                if (FieldBounds(viewport, i).Contains(_mouse)) { _focus = i; _selectAll = false; }
        if (_screen == GameState.WorldSelection)
        {
            Rectangle area = ListBounds(viewport);
            int rows = area.Height / 60;
            if (area.Contains(_mouse))
            {
                _scroll = Math.Clamp(_scroll - (mouse.ScrollWheelValue - previousMouse.ScrollWheelValue) / 120,
                    0, Math.Max(0, _worlds.Count - rows));
                int row = (_mouse.Y - area.Y) / 60 + _scroll;
                if (click && row < _worlds.Count) { _selected = row; _focus = 0; _message = ""; }
            }
        }

        for (int i = 0; i < Buttons.Length; i++)
            if (click && ButtonBounds(viewport, i).Contains(_mouse))
            {
                _focus = i + FocusOffset;
                return CanActivate(Buttons[i].Action) ? Buttons[i].Action : MenuAction.None;
            }
        if (Pressed(Keys.Enter))
        {
            if (_screen == GameState.WorldCreation && _focus < 2) return MenuAction.CreateWorld;
            if (_screen == GameState.WorldSelection && _focus == 0)
                return _worlds.Count > 0 ? MenuAction.LoadWorld : MenuAction.None;
            return CanActivate(Buttons[_focus - FocusOffset].Action) ? Buttons[_focus - FocusOffset].Action : MenuAction.None;
        }
        return MenuAction.None;
    }

    private bool CanActivate(MenuAction action) => action != MenuAction.LoadWorld || _worlds.Count > 0;

    private void EnsureVisible(Viewport viewport)
    {
        int rows = ListBounds(viewport).Height / 60;
        _scroll = Math.Clamp(_scroll, Math.Max(0, _selected - rows + 1), _selected);
    }

    private static Rectangle ListBounds(Viewport viewport) => new(
        Math.Max(16, (viewport.Width - 620) / 2), 110, Math.Min(620, viewport.Width - 32),
        Math.Max(60, Math.Min(360, (viewport.Height - 300) / 60 * 60)));

    private static Rectangle FieldBounds(Viewport viewport, int index) =>
        new((viewport.Width - Math.Min(520, viewport.Width - 32)) / 2, viewport.Height / 2 - 95 + index * 80,
            Math.Min(520, viewport.Width - 32), 42);

    private Rectangle ButtonBounds(Viewport viewport, int index)
    {
        if (_screen is GameState.MainMenu or GameState.Paused)
            return new Rectangle((viewport.Width - 380) / 2, viewport.Height / 2 - 50 + index * 58, 380, 46);
        int width = Math.Min(250, (viewport.Width - 48) / 2);
        int y = _screen == GameState.WorldSelection ? ListBounds(viewport).Bottom + 20 : viewport.Height / 2 + 65;
        return new Rectangle(viewport.Width / 2 - width - 8 + index * (width + 16), y, width, 46);
    }

    public void Draw(Viewport viewport, string worldName = null)
    {
        int scale = viewport.Width >= 900 ? 2 : 1;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        if (_screen == GameState.Paused)
            Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.7f);
        string title = _screen switch
        {
            GameState.WorldCreation => "CREATE WORLD",
            GameState.WorldSelection => "SELECT WORLD",
            GameState.Paused => "PAUSED",
            _ => "MDSM"
        };
        Center(title, viewport, _screen == GameState.WorldSelection ? 38 : viewport.Height / 2 - 190, 3, Color.White);
        if (_screen == GameState.MainMenu)
            Center("FINITE WORLDS / INDEV INSPIRED", viewport, viewport.Height / 2 - 140, scale, Color.LightGray);
        if (_screen == GameState.Paused)
            Center(PixelFont.Fit(worldName ?? "", 44), viewport, viewport.Height / 2 - 140, scale, Color.LightGray);

        if (_screen == GameState.WorldCreation)
        {
            DrawField(viewport, 0, "WORLD NAME", NameText, "ENTER A NAME", scale);
            DrawField(viewport, 1, "OPTIONAL INTEGER SEED", SeedText, "RANDOM", scale);
        }
        if (_screen == GameState.WorldSelection)
        {
            Rectangle area = ListBounds(viewport);
            Fill(area, Color.Black * 0.35f);
            int rows = area.Height / 60;
            if (_worlds.Count == 0) Center("NO SAVED WORLDS YET", viewport, area.Y + 22, scale, Color.LightGray);
            for (int row = 0; row < rows && row + _scroll < _worlds.Count; row++)
            {
                int index = row + _scroll;
                var bounds = new Rectangle(area.X + 2, area.Y + row * 60 + 2, area.Width - 4, 56);
                Fill(bounds, index == _selected ? new Color(84, 91, 94) : new Color(43, 50, 58));
                Text(PixelFont.Fit(_worlds[index].Name, (bounds.Width - 16) / (6 * scale)), bounds.X + 8, bounds.Y + 8, scale, Color.White);
                Text(PixelFont.Fit(_worldDetails[index], (bounds.Width - 16) / 6), bounds.X + 8, bounds.Y + 35, 1,
                    _worlds[index].Error == null ? Color.LightGray : Color.Salmon);
            }
        }

        for (int i = 0; i < Buttons.Length; i++)
        {
            Rectangle bounds = ButtonBounds(viewport, i);
            bool active = CanActivate(Buttons[i].Action);
            bool highlighted = _focus == i + FocusOffset || bounds.Contains(_mouse);
            Fill(bounds, highlighted && active ? new Color(235, 216, 135) : new Color(74, 80, 89));
            Fill(new Rectangle(bounds.X + 3, bounds.Y + 3, bounds.Width - 6, bounds.Height - 6), new Color(38, 44, 52));
            Text(Buttons[i].Text, bounds.Center.X - Buttons[i].Text.Length * 3 * scale,
                bounds.Center.Y - 3 * scale, scale, active ? Color.White : Color.Gray);
        }
        if (_message.Length > 0)
        {
            int lines = 1;
            foreach (char character in _message) if (character == '\n') lines++;
            int x = Math.Max(16, viewport.Width / 2 - 168 * scale), y = viewport.Height - 92;
            Fill(new Rectangle(x - 8, y - 8, 336 * scale + 16, lines * 10 * scale + 16), Color.Black * 0.8f);
            Text(_message, x, y, scale, _messageIsError ? new Color(255, 170, 140) : new Color(190, 232, 181));
        }
        _sprites.End();
    }

    private void DrawField(Viewport viewport, int index, string label, string value, string placeholder, int scale)
    {
        Rectangle bounds = FieldBounds(viewport, index);
        Text(label, bounds.X, bounds.Y - 22, scale, Color.LightGray);
        Fill(bounds, _focus == index ? new Color(235, 216, 135) : new Color(74, 80, 89));
        Fill(new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), new Color(24, 29, 35));
        if (_selectAll && _focus == index)
            Fill(new Rectangle(bounds.X + 7, bounds.Y + 8, bounds.Width - 14, bounds.Height - 16), new Color(55, 75, 100));
        string display = value.Length == 0 ? placeholder : value;
        display = PixelFont.Fit(display, (bounds.Width - 28) / (6 * scale));
        Text(display, bounds.X + 8, bounds.Y + 13, scale, value.Length == 0 ? Color.Gray : Color.White);
        if (_focus == index)
            Fill(new Rectangle(bounds.X + 8 + (value.Length == 0 ? 0 : display.Length) * 6 * scale, bounds.Y + 10, scale, 18), Color.White);
    }

    private void Center(string text, Viewport viewport, int y, int scale, Color color) =>
        Text(text, (viewport.Width - text.Length * 6 * scale) / 2, y, scale, color);
    private void Text(string text, int x, int y, int scale, Color color) => PixelFont.Draw(_sprites, _pixel, text, x, y, scale, color);
    private void Fill(Rectangle bounds, Color color) => _sprites.Draw(_pixel, bounds, color);

    private static string Wrap(string text, int width)
    {
        var result = new StringBuilder();
        string remaining = text.Replace('\r', ' ').Replace('\n', ' ');
        for (int line = 0; remaining.Length > 0 && line < 4; line++)
        {
            int count = Math.Min(width, remaining.Length);
            if (count < remaining.Length)
            {
                int space = remaining.LastIndexOf(' ', count - 1, count);
                if (space > 0) count = space;
            }
            result.Append(remaining.AsSpan(0, count));
            remaining = remaining[count..].TrimStart();
            if (remaining.Length > 0) result.Append(line == 3 ? "..." : "\n");
        }
        return result.ToString();
    }

    public void Dispose()
    {
        _sprites.Dispose();
        _pixel.Dispose();
    }
}
