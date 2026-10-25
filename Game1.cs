using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace MDSM;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly World _world = new();
    private readonly Camera _camera = new();
    private readonly Player _player;
    private TextureAtlas _atlas;
    private WorldRenderer _renderer;
    private Hud _hud;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private BlockHit? _selection;
    private bool _captured;
    private bool _ignoreMouseDelta;
    private int _selectedSlot;
    private bool _showDebug;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            PreferredDepthStencilFormat = DepthFormat.Depth24,
            SynchronizeWithVerticalRetrace = true
        };
        Content.RootDirectory = "Content";
        Window.Title = "MDSM - Voxel Prototype";
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) => _ignoreMouseDelta = true;
        Deactivated += (_, _) => ReleaseMouse();
        _player = new Player(_world.Spawn);
    }

    protected override void LoadContent()
    {
        _atlas = new TextureAtlas(Content);
        _renderer = new WorldRenderer(GraphicsDevice, _atlas, _world);
        _hud = new Hud(GraphicsDevice, _atlas);
        _camera.Position = _player.EyePosition;
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
        CaptureMouse();
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();
        if (IsActive && Pressed(keyboard, Keys.F3))
            _showDebug = !_showDebug;
        if (keyboard.IsKeyDown(Keys.F10) || GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed)
        {
            Exit();
            return;
        }

        if (!IsActive)
            ReleaseMouse();
        else if (Pressed(keyboard, Keys.Escape))
            ReleaseMouse();
        else if (!_captured)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                CaptureMouse();
        }
        else
        {
            int centerX = GraphicsDevice.Viewport.Width / 2;
            int centerY = GraphicsDevice.Viewport.Height / 2;
            if (!_ignoreMouseDelta)
                _camera.Look(mouse.X - centerX, mouse.Y - centerY);
            _ignoreMouseDelta = false;
            Mouse.SetPosition(centerX, centerY);

            Vector3 movement = Vector3.Zero;
            if (keyboard.IsKeyDown(Keys.W)) movement += _camera.HorizontalForward;
            if (keyboard.IsKeyDown(Keys.S)) movement -= _camera.HorizontalForward;
            if (keyboard.IsKeyDown(Keys.D)) movement += _camera.Right;
            if (keyboard.IsKeyDown(Keys.A)) movement -= _camera.Right;
            _player.Update(_world, movement, Pressed(keyboard, Keys.Space), (float)gameTime.ElapsedGameTime.TotalSeconds);
            _camera.Position = _player.EyePosition;

            for (int i = 0; i < 3; i++)
                if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + i)) || keyboard.IsKeyDown((Keys)((int)Keys.NumPad1 + i)))
                    _selectedSlot = i;

            _selection = BlockRaycaster.Cast(_world, _camera.Position, _camera.Forward);
            if (_selection is BlockHit hit)
            {
                if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                    _world.SetBlock(hit.Cell.X, hit.Cell.Y, hit.Cell.Z, BlockType.Air);
                else if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released && hit.HasPlacementFace)
                    _world.TryPlace(hit.PlacementCell, (BlockType)(_selectedSlot + 1), _player.Bounds);

                _selection = BlockRaycaster.Cast(_world, _camera.Position, _camera.Forward);
            }
        }

        _previousKeyboard = keyboard;
        _previousMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState keyboard, Keys key) => keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    private void CaptureMouse()
    {
        _captured = true;
        IsMouseVisible = false;
        _ignoreMouseDelta = true;
        Mouse.SetPosition(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2);
    }

    private void ReleaseMouse()
    {
        _captured = false;
        IsMouseVisible = true;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (GraphicsDevice.Viewport.Width <= 0 || GraphicsDevice.Viewport.Height <= 0)
            return;

        GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, WorldRenderer.SkyColor, 1f, 0);
        _renderer.Draw(_camera, _captured ? _selection : null);
        _hud.Draw(GraphicsDevice.Viewport, _selectedSlot, _captured, _showDebug, _player.Position, _world.Seed, gameTime);
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _hud?.Dispose();
        _renderer?.Dispose();
        // The game's ContentManager owns and disposes the shared terrain texture.
        base.UnloadContent();
    }
}
