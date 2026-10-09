using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-34: the match's numbers for one seat, tallied from what the client
/// already reads after every step: the tick's GameEvents, its own harvesters'
/// loads and its own treasury. The sim keeps no match statistics and none are
/// added to it (this is a client row), so each figure is a reading of state
/// the sim already publishes, and each reading is stated where it is taken.
///
/// UNITS BUILT: own ProductionComplete events whose entity is a mobile (a
/// yard's completion names the yard and is a building, counted when placed).
/// UNITS LOST: own mobiles that DIED. A boarding is its own event (D34) and an
/// unload is not production, so neither moves a count.
/// UNITS KILLED: hostile mobiles that died with this seat as the last to shoot
/// at them (Fired, A the shooter, B the target). A death with no shooter on
/// record (a mine, a strike, a crush) is credited when this seat's own area
/// weapon went off on the same tick (its mine detonating, its superweapon
/// landing, its support power used) or an own heavy unit stands within a cell
/// of the body (a crush, which fires nothing); otherwise it is nobody's, which
/// is the honest answer in a free-for-all where another commander did it.
/// STRUCTURES BUILT: own placements (StructurePlaced) and own MCVs unpacked
/// (Deployed). STRUCTURES LOST: own structures that died, a mine excepted (a
/// mine dies by detonating, which is it working), plus own structures taken
/// by capture. A sale is not a loss and raises no death.
/// CREDITS HARVESTED: every ferrite load an own harvester delivered, read as
/// its Carry falling (the sim pays a whole load on unloading).
/// CREDITS SPENT: each tick, what left the treasury beyond the known income
/// (the harvest above and credits stolen or seized): production's
/// pay-as-you-build slices, wall segments, repairs and power fire. A refund
/// landing on the same tick as a payment nets against it, so the figure can
/// read a little under the gross outflow; never over.
/// </summary>
public sealed class MatchStats
{
    public int UnitsBuilt { get; private set; }
    public int UnitsLost { get; private set; }
    public int UnitsKilled { get; private set; }
    public int StructuresBuilt { get; private set; }
    public int StructuresLost { get; private set; }
    public long CreditsHarvested { get; private set; }
    public long CreditsSpent { get; private set; }
    /// <summary>The tick the tally began at: 0 for a fresh match, the save's
    /// tick for a resumed one.</summary>
    public int StartTick { get; }

    private readonly int _seat;
    private long _credits;
    private readonly Dictionary<int, int> _carry = new();
    private readonly Dictionary<int, int> _lastShooterSeat = new();
    private readonly Dictionary<int, int> _tickOwner = new();

    public MatchStats(World w, int seat)
    {
        _seat = seat;
        StartTick = w.Tick;
        _credits = w.Credits(seat);
        var ents = w.Entities;
        for (int i = 0; i < ents.Count; i++)
            if (ents[i].Alive && ents[i].PlayerId == seat && ents[i].Kind == EntityKind.Harvester) _carry[i] = ents[i].Carry;
    }

    private static bool Mobile(EntityKind k) => k is EntityKind.Unit or EntityKind.Harvester;

    /// <summary>Fold one stepped tick in. `preTickOwner` is the structure
    /// owner cache as it stood before the tick (a Captured event's entity has
    /// already changed hands by the time it is read).</summary>
    public void Observe(World w, IReadOnlyDictionary<int, int> preTickOwner, System.Func<int, bool> isHostileSeat)
    {
        var ents = w.Entities;
        bool Valid(int id) => id >= 0 && id < ents.Count;
        long robbedIn = 0, robbedOut = 0;
        bool ownBlast = false;
        // Pass 1: who shot whom, and whether an area weapon of mine went off,
        // before any death of the tick is attributed.
        foreach (var ev in w.Events)
        {
            switch (ev.Type)
            {
                case GameEventType.Fired when Valid(ev.A) && Valid(ev.B):
                    _lastShooterSeat[ev.B] = ents[ev.A].PlayerId;
                    break;
                case GameEventType.SuperweaponImpact when Valid(ev.A) && ents[ev.A].PlayerId == _seat:
                case GameEventType.SupportPowerUsed when Valid(ev.A) && ents[ev.A].PlayerId == _seat:
                    ownBlast = true;
                    break;
                case GameEventType.Died when Valid(ev.A) && ents[ev.A].Kind == EntityKind.Mine && ents[ev.A].PlayerId == _seat:
                    ownBlast = true;
                    break;
                case GameEventType.Robbed when Valid(ev.A):
                    if (ev.B == _seat) robbedIn += System.Math.Max(0, ev.C);
                    else if (ents[ev.A].PlayerId == _seat) robbedOut += System.Math.Max(0, ev.C);
                    break;
            }
        }
        _tickOwner.Clear();
        foreach (var ev in w.Events)
        {
            switch (ev.Type)
            {
                case GameEventType.ProductionComplete when Valid(ev.A):
                    if (ents[ev.A].PlayerId == _seat && Mobile(ents[ev.A].Kind)) UnitsBuilt++;
                    break;
                case GameEventType.StructurePlaced when Valid(ev.A):
                    if (ents[ev.A].PlayerId == _seat) StructuresBuilt++;
                    break;
                case GameEventType.Deployed when Valid(ev.B):
                    if (ents[ev.B].PlayerId == _seat) StructuresBuilt++;
                    break;
                case GameEventType.Died when Valid(ev.A):
                {
                    var e = ents[ev.A];
                    if (Mobile(e.Kind))
                    {
                        if (e.PlayerId == _seat) UnitsLost++;
                        else if (isHostileSeat(e.PlayerId) && CreditedToMe(ev.A, e, ents, ownBlast)) UnitsKilled++;
                    }
                    else if (World.IsStructure(e.Kind) && e.Kind != EntityKind.Mine && e.PlayerId == _seat)
                        StructuresLost++;
                    _lastShooterSeat.Remove(ev.A);
                    break;
                }
                case GameEventType.Captured when Valid(ev.A):
                {
                    int before = _tickOwner.TryGetValue(ev.A, out int t) ? t
                               : preTickOwner.TryGetValue(ev.A, out int p) ? p : -1;
                    if (before == _seat && ev.B != _seat) StructuresLost++;
                    _tickOwner[ev.A] = ev.B;
                    break;
                }
            }
        }
        long harvested = 0;
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i];
            if (e.Kind != EntityKind.Harvester) continue;
            if (!e.Alive || e.PlayerId != _seat) { _carry.Remove(i); continue; }
            if (_carry.TryGetValue(i, out int was) && e.Carry < was) harvested += was - e.Carry;
            _carry[i] = e.Carry;
        }
        CreditsHarvested += harvested;
        long now = w.Credits(_seat);
        long expected = _credits + harvested + robbedIn - robbedOut;
        if (expected > now) CreditsSpent += expected - now;
        _credits = now;
    }

    /// <summary>Was this hostile mobile's death this seat's doing? The last
    /// shooter on record decides; with none, an own area weapon going off this
    /// tick or an own heavy unit within a cell of the body (a crush) does.</summary>
    private bool CreditedToMe(int id, in Entity body, IReadOnlyList<Entity> ents, bool ownBlast)
    {
        if (_lastShooterSeat.TryGetValue(id, out int shooter)) return shooter == _seat;
        if (ownBlast) return true;
        var oneCell = Fix64.FromInt(1);
        for (int i = 0; i < ents.Count; i++)
        {
            var o = ents[i];
            if (o.Alive && o.PlayerId == _seat && o.Kind == EntityKind.Unit && o.Armour == ArmourClass.Heavy
                && Fix64.DistSq(o.X - body.X, o.Y - body.Y) <= oneCell)
                return true;
        }
        return false;
    }
}

/// <summary>
/// P8-34: every match ends HERE, not on a banner. A banner said VICTORY or
/// DEFEAT and "press escape", so the only way on was the main menu: no way to
/// try the same match again, no way into the next mission, and nothing about
/// how the match had gone. This is the results screen in the uplink overlay's
/// clothes, over the battlefield it describes: the verdict, the numbers
/// (MatchStats), and the ways on. RETRY, offered only where this client can
/// actually restart the match; NEXT MISSION, after a campaign win with a
/// mission after it; MAIN MENU always, and the cancel key with it, named from
/// the live binding.
///
/// It issues nothing itself: each button calls back into the scene, which
/// owns the scene change.
/// </summary>
public partial class ResultsScreen : Control
{
    /// <summary>Everything the screen shows, decided by the scene.</summary>
    public sealed record Model(
        string Verdict, Color VerdictColour, string Describe,
        IReadOnlyList<(string Label, string Value)> Rows,
        IReadOnlyList<string> Notes,
        System.Action? OnRetry, System.Action? OnNextMission, string? NextMissionTitle,
        System.Action OnMainMenu, string CancelKey);

    public const string RetryText = "RETRY";
    public const string NextMissionText = "NEXT MISSION";
    public const string MainMenuText = "MAIN MENU";

    private Label _heading = null!;
    private Label _footer = null!;
    private readonly Dictionary<string, Label> _values = new();
    private readonly Dictionary<string, Button> _buttons = new();

    /// <summary>The stat rows, in screen order, from one tally. One place, so
    /// the labels a player reads and the ones a check asks for cannot differ.</summary>
    public static List<(string Label, string Value)> RowsOf(MatchStats s, int matchTick) => new()
    {
        ("UNITS BUILT", s.UnitsBuilt.ToString()),
        ("UNITS LOST", s.UnitsLost.ToString()),
        ("UNITS KILLED", s.UnitsKilled.ToString()),
        ("STRUCTURES BUILT", s.StructuresBuilt.ToString()),
        ("STRUCTURES LOST", s.StructuresLost.ToString()),
        ("CREDITS HARVESTED", s.CreditsHarvested.ToString()),
        ("CREDITS SPENT", s.CreditsSpent.ToString()),
        ("DURATION", Duration(matchTick)),
    };

    /// <summary>A match clock in ticks as a player reads it: minutes and
    /// seconds, with hours in front once there are any.</summary>
    public static string Duration(int ticks)
    {
        int secs = System.Math.Max(0, ticks) / World.TicksPerSecond;
        return secs >= 3600
            ? $"{secs / 3600}:{secs / 60 % 60:00}:{secs % 60:00}"
            : $"{secs / 60}:{secs % 60:00}";
    }

    public void Init(Model m)
    {
        Name = "ResultsScreen";
        AnchorRight = 1; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
        // The battle's own scrim weight, so the field the numbers describe
        // still shows behind them (the pause menu's reasoning).
        var overlay = UplinkUi.FullOverlay(this, 0.82f);
        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -300, OffsetRight = 300, OffsetTop = -300, OffsetBottom = 300,
        };
        var style = new StyleBoxFlat { BgColor = UplinkUi.Panel, BorderColor = UplinkUi.Seam };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 28; style.ContentMarginRight = 28;
        style.ContentMarginTop = 20; style.ContentMarginBottom = 20;
        panel.AddThemeStyleboxOverride("panel", style);
        overlay.AddChild(panel);
        var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 8);
        panel.AddChild(v);

        _heading = new Label { Text = m.Verdict, HorizontalAlignment = HorizontalAlignment.Center };
        _heading.AddThemeFontSizeOverride("font_size", 40);
        _heading.AddThemeColorOverride("font_color", m.VerdictColour);
        v.AddChild(_heading);
        v.AddChild(UplinkUi.Note(m.Describe, 13));
        v.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 48);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (var (label, value) in m.Rows)
        {
            var l = new Label { Text = label };
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", UplinkUi.Dim);
            grid.AddChild(l);
            var val = new Label { Text = value, HorizontalAlignment = HorizontalAlignment.Right };
            val.AddThemeFontSizeOverride("font_size", 14);
            val.AddThemeColorOverride("font_color", UplinkUi.Bone);
            grid.AddChild(val);
            _values[label] = val;
        }
        v.AddChild(grid);
        foreach (string note in m.Notes) v.AddChild(UplinkUi.Note(note, 11));
        v.AddChild(new HSeparator());

        if (m.OnRetry is { } retry) AddButton(v, RetryText, retry);
        if (m.OnNextMission is { } next)
        {
            var b = AddButton(v, NextMissionText, next);
            if (m.NextMissionTitle is { } title) b.TooltipText = $"Mission briefing: {title}";
        }
        AddButton(v, MainMenuText, m.OnMainMenu);
        _footer = UplinkUi.Note($"or press {m.CancelKey} for the main menu", 11);
        v.AddChild(_footer);
    }

    private Button AddButton(VBoxContainer v, string text, System.Action onPress)
    {
        var b = UplinkUi.MenuButton(text, onPress);
        _buttons[text] = b;
        v.AddChild(b);
        return b;
    }

    // ---- Verification surface: what the screen SHOWS, never a recomputation.
    public string HeadingText => _heading.Text;
    public string FooterText => _footer.Text;
    /// <summary>The value shown beside a stat label, or null if no such row.</summary>
    public string? ValueShown(string label) => _values.TryGetValue(label, out var l) ? l.Text : null;
    public IReadOnlyCollection<string> RowLabels => _values.Keys;
    public bool Offers(string button) => _buttons.ContainsKey(button);
    /// <summary>Press a button the way a click does, through its own signal.</summary>
    public bool Press(string button)
    {
        if (!_buttons.TryGetValue(button, out var b) || b.Disabled) return false;
        b.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }
    /// <summary>Every word on the screen, notes included.</summary>
    public string AllText()
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Node n)
        {
            if (n is Label l) sb.Append(l.Text).Append('\n');
            else if (n is Button b) sb.Append(b.Text).Append('\n');
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(this);
        return sb.ToString();
    }
}
