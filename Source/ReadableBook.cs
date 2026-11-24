using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Cultist;

public sealed class CultistBookProperties : CompProperties
{
    public AncientLanguageDef requiredLanguage;
    public AncientLanguageDef teachesLanguage;
    public Aspect grantsAspect = Aspect.Lantern;
    public int grantsLoreTier;
    public ThingDef grantsLoreDef;
    public int grantsInfluenceStrength;
    public int siteTier; // 0 = none, 1..4 = tiered world exploration site
    public int readWorkTicks = 600;
    public bool consumeOnRead;

    public CultistBookProperties() => compClass = typeof(CultistBookComp);
}

public sealed class CultistBookComp : ThingComp
{
    private bool hasBeenRead;

    public CultistBookProperties BookProps => (CultistBookProperties)props;
    public bool HasBeenRead => hasBeenRead;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref hasBeenRead, "cultistBookRead");
    }

    public override string CompInspectStringExtra()
    {
        var lines = new List<string>();
        if (hasBeenRead)
            lines.Add("Cultist.Book.AlreadyRead".Translate());
        if (BookProps.requiredLanguage != null)
            lines.Add("Cultist.CodexRequiresLang".Translate(BookProps.requiredLanguage.LabelCap));
        if (BookProps.teachesLanguage != null)
            lines.Add("Cultist.Book.TeachesLang".Translate(BookProps.teachesLanguage.LabelCap));
        if ((BookProps.siteTier > 0 || BookProps.grantsLoreDef != null) && !hasBeenRead)
        {
            int previewTier = BookProps.siteTier > 0
                ? BookProps.siteTier
                : (BookProps.grantsLoreDef?.GetCompProperties<RitualMaterialProperties>()?.loreTier ?? 1);
            lines.Add("Cultist.Book.RevealsSite".Translate(previewTier));
        }
        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || !pawn.IsColonistPlayerControlled)
            yield break;
        if (hasBeenRead)
            yield break;

        if (BookProps.requiredLanguage != null && !campaign.KnowsLanguage(pawn, BookProps.requiredLanguage))
        {
            yield return new FloatMenuOption(
                "Cultist.Book.NeedLangOption".Translate(parent.LabelShortCap, BookProps.requiredLanguage.LabelCap),
                null);
            yield break;
        }

        if (!pawn.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Some))
        {
            yield return new FloatMenuOption(
                "Cultist.Unreachable".Translate("Cultist.Book.ReadOption".Translate(parent.LabelShortCap)),
                null);
            yield break;
        }

        yield return new FloatMenuOption(
            "Cultist.Book.ReadOption".Translate(parent.LabelShortCap),
            () =>
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamed("Cultist_ReadOccultBook");
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc);
            });
    }

    internal void FinishReading(Pawn pawn)
    {
        if (hasBeenRead || parent.Destroyed || Campaign.Current is not { } campaign)
            return;
        if (BookProps.requiredLanguage != null && !campaign.KnowsLanguage(pawn, BookProps.requiredLanguage))
            return;

        hasBeenRead = true;

        // 1. Teach ancient language if configured
        if (BookProps.teachesLanguage != null)
            campaign.GrantLanguage(pawn, BookProps.teachesLanguage);

        // 2. Grant lore tier learning & temporary influence
        if (BookProps.grantsLoreTier > 0)
            campaign.LearnLore(pawn, BookProps.grantsLoreTier);
        if (BookProps.grantsInfluenceStrength > 0)
            campaign.GrantBookInfluence(pawn, BookProps.grantsAspect, BookProps.grantsInfluenceStrength);

        // 3. Classic occult books & site books unlock a seeded fog-of-war Occult Ruin Complex
        //    holding the target Aspect Lore inside its Vault Chest (Option B).
        bool unlocksVault = BookProps.siteTier > 0 || BookProps.grantsLoreDef != null;
        if (unlocksVault)
        {
            int rewardTier = BookProps.grantsLoreDef?.GetCompProperties<RitualMaterialProperties>()?.loreTier
                ?? (BookProps.siteTier > 0 ? BookProps.siteTier : 1);
            Aspect rewardAspect = BookProps.grantsLoreDef != null
                ? BookProps.grantsAspect
                : campaign.ChurchAspect;
            if (campaign.SpawnVaultComplexSite(pawn, parent.LabelCap, rewardAspect, rewardTier))
            {
                Campaign.PlayCultistSound("Cultist_Sfx_SiteRevealed");
            }
            else if (BookProps.grantsLoreDef != null)
            {
                // Fallback if no world tile was available
                campaign.GrantLore(pawn, 1, rewardTier, rewardAspect);
                Campaign.PlayCultistSound("Cultist_Sfx_BookRead");
            }
        }
        else
        {
            Campaign.PlayCultistSound("Cultist_Sfx_BookRead");
        }

        Messages.Message("Cultist.Book.FinishedReading".Translate(pawn.LabelShortCap, parent.LabelShortCap),
            pawn, MessageTypeDefOf.PositiveEvent);
        Campaign.ApplyLibraryStudyBonus(pawn, parent);

        if (BookProps.consumeOnRead && !parent.Destroyed)
            parent.Destroy();
    }
}

public sealed class CultistBookJobDriver : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) =>
        pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => job.targetA.Thing.IsForbidden(pawn));
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

        CultistBookComp comp = job.targetA.Thing.TryGetComp<CultistBookComp>();
        int baseTicks = comp?.BookProps.readWorkTicks ?? 600;
        bool isAncient = comp?.BookProps.requiredLanguage != null;
        int ticks = Campaign.CalculateStudyTicks(pawn, job.targetA.Thing, baseTicks, isAncient);
        yield return Toils_General.Wait(ticks).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            job.targetA.Thing?.TryGetComp<CultistBookComp>()?.FinishReading(pawn);
        });
    }
}

public sealed partial class Campaign
{
    internal void GrantBookInfluence(Pawn pawn, Aspect aspect, int strength)
    {
        PersonalDream dream = DreamOf(pawn) ?? (IsMember(pawn) ? RegisterDreamer(pawn) : null);
        if (dream == null)
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

    /// <summary>
    /// Spawns a tiered RimWorld exploration site using native SiteMaker & vanilla/mod SitePartDef templates.
    /// </summary>
    internal bool SpawnBookExplorationSite(Pawn reader, string bookLabel, int tier)
    {
        if (reader?.Map == null)
            return false;

        int clampedTier = System.Math.Clamp(tier, 1, 4);
        int minDist = 3 + clampedTier * 2;
        int maxDist = 8 + clampedTier * 4;
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, reader.Map.Tile, minDist, maxDist,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        // Choose native RimWorld site templates (randomized between ItemStash / Outpost / custom shelter)
        var partDefs = new List<SitePartDef>();
        SitePartDef stashDef = DefDatabase<SitePartDef>.GetNamedSilentFail("ItemStash");
        SitePartDef outpostDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Outpost");
        SitePartDef shelterDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Cultist_ScholarShelter");

        if (clampedTier == 1)
        {
            SitePartDef picked = (Rand.Bool && shelterDef != null) ? shelterDef : (stashDef ?? shelterDef);
            if (picked != null) partDefs.Add(picked);
        }
        else
        {
            if (stashDef != null) partDefs.Add(stashDef);
            else if (shelterDef != null) partDefs.Add(shelterDef);

            if (outpostDef != null && (clampedTier >= 3 || Rand.Chance(0.65f)))
                partDefs.Add(outpostDef);
        }

        if (partDefs.Count == 0)
            return false;

        // Select hostile faction for guarded Tier 2+ sites (Suppression Bureau or Rival Long)
        Faction siteFaction = null;
        if (clampedTier >= 2)
        {
            string factionDefName = clampedTier >= 3 && Rand.Bool
                ? "Cultist_RivalLongFaction"
                : "Cultist_SuppressionBureau";
            siteFaction = EnsureFaction(factionDefName) ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        }

        float threatPoints = System.Math.Max(80f, StorytellerUtility.DefaultThreatPointsNow(reader.Map) * (0.45f + clampedTier * 0.22f));
        Site site = SiteMaker.MakeSite(partDefs, tile, siteFaction, ifHostileThenMustRemainHostile: true, threatPoints: threatPoints);
        if (site == null)
            return false;

        // Populate tiered occult loot into the stash/shelter SitePart
        SitePart lootPart = site.parts.FirstOrDefault(p => p.def == stashDef || p.def == shelterDef) ?? site.parts[0];
        lootPart.things ??= new ThingOwner<Thing>(lootPart);
        PopulateVaultLoot(lootPart.things, clampedTier);

        site.GetComponent<TimeoutComp>()?.StartTimeout((12 + clampedTier * 4) * GenDate.TicksPerDay);
        Find.WorldObjects.Add(site);

        Quest quest = CreateCampaignQuest(
            "Cultist.BookSiteTitle".Translate(clampedTier, bookLabel),
            "Cultist.BookSiteDesc".Translate(bookLabel, clampedTier),
            clampedTier,
            offered: false);
        if (quest != null)
            QuestUtility.SendLetterQuestAvailable(quest);

        Find.LetterStack.ReceiveLetter(
            "Cultist.BookSiteTitle".Translate(clampedTier, bookLabel),
            "Cultist.BookSiteDesc".Translate(bookLabel, clampedTier),
            LetterDefOf.PositiveEvent,
            new LookTargets(site));
        return true;
    }

    private void PopulateVaultLoot(ThingOwner container, int tier)
    {
        // 1. Lore book matching active society aspect & tier
        if (GetLoreDef(System.Math.Clamp(tier, 1, 7)) is { } loreDef)
            container.TryAdd(ThingMaker.MakeThing(loreDef));

        // 2. Cracked ritual tool scaled by tier
        string toolDefName = tier switch
        {
            1 => "Cultist_RiteTool1Cracked",
            2 => "Cultist_RiteTool2Cracked",
            3 => "Cultist_RiteTool3Cracked",
            _ => "Cultist_RiteTool3Ready"
        };
        if (DefDatabase<ThingDef>.GetNamedSilentFail(toolDefName) is { } toolDef)
            container.TryAdd(ThingMaker.MakeThing(toolDef));

        // 3. Spintriae & optional untranslated codex for Tier 2+
        if (DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria") is { } spintriaDef)
        {
            Thing coins = ThingMaker.MakeThing(spintriaDef);
            coins.stackCount = tier * 2;
            container.TryAdd(coins);
        }
        if (tier >= 2 && DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_AncientCodex") is { } codexDef)
            container.TryAdd(ThingMaker.MakeThing(codexDef));
    }
}
