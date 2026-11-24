using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private float notoriety;
    private int nextBureauRaidTick = -1;
    private int nextRivalLongRaidTick = -1;
    private HashSet<int> countedMurderIds = new();

    public float Notoriety => notoriety;

    private void ExposeNotoriety()
    {
        Scribe_Values.Look(ref notoriety, "notoriety");
        Scribe_Values.Look(ref nextBureauRaidTick, "nextBureauRaidTick", -1);
        Scribe_Values.Look(ref nextRivalLongRaidTick, "nextRivalLongRaidTick", -1);
        Scribe_Collections.Look(ref countedMurderIds, "countedMurderIds", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            countedMurderIds ??= new HashSet<int>();
    }

    internal TaggedString NotorietyTierLabel() => notoriety switch
    {
        >= 70f => "Cultist.Notoriety.Extreme".Translate(),
        >= 40f => "Cultist.Notoriety.High".Translate(),
        >= 15f => "Cultist.Notoriety.Notable".Translate(),
        _ => "Cultist.Notoriety.Quiet".Translate()
    };

    internal void AddNotoriety(float amount, Thing site = null, Pawn actor = null)
    {
        if (amount <= 0f)
            return;
        float multiplier = site != null && EvaluateHqTraits(site).HasFlag(HqTrait.Secret) ? 0.6f : 1f;
        if (actor != null && HasExaltedTrait(actor, ExaltedTraitId.SilentStep))
            multiplier *= 0.75f;
        notoriety = System.Math.Min(100f, notoriety + amount * multiplier);
        if (notoriety >= 40f)
            SendMilestoneLetterOnce("HighNotoriety", LetterDefOf.NegativeEvent, leader);
    }

    internal void ReduceNotoriety(float amount)
    {
        if (amount <= 0f)
            return;
        notoriety = System.Math.Max(0f, notoriety - amount);
    }

    internal void EnsureWorldFactionsWithSettlements(bool notifyIfAdded)
    {
        EnsureFaction("Cultist_RivalLongFaction", spawnSettlements: 0, notifyIfAdded: false);
        EnsureFaction("Cultist_SuppressionBureau", spawnSettlements: 2, notifyIfAdded: notifyIfAdded);
    }

    private Faction EnsureFaction(string defName, int spawnSettlements = 0, bool notifyIfAdded = false)
    {
        FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(defName);
        if (def == null)
            return null;
        Faction existing = Find.FactionManager.FirstFactionOfDef(def);
        if (existing != null)
            return existing;
        Faction created = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));
        Find.FactionManager.Add(created);

        int placed = 0;
        for (int i = 0; i < spawnSettlements; i++)
        {
            PlanetTile tile = TileFinder.RandomSettlementTileFor(created);
            if (!tile.Valid)
                continue;
            var settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(created);
            settlement.Tile = tile;
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
            Find.WorldObjects.Add(settlement);
            placed++;
        }

        if (notifyIfAdded && placed > 0)
        {
            Find.LetterStack.ReceiveLetter(
                "Cultist.BureauArrivedTitle".Translate(),
                "Cultist.BureauArrivedBody".Translate(created.Name, placed),
                LetterDefOf.NeutralEvent);
        }
        return created;
    }

    private void TickNotoriety(int now)
    {
        if (stage < CampaignStage.Dreaming)
            return;

        // Gentle natural decay (~2 points per day, -40% slower under Secret Histories doctrine)
        if (notoriety > 0f)
        {
            float decayMult = ChurchAspect == Aspect.SecretHistories ? 0.60f : 1.0f;
            notoriety = System.Math.Max(0f, notoriety - (2f / 240f) * decayMult);
        }

        Map map = leader?.Map ?? Find.Maps.FirstOrDefault(candidate => candidate.IsPlayerHome);
        if (map is not { IsPlayerHome: true })
            return;

        CheckNonRaidKillings(map);

        // 1. Suppression Bureau armed raid driven by high notoriety
        if (notoriety >= 40f && (nextBureauRaidTick < 0 || now >= nextBureauRaidTick))
        {
            nextBureauRaidTick = now + GenDate.TicksPerDay * 4;
            if (Rand.Chance(notoriety >= 70f ? 0.5f : 0.25f) && TryFireFactionRaid(map, "Cultist_SuppressionBureau",
                    "Cultist.BureauRaidTitle".Translate(), "Cultist.BureauRaidBody".Translate(),
                    0.85f + notoriety / 200f))
            {
                ReduceNotoriety(30f);
            }
        }

        // 2. Rival Long periodic competition raids during normal progression
        if (marks >= 1)
        {
            if (nextRivalLongRaidTick < 0)
                nextRivalLongRaidTick = now + Rand.RangeInclusive(GenDate.TicksPerDay * 10, GenDate.TicksPerDay * 16);
            else if (now >= nextRivalLongRaidTick)
            {
                nextRivalLongRaidTick = now + Rand.RangeInclusive(GenDate.TicksPerDay * 12, GenDate.TicksPerDay * 18);
                if (Rand.Chance(0.65f))
                {
                    TryFireFactionRaid(map, "Cultist_RivalLongFaction",
                        "Cultist.RivalLongRaidTitle".Translate(), "Cultist.RivalLongRaidBody".Translate(),
                        0.75f + marks * 0.08f);
                }
            }
        }
    }

    private bool TryFireFactionRaid(Map map, string factionDefName, TaggedString title, TaggedString body, float pointScale)
    {
        Faction faction = EnsureFaction(factionDefName);
        if (faction == null)
            return false;
        IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
        parms.faction = faction;
        parms.points = System.Math.Max(60f, parms.points * pointScale);
        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
        parms.forced = true;
        if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            return false;
        PlayCultistSound("Cultist_Sfx_Backlash");
        Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.ThreatBig, new LookTargets(map.Center, map));
        return true;
    }

    private void CheckNonRaidKillings(Map map)
    {
        foreach (Thing item in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
        {
            if (item is not Corpse { InnerPawn: { RaceProps.Humanlike: true } victim } corpse)
                continue;
            if (corpse.Age > GenDate.TicksPerHour * 2 || countedMurderIds.Contains(victim.thingIDNumber))
                continue;

            bool nonHostileOrCaptive = victim.IsColonist || victim.IsSlaveOfColony || victim.IsPrisonerOfColony
                || (victim.Faction != null && victim.Faction != Faction.OfPlayer && !victim.Faction.HostileTo(Faction.OfPlayer));
            if (!nonHostileOrCaptive)
                continue;

            bool violent = victim.health?.hediffSet?.hediffs.Any(h => h is Hediff_Injury or Hediff_MissingPart) == true;
            if (!violent)
                continue;

            countedMurderIds.Add(victim.thingIDNumber);
            AddNotoriety(4f);
            Messages.Message("Cultist.Notoriety.Murder".Translate(victim.LabelShortCap),
                corpse, MessageTypeDefOf.NegativeEvent);
        }
    }
}
