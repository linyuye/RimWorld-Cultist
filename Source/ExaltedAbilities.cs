using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Cultist;

public enum ExaltedTraitId
{
    PhaseWalker,
    MoltingMist,
    AshenSkin,
    PainFervor,
    GraveyardWhisper,
    HoneyedTongue,
    SleeplessVigil,
    HourForesight,
    PulseHemostasis,
    NightEyes,
    VoraciousGut,
    BedrockMind,
    LoreSpeedReader,
    LongStrider,
    SilentStep
}

public sealed class PawnExaltedProfile : IExposable
{
    public List<ExaltedTraitId> Traits = new();
    public int FixedReadyTick;
    public int PhaseTraitReadyTick;
    public int GraveTraitReadyTick;
    public int MistReadyTick;

    public void ExposeData()
    {
        Scribe_Collections.Look(ref Traits, "traits", LookMode.Value);
        Scribe_Values.Look(ref FixedReadyTick, "fixedReadyTick");
        Scribe_Values.Look(ref PhaseTraitReadyTick, "phaseTraitReadyTick");
        Scribe_Values.Look(ref GraveTraitReadyTick, "graveTraitReadyTick");
        Scribe_Values.Look(ref MistReadyTick, "mistReadyTick");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            Traits ??= new List<ExaltedTraitId>();
    }
}

public sealed class KnockPhaseCompProperties : HediffCompProperties
{
    public KnockPhaseCompProperties() => compClass = typeof(HediffComp_KnockPhase);
}

public sealed class HediffComp_KnockPhase : HediffComp
{
    public override void CompPostTick(ref float severityAdjustment)
    {
        Pawn p = Pawn;
        if (p == null || !p.Spawned || p.Map == null || p.pather is not { Moving: true })
            return;

        IntVec3 next = p.pather.nextCell;
        if (!next.IsValid || !next.InBounds(p.Map) || next == p.Position)
            return;

        Building edifice = next.GetEdifice(p.Map);
        if (edifice is Building_Door or Building && (edifice is Building_Door door && !door.Open || edifice.def.passability == Traversability.Impassable))
        {
            IntVec3 delta = next - p.Position;
            for (int step = 2; step <= 4; step++)
            {
                IntVec3 beyond = p.Position + new IntVec3(delta.x * step, 0, delta.z * step);
                if (beyond.InBounds(p.Map) && beyond.Standable(p.Map))
                {
                    FleckMaker.ThrowDustPuffThick(p.DrawPos, p.Map, 1.0f, new Color(0.72f, 0.45f, 0.95f));
                    p.Position = beyond;
                    p.Notify_Teleported(endCurrentJob: false, resetTweenedPos: true);
                    break;
                }
            }
        }
    }
}

public sealed partial class Campaign
{
    internal const int ExaltedFixedCooldownTicks = GenDate.TicksPerDay * 3;
    private Dictionary<string, PawnExaltedProfile> exaltedProfiles = new();
    private int bonusFaceUpCards;

    public int BonusFaceUpCards => bonusFaceUpCards;

    internal void ConsumeBonusFaceUpCards() => bonusFaceUpCards = 0;

    private static readonly (ExaltedTraitId Id, Aspect[] Preferred, string HediffName)[] TraitCatalog =
    {
        (ExaltedTraitId.PhaseWalker, new[] { Aspect.Knock, Aspect.Moth }, null),
        (ExaltedTraitId.MoltingMist, new[] { Aspect.Moth }, null),
        (ExaltedTraitId.AshenSkin, new[] { Aspect.Forge }, "Cultist_Trait_AshenSkin"),
        (ExaltedTraitId.PainFervor, new[] { Aspect.Edge, Aspect.Knock }, null),
        (ExaltedTraitId.GraveyardWhisper, new[] { Aspect.Winter }, null),
        (ExaltedTraitId.HoneyedTongue, new[] { Aspect.Grail }, "Cultist_Trait_HoneyedTongue"),
        (ExaltedTraitId.SleeplessVigil, new[] { Aspect.Lantern, Aspect.Heart }, "Cultist_Trait_Sleepless"),
        (ExaltedTraitId.HourForesight, new[] { Aspect.Lantern, Aspect.SecretHistories }, "Cultist_Trait_Foresight"),
        (ExaltedTraitId.PulseHemostasis, new[] { Aspect.Heart }, "Cultist_Trait_Hemostasis"),
        (ExaltedTraitId.NightEyes, new[] { Aspect.Moth, Aspect.Winter }, "Cultist_Trait_NightEyes"),
        (ExaltedTraitId.VoraciousGut, new[] { Aspect.Grail }, "Cultist_Trait_VoraciousGut"),
        (ExaltedTraitId.BedrockMind, new[] { Aspect.Winter, Aspect.Forge }, "Cultist_Trait_BedrockMind"),
        (ExaltedTraitId.LoreSpeedReader, new[] { Aspect.Lantern, Aspect.SecretHistories }, "Cultist_Trait_SpeedReader"),
        (ExaltedTraitId.LongStrider, new[] { Aspect.Forge, Aspect.SecretHistories }, "Cultist_Trait_LongStrider"),
        (ExaltedTraitId.SilentStep, new[] { Aspect.Winter, Aspect.Moth }, "Cultist_Trait_SilentStep"),
    };

    private void ExposeExalted()
    {
        Scribe_Collections.Look(ref exaltedProfiles, "exaltedProfiles", LookMode.Value, LookMode.Deep);
        Scribe_Values.Look(ref bonusFaceUpCards, "bonusFaceUpCards");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            exaltedProfiles ??= new Dictionary<string, PawnExaltedProfile>();
    }

    internal PawnExaltedProfile GetExaltedProfile(Pawn pawn, bool createIfRank3 = true)
    {
        if (pawn == null)
            return null;
        string id = pawn.GetUniqueLoadID();
        if (exaltedProfiles.TryGetValue(id, out PawnExaltedProfile prof))
            return prof;
        if (createIfRank3 && Rank(pawn) >= 3)
            return AwakenExaltedTraits(pawn, announce: false);
        return null;
    }

    internal bool HasExaltedTrait(Pawn pawn, ExaltedTraitId trait) =>
        Rank(pawn) >= 3 && GetExaltedProfile(pawn)?.Traits.Contains(trait) == true;

    internal PawnExaltedProfile AwakenExaltedTraits(Pawn pawn, bool announce = true)
    {
        string id = pawn.GetUniqueLoadID();
        if (exaltedProfiles.TryGetValue(id, out PawnExaltedProfile existing) && existing.Traits.Count > 0)
            return existing;

        var profile = new PawnExaltedProfile();
        Aspect pawnAspect = GetAspect(pawn);
        int count = Rand.RangeInclusive(2, 3);
        var available = TraitCatalog.ToList();

        for (int i = 0; i < count && available.Count > 0; i++)
        {
            var picked = available.RandomElementByWeight(entry => entry.Preferred.Contains(pawnAspect) ? 2.5f : 1f);
            profile.Traits.Add(picked.Id);
            available.Remove(picked);
        }

        exaltedProfiles[id] = profile;
        SyncExaltedPassiveHediffs(pawn, profile);

        if (announce)
        {
            string traitNames = string.Join("、", profile.Traits.Select(t => $"Cultist.ExaltedTrait.{t}".Translate().ToString()));
            Messages.Message("Cultist.Exalted.Awakened".Translate(
                pawn.LabelShortCap,
                $"Cultist.ExaltedSkill.{pawnAspect}".Translate(),
                traitNames), pawn, MessageTypeDefOf.PositiveEvent);
        }
        return profile;
    }

    private void TickExaltedTraits(int now)
    {
        foreach (Map map in Find.Maps)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Dead || Rank(pawn) < 3)
                    continue;
                PawnExaltedProfile prof = GetExaltedProfile(pawn);
                if (prof == null)
                    continue;

                SyncExaltedPassiveHediffs(pawn, prof);

                if (prof.Traits.Contains(ExaltedTraitId.AshenSkin))
                {
                    if (pawn.GetAttachment(ThingDefOf.Fire) is { Destroyed: false } fire)
                        fire.Destroy();
                }

                if (prof.Traits.Contains(ExaltedTraitId.NightEyes) && pawn.needs?.mood?.thoughts?.memories is { } mems1)
                {
                    if (DefDatabase<ThoughtDef>.GetNamedSilentFail("Darkness") is { } darkTd)
                        mems1.RemoveMemoriesOfDef(darkTd);
                }

                if (prof.Traits.Contains(ExaltedTraitId.VoraciousGut))
                {
                    if (pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.FoodPoisoning) is { } fp)
                        pawn.health.RemoveHediff(fp);
                    if (pawn.needs?.mood?.thoughts?.memories is { } mems2)
                    {
                        foreach (string badFood in new[] { "AteRawFood", "AteInsectMeatDirect", "AteInsectMeatAsIngredient" })
                        {
                            if (DefDatabase<ThoughtDef>.GetNamedSilentFail(badFood) is { } td)
                                mems2.RemoveMemoriesOfDef(td);
                        }
                    }
                }
            }
        }
    }

    internal void CheckExaltedMothEscape(Pawn pawn, int now)
    {
        if (Rank(pawn) < 3 || !HasExaltedTrait(pawn, ExaltedTraitId.MoltingMist))
            return;
        PawnExaltedProfile prof = GetExaltedProfile(pawn);
        if (prof == null || now < prof.MistReadyTick || pawn.Map == null)
            return;
        if (pawn.health.summaryHealth.SummaryHealthPercent >= 0.35f)
            return;

        prof.MistReadyTick = now + GenDate.TicksPerDay * 5;
        GasUtility.AddGas(pawn.Position, pawn.Map, GasType.BlindSmoke, 160);
        if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_MothEscape") is { } dash)
            pawn.health.AddHediff(dash);
        PlayCultistSound("Cultist_Sfx_MansusExit");
        Messages.Message("Cultist.Exalted.MoltingTriggered".Translate(pawn.LabelShortCap),
            pawn, MessageTypeDefOf.NeutralEvent);
    }

    private static void SyncExaltedPassiveHediffs(Pawn pawn, PawnExaltedProfile profile)
    {
        if (pawn?.health?.hediffSet == null)
            return;
        foreach (var entry in TraitCatalog)
        {
            if (entry.HediffName == null)
                continue;
            HediffDef hd = DefDatabase<HediffDef>.GetNamedSilentFail(entry.HediffName);
            if (hd == null)
                continue;
            bool want = profile.Traits.Contains(entry.Id);
            Hediff cur = pawn.health.hediffSet.GetFirstHediffOfDef(hd);
            if (want && cur == null)
                pawn.health.AddHediff(hd);
            else if (!want && cur != null)
                pawn.health.RemoveHediff(cur);
        }
    }

    internal IEnumerable<Gizmo> GetExaltedGizmos(Pawn pawn)
    {
        if (Rank(pawn) < 3 || !IsMember(pawn) || pawn.Downed)
            yield break;

        PawnExaltedProfile prof = GetExaltedProfile(pawn);
        if (prof == null)
            yield break;

        int now = Find.TickManager.TicksGame;
        Aspect aspect = GetAspect(pawn);

        // 1. Fixed Aspect Specialty (3-day CD)
        bool fixedReady = now >= prof.FixedReadyTick;
        var fixedCmd = new Command_Action
        {
            defaultLabel = $"Cultist.ExaltedSkill.{aspect}".Translate().ToString(),
            defaultDesc = $"Cultist.ExaltedSkill.{aspect}.Desc".Translate().ToString()
                + (fixedReady ? "" : "\n" + "Cultist.Exalted.CooldownLeft".Translate(HoursRemaining(prof.FixedReadyTick, now)).ToString()),
            icon = ContentFinder<Texture2D>.Get($"Cultist/Icons/Aspect_{aspect}", false) ?? TexCommand.Attack,
            Disabled = !fixedReady,
            disabledReason = fixedReady ? null : "Cultist.Exalted.CooldownLeft".Translate(HoursRemaining(prof.FixedReadyTick, now)).ToString(),
            action = () => TriggerFixedExaltedAbility(pawn, aspect, prof)
        };
        yield return fixedCmd;

        // 2. Active Random Trait: PhaseWalker (2-day CD)
        if (prof.Traits.Contains(ExaltedTraitId.PhaseWalker))
        {
            bool ready = now >= prof.PhaseTraitReadyTick;
            yield return new Command_Action
            {
                defaultLabel = "Cultist.ExaltedTrait.PhaseWalker".Translate(),
                defaultDesc = "Cultist.ExaltedTrait.PhaseWalker.Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Cultist/Icons/Aspect_Knock", false) ?? TexCommand.RearmTrap,
                Disabled = !ready,
                disabledReason = ready ? null : "Cultist.Exalted.CooldownLeft".Translate(HoursRemaining(prof.PhaseTraitReadyTick, now)),
                action = () =>
                {
                    if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_KnockPhase") is { } phaseDef)
                    {
                        pawn.health.AddHediff(phaseDef);
                        prof.PhaseTraitReadyTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 2;
                        PlayCultistSound("Cultist_Sfx_MansusEnter");
                    }
                }
            };
        }

        // 3. Active Random Trait: GraveyardWhisper (3-day CD)
        if (prof.Traits.Contains(ExaltedTraitId.GraveyardWhisper))
        {
            bool ready = now >= prof.GraveTraitReadyTick;
            yield return new Command_Action
            {
                defaultLabel = "Cultist.ExaltedTrait.GraveyardWhisper".Translate(),
                defaultDesc = "Cultist.ExaltedTrait.GraveyardWhisper.Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Cultist/Icons/Aspect_Winter", false) ?? TexCommand.Draft,
                Disabled = !ready,
                disabledReason = ready ? null : "Cultist.Exalted.CooldownLeft".Translate(HoursRemaining(prof.GraveTraitReadyTick, now)),
                action = () => TargetCorpseForRisen(pawn, prof)
            };
        }

        // 4. While Knock Phase-Walk Hediff is active: free Obstacle/Wall Phase-Step targeting!
        HediffDef knockPhase = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_KnockPhase");
        if (knockPhase != null && pawn.health.hediffSet.HasHediff(knockPhase))
        {
            yield return new Command_Action
            {
                defaultLabel = "Cultist.Exalted.PhaseStep".Translate(),
                defaultDesc = "Cultist.Exalted.PhaseStep.Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Cultist/Icons/Aspect_Knock", false) ?? TexCommand.RearmTrap,
                action = () => BeginPhaseStepTargeting(pawn)
            };
        }
    }

    private static string HoursRemaining(int readyTick, int now) =>
        ((readyTick - now) / (float)GenDate.TicksPerHour).ToString("0.0");

    private void TriggerFixedExaltedAbility(Pawn caster, Aspect aspect, PawnExaltedProfile prof)
    {
        int now = Find.TickManager.TicksGame;
        Map map = caster.Map;
        if (map == null)
            return;

        switch (aspect)
        {
            case Aspect.Lantern:
                bonusFaceUpCards = 1;
                prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                PlayCultistSound("Cultist_Sfx_RiteComplete");
                Messages.Message("Cultist.Exalted.LanternUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
                break;

            case Aspect.Forge:
            {
                Thing damaged = FindDamagedEquipmentOnMap(caster, map);
                if (damaged == null)
                {
                    Messages.Message("Cultist.Exalted.NoDamagedGear".Translate(), caster, MessageTypeDefOf.RejectInput);
                    return;
                }
                if (!TryConsumeResourceOnMap(map, ThingDefOf.Steel, 10))
                {
                    Messages.Message("Cultist.Exalted.NeedSteel".Translate(10), caster, MessageTypeDefOf.RejectInput);
                    return;
                }
                int heal = System.Math.Max(1, (int)(damaged.MaxHitPoints * 0.60f));
                damaged.HitPoints = System.Math.Min(damaged.MaxHitPoints, damaged.HitPoints + heal);
                prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                PlayCultistSound("Cultist_Sfx_RiteComplete");
                Messages.Message("Cultist.Exalted.ForgeUsed".Translate(caster.LabelShortCap, damaged.LabelCap), damaged, MessageTypeDefOf.PositiveEvent);
                break;
            }

            case Aspect.Grail:
            {
                var prisoners = map.mapPawns.PrisonersOfColonySpawned.Where(p => !p.Dead && p.guest != null).ToList();
                if (prisoners.Count == 0)
                {
                    Messages.Message("Cultist.Exalted.NoPrisoner".Translate(), caster, MessageTypeDefOf.RejectInput);
                    return;
                }
                Find.WindowStack.Add(new FloatMenu(prisoners.Select(pris => new FloatMenuOption(
                    pris.LabelShortCap,
                    () =>
                    {
                        pris.guest.resistance = System.Math.Max(0f, pris.guest.resistance - 8f);
                        pris.guest.will = System.Math.Max(0f, pris.guest.will - 8f);
                        if (pris.ideo != null && church != null && pris.Ideo != church)
                            pris.ideo.OffsetCertainty(-0.25f);
                        prof.FixedReadyTick = Find.TickManager.TicksGame + ExaltedFixedCooldownTicks;
                        PlayCultistSound("Cultist_Sfx_RiteComplete");
                        Messages.Message("Cultist.Exalted.GrailUsed".Translate(caster.LabelShortCap, pris.LabelShortCap), pris, MessageTypeDefOf.PositiveEvent);
                    })).ToList()));
                break;
            }

            case Aspect.Edge:
                if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_EdgeResolve") is { } edgeHd)
                {
                    caster.health.AddHediff(edgeHd);
                    prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                    PlayCultistSound("Cultist_Sfx_RiteComplete");
                    Messages.Message("Cultist.Exalted.EdgeUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
                }
                break;

            case Aspect.Winter:
                Find.Targeter.BeginTargeting(TargetingParameters.ForAttackAny(), target =>
                {
                    if (target.Thing is not Pawn foe || foe.Dead || !foe.RaceProps.IsFlesh
                        || !foe.HostileTo(Faction.OfPlayer)
                        || !foe.Position.InHorDistOf(caster.Position, 12f))
                    {
                        Messages.Message("Cultist.Exalted.InvalidWinterTarget".Translate(), caster, MessageTypeDefOf.RejectInput);
                        return;
                    }
                    if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_WinterGaze") is { } gaze)
                    {
                        foe.health.AddHediff(gaze);
                        prof.FixedReadyTick = Find.TickManager.TicksGame + ExaltedFixedCooldownTicks;
                        PlayCultistSound("Cultist_Sfx_MansusExit");
                        Messages.Message("Cultist.Exalted.WinterUsed".Translate(caster.LabelShortCap, foe.LabelShortCap), foe, MessageTypeDefOf.PositiveEvent);
                    }
                });
                break;

            case Aspect.Heart:
            {
                HediffDef chorusHd = DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_HeartChorus");
                ThoughtDef chorusTd = DefDatabase<ThoughtDef>.GetNamedSilentFail("Cultist_Thought_HeartChorus");
                foreach (Pawn ally in map.mapPawns.FreeColonistsSpawned)
                {
                    if (!ally.Position.InHorDistOf(caster.Position, 8f))
                        continue;
                    if (ally.needs?.rest != null)
                        ally.needs.rest.CurLevel = System.Math.Min(1f, ally.needs.rest.CurLevel + 0.15f);
                    if (chorusHd != null)
                        ally.health.AddHediff(chorusHd);
                    if (chorusTd != null)
                        ally.needs?.mood?.thoughts?.memories?.TryGainMemory(chorusTd);
                }
                prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                PlayCultistSound("Cultist_Sfx_RiteComplete");
                Messages.Message("Cultist.Exalted.HeartUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
                break;
            }

            case Aspect.Moth:
                ReduceNotoriety(12f);
                prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                PlayCultistSound("Cultist_Sfx_MansusExit");
                Messages.Message("Cultist.Exalted.MothUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
                break;

            case Aspect.Knock:
                if (DefDatabase<HediffDef>.GetNamedSilentFail("Cultist_Hediff_KnockPhase") is { } phaseDef)
                {
                    caster.health.AddHediff(phaseDef);
                    prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                    PlayCultistSound("Cultist_Sfx_MansusEnter");
                    Messages.Message("Cultist.Exalted.KnockUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
                }
                break;

            case Aspect.SecretHistories:
                if (SpawnBookExplorationSite(caster, caster.LabelShortCap, Rand.RangeInclusive(1, 2)))
                {
                    prof.FixedReadyTick = now + ExaltedFixedCooldownTicks;
                    PlayCultistSound("Cultist_Sfx_SiteRevealed");
                }
                break;
        }
    }

    private static Thing FindDamagedEquipmentOnMap(Pawn caster, Map map)
    {
        if (caster.equipment?.Primary is { } w && w.def.useHitPoints && w.HitPoints < w.MaxHitPoints)
            return w;
        if (caster.apparel != null)
        {
            Apparel worn = caster.apparel.WornApparel.FirstOrDefault(a => a.def.useHitPoints && a.HitPoints < a.MaxHitPoints);
            if (worn != null)
                return worn;
        }
        return map.listerThings.AllThings
            .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer)
                && (t.def.IsWeapon || t.def.IsApparel) && t.def.useHitPoints && t.HitPoints < t.MaxHitPoints)
            .OrderBy(t => t.Position.DistanceToSquared(caster.Position))
            .FirstOrDefault();
    }

    internal static bool TryConsumeResourceOnMap(Map map, ThingDef resourceDef, int amount)
    {
        if (map == null || resourceDef == null || amount <= 0)
            return false;
        var stacks = map.listerThings.ThingsOfDef(resourceDef)
            .Where(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer))
            .ToList();
        if (stacks.Sum(s => s.stackCount) < amount)
            return false;
        int left = amount;
        foreach (Thing s in stacks)
        {
            int take = System.Math.Min(left, s.stackCount);
            s.SplitOff(take).Destroy();
            left -= take;
            if (left <= 0)
                break;
        }
        return true;
    }

    private static void BeginPhaseStepTargeting(Pawn caster)
    {
        Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
        {
            Map map = caster.Map;
            if (map == null || !target.Cell.InBounds(map) || !target.Cell.Standable(map)
                || !target.Cell.InHorDistOf(caster.Position, 18f))
            {
                Messages.Message("Cultist.Exalted.PhaseInvalidCell".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }
            FleckMaker.ThrowDustPuffThick(caster.DrawPos, map, 1.4f, new Color(0.70f, 0.42f, 0.95f));
            caster.Position = target.Cell;
            caster.Notify_Teleported(endCurrentJob: false, resetTweenedPos: true);
            FleckMaker.ThrowDustPuffThick(caster.DrawPos, map, 1.4f, new Color(0.70f, 0.42f, 0.95f));
            PlayCultistSound("Cultist_Sfx_MansusEnter");
        });
    }

    private void TargetCorpseForRisen(Pawn caster, PawnExaltedProfile prof)
    {
        Map map = caster.Map;
        if (map == null)
            return;
        var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
            .OfType<Corpse>()
            .Where(c => c.Spawned && c.InnerPawn?.RaceProps.Humanlike == true
                && c.GetRotStage() == RotStage.Fresh
                && c.Position.InHorDistOf(caster.Position, 10f))
            .ToList();
        if (corpses.Count == 0)
        {
            Messages.Message("Cultist.Exalted.NoFreshCorpse".Translate(), caster, MessageTypeDefOf.RejectInput);
            return;
        }

        Corpse chosen = corpses[0];
        IntVec3 pos = chosen.Position;
        chosen.Destroy();
        if (SpawnTemporaryAlliedSpirit(map, pos, "Cultist_Summon_ShatteredRisen", GenDate.TicksPerHour * 6))
        {
            prof.GraveTraitReadyTick = Find.TickManager.TicksGame + GenDate.TicksPerDay * 3;
            PlayCultistSound("Cultist_Sfx_RiteComplete");
            Messages.Message("Cultist.Exalted.GraveUsed".Translate(caster.LabelShortCap), caster, MessageTypeDefOf.PositiveEvent);
        }
    }
}
