using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public enum SeasonEffect { Comfort, Fascination, Fear, Affection, Suspicion, Sickness, Silence }

public sealed class CultistSeasonSettings : Def
{
    public float minimumDays = 3f;
    public float durationDays = 4f;
    public int activeLimit = 2;
    public float ordinaryChance = 0.4f;
    public float archivistChance = 0.85f;
    public float affectionRomanceChance = 0.10f;
    public List<IncidentDef> pool = new();
}

public sealed class CultistSeasonCondition : GameCondition
{
}

public sealed class CultistSeasonEffect : DefModExtension
{
    public SeasonEffect effect;
}

public sealed class IncidentWorker_CultistSeason : IncidentWorker
{
    protected override bool CanFireNowSub(IncidentParms parms) =>
        base.CanFireNowSub(parms) && parms.target is Map map
        && Campaign.Current?.CanStartSeason(map, def.gameCondition, Find.TickManager.TicksGame) == true;

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        if (parms.target is not Map map || Campaign.Current is not { } campaign)
            return false;

        int now = Find.TickManager.TicksGame;
        if (!campaign.CanStartSeason(map, def.gameCondition, now))
            return false;

        var settings = Campaign.SeasonSettings;
        int duration = System.Math.Max(1, (int)(settings.durationDays * GenDate.TicksPerDay));
        GameCondition condition = GameConditionMaker.MakeCondition(def.gameCondition, duration);
        map.gameConditionManager.RegisterCondition(condition);
        campaign.RecordSeason(def.GetModExtension<CultistSeasonEffect>().effect, now);
        return true;
    }
}

public sealed partial class Campaign
{
    private int nextSeasonCheck = -1;
    private int lastSeasonStart = -1;
    internal static CultistSeasonSettings SeasonSettings =>
        DefDatabase<CultistSeasonSettings>.GetNamed("Cultist_SeasonCadence");

    private void ExposeSeasons()
    {
        Scribe_Values.Look(ref nextSeasonCheck, "nextSeasonCheck", -1);
        Scribe_Values.Look(ref lastSeasonStart, "lastSeasonStart", -1);
    }

    private static int MinimumSeasonTicks =>
        System.Math.Max(250, (int)(SeasonSettings.minimumDays * GenDate.TicksPerDay));

    private bool CanFollowSeason(Map map) => ModsConfig.IdeologyActive && map is { IsPlayerHome: true }
        && stage >= CampaignStage.Dreaming && leader is { Dead: false, Spawned: true }
        && leader.Map == map && IsMember(leader) && !awaitingEnding && !VigilInProgress && LeaderDream != null;

    internal bool CanStartSeason(Map map, GameConditionDef condition, int now) =>
        CanFollowSeason(map)
        && (lastSeasonStart < 0 || now - lastSeasonStart >= MinimumSeasonTicks)
        && !map.gameConditionManager.ConditionIsActive(condition)
        && Find.Maps.Sum(candidate => candidate.gameConditionManager.ActiveConditions
            .Count(active => active is CultistSeasonCondition && !active.Expired)) < SeasonSettings.activeLimit;

    private void TickSeasons(int now)
    {
        Map map = leader?.Map;
        if (!CanFollowSeason(map))
            return;
        if (nextSeasonCheck < 0)
            nextSeasonCheck = now + MinimumSeasonTicks;
        if (now < nextSeasonCheck)
            return;

        var settings = SeasonSettings;
        if (HasQueuedSeason() || !settings.pool
            .Where(candidate => CanStartSeason(map, candidate.gameCondition, now)).TryRandomElement(out IncidentDef incident))
            return;

        nextSeasonCheck = now + MinimumSeasonTicks;
        if (!Rand.Chance(Find.Storyteller.def.defName == "Cultist_Archivist"
                ? settings.archivistChance : settings.ordinaryChance))
            return;

        var parms = new IncidentParms { target = map };
        const int delay = 250;
        Find.Storyteller.incidentQueue.Add(incident, now + delay, parms, GenDate.TicksPerHour * 6);
    }

    private static bool HasQueuedSeason()
    {
        foreach (QueuedIncident queued in Find.Storyteller.incidentQueue)
        {
            if (queued.FiringIncident.def.Worker is IncidentWorker_CultistSeason)
                return true;
        }

        return false;
    }

    internal void RecordSeason(SeasonEffect effect, int now)
    {
        lastSeasonStart = now;
        nextSeasonCheck = now + MinimumSeasonTicks;
        if (effect == SeasonEffect.Affection)
        {
            AffectionSeason.Apply(leader.Map, Rand.Chance(SeasonSettings.affectionRomanceChance));
            return;
        }
        if (effect == SeasonEffect.Suspicion)
        {
            ApplySuspicionSeason(leader.Map);
            return;
        }
        if (effect == SeasonEffect.Sickness)
        {
            HediffDef illness = InflictRandomCurableDisease(leader);
            Messages.Message(
                illness != null
                    ? "Cultist.SicknessSeason".Translate(leader.LabelShortCap, illness.LabelCap)
                    : "Cultist.SicknessSeasonSpared".Translate(leader.LabelShortCap),
                leader, MessageTypeDefOf.NegativeEvent);
            return;
        }
        if (effect == SeasonEffect.Silence)
        {
            HediffDef silence = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_SeasonSilence");
            if (silence != null && leader?.health != null)
            {
                Hediff existing = leader.health.hediffSet.GetFirstHediffOfDef(silence);
                if (existing != null)
                    leader.health.RemoveHediff(existing);
                leader.health.AddHediff(silence);
            }
            Messages.Message("Cultist.SilenceSeason".Translate(leader.LabelShortCap),
                leader, MessageTypeDefOf.NeutralEvent);
            return;
        }

        PersonalDream state = LeaderDream;
        if (effect == SeasonEffect.Comfort)
        {
            state.Comfort.Add(now + GenDate.TicksPerDay);
            GrantLore(leader, 1);
            Messages.Message("Cultist.QuietSeason".Translate(), leader, MessageTypeDefOf.PositiveEvent);
            return;
        }

        bool fear = effect == SeasonEffect.Fear;
        state.AddRisk(fear, now);
        Messages.Message((fear ? "Cultist.DarkSeason" : "Cultist.BrightSeason").Translate(),
            leader, MessageTypeDefOf.NegativeEvent);
    }

    internal static HediffDef InflictRandomCurableDisease(Pawn pawn)
    {
        if (pawn?.health == null)
            return null;
        string[] candidates = { "Flu", "Malaria", "Plague", "GutWorms", "MuscleParasites" };
        var available = candidates
            .Select(DefDatabase<HediffDef>.GetNamedSilentFail)
            .Where(def => def != null && !pawn.health.hediffSet.HasHediff(def))
            .ToList();
        if (!available.TryRandomElement(out HediffDef chosen))
            return null;
        Hediff hediff = HediffMaker.MakeHediff(chosen, pawn);
        hediff.Severity = 0.05f;
        pawn.health.AddHediff(hediff);
        return chosen;
    }

    private void ApplySuspicionSeason(Map map)
    {
        const float normalRaidThreshold = 40f;
        if (notoriety > normalRaidThreshold * 0.75f && map != null)
        {
            if (TryFireFactionRaid(map, "Cultist_SuppressionBureau",
                    "Cultist.SuspicionRaidTitle".Translate(),
                    "Cultist.SuspicionRaidBody".Translate(),
                    0.85f + notoriety / 200f))
            {
                ReduceNotoriety(25f);
                return;
            }
        }
        AddNotoriety(5f);
        Messages.Message("Cultist.SuspicionSeasonQuiet".Translate(), leader, MessageTypeDefOf.NegativeEvent);
    }
}
