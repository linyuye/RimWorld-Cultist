using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public enum ApostleStage { None, Offered, Active, Completed }

public sealed class ApostleOfferLetter : ChoiceLetter
{
    public override IEnumerable<DiaOption> Choices
    {
        get
        {
            yield return new DiaOption("Cultist.Apostle.Accept".Translate())
            {
                resolveTree = true,
                action = () =>
                {
                    if (Campaign.Current?.AcceptApostleCall() == true)
                        Find.LetterStack.RemoveLetter(this);
                }
            };
            yield return new DiaOption("Cultist.Later".Translate()) { resolveTree = true };
        }
    }
}

public sealed partial class Campaign
{
    private ApostleStage apostleStage;
    private Aspect apostleAspect = Aspect.Lantern;
    private int apostleOfferTick = -1;
    private Pawn apostlePawn;
    private Quest apostleQuest;
    private int apostleVigilUntil = -1;
    private Thing apostleAltar;

    private bool guardianNameActive;
    private Aspect guardianNameAspect = Aspect.Lantern;
    private string guardianNameTitle = "";
    private int guardianBoonCooldownUntil;

    public ApostleStage CurrentApostleStage => apostleStage;
    public Aspect ApostleAspect => apostleAspect;
    public bool ApostleVigilInProgress => apostleVigilUntil >= 0;
    public bool GuardianNameActive => guardianNameActive;
    public Aspect GuardianNameAspect => guardianNameAspect;
    public string GuardianNameTitle => guardianNameTitle;

    private void ExposeApostleLegacy()
    {
        Scribe_Values.Look(ref apostleStage, "apostleStage");
        Scribe_Values.Look(ref apostleAspect, "apostleAspect", Aspect.Lantern);
        Scribe_Values.Look(ref apostleOfferTick, "apostleOfferTick", -1);
        Scribe_References.Look(ref apostlePawn, "apostlePawn");
        Scribe_References.Look(ref apostleQuest, "apostleQuest");
        Scribe_Values.Look(ref apostleVigilUntil, "apostleVigilUntil", -1);
        Scribe_References.Look(ref apostleAltar, "apostleAltar");
        Scribe_Values.Look(ref guardianNameActive, "guardianNameActive");
        Scribe_Values.Look(ref guardianNameAspect, "guardianNameAspect", Aspect.Lantern);
        Scribe_Values.Look(ref guardianNameTitle, "guardianNameTitle", "");
        Scribe_Values.Look(ref guardianBoonCooldownUntil, "guardianBoonCooldownUntil");
    }

    internal void ScheduleApostleCallIfEligible(Aspect aspect)
    {
        if (aspect is not (Aspect.Lantern or Aspect.Forge or Aspect.Grail))
            return;
        if (apostleStage != ApostleStage.None)
            return;
        apostleAspect = aspect;
        apostleOfferTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 3;
    }

    private void TickApostleLegacy(int now)
    {
        if (apostleStage == ApostleStage.None && apostleOfferTick > 0 && now >= apostleOfferTick)
        {
            apostleOfferTick = -1;
            apostleStage = ApostleStage.Offered;
            LetterDef ld = DefDatabase<LetterDef>.GetNamedSilentFail("Cultist_ApostleOfferLetter") ?? LetterDefOf.PositiveEvent;
            Find.LetterStack.ReceiveLetter(
                "Cultist.Apostle.OfferTitle".Translate(("Cultist.Aspect." + apostleAspect).Translate()),
                "Cultist.Apostle.OfferBody".Translate(ascendedLongName, ("Cultist.Aspect." + apostleAspect).Translate()),
                ld);
        }

        if (ApostleVigilInProgress)
        {
            if (apostleAltar is not { Spawned: true } || leader is not { Spawned: true, Dead: false }
                || leader.Map != apostleAltar.Map)
            {
                apostleVigilUntil = -1;
                apostleAltar = null;
                Messages.Message("Cultist.VigilFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                return;
            }
            if (now >= apostleVigilUntil && !leader.Downed && !GenHostility.AnyHostileActiveThreatToPlayer(apostleAltar.Map))
                CompleteApostleGreatWork();
        }
    }

    internal bool AcceptApostleCall()
    {
        if (apostleStage != ApostleStage.Offered)
            return false;
        Map map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        if (map == null)
            return false;

        Pawn template = departedKeepers?.InnerListForReading.LastOrDefault();
        Pawn apostle = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
            fixedGender: template?.gender, fixedIdeo: church));

        if (template != null)
            CloneLeaderTemplate(template, apostle);

        if (apostle.Name is NameTriple nt)
        {
            string prefix = $"Cultist.Apostle.Prefix.{apostleAspect}".Translate();
            apostle.Name = new NameTriple(nt.First, $"{prefix}{nt.Nick}", nt.Last);
        }

        IntVec3 spawnCell = DropCellFinder.TradeDropSpot(map);
        if (!spawnCell.IsValid || !spawnCell.Standable(map))
            spawnCell = CellFinder.RandomEdgeCell(map);
        GenSpawn.Spawn(apostle, spawnCell, map, WipeMode.Vanish);

        if (church != null && apostle.ideo != null)
            apostle.ideo.SetIdeo(church);
        leader = apostle;
        apostlePawn = apostle;
        aspects[apostle.GetUniqueLoadID()] = apostleAspect;
        RegisterDreamer(apostle);
        SyncLeaderIdeoRole();
        apostleStage = ApostleStage.Active;

        apostleQuest = CreateCampaignQuest(
            $"Cultist.Apostle.QuestTitle.{apostleAspect}".Translate(),
            $"Cultist.Apostle.QuestDesc.{apostleAspect}".Translate(ascendedLongName),
            4);

        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Apostle.Joined".Translate(apostle.LabelShortCap, ascendedLongName),
            apostle, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    private static void CloneLeaderTemplate(Pawn src, Pawn dst)
    {
        if (src.story != null && dst.story != null)
        {
            dst.story.bodyType = src.story.bodyType;
            dst.story.traits.allTraits.Clear();
            foreach (Trait t in src.story.traits.allTraits)
            {
                if (!dst.story.traits.HasTrait(t.def))
                    dst.story.traits.GainTrait(new Trait(t.def, t.Degree, forced: true));
            }
        }
        if (src.skills != null && dst.skills != null)
        {
            foreach (SkillRecord s in src.skills.skills)
            {
                SkillRecord target = dst.skills.GetSkill(s.def);
                if (target == null)
                    continue;
                target.Level = s.Level;
                target.passion = s.passion;
            }
        }
    }

    private static Thing FindNearbyDef(Pawn pawn, Thing site, string defName)
    {
        if (site is not { Spawned: true } || site.Map == null)
            return null;
        ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        if (def == null)
            return null;
        return site.Map.listerThings.ThingsOfDef(def)
            .FirstOrDefault(t => t.Spawned && !t.IsForbidden(pawn) && t.Position.InHorDistOf(site.Position, RiteRadius));
    }

    internal TaggedString ApostleGreatWorkRequirement(Pawn pawn, Thing site)
    {
        if (apostleStage != ApostleStage.Active || ApostleVigilInProgress)
            return TaggedString.Empty;
        if (site is not { Spawned: true } || GenHostility.AnyHostileActiveThreatToPlayer(site.Map))
            return "Cultist.VigilUnavailable".Translate();

        ThingDef lore7 = CultPathDef.LoreDefFor(apostleAspect, 7);
        bool hasLore7 = FindNearbyDef(pawn, site, lore7.defName) != null;
        if (!hasLore7)
            return "Cultist.Apostle.NeedLore7".Translate(lore7.LabelCap, RiteRadius);

        switch (apostleAspect)
        {
            case Aspect.Lantern:
                if (FindNearbyDef(pawn, site, "Cultist_RiteTool3Ready") == null)
                    return "Cultist.Apostle.NeedMirror3".Translate(RiteRadius);
                if (MaxInfluence(Aspect.Lantern) < 15)
                    return "Cultist.Apostle.NeedInfluence".Translate(15, ("Cultist.Aspect." + Aspect.Lantern).Translate());
                if (site.Map.mapPawns.FreeColonistsSpawned.Count(IsMember) < 3)
                    return "Cultist.Apostle.NeedMembers".Translate(3);
                break;

            case Aspect.Forge:
                if (FindNearbyDef(pawn, site, "Cultist_DawnbreakerCore") == null)
                    return "Cultist.Apostle.NeedRelic".Translate(
                        DefDatabase<ThingDef>.GetNamed("Cultist_DawnbreakerCore").LabelCap, RiteRadius);
                bool hasCrucible = activeSummons.Any(s => s.Def?.defName == "Cultist_Summon_KingCrucible" && s.Spirit is { Spawned: true, Dead: false } && !s.Hostile);
                if (!hasCrucible && !(MaxInfluence(Aspect.Forge) >= 10 && EvaluateHqTraits(site).HasFlag(HqTrait.Foundry)))
                    return "Cultist.Apostle.NeedForgeSupport".Translate();
                break;

            case Aspect.Grail:
                if (FindNearbyDef(pawn, site, "Cultist_VitulationChalice") == null)
                    return "Cultist.Apostle.NeedRelic".Translate(
                        DefDatabase<ThingDef>.GetNamed("Cultist_VitulationChalice").LabelCap, RiteRadius);
                bool hasEzeem = activeSummons.Any(s => s.Def?.defName == "Cultist_Summon_Ezeem" && s.Spirit is { Spawned: true, Dead: false } && !s.Hostile);
                bool hasExalted = site.Map.mapPawns.FreeColonistsSpawned.Any(p => IsMember(p) && Rank(p) >= 3);
                if (!hasEzeem && !(MaxInfluence(Aspect.Grail) >= 10 && hasExalted))
                    return "Cultist.Apostle.NeedGrailSupport".Translate();
                break;
        }
        return TaggedString.Empty;
    }

    internal void BeginApostleGreatWork(Pawn pawn, Thing site)
    {
        if (apostleStage != ApostleStage.Active || ApostleVigilInProgress || pawn != leader)
            return;
        TaggedString req = ApostleGreatWorkRequirement(pawn, site);
        if (!req.NullOrEmpty())
        {
            Messages.Message(req, site, MessageTypeDefOf.RejectInput);
            return;
        }

        ThingDef lore7 = CultPathDef.LoreDefFor(apostleAspect, 7);
        FindNearbyDef(pawn, site, lore7.defName)?.Destroy();
        if (apostleAspect == Aspect.Forge)
            FindNearbyDef(pawn, site, "Cultist_DawnbreakerCore")?.Destroy();
        else if (apostleAspect == Aspect.Grail)
            FindNearbyDef(pawn, site, "Cultist_VitulationChalice")?.Destroy();

        Map map = site.Map;
        bool raidStarted = TryFireFactionRaid(map, "Cultist_SuppressionBureau",
            "Cultist.Apostle.RaidTitle".Translate(), "Cultist.Apostle.RaidBody".Translate(), 1.10f);

        IncidentParms wave2 = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
        wave2.faction = EnsureFaction("Cultist_RivalLongFaction") ?? wave2.faction;
        wave2.points = System.Math.Max(220f, wave2.points * 1.15f);
        Find.Storyteller.incidentQueue.Add(IncidentDefOf.RaidEnemy, Find.TickManager.TicksGame + GenDate.TicksPerHour * 4, wave2);

        if (apostleAspect == Aspect.Forge && DefDatabase<GameConditionDef>.GetNamedSilentFail("HeatWave") is { } heat)
            map.gameConditionManager.RegisterCondition(GameConditionMaker.MakeCondition(heat, GenDate.TicksPerDay / 2));
        else if (apostleAspect == Aspect.Grail)
            LeaderDream?.AddRisk(false, Find.TickManager.TicksGame);

        apostleAltar = site;
        apostleVigilUntil = Find.TickManager.TicksGame + GenDate.TicksPerHour * 8;
        PlayCultistSound("Cultist_Sfx_MansusEnter");
        if (!raidStarted)
            Messages.Message("Cultist.Apostle.VigilStarted".Translate(), site, MessageTypeDefOf.ThreatBig);
    }

    private void CompleteApostleGreatWork()
    {
        apostleVigilUntil = -1;
        apostleAltar = null;
        apostleStage = ApostleStage.Completed;
        EndCampaignQuest(ref apostleQuest, QuestEndOutcome.Success);

        // Apostle remains in the colony as the Guardian Name's Earthly Envoy!
        Pawn envoy = apostlePawn is { Dead: false } ? apostlePawn : leader;
        if (envoy != null && DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_NameEnvoy") is { } envoyHd
            && !envoy.health.hediffSet.HasHediff(envoyHd))
        {
            envoy.health.AddHediff(envoyHd);
        }

        guardianNameActive = true;
        guardianNameAspect = apostleAspect;
        guardianNameTitle = string.IsNullOrEmpty(ascendedLongName)
            ? ("Cultist.Aspect." + apostleAspect).Translate().ToString()
            : ascendedLongName;

        string title = $"Cultist.Apostle.VictoryTitle.{apostleAspect}".Translate();
        string body = $"Cultist.Apostle.VictoryBody.{apostleAspect}".Translate(guardianNameTitle, envoy?.LabelShortCap ?? "");
        PlayCultistSound("Cultist_Sfx_EndingGlory");
        Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent);
        Find.WindowStack.Add(new VictoryCreditsWindow(
            "Cultist.Victory.NameSubtitle".Translate(("Cultist.Aspect." + apostleAspect).Translate(), guardianNameTitle),
            title,
            body));
    }

    internal bool CanInvokeGuardianBoon =>
        guardianNameActive && Find.TickManager.TicksGame >= guardianBoonCooldownUntil;

    internal int GuardianBoonDaysLeft =>
        guardianBoonCooldownUntil <= Find.TickManager.TicksGame
            ? 0
            : System.Math.Max(1, (guardianBoonCooldownUntil - Find.TickManager.TicksGame + GenDate.TicksPerDay - 1) / GenDate.TicksPerDay);

    internal void InvokeGuardianNameAvatar(Pawn caller, Thing site)
    {
        if (!CanInvokeGuardianBoon || site?.Map == null)
            return;
        string spiritDefName = guardianNameAspect switch
        {
            Aspect.Forge => "Cultist_Summon_KingCrucible",
            Aspect.Grail => "Cultist_Summon_Ezeem",
            _ => "Cultist_Summon_Teresa"
        };
        string title = "Cultist.Guardian.AvatarName".Translate(("Cultist.Aspect." + guardianNameAspect).Translate(), guardianNameTitle);
        if (SpawnTemporaryAlliedSpirit(site.Map, site.Position, spiritDefName, GenDate.TicksPerHour * 12, title))
        {
            guardianBoonCooldownUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay * 10;
            PlayCultistSound("Cultist_Sfx_EndingGlory");
            Messages.Message("Cultist.Guardian.AvatarArrived".Translate(title), site, MessageTypeDefOf.PositiveEvent);
        }
    }

    internal void InvokeGuardianNameBlessing(Pawn caller, Thing site)
    {
        if (!CanInvokeGuardianBoon || site?.Map == null)
            return;
        HediffDef hd = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_GuardianBlessing");
        ThoughtDef td = DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_GuardianBlessing");
        foreach (Pawn p in site.Map.mapPawns.FreeColonistsSpawned)
        {
            if (hd != null) p.health?.AddHediff(hd);
            if (td != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(td);
        }
        if (DefDatabase<ThingDef>.GetNamedSilentFail("Cultist_Spintria") is { } spintria)
        {
            Thing coins = ThingMaker.MakeThing(spintria);
            coins.stackCount = 3;
            DropPodUtility.DropThingsNear(DropCellFinder.TradeDropSpot(site.Map), site.Map, new List<Thing> { coins });
        }
        guardianBoonCooldownUntil = Find.TickManager.TicksGame + GenDate.TicksPerDay * 10;
        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Guardian.BlessingGranted".Translate(guardianNameTitle), site, MessageTypeDefOf.PositiveEvent);
    }
}
