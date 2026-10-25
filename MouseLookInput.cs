using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;

namespace MDSM;

// Consume motion, rather than inferring it from absolute OS cursor positions and cursor warps.
internal sealed class MouseLookInput : IDisposable
{
    private readonly Action<bool> _capture;
    private readonly Func<Point> _read;
    private readonly IDisposable _native;
    public bool Captured { get; private set; }

    public MouseLookInput() : this(new SdlRelativeMouse()) { }
    private MouseLookInput(SdlRelativeMouse native) : this(native.SetCaptured, native.Read) => _native = native;
    internal MouseLookInput(Action<bool> capture, Func<Point> read) { _capture = capture; _read = read; }

    public void SetCaptured(bool captured)
    {
        if (Captured == captured) return;
        _capture(captured);
        Captured = captured;
        DiscardMotion(); // Flush menu/focus/activation motion, without delaying the next gameplay sample.
    }
    public Point ReadDelta() => Captured ? _read() : Point.Zero;
    public void DiscardMotion() => _read();
    public void Dispose() { SetCaptured(false); _native?.Dispose(); }
}

// MonoGame DesktopGL already ships and initializes SDL2. No additional native dependency.
internal sealed class SdlRelativeMouse : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetRelativeMode(int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetRelativeState(out int x, out int y);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetError();
    private readonly IntPtr _library;
    private readonly SetRelativeMode _setMode;
    private readonly GetRelativeState _getState;
    private readonly GetError _getError;

    public SdlRelativeMouse()
    {
        string library = OperatingSystem.IsWindows() ? "SDL2.dll"
            : OperatingSystem.IsMacOS() ? "libSDL2-2.0.0.dylib" : "libSDL2-2.0.so.0";
        _library = NativeLibrary.Load(library, typeof(Game).Assembly, DllImportSearchPath.SafeDirectories);
        try
        {
            _setMode = Export<SetRelativeMode>("SDL_SetRelativeMouseMode");
            _getState = Export<GetRelativeState>("SDL_GetRelativeMouseState");
            _getError = Export<GetError>("SDL_GetError");
        }
        catch { NativeLibrary.Free(_library); throw; }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
    public void SetCaptured(bool captured)
    {
        if (_setMode(captured ? 1 : 0) != 0)
            throw new InvalidOperationException("Mouse capture failed: " + Marshal.PtrToStringUTF8(_getError()));
    }
    public Point Read() { _getState(out int x, out int y); return new Point(x, y); }
    public void Dispose() => NativeLibrary.Free(_library); // Release our reference; MonoGame still owns its SDL lifetime.
}
