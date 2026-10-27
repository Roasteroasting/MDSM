using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Worlds;

namespace MDSM.Rendering;

internal readonly record struct WaterFace(Vector3 A, Vector3 B, Vector3 C, Vector3 D, float Shade)
{
    public Vector3 Center => (A + B + C + D) * 0.25f;
    public void CopyTo(VertexPositionColorTexture[] vertices, int offset)
    {
        Color color = new(new Vector4(Shade, Shade, Shade, 0.58f));
        // Standalone tile: stay inside texel centers just like the terrain atlas.
        const float lo = 0.5f / 16, hi = 15.5f / 16;
        var a = new VertexPositionColorTexture(A, color, new Vector2(lo, hi));
        var b = new VertexPositionColorTexture(B, color, new Vector2(hi, hi));
        var c = new VertexPositionColorTexture(C, color, new Vector2(hi, lo));
        var d = new VertexPositionColorTexture(D, color, new Vector2(lo, lo));
        vertices[offset] = a; vertices[offset + 1] = c; vertices[offset + 2] = b;
        vertices[offset + 3] = a; vertices[offset + 4] = d; vertices[offset + 5] = c;
    }
}

internal static class WaterMeshBuilder
{
    public static void Build(World world, Chunk chunk, List<WaterFace> faces)
    {
        faces.Clear();
        if (chunk.WaterCount == 0) return;
        for (int y = 0; y < World.Height; y++)
        for (int z = chunk.OriginZ; z < chunk.OriginZ + Chunk.Size; z++)
        for (int x = chunk.OriginX; x < chunk.OriginX + Chunk.Size; x++)
        {
            if (!Water.IsWater(world.GetBlock(x, y, z))) continue;
            bool Visible(int nx, int ny, int nz) => !Water.IsWater(world.GetBlock(nx, ny, nz)) && !Blocks.IsOpaque(world.GetBlock(nx, ny, nz));
            bool top = Visible(x, y + 1, z), bottom = Visible(x, y - 1, z), near = Visible(x, y, z - 1), far = Visible(x, y, z + 1),
                left = Visible(x - 1, y, z), right = Visible(x + 1, y, z);
            // Deep lakes contain mostly enclosed cells: no corner-height work or geometry for them.
            if (!(top || bottom || near || far || left || right)) continue;
            Vector3 a = new(x, y + Water.CornerHeight(world, x, y, z), z);
            Vector3 b = new(x + 1, y + Water.CornerHeight(world, x + 1, y, z), z);
            Vector3 c = new(x + 1, y + Water.CornerHeight(world, x + 1, y, z + 1), z + 1);
            Vector3 d = new(x, y + Water.CornerHeight(world, x, y, z + 1), z + 1);
            if (top) faces.Add(new WaterFace(d, c, b, a, 1));
            if (near) faces.Add(new WaterFace(new(x + 1, y, z), new(x, y, z), a, b, 0.9f));
            if (far) faces.Add(new WaterFace(new(x, y, z + 1), new(x + 1, y, z + 1), c, d, 0.9f));
            if (left) faces.Add(new WaterFace(new(x, y, z), new(x, y, z + 1), d, a, 0.78f));
            if (right) faces.Add(new WaterFace(new(x + 1, y, z + 1), new(x + 1, y, z), b, c, 0.78f));
            if (bottom) faces.Add(new WaterFace(new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1), new(x, y, z + 1), 0.5f));
        }
    }
}
