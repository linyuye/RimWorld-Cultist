using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class WorldObject_CultistEvidence : WorldObject
{
    private const int BaseWorkTicks = GenDate.TicksPerHour * 10;

    private Caravan activeCaravan;
    private int workTicksRemaining = -1;
    private int expiresTick = -1;

    public override void PostMake()
    {
        base.PostMake();
        expiresTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 8;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref activeCaravan, "activeCaravan");
        Scribe_Values.Look(ref workTicksRemaining, "workTicksRemaining", -1);
        Scribe_Values.Look(ref expiresTick, "expiresTick", -1);
    }

    public bool Infiltrating => activeCaravan != null && workTicksRemaining >= 0;

    public override string GetInspectString()
    {
        var lines = new List<string>();
        string baseStr = base.GetInspectString();
        if (!string.IsNullOrEmpty(baseStr))
            lines.Add(baseStr);

        int now = Find.TickManager.TicksGame;
        float daysLeft = System.Math.Max(0f, (expiresTick - now) / (float)GenDate.TicksPerDay);
        lines.Add("Cultist.Evidence.DaysLeft".Translate(daysLeft.ToString("0.0")));
        if (Infiltrating)
        {
            float pct = 1f - System.Math.Clamp(workTicksRemaining / (float)BaseWorkTicks, 0f, 1f);
            float chance = SuccessChanceFor(activeCaravan);
            lines.Add("Cultist.Evidence.Progress".Translate(pct.ToStringPercent(), chance.ToStringPercent()));
        }
        return string.Join("\n", lines);
    }

    public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
    {
        foreach (FloatMenuOption opt in base.GetFloatMenuOptions(caravan))
            yield return opt;

        if (caravan == null || !caravan.IsPlayerControlled)
            yield break;

        if (caravan.Tile == Tile)
        {
            if (!Infiltrating)
            {
                yield return new FloatMenuOption(
                    "Cultist.Evidence.StartInfiltrate".Translate(SuccessChanceFor(caravan).ToStringPercent()),
                    () => StartInfiltration(caravan));
            }
        }
        else
        {
            yield return new FloatMenuOption(
                "Cultist.Evidence.TravelAndInfiltrate".Translate(),
                () => caravan.pather.StartPath(Tile, new CaravanArrivalAction_DestroyEvidence(this), repathImmediately: true));
        }
    }

    public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
    {
        foreach (Gizmo g in base.GetCaravanGizmos(caravan))
            yield return g;

        if (caravan != null && caravan.IsPlayerControlled && caravan.Tile == Tile && !Infiltrating)
        {
            yield return new Command_Action
            {
                defaultLabel = "Cultist.Evidence.GizmoLabel".Translate(),
                defaultDesc = "Cultist.Evidence.GizmoDesc".Translate(SuccessChanceFor(caravan).ToStringPercent()),
                icon = TexCommand.DesirePower,
                action = () => StartInfiltration(caravan)
            };
        }
    }

    internal void StartInfiltration(Caravan caravan)
    {
        if (caravan == null || caravan.Tile != Tile || Destroyed)
            return;
        caravan.pather.StopDead();
        activeCaravan = caravan;
        workTicksRemaining = BaseWorkTicks;
        Messages.Message("Cultist.Evidence.InfiltrationStarted".Translate(caravan.LabelCap),
            this, MessageTypeDefOf.NeutralEvent);
    }

    public static float SuccessChanceFor(Caravan caravan)
    {
        if (caravan == null || Campaign.Current is not { } campaign)
            return 0.55f;
        float chance = 0.55f;
        foreach (Pawn pawn in caravan.PawnsListForReading)
        {
            if (!pawn.IsColonist || pawn.Downed || pawn.Dead)
                continue;
            if (campaign.IsMember(pawn))
            {
                chance += 0.08f * campaign.Rank(pawn);
                Aspect aspect = campaign.GetAspect(pawn);
                if (aspect is Aspect.Moth or Aspect.Edge or Aspect.Lantern)
                    chance += 0.12f;
            }
        }
        return System.Math.Clamp(chance, 0.50f, 0.95f);
    }

    protected override void Tick()
    {
        base.Tick();
        if (Destroyed)
            return;

        if (Infiltrating)
        {
            if (activeCaravan.Destroyed || activeCaravan.Tile != Tile)
            {
                activeCaravan = null;
                workTicksRemaining = -1;
            }
            else
            {
                activeCaravan.pather.StopDead();
                int workers = System.Math.Max(1, activeCaravan.PawnsListForReading.Count(p => p.IsColonist && !p.Downed && !p.Dead));
                workTicksRemaining -= workers;
                if (workTicksRemaining <= 0)
                {
                    ResolveInfiltration();
                    return;
                }
            }
        }

        if (expiresTick > 0 && Find.TickManager.TicksGame >= expiresTick)
        {
            Campaign.Current?.OnEvidenceExpired(this);
            Destroy();
        }
    }

    private void ResolveInfiltration()
    {
        Caravan caravan = activeCaravan;
        activeCaravan = null;
        workTicksRemaining = -1;
        bool success = Rand.Chance(SuccessChanceFor(caravan));
        Campaign.Current?.OnEvidenceResolved(caravan, success);
        Destroy();
    }
}

public sealed class CaravanArrivalAction_DestroyEvidence : CaravanArrivalAction
{
    private WorldObject_CultistEvidence target;

    public CaravanArrivalAction_DestroyEvidence() { }
    public CaravanArrivalAction_DestroyEvidence(WorldObject_CultistEvidence target) => this.target = target;

    public override string Label => "Cultist.Evidence.GizmoLabel".Translate();
    public override string ReportString => "Cultist.Evidence.TravelAndInfiltrate".Translate();

    public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile) =>
        target != null && !target.Destroyed && target.Tile == destinationTile;

    public override void Arrived(Caravan caravan) => target?.StartInfiltration(caravan);

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref target, "target");
    }
}

public sealed partial class Campaign
{
    private int nextEvidenceSpawnTick = -1;

    private void ExposeEvidence() =>
        Scribe_Values.Look(ref nextEvidenceSpawnTick, "nextEvidenceSpawnTick", -1);

    private void TickEvidence(int now)
    {
        if (stage < CampaignStage.Dreaming || notoriety < 25f)
            return;
        if (nextEvidenceSpawnTick > 0 && now < nextEvidenceSpawnTick)
            return;
        if (Find.WorldObjects.AllWorldObjects.Any(obj => obj is WorldObject_CultistEvidence))
            return;

        Map home = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (home == null)
            return;

        nextEvidenceSpawnTick = now + GenDate.TicksPerDay * 7;
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, home.Tile, 3, 9, tileFinderMode: TileFinderMode.Near))
            return;

        WorldObjectDef def = DefDatabase<WorldObjectDef>.GetNamedSilentFail("Cultist_EvidenceSite");
        if (def == null)
            return;
        var site = (WorldObject_CultistEvidence)WorldObjectMaker.MakeWorldObject(def);
        site.Tile = tile;
        Find.WorldObjects.Add(site);
        Find.LetterStack.ReceiveLetter(
            "Cultist.Evidence.SpawnedTitle".Translate(),
            "Cultist.Evidence.SpawnedBody".Translate(),
            LetterDefOf.NegativeEvent,
            site);
    }

    internal void OnEvidenceResolved(Caravan caravan, bool success)
    {
        if (success)
        {
            ReduceNotoriety(25f);
            PlayCultistSound("Cultist_Sfx_RiteComplete");
            Find.LetterStack.ReceiveLetter(
                "Cultist.Evidence.SuccessTitle".Translate(),
                "Cultist.Evidence.SuccessBody".Translate(caravan?.LabelCap ?? ""),
                LetterDefOf.PositiveEvent,
                caravan);
            return;
        }

        AddNotoriety(15f);
        PlayCultistSound("Cultist_Sfx_Backlash");
        Map home = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (home != null)
        {
            TryFireFactionRaid(home, "Cultist_SuppressionBureau",
                "Cultist.Evidence.FailTitle".Translate(),
                "Cultist.Evidence.FailBody".Translate(),
                0.95f);
        }
    }

    internal void OnEvidenceExpired(WorldObject_CultistEvidence site)
    {
        AddNotoriety(15f);
        Map home = leader?.Map ?? Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (home != null)
        {
            TryFireFactionRaid(home, "Cultist_SuppressionBureau",
                "Cultist.Evidence.ExpiredTitle".Translate(),
                "Cultist.Evidence.ExpiredBody".Translate(),
                0.95f);
        }
    }
}
