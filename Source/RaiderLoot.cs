using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed class BureauDossierProperties : CompProperties
{
    public BureauDossierProperties() => compClass = typeof(BureauDossierComp);
}

public sealed class BureauDossierComp : ThingComp
{
    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || !pawn.IsColonistPlayerControlled)
            yield break;

        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield return new FloatMenuOption("Cultist.Unreachable".Translate(parent.LabelShortCap), null);
            yield break;
        }

        bool isSecret = EvaluateRoomSecret(parent);
        int burnReduction = isSecret ? 12 : 10;
        yield return new FloatMenuOption(
            "Cultist.Dossier.BurnOption".Translate(burnReduction),
            () => StartDossierJob(pawn, mode: 0));

        if (campaign.Stage >= CampaignStage.Dreaming && campaign.IsMember(pawn))
        {
            yield return new FloatMenuOption(
                "Cultist.Dossier.DecodeOption".Translate(),
                () => StartDossierJob(pawn, mode: 1));
        }
    }

    internal static bool EvaluateRoomSecret(Thing thing) =>
        thing is { Spawned: true } && Campaign.EvaluateHqTraits(thing).HasFlag(HqTrait.Secret);

    private void StartDossierJob(Pawn pawn, int mode)
    {
        JobDef jobDef = DefDatabase<JobDef>.GetNamed("Cultist_UseBureauDossier");
        Job job = JobMaker.MakeJob(jobDef, parent);
        job.count = mode;
        pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
    }
}

public sealed class BureauDossierJobDriver : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => job.targetA.Thing.IsForbidden(pawn));
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(240).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            Thing dossier = job.targetA.Thing;
            if (dossier == null || dossier.Destroyed || Campaign.Current is not { } campaign)
                return;

            if (job.count == 0)
            {
                bool secret = BureauDossierComp.EvaluateRoomSecret(dossier);
                float amount = secret ? 12f : 10f;
                if (dossier.Map != null)
                    FleckMaker.ThrowSmoke(dossier.DrawPos, dossier.Map, 1.6f);
                campaign.ReduceNotoriety(amount);
                Campaign.PlayCultistSound("Cultist_Sfx_RiteComplete");
                Messages.Message("Cultist.Dossier.Burned".Translate(pawn.LabelShortCap, amount.ToString("0")),
                    pawn, MessageTypeDefOf.PositiveEvent);
                dossier.Destroy();
            }
            else
            {
                int tier = Rand.RangeInclusive(2, 3);
                if (campaign.SpawnBookExplorationSite(pawn, dossier.LabelCap, tier))
                {
                    Campaign.PlayCultistSound("Cultist_Sfx_SiteRevealed");
                    Messages.Message("Cultist.Dossier.Decoded".Translate(pawn.LabelShortCap, tier),
                        pawn, MessageTypeDefOf.PositiveEvent);
                    dossier.Destroy();
                }
            }
        });
    }
}

public sealed partial class Campaign
{
    private HashSet<int> processedRaiderPawnIds = new();

    private static readonly (Aspect Aspect, string Apparel, string Weapon)[] OccultSets =
    {
        (Aspect.Lantern, "Cultist_FieldCoat", "Cultist_Engraver"),
        (Aspect.Forge, "Cultist_ForgeCoat", "Cultist_ForgeHammer"),
        (Aspect.Edge, "Cultist_EdgeGarb", "Cultist_EdgeDagger"),
        (Aspect.Winter, "Cultist_WinterRobe", "Cultist_WinterBlade"),
        (Aspect.Heart, "Cultist_HeartVestments", "Cultist_HeartChime"),
        (Aspect.Grail, "Cultist_GrailRobe", "Cultist_GrailChalice"),
        (Aspect.Knock, "Cultist_KnockRobe", "Cultist_KnockKeyBlade"),
    };

    private static readonly string[] HunterApparels =
    {
        "Cultist_ForgeCoat", "Cultist_WinterRobe", "Cultist_EdgeGarb",
        "Cultist_MothMask", "Cultist_FieldCoat", "Cultist_KnockRobe"
    };

    private static readonly string[] HunterWeapons =
    {
        "Cultist_EdgeDagger", "Cultist_WinterBlade", "Cultist_KnockKeyBlade"
    };

    private void ExposeRaiderLoot()
    {
        Scribe_Collections.Look(ref processedRaiderPawnIds, "processedRaiderPawnIds", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            processedRaiderPawnIds ??= new HashSet<int>();
    }

    private void TickRaiderLoadouts()
    {
        foreach (Map map in Find.Maps)
        {
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn?.kindDef == null || pawn.Dead || pawn.IsColonist)
                    continue;
                string kind = pawn.kindDef.defName;
                if (!kind.StartsWith("Cultist_Bureau") && !kind.StartsWith("Cultist_Long"))
                    continue;
                if (!processedRaiderPawnIds.Add(pawn.thingIDNumber))
                    continue;

                ApplyRaiderOccultLoadout(pawn, kind);
            }
        }
    }

    private static void ApplyRaiderOccultLoadout(Pawn pawn, string kind)
    {
        switch (kind)
        {
            case "Cultist_BureauAgent":
                if (Rand.Chance(0.25f))
                    TryWearOccultApparel(pawn, Rand.Bool ? "Cultist_FieldCoat" : "Cultist_EdgeGarb", QualityCategory.Normal);
                if (Rand.Chance(0.12f))
                    TryAddInventoryItem(pawn, "Cultist_BureauDossier", 1);
                break;

            case "Cultist_BureauHunter":
                TryApplyBureauHunterAppearance(pawn);
                if (Rand.Chance(0.50f))
                    TryWearOccultApparel(pawn, HunterApparels.RandomElement(), QualityCategory.Good);
                if (Rand.Chance(0.40f))
                    TryEquipOccultWeapon(pawn, HunterWeapons.RandomElement(), QualityCategory.Good);
                if (Rand.Chance(0.45f))
                    TryAddInventoryItem(pawn, "Cultist_BureauDossier", 1);
                break;

            case "Cultist_LongServant":
            {
                var set = OccultSets.RandomElement();
                if (Rand.Chance(0.40f))
                    TryWearOccultApparel(pawn, set.Apparel, QualityCategory.Normal);
                if (Rand.Chance(0.30f))
                    TryEquipOccultWeapon(pawn, set.Weapon, QualityCategory.Normal);
                if (Rand.Chance(0.25f))
                    TryAddInventoryItem(pawn, "Cultist_Spintria", 1);
                break;
            }

            case "Cultist_LongAssassin":
            {
                var set = OccultSets.RandomElement();
                QualityCategory qual = Rand.Bool ? QualityCategory.Excellent : QualityCategory.Good;
                if (Rand.Chance(0.80f))
                    TryWearOccultApparel(pawn, set.Apparel, QualityCategory.Good);
                if (Rand.Chance(0.70f))
                    TryEquipOccultWeapon(pawn, set.Weapon, qual);
                if (Rand.Chance(0.65f))
                    TryAddInventoryItem(pawn, "Cultist_Spintria", Rand.RangeInclusive(1, 2));
                if (Rand.Chance(0.25f))
                {
                    Aspect loreAsp = (Aspect)Rand.Range(0, 8);
                    ThingDef lore2 = CultPathDef.LoreDefFor(loreAsp, 2);
                    if (lore2 != null)
                        TryAddInventoryDef(pawn, lore2, 1);
                }
                if (Rand.Chance(0.15f))
                {
                    if (Rand.Bool)
                        TryAddInventoryItem(pawn, "Cultist_RiteTool2Cracked", 1);
                    else
                    {
                        ThingDef book = DefDatabase<ThingDef>.AllDefsListForReading
                            .Where(d => d.tradeTags != null && d.tradeTags.Contains("CultistBook"))
                            .RandomElementWithFallback();
                        if (book != null)
                            TryAddInventoryDef(pawn, book, 1);
                    }
                }
                break;
            }
        }
    }

    private static void TryApplyBureauHunterAppearance(Pawn pawn)
    {
        if (pawn?.health == null)
            return;
        string[] pool = pawn.gender == Gender.Female
            ? new[] { "Cultist_Appearance_Connie", "Cultist_Appearance_Natalia" }
            : new[] { "Cultist_Appearance_Douglas", "Cultist_Appearance_Spencer", "Cultist_Appearance_Zachary" };
        HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(pool.RandomElement());
        if (def != null && !pawn.health.hediffSet.HasHediff(def))
        {
            pawn.health.AddHediff(def);
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }

    private static void TryWearOccultApparel(Pawn pawn, string defName, QualityCategory quality)
    {
        if (pawn.apparel == null || DefDatabase<ThingDef>.GetNamedSilentFail(defName) is not { } def)
            return;
        ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
        if (ThingMaker.MakeThing(def, stuff) is not Apparel apparel)
            return;
        apparel.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
        var worn = pawn.apparel.WornApparel;
        for (int i = worn.Count - 1; i >= 0; i--)
        {
            if (!ApparelUtility.CanWearTogether(def, worn[i].def, pawn.RaceProps.body))
            {
                Apparel old = worn[i];
                pawn.apparel.Remove(old);
                old.Destroy();
            }
        }
        pawn.apparel.Wear(apparel, dropReplacedApparel: false);
    }

    private static void TryEquipOccultWeapon(Pawn pawn, string defName, QualityCategory quality)
    {
        if (pawn.equipment == null || DefDatabase<ThingDef>.GetNamedSilentFail(defName) is not { } def)
            return;
        ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
        if (ThingMaker.MakeThing(def, stuff) is not ThingWithComps weapon)
            return;
        weapon.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
        if (pawn.equipment.Primary is { } existing)
        {
            pawn.equipment.Remove(existing);
            existing.Destroy();
        }
        pawn.equipment.AddEquipment(weapon);
    }

    private static void TryAddInventoryItem(Pawn pawn, string defName, int count)
    {
        if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) is { } def)
            TryAddInventoryDef(pawn, def, count);
    }

    private static void TryAddInventoryDef(Pawn pawn, ThingDef def, int count)
    {
        if (pawn.inventory?.innerContainer == null || count <= 0)
            return;
        Thing item = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        item.stackCount = System.Math.Min(count, def.stackLimit);
        if (!pawn.inventory.innerContainer.TryAdd(item))
            item.Destroy();
    }
}
