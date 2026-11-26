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
    private int bureauActCompleted;
    private int activeBureauActSiteId = -1;

    public int BureauActCompleted => bureauActCompleted;
    public bool BureauNetworkCrippled => bureauActCompleted >= 5;

    private void ExposeBureauCounterOffensive()
    {
        Scribe_Values.Look(ref bureauActCompleted, "bureauActCompleted");
        Scribe_Values.Look(ref activeBureauActSiteId, "activeBureauActSiteId", -1);
    }

    internal bool HasActiveBureauActSite()
    {
        if (activeBureauActSiteId < 0)
            return false;
        bool exists = Find.WorldObjects.Sites.Any(s => s.ID == activeBureauActSiteId && !s.Destroyed);
        if (!exists)
            activeBureauActSiteId = -1;
        return exists;
    }

    internal bool CanOpenBureauCounterOffensive() =>
        stage >= CampaignStage.Dreaming && bureauActCompleted < 5;

    internal int NextBureauActNumber => Math.Clamp(bureauActCompleted + 1, 1, 5);

    internal void OpenBureauActDialog(Pawn speaker)
    {
        if (speaker?.Map == null || bureauActCompleted >= 5)
            return;

        int act = NextBureauActNumber;
        (string portrait, string profile, Color accent) = BureauActVisuals(act);
        bool hasActive = HasActiveBureauActSite();

        var options = new List<StoryDialogOption>();
        if (hasActive)
        {
            options.Add(new StoryDialogOption
            {
                Label = "Cultist.BureauAct.AlreadyActive".Translate(act),
                Disabled = true,
                DisabledReason = "Cultist.BureauAct.AlreadyActiveReason".Translate()
            });
        }
        else
        {
            options.Add(new StoryDialogOption
            {
                Label = "Cultist.BureauAct.Launch".Translate(act),
                Tag = $"Cultist.BureauAct.RewardTag{act}".Translate(),
                OnSelect = () => LaunchBureauActSite(speaker, act),
                CloseAfterSelect = true
            });
        }

        options.Add(new StoryDialogOption
        {
            Label = "Cultist.Dialog.Later".Translate(),
            CloseAfterSelect = true
        });

        var node = new StoryDialogNode
        {
            NodeId = $"bureau_act_{act}",
            Title = $"Cultist.BureauAct.Title{act}".Translate(),
            SpeakerName = $"Cultist.BureauAct.Boss{act}".Translate(),
            SpeakerSubtitle = $"Cultist.BureauAct.Sub{act}".Translate(),
            PortraitTexPath = portrait,
            ProfileTexPath = profile,
            AccentColor = accent,
            BodyText = $"Cultist.BureauAct.Brief{act}".Translate(),
            Options = options
        };

        Find.WindowStack.Add(new CultistStoryDialogWindow(node));
    }

    private static (string Portrait, string Profile, Color Accent) BureauActVisuals(int act) => act switch
    {
        1 => ("Cultist/Catalog/Follower/douglas_r", CultistDialogBg.Douglas, new Color(0.88f, 0.72f, 0.34f)),
        2 => ("Cultist/Catalog/Follower/spencer_r", CultistDialogBg.Spencer, new Color(0.76f, 0.62f, 0.44f)),
        3 => ("Cultist/Catalog/Follower/natalia_r", CultistDialogBg.Natalia, new Color(0.86f, 0.42f, 0.46f)),
        4 => ("Cultist/Catalog/Follower/connie_r",  CultistDialogBg.Connie,  new Color(0.46f, 0.70f, 0.88f)),
        _ => ("Cultist/Catalog/Follower/zachary_r", CultistDialogBg.Zachary, new Color(0.68f, 0.76f, 0.84f)),
    };

    private bool LaunchBureauActSite(Pawn speaker, int act)
    {
        if (speaker?.Map == null || HasActiveBureauActSite())
            return false;

        int tier = Math.Clamp(act, 1, 4);
        int minDist = 4 + act * 2;
        int maxDist = 10 + act * 4;
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, speaker.Map.Tile, minDist, maxDist,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        SitePartDef vaultDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Cultist_OccultVault");
        if (vaultDef == null)
            return false;

        Faction bureau = EnsureFaction("Cultist_SuppressionBureau")
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        float threatPoints = Math.Max(180f, StorytellerUtility.DefaultThreatPointsNow(speaker.Map) * (0.55f + act * 0.18f));
        Site site = SiteMaker.MakeSite(vaultDef, tile, bureau, ifHostileThenMustRemainHostile: true, threatPoints: threatPoints);
        if (site == null)
            return false;

        int seed = Gen.HashCombineInt(tile.tileId, Find.TickManager.TicksGame ^ (act * 9187));
        vaultSites.RemoveAll(r => r.SiteId == site.ID);
        vaultSites.Add(new VaultSiteRecord
        {
            SiteId = site.ID,
            Aspect = ChurchAspect,
            Tier = tier,
            Seed = seed,
            BookLabel = $"Cultist.BureauAct.Title{act}".Translate(),
            MissionKind = VaultMissionKind.BureauAct,
            MissionIndex = act
        });

        activeBureauActSiteId = site.ID;
        site.GetComponent<TimeoutComp>()?.StartTimeout((20 + act * 3) * GenDate.TicksPerDay);
        Find.WorldObjects.Add(site);

        CreateCampaignQuest(
            $"Cultist.BureauAct.Title{act}".Translate(),
            $"Cultist.BureauAct.QuestDesc{act}".Translate(),
            Math.Min(act, 3),
            offered: false,
            target: site);

        PlayCultistSound("Cultist_Sfx_SiteRevealed");
        Messages.Message("Cultist.BureauAct.SiteMarked".Translate(act), site, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    internal static void SpawnBureauActBosses(Map map, IntVec3 center, int act)
    {
        Faction bureau = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("Cultist_SuppressionBureau"))
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        if (bureau == null || map == null)
            return;

        var bosses = new List<Pawn>();
        switch (act)
        {
            case 1:
                TrySpawnNamedHunter(map, center, bureau, "Douglas", "Douglas", "Meredith", Gender.Male, "Cultist_Appearance_Douglas", bosses);
                break;
            case 2:
                TrySpawnNamedHunter(map, center, bureau, "Spencer", "Spencer", "Hobson", Gender.Male, "Cultist_Appearance_Spencer", bosses);
                break;
            case 3:
                TrySpawnNamedHunter(map, center, bureau, "Natalia", "Natalia", "Brulleau", Gender.Female, "Cultist_Appearance_Natalia", bosses);
                break;
            case 4:
                TrySpawnNamedHunter(map, center, bureau, "Connie", "Connie", "Lee", Gender.Female, "Cultist_Appearance_Connie", bosses);
                TrySpawnNamedHunter(map, center, bureau, "Zachary", "Zachary", "Wakefield", Gender.Male, "Cultist_Appearance_Zachary", bosses);
                break;
            default:
                TrySpawnNamedHunter(map, center, bureau, "Zachary", "Zachary", "Wakefield", Gender.Male, "Cultist_Appearance_Zachary", bosses);
                break;
        }

        if (bosses.Count > 0)
            LordMaker.MakeNewLord(bureau, new LordJob_DefendPoint(center, 14f, 22f), map, bosses);
    }

    private static void TrySpawnNamedHunter(Map map, IntVec3 near, Faction bureau,
        string first, string nick, string last, Gender gender, string appearanceHediff, List<Pawn> outList)
    {
        PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Cultist_BureauHunter") ?? PawnKindDefOf.AncientSoldier;
        if (!CellFinder.TryFindRandomCellNear(near, map, 6, c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null, out IntVec3 cell))
            cell = near;

        Pawn boss = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            kind, bureau, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false, fixedGender: gender));
        boss.Name = new NameTriple(first, nick, last);

        if (DefDatabase<HediffDef>.GetNamedSilentFail(appearanceHediff) is { } appDef
            && !boss.health.hediffSet.HasHediff(appDef))
        {
            boss.health.AddHediff(appDef);
        }

        ApplyEnemyWealthScaling(boss, isBoss: true);
        GenSpawn.Spawn(boss, cell, map, WipeMode.Vanish);
        boss.Drawer?.renderer?.SetAllGraphicsDirty();
        outList.Add(boss);
    }

    internal void OnBureauActCompleted(int act, Pawn actor, Map map, IntVec3 pos)
    {
        if (act <= bureauActCompleted)
            return;

        bureauActCompleted = Math.Max(bureauActCompleted, act);
        activeBureauActSiteId = -1;
        ReduceNotoriety(15f + act * 5f);

        switch (act)
        {
            case 1:
                DropVaultBonus(map, pos, "Cultist_BureauDossier", 2);
                DropVaultBonus(map, pos, "Cultist_Spintria", 4);
                break;
            case 2:
                DropRandomClassicBooks(map, pos, 2);
                DropVaultBonus(map, pos, "Cultist_Spintria", 6);
                break;
            case 3:
                SpawnRescuedHeretic(map, pos);
                DropVaultBonus(map, pos, "Cultist_Spintria", 6);
                break;
            case 4:
                DropVaultBonus(map, pos, "Cultist_Spintria", 10);
                DropRandomClassicBooks(map, pos, 1);
                break;
            case 5:
                DropVaultBonus(map, pos, "Cultist_Spintria", 15);
                DropRandomClassicBooks(map, pos, 2);
                if (GetLoreDef(5) is { } lore5)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(lore5), pos, map, ThingPlaceMode.Near);
                break;
        }

        (string portrait, string profile, Color accent) = BureauActVisuals(act);
        var node = new StoryDialogNode
        {
            NodeId = $"bureau_act_done_{act}",
            Title = $"Cultist.BureauAct.DoneTitle{act}".Translate(),
            SpeakerName = $"Cultist.BureauAct.Boss{act}".Translate(),
            SpeakerSubtitle = $"Cultist.BureauAct.Sub{act}".Translate(),
            PortraitTexPath = portrait,
            ProfileTexPath = profile,
            AccentColor = accent,
            BodyText = $"Cultist.BureauAct.DoneBody{act}".Translate(),
            Options = new List<StoryDialogOption>
            {
                new() { Label = "OK".Translate(), CloseAfterSelect = true }
            }
        };
        Find.WindowStack.Add(new CultistStoryDialogWindow(node));
    }

    private void SpawnRescuedHeretic(Map map, IntVec3 pos)
    {
        Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
        if (church != null && pawn.ideo != null)
            pawn.ideo.SetIdeo(church);
        Aspect asp = (Aspect)Rand.Range(0, 8);
        aspects[pawn.GetUniqueLoadID()] = asp;
        ranks[pawn.GetUniqueLoadID()] = 2;
        GenSpawn.Spawn(pawn, pos, map, WipeMode.Vanish);
        Messages.Message("Cultist.BureauAct.RescuedHeretic".Translate(pawn.LabelShortCap, ("Cultist.Aspect." + asp).Translate()),
            pawn, MessageTypeDefOf.PositiveEvent);
    }

    internal static void DropVaultBonus(Map map, IntVec3 pos, string defName, int count)
    {
        if (map == null || count <= 0 || DefDatabase<ThingDef>.GetNamedSilentFail(defName) is not { } def)
            return;
        Thing t = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        t.stackCount = Math.Min(count, def.stackLimit);
        GenPlace.TryPlaceThing(t, pos, map, ThingPlaceMode.Near);
    }

    internal static void DropRandomClassicBooks(Map map, IntVec3 pos, int count)
    {
        if (map == null || count <= 0)
            return;
        var books = DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.tradeTags != null && d.tradeTags.Contains("CultistBook"))
            .ToList();
        if (books.Count == 0)
            return;
        for (int i = 0; i < count; i++)
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(books.RandomElement()), pos, map, ThingPlaceMode.Near);
    }
}
