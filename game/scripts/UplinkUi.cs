using Godot;
using System;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SAVE-01: the uplink overlay chrome, factored out of MainMenu so
/// the pause menu wears the same clothes rather than a third hand-copied set
/// of StyleBoxFlats. The palette is doc 16's: cinder panels, seam borders,
/// bone text, ferrite accent.
/// </summary>
public static class UplinkUi
{
    public static readonly Color Cinder = new(0.055f, 0.06f, 0.065f);
    public static readonly Color Panel = new(0.086f, 0.094f, 0.102f);
    public static readonly Color Seam = new(0.18f, 0.196f, 0.21f);
    public static readonly Color Bone = new(0.84f, 0.82f, 0.77f);
    public static readonly Color FerriteGold = new(0.79f, 0.63f, 0.36f);
    public static readonly Color Dim = new(0.45f, 0.44f, 0.42f);

    /// <summary>A full-bleed scrim. Its default MouseFilter of Stop is the
    /// point as much as the colour is: over a live battle it is what keeps a
    /// click on RESUME from also ordering the army underneath it.</summary>
    public static ColorRect FullOverlay(Node parent, float alpha = 0.97f)
    {
        var overlay = new ColorRect
        {
            Name = "Overlay",
            Color = Cinder with { A = alpha },
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        parent.AddChild(overlay);
        return overlay;
    }

    public static VBoxContainer OverlayBox(Control overlay, string heading, int halfW = 240, int halfH = 220)
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -halfW, OffsetRight = halfW, OffsetTop = -halfH, OffsetBottom = halfH,
        };
        var style = new StyleBoxFlat { BgColor = Panel, BorderColor = Seam };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 24; style.ContentMarginRight = 24;
        style.ContentMarginTop = 18; style.ContentMarginBottom = 18;
        panel.AddThemeStyleboxOverride("panel", style);
        overlay.AddChild(panel);
        var v = new VBoxContainer();
        // Centre the block: a PanelContainer stretches its only child to fill,
        // so a short list (one replay, four slots) otherwise sits jammed against
        // the heading with a void beneath it.
        v.Alignment = BoxContainer.AlignmentMode.Center;
        v.AddThemeConstantOverride("separation", 8);
        panel.AddChild(v);
        var h = new Label { Text = heading, HorizontalAlignment = HorizontalAlignment.Center };
        h.AddThemeFontSizeOverride("font_size", 22);
        h.AddThemeColorOverride("font_color", FerriteGold);
        v.AddChild(h);
        v.AddChild(new HSeparator());
        return v;
    }

    public static Button MenuButton(string text, Action onPress, bool enabled = true)
    {
        var b = new Button { Text = text, Disabled = !enabled };
        b.AddThemeColorOverride("font_color", Bone);
        b.AddThemeColorOverride("font_disabled_color", Dim);
        var normal = new StyleBoxFlat { BgColor = new Color(0.12f, 0.13f, 0.14f), BorderColor = Seam };
        normal.SetBorderWidthAll(1);
        normal.ContentMarginTop = 8; normal.ContentMarginBottom = 8;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BorderColor = FerriteGold;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.AddThemeStyleboxOverride("disabled", normal);
        b.Pressed += () => onPress();
        return b;
    }

    /// <summary>A dim caption line: the place for an honest caveat the player
    /// needs to read before pressing the button above it.</summary>
    public static Label Note(string text, int size = 11)
    {
        var l = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", Dim);
        return l;
    }
}
