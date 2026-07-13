using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Entry point in the uplink style: skirmish setup (map, AI temperament,
/// starting credits), the replay theatre, and quit. Choices flow into
/// SkirmishLive via MatchConfig before the scene change.
/// </summary>
public partial class MainMenu : Control
{
    private static readonly Color Cinder = new(0.055f, 0.06f, 0.065f);
    private static readonly Color Seam = new(0.18f, 0.196f, 0.21f);
    private static readonly Color Bone = new(0.84f, 0.82f, 0.77f);
    private static readonly Color FerriteGold = new(0.79f, 0.63f, 0.36f);

    private OptionButton _mapPick = null!;
    private OptionButton _aiPick = null!;
    private OptionButton _creditPick = null!;
    private readonly List<string> _maps = new();

    public override void _Ready()
    {
        AnchorRight = 1; AnchorBottom = 1;
        var bg = new ColorRect { Color = Cinder, AnchorRight = 1, AnchorBottom = 1 };
        AddChild(bg);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -190, OffsetBottom = 190,
        };
        var style = new StyleBoxFlat { BgColor = new Color(0.086f, 0.094f, 0.102f), BorderColor = Seam };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 28; style.ContentMarginRight = 28;
        style.ContentMarginTop = 22; style.ContentMarginBottom = 22;
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);

        var title = new Label { Text = "FERROSTORM", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", FerriteGold);
        v.AddChild(title);
        var sub = new Label { Text = "UPLINK ESTABLISHED", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeFontSizeOverride("font_size", 11);
        sub.AddThemeColorOverride("font_color", new Color(0.45f, 0.44f, 0.42f));
        v.AddChild(sub);
        v.AddChild(new HSeparator());

        _mapPick = Row(v, "THEATRE");
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.GetFullPath(
            System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data", "maps")), "*.fmap"))
        {
            _maps.Add(f);
            _mapPick.AddItem(System.IO.Path.GetFileNameWithoutExtension(f).ToUpperInvariant());
        }
        _aiPick = Row(v, "OPPOSITION");
        _aiPick.AddItem("STANDARD"); _aiPick.AddItem("RUSHER"); _aiPick.AddItem("TURTLE");
        _creditPick = Row(v, "TREASURY");
        _creditPick.AddItem("5000"); _creditPick.AddItem("8000"); _creditPick.AddItem("12000");
        _creditPick.Select(1);

        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("COMMENCE OPERATION", StartSkirmish));
        v.AddChild(MenuButton("REPLAY THEATRE", () => GetTree().ChangeSceneToFile("res://scenes/Battle3D.tscn")));
        v.AddChild(MenuButton("STAND DOWN", () => GetTree().Quit()));
    }

    private static OptionButton Row(VBoxContainer parent, string label)
    {
        var h = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.45f, 0.44f, 0.42f));
        h.AddChild(l);
        var opt = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        h.AddChild(opt);
        parent.AddChild(h);
        return opt;
    }

    private Button MenuButton(string text, System.Action onPress)
    {
        var b = new Button { Text = text };
        b.AddThemeColorOverride("font_color", Bone);
        var normal = new StyleBoxFlat { BgColor = new Color(0.12f, 0.13f, 0.14f), BorderColor = Seam };
        normal.SetBorderWidthAll(1);
        normal.ContentMarginTop = 8; normal.ContentMarginBottom = 8;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BorderColor = FerriteGold;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.Pressed += () => onPress();
        return b;
    }

    private void StartSkirmish()
    {
        MatchConfig.MapPath = _maps.Count > 0 ? _maps[_mapPick.Selected] : null;
        MatchConfig.AiPreset = _aiPick.Selected;
        MatchConfig.StartCredits = long.Parse(_creditPick.GetItemText(_creditPick.Selected));
        GetTree().ChangeSceneToFile("res://scenes/Skirmish.tscn");
    }
}

/// <summary>Match options carried from the menu into the battle scene.</summary>
public static class MatchConfig
{
    public static string? MapPath;
    public static int AiPreset;         // 0 standard, 1 rusher, 2 turtle
    public static long StartCredits = 8000;
}
