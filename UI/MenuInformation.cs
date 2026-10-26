using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using MDSM.Core;

namespace MDSM.UI;

internal static class MenuInformation
{
    private static readonly string[] Controls =
    {
        "W / A / S / D - Move",
        "Mouse movement - Look around",
        "Left mouse (hold) - Mine / break blocks or punch passive mobs",
        "Right mouse - Place selected block / eat raw porkchop",
        "Space - Jump / hold to swim upward in water",
        "Left Shift (hold) - Crouch",
        "E - Open / close inventory",
        "1-9 or numpad 1-9 - Select hotbar slot",
        "Mouse wheel - Down: next slot / Up: previous slot",
        "F1 - Toggle HUD visibility",
        "F2 - Take screenshot",
        "F3 - Toggle debug overlay",
        "Escape - Pause / resume or close interface",
        "F10 or gamepad Back - Save and quit",
        "",
        "INVENTORY",
        "Left click slot - Pick up / place / merge / swap stack",
        "Right click slot - Take half / place one item",
        "Left click outside panel - Drop cursor stack",
        "",
        "MENUS",
        "Mouse click - Select / activate",
        "Up / Down / Tab / Shift+Tab - Navigate",
        "Enter - Activate / Escape - Back",
        "World list: Up / Down or mouse wheel - Browse",
        "Text fields: Ctrl+A - Select all; Backspace - Erase",
        "World size: Left / Right or click - Change size",
        "FPS slider: Drag or Left / Right - Adjust by 1",
        "FPS slider: Home / End - Minimum / maximum",
        "Uncapped FPS: Click / Space / Enter - Toggle",
        "Controls / About: Wheel / Up / Down / Page Up / Page Down / Home / End - Scroll"
    };

    private static readonly string[] About =
    {
        "MDSM",
        "Mojang Don't Sue Me",
        "",
        "A Minecraft Indev-inspired finite-world voxel sandbox game.",
        "",
        "Project author: Mark Arnold (Roasteroasting)",
        "",
        "The codebase was written using GPT-6.1 Sol, and the game textures were AI-generated."
    };

    public static bool IsInformation(GameState state) => state is GameState.Controls or GameState.About;
    public static int Scale(Viewport viewport) => viewport.Width >= 900 ? 2 : 1;
    public static int LineHeight(Viewport viewport) => 11 * Scale(viewport);
    public static Rectangle Content(Viewport viewport) => new(
        Math.Max(16, (viewport.Width - 760) / 2), 58,
        Math.Min(760, Math.Max(32, viewport.Width - 32)), Math.Max(11, viewport.Height - 134));
    public static Rectangle Back(Viewport viewport) => new(
        (viewport.Width - Math.Min(300, viewport.Width - 32)) / 2, viewport.Height - 46,
        Math.Min(300, viewport.Width - 32), 34);
    public static int VisibleRows(Viewport viewport) => Math.Max(1, Content(viewport).Height / LineHeight(viewport));

    // Rebuilt only on menu entry or resize, never once per rendered frame.
    public static string[] Lines(GameState state, Viewport viewport)
    {
        if (!IsInformation(state)) throw new ArgumentException("Expected an information screen.", nameof(state));
        int width = Math.Max(1, (Content(viewport).Width - 16) / (6 * Scale(viewport)));
        var lines = new List<string>();
        foreach (string paragraph in state == GameState.Controls ? Controls : About)
        {
            string remaining = paragraph;
            while (remaining.Length > width)
            {
                int length = remaining.LastIndexOf(' ', width - 1, width);
                if (length <= 0) length = width;
                lines.Add(remaining[..length]);
                remaining = remaining[length..].TrimStart();
            }
            lines.Add(remaining);
        }
        return lines.ToArray();
    }
}
