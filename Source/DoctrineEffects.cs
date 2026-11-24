using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private Dictionary<string, float> lastDownedCounts = new();
    private Dictionary<string, int> lastCombatTickByPawn = new();

    private void ExposeDoctrines()
    {
        Scribe_Collections.Look(ref lastDownedCounts, "lastDownedCounts", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref lastCombatTickByPawn, "lastCombatTickByPawn", LookMode.Value, LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            lastDownedCounts ??= new Dictionary<string, float>();
            lastCombatTickByPawn ??= new Dictionary<string, int>();
        }
    }

    internal static bool HasDoctrine(Pawn pawn, string preceptDefName)
    {
        if (pawn?.Ideo == null)
            return false;
        PreceptDef def = DefDatabase<PreceptDef>.GetNamedSilentFail(preceptDefName);
        return def != null && pawn.Ideo.HasPrecept(def);
    }

    internal bool IsEdgeRusted(Pawn pawn)
    {
        if (pawn == null || !HasDoctrine(pawn, "Cultist_EdgeDoctrine"))
            return false;
        int now = Find.TickManager.TicksGame;
        string id = pawn.GetUniqueLoadID();
        if (!lastCombatTickByPawn.TryGetValue(id, out int lastTick))
        {
            lastCombatTickByPawn[id] = now;
            return false;
        }
        return now - lastTick >= 12 * GenDate.TicksPerDay;
    }

    private void TickDoctrineEffects(int now)
    {
        HediffDef grailH = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_GrailAppetite");
        HediffDef edgeH = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_EdgeTemper");
        HediffDef winterH = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_WinterHush");
        HediffDef heartH = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_HeartPulse");
        ThoughtDef edgeTriumph = DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_EdgeTriumph");

        foreach (Map map in Find.Maps)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || pawn.health?.hediffSet == null)
                    continue;

                SyncDoctrineHediff(pawn, grailH, HasDoctrine(pawn, "Cultist_GrailDoctrine"));
                SyncDoctrineHediff(pawn, edgeH, HasDoctrine(pawn, "Cultist_EdgeDoctrine"));
                SyncDoctrineHediff(pawn, winterH, HasDoctrine(pawn, "Cultist_WinterDoctrine"));
                SyncDoctrineHediff(pawn, heartH, HasDoctrine(pawn, "Cultist_HeartDoctrine"));

                // Winter: ignore non-colonist corpse ugliness & ordinary party mood
                if (HasDoctrine(pawn, "Cultist_WinterDoctrine") && pawn.needs?.mood?.thoughts?.memories is { } mems)
                {
                    RemoveMemoryByDefName(mems, "ObservedLayingCorpse");
                    RemoveMemoryByDefName(mems, "ObservedLayingRottingCorpse");
                    RemoveMemoryByDefName(mems, "AttendedParty");
                }

                // Edge: track combat kills/downs and recent combat activity
                if (HasDoctrine(pawn, "Cultist_EdgeDoctrine") && pawn.records != null)
                {
                    string id = pawn.GetUniqueLoadID();
                    if (!lastCombatTickByPawn.ContainsKey(id))
                        lastCombatTickByPawn[id] = now;
                    if (pawn.mindState?.lastHarmTick > 0 && now - pawn.mindState.lastHarmTick < 2500)
                        lastCombatTickByPawn[id] = now;
                    if (pawn.mindState?.lastMeleeThreatHarmTick > 0 && now - pawn.mindState.lastMeleeThreatHarmTick < 2500)
                        lastCombatTickByPawn[id] = now;

                    float downed = pawn.records.GetValue(RecordDefOf.PawnsDowned);
                    if (lastDownedCounts.TryGetValue(id, out float prev))
                    {
                        if (downed > prev)
                        {
                            lastCombatTickByPawn[id] = now;
                            if (edgeTriumph != null)
                                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(edgeTriumph);
                        }
                    }
                    lastDownedCounts[id] = downed;
                }

                // Moth: when mentally broken into berserk/social-aggression, prefer dazed wandering
                if (HasDoctrine(pawn, "Cultist_MothDoctrine")
                    && pawn.MentalStateDef == MentalStateDefOf.Berserk
                    && DefDatabase<MentalStateDef>.GetNamedSilentFail("Wander_Psychotic") is { } wander)
                {
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(wander, forced: true);
                }

                // Exalted Moth passive trigger: Molting Smoke (<35% health summary)
                CheckExaltedMothEscape(pawn, now);
            }
        }
    }

    private static void SyncDoctrineHediff(Pawn pawn, HediffDef def, bool shouldHave)
    {
        if (def == null)
            return;
        Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
        if (shouldHave && existing == null)
            pawn.health.AddHediff(def);
        else if (!shouldHave && existing != null)
            pawn.health.RemoveHediff(existing);
    }

    private static void RemoveMemoryByDefName(MemoryThoughtHandler mems, string defName)
    {
        if (DefDatabase<ThoughtDef>.GetNamedSilentFail(defName) is { } td)
            mems.RemoveMemoriesOfDef(td);
    }
}

public sealed class ThoughtWorker_DoctrineLantern : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!p.Spawned || p.Map == null || !Campaign.HasDoctrine(p, "Cultist_LanternDoctrine"))
            return ThoughtState.Inactive;
        float glow = p.Map.glowGrid.GroundGlowAt(p.Position);
        if (glow >= 0.50f)
            return ThoughtState.ActiveAtStage(0);
        if (glow < 0.30f && !p.Awake() == false)
            return ThoughtState.ActiveAtStage(1);
        return ThoughtState.Inactive;
    }
}

public sealed class ThoughtWorker_DoctrineForge : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!Campaign.HasDoctrine(p, "Cultist_ForgeDoctrine"))
            return ThoughtState.Inactive;

        bool hasBad = false;
        bool hasGood = false;

        if (p.equipment?.Primary is { } weapon)
        {
            CheckGear(weapon, ref hasGood, ref hasBad);
        }
        if (p.apparel != null)
        {
            foreach (Apparel ap in p.apparel.WornApparel)
                CheckGear(ap, ref hasGood, ref hasBad);
        }

        if (hasBad)
            return ThoughtState.ActiveAtStage(1);
        if (hasGood)
            return ThoughtState.ActiveAtStage(0);
        return ThoughtState.Inactive;
    }

    private static void CheckGear(Thing item, ref bool hasGood, ref bool hasBad)
    {
        if (item.def.useHitPoints && item.MaxHitPoints > 0 && item.HitPoints < item.MaxHitPoints * 0.5f)
            hasBad = true;
        if (item.TryGetQuality(out QualityCategory q))
        {
            if (q <= QualityCategory.Poor)
                hasBad = true;
            else if (q >= QualityCategory.Good)
                hasGood = true;
        }
    }
}

public sealed class ThoughtWorker_DoctrineGrail : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!Campaign.HasDoctrine(p, "Cultist_GrailDoctrine") || p.needs?.mood?.thoughts?.memories is not { } mems)
            return ThoughtState.Inactive;

        foreach (Thought_Memory m in mems.Memories)
        {
            string dn = m.def.defName;
            if (dn is "AteNutrientPasteMeal" or "AteRawFood" or "AteKibble" or "AteCorpse")
                return ThoughtState.ActiveAtStage(1);
        }
        foreach (Thought_Memory m in mems.Memories)
        {
            string dn = m.def.defName;
            if (dn is "AteFineMeal" or "AteLavishMeal")
                return ThoughtState.ActiveAtStage(0);
        }
        return ThoughtState.Inactive;
    }
}

public sealed class ThoughtWorker_DoctrineEdgeRust : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p) =>
        Campaign.Current?.IsEdgeRusted(p) == true ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
}

public sealed class ThoughtWorker_DoctrineHeart : ThoughtWorker
{
    private static readonly HashSet<string> GriefMemories = new()
    {
        "KnowColonistDied", "PawnWithGoodOpinionDied", "KnowPrisonerDiedInnocent",
        "ExecutedPrisoner", "KnowGuestOrganHarvested", "KnowColonistOrganHarvested",
        "MySonDied", "MyDaughterDied", "MySpouseDied", "MyLoverDied"
    };

    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!Campaign.HasDoctrine(p, "Cultist_HeartDoctrine") || p.needs?.mood?.thoughts?.memories is not { } mems)
            return ThoughtState.Inactive;
        return mems.Memories.Any(m => GriefMemories.Contains(m.def.defName))
            ? ThoughtState.ActiveAtStage(0)
            : ThoughtState.Inactive;
    }
}

public sealed class ThoughtWorker_DoctrineMoth : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!p.Spawned || !Campaign.HasDoctrine(p, "Cultist_MothDoctrine"))
            return ThoughtState.Inactive;
        int hour = GenLocalDate.HourOfDay(p);
        return (hour >= 18 || hour < 6) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
    }
}

public sealed class ThoughtWorker_DoctrineKnock : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!Campaign.HasDoctrine(p, "Cultist_KnockDoctrine") || p.health?.hediffSet == null)
            return ThoughtState.Inactive;

        int lockScars = p.health.hediffSet.hediffs.Count(h => h.def.defName.StartsWith("Cultist_Scar_"));
        if (lockScars > 0)
            return ThoughtState.ActiveAtStage(System.Math.Min(lockScars, 5));

        int regularScars = Campaign.PermanentScarsOf(p).Count;
        return regularScars == 0 ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
    }
}

public sealed class ThoughtWorker_LongGrace : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p) =>
        Campaign.Current is { HasAscendedLongLegacy: true } && p.IsColonist
            ? ThoughtState.ActiveAtStage(0)
            : ThoughtState.Inactive;
}
