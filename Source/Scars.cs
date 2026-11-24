using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    internal static readonly Aspect[] SevenLockScarAspects =
    {
        Aspect.Lantern, Aspect.Forge, Aspect.Edge, Aspect.Winter,
        Aspect.Heart, Aspect.Grail, Aspect.Moth
    };

    private HashSet<Aspect> consecratedLockScars = new();

    public IReadOnlyCollection<Aspect> ConsecratedLockScars => consecratedLockScars;
    public bool HasAllSevenLockScars => SevenLockScarAspects.All(a => consecratedLockScars.Contains(a));

    private void ExposeScars()
    {
        Scribe_Collections.Look(ref consecratedLockScars, "consecratedLockScars", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            consecratedLockScars ??= new HashSet<Aspect>();
    }

    public static List<Hediff_Injury> PermanentScarsOf(Pawn pawn)
    {
        if (pawn?.health?.hediffSet == null)
            return new List<Hediff_Injury>();
        return pawn.health.hediffSet.hediffs
            .OfType<Hediff_Injury>()
            .Where(h => h.TryGetComp<HediffComp_GetsPermanent>() is { IsPermanent: true })
            .ToList();
    }

    public bool CanConsecrateScar(Pawn pawn) =>
        pawn != null && IsMember(pawn) && PermanentScarsOf(pawn).Count > 0;

    public bool TryConsecrateScar(Pawn target, DreamInfluence influence, Thing altar)
    {
        if (target == null || influence == null || LeaderDream is not { } dream)
            return false;
        if (!dream.Influences.Contains(influence))
            return false;
        List<Hediff_Injury> scars = PermanentScarsOf(target);
        if (scars.Count == 0)
        {
            Messages.Message("Cultist.Scar.NoPermanentScar".Translate(target.LabelShortCap),
                target, MessageTypeDefOf.RejectInput);
            return false;
        }

        string hediffDefName = $"Cultist_Scar_{influence.Affinity}";
        HediffDef scarDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
        if (scarDef == null)
            return false;
        if (target.health.hediffSet.HasHediff(scarDef))
        {
            Messages.Message("Cultist.Scar.AlreadyHasAspect".Translate(target.LabelShortCap, scarDef.LabelCap),
                target, MessageTypeDefOf.RejectInput);
            return false;
        }

        // Consume the influence (tier does not affect scar bonus) & transform the permanent injury
        dream.Influences.Remove(influence);
        target.health.RemoveHediff(scars[0]);
        target.health.AddHediff(scarDef);

        if (SevenLockScarAspects.Contains(influence.Affinity))
            consecratedLockScars.Add(influence.Affinity);

        AddNotoriety(5f, altar, target);
        ApplySanctuaryRiteMood(target, altar);
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        SendMilestoneLetterOnce("FirstScar", LetterDefOf.NeutralEvent, target);
        Messages.Message("Cultist.Scar.Consecrated".Translate(target.LabelShortCap, scarDef.LabelCap),
            target, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    public TaggedString KnockVictoryRequirement()
    {
        if (HasAllSevenLockScars)
            return TaggedString.Empty;
        var missing = SevenLockScarAspects
            .Where(a => !consecratedLockScars.Contains(a))
            .Select(a => ("Cultist.Aspect." + a).Translate().ToString());
        return "Cultist.KnockVictory.MissingScars".Translate(consecratedLockScars.Count, string.Join("、", missing));
    }

    public void TriggerKnockSevenScarsVictory(Pawn pawn, Thing altar)
    {
        if (ChurchAspect != Aspect.Knock || completed || awaitingEnding || !HasAllSevenLockScars)
            return;
        PlayCultistSound("Cultist_Sfx_EndingGlory");
        FleckMaker.ThrowSmoke(altar.DrawPos, altar.Map, 2.5f);
        awaitingEnding = true;
        Messages.Message("Cultist.KnockVictory.Opened".Translate(pawn.LabelShortCap),
            pawn, MessageTypeDefOf.PositiveEvent);
    }
}

public sealed class ScarConsecrationWindow : Window
{
    private readonly Campaign campaign;
    private readonly Pawn target;
    private readonly Thing altar;
    private Vector2 scroll;

    public ScarConsecrationWindow(Campaign campaign, Pawn target, Thing altar)
    {
        this.campaign = campaign;
        this.target = target;
        this.altar = altar;
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(620, 480);

    public override void DoWindowContents(Rect inRect)
    {
        int scarCount = Campaign.PermanentScarsOf(target).Count;
        Widgets.Label(inRect.TopPartPixels(28f),
            "Cultist.Scar.WindowHeader".Translate(target.LabelShortCap, scarCount, campaign.ConsecratedLockScars.Count));
        Widgets.Label(new Rect(inRect.x, inRect.y + 30f, inRect.width, 28f),
            "Cultist.Scar.WindowNote".Translate());

        var influences = campaign.LeaderDreamInfluences();
        var listRect = new Rect(inRect.x, inRect.y + 64f, inRect.width, inRect.height - 120f);
        if (influences.Count == 0)
        {
            Widgets.Label(listRect, "Cultist.Scar.NoInfluences".Translate());
            return;
        }

        var viewRect = new Rect(0f, 0f, listRect.width - 18f, influences.Count * 54f + 8f);
        Widgets.BeginScrollView(listRect, ref scroll, viewRect);
        float y = 0f;
        foreach (DreamInfluence infl in influences)
        {
            var row = new Rect(0f, y, viewRect.width, 46f);
            Widgets.DrawBoxSolid(row, new Color(0.15f, 0.14f, 0.20f, 0.85f));
            Rect inner = row.ContractedBy(6f);
            string aspectName = ("Cultist.Aspect." + infl.Affinity).Translate();
            Widgets.Label(new Rect(inner.x, inner.y + 4f, inner.width * 0.62f, 26f),
                "Cultist.Scar.InfluenceRow".Translate(aspectName, infl.Strength));
            if (Widgets.ButtonText(new Rect(inner.x + inner.width * 0.64f, inner.y, inner.width * 0.36f, 32f),
                    "Cultist.Scar.ConsecrateBtn".Translate(aspectName)))
            {
                if (campaign.TryConsecrateScar(target, infl, altar))
                {
                    Close();
                    break;
                }
            }
            y += 54f;
        }
        Widgets.EndScrollView();
    }
}
