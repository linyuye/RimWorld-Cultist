using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

public sealed class FoundingWindow : Window
{
    private readonly Campaign campaign;
    private Aspect? chosenPersonal;

    public FoundingWindow(Campaign campaign)
    {
        this.campaign = campaign;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnCancel = false;
        closeOnAccept = false;
    }

    public override Vector2 InitialSize => new(540, 520);

    public override void DoWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        if (chosenPersonal == null)
        {
            listing.Label("Cultist.ChooseAspect".Translate());
            listing.Gap(8);
            foreach (Aspect aspect in System.Enum.GetValues<Aspect>())
            {
                if (aspect == Aspect.SecretHistories)
                    continue;
                if (!listing.ButtonText(("Cultist.Aspect." + aspect).Translate()))
                    continue;
                chosenPersonal = aspect;
                break;
            }
        }
        else
        {
            listing.Label("Cultist.ChooseSocietyPath".Translate(("Cultist.Aspect." + chosenPersonal.Value).Translate()));
            listing.Gap(8);
            foreach (CultPathDef path in DefDatabase<CultPathDef>.AllDefsListForReading.Where(p => p.playable))
            {
                string btn = $"{path.churchNameKey.Translate()} ({("Cultist.Aspect." + path.aspect).Translate()})";
                if (!listing.ButtonText(btn))
                    continue;
                campaign.Found(chosenPersonal.Value, path.aspect);
                Close();
                break;
            }
            listing.Gap(10);
            if (listing.ButtonText("Cultist.DreamBack".Translate()))
                chosenPersonal = null;
        }
        listing.End();
    }
}

public sealed class DreamWindow : Window
{
    private const float MapWidth = 2048f;
    private const float MapHeight = 1152f;
    private static readonly Vector2[] RingPoints = Enumerable.Range(0, 32).Select(i =>
    {
        float angle = i * (2f * System.MathF.PI / 32f);
        return new Vector2(System.MathF.Cos(angle), System.MathF.Sin(angle));
    }).ToArray();

    private static Texture2D MapTex => ContentFinder<Texture2D>.Get("Cultist/UI/MansusMap", false);

    private readonly Campaign campaign;

    public DreamWindow(Campaign campaign)
    {
        this.campaign = campaign;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnCancel = false;
        closeOnAccept = false;
    }

    public override Vector2 InitialSize => new(
        System.MathF.Min(940f, UI.screenWidth - 24f),
        System.MathF.Min(615f, UI.screenHeight - 24f));

    public override void DoWindowContents(Rect inRect)
    {
        int gate = campaign.SelectedDreamGate;
        int maxGate = System.Math.Min(campaign.Marks, 4);
        int region = campaign.SelectedDreamRegion;

        // Header bar
        string header = campaign.Offers is { Count: > 0 }
            ? "Cultist.Mansus.PickCardHeader".Translate(
                ("Cultist.Gate." + gate).Translate(),
                DreamTable.PoolDef(gate, region)?.LabelCap
                    ?? ("Cultist.DreamRegion." + gate + "." + region).Translate().ToString())
            : gate >= 0
                ? "Cultist.Mansus.PickRegionHeader".Translate(("Cultist.Gate." + gate).Translate())
                : "Cultist.Mansus.PickGateHeader".Translate();
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 28f), header);

        var viewport = new Rect(inRect.x, inRect.y + 32f, inRect.width, inRect.height - 70f);
        Rect mapRect = FitMap(viewport);
        float mapScale = mapRect.width / MapWidth;
        Texture2D map = MapTex;
        if (map != null)
            GUI.DrawTexture(mapRect, map, ScaleMode.StretchToFill);
        else
            Widgets.DrawBoxSolid(mapRect, new Color(0.08f, 0.12f, 0.16f));

        if (campaign.Offers is { Count: > 0 })
        {
            DrawCardFlipOverlay(mapRect, campaign.GetLoreDef(DreamTable.LoreTierFor(gate, region)));
            return;
        }

        if (gate < 0)
        {
            for (int g = 0; g < DreamTable.GateCount; g++)
            {
                if (DreamTable.PoolDef(g, 0) is not { } pool)
                    continue;
                bool unlocked = g <= maxGate;
                string label = ("Cultist.Gate." + g).Translate();
                if (DrawMapNode(mapRect, pool, mapScale, label, unlocked, active: false))
                {
                    Campaign.PlayCultistSound("Cultist_Sfx_MansusEnter");
                    campaign.SelectDreamGate(g);
                }
            }
        }
        else
        {
            for (int r = 0; r < DreamTable.RegionCount; r++)
            {
                if (DreamTable.PoolDef(gate, r) is not { } pool)
                    continue;
                if (DrawMapNode(mapRect, pool, mapScale, pool.LabelCap, unlocked: true, active: r == 0))
                    campaign.GenerateOffers(gate, r);
            }

            if (Widgets.ButtonText(new Rect(mapRect.x, inRect.yMax - 32f, 160f, 32f),
                    "Cultist.DreamBack".Translate()))
                campaign.SelectDreamGate(-1);
        }
    }

    private static Rect FitMap(Rect viewport)
    {
        float scale = System.MathF.Min(viewport.width / MapWidth, viewport.height / MapHeight);
        float width = MapWidth * scale;
        float height = MapHeight * scale;
        return new Rect(viewport.center.x - width / 2f, viewport.center.y - height / 2f, width, height);
    }

    private static Rect MapNodeRect(Rect mapRect, MansusPoolDef pool, float mapScale)
    {
        float size = pool.mapDiameter * mapScale;
        float x = mapRect.x + mapRect.width * pool.mapCoords.x;
        float y = mapRect.y + mapRect.height * pool.mapCoords.y;
        return new Rect(x - size / 2f, y - size / 2f, size, size);
    }

    private static bool DrawMapNode(Rect mapRect, MansusPoolDef pool, float mapScale, string label,
        bool unlocked, bool active)
    {
        Rect nodeRect = MapNodeRect(mapRect, pool, mapScale);
        Vector2 center = nodeRect.center;
        float radius = nodeRect.width / 2f;
        Vector2 fromCenter = Event.current.mousePosition - center;
        bool hovered = unlocked && Mouse.IsOver(nodeRect)
            && fromCenter.x * fromCenter.x + fromCenter.y * fromCenter.y <= radius * radius;

        Color outline = !unlocked ? new Color(0.40f, 0.48f, 0.56f, 0.75f)
            : hovered || active ? new Color(1f, 0.91f, 0.53f, 1f)
            : new Color(0.95f, 0.85f, 0.57f, 0.9f);
        float stroke = System.MathF.Max(1f, (hovered ? 7f : 4f) * mapScale);
        for (int i = 0; i < RingPoints.Length; i++)
            Widgets.DrawLine(center + RingPoints[i] * radius,
                center + RingPoints[(i + 1) % RingPoints.Length] * radius, outline, stroke);

        Color prev = GUI.color;
        Vector2 textSize = Text.CalcSize(label);
        float pillW = System.MathF.Min(System.MathF.Max(74f, textSize.x + 14f), mapRect.width);
        float pillX = Mathf.Clamp(center.x - pillW / 2f, mapRect.x, mapRect.xMax - pillW);
        float pillY = System.MathF.Min(nodeRect.yMax + 3f, mapRect.yMax - 22f);
        var pillRect = new Rect(pillX, pillY, pillW, 22f);
        Widgets.DrawBoxSolid(pillRect, unlocked ? new Color(0.06f, 0.09f, 0.13f, 0.85f) : new Color(0.08f, 0.08f, 0.09f, 0.65f));
        TextAnchor oldAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = unlocked ? Color.white : new Color(0.65f, 0.65f, 0.65f);
        Widgets.Label(pillRect, label);
        GUI.color = prev;
        Text.Anchor = oldAnchor;

        return hovered && Widgets.ButtonInvisible(nodeRect);
    }

    private void DrawCardFlipOverlay(Rect mapRect, ThingDef lore)
    {
        Widgets.DrawBoxSolid(mapRect, new Color(0.04f, 0.06f, 0.09f, 0.68f));
        float scale = System.MathF.Min(1f, System.MathF.Min(mapRect.width / 796f, mapRect.height / 334f));
        float cardW = 240f * scale;
        float cardH = 310f * scale;
        float gap = 26f * scale;
        float totalW = cardW * 3f + gap * 2f;
        float startX = mapRect.x + (mapRect.width - totalW) / 2f;
        float startY = mapRect.y + (mapRect.height - cardH) / 2f;

        TextAnchor oldAnchor = Text.Anchor;
        int faceUpCount = 1 + campaign.BonusFaceUpCards;
        for (int i = 0; i < campaign.Offers.Count && i < 3; i++)
        {
            bool faceUp = i < faceUpCount;
            var cardRect = new Rect(startX + i * (cardW + gap), startY, cardW, cardH);
            bool hovered = Mouse.IsOver(cardRect);

            Color bg = faceUp
                ? (hovered ? new Color(0.22f, 0.20f, 0.14f, 0.96f) : new Color(0.16f, 0.15f, 0.11f, 0.94f))
                : (hovered ? new Color(0.14f, 0.22f, 0.28f, 0.96f) : new Color(0.09f, 0.14f, 0.19f, 0.94f));
            Widgets.DrawBoxSolid(cardRect, bg);
            GUI.color = faceUp ? new Color(0.95f, 0.80f, 0.38f) : new Color(0.45f, 0.78f, 0.86f);
            Widgets.DrawBox(cardRect, 2);
            GUI.color = Color.white;

            Rect inner = cardRect.ContractedBy(14f * scale);
            Text.Anchor = TextAnchor.UpperCenter;
            if (!faceUp)
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f),
                    "Cultist.Mansus.CardFaceDown".Translate());
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inner.x, inner.y + 40f, inner.width, inner.height - 96f),
                    DreamTable.RewardLabel(campaign.Offers[i], lore));
            }

            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 36f, inner.width, 34f),
                    (faceUp ? "Cultist.Mansus.TakeKnown" : "Cultist.Mansus.FlipHidden").Translate()))
            {
                DreamReward picked = campaign.Offers[i];
                campaign.TakeReward(i);
                if (!faceUp)
                {
                    Messages.Message("Cultist.Mansus.FlippedResult".Translate(DreamTable.RewardLabel(picked, lore)),
                        MessageTypeDefOf.PositiveEvent);
                }
                Close();
                break;
            }
        }
        Text.Anchor = oldAnchor;
    }
}
