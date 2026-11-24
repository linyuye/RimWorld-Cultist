using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class OccultProjectileProps : DefModExtension
{
    public bool applyFrostSlow;
    public int staggerTicks;
    public float allyRestRadius;
    public Color fleckColor = Color.white;
}

public sealed class Projectile_Occult : Bullet
{
    protected override void Impact(Thing hitThing, bool blockedByShield = false)
    {
        Map map = Map;
        IntVec3 pos = Position;
        Thing shooter = launcher;
        OccultProjectileProps ext = def.GetModExtension<OccultProjectileProps>();

        base.Impact(hitThing, blockedByShield);

        if (map == null || ext == null)
            return;

        if (ext.fleckColor != Color.white)
            FleckMaker.ThrowDustPuffThick(pos.ToVector3Shifted(), map, 0.9f, ext.fleckColor);

        if (!blockedByShield && hitThing is Pawn victim && !victim.Dead)
        {
            if (ext.applyFrostSlow && victim.RaceProps.IsFlesh
                && DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_FrostSlow") is { } frostDef)
            {
                victim.health?.AddHediff(frostDef);
            }
            if (ext.staggerTicks > 0 && victim.stances?.stagger != null)
            {
                victim.stances.stagger.StaggerFor(ext.staggerTicks);
            }
        }

        if (ext.allyRestRadius > 0f && shooter is Pawn caster && caster.Spawned && caster.Map == map && Rand.Chance(0.25f))
        {
            foreach (Pawn ally in map.mapPawns.FreeColonistsSpawned)
            {
                if (ally.Position.InHorDistOf(caster.Position, ext.allyRestRadius) && ally.needs?.rest != null)
                    ally.needs.rest.CurLevel = System.Math.Min(1f, ally.needs.rest.CurLevel + 0.02f);
            }
        }
    }
}
