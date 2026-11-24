using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed class JournalOffer : ChoiceLetter
{
    public override IEnumerable<DiaOption> Choices
    {
        get
        {
            yield return new DiaOption("Cultist.Accept".Translate())
            {
                resolveTree = true,
                action = () =>
                {
                    if (Campaign.Current?.AcceptJournal() == true)
                        Find.LetterStack.RemoveLetter(this);
                }
            };
            yield return new DiaOption("Cultist.Later".Translate()) { resolveTree = true };
        }
    }
}

public sealed class InteractionProperties : CompProperties
{
    public bool bed;
    public bool transcript;
    public ScholarDocument scholarDocument;
    public InteractionProperties() => compClass = typeof(CultistInteraction);
}

public sealed class CultistInteraction : ThingComp
{
    private bool studied;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref studied, "cultistStudied");
    }

    public override string CompInspectStringExtra() =>
        studied ? "Cultist.TranscriptStudied".Translate().ToString() : null;

    private int LoreTier => parent.TryGetComp<RitualMaterial>()?.LoreTier ?? 0;

    private ScholarDocument ScholarDocument => ((InteractionProperties)props).scholarDocument;
    private bool IsQuestDocument => ScholarDocument != ScholarDocument.None;

    private bool CanStudy(Pawn pawn, Campaign campaign)
    {
        if (!pawn.IsColonistPlayerControlled || !campaign.IsMember(pawn))
            return false;
        if (IsQuestDocument)
            return !studied && campaign.CanReadScholarDocument(pawn, ScholarDocument);
        return IsTranscript ? !studied : LoreTier > campaign.LearnedLoreTier;
    }

    private bool IsTranscript => ((InteractionProperties)props).transcript;
    public bool IsStudyMaterial => IsTranscript || IsQuestDocument || LoreTier > 0;

    public void Study(Pawn pawn, Campaign campaign)
    {
        if (parent.IsForbidden(pawn) || !CanStudy(pawn, campaign))
            return;
        if (IsTranscript)
        {
            campaign.RecordTranscript(pawn);
            studied = true;
            return;
        }
        if (IsQuestDocument)
        {
            studied = campaign.ReadScholarDocument(pawn, ScholarDocument);
            return;
        }
        campaign.LearnLore(pawn, LoreTier);
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        Campaign campaign = Campaign.Current;
        if (campaign == null || !ModsConfig.IdeologyActive || !pawn.IsColonistPlayerControlled)
            yield break;
        bool bed = ((InteractionProperties)props).bed;
        if (bed && pawn == campaign.Leader && campaign.Stage >= CampaignStage.Dreaming)
            yield return new FloatMenuOption(
                (campaign.AutoDream ? "Cultist.AutoOff" : "Cultist.AutoOn").Translate(),
                campaign.ToggleAutoDream);
        bool available = bed ? campaign.CanDream(pawn)
            : IsStudyMaterial ? CanStudy(pawn, campaign)
            : (campaign.Stage == CampaignStage.Reading || campaign.CanRefoundSociety);
        if (!available)
            yield break;
        JobDef def = bed ? CultistDefs.Cultist_EnterDream : CultistDefs.Cultist_ReadJournal;
        string label = bed ? "Cultist.DreamAction" : IsTranscript ? "Cultist.StudyTranscript"
            : IsQuestDocument ? "Cultist.StudyScholarDocument"
            : LoreTier > 0 ? "Cultist.StudyLore" : "Cultist.ReadAction";
        bool reachable = bed && parent is Building_Bed targetBed
            ? CultistInteractionJob.CanReachDreamBed(pawn, targetBed)
            : pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some);
        if (!reachable)
        {
            yield return new FloatMenuOption("Cultist.Unreachable".Translate(label.Translate()), null);
            yield break;
        }
        yield return new FloatMenuOption(label.Translate(), () =>
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(def, parent), JobTag.Misc));
    }
}

public sealed class CultistInteractionJob : JobDriver
{
    private bool IsDream => job.def == CultistDefs.Cultist_EnterDream;
    private int dreamStarted = -1;

    internal static bool CanReachDreamBed(Pawn pawn, Building_Bed bed) =>
        bed.Spawned && !bed.IsForbidden(pawn) && !Toils_Bed.BedNoLongerUsable(pawn, bed, false)
        && pawn.CanReserveAndReach(bed, PathEndMode.ClosestTouch, Danger.Some, bed.SleepingSlotsCount, 0);

    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        IsDream && job.targetA.Thing is Building_Bed bed
            ? pawn.Reserve(bed, job, bed.SleepingSlotsCount, 0, errorOnFailed: errorOnFailed)
            : pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref dreamStarted, "cultistDreamStarted", -1);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        if (IsDream)
        {
            this.FailOn(() => Campaign.Current?.CanDream(pawn) != true
                || job.targetA.Thing.IsForbidden(pawn)
                || (dreamStarted >= 0 && pawn.mindState.lastHarmTick >= dreamStarted));
            yield return Toils_Bed.GotoBed(TargetIndex.A);
            Toil prepare = Toils_LayDown.LayDown(TargetIndex.A, hasBed: true,
                lookForOtherJobs: false, canSleep: false, gainRestAndHealth: false);
            prepare.AddPreInitAction(() => dreamStarted = Find.TickManager.TicksGame);
            prepare.defaultCompleteMode = ToilCompleteMode.Delay;
            prepare.defaultDuration = Campaign.DreamPreparationTicks;
            yield return prepare.WithProgressBarToilDelay(TargetIndex.A);
            yield return Toils_General.Do(() =>
            {
                if (pawn.CurrentBed() == job.targetA.Thing)
                    Campaign.Current?.BeginDream(pawn);
            });
            yield break;
        }

        this.FailOn(() => job.targetA.Thing.IsForbidden(pawn));
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        int studyTicks = Campaign.CalculateStudyTicks(pawn, job.targetA.Thing, 600);
        yield return Toils_General.Wait(studyTicks).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            if (Campaign.Current is not { } campaign)
                return;
            if (job.targetA.Thing.TryGetComp<CultistInteraction>() is { IsStudyMaterial: true } interaction)
            {
                interaction.Study(pawn, campaign);
                Campaign.ApplyLibraryStudyBonus(pawn, job.targetA.Thing);
            }
            else
                campaign.Read(pawn);
        });
    }
}
