using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Core;
using MDSM.Worlds;

namespace MDSM.UI;

internal static class WorldCreationLayout
{
    public const string NoticeText = "Performance Notice: Massive worlds (2048 \u00d7 128 \u00d7 2048) are resource-intensive. Performance may vary depending on your hardware, and smooth gameplay is not guaranteed.";
    public static bool NoticeVisible(GameState state, WorldSize size) => state == GameState.WorldCreation && size == WorldSize.Massive;
    public static int Scale(Viewport v) => v.Width >= 900 && v.Height >= 600 ? 2 : 1;
    public static int NoticeScale(Viewport v) => v.Width >= 1100 && v.Height >= 600 ? 2 : 1;
    public static Rectangle Field(Viewport v, int index) => new((v.Width - Math.Min(520, v.Width - 32)) / 2,
        v.Height < 400 ? 52 + index * 31 : v.Height / 2 - 110 + index * 70,
        Math.Min(520, v.Width - 32), v.Height < 400 ? 22 : 42);
    public static string[] Lines(Viewport v)
    {
        int characters = Math.Max(1, (Math.Min(760, v.Width - 32) - 16) / (6 * NoticeScale(v)));
        var lines = new List<string>(); string remaining = NoticeText;
        while (remaining.Length > characters)
        {
            int end = remaining.LastIndexOf(' ', characters - 1, characters);
            if (end <= 0) end = characters;
            lines.Add(remaining[..end]); remaining = remaining[end..].TrimStart();
        }
        lines.Add(remaining); return lines.ToArray();
    }
    public static Rectangle Notice(Viewport v) => new((v.Width - Math.Min(760, v.Width - 32)) / 2,
        Field(v, 2).Bottom + 8, Math.Min(760, v.Width - 32), LineCount(v) * 10 * NoticeScale(v) + 12);
    private static int LineCount(Viewport v)
    {
        int width = Math.Max(1, (Math.Min(760, v.Width - 32) - 16) / (6 * NoticeScale(v))), start = 0, count = 1;
        while (NoticeText.Length - start > width)
        {
            int end = NoticeText.LastIndexOf(' ', start + width - 1, width);
            start = end < start ? start + width : end + 1; count++;
        }
        return count;
    }
    public static Rectangle Button(Viewport v, int index)
    {
        int width = Math.Min(250, (v.Width - 48) / 2);
        return new Rectangle(v.Width / 2 - width - 8 + index * (width + 16),
            v.Height < 400 ? Math.Max(Notice(v).Bottom + 6, v.Height - 30) : Math.Max(Notice(v).Bottom + 12, v.Height / 2 + 112),
            width, v.Height < 400 ? 24 : 46);
    }
}
