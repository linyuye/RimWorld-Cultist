using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Cultist;

/// <summary>
/// Renders Cultist aspects using RimWorld's native favorite-color swatch idiom:
/// a crisp pure-color square with border and hover tooltip showing aspect name, tier/power, and description.
/// </summary>
public static class AspectBadgeUI
{
    public static Color ColorFor(Aspect aspect) => aspect switch
    {
        Aspect.Lantern => new Color(0.96f, 0.83f, 0.26f),
        Aspect.Forge => new Color(0.94f, 0.41f, 0.14f),
        Aspect.Edge => new Color(0.56f, 0.66f, 0.26f),
        Aspect.Winter => new Color(0.66f, 0.83f, 0.90f),
        Aspect.Heart => new Color(0.85f, 0.29f, 0.42f),
        Aspect.Grail => new Color(0.72f, 0.11f, 0.23f),
        Aspect.Moth => new Color(0.79f, 0.73f, 0.48f),
        Aspect.Knock => new Color(0.55f, 0.28f, 0.72f),
        _ => new Color(0.77f, 0.37f, 0.66f),
    };

    public static void DrawSwatch(Rect squareRect, Aspect aspect, string tooltip = null)
    {
        Widgets.DrawBoxSolid(squareRect, ColorFor(aspect));
        GUI.color = new Color(0.08f, 0.08f, 0.08f, 0.85f);
        Widgets.DrawBox(squareRect, 1);
        GUI.color = Color.white;

        if (!string.IsNullOrEmpty(tooltip))
            TooltipHandler.TipRegion(squareRect, tooltip);
    }

    public static float DrawSwatchWithValue(float x, float y, Aspect aspect, string valueText, string tooltip = null, Color? textColor = null)
    {
        const float size = 16f;
        Rect swatch = new(x, y + 3f, size, size);
        string tip = tooltip ?? DefaultTooltip(aspect, valueText);
        DrawSwatch(swatch, aspect, tip);

        Text.Font = GameFont.Small;
        float textW = Text.CalcSize(valueText ?? string.Empty).x + 6f;
        Rect labelRect = new(swatch.xMax + 4f, y, textW, 22f);
        GUI.color = textColor ?? Color.white;
        Widgets.Label(labelRect, valueText ?? string.Empty);
        GUI.color = Color.white;

        TooltipHandler.TipRegion(new Rect(x, y, size + 4f + textW, 22f), tip);
        return size + 4f + textW + 8f;
    }

    public static string DefaultTooltip(Aspect aspect, string valueText = null)
    {
        string name = ("Cultist.Aspect." + aspect).Translate();
        string desc = $"Cultist.AspectDesc.{aspect}".Translate();
        return string.IsNullOrEmpty(valueText)
            ? $"{name}\n{desc}"
            : $"{name} {valueText}\n{desc}";
    }
}

/// <summary>
/// Native RimWorld ITab attached to Lore Books, Ritual Tools, and Rite Scripts,
/// displaying pure-color aspect swatches with hover tooltips (modeled after vanilla Favorite Color).
/// </summary>
public sealed class ITab_CultistAspect : ITab
{
    public ITab_CultistAspect()
    {
        size = new Vector2(300f, 175f);
        labelKey = "Cultist.TabAspect";
    }

    public override bool IsVisible =>
        SelThing?.TryGetComp<RitualMaterial>() is { DisplayTier: > 0 };

    protected override void FillTab()
    {
        RitualMaterial mat = SelThing?.TryGetComp<RitualMaterial>();
        if (mat == null)
            return;

        Rect inner = new Rect(0f, 0f, size.x, size.y).ContractedBy(14f);
        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inner.x, inner.y, inner.width, 28f), SelThing.LabelCap);

        Text.Font = GameFont.Small;
        float rowY = inner.y + 36f;
        string aspectName = ("Cultist.Aspect." + mat.Aspect).Translate();
        string tooltip = mat.LoreTier > 0
            ? $"{aspectName} · {"Cultist.AspectTierPower".Translate(mat.LoreTier, mat.AspectValue)}\n{"Cultist.AspectDesc." + mat.Aspect}"
            : $"{aspectName} · {"Cultist.AspectTierPower".Translate(mat.DisplayTier, mat.AspectValue)}\n{"Cultist.AspectDesc." + mat.Aspect}";
        tooltip = mat.LoreTier > 0
            ? $"{aspectName} · {"Cultist.AspectTierPower".Translate(mat.LoreTier, mat.AspectValue)}\n{("Cultist.AspectDesc." + mat.Aspect).Translate()}"
            : $"{aspectName} · {"Cultist.AspectTierPower".Translate(mat.DisplayTier, mat.AspectValue)}\n{("Cultist.AspectDesc." + mat.Aspect).Translate()}";

        // Pure-color aspect swatch (vanilla Favorite Color style) + tier/strength
        Rect swatch = new(inner.x, rowY + 2f, 20f, 20f);
        AspectBadgeUI.DrawSwatch(swatch, mat.Aspect, tooltip);

        Rect textRect = new(swatch.xMax + 8f, rowY, inner.width - 28f, 24f);
        Widgets.Label(textRect, $"{aspectName} {mat.DisplayTier}（{"Cultist.AspectStrength".Translate(mat.AspectValue)}）");
        TooltipHandler.TipRegion(new Rect(inner.x, rowY, inner.width, 24f), tooltip);

        // Concise usage hint
        GUI.color = new Color(0.76f, 0.76f, 0.76f);
        Text.Font = GameFont.Tiny;
        string hint = mat.LoreTier > 0
            ? "Cultist.LoreInspect".Translate(mat.LoreTier, mat.AspectValue, aspectName)
            : "Cultist.ToolInspect".Translate(mat.ToolLight, aspectName);
        Widgets.Label(new Rect(inner.x, rowY + 34f, inner.width, inner.height - 72f), hint);
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }
}
