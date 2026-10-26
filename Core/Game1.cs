using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MDSM.Gameplay;
using MDSM.Entities;
using MDSM.Input;
using MDSM.Inventories;
using MDSM.Persistence;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Core;

public class Game1 : Game
{
    private const double AutosaveInterval = 5 * 60;
    private readonly GraphicsDeviceManager _graphics;
    private readonly WorldSaveManager _saves;
    private readonly SettingsStore _settingsStore;
    private readonly GameSettings _settings;
    private readonly FrameLimiter _frameLimiter = new();
    private readonly SimulationClock _simulationClock = new();
    private readonly PresentationInput _presentation = new();
    private readonly ScreenshotCapture _screenshots;
    private bool _pendingJump, _settingsDirty;
    private double _settingsSaveDelay;
    private GameState _settingsReturnState = GameState.MainMenu;
    private GameSession _session;
    private TextureAtlas _atlas;
    private WorldRenderer _renderer;
    private Hud _hud;
    private MenuController _menu;
    private InventoryScreen _inventoryScreen;
    private FirstPersonRenderer _firstPerson;
    private PigRenderer _pigs;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private bool _previousBack;
    private BlockHit? _selection;
    private MouseLookInput _mouseLook;
    private Point _menuCursor;
    private bool _awaitMouseRelease;
    private readonly HotbarScroll _hotbarScroll = new();
    private double _autosaveElapsed;
    private double _noticeSeconds;
    private string _notice;
    public GameState State { get; private set; } = GameState.MainMenu;

    public Game1() : this(new WorldSaveManager()) { }

    public Game1(WorldSaveManager saves, SettingsStore settingsStore = null)
    {
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
        _screenshots = new ScreenshotCapture(Path.Combine(Path.GetDirectoryName(_saves.RootDirectory), "screenshots"));
        _settingsStore = settingsStore ?? new SettingsStore(Path.Combine(Path.GetDirectoryName(_saves.RootDirectory), "settings.json"));
        _settings = _settingsStore.Load();
        IsFixedTimeStep = false; // Rendering is paced separately; gameplay uses SimulationClock.
        InactiveSleepTime = TimeSpan.Zero; // The same FPS preference applies in every menu/focus state.
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            PreferredDepthStencilFormat = DepthFormat.Depth24,
            SynchronizeWithVerticalRetrace = false
        };
        Content.RootDirectory = "Content";
        Window.Title = "MDSM";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) => _mouseLook?.DiscardMotion();
        Window.TextInput += (_, e) =>
        {
            if (IsActive && (State is GameState.WorldCreation or GameState.WorldRename)) _menu?.TextInput(e.Character);
        };
        Deactivated += (_, _) => Pause();
    }

    protected override void LoadContent()
    {
        _atlas = new TextureAtlas(Content);
        _hud = new Hud(GraphicsDevice, _atlas);
        _menu = new MenuController(GraphicsDevice, _atlas, _settings);
        _inventoryScreen = new InventoryScreen(GraphicsDevice, _atlas);
        _firstPerson = new FirstPersonRenderer(GraphicsDevice, _atlas);
        _pigs = new PigRenderer(GraphicsDevice, Content);
        _mouseLook = new MouseLookInput();
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
        SetState(GameState.MainMenu);
        if (_settingsStore.LoadError != null) _menu.Message(_settingsStore.LoadError, true);
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        bool back = GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed;
        Point lookDelta = _mouseLook.ReadDelta();
        ProcessInput(keyboard, mouse, back, gameTime, lookDelta);
        base.Update(gameTime);
    }

    private void ProcessInput(KeyboardState keyboard, MouseState mouse, bool back, GameTime gameTime, Point lookDelta)
    {
        _presentation.Update(keyboard, _previousKeyboard, IsActive, _session != null);
        if (_session?.Player.IsDead == true && State != GameState.Death)
        {
            EnterDeath(); RememberInput(keyboard, mouse); return;
        }
        bool quit = Pressed(keyboard, Keys.F10) || (back && !_previousBack);
        _previousBack = back;
        if (quit)
        {
            Exit(); // OnExiting saves and can cancel a failed exit.
            RememberInput(keyboard, mouse);
            return;
        }

        if (!IsActive)
            Pause();
        else if (State == GameState.Playing)
        {
            if (Pressed(keyboard, Keys.Escape))
                Pause();
            else if (Pressed(keyboard, Keys.E))
                SetState(GameState.Inventory);
            else
            {
                UpdateGameplay(keyboard, mouse, gameTime, lookDelta);
                if (State == GameState.Playing)
                {
                    _autosaveElapsed += gameTime.ElapsedGameTime.TotalSeconds;
                    _noticeSeconds -= gameTime.ElapsedGameTime.TotalSeconds;
                    if (_noticeSeconds <= 0) _notice = null;
                    if (_autosaveElapsed >= AutosaveInterval) SaveCurrentWorld();
                }
            }
        }
        else if (State == GameState.Inventory)
        {
            if (Pressed(keyboard, Keys.E) || Pressed(keyboard, Keys.Escape))
            {
                if (_session.InventoryInteraction.TryReturnCursor()) SetState(GameState.Playing);
                else _inventoryScreen.CannotClose();
            }
            else _inventoryScreen.Update(_session, mouse, _previousMouse, GraphicsDevice.Viewport);
        }
        else
            HandleMenuAction(_menu.Update(keyboard, _previousKeyboard, mouse, _previousMouse, GraphicsDevice.Viewport));

        if (_settingsDirty && (_settingsSaveDelay -= gameTime.ElapsedGameTime.TotalSeconds) <= 0) SaveSettings();
        RememberInput(keyboard, mouse);
    }

    private void UpdateGameplay(KeyboardState keyboard, MouseState mouse, GameTime gameTime, Point lookDelta)
    {
        Camera camera = _session.Camera;
        Player player = _session.Player;
        World world = _session.World;
        camera.Look(lookDelta.X, lookDelta.Y);
        Vector3 movement = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) movement += camera.HorizontalForward;
        if (keyboard.IsKeyDown(Keys.S)) movement -= camera.HorizontalForward;
        if (keyboard.IsKeyDown(Keys.D)) movement += camera.Right;
        if (keyboard.IsKeyDown(Keys.A)) movement -= camera.Right;
        _hotbarScroll.Apply(_session.Inventory, (long)mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        for (int i = 0; i < Inventory.HotbarSlots; i++)
            if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + i)) || keyboard.IsKeyDown((Keys)((int)Keys.NumPad1 + i)))
                _session.Inventory.SelectedSlot = i;
        _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
        bool held = mouse.LeftButton == ButtonState.Pressed;
        if (_awaitMouseRelease)
        {
            if (mouse.LeftButton == ButtonState.Released && mouse.RightButton == ButtonState.Released) _awaitMouseRelease = false;
            held = false;
        }
        if (!held || _session.Mining.Target != _selection?.Cell) _session.Mining.Reset();
        _pendingJump |= Pressed(keyboard, Keys.Space);
        int steps = _simulationClock.Advance(gameTime.ElapsedGameTime.TotalSeconds);
        for (int i = 0; i < steps; i++)
        {
            const float dt = (float)SimulationClock.StepSeconds;
            player.Update(world, movement, _pendingJump, dt, keyboard.IsKeyDown(Keys.LeftShift));
            _pendingJump = false;
            camera.Position = player.EyePosition;
            if (player.IsDead) { EnterDeath(); return; }
            _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
            _session.Pigs.Update(dt);
            _session.Combat.Update(world, _session.Pigs, _session.Items, _session.Mining,
                camera.Position, camera.Forward, _selection, held, dt);
            _session.Items.Update(player, _session.Inventory, dt);
        }
        _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
        if (!_awaitMouseRelease)
            ItemUse.TryConsume(player, _session.Inventory, mouse.RightButton == ButtonState.Pressed,
                _previousMouse.RightButton == ButtonState.Pressed);
        if (_selection is BlockHit hit)
        {
            if (!_awaitMouseRelease && !held && mouse.RightButton == ButtonState.Pressed
                && _previousMouse.RightButton == ButtonState.Released && hit.HasPlacementFace)
                BlockPlacement.TryPlace(world, _session.Inventory, hit.PlacementCell, player.Bounds);
            _selection = BlockRaycaster.Cast(world, camera.Position, camera.Forward);
        }
        if (EntityRaycaster.Cast(world, _session.Pigs, camera.Position, camera.Forward) != null) _selection = null;
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
                    RefreshWorlds();
                    break;
                case MenuAction.CreateWorld:
                    string name = WorldSerializer.ValidateName(_menu.NameText);
                    int seed;
                    if (string.IsNullOrWhiteSpace(_menu.SeedText)) seed = Random.Shared.Next();
                    else if (!int.TryParse(_menu.SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
                        throw new ArgumentException("Seed must be a signed 32-bit integer, or blank for a random seed.");
                    var world = new World(seed, _menu.SelectedSize);
                    var camera = new Camera();
                    var save = new WorldSave(world, name, world.Spawn, camera.Yaw, camera.Pitch, Inventory.CreateNewWorld(),
                        Pigs: PigPopulation.Generate(world));
                    BeginWorld(_saves.Create(save), save);
                    break;
                case MenuAction.LoadWorld:
                    WorldEntry entry = _menu.SelectedWorld;
                    if (entry != null) BeginWorld(entry.Id, _saves.Load(entry.Id));
                    break;
                case MenuAction.RenameWorld:
                case MenuAction.DeleteWorld:
                    if (_menu.SelectedWorld is WorldEntry selected)
                    {
                        _menu.BeginManage(selected);
                        SetState(action == MenuAction.RenameWorld ? GameState.WorldRename : GameState.WorldDeleteConfirmation);
                    }
                    break;
                case MenuAction.ConfirmRename when State == GameState.WorldRename:
                    string renamedId = _menu.ManagedWorld.Id;
                    _saves.Rename(renamedId, _menu.NameText);
                    RefreshWorlds(renamedId);
                    _menu.Message("WORLD RENAMED.");
                    break;
                case MenuAction.ConfirmDelete when State == GameState.WorldDeleteConfirmation:
                    _saves.Delete(_menu.ManagedWorld.Id);
                    RefreshWorlds();
                    _menu.Message("WORLD DELETED PERMANENTLY.");
                    break;
                case MenuAction.OpenSavesFolder:
                    _saves.OpenSavesFolder();
                    break;
                case MenuAction.OpenSettings when State is GameState.MainMenu or GameState.Paused:
                    _settingsReturnState = State;
                    SetState(GameState.Settings);
                    break;
                case MenuAction.SettingsChanged:
                    _settingsDirty = true;
                    _settingsSaveDelay = 0.5;
                    break;
                case MenuAction.OpenControls when State == GameState.Settings:
                    SetState(GameState.Controls);
                    break;
                case MenuAction.OpenAbout when State == GameState.Settings:
                    SetState(GameState.About);
                    break;
                case MenuAction.Back:
                    if (State is GameState.Controls or GameState.About) SetState(GameState.Settings);
                    else if (State == GameState.Settings)
                    {
                        bool savedSettings = SaveSettings();
                        SetState(_settingsReturnState);
                        if (!savedSettings) _menu.Message("SETTINGS NOT SAVED. CHANGES STILL APPLY THIS SESSION.", true);
                    }
                    else if (State is GameState.WorldRename or GameState.WorldDeleteConfirmation)
                        RefreshWorlds(_menu.ManagedWorld.Id);
                    else SetState(GameState.MainMenu);
                    break;
                case MenuAction.Resume:
                    SetState(GameState.Playing);
                    break;
                case MenuAction.Respawn when State == GameState.Death:
                    _session.Respawn();
                    SetState(_session.InventoryInteraction.Cursor.IsEmpty ? GameState.Playing : GameState.Inventory);
                    if (_session.UndroppedDeathItems)
                    {
                        _notice = "SOME ITEMS COULD NOT DROP AND WERE RETAINED";
                        _noticeSeconds = 6;
                    }
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
                        _saves.SetActiveWorld(null);
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

    private void RefreshWorlds(string selectedId = null)
    {
        SetState(GameState.WorldSelection);
        _menu.SetWorlds(Array.Empty<WorldEntry>());
        _menu.SetWorlds(_saves.ListWorlds(), selectedId);
    }

    private void BeginWorld(string id, WorldSave save)
    {
        var session = new GameSession(id, save);
        var renderer = new WorldRenderer(GraphicsDevice, _atlas, session.World);
        _renderer?.Dispose();
        _renderer = renderer;
        _session = session;
        _saves.SetActiveWorld(id);
        _hud.InvalidateDebug();
        _selection = null;
        _notice = null;
        _autosaveElapsed = 0;
        SetState(GameState.Playing);
    }

    private bool SaveCurrentWorld()
    {
        if (_session == null) return true;
        if (_session.Player.IsDead) EnterDeath();
        if (!_session.InventoryInteraction.TryReturnCursor())
        {
            if (_session.Player.IsDead) _menu.Message("ITEMS COULD NOT DROP. RESPAWN TO ORGANIZE RETAINED ITEMS BEFORE SAVING.", true);
            else { SetState(GameState.Inventory); _inventoryScreen.CannotClose(); }
            return false;
        }
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
            if (_session.Player.IsDead) SetState(GameState.Death);
            else if (State == GameState.Inventory) SetState(GameState.Paused);
            else Pause();
            _menu.Message("WORLD NOT SAVED. " + exception.Message, true);
            return false;
        }
    }

    private static bool IsStorageError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException;

    private bool SaveSettings()
    {
        if (!_settingsDirty) return true;
        try { _settingsStore.Save(_settings); _settingsDirty = false; return true; }
        catch (Exception exception) when (IsStorageError(exception))
        {
            _settingsSaveDelay = double.PositiveInfinity; // Retry on Back/exit or another edit, not every frame.
            _menu.Message("SETTINGS NOT SAVED. " + exception.Message, true);
            return false;
        }
    }

    private void Pause()
    {
        if (State == GameState.Playing) SetState(GameState.Paused);
        else ReleaseMouse();
    }

    private void EnterDeath()
    {
        bool first = _session.HandleDeath();
        if (State != GameState.Death) SetState(GameState.Death);
        _selection = null;
        if (first && _session.UndroppedDeathItems)
            _menu.Message("SOME ITEMS COULD NOT BE DROPPED AND WERE RETAINED. RESPAWN TO RECOVER THEM.", true);
    }

    private void SetState(GameState state)
    {
        _session?.Mining.Reset();
        _simulationClock.Reset();
        _pendingJump = false;
        _hotbarScroll.Reset();
        if (state != GameState.Playing && _session != null)
        {
            _session.Player.StopCrouching();
            _session.Camera.Position = _session.Player.EyePosition;
        }
        State = state;
        if (state == GameState.Playing)
        {
            MouseState mouse = Mouse.GetState();
            _awaitMouseRelease = mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed;
            if (IsActive && !_mouseLook.Captured)
            {
                _menuCursor = mouse.Position;
                try { _mouseLook.SetCaptured(true); }
                catch (InvalidOperationException exception)
                {
                    State = GameState.Paused;
                    IsMouseVisible = true;
                    _menu.Open(GameState.Paused);
                    _menu.Message(exception.Message, true);
                    return;
                }
            }
            IsMouseVisible = !IsActive;
        }
        else
        {
            ReleaseMouse();
            if (state == GameState.Inventory) _inventoryScreen.Open();
            else _menu?.Open(state);
        }
    }

    private void ReleaseMouse()
    {
        if (_mouseLook?.Captured == true)
        {
            _mouseLook.SetCaptured(false);
            Mouse.SetPosition(Math.Clamp(_menuCursor.X, 0, Math.Max(0, GraphicsDevice.Viewport.Width - 1)),
                Math.Clamp(_menuCursor.Y, 0, Math.Max(0, GraphicsDevice.Viewport.Height - 1)));
        }
        IsMouseVisible = true;
    }

    private bool Pressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);
    private void RememberInput(KeyboardState keyboard, MouseState mouse)
    {
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (!SaveCurrentWorld() || !SaveSettings()) args.Cancel = true;
        if (!args.Cancel) ReleaseMouse();
        base.OnExiting(sender, args);
    }

    protected override void EndDraw()
    {
        base.EndDraw();
        _frameLimiter.Wait(_settings);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (GraphicsDevice.Viewport.Width <= 0 || GraphicsDevice.Viewport.Height <= 0) return;
        if (_session != null)
        {
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, WorldRenderer.SkyColor, 1f, 0);
            float progress = State == GameState.Playing ? _session.Mining.Progress : 0;
            _renderer.Draw(_session.Camera, State == GameState.Playing && _presentation.HudVisible ? _selection : null, _session.Items, progress);
            _pigs.Draw(_session.Camera, _session.Pigs);
            if (_presentation.HudVisible) _firstPerson.Draw(State, _session.Inventory);
            _hud.Draw(GraphicsDevice.Viewport, _session.Inventory, State == GameState.Playing, _presentation.DebugEnabled,
                _session.Player.Position, _session.World, gameTime, State == GameState.Playing ? _notice : null, progress,
                _session.Player.Health, _presentation.HudVisible);
            if (State == GameState.Inventory) _inventoryScreen.Draw(_session.InventoryInteraction, Mouse.GetState(), GraphicsDevice.Viewport);
            if (State is GameState.Paused or GameState.Death or GameState.Settings or GameState.Controls or GameState.About)
                _menu.Draw(GraphicsDevice.Viewport, _session.Name);
        }
        else
        {
            GraphicsDevice.Clear(new Color(32, 39, 48));
            _menu.Draw(GraphicsDevice.Viewport);
        }
        base.Draw(gameTime);
        if (_presentation.TakeScreenshotRequest())
        {
            try { _screenshots.Capture(GraphicsDevice); }
            catch (Exception exception) when (IsStorageError(exception) || exception is InvalidOperationException)
            {
                string message = "SCREENSHOT NOT SAVED. " + exception.Message;
                _menu.Message(message, true);
                _notice = message;
                _noticeSeconds = 8;
                Console.Error.WriteLine(message);
            }
        }
    }

    protected override void UnloadContent()
    {
        ReleaseMouse();
        _mouseLook?.Dispose();
        _mouseLook = null;
        _renderer?.Dispose();
        _renderer = null;
        _saves.SetActiveWorld(null);
        _menu?.Dispose();
        _inventoryScreen?.Dispose();
        _hud?.Dispose();
        _firstPerson?.Dispose();
        _pigs?.Dispose();
        // The game's ContentManager owns and disposes the shared terrain texture.
        base.UnloadContent();
    }
}
