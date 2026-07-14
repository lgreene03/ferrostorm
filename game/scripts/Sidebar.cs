using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Classic right-hand build sidebar in the uplink style (doc 16 palette:
/// cinder panels, seam borders, bone text, ferrite accent). Two sections:
/// STRUCTURES (queued at the Construction Yard, then placed from the ready
/// slot) and UNITS (queued at a Factory). Reads sim state through public
/// accessors only; emits commands through the scene's pending list.
/// </summary>
public partial class Sidebar : PanelContainer
{
    public record BuildItem(string Label, int TypeId, int Cost, string Icon);

    private static readonly BuildItem[] Structures =
    {
        new("POWER PLANT", 1, 300, "com_power_plant"),
        new("REFINERY", 3, 2000, "com_refinery"),
        new("FACTORY", 2, 2000, "com_factory"),
        new("TURRET", 5, 600, "dir_turret"),
        new("SERVICE DEPOT", 8, 1200, "com_service_depot"),
        new("SUPERWEAPON", 6, 4000, "dir_superweapon"),
    };
    private static readonly BuildItem[] Units =
    {
        new("RIFLE SQUAD", 2, 200, "com_rifle_squad"),
        new("ROCKET SQUAD", 3, 300, "com_rocket_squad"),
        new("SENTINEL SCOUT", 6, 400, "dir_sentinel_scout"),
        new("ENGINEER", 11, 500, "com_engineer"),
        new("VANGUARD CAR", 12, 450, "dir_vanguard_car"),
        new("CANNON TANK", 1, 600, "dir_cannon_tank"),
        new("HOWITZER", 8, 900, "dir_howitzer"),
        new("HARVESTER", 4, 1400, "com_harvester"),
        new("BULWARK TANK", 10, 1600, "dir_bulwark_tank"),
        new("MCV", 7, 3000, "com_mcv"),
    };

    private static readonly Color Cinder = new(0.086f, 0.094f, 0.102f);
    private static readonly Color Seam = new(0.18f, 0.196f, 0.21f);
    private static readonly Color Bone = new(0.84f, 0.82f, 0.77f);
    private static readonly Color FerriteGold = new(0.79f, 0.63f, 0.36f);
    private static readonly Color Dim = new(0.45f, 0.44f, 0.42f);

    private SkirmishLive _game = null!;
    private readonly Dictionary<int, Button> _structButtons = new();
    private readonly Dictionary<int, Button> _unitButtons = new();
    private Label _power = null!;
    private Button _placeButton = null!;
    private int _readyType;

    public void Init(SkirmishLive game)
    {
        _game = game;
        CustomMinimumSize = new Vector2(190, 0);
        AnchorLeft = 1; AnchorRight = 1; AnchorBottom = 1;
        OffsetLeft = -190;
        var panelStyle = new StyleBoxFlat { BgColor = Cinder with { A = 0.92f }, BorderColor = Seam };
        panelStyle.SetBorderWidthAll(1);
        AddThemeStyleboxOverride("panel", panelStyle);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        AddChild(v);

        v.AddChild(Header("FERROSTORM UPLINK"));
        _power = new Label { Text = "PWR --" };
        _power.AddThemeColorOverride("font_color", FerriteGold);
        _power.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(_power);

        v.AddChild(Header("STRUCTURES"));
        foreach (var it in Structures)
        {
            var b = MakeButton(it, () => _game.QueueStructure(it.TypeId));
            // Classic campaign tech gating: disallowed items are absent,
            // not greyed - progression should read as the tree growing.
            b.Visible = MatchConfig.AllowedStructures?.Contains(it.TypeId) ?? true;
            _structButtons[it.TypeId] = b;
            v.AddChild(b);
        }
        _placeButton = MakeButton(new BuildItem("PLACE >>", 0, 0, ""), () => _game.EnterPlacement(_readyType));
        _placeButton.Visible = false;
        v.AddChild(_placeButton);

        v.AddChild(Header("UNITS"));
        foreach (var it in Units)
        {
            var b = MakeButton(it, () => _game.QueueUnit(it.TypeId));
            b.Visible = MatchConfig.AllowedUnits?.Contains(it.TypeId) ?? true;
            _unitButtons[it.TypeId] = b;
            v.AddChild(b);
        }
    }

    private static Label Header(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", Dim);
        l.AddThemeFontSizeOverride("font_size", 11);
        return l;
    }

    private Button MakeButton(BuildItem it, System.Action onPress)
    {
        var b = new Button
        {
            Text = it.Cost > 0 ? $"{it.Label}  {it.Cost}" : it.Label,
            Alignment = HorizontalAlignment.Left,
        };
        if (it.Icon.Length > 0 && ResourceLoader.Exists($"res://ui/icons/{it.Icon}.png"))
        {
            b.Icon = GD.Load<Texture2D>($"res://ui/icons/{it.Icon}.png");
            b.ExpandIcon = true;
            b.AddThemeConstantOverride("icon_max_width", 26);
        }
        b.AddThemeFontSizeOverride("font_size", 12);
        b.AddThemeColorOverride("font_color", Bone);
        var normal = new StyleBoxFlat { BgColor = new Color(0.12f, 0.13f, 0.14f), BorderColor = Seam };
        normal.SetBorderWidthAll(1);
        normal.ContentMarginLeft = 8; normal.ContentMarginTop = 5; normal.ContentMarginBottom = 5;
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.16f, 0.17f, 0.19f);
        hover.BorderColor = FerriteGold;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", hover);
        b.AddThemeStyleboxOverride("pressed", hover);
        b.Pressed += () => onPress();
        return b;
    }

    /// <summary>Called by the scene each frame with fresh sim reads.</summary>
    public void Refresh(long credits, int readyStructureType, bool hasFactory, bool hasYard, int yardQueue, int factoryQueue)
    {
        _readyType = readyStructureType;
        _placeButton.Visible = readyStructureType > 0;
        if (readyStructureType > 0)
            _placeButton.Text = $"PLACE {NameOf(readyStructureType)} >>";
        foreach (var (typeId, b) in _structButtons)
        {
            var def = World.GetStructureType(typeId);
            b.Disabled = !hasYard || readyStructureType > 0 || credits < def.Cost;
        }
        foreach (var (typeId, b) in _unitButtons)
            b.Disabled = !hasFactory;
        _power.Text = $"CREDITS {credits}    CY Q{yardQueue}  FAC Q{factoryQueue}";
    }

    private static string NameOf(int structType)
    {
        foreach (var it in Structures) if (it.TypeId == structType) return it.Label;
        return "STRUCTURE";
    }
}
