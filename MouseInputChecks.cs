using System;
using Microsoft.Xna.Framework;

namespace MDSM;

internal static class MouseInputChecks
{
    public static void Motion()
    {
        foreach (int fps in new[] { 60, 144, 240, 360, 2000 })
        {
            var source = new MotionSource();
            using var input = new MouseLookInput(source.Capture, source.Read);
            input.SetCaptured(true);
            var camera = new Camera(); float yaw = camera.Yaw, pitch = camera.Pitch;
            for (int frame = 0; frame < fps * 10; frame++)
            {
                Point delta = input.ReadDelta(); camera.Look(delta.X, delta.Y);
            }
            Require(camera.Yaw == yaw && camera.Pitch == pitch, "Stationary high-frequency polling moved the camera.");
            for (int frame = 0; frame < fps; frame++)
            {
                source.Pending += new Point((frame + 1) * 600 / fps - frame * 600 / fps,
                    (frame + 1) * 80 / fps - frame * 80 / fps);
                Point delta = input.ReadDelta(); camera.Look(delta.X, delta.Y);
                Require(input.ReadDelta() == Point.Zero && input.ReadDelta() == Point.Zero, "Repeated polling replayed motion.");
            }
            Require(MathF.Abs(camera.Yaw - 1.5f) < 0.0001f && MathF.Abs(camera.Pitch - (pitch - 0.2f)) < 0.0001f,
                "Relative movement depends on update frequency or changed mouse sensitivity.");
            source.Pending = new Point(1, 0); Point single = input.ReadDelta();
            camera.Look(single.X, single.Y);
            Require(MathF.Abs(camera.Yaw - 1.5025f) < 0.0001f, "Single-pixel input was delayed or suppressed by a dead zone.");
        }
    }

    public static void CaptureTransitions()
    {
        var source = new MotionSource { Pending = new Point(50, -40) };
        using var input = new MouseLookInput(source.Capture, source.Read);
        Require(input.ReadDelta() == Point.Zero && source.Reads == 0, "Menu input was consumed as look movement.");
        input.SetCaptured(true);
        Require(source.CaptureCalls == 1 && input.ReadDelta() == Point.Zero, "Capture applied stale menu or activation movement.");
        source.Pending = new Point(3, -2);
        input.SetCaptured(true);
        Require(source.CaptureCalls == 1 && input.ReadDelta() == new Point(3, -2), "Repeated capture discarded genuine motion.");
        source.Pending = new Point(99, 42); input.SetCaptured(false);
        Require(!source.Captured && !input.Captured && input.ReadDelta() == Point.Zero, "Release did not flush gameplay movement.");
        source.Pending = new Point(-500, 123); input.SetCaptured(true);
        Require(input.ReadDelta() == Point.Zero, "Recapture interpreted cursor restoration as movement.");
        source.Pending = new Point(-1, 1);
        Require(input.ReadDelta() == new Point(-1, 1), "First real movement after recapture was delayed.");
        source.Pending = new Point(11, 12); input.DiscardMotion();
        Require(input.ReadDelta() == Point.Zero, "Resize/focus reset retained stale motion.");
    }

    private sealed class MotionSource
    {
        public Point Pending;
        public bool Captured;
        public int CaptureCalls, Reads;
        public void Capture(bool captured)
        {
            Captured = captured; CaptureCalls++;
            Pending += new Point(17, -13); // Simulate mode-transition noise, consumed only on transitions.
        }
        public Point Read() { Reads++; Point value = Pending; Pending = Point.Zero; return value; }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
