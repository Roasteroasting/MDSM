using System;
using Microsoft.Xna.Framework;
using MDSM.Entities;
using MDSM.Worlds;
using MDSM.Inventories;
using MDSM.Audio;

namespace MDSM.Gameplay;

public static class EntityRaycaster
{
    public static PassiveMob Cast(World world, PassiveMobPopulation pigs, Vector3 origin, Vector3 direction, float reach = BlockRaycaster.Reach,HostilePopulation hostiles=null)
    {
        if (!float.IsFinite(reach) || reach < 0 || !float.IsFinite(direction.LengthSquared()) || direction.LengthSquared() < 0.000001f) return null;
        direction.Normalize();
        float limit = BlockRaycaster.Cast(world, origin, direction, reach, solidOnly: true)?.Distance ?? reach + 0.0001f;
        var ray = new Ray(origin, direction);
        PassiveMob selected = null;
        for (int i = 0; i < pigs.Entities.Count; i++)
        {
            PassiveMob pig = pigs.Entities[i];
            float? distance = ray.Intersects(pig.Bounds);
            if (pig.IsDead || !distance.HasValue || distance.Value > reach || distance.Value >= limit - 0.00001f) continue;
            limit = distance.Value; selected = pig;
        }
        if(hostiles!=null)for(int i=0;i<hostiles.Entities.Count;i++)
        {PassiveMob mob=hostiles.Entities[i];float? distance=ray.Intersects(mob.Bounds);if(distance.HasValue && distance.Value<=reach && distance.Value<limit-.00001f){limit=distance.Value;selected=mob;}}
        return selected;
    }
}

public sealed class CombatController
{
    public const float AttackCooldown = 0.4f;
    private float _cooldown;
    public PassiveMob Target { get; private set; }

    public void Update(World world, PassiveMobPopulation pigs, DroppedItems items, MiningController mining,
        Vector3 origin, Vector3 direction, BlockHit? block, bool held, float elapsed,Inventory inventory=null,HostilePopulation hostiles=null,bool creative=false)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) return;
        _cooldown = Math.Max(0, _cooldown - elapsed);
        Target = EntityRaycaster.Cast(world, pigs, origin, direction,hostiles:hostiles);
        if (Target == null) { mining.Update(world, items, block, held, elapsed,inventory,creative); return; }
        mining.Update(world,items,null,false,elapsed,inventory,creative);
        if(held && _cooldown<=0)
        {
            ItemStack tool=inventory?.Selected??default;int damage=Items.Get(tool.Item).AttackDamage;
            bool hit=(int)Target.Species>=4?hostiles.Hit(Target,damage>0?damage:Random.Shared.Next(1,3),origin,items):damage>0?pigs.Hit(Target,damage,origin,items):pigs.Punch(Target,origin,items);
            if(hit){world.Sounds?.At(SoundKind.Swing,origin);_cooldown=AttackCooldown;if(!creative && inventory!=null && MiningRules.Wears(tool))inventory.WearSelected();}
        }
    }
}
