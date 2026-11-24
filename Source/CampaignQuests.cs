using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Cultist;

public sealed class MarkDef : Def
{
    public int number;
    public int readTier;
    public int books;
    public ResearchProjectDef research;
    public int influence;
    public bool needsDisciple;
    public bool needsPlate;
    public bool needsGear;
}

public sealed class CampaignQuestTarget : QuestPart
{
    public WorldObject target;

    public override IEnumerable<GlobalTargetInfo> QuestLookTargets
    {
        get
        {
            if (target is { Spawned: true })
                yield return target;
        }
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref target, "target");
    }
}

public sealed partial class Campaign
{
    private Quest openingQuest;
    private Quest chapterQuest;
    private Quest expeditionQuest;
    private Quest vigilQuest;
    private Quest seerQuest;
    private Quest repairQuest;
    private Quest transcriptQuest;
    private int chapterQuestNumber;
    private bool vigilOffered;

    internal MarkDef ActiveMark => DefDatabase<MarkDef>.AllDefsListForReading
        .FirstOrDefault(node => node.number == marks + 1);

    private void ExposeCampaignQuests()
    {
        Scribe_References.Look(ref openingQuest, "openingQuest");
        Scribe_References.Look(ref chapterQuest, "chapterQuest");
        Scribe_References.Look(ref expeditionQuest, "expeditionQuest");
        Scribe_References.Look(ref vigilQuest, "vigilQuest");
        Scribe_References.Look(ref seerQuest, "seerQuest");
        Scribe_References.Look(ref repairQuest, "repairQuest");
        Scribe_References.Look(ref transcriptQuest, "transcriptQuest");
        Scribe_Values.Look(ref chapterQuestNumber, "chapterQuestNumber");
        Scribe_Values.Look(ref vigilOffered, "vigilOffered");
        ExposeFieldQuests();
    }

    private static Quest CreateCampaignQuest(string title, TaggedString description, int stars,
        bool offered = false, WorldObject target = null)
    {
        Quest quest = Quest.MakeRaw();
        quest.root = DefDatabase<QuestScriptDef>.GetNamed("Cultist_TrackedQuest");
        quest.name = title;
        quest.description = description;
        quest.challengeRating = stars;
        if (target != null)
            quest.AddPart(new CampaignQuestTarget { target = target });
        if (offered)
            quest.SetNotYetAccepted();
        else
            quest.SetInitiallyAccepted();
        Find.QuestManager.Add(quest);
        QuestUtility.SendLetterQuestAvailable(quest);
        return quest;
    }

    private static void EndCampaignQuest(ref Quest quest, QuestEndOutcome outcome)
    {
        if (quest?.State == QuestState.Ongoing)
            quest.End(outcome);
        quest = null;
    }

    private void TickCampaignQuests(int now)
    {
        if (stage < CampaignStage.Founding)
        {
            TickOpeningQuest(now);
            return;
        }
        if (stage < CampaignStage.Dreaming)
            return;

        SyncChapterQuest();

        if (HasActiveExpedition && expeditionQuest == null && marks < 5 && !HasPlateAtHome())
            expeditionQuest = CreateCampaignQuest("Cultist.ExpeditionTitle".Translate(),
                "Cultist.Quest.Expedition".Translate(), 2, target: expedition);
        if (expeditionQuest?.State == QuestState.Ongoing)
        {
            if (HasPlateAtHome() || marks >= 5)
                EndCampaignQuest(ref expeditionQuest, QuestEndOutcome.Success);
            else if (!HasActiveExpedition && !HasAccessiblePlate())
                EndCampaignQuest(ref expeditionQuest, QuestEndOutcome.Fail);
        }
        TickSideQuests();
        TickFieldQuests(now);
    }

    private void TickOpeningQuest(int now)
    {
        if (stage == CampaignStage.Offered && openingQuest?.State == QuestState.Ongoing)
            AcceptJournal();

        if (!Find.Maps.Any(map => map.IsPlayerHome))
            return;

        if ((stage == CampaignStage.Waiting && now >= 5 * GenDate.TicksPerDay)
            || (stage == CampaignStage.Offered && openingQuest == null
                && !Find.LetterStack.LettersListForReading.Any(letter => letter is JournalOffer)))
        {
            OfferJournal();
        }

        if (stage != CampaignStage.Reading)
            return;
        if (JournalExists())
        {
            missingSince = -1;
            return;
        }
        if (missingSince < 0)
            missingSince = now;
        if (now - missingSince < GenDate.TicksPerDay)
            return;
        EndCampaignQuest(ref openingQuest, QuestEndOutcome.Fail);
        OfferJournal();
    }

    private void SyncChapterQuest()
    {
        if (chapterQuestNumber <= marks && chapterQuest != null)
            EndCampaignQuest(ref chapterQuest, QuestEndOutcome.Success);
        if (ActiveMark is { } node && node.number != chapterQuestNumber)
        {
            chapterQuest = CreateCampaignQuest(node.LabelCap, node.description, node.number >= 4 ? 2 : 1);
            chapterQuestNumber = node.number;
        }
        if (marks == 6 && !completed && !vigilOffered)
            EnsureVigilQuest();
    }

    private void EnsureVigilQuest()
    {
        if (vigilQuest?.State != QuestState.Ongoing)
            vigilQuest = CreateCampaignQuest("Cultist.Quest.Vigil.Title".Translate(),
                "Cultist.Quest.Vigil.Description".Translate(VigilThreshold, VigilDurationHours), 4);
        vigilOffered = true;
    }

    private bool HasPlateAtHome() => Find.Maps.Any(map => FindReturnedPlate(map) != null);

    private void TickSideQuests()
    {
        if (marks >= 4 && seerQuest == null && !ranks.Values.Any(rank => rank >= 3))
            seerQuest = CreateCampaignQuest("Cultist.Quest.Seer.Title".Translate(),
                "Cultist.Quest.Seer.Description".Translate(ActivePath.promotionInfluenceCost), 1);
        if (seerQuest?.State == QuestState.Ongoing && ranks.Values.Any(rank => rank >= 3))
            seerQuest.End(QuestEndOutcome.Success);

        if (transcriptQuest == null && SettlementHas("Cultist_SurveyTranscript"))
            EnsureTranscriptQuest();

        if (repairQuest == null && SettlementHas("Cultist_RiteTool2Cracked"))
            repairQuest = CreateCampaignQuest("Cultist.Quest.Repair.Title".Translate(),
                "Cultist.Quest.Repair.Description".Translate(), 1);
        if (repairQuest?.State == QuestState.Ongoing && SettlementHas("Cultist_RiteTool2Ready"))
            repairQuest.End(QuestEndOutcome.Success);
    }

    private static bool SettlementHas(string defName)
    {
        ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
        return Find.Maps.Any(map => map.IsPlayerHome && map.listerThings.ThingsOfDef(def).Count != 0);
    }

    private void EnsureTranscriptQuest() => transcriptQuest ??= CreateCampaignQuest(
        "Cultist.Quest.Transcript.Title".Translate(), "Cultist.Quest.Transcript.Description".Translate(), 1);

    private void TrackTranscriptQuest()
    {
        EnsureTranscriptQuest();
        if (transcriptQuest.State == QuestState.Ongoing)
            transcriptQuest.End(QuestEndOutcome.Success);
    }
}
