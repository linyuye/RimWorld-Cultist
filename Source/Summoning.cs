using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Cultist;

public sealed class SummonSpiritDef : Def
{
    public string catalogSourceId;
    public PawnKindDef pawnKindDef;
    public Aspect requiredAspect = Aspect.Lantern;
    public int requiredLoreTier = 1;
    public int requiredInfluence;
    public float durationDays = 2f;
    public float baseBacklashChance = 0.15f;
    public float notorietyGain = 12f;
    public AncientLanguageDef taughtLanguage;
}

public sealed class SummonedSpiritRecord : IExposable
{
    public Pawn Spirit;
    public SummonSpiritDef Def;
    public int ExpiresTick;
    public bool Hostile;

    public void ExposeData()
    {
        Scribe_References.Look(ref Spirit, "spirit");
        Scribe_Defs.Look(ref Def, "def");
        Scribe_Values.Look(ref ExpiresTick, "expiresTick");
        Scribe_Values.Look(ref Hostile, "hostile");
    }
}

public sealed class CultistSpiritCompProperties : CompProperties
{
    public CultistSpiritCompProperties() => compClass = typeof(CultistSpiritComp);
}

public sealed class CultistSpiritComp : ThingComp
{
    public bool Drafted;
    public bool IsBacklash;

    private Pawn SpiritPawn => parent as Pawn;

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Values.Look(ref Drafted, "cultistSpiritDrafted");
        Scribe_Values.Look(ref IsBacklash, "cultistSpiritBacklash");
    }

    public override void CompTickRare()
    {
        base.CompTickRare();
        Pawn pawn = SpiritPawn;
        if (pawn == null || pawn.Dead || !pawn.Spawned)
            return;

        if (IsBacklash && pawn.MentalStateDef != MentalStateDefOf.Berserk)
        {
            pawn.mindState?.mentalStateHandler?.TryStartMentalState(
                MentalStateDefOf.Berserk, forced: true, forceWake: true, causedByMood: false);
        }
        else if (Drafted && !IsBacklash && pawn.Faction == Faction.OfPlayer
            && (pawn.CurJobDef == null || pawn.CurJobDef == JobDefOf.GotoWander))
        {
            pawn.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait_Combat), JobTag.Misc);
        }
    }

    public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
    {
        base.Notify_Killed(prevMap, dinfo);
        Pawn pawn = SpiritPawn;
        if (prevMap != null && pawn != null)
        {
            FleckMaker.ThrowSmoke(pawn.DrawPos, prevMap, 2.0f);
            Campaign.PlayCultistSound("Cultist_Sfx_MansusExit");
        }
        if (pawn?.Corpse is { Destroyed: false } corpse)
            corpse.Destroy(DestroyMode.Vanish);
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        Pawn pawn = SpiritPawn;
        if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Faction != Faction.OfPlayer
            || IsBacklash || pawn.InMentalState)
            yield break;

        yield return new Command_Toggle
        {
            defaultLabel = "Cultist.Spirit.Draft".Translate(),
            defaultDesc = "Cultist.Spirit.DraftDesc".Translate(),
            icon = TexCommand.Draft,
            isActive = () => Drafted,
            toggleAction = () =>
            {
                Drafted = !Drafted;
                if (Drafted)
                    pawn.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wait_Combat), JobTag.Misc);
                else
                    pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            }
        };

        if (!Drafted)
            yield break;

        yield return new Command_Action
        {
            defaultLabel = "Cultist.Spirit.Move".Translate(),
            defaultDesc = "Cultist.Spirit.MoveDesc".Translate(),
            icon = TexCommand.RearmTrap,
            action = () => Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
            {
                if (target.Cell.InBounds(pawn.Map) && target.Cell.Standable(pawn.Map))
                    pawn.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, target.Cell), JobTag.Misc);
            })
        };

        yield return new Command_Action
        {
            defaultLabel = "Cultist.Spirit.Attack".Translate(),
            defaultDesc = "Cultist.Spirit.AttackDesc".Translate(),
            icon = TexCommand.AttackMelee,
            action = () => Find.Targeter.BeginTargeting(TargetingParameters.ForAttackAny(), target =>
            {
                if (target.Thing != null && target.Thing != pawn)
                    pawn.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.AttackMelee, target.Thing), JobTag.Misc);
            })
        };
    }

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
    {
        Pawn spirit = SpiritPawn;
        Campaign campaign = Campaign.Current;
        if (spirit == null || spirit.Dead || !spirit.Spawned || campaign == null
            || !IsBacklash || selPawn != campaign.Leader || !selPawn.IsColonistPlayerControlled)
            yield break;

        yield return new FloatMenuOption(
            "Cultist.Spirit.PacifyOption".Translate(spirit.LabelShortCap),
            () =>
            {
                JobDef pacifyJob = DefDatabase<JobDef>.GetNamed("Cultist_PacifySpirit");
                selPawn.jobs?.TryTakeOrderedJob(JobMaker.MakeJob(pacifyJob, spirit), JobTag.Misc);
            });
    }
}

public sealed class PacifySpiritJobDriver : JobDriver
{
    public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDespawnedOrNull(TargetIndex.A);
        this.FailOn(() => job.targetA.Thing is not Pawn target || target.Dead);
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.Wait(180).WithProgressBarToilDelay(TargetIndex.A);
        yield return Toils_General.Do(() =>
        {
            if (job.targetA.Thing is Pawn spirit)
                Campaign.Current?.PacifyRogueSpirit(pawn, spirit);
        });
    }
}

public sealed partial class Campaign
{
    private List<SummonedSpiritRecord> activeSummons = new();

    public IReadOnlyList<SummonedSpiritRecord> ActiveSummons => activeSummons;

    private void ExposeSummons()
    {
        Scribe_Collections.Look(ref activeSummons, "activeSummons", LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            activeSummons ??= new List<SummonedSpiritRecord>();
    }

    private void TickSummons(int now)
    {
        for (int i = activeSummons.Count - 1; i >= 0; i--)
        {
            SummonedSpiritRecord rec = activeSummons[i];
            if (rec?.Spirit == null || rec.Spirit.Destroyed)
            {
                activeSummons.RemoveAt(i);
                continue;
            }
            if (rec.Spirit.Dead)
            {
                if (rec.Spirit.Corpse is { Destroyed: false } corpse)
                {
                    if (corpse.Spawned)
                        FleckMaker.ThrowSmoke(corpse.DrawPos, corpse.Map, 1.8f);
                    corpse.Destroy(DestroyMode.Vanish);
                }
                activeSummons.RemoveAt(i);
                continue;
            }
            if (now >= rec.ExpiresTick)
                DissipateSpirit(rec, i, silent: false);
        }
    }

    public void DismissSpirit(SummonedSpiritRecord rec)
    {
        int idx = activeSummons.IndexOf(rec);
        if (idx >= 0)
            DissipateSpirit(rec, idx, silent: false);
    }

    private void DissipateSpirit(SummonedSpiritRecord rec, int index, bool silent)
    {
        activeSummons.RemoveAt(index);
        if (rec.Spirit is { Spawned: true } pawn)
        {
            FleckMaker.ThrowSmoke(pawn.DrawPos, pawn.Map, 1.5f);
            PlayCultistSound("Cultist_Sfx_MansusExit");
            if (!silent)
                Messages.Message("Cultist.Summon.Dissipated".Translate(rec.Def?.LabelCap ?? pawn.LabelShortCap),
                    new LookTargets(pawn.Position, pawn.Map), MessageTypeDefOf.NeutralEvent);
            pawn.Destroy(DestroyMode.Vanish);
        }
    }

    public void PacifyRogueSpirit(Pawn pacifyingLeader, Pawn spirit)
    {
        if (pacifyingLeader == null || spirit == null || spirit.Dead)
            return;
        CultistSpiritComp comp = spirit.TryGetComp<CultistSpiritComp>();
        if (comp == null || !comp.IsBacklash)
            return;

        comp.IsBacklash = false;
        spirit.mindState?.mentalStateHandler?.Reset();
        if (spirit.Faction != Faction.OfPlayer)
            spirit.SetFaction(Faction.OfPlayer);
        foreach (SummonedSpiritRecord rec in activeSummons)
        {
            if (rec.Spirit == spirit)
                rec.Hostile = false;
        }

        // Random pacification backlash cost on leader
        int now = Find.TickManager.TicksGame;
        float roll = Rand.Value;
        string costText;
        if (roll < 0.25f)
        {
            LeaderDream?.AddRisk(true, now);
            costText = "Cultist.Pacify.CostFear".Translate();
        }
        else if (roll < 0.50f)
        {
            LeaderDream?.AddRisk(false, now);
            costText = "Cultist.Pacify.CostFascination".Translate();
        }
        else if (roll < 0.70f)
        {
            AddNotoriety(10f);
            costText = "Cultist.Pacify.CostNotoriety".Translate();
        }
        else if (roll < 0.90f)
        {
            HediffDef disease = InflictRandomCurableDisease(pacifyingLeader);
            costText = disease != null
                ? "Cultist.Pacify.CostDisease".Translate(disease.LabelCap)
                : "Cultist.Pacify.CostNotoriety".Translate();
            if (disease == null) AddNotoriety(10f);
        }
        else
        {
            LeaderDream?.Comfort.Add(now + GenDate.TicksPerDay);
            costText = "Cultist.Pacify.CostComfort".Translate();
        }

        // Fatigue / Exhaustion scaled by spirit combat power
        string fatigueDefName = spirit.kindDef.combatPower >= 150f
            ? "Cultist_SpiritExhaustion"
            : "Cultist_SpiritFatigue";
        if (DefDatabase<HediffDef>.GetNamedSilentFail(fatigueDefName) is { } fatigueDef
            && pacifyingLeader.health != null)
        {
            pacifyingLeader.health.AddHediff(fatigueDef);
        }

        PlayCultistSound("Cultist_Sfx_RiteComplete");
        Messages.Message("Cultist.Spirit.Pacified".Translate(pacifyingLeader.LabelShortCap, spirit.LabelShortCap, costText),
            spirit, MessageTypeDefOf.PositiveEvent);
    }

    public float CalculateBacklashChance(Pawn summoner, Thing altar, SummonSpiritDef spirit)
    {
        float chance = spirit.baseBacklashChance;
        if (DreamOf(summoner) is { } dream)
        {
            if (dream.Fascination.Count >= 2) chance += 0.15f;
            if (dream.Fear.Count >= 2) chance += 0.15f;
        }
        HqTrait hq = EvaluateHqTraits(altar);
        if (hq.HasFlag(HqTrait.Sanctuary)) chance -= 0.08f;
        if (hq.HasFlag(HqTrait.Pit)) chance -= 0.08f;
        if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Scar_Knock") is { } knockScar
            && summoner?.health?.hediffSet?.HasHediff(knockScar) == true)
            chance -= 0.06f;
        if (hq.HasFlag(HqTrait.Foundry) && spirit.requiredAspect == Aspect.Forge)
            chance -= 0.08f;
        return System.Math.Clamp(chance, 0.02f, 0.85f);
    }

    internal bool SpawnTemporaryAlliedSpirit(Map map, IntVec3 pos, string summonDefName, int durationTicks, string customTitle = null)
    {
        if (map == null || DefDatabase<SummonSpiritDef>.GetNamedSilentFail(summonDefName) is not { pawnKindDef: { } kind } spiritDef)
            return false;
        Pawn spawned = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
        if (!string.IsNullOrEmpty(customTitle))
            spawned.Name = new NameSingle(customTitle);
        IntVec3 spawnCell = pos.InBounds(map) && pos.Standable(map) ? pos : CellFinder.RandomClosewalkCellNear(pos, map, 4);
        GenSpawn.Spawn(spawned, spawnCell, map, WipeMode.Vanish);
        FleckMaker.ThrowSmoke(spawned.DrawPos, map, 2.0f);
        activeSummons.Add(new SummonedSpiritRecord
        {
            Spirit = spawned,
            Def = spiritDef,
            ExpiresTick = Find.TickManager.TicksGame + durationTicks,
            Hostile = false
        });
        return true;
    }

    public bool TrySummonSpirit(Pawn summoner, Thing altar, SummonSpiritDef spirit)
    {
        if (summoner == null || altar is not { Spawned: true } || spirit?.pawnKindDef == null)
            return false;
        if (learnedLoreTier < spirit.requiredLoreTier)
        {
            Messages.Message("Cultist.Summon.NeedLoreTier".Translate(spirit.requiredLoreTier),
                altar, MessageTypeDefOf.RejectInput);
            return false;
        }
        int availInfluence = System.Math.Max(MaxInfluence(spirit.requiredAspect), Influence);
        if (spirit.requiredInfluence > 0 && availInfluence < spirit.requiredInfluence)
        {
            Messages.Message("Cultist.Summon.NeedInfluence".Translate(spirit.requiredInfluence),
                altar, MessageTypeDefOf.RejectInput);
            return false;
        }

        Thing loreBook = NearbyMaterials(summoner, altar)
            .FirstOrDefault(t => t.TryGetComp<RitualMaterial>()?.LoreTier > 0);
        if (loreBook == null)
        {
            Messages.Message("Cultist.NeedRiteBooks".Translate(1, RiteRadius),
                altar, MessageTypeDefOf.RejectInput);
            return false;
        }

        loreBook.Destroy();
        if (spirit.requiredInfluence > 0)
            ConsumeInfluence(spirit.requiredInfluence);
        AddNotoriety(spirit.notorietyGain, altar);

        float backlash = CalculateBacklashChance(summoner, altar, spirit);
        bool hostile = Rand.Chance(backlash);

        Pawn spawned = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
            spirit.pawnKindDef, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
            forceGenerateNewPawn: true, allowDead: false, allowDowned: false));
        GenSpawn.Spawn(spawned, altar.Position, altar.Map, WipeMode.Vanish);
        FleckMaker.ThrowSmoke(spawned.DrawPos, altar.Map, 1.8f);

        activeSummons.Add(new SummonedSpiritRecord
        {
            Spirit = spawned,
            Def = spirit,
            ExpiresTick = Find.TickManager.TicksGame + (int)(spirit.durationDays * GenDate.TicksPerDay),
            Hostile = hostile
        });

        if (hostile)
        {
            if (spawned.TryGetComp<CultistSpiritComp>() is { } comp)
                comp.IsBacklash = true;
            spawned.mindState?.mentalStateHandler?.TryStartMentalState(
                MentalStateDefOf.Berserk, forced: true, forceWake: true, causedByMood: false);
            PlayCultistSound("Cultist_Sfx_Backlash");
            Find.LetterStack.ReceiveLetter(
                "Cultist.Summon.BacklashTitle".Translate(spirit.LabelCap),
                "Cultist.Summon.BacklashBody".Translate(summoner.LabelShortCap, spirit.LabelCap),
                LetterDefOf.ThreatBig, spawned);
        }
        else
        {
            PlayCultistSound("Cultist_Sfx_SummonSuccess");
            SendMilestoneLetterOnce("FirstSummon", LetterDefOf.NeutralEvent, spawned);
            Messages.Message("Cultist.Summon.Success".Translate(summoner.LabelShortCap, spirit.LabelCap),
                spawned, MessageTypeDefOf.PositiveEvent);
        }
        return true;
    }

    internal static void PlayCultistSound(string soundDefName)
    {
        if (DefDatabase<SoundDef>.GetNamedSilentFail(soundDefName) is { } sound)
            sound.PlayOneShotOnCamera();
    }
}

public sealed class SummonWindow : Window
{
    private readonly Campaign campaign;
    private readonly Pawn summoner;
    private readonly Thing altar;
    private Vector2 scroll;

    public SummonWindow(Campaign campaign, Pawn summoner, Thing altar)
    {
        this.campaign = campaign;
        this.summoner = summoner;
        this.altar = altar;
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(680, 580);

    public override void DoWindowContents(Rect inRect)
    {
        Widgets.Label(inRect.TopPartPixels(30f), "Cultist.Summon.WindowTitle".Translate());
        var listRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 92f);
        var spirits = DefDatabase<SummonSpiritDef>.AllDefsListForReading;
        var active = campaign.ActiveSummons.Where(s => s?.Spirit != null && !s.Spirit.Dead).ToList();
        float totalHeight = active.Count * 66f + spirits.Count * 116f + 40f;
        var viewRect = new Rect(0f, 0f, listRect.width - 18f, totalHeight);

        Widgets.BeginScrollView(listRect, ref scroll, viewRect);
        float y = 0f;
        int now = Find.TickManager.TicksGame;

        foreach (SummonedSpiritRecord rec in active)
        {
            var card = new Rect(0f, y, viewRect.width, 58f);
            Widgets.DrawBoxSolid(card, rec.Hostile ? new Color(0.32f, 0.12f, 0.12f, 0.85f) : new Color(0.12f, 0.24f, 0.22f, 0.85f));
            Rect inner = card.ContractedBy(8f);
            float hours = System.Math.Max(0f, (rec.ExpiresTick - now) / (float)GenDate.TicksPerHour);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width * 0.52f, 24f),
                "Cultist.Summon.ActiveRow".Translate(rec.Def.LabelCap, hours.ToString("0.0")));

            if (!rec.Hostile && rec.Def.taughtLanguage != null && !campaign.KnowsLanguage(summoner, rec.Def.taughtLanguage))
            {
                if (Widgets.ButtonText(new Rect(inner.x + inner.width * 0.54f, inner.y, inner.width * 0.26f, 28f),
                        "Cultist.Summon.LearnLang".Translate(rec.Def.taughtLanguage.LabelCap)))
                    campaign.GrantLanguage(summoner, rec.Def.taughtLanguage);
            }
            if (!rec.Hostile && Widgets.ButtonText(new Rect(inner.x + inner.width * 0.82f, inner.y, inner.width * 0.18f, 28f),
                    "Cultist.Summon.Dismiss".Translate()))
            {
                campaign.DismissSpirit(rec);
                break;
            }
            y += 66f;
        }

        foreach (SummonSpiritDef spirit in spirits)
        {
            var card = new Rect(0f, y, viewRect.width, 108f);
            Widgets.DrawBoxSolid(card, new Color(0.14f, 0.15f, 0.18f, 0.85f));
            Rect inner = card.ContractedBy(8f);
            float backlash = campaign.CalculateBacklashChance(summoner, altar, spirit);
            string header = $"{spirit.LabelCap}  ({("Cultist.Aspect." + spirit.requiredAspect).Translate()})";
            if (spirit.taughtLanguage != null)
                header += "  [" + "Cultist.Book.TeachesLang".Translate(spirit.taughtLanguage.LabelCap) + "]";
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), header);
            Widgets.Label(new Rect(inner.x, inner.y + 24f, inner.width, 36f), spirit.description);

            string reqText = "Cultist.Summon.ReqSummary".Translate(
                spirit.requiredLoreTier, spirit.requiredInfluence, backlash.ToStringPercent(), spirit.durationDays.ToString("0.0"));
            Widgets.Label(new Rect(inner.x, inner.y + 64f, inner.width * 0.68f, 28f), reqText);

            if (Widgets.ButtonText(new Rect(inner.x + inner.width * 0.70f, inner.y + 62f, inner.width * 0.30f, 30f),
                    "Cultist.Summon.Button".Translate()))
            {
                if (campaign.TrySummonSpirit(summoner, altar, spirit))
                    Close();
            }
            y += 116f;
        }

        Widgets.EndScrollView();
    }
}
