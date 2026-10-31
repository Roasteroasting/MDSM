using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;
using MDSM.Entities;
using MDSM.Gameplay;
using MDSM.Inventories;
using MDSM.Rendering;
using MDSM.UI;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class PresentationChecks
{
    public static void SettingsValues()
    {
        var settings = new GameSettings();
        Require(settings.FpsCap == 60 && settings.EffectiveFps == 60 && !settings.UncappedFps, "Default timing changed.");
        settings.FpsCap = int.MinValue; Require(settings.FpsCap == 5, "Minimum is not 5.");
        settings.FpsCap = int.MaxValue; Require(settings.FpsCap == 360, "Maximum is not 360.");
        Rectangle slider = SettingsLayout.Slider(new Viewport(0, 0, 1280, 720));
        for (int value = 5; value <= 360; value++)
        {
            settings.FpsCap = value;
            Require(settings.EffectiveFps == value && SettingsLayout.ValueAt(slider, SettingsLayout.ThumbX(slider, value)) == value,
                "A whole-number slider increment cannot be selected.");
        }
        settings.FpsCap = 144; settings.UncappedFps = true;
        Require(settings.EffectiveFps == null && settings.FpsCap == 144, "Uncapped did not override the cap.");
        settings.FpsCap = 240;
        Require(settings.EffectiveFps == null, "Moving the slider reimposed a cap.");
        settings.UncappedFps = false; Require(settings.EffectiveFps == 240, "Turning uncapped off did not use retained slider value.");
        foreach (Point size in new[] { new Point(320, 240), new Point(640, 480), new Point(1280, 720), new Point(2560, 1080) })
        {
            var viewport = new Viewport(0, 0, size.X, size.Y);
            Rectangle a = SettingsLayout.Slider(viewport), b = SettingsLayout.Toggle(viewport), c = SettingsLayout.Back(viewport);
            Require(a.Left >= 0 && a.Right <= size.X && a.Top >= 22 && a.Bottom < b.Top && b.Bottom < c.Top && c.Bottom <= size.Y,
                "Settings controls overlap or leave the viewport.");
            Require(SettingsLayout.ValueAt(a, a.Left - 100) == 5 && SettingsLayout.ValueAt(a, a.Right + 100) == 360, "Slider dragging escaped bounds.");
        }
    }

    public static void SettingsPersistence()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-settings-check-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "settings.json");
        try
        {
            var store = new SettingsStore(path);
            Require(store.Load().EffectiveFps == 60 && store.LoadError == null, "Missing settings failed defaults.");
            var settings = new GameSettings { FpsCap = 144, UncappedFps = true };
            store.Save(settings);
            var restarted = new SettingsStore(path).Load();
            Require(restarted.FpsCap == 144 && restarted.UncappedFps, "Application restart lost settings.");
            settings.FpsCap = 240; store.Save(settings); restarted = store.Load();
            Require(restarted.FpsCap == 240 && restarted.EffectiveFps == null, "Uncapped slider value did not persist.");
            foreach (string malformed in new[] { "{", "[]", "null", "{\"FpsCap\":\"bad\"}", new string('x', 4097) })
            {
                File.WriteAllText(path, malformed);
                Require(store.Load().EffectiveFps == 60 && store.LoadError != null, "Invalid settings crashed or bypassed defaults.");
            }
            foreach (int bad in new[] { -300, 0, 999 })
            {
                File.WriteAllText(path, "{\"FpsCap\":" + bad + ",\"UncappedFps\":false}");
                Require(store.Load().FpsCap == Math.Clamp(bad, 5, 360), "Out-of-range settings were not validated.");
            }
            File.WriteAllText(path, "{\"FpsCap\":24.5,\"UncappedFps\":false}");
            Require(store.Load().FpsCap == 60, "Fractional cap accepted.");
            store.Save(settings); byte[] valid = File.ReadAllBytes(path);
            if (OperatingSystem.IsWindows())
            {
                using var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                try { store.Save(new GameSettings()); throw new InvalidOperationException("Locked settings replaced."); }
                catch (IOException) { }
                Require(valid.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Failed settings write damaged prior preferences.");
            }
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped root.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    public static void SimulationRates()
    {
        Vector3? reference = null; float? peakReference = null; int? healthReference = null;
        var world = new World(0, WorldSize.Small, false);
        for (int z = 10; z < 50; z++) for (int x = 10; x < 50; x++) world.SetBlock(x, 0, z, BlockType.Stone);
        foreach (int fps in new[] { 5, 30, 60, 144, 240, 360, 997 })
        {
            var clock = new SimulationClock(); var player = new Player(new Vector3(20.5f, 14, 20.5f));
            var inventory = new Inventory(); var items = new DroppedItems(world);
            inventory.SetSlot(0,new ItemStack(Items.Tool(ToolTier.Wood,ToolKind.Pickaxe),1));
            items.Spawn(Items.FromBlock(BlockType.Dirt), 40, new Vector3(12, 3, 12), 1);
            items.Spawn(Items.FromBlock(BlockType.Dirt), 40, new Vector3(12.2f, 3, 12), 1);
            var mining = new MiningController(); world.SetBlock(40, 1, 40, BlockType.Stone);
            var hit = new BlockHit(new Point3(40, 1, 40), new Point3(0, 0, 1), 1);
            int ticks = 0; double seconds = 0;
            for (int frame = 0; frame < fps * 4; frame++)
            {
                seconds += 1.0 / fps;
                int steps = clock.Advance(1.0 / fps); ticks += steps;
                for (int step = 0; step < steps; step++)
                {
                    player.Update(world, Vector3.Right, false, (float)SimulationClock.StepSeconds);
                    items.Update(player, inventory, (float)SimulationClock.StepSeconds);
                    mining.Update(world, items, hit, true, (float)SimulationClock.StepSeconds,inventory);
                }
            }
            Require(ticks == 480 && Math.Abs(seconds - 4) < 1e-6, "Render rate changed simulated/autosave time.");
            Require(player.Health == 10 && player.Grounded && world.GetBlock(40, 1, 40) == BlockType.Air,
                "Low/high FPS changed fall or mining behavior.");
            Require(items.Entities.Count == 3 && items.Entities[0].Count == 64 && items.Entities[1].Count == 16
                && items.Entities[0].PickupDelay == 0 && Math.Abs(items.Entities[0].Position.Y - 1.125f) < 0.001f,
                "Item gravity/delay/merging changed with render rate.");
            if (reference.HasValue) Require(player.Position == reference && player.Health == healthReference, "Movement/falling differs across FPS limits.");
            reference = player.Position; healthReference = player.Health;
            player = new Player(new Vector3(20.5f, 1, 20.5f)); player.Update(world, Vector3.Zero, false, (float)SimulationClock.StepSeconds);
            player.Update(world, Vector3.Zero, true, (float)SimulationClock.StepSeconds, true);
            float peak = player.Position.Y; clock.Reset();
            for (int frame = 0; frame < fps; frame++)
                for (int step = clock.Advance(1.0 / fps); step > 0; step--)
                { player.Update(world, Vector3.Zero, false, (float)SimulationClock.StepSeconds, true); peak = Math.Max(peak, player.Position.Y); }
            Require(player.Grounded && player.Health == 20 && peak > 2 && (!peakReference.HasValue || peak == peakReference), "Crouch jump changed with FPS.");
            peakReference = peak;
            var camera = new Camera();
            for (int frame = 0; frame < fps; frame++) camera.Look((frame + 1) * 600 / fps - frame * 600 / fps, 0);
            Require(Math.Abs(camera.Yaw - 1.5f) < 0.0001f, "Mouse look scales with rendered frames.");
            clock.Advance(0.004); clock.Reset(); Require(clock.Advance(0.004) == 0, "Menu transition retained fractional simulation time.");
        }
        Require(Player.StandingEyeHeight - Player.CrouchingEyeHeight > 0 && Player.StandingEyeHeight - Player.CrouchingEyeHeight < 0.15f,
            "Crouch camera difference was not reduced.");
    }

    public static void ViewModels()
    {
        var inventory = new Inventory(); var geometry = new ViewModelGeometry();
        var selection = ViewModelSelection.Select(GameState.Playing, inventory);
        Require(selection.Kind == ViewModelKind.Hidden, "Empty hotbar shows a view model.");
        geometry.Build(selection); Require(geometry.VertexCount == 0, "Empty hotbar generated geometry.");
        foreach (BlockType block in Blocks.Selection)
        {
            inventory.SetSlot(0, new ItemStack(Items.FromBlock(block), 1));
            selection = ViewModelSelection.Select(GameState.Playing, inventory);
            bool plant = Blocks.Get(block).Shape == BlockShape.Cross;
            Require(selection.Item == inventory.Selected.Item && selection.Kind == (plant ? ViewModelKind.Plant : ViewModelKind.Cube), "Held item ignores inventory/shape.");
            geometry.Build(selection); Require(geometry.VertexCount == (plant ? 24 : 36), "Held geometry changed representation.");
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < geometry.VertexCount; i++)
            {
                var vertex = geometry.Vertices[i]; minZ = Math.Min(minZ, vertex.Position.Z); maxZ = Math.Max(maxZ, vertex.Position.Z);
                Require(vertex.TextureCoordinate.X > 0 && vertex.TextureCoordinate.X < 1 && vertex.TextureCoordinate.Y > 0 && vertex.TextureCoordinate.Y < 1,
                    "Model UVs escaped atlas texel centers.");
            }
            Require(maxZ - minZ > 0.5f, "Held cube/plant became a flat HUD icon.");
        }
        inventory.SelectedSlot = 1; Require(ViewModelSelection.Select(GameState.Playing, inventory).Kind == ViewModelKind.Hidden, "Empty slot did not hide the held item.");
        geometry.Build(ViewModelSelection.Select(GameState.Playing, inventory)); Require(geometry.VertexCount == 0, "Empty slot retained item geometry.");
        inventory.SelectedSlot = 0;
        Require(ViewModelSelection.Select(GameState.Playing, inventory).Item == inventory.Selected.Item, "Returning to an occupied slot lost its item model.");
        foreach (GameState state in Enum.GetValues<GameState>())
            Require(state == GameState.Playing ? ViewModelSelection.Select(state, inventory).Kind != ViewModelKind.Hidden
                : ViewModelSelection.Select(state, inventory).Kind == ViewModelKind.Hidden, "View model leaks into a menu.");
        foreach (Point size in new[] { new Point(320, 240), new Point(640, 480), new Point(1280, 720), new Point(2560, 1080) })
        {
            var viewport = new Viewport(0, 0, size.X, size.Y);
            Rectangle bounds = ViewModelLayout.Bounds(viewport), hotbar = InventoryLayout.HotbarSlot(viewport, 0);
            Require(bounds.Left > size.X / 2 && bounds.Right <= size.X && bounds.Top > 0 && bounds.Bottom <= size.Y
                && !bounds.Intersects(HealthHud.Bounds(viewport)) && !bounds.Intersects(InventoryLayout.HotbarSlot(viewport, 8)), "View model overlaps HUD/crosshair or leaves viewport.");
        }
    }

    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
