using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class CultistSettings : ModSettings
{
    public bool enableExperimental;
    public bool useCustomApparelTextures = true;
    public bool enableEasterEggs = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref enableExperimental, "enableExperimental", false);
        Scribe_Values.Look(ref useCustomApparelTextures, "useCustomApparelTextures", true);
        Scribe_Values.Look(ref enableEasterEggs, "enableEasterEggs", true);
    }
}

public sealed class CultistMod : Mod
{
    public static CultistSettings Settings { get; private set; }

    private static readonly (string DefName, string CustomPath, string VanillaPath)[] ApparelTexturePairs =
    {
        ("Cultist_FieldCoat", "Cultist/Apparel/FieldCoat/FieldCoat", "Things/Pawn/Humanlike/Apparel/ShirtBasic/ShirtBasic"),
        ("Cultist_ForgeCoat", "Cultist/Apparel/ForgeCoat/ForgeCoat", "Things/Pawn/Humanlike/Apparel/Duster/Duster"),
        ("Cultist_EdgeGarb", "Cultist/Apparel/EdgeGarb/EdgeGarb", "Things/Pawn/Humanlike/Apparel/ShirtBasic/ShirtBasic"),
        ("Cultist_WinterRobe", "Cultist/Apparel/WinterRobe/WinterRobe", "Things/Pawn/Humanlike/Apparel/Parka/Parka"),
        ("Cultist_HeartVestments", "Cultist/Apparel/HeartVestments/HeartVestments", "Things/Pawn/Humanlike/Apparel/ShirtBasic/ShirtBasic"),
        ("Cultist_GrailRobe", "Cultist/Apparel/GrailRobe/GrailRobe", "Things/Pawn/Humanlike/Apparel/Duster/Duster"),
        ("Cultist_MothCloak", "Cultist/Apparel/MothCloak/MothCloak", "Things/Pawn/Humanlike/Apparel/Duster/Duster"),
        ("Cultist_KnockRobe", "Cultist/Apparel/KnockRobe/KnockRobe", "Things/Pawn/Humanlike/Apparel/Duster/Duster"),
        ("Cultist_MothMask", "Cultist/Apparel/MothMask/MothMask", "Things/Pawn/Humanlike/Apparel/WarMask/WarMask"),
    };

    public CultistMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<CultistSettings>();

        if (!ModsConfig.IdeologyActive)
        {
            Log.Warning("[Cultist] Ideology is required and currently inactive.");
            return;
        }

        Log.Message("[Cultist] Assembly loaded. Ideology is active.");
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            var archivist = DefDatabase<StorytellerDef>.GetNamed("Cultist_Archivist");
            var cassandra = DefDatabase<StorytellerDef>.GetNamed("Cassandra");
            archivist.comps = new List<StorytellerCompProperties>(cassandra.comps);

            ApplyApparelTextureSetting(refreshPawns: false);

            var research = DefDatabase<ResearchProjectDef>.GetNamed("Cultist_StudiesOfLight");
            var coat = DefDatabase<ThingDef>.GetNamed("Cultist_FieldCoat");
            var engraver = DefDatabase<ThingDef>.GetNamed("Cultist_Engraver");
            var station = DefDatabase<SitePartDef>.GetNamed("Cultist_AbandonedObservatory");
            Log.Message($"[Cultist] Content loaded: {research.label}, {coat.label}, {engraver.label}, {station.label}.");
            bool soundtrack = ContentFinder<AudioClip>.Get("Cultist/ObservatoryAfterDusk", false) != null;
            bool sting = ContentFinder<AudioClip>.Get("Cultist/DreamSting", false) != null;
            bool portrait = ContentFinder<Texture2D>.Get("Cultist/Archivist", false) != null;
            Log.Message($"[Cultist] Asset lookup: soundtrack={soundtrack}, dream sound={sting}, portrait={portrait}.");
        });
    }

    private static bool CanOfferJournal => ModsConfig.IdeologyActive && Prefs.DevMode
        && Campaign.Current?.Stage == CampaignStage.Waiting;

    public override string SettingsCategory() => "Cultist.SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        if (!ModsConfig.IdeologyActive)
        {
            listing.Label("Cultist.IdeologyRequired".Translate());
            listing.GapLine();
        }

        bool prevApparel = Settings.useCustomApparelTextures;

        listing.CheckboxLabeled(
            "Cultist.Settings.Experimental".Translate(),
            ref Settings.enableExperimental,
            "Cultist.Settings.ExperimentalDesc".Translate());

        listing.CheckboxLabeled(
            "Cultist.Settings.CustomApparel".Translate(),
            ref Settings.useCustomApparelTextures,
            "Cultist.Settings.CustomApparelDesc".Translate());

        listing.CheckboxLabeled(
            "Cultist.Settings.EasterEggs".Translate(),
            ref Settings.enableEasterEggs,
            "Cultist.Settings.EasterEggsDesc".Translate());

        if (prevApparel != Settings.useCustomApparelTextures)
            ApplyApparelTextureSetting(refreshPawns: true);

        if (CanOfferJournal)
        {
            listing.GapLine();
            if (listing.ButtonText("Cultist.OfferTitle".Translate()))
                Campaign.DebugOfferJournal();
        }

        listing.End();
    }

    public static void ApplyApparelTextureSetting(bool refreshPawns)
    {
        bool useCustom = Settings?.useCustomApparelTextures ?? true;
        foreach (var (defName, customPath, vanillaPath) in ApparelTexturePairs)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def?.apparel == null)
                continue;
            def.apparel.wornGraphicPath = useCustom ? customPath : vanillaPath;
        }

        if (!refreshPawns || Current.ProgramState != ProgramState.Playing || Find.Maps == null)
            return;

        foreach (Map map in Find.Maps)
        {
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.RaceProps?.Humanlike == true)
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }
    }
}
