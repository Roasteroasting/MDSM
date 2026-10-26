using System;
using MDSM.Worlds;

namespace MDSM.Entities;

// Pig-only compatibility facade. Live mixed populations use PassiveMobPopulation.
public sealed class PigPopulation : PassiveMobPopulation
{
    public const int MaximumPigs = MaximumMobs;
    public PigPopulation(World world, ReadOnlySpan<PigState> saved = default) : base(world, saved, default) { }
    public static new PigState[] Generate(World world)
    {
        MobState[] states = PassiveMobPopulation.Generate(world, MobSpecies.Pig);
        var result = new PigState[states.Length];
        for (int i = 0; i < result.Length; i++) result[i] = new PigState(states[i].Position, states[i].Yaw, states[i].Health);
        return result;
    }
    public PigState[] Capture() => CapturePigs();
    public static void Validate(PigState state, WorldSize size) => PassiveMobPopulation.Validate(
        new MobState(MobSpecies.Pig, state.Position, state.Yaw, state.Health), size);
}