using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;

namespace MDSM.Rendering;

internal static class PassiveMobModels
{
    public static void Append(CuboidModelBuilder mesh, PassiveMob mob)
    {
        if (mob.Species == MobSpecies.Pig) { PigRenderer.Append(mesh, mob); return; }
        Matrix world = Matrix.CreateRotationY(mob.Yaw) * Matrix.CreateTranslation(mob.Position);
        Color tint = mob.HurtSeconds > 0 ? new Color(255, 85, 85) : Color.White;
        float swing = mob.Walking ? MathF.Sin(mob.WalkPhase) * 0.65f : 0;
        switch (mob.Species)
        {
            case MobSpecies.Cow:
                mesh.Cuboid(new(-0.42f,0.65f,-0.6f), new(0.42f,1.4f,0.6f), world, 0, tint);
                mesh.Cuboid(new(-0.3f,1,-0.86f), new(0.3f,1.6f,-0.42f), world, 0, tint, 1);
                mesh.Cuboid(new(-0.23f,1,-0.95f), new(0.23f,1.22f,-0.86f), world, 2, tint);
                for (int side = -1; side <= 1; side += 2)
                    mesh.Cuboid(new(side * 0.32f - 0.055f,1.55f,-0.7f), new(side * 0.32f + 0.055f,1.8f,-0.56f), world, 2, tint,
                        region: new Rectangle(35, 0, 9, 3)); // Pale fur above the muzzle supplies ivory horn pixels.
                Legs(4, 0.28f, 0.4f, 0.65f, 0.13f);
                break;
            case MobSpecies.Sheep:
                mesh.Cuboid(new(-0.4f,0.35f,-0.5f), new(0.4f,1.15f,0.5f), world, 0, tint);
                mesh.Cuboid(new(-0.22f,0.75f,-0.73f), new(0.22f,1.2f,-0.35f), world, 2, tint, 1);
                mesh.Cuboid(new(-0.25f,1.12f,-0.65f), new(0.25f,1.3f,-0.35f), world, 0, tint);
                Legs(4, 0.24f, 0.32f, 0.45f, 0.11f);
                break;
            case MobSpecies.Chicken:
                mesh.Cuboid(new(-0.2f,0.25f,-0.2f), new(0.2f,0.65f,0.25f), world, 0, tint);
                mesh.Cuboid(new(-0.16f,0.6f,-0.32f), new(0.16f,0.92f,0), world, 0, tint, 1);
                mesh.Cuboid(new(-0.12f,0.65f,-0.46f), new(0.12f,0.75f,-0.32f), world, 2, tint);
                Color red = mob.HurtSeconds > 0 ? tint : new Color(230, 40, 35);
                mesh.Cuboid(new(-0.06f,0.5f,-0.39f), new(0.06f,0.65f,-0.33f), world, 2, red);
                for (int side = -1; side <= 1; side += 2)
                {
                    Matrix wing = Matrix.CreateRotationZ(swing * 0.4f * side)
                        * Matrix.CreateTranslation(side * 0.2f, 0.58f, 0) * world;
                    mesh.Cuboid(new(side < 0 ? -0.13f : 0,-0.28f,-0.12f), new(side < 0 ? 0 : 0.13f,0,0.2f), wing, 0, tint);
                }
                Legs(2, 0.1f, 0.02f, 0.3f, 0.045f);
                break;
        }
        void Legs(int count, float width, float length, float height, float radius)
        {
            for (int leg = 0; leg < count; leg++)
            {
                float x = (leg & 1) == 0 ? -width : width, z = leg < 2 ? -length : length;
                float angle = (leg == 0 || leg == 3) ? swing : -swing;
                Matrix transform = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(x, height, z) * world;
                mesh.Cuboid(new(-radius,-height,-radius), new(radius,0,radius), transform, 3, tint);
            }
        }
    }
}
