using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private Site expedition;

    private void ExposeExpedition()
    {
        Scribe_References.Look(ref expedition, "expedition");
    }

    public void RecordTranscript(Pawn reader)
    {
        GrantLore(reader, 2);
        TrackTranscriptQuest();
        Messages.Message("Cultist.TranscriptRead".Translate(reader.LabelShortCap),
            reader, MessageTypeDefOf.PositiveEvent);
    }

    public bool HasActiveExpedition => expedition is { Spawned: true, Destroyed: false };

    public Thing FindReturnedPlate(Map map)
    {
        if (map == null || !map.IsPlayerHome)
            return null;
        ThingDef plate = CultistDefs.Cultist_LightPlate;
        return map.listerThings.ThingsOfDef(plate).FirstOrDefault(item => !item.IsForbidden(Faction.OfPlayer))
            ?? map.mapPawns.FreeColonistsSpawned.Select(pawn => CarriedSupply(pawn, plate))
                .FirstOrDefault(item => item != null);
    }

    private static Thing CarriedSupply(Pawn pawn, ThingDef supply) =>
        pawn.carryTracker?.CarriedThing is { } carried && carried.def == supply ? carried
            : pawn.inventory?.innerContainer.FirstOrDefault(item => item.def == supply);

    private bool HasAccessiblePlate() => HasAccessibleSupply(CultistDefs.Cultist_LightPlate);

    private static bool HasAccessibleSupply(ThingDef supply)
    {
        return Find.Maps.Any(map => map.IsPlayerHome
                && (map.listerThings.ThingsOfDef(supply).Any()
                    || map.mapPawns.FreeColonistsSpawned.Any(pawn => CarriedSupply(pawn, supply) != null)))
            || Find.WorldObjects.Caravans.Any(caravan => caravan.IsPlayerControlled
                && caravan.PawnsListForReading.Any(pawn => CarriedSupply(pawn, supply) != null));
    }

    public TaggedString ExpeditionRequirement(Pawn pawn)
    {
        if (!pawn.Spawned || !pawn.Map.IsPlayerHome)
            return "Cultist.NeedHome".Translate();
        if (HasActiveExpedition)
            return "Cultist.ExpeditionActive".Translate();
        if (HasAccessiblePlate())
            return "Cultist.ExpeditionPlateExists".Translate();
        return LearnedLoreTier == 0 ? "Cultist.NeedLearnedLore".Translate() : TaggedString.Empty;
    }

    public void OpenExpedition(Pawn pawn)
    {
        if (!CanLeadRite(pawn) || marks < 3 || marks >= 5 || !ExpeditionRequirement(pawn).NullOrEmpty())
            return;
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, pawn.Map.Tile, 2, 10,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        Site site = SiteMaker.MakeSite(CultistDefs.Cultist_AbandonedObservatory, tile, null, true);
        var part = site.parts[0];
        part.things ??= new ThingOwner<Thing>(part);
        Thing plate = ThingMaker.MakeThing(CultistDefs.Cultist_LightPlate);
        if (!part.things.TryAdd(plate))
        {
            plate.Destroy();
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        Find.WorldObjects.Add(site);
        expedition = site;
        expeditionQuest = CreateCampaignQuest("Cultist.ExpeditionTitle".Translate(),
            "Cultist.Quest.Expedition".Translate(), 2, target: site);
        site.GetComponent<TimeoutComp>()?.StartTimeout(15 * GenDate.TicksPerDay);
        Find.LetterStack.ReceiveLetter("Cultist.ExpeditionTitle".Translate(),
            "Cultist.ExpeditionBody".Translate(), LetterDefOf.PositiveEvent, site);
    }

}

public sealed class GenStep_ObservatoryCache : GenStep
{
    public override int SeedPart => 1103842789;

    public override void Generate(Map map, GenStepParams parms)
    {
        if (parms.sitePart?.things is not { Count: > 0 } contents)
            return;

        if (!CellFinder.TryFindRandomCellNear(map.Center, map, 20,
                cell => cell.Standable(map) && !cell.Fogged(map), out IntVec3 landing))
            landing = CellFinder.RandomClosewalkCellNear(map.Center, map, 20);

        if (contents.TryDropAll(landing, map, ThingPlaceMode.Near))
            map.Parent.GetComponent<TimeoutComp>()?.StopTimeout();
    }
}
