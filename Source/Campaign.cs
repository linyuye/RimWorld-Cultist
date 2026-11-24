using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;

namespace Cultist;

public enum Aspect { Lantern, Forge, Edge, Winter, Heart, Grail, Moth, Knock, SecretHistories }
public enum CampaignStage { Waiting, Offered, Reading, Founding, Dreaming, Studied }

[DefOf]
public static class CultistDefs
{
    public static ThingDef Cultist_NightJournal;
    public static ThingDef Cultist_LightPlate;
    public static SitePartDef Cultist_AbandonedObservatory;
    public static SoundDef Cultist_DreamSting;
    public static HediffDef Cultist_ReturnedKeeper;
    public static LetterDef Cultist_JournalOffer;
    public static JobDef Cultist_ReadJournal;
    public static JobDef Cultist_EnterDream;
    public static JobDef Cultist_AltarRite;
    public static JobDef Cultist_DiscussDoctrine;

    static CultistDefs() => DefOfHelper.EnsureInitializedInCtor(typeof(CultistDefs));
}

public sealed partial class Campaign : GameComponent, IThingHolder
{
    private Ideo church;
    private Aspect churchAspect = Aspect.Lantern;
    private int missingSince = -1;
    private CampaignStage stage;
    private Pawn leader;
    private Dictionary<string, Aspect> aspects = new();
    private List<DreamReward> offers;
    private int dreamGate = -1;
    private int dreamRegion = -1;
    private bool recoveredInterpreterNotes;
    internal const int DreamPreparationTicks = 60;

    public Campaign(Game game) => departedKeepers = new ThingOwner<Pawn>(this, true, LookMode.Deep);

    public override void LoadedGame()
    {
        base.LoadedGame();
        EnsureWorldFactionsWithSettlements(notifyIfAdded: true);
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        EnsureWorldFactionsWithSettlements(notifyIfAdded: false);
    }

    public static Campaign Current => Verse.Current.Game?.GetComponent<Campaign>();
    public Aspect ChurchAspect => churchAspect;
    public CultPathDef ActivePath => CultPathDef.ForAspect(churchAspect);
    public CampaignStage Stage => stage;
    public Pawn Leader => leader;
    public int LearnedLoreTier => learnedLoreTier;
    public IReadOnlyList<DreamReward> Offers => offers;
    public int SelectedDreamGate => dreamGate;
    public int SelectedDreamRegion => dreamRegion;

    public override void ExposeData()
    {
        ExposeSociety();
        ExposeChurchRules();
        ExposeExpedition();
        ExposeSeasons();
        ExposeCampaignQuests();
        Scribe_Values.Look(ref missingSince, "missingSince", -1);
        Scribe_Values.Look(ref stage, "stage");
        Scribe_References.Look(ref leader, "leader");
        Scribe_References.Look(ref church, "church");
        Scribe_Values.Look(ref churchAspect, "churchAspect", Aspect.Lantern);
        ExposePersonalDreams();
        ExposeNotoriety();
        ExposeLanguages();
        ExposePatrons();
        ExposeSummons();
        ExposeEvidence();
        ExposeOriflamme();
        ExposeScars();
        ExposeAnomalyEasterEgg();
        ExposeMilestones();
        ExposeRaiderLoot();
        ExposeDoctrines();
        ExposeExalted();
        ExposeApostleLegacy();
        Scribe_Collections.Look(ref aspects, "aspects", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref offers, "dreamOffers", LookMode.Value);
        Scribe_Values.Look(ref dreamGate, "dreamGate", -1);
        Scribe_Values.Look(ref dreamRegion, "dreamRegion", -1);
        Scribe_Values.Look(ref recoveredInterpreterNotes, "recoveredInterpreterNotes");
        ExposeLore();
        Scribe_Values.Look(ref nextDoctrineTalk, "nextDoctrineTalk", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            aspects ??= new Dictionary<string, Aspect>();
        }
    }

    public override void GameComponentTick()
    {
        if (!ModsConfig.IdeologyActive)
            return;
        int now = Find.TickManager.TicksGame;
        TickPersonalDreams(now);
        TickAutoDream(now);
        if (now % 120 == 0)
            TickVigil(now);
        if (now % 250 != 0)
            return;
        TickSeasons(now);
        TickNotoriety(now);
        TickEvidence(now);
        TickPoppy(now);
        TickSummons(now);
        TickAnomalyEasterEgg(now);
        TickRaiderLoadouts();
        TickDoctrineEffects(now);
        TickExaltedTraits(now);
        TickApostleLegacy(now);
        DeliverLore();

        if (now % 2500 == 0)
        {
            foreach (Map map in Find.Maps)
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                GetAspect(pawn);
        }
        TrackLeadership(now);

        TickCampaignQuests(now);
    }

    public override void GameComponentUpdate()
    {
        if (!ModsConfig.IdeologyActive || Find.CurrentMap == null)
            return;
        EnsurePathDoctrine();
        if (stage == CampaignStage.Founding && !Find.WindowStack.IsOpen(typeof(FoundingWindow)))
        {
            if (leader == null || leader.Dead || leader.Faction != Faction.OfPlayer)
            {
                leader = null;
                stage = CampaignStage.Reading;
                return;
            }
            Find.WindowStack.Add(new FoundingWindow(this));
        }
        CheckEndingParticipant();
        if (awaitingEnding && !Find.WindowStack.IsOpen(typeof(EndingWindow)))
            Find.WindowStack.Add(new EndingWindow(this));
        if (offers == null)
            return;
        if (leader == null || leader.Dead || !IsMember(leader))
        {
            CancelDream();
            return;
        }
        if (!Find.WindowStack.IsOpen(typeof(DreamWindow)))
            Find.WindowStack.Add(new DreamWindow(this));
    }

    public Aspect GetAspect(Pawn pawn)
    {
        string id = pawn.GetUniqueLoadID();
        if (!aspects.TryGetValue(id, out Aspect aspect))
        {
            if (pawn.kindDef?.GetModExtension<AspectProfile>() is { } profile)
                aspect = profile.affinity;
            else
            {
                Rand.PushState(GenText.StableStringHash(id));
                try { aspect = (Aspect)Rand.Range(0, 8); }
                finally { Rand.PopState(); }
            }
            aspects.Add(id, aspect);
        }
        return aspect;
    }

    private bool JournalExists()
    {
        return Find.Maps.Any(map =>
                map.listerThings.ThingsOfDef(CultistDefs.Cultist_NightJournal).Count != 0
                || map.mapPawns.FreeColonistsSpawned.Any(CarriesJournal)
                || map.listerThings.ThingsInGroup(ThingRequestGroup.ThingHolder)
                    .Any(item => item is Skyfaller or ActiveTransporter
                        && item is IThingHolder holder && ThingOwnerUtility.GetAllThingsRecursively(holder, true)
                            .Any(cargo => cargo.def == CultistDefs.Cultist_NightJournal)))
            || Find.WorldObjects.Caravans.Any(caravan => caravan.IsPlayerControlled
                && caravan.PawnsListForReading.Any(CarriesJournal));
    }

    private static bool CarriesJournal(Pawn pawn) =>
        pawn.inventory?.innerContainer.Any(thing => thing.def == CultistDefs.Cultist_NightJournal) == true
        || pawn.carryTracker?.CarriedThing?.def == CultistDefs.Cultist_NightJournal;

    internal static void DebugOfferJournal()
    {
        Campaign campaign = Current;
        if (ModsConfig.IdeologyActive && campaign?.stage == CampaignStage.Waiting)
            campaign.OfferJournal();
    }

    private void OfferJournal()
    {
        stage = CampaignStage.Offered;
        openingQuest = CreateCampaignQuest("Cultist.OfferTitle".Translate(), "Cultist.OfferBody".Translate(),
            1, offered: true);
    }

    public bool AcceptJournal()
    {
        if (stage != CampaignStage.Offered)
            return false;
        Map map = Find.Maps.FirstOrDefault(candidate => candidate.IsPlayerHome);
        if (map == null)
            return false;
        if (!JournalExists())
        {
            Thing journal = ThingMaker.MakeThing(CultistDefs.Cultist_NightJournal);
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(map), map, new List<Thing> { journal });
        }
        missingSince = -1;
        stage = CampaignStage.Reading;
        Messages.Message("Cultist.JournalArrived".Translate(), MessageTypeDefOf.PositiveEvent);
        return true;
    }

    public void Read(Pawn reader)
    {
        if (reader == null || reader.Dead || !reader.IsColonistPlayerControlled)
            return;
        if (stage == CampaignStage.Reading)
        {
            leader = reader;
            stage = CampaignStage.Founding;
            return;
        }
        if (CanRefoundSociety)
        {
            // Begin a fresh society in the same save while preserving Long/Guardian Name legacies!
            marks = 0;
            completed = false;
            completedEnding = VigilEnding.None;
            awaitingEnding = false;
            pendingEnding = VigilEnding.None;
            chapterQuestNumber = 0;
            vigilOffered = false;
            ranks.Clear();
            consecratedLockScars.Clear();
            church = null;
            leader = reader;
            stage = CampaignStage.Founding;
        }
    }

    public void Found(Aspect aspect, Aspect societyAspect = Aspect.Lantern)
    {
        if (stage != CampaignStage.Founding || leader == null || leader.Dead)
            return;
        churchAspect = societyAspect;
        CultPathDef path = ActivePath;
        var memes = new List<MemeDef> { DefDatabase<MemeDef>.GetNamed("Structure_Ideological") };
        if (path.memeDef != null)
            memes.Add(path.memeDef);
        church = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(
            null, false, null, null, memes, false, false, false, false,
            path.churchNameKey.Translate().ToString(), null, null, false,
            path.churchDescriptionKey.Translate().ToString(), true));
        Find.IdeoManager.Add(church);
        EnsurePathDoctrine();
        leader.ideo.SetIdeo(church);
        SyncLeaderIdeoRole();
        aspects[leader.GetUniqueLoadID()] = aspect;
        RegisterDreamer(leader);
        stage = CampaignStage.Dreaming;
        EndCampaignQuest(ref openingQuest, QuestEndOutcome.Success);
        SyncChapterQuest();
        Messages.Message("Cultist.Founded".Translate(leader.LabelShortCap),
            leader, MessageTypeDefOf.PositiveEvent);
        Find.WindowStack.Add(new ChurchRulesWindow(church));
    }

    public bool CanDream(Pawn pawn)
    {
        if (stage < CampaignStage.Dreaming || pawn != leader || pawn.Dead || !pawn.Spawned
            || pawn.Downed || pawn.Drafted || offers != null || awaitingEnding
            || VigilInProgress || !IsMember(pawn) || DreamOf(pawn) is not { } state)
            return false;
        int hour = GenLocalDate.HourOfDay(pawn);
        int now = Find.TickManager.TicksGame;
        return (hour >= 18 || hour < 6) &&
            (state.LastTick < 0 || (now - state.LastTick >= GenDate.TicksPerDay / 2
                && state.Night != Night(pawn, hour)));
    }

    private static int Night(Pawn pawn, int hour) => GenLocalDate.Year(pawn) * 60 + GenLocalDate.DayOfYear(pawn)
        - (hour < 6 ? 1 : 0);

    private void TickAutoDream(int now)
    {
        if (LeaderDream is not { } state)
            return;
        if (!state.Auto || !CanDream(leader) || leader.Awake() || leader.CurJobDef != JobDefOf.LayDown
            || leader.CurrentBed() is not { Medical: false } bed || bed.IsForbidden(leader)
            || Toils_Bed.BedNoLongerUsable(leader, bed, false)
            || (state.AutoAt >= 0 && leader.mindState.lastHarmTick >= state.AutoAt - DreamPreparationTicks))
        {
            state.AutoAt = -1;
            return;
        }
        if (state.AutoAt < 0)
            state.AutoAt = now + DreamPreparationTicks;
        else if (now >= state.AutoAt)
        {
            state.AutoAt = -1;
            BeginDream(leader);
        }
    }

    public void BeginDream(Pawn pawn)
    {
        if (!CanDream(pawn))
            return;
        PersonalDream state = DreamOf(pawn);
        state.LastTick = Find.TickManager.TicksGame;
        state.Night = Night(pawn, GenLocalDate.HourOfDay(pawn));
        offers = new List<DreamReward>();
        dreamGate = -1;
        dreamRegion = -1;
        CultistDefs.Cultist_DreamSting.PlayOneShotOnCamera(pawn.Map);
        SendMilestoneLetterOnce("FirstDream", LetterDefOf.NeutralEvent, pawn);
        Find.WindowStack.Add(new DreamWindow(this));
    }

    internal void SelectDreamGate(int gate)
    {
        if (offers is { Count: 0 } && gate >= -1 && gate <= System.Math.Min(Marks, 4))
            dreamGate = gate;
    }

    public void GenerateOffers(int gate, int region)
    {
        if (offers == null || offers.Count != 0 || gate < 0 || gate > System.Math.Min(Marks, 4)
            || region is < 0 or >= DreamTable.RegionCount)
            return;
        dreamGate = gate;
        dreamRegion = region;
        List<DreamReward> pool = DreamTable.Candidates(gate, region, includeUnique: !recoveredInterpreterNotes);
        for (int i = 0; i < 3; i++)
            offers.Add(pool[Rand.Range(0, pool.Count)]);
    }

    private void CancelDream()
    {
        offers = null;
        dreamGate = -1;
        dreamRegion = -1;
        Find.WindowStack.TryRemove(typeof(DreamWindow), doCloseSound: false);
    }

    public void TakeReward(int index)
    {
        if (offers == null || index < 0 || index >= offers.Count || LeaderDream is not { } state)
            return;
        DreamReward reward = offers[index];
        int selectedGate = dreamGate;
        int selectedRegion = dreamRegion;
        int now = Find.TickManager.TicksGame;
        offers = null;
        dreamGate = -1;
        dreamRegion = -1;
        bonusFaceUpCards = 0;
        PlayCultistSound("Cultist_Sfx_MansusExit");
        if (DreamTable.Influence(reward) is { } influence)
        {
            InfluenceDecayDef decay = InfluenceDecayDef.ForAspect(influence.Affinity);
            int duration = decay?.DurationTicks ?? PersonalDream.InfluenceDuration;
            state.Influences.Add(new DreamInfluence
            {
                Affinity = influence.Affinity, Strength = influence.Strength,
                Expires = now + duration
            });
            return;
        }
        int loreTier = DreamTable.LoreTierFor(selectedGate, selectedRegion);
        switch (DreamTable.Effect(reward))
        {
            case DreamReward.Lore:
                GrantLore(state.Owner, 1, loreTier);
                stage = CampaignStage.Studied;
                break;
            case DreamReward.InterpreterNotes:
                if (!recoveredInterpreterNotes)
                {
                    recoveredInterpreterNotes = true;
                    GrantLore(state.Owner, 1, loreTier);
                    if (DefDatabase<AncientLanguageDef>.GetNamedSilentFail("Cultist_Lang_Vak") is { } vak)
                        GrantLanguage(state.Owner, vak);
                    stage = CampaignStage.Studied;
                }
                break;
            case DreamReward.Fascination:
                state.AddRisk(false, now);
                break;
            case DreamReward.Memory:
                state.Memory.Add(now + GenDate.TicksPerDay);
                break;
            case DreamReward.Fear:
                state.AddRisk(true, now);
                break;
            case DreamReward.Comfort:
                state.Comfort.Add(now + GenDate.TicksPerDay);
                break;
        }
    }

}

public sealed class DreamInfluence : IExposable
{
    private int strength;
    private int expires;

    public Aspect Affinity { get; set; }
    public int Strength { get => strength; set => strength = value; }
    public int Expires { get => expires; set => expires = value; }

    public void ExposeData()
    {
        Aspect affinity = Affinity;
        Scribe_Values.Look(ref affinity, "affinity", Aspect.Lantern);
        Affinity = affinity;
        Scribe_Values.Look(ref strength, "strength");
        Scribe_Values.Look(ref expires, "expires");
    }
}
