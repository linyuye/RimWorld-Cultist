using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Cultist;

public sealed partial class Campaign
{
    private HashSet<string> sentMilestones = new();

    private void ExposeMilestones()
    {
        Scribe_Collections.Look(ref sentMilestones, "sentMilestones", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            sentMilestones ??= new HashSet<string>();
    }

    internal void SendMilestoneLetterOnce(string key, LetterDef letterDef = null, LookTargets target = null)
    {
        if (string.IsNullOrEmpty(key) || !sentMilestones.Add(key))
            return;
        string titleKey = $"Cultist.Milestone.{key}.Title";
        string bodyKey = $"Cultist.Milestone.{key}.Body";
        if (!titleKey.CanTranslate() || !bodyKey.CanTranslate())
            return;
        Find.LetterStack.ReceiveLetter(titleKey.Translate(), bodyKey.Translate(),
            letterDef ?? LetterDefOf.NeutralEvent, target);
    }
}
