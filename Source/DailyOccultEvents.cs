using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private int nextOccultVisitorTick = -1;
    private int lastVisitorEventIndex = -1;

    private void ExposeDailyOccultEvents()
    {
        Scribe_Values.Look(ref nextOccultVisitorTick, "nextOccultVisitorTick", -1);
        Scribe_Values.Look(ref lastVisitorEventIndex, "lastVisitorEventIndex", -1);
    }

    private void TickDailyOccultEvents(int now)
    {
        if (stage < CampaignStage.Dreaming || leader == null || leader.Dead)
            return;

        if (nextOccultVisitorTick < 0)
        {
            nextOccultVisitorTick = now + Rand.RangeInclusive(GenDate.TicksPerDay * 8, GenDate.TicksPerDay * 12);
            return;
        }

        if (now < nextOccultVisitorTick)
            return;

        Map map = leader.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map == null || Find.WindowStack.IsOpen(typeof(CultistStoryDialogWindow)))
            return;

        nextOccultVisitorTick = now + Rand.RangeInclusive(GenDate.TicksPerDay * 12, GenDate.TicksPerDay * 18);

        var candidates = new List<int> { 0, 1, 3 };
        if (notoriety >= 40f && CountSocietyMembers() >= 3)
            candidates.Add(2);
        if (candidates.Count > 1)
            candidates.Remove(lastVisitorEventIndex);

        int pick = candidates.RandomElement();
        lastVisitorEventIndex = pick;

        switch (pick)
        {
            case 0: OpenFugitiveHereticDialog(map); break;
            case 1: OpenPoppyMidnightDialog(map); break;
            case 2: OpenBureauMoleDialog(map); break;
            default: OpenOriflammeAuctionDialog(map); break;
        }
    }

    private int CountSocietyMembers()
    {
        int count = 0;
        foreach (Map m in Find.Maps)
        foreach (Pawn p in m.mapPawns.FreeColonistsSpawned)
            if (IsMember(p))
                count++;
        return count;
    }

    internal void GrantInfluence(Aspect aspect, int strength)
    {
        if (LeaderDream is not { } dream || strength <= 0)
            return;
        InfluenceDecayDef decay = InfluenceDecayDef.ForAspect(aspect);
        int duration = decay?.DurationTicks ?? PersonalDream.InfluenceDuration;
        dream.Influences.Add(new DreamInfluence
        {
            Affinity = aspect,
            Strength = strength,
            Expires = Find.TickManager.TicksGame + duration
        });
    }

    private static Thing FindLoreBookOnMap(Map map, int minTier)
    {
        if (map == null)
            return null;
        return map.listerThings.AllThings.FirstOrDefault(t =>
            t.Spawned && !t.IsForbidden(Faction.OfPlayer)
            && t.TryGetComp<RitualMaterial>() is { LoreTier: > 0 } mat
            && mat.LoreTier >= minTier);
    }

    private void OpenFugitiveHereticDialog(Map map)
    {
        bool canSacrifice = ChurchAspect is Aspect.Grail or Aspect.Winter or Aspect.Edge;
        var options = new List<StoryDialogOption>
        {
            new()
            {
                Label = "Cultist.Visitor.Heretic.OptShelter".Translate(),
                Tag = "Cultist.Visitor.Heretic.TagShelter".Translate(),
                OnSelect = () =>
                {
                    IntVec3 spot = DropCellFinder.TradeDropSpot(map);
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
                    if (church != null && pawn.ideo != null)
                        pawn.ideo.SetIdeo(church);
                    Aspect asp = (Aspect)Rand.Range(0, 8);
                    aspects[pawn.GetUniqueLoadID()] = asp;
                    GenSpawn.Spawn(pawn, spot, map, WipeMode.Vanish);
                    if (CultPathDef.LoreDefFor(asp, 2) is { } lore2)
                        GenPlace.TryPlaceThing(ThingMaker.MakeThing(lore2), spot, map, ThingPlaceMode.Near);
                    AddNotoriety(15f);
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Heretic.Sheltered".Translate(pawn.LabelShortCap),
                        pawn, MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Heretic.OptTurnIn".Translate(),
                Tag = "Cultist.Visitor.Heretic.TagTurnIn".Translate(),
                OnSelect = () =>
                {
                    ReduceNotoriety(20f);
                    DropVaultBonus(map, DropCellFinder.TradeDropSpot(map), ThingDefOf.Silver.defName, 400);
                    Messages.Message("Cultist.Visitor.Heretic.TurnedIn".Translate(), MessageTypeDefOf.NeutralEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Heretic.OptSacrifice".Translate(),
                Tag = "Cultist.Visitor.Heretic.TagSacrifice".Translate(),
                Disabled = !canSacrifice,
                DisabledReason = "Cultist.Visitor.Heretic.SacrificeReason".Translate(),
                OnSelect = () =>
                {
                    GrantInfluence(ChurchAspect, 10);
                    if (CultPathDef.ToolDefFor(ChurchAspect, 2, crackedIfLantern: true) is { } sacTool)
                        DropVaultBonus(map, DropCellFinder.TradeDropSpot(map), sacTool.defName, 1);
                    AddNotoriety(10f);
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Heretic.Sacrificed".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "visitor_heretic",
            Title = "Cultist.Visitor.Heretic.Title".Translate(),
            SpeakerName = "Cultist.Visitor.Heretic.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.Visitor.Heretic.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Follower/devotee_moth",
            ProfileTexPath = CultistDialogBg.Mansus,
            AccentColor = new Color(0.62f, 0.56f, 0.90f),
            BodyText = "Cultist.Visitor.Heretic.Body".Translate(),
            Options = options
        }));
    }

    private void OpenPoppyMidnightDialog(Map map)
    {
        Thing loreBook = FindLoreBookOnMap(map, 2);
        var options = new List<StoryDialogOption>
        {
            new()
            {
                Label = "Cultist.Visitor.Poppy.OptTrade".Translate(),
                Tag = "Cultist.Visitor.Poppy.TagTrade".Translate(),
                Disabled = loreBook == null,
                DisabledReason = "Cultist.Visitor.Poppy.NeedLore2".Translate(),
                OnSelect = () =>
                {
                    Thing book = FindLoreBookOnMap(map, 2);
                    if (book == null)
                        return;
                    book.Destroy();
                    IntVec3 spot = DropCellFinder.TradeDropSpot(map);
                    DropVaultBonus(map, spot, "Cultist_Spintria", 6);
                    DropVaultBonus(map, spot, ThingDefOf.Silver.defName, 350);
                    ReduceNotoriety(15f);
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Poppy.Traded".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Poppy.OptPatronage".Translate(),
                Tag = "Cultist.Visitor.Poppy.TagPatronage".Translate(),
                Disabled = !CanAcceptPoppy,
                DisabledReason = "Cultist.Visitor.Poppy.PatronageDone".Translate(),
                OnSelect = () =>
                {
                    if (leader != null)
                        AcceptPoppyAid(leader);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Poppy.OptDecline".Translate(),
                CloseAfterSelect = true
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "visitor_poppy",
            Title = "Cultist.Visitor.Poppy.Title".Translate(),
            SpeakerName = "Cultist.Visitor.Poppy.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.Visitor.Poppy.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Patron/poppy",
            ProfileTexPath = CultistDialogBg.Poppy,
            AccentColor = new Color(0.84f, 0.28f, 0.36f),
            BodyText = "Cultist.Visitor.Poppy.Body".Translate(),
            Options = options
        }));
    }

    private void OpenBureauMoleDialog(Map map)
    {
        bool canLantern = ChurchAspect == Aspect.Lantern || Influence >= 6;
        int spintria = CountSpintriaOnMap(map);
        var options = new List<StoryDialogOption>
        {
            new()
            {
                Label = "Cultist.Visitor.Mole.OptLantern".Translate(),
                Tag = "Cultist.Visitor.Mole.TagLantern".Translate(),
                Disabled = !canLantern,
                DisabledReason = "Cultist.Visitor.Mole.NeedLantern".Translate(),
                OnSelect = () =>
                {
                    ReduceNotoriety(18f);
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Mole.Cleared".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Mole.OptMoth".Translate(),
                Tag = "Cultist.Visitor.Mole.TagMoth".Translate(),
                Disabled = spintria < 2,
                DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(2),
                OnSelect = () =>
                {
                    if (!TryConsumeSpintria(map, 2))
                        return;
                    ReduceNotoriety(25f);
                    nextBureauRaidTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 20;
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Mole.Misled".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Mole.OptPurge".Translate(),
                Tag = "Cultist.Visitor.Mole.TagPurge".Translate(),
                OnSelect = () =>
                {
                    ReduceNotoriety(12f);
                    DropVaultBonus(map, DropCellFinder.TradeDropSpot(map), "Cultist_BureauDossier", 1);
                    LeaderDream?.AddRisk(true, Find.TickManager.TicksGame);
                    Messages.Message("Cultist.Visitor.Mole.Purged".Translate(), MessageTypeDefOf.NeutralEvent);
                },
                CloseAfterSelect = true
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "visitor_mole",
            Title = "Cultist.Visitor.Mole.Title".Translate(),
            SpeakerName = "Cultist.Visitor.Mole.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.Visitor.Mole.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Follower/douglas_r",
            ProfileTexPath = CultistDialogBg.Douglas,
            AccentColor = new Color(0.88f, 0.72f, 0.34f),
            BodyText = "Cultist.Visitor.Mole.Body".Translate(),
            Options = options
        }));
    }

    private void OpenOriflammeAuctionDialog(Map map)
    {
        int spintria = CountSpintriaOnMap(map);
        var options = new List<StoryDialogOption>
        {
            new()
            {
                Label = "Cultist.Visitor.Auction.OptLore4".Translate(),
                Tag = "Cultist.Visitor.Auction.TagLore4".Translate(),
                Disabled = spintria < 6,
                DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(6),
                OnSelect = () =>
                {
                    if (!TryConsumeSpintria(map, 6))
                        return;
                    ThingDef lore4 = GetLoreDef(4);
                    if (lore4 != null)
                        DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, new List<Thing> { ThingMaker.MakeThing(lore4) });
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Auction.Bought".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Auction.OptMirror".Translate(CultPathDef.ToolDefFor(ChurchAspect, 3)?.LabelCap ?? "Tier 3 Tool"),
                Tag = "Cultist.Visitor.Auction.TagMirror".Translate(),
                Disabled = spintria < 8,
                DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(8),
                OnSelect = () =>
                {
                    if (!TryConsumeSpintria(map, 8))
                        return;
                    if (CultPathDef.ToolDefFor(ChurchAspect, 3) is { } def)
                        DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, new List<Thing> { ThingMaker.MakeThing(def) });
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Auction.Bought".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Visitor.Auction.OptBooks".Translate(),
                Tag = "Cultist.Visitor.Auction.TagBooks".Translate(),
                Disabled = spintria < 4,
                DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(4),
                OnSelect = () =>
                {
                    if (!TryConsumeSpintria(map, 4))
                        return;
                    DropRandomClassicBooks(map, DropCellFinder.TradeDropSpot(map), 2);
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Visitor.Auction.Bought".Translate(), MessageTypeDefOf.PositiveEvent);
                },
                CloseAfterSelect = true
            },
            new()
            {
                Label = "Cultist.Dialog.Later".Translate(),
                CloseAfterSelect = true
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "visitor_auction",
            Title = "Cultist.Visitor.Auction.Title".Translate(),
            SpeakerName = "Cultist.Visitor.Auction.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.Visitor.Auction.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Location/locationauctionhouse",
            ProfileTexPath = CultistDialogBg.Oriflamme,
            AccentColor = new Color(0.92f, 0.68f, 0.28f),
            BodyText = "Cultist.Visitor.Auction.Body".Translate(spintria),
            Options = options
        }));
    }
}
