using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class CultistMod : Mod
{
    public CultistMod(ModContentPack content) : base(content)
    {
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

    public override string SettingsCategory() => !ModsConfig.IdeologyActive || CanOfferJournal
        ? "Cultist.SettingsCategory".Translate()
        : null;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        if (!ModsConfig.IdeologyActive)
            listing.Label("Cultist.IdeologyRequired".Translate());
        else if (CanOfferJournal
            && listing.ButtonText("Cultist.OfferTitle".Translate()))
        {
            Campaign.DebugOfferJournal();
        }
        listing.End();
    }
}
