using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Worlds;

namespace MDSM.Gameplay;

public static class EntityRaycaster
{
    public static Pig Cast(World world, PigPopulation pigs, Vector3 origin, Vector3 direction, float reach = BlockRaycaster.Reach)
    {
        if (!float.IsFinite(reach) || reach < 0 || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 0.000001f) return null;
        direction.Normalize();
        float limit = BlockRaycaster.Cast(world, origin, direction, reach, solidOnly: true)?.Distance ?? reach + 0.0001f;
        var ray = new Ray(origin, direction);
        Pig selected = null;
        for (int i = 0; i < pigs.Entities.Count; i++)
        {
            Pig pig = pigs.Entities[i];
            float? distance = ray.Intersects(pig.Bounds);
            if (pig.IsDead || !distance.HasValue || distance.Value > reach || distance.Value >= limit - 0.00001f) continue;
            limit = distance.Value; selected = pig;
        }
        return selected;
    }
}

public sealed class CombatController
{
    public const float AttackCooldown = 0.4f;
    private float _cooldown;
    public Pig Target { get; private set; }

    public void Update(World world, PigPopulation pigs, DroppedItems items, MiningController mining,
        Vector3 origin, Vector3 direction, BlockHit? block, bool held, float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) return;
        _cooldown = Math.Max(0, _cooldown - elapsed);
        Target = EntityRaycaster.Cast(world, pigs, origin, direction);
        if (Target == null) { mining.Update(world, items, block, held, elapsed); return; }
        mining.Reset();
        if (held && _cooldown <= 0 && pigs.Punch(Target, origin, items)) _cooldown = AttackCooldown;
    }
}
