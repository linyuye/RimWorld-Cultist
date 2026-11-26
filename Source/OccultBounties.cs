using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private int bountyCooldownUntilTick = -1;
    private int activeBountySiteId = -1;

    private void ExposeOccultBounties()
    {
        Scribe_Values.Look(ref bountyCooldownUntilTick, "bountyCooldownUntilTick", -1);
        Scribe_Values.Look(ref activeBountySiteId, "activeBountySiteId", -1);
    }

    private bool HasActiveBountySite()
    {
        if (activeBountySiteId < 0)
            return false;
        bool exists = Find.WorldObjects.Sites.Any(s => s.ID == activeBountySiteId && !s.Destroyed);
        if (!exists)
            activeBountySiteId = -1;
        return exists;
    }

    internal void OpenBountyAndSupportHub(Pawn speaker)
    {
        if (speaker?.Map == null)
            return;
        Map map = speaker.Map;
        int now = Find.TickManager.TicksGame;
        int spintria = CountSpintriaOnMap(map);
        bool activeBounty = HasActiveBountySite();
        int cdDays = bountyCooldownUntilTick > now
            ? Math.Max(1, (bountyCooldownUntilTick - now + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay)
            : 0;
        bool bountyLocked = activeBounty || cdDays > 0;
        string lockReason = activeBounty
            ? "Cultist.Bounty.AlreadyActive".Translate()
            : "Cultist.Bounty.Cooldown".Translate(cdDays);

        var bountyNode = new StoryDialogNode
        {
            NodeId = "bounties",
            Title = "Cultist.Bounty.HubTitle".Translate(),
            SpeakerName = "Cultist.Bounty.HubSpeaker".Translate(),
            SpeakerSubtitle = "Cultist.Bounty.HubSub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Location/locationauctionhouse",
            ProfileTexPath = CultistDialogBg.Oriflamme,
            AccentColor = new Color(0.92f, 0.68f, 0.28f),
            BodyText = "Cultist.Bounty.HubBody".Translate(spintria),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.Bounty.OptTier1".Translate(),
                    Tag = "Cultist.Bounty.TagTier1".Translate(),
                    Disabled = bountyLocked,
                    DisabledReason = lockReason,
                    OnSelect = () => LaunchOccultBountySite(speaker, 1),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Bounty.OptTier2".Translate(),
                    Tag = "Cultist.Bounty.TagTier2".Translate(),
                    Disabled = bountyLocked,
                    DisabledReason = lockReason,
                    OnSelect = () => LaunchOccultBountySite(speaker, 2),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Bounty.OptTier3".Translate(),
                    Tag = "Cultist.Bounty.TagTier3".Translate(),
                    Disabled = bountyLocked,
                    DisabledReason = lockReason,
                    OnSelect = () => LaunchOccultBountySite(speaker, 3),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Bounty.SwitchSupport".Translate(),
                    Tag = "Cultist.Bounty.SpintriaCount".Translate(spintria),
                    NextNodeId = "support",
                    CloseAfterSelect = false
                },
                new()
                {
                    Label = "Cultist.Dialog.Later".Translate(),
                    CloseAfterSelect = true
                }
            }
        };

        var supportNode = new StoryDialogNode
        {
            NodeId = "support",
            Title = "Cultist.Support.Title".Translate(),
            SpeakerName = "Cultist.Support.Speaker".Translate(),
            SpeakerSubtitle = "Cultist.Support.Sub".Translate(),
            PortraitTexPath = "Cultist/UI/MansusMap",
            ProfileTexPath = CultistDialogBg.Mansus,
            AccentColor = new Color(0.62f, 0.56f, 0.90f),
            BodyText = "Cultist.Support.Body".Translate(spintria),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.Support.OptSpiritSquad".Translate(),
                    Tag = "Cultist.Support.TagSpiritSquad".Translate(),
                    Disabled = spintria < 5,
                    DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(5),
                    OnSelect = () => CallSpiritReinforcements(map, elite: false, cost: 5),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Support.OptEliteSpirit".Translate(),
                    Tag = "Cultist.Support.TagEliteSpirit".Translate(),
                    Disabled = spintria < 10,
                    DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(10),
                    OnSelect = () => CallSpiritReinforcements(map, elite: true, cost: 10),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Support.OptCleanseNotoriety".Translate(),
                    Tag = "Cultist.Support.TagCleanseNotoriety".Translate(),
                    Disabled = spintria < 12,
                    DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(12),
                    OnSelect = () =>
                    {
                        if (!TryConsumeSpintria(map, 12))
                            return;
                        ReduceNotoriety(45f);
                        DropVaultBonus(map, DropCellFinder.TradeDropSpot(map), "Cultist_BureauDossier", 1);
                        PlayCultistSound("Cultist_Sfx_RiteComplete");
                        Messages.Message("Cultist.Support.Cleansed".Translate(), MessageTypeDefOf.PositiveEvent);
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Support.BackToBounties".Translate(),
                    NextNodeId = "bounties",
                    CloseAfterSelect = false
                },
                new()
                {
                    Label = "Cultist.Dialog.Later".Translate(),
                    CloseAfterSelect = true
                }
            }
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(new[] { bountyNode, supportNode }, "bounties"));
    }

    private bool LaunchOccultBountySite(Pawn speaker, int tier)
    {
        if (speaker?.Map == null || HasActiveBountySite())
            return false;

        int clamped = Math.Clamp(tier, 1, 3);
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, speaker.Map.Tile, 3 + clamped * 2, 8 + clamped * 4,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        SitePartDef vaultDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Cultist_OccultVault");
        if (vaultDef == null)
            return false;

        string factionDef = clamped == 2 ? "Cultist_SuppressionBureau" : "Cultist_RivalLongFaction";
        Faction foe = EnsureFaction(factionDef) ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        float threatPoints = Math.Max(140f, StorytellerUtility.DefaultThreatPointsNow(speaker.Map) * (0.50f + clamped * 0.25f));
        Site site = SiteMaker.MakeSite(vaultDef, tile, foe, ifHostileThenMustRemainHostile: true, threatPoints: threatPoints);
        if (site == null)
            return false;

        int seed = Gen.HashCombineInt(tile.tileId, Find.TickManager.TicksGame ^ (clamped * 3571));
        vaultSites.RemoveAll(r => r.SiteId == site.ID);
        vaultSites.Add(new VaultSiteRecord
        {
            SiteId = site.ID,
            Aspect = ChurchAspect,
            Tier = clamped == 3 ? 4 : clamped,
            Seed = seed,
            BookLabel = $"Cultist.Bounty.SiteTitle{clamped}".Translate(),
            MissionKind = VaultMissionKind.Bounty,
            MissionIndex = clamped
        });

        activeBountySiteId = site.ID;
        bountyCooldownUntilTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 3;
        site.GetComponent<TimeoutComp>()?.StartTimeout((14 + clamped * 3) * GenDate.TicksPerDay);
        Find.WorldObjects.Add(site);

        CreateCampaignQuest(
            $"Cultist.Bounty.SiteTitle{clamped}".Translate(),
            $"Cultist.Bounty.SiteDesc{clamped}".Translate(),
            clamped,
            offered: false,
            target: site);

        PlayCultistSound("Cultist_Sfx_SiteRevealed");
        Messages.Message("Cultist.Bounty.Accepted".Translate(clamped), site, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    internal void OnBountyCompleted(int tier, Map map, IntVec3 pos)
    {
        activeBountySiteId = -1;
        switch (tier)
        {
            case 1:
                DropVaultBonus(map, pos, "Cultist_Spintria", 4);
                break;
            case 2:
                DropVaultBonus(map, pos, "Cultist_Spintria", 8);
                DropRandomClassicBooks(map, pos, 1);
                if (CultPathDef.ToolDefFor(ChurchAspect, 2, crackedIfLantern: true) is { } bTool)
                    DropVaultBonus(map, pos, bTool.defName, 1);
                break;
            default:
                DropVaultBonus(map, pos, "Cultist_Spintria", 14);
                if (GetLoreDef(4) is { } lore4)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(lore4), pos, map, ThingPlaceMode.Near);
                break;
        }
        Messages.Message("Cultist.Bounty.Completed".Translate(tier), new LookTargets(pos, map), MessageTypeDefOf.PositiveEvent);
    }

    private void CallSpiritReinforcements(Map map, bool elite, int cost)
    {
        if (map == null || !TryConsumeSpintria(map, cost))
            return;

        IntVec3 spot = leader?.Map == map ? leader.Position : DropCellFinder.TradeDropSpot(map);
        string[] kinds = elite
            ? new[] { "Cultist_PawnKind_KingCrucible", "Cultist_PawnKind_Percussigant", "Cultist_PawnKind_Percussigant" }
            : new[] { "Cultist_PawnKind_Hint", "Cultist_PawnKind_Percussigant" };

        int spawned = 0;
        for (int i = 0; i < kinds.Length; i++)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kinds[i]);
            if (kind == null)
                continue;
            if (!CellFinder.TryFindRandomCellNear(spot, map, 5, c => c.InBounds(map) && c.Standable(map), out IntVec3 cell))
                cell = spot;
            Pawn spirit = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
            GenSpawn.Spawn(spirit, cell, map, WipeMode.Vanish);
            RegisterTimedSummon(spirit, GenDate.TicksPerHour * 12);
            spawned++;
        }

        if (spawned > 0)
        {
            PlayCultistSound("Cultist_Sfx_RiteComplete");
            FleckMaker.ThrowLightningGlow(spot.ToVector3Shifted(), map, 2.2f);
            Messages.Message("Cultist.Support.SpiritsArrived".Translate(spawned), new LookTargets(spot, map), MessageTypeDefOf.PositiveEvent);
        }
    }

    private void RegisterTimedSummon(Pawn spirit, int durationTicks)
    {
        if (spirit == null)
            return;
        activeSummons.Add(new SummonedSpiritRecord
        {
            Spirit = spirit,
            ExpiresTick = Find.TickManager.TicksGame + durationTicks,
            Hostile = false
        });
    }
}
