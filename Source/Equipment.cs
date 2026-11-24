using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed class AffinityGear : DefModExtension
{
    public Aspect affinity;
    public StatDef stat;
    public List<float> rankBonuses = new();
    public List<float> stageBonuses = new();
    public List<float> meleeDamageStages = new();
    public bool influenceResonanceEnabled = true;

    internal float BonusAt(int rank) => rank >= 1 && rank <= rankBonuses.Count ? rankBonuses[rank - 1] : 0f;

    internal float StageBonusAt(int stage)
    {
        if (stageBonuses is { Count: > 0 })
            return stageBonuses[System.Math.Clamp(stage, 0, stageBonuses.Count - 1)];
        return BonusAt(stage);
    }

    internal float MeleeMultiplierAt(int stage)
    {
        if (meleeDamageStages is not { Count: > 0 })
            return 1f;
        return meleeDamageStages[System.Math.Clamp(stage, 0, meleeDamageStages.Count - 1)];
    }
}

public sealed partial class Campaign
{
    private static IEnumerable<AffinityGear> EquippedAffinityGear(Pawn pawn)
    {
        if (pawn.equipment?.Primary?.def.GetModExtension<AffinityGear>() is { } weapon)
            yield return weapon;
        if (pawn.apparel == null)
            yield break;
        foreach (Apparel apparel in pawn.apparel.WornApparel)
        {
            if (apparel.def.GetModExtension<AffinityGear>() is { } gear)
                yield return gear;
        }
    }

    public static bool HasPathGear(Pawn pawn, Aspect aspect) =>
        EquippedAffinityGear(pawn).Any(gear => gear.affinity == aspect);

    public static bool HasAnyAffinityGear(Pawn pawn) =>
        EquippedAffinityGear(pawn).Any();

    public static bool HasLanternGear(Pawn pawn) => HasPathGear(pawn, Aspect.Lantern);

    /// <summary>
    /// Computes a smooth 0..5 growth stage combining follower rank, society marks, and Returned Keeper status.
    /// </summary>
    public int GearGrowthStage(Pawn pawn)
    {
        if (!IsMember(pawn))
            return 0;
        int raw = Rank(pawn) + (marks / 2) + (pawn == returnedLeader ? 1 : 0) - 1;
        return System.Math.Clamp(raw, 0, 5);
    }

    /// <summary>
    /// Psylink-style temporary resonance bonus from active matching Dream Influence (2/6/10/15 -> +10%/20%/35%/50%).
    /// </summary>
    public float InfluenceResonanceBonus(Aspect aspect)
    {
        int strength = MaxInfluence(aspect);
        return strength switch
        {
            >= 15 => 0.50f,
            >= 10 => 0.35f,
            >= 6 => 0.20f,
            >= 2 => 0.10f,
            _ => 0f
        };
    }

    private float SocietyStatBonus(Pawn pawn, StatDef stat)
    {
        if (!IsMember(pawn) || stat == null)
            return 0f;
        CultPathDef path = ActivePath;
        StatDef target = path.societyBonusStat ?? StatDefOf.ResearchSpeed;
        if (target != stat)
            return 0f;
        float bonus = hasAscendedLongLegacy ? path.returnedSocietyBonus : path.baseSocietyBonus;
        if (pawn == returnedLeader && pawn.health?.hediffSet?.HasHediff(CultistDefs.Cultist_ReturnedKeeper) == true)
            bonus += 0.15f;
        return bonus;
    }

    private float GearStatBonus(Pawn pawn, StatDef stat)
    {
        if (!IsMember(pawn) || stat == null)
            return 0f;

        int stage = GearGrowthStage(pawn);
        Aspect affinity = GetAspect(pawn);
        float largest = 0f;
        foreach (AffinityGear gear in EquippedAffinityGear(pawn))
        {
            if (gear.affinity != affinity || gear.stat != stat)
                continue;
            float val = gear.StageBonusAt(stage);
            if (gear.influenceResonanceEnabled)
                val += InfluenceResonanceBonus(gear.affinity) * 0.5f;
            largest = System.Math.Max(largest, val);
        }
        return largest;
    }

    public float StatBonusTotal(Pawn pawn, StatDef stat)
    {
        float total = SocietyStatBonus(pawn, stat) + GearStatBonus(pawn, stat);
        if (stat == StatDefOf.GeneralLaborSpeed && pawn is { Spawned: true } && IsMember(pawn)
            && EvaluateHqTraits(pawn).HasFlag(HqTrait.Foundry))
            total += 0.15f;
        return total;
    }

    public float ResearchMultiplier(Pawn pawn) => 1f + StatBonusTotal(pawn, StatDefOf.ResearchSpeed);

    public float MeleeGrowthMultiplier(Pawn pawn)
    {
        if (!IsMember(pawn))
            return 1f;
        int stage = GearGrowthStage(pawn);
        Aspect affinity = GetAspect(pawn);
        float mult = 1f;
        foreach (AffinityGear gear in EquippedAffinityGear(pawn))
        {
            if (gear.affinity != affinity || gear.meleeDamageStages is not { Count: > 0 })
                continue;
            float baseMult = gear.MeleeMultiplierAt(stage);
            float resMult = gear.influenceResonanceEnabled ? 1f + InfluenceResonanceBonus(gear.affinity) : 1f;
            mult = System.Math.Max(mult, baseMult * resMult);
        }
        if (HasExaltedTrait(pawn, ExaltedTraitId.PainFervor) && pawn.health?.summaryHealth != null)
        {
            float missingPct = 1f - pawn.health.summaryHealth.SummaryHealthPercent;
            int stacks = System.Math.Clamp((int)(missingPct / 0.10f), 0, 5);
            if (stacks > 0)
                mult *= 1f + stacks * 0.03f;
        }
        return mult;
    }

    internal string ExplainSocietyStat(Pawn pawn, StatDef stat)
    {
        float society = SocietyStatBonus(pawn, stat);
        float gear = GearStatBonus(pawn, stat);
        if (society == 0f && gear == 0f)
            return null;

        if (stat == StatDefOf.ComfyTemperatureMin)
        {
            var lines = new List<string>();
            if (society > 0f)
                lines.Add("Cultist.Research.Society".Translate($"-{(society * 100f):0.#}C"));
            if (gear > 0f)
                lines.Add("Cultist.Research.Equipment".Translate($"-{(gear * 100f):0.#}C"));
            return string.Join("\n", lines);
        }

        var parts = new List<string>();
        if (society > 0f)
            parts.Add("Cultist.Research.Society".Translate(society.ToStringPercent()));
        if (gear > 0f)
            parts.Add("Cultist.Research.Equipment".Translate(gear.ToStringPercent()));
        return string.Join("\n", parts);
    }

    internal string ExplainMeleeGrowth(Pawn pawn)
    {
        float mult = MeleeGrowthMultiplier(pawn);
        if (mult <= 1.001f)
            return null;
        return "Cultist.Melee.GrowthExplain".Translate(GearGrowthStage(pawn),
            InfluenceResonanceBonus(GetAspect(pawn)).ToStringPercent(), mult.ToString("0.00"));
    }
}

public sealed class StatPart_SocietyResearch : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.Thing is not Pawn pawn || Campaign.Current is not { } campaign)
            return;
        StatDef stat = parentStat ?? StatDefOf.ResearchSpeed;
        float bonus = campaign.StatBonusTotal(pawn, stat);
        if (bonus <= 0f)
            return;
        if (stat == StatDefOf.ComfyTemperatureMin)
            val -= bonus * 100f;
        else
            val *= 1f + bonus;
    }

    public override string ExplanationPart(StatRequest req) => req.Thing is Pawn pawn
        ? Campaign.Current?.ExplainSocietyStat(pawn, parentStat ?? StatDefOf.ResearchSpeed) : null;
}

public sealed class StatPart_CultistMeleeGrowth : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.Thing is Pawn pawn && Campaign.Current is { } campaign)
            val *= campaign.MeleeGrowthMultiplier(pawn);
    }

    public override string ExplanationPart(StatRequest req) => req.Thing is Pawn pawn
        ? Campaign.Current?.ExplainMeleeGrowth(pawn) : null;
}

public sealed class AffinityGearInspectProperties : CompProperties
{
    public AffinityGearInspectProperties() => compClass = typeof(AffinityGearInspect);
}

public sealed class AffinityGearInspect : ThingComp
{
    public override string CompInspectStringExtra()
    {
        if (parent.def.GetModExtension<AffinityGear>() is not { } gear)
            return null;
        return "Cultist.GearGrowthInspect".Translate(
            ("Cultist.Aspect." + gear.affinity).Translate(),
            gear.stat.LabelCap,
            gear.StageBonusAt(0).ToStringPercent(),
            gear.StageBonusAt(5).ToStringPercent());
    }
}
