using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Cultist;

public sealed class AncientLanguageDef : Def
{
    public bool isSecretLanguage;
}

public sealed class AncientCodexProperties : CompProperties
{
    public AncientLanguageDef requiredLanguage;
    public ThingDef rewardLoreDef;

    public AncientCodexProperties() => compClass = typeof(AncientCodexComp);
}

public sealed class AncientCodexComp : ThingComp
{
    private AncientCodexProperties Props => (AncientCodexProperties)props;

    public override string CompInspectStringExtra() => Props.requiredLanguage != null
        ? "Cultist.CodexRequiresLang".Translate(Props.requiredLanguage.LabelCap).ToString()
        : null;

    public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
    {
        if (Campaign.Current is not { } campaign || !pawn.IsColonistPlayerControlled
            || !campaign.IsMember(pawn) || Props.requiredLanguage == null)
            yield break;

        if (!campaign.KnowsLanguage(pawn, Props.requiredLanguage))
        {
            yield return new FloatMenuOption(
                "Cultist.TranslateCodexNeedLang".Translate(Props.requiredLanguage.LabelCap), null);
            yield break;
        }

        yield return new FloatMenuOption(
            "Cultist.TranslateCodex".Translate(Props.requiredLanguage.LabelCap),
            () => TranslateNow(pawn));
    }

    private void TranslateNow(Pawn pawn)
    {
        if (parent.Destroyed || pawn.Map == null)
            return;
        ThingDef rewardDef = Props.rewardLoreDef ?? Campaign.Current?.ActivePath.tier1LoreDef;
        if (rewardDef != null)
        {
            Thing lore = ThingMaker.MakeThing(rewardDef);
            GenPlace.TryPlaceThing(lore, parent.Position, parent.Map, ThingPlaceMode.Near);
        }
        Messages.Message("Cultist.CodexTranslated".Translate(pawn.LabelShortCap, Props.requiredLanguage.LabelCap),
            pawn, MessageTypeDefOf.PositiveEvent);
        parent.Destroy();
    }
}

public sealed partial class Campaign
{
    private Dictionary<string, string> knownLanguages = new();

    private void ExposeLanguages()
    {
        Scribe_Collections.Look(ref knownLanguages, "knownLanguages", LookMode.Value, LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            knownLanguages ??= new Dictionary<string, string>();
    }

    public bool KnowsLanguage(Pawn pawn, AncientLanguageDef lang)
    {
        if (pawn == null || lang == null)
            return false;
        return knownLanguages.TryGetValue(pawn.GetUniqueLoadID(), out string list)
            && list.Split(',').Contains(lang.defName);
    }

    public void GrantLanguage(Pawn pawn, AncientLanguageDef lang)
    {
        if (pawn == null || lang == null || KnowsLanguage(pawn, lang))
            return;
        string id = pawn.GetUniqueLoadID();
        knownLanguages[id] = knownLanguages.TryGetValue(id, out string existing) && !string.IsNullOrEmpty(existing)
            ? existing + "," + lang.defName
            : lang.defName;
        Messages.Message("Cultist.LanguageLearned".Translate(pawn.LabelShortCap, lang.LabelCap),
            pawn, MessageTypeDefOf.PositiveEvent);
    }

    public string KnownLanguagesSummary(Pawn pawn)
    {
        if (pawn == null || !knownLanguages.TryGetValue(pawn.GetUniqueLoadID(), out string raw)
            || string.IsNullOrEmpty(raw))
            return "";
        var names = raw.Split(',')
            .Select(defName => DefDatabase<AncientLanguageDef>.GetNamedSilentFail(defName)?.LabelCap.ToString())
            .Where(name => !string.IsNullOrEmpty(name));
        return string.Join(", ", names);
    }
}
