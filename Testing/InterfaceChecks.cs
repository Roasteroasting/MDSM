using System;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MDSM.Core;
using MDSM.Input;
using MDSM.Rendering;
using MDSM.UI;

namespace MDSM.Testing;

internal static class InterfaceChecks
{
    public static void PresentationKeys()
    {
        var input = new PresentationInput();
        KeyboardState previous = default;
        void Update(params Keys[] keys)
        {
            var current = new KeyboardState(keys);
            input.Update(current, previous, true, true);
            previous = current;
        }
        Require(input.HudVisible && !input.DebugEnabled && !input.TakeScreenshotRequest(), "Presentation defaults changed.");
        Update(Keys.F3); Require(input.DebugVisible, "F3 did not enable debug.");
        Update(Keys.F1); Require(!input.HudVisible && input.DebugEnabled && !input.DebugVisible, "F1 reset F3 state.");
        for (int i = 0; i < 10000; i++) Update(Keys.F1);
        Require(!input.HudVisible, "Held F1 toggled repeatedly.");
        Update(); Update(Keys.F1); Require(input.DebugVisible, "Restoring HUD lost enabled debug.");
        Update(); Update(Keys.F1); Update(Keys.F3);
        Require(!input.DebugEnabled && !input.DebugVisible, "F3 could not change while HUD hidden.");
        Update(Keys.F1); Require(input.HudVisible && !input.DebugVisible, "HUD restored disabled debug.");
        Update(Keys.F2); Require(input.TakeScreenshotRequest() && !input.TakeScreenshotRequest(), "F2 request was lost or consumed twice.");
        for (int i = 0; i < 10000; i++) { Update(Keys.F2); Require(!input.TakeScreenshotRequest(), "Held F2 repeated capture."); }
        Update(); Update(Keys.F2); Require(input.TakeScreenshotRequest(), "Second F2 press failed.");
        input.Update(new KeyboardState(Keys.F1, Keys.F2, Keys.F3), default, false, true);
        Require(input.HudVisible && !input.DebugEnabled && !input.TakeScreenshotRequest(), "Inactive window processed shortcuts.");
        input.Update(new KeyboardState(Keys.F1, Keys.F2, Keys.F3), default, true, false);
        Require(input.HudVisible && !input.DebugEnabled && input.TakeScreenshotRequest(), "Menu screenshots or world-only toggles failed.");
    }

    public static void ScreenshotFiles()
    {
        string parent = Path.GetFullPath(AppContext.BaseDirectory);
        string root = Path.Combine(parent, ".mdsm-screenshot-check-" + Guid.NewGuid().ToString("N"));
        // A fixed PNG fixture tests file handling without needing a graphics device.
        // Full framebuffer dimensions/orientation/pixels are checked by DesktopGL.
        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a+XcAAAAASUVORK5CYII=");
        try
        {
            var shots = new ScreenshotCapture(Path.Combine(root, "screenshots"));
            Require(!Directory.Exists(shots.RootDirectory), "Screenshot directory should be lazy.");
            var timestamp = new DateTime(2026, 10, 25, 17, 30, 45);
            for (int i = 0; i < 12; i++)
            {
                string file = shots.WritePng(stream => stream.Write(png), timestamp);
                Require(Path.GetDirectoryName(file) == shots.RootDirectory && Path.GetExtension(file) == ".png"
                    && File.ReadAllBytes(file).AsSpan().SequenceEqual(png), "PNG was altered or saved outside screenshots.");
            }
            Require(Directory.GetFiles(shots.RootDirectory).Length == 12, "Colliding timestamps overwrote screenshots.");
            try { shots.WritePng(stream => { stream.WriteByte(0); throw new IOException("Injected encoder failure."); }, timestamp); }
            catch (IOException) { }
            Require(Directory.GetFiles(shots.RootDirectory).Length == 12
                && Directory.GetFiles(shots.RootDirectory).All(path => File.ReadAllBytes(path).AsSpan().SequenceEqual(png)),
                "Failed encoding left a partial image or damaged a prior shot.");
            string blocker = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocker, "untouched");
            try
            {
                new ScreenshotCapture(Path.Combine(blocker, "screenshots")).WritePng(stream => stream.Write(png), timestamp);
                throw new InvalidOperationException("Invalid screenshot directory accepted.");
            }
            catch (IOException) { }
            Require(File.ReadAllText(blocker) == "untouched", "Failed directory creation modified another file.");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.TrimEndingDirectorySeparator(parent)) throw new InvalidOperationException("Cleanup escaped root.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    public static void InformationAndLayouts()
    {
        foreach (Point size in new[] { new Point(320, 240), new Point(640, 480), new Point(801, 603), new Point(1280, 720), new Point(2560, 1080) })
        {
            var viewport = new Viewport(0, 0, size.X, size.Y);
            Rectangle left = SettingsLayout.Information(viewport, 0), right = SettingsLayout.Information(viewport, 1);
            Require(left.Bottom < SettingsLayout.Back(viewport).Top && left.Left >= 0 && right.Right <= size.X
                && left.Right < right.Left && left.Top > SettingsLayout.Toggle(viewport).Bottom, "Settings information buttons overlap.");
            foreach (GameState state in new[] { GameState.Controls, GameState.About })
            {
                Rectangle area = MenuInformation.Content(viewport), back = MenuInformation.Back(viewport);
                string[] lines = MenuInformation.Lines(state, viewport);
                Require(area.Bottom + 16 < back.Top && back.Bottom <= size.Y
                    && MenuInformation.VisibleRows(viewport) * MenuInformation.LineHeight(viewport) <= area.Height,
                    "Information text / scrolling hint / Back overlap.");
                Require(lines.All(line => line.Length * 6 * MenuInformation.Scale(viewport) <= area.Width - 16), "Information text leaves the panel.");
                string text = string.Join(' ', lines);
                if (state == GameState.About)
                    foreach (string phrase in new[] { "MDSM", "Mojang Don't Sue Me", "Minecraft Indev-inspired finite-world voxel sandbox game.",
                        "Mark Arnold (Roasteroasting)", "codebase was written using GPT-6.1 Sol", "game textures were AI-generated." })
                        Require(text.Contains(phrase, StringComparison.Ordinal), "About acknowledgement is missing: " + phrase);
                else
                    foreach (string phrase in new[] { "W / A / S / D", "Mouse movement", "Left mouse (hold)", "Right mouse", "Space - Jump",
                        "Left Shift (hold)", "E - Open / close inventory", "1-9 or numpad 1-9", "Mouse wheel", "F1", "F2", "F3", "Escape", "F10",
                        "Right click slot", "Left click outside panel", "Ctrl+A", "FPS slider" })
                        Require(text.Contains(phrase, StringComparison.Ordinal), "Implemented binding is undocumented: " + phrase);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
