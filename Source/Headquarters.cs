using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Cultist;

[Flags]
public enum HqTrait
{
    None = 0,
    Library = 1,
    Sanctuary = 2,
    Pit = 4,
    Secret = 8,
    Foundry = 16
}

public sealed partial class Campaign
{
    public static HqTrait EvaluateHqTraits(Thing site)
    {
        if (site is not { Spawned: true } || site.Map == null)
            return HqTrait.None;

        Room room = site.GetRoom();
        if (room == null || room.PsychologicallyOutdoors || room.TouchesMapEdge)
            return HqTrait.None;

        HqTrait traits = HqTrait.None;
        Map map = site.Map;
        int cellCount = 0;
        int overheadMountainCells = 0;
        int unroofedCells = 0;
        int loreAndBookCount = 0;
        bool hasBookcase = false;
        bool hasSarcophagusOrPrison = room.IsPrisonCell;
        bool hasSmithy = false;
        int litBraziers = 0;

        foreach (IntVec3 cell in room.Cells)
        {
            cellCount++;
            RoofDef roof = cell.GetRoof(map);
            if (roof == null)
                unroofedCells++;
            else if (roof.isThickRoof)
                overheadMountainCells++;

            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Building_Bookcase)
                    hasBookcase = true;
                if (thing.TryGetComp<RitualMaterial>()?.LoreTier > 0 || thing is Book)
                    loreAndBookCount++;
                if (thing is Building_Sarcophagus)
                    hasSarcophagusOrPrison = true;
                string dn = thing.def.defName;
                if (dn is "FueledSmithy" or "ElectricSmithy")
                    hasSmithy = true;
                if (dn.IndexOf("Brazier", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    CompRefuelable refuel = thing.TryGetComp<CompRefuelable>();
                    if (refuel == null || refuel.HasFuel)
                        litBraziers++;
                }
            }
        }

        float cleanliness = room.GetStat(RoomStatDefOf.Cleanliness);
        float impressiveness = room.GetStat(RoomStatDefOf.Impressiveness);
        float beauty = room.GetStat(RoomStatDefOf.Beauty);

        if ((hasBookcase || loreAndBookCount >= 3) && cleanliness >= -0.2f)
            traits |= HqTrait.Library;
        if (impressiveness >= 40f && beauty >= 0f)
            traits |= HqTrait.Sanctuary;
        if (hasSarcophagusOrPrison || room.Temperature <= 5f || (cellCount > 0 && overheadMountainCells * 2 >= cellCount))
            traits |= HqTrait.Pit;
        if (unroofedCells == 0 && (overheadMountainCells * 2 >= Math.Max(1, cellCount) || cellCount <= 36))
            traits |= HqTrait.Secret;
        if (hasSmithy || litBraziers >= 2)
            traits |= HqTrait.Foundry;

        return traits;
    }

    public static int HqAspectBonus(Thing site, Aspect aspect)
    {
        HqTrait traits = EvaluateHqTraits(site);
        int bonus = 0;
        if (traits.HasFlag(HqTrait.Library) && aspect == Aspect.Lantern)
            bonus++;
        if (traits.HasFlag(HqTrait.Sanctuary) && aspect is Aspect.Lantern or Aspect.Heart or Aspect.Grail)
            bonus++;
        if (traits.HasFlag(HqTrait.Pit) && aspect is Aspect.Winter or Aspect.Edge or Aspect.Knock)
            bonus++;
        if (traits.HasFlag(HqTrait.Foundry) && aspect == Aspect.Forge)
            bonus++;
        return bonus;
    }

    public static int CalculateStudyTicks(Pawn pawn, Thing book, int baseTicks, bool isAncientLanguage = false)
    {
        float factor = 1f;
        if (book != null && EvaluateHqTraits(book).HasFlag(HqTrait.Library))
            factor *= 0.75f;
        if (HasDoctrine(pawn, "Cultist_LanternDoctrine"))
            factor *= 0.80f;
        if (isAncientLanguage && HasDoctrine(pawn, "Cultist_SecretHistoriesDoctrine"))
            factor *= 0.75f;
        if (Current?.HasExaltedTrait(pawn, ExaltedTraitId.LoreSpeedReader) == true)
            factor *= 0.75f;
        return Math.Max(120, (int)(baseTicks * factor));
    }

    public static void ApplyLibraryStudyBonus(Pawn pawn, Thing book)
    {
        if (pawn == null || book == null || !EvaluateHqTraits(book).HasFlag(HqTrait.Library))
            return;
        if (DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_HqLibraryClarity") is { } td)
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(td);
        if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_HqLibraryClarity") is { } hd)
            pawn.health?.AddHediff(hd);
    }

    public static void ApplySanctuaryRiteMood(Pawn pawn, Thing site, Pawn helper = null)
    {
        if (site == null || !EvaluateHqTraits(site).HasFlag(HqTrait.Sanctuary))
            return;
        if (DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_HqSanctuaryRite") is not { } td)
            return;
        pawn?.needs?.mood?.thoughts?.memories?.TryGainMemory(td);
        helper?.needs?.mood?.thoughts?.memories?.TryGainMemory(td);
    }

    public static string HqSummary(Thing site)
    {
        HqTrait traits = EvaluateHqTraits(site);
        if (traits == HqTrait.None)
            return "Cultist.HQ.Temporary".Translate();

        var labels = new List<string>();
        if (traits.HasFlag(HqTrait.Library)) labels.Add("Cultist.HQ.Library".Translate());
        if (traits.HasFlag(HqTrait.Sanctuary)) labels.Add("Cultist.HQ.Sanctuary".Translate());
        if (traits.HasFlag(HqTrait.Pit)) labels.Add("Cultist.HQ.Pit".Translate());
        if (traits.HasFlag(HqTrait.Secret)) labels.Add("Cultist.HQ.Secret".Translate());
        if (traits.HasFlag(HqTrait.Foundry)) labels.Add("Cultist.HQ.Foundry".Translate());
        return "Cultist.HQ.Active".Translate(string.Join(" / ", labels));
    }
}
