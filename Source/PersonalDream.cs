using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private List<PersonalDream> dreamers = new();
    private PersonalDream legacyDream;
    private int legacyFascination;

    private PersonalDream DreamOf(Pawn pawn) =>
        pawn == null ? null : dreamers.Find(state => state.Owner == pawn);

    private PersonalDream LeaderDream => DreamOf(leader);

    private PersonalDream RegisterDreamer(Pawn pawn)
    {
        PersonalDream existing = DreamOf(pawn);
        if (existing != null)
            return existing;
        var state = new PersonalDream { Owner = pawn };
        dreamers.Add(state);
        return state;
    }

    public bool AutoDream => LeaderDream?.Auto == true;
    public void ToggleAutoDream()
    {
        if (LeaderDream is { } state)
            state.Auto = !state.Auto;
    }
    public int MaxInfluence(Aspect aspect) =>
        LeaderDream?.Influences.Where(value => value.Affinity == aspect)
            .Select(value => value.Strength).DefaultIfEmpty().Max() ?? 0;

    internal IReadOnlyList<DreamInfluence> LeaderDreamInfluences() =>
        LeaderDream?.Influences ?? (IReadOnlyList<DreamInfluence>)System.Array.Empty<DreamInfluence>();

    public int Influence => ChurchAspect == Aspect.SecretHistories
        ? (LeaderDream?.Influences.Select(v => v.Strength).DefaultIfEmpty().Max() ?? 0)
        : MaxInfluence(ChurchAspect);
    public int Fascination => LeaderDream?.Fascination.Count ?? 0;
    public int Fear => LeaderDream?.Fear.Count ?? 0;
    public int Memory => LeaderDream?.Memory.Count ?? 0;
    public int Comfort => LeaderDream?.Comfort.Count ?? 0;

    public string DreamTiming()
    {
        if (LeaderDream is not { } state)
            return "";
        int now = Find.TickManager.TicksGame;
        var lines = state.Influences.OrderByDescending(value => value.Strength)
            .ThenBy(value => value.Expires)
            .Select(value =>
            {
                InfluenceDecayDef decay = InfluenceDecayDef.ForAspect(value.Affinity);
                int nextTier = decay?.NextTier(value.Strength) ?? (value.Strength switch { 15 => 10, 10 => 6, 6 => 2, _ => 0 });
                string nextLabel = nextTier > 0
                    ? nextTier.ToString()
                    : (decay?.terminalRisk switch
                    {
                        "Fascination" => "Cultist.NextFascination",
                        "Fear" => "Cultist.NextDespair",
                        _ => "Cultist.DreamFade"
                    }).Translate().ToString();
                return "Cultist.InfluenceTiming".Translate(value.Strength,
                    HoursLeft(value.Expires, now), nextLabel,
                    ("Cultist.Aspect." + value.Affinity).Translate()).ToString();
            })
            .ToList();
        if (state.FascinationCrisis >= now)
            lines.Add("Cultist.CrisisTiming".Translate("Cultist.NextFascination".Translate(),
                HoursLeft(state.FascinationCrisis, now)));
        if (state.DespairCrisis >= now)
            lines.Add("Cultist.CrisisTiming".Translate("Cultist.NextDespair".Translate(),
                HoursLeft(state.DespairCrisis, now)));
        return string.Join("\n", lines);
    }

    private static string HoursLeft(int expiry, int now) =>
        (System.Math.Max(0, expiry - now) / (float)GenDate.TicksPerHour).ToString("0.0");

    private void ExposePersonalDreams()
    {
        bool loadingLegacy = Scribe.mode == LoadSaveMode.LoadingVars && Scribe.loader.curXmlParent["dreamers"] == null;
        Scribe_Collections.Look(ref dreamers, "dreamers", LookMode.Deep);
        if (loadingLegacy)
        {
            legacyDream = new PersonalDream();
            Scribe_Values.Look(ref legacyDream.Auto, "autoDream");
            Scribe_Values.Look(ref legacyDream.AutoAt, "autoDreamAt", -1);
            Scribe_Values.Look(ref legacyDream.LastTick, "lastDreamTick", -1);
            Scribe_Values.Look(ref legacyDream.Night, "dreamNight");
            Scribe_Values.Look(ref legacyDream.FascinationCrisis, "crisisUntil", -1);
            Scribe_Values.Look(ref legacyFascination, "fascination");
        }
        if (legacyDream != null)
            Scribe_Collections.Look(ref legacyDream.Influences, "influences", LookMode.Deep);
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        dreamers ??= new List<PersonalDream>();
        if (legacyDream != null && leader != null && stage >= CampaignStage.Dreaming)
        {
            legacyDream.Owner = leader;
            legacyDream.Influences ??= new List<DreamInfluence>();
            int now = Find.TickManager.TicksGame;
            for (int index = 0; index < legacyFascination; index++)
                legacyDream.Fascination.Add(now + PersonalDream.RiskDuration);
            dreamers.Add(legacyDream);
        }
        legacyDream = null;
        if (leader != null && stage >= CampaignStage.Dreaming)
            RegisterDreamer(leader);
    }

    public bool CanRelieve(Pawn pawn, AltarAction action)
    {
        if (pawn != leader || pawn.Dead || pawn.Downed || !pawn.IsColonistPlayerControlled
            || !IsMember(pawn) || LeaderDream is not { } state)
            return false;
        return action switch
        {
            AltarAction.RelieveMemory => state.Fascination.Count > 0 && state.Memory.Count > 0,
            AltarAction.RelieveFear => state.Fascination.Count > 0 && state.Fear.Count > 0,
            AltarAction.RelieveComfort => state.Fear.Count > 0 && state.Comfort.Count > 0,
            _ => false
        };
    }

    public void Relieve(Pawn pawn, AltarAction action)
    {
        if (!CanRelieve(pawn, action))
            return;
        PersonalDream state = LeaderDream;
        switch (action)
        {
            case AltarAction.RelieveMemory:
                PersonalDream.SpendOldest(state.Memory);
                PersonalDream.SpendOldest(state.Fascination);
                break;
            case AltarAction.RelieveFear:
                PersonalDream.SpendOldest(state.Fear);
                PersonalDream.SpendOldest(state.Fascination);
                break;
            case AltarAction.RelieveComfort:
                PersonalDream.SpendOldest(state.Comfort);
                PersonalDream.SpendOldest(state.Fear);
                break;
        }
        Messages.Message("Cultist.Relieved".Translate(pawn.LabelShortCap),
            pawn, MessageTypeDefOf.PositiveEvent);
    }

    private void TickPersonalDreams(int now)
    {
        foreach (PersonalDream state in dreamers)
        {
            if (state.Owner == null || state.Owner.Dead || departedKeepers.Contains(state.Owner))
                continue;
            state.Tick(now);
        }
    }
}

public sealed class PersonalDream : IExposable
{
    internal const int RiskDuration = GenDate.TicksPerDay * 3 / 2;
    internal const int InfluenceDuration = GenDate.TicksPerDay / 2;
    internal const int RescueDuration = GenDate.TicksPerDay / 4;

    public Pawn Owner;
    public bool Auto;
    public int AutoAt = -1;
    public int LastTick = -1;
    public int Night;
    public List<DreamInfluence> Influences = new();
    public List<int> Fascination = new();
    public List<int> Fear = new();
    public List<int> Memory = new();
    public List<int> Comfort = new();
    internal int FascinationCrisis = -1;
    internal int DespairCrisis = -1;

    public void ExposeData()
    {
        Scribe_References.Look(ref Owner, "owner");
        Scribe_Values.Look(ref Auto, "auto");
        Scribe_Values.Look(ref AutoAt, "autoAt", -1);
        Scribe_Values.Look(ref LastTick, "lastTick", -1);
        Scribe_Values.Look(ref Night, "night");
        Scribe_Collections.Look(ref Influences, "influences", LookMode.Deep);
        Scribe_Collections.Look(ref Fascination, "fascination", LookMode.Value);
        Scribe_Collections.Look(ref Fear, "fear", LookMode.Value);
        Scribe_Collections.Look(ref Memory, "memory", LookMode.Value);
        Scribe_Collections.Look(ref Comfort, "comfort", LookMode.Value);
        Scribe_Values.Look(ref FascinationCrisis, "fascinationCrisis", -1);
        Scribe_Values.Look(ref DespairCrisis, "despairCrisis", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            Influences ??= new List<DreamInfluence>();
            Fascination ??= new List<int>();
            Fear ??= new List<int>();
            Memory ??= new List<int>();
            Comfort ??= new List<int>();
        }
    }

    public void AddRisk(bool fear, int now)
    {
        List<int> entries = fear ? Fear : Fascination;
        entries.Add(now + RiskDuration);
        if (entries.Count == 2)
        {
            Messages.Message((fear ? "Cultist.FearWarning" : "Cultist.FascinationWarning").Translate(Owner.LabelShortCap),
                Owner, MessageTypeDefOf.NegativeEvent);
        }
    }

    public void Tick(int now)
    {
        Fascination.RemoveAll(expiry => expiry <= now);
        Fear.RemoveAll(expiry => expiry <= now);
        Memory.RemoveAll(expiry => expiry <= now);
        Comfort.RemoveAll(expiry => expiry <= now);
        for (int index = Influences.Count - 1; index >= 0; index--)
        {
            DreamInfluence value = Influences[index];
            InfluenceDecayDef decay = InfluenceDecayDef.ForAspect(value.Affinity);
            int stepDuration = decay?.DurationTicks ?? InfluenceDuration;
            while (value.Strength > 0 && now >= value.Expires)
            {
                value.Strength = decay?.NextTier(value.Strength)
                    ?? (value.Strength switch { 15 => 10, 10 => 6, 6 => 2, _ => 0 });
                value.Expires += stepDuration;
            }
            if (value.Strength != 0)
                continue;
            Influences.RemoveAt(index);
            int fadedAt = value.Expires - stepDuration;
            if (now < fadedAt + RiskDuration)
            {
                string risk = decay?.terminalRisk ?? (value.Affinity == Aspect.Lantern ? "Fascination" : "None");
                if (risk == "Fascination")
                    AddRisk(false, fadedAt);
                else if (risk == "Fear")
                    AddRisk(true, fadedAt);
            }
        }
        TickCrisis(now, false, ref FascinationCrisis);
        if (!Owner.Dead)
            TickCrisis(now, true, ref DespairCrisis);
    }

    private void TickCrisis(int now, bool fear, ref int deadline)
    {
        if ((fear ? Fear : Fascination).Count < 3)
        {
            deadline = -1;
            return;
        }
        if (deadline < 0)
        {
            deadline = now + RescueDuration;
            Messages.Message((fear ? "Cultist.DespairCrisis" : "Cultist.FascinationCrisis").Translate(Owner.LabelShortCap),
                Owner, MessageTypeDefOf.ThreatBig);
        }
        else if (now >= deadline)
        {
            deadline = -1;
            Owner.Kill(null);
        }
    }

    public static void SpendOldest(List<int> expiry) => expiry.Remove(expiry.Min());
}
