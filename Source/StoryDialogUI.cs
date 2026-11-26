using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Cultist;

public static class CultistDialogBg
{
    public const string Douglas = "Cultist/Dialog/DouglasProfile";
    public const string Connie = "Cultist/Dialog/ConnieLeeProfile";
    public const string Natalia = "Cultist/Dialog/NataliaBrulleauProfile";
    public const string Spencer = "Cultist/Dialog/SpencerHobsonProfile";
    public const string Zachary = "Cultist/Dialog/ZacharyWakefieldProfile";
    public const string Poppy = "Cultist/Dialog/PoppyLascellesProfile";
    public const string Oriflamme = "Cultist/Dialog/OriflammeAuctionBackground";
    public const string RivalLong = "Cultist/Dialog/RivalLongProfile";
    public const string Mansus = "Cultist/Dialog/MansusOracleBackground";
}

public sealed class StoryDialogOption
{
    public string Label;
    public string Tag;
    public Color? TagColor;
    public bool Disabled;
    public string DisabledReason;
    public string NextNodeId;
    public Action OnSelect;
    public bool CloseAfterSelect = true;
}

public sealed class StoryDialogNode
{
    public string NodeId;
    public string Title;
    public string SpeakerName;
    public string SpeakerSubtitle;
    public string PortraitTexPath;
    public string ProfileTexPath;
    public Color AccentColor = new(0.84f, 0.69f, 0.36f);
    public string BodyText;
    public List<StoryDialogOption> Options = new();
}

/// <summary>
/// State-machine driven portrait narrative dialog window built on native Verse.Window.
/// Zero external framework dependencies; safe against UI conflicts with other mods.
/// </summary>
[StaticConstructorOnStartup]
public sealed class CultistStoryDialogWindow : Window
{
    private readonly Dictionary<string, StoryDialogNode> nodes = new();
    private StoryDialogNode currentNode;
    private Vector2 bodyScroll;
    private Texture2D profileTexture;

    private static readonly Color BgDark = new(0.09f, 0.10f, 0.12f, 1f);
    private static readonly Color PanelCard = new(0.13f, 0.14f, 0.17f, 1f);
    private static readonly Color BorderMuted = new(0.32f, 0.30f, 0.25f, 0.9f);
    private static readonly Color BtnNormal = new(0.16f, 0.17f, 0.20f, 1f);
    private static readonly Color BtnHover = new(0.22f, 0.24f, 0.28f, 1f);
    private static readonly Color BtnDisabled = new(0.12f, 0.12f, 0.14f, 0.85f);
    private static readonly Color TextMuted = new(0.72f, 0.70f, 0.66f);
    private static readonly Color DefaultTagColor = new(0.90f, 0.76f, 0.42f);
    private const float ProfileFadeStart = 0.32f;
    private const float ProfileFadeEnd = 0.85f;
    private static Texture2D profileFade;

    public override Vector2 InitialSize
    {
        get
        {
            float width = 760f;
            foreach (StoryDialogNode node in nodes.Values)
            {
                if (!string.IsNullOrEmpty(node.ProfileTexPath))
                {
                    width = 920f;
                    break;
                }
            }

            return new Vector2(Mathf.Min(width, UI.screenWidth - 24f), Mathf.Min(460f, UI.screenHeight - 24f));
        }
    }

    public CultistStoryDialogWindow(IEnumerable<StoryDialogNode> dialogNodes, string startNodeId)
    {
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
        closeOnCancel = false;
        doCloseX = false;
        draggable = true;
        drawShadow = true;

        if (dialogNodes != null)
        {
            foreach (StoryDialogNode node in dialogNodes)
            {
                if (node != null && !string.IsNullOrEmpty(node.NodeId))
                    nodes[node.NodeId] = node;
            }
        }

        TransitionTo(startNodeId);
    }

    public CultistStoryDialogWindow(StoryDialogNode singleNode)
        : this(new[] { singleNode }, singleNode?.NodeId ?? "root")
    {
    }

    private void TransitionTo(string nodeId)
    {
        bodyScroll = Vector2.zero;
        if (!string.IsNullOrEmpty(nodeId) && nodes.TryGetValue(nodeId, out StoryDialogNode next))
            currentNode = next;
        else
            currentNode = null;

        string profilePath = currentNode?.ProfileTexPath;
        profileTexture = string.IsNullOrEmpty(profilePath)
            ? null
            : ContentFinder<Texture2D>.Get(profilePath, reportFailure: false);
    }

    protected override float Margin => 0f;

    public override void DoWindowContents(Rect inRect)
    {
        if (currentNode == null)
        {
            Close();
            return;
        }

        // Background + outer border + top accent strip
        Widgets.DrawBoxSolid(inRect, BgDark);
        GUI.color = BorderMuted;
        Widgets.DrawBox(inRect, 1);
        GUI.color = Color.white;

        Rect topStrip = new(inRect.x, inRect.y, inRect.width, 3f);
        Widgets.DrawBoxSolid(topStrip, currentNode.AccentColor);

        Rect inner = inRect.ContractedBy(18f);
        inner.yMin += 4f;

        // Left column: Portrait card + speaker identity
        float leftWidth = Mathf.Min(212f, inner.width * 0.29f);
        Rect leftRect = new(inner.x, inner.y, leftWidth, inner.height);
        DrawPortraitColumn(leftRect, currentNode);

        // Right column: Header + narrative scroll + decision buttons
        float gap = 18f;
        Rect rightRect = new(leftRect.xMax + gap, inner.y, inner.width - leftWidth - gap, inner.height);
        DrawNarrativeColumn(rightRect, currentNode);
    }

    private static void DrawPortraitColumn(Rect rect, StoryDialogNode node)
    {
        Widgets.DrawBoxSolid(rect, PanelCard);
        GUI.color = BorderMuted;
        Widgets.DrawBox(rect, 1);
        GUI.color = Color.white;

        float pad = 12f;
        float imgSize = rect.width - pad * 2f;
        Rect imgRect = new(rect.x + pad, rect.y + pad, imgSize, imgSize);
        Widgets.DrawBoxSolid(imgRect, new Color(0.06f, 0.07f, 0.09f, 1f));

        Texture2D tex = !string.IsNullOrEmpty(node.PortraitTexPath)
            ? ContentFinder<Texture2D>.Get(node.PortraitTexPath, reportFailure: false)
            : null;
        if (tex != null)
            GUI.DrawTexture(imgRect.ContractedBy(2f), tex, ScaleMode.ScaleToFit);

        GUI.color = new Color(node.AccentColor.r, node.AccentColor.g, node.AccentColor.b, 0.65f);
        Widgets.DrawBox(imgRect, 1);
        GUI.color = Color.white;

        // Speaker name & subtitle
        float textY = imgRect.yMax + 14f;
        Text.Anchor = TextAnchor.UpperCenter;
        Text.Font = GameFont.Medium;
        Rect nameRect = new(rect.x + 8f, textY, rect.width - 16f, 32f);
        Widgets.Label(nameRect, node.SpeakerName ?? string.Empty);

        Text.Font = GameFont.Tiny;
        GUI.color = node.AccentColor;
        Rect subRect = new(rect.x + 8f, nameRect.yMax + 2f, rect.width - 16f, 36f);
        Widgets.Label(subRect, node.SpeakerSubtitle ?? string.Empty);

        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
    }

    private void DrawNarrativeColumn(Rect rect, StoryDialogNode node)
    {
        // Header title
        Text.Font = GameFont.Medium;
        Rect titleRect = new(rect.x, rect.y, rect.width, 32f);
        Widgets.Label(titleRect, node.Title ?? string.Empty);

        Rect divider = new(rect.x, titleRect.yMax + 4f, rect.width, 1f);

        // Calculate bottom decision buttons height
        int optCount = node.Options?.Count ?? 0;
        float btnHeight = 36f;
        float btnGap = 8f;
        float optionsTotalH = optCount > 0 ? optCount * btnHeight + (optCount - 1) * btnGap + 12f : 44f;

        // Scrollable narrative body
        float bodyTop = divider.yMax + 12f;
        float bodyHeight = Mathf.Max(0f, rect.yMax - optionsTotalH - bodyTop);
        float bodyWidth = rect.width;
        if (profileTexture != null && rect.width >= 360f && bodyHeight >= 80f)
        {
            float artHeight = bodyTop + bodyHeight - rect.y;
            float artWidth = Mathf.Min(rect.width * 0.61f, artHeight * profileTexture.width / profileTexture.height);
            artHeight = artWidth * profileTexture.height / profileTexture.width;
            Rect artRect = new(rect.xMax - artWidth, bodyTop + bodyHeight - artHeight, artWidth, artHeight);
            DrawProfile(artRect, profileTexture);
            bodyWidth = rect.width - artWidth * 0.66f;
            divider.width = bodyWidth;
        }

        Widgets.DrawBoxSolid(divider, BorderMuted);
        Rect bodyOuter = new(rect.x, bodyTop, bodyWidth, bodyHeight);
        Text.Font = GameFont.Small;
        float viewW = bodyOuter.width - 16f;
        float textH = Text.CalcHeight(node.BodyText ?? string.Empty, viewW) + 8f;
        Rect viewRect = new(0f, 0f, viewW, Mathf.Max(textH, bodyOuter.height));

        Widgets.BeginScrollView(bodyOuter, ref bodyScroll, viewRect);
        GUI.color = new Color(0.92f, 0.91f, 0.88f);
        Widgets.Label(viewRect, node.BodyText ?? string.Empty);
        GUI.color = Color.white;
        Widgets.EndScrollView();

        // Decision buttons
        float curY = bodyOuter.yMax + 12f;
        if (optCount == 0)
        {
            Rect closeRect = new(rect.x, curY, rect.width, btnHeight);
            if (DrawOptionButton(closeRect, new StoryDialogOption { Label = "OK".Translate() }, node.AccentColor))
                Close();
            return;
        }

        for (int i = 0; i < optCount; i++)
        {
            StoryDialogOption opt = node.Options[i];
            Rect btnRect = new(rect.x, curY, rect.width, btnHeight);
            if (DrawOptionButton(btnRect, opt, node.AccentColor))
            {
                ExecuteOption(opt);
                return;
            }
            curY += btnHeight + btnGap;
        }
    }

    private static void DrawProfile(Rect rect, Texture2D texture)
    {
        if (profileFade == null)
        {
            const int samples = 128;
            profileFade = new Texture2D(samples, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[samples];
            for (int i = 0; i < samples; i++)
                pixels[i] = new Color(BgDark.r, BgDark.g, BgDark.b, 1f - i / (samples - 1f));
            profileFade.SetPixels(pixels);
            profileFade.Apply();
        }

        GUI.DrawTexture(rect, texture);
        float fadeStart = rect.width * ProfileFadeStart;
        Rect hiddenRect = new(rect.x, rect.y, fadeStart, rect.height);
        Widgets.DrawBoxSolid(hiddenRect, BgDark);
        Rect fadeRect = new(rect.x + fadeStart, rect.y, rect.width * (ProfileFadeEnd - ProfileFadeStart), rect.height);
        GUI.DrawTexture(fadeRect, profileFade);
    }

    private static bool DrawOptionButton(Rect rect, StoryDialogOption opt, Color accent)
    {
        bool hovered = !opt.Disabled && Mouse.IsOver(rect);
        Widgets.DrawBoxSolid(rect, opt.Disabled ? BtnDisabled : (hovered ? BtnHover : BtnNormal));

        // Left accent bar
        Rect bar = new(rect.x, rect.y, 3f, rect.height);
        Widgets.DrawBoxSolid(bar, opt.Disabled ? new Color(0.3f, 0.3f, 0.3f) : (hovered ? accent : new Color(accent.r, accent.g, accent.b, 0.55f)));

        GUI.color = hovered ? accent : BorderMuted;
        Widgets.DrawBox(rect, 1);
        GUI.color = Color.white;

        // Left label
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleLeft;
        Rect labelRect = new(rect.x + 14f, rect.y, rect.width - 28f, rect.height);
        string displayLabel = opt.Disabled && !string.IsNullOrEmpty(opt.DisabledReason)
            ? $"{opt.Label}（{opt.DisabledReason}）"
            : opt.Label;
        GUI.color = opt.Disabled ? TextMuted : Color.white;
        Widgets.Label(labelRect, displayLabel ?? string.Empty);

        // Optional right-aligned concise cost/reward tag
        if (!string.IsNullOrEmpty(opt.Tag))
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = opt.Disabled ? TextMuted : (opt.TagColor ?? DefaultTagColor);
            Rect tagRect = new(rect.x + 14f, rect.y, rect.width - 26f, rect.height);
            Widgets.Label(tagRect, opt.Tag);
        }

        GUI.color = Color.white;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;

        if (opt.Disabled)
            return false;

        if (Widgets.ButtonInvisible(rect))
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            return true;
        }
        return false;
    }

    private void ExecuteOption(StoryDialogOption opt)
    {
        if (opt == null)
            return;

        try
        {
            opt.OnSelect?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error($"[Cultist] StoryDialog option error: {ex}");
        }

        if (!string.IsNullOrEmpty(opt.NextNodeId) && nodes.ContainsKey(opt.NextNodeId))
        {
            TransitionTo(opt.NextNodeId);
            return;
        }

        if (opt.CloseAfterSelect)
            Close();
    }
}
