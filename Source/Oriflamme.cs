using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class OriflammeCommsProperties : CompProperties
{
    public OriflammeCommsProperties() => compClass = typeof(OriflammeCommsComp);
}

public sealed class OriflammeCommsComp : ThingComp
{
    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || campaign.Stage < CampaignStage.Dreaming
            || !pawn.IsColonistPlayerControlled)
            yield break;

        CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
        if (power != null && !power.PowerOn)
        {
            yield return new FloatMenuOption("Cultist.Oriflamme.NoPower".Translate(), null);
            yield break;
        }

        yield return new FloatMenuOption(
            "Cultist.Oriflamme.CommsOption".Translate(),
            () => Find.WindowStack.Add(new OriflammeWindow(campaign, pawn)));
    }
}

public sealed class OriflammeOffer : IExposable
{
    public string DefName;
    public int CostSpintria;
    public bool Sold;
    public bool IsNamedFollowerLead;

    public void ExposeData()
    {
        Scribe_Values.Look(ref DefName, "defName");
        Scribe_Values.Look(ref CostSpintria, "costSpintria");
        Scribe_Values.Look(ref Sold, "sold");
        Scribe_Values.Look(ref IsNamedFollowerLead, "isLead");
    }
}

public sealed partial class Campaign
{
    private List<OriflammeOffer> oriflammeOffers = new();
    private int oriflammeRefreshTick = -1;
    private HashSet<string> recruitedNamedFollowers = new();

    private void ExposeOriflamme()
    {
        Scribe_Collections.Look(ref oriflammeOffers, "oriflammeOffers", LookMode.Deep);
        Scribe_Values.Look(ref oriflammeRefreshTick, "oriflammeRefreshTick", -1);
        Scribe_Collections.Look(ref recruitedNamedFollowers, "recruitedNamedFollowers", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            oriflammeOffers ??= new List<OriflammeOffer>();
            recruitedNamedFollowers ??= new HashSet<string>();
        }
    }

    internal IReadOnlyList<OriflammeOffer> GetOriflammeOffers()
    {
        int now = Find.TickManager.TicksGame;
        if (oriflammeOffers.Count == 0 || (oriflammeRefreshTick > 0 && now >= oriflammeRefreshTick))
            RefreshOriflammeStock(now);
        return oriflammeOffers;
    }

    internal int OriflammeRefreshDaysLeft()
    {
        if (oriflammeRefreshTick < 0) return 0;
        int rem = oriflammeRefreshTick - Find.TickManager.TicksGame;
        return rem <= 0 ? 0 : System.Math.Max(1, (rem + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay);
    }

    private void RefreshOriflammeStock(int now)
    {
        oriflammeOffers.Clear();
        oriflammeRefreshTick = now + GenDate.TicksPerDay * 10;

        // 3 random Cultist books
        var books = DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.tradeTags != null && d.tradeTags.Contains("CultistBook"))
            .InRandomOrder()
            .Take(3);
        foreach (ThingDef book in books)
        {
            oriflammeOffers.Add(new OriflammeOffer
            {
                DefName = book.defName,
                CostSpintria = book.BaseMarketValue >= 220f ? 3 : 2
            });
        }

        // 1 random ritual tool
        string[] tools = { "Cultist_RiteTool1Ready", "Cultist_RiteTool2Cracked", "Cultist_RiteTool3Cracked" };
        string pickedTool = tools.RandomElement();
        oriflammeOffers.Add(new OriflammeOffer
        {
            DefName = pickedTool,
            CostSpintria = pickedTool == "Cultist_RiteTool3Cracked" ? 4 : 3
        });

        // 1 untranslated codex
        oriflammeOffers.Add(new OriflammeOffer
        {
            DefName = "Cultist_AncientCodex",
            CostSpintria = 2
        });

        // 1 Named Follower recruitment lead (if any remain unrecruited)
        CultistNamedFollowerDef lead = PickUnrecruitedNamedFollower();
        if (lead != null)
        {
            oriflammeOffers.Add(new OriflammeOffer
            {
                DefName = lead.defName,
                CostSpintria = 6,
                IsNamedFollowerLead = true
            });
        }
    }

    private CultistNamedFollowerDef PickUnrecruitedNamedFollower()
    {
        var all = DefDatabase<CultistNamedFollowerDef>.AllDefsListForReading
            .Where(f => !recruitedNamedFollowers.Contains(f.defName))
            .ToList();
        var matching = all.Where(f => f.affinity == ChurchAspect).ToList();
        if (matching.Count > 0)
            return matching.RandomElement();
        return all.Count > 0 ? all.RandomElement() : null;
    }

    internal int CountSpintriaOnMap(Map map)
    {
        if (map == null) return 0;
        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria");
        if (def == null) return 0;
        return map.listerThings.ThingsOfDef(def)
            .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer))
            .Sum(t => t.stackCount);
    }

    internal static bool TryConsumeSpintria(Map map, int amount)
    {
        if (map == null || amount <= 0) return false;
        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria");
        if (def == null) return false;
        var stacks = map.listerThings.ThingsOfDef(def)
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
            if (left <= 0) break;
        }
        return true;
    }

    internal bool TryBuyOriflammeOffer(OriflammeOffer offer, Pawn buyer)
    {
        if (offer == null || offer.Sold || buyer?.Map == null)
            return false;
        if (!TryConsumeSpintria(buyer.Map, offer.CostSpintria))
        {
            Messages.Message("Cultist.Oriflamme.NeedSpintria".Translate(offer.CostSpintria),
                buyer, MessageTypeDefOf.RejectInput);
            return false;
        }

        offer.Sold = true;
        if (offer.IsNamedFollowerLead)
        {
            RecruitNamedFollower(offer.DefName, buyer.Map);
            return true;
        }

        ThingDef itemDef = DefDatabase<ThingDef>.GetNamedSilentFail(offer.DefName);
        if (itemDef != null)
        {
            Thing item = ThingMaker.MakeThing(itemDef);
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(buyer.Map), buyer.Map, new List<Thing> { item });
            PlayCultistSound("Cultist_Sfx_RiteComplete");
            Messages.Message("Cultist.Oriflamme.Bought".Translate(itemDef.LabelCap, offer.CostSpintria),
                buyer, MessageTypeDefOf.PositiveEvent);
        }
        return true;
    }

    internal void RecruitNamedFollower(string followerDefName, Map map)
    {
        CultistNamedFollowerDef fDef = DefDatabase<CultistNamedFollowerDef>.GetNamedSilentFail(followerDefName);
        if (fDef == null || map == null)
            return;

        recruitedNamedFollowers.Add(fDef.defName);
        Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
        pawn.Name = new NameSingle(fDef.label);
        if (church != null)
            pawn.ideo?.SetIdeo(church);
        aspects[pawn.GetUniqueLoadID()] = fDef.affinity;
        ranks[pawn.GetUniqueLoadID()] = 2; // Joins as Disciple (Rank 2)

        IntVec3 dropCell = DropCellFinder.TradeDropSpot(map);
        GenSpawn.Spawn(pawn, dropCell, map, WipeMode.Vanish);
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Find.LetterStack.ReceiveLetter(
            "Cultist.NamedFollower.JoinedTitle".Translate(fDef.label),
            "Cultist.NamedFollower.JoinedBody".Translate(fDef.label, ("Cultist.Aspect." + fDef.affinity).Translate(), fDef.description),
            LetterDefOf.PositiveEvent,
            pawn);
    }
}

public sealed class OriflammeWindow : Window
{
    private readonly Campaign campaign;
    private readonly Pawn buyer;
    private Vector2 scroll;

    public OriflammeWindow(Campaign campaign, Pawn buyer)
    {
        this.campaign = campaign;
        this.buyer = buyer;
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(660, 540);

    public override void DoWindowContents(Rect inRect)
    {
        var offers = campaign.GetOriflammeOffers();
        int owned = campaign.CountSpintriaOnMap(buyer?.Map);
        Widgets.Label(inRect.TopPartPixels(32f),
            "Cultist.Oriflamme.WindowHeader".Translate(owned, campaign.OriflammeRefreshDaysLeft()));

        var listRect = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 96f);
        var viewRect = new Rect(0f, 0f, listRect.width - 18f, offers.Count * 74f + 12f);

        Widgets.BeginScrollView(listRect, ref scroll, viewRect);
        float y = 0f;
        foreach (OriflammeOffer offer in offers)
        {
            var card = new Rect(0f, y, viewRect.width, 66f);
            Widgets.DrawBoxSolid(card, offer.Sold ? new Color(0.10f, 0.10f, 0.11f, 0.7f) : new Color(0.16f, 0.15f, 0.20f, 0.85f));
            Rect inner = card.ContractedBy(8f);

            string title;
            string desc;
            if (offer.IsNamedFollowerLead)
            {
                CultistNamedFollowerDef fDef = DefDatabase<CultistNamedFollowerDef>.GetNamedSilentFail(offer.DefName);
                title = "Cultist.Oriflamme.FollowerLeadTitle".Translate(fDef?.label ?? offer.DefName);
                desc = fDef != null
                    ? "Cultist.Oriflamme.FollowerLeadDesc".Translate(("Cultist.Aspect." + fDef.affinity).Translate())
                    : "";
            }
            else
            {
                ThingDef tDef = DefDatabase<ThingDef>.GetNamedSilentFail(offer.DefName);
                title = tDef?.LabelCap.ToString() ?? offer.DefName;
                desc = tDef?.description ?? "";
            }

            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.68f, 24f), title);
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width * 0.68f, 26f), desc.Truncate(inner.width * 0.68f));

            Rect btnRect = new(inner.x + inner.width * 0.70f, inner.y + 8f, inner.width * 0.30f, 34f);
            if (offer.Sold)
                Widgets.Label(btnRect, "Cultist.Oriflamme.Sold".Translate());
            else if (Widgets.ButtonText(btnRect, "Cultist.Oriflamme.BuyBtn".Translate(offer.CostSpintria)))
                campaign.TryBuyOriflammeOffer(offer, buyer);

            y += 74f;
        }
        Widgets.EndScrollView();
    }
}
