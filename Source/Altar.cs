using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public enum AltarAction { Succeed, Promote, Mark, Vigil, Expedition, RelieveMemory, RelieveFear, RelieveComfort, AnswerInquiry, SignWitness }

public sealed class AltarProperties : CompProperties
{
    public AltarProperties() => compClass = typeof(AltarComp);
}

public sealed class AltarComp : ThingComp
{
    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || !pawn.IsColonistPlayerControlled
            || !campaign.IsMember(pawn))
            yield break;

        if (campaign.CanSucceed(pawn))
            yield return Option(pawn, AltarAction.Succeed);
        if (campaign.Rank(pawn) < 3)
            yield return Option(pawn, AltarAction.Promote, campaign.PromotionRequirement(pawn));
        if (campaign.CanRelieve(pawn, AltarAction.RelieveMemory))
            yield return Option(pawn, AltarAction.RelieveMemory);
        if (campaign.CanRelieve(pawn, AltarAction.RelieveFear))
            yield return Option(pawn, AltarAction.RelieveFear);
        if (campaign.CanRelieve(pawn, AltarAction.RelieveComfort))
            yield return Option(pawn, AltarAction.RelieveComfort);
        if (campaign.CanSignWitness(pawn))
            yield return Option(pawn, AltarAction.SignWitness);
        if (campaign.CanConsecrateScar(pawn))
            yield return new FloatMenuOption("Cultist.Altar.ConsecrateScar".Translate(),
                () => Find.WindowStack.Add(new ScarConsecrationWindow(campaign, pawn, parent)));
        if (campaign.CanCalmThresholdWeather(pawn))
            yield return new FloatMenuOption("Cultist.Altar.CalmWeather".Translate(),
                () => campaign.CalmThresholdWeather(pawn, parent));
        if (campaign.CanRefoundSociety)
            yield return new FloatMenuOption("Cultist.Altar.SpawnRefoundJournal".Translate(),
                () => campaign.SpawnRefoundJournal(pawn, parent));
        if (campaign.GuardianNameActive)
        {
            if (campaign.CanInvokeGuardianBoon)
            {
                yield return new FloatMenuOption("Cultist.Altar.GuardianAvatar".Translate(campaign.GuardianNameTitle),
                    () => campaign.InvokeGuardianNameAvatar(pawn, parent));
                yield return new FloatMenuOption("Cultist.Altar.GuardianBlessing".Translate(campaign.GuardianNameTitle),
                    () => campaign.InvokeGuardianNameBlessing(pawn, parent));
            }
            else
            {
                yield return new FloatMenuOption("Cultist.Altar.GuardianCooldown".Translate(campaign.GuardianBoonDaysLeft), null);
            }
        }
        if (!campaign.CanLeadRite(pawn))
            yield break;
        if (campaign.CurrentApostleStage == ApostleStage.Active && !campaign.ApostleVigilInProgress)
        {
            TaggedString apReq = campaign.ApostleGreatWorkRequirement(pawn, parent);
            if (!apReq.NullOrEmpty())
                yield return new FloatMenuOption("Cultist.Altar.ApostleGreatWork".Translate() + ": " + apReq, null);
            else
                yield return new FloatMenuOption("Cultist.Altar.ApostleGreatWork".Translate(),
                    () => campaign.BeginApostleGreatWork(pawn, parent));
        }
        if (campaign.CanAnswerInquiry(pawn))
            yield return Option(pawn, AltarAction.AnswerInquiry);
        yield return new FloatMenuOption("Cultist.Altar.Summon".Translate(),
            () => Find.WindowStack.Add(new SummonWindow(campaign, pawn, parent)));
        if (campaign.ChurchAspect == Aspect.Knock && !campaign.Completed)
        {
            TaggedString req = campaign.KnockVictoryRequirement();
            if (!req.NullOrEmpty())
                yield return new FloatMenuOption("Cultist.Altar.KnockVictory".Translate() + ": " + req, null);
            else
                yield return new FloatMenuOption("Cultist.Altar.KnockVictory".Translate(),
                    () => campaign.TriggerKnockSevenScarsVictory(pawn, parent));
        }

        if (campaign.Marks is >= 3 and < 5)
            yield return Option(pawn, AltarAction.Expedition, campaign.ExpeditionRequirement(pawn));
        if (campaign.Marks < 6)
            yield return Option(pawn, AltarAction.Mark, campaign.MarkRequirement(pawn));
        else if (!campaign.Completed)
            yield return Option(pawn, AltarAction.Vigil, campaign.VigilRequirement(parent));
    }

    private FloatMenuOption Option(Pawn pawn, AltarAction action, TaggedString why = default)
    {
        Campaign campaign = Campaign.Current;
        TaggedString title = ("Cultist.Altar." + action).Translate();
        if (!why.NullOrEmpty())
            return new FloatMenuOption(title + ": " + why, null);
        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
            return new FloatMenuOption("Cultist.Unreachable".Translate(title), null);
        int count = campaign.LoreCost(action, pawn);
        Thing[] books = count == 0 ? System.Array.Empty<Thing>() : Campaign.NearbyMaterials(pawn, parent)
            .Where(item => item.TryGetComp<RitualMaterial>().LoreTier > 0)
            .OrderBy(item => item.TryGetComp<RitualMaterial>().LoreTier)
            .ThenBy(item => item.Position.DistanceToSquared(parent.Position)).ToArray();
        if (books.Length < count)
            return new FloatMenuOption(title + ": " + "Cultist.NeedRiteBooks".Translate(count, Campaign.RiteRadius), null);
        if (action == AltarAction.Vigil)
            return new FloatMenuOption(title, () => ChooseVigilInputs(pawn, books));
        return new FloatMenuOption(title, () => OrderRite(pawn, action, books.Take(count)));
    }

    private void ChooseVigilInputs(Pawn pawn, IEnumerable<Thing> books)
    {
        Find.WindowStack.Add(new FloatMenu(books.Select(book => new FloatMenuOption(
            "Cultist.ChooseRiteBook".Translate(book.LabelCap, book.TryGetComp<RitualMaterial>().LoreTier * 2),
            () => ChooseVigilTool(pawn, book))).ToList()));
    }

    private void ChooseVigilTool(Pawn pawn, Thing book)
    {
        Campaign campaign = Campaign.Current;
        int threshold = campaign?.VigilThreshold ?? 8;
        var tools = Campaign.NearbyMaterials(pawn, parent)
            .Where(item => campaign != null && campaign.CanPairVigilMaterials(book, item))
            .Select(tool => new FloatMenuOption(
                "Cultist.ChooseRiteTool".Translate(tool.LabelCap, tool.TryGetComp<RitualMaterial>().ToolLight),
                () => ChooseVigilAssistant(pawn, book, tool))).ToList();
        if (tools.Count == 0)
            Messages.Message("Cultist.NeedRiteTool".Translate(Campaign.RiteRadius, threshold), parent, MessageTypeDefOf.RejectInput);
        else
            Find.WindowStack.Add(new FloatMenu(tools));
    }

    private void ChooseVigilAssistant(Pawn pawn, Thing book, Thing tool)
    {
        Campaign campaign = Campaign.Current;
        var helpers = parent.Map.mapPawns.FreeColonistsSpawned
            .Where(other => campaign.IsVigilAssistant(pawn, parent, other, near: true))
            .Select(helper => new FloatMenuOption("Cultist.ChooseRiteAssistant".Translate(helper.LabelShortCap),
                () => OrderRite(pawn, AltarAction.Vigil, new[] { book }, tool, helper))).ToList();
        if (helpers.Count == 0)
            Messages.Message("Cultist.NeedRiteAssistant".Translate(Campaign.RiteRadius), parent, MessageTypeDefOf.RejectInput);
        else
            Find.WindowStack.Add(new FloatMenu(helpers));
    }

    private void OrderRite(Pawn pawn, AltarAction action, IEnumerable<Thing> books,
        Thing tool = null, Pawn helper = null)
    {
        Job job = JobMaker.MakeJob(CultistDefs.Cultist_AltarRite, parent);
        job.count = (int)action;
        job.targetQueueB = books.Select(book => new LocalTargetInfo(book)).ToList();
        if (tool != null)
            job.targetB = tool;
        if (helper != null)
            job.targetC = helper;
        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }

    public override string CompInspectStringExtra()
    {
        if (Campaign.Current is not { } campaign || campaign.Stage < CampaignStage.Dreaming)
            return null;
        var summary = "Cultist.AltarSummary".Translate(campaign.Marks, campaign.LearnedLoreTier,
            campaign.Influence, campaign.Fascination, campaign.Fear, campaign.Memory, campaign.Comfort);
        string hq = Campaign.HqSummary(parent);
        string timing = campaign.DreamTiming();
        string combined = summary + "\n" + hq;
        return string.IsNullOrEmpty(timing) ? combined : combined + "\n" + timing;
    }
}

public sealed class AltarRiteJob : JobDriver
{
    private IReadOnlyList<Thing> Books => job.targetQueueB?.Select(target => target.Thing).ToArray()
        ?? System.Array.Empty<Thing>();

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        if (!pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed))
            return false;
        foreach (Thing book in Books)
        {
            if (book == null || !pawn.Reserve(book, job, errorOnFailed: errorOnFailed))
                return false;
        }
        return !job.targetB.IsValid || pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => pawn.Drafted || pawn.Downed || Campaign.Current?.IsMember(pawn) != true);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        int riteTicks = Campaign.EvaluateHqTraits(job.targetA.Thing).HasFlag(HqTrait.Sanctuary) ? 210 : 300;
        yield return Toils_General.Wait(riteTicks).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            if (Campaign.Current is not { } campaign)
                return;
            Thing site = job.targetA.Thing;
            IReadOnlyList<Thing> books = Books;
            AltarAction action = (AltarAction)job.count;
            if (!Campaign.HasRiteLore(pawn, site, books, campaign.LoreCost(action, pawn)))
            {
                Messages.Message("Cultist.RiteInputsChanged".Translate(), site, MessageTypeDefOf.RejectInput);
                return;
            }

            switch (action)
            {
                case AltarAction.Succeed: campaign.Succeed(pawn); break;
                case AltarAction.Promote: campaign.Promote(pawn, books); break;
                case AltarAction.Mark: campaign.RecordMark(pawn, books); break;
                case AltarAction.Expedition: campaign.OpenExpedition(pawn); break;
                case AltarAction.AnswerInquiry: campaign.AnswerInquiry(pawn, site, books); break;
                case AltarAction.SignWitness: campaign.SignWitness(pawn); break;
                case AltarAction.RelieveMemory:
                case AltarAction.RelieveFear:
                case AltarAction.RelieveComfort: campaign.Relieve(pawn, (AltarAction)job.count); break;
                case AltarAction.Vigil: campaign.BeginVigil(pawn, site, books[0], job.targetB.Thing, job.targetC.Thing as Pawn); break;
            }
        });
    }
}
