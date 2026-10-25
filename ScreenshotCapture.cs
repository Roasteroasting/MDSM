using System;
using System.Globalization;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM;

public sealed class ScreenshotCapture
{
    public string RootDirectory { get; }

    public ScreenshotCapture(string rootDirectory) => RootDirectory = Path.GetFullPath(rootDirectory);

    // Call on the graphics thread, after drawing and before Present. DesktopGL's
    // GetBackBufferData returns top-to-bottom Color pixels, including row alignment.
    // SaveAsPng writes those same pixels; no resizing, flipping, or color processing.
    public string Capture(GraphicsDevice device)
    {
        int width = device.PresentationParameters.BackBufferWidth;
        int height = device.PresentationParameters.BackBufferHeight;
        var pixels = new Color[checked(width * height)];
        device.GetBackBufferData(pixels);
        Texture previousTexture = device.Textures[0];
        try
        {
            using var image = new Texture2D(device, width, height, false, SurfaceFormat.Color);
            image.SetData(pixels);
            return WritePng(stream => image.SaveAsPng(stream, width, height), DateTime.Now);
        }
        finally
        {
            // DesktopGL texture creation/GetData (inside SaveAsPng) bind the
            // temporary texture directly, bypassing MonoGame's binding cache.
            // Its disposal leaves that native binding empty. All MDSM passes
            // use slot 0; dirty it so the previous texture is actually rebound
            // on the next draw, even when no HUD SpriteBatch runs in between.
            // Reassigning only previousTexture would be skipped as unchanged.
            device.Textures[0] = null;
            device.Textures[0] = previousTexture;
        }
    }

    internal string WritePng(Action<Stream> encode, DateTime timestamp)
    {
        Directory.CreateDirectory(RootDirectory);
        string stem = timestamp.ToString("yyyy-MM-dd_HH-mm-ss_fff", CultureInfo.InvariantCulture);
        for (int suffix = 0; ; suffix++)
        {
            string path = Path.Combine(RootDirectory, stem + (suffix == 0 ? "" : "_" + suffix.ToString(CultureInfo.InvariantCulture)) + ".png");
            FileStream file;
            try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException) when (File.Exists(path)) { continue; }
            try
            {
                using (file) encode(file);
                return path;
            }
            catch
            {
                // Only remove the incomplete file this call created, never a prior shot.
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                throw;
            }
        }
    }
}
