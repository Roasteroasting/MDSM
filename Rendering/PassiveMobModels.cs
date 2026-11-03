using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;

namespace MDSM.Rendering;

internal static class PassiveMobModels
{
    public static void Append(CuboidModelBuilder mesh, PassiveMob mob)
    {
        Matrix world = Matrix.CreateRotationY(mob.Yaw) * Matrix.CreateTranslation(mob.Position);
        Matrix body=mob.Baby?Matrix.CreateScale(.5f)*world:world;
        Vector3 shift=mob.Species switch{MobSpecies.Pig=>new(0,-.2f,.3f),MobSpecies.Cow=>new(0,-.65f,.4f),MobSpecies.Sheep=>new(0,-.4f,.3f),_=>new(0,-.3f,.15f)};
        Matrix head=mob.Baby?Matrix.CreateTranslation(shift)*world:world;
        Color tint = mob.HurtSeconds > 0 ? new Color(255, 85, 85) : Color.White;
        float swing = mob.Walking ? MathF.Sin(mob.WalkPhase) * 0.65f : 0;
        switch (mob.Species)
        {
            case MobSpecies.Zombie:
            case MobSpecies.Skeleton:
                bool skeleton=mob.Species==MobSpecies.Skeleton;float limb=skeleton?.065f:.12f;
                mesh.Cuboid(new(-.23f,.65f,-.13f),new(.23f,1.35f,.13f),world,2,tint);
                mesh.Cuboid(new(-.25f,1.3f,-.25f),new(.25f,1.8f,.25f),world,0,tint,1);
                for(int side=-1;side<=1;side+=2)
                {
                    Matrix leg=Matrix.CreateRotationX(swing*side)*Matrix.CreateTranslation(side*.13f,.7f,0)*world;
                    mesh.Cuboid(new(-limb,-.7f,-limb),new(limb,0,limb),leg,3,tint);
                    Matrix arm=Matrix.CreateRotationX(skeleton?.35f:-1.2f)*Matrix.CreateTranslation(side*.31f,1.28f,0)*world;
                    mesh.Cuboid(new(-limb,-.65f,-limb),new(limb,0,limb),arm,0,tint);
                }
                if(skeleton)
                    for(int segment=0;segment<3;segment++)mesh.Cuboid(new(-.48f,.75f+segment*.17f,-.48f),new(-.42f,.95f+segment*.17f,-.40f),world,3,new Color(155,95,45));
                break;
            case MobSpecies.Spider:
                mesh.Cuboid(new(-.32f,.3f,-.2f),new(.32f,.7f,.55f),world,2,tint);
                mesh.Cuboid(new(-.28f,.3f,-.55f),new(.28f,.72f,-.12f),world,0,tint,1);
                for(int leg=0;leg<8;leg++)
                {
                    int side=leg%2==0?-1:1;float z=-.25f+leg/2*.2f;
                    Matrix transform=Matrix.CreateRotationZ(side*.65f+MathF.Sin(mob.WalkPhase+leg)*.12f)*Matrix.CreateTranslation(side*.3f,.5f,z)*world;
                    mesh.Cuboid(new(side<0?-.5f:0,-.08f,-.045f),new(side<0?0:.5f,.02f,.045f),transform,3,tint);
                }
                break;
            case MobSpecies.Pig:
                mesh.Cuboid(new(-0.32f,0.35f,-0.4f), new(0.32f,0.85f,0.4f), body, 0, tint);
                mesh.Cuboid(new(-0.27f,0.45f,-0.65f), new(0.27f,0.95f,-0.25f), head, 0, tint, 1);
                mesh.Cuboid(new(-0.18f,0.50f,-0.78f), new(0.18f,0.70f,-0.65f), head, 0, tint, 2);
                Legs(4, 0.22f, 0.25f, 0.35f, 0.11f);
                break;
            case MobSpecies.Cow:
                mesh.Cuboid(new(-0.42f,0.65f,-0.6f), new(0.42f,1.4f,0.6f), body, 0, tint);
                mesh.Cuboid(new(-0.3f,1,-0.86f), new(0.3f,1.6f,-0.42f), head, 0, tint, 1);
                mesh.Cuboid(new(-0.23f,1,-0.95f), new(0.23f,1.22f,-0.86f), head, 2, tint);
                for (int side = -1; side <= 1; side += 2)
                    mesh.Cuboid(new(side * 0.32f - 0.055f,1.55f,-0.7f), new(side * 0.32f + 0.055f,1.8f,-0.56f), head, 2, tint,
                        region: new Rectangle(35, 0, 9, 3)); // Pale fur above the muzzle supplies ivory horn pixels.
                Legs(4, 0.28f, 0.4f, 0.65f, 0.13f);
                break;
            case MobSpecies.Sheep:
                if(mob.Sheared)mesh.Cuboid(new(-.3f,.45f,-.43f),new(.3f,1.02f,.43f),body,2,tint);
                else mesh.Cuboid(new(-0.4f,0.35f,-0.5f), new(0.4f,1.15f,0.5f), body, 0, tint);
                mesh.Cuboid(new(-0.22f,0.75f,-0.73f), new(0.22f,1.2f,-0.35f), head, 2, tint, 1);
                if(!mob.Sheared)mesh.Cuboid(new(-0.25f,1.12f,-0.65f), new(0.25f,1.3f,-0.35f), head, 0, tint);
                Legs(4, 0.24f, 0.32f, 0.45f, 0.11f);
                break;
            case MobSpecies.Chicken:
                mesh.Cuboid(new(-0.2f,0.25f,-0.2f), new(0.2f,0.65f,0.25f), body, 0, tint);
                mesh.Cuboid(new(-0.16f,0.6f,-0.32f), new(0.16f,0.92f,0), head, 0, tint, 1);
                mesh.Cuboid(new(-0.12f,0.65f,-0.46f), new(0.12f,0.75f,-0.32f), head, 2, tint);
                Color red = mob.HurtSeconds > 0 ? tint : new Color(230, 40, 35);
                mesh.Cuboid(new(-0.06f,0.5f,-0.39f), new(0.06f,0.65f,-0.33f), head, 2, red);
                for (int side = -1; side <= 1; side += 2)
                {
                    Matrix wing = Matrix.CreateRotationZ(swing * 0.4f * side)
                        * Matrix.CreateTranslation(side * 0.2f, 0.58f, 0) * body;
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
                Matrix transform = Matrix.CreateRotationX(angle) * Matrix.CreateTranslation(x, height, z) * body;
                mesh.Cuboid(new(-radius,-height,-radius), new(radius,0,radius), transform, 3, tint);
            }
        }
    }
}
