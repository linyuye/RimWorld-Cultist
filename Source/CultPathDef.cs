using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed class CultPathDef : Def
{
    public Aspect aspect = Aspect.Lantern;
    public bool playable = true;
    public string churchNameKey = "Cultist.ChurchName";
    public string churchDescriptionKey = "Cultist.ChurchDescription";
    public MemeDef memeDef;
    public List<PreceptDef> lockedPrecepts = new();
    public IdeoIconDef iconDef;
    public ColorDef colorDef;
    public ThingDef tier1LoreDef;
    public List<ThingDef> loreDefs = new();
    public StatDef societyBonusStat;
    public float baseSocietyBonus = 0.05f;
    public float returnedSocietyBonus = 0.10f;
    public int promotionInfluenceCost = 6;
    public int vigilAspectThreshold = 8;
    public int vigilDurationHours = 6;

    public ThingDef GetLoreDef(int tier)
    {
        int clamped = System.Math.Clamp(tier, 1, 7);
        int idx = clamped - 1;
        if (loreDefs != null && idx < loreDefs.Count && loreDefs[idx] != null)
            return loreDefs[idx];
        if (clamped == 1 && tier1LoreDef != null)
            return tier1LoreDef;
        string defName = aspect == Aspect.Lantern
            ? $"Cultist_Lore{clamped}Book"
            : $"Cultist_Lore_{aspect}{clamped}Book";
        return DefDatabase<ThingDef>.GetNamedSilentFail(defName)
            ?? DefDatabase<ThingDef>.GetNamed("Cultist_Lore1Book");
    }

    public static ThingDef LoreDefFor(Aspect aspect, int tier) =>
        ForAspect(aspect).GetLoreDef(tier);

    public static CultPathDef ForAspect(Aspect aspect) =>
        DefDatabase<CultPathDef>.AllDefsListForReading.FirstOrDefault(path => path.aspect == aspect)
        ?? DefDatabase<CultPathDef>.GetNamed("Cultist_Path_Lantern");
}

public sealed class InfluenceDecayDef : Def
{
    public Aspect aspect = Aspect.Lantern;
    public List<int> tiers = new() { 15, 10, 6, 2 };
    public float durationHours = 12f;
    public string terminalRisk = "None"; // Fascination, Fear, Restlessness, None

    public int NextTier(int current)
    {
        for (int i = 0; i < tiers.Count; i++)
        {
            if (tiers[i] == current)
                return i + 1 < tiers.Count ? tiers[i + 1] : 0;
        }
        return 0;
    }

    public int DurationTicks => System.Math.Max(250, (int)(durationHours * GenDate.TicksPerHour));

    public static InfluenceDecayDef ForAspect(Aspect aspect) =>
        DefDatabase<InfluenceDecayDef>.AllDefsListForReading.FirstOrDefault(def => def.aspect == aspect);
}
