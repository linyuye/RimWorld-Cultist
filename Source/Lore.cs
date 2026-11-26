using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed class RitualMaterialProperties : CompProperties
{
    public Aspect aspect = Aspect.Lantern;
    public int loreTier;
    public int toolLight;

    public RitualMaterialProperties() => compClass = typeof(RitualMaterial);
}

public sealed class RitualMaterial : ThingComp
{
    private RitualMaterialProperties Material => (RitualMaterialProperties)props;
    public Aspect Aspect => Material.aspect;
    public int LoreTier => Material.loreTier;
    public int ToolLight => Material.toolLight;
    public int AspectValue => LoreTier > 0 ? LoreTier * 2 : ToolLight;
    public int DisplayTier => LoreTier > 0 ? LoreTier : (ToolLight > 0 ? System.Math.Max(1, ToolLight / 4) : 0);

    public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
    {
        int tier = DisplayTier;
        if (tier <= 0)
            yield break;

        string aspectName = ("Cultist.Aspect." + Aspect).Translate();
        string val = $"{aspectName} {tier}（{"Cultist.AspectStrength".Translate(AspectValue)}）";
        string report = $"{aspectName} · {"Cultist.AspectTierPower".Translate(tier, AspectValue)}\n\n{("Cultist.AspectDesc." + Aspect).Translate()}";

        yield return new StatDrawEntry(
            StatCategoryDefOf.BasicsImportant,
            "Cultist.AspectStatLabel".Translate(),
            val,
            report,
            4050);
    }

    public override string CompInspectStringExtra() => LoreTier > 0
        ? "Cultist.LoreInspect".Translate(LoreTier, LoreTier * 2, ("Cultist.Aspect." + Aspect).Translate()).ToString()
        : ToolLight > 0
            ? "Cultist.ToolInspect".Translate(ToolLight, ("Cultist.Aspect." + Aspect).Translate()).ToString()
            : null;
}

public sealed class PendingLoreEntry : IExposable
{
    public Aspect Aspect = Aspect.Lantern;
    public int Tier = 1;
    public int Count = 1;

    public void ExposeData()
    {
        Scribe_Values.Look(ref Aspect, "aspect", Aspect.Lantern);
        Scribe_Values.Look(ref Tier, "tier", 1);
        Scribe_Values.Look(ref Count, "count", 1);
    }
}

public sealed partial class Campaign
{
    internal const int RiteRadius = 8;
    private int awaitingLoreDelivery;
    private List<PendingLoreEntry> pendingLore = new();
    private int learnedLoreTier;

    public ThingDef GetLoreDef(int tier, Aspect? aspect = null) =>
        CultPathDef.LoreDefFor(aspect ?? ChurchAspect, tier);

    private void ExposeLore()
    {
        Scribe_Values.Look(ref awaitingLoreDelivery, "lore");
        Scribe_Collections.Look(ref pendingLore, "pendingLore", LookMode.Deep);
        Scribe_Values.Look(ref learnedLoreTier, "learnedLoreTier");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            pendingLore ??= new List<PendingLoreEntry>();
            if (awaitingLoreDelivery > 0)
            {
                pendingLore.Add(new PendingLoreEntry
                {
                    Aspect = ChurchAspect,
                    Tier = 1,
                    Count = awaitingLoreDelivery
                });
                awaitingLoreDelivery = 0;
            }
            if (marks > 0)
                learnedLoreTier = System.Math.Max(1, learnedLoreTier);
        }
    }

    internal void GrantLore(Pawn recipient, int count, int tier = 1, Aspect? aspect = null)
    {
        if (count <= 0)
            return;
        Aspect resolvedAspect = aspect ?? ChurchAspect;
        int clampedTier = System.Math.Clamp(tier, 1, 7);
        PendingLoreEntry existing = pendingLore.FirstOrDefault(e => e.Aspect == resolvedAspect && e.Tier == clampedTier);
        if (existing != null)
            existing.Count += count;
        else
            pendingLore.Add(new PendingLoreEntry { Aspect = resolvedAspect, Tier = clampedTier, Count = count });

        DeliverLore(recipient);
    }

    private void DeliverLore(Pawn recipient = null)
    {
        if (pendingLore.Count == 0)
            return;

        Pawn targetPawn = recipient is { Dead: false } ? recipient : leader;
        Map map = null;
        IntVec3 dropPos = IntVec3.Invalid;

        if (targetPawn is { Spawned: true } && targetPawn.Map != null)
        {
            map = targetPawn.Map;
            dropPos = targetPawn.Position;
        }
        else if (leader is { Spawned: true, Dead: false } && leader.Map != null)
        {
            targetPawn = leader;
            map = leader.Map;
            dropPos = leader.Position;
        }
        else if (Find.Maps.FirstOrDefault(m => m.IsPlayerHome) is { } homeMap)
        {
            map = homeMap;
            dropPos = DropCellFinder.TradeDropSpot(homeMap);
        }

        for (int i = 0; i < pendingLore.Count; i++)
        {
            PendingLoreEntry entry = pendingLore[i];
            ThingDef def = GetLoreDef(entry.Tier, entry.Aspect);
            while (entry.Count > 0)
            {
                Thing book = ThingMaker.MakeThing(def);
                bool delivered = false;
                if (map != null && dropPos.IsValid && dropPos.InBounds(map))
                    delivered = GenPlace.TryPlaceThing(book, dropPos, map, ThingPlaceMode.Near);
                if (!delivered && targetPawn is { Dead: false } && targetPawn.inventory?.innerContainer != null)
                    delivered = targetPawn.inventory.innerContainer.TryAdd(book);

                if (!delivered)
                {
                    book.Destroy();
                    pendingLore.RemoveAll(e => e.Count <= 0);
                    return;
                }
                entry.Count--;
            }
        }
        pendingLore.RemoveAll(e => e.Count <= 0);
    }

    public void LearnLore(Pawn pawn, int tier)
    {
        if (tier <= learnedLoreTier || pawn is not { IsColonistPlayerControlled: true, Downed: false }
            || !IsMember(pawn))
            return;
        learnedLoreTier = tier;
        Messages.Message("Cultist.LoreLearned".Translate(pawn.LabelShortCap, tier), pawn,
            MessageTypeDefOf.PositiveEvent);
    }

    internal static bool IsRiteMaterial(Pawn pawn, Thing site, Thing item) =>
        item is { Spawned: true } && item.Map == site.Map && !item.IsForbidden(pawn)
        && item.Position.InHorDistOf(site.Position, RiteRadius);

    internal static IEnumerable<Thing> NearbyMaterials(Pawn pawn, Thing site)
    {
        if (site is not { Spawned: true } || pawn.Map != site.Map)
            yield break;
        foreach (IntVec3 cell in GenRadial.RadialCellsAround(site.Position, RiteRadius, true))
        {
            if (!cell.InBounds(site.Map))
                continue;
            foreach (Thing item in cell.GetThingList(site.Map))
            {
                if (item.TryGetComp<RitualMaterial>() != null && !item.IsForbidden(pawn)
                    && pawn.CanReserveAndReach(item, PathEndMode.Touch, Danger.Some))
                    yield return item;
            }
        }
    }

    internal int LoreCost(AltarAction action, Pawn pawn) => action switch
    {
        AltarAction.Promote => Rank(pawn),
        AltarAction.Mark => ActiveMark?.books ?? 0,
        AltarAction.Vigil => 1,
        AltarAction.AnswerInquiry => 1,
        _ => 0
    };

    internal static bool HasRiteLore(Pawn pawn, Thing site, IReadOnlyList<Thing> books, int count) =>
        count == 0 || (books.Count >= count && books.Take(count).Distinct().Count() == count
            && books.Take(count).All(book => IsRiteMaterial(pawn, site, book)
                && book.TryGetComp<RitualMaterial>()?.LoreTier > 0));

    internal static void ConsumeLore(IEnumerable<Thing> books)
    {
        foreach (Thing book in books)
            book.Destroy();
    }
}
