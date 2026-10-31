using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Inventories;

namespace MDSM.Rendering;

internal static class ItemSpriteGeometry
{
    public const int VertexCount = 12;
    public static void Fill(VertexPositionColorTexture[] vertices, int start, Vector3 center, Vector3 right, Vector3 up,ItemId item=default)
    {
        right *= 0.125f; up *= 0.125f;
        float left=.5f/16,rightUv=15.5f/16,top=.5f/16,bottom=15.5f/16;
        if(item.Value>=259 && Items.IsSprite(item))
        {int tile=Items.Get(item).Sprite;left=(tile%8*16+.5f)/128;rightUv=(tile%8*16+15.5f)/128;top=(tile/8*16+.5f)/64;bottom=(tile/8*16+15.5f)/64;}
        var a = new VertexPositionColorTexture(center - right - up, Color.White, new(left, bottom));
        var b = new VertexPositionColorTexture(center + right - up, Color.White, new(rightUv, bottom));
        var c = new VertexPositionColorTexture(center + right + up, Color.White, new(rightUv, top));
        var d = new VertexPositionColorTexture(center - right + up, Color.White, new(left, top));
        vertices[start] = a; vertices[start+1] = c; vertices[start+2] = b;
        vertices[start+3] = a; vertices[start+4] = d; vertices[start+5] = c;
        vertices[start+6] = a; vertices[start+7] = b; vertices[start+8] = c;
        vertices[start+9] = a; vertices[start+10] = c; vertices[start+11] = d;
    }
}
