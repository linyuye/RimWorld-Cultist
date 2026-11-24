using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed partial class Campaign
{
    private int nextDoctrineTalk = -1;
    internal bool DoctrineTalkReady => Find.TickManager.TicksGame >= nextDoctrineTalk;

    internal bool CanDiscussDoctrine(Pawn speaker, Pawn recipient) => speaker?.interactions != null
        && CanLeadRite(speaker) && !speaker.Drafted && !speaker.WorkTagIsDisabled(WorkTags.Social)
        && recipient is { Spawned: true, Dead: false, Downed: false, IsColonistPlayerControlled: true }
        && recipient.ideo != null && recipient.Map == speaker.Map && !IsMember(recipient);

    internal void DiscussDoctrine(Pawn speaker, Pawn recipient)
    {
        if (!CanDiscussDoctrine(speaker, recipient) || !DoctrineTalkReady)
            return;

        var interaction = DefDatabase<InteractionDef>.GetNamed("ConvertIdeoAttempt");
        if (speaker.interactions.TryInteractWith(recipient, interaction))
            nextDoctrineTalk = Find.TickManager.TicksGame + 4 * GenDate.TicksPerHour;
        else
            Messages.Message("Cultist.DoctrineTalkInterrupted".Translate(recipient.LabelShortCap),
                recipient, MessageTypeDefOf.RejectInput);
    }
}

public sealed class DoctrineTalkJob : JobDriver
{
    private Pawn Recipient => job.targetA.Thing as Pawn;

    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => Campaign.Current?.CanDiscussDoctrine(pawn, Recipient) != true);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Do(() => Campaign.Current?.DiscussDoctrine(pawn, Recipient));
    }
}
