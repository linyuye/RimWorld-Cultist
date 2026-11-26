using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

/// <summary>
/// Hidden enemy-only combat scaling driven by vanilla colony threat points / wealth curve.
/// Automatically hides itself from the Health UI and removes itself if the pawn ceases to be hostile.
/// </summary>
public sealed class Hediff_EnemyWealthScaling : Hediff
{
    public override bool Visible => false;

    public override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        if (pawn == null || pawn.Dead)
            return;

        if (!Campaign.IsHostileToPlayer(pawn))
            pawn.health?.RemoveHediff(this);
    }
}

public sealed partial class Campaign
{
    private static readonly SimpleCurve EnemyThreatToSeverityCurve = new()
    {
        new CurvePoint(300f, 0.10f),
        new CurvePoint(1000f, 0.30f),
        new CurvePoint(2500f, 0.55f),
        new CurvePoint(5000f, 0.80f),
        new CurvePoint(8500f, 1.00f),
    };

    internal static bool IsHostileToPlayer(Pawn pawn) =>
        pawn != null
        && !pawn.Dead
        && pawn.Faction != null
        && pawn.Faction != Faction.OfPlayer
        && pawn.Faction.HostileTo(Faction.OfPlayer)
        && !pawn.IsPrisonerOfColony
        && !pawn.IsSlaveOfColony;

    private static bool IsCultistEnemyOrGearBearer(Pawn pawn)
    {
        string fDef = pawn.Faction?.def?.defName;
        if (fDef == "Cultist_SuppressionBureau" || fDef == "Cultist_RivalLongFaction")
            return true;

        string kDef = pawn.kindDef?.defName;
        if (kDef != null && (kDef.StartsWith("Cultist_Bureau") || kDef.StartsWith("Cultist_Long")))
            return true;

        if (pawn.equipment?.Primary?.def?.defName is { } wDef && wDef.StartsWith("Cultist_"))
            return true;

        if (pawn.apparel?.WornApparel is { } worn)
        {
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i]?.def?.defName is { } aDef && aDef.StartsWith("Cultist_"))
                    return true;
            }
        }

        return false;
    }

    internal static float ComputeEnemyWealthSeverity(bool isBoss = false)
    {
        Map refMap = Current?.Leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome) ?? Find.AnyPlayerHomeMap;
        float threatPoints = refMap != null ? StorytellerUtility.DefaultThreatPointsNow(refMap) : 600f;
        float sev = EnemyThreatToSeverityCurve.Evaluate(threatPoints);
        if (isBoss)
            sev += 0.20f;
        return Math.Clamp(sev, 0.05f, 1.0f);
    }

    internal static void ApplyEnemyWealthScaling(Pawn pawn, bool isBoss = false)
    {
        if (pawn?.health?.hediffSet == null)
            return;

        HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_EnemyWealthScaling");
        if (def == null)
            return;

        if (!IsHostileToPlayer(pawn))
        {
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing != null)
                pawn.health.RemoveHediff(existing);
            return;
        }

        bool bossPawn = isBoss
            || pawn.kindDef?.defName == "Cultist_BureauHunter"
            || pawn.kindDef?.defName == "Cultist_LongAssassin";

        float targetSeverity = ComputeEnemyWealthSeverity(bossPawn);
        Hediff current = pawn.health.hediffSet.GetFirstHediffOfDef(def);
        if (current == null)
        {
            current = pawn.health.AddHediff(def);
            if (current != null)
                current.Severity = targetSeverity;
        }
        else if (Math.Abs(current.Severity - targetSeverity) > 0.03f)
        {
            current.Severity = Math.Max(current.Severity, targetSeverity);
        }
    }

    private void TickEnemyWealthScaling()
    {
        HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_EnemyWealthScaling");
        if (def == null)
            return;

        foreach (Map map in Find.Maps)
        {
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
                    continue;

                if (!IsHostileToPlayer(pawn))
                {
                    Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(def);
                    if (existing != null)
                        pawn.health.RemoveHediff(existing);
                    continue;
                }

                if (IsCultistEnemyOrGearBearer(pawn))
                    ApplyEnemyWealthScaling(pawn);
            }
        }
    }
}
