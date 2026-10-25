using System;
using System.Diagnostics;
using System.Threading;

namespace MDSM.Core;

// Presentation pacing is separate from simulation. Sleep rather than spin while capped.
internal sealed class FrameLimiter
{
    private double _deadline;
    private int _lastFps;
    public void Wait(GameSettings settings)
    {
        if (settings.EffectiveFps is not int fps) { _deadline = 0; _lastFps = 0; return; }
        long now = Stopwatch.GetTimestamp();
        double period = Stopwatch.Frequency / (double)fps;
        if (_lastFps != fps || _deadline == 0) _deadline = now;
        _lastFps = fps;
        _deadline += period;
        // Carry small sleep overshoots forward to preserve the requested average rate.
        // Rebase after a slow frame, rather than rendering a burst to catch up.
        if (now - _deadline > period) _deadline = now;
        double remaining;
        while ((remaining = _deadline - Stopwatch.GetTimestamp()) > 0)
            Thread.Sleep(Math.Max(1, (int)Math.Ceiling(remaining * 1000.0 / Stopwatch.Frequency)));
    }
}

public sealed class SimulationClock
{
    public const double StepSeconds = 1.0 / 120;
    private double _remaining;
    public void Reset() => _remaining = 0;
    public int Advance(double elapsed)
    {
        if (!double.IsFinite(elapsed) || elapsed <= 0) return 0;
        _remaining += elapsed;
        int steps = (int)Math.Floor((_remaining + 1e-9) / StepSeconds);
        _remaining = Math.Max(0, _remaining - steps * StepSeconds);
        return steps;
    }
}
