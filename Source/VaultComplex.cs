using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Cultist;

public sealed class VaultSiteRecord : IExposable
{
    public int SiteId;
    public Aspect Aspect = Aspect.Lantern;
    public int Tier = 1;
    public int Seed;
    public string BookLabel = string.Empty;

    public void ExposeData()
    {
        Scribe_Values.Look(ref SiteId, "siteId");
        Scribe_Values.Look(ref Aspect, "aspect", Aspect.Lantern);
        Scribe_Values.Look(ref Tier, "tier", 1);
        Scribe_Values.Look(ref Seed, "seed");
        Scribe_Values.Look(ref BookLabel, "bookLabel", string.Empty);
    }
}

public sealed partial class Campaign
{
    private List<VaultSiteRecord> vaultSites = new();

    private void ExposeVaultSites()
    {
        Scribe_Collections.Look(ref vaultSites, "vaultSites", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            vaultSites ??= new List<VaultSiteRecord>();
    }

    internal VaultSiteRecord GetVaultRecord(int siteId) =>
        vaultSites?.FirstOrDefault(r => r.SiteId == siteId);

    internal bool SpawnVaultComplexSite(Pawn reader, string bookLabel, Aspect targetAspect, int tier)
    {
        if (reader?.Map == null)
            return false;

        int clampedTier = Math.Clamp(tier, 1, 4);
        int minDist = 3 + clampedTier * 2;
        int maxDist = 8 + clampedTier * 4;
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, reader.Map.Tile, minDist, maxDist,
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
        float threatPoints = Math.Max(100f, StorytellerUtility.DefaultThreatPointsNow(reader.Map) * (0.45f + clampedTier * 0.25f));
        Site site = SiteMaker.MakeSite(vaultDef, tile, bureau, ifHostileThenMustRemainHostile: true, threatPoints: threatPoints);
        if (site == null)
            return false;

        int seed = Gen.HashCombineInt(tile.tileId, Find.TickManager.TicksGame ^ (bookLabel?.GetHashCode() ?? 0));
        vaultSites.RemoveAll(r => r.SiteId == site.ID);
        vaultSites.Add(new VaultSiteRecord
        {
            SiteId = site.ID,
            Aspect = targetAspect,
            Tier = clampedTier,
            Seed = seed,
            BookLabel = bookLabel ?? string.Empty
        });

        site.GetComponent<TimeoutComp>()?.StartTimeout((15 + clampedTier * 4) * GenDate.TicksPerDay);
        Find.WorldObjects.Add(site);

        string aspectLabel = ("Cultist.Aspect." + targetAspect).Translate();
        Quest quest = CreateCampaignQuest(
            "Cultist.VaultSite.Title".Translate(clampedTier, bookLabel),
            "Cultist.VaultSite.Desc".Translate(bookLabel, aspectLabel, clampedTier),
            clampedTier,
            offered: false,
            target: site);
        if (quest != null)
            QuestUtility.SendLetterQuestAvailable(quest);

        Find.LetterStack.ReceiveLetter(
            "Cultist.VaultSite.Title".Translate(clampedTier, bookLabel),
            "Cultist.VaultSite.Desc".Translate(bookLabel, aspectLabel, clampedTier),
            LetterDefOf.PositiveEvent,
            new LookTargets(site));
        return true;
    }
}

public sealed class CompProperties_CultistVaultChest : CompProperties
{
    public int openWorkTicks = 180;
    public CompProperties_CultistVaultChest() => compClass = typeof(CompCultistVaultChest);
}

public sealed class CompCultistVaultChest : ThingComp
{
    public Aspect targetAspect = Aspect.Lantern;
    public int targetTier = 1;
    public bool opened;

    public CompProperties_CultistVaultChest ChestProps => (CompProperties_CultistVaultChest)props;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref targetAspect, "targetAspect", Aspect.Lantern);
        Scribe_Values.Look(ref targetTier, "targetTier", 1);
        Scribe_Values.Look(ref opened, "opened");
    }

    public override string CompInspectStringExtra()
    {
        if (opened)
            return "Cultist.VaultChest.OpenedInspect".Translate();
        string aspectLabel = ("Cultist.Aspect." + targetAspect).Translate();
        return "Cultist.VaultChest.SealedInspect".Translate(aspectLabel, targetTier);
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (opened || !pawn.IsColonistPlayerControlled)
            yield break;

        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield return new FloatMenuOption(
                "Cultist.Unreachable".Translate("Cultist.VaultChest.OpenOption".Translate()),
                null);
            yield break;
        }

        yield return new FloatMenuOption(
            "Cultist.VaultChest.OpenOption".Translate(),
            () =>
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamed("Cultist_OpenVaultChest");
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc);
            });
    }

    internal void OpenBy(Pawn pawn)
    {
        if (opened || parent.Destroyed || parent.Map == null)
            return;
        opened = true;

        Map map = parent.Map;
        IntVec3 pos = parent.Position;
        int tier = Math.Clamp(targetTier, 1, 7);

        // 1. Guaranteed target Aspect Lore book
        ThingDef loreDef = CultPathDef.ForAspect(targetAspect)?.GetLoreDef(tier)
            ?? Campaign.Current?.GetLoreDef(tier);
        if (loreDef != null)
            GenPlace.TryPlaceThing(ThingMaker.MakeThing(loreDef), pos, map, ThingPlaceMode.Near);

        // 2. Ancient Spintriae coins
        if (DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria") is { } coinDef)
        {
            Thing coins = ThingMaker.MakeThing(coinDef);
            coins.stackCount = Rand.RangeInclusive(1 + tier, 3 + tier * 2);
            GenPlace.TryPlaceThing(coins, pos, map, ThingPlaceMode.Near);
        }

        // 3. Basic & valuable colony resources
        DropResource(ThingDefOf.Silver, Rand.RangeInclusive(80 * tier, 180 * tier), pos, map);
        DropResource(ThingDefOf.ComponentIndustrial, Rand.RangeInclusive(tier, 2 + tier), pos, map);
        if (Rand.Chance(0.65f))
            DropResource(ThingDefOf.Jade, Rand.RangeInclusive(12 * tier, 25 * tier), pos, map);
        if (tier >= 2 && Rand.Chance(0.60f))
            DropResource(ThingDefOf.Gold, Rand.RangeInclusive(8 * tier, 18 * tier), pos, map);
        if (Rand.Chance(0.70f))
            DropResource(ThingDefOf.MedicineIndustrial, Rand.RangeInclusive(2, 3 + tier), pos, map);

        // 4. Optional cracked ritual tool or dossier
        if (Rand.Chance(0.55f))
        {
            string toolName = tier switch
            {
                1 => "Cultist_RiteTool1Cracked",
                2 => "Cultist_RiteTool2Cracked",
                3 => "Cultist_RiteTool3Cracked",
                _ => "Cultist_RiteTool3Ready"
            };
            if (DefDatabase<ThingDef>.GetNamedSilentFail(toolName) is { } toolDef)
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(toolDef), pos, map, ThingPlaceMode.Near);
        }

        Campaign.PlayCultistSound("Cultist_Sfx_RiteComplete");
        FleckMaker.ThrowMicroSparks(pos.ToVector3Shifted(), map);
        string aspectLabel = ("Cultist.Aspect." + targetAspect).Translate();
        Messages.Message("Cultist.VaultChest.OpenedMsg".Translate(pawn.LabelShortCap, aspectLabel, tier),
            new LookTargets(pos, map), MessageTypeDefOf.PositiveEvent);
    }

    private static void DropResource(ThingDef def, int count, IntVec3 pos, Map map)
    {
        if (def == null || count <= 0)
            return;
        Thing t = ThingMaker.MakeThing(def);
        t.stackCount = Math.Min(count, def.stackLimit);
        GenPlace.TryPlaceThing(t, pos, map, ThingPlaceMode.Near);
    }
}

public sealed class JobDriver_OpenVaultChest : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => job.targetA.Thing.TryGetComp<CompCultistVaultChest>()?.opened ?? true);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        int ticks = job.targetA.Thing.TryGetComp<CompCultistVaultChest>()?.ChestProps.openWorkTicks ?? 180;
        yield return Toils_General.Wait(ticks).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            job.targetA.Thing?.TryGetComp<CompCultistVaultChest>()?.OpenBy(pawn);
        });
    }
}

public sealed class GenStep_CultistVaultComplex : GenStep_AncientComplex
{
    internal static VaultSiteRecord CurrentVaultContext;

    public override int SeedPart => 1874291103;

    public override void Generate(Map map, GenStepParams parms)
    {
        VaultSiteRecord record = Campaign.Current?.GetVaultRecord(map.Parent?.ID ?? -1)
            ?? new VaultSiteRecord
            {
                Aspect = Campaign.Current?.ChurchAspect ?? Aspect.Lantern,
                Tier = 1,
                Seed = map.uniqueID
            };

        CurrentVaultContext = record;
        Rand.PushState(record.Seed);
        try
        {
            int size = record.Tier switch
            {
                1 => 28,
                2 => 34,
                3 => 40,
                _ => 46
            };
            CellRect rect = CellRect.CenteredOn(map.Center, size, size);
            rect.ClipInsideMap(map);

            LayoutDef layoutDef = DefDatabase<LayoutDef>.GetNamed("Cultist_OccultVaultLayout");
            GenerateAndSpawn(rect, map, parms, layoutDef);
            EnsureVaultChestPlaced(map, rect, record);
        }
        finally
        {
            Rand.PopState();
            CurrentVaultContext = null;
        }
    }

    private static void EnsureVaultChestPlaced(Map map, CellRect rect, VaultSiteRecord record)
    {
        ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_VaultChest");
        if (chestDef == null)
            return;
        if (map.listerThings.ThingsOfDef(chestDef).Count > 0)
            return;

        IntVec3 spot = rect.Cells.FirstOrDefault(c => c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null);
        if (!spot.IsValid)
            spot = map.Center;
        SpawnVaultChestAt(map, spot, record);
    }

    internal static void SpawnVaultChestAt(Map map, IntVec3 cell, VaultSiteRecord record)
    {
        ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_VaultChest");
        if (chestDef == null || !cell.InBounds(map))
            return;
        Thing chest = ThingMaker.MakeThing(chestDef);
        if (chest.TryGetComp<CompCultistVaultChest>() is { } comp && record != null)
        {
            comp.targetAspect = record.Aspect;
            comp.targetTier = record.Tier;
        }
        GenSpawn.Spawn(chest, cell, map, WipeMode.Vanish);
    }
}

// ==================== SHARED ROOM HELPERS ====================

public abstract class RoomContents_CultistBase : RoomContentsWorker
{
    protected static VaultSiteRecord CurrentRecord =>
        GenStep_CultistVaultComplex.CurrentVaultContext
        ?? new VaultSiteRecord { Aspect = Aspect.Lantern, Tier = 1, Seed = 1 };

    protected static void TrySpawnThing(Map map, LayoutRoom room, string defName, ThingDef stuff = null, int count = 1)
    {
        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (def == null)
            return;
        if (def.MadeFromStuff && stuff == null)
            stuff = GenStuff.DefaultStuffFor(def);
        if (!room.TryGetRandomCellInRoom(def, map, out IntVec3 cell, null, contractedBy: 1))
            return;
        Thing t = ThingMaker.MakeThing(def, stuff);
        if (count > 1 && def.stackLimit > 1)
            t.stackCount = Math.Min(count, def.stackLimit);
        GenSpawn.Spawn(t, cell, map, WipeMode.Vanish);
    }

    protected static void SpawnBureauGuards(Map map, LayoutRoom room, int count)
    {
        Faction bureau = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("Cultist_SuppressionBureau"))
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        if (bureau == null || count <= 0)
            return;

        string[] kinds = { "Cultist_BureauAgent", "Cultist_BureauInvestigator", "Cultist_BureauHunter" };
        var spawned = new List<Pawn>();
        for (int i = 0; i < count; i++)
        {
            int idx = Math.Clamp(CurrentRecord.Tier - 1 + (Rand.Bool ? 0 : 1), 0, kinds.Length - 1);
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(kinds[idx]) ?? PawnKindDefOf.AncientSoldier;
            if (!room.TryGetRandomCellInRoom(map, out IntVec3 cell, contractedBy: 1))
                continue;
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, bureau, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
            GenSpawn.Spawn(p, cell, map, WipeMode.Vanish);
            spawned.Add(p);
        }
        AssignRoomGuardLord(map, room, bureau, spawned);
    }

    protected static void SpawnSpiritGuards(Map map, LayoutRoom room, int count, bool elite)
    {
        Faction hostile = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("Cultist_RivalLongFaction"))
            ?? Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamedSilentFail("Cultist_SuppressionBureau"))
            ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: true);
        if (hostile == null || count <= 0)
            return;

        string[] pool = elite || CurrentRecord.Tier >= 3
            ? new[] { "Cultist_PawnKind_MaidInMirror", "Cultist_PawnKind_Percussigant", "Cultist_PawnKind_RawProphet", "Cultist_PawnKind_VoicelessDead" }
            : new[] { "Cultist_PawnKind_Hint", "Cultist_PawnKind_Caligine", "Cultist_PawnKind_ShatteredRisen", "Cultist_PawnKind_BurgeoningRisen" };

        var spawned = new List<Pawn>();
        for (int i = 0; i < count; i++)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(pool.RandomElement());
            if (kind == null || !room.TryGetRandomCellInRoom(map, out IntVec3 cell, contractedBy: 1))
                continue;
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, hostile, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
            GenSpawn.Spawn(p, cell, map, WipeMode.Vanish);
            spawned.Add(p);
        }
        AssignRoomGuardLord(map, room, hostile, spawned);
    }

    private static void AssignRoomGuardLord(Map map, LayoutRoom room, Faction faction, List<Pawn> pawns)
    {
        if (pawns.Count == 0)
            return;
        IntVec3 center = room.Boundary.CenterCell;
        if (!center.InBounds(map) || !center.Standable(map))
            center = pawns[0].Position;
        LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(center, 5f), map, pawns);
    }

    protected static void ClearInteriorRoof(Map map, LayoutRoom room)
    {
        foreach (IntVec3 c in room.Cells)
        {
            if (c.InBounds(map) && room.Contains(c, contractedBy: 1))
                map.roofGrid.SetRoof(c, null);
        }
    }
}

// 1. 秘藏圣室 (Vault Sanctum - 必出 1 间，核心秘藏铁匣 + 精英守卫)
public sealed class RoomContents_CultistVaultSanctum : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);

        // Place the Vault Chest near room center
        IntVec3 center = room.Boundary.CenterCell;
        if (!center.InBounds(map) || !center.Standable(map) || center.GetEdifice(map) != null)
            room.TryGetRandomCellInRoom(map, out center, contractedBy: 1);
        if (center.IsValid)
            GenStep_CultistVaultComplex.SpawnVaultChestAt(map, center, CurrentRecord);

        // Columns & decorative braziers/torches
        int decor = Math.Clamp(room.Area / 22, 2, 4);
        for (int i = 0; i < decor; i++)
            TrySpawnThing(map, room, Rand.Bool ? "Column" : "TorchLamp", ThingDefOf.BlocksGranite);

        // Elite guards scaled by tier
        int tier = CurrentRecord.Tier;
        if (Rand.Bool)
            SpawnSpiritGuards(map, room, Math.Clamp(tier, 1, 3), elite: tier >= 2);
        else
            SpawnBureauGuards(map, room, Math.Clamp(1 + tier, 2, 4));
    }
}

// 2. 封存藏书室 (Archive Library)
public sealed class RoomContents_CultistArchiveLibrary : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        for (int i = 0; i < 3; i++)
            TrySpawnThing(map, room, "Shelf", ThingDefOf.WoodLog);
        TrySpawnThing(map, room, "Table1x2c", ThingDefOf.WoodLog);
        TrySpawnThing(map, room, "Stool", ThingDefOf.WoodLog);
        if (Rand.Chance(0.65f))
            TrySpawnThing(map, room, "Cultist_Spintria", count: Rand.RangeInclusive(1, 3));
        if (Rand.Chance(0.55f))
            SpawnSpiritGuards(map, room, Rand.RangeInclusive(1, Math.Min(2, CurrentRecord.Tier)), elite: false);
    }
}

// 3. 废弃仪式厅 (Ritual Hall)
public sealed class RoomContents_CultistRitualHall : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        for (int i = 0; i < 3; i++)
            TrySpawnThing(map, room, "Column", DefDatabase<ThingDef>.GetNamedSilentFail("BlocksMarble") ?? ThingDefOf.BlocksGranite);
        TrySpawnThing(map, room, "TorchLamp");
        TrySpawnThing(map, room, "TorchLamp");
        SpawnSpiritGuards(map, room, Rand.RangeInclusive(1, Math.Clamp(CurrentRecord.Tier, 1, 3)), elite: CurrentRecord.Tier >= 3);
    }
}

// 4. 防剿局哨室 (Bureau Outpost)
public sealed class RoomContents_CultistBureauOutpost : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        TrySpawnThing(map, room, "Table2x2c", ThingDefOf.Steel);
        for (int i = 0; i < 2; i++)
            TrySpawnThing(map, room, "Stool", ThingDefOf.Steel);
        TrySpawnThing(map, room, "Sandbags", ThingDefOf.Cloth);
        if (Rand.Chance(0.75f))
            TrySpawnThing(map, room, "Cultist_BureauDossier");
        SpawnBureauGuards(map, room, Rand.RangeInclusive(1, Math.Clamp(1 + CurrentRecord.Tier, 2, 4)));
    }
}

// 5. 秘库杂物间 (Relic Storage)
public sealed class RoomContents_CultistRelicStorage : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        for (int i = 0; i < 2; i++)
            TrySpawnThing(map, room, "Shelf", ThingDefOf.WoodLog);
        TrySpawnThing(map, room, "Steel", count: Rand.RangeInclusive(25, 60));
        if (Rand.Chance(0.6f))
            TrySpawnThing(map, room, "ComponentIndustrial", count: Rand.RangeInclusive(1, 3));
        if (Rand.Chance(0.5f))
            TrySpawnThing(map, room, "MedicineHerbal", count: Rand.RangeInclusive(2, 5));
        if (Rand.Chance(0.45f))
            TrySpawnThing(map, room, "Cultist_Spintria", count: Rand.RangeInclusive(1, 2));
        if (Rand.Chance(0.35f))
            SpawnSpiritGuards(map, room, 1, elite: false);
    }
}

// 6. 荒废庭院 (Courtyard - 露天自然光 + 石柱枯树装饰)
public sealed class RoomContents_CultistCourtyard : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        ClearInteriorRoof(map, room);

        ThingDef limestone = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksLimestone") ?? ThingDefOf.BlocksGranite;
        ThingDef marble = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksMarble") ?? ThingDefOf.BlocksGranite;
        for (int i = 0; i < 2; i++)
            TrySpawnThing(map, room, "Column", limestone);
        TrySpawnThing(map, room, "PlantPot", marble);

        ThingDef bushDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Bush") ?? ThingDefOf.Plant_Grass;
        foreach (IntVec3 c in room.Cells)
        {
            if (c.InBounds(map) && room.Contains(c, contractedBy: 1) && c.GetEdifice(map) == null && Rand.Chance(0.14f))
                GenSpawn.Spawn(ThingMaker.MakeThing(bushDef), c, map, WipeMode.Vanish);
        }
    }
}

// 7. 遗迹空地 / 残垣广场 (Open Plinth - 开阔石砾过渡区)
public sealed class RoomContents_CultistOpenPlinth : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        ClearInteriorRoof(map, room);

        for (int i = 0; i < 3; i++)
            TrySpawnThing(map, room, "ChunkLimestone");
        TrySpawnThing(map, room, "Column", ThingDefOf.BlocksGranite);
    }
}

// 8. 废弃种植区 (Abandoned Farm - 荒废菜圃、野药草与农具箱)
public sealed class RoomContents_CultistAbandonedFarm : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        ClearInteriorRoof(map, room);

        TerrainDef soil = TerrainDefOf.Soil;
        ThingDef healroot = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_HealrootWild") ?? ThingDefOf.Plant_Grass;
        foreach (IntVec3 c in room.Cells)
        {
            if (!c.InBounds(map) || !room.Contains(c, contractedBy: 1))
                continue;
            map.terrainGrid.SetTerrain(c, soil);
            if (c.GetEdifice(map) == null && Rand.Chance(0.22f))
                GenSpawn.Spawn(ThingMaker.MakeThing(Rand.Chance(0.35f) ? healroot : ThingDefOf.Plant_Grass), c, map, WipeMode.Vanish);
        }
        TrySpawnThing(map, room, "WoodLog", count: Rand.RangeInclusive(15, 35));
    }
}

// 9. 废弃工业区 (Abandoned Workshop - 旧工作台、废钢渣与残存零件)
public sealed class RoomContents_CultistAbandonedWorkshop : RoomContents_CultistBase
{
    public override void FillRoom(Map map, LayoutRoom room, Faction faction, float? threatPoints = null)
    {
        base.FillRoom(map, room, faction, threatPoints);
        TrySpawnThing(map, room, "TableStonecutter", ThingDefOf.Steel);
        for (int i = 0; i < 3; i++)
            TrySpawnThing(map, room, "ChunkSlagSteel");
        TrySpawnThing(map, room, "Steel", count: Rand.RangeInclusive(20, 45));
        if (Rand.Chance(0.5f))
            TrySpawnThing(map, room, "ComponentIndustrial", count: Rand.RangeInclusive(1, 2));
    }
}
