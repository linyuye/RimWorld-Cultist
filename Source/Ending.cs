using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Cultist;

public enum VigilEnding { None, Returned, Departed }

public sealed partial class Campaign
{
    private VigilEnding pendingEnding;
    private VigilEnding completedEnding;
    private ThingOwner<Pawn> departedKeepers;
    private bool hasAscendedLongLegacy;
    private Aspect ascendedLongAspect = Aspect.Lantern;
    private string ascendedLongName = "";
    private int returnedWeatherCooldownUntil;

    public VigilEnding PendingEnding => pendingEnding;
    public VigilEnding CompletedEnding => completedEnding;
    public bool HasAscendedLongLegacy => hasAscendedLongLegacy;
    public Aspect AscendedLongAspect => ascendedLongAspect;
    public string AscendedLongName => ascendedLongName;
    public bool CanRefoundSociety => completed && !awaitingEnding && !VigilInProgress && !ApostleVigilInProgress;
    public IThingHolder ParentHolder => null;

    public ThingOwner GetDirectlyHeldThings() => departedKeepers;

    public void GetChildHolders(List<IThingHolder> children) =>
        ThingOwnerUtility.AppendThingHoldersFromThings(children, departedKeepers);

    private void ExposeEnding()
    {
        Scribe_Values.Look(ref pendingEnding, "pendingVigilEnding");
        Scribe_Values.Look(ref completedEnding, "completedVigilEnding");
        Scribe_Deep.Look(ref departedKeepers, "departedKeepers", this);
        Scribe_Values.Look(ref hasAscendedLongLegacy, "hasAscendedLongLegacy");
        Scribe_Values.Look(ref ascendedLongAspect, "ascendedLongAspect", Aspect.Lantern);
        Scribe_Values.Look(ref ascendedLongName, "ascendedLongName", "");
        Scribe_Values.Look(ref returnedWeatherCooldownUntil, "returnedWeatherCooldownUntil");
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
            return;

        departedKeepers ??= new ThingOwner<Pawn>(this, true, LookMode.Deep);
        if (completed && completedEnding == VigilEnding.None)
            completedEnding = returnedLeader == null ? VigilEnding.Departed : VigilEnding.Returned;
        if (completed && completedEnding == VigilEnding.Departed)
            hasAscendedLongLegacy = true;
    }

    private void CheckEndingParticipant()
    {
        if (!awaitingEnding || (leader is { Dead: false, Spawned: true } && IsMember(leader)))
            return;
        awaitingEnding = false;
        pendingEnding = VigilEnding.None;
        Find.WindowStack.TryRemove(typeof(EndingWindow), doCloseSound: false);
        EndCampaignQuest(ref vigilQuest, QuestEndOutcome.Fail);
        Messages.Message("Cultist.VigilFailed".Translate(), MessageTypeDefOf.NegativeEvent);
    }

    internal bool ChooseEnding(VigilEnding choice)
    {
        if (!awaitingEnding || completed || pendingEnding != VigilEnding.None
            || choice is not (VigilEnding.Returned or VigilEnding.Departed)
            || leader is not { Dead: false, Spawned: true } || !IsMember(leader))
            return false;

        pendingEnding = choice;
        CultistDefs.Cultist_DreamSting.PlayOneShotOnCamera(leader.Map);
        return true;
    }

    public void FinishVigil(bool returnToColony)
    {
        var choice = returnToColony ? VigilEnding.Returned : VigilEnding.Departed;
        if (!awaitingEnding || completed || leader is not { Dead: false, Spawned: true }
            || !IsMember(leader) || (pendingEnding != VigilEnding.None && pendingEnding != choice))
            return;

        Pawn ascendant = leader;
        string ascendantShort = ascendant.LabelShortCap;
        Aspect finishedAspect = ChurchAspect;

        if (choice == VigilEnding.Returned)
        {
            returnedLeader = ascendant;
            if (!ascendant.health.hediffSet.HasHediff(CultistDefs.Cultist_ReturnedKeeper))
                ascendant.health.AddHediff(CultistDefs.Cultist_ReturnedKeeper);
        }
        else
        {
            hasAscendedLongLegacy = true;
            ascendedLongAspect = finishedAspect;
            ascendedLongName = ascendantShort;
            ascendant.DeSpawn();
            ascendant.SetFaction(null);
            if (!departedKeepers.TryAdd(ascendant))
                Find.WorldPawns.PassToWorld(ascendant, PawnDiscardDecideMode.KeepForever);
            leader = null;
            if (offers != null)
                CancelDream();
            ScheduleApostleCallIfEligible(finishedAspect);
        }

        awaitingEnding = false;
        pendingEnding = VigilEnding.None;
        completedEnding = choice;
        completed = true;
        CloseWitnessQuest();
        EndCampaignQuest(ref vigilQuest, QuestEndOutcome.Success);

        string titleKey = $"Cultist.Ending.{finishedAspect}.Title";
        string bodyKey = $"Cultist.Ending.{finishedAspect}.{(choice == VigilEnding.Returned ? "Returned" : "Departed")}";
        TaggedString title = titleKey.CanTranslate() ? titleKey.Translate() : "Cultist.EndingTitle".Translate();
        TaggedString body = bodyKey.CanTranslate()
            ? bodyKey.Translate()
            : (choice == VigilEnding.Returned ? "Cultist.EndingReturned" : "Cultist.EndingDeparted").Translate(ascendantShort);

        Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent);

        // Minor Victory Departed triggers full credits screen (with continue button); Returned stays seamless without credits.
        if (choice == VigilEnding.Departed)
        {
            PlayCultistSound("Cultist_Sfx_EndingGlory");
            Find.WindowStack.Add(new VictoryCreditsWindow(
                "Cultist.Victory.LongSubtitle".Translate(("Cultist.Aspect." + finishedAspect).Translate(), ascendantShort),
                title,
                body));
        }
    }

    internal bool CanCalmThresholdWeather(Pawn pawn) =>
        pawn != null && pawn == returnedLeader && completed && completedEnding == VigilEnding.Returned
        && Find.TickManager.TicksGame >= returnedWeatherCooldownUntil;

    internal void CalmThresholdWeather(Pawn pawn, Thing site)
    {
        if (!CanCalmThresholdWeather(pawn) || site?.Map == null)
            return;
        Thing loreBook = NearbyMaterials(pawn, site).FirstOrDefault(t => t.TryGetComp<RitualMaterial>()?.LoreTier > 0);
        if (loreBook == null)
        {
            Messages.Message("Cultist.NeedRiteBooks".Translate(1, RiteRadius), site, MessageTypeDefOf.RejectInput);
            return;
        }
        loreBook.Destroy();
        Map map = site.Map;
        var active = map.gameConditionManager.ActiveConditions;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            string dn = active[i].def.defName;
            if (dn is "HeatWave" or "ColdSnap" or "ToxicFallout" or "VolcanicWinter" or "PsychicDrone")
                active[i].End();
        }
        if (DefDatabase<WeatherDef>.GetNamedSilentFail("Clear") is { } clear)
            map.weatherManager.TransitionTo(clear);

        returnedWeatherCooldownUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay * 15;
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Returned.WeatherCalmed".Translate(pawn.LabelShortCap), site, MessageTypeDefOf.PositiveEvent);
    }

    internal void SpawnRefoundJournal(Pawn requester, Thing site)
    {
        if (!CanRefoundSociety || site?.Map == null)
            return;
        Thing journal = ThingMaker.MakeThing(CultistDefs.Cultist_NightJournal);
        if (!GenPlace.TryPlaceThing(journal, site.Position, site.Map, ThingPlaceMode.Near))
            journal.Destroy();
        else
            Messages.Message("Cultist.Refound.JournalSpawned".Translate(requester.LabelShortCap),
                journal, MessageTypeDefOf.PositiveEvent);
    }
}

public sealed class EndingWindow : Window
{
    private const float RevealSeconds = 2.4f;
    private static readonly Color Dark = new(0.065f, 0.071f, 0.081f);
    private static readonly Color Brass = new(0.95f, 0.76f, 0.36f);
    private static readonly Color Pale = new(0.97f, 0.92f, 0.76f);
    private readonly Campaign campaign;
    private float revealStarted = -1f;

    public EndingWindow(Campaign campaign)
    {
        this.campaign = campaign;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnCancel = false;
        closeOnAccept = false;
    }

    public override Vector2 InitialSize => new(620, 420);

    public override void DoWindowContents(Rect inRect)
    {
        if (!campaign.AwaitingEnding)
        {
            Close();
            return;
        }

        VigilEnding choice = campaign.PendingEnding;
        float progress = choice == VigilEnding.None ? 0f : RevealProgress();
        var image = new Rect(inRect.x, inRect.y, inRect.width, 150f);
        DrawThreshold(image, progress, choice);
        var narrative = new Rect(inRect.x + 10f, image.yMax + 15f, inRect.width - 20f, 100f);
        string narrativeKey = choice switch
        {
            VigilEnding.Returned => "Cultist.EndingReveal.Returned",
            VigilEnding.Departed => "Cultist.EndingReveal.Departed",
            _ => "Cultist.EndingPrompt"
        };
        TaggedString narrativeText = narrativeKey.Translate();
        if (choice != VigilEnding.None && campaign.HasWitnesses)
            narrativeText += "\n\n" + "Cultist.EndingWitnesses".Translate();
        Widgets.Label(narrative, narrativeText);

        var controls = new Rect(inRect.x + 10f, narrative.yMax + 8f, inRect.width - 20f, 40f);
        if (choice == VigilEnding.None)
        {
            float half = (controls.width - 10f) / 2f;
            if (Widgets.ButtonText(new Rect(controls.x, controls.y, half, controls.height),
                "Cultist.ReturnChoice".Translate()))
                campaign.ChooseEnding(VigilEnding.Returned);
            if (Widgets.ButtonText(new Rect(controls.x + half + 10f, controls.y, half, controls.height),
                "Cultist.DepartChoice".Translate()))
                campaign.ChooseEnding(VigilEnding.Departed);
            return;
        }

        if (progress < 1f)
        {
            if (Widgets.ButtonText(controls, "Cultist.EndingSkip".Translate()))
                revealStarted = Time.realtimeSinceStartup - RevealSeconds;
            return;
        }

        if (Widgets.ButtonText(controls, "Cultist.EndingContinue".Translate()))
        {
            campaign.FinishVigil(choice == VigilEnding.Returned);
            if (!campaign.AwaitingEnding)
                Close();
        }
    }

    private float RevealProgress()
    {
        if (revealStarted < 0f)
            revealStarted = Time.realtimeSinceStartup;
        return Mathf.Clamp01((Time.realtimeSinceStartup - revealStarted) / RevealSeconds);
    }

    private static void DrawThreshold(Rect area, float progress, VigilEnding choice)
    {
        Widgets.DrawBoxSolid(area, Dark);
        float opened = Mathf.SmoothStep(0f, 1f, progress);
        float aperture = Mathf.Lerp(7f, 94f, opened);
        if (choice == VigilEnding.Returned)
            aperture *= 1f - 0.65f * opened;
        float centre = area.center.x;
        float height = area.height * 0.72f;
        float top = area.y + (area.height - height) / 2f;
        Widgets.DrawBoxSolid(new Rect(centre - aperture * 1.15f, top, aperture * 2.3f, height),
            new Color(Brass.r, Brass.g, Brass.b, 0.12f + 0.16f * opened));
        Widgets.DrawBoxSolid(new Rect(centre - aperture * 0.5f, top, aperture, height),
            new Color(Pale.r, Pale.g, Pale.b, 0.45f + 0.45f * opened));
        GUI.color = Brass;
        Widgets.DrawBox(area, 2);
        GUI.color = Color.white;
    }
}

public sealed class VictoryCreditsWindow : Window
{
    private readonly string subtitle;
    private readonly string title;
    private readonly string body;
    private Vector2 scroll;

    public VictoryCreditsWindow(string subtitle, string title, string body)
    {
        this.subtitle = subtitle;
        this.title = title;
        this.body = body;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnCancel = false;
        closeOnAccept = false;
    }

    public override Vector2 InitialSize => new(680, 520);

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.DrawBoxSolid(inRect, new Color(0.055f, 0.06f, 0.075f, 0.96f));
        GUI.color = new Color(0.95f, 0.76f, 0.36f);
        Widgets.DrawBox(inRect, 2);
        GUI.color = Color.white;

        Rect inner = inRect.ContractedBy(20f);
        TextAnchor oldAnchor = Text.Anchor;
        GameFont oldFont = Text.Font;

        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Medium;
        GUI.color = new Color(0.97f, 0.85f, 0.48f);
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 36f), title);

        Text.Font = GameFont.Small;
        GUI.color = new Color(0.80f, 0.84f, 0.90f);
        Widgets.Label(new Rect(inner.x, inner.y + 36f, inner.width, 26f), subtitle);
        GUI.color = Color.white;

        Rect scrollOut = new(inner.x, inner.y + 72f, inner.width, inner.height - 130f);
        string fullText = body + "\n\n────────────────────────────\n\n" + "Cultist.Victory.Credits".Translate();
        float textH = Text.CalcHeight(fullText, scrollOut.width - 20f) + 20f;
        Rect scrollView = new(0f, 0f, scrollOut.width - 18f, Mathf.Max(scrollOut.height, textH));

        Text.Anchor = TextAnchor.UpperLeft;
        Widgets.BeginScrollView(scrollOut, ref scroll, scrollView);
        Widgets.Label(scrollView, fullText);
        Widgets.EndScrollView();

        Text.Anchor = oldAnchor;
        Text.Font = oldFont;

        if (Widgets.ButtonText(new Rect(inner.center.x - 120f, inner.yMax - 42f, 240f, 38f),
                "Cultist.Victory.ContinueColony".Translate()))
        {
            Close();
        }
    }
}
