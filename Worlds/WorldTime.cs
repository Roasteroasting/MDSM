using System;
using Microsoft.Xna.Framework;

namespace MDSM.Worlds;

// Time is gameplay seconds since sunrise, wrapped to one twenty-minute day.
public sealed class WorldTime
{
    public const double DaySeconds = 1200, Morning = 60;
    public double Seconds { get; private set; } = Morning;
    public double Phase => Seconds / DaySeconds;
    public bool IsNight => Phase >= .55 && Phase < .95;
    public float Daylight
    {
        get
        {
            double p = Phase;
            return (float)(p < .45 ? 14 : p < .55 ? 14 * (.55-p)/.1 : p < .95 ? 0 : 14*(p-.95)/.05);
        }
    }
    public void Advance(double elapsed)
    { if (double.IsFinite(elapsed) && elapsed > 0) Seconds = (Seconds + elapsed % DaySeconds) % DaySeconds; }
    public void Restore(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds >= DaySeconds) throw new ArgumentOutOfRangeException(nameof(seconds));
        Seconds = seconds;
    }
    public bool Sleep() { if (!IsNight) return false; Seconds = Morning; return true; }
    public Vector3 SunDirection => Direction(Phase);
    public Vector3 MoonDirection => -SunDirection;
    private static Vector3 Direction(double phase)
    { float angle = (float)(phase * MathHelper.TwoPi); return new(MathF.Cos(angle), MathF.Sin(angle), .12f*MathF.Cos(angle)); }
    public Color Sky
    {
        get
        {
            float day=Daylight/14;
            Color sky=Color.Lerp(new Color(8,12,29),new Color(143,190,232),day);
            float horizon=MathF.Max(0,1-MathF.Abs(SunDirection.Y)/.32f);
            return Color.Lerp(sky,new Color(199,106,76),horizon*.65f);
        }
    }
}
