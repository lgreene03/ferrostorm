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
        // TICKET-P5-PROD-01: the Sodality's signature building, fully
        // implemented in the sim since P2 and never buildable by a person.
        // The button is gated on faction in Init - absent for the
        // Directorate, exactly as the sim itself refuses the command
        // (World.cs BuildStructure's faction check, which stays authoritative).
        new("VEIL PROJECTOR", VeilType, 1500, "sod_veil_projector"),
        new("SUPERWEAPON", 6, 4000, "dir_superweapon"),
        // TICKET-P5-DEF-08 clause 9. ADR-005 clause 3: a barrier has no ready
        // slot and no build time, so it is never queued at the yard - the button
        // enters placement directly and the treasury is charged per segment as
        // it lands.
        new("WALL", BarrierType, 100, "com_wall_straight"),
    };
    /// <summary>ADR-005 reserves struct type 9 for the wall segment (10 is the
    /// deferred gate).</summary>
    private const int BarrierType = 9;
    /// <summary>Struct type 7, the Veil Projector - Sodality doctrine
    /// (TICKET-P5-PROD-01; the sim's gate lives in World.cs BuildStructure).</summary>
    private const int VeilType = 7;
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
    // BD-10: the same two values the health bars already use (SkirmishLive
    // FillAmber/FillRed). Not new colours - doc 16 is a closed palette.
    private static readonly Color FillAmber = new(0.90f, 0.68f, 0.22f);
    private static readonly Color FillRed = new(0.85f, 0.28f, 0.20f);

    private SkirmishLive _game = null!;
    private readonly Dictionary<int, Button> _structButtons = new();
    private readonly Dictionary<int, Button> _unitButtons = new();
    private readonly Dictionary<Button, string> _baseText = new();
    private Label _powerLabel = null!;
    private Control _powerBar = null!;
    private ColorRect _powerFill = null!;
    private ColorRect _powerTick = null!;
    private Tween? _powerPulse;   // BD-10: brown-out pulse; killed on recovery
    private Label _structHeader = null!;
    private Label _unitHeader = null!;
    private Button _placeButton = null!;
    private int _readyType;
    private Tween? _placePulse;   // W3-16: PLACE-button ready pulse

    // BD-02: build time is sim data and the sidebar has no World, so it is
    // handed the two catalogue reads it needs rather than reaching for a static.
    // BD-06 made GetStructureType an instance method, which is why the structure
    // side is a delegate too: a live match may register its own catalogue.
    private System.Func<int, int> _unitBuildTicks = _ => 0;
    private System.Func<int, World.StructureTypeDef> _structDef = World.DefaultStructureType;

    private const float BarWidth = 174f;

    public void Init(SkirmishLive game, System.Func<int, int> unitBuildTicks,
        System.Func<int, World.StructureTypeDef> structDef)
    {
        _game = game;
        _unitBuildTicks = unitBuildTicks;
        _structDef = structDef;
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

        // BD-10. GDD s5 asks for total supply against draw as a bar. What stood
        // here was a label named _power that rendered the credits total a second
        // time (the status line already has it) and two queue counters. The bar
        // is the supply fill with a seam-coloured tick marking the draw line:
        // the classic supply-bar-with-demand-marker, so headroom is a glance.
        _powerLabel = new Label { Text = "POWER 0 / 0" };
        _powerLabel.AddThemeColorOverride("font_color", Bone);
        _powerLabel.AddThemeFontSizeOverride("font_size", 12);
        v.AddChild(_powerLabel);
        _powerBar = new Control { CustomMinimumSize = new Vector2(BarWidth, 8) };
        var powerBack = new ColorRect { Color = Cinder, Position = Vector2.Zero, Size = new Vector2(BarWidth, 8) };
        _powerBar.AddChild(powerBack);
        _powerFill = new ColorRect { Color = FerriteGold, Position = Vector2.Zero, Size = new Vector2(0, 8) };
        _powerBar.AddChild(_powerFill);
        _powerTick = new ColorRect { Color = Seam, Position = Vector2.Zero, Size = new Vector2(2, 8) };
        _powerBar.AddChild(_powerTick);
        v.AddChild(_powerBar);

        _structHeader = Header("STRUCTURES");
        v.AddChild(_structHeader);
        foreach (var it in Structures)
        {
            // DEF-08 clause 9: a barrier bypasses the yard queue entirely, so
            // its button enters placement rather than queueing.
            var b = it.TypeId == BarrierType
                ? MakeButton(it, () => _game.EnterPlacement(BarrierType), _structDef(it.TypeId).BuildTicks)
                : MakeButton(it, () => _game.QueueStructure(it.TypeId), _structDef(it.TypeId).BuildTicks);
            // Classic campaign tech gating: disallowed items are absent,
            // not greyed - progression should read as the tree growing.
            // TICKET-P5-PROD-01: the faction gate reads the same shape - the
            // Veil Projector button exists only for a Sodality player 0,
            // mirroring the sim's own refusal rather than second-guessing it
            // (the sim's check is untouched and still refuses a hand-crafted
            // command). Faction is map content, set before tick 0 and never
            // mutated mid-match, so an Init-time read is sound.
            b.Visible = (MatchConfig.AllowedStructures?.Contains(it.TypeId) ?? true)
                && (it.TypeId != VeilType || _game.FactionOf(0) == World.FactionSodality);
            _structButtons[it.TypeId] = b;
            v.AddChild(b);
        }
        _placeButton = MakeButton(new BuildItem("PLACE >>", 0, 0, ""), () => _game.EnterPlacement(_readyType));
        _placeButton.Visible = false;
        v.AddChild(_placeButton);

        _unitHeader = Header("UNITS");
        v.AddChild(_unitHeader);
        foreach (var it in Units)
        {
            var b = MakeButton(it, () => _game.QueueUnit(it.TypeId), _unitBuildTicks(it.TypeId));
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

    /// <summary>BD-02: the label carries cost AND build time. No build time was
    /// shown anywhere in the game, so the player could not tell a 6.7-second
    /// plant from a 20-second refinery except by watching one. Seconds, not
    /// ticks: ticks are the sim's unit, seconds are the player's.</summary>
    private Button MakeButton(BuildItem it, System.Action onPress, int buildTicks = 0)
    {
        string label = it.Cost > 0 ? $"{it.Label}  {it.Cost}" : it.Label;
        // A zero-tick item is not instant, it is not queued at all (the barrier
        // is bought and placed outright), so a "0s" readout would be a lie.
        if (buildTicks > 0) label += $"  {buildTicks / (float)World.TicksPerSecond:0.#}s";
        var b = new Button
        {
            Text = label,
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
        // W3-16: a real pressed state (darker gold, thicker border) so clicks
        // give feedback, and a disabled state so unaffordable items read dim.
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(0.23f, 0.19f, 0.11f);
        pressed.BorderColor = FerriteGold;
        pressed.SetBorderWidthAll(2);
        b.AddThemeStyleboxOverride("pressed", pressed);
        var disabled = (StyleBoxFlat)normal.Duplicate();
        disabled.BgColor = new Color(0.075f, 0.08f, 0.085f);
        disabled.BorderColor = new Color(0.12f, 0.13f, 0.14f);
        b.AddThemeStyleboxOverride("disabled", disabled);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.35f, 0.34f, 0.32f));
        // W3-15: build-progress overlay on the queue head; Refresh drives
        // OffsetRight from 0 to the button width as the head builds.
        var fill = new ColorRect
        {
            Name = "Fill",
            Color = new Color(0.79f, 0.63f, 0.36f, 0.20f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fill.AnchorTop = 0; fill.AnchorBottom = 1; fill.AnchorLeft = 0; fill.AnchorRight = 0;
        fill.OffsetRight = 0;
        b.AddChild(fill);
        _baseText[b] = b.Text;
        b.Pressed += () => onPress();
        return b;
    }

    /// <summary>Called by the scene each frame with fresh sim reads. W3-15:
    /// queue contents drive per-button count badges and a progress fill on
    /// the queue head (the classic clock substitute).</summary>
    public void Refresh(long credits, int readyStructureType, bool hasFactory, bool hasYard,
        IReadOnlyList<int> yardQ, IReadOnlyList<int> facQ, float yardProgress, float facProgress,
        int supply, int draw)
    {
        RefreshPower(supply, draw);
        _readyType = readyStructureType;
        _placeButton.Visible = readyStructureType > 0;
        if (readyStructureType > 0)
            _placeButton.Text = $"PLACE {NameOf(readyStructureType)} >>";
        // W3-16: the ready-to-place state is the most important sidebar
        // prompt in the classic loop; pulse until the structure is placed.
        if (readyStructureType > 0 && _placePulse == null)
        {
            _placePulse = _placeButton.CreateTween().SetLoops();
            _placePulse.TweenProperty(_placeButton, "modulate", new Color(1f, 0.88f, 0.62f), 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _placePulse.TweenProperty(_placeButton, "modulate", Colors.White, 0.5f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        else if (readyStructureType == 0 && _placePulse != null)
        {
            _placePulse.Kill();
            _placePulse = null;
            _placeButton.Modulate = Colors.White;
        }
        var structCounts = new Dictionary<int, int>();
        foreach (int t in yardQ) structCounts[t] = structCounts.GetValueOrDefault(t) + 1;
        foreach (var (typeId, b) in _structButtons)
        {
            var def = _structDef(typeId);
            // DEF-08 clause 9: a full ready slot pauses the yard's queue and so
            // disables the queued structures, but a barrier never enters that
            // slot - it stays buildable while a finished structure waits.
            b.Disabled = !hasYard || credits < def.Cost
                         || (typeId != BarrierType && readyStructureType > 0);
            int n = structCounts.GetValueOrDefault(typeId);
            b.Text = _baseText[b] + QueueSuffix(n, yardQ.Count > 0 && typeId == yardQ[0],
                def.BuildTicks, yardProgress);
            ((ColorRect)b.GetNode("Fill")).OffsetRight =
                yardQ.Count > 0 && typeId == yardQ[0] ? b.Size.X * yardProgress : 0;
        }
        var unitCounts = new Dictionary<int, int>();
        foreach (int t in facQ) unitCounts[t] = unitCounts.GetValueOrDefault(t) + 1;
        foreach (var (typeId, b) in _unitButtons)
        {
            b.Disabled = !hasFactory;
            int n = unitCounts.GetValueOrDefault(typeId);
            b.Text = _baseText[b] + QueueSuffix(n, facQ.Count > 0 && typeId == facQ[0],
                _unitBuildTicks(typeId), facProgress);
            ((ColorRect)b.GetNode("Fill")).OffsetRight =
                facQ.Count > 0 && typeId == facQ[0] ? b.Size.X * facProgress : 0;
        }
        ((ColorRect)_placeButton.GetNode("Fill")).OffsetRight = 0;
        // BD-10 clause 5: the queue counters move onto the section headers and
        // the duplicated credits total goes. Credits appear once, on the status
        // line, where they were already.
        _structHeader.Text = yardQ.Count > 0 ? $"STRUCTURES   Q{yardQ.Count}" : "STRUCTURES";
        _unitHeader.Text = facQ.Count > 0 ? $"UNITS   Q{facQ.Count}" : "UNITS";
    }

    /// <summary>BD-02 clause 3: the queue head also shows the seconds left on
    /// it, so the player can decide whether to wait. Everything behind the head
    /// shows only its count: the sim builds one item at a time per producer, so
    /// a countdown on a queued-but-not-started item would be fiction.</summary>
    private static string QueueSuffix(int n, bool isHead, int totalTicks, float progress)
    {
        if (n <= 0) return "";
        if (!isHead || totalTicks <= 0) return $"  x{n}";
        int remain = Mathf.CeilToInt(totalTicks * (1f - progress) / World.TicksPerSecond);
        return n > 1 ? $"  {remain}s  x{n}" : $"  {remain}s";
    }

    /// <summary>
    /// BD-10. The fill is supply, the tick is draw, and both are scaled to
    /// whichever is larger so the relationship stays readable at any base size.
    /// Colour is headroom: gold while supply covers draw, amber down to 75 per
    /// cent (the level GDD s5 says turrets survive to), red below it, where the
    /// label pulses. Every colour is an existing doc 16 token, so no style-bible
    /// amendment is owed for this ticket.
    /// </summary>
    private void RefreshPower(int supply, int draw)
    {
        _powerLabel.Text = $"POWER {supply} / {draw}";
        int span = Mathf.Max(Mathf.Max(supply, draw), 1);
        _powerFill.Size = new Vector2(BarWidth * supply / span, 8);
        _powerTick.Position = new Vector2(Mathf.Min(BarWidth * draw / span, BarWidth - 2), 0);
        _powerTick.Visible = draw > 0;
        bool brownOut = draw > 0 && supply * 4 < draw * 3;   // integer maths: below 75 per cent
        _powerFill.Color = supply >= draw ? FerriteGold
            : brownOut ? FillRed : FillAmber;
        // The pulse follows the exact lifecycle of the PLACE-button tween above,
        // including nulling the field on kill: a looping tween left behind on a
        // state change leaks and keeps writing to modulate forever.
        if (brownOut && _powerPulse == null)
        {
            _powerPulse = _powerLabel.CreateTween().SetLoops();
            _powerPulse.TweenProperty(_powerLabel, "modulate:a", 0.45f, 0.4f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            _powerPulse.TweenProperty(_powerLabel, "modulate:a", 1.0f, 0.4f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        }
        else if (!brownOut && _powerPulse != null)
        {
            _powerPulse.Kill();
            _powerPulse = null;
            _powerLabel.Modulate = Colors.White;
        }
    }

    private static string NameOf(int structType)
    {
        foreach (var it in Structures) if (it.TypeId == structType) return it.Label;
        return "STRUCTURE";
    }

    // ---- Verification surface (TICKET-P5-BD-01), following the SkirmishLive
    // precedent: expose what is on screen, not a recomputation of it.
    public string StructButtonText(int typeId) => _structButtons.TryGetValue(typeId, out var b) ? b.Text : "";
    /// <summary>TICKET-P5-PROD-01: is this structure's button actually on
    /// offer? Visibility is the gate (absent, not greyed), so it is what a
    /// test must read.</summary>
    public bool StructButtonVisible(int typeId) => _structButtons.TryGetValue(typeId, out var b) && b.Visible;
    public string UnitButtonText(int typeId) => _unitButtons.TryGetValue(typeId, out var b) ? b.Text : "";
    public string PowerText => _powerLabel.Text;
    public float PowerFillWidth => _powerFill.Size.X;
    public float PowerTickX => _powerTick.Position.X;
    public Color PowerFillColour => _powerFill.Color;
    public bool PowerPulsing => _powerPulse != null;
    public string StructHeaderText => _structHeader.Text;
}
