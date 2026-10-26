using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;

namespace MDSM.Rendering;

// Original pig geometry retained unchanged; GPU submission is shared by every species.
internal static class PigRenderer
{
    internal static void Append(CuboidModelBuilder builder, PassiveMob pig)
    {
        Matrix world = Matrix.CreateRotationY(pig.Yaw) * Matrix.CreateTranslation(pig.Position);
        Color tint = pig.HurtSeconds > 0 ? new Color(255, 85, 85) : Color.White;
        builder.Cuboid(new(-0.32f,0.35f,-0.4f), new(0.32f,0.85f,0.4f), world, 0, tint);
        builder.Cuboid(new(-0.27f,0.45f,-0.65f), new(0.27f,0.95f,-0.25f), world, 0, tint, 1);
        builder.Cuboid(new(-0.18f,0.50f,-0.78f), new(0.18f,0.70f,-0.65f), world, 0, tint, 2);
        float swing = pig.Walking ? MathF.Sin(pig.WalkPhase) * 0.65f : 0;
        for (int leg = 0; leg < 4; leg++)
        {
            float x = (leg & 1) == 0 ? -0.22f : 0.22f, z = leg < 2 ? -0.25f : 0.25f;
            float angle = (leg == 0 || leg == 3) ? swing : -swing;
            Matrix transform = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(x, 0.35f, z) * world;
            builder.Cuboid(new(-0.11f,-0.35f,-0.11f), new(0.11f,0,0.11f), transform, 3, tint);
        }
    }

}
