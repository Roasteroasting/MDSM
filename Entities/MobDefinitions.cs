using System;
using Microsoft.Xna.Framework;
using MDSM.Inventories;
using MDSM.Worlds;

namespace MDSM.Entities;

// Persistent species IDs. All species share passive movement/combat; differences stay here.
public enum MobSpecies : byte { Pig = 0, Cow = 1, Sheep = 2, Chicken = 3 }
public readonly record struct MobState(MobSpecies Species, Vector3 Position, float Yaw, int Health);
public readonly record struct MobDefinition(int MaximumHealth, float HalfWidth, float Height, float WalkSpeed,
    ItemId Drop, int MaximumDrop, string Texture);

public static class MobDefinitions
{
    public const int SpeciesCount = 4;
    private static readonly MobDefinition[] Definitions =
    {
        new(10, 0.8f, 1, 0.8f, Items.RawPorkchop, 2, "pig"),
        new(10, 0.95f, 1.8f, 0.7f, Items.Leather, 4, "cow"),
        new(10, 0.75f, 1.3f, 0.75f, Items.FromBlock(BlockType.WhiteWool), 4, "sheep"),
        new(6, 0.5f, 0.95f, 0.9f, Items.Feather, 4, "chicken")
    };
    public static bool IsDefined(MobSpecies species) => (uint)species < SpeciesCount;
    public static MobDefinition Get(MobSpecies species) => IsDefined(species) ? Definitions[(int)species]
        : throw new ArgumentOutOfRangeException(nameof(species));
}
