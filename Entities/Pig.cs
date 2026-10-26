using Microsoft.Xna.Framework;

namespace MDSM.Entities;

// The legacy pig state and constants retain the pre-species save/fixture contract.
public readonly record struct PigState(Vector3 Position, float Yaw, int Health);
public sealed class Pig : PassiveMob
{
    public const int MaximumHealth = 10;
    public const float HalfWidth = 0.8f;
    public const float Height = 1f;
    internal Pig(PigState state, int aiSeed) : base(new MobState(MobSpecies.Pig, state.Position, state.Yaw, state.Health), aiSeed) { }
    public PigState Capture() => new(Position, Yaw, Health);
    public static BoundingBox BoundsAt(Vector3 feet) => PassiveMob.BoundsAt(MobSpecies.Pig, feet);
}