using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class PatronDef : Def
{
    public Aspect preferredAspect = Aspect.Lantern;
    public AncientLanguageDef taughtLanguage;
    public int tuitionSilver = 180;
    public int commissionRequiredTier = 1;
    public int commissionRewardSilver = 200;
    public int commissionRewardSpintria = 2;
    public int commissionCooldownDays = 5;
}

public sealed partial class Campaign
{
    private Dictionary<string, int> patronCooldownUntil = new();
    private bool poppyAccepted;
    private bool poppyResolved;
    private int poppyDueTick = -1;

    public bool CanAcceptPoppy => !poppyAccepted && !poppyResolved;
    public bool PoppyDebtActive => poppyAccepted && !poppyResolved;

    private void ExposePatrons()
    {
        Scribe_Collections.Look(ref patronCooldownUntil, "patronCooldownUntil", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref poppyAccepted, "poppyAccepted");
        Scribe_Values.Look(ref poppyResolved, "poppyResolved");
        Scribe_Values.Look(ref poppyDueTick, "poppyDueTick", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            patronCooldownUntil ??= new Dictionary<string, int>();
    }

    public void AcceptPoppyAid(Pawn speaker)
    {
        if (!CanAcceptPoppy || speaker?.Map == null)
            return;
        poppyAccepted = true;
        poppyDueTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 10;
        SpawnItemNear(speaker, ThingDefOf.Silver, 600);
        if (DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria") is { } spintria)
            SpawnItemNear(speaker, spintria, 5);
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Poppy.AidAccepted".Translate(), speaker, MessageTypeDefOf.PositiveEvent);
    }

    private void TickPoppy(int now)
    {
        if (!PoppyDebtActive || poppyDueTick < 0 || now < poppyDueTick)
            return;
        poppyDueTick = -1;
        LetterDef letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("Cultist_PoppyDebtLetter") ?? LetterDefOf.ThreatBig;
        Find.LetterStack.ReceiveLetter(
            "Cultist.Poppy.DueTitle".Translate(),
            "Cultist.Poppy.DueBody".Translate(),
            letterDef);
    }

    internal void ResolvePoppyWithSpintria()
    {
        Map map = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map == null || !TryConsumeSpintria(map, 10))
        {
            Messages.Message("Cultist.Oriflamme.NeedSpintria".Translate(10), MessageTypeDefOf.RejectInput);
            return;
        }
        poppyResolved = true;
        GrantPoppyReward(map);
        Messages.Message("Cultist.Poppy.PaidSpintria".Translate(), MessageTypeDefOf.PositiveEvent);
    }

    internal void ResolvePoppyWithFollower()
    {
        Map map = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        Pawn candidate = map?.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => p != leader && !p.Dead);
        if (candidate == null)
        {
            Messages.Message("Cultist.Poppy.NoFollower".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }
        poppyResolved = true;
        string name = candidate.LabelShortCap;
        candidate.DeSpawn(DestroyMode.Vanish);
        GrantPoppyReward(map);
        Messages.Message("Cultist.Poppy.PaidFollower".Translate(name), MessageTypeDefOf.NeutralEvent);
    }

    internal void DefaultOnPoppyDebt()
    {
        poppyResolved = true;
        AddNotoriety(15f);
        LeaderDream?.AddRisk(true, Find.TickManager.TicksGame);
        Map map = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map != null)
        {
            TryFireFactionRaid(map, "Cultist_RivalLongFaction",
                "Cultist.Poppy.WrathTitle".Translate(),
                "Cultist.Poppy.WrathBody".Translate(),
                1.05f);
        }
    }

    private void GrantPoppyReward(Map map)
    {
        ThingDef lore4 = GetLoreDef(4);
        if (lore4 != null)
        {
            Thing book = ThingMaker.MakeThing(lore4);
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, new List<Thing> { book });
        }
        PlayCultistSound("Cultist_Sfx_RiteComplete");
    }

    public bool IsPatronReady(PatronDef patron) =>
        patron != null && (!patronCooldownUntil.TryGetValue(patron.defName, out int until)
            || Find.TickManager.TicksGame >= until);

    public int PatronCooldownDaysLeft(PatronDef patron)
    {
        if (patron == null || !patronCooldownUntil.TryGetValue(patron.defName, out int until))
            return 0;
        int remaining = until - Find.TickManager.TicksGame;
        return remaining <= 0 ? 0 : System.Math.Max(1, (remaining + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay);
    }

    public bool TryFulfillCommission(PatronDef patron, Pawn speaker)
    {
        if (patron == null || speaker?.Map == null || !IsPatronReady(patron))
            return false;
        if (learnedLoreTier < patron.commissionRequiredTier)
        {
            Messages.Message("Cultist.PatronNeedLoreTier".Translate(patron.commissionRequiredTier),
                speaker, MessageTypeDefOf.RejectInput);
            return false;
        }

        Thing loreBook = speaker.Map.listerThings.AllThings
            .FirstOrDefault(t => t.Spawned && !t.IsForbidden(speaker)
                && t.TryGetComp<RitualMaterial>() is { LoreTier: > 0 } mat
                && mat.LoreTier >= patron.commissionRequiredTier);
        if (loreBook == null)
        {
            Messages.Message("Cultist.PatronNeedLoreBook".Translate(patron.commissionRequiredTier),
                speaker, MessageTypeDefOf.RejectInput);
            return false;
        }

        loreBook.Destroy();
        SpawnItemNear(speaker, ThingDefOf.Silver, patron.commissionRewardSilver);
        ThingDef spintriaDef = DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria");
        if (spintriaDef != null && patron.commissionRewardSpintria > 0)
            SpawnItemNear(speaker, spintriaDef, patron.commissionRewardSpintria);

        patronCooldownUntil[patron.defName] = Find.TickManager.TicksGame + patron.commissionCooldownDays * GenDate.TicksPerDay;
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.PatronCommissionDone".Translate(patron.LabelCap,
            patron.commissionRewardSilver, patron.commissionRewardSpintria),
            speaker, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    public bool TryBuyLanguageLesson(PatronDef patron, Pawn student)
    {
        if (patron?.taughtLanguage == null || student?.Map == null)
            return false;
        if (KnowsLanguage(student, patron.taughtLanguage))
        {
            Messages.Message("Cultist.AlreadyKnowsLanguage".Translate(student.LabelShortCap,
                patron.taughtLanguage.LabelCap), student, MessageTypeDefOf.RejectInput);
            return false;
        }
        if (!TryConsumeSilver(student.Map, patron.tuitionSilver))
        {
            Messages.Message("Cultist.PatronNeedSilver".Translate(patron.tuitionSilver),
                student, MessageTypeDefOf.RejectInput);
            return false;
        }

        GrantLanguage(student, patron.taughtLanguage);
        return true;
    }

    private static bool TryConsumeSilver(Map map, int amount)
    {
        var stacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver)
            .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer))
            .ToList();
        if (stacks.Sum(t => t.stackCount) < amount)
            return false;
        int left = amount;
        foreach (Thing stack in stacks)
        {
            int take = System.Math.Min(left, stack.stackCount);
            stack.SplitOff(take).Destroy();
            left -= take;
            if (left <= 0)
                break;
        }
        return true;
    }

    private static void SpawnItemNear(Pawn pawn, ThingDef def, int count)
    {
        int remaining = count;
        while (remaining > 0)
        {
            int batch = System.Math.Min(remaining, def.stackLimit);
            Thing item = ThingMaker.MakeThing(def);
            item.stackCount = batch;
            GenPlace.TryPlaceThing(item, pawn.Position, pawn.Map, ThingPlaceMode.Near);
            remaining -= batch;
        }
    }
}

public sealed class PatronsWindow : Window
{
    private readonly Campaign campaign;
    private readonly Pawn speaker;
    private Vector2 scroll;

    public PatronsWindow(Campaign campaign, Pawn speaker)
    {
        this.campaign = campaign;
        this.speaker = speaker;
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(660, 560);

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.Label(inRect.TopPartPixels(30f), "Cultist.PatronsWindowTitle".Translate());
        var listRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 92f);
        var patrons = DefDatabase<PatronDef>.AllDefsListForReading;
        var viewRect = new Rect(0f, 0f, listRect.width - 18f, patrons.Count * 116f + 120f);

        Widgets.BeginScrollView(listRect, ref scroll, viewRect);
        float y = 0f;
        foreach (PatronDef patron in patrons)
        {
            var card = new Rect(0f, y, viewRect.width, 108f);
            Widgets.DrawBoxSolid(card, new Color(0.14f, 0.15f, 0.18f, 0.85f));
            Rect inner = card.ContractedBy(8f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f),
                patron.LabelCap + " — " + ("Cultist.Aspect." + patron.preferredAspect).Translate());
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 36f), patron.description);

            Rect btnLeft = new(inner.x, inner.y + 64f, inner.width * 0.48f, 28f);
            Rect btnRight = new(inner.x + inner.width * 0.52f, inner.y + 64f, inner.width * 0.48f, 28f);

            string commLabel = campaign.IsPatronReady(patron)
                ? "Cultist.PatronSubmitCommission".Translate(patron.commissionRequiredTier, patron.commissionRewardSilver)
                : "Cultist.PatronCooldown".Translate(campaign.PatronCooldownDaysLeft(patron));
            if (Widgets.ButtonText(btnLeft, commLabel) && campaign.IsPatronReady(patron))
                campaign.TryFulfillCommission(patron, speaker);

            if (patron.taughtLanguage != null)
            {
                string lessonLabel = "Cultist.PatronLearnLang".Translate(
                    patron.taughtLanguage.LabelCap, patron.tuitionSilver);
                if (Widgets.ButtonText(btnRight, lessonLabel))
                    campaign.TryBuyLanguageLesson(patron, speaker);
            }

            y += 116f;
        }

        // Poppy Lascelles special patronage card
        var poppyCard = new Rect(0f, y, viewRect.width, 100f);
        Widgets.DrawBoxSolid(poppyCard, new Color(0.24f, 0.12f, 0.16f, 0.85f));
        Rect pInner = poppyCard.ContractedBy(8f);
        Widgets.Label(new Rect(pInner.x, pInner.y, pInner.width, 24f), "Cultist.Poppy.Title".Translate());
        Widgets.Label(new Rect(pInner.x, pInner.y + 24f, pInner.width, 32f), "Cultist.Poppy.Desc".Translate());
        Rect pBtn = new(pInner.x, pInner.y + 58f, pInner.width * 0.6f, 28f);
        if (campaign.CanAcceptPoppy)
        {
            if (Widgets.ButtonText(pBtn, "Cultist.Poppy.AcceptBtn".Translate()))
                campaign.AcceptPoppyAid(speaker);
        }
        else if (campaign.PoppyDebtActive)
        {
            Widgets.Label(pBtn, "Cultist.Poppy.DebtPending".Translate());
        }
        else
        {
            Widgets.Label(pBtn, "Cultist.Poppy.Resolved".Translate());
        }

        Widgets.EndScrollView();
    }
}

public sealed class PoppyDebtLetter : ChoiceLetter
{
    public override IEnumerable<DiaOption> Choices
    {
        get
        {
            yield return new DiaOption("Cultist.Poppy.ChoiceSpintria".Translate())
            {
                resolveTree = true,
                action = () =>
                {
                    Campaign.Current?.ResolvePoppyWithSpintria();
                    if (Campaign.Current?.PoppyDebtActive == false)
                        Find.LetterStack.RemoveLetter(this);
                }
            };
            yield return new DiaOption("Cultist.Poppy.ChoiceFollower".Translate())
            {
                resolveTree = true,
                action = () =>
                {
                    Campaign.Current?.ResolvePoppyWithFollower();
                    if (Campaign.Current?.PoppyDebtActive == false)
                        Find.LetterStack.RemoveLetter(this);
                }
            };
            yield return new DiaOption("Cultist.Poppy.ChoiceRefuse".Translate())
            {
                resolveTree = true,
                action = () =>
                {
                    Campaign.Current?.DefaultOnPoppyDebt();
                    Find.LetterStack.RemoveLetter(this);
                }
            };
        }
    }
}
