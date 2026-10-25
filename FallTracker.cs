using System;

namespace MDSM;

// Only feet elevation enters this calculation, never velocity or horizontal travel.
public sealed class FallTracker
{
    private float _peak;
    public void Reset(float feetY) => _peak = feetY;
    public void Observe(float feetY) => _peak = Math.Max(_peak, feetY);
    public int Land(float feetY)
    {
        int damage = DamageFor(_peak - feetY);
        Reset(feetY);
        return damage;
    }
    public static int DamageFor(float distance) => !float.IsFinite(distance) || distance <= 3
        ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, (double)distance - 3) + 0.0001));
}
