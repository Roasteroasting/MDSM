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
    public static Rectangle Field(Viewport v, int index)
    {
        int width=Math.Min(520,v.Width-32),left=(v.Width-width)/2;
        if(v.Height<500)
        {
            int top=44+Math.Max(0,(v.Height-240)/2);
            return index<2?new(left+index*((width-8)/2+8),top,(width-8)/2,20):new(left,top+36,width,20);
        }
        return new(left,Math.Max(100,v.Height/2-160)+index*60,width,36);
    }
    public static Rectangle Option(Viewport v,int index)
    {
        Rectangle field=Field(v,2);int width=(field.Width-12)/3;
        return new(field.Left+index*(width+6),field.Bottom+(v.Height<500?16:32),width,v.Height<500?20:36);
    }
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
        Option(v, 2).Bottom + 6, Math.Min(760, v.Width - 32), LineCount(v) * 10 * NoticeScale(v) + 12);
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
            v.Height < 500 ? Math.Max(Notice(v).Bottom + 6, v.Height - 30) : Math.Max(Notice(v).Bottom + 12, v.Height / 2 + 170),
            width, v.Height < 500 ? 24 : 46);
    }
}
