using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed class CultistRiteDef : Def
{
    public bool starterUnlocked;
    public bool allowLore = true;
    public bool allowTool = true;
    public bool allowInfluence = true;
    public bool allowAssistant;
    public bool allowSacrifice;
    public bool consumeLore = true;
    public bool consumeInfluence = true;
    public float backlashModifier;
}

public sealed class CompProperties_RiteScript : CompProperties
{
    public CultistRiteDef riteDef;
    public CompProperties_RiteScript() => compClass = typeof(CompRiteScript);
}

public sealed class CompRiteScript : ThingComp
{
    public CultistRiteDef RiteDef => ((CompProperties_RiteScript)props).riteDef;

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || RiteDef == null || !pawn.IsColonistPlayerControlled)
            yield break;

        if (campaign.IsRiteUnlocked(RiteDef))
        {
            yield return new FloatMenuOption("Cultist.Rite.AlreadyKnown".Translate(RiteDef.LabelCap), null);
            yield break;
        }

        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield return new FloatMenuOption("Cultist.Unreachable".Translate(parent.LabelShortCap), null);
            yield break;
        }

        yield return new FloatMenuOption(
            "Cultist.Rite.LearnOption".Translate(RiteDef.LabelCap),
            () =>
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamed("Cultist_LearnRiteScript");
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc);
            });
    }
}

public sealed class JobDriver_LearnRiteScript : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => job.targetA.Thing.IsForbidden(pawn));
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(200).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            Thing script = job.targetA.Thing;
            CompRiteScript comp = script?.TryGetComp<CompRiteScript>();
            if (comp?.RiteDef == null || Campaign.Current is not { } campaign)
                return;

            campaign.UnlockRite(comp.RiteDef, pawn);
            script.Destroy();
        });
    }
}

public sealed partial class Campaign
{
    private HashSet<string> unlockedRites = new();

    private void ExposeRites()
    {
        Scribe_Collections.Look(ref unlockedRites, "unlockedRites", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            unlockedRites ??= new HashSet<string>();
    }

    public bool IsRiteUnlocked(CultistRiteDef rite) =>
        rite != null && (rite.starterUnlocked || unlockedRites.Contains(rite.defName));

    public IEnumerable<CultistRiteDef> UnlockedRites =>
        DefDatabase<CultistRiteDef>.AllDefsListForReading.Where(IsRiteUnlocked);

    public void UnlockRite(CultistRiteDef rite, Pawn learner = null)
    {
        if (rite == null)
            return;
        if (unlockedRites.Add(rite.defName))
        {
            PlayCultistSound("Cultist_Sfx_RiteComplete");
            Messages.Message("Cultist.Rite.Learned".Translate(learner?.LabelShortCap ?? "Cultist.ChurchName".Translate(), rite.LabelCap),
                learner, MessageTypeDefOf.PositiveEvent);
        }
    }

    internal static ThingDef RandomLockedRiteScriptDef()
    {
        var scripts = DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.GetCompProperties<CompProperties_RiteScript>() is { riteDef: { } r }
                && (Current == null || !Current.IsRiteUnlocked(r)))
            .ToList();
        return scripts.Count > 0 ? scripts.RandomElement() : null;
    }
}
