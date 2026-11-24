using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Cultist;

public enum ScholarDocument { None, Dispatch, Notebook }

public sealed partial class Campaign
{
    private const int WitnessesNeeded = 2;
    private Quest inquiryQuest;
    private int inquiryDeadline = -1;
    private bool inquiryOffered;
    private Quest scholarQuest;
    private Site scholarSite;
    private bool scholarResolved;
    private Quest witnessQuest;
    private bool witnessOffered;
    private HashSet<string> signedWitnesses = new();
    public bool HasWitnesses => signedWitnesses.Count >= WitnessesNeeded;

    private void ExposeFieldQuests()
    {
        Scribe_References.Look(ref inquiryQuest, "inquiryQuest");
        Scribe_Values.Look(ref inquiryDeadline, "inquiryDeadline", -1);
        Scribe_Values.Look(ref inquiryOffered, "inquiryOffered");
        Scribe_References.Look(ref scholarQuest, "scholarQuest");
        Scribe_References.Look(ref scholarSite, "scholarSite");
        Scribe_Values.Look(ref scholarResolved, "scholarResolved");
        Scribe_References.Look(ref witnessQuest, "witnessQuest");
        Scribe_Values.Look(ref witnessOffered, "witnessOffered");
        Scribe_Collections.Look(ref signedWitnesses, "signedWitnesses", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            signedWitnesses ??= new HashSet<string>();
    }

    private void TickFieldQuests(int now)
    {
        if (!completed && marks >= 3 && !inquiryOffered)
        {
            inquiryQuest = CreateCampaignQuest("Cultist.InquiryTitle".Translate(),
                "Cultist.InquiryDescription".Translate(), 1, offered: true);
            inquiryOffered = true;
        }
        if (inquiryQuest?.State == QuestState.Ongoing)
        {
            if (inquiryDeadline < 0)
                inquiryDeadline = now + 3 * GenDate.TicksPerDay;
            if (now >= inquiryDeadline)
            {
                EndCampaignQuest(ref inquiryQuest, QuestEndOutcome.Fail);
                if (leader is { Dead: false } && IsMember(leader))
                    LeaderDream?.AddRisk(true, now);
            }
        }

        if (scholarQuest?.State == QuestState.Ongoing && !ScholarNotebookAvailable())
            EndCampaignQuest(ref scholarQuest, QuestEndOutcome.Fail);

        if (!completed && marks >= 5 && !witnessOffered)
        {
            witnessQuest = CreateCampaignQuest("Cultist.WitnessTitle".Translate(),
                "Cultist.WitnessDescription".Translate(WitnessesNeeded), 1, offered: true);
            witnessOffered = true;
        }
    }

    internal bool CanAnswerInquiry(Pawn pawn) => CanLeadRite(pawn)
        && inquiryQuest?.State == QuestState.Ongoing
        && (inquiryDeadline < 0 || Find.TickManager.TicksGame < inquiryDeadline);

    internal void AnswerInquiry(Pawn pawn, Thing altar, IReadOnlyList<Thing> books)
    {
        if (!CanAnswerInquiry(pawn) || !HasRiteLore(pawn, altar, books, 1))
            return;
        ConsumeLore(books);
        if (LeaderDream is { Fear.Count: > 0 } state)
            PersonalDream.SpendOldest(state.Fear);
        EndCampaignQuest(ref inquiryQuest, QuestEndOutcome.Success);
    }

    internal bool CanSignWitness(Pawn pawn) => !completed && !HasWitnesses && pawn != leader
        && witnessQuest?.State == QuestState.Ongoing && IsMember(pawn)
        && pawn is { IsColonistPlayerControlled: true, Spawned: true, Downed: false, Drafted: false }
        && !signedWitnesses.Contains(pawn.GetUniqueLoadID());

    internal void SignWitness(Pawn pawn)
    {
        if (!CanSignWitness(pawn))
            return;
        signedWitnesses.Add(pawn.GetUniqueLoadID());
        Messages.Message("Cultist.WitnessSigned".Translate(signedWitnesses.Count, WitnessesNeeded),
            pawn, MessageTypeDefOf.PositiveEvent);
        if (HasWitnesses)
            EndCampaignQuest(ref witnessQuest, QuestEndOutcome.Success);
    }

    private void CloseWitnessQuest()
    {
        if (witnessQuest?.State == QuestState.NotYetAccepted)
            witnessQuest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false);
        else
            EndCampaignQuest(ref witnessQuest, QuestEndOutcome.Unknown);
    }

    private bool ScholarNotebookAvailable()
    {
        var notebook = DefDatabase<ThingDef>.GetNamed("Cultist_ScholarNotebook");
        if (scholarSite is { Spawned: true, Destroyed: false })
        {
            Map map = scholarSite.Map;
            if (map == null || map.listerThings.ThingsOfDef(notebook).Count > 0)
                return true;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (CarriedSupply(pawn, notebook) != null)
                    return true;
            }
        }

        return HasAccessibleSupply(notebook);
    }

    internal bool CanReadScholarDocument(Pawn reader, ScholarDocument document) =>
        !scholarResolved && IsMember(reader) && reader is { Spawned: true, Downed: false }
        && (document == ScholarDocument.Dispatch
            ? reader.Map.IsPlayerHome && scholarQuest?.State != QuestState.Ongoing
            : document == ScholarDocument.Notebook && scholarQuest?.State == QuestState.Ongoing);

    internal bool ReadScholarDocument(Pawn reader, ScholarDocument document)
    {
        if (!CanReadScholarDocument(reader, document))
            return false;
        if (document == ScholarDocument.Dispatch)
            return OpenScholarSite(reader);

        GrantLore(reader, 2);
        scholarResolved = true;
        EndCampaignQuest(ref scholarQuest, QuestEndOutcome.Success);
        return true;
    }

    private bool OpenScholarSite(Pawn reader)
    {
        if (!TileFinder.TryFindNewSiteTile(out PlanetTile tile, reader.Map.Tile, 3, 11,
                tileFinderMode: TileFinderMode.Near))
        {
            Messages.Message("Cultist.ExpeditionUnavailable".Translate(), MessageTypeDefOf.RejectInput);
            return false;
        }

        var partDef = DefDatabase<SitePartDef>.GetNamed("Cultist_ScholarShelter");
        Site site = SiteMaker.MakeSite(partDef, tile, null, true);
        SitePart part = site.parts[0];
        part.things ??= new ThingOwner<Thing>(part);
        foreach (string id in new[] { "Cultist_ScholarNotebook", "Cultist_RiteTool2Cracked" })
            part.things.TryAdd(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(id)));

        Find.WorldObjects.Add(site);
        scholarSite = site;
        site.GetComponent<TimeoutComp>()?.StartTimeout(15 * GenDate.TicksPerDay);
        scholarQuest = CreateCampaignQuest("Cultist.ScholarTitle".Translate(),
            "Cultist.ScholarDescription".Translate(), 2, target: site);
        return true;
    }
}
