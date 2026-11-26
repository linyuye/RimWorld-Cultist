using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Cultist;

public sealed partial class Campaign
{
    private int rivalLongStage;
    private int nextRivalStageTick = -1;
    private int activeRivalFortressSiteId = -1;
    private string rivalLongName = string.Empty;

    private static readonly string[] RivalLongNames =
    {
        "Julian Coseley",
        "Captain Welland",
        "Lady Tryphon",
        "J.C. Diarist"
    };

    public int RivalLongStage => rivalLongStage;
    public bool RivalLongDefeated => rivalLongStage >= 5;

    private string EnsureRivalLongName()
    {
        if (string.IsNullOrEmpty(rivalLongName))
            rivalLongName = RivalLongNames.RandomElement();
        return rivalLongName;
    }

    private void ExposeRivalLongArc()
    {
        Scribe_Values.Look(ref rivalLongStage, "rivalLongStage");
        Scribe_Values.Look(ref nextRivalStageTick, "nextRivalStageTick", -1);
        Scribe_Values.Look(ref activeRivalFortressSiteId, "activeRivalFortressSiteId", -1);
        Scribe_Values.Look(ref rivalLongName, "rivalLongName", string.Empty);
    }

    internal bool HasActiveRivalFortressSite()
    {
        if (activeRivalFortressSiteId < 0)
            return false;
        bool exists = Find.WorldObjects.Sites.Any(s => s.ID == activeRivalFortressSiteId && !s.Destroyed);
        if (!exists)
            activeRivalFortressSiteId = -1;
        return exists;
    }

    internal bool CanReopenRivalFortress() =>
        rivalLongStage == 4 && !HasActiveRivalFortressSite();

    private void TickRivalLongArc(int now)
    {
        if (stage < CampaignStage.Dreaming || marks < 3 || rivalLongStage >= 4 || leader == null || leader.Dead)
            return;

        if (nextRivalStageTick < 0)
        {
            nextRivalStageTick = now + GenDate.TicksPerDay * 6;
            return;
        }

        if (now < nextRivalStageTick)
            return;

        Map map = leader.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map == null || Find.WindowStack.IsOpen(typeof(CultistStoryDialogWindow)))
            return;

        rivalLongStage++;
        nextRivalStageTick = now + GenDate.TicksPerDay * 12;

        switch (rivalLongStage)
        {
            case 1: OpenRivalStage1Dialog(map); break;
            case 2: OpenRivalStage2Dialog(map); break;
            case 3: OpenRivalStage3Dialog(map); break;
            default: OpenRivalStage4Dialog(map); break;
        }
    }

    private void OpenRivalStage1Dialog(Map map)
    {
        string name = EnsureRivalLongName();
        int spintria = CountSpintriaOnMap(map);
        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "rival_stage_1",
            Title = "Cultist.RivalArc.Title1".Translate(),
            SpeakerName = name,
            SpeakerSubtitle = "Cultist.RivalArc.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Summon/apostlelantern.pillar4",
            ProfileTexPath = CultistDialogBg.RivalLong,
            AccentColor = new Color(0.94f, 0.82f, 0.38f),
            BodyText = "Cultist.RivalArc.Body1".Translate(name),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.RivalArc.Opt1Counter".Translate(),
                    Tag = "Cultist.RivalArc.Tag1Counter".Translate(),
                    Disabled = spintria < 4,
                    DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(4),
                    OnSelect = () =>
                    {
                        if (TryConsumeSpintria(map, 4))
                            Messages.Message("Cultist.RivalArc.Countered".Translate(name), MessageTypeDefOf.PositiveEvent);
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.RivalArc.Opt1Endure".Translate(),
                    Tag = "Cultist.RivalArc.Tag1Endure".Translate(),
                    OnSelect = () => AddNotoriety(18f),
                    CloseAfterSelect = true
                }
            }
        }));
    }

    private void OpenRivalStage2Dialog(Map map)
    {
        string name = EnsureRivalLongName();
        int spintria = CountSpintriaOnMap(map);
        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "rival_stage_2",
            Title = "Cultist.RivalArc.Title2".Translate(),
            SpeakerName = name,
            SpeakerSubtitle = "Cultist.RivalArc.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Summon/apostlelantern.pillar4",
            ProfileTexPath = CultistDialogBg.RivalLong,
            AccentColor = new Color(0.94f, 0.82f, 0.38f),
            BodyText = "Cultist.RivalArc.Body2".Translate(name),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.RivalArc.Opt2Bribe".Translate(),
                    Tag = "Cultist.RivalArc.Tag2Bribe".Translate(),
                    Disabled = spintria < 6,
                    DisabledReason = "Cultist.Oriflamme.NeedSpintria".Translate(6),
                    OnSelect = () =>
                    {
                        if (TryConsumeSpintria(map, 6))
                            Messages.Message("Cultist.RivalArc.Countered".Translate(name), MessageTypeDefOf.PositiveEvent);
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.RivalArc.Opt2Fight".Translate(),
                    Tag = "Cultist.RivalArc.Tag2Fight".Translate(),
                    OnSelect = () => TryFireFactionRaid(map, "Cultist_RivalLongFaction",
                        "Cultist.RivalArc.RaidTitle".Translate(name),
                        "Cultist.RivalArc.RaidBody".Translate(name),
                        1.05f),
                    CloseAfterSelect = true
                }
            }
        }));
    }

    private void OpenRivalStage3Dialog(Map map)
    {
        string name = EnsureRivalLongName();
        bool hasInf = Influence >= 8;
        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "rival_stage_3",
            Title = "Cultist.RivalArc.Title3".Translate(),
            SpeakerName = name,
            SpeakerSubtitle = "Cultist.RivalArc.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Summon/apostlelantern.pillar4",
            ProfileTexPath = CultistDialogBg.RivalLong,
            AccentColor = new Color(0.94f, 0.82f, 0.38f),
            BodyText = "Cultist.RivalArc.Body3".Translate(name),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.RivalArc.Opt3Ward".Translate(),
                    Tag = "Cultist.RivalArc.Tag3Ward".Translate(),
                    Disabled = !hasInf,
                    DisabledReason = "Cultist.RivalArc.NeedInf8".Translate(),
                    OnSelect = () =>
                    {
                        ConsumeInfluence(8);
                        IntVec3 spot = DropCellFinder.TradeDropSpot(map);
                        DropVaultBonus(map, spot, "Cultist_Spintria", 3);
                        if (GetLoreDef(3) is { } lore3)
                            GenPlace.TryPlaceThing(ThingMaker.MakeThing(lore3), spot, map, ThingPlaceMode.Near);
                        PlayCultistSound("Cultist_Sfx_RiteComplete");
                        Messages.Message("Cultist.RivalArc.Countered".Translate(name), MessageTypeDefOf.PositiveEvent);
                    },
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.RivalArc.Opt3Endure".Translate(),
                    Tag = "Cultist.RivalArc.Tag3Endure".Translate(),
                    OnSelect = () =>
                    {
                        int now = Find.TickManager.TicksGame;
                        LeaderDream?.AddRisk(true, now);
                        LeaderDream?.AddRisk(false, now);
                        AddNotoriety(12f);
                    },
                    CloseAfterSelect = true
                }
            }
        }));
    }

    internal void OpenRivalStage4Dialog(Map map)
    {
        string name = EnsureRivalLongName();
        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "rival_stage_4",
            Title = "Cultist.RivalArc.Title4".Translate(),
            SpeakerName = name,
            SpeakerSubtitle = "Cultist.RivalArc.Sub".Translate(),
            PortraitTexPath = "Cultist/Catalog/Summon/apostlelantern.pillar4",
            ProfileTexPath = CultistDialogBg.RivalLong,
            AccentColor = new Color(0.94f, 0.82f, 0.38f),
            BodyText = "Cultist.RivalArc.Body4".Translate(name),
            Options = new List<StoryDialogOption>
            {
                new()
                {
                    Label = "Cultist.RivalArc.Opt4Launch".Translate(),
                    Tag = "Cultist.RivalArc.Tag4Launch".Translate(),
                    OnSelect = () => LaunchRivalFortressSite(map),
                    CloseAfterSelect = true
                },
                new()
                {
                    Label = "Cultist.Dialog.Later".Translate(),
                    CloseAfterSelect = true
                }
            }
        }));
    }

    internal bool LaunchRivalFortressSite(Map homeMap)
    {
        if (homeMap == null || HasActiveRivalFortressSite())
            return false;

        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, homeMap.Tile, 8, 22,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        SitePartDef vaultDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Cultist_OccultVault");
        if (vaultDef == null)
            return false;

        Faction rival = EnsureFaction("Cultist_RivalLongFaction")
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        float threatPoints = Math.Max(450f, StorytellerUtility.DefaultThreatPointsNow(homeMap) * 1.25f);
        Site site = SiteMaker.MakeSite(vaultDef, tile, rival, ifHostileThenMustRemainHostile: true, threatPoints: threatPoints);
        if (site == null)
            return false;

        string name = EnsureRivalLongName();
        int seed = Gen.HashCombineInt(tile.tileId, Find.TickManager.TicksGame ^ 7717);
        vaultSites.RemoveAll(r => r.SiteId == site.ID);
        vaultSites.Add(new VaultSiteRecord
        {
            SiteId = site.ID,
            Aspect = ChurchAspect,
            Tier = 4,
            Seed = seed,
            BookLabel = "Cultist.RivalArc.FortressTitle".Translate(name),
            MissionKind = VaultMissionKind.RivalFortress,
            MissionIndex = 4
        });

        activeRivalFortressSiteId = site.ID;
        site.GetComponent<TimeoutComp>()?.StartTimeout(GenDate.TicksPerDay * 30);
        Find.WorldObjects.Add(site);

        CreateCampaignQuest(
            "Cultist.RivalArc.FortressTitle".Translate(name),
            "Cultist.RivalArc.FortressDesc".Translate(name),
            3,
            offered: false,
            target: site);

        PlayCultistSound("Cultist_Sfx_SiteRevealed");
        Messages.Message("Cultist.RivalArc.FortressMarked".Translate(name), site, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    internal static void SpawnRivalFortressBoss(Map map, IntVec3 center)
    {
        Faction rival = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("Cultist_RivalLongFaction"))
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        if (rival == null || map == null)
            return;

        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Cultist_LongAssassin") ?? PawnKindDefOf.AncientSoldier;
        if (!CellFinder.TryFindRandomCellNear(center, map, 6, c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null, out IntVec3 cell))
            cell = center;

        Pawn boss = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            kind, rival, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false));

        string fullName = Current?.EnsureRivalLongName() ?? "Julian Coseley";
        string[] parts = fullName.Split(' ');
        boss.Name = parts.Length >= 2
            ? new NameTriple(parts[0], parts[0], parts[parts.Length - 1])
            : new NameSingle(fullName);

        ApplyEnemyWealthScaling(boss, isBoss: true);
        GenSpawn.Spawn(boss, cell, map, WipeMode.Vanish);

        var squad = new List<Pawn> { boss };
        string[] guardKinds = { "Cultist_PawnKind_MaidInMirror", "Cultist_PawnKind_Percussigant" };
        for (int i = 0; i < guardKinds.Length; i++)
        {
            if (DefDatabase<PawnKindDef>.GetNamedSilentFail(guardKinds[i]) is not { } gKind)
                continue;
            if (!CellFinder.TryFindRandomCellNear(center, map, 7, c => c.InBounds(map) && c.Standable(map), out IntVec3 gCell))
                continue;
            Pawn g = PawnGenerator.GeneratePawn(new PawnGenerationRequest(gKind, rival, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
            GenSpawn.Spawn(g, gCell, map, WipeMode.Vanish);
            squad.Add(g);
        }

        LordMaker.MakeNewLord(rival, new LordJob_DefendPoint(center, 15f, 24f), map, squad);
    }

    internal void OnRivalFortressDefeated(Pawn actor, Map map, IntVec3 pos)
    {
        if (rivalLongStage >= 5)
            return;
        rivalLongStage = 5;
        activeRivalFortressSiteId = -1;

        DropVaultBonus(map, pos, "Cultist_Spintria", 15);
        if (CultPathDef.ToolDefFor(ChurchAspect, 3) is { } rTool)
            DropVaultBonus(map, pos, rTool.defName, 1);
        if (GetLoreDef(5) is { } lore5)
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(lore5), pos, map, ThingPlaceMode.Near);

        ThoughtDef triumph = DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_RivalVanquished");
        if (triumph != null)
        {
            foreach (Map m in Find.Maps)
            foreach (Pawn c in m.mapPawns.FreeColonistsSpawned)
                c.needs?.mood?.thoughts?.memories?.TryGainMemory(triumph);
        }

        string name = EnsureRivalLongName();
        Find.WindowStack.Add(new CultistStoryDialogWindow(new StoryDialogNode
        {
            NodeId = "rival_defeated",
            Title = "Cultist.RivalArc.DoneTitle".Translate(),
            SpeakerName = name,
            SpeakerSubtitle = "Cultist.RivalArc.DoneSub".Translate(),
            PortraitTexPath = "Cultist/UI/MansusMap",
            ProfileTexPath = CultistDialogBg.Mansus,
            AccentColor = new Color(0.62f, 0.56f, 0.90f),
            BodyText = "Cultist.RivalArc.DoneBody".Translate(name),
            Options = new List<StoryDialogOption>
            {
                new() { Label = "OK".Translate(), CloseAfterSelect = true }
            }
        }));
    }
}
