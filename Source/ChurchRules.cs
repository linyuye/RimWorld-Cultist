using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class AspectProfile : DefModExtension
{
    public Aspect affinity;
}

public sealed partial class Campaign
{
    private int leaderUnavailableSince = -1;
    internal Ideo Church => church;
    private bool LeaderOutOfOffice => leader == null || leader.Dead
        || leader.Faction != Faction.OfPlayer || !IsMember(leader);
    private bool LeaderUnavailable => leader != null && (!leader.Spawned || leader.Downed);

    private void ExposeChurchRules() =>
        Scribe_Values.Look(ref leaderUnavailableSince, "leaderUnavailableSince", -1);

    private void TrackLeadership(int now)
    {
        if (LeaderOutOfOffice || !LeaderUnavailable)
            leaderUnavailableSince = -1;
        else if (leaderUnavailableSince < 0)
            leaderUnavailableSince = now;
    }

    private void EnsurePathDoctrine()
    {
        if (church == null)
            return;

        CultPathDef path = ActivePath;
        if (path.memeDef != null && !church.HasMeme(path.memeDef))
        {
            church.memes.Add(path.memeDef);
            church.SortMemesInDisplayOrder();
            church.RecachePrecepts();
        }
        if (path.iconDef != null && path.colorDef != null
            && (church.iconDef != path.iconDef || church.colorDef != path.colorDef))
        {
            church.SetIcon(path.iconDef, path.colorDef);
        }
        foreach (PreceptDef required in path.lockedPrecepts)
        {
            if (required != null)
                EnsurePrecept(required);
        }
    }

    internal void SyncLeaderIdeoRole()
    {
        if (church == null || leader == null || leader.Dead || !IsMember(leader))
            return;
        Precept_RoleSingle leaderRole = church.PreceptsListForReading
            .OfType<Precept_RoleSingle>()
            .FirstOrDefault(role => role.def == PreceptDefOf.IdeoRole_Leader);
        if (leaderRole != null && leaderRole.ChosenPawnSingle() != leader && leaderRole.RequirementsMet(leader))
            leaderRole.Assign(leader, addThoughts: false);
    }

    internal bool IsChurchPrimary => church != null && Faction.OfPlayer?.ideos?.PrimaryIdeo == church;

    internal void SetChurchAsPrimary()
    {
        if (church == null || Faction.OfPlayer?.ideos == null || IsChurchPrimary)
            return;
        Faction.OfPlayer.ideos.SetPrimary(church);
        Messages.Message("Cultist.ChurchPrimarySet".Translate(church.name),
            leader, MessageTypeDefOf.PositiveEvent);
    }

    private void EnsurePrecept(PreceptDef required)
    {
        var precepts = church.PreceptsListForReading;
        for (int i = precepts.Count - 1; i >= 0; i--)
        {
            Precept precept = precepts[i];
            if (precept.def.issue == required.issue && (precept.def != required || precept.Id < 0))
                church.RemovePrecept(precept, false);
        }
        if (!church.HasPrecept(required))
            church.AddPrecept(PreceptMaker.MakePrecept(required), true, Faction.OfPlayer.def, null);
    }

    internal TaggedString SocietyRankLabel(Pawn pawn)
    {
        if (!IsMember(pawn))
            return "Cultist.OutsideChurch".Translate();
        int rank = Rank(pawn);
        if (rank == 3)
        {
            string aspectKey = $"Cultist.Rank.{ChurchAspect}.3";
            if (aspectKey.CanTranslate())
                return aspectKey.Translate();
        }
        return ("Cultist.Rank." + rank).Translate();
    }
}

public sealed class ITab_Cultist : ITab
{
    public ITab_Cultist()
    {
        labelKey = "Cultist.CharacterTab";
        size = new Vector2(400, 370);
    }

    public override bool IsVisible => ModsConfig.IdeologyActive && SelPawn is { IsColonist: true }
        && Campaign.Current != null;

    protected override void FillTab()
    {
        if (SelPawn is not { } pawn || Campaign.Current is not { } campaign)
            return;

        var listing = new Listing_Standard();
        listing.Begin(new Rect(Vector2.zero, size).ContractedBy(12f));
        listing.Label("Cultist.PersonalAspect".Translate(("Cultist.Aspect." + campaign.GetAspect(pawn)).Translate()));
        string langs = campaign.KnownLanguagesSummary(pawn);
        if (!string.IsNullOrEmpty(langs))
            listing.Label("Cultist.KnownLanguages".Translate(langs));
        if (campaign.Church == null)
        {
            listing.End();
            return;
        }

        listing.Label("Cultist.SocietyRank".Translate(campaign.SocietyRankLabel(pawn)));
        if (campaign.Rank(pawn) >= 3 && campaign.GetExaltedProfile(pawn) is { } exalted)
        {
            Aspect pa = campaign.GetAspect(pawn);
            listing.Label("Cultist.Exalted.TabSkill".Translate($"Cultist.ExaltedSkill.{pa}".Translate()));
            if (exalted.Traits.Count > 0)
            {
                string tList = string.Join(" / ", exalted.Traits.Select(t => $"Cultist.ExaltedTrait.{t}".Translate().ToString()));
                listing.Label("Cultist.Exalted.TabTraits".Translate(tList));
            }
        }
        listing.Label("Cultist.NotorietyLabel".Translate(campaign.Notoriety.ToString("0"), campaign.NotorietyTierLabel()));
        listing.Gap(6f);
        if (campaign.Leader == pawn && campaign.IsMember(pawn))
        {
            listing.Label("Cultist.LeaderTitle".Translate());
            if (listing.ButtonText("Cultist.EditChurchRules".Translate()))
                Find.WindowStack.Add(new ChurchRulesWindow(campaign.Church));
            if (!campaign.IsChurchPrimary && listing.ButtonText("Cultist.SetPrimaryIdeo".Translate()))
                campaign.SetChurchAsPrimary();
            if (listing.ButtonText("Cultist.OpenPatrons".Translate()))
                Find.WindowStack.Add(new PatronsWindow(campaign, pawn));
        }
        else if (campaign.CanAppointSuccessor(pawn)
            && listing.ButtonText("Cultist.AppointSuccessor".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Cultist.ConfirmSuccessor".Translate(pawn.LabelShortCap),
                () => campaign.AppointSuccessor(pawn)));
        }
        listing.End();
    }
}

public sealed class ChurchRulesWindow : Window
{
    private readonly Ideo church;
    private Vector2 scroll;
    private float contentHeight;

    public ChurchRulesWindow(Ideo church)
    {
        this.church = church;
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(760, 700);

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.Label(inRect.TopPartPixels(30f), "Cultist.FixedChurchRules".Translate());
        var area = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 92f);
        float width = area.width - 18f;
        Widgets.BeginScrollView(area, ref scroll, new Rect(0, 0, width, contentHeight));
        float y = 0f;
        IdeoUIUtility.DoPrecepts(ref y, width, church, IdeoEditMode.GameStart);
        contentHeight = y + 12f;
        Widgets.EndScrollView();
    }
}
