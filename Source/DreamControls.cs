using System.Collections.Generic;
using Verse;
using RimWorld;
using Verse.AI;

namespace Cultist;

public sealed class DreamControlProperties : CompProperties
{
    public DreamControlProperties() => compClass = typeof(DreamControl);
}

public sealed class DreamControl : ThingComp
{
    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        if (!respawningAfterLoad && ModsConfig.IdeologyActive && parent is Pawn pawn && pawn.Faction == Faction.OfPlayer)
            Campaign.Current?.GetAspect(pawn);
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (parent is not Pawn recipient || Campaign.Current is not { } campaign
            || !campaign.CanDiscussDoctrine(pawn, recipient))
            yield break;

        TaggedString label = "Cultist.DiscussDoctrine".Translate(recipient.LabelShortCap);
        if (!campaign.DoctrineTalkReady)
        {
            yield return new FloatMenuOption("Cultist.DoctrineTalkLater".Translate(label), null);
            yield break;
        }
        if (!pawn.CanReserveAndReach(recipient, PathEndMode.Touch, Danger.Some))
        {
            yield return new FloatMenuOption("Cultist.Unreachable".Translate(label), null);
            yield break;
        }

        yield return new FloatMenuOption(label, () => pawn.jobs.TryTakeOrderedJob(
            JobMaker.MakeJob(CultistDefs.Cultist_DiscussDoctrine, recipient), JobTag.Misc));
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (!ModsConfig.IdeologyActive || parent is not Pawn pawn || !pawn.IsColonistPlayerControlled
            || Campaign.Current is not { Stage: >= CampaignStage.Dreaming } campaign
            || !campaign.IsMember(pawn))
            yield break;

        if (campaign.Leader == pawn)
        {
            yield return new Command_Toggle
            {
                defaultLabel = "Cultist.AutoDreamToggle".Translate(),
                defaultDesc = "Cultist.AutoDreamDescription".Translate(),
                icon = CultistDefs.Cultist_NightJournal.uiIcon,
                isActive = () => campaign.AutoDream,
                toggleAction = campaign.ToggleAutoDream
            };
        }

        foreach (Gizmo gizmo in campaign.GetExaltedGizmos(pawn))
            yield return gizmo;
    }
}
