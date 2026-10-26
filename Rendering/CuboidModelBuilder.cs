using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MDSM.Rendering;

// Small reusable CPU mesh for textured cuboid models. One batch can contain many mobs.
internal sealed class CuboidModelBuilder(int textureWidth, int textureHeight)
{
    public VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[4096];
    public int Count { get; private set; }
    public void Clear() => Count = 0;

    public void Cuboid(Vector3 min, Vector3 max, Matrix transform, int tile, Color tint, int frontTile = -1, Rectangle? region = null)
    {
        if (Count + 36 > Vertices.Length) Array.Resize(ref Vertices, Vertices.Length * 2);
        Quad(new(max.X,min.Y,max.Z), new(max.X,min.Y,min.Z), new(max.X,max.Y,min.Z), new(max.X,max.Y,max.Z), tile, 0.78f);
        Quad(new(min.X,min.Y,min.Z), new(min.X,min.Y,max.Z), new(min.X,max.Y,max.Z), new(min.X,max.Y,min.Z), tile, 0.78f);
        Quad(new(min.X,max.Y,max.Z), new(max.X,max.Y,max.Z), new(max.X,max.Y,min.Z), new(min.X,max.Y,min.Z), tile, 1);
        Quad(new(min.X,min.Y,min.Z), new(max.X,min.Y,min.Z), new(max.X,min.Y,max.Z), new(min.X,min.Y,max.Z), tile, 0.5f);
        Quad(new(min.X,min.Y,max.Z), new(max.X,min.Y,max.Z), new(max.X,max.Y,max.Z), new(min.X,max.Y,max.Z), tile, 0.86f);
        Quad(new(max.X,min.Y,min.Z), new(min.X,min.Y,min.Z), new(min.X,max.Y,min.Z), new(max.X,max.Y,min.Z), frontTile < 0 ? tile : frontTile, 0.86f);

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int faceTile, float shade)
        {
            int columns = textureWidth / 16;
            Rectangle source = region ?? new Rectangle(faceTile % columns * 16, faceTile / columns * 16, 16, 16);
            Vector2 Uv(float u, float v) => new((source.X + 0.5f + u * (source.Width - 1)) / textureWidth,
                (source.Y + 0.5f + v * (source.Height - 1)) / textureHeight);
            Color color = new(tint.ToVector3() * shade);
            var va = new VertexPositionColorTexture(Vector3.Transform(a, transform), color, Uv(0, 1));
            var vb = new VertexPositionColorTexture(Vector3.Transform(b, transform), color, Uv(1, 1));
            var vc = new VertexPositionColorTexture(Vector3.Transform(c, transform), color, Uv(1, 0));
            var vd = new VertexPositionColorTexture(Vector3.Transform(d, transform), color, Uv(0, 0));
            Vertices[Count++] = va; Vertices[Count++] = vc; Vertices[Count++] = vb;
            Vertices[Count++] = va; Vertices[Count++] = vd; Vertices[Count++] = vc;
        }
    }
}
