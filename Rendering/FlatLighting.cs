using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Gameplay;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal static class FlatLighting
{
    public static void Apply(Effect effect,GraphicsDevice device,Texture2D texture,World world,Camera camera,float brightness,float alphaCutoff)
    {
        effect.Parameters["View"].SetValue(camera.View);
        effect.Parameters["ViewProjection"].SetValue(camera.View*camera.Projection(device.Viewport.AspectRatio));
        effect.Parameters["Texture"].SetValue(texture);
        effect.Parameters["Daylight"].SetValue(world.Time.Daylight);
        effect.Parameters["Brightness"].SetValue(brightness);
        effect.Parameters["FogColor"].SetValue(world.Time.Sky.ToVector3());
        effect.Parameters["FogStart"].SetValue(WorldRenderer.FogStart);effect.Parameters["FogEnd"].SetValue(WorldRenderer.FogEnd);
        effect.Parameters["AlphaCutoff"].SetValue(alphaCutoff);
        // Readbacks can disturb DesktopGL's native binding behind its cached state.
        device.Textures[0]=null;device.Textures[0]=texture;
    }
    public static Color Encode(Color shade,byte packed)=>new(shade.R,(byte)(packed>>4),(byte)(packed&15),shade.A);
}
