using System;
using Microsoft.Xna.Framework;
using MDSM.Worlds;

namespace MDSM.Gameplay;

// Gameplay time only; presentation bobbing never affects the head sample.
public sealed class BreathMeter
{
    public const double Capacity = 15;
    public double Remaining { get; private set; } = Capacity;
    public bool Submerged { get; private set; }
    public int Bubbles => Math.Clamp((int)Math.Ceiling((Remaining - 1e-8) / Capacity * 10), 0, 10);
    private double _damageClock;

    public static bool HeadInWater(World world, Vector3 head)
    {
        int x = (int)MathF.Floor(head.X), y = (int)MathF.Floor(head.Y), z = (int)MathF.Floor(head.Z);
        BlockType block = world.GetBlock(x, y, z);
        if (!Water.IsWater(block)) return false;
        float fx = head.X - x, fz = head.Z - z;
        float near = MathHelper.Lerp(Water.CornerHeight(world,x,y,z), Water.CornerHeight(world,x+1,y,z),fx);
        float far = MathHelper.Lerp(Water.CornerHeight(world,x,y,z+1), Water.CornerHeight(world,x+1,y,z+1),fx);
        return head.Y < y + MathHelper.Lerp(near,far,fz) - .001f;
    }
    public void Reset() { Remaining = Capacity; Submerged = false; _damageClock = 0; }
    public void Update(Player player, World world, double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return;
        if (player.Creative || !HeadInWater(world, player.EyePosition)) { Reset(); return; }
        Submerged = true;
        double beyond = Math.Max(0, elapsed - Remaining);
        Remaining = Math.Max(0, Remaining - elapsed);
        _damageClock += beyond;
        while (_damageClock >= 1 - 1e-9)
        { _damageClock = Math.Max(0, _damageClock - 1); player.TakeDamage(2, false); }
    }
}
