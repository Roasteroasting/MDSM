using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM.Rendering;

internal static class ItemSpriteGeometry
{
    public const int VertexCount = 12;
    public static void Fill(VertexPositionColorTexture[] vertices, int start, Vector3 center, Vector3 right, Vector3 up)
    {
        right *= 0.125f; up *= 0.125f;
        const float low = 0.5f / 16, high = 15.5f / 16;
        var a = new VertexPositionColorTexture(center - right - up, Color.White, new(low, high));
        var b = new VertexPositionColorTexture(center + right - up, Color.White, new(high, high));
        var c = new VertexPositionColorTexture(center + right + up, Color.White, new(high, low));
        var d = new VertexPositionColorTexture(center - right + up, Color.White, new(low, low));
        vertices[start] = a; vertices[start+1] = c; vertices[start+2] = b;
        vertices[start+3] = a; vertices[start+4] = d; vertices[start+5] = c;
        vertices[start+6] = a; vertices[start+7] = b; vertices[start+8] = c;
        vertices[start+9] = a; vertices[start+10] = c; vertices[start+11] = d;
    }
}
