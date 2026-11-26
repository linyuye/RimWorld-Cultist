using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private int lastAnomalyLevel = -1;
    private int lastCodexDiscovered = -1;
    private int lastVagabondRollTick = -1;
    private int lastAnomalyRaidRollTick = -1;
    private bool glimpsedLaughingthrush;
    private HashSet<string> seenEasterEggs = new();

    private static readonly (string Id, string TitleKey, string BodyKey)[] AnomalyEasterEggPool =
    {
        ("Vagabond", "Cultist.Vagabond.LetterTitle", "Cultist.Vagabond.LetterBody"),
        ("TwinWitches", "Cultist.EasterEgg.TwinWitches.Title", "Cultist.EasterEgg.TwinWitches.Body"),
        ("Beachcomber", "Cultist.EasterEgg.Beachcomber.Title", "Cultist.EasterEgg.Beachcomber.Body"),
        ("Elegiast", "Cultist.EasterEgg.Elegiast.Title", "Cultist.EasterEgg.Elegiast.Body"),
        ("Moth", "Cultist.EasterEgg.Moth.Title", "Cultist.EasterEgg.Moth.Body"),
    };

    public bool GlimpsedLaughingthrush => glimpsedLaughingthrush;

    private void ExposeAnomalyEasterEgg()
    {
        Scribe_Values.Look(ref lastAnomalyLevel, "lastAnomalyLevel", -1);
        Scribe_Values.Look(ref lastCodexDiscovered, "lastCodexDiscovered", -1);
        Scribe_Values.Look(ref lastVagabondRollTick, "lastVagabondRollTick", -1);
        Scribe_Values.Look(ref lastAnomalyRaidRollTick, "lastAnomalyRaidRollTick", -1);
        Scribe_Values.Look(ref glimpsedLaughingthrush, "glimpsedLaughingthrush");
        Scribe_Collections.Look(ref seenEasterEggs, "seenEasterEggs", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            seenEasterEggs ??= new HashSet<string>();
    }

    private void TickAnomalyEasterEgg(int now)
    {
        if (CultistMod.Settings is { enableEasterEggs: false })
            return;
        if (!ModsConfig.AnomalyActive || Find.Anomaly == null)
            return;

        bool anomalyStepOccurred = false;

        int currentLevel = Find.Anomaly.Level;
        if (lastAnomalyLevel < 0)
            lastAnomalyLevel = currentLevel;
        else if (currentLevel != lastAnomalyLevel)
        {
            lastAnomalyLevel = currentLevel;
            anomalyStepOccurred = true;
        }

        if (Find.EntityCodex != null)
        {
            int discovered = DefDatabase<EntityCategoryDef>.AllDefsListForReading
                .Sum(cat => Find.EntityCodex.DiscoveredCount(cat));
            if (lastCodexDiscovered < 0)
                lastCodexDiscovered = discovered;
            else if (discovered > lastCodexDiscovered)
            {
                lastCodexDiscovered = discovered;
                anomalyStepOccurred = true;
            }
        }

        // Also detect active Anomaly study or psychic ritual invocation (with 1-day roll cooldown)
        if (!anomalyStepOccurred && (lastVagabondRollTick < 0 || now - lastVagabondRollTick >= GenDate.TicksPerDay))
        {
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                    continue;
                bool studyingOrRitual = map.mapPawns.FreeColonistsSpawned.Any(p =>
                    p.CurJobDef == JobDefOf.StudyInteract
                    || p.CurJobDef?.defName == "InvokePsychicRitual");
                if (studyingOrRitual)
                {
                    lastVagabondRollTick = now;
                    anomalyStepOccurred = true;
                    break;
                }
            }
        }

        // Also detect hostile Anomaly entity / Horax cultist raids (1-day roll cooldown)
        if (!anomalyStepOccurred && (lastAnomalyRaidRollTick < 0 || now - lastAnomalyRaidRollTick >= GenDate.TicksPerDay))
        {
            Faction entities = Faction.OfEntities;
            Faction horax = Faction.OfHoraxCult;
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome)
                    continue;
                bool anomalyRaidPresent = map.mapPawns.AllPawnsSpawned.Any(p =>
                    !p.Dead && !p.Downed
                    && ((entities != null && p.Faction == entities) || (horax != null && p.Faction == horax))
                    && p.HostileTo(Faction.OfPlayer));
                if (anomalyRaidPresent)
                {
                    lastAnomalyRaidRollTick = now;
                    anomalyStepOccurred = true;
                    break;
                }
            }
        }

        if (!anomalyStepOccurred)
            return;

        // 5% chance to send non-intrusive vanilla right-side Letter without interfering with vanilla Anomaly
        if (!Rand.Chance(0.05f))
            return;

        seenEasterEggs ??= new HashSet<string>();
        var unseen = AnomalyEasterEggPool.Where(e => !seenEasterEggs.Contains(e.Id)).ToList();
        var chosen = unseen.Count > 0 ? unseen.RandomElement() : AnomalyEasterEggPool.RandomElement();
        seenEasterEggs.Add(chosen.Id);
        if (chosen.Id == "Vagabond")
            glimpsedLaughingthrush = true;

        PlayCultistSound("Cultist_Sfx_SiteRevealed");
        Find.LetterStack.ReceiveLetter(
            chosen.TitleKey.Translate(),
            chosen.BodyKey.Translate(),
            LetterDefOf.NeutralEvent);
    }
}
