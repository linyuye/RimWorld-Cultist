using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private Dictionary<string, int> ranks = new();
    private int marks;
    private Thing altar;
    private int vigilUntil = -1;
    private bool vigilRequiresInputs;
    private Thing vigilBook;
    private Thing vigilTool;
    private Pawn vigilAssistant;
    internal int VigilThreshold => System.Math.Max(2, ActivePath.vigilAspectThreshold);
    internal int VigilDurationHours => System.Math.Max(1, ActivePath.vigilDurationHours);
    internal int VigilDurationTicks => VigilDurationHours * GenDate.TicksPerHour;
    private bool awaitingEnding;
    private bool completed;
    private Pawn returnedLeader;

    public int Marks => marks;
    public bool AwaitingEnding => awaitingEnding;
    public bool Completed => completed;
    public bool VigilInProgress => vigilUntil >= 0;
    public bool IsMember(Pawn pawn) => church != null && pawn?.Ideo == church;

    public int Rank(Pawn pawn) => IsMember(pawn)
        ? ranks.GetValueOrDefault(pawn.GetUniqueLoadID(), 1)
        : 0;

    private void ExposeSociety()
    {
        Scribe_Collections.Look(ref ranks, "societyRanks", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref marks, "marks");
        Scribe_References.Look(ref altar, "vigilAltar");
        Scribe_Values.Look(ref vigilUntil, "vigilUntil", -1);
        Scribe_Values.Look(ref vigilRequiresInputs, "vigilRequiresInputs");
        Scribe_References.Look(ref vigilBook, "vigilBook");
        Scribe_References.Look(ref vigilTool, "vigilTool");
        Scribe_References.Look(ref vigilAssistant, "vigilAssistant");
        Scribe_Values.Look(ref awaitingEnding, "awaitingEnding");
        Scribe_Values.Look(ref completed, "completed");
        Scribe_References.Look(ref returnedLeader, "returnedLeader");
        ExposeEnding();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            ranks ??= new Dictionary<string, int>();
    }

    public bool CanSucceed(Pawn pawn) => IsSuccessionCandidate(pawn)
        && (LeaderOutOfOffice || (leaderUnavailableSince >= 0 && LeaderUnavailable
            && Find.TickManager.TicksGame >= leaderUnavailableSince + GenDate.TicksPerDay));

    internal bool CanAppointSuccessor(Pawn pawn) => IsSuccessionCandidate(pawn)
        && (LeaderOutOfOffice || LeaderUnavailable);

    private bool IsSuccessionCandidate(Pawn pawn) => stage >= CampaignStage.Dreaming
        && !VigilInProgress && !awaitingEnding && pawn != leader && IsMember(pawn)
        && pawn is { Spawned: true, IsColonistPlayerControlled: true, Downed: false, Dead: false };

    public void Succeed(Pawn pawn)
    {
        if (CanSucceed(pawn))
            AppointSuccessor(pawn);
    }

    internal void AppointSuccessor(Pawn pawn)
    {
        if (!CanAppointSuccessor(pawn))
            return;
        if (LeaderDream is { } previousDream)
            previousDream.AutoAt = -1;
        leader = pawn;
        RegisterDreamer(pawn);
        SyncLeaderIdeoRole();
        leaderUnavailableSince = -1;
        if (offers != null)
            CancelDream();
        Messages.Message("Cultist.SuccessorMade".Translate(pawn.LabelShortCap), pawn,
            MessageTypeDefOf.PositiveEvent);
    }

    public TaggedString PromotionRequirement(Pawn pawn)
    {
        return Rank(pawn) switch
        {
            2 when ChurchAspect == Aspect.SecretHistories => "Cultist.StHydraNoExalted".Translate(),
            2 when GetAspect(pawn) != ChurchAspect => "Cultist.NeedLantern".Translate(),
            2 when Influence < ActivePath.promotionInfluenceCost => "Cultist.NeedPromotionInputs".Translate(),
            3 => "Cultist.RankMaximum".Translate(),
            _ => TaggedString.Empty
        };
    }

    internal void Promote(Pawn pawn, IReadOnlyList<Thing> books)
    {
        int rank = Rank(pawn);
        if (pawn.Dead || pawn.Downed || !pawn.IsColonistPlayerControlled
            || rank is < 1 or > 2)
            return;
        TaggedString requirement = PromotionRequirement(pawn);
        if (!requirement.NullOrEmpty())
        {
            Messages.Message(requirement, pawn, MessageTypeDefOf.RejectInput);
            return;
        }
        ConsumeLore(books);
        if (rank == 2)
            ConsumeInfluence(ActivePath.promotionInfluenceCost);
        ranks[pawn.GetUniqueLoadID()] = rank + 1;
        AddNotoriety(rank == 2 ? 12f : 6f, altar, pawn);
        ApplySanctuaryRiteMood(pawn, altar);
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Promoted".Translate(pawn.LabelShortCap,
            ("Cultist.Rank." + (rank + 1)).Translate()), pawn, MessageTypeDefOf.PositiveEvent);
        if (rank + 1 >= 3)
            AwakenExaltedTraits(pawn, announce: true);
    }

    public TaggedString MarkRequirement(Pawn pawn)
    {
        MarkDef node = ActiveMark;
        if (node == null)
            return TaggedString.Empty;
        if (LearnedLoreTier < node.readTier)
            return "Cultist.NeedLearnedLore".Translate();
        if (node.research is { IsFinished: false })
            return "Cultist.NeedResearch".Translate();
        if (Influence < node.influence)
            return "Cultist.MarkNeedsInfluence".Translate(node.influence);
        if (node.needsDisciple && !pawn.Map.mapPawns.FreeColonistsSpawned.Any(other => other != pawn && Rank(other) >= 2))
            return "Cultist.NeedDisciple".Translate();
        if (node.needsPlate && FindReturnedPlate(pawn.Map) == null)
            return "Cultist.NeedPlate".Translate();
        if (node.needsGear && !(ChurchAspect == Aspect.SecretHistories ? HasAnyAffinityGear(pawn) : HasPathGear(pawn, ChurchAspect)))
            return "Cultist.NeedGear".Translate();
        return TaggedString.Empty;
    }

    internal void RecordMark(Pawn pawn, IReadOnlyList<Thing> books)
    {
        if (!CanLeadRite(pawn) || marks >= 6)
            return;
        TaggedString requirement = MarkRequirement(pawn);
        if (!requirement.NullOrEmpty())
        {
            Messages.Message(requirement, pawn, MessageTypeDefOf.RejectInput);
            return;
        }
        MarkDef node = ActiveMark;
        ConsumeLore(books);
        if (node.influence > 0)
            ConsumeInfluence(node.influence);
        if (node.needsPlate)
        {
            FindReturnedPlate(pawn.Map).Destroy();
            EndCampaignQuest(ref expeditionQuest, QuestEndOutcome.Success);
        }
        marks = node.number;
        AddNotoriety(10f, altar, pawn);
        ApplySanctuaryRiteMood(pawn, altar);
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        SyncChapterQuest();
        Messages.Message("Cultist.MarkEarned".Translate(marks), pawn, MessageTypeDefOf.PositiveEvent);
    }

    public bool CanLeadRite(Pawn pawn) => pawn == leader && pawn.Spawned && !pawn.Downed
        && !pawn.Dead && pawn.IsColonistPlayerControlled && IsMember(pawn) && !awaitingEnding && !VigilInProgress;

    private void ConsumeInfluence(int minimum)
    {
        if (LeaderDream is not { Influences.Count: > 0 } dream)
            return;
        List<DreamInfluence> influences = dream.Influences;
        DreamInfluence best = (ChurchAspect == Aspect.SecretHistories
                ? influences.Where(v => v.Strength >= minimum)
                : influences.Where(v => v.Affinity == ChurchAspect && v.Strength >= minimum))
            .OrderBy(v => v.Strength).ThenBy(v => v.Expires).FirstOrDefault()
            ?? influences.Where(v => v.Strength >= minimum)
                .OrderBy(v => v.Strength).ThenBy(v => v.Expires).FirstOrDefault();
        if (best != null)
            influences.Remove(best);
    }

    internal bool IsVigilAssistant(Pawn pawn, Thing site, Pawn other, bool near) =>
        other is { Spawned: true, Downed: false, IsColonistPlayerControlled: true }
        && other != pawn && other.Map == site.Map && IsMember(other)
        && (!near || other.Position.InHorDistOf(site.Position, RiteRadius));

    internal TaggedString VigilRequirement(Thing site) =>
        GenHostility.AnyHostileActiveThreatToPlayer(site.Map)
            ? "Cultist.VigilUnavailable".Translate() : TaggedString.Empty;

    internal void BeginVigil(Pawn pawn, Thing site, Thing book, Thing tool, Pawn helper)
    {
        if (!CanLeadRite(pawn) || completed || marks != 6 || site.Map != pawn.Map)
            return;
        if (!VigilRequirement(site).NullOrEmpty() || !IsRiteMaterial(pawn, site, book)
            || !IsRiteMaterial(pawn, site, tool) || !CanPairVigilMaterials(book, tool)
            || !IsVigilAssistant(pawn, site, helper, near: true))
        {
            Messages.Message("Cultist.RiteInputsChanged".Translate(), site, MessageTypeDefOf.RejectInput);
            return;
        }
        Map map = site.Map;
        var raid = IncidentDefOf.RaidEnemy;
        var parms = StorytellerUtility.DefaultParmsNow(raid.category, map);
        float mult = ChurchAspect == Aspect.Edge ? 1.50f : 1.30f;
        parms.points = System.Math.Max(250f, parms.points * mult);
        if (ChurchAspect == Aspect.Edge)
            parms.faction = EnsureFaction("Cultist_RivalLongFaction") ?? parms.faction;
        if (!raid.Worker.TryExecute(parms))
        {
            Messages.Message("Cultist.VigilUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }
        ApplyAspectVigilCrisis(map, pawn);
        EnsureVigilQuest();
        vigilRequiresInputs = true;
        vigilBook = book;
        vigilTool = tool;
        vigilAssistant = helper;
        altar = site;
        vigilUntil = Find.TickManager.TicksGame + VigilDurationTicks;
        Messages.Message("Cultist.VigilStarted".Translate(), site, MessageTypeDefOf.ThreatBig);
    }

    private void ApplyAspectVigilCrisis(Map map, Pawn pawn)
    {
        int halfDay = GenDate.TicksPerDay / 2;
        switch (ChurchAspect)
        {
            case Aspect.Forge:
                if (DefDatabase<GameConditionDef>.GetNamedSilentFail("HeatWave") is { } heat)
                    map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(heat, halfDay));
                break;
            case Aspect.Winter:
                if (DefDatabase<GameConditionDef>.GetNamedSilentFail("ColdSnap") is { } cold)
                    map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(cold, halfDay));
                break;
            case Aspect.Moth:
                if (DefDatabase<WeatherDef>.GetNamedSilentFail("Fog") is { } fog)
                    map.weatherManager.TransitionTo(fog);
                break;
            case Aspect.Grail:
                int now = Find.TickManager.TicksGame;
                LeaderDream?.AddRisk(false, now);
                LeaderDream?.Comfort.Add(now + GenDate.TicksPerDay);
                break;
            case Aspect.Heart:
                IncidentParms wave2 = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                wave2.points = System.Math.Max(150f, wave2.points * 0.65f);
                Find.Storyteller.incidentQueue.Add(IncidentDefOf.RaidEnemy, Find.TickManager.TicksGame + GenDate.TicksPerHour * 3, wave2);
                break;
            case Aspect.SecretHistories:
                SpawnBookExplorationSite(pawn, ActivePath.churchNameKey.Translate(), 4);
                break;
        }
    }

    internal bool CanPairVigilMaterials(Thing book, Thing tool) =>
        book.TryGetComp<RitualMaterial>()?.LoreTier is > 0 and var tier
        && tool.TryGetComp<RitualMaterial>() is { ToolLight: > 0 } toolMat
        && (ChurchAspect == Aspect.SecretHistories || toolMat.Aspect == ChurchAspect)
        && (2 * tier + toolMat.ToolLight) >= VigilThreshold;

    private void TickVigil(int now)
    {
        if (!VigilInProgress)
            return;
        if (altar is not { Spawned: true } || leader is not { Spawned: true, Dead: false }
            || leader.Map != altar.Map || !IsMember(leader))
        {
            ClearVigil();
            EndCampaignQuest(ref vigilQuest, QuestEndOutcome.Fail);
            Messages.Message("Cultist.VigilFailed".Translate(), MessageTypeDefOf.NegativeEvent);
            return;
        }
        if (now < vigilUntil || leader.Downed || GenHostility.AnyHostileActiveThreatToPlayer(altar.Map))
            return;
        if (vigilRequiresInputs)
        {
            if (!IsRiteMaterial(leader, altar, vigilBook) || !IsRiteMaterial(leader, altar, vigilTool)
                || !IsVigilAssistant(leader, altar, vigilAssistant, near: false))
            {
                Messages.Message("Cultist.RiteInputsChanged".Translate(), altar, MessageTypeDefOf.RejectInput);
                ClearVigil();
                EndCampaignQuest(ref vigilQuest, QuestEndOutcome.Fail);
                return;
            }
            vigilBook.Destroy();
        }
        ClearVigil();
        awaitingEnding = true;
        pendingEnding = VigilEnding.None;
    }

    private void ClearVigil()
    {
        altar = null;
        vigilUntil = -1;
        vigilRequiresInputs = false;
        vigilBook = null;
        vigilTool = null;
        vigilAssistant = null;
    }

}
