using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Cultist;

public enum DreamReward
{
    // Keep the original rewards readable in existing saves.
    Lore, Light, Fascination, Memory, IntenseLight, BrilliantLight, Fear, Comfort,
    Whisper, TornPage, Rumor, Appetite, Vitality, HiddenAccount, FineCrack, Shiver,
    FrozenAir, Restless, DoorSliver, LostHistory, Insight, MarginalNotes, SealOfFavor,
    Pulse, BrokenThreshold, ShakingHeat, Anger, Hunger, BitterCold, ForbiddenEpic,
    ScholarsNotes, Famine, Drumming, Furnace, Ambiguity, RedResidue, WrongDoor,
    HeartRoar, InterpreterNotes, RedBody, Nectar, WanderingMap, FlawlessIce, FierceEdge
}

public sealed class MansusPoolDef : Def
{
    public int gate;
    public int region;
    public int loreTier = 1;
    public Vector2 mapCoords;
    public float mapDiameter = 108f;
    public List<DreamReward> rewards = new();
    public DreamReward? uniqueOnceReward;
}

internal static class DreamTable
{
    internal const int GateCount = 5;
    internal const int RegionCount = 3;

    internal static MansusPoolDef PoolDef(int gate, int region) =>
        DefDatabase<MansusPoolDef>.AllDefsListForReading
            .FirstOrDefault(def => def.gate == gate && def.region == region);

    internal static List<DreamReward> Candidates(int gate, int region, bool includeUnique)
    {
        MansusPoolDef def = PoolDef(gate, region);
        var list = new List<DreamReward>();
        if (def != null)
        {
            if (def.rewards != null)
                list.AddRange(def.rewards);
            if (includeUnique && def.uniqueOnceReward is { } unique)
                list.Add(unique);
        }
        if (list.Count == 0)
            list.Add(DreamReward.Rumor);
        return list;
    }

    internal static int LoreTierFor(int gate, int region) =>
        PoolDef(gate, region)?.loreTier ?? (gate >= 4 ? 3 : gate >= 2 ? 2 : 1);

    internal static (Aspect Affinity, int Strength)? Influence(DreamReward reward) => reward switch
    {
        DreamReward.Light => (Aspect.Lantern, 6),
        DreamReward.IntenseLight => (Aspect.Lantern, 10),
        DreamReward.BrilliantLight => (Aspect.Lantern, 15),
        DreamReward.Whisper => (Aspect.Moth, 2),
        DreamReward.Ambiguity => (Aspect.Moth, 10),
        DreamReward.Appetite => (Aspect.Grail, 2),
        DreamReward.Hunger => (Aspect.Grail, 6),
        DreamReward.Famine => (Aspect.Grail, 10),
        DreamReward.Pulse => (Aspect.Heart, 6),
        DreamReward.Drumming => (Aspect.Heart, 6),
        DreamReward.HeartRoar => (Aspect.Heart, 10),
        DreamReward.FineCrack or DreamReward.DoorSliver or DreamReward.BrokenThreshold => (Aspect.Knock, 2),
        DreamReward.WrongDoor => (Aspect.Knock, 10),
        DreamReward.Shiver => (Aspect.Edge, 2),
        DreamReward.Anger => (Aspect.Edge, 6),
        DreamReward.FierceEdge => (Aspect.Edge, 10),
        DreamReward.FrozenAir or DreamReward.BitterCold => (Aspect.Winter, 6),
        DreamReward.FlawlessIce => (Aspect.Winter, 15),
        DreamReward.ShakingHeat => (Aspect.Forge, 6),
        DreamReward.Furnace => (Aspect.Forge, 10),
        _ => null
    };

    internal static DreamReward Effect(DreamReward reward) => reward switch
    {
        DreamReward.TornPage or DreamReward.Rumor or DreamReward.HiddenAccount or DreamReward.LostHistory
            or DreamReward.MarginalNotes or DreamReward.ForbiddenEpic or DreamReward.ScholarsNotes
            or DreamReward.WanderingMap or DreamReward.SealOfFavor => DreamReward.Lore,
        DreamReward.Vitality or DreamReward.Nectar or DreamReward.RedBody => DreamReward.Comfort,
        DreamReward.Insight or DreamReward.RedResidue => DreamReward.Memory,
        DreamReward.Restless => DreamReward.Fear,
        _ => reward
    };

    internal static TaggedString RewardLabel(DreamReward reward, ThingDef lore)
    {
        TaggedString name = ("Cultist.Reward." + reward).Translate();
        if (Influence(reward) is { } influence)
            return "Cultist.DreamInfluenceReward".Translate(name,
                ("Cultist.Aspect." + influence.Affinity).Translate(), influence.Strength);
        DreamReward effect = Effect(reward);
        string effectKey = "Cultist.DreamEffect." + effect;
        TaggedString description = effect is DreamReward.Lore or DreamReward.InterpreterNotes
            ? effectKey.Translate(lore.LabelCap)
            : effectKey.Translate();
        return "Cultist.DreamMappedReward".Translate(name, description);
    }
}
