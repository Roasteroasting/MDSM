using System;

namespace MDSM.Worlds;

// Gameplay balance in watts and active gameplay seconds. No electrical losses.
public static class ElectricalBalance
{
    public const double SolarWatts = 750;
    public const double CoalWatts = 6000;
    public const double CoalBurnSeconds = 80;
    public const double OvercastMultiplier = .3;
    public const double UpdateSeconds = .25;
    public const int SwitchMachineLimit = 5;
    public static string Power(double watts) => watts >= 1000 ? $"{watts / 1000:0.00} KW" : $"{watts:0} W";
}
