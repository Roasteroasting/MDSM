using System;
using System.IO;

namespace MDSM.Entities;

public readonly record struct MobRespawnState(MobSpecies Species, double RemainingSeconds);

// Per-world intended counts, separate from the live entities and their temporary AI state.
public sealed record MobPopulationState(int[] Targets, MobRespawnState[] Pending)
{
    public static int[] LivingCounts(ReadOnlySpan<PigState> pigs, ReadOnlySpan<MobState> others)
    {
        var counts = new int[MobDefinitions.SpeciesCount]; counts[(int)MobSpecies.Pig] = pigs.Length;
        foreach (MobState mob in others)
        {
            if (!MobDefinitions.IsDefined(mob.Species) || mob.Species == MobSpecies.Pig) throw new InvalidDataException("Invalid non-pig species.");
            counts[(int)mob.Species]++;
        }
        return counts;
    }
    public static MobPopulationState FromLiving(ReadOnlySpan<PigState> pigs, ReadOnlySpan<MobState> others) =>
        new(LivingCounts(pigs, others), Array.Empty<MobRespawnState>());

    public void Validate(ReadOnlySpan<int> living)
    {
        if (Targets == null || Targets.Length != MobDefinitions.SpeciesCount || Pending == null
            || Pending.Length > PassiveMobPopulation.MaximumMobs || living.Length != Targets.Length)
            throw new InvalidDataException("Invalid population metadata layout.");
        Span<int> pendingCounts = stackalloc int[MobDefinitions.SpeciesCount];
        foreach (MobRespawnState respawn in Pending)
        {
            if (!MobDefinitions.IsDefined(respawn.Species) || !double.IsFinite(respawn.RemainingSeconds)
                || respawn.RemainingSeconds < 0 || respawn.RemainingSeconds > PassiveMobPopulation.MaximumRespawnSeconds)
                throw new InvalidDataException("Invalid pending mob respawn.");
            pendingCounts[(int)respawn.Species]++;
        }
        int total = 0;
        for (int i = 0; i < Targets.Length; i++)
        {
            if (Targets[i] < 0 || Targets[i] > PassiveMobPopulation.MaximumMobs || living[i] < 0
                || living[i] + pendingCounts[i] != Targets[i]) throw new InvalidDataException("Population targets do not match living and pending mobs.");
            total += Targets[i];
        }
        if (total > PassiveMobPopulation.MaximumMobs) throw new InvalidDataException("Population target exceeds the shared limit.");
    }
}
