using System;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

// Persistent species IDs. All species share passive movement/combat; differences stay here.
public enum MobSpecies : byte { Pig = 0, Cow = 1, Sheep = 2, Chicken = 3, Zombie=4,Skeleton=5,Spider=6 }
public readonly record struct MobState(MobSpecies Species, Vector3 Position, float Yaw, int Health,bool Sheared=false,double WoolSeconds=0,double BabySeconds=0,double LoveSeconds=0,double BreedCooldown=0,bool Bred=false);
// Legacy pig-only save representation; binary readers/writers retain its existing fields.
public readonly record struct PigState(Vector3 Position, float Yaw, int Health,double BabySeconds=0,double LoveSeconds=0,double BreedCooldown=0,bool Bred=false)
{
    public MobState ToMobState() => new(MobSpecies.Pig, Position, Yaw, Health,BabySeconds:BabySeconds,LoveSeconds:LoveSeconds,BreedCooldown:BreedCooldown,Bred:Bred);
}
public readonly record struct MobDefinition(int MaximumHealth, float HalfWidth, float Height, float WalkSpeed,
    ItemId Drop, int MaximumDrop, string Texture);

public static class MobDefinitions
{
    public const int SpeciesCount = 4;
    private static readonly MobDefinition[] Definitions =
    {
        new(10, 0.45f, 1, 1.5f, Items.RawPorkchop, 2, "Mobs/pig"),
        new(10, 0.45f, 1.8f, 1.5f, Items.Leather, 4, "Mobs/cow"),
        new(10, 0.45f, 1.3f, 1.5f, Items.FromBlock(BlockType.WhiteWool), 4, "Mobs/sheep"),
        new(6, 0.3f, 0.95f, 1.5f, Items.Feather, 4, "Mobs/chicken"),
        new(20,.3f,1.8f,1.5f,Items.RottenFlesh,4,"Mobs/zombie"),
        new(20,.3f,1.8f,1.5f,Items.Bone,4,"Mobs/skeleton"),
        new(16,.7f,.85f,1.5f,Items.String,4,"Mobs/spider")
    };
    public static bool IsDefined(MobSpecies species) => (uint)species < Definitions.Length;
    public static MobDefinition Get(MobSpecies species) => IsDefined(species) ? Definitions[(int)species]
        : throw new ArgumentOutOfRangeException(nameof(species));
}
