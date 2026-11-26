using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Cultist;

public sealed class AspectRequirement
{
    public Aspect aspect = Aspect.Lantern;
    public int amount = 2;
}

public sealed class SummonSpiritDef : Def
{
    public string catalogSourceId;
    public PawnKindDef pawnKindDef;
    public Aspect requiredAspect = Aspect.Lantern;
    public int requiredLoreTier = 1;
    public int requiredInfluence;
    public List<AspectRequirement> requiredAspects = new();
    public float durationDays = 2f;
    public float baseBacklashChance = 0.15f;
    public float notorietyGain = 12f;
    public AncientLanguageDef taughtLanguage;

    public IReadOnlyList<AspectRequirement> EffectiveRequirements
    {
        get
        {
            if (requiredAspects is { Count: > 0 })
                return requiredAspects;
            return new[]
            {
                new AspectRequirement { aspect = requiredAspect, amount = System.Math.Max(2, requiredLoreTier * 2) },
                new AspectRequirement { aspect = Aspect.Knock, amount = 2 }
            };
        }
    }
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

    public sealed class RiteEvaluation
    {
        public Dictionary<Aspect, int> Totals = new();
        public Aspect? SacrificeFilledAspect;
        public int SacrificeFilledAmount;
        public bool AllMet;
    }

    public RiteEvaluation EvaluateRiteSummon(SummonSpiritDef spirit, CultistRiteDef rite,
        Thing loreBook, Thing tool, DreamInfluence influence, Pawn assistant, Pawn sacrifice)
    {
        var eval = new RiteEvaluation();
        if (spirit == null || rite == null)
            return eval;

        void Add(Aspect asp, int amt)
        {
            if (amt <= 0)
                return;
            eval.Totals[asp] = eval.Totals.GetValueOrDefault(asp) + amt;
        }

        if (rite.allowLore && loreBook?.TryGetComp<RitualMaterial>() is { LoreTier: > 0 } loreMat)
            Add(loreMat.Aspect, loreMat.LoreTier * 2);
        if (rite.allowTool && tool?.TryGetComp<RitualMaterial>() is { ToolLight: > 0 } toolMat)
            Add(toolMat.Aspect, toolMat.ToolLight);
        if (rite.allowInfluence && influence != null)
            Add(influence.Affinity, influence.Strength);
        if (rite.allowAssistant && assistant != null && !assistant.Dead)
            Add(GetAspect(assistant), System.Math.Max(2, Rank(assistant) * 3));

        var reqs = spirit.EffectiveRequirements;
        // Sacrifice auto-fills the FIRST unmet aspect requirement completely to its needed threshold
        if (rite.allowSacrifice && sacrifice != null && !sacrifice.Dead)
        {
            for (int i = 0; i < reqs.Count; i++)
            {
                AspectRequirement req = reqs[i];
                int have = eval.Totals.GetValueOrDefault(req.aspect);
                if (have < req.amount)
                {
                    int fill = req.amount - have;
                    Add(req.aspect, fill);
                    eval.SacrificeFilledAspect = req.aspect;
                    eval.SacrificeFilledAmount = fill;
                    break;
                }
            }
        }

        eval.AllMet = true;
        for (int i = 0; i < reqs.Count; i++)
        {
            AspectRequirement req = reqs[i];
            if (eval.Totals.GetValueOrDefault(req.aspect) < req.amount)
            {
                eval.AllMet = false;
                break;
            }
        }
        return eval;
    }

    public bool TrySummonSpiritWithRite(Pawn summoner, Thing altar, SummonSpiritDef spirit, CultistRiteDef rite,
        Thing loreBook, Thing tool, DreamInfluence influence, Pawn assistant, Pawn sacrifice)
    {
        if (summoner == null || altar is not { Spawned: true } || spirit?.pawnKindDef == null || rite == null)
            return false;

        RiteEvaluation eval = EvaluateRiteSummon(spirit, rite, loreBook, tool, influence, assistant, sacrifice);
        if (!eval.AllMet)
        {
            Messages.Message("Cultist.Rite.AspectsNotMet".Translate(), altar, MessageTypeDefOf.RejectInput);
            return false;
        }

        if (rite.allowLore && rite.consumeLore && loreBook is { Destroyed: false })
            loreBook.Destroy();
        if (rite.allowInfluence && rite.consumeInfluence && influence != null)
            LeaderDream?.Influences?.Remove(influence);
        if (rite.allowSacrifice && sacrifice != null && !sacrifice.Dead)
        {
            string victimName = sacrifice.LabelShortCap;
            sacrifice.Kill(null);
            AddNotoriety(12f, altar, summoner);
            Messages.Message("Cultist.Rite.Sacrificed".Translate(victimName), altar, MessageTypeDefOf.NeutralEvent);
        }

        AddNotoriety(spirit.notorietyGain, altar, summoner);

        float backlash = System.Math.Clamp(CalculateBacklashChance(summoner, altar, spirit) + rite.backlashModifier, 0.02f, 0.85f);
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

    internal IReadOnlyList<DreamInfluence> ActiveLeaderInfluences =>
        LeaderDream?.Influences ?? (IReadOnlyList<DreamInfluence>)System.Array.Empty<DreamInfluence>();

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
    private Vector2 leftScroll;

    private SummonSpiritDef selectedSpirit;
    private CultistRiteDef selectedRite;
    private Thing selectedLore;
    private Thing selectedTool;
    private DreamInfluence selectedInfluence;
    private Pawn selectedAssistant;
    private Pawn selectedSacrifice;

    public SummonWindow(Campaign campaign, Pawn summoner, Thing altar)
    {
        this.campaign = campaign;
        this.summoner = summoner;
        this.altar = altar;
        selectedSpirit = DefDatabase<SummonSpiritDef>.AllDefsListForReading.FirstOrDefault();
        selectedRite = campaign.UnlockedRites.FirstOrDefault();
        forcePause = true;
        doCloseButton = true;
        absorbInputAroundWindow = true;
    }

    public override Vector2 InitialSize => new(860f, 600f);

    public override void DoWindowContents(Rect inRect)
    {
        Text.Font = GameFont.Medium;
        Widgets.Label(inRect.TopPartPixels(30f), "Cultist.Summon.WindowTitle".Translate());
        Text.Font = GameFont.Small;

        Rect content = new(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 86f);
        float leftW = content.width * 0.47f;
        Rect leftRect = new(content.x, content.y, leftW, content.height);
        Rect rightRect = new(leftRect.xMax + 14f, content.y, content.width - leftW - 14f, content.height);

        DrawSpiritListColumn(leftRect);
        DrawRiteComposerColumn(rightRect);
    }

    private void DrawSpiritListColumn(Rect rect)
    {
        var spirits = DefDatabase<SummonSpiritDef>.AllDefsListForReading;
        float viewWidth = rect.width - 16f;
        int now = Find.TickManager.TicksGame;
        var activeRows = campaign.ActiveSummons
            .Where(rec => rec?.Spirit != null && !rec.Spirit.Dead)
            .Select(rec =>
            {
                float hours = System.Math.Max(0f, (rec.ExpiresTick - now) / (float)GenDate.TicksPerHour);
                string label = "Cultist.Summon.ActiveRow".Translate(
                    rec.Def?.LabelCap ?? rec.Spirit.LabelShortCap, hours.ToString("0.0"));
                float labelHeight = Text.CalcHeight(label, viewWidth - 12f);
                float cardHeight = labelHeight + (rec.Hostile ? 12f : 42f);
                return (Record: rec, Label: label, LabelHeight: labelHeight, CardHeight: cardHeight);
            }).ToList();
        float totalH = activeRows.Sum(row => row.CardHeight + 6f) + spirits.Count * 76f + 24f;
        Rect viewRect = new(0f, 0f, viewWidth, System.Math.Max(totalH, rect.height));

        Widgets.BeginScrollView(rect, ref leftScroll, viewRect);
        float y = 0f;

        foreach (var row in activeRows)
        {
            SummonedSpiritRecord rec = row.Record;
            Rect card = new(0f, y, viewRect.width, row.CardHeight);
            Widgets.DrawBoxSolid(card, rec.Hostile ? new Color(0.32f, 0.12f, 0.12f, 0.85f) : new Color(0.12f, 0.24f, 0.22f, 0.85f));
            Rect inner = card.ContractedBy(6f);
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, row.LabelHeight), row.Label);
            float buttonY = inner.y + row.LabelHeight + 4f;

            if (!rec.Hostile && rec.Def?.taughtLanguage != null && !campaign.KnowsLanguage(summoner, rec.Def.taughtLanguage))
            {
                if (Widgets.ButtonText(new Rect(inner.x, buttonY, inner.width * 0.74f, 26f),
                        "Cultist.Summon.LearnLang".Translate(rec.Def.taughtLanguage.LabelCap)))
                    campaign.GrantLanguage(summoner, rec.Def.taughtLanguage);
            }
            if (!rec.Hostile && Widgets.ButtonText(new Rect(inner.x + inner.width * 0.76f, buttonY, inner.width * 0.24f, 26f),
                    "Cultist.Summon.Dismiss".Translate()))
            {
                campaign.DismissSpirit(rec);
                break;
            }
            y += row.CardHeight + 6f;
        }

        foreach (SummonSpiritDef spirit in spirits)
        {
            bool selected = spirit == selectedSpirit;
            Rect card = new(0f, y, viewRect.width, 70f);
            Widgets.DrawBoxSolid(card, selected ? new Color(0.22f, 0.24f, 0.30f, 0.95f) : new Color(0.13f, 0.14f, 0.17f, 0.88f));
            if (selected)
            {
                GUI.color = new Color(0.88f, 0.74f, 0.38f);
                Widgets.DrawBox(card, 1);
                GUI.color = Color.white;
            }

            Rect inner = card.ContractedBy(7f);
            string title = spirit.LabelCap;
            if (spirit.taughtLanguage != null)
                title += " · " + spirit.taughtLanguage.LabelCap;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), title);

            // Pure-color aspect requirement swatches with hover tooltips
            float sx = inner.x;
            float sy = inner.y + 26f;
            var reqs = spirit.EffectiveRequirements;
            for (int i = 0; i < reqs.Count; i++)
            {
                AspectRequirement req = reqs[i];
                string tip = AspectBadgeUI.DefaultTooltip(req.aspect, req.amount.ToString());
                sx += AspectBadgeUI.DrawSwatchWithValue(sx, sy, req.aspect, req.amount.ToString(), tip);
            }

            if (Widgets.ButtonInvisible(card))
                selectedSpirit = spirit;
            y += 76f;
        }

        Widgets.EndScrollView();
    }

    private void DrawRiteComposerColumn(Rect rect)
    {
        Widgets.DrawBoxSolid(rect, new Color(0.11f, 0.12f, 0.15f, 0.92f));
        GUI.color = new Color(0.32f, 0.30f, 0.25f, 0.9f);
        Widgets.DrawBox(rect, 1);
        GUI.color = Color.white;

        Rect inner = rect.ContractedBy(12f);
        if (selectedSpirit == null || selectedRite == null)
            return;

        float y = inner.y;
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, y, inner.width, 28f), selectedSpirit.LabelCap);
        y += 30f;

        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.78f, 0.76f, 0.72f);
        Widgets.Label(new Rect(inner.x, y, inner.width, 36f), selectedSpirit.description);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 40f;

        // 1. Rite selector
        var unlockedRites = campaign.UnlockedRites.ToList();
        if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f),
                "Cultist.Rite.SelectButton".Translate(selectedRite.LabelCap)))
        {
            var opts = unlockedRites.Select(r => new FloatMenuOption(r.LabelCap, () =>
            {
                selectedRite = r;
                if (!r.allowLore) selectedLore = null;
                if (!r.allowTool) selectedTool = null;
                if (!r.allowInfluence) selectedInfluence = null;
                if (!r.allowAssistant) selectedAssistant = null;
                if (!r.allowSacrifice) selectedSacrifice = null;
            })).ToList();
            Find.WindowStack.Add(new FloatMenu(opts));
        }
        y += 34f;
        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.74f, 0.72f, 0.68f);
        Widgets.Label(new Rect(inner.x, y, inner.width, 34f), selectedRite.description);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        y += 38f;

        // 2. Material & Pawn slots
        var nearby = Campaign.NearbyMaterials(summoner, altar).ToList();

        if (selectedRite.allowLore)
        {
            if (selectedLore != null && !nearby.Contains(selectedLore))
                selectedLore = null;
            string loreLabel = selectedLore != null
                ? $"{selectedLore.LabelCap} ({("Cultist.Aspect." + selectedLore.TryGetComp<RitualMaterial>().Aspect).Translate()} {selectedLore.TryGetComp<RitualMaterial>().AspectValue})"
                : "Cultist.Rite.SlotEmpty".Translate();
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f), "Cultist.Rite.SlotLore".Translate(loreLabel)))
            {
                var opts = new List<FloatMenuOption> { new("Cultist.Rite.ClearSlot".Translate(), () => selectedLore = null) };
                foreach (Thing t in nearby.Where(m => m.TryGetComp<RitualMaterial>()?.LoreTier > 0))
                {
                    RitualMaterial mat = t.TryGetComp<RitualMaterial>();
                    Thing captured = t;
                    opts.Add(new FloatMenuOption($"{t.LabelCap} ({("Cultist.Aspect." + mat.Aspect).Translate()} {mat.AspectValue})", () => selectedLore = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 36f;
        }

        if (selectedRite.allowTool)
        {
            if (selectedTool != null && !nearby.Contains(selectedTool))
                selectedTool = null;
            string toolLabel = selectedTool != null
                ? $"{selectedTool.LabelCap} ({("Cultist.Aspect." + selectedTool.TryGetComp<RitualMaterial>().Aspect).Translate()} {selectedTool.TryGetComp<RitualMaterial>().ToolLight})"
                : "Cultist.Rite.SlotEmpty".Translate();
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f), "Cultist.Rite.SlotTool".Translate(toolLabel)))
            {
                var opts = new List<FloatMenuOption> { new("Cultist.Rite.ClearSlot".Translate(), () => selectedTool = null) };
                foreach (Thing t in nearby.Where(m => m.TryGetComp<RitualMaterial>()?.ToolLight > 0))
                {
                    RitualMaterial mat = t.TryGetComp<RitualMaterial>();
                    Thing captured = t;
                    opts.Add(new FloatMenuOption($"{t.LabelCap} ({("Cultist.Aspect." + mat.Aspect).Translate()} {mat.ToolLight})", () => selectedTool = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 36f;
        }

        if (selectedRite.allowInfluence)
        {
            var influences = campaign.ActiveLeaderInfluences;
            if (selectedInfluence != null && !influences.Contains(selectedInfluence))
                selectedInfluence = null;
            string infLabel = selectedInfluence != null
                ? $"{("Cultist.Aspect." + selectedInfluence.Affinity).Translate()} {selectedInfluence.Strength}"
                : "Cultist.Rite.SlotEmpty".Translate();
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f), "Cultist.Rite.SlotInfluence".Translate(infLabel)))
            {
                var opts = new List<FloatMenuOption> { new("Cultist.Rite.ClearSlot".Translate(), () => selectedInfluence = null) };
                foreach (DreamInfluence inf in influences)
                {
                    DreamInfluence captured = inf;
                    opts.Add(new FloatMenuOption($"{("Cultist.Aspect." + inf.Affinity).Translate()} {inf.Strength}", () => selectedInfluence = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 36f;
        }

        if (selectedRite.allowAssistant)
        {
            var helpers = altar.Map.mapPawns.FreeColonistsSpawned
                .Where(p => p != summoner && !p.Dead && !p.Downed && campaign.IsMember(p)).ToList();
            if (selectedAssistant != null && !helpers.Contains(selectedAssistant))
                selectedAssistant = null;
            string asstLabel = selectedAssistant != null
                ? $"{selectedAssistant.LabelShortCap} ({("Cultist.Aspect." + campaign.GetAspect(selectedAssistant)).Translate()} {System.Math.Max(2, campaign.Rank(selectedAssistant) * 3)})"
                : "Cultist.Rite.SlotEmpty".Translate();
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f), "Cultist.Rite.SlotAssistant".Translate(asstLabel)))
            {
                var opts = new List<FloatMenuOption> { new("Cultist.Rite.ClearSlot".Translate(), () => selectedAssistant = null) };
                foreach (Pawn p in helpers)
                {
                    Pawn captured = p;
                    int val = System.Math.Max(2, campaign.Rank(p) * 3);
                    opts.Add(new FloatMenuOption($"{p.LabelShortCap} ({("Cultist.Aspect." + campaign.GetAspect(p)).Translate()} {val})", () => selectedAssistant = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 36f;
        }

        if (selectedRite.allowSacrifice)
        {
            var victims = altar.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != summoner && !p.Dead && p.RaceProps.Humanlike && (p.IsPrisonerOfColony || p.IsSlaveOfColony || p.IsColonist))
                .ToList();
            if (selectedSacrifice != null && !victims.Contains(selectedSacrifice))
                selectedSacrifice = null;
            string sacLabel = selectedSacrifice != null
                ? selectedSacrifice.LabelShortCap
                : "Cultist.Rite.SlotEmpty".Translate();
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 30f), "Cultist.Rite.SlotSacrifice".Translate(sacLabel)))
            {
                var opts = new List<FloatMenuOption> { new("Cultist.Rite.ClearSlot".Translate(), () => selectedSacrifice = null) };
                foreach (Pawn p in victims)
                {
                    Pawn captured = p;
                    opts.Add(new FloatMenuOption(p.LabelShortCap, () => selectedSacrifice = captured));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += 36f;
        }

        // 3. Live Aspect Progress with Pure-Color Swatches & Sacrifice Auto-Fill indicator
        y += 8f;
        var eval = campaign.EvaluateRiteSummon(selectedSpirit, selectedRite,
            selectedLore, selectedTool, selectedInfluence, selectedAssistant, selectedSacrifice);

        Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Cultist.Rite.AspectProgress".Translate());
        y += 24f;
        float px = inner.x;
        var reqs = selectedSpirit.EffectiveRequirements;
        for (int i = 0; i < reqs.Count; i++)
        {
            AspectRequirement req = reqs[i];
            int have = eval.Totals.GetValueOrDefault(req.aspect);
            bool met = have >= req.amount;
            string val = $"{have}/{req.amount}";
            string tip = AspectBadgeUI.DefaultTooltip(req.aspect, val);
            px += AspectBadgeUI.DrawSwatchWithValue(px, y, req.aspect, val, tip,
                met ? new Color(0.45f, 0.92f, 0.52f) : new Color(0.95f, 0.55f, 0.48f));
        }
        y += 28f;

        if (eval.SacrificeFilledAspect.HasValue)
        {
            GUI.color = new Color(0.92f, 0.78f, 0.42f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f),
                "Cultist.Rite.SacrificeAutoFill".Translate(
                    eval.SacrificeFilledAmount,
                    ("Cultist.Aspect." + eval.SacrificeFilledAspect.Value).Translate()));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 24f;
        }

        float backlash = System.Math.Clamp(campaign.CalculateBacklashChance(summoner, altar, selectedSpirit) + selectedRite.backlashModifier, 0.02f, 0.85f);
        GUI.color = new Color(0.75f, 0.73f, 0.70f);
        Text.Font = GameFont.Tiny;
        Widgets.Label(new Rect(inner.x, inner.yMax - 60f, inner.width, 22f),
            "Cultist.Rite.BacklashSummary".Translate(backlash.ToStringPercent(), selectedSpirit.durationDays.ToString("0.0")));
        GUI.color = Color.white;
        Text.Font = GameFont.Small;

        Rect btnRect = new(inner.x, inner.yMax - 34f, inner.width, 34f);
        if (eval.AllMet)
        {
            if (Widgets.ButtonText(btnRect, "Cultist.Summon.Button".Translate()))
            {
                if (campaign.TrySummonSpiritWithRite(summoner, altar, selectedSpirit, selectedRite,
                        selectedLore, selectedTool, selectedInfluence, selectedAssistant, selectedSacrifice))
                    Close();
            }
        }
        else
        {
            GUI.color = new Color(0.55f, 0.55f, 0.55f);
            Widgets.ButtonText(btnRect, "Cultist.Rite.AspectsNotMet".Translate(), active: false);
            GUI.color = Color.white;
        }
    }
}
