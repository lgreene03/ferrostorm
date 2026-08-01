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
/// AIRCRAFT is the fifth name on GDD line 86 and is still deliberately ABSENT,
/// though the reason has MOVED and the old one is no longer true. ADR-009
/// clause 10 kept the airfield out of that wave entirely and clause 6 said the
/// tab waits with it; ADR-028 has since shipped the airfield, so that argument
/// has expired. What holds the tab back now is one rung lower down: the sim's
/// own producer predicate covers the Factory, the Construction Yard and the
/// Barracks, so a Produce command sent to an airfield is dropped in silence and
/// the strike flyer cannot be built by anybody at all. A tab over it would be a
/// tab of buttons that do nothing, and this panel's contract is that what it
/// offers is what the sim accepts. Widening the sim's predicate is a sim change
/// with an ADR behind it; the tab lands with that, not before it.
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

    /// <summary>
    /// One structure button, DERIVED from a registered type id, exactly as
    /// UnitItem below derives a unit's. There are no Structures and Defences
    /// tables any more: there were two, hand-kept here, and they were the last
    /// hand-maintained copy of the catalogue in this file. The mine had to be
    /// added to one of them BY HAND in the wave that shipped it, and the comment
    /// beside it said so - which is the same rule keyed on an INSTANCE (a list of
    /// ids) where it should key on a PROPERTY (what the catalogue registers) that
    /// had already left seven units unbuildable.
    ///
    /// The label comes off the /data id through the one derivation the unit
    /// labels use (StructureCatalogue.DisplayNameOf): the faction prefix cut,
    /// underscores to spaces, upper-cased. That reproduces all fifteen labels the
    /// two tables carried, exactly and with no exceptions - com_power_plant gives
    /// POWER PLANT, sod_veil_projector gives VEIL PROJECTOR - which is what makes
    /// deriving the lists inert for everything that already had a button and
    /// additive for everything that did not.
    ///
    /// WHICH TAB is NOT derived, because it is not derivable: the Airfield is a
    /// producer filed under DEFENCE, and the wall, veil, superweapon and mine
    /// carry no weapon, so nothing on the def implies the split. It is authored
    /// per building in /data (build_tab) and read here off the live catalogue,
    /// which is the same discipline every other number on this panel follows.
    ///
    /// The icon name is the id, the convention every icon in ui/icons follows and
    /// the one the units already use; MakeButton's Exists guard tolerates a
    /// sprite that has not been cut yet. The exceptions are in PlaceholderIcon
    /// below and are art debt rather than identity.
    /// </summary>
    private static BuildItem StructItem(int typeId)
    {
        string id = StructureCatalogue.IdOf(typeId);
        return new BuildItem(DataLoader.DisplayNameFromId(id), typeId, PlaceholderIcon(id));
    }

    /// <summary>Buildings still wearing another building's art, and the only
    /// reason this is a list rather than a rule: the bespoke PNG has not been cut
    /// (all six are owed to art-pipeline, the ADR-019 interim precedent). Keyed
    /// by /data id so a renumbering cannot re-point one, and deliberately NOT
    /// part of the decision about which buttons exist - a building missing from
    /// here gets no icon, which MakeButton already tolerates, rather than no
    /// button. Each entry disappears the day its own sprite lands.</summary>
    private static string PlaceholderIcon(string id) => id switch
    {
        // The anti-infantry hardpoint and the Directorate's defence both wear
        // the turret's model for now.
        "com_emplacement" => "dir_turret",
        "dir_bastion" => "dir_turret",
        // The Sodality's nest wears the veil's.
        "sod_shroud_nest" => "sod_veil_projector",
        // ADR-028's airfield wears the factory's.
        "com_airfield" => "com_factory",
        // The barrier segment, and the mine after it: a low slab that reads as
        // something laid on the ground.
        "com_wall" => "com_wall_straight",
        "com_mine" => "com_wall_straight",
        // P7-10's gate wears the wall's slab too, which is honest enough for a
        // segment of wall that moves. Its own sprite is owed to art-pipeline.
        "com_gate" => "com_wall_straight",
        _ => id,
    };

    /// <summary>Which page a building's authored tab names, or null for a
    /// building that carries no button at all. BuildTab.None is the three
    /// map-placed types (the MCV-deployed yard, the outpost, the bridge) and it
    /// is not a second list of them: the loader refuses any /data file whose tab
    /// disagrees with the sim's own queueability rule, which is the same
    /// equivalence reachabilitygate holds from the other side.</summary>
    private VBoxContainer? PageFor(BuildTab tab) => tab switch
    {
        BuildTab.Buildings => _tabPages[TabBuildings],
        BuildTab.Defence => _tabPages[TabDefence],
        _ => null,
    };

    /// <summary>Is this struct type a barrier? From the live catalogue's Kind,
    /// which is what SkirmishLive.IsBarrier already asks - and P7-10 made both of
    /// them ask World.IsBarrier itself rather than each writing
    /// `Kind == EntityKind.Wall` again. The gate is the second barrier and this
    /// was one of the four places that would have missed it, silently: a gate
    /// button on the queueing branch would send a BuildStructure the sim refuses
    /// for having no build time, so the button would exist and do nothing.</summary>
    private bool IsBarrierType(int structType) => World.IsBarrier(_structDef(structType).Kind);

    /// <summary>
    /// One unit button, DERIVED from a registered type id rather than authored
    /// beside it. There is no Units table any more: there was one, hand-kept
    /// here, and it had fallen SEVEN units behind the catalogue - the transport,
    /// the flak track, the infiltrator, the saboteur and both heroes all existed
    /// in the sim and no player could build any of them, because a unit only
    /// reached the panel if whoever added it remembered this file. That is a
    /// rule keyed on an INSTANCE (a list of ids) where it should key on a
    /// PROPERTY (what the catalogue registers), which is the defect shape this
    /// project keeps finding.
    ///
    /// The label comes off the /data id: the faction prefix is cut, underscores
    /// become spaces, and the rest is upper-cased. That reproduces all thirteen
    /// labels the old table carried, exactly and with no exceptions -
    /// com_rifle_squad gives RIFLE SQUAD, com_mcv gives MCV, dir_sentinel_scout
    /// gives SENTINEL SCOUT - which is what makes deriving the list inert for
    /// everything that already had a button and additive for everything that
    /// did not. The icon name is the id itself, the convention every icon in
    /// ui/icons already follows, and MakeButton's Exists guard tolerates a
    /// sprite that has not been cut yet.
    /// </summary>
    private static BuildItem UnitItem(int typeId)
    {
        return new BuildItem(UnitCatalogue.DisplayNameOf(typeId), typeId, UnitCatalogue.IdOf(typeId));
    }

    /// <summary>Does this panel have a tab for the producer this unit names?
    /// Asked of the CATALOGUE's produced_at, never of a list of ids, so the
    /// answer follows what /data declares.
    ///
    /// One producer says no today, and it is the AIRFIELD, which the strike
    /// flyer names. Two things would have to change before an aircraft could
    /// carry a button honestly: this panel needs a tab for it (GDD line 86's
    /// fifth name, see the class comment), and the SIM needs to accept the
    /// order at all - World's own producer predicate covers the Factory, the
    /// Construction Yard and the Barracks and nothing else, so a Produce sent
    /// to an airfield is dropped without a word. A button here before both of
    /// those would be a button that does nothing, which is worse than an absent
    /// one: the panel's whole contract is that what it offers is what the sim
    /// accepts.</summary>
    private bool HasTabFor(int producedAt)
        => producedAt == World.BarracksStructType || producedAt == World.FactoryStructType
           || producedAt == World.AirfieldStructType;

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

    // ADR-009 clause 6: GDD line 86's tabs. FIVE now. The comment here read
    // "Four, not five - AIRCRAFT waits for the air ADR with the airfield it
    // would build", and it waited past the point it was waiting for: ADR-028
    // shipped the airfield and nobody came back, exactly as World.IsProducer's
    // comment did about the same building. Both are actioned together, because
    // a producer the sim accepts and the panel has no tab for is a unit that
    // is buildable and unreachable, which is the defect this pair just fixed.
    public const int TabBuildings = 0, TabDefence = 1, TabInfantry = 2, TabVehicles = 3, TabAircraft = 4;
    private static readonly string[] TabTitles = { "BUILDINGS", "DEFENCE", "INFANTRY", "VEHICLES", "AIRCRAFT" };
    /// <summary>How many tabs there are, for anything that cycles them. Read
    /// rather than written, so adding a tab cannot break a test whose subject
    /// is the cycling rather than the count.</summary>
    public static int TabTitleCount => TabTitles.Length;
    private TabContainer _tabs = null!;
    private readonly VBoxContainer[] _tabPages = new VBoxContainer[5];
    /// <summary>Per tab, the line shown when every item in it is hidden. An
    /// empty tab is the ADR's teaching moment (absent, not greyed), but an
    /// empty PANEL teaches nothing, so each says what would fill it.</summary>
    private readonly Label[] _tabEmptyNote = new Label[5];

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

        // The structure list IS the catalogue, walked in ascending type id for
        // the reason the unit walk below sorts: the panel must read the same at
        // every seat and on every machine, or the grid hotkeys mean different
        // things at each end of a LAN match. Ascending id is not the old tables'
        // rough tech order, so the buttons within each tab sit in a different
        // sequence than before; every label, every tab and every icon is
        // unchanged, which is what the harness asserts.
        foreach (int typeId in _game.LiveWorld.StructureTypeIds())
        {
            var page = PageFor(_structDef(typeId).Tab);
            if (page == null) continue;
            AddStructButton(StructItem(typeId), page);
        }

        // The unit list IS the catalogue, walked in ascending type id so the
        // panel reads the same at every seat and on every machine (World's
        // accessor sorts; see UnitItem for why there is no table here any more).
        foreach (int typeId in _game.LiveWorld.UnitTypeIds())
        {
            // ADR-009 clause 6: membership follows the unit's OWN produced_at,
            // read from the live catalogue. Struct type 11 is the barracks
            // here; unit type 11 (the engineer) is one of the units being
            // sorted BY it, which is exactly the confusion the ADR asked to
            // be named out loud.
            int producedAt = _unitProducedAt(typeId);
            if (!HasTabFor(producedAt)) continue;
            var it = UnitItem(typeId);
            var page = producedAt == World.BarracksStructType ? _tabPages[TabInfantry]
                     : producedAt == World.AirfieldStructType ? _tabPages[TabAircraft]
                     : _tabPages[TabVehicles];
            var b = MakeButton(it, () => _game.QueueUnit(it.TypeId), _unitCost(it.TypeId), _unitBuildTicks(it.TypeId),
                onCancel: () => _game.CancelUnit(it.TypeId));   // C3 (ADR-020): right-click cancels
            // TICKET-P6-FACTION-01: the veil button's gate, generalised to the
            // unit column, and it survives the move into tabs UNCHANGED - the
            // gate is per item, so it binds inside whichever tab the item
            // lands in. The visibility test mirrors the sim's own Produce
            // refusal (World.cs: Faction must be common or the player's own),
            // hidden not greyed, and the sim's check stays the authority - a
            // hand-crafted wrong-faction Produce is still refused unchanged.
            b.Visible = FixedGatesAllowUnit(it.TypeId);
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
        // C3 (ADR-020): a queued structure gets a right-click cancel; a barrier
        // has no queue (bought and placed outright), so it gets none.
        var b = IsBarrierType(it.TypeId)
            ? MakeButton(it, () => _game.EnterPlacement(it.TypeId), _structDef(it.TypeId).Cost, _structDef(it.TypeId).BuildTicks)
            : MakeButton(it, () => _game.QueueStructure(it.TypeId), _structDef(it.TypeId).Cost, _structDef(it.TypeId).BuildTicks,
                onCancel: () => _game.CancelStructure(it.TypeId));
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
        b.Visible = FixedGatesAllow(it.TypeId);
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
    /// <summary>DR-08: cycle the active tab, the keyboard's way around a
    /// mouse-only sidebar. Wraps.</summary>
    public void CycleTab() => _tabs.CurrentTab = (_tabs.CurrentTab + 1) % TabTitles.Length;
    /// <summary>Verification read: the tab actually showing.</summary>
    public int CurrentTabForTest => _tabs.CurrentTab;

    /// <summary>
    /// DR-08: queue the Nth VISIBLE item of the active tab - visible, because
    /// the grid must mean what the player sees. Hidden buttons (faction and
    /// campaign gates) shift later slots, which is the price of what-you-see
    /// semantics and the same price the mouse pays.
    ///
    /// Fires the button's own Pressed signal, so the hotkey and the mouse run
    /// THE SAME handler and cannot drift (the one-rule law): the affordability
    /// disable, the placement entry for barriers and the cancel wiring all
    /// come for free. A disabled slot refuses quietly and returns false.
    /// </summary>
    public bool TriggerSlot(int index)
    {
        int seen = 0;
        foreach (var child in _tabPages[_tabs.CurrentTab].GetChildren())
            if (child is Button b && b.Visible)
            {
                if (seen++ != index) continue;
                if (b.Disabled) return false;
                b.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                return true;
            }
        return false;
    }

    private Button MakeButton(BuildItem it, System.Action onPress, int cost = 0, int buildTicks = 0,
        System.Action? onCancel = null)
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
        // C3 (ADR-020): right-click cancels one queued item of this type, the
        // classic sidebar affordance the client never had (CancelProduce was
        // issued nowhere). Left-click still builds; the tooltip advertises both.
        if (onCancel != null)
        {
            b.TooltipText = "Left-click: build     Right-click: cancel / refund";
            b.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
                {
                    onCancel();
                    b.AcceptEvent();
                }
            };
        }
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
    /// for units, a living producer of the right produced_at. prereqsMet
    /// answers the middle one by asking THE SIM'S OWN World.HasPrereqs, rather
    /// than by a fold over an ownsStructType the client computed itself: two
    /// implementations of the tech tree agree only until one is edited, and the
    /// failure mode is a lit button whose order the sim silently drops.</summary>
    public void Refresh(long credits, int readyStructureType,
        ProducerLine yard, ProducerLine factory, ProducerLine barracks,
        int supply, int draw, System.Func<int[]?, bool> prereqsMet,
        ProducerLine yardLane2 = default, int readyStructureType2 = 0)
    {
        bool hasYard = yard.Live;
        var yardQ = yard.Queue;
        float yardProgress = yard.HeadProgress;
        // ADR-023: the yard's second lane. Empty on a yard that has never
        // overflowed, which is the ordinary case and reads exactly as before.
        var laneQ = yardLane2.Queue ?? System.Array.Empty<int>();
        float laneProgress = yardLane2.HeadProgress;
        RefreshPower(supply, draw);
        // ADR-023: two lanes mean two possible ready structures. One PLACE
        // prompt still, showing lane 1's first and lane 2's once that is
        // placed, so the player works through them without a second widget
        // competing for the same corner of the screen.
        _readyType = readyStructureType != 0 ? readyStructureType : readyStructureType2;
        readyStructureType = _readyType;
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
        // ADR-023: a type queued in EITHER lane counts once on its button; the
        // player cares how many are coming, not which line they are on.
        foreach (int t in laneQ) structCounts[t] = structCounts.GetValueOrDefault(t) + 1;
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
                bool treeMet = prereqsMet(_structDef(typeId).Prereqs);
                if (!treeMet) _prereqHidden.Add(typeId); else _prereqHidden.Remove(typeId);
                b.Visible = treeMet && FixedGatesAllow(typeId);
            }
            // DEF-08 clause 9: a full ready slot pauses the yard's queue and so
            // disables the queued structures, but a barrier never enters that
            // slot - it stays buildable while a finished structure waits.
            // ADR-023: a full ready slot pauses ONE lane, not the yard. While
            // the other lane is still free the player can keep queueing, which
            // is the whole point of the second line; only both slots full
            // disables the tab.
            b.Disabled = !hasYard || credits < def.Cost
                         || (!IsBarrierType(typeId) && readyStructureType > 0 && readyStructureType2 > 0);
            int n = structCounts.GetValueOrDefault(typeId);
            // Head progress comes from whichever lane actually holds this type
            // at its head; the sim decided that at order time, so the client
            // looks rather than assumes.
            bool headL1 = yardQ.Count > 0 && typeId == yardQ[0];
            bool headL2 = laneQ.Count > 0 && typeId == laneQ[0];
            float headProg = headL1 ? yardProgress : laneProgress;
            b.Text = _baseText[b] + QueueSuffix(n, headL1 || headL2, def.BuildTicks, headProg);
            ((ColorRect)b.GetNode("Fill")).OffsetRight =
                headL1 || headL2 ? b.Size.X * headProg : 0;
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
                bool met = line.Live && prereqsMet(_unitPrereqs(typeId));
                if (!met) _prereqHiddenUnits.Add(typeId); else _prereqHiddenUnits.Remove(typeId);
                int fac = _unitFaction(typeId);
                b.Visible = met && FixedGatesAllowUnit(typeId);
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
        // ADR-023: the yard's badge is both lanes, since both are its line.
        SetTabTitle(TabBuildings, yardQ.Count + laneQ.Count);
        SetTabTitle(TabDefence, yardQ.Count + laneQ.Count);
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

    /// <summary>The two clauses of item visibility that are FIXED before tick 0:
    /// the campaign allow-list and the faction gate. One method, because Init
    /// and Refresh each open-coded them and the pair is only ever true together
    /// - and because Refresh runs only for items Init did not already hide, so a
    /// change to one copy silently failed to reach what the other had hidden.
    /// The LIVE prerequisite clause is deliberately NOT here: it changes as the
    /// base grows, so it belongs to Refresh alone.</summary>
    private bool FixedGatesAllow(int structType) =>
        SkirmishLive.StructureAllowed(structType)
        // P7-1: an INSTANCE call now, because the rule moved out of a hardcoded
        // predicate and into the catalogue. The client still ASKS the sim
        // rather than deciding for itself, which is the point: one rule, one
        // implementation, and the answer follows whatever /data declares.
        && _game.LiveWorld.StructureAllowedForFaction(structType, _game.FactionOf(_game.LocalPlayerId));

    private bool FixedGatesAllowUnit(int unitType) =>
        SkirmishLive.UnitAllowed(unitType)
        && (_unitFaction(unitType) == World.FactionCommon
            || _unitFaction(unitType) == _game.FactionOf(_game.LocalPlayerId));

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
        bool brownOut = SkirmishLive.BrownedOut(supply, draw);   // the one client threshold
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

    /// <summary>The name on the PLACE prompt. From the catalogue's own
    /// derivation rather than from a scan of the two tables that used to stand
    /// here, so a building the panel offers can never be the one this cannot
    /// name. The fallback covers only the ready slot's empty value 0, which
    /// IdOf refuses by design rather than returning a blank for.</summary>
    private static string NameOf(int structType)
    {
        try { return StructureCatalogue.DisplayNameOf(structType); }
        catch (System.FormatException) { return "STRUCTURE"; }
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
    /// <summary>How many unit buttons the panel actually built. The number the
    /// hand-kept table used to fix at thirteen while the catalogue grew to
    /// twenty, so it is the measurement that would have caught the gap.</summary>
    public int UnitButtonCount => _unitButtons.Count;
    /// <summary>The FIXED half of a unit button's visibility - the campaign
    /// allow-list and the faction gate - without the live producer and
    /// prerequisite clauses Refresh adds. A check needs it separately, because
    /// the live half hides almost everything at tick 0 and would mask a faction
    /// gate that had stopped binding.</summary>
    public bool UnitFixedGateForTest(int typeId) => FixedGatesAllowUnit(typeId);
    /// <summary>Press a unit's button as the mouse would, through the button's
    /// OWN Pressed signal, so a check proves the wired handler rather than a
    /// recomputation of it (the TriggerSlot precedent, and the same one-rule
    /// law). False if there is no such button, if it is hidden or if it is
    /// disabled: all three are ways a button can be present and unreachable,
    /// and "present" was never the claim worth checking.</summary>
    public bool PressUnitButton(int typeId)
    {
        if (!_unitButtons.TryGetValue(typeId, out var b) || !b.Visible || b.Disabled) return false;
        b.EmitSignal(Godot.BaseButton.SignalName.Pressed);
        return true;
    }
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
