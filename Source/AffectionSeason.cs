using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

internal static class AffectionSeason
{
    internal static void Apply(Map map, bool seekRomance)
    {
        Pawn[] colonists = map.mapPawns.FreeColonistsSpawned
            .Where(pawn => pawn.IsColonistPlayerControlled && pawn.RaceProps.Humanlike
                && pawn.needs?.mood?.thoughts?.memories != null)
            .ToArray();

        if (seekRomance && TryFormRelationship(colonists))
            return;

        var memory = DefDatabase<ThoughtDef>.GetNamed("Cultist_SeasonGoodwill");
        for (int i = 0; i < colonists.Length; i++)
        for (int j = i + 1; j < colonists.Length; j++)
        {
            colonists[i].needs.mood.thoughts.memories.TryGainMemory(memory, colonists[j]);
            colonists[j].needs.mood.thoughts.memories.TryGainMemory(memory, colonists[i]);
        }
        if (colonists.Length >= 2)
            Messages.Message("Cultist.AffectionGoodwill".Translate(), MessageTypeDefOf.PositiveEvent);
    }

    private static bool TryFormRelationship(Pawn[] colonists)
    {
        Pawn[] candidates = colonists.Where(pawn => pawn.DevelopmentalStage.Adult()
            && !pawn.Downed && !pawn.InMentalState && !QuestUtility.IsQuestHelper(pawn)
            && !LovePartnerRelationUtility.HasAnyLovePartner(pawn)).ToArray();
        InteractionWorker romance = DefDatabase<InteractionDef>.GetNamed("RomanceAttempt").Worker;
        var pairs = from a in candidates
                    from b in candidates
                    where a.thingIDNumber < b.thingIDNumber && CanFormRelationship(a, b, romance)
                    select (a, b);

        if (!pairs.TryRandomElement(out var pair))
            return false;

        pair.a.relations.AddDirectRelation(PawnRelationDefOf.Lover, pair.b);
        TaleRecorder.RecordTale(TaleDefOf.BecameLover, pair.a, pair.b);
        Find.LetterStack.ReceiveLetter("Cultist.AffectionLoversTitle".Translate(),
            "Cultist.AffectionLovers".Translate(pair.a.LabelShortCap, pair.b.LabelShortCap),
            LetterDefOf.PositiveEvent, new LookTargets(pair.a, pair.b));
        return true;
    }

    private static bool CanFormRelationship(Pawn a, Pawn b, InteractionWorker romance) =>
        !a.relations.FamilyByBlood.Contains(b) && !b.relations.FamilyByBlood.Contains(a)
        && !a.GetRelations(b).Any(relation => relation.incestOpinionOffset < 0f)
        && !LovePartnerRelationUtility.ExLovePartnerRelationExists(a, b)
        && romance.RandomSelectionWeight(a, b) > 0f && romance.RandomSelectionWeight(b, a) > 0f;
}
