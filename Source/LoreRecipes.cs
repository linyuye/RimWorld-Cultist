using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed class CultistLoreRecipeExt : DefModExtension
{
    public int targetTier;
    public int requiredInfluence;
    public bool isSubversion;
    public Aspect targetAspect = Aspect.Lantern;
}

public sealed class RecipeWorker_CompileLore : RecipeWorker
{
    public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
    {
        base.Notify_IterationCompleted(billDoer, ingredients);
        if (billDoer?.Map == null || recipe == null)
            return;

        CultistLoreRecipeExt ext = recipe.GetModExtension<CultistLoreRecipeExt>();
        if (ext == null || ext.targetTier <= 0)
            return;

        // Determine aspect from consumed lore books
        var loreInputs = new List<(ThingDef def, Aspect aspect, int tier)>();
        if (ingredients != null)
        {
            foreach (Thing t in ingredients)
            {
                RitualMaterialProperties mat = t?.def?.GetCompProperties<RitualMaterialProperties>();
                if (mat != null && mat.loreTier > 0)
                    loreInputs.Add((t.def, mat.aspect, mat.loreTier));
            }
        }

        Aspect aspect = loreInputs.Count > 0
            ? loreInputs[0].aspect
            : (Campaign.Current?.ChurchAspect ?? Aspect.Lantern);
        int targetTier = Math.Clamp(ext.targetTier, 1, 7);

        // Verify and consume matching active Influence from billDoer or society dreamers
        int reqInf = Math.Max(0, ext.requiredInfluence);
        if (reqInf > 0 && !(Campaign.Current?.TryConsumeLoreUpgradeInfluence(billDoer, aspect, reqInf) ?? false))
        {
            // Refund consumed books and cloth so players never lose rare lore if influence expired
            RefundIngredients(billDoer, ingredients);
            string aspLabel = ("Cultist.Aspect." + aspect).Translate();
            Messages.Message(
                "Cultist.LoreCompile.NeedInfluence".Translate(targetTier, aspLabel, reqInf),
                billDoer, MessageTypeDefOf.RejectInput);
            return;
        }

        ThingDef outDef = CultPathDef.ForAspect(aspect)?.GetLoreDef(targetTier)
            ?? Campaign.Current?.GetLoreDef(targetTier);
        if (outDef == null)
        {
            RefundIngredients(billDoer, ingredients);
            return;
        }

        Thing result = ThingMaker.MakeThing(outDef);
        if (!GenPlace.TryPlaceThing(result, billDoer.Position, billDoer.Map, ThingPlaceMode.Near))
        {
            result.Destroy();
            Campaign.Current?.GrantLore(billDoer, 1, targetTier, aspect);
        }

        Campaign.PlayCultistSound("Cultist_Sfx_RiteComplete");
        string aspectName = ("Cultist.Aspect." + aspect).Translate();
        Messages.Message(
            "Cultist.LoreCompile.Success".Translate(billDoer.LabelShortCap, outDef.LabelCap, aspectName, targetTier),
            billDoer, MessageTypeDefOf.PositiveEvent);
    }

    private static void RefundIngredients(Pawn billDoer, List<Thing> ingredients)
    {
        if (billDoer?.Map == null || ingredients == null)
            return;
        foreach (Thing t in ingredients)
        {
            if (t?.def == null || t.stackCount <= 0)
                continue;
            int remaining = t.stackCount;
            while (remaining > 0)
            {
                Thing refund = ThingMaker.MakeThing(t.def, t.Stuff);
                int chunk = Math.Min(remaining, t.def.stackLimit);
                refund.stackCount = chunk;
                remaining -= chunk;
                GenPlace.TryPlaceThing(refund, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
            }
        }
    }
}

public sealed class RecipeWorker_SubvertLore : RecipeWorker
{
    public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
    {
        base.Notify_IterationCompleted(billDoer, ingredients);
        if (billDoer?.Map == null || recipe == null)
            return;

        CultistLoreRecipeExt ext = recipe.GetModExtension<CultistLoreRecipeExt>();
        if (ext == null || !ext.isSubversion)
            return;

        int inputTier = 1;
        Aspect inputAspect = Aspect.Lantern;
        if (ingredients != null)
        {
            foreach (Thing t in ingredients)
            {
                RitualMaterialProperties mat = t?.def?.GetCompProperties<RitualMaterialProperties>();
                if (mat != null && mat.loreTier > 0)
                {
                    inputTier = Math.Clamp(mat.loreTier, 1, 7);
                    inputAspect = mat.aspect;
                    break;
                }
            }
        }

        ThingDef outDef = CultPathDef.ForAspect(ext.targetAspect)?.GetLoreDef(inputTier)
            ?? Campaign.Current?.GetLoreDef(inputTier);
        if (outDef == null)
            return;

        Thing result = ThingMaker.MakeThing(outDef);
        if (!GenPlace.TryPlaceThing(result, billDoer.Position, billDoer.Map, ThingPlaceMode.Near))
        {
            result.Destroy();
            Campaign.Current?.GrantLore(billDoer, 1, inputTier, ext.targetAspect);
        }

        Campaign.PlayCultistSound("Cultist_Sfx_RiteComplete");
        string fromLabel = ("Cultist.Aspect." + inputAspect).Translate();
        string toLabel = ("Cultist.Aspect." + ext.targetAspect).Translate();
        Messages.Message(
            "Cultist.LoreSubvert.Success".Translate(billDoer.LabelShortCap, fromLabel, toLabel, inputTier, outDef.LabelCap),
            billDoer, MessageTypeDefOf.PositiveEvent);
    }
}

public sealed partial class Campaign
{
    internal bool TryConsumeLoreUpgradeInfluence(Pawn billDoer, Aspect requiredAspect, int minStrength)
    {
        int now = Find.TickManager.TicksGame;
        var candidates = new List<Pawn>();
        if (billDoer != null)
            candidates.Add(billDoer);
        if (leader != null && !candidates.Contains(leader))
            candidates.Add(leader);
        if (billDoer?.Map != null)
        {
            foreach (Pawn p in billDoer.Map.mapPawns.FreeColonistsSpawned)
            {
                if (IsMember(p) && !candidates.Contains(p))
                    candidates.Add(p);
            }
        }

        foreach (Pawn p in candidates)
        {
            PersonalDream dream = DreamOf(p);
            if (dream?.Influences == null)
                continue;
            dream.Influences.RemoveAll(inf => inf.Expires <= now);

            DreamInfluence match = dream.Influences
                .Where(inf => inf.Strength >= minStrength
                    && (requiredAspect == Aspect.SecretHistories || inf.Affinity == requiredAspect))
                .OrderBy(inf => inf.Strength)
                .ThenBy(inf => inf.Expires)
                .FirstOrDefault();
            if (match != null)
            {
                dream.Influences.Remove(match);
                return true;
            }
        }

        return false;
    }
}
