using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MDSM;

public class Game1 : Game
{
    private const double AutosaveInterval = 5 * 60;
    private readonly GraphicsDeviceManager _graphics;
    private readonly WorldSaveManager _saves;
    private GameSession _session;
    private TextureAtlas _atlas;
    private WorldRenderer _renderer;
    private Hud _hud;
    private MenuController _menu;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private bool _previousBack;
    private BlockHit? _selection;
    private bool _ignoreMouseDelta;
    private readonly BlockPalette _palette = new();
    private bool _showDebug;
    private double _autosaveElapsed;
    private double _noticeSeconds;
    private string _notice;
    public GameState State { get; private set; } = GameState.MainMenu;

    public Game1() : this(new WorldSaveManager()) { }

    public Game1(WorldSaveManager saves)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            PreferredDepthStencilFormat = DepthFormat.Depth24,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        Window.Title = "MDSM";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) => _ignoreMouseDelta = true;
        Window.TextInput += (_, e) =>
        {
            if (IsActive && State == GameState.WorldCreation) _menu?.TextInput(e.Character);
        };
        Deactivated += (_, _) => Pause();
    }

    protected override void LoadContent()
    {
        _atlas = new TextureAtlas(Content);
        _hud = new Hud(GraphicsDevice, _atlas);
        _menu = new MenuController(GraphicsDevice);
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
        SetState(GameState.MainMenu);
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        bool back = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed;
        bool quit = Pressed(keyboard, Keys.F10) || (back && !_previousBack);
        _previousBack = back;
        if (quit)
        {
            Exit(); // OnExiting saves and can cancel a failed exit.
            RememberInput(keyboard, mouse);
            return;
        }

        if (IsActive && _session != null && Pressed(keyboard, Keys.F3)) _showDebug = !_showDebug;

        if (!IsActive)
            Pause();
        else if (State == GameState.Playing)
        {
            if (Pressed(keyboard, Keys.Escape))
                Pause();
            else
            {
                UpdateGameplay(keyboard, mouse, gameTime);
                _autosaveElapsed += gameTime.ElapsedGameTime.TotalSeconds;
                _noticeSeconds -= gameTime.ElapsedGameTime.TotalSeconds;
                if (_noticeSeconds <= 0) _notice = null;
                if (_autosaveElapsed >= AutosaveInterval) SaveCurrentWorld();
            }
        }
        else
            HandleMenuAction(_menu.Update(keyboard, _previousKeyboard, mouse, _previousMouse, GraphicsDevice.Viewport));

        RememberInput(keyboard, mouse);
        base.Update(gameTime);
    }

    private void UpdateGameplay(KeyboardState keyboard, MouseState mouse, GameTime gameTime)
    {
        Camera camera = _session.Camera;
        Player player = _session.Player;
        World world = _session.World;
        int centerX = GraphicsDevice.Viewport.Width / 2, centerY = GraphicsDevice.Viewport.Height / 2;
        if (!_ignoreMouseDelta) camera.Look(mouse.X - centerX, mouse.Y - centerY);
        _ignoreMouseDelta = false;
        Mouse.SetPosition(centerX, centerY);
        Vector3 movement = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) movement += camera.HorizontalForward;
        if (keyboard.IsKeyDown(Keys.S)) movement -= camera.HorizontalForward;
        if (keyboard.IsKeyDown(Keys.D)) movement += camera.Right;
        if (keyboard.IsKeyDown(Keys.A)) movement -= camera.Right;
        player.Update(world, movement, Pressed(keyboard, Keys.Space), (float)gameTime.ElapsedGameTime.TotalSeconds);
        camera.Position = player.EyePosition;
        if (Pressed(keyboard, Keys.Tab)) _palette.NextPage();
        for (int i = 0; i < BlockPalette.SlotsPerPage; i++)
            if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + i)) || keyboard.IsKeyDown((Keys)((int)Keys.NumPad1 + i)))
                _palette.SelectSlot(i);
        _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
        if (_selection is BlockHit hit)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air);
            else if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released && hit.HasPlacementFace)
                world.TryPlace(hit.PlacementCell, _palette.Selected, player.Bounds);
            _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
        }
    }

    private void HandleMenuAction(MenuAction action)
    {
        try
        {
            switch (action)
            {
                case MenuAction.NewWorld:
                    SetState(GameState.WorldCreation);
                    break;
                case MenuAction.SelectWorld:
                    SetState(GameState.WorldSelection);
                    _menu.SetWorlds(Array.Empty<WorldEntry>());
                    _menu.SetWorlds(_saves.ListWorlds());
                    break;
                case MenuAction.CreateWorld:
                    string name = WorldSerializer.ValidateName(_menu.NameText);
                    int seed;
                    if (string.IsNullOrWhiteSpace(_menu.SeedText)) seed = Random.Shared.Next();
                    else if (!int.TryParse(_menu.SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                        throw new ArgumentException("Seed must be a signed 32-bit integer, or blank for a random seed.");
                    var world = new World(seed);
                    var camera = new Camera();
                    var save = new WorldSave(world, name, world.Spawn, camera.Yaw, camera.Pitch);
                    BeginWorld(_saves.Create(save), save);
                    break;
                case MenuAction.LoadWorld:
                    WorldEntry entry = _menu.SelectedWorld;
                    if (entry != null) BeginWorld(entry.Id, _saves.Load(entry.Id));
                    break;
                case MenuAction.Back:
                    SetState(GameState.MainMenu);
                    break;
                case MenuAction.Resume:
                    SetState(GameState.Playing);
                    break;
                case MenuAction.Save:
                    SaveCurrentWorld();
                    break;
                case MenuAction.SaveAndReturn:
                    if (SaveCurrentWorld())
                    {
                        _renderer.Dispose();
                        _renderer = null;
                        _session = null;
                        _selection = null;
                        SetState(GameState.MainMenu);
                        _menu.Message("WORLD SAVED.");
                    }
                    break;
                case MenuAction.Exit:
                    Exit();
                    break;
            }
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            _menu.Message(exception.Message, true);
        }
    }

    private void BeginWorld(string id, WorldSave save)
    {
        var session = new GameSession(id, save);
        var renderer = new WorldRenderer(GraphicsDevice, _atlas, session.World);
        _renderer?.Dispose();
        _renderer = renderer;
        _session = session;
        _palette.Reset();
        _selection = null;
        _notice = null;
        _autosaveElapsed = 0;
        SetState(GameState.Playing);
    }

    private bool SaveCurrentWorld()
    {
        if (_session == null) return true;
        try
        {
            _saves.Save(_session.Id, _session.Capture());
            _autosaveElapsed = 0;
            _notice = "WORLD SAVED";
            _noticeSeconds = 4;
            _menu.Message("WORLD SAVED.");
            return true;
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            _autosaveElapsed = 0;
            Pause();
            _menu.Message("WORLD NOT SAVED. " + exception.Message, true);
            return false;
        }
    }

    private static bool IsStorageError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void Pause()
    {
        if (State == GameState.Playing) SetState(GameState.Paused);
        else IsMouseVisible = true;
    }

    private void SetState(GameState state)
    {
        State = state;
        IsMouseVisible = state != GameState.Playing;
        if (state == GameState.Playing)
        {
            _ignoreMouseDelta = true;
            Mouse.SetPosition(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2);
        }
        else
            _menu?.Open(state);
    }

    private bool Pressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
    private void RememberInput(KeyboardState keyboard, MouseState mouse)
    {
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (!SaveCurrentWorld()) args.Cancel = true;
        base.OnExiting(sender, args);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (GraphicsDevice.Viewport.Width <= 0 || GraphicsDevice.Viewport.Height <= 0) return;
        if (_session != null)
        {
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, WorldRenderer.SkyColor, 1f, 0);
            _renderer.Draw(_session.Camera, State == GameState.Playing ? _selection : null);
            _hud.Draw(GraphicsDevice.Viewport, _palette, State == GameState.Playing, _showDebug,
                _session.Player.Position, _session.World.Seed, gameTime, State == GameState.Playing ? _notice : null);
            if (State == GameState.Paused) _menu.Draw(GraphicsDevice.Viewport, _session.Name);
        }
        else
        {
            GraphicsDevice.Clear(new Color(32, 39, 48));
            _menu.Draw(GraphicsDevice.Viewport);
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _renderer?.Dispose();
        _renderer = null;
        _menu?.Dispose();
        _hud?.Dispose();
        // The game's ContentManager owns and disposes the shared terrain texture.
        base.UnloadContent();
    }
}
