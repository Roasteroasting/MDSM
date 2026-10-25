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
    private readonly TextureAtlas _atlas;
    private readonly GameSettings _settings;
    private bool _draggingSlider;
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
    public WorldSize SelectedSize { get; private set; } = WorldSize.Normal;
    public WorldEntry SelectedWorld => _worlds.Count == 0 ? null : _worlds[_selected];
    public WorldEntry ManagedWorld { get; private set; }

    private readonly record struct Button(string Text, MenuAction Action);
    private static readonly Button[] MainButtons =
    {
        new("CREATE NEW WORLD", MenuAction.NewWorld), new("SELECT WORLD", MenuAction.SelectWorld),
        new("SETTINGS", MenuAction.OpenSettings), new("EXIT", MenuAction.Exit)
    };
    private static readonly Button[] PauseButtons =
    {
        new("RESUME", MenuAction.Resume), new("SAVE WORLD", MenuAction.Save), new("SAVE AND MAIN MENU", MenuAction.SaveAndReturn),
        new("SETTINGS", MenuAction.OpenSettings)
    };
    private static readonly Button[] DeathButtons = { new("RESPAWN", MenuAction.Respawn), new("MAIN MENU", MenuAction.SaveAndReturn) };
    private static readonly Button[] SettingsButtons = { new("BACK", MenuAction.Back) };
    private static readonly Button[] CreateButtons = { new("CREATE", MenuAction.CreateWorld), new("BACK", MenuAction.Back) };
    private static readonly Button[] SelectButtons =
    {
        new("LOAD WORLD", MenuAction.LoadWorld), new("RENAME WORLD", MenuAction.RenameWorld),
        new("DELETE WORLD", MenuAction.DeleteWorld), new("OPEN SAVES FOLDER", MenuAction.OpenSavesFolder), new("BACK", MenuAction.Back)
    };
    private static readonly Button[] RenameButtons = { new("RENAME", MenuAction.ConfirmRename), new("CANCEL", MenuAction.Back) };
    // Cancel has initial keyboard focus. Deletion always requires a separate explicit action.
    private static readonly Button[] DeleteButtons = { new("CANCEL", MenuAction.Back), new("DELETE PERMANENTLY", MenuAction.ConfirmDelete) };
    private Button[] Buttons => _screen switch
    {
        GameState.Paused => PauseButtons,
        GameState.Death => DeathButtons,
        GameState.Settings => SettingsButtons,
        GameState.WorldCreation => CreateButtons,
        GameState.WorldSelection => SelectButtons,
        GameState.WorldRename => RenameButtons,
        GameState.WorldDeleteConfirmation => DeleteButtons,
        _ => MainButtons
    };
    private int FocusOffset => _screen == GameState.WorldCreation ? 3
        : _screen == GameState.Settings ? 2 : _screen is GameState.WorldSelection or GameState.WorldRename ? 1 : 0;

    public MenuController(GraphicsDevice device, TextureAtlas atlas, GameSettings settings)
    {
        _atlas = atlas;
        _settings = settings;
        _sprites = new SpriteBatch(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    public void Open(GameState screen)
    {
        _screen = screen;
        _focus = 0;
        _selectAll = false;
        _draggingSlider = false;
        _message = "";
        if (screen == GameState.WorldCreation)
        {
            NameText = "";
            SeedText = "";
            SelectedSize = WorldSize.Normal;
        }
        if (screen == GameState.WorldRename) NameText = ManagedWorld.Name;
    }

    public void BeginManage(WorldEntry entry) => ManagedWorld = entry;

    public void SetWorlds(IReadOnlyList<WorldEntry> worlds, string selectedId = null)
    {
        _worlds = worlds;
        _worldDetails = new string[worlds.Count];
        _selected = _scroll = 0;
        for (int i = 0; i < worlds.Count; i++)
        {
            if (worlds[i].Id == selectedId) _selected = i;
            _worldDetails[i] = worlds[i].Error == null
                ? $"{worlds[i].Size ?? WorldSize.Normal} / SEED {worlds[i].Seed} / {worlds[i].ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
                : "UNAVAILABLE - SELECT LOAD FOR DETAILS";
        }
        _scroll = _selected;
    }

    public void Message(string text, bool error = false)
    {
        _message = Wrap(text, 56);
        _messageIsError = error;
    }

    public void TextInput(char character)
    {
        if (!IsTextField)
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
            return _screen == GameState.Paused ? MenuAction.Resume
                : _screen is GameState.MainMenu or GameState.Death ? MenuAction.None : MenuAction.Back;
        if (IsTextField && Pressed(Keys.A)
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

        if (_screen == GameState.Settings)
        {
            int oldCap = _settings.FpsCap; bool oldUncapped = _settings.UncappedFps;
            Rectangle slider = SettingsLayout.Slider(viewport), toggle = SettingsLayout.Toggle(viewport);
            if (click && slider.Contains(_mouse)) { _focus = 0; _draggingSlider = true; }
            if (mouse.LeftButton == ButtonState.Released) _draggingSlider = false;
            if (_draggingSlider) _settings.FpsCap = SettingsLayout.ValueAt(slider, _mouse.X);
            if (_focus == 0)
            {
                if (Pressed(Keys.Left)) _settings.FpsCap--;
                if (Pressed(Keys.Right)) _settings.FpsCap++;
                if (Pressed(Keys.Home)) _settings.FpsCap = GameSettings.MinimumFps;
                if (Pressed(Keys.End)) _settings.FpsCap = GameSettings.MaximumFps;
            }
            if (click && toggle.Contains(_mouse)) { _focus = 1; _settings.UncappedFps = !_settings.UncappedFps; }
            else if (_focus == 1 && (Pressed(Keys.Space) || Pressed(Keys.Enter))) _settings.UncappedFps = !_settings.UncappedFps;
            if (oldCap != _settings.FpsCap || oldUncapped != _settings.UncappedFps) return MenuAction.SettingsChanged;
            if (Pressed(Keys.Enter) && _focus < 2) return MenuAction.None;
        }

        if (click && (_screen is GameState.WorldCreation or GameState.WorldRename))
            for (int i = 0; i < (_screen == GameState.WorldCreation ? 3 : 1); i++)
                if (FieldBounds(viewport, i).Contains(_mouse))
                {
                    _focus = i; _selectAll = false;
                    if (i == 2) CycleSize(1);
                }
        if (_screen == GameState.WorldCreation && _focus == 2)
        {
            if (Pressed(Keys.Left)) CycleSize(-1);
            if (Pressed(Keys.Right)) CycleSize(1);
        }
        if (_screen == GameState.WorldSelection)
        {
            Rectangle area = ListBounds(viewport);
            int rows = area.Height / 60;
            _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _worlds.Count - rows));
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
            if (_screen == GameState.WorldCreation && _focus == 2) { CycleSize(1); return MenuAction.None; }
            if (_screen == GameState.WorldRename && _focus == 0) return MenuAction.ConfirmRename;
            if (_screen == GameState.WorldSelection && _focus == 0)
                return _worlds.Count > 0 ? MenuAction.LoadWorld : MenuAction.None;
            return CanActivate(Buttons[_focus - FocusOffset].Action) ? Buttons[_focus - FocusOffset].Action : MenuAction.None;
        }
        return MenuAction.None;
    }

    private bool IsTextField => (_screen == GameState.WorldCreation && _focus < 2)
        || (_screen == GameState.WorldRename && _focus == 0);
    private void CycleSize(int direction) => SelectedSize = (WorldSize)(((int)SelectedSize + direction + 3) % 3);
    private bool CanActivate(MenuAction action) => action switch
    {
        MenuAction.LoadWorld or MenuAction.DeleteWorld => _worlds.Count > 0,
        MenuAction.RenameWorld => SelectedWorld != null && SelectedWorld.Error == null,
        _ => true
    };

    private void EnsureVisible(Viewport viewport)
    {
        int rows = ListBounds(viewport).Height / 60;
        _scroll = Math.Clamp(_scroll, Math.Max(0, _selected - rows + 1), _selected);
    }

    private static Rectangle ListBounds(Viewport viewport) => new(
        Math.Max(16, (viewport.Width - 620) / 2), 110, Math.Min(620, viewport.Width - 32),
        Math.Max(60, Math.Min(360, (viewport.Height - 390) / 60 * 60)));

    private static Rectangle FieldBounds(Viewport viewport, int index) =>
        new((viewport.Width - Math.Min(520, viewport.Width - 32)) / 2, viewport.Height / 2 - 110 + index * 70,
            Math.Min(520, viewport.Width - 32), 42);

    private Rectangle ButtonBounds(Viewport viewport, int index)
    {
        if (_screen == GameState.Settings) return SettingsLayout.Back(viewport);
        if (_screen is GameState.MainMenu or GameState.Paused or GameState.Death)
        {
            int buttonWidth = Math.Min(380, viewport.Width - 32);
            if (viewport.Height < 400 && _screen != GameState.Death)
            {
                int height = Math.Clamp((viewport.Height - 88) / Buttons.Length - 8, 24, 46);
                return new Rectangle((viewport.Width - buttonWidth) / 2, 64 + index * (height + 8), buttonWidth, height);
            }
            return new Rectangle((viewport.Width - buttonWidth) / 2, viewport.Height / 2 - 50 + index * 58, buttonWidth, 46);
        }
        int width = Math.Min(250, (viewport.Width - 48) / 2);
        if (_screen == GameState.WorldSelection)
        {
            int row = index / 2, column = index % 2;
            int x = index == 4 ? (viewport.Width - width) / 2 : viewport.Width / 2 - width - 8 + column * (width + 16);
            return new Rectangle(x, ListBounds(viewport).Bottom + 16 + row * 54, width, 44);
        }
        int y = _screen == GameState.WorldCreation ? viewport.Height / 2 + 112 : viewport.Height / 2 + 65;
        return new Rectangle(viewport.Width / 2 - width - 8 + index * (width + 16), y, width, 46);
    }

    public void Draw(Viewport viewport, string worldName = null)
    {
        int scale = viewport.Width >= 900 ? 2 : 1;
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        if (_screen is GameState.Paused or GameState.Death || (_screen == GameState.Settings && worldName != null))
            Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.7f);
        else
        {
            const int tileSize = 64;
            Rectangle dirt = TextureAtlas.Source(Blocks.Tile(BlockType.Dirt, Vector3.Up));
            for (int y = 0; y < viewport.Height; y += tileSize)
            for (int x = 0; x < viewport.Width; x += tileSize)
                _sprites.Draw(_atlas.Texture, new Rectangle(x, y, tileSize, tileSize), dirt, Color.White);
            Fill(new Rectangle(0, 0, viewport.Width, viewport.Height), Color.Black * 0.5f);
        }
        string title = _screen switch
        {
            GameState.WorldCreation => "CREATE WORLD",
            GameState.WorldSelection => "SELECT WORLD",
            GameState.WorldRename => "RENAME WORLD",
            GameState.WorldDeleteConfirmation => "DELETE WORLD?",
            GameState.Paused => "PAUSED",
            GameState.Death => "You died!",
            GameState.Settings => "SETTINGS",
            _ => "MDSM"
        };
        int titleScale = _screen == GameState.MainMenu ? Math.Clamp(viewport.Width / 100, 6, 12)
            : _screen == GameState.Death && viewport.Width >= 900 ? 5 : 3;
        int titleY = _screen == GameState.WorldSelection ? 38 : Math.Max(20, viewport.Height / 2 - 210);
        Center(title, viewport, titleY + 3, titleScale, Color.Black * 0.7f);
        Center(title, viewport, titleY, titleScale, Color.White);
        if (_screen == GameState.Paused)
            Center(PixelFont.Fit(worldName ?? "", 44), viewport, viewport.Height / 2 - 140, scale, Color.LightGray);

        if (_screen == GameState.Settings)
        {
            Rectangle slider = SettingsLayout.Slider(viewport), toggle = SettingsLayout.Toggle(viewport);
            Text($"FPS CAP: {_settings.FpsCap}", slider.X, slider.Y - 22, scale, Color.White);
            Fill(slider, _focus == 0 ? new Color(235, 216, 135) : new Color(74, 80, 89));
            Fill(new Rectangle(slider.X + 3, slider.Y + 3, slider.Width - 6, slider.Height - 6), new Color(24, 29, 35));
            int thumb = SettingsLayout.ThumbX(slider, _settings.FpsCap);
            Fill(new Rectangle(thumb - 5, slider.Y + 4, 11, slider.Height - 8), Color.LightGray);
            Fill(toggle, _focus == 1 ? new Color(84, 91, 94) : new Color(38, 44, 52));
            Fill(new Rectangle(toggle.X + 8, toggle.Y + 8, 20, 20), Color.LightGray);
            Fill(new Rectangle(toggle.X + 10, toggle.Y + 10, 16, 16), new Color(24, 29, 35));
            if (_settings.UncappedFps) Text("X", toggle.X + 12, toggle.Y + 11, 2, Color.LightGoldenrodYellow);
            Text("UNCAPPED FPS", toggle.X + 38, toggle.Center.Y - 3 * scale, scale, Color.White);
        }

        if (_screen == GameState.WorldCreation)
        {
            DrawField(viewport, 0, "WORLD NAME", NameText, "ENTER A NAME", scale);
            DrawField(viewport, 1, "OPTIONAL INTEGER SEED", SeedText, "RANDOM", scale);
            DrawField(viewport, 2, "WORLD SIZE - CLICK OR LEFT/RIGHT",
                $"{SelectedSize} / {SelectedSize.Width()} X 64 X {SelectedSize.Width()}", "", scale);
        }
        if (_screen == GameState.WorldRename)
        {
            Center(PixelFont.Fit(ManagedWorld.Name, 48), viewport, viewport.Height / 2 - 160, scale, Color.LightGray);
            DrawField(viewport, 0, "NEW WORLD NAME", NameText, "ENTER A NAME", scale);
        }
        if (_screen == GameState.WorldDeleteConfirmation)
        {
            Center("PERMANENTLY DELETE THIS WORLD?", viewport, viewport.Height / 2 - 110, scale, Color.White);
            Center(ManagedWorld.Name, viewport, viewport.Height / 2 - 65, scale, Color.LightGoldenrodYellow);
            Center("THIS CANNOT BE UNDONE.", viewport, viewport.Height / 2 - 20, scale, Color.Salmon);
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
        if (_focus == index && index < 2)
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
