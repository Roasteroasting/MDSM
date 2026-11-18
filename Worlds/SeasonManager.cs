using System;

namespace MDSM.Worlds;

public enum SeasonsMode : byte { Off, On, NuclearWinter }
public enum Season : byte { Spring, Summer, Autumn, Winter }

public sealed class SeasonManager
{
    internal const double WinterCropRate = .25;
    internal const double WinterYieldRate = .5;
    internal const double WinterFeedChance = .65;
    internal const double WinterBreedingChance = .5;
    private double _untilRefresh;
    private Season? _override;
    public SeasonsMode Mode { get; }
    public Season Current { get; private set; }
    internal bool Winter => Current == Season.Winter;

    internal SeasonManager(SeasonsMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode;
        Refresh(mode==SeasonsMode.On?DateTime.Today:default);
    }

    internal void Tick(double elapsed)
    {
        if (Mode != SeasonsMode.On || _override.HasValue || (_untilRefresh -= elapsed) > 0) return;
        _untilRefresh = 1;
        Refresh(DateTime.Today);
    }

    internal void Refresh(DateTime localDate) => Current = _override ?? Resolve(Mode, localDate);

    internal bool TryOverride(Season season)
    {
        if (Mode != SeasonsMode.On) return false;
        _override = Current = season;
        return true;
    }

    internal static Season Resolve(SeasonsMode mode, DateTime localDate)
    {
        if (mode == SeasonsMode.Off) return Season.Summer;
        if (mode == SeasonsMode.NuclearWinter) return Season.Winter;
        int date = localDate.Month * 100 + localDate.Day;
        return date switch
        {
            >= 320 and < 620 => Season.Spring,
            >= 620 and < 920 => Season.Summer,
            >= 920 and < 1220 => Season.Autumn,
            _ => Season.Winter
        };
    }

    internal static int WinterYield(int quantity) => (int)Math.Ceiling(quantity*WinterYieldRate);
}
