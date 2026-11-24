using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private int lastAnomalyLevel = -1;
    private int lastCodexDiscovered = -1;
    private int lastVagabondRollTick = -1;
    private bool glimpsedLaughingthrush;

    public bool GlimpsedLaughingthrush => glimpsedLaughingthrush;

    private void ExposeAnomalyEasterEgg()
    {
        Scribe_Values.Look(ref lastAnomalyLevel, "lastAnomalyLevel", -1);
        Scribe_Values.Look(ref lastCodexDiscovered, "lastCodexDiscovered", -1);
        Scribe_Values.Look(ref lastVagabondRollTick, "lastVagabondRollTick", -1);
        Scribe_Values.Look(ref glimpsedLaughingthrush, "glimpsedLaughingthrush");
    }

    private void TickAnomalyEasterEgg(int now)
    {
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

        if (!anomalyStepOccurred)
            return;

        // 5% chance to send non-intrusive vanilla right-side Letter without interfering with vanilla Anomaly
        if (!Rand.Chance(0.05f))
            return;

        glimpsedLaughingthrush = true;
        PlayCultistSound("Cultist_Sfx_SiteRevealed");
        Find.LetterStack.ReceiveLetter(
            "Cultist.Vagabond.LetterTitle".Translate(),
            "Cultist.Vagabond.LetterBody".Translate(),
            LetterDefOf.NeutralEvent);
    }
}
