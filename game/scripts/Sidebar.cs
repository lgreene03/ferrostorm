using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Classic right-hand build sidebar in the uplink style (doc 16 palette:
/// cinder panels, seam borders, bone text, ferrite accent). GDD s7 line 86's
/// TABS since ADR-009 clause 6, replacing the two flat arrays under two
/// headers: BUILDINGS and DEFENCE queue at the Construction Yard and place
/// from the ready slot, INFANTRY queues at a Barracks, VEHICLES queues at a
/// Factory. Reads sim state through public accessors only; emits commands
/// through the scene's pending list.
///
/// AIRCRAFT is the fifth name on GDD line 86 and is deliberately ABSENT.
/// ADR-009 clause 10 keeps the airfield out of this wave entirely, and clause
/// 6 says the tab waits with it, matching the philosophy this file already
/// shipped: disallowed items are absent, not greyed, because progression
/// should read as the tree growing. An AIRCRAFT tab over an empty list would
/// advertise a building the game cannot build.
///
/// Struct type 11 is the BARRACKS. Unit type 11 is the ENGINEER. Different
/// namespaces, no clash, and both appear in this file within a few lines of
/// each other, so read which table you are in.
/// </summary>
public partial class Sidebar : PanelContainer
{
    /// <summary>ADR-006: no Cost column. The tables carried a second copy of
    /// every price, which under a runtime /data would show the player compiled
    /// numbers while the sim charged authored ones. Prices are read from the
    /// live catalogue delegates at Init, so an edited YAML is what the button
    /// says and what the treasury pays, from one source.</summary>
    public record BuildItem(string Label, int TypeId, string Icon);

    /// <summary>ADR-009 clause 6 / doc 23 s4.3: the BUILDINGS tab, in rough
    /// tech order. The barracks sits between the refinery and the factory,
    /// which is both its cost order and the order the AI's ladder builds
    /// them in.</summary>
    private static readonly BuildItem[] Structures =
    {
        new("POWER PLANT", 1, "com_power_plant"),
        new("REFINERY", 3, "com_refinery"),
        // ADR-009 clause 5: struct type 11, buildable at last. No bespoke
        // icon PNG is cut yet and MakeButton's Exists guard tolerates that,
        // flipping on its own when the sprite lands (owed to art-pipeline).
        new("BARRACKS", 11, "com_barracks"),
        new("FACTORY", 2, "com_factory"),
        new("SERVICE DEPOT", 8, "com_service_depot"),
        // ADR-008 clause 4: the Radar Uplink becomes reachable. Common
        // faction (the com_ prefix and the data file agree; the sim gates
        // only the veil projector), so no Init-time faction clause. The icon
        // PNG is not cut yet; MakeButton's Exists guard tolerates that and
        // flips on its own when the sprite lands.
        new("RADAR UPLINK", 12, "com_radar_uplink"),
    };

    /// <summary>ADR-009 clause 6 / doc 23 s4.3: the DEFENCE tab. These queue
    /// at the same Construction Yard as the BUILDINGS tab - the split is what
    /// the player is looking FOR, not which building makes it - so both tabs
    /// read the yard's queue and both are disabled by a full ready slot.</summary>
    private static readonly BuildItem[] Defences =
    {
        new("TURRET", 5, "dir_turret"),
        // TICKET-P5-DEF-08 clause 9. ADR-005 clause 3: a barrier has no ready
        // slot and no build time, so it is never queued at the yard - the button
        // enters placement directly and the treasury is charged per segment as
        // it lands.
        new("WALL", BarrierType, "com_wall_straight"),
        // TICKET-P5-PROD-01: the Sodality's signature building, fully
        // implemented in the sim since P2 and never buildable by a person.
        // The button is gated on faction in Init - absent for the
        // Directorate, exactly as the sim itself refuses the command
        // (World.cs BuildStructure's faction check, which stays authoritative).
        new("VEIL PROJECTOR", VeilType, "sod_veil_projector"),
        new("SUPERWEAPON", 6, "dir_superweapon"),
    };
    /// <summary>ADR-005 reserves struct type 9 for the wall segment (10 is the
    /// deferred gate).</summary>
    private const int BarrierType = 9;
    /// <summary>Struct type 7, the Veil Projector - Sodality doctrine
    /// (TICKET-P5-PROD-01; the sim's gate lives in World.cs BuildStructure).</summary>
    private const int VeilType = 7;
    private static readonly BuildItem[] Units =
    {
        new("RIFLE SQUAD", 2, "com_rifle_squad"),
        new("ROCKET SQUAD", 3, "com_rocket_squad"),
        new("SENTINEL SCOUT", 6, "dir_sentinel_scout"),
        new("ENGINEER", 11, "com_engineer"),
        // TICKET-P6-FACTION-01: the Sodality's two signature units, in the
        // sim's catalogue since P3 and never buttoned until the faction
        // picker made a Sodality player 0 reachable. The icon names follow
        // the id convention and the Exists guard in MakeButton tolerates the
        // sprites not being cut yet.
        new("SHADE RAIDER", 5, "sod_shade_raider"),
        new("VANGUARD CAR", 12, "dir_vanguard_car"),
        new("CANNON TANK", 1, "dir_cannon_tank"),
        new("HOWITZER", 8, "dir_howitzer"),
        new("PHANTOM TANK", 9, "sod_phantom_tank"),
        new("HARVESTER", 4, "com_harvester"),
        new("BULWARK TANK", 10, "dir_bulwark_tank"),
        new("MCV", 7, "com_mcv"),
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
    private Button _placeButton = null!;
    private int _readyType;
    private Tween? _placePulse;   // W3-16: PLACE-button ready pulse

    // ADR-009 clause 6: GDD line 86's tabs. Four, not five - AIRCRAFT waits
    // for the air ADR with the airfield it would build (see the class comment).
    public const int TabBuildings = 0, TabDefence = 1, TabInfantry = 2, TabVehicles = 3;
    private static readonly string[] TabTitles = { "BUILDINGS", "DEFENCE", "INFANTRY", "VEHICLES" };
    private TabContainer _tabs = null!;
    private readonly VBoxContainer[] _tabPages = new VBoxContainer[4];
    /// <summary>Per tab, the line shown when every item in it is hidden. An
    /// empty tab is the ADR's teaching moment (absent, not greyed), but an
    /// empty PANEL teaches nothing, so each says what would fill it.</summary>
    private readonly Label[] _tabEmptyNote = new Label[4];

    // BD-02: build time is sim data and the sidebar has no World, so it is
    // handed the two catalogue reads it needs rather than reaching for a static.
    // BD-06 made GetStructureType an instance method, which is why the structure
    // side is a delegate too: a live match may register its own catalogue.
    private System.Func<int, int> _unitBuildTicks = _ => 0;
    private System.Func<int, World.StructureTypeDef> _structDef = World.DefaultStructureType;
    // TICKET-P6-FACTION-01: the unit catalogue's faction column, by delegate
    // for the same BD-02/BD-06 reason as the two reads above - the answer
    // belongs to THIS match's catalogue, not the compiled defaults.
    private System.Func<int, int> _unitFaction = _ => World.FactionCommon;
    // ADR-006: the unit price column, by delegate for the same reason. The
    // structure side already rides _structDef.
    private System.Func<int, int> _unitCost = _ => 0;
    // ADR-009 clause 6: which producer builds this unit type, from the LIVE
    // catalogue rather than a second table in this file. Tab membership is a
    // read of the same produced_at the sim gates on, so the button a player
    // finds under INFANTRY and the order the sim accepts cannot disagree.
    private System.Func<int, int> _unitProducedAt = _ => World.FactoryStructType;

    /// <summary>Panel width, and the power bar's width inside it. Both moved
    /// together with ADR-009's tab bar; the bar keeps its old 16px of margin.</summary>
    public const float PanelWidth = 250f;
    private const float BarWidth = PanelWidth - 16f;

    public void Init(SkirmishLive game, System.Func<int, int> unitBuildTicks,
        System.Func<int, World.StructureTypeDef> structDef,
        System.Func<int, int> unitFaction,
        System.Func<int, int> unitCost,
        System.Func<int, int> unitProducedAt,
        System.Func<int, int[]?> unitPrereqs)
    {
        _game = game;
        _unitBuildTicks = unitBuildTicks;
        _structDef = structDef;
        _unitFaction = unitFaction;
        _unitCost = unitCost;
        _unitProducedAt = unitProducedAt;
        _unitPrereqs = unitPrereqs;
        // ADR-009 clause 6: the panel widens from 190 to PanelWidth because
        // GDD line 86's tab bar has to FIT. At 190 the four titles overflowed
        // and Godot's TabBar silently collapsed INFANTRY and VEHICLES behind
        // scroll arrows, which is a sidebar that hides half the game - found
        // in the offscreen run's own screenshot, not reasoned about.
        CustomMinimumSize = new Vector2(PanelWidth, 0);
        AnchorLeft = 1; AnchorRight = 1; AnchorBottom = 1;
        OffsetLeft = -PanelWidth;
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

        // ADR-009 clause 6: five tabs become four VBoxes inside a TabContainer,
        // styled to the same closed doc 16 palette as everything else here.
        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _tabs.AddThemeFontSizeOverride("font_size", 10);
        _tabs.AddThemeColorOverride("font_selected_color", FerriteGold);
        _tabs.AddThemeColorOverride("font_unselected_color", Dim);
        _tabs.AddThemeColorOverride("font_hovered_color", Bone);
        var tabPanel = new StyleBoxFlat { BgColor = Cinder with { A = 0.0f }, BorderColor = Seam };
        tabPanel.SetBorderWidthAll(0);
        tabPanel.BorderWidthTop = 1;
        _tabs.AddThemeStyleboxOverride("panel", tabPanel);
        v.AddChild(_tabs);
        for (int t = 0; t < TabTitles.Length; t++)
        {
            var page = new VBoxContainer { Name = TabTitles[t] };
            page.AddThemeConstantOverride("separation", 3);
            _tabs.AddChild(page);
            _tabPages[t] = page;
            var note = Header("");
            note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            note.Visible = false;
            page.AddChild(note);
            _tabEmptyNote[t] = note;
        }

        foreach (var it in Structures) AddStructButton(it, _tabPages[TabBuildings]);
        foreach (var it in Defences) AddStructButton(it, _tabPages[TabDefence]);

        foreach (var it in Units)
        {
            // ADR-009 clause 6: membership follows the unit's OWN produced_at,
            // read from the live catalogue. Struct type 11 is the barracks
            // here; unit type 11 (the engineer) is one of the units being
            // sorted BY it, which is exactly the confusion the ADR asked to
            // be named out loud.
            var page = _unitProducedAt(it.TypeId) == World.BarracksStructType
                ? _tabPages[TabInfantry] : _tabPages[TabVehicles];
            var b = MakeButton(it, () => _game.QueueUnit(it.TypeId), _unitCost(it.TypeId), _unitBuildTicks(it.TypeId));
            // TICKET-P6-FACTION-01: the veil button's gate, generalised to the
            // unit column, and it survives the move into tabs UNCHANGED - the
            // gate is per item, so it binds inside whichever tab the item
            // lands in. The visibility test mirrors the sim's own Produce
            // refusal (World.cs: Faction must be common or the player's own),
            // hidden not greyed, and the sim's check stays the authority - a
            // hand-crafted wrong-faction Produce is still refused unchanged.
            int fac = _unitFaction(it.TypeId);
            b.Visible = (MatchConfig.AllowedUnits?.Contains(it.TypeId) ?? true)
                && (fac == World.FactionCommon || fac == _game.FactionOf(0));
            _unitButtons[it.TypeId] = b;
            page.AddChild(b);
        }

        // The PLACE prompt sits BELOW the tabs rather than inside BUILDINGS:
        // it is the single most important prompt in the classic loop (W3-16)
        // and a player who wandered to the INFANTRY tab must not lose sight
        // of a finished building waiting to be sited.
        _placeButton = MakeButton(new BuildItem("PLACE >>", 0, ""), () => _game.EnterPlacement(_readyType));
        _placeButton.Visible = false;
        v.AddChild(_placeButton);
    }

    /// <summary>One structure button, into the tab that owns it. The two
    /// structure tabs share this because they share a producer: the split is
    /// what the player is looking for, not which building makes it.</summary>
    private void AddStructButton(BuildItem it, VBoxContainer page)
    {
        // DEF-08 clause 9: a barrier bypasses the yard queue entirely, so
        // its button enters placement rather than queueing.
        var b = it.TypeId == BarrierType
            ? MakeButton(it, () => _game.EnterPlacement(BarrierType), _structDef(it.TypeId).Cost, _structDef(it.TypeId).BuildTicks)
            : MakeButton(it, () => _game.QueueStructure(it.TypeId), _structDef(it.TypeId).Cost, _structDef(it.TypeId).BuildTicks);
        // Classic campaign tech gating: disallowed items are absent,
        // not greyed - progression should read as the tree growing.
        // TICKET-P5-PROD-01: the faction gate reads the same shape - the
        // Veil Projector button exists only for a Sodality player 0,
        // mirroring the sim's own refusal rather than second-guessing it
        // (the sim's check is untouched and still refuses a hand-crafted
        // command). Faction is map content, set before tick 0 and never
        // mutated mid-match, so an Init-time read is sound. The LIVE
        // prerequisite half of ADR-009 clause 6's three-way AND is not here:
        // it changes as the base grows, so it belongs in Refresh.
        b.Visible = (MatchConfig.AllowedStructures?.Contains(it.TypeId) ?? true)
            && (it.TypeId != VeilType || _game.FactionOf(0) == World.FactionSodality);
        _structButtons[it.TypeId] = b;
        page.AddChild(b);
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
    /// ticks: ticks are the sim's unit, seconds are the player's. ADR-006: the
    /// cost arrives from the live catalogue delegates, never from a table.</summary>
    private Button MakeButton(BuildItem it, System.Action onPress, int cost = 0, int buildTicks = 0)
    {
        string label = cost > 0 ? $"{it.Label}  {cost}" : it.Label;
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

    /// <summary>One producer's live state for the tab that reads it: whether
    /// the player has one standing at all, its queue contents, and the head's
    /// build fraction (ADR-009 clause 6 - one line per producer, because the
    /// tabs no longer share a single queue).</summary>
    public readonly record struct ProducerLine(bool Live, IReadOnlyList<int> Queue, float HeadProgress)
    {
        public static readonly ProducerLine None = new(false, System.Array.Empty<int>(), 0f);
    }

    /// <summary>Called by the scene each frame with fresh sim reads. W3-15:
    /// queue contents drive per-button count badges and a progress fill on
    /// the queue head (the classic clock substitute). ADR-009 clause 6: each
    /// tab reads its OWN producer, and per-item visibility becomes the AND of
    /// three things where it used to be one - the campaign allow-list and the
    /// faction gate (both fixed at Init), the live prerequisite check, and,
    /// for units, a living producer of the right produced_at. ownsStructType
    /// answers the middle one from live sim state.</summary>
    public void Refresh(long credits, int readyStructureType,
        ProducerLine yard, ProducerLine factory, ProducerLine barracks,
        int supply, int draw, System.Func<int, bool> ownsStructType)
    {
        bool hasYard = yard.Live;
        var yardQ = yard.Queue;
        float yardProgress = yard.HeadProgress;
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
            // ADR-009 clause 6, the live half of the three-way AND. The
            // allow-list and faction gate already decided at Init whether this
            // button exists at all; the TREE decides whether it exists YET,
            // and it is re-read every frame because a base grows and shrinks.
            // Absent, not greyed, exactly as this file has always done it -
            // that is what makes the panel read as the tree growing rather
            // than as a wall of things you cannot have.
            if (b.Visible || _prereqHidden.Contains(typeId))
            {
                bool treeMet = PrereqsMet(_structDef(typeId).Prereqs, ownsStructType);
                if (!treeMet) _prereqHidden.Add(typeId); else _prereqHidden.Remove(typeId);
                b.Visible = treeMet && (MatchConfig.AllowedStructures?.Contains(typeId) ?? true)
                    && (typeId != VeilType || _game.FactionOf(0) == World.FactionSodality);
            }
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
        foreach (var (typeId, b) in _unitButtons)
        {
            // ADR-009 clause 6: for a unit the AND has a third clause, a
            // LIVING PRODUCER of the right produced_at. That clause is what
            // makes an empty INFANTRY tab teach the player to build a
            // barracks, which is the whole point of the tab existing.
            var line = _unitProducedAt(typeId) == World.BarracksStructType ? barracks : factory;
            if (b.Visible || _prereqHiddenUnits.Contains(typeId))
            {
                bool met = line.Live && PrereqsMet(_unitPrereqs(typeId), ownsStructType);
                if (!met) _prereqHiddenUnits.Add(typeId); else _prereqHiddenUnits.Remove(typeId);
                int fac = _unitFaction(typeId);
                b.Visible = met && (MatchConfig.AllowedUnits?.Contains(typeId) ?? true)
                    && (fac == World.FactionCommon || fac == _game.FactionOf(0));
            }
            var q = line.Queue;
            int n = 0;
            foreach (int t in q) if (t == typeId) n++;
            b.Disabled = !line.Live || credits < _unitCost(typeId);
            b.Text = _baseText[b] + QueueSuffix(n, q.Count > 0 && typeId == q[0],
                _unitBuildTicks(typeId), line.HeadProgress);
            ((ColorRect)b.GetNode("Fill")).OffsetRight =
                q.Count > 0 && typeId == q[0] ? b.Size.X * line.HeadProgress : 0;
        }
        ((ColorRect)_placeButton.GetNode("Fill")).OffsetRight = 0;
        // BD-10 clause 5's queue counters, now on the TABS that own those
        // queues: BUILDINGS and DEFENCE both read the yard, INFANTRY the
        // barracks, VEHICLES the factory. Credits still appear once, on the
        // status line, where they were already.
        SetTabTitle(TabBuildings, yardQ.Count);
        SetTabTitle(TabDefence, yardQ.Count);
        SetTabTitle(TabInfantry, barracks.Queue.Count);
        SetTabTitle(TabVehicles, factory.Queue.Count);
        // An empty tab is the teaching moment, but a blank panel teaches
        // nothing, so each empty tab says what would fill it.
        UpdateEmptyNote(TabBuildings, hasYard ? "NO BUILDINGS AVAILABLE YET" : "REQUIRES A CONSTRUCTION YARD");
        UpdateEmptyNote(TabDefence, hasYard ? "REQUIRES A POWER PLANT" : "REQUIRES A CONSTRUCTION YARD");
        UpdateEmptyNote(TabInfantry, "REQUIRES A BARRACKS");
        UpdateEmptyNote(TabVehicles, "REQUIRES A FACTORY");
    }

    // Buttons hidden by the live tree, so Refresh knows to reconsider them
    // when the base grows. Without this the visibility test could only ever
    // hide: a button already invisible would never be looked at again.
    private readonly HashSet<int> _prereqHidden = new();
    private readonly HashSet<int> _prereqHiddenUnits = new();

    /// <summary>The unit prerequisite list, by delegate for the same
    /// live-catalogue reason as every other read in this file.</summary>
    private System.Func<int, int[]?> _unitPrereqs = _ => null;

    private static bool PrereqsMet(int[]? prereqs, System.Func<int, bool> ownsStructType)
    {
        if (prereqs == null) return true;
        foreach (int p in prereqs) if (!ownsStructType(p)) return false;
        return true;
    }

    private void SetTabTitle(int tab, int queued)
        => _tabs.SetTabTitle(tab, queued > 0 ? $"{TabTitles[tab]} {queued}" : TabTitles[tab]);

    private void UpdateEmptyNote(int tab, string text)
    {
        bool anyVisible = false;
        foreach (var child in _tabPages[tab].GetChildren())
            if (child is Button { Visible: true }) { anyVisible = true; break; }
        _tabEmptyNote[tab].Text = text;
        _tabEmptyNote[tab].Visible = !anyVisible;
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
        foreach (var it in Defences) if (it.TypeId == structType) return it.Label;
        return "STRUCTURE";
    }

    // ---- Verification surface (TICKET-P5-BD-01), following the SkirmishLive
    // precedent: expose what is on screen, not a recomputation of it.
    public string StructButtonText(int typeId) => _structButtons.TryGetValue(typeId, out var b) ? b.Text : "";
    /// <summary>TICKET-P5-PROD-01: is this structure's button actually on
    /// offer? Visibility is the gate (absent, not greyed), so it is what a
    /// test must read.</summary>
    public bool StructButtonVisible(int typeId) => _structButtons.TryGetValue(typeId, out var b) && b.Visible;
    /// <summary>TICKET-P5-ALERT-02 wave: does the button carry a real icon
    /// texture? The Exists guard in MakeButton flips on its own when the PNG
    /// lands, and this is the read that proves it flipped.</summary>
    public bool StructButtonHasIcon(int typeId) => _structButtons.TryGetValue(typeId, out var b) && b.Icon != null;
    public string UnitButtonText(int typeId) => _unitButtons.TryGetValue(typeId, out var b) ? b.Text : "";
    /// <summary>TICKET-P6-FACTION-01: the unit-side twin of StructButtonVisible,
    /// for the same reason - visibility IS the faction gate, so it is what a
    /// test must read.</summary>
    public bool UnitButtonVisible(int typeId) => _unitButtons.TryGetValue(typeId, out var b) && b.Visible;
    public string PowerText => _powerLabel.Text;
    public float PowerFillWidth => _powerFill.Size.X;
    public float PowerTickX => _powerTick.Position.X;
    public Color PowerFillColour => _powerFill.Color;
    public bool PowerPulsing => _powerPulse != null;
    // ADR-009 clause 6: the tab surface. The old StructHeaderText read a
    // header that no longer exists; the queue counter it carried lives on the
    // BUILDINGS tab title now, so the read moves with it rather than dying.
    public string TabTitle(int tab) => _tabs.GetTabTitle(tab);
    public string StructHeaderText => TabTitle(TabBuildings);
    public int TabCount => _tabs.GetTabCount();
    public int CurrentTab { get => _tabs.CurrentTab; set => _tabs.CurrentTab = value; }
    /// <summary>Which tab does this unit's button live in? A read of the real
    /// scene tree, not of the membership rule that built it.</summary>
    public int TabOfUnit(int typeId)
    {
        if (!_unitButtons.TryGetValue(typeId, out var b)) return -1;
        for (int t = 0; t < _tabPages.Length; t++)
            if (b.GetParent() == _tabPages[t]) return t;
        return -1;
    }
    /// <summary>Which tab does this structure's button live in?</summary>
    public int TabOfStruct(int typeId)
    {
        if (!_structButtons.TryGetValue(typeId, out var b)) return -1;
        for (int t = 0; t < _tabPages.Length; t++)
            if (b.GetParent() == _tabPages[t]) return t;
        return -1;
    }
    /// <summary>The empty-tab note, or "" while the tab has visible items.</summary>
    public string TabEmptyNote(int tab) => _tabEmptyNote[tab].Visible ? _tabEmptyNote[tab].Text : "";
}
