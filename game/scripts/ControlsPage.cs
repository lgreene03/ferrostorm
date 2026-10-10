using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// P8-35: the CONTROLS page, every key the battle answers to and the key it
/// is bound to NOW.
///
/// GENERATED from <see cref="Settings.Bindable"/>, one row per entry in that
/// table's order, each key read from the live binding. There is no list of
/// controls anywhere else to fall behind the table: an action added to
/// Bindable appears here with no edit to this file, and a rebind shows the
/// moment it lands (Settings.BindsChanged), on this page and on any other
/// open copy of it. The one hand-written line is the note under the rows,
/// which says what the modifier keys do to the keys above it, because a
/// modifier is read off the key event rather than bound.
///
/// Reachable from the main menu and from the pause menu, the same page in
/// both, and (P8-45) from the battle's help key, where the help overlay is
/// this page. Presentation only: it reads Settings and changes nothing.
///
/// P8-45 put the MOUSE above the keys. The key strip along the bottom of the
/// battle named the mouse verbs and the overlay replaced the strip, so the
/// page has to name them or the replacement loses them. A gesture is not
/// bindable, so it cannot come from Settings.Bindable; the gestures are one
/// table here (MouseGestures), read by every copy of the page.
///
/// P8-36: one table PER SCHEME, and the page shows the scheme in force,
/// named in the heading, re-read whenever the scheme changes (SetScheme
/// raises BindsChanged), so the page never describes a mouse the battle is
/// not answering to.
/// </summary>
public partial class ControlsPage : VBoxContainer
{
    private readonly List<(string Action, Label Label, Label Key)> _rows = new();
    private readonly List<(Label Gesture, Label Does)> _mouseRows = new();
    private GridContainer _mouseGrid = null!;
    private Label _mouseHeading = null!;
    private ControlScheme _mouseScheme;

    /// <summary>What each mouse gesture does in a battle under the scheme in
    /// force. The one list of them: the strip's verbs, and the force-attack
    /// gesture (D23) that nothing on screen advertised (P8-8's finding).</summary>
    public static IReadOnlyList<(string Gesture, string Does)> MouseGestures() => MouseGestures(Settings.Scheme);

    /// <summary>P8-36: what each gesture does under a given scheme.</summary>
    public static IReadOnlyList<(string Gesture, string Does)> MouseGestures(ControlScheme scheme) =>
        scheme == ControlScheme.Classic ? ClassicMouse : ModernMouse;

    /// <summary>The heading the mouse rows sit under, naming the scheme.</summary>
    public static string MouseHeading(ControlScheme scheme) =>
        scheme == ControlScheme.Classic ? "MOUSE: CLASSIC SCHEME" : "MOUSE: MODERN SCHEME";

    private static readonly (string Gesture, string Does)[] ModernMouse =
    {
        ("LEFT CLICK", "SELECT; DRAG TO BOX-SELECT; SHIFT ADDS TO THE SELECTION"),
        ("DOUBLE-CLICK OR CTRL + CLICK", "EVERY UNIT OF THAT TYPE ON SCREEN; AN MCV DEPLOYS"),
        ("RIGHT CLICK", "MOVE, ATTACK, HARVEST, ENTER OR BOARD; SETS A FACTORY'S RALLY POINT"),
        ("SHIFT + RIGHT CLICK", "QUEUES THE ORDER BEHIND THE LAST"),
        ("CTRL + RIGHT CLICK", "FORCE-ATTACK: FELLS A NEUTRAL BRIDGE"),
        ("WHEEL, SCREEN EDGE, MINIMAP", "MOVE THE CAMERA"),
    };

    /// <summary>Doc 11's Scheme A as built: the click on something of your
    /// own selects it, the click anywhere else orders the selection there,
    /// and the right click takes things away. An own Carrier is the one thing
    /// of your own a click orders at, when the selection can board it.</summary>
    private static readonly (string Gesture, string Does)[] ClassicMouse =
    {
        ("LEFT CLICK ON YOUR OWN", "SELECT; DRAG TO BOX-SELECT; SHIFT ADDS TO THE SELECTION"),
        ("LEFT CLICK ANYWHERE ELSE", "MOVE, ATTACK, HARVEST OR ENTER; WITH A FACTORY SELECTED, SETS ITS RALLY POINT"),
        ("LEFT CLICK ON YOUR OWN CARRIER", "BOARDS IT, WHEN WHAT IS SELECTED CAN BOARD"),
        ("DOUBLE-CLICK OR CTRL + CLICK ON YOUR OWN", "EVERY UNIT OF THAT TYPE ON SCREEN; AN MCV DEPLOYS"),
        ("SHIFT + LEFT CLICK ELSEWHERE", "QUEUES THE ORDER BEHIND THE LAST"),
        ("CTRL + LEFT CLICK ON A BRIDGE", "FORCE-ATTACK: FELLS A NEUTRAL BRIDGE"),
        ("RIGHT CLICK", "CANCELS A TARGETING OR A PLACEMENT; OTHERWISE DESELECTS"),
        ("WHEEL, SCREEN EDGE, MINIMAP", "MOVE THE CAMERA"),
    };

    public override void _Ready()
    {
        Name = "ControlsPage";
        AddThemeConstantOverride("separation", 8);
        Settings.EnsureLoaded();
        // The table is long (every group and bookmark has its own row), so it
        // scrolls inside the page rather than pushing the page off the screen.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 340),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        // The scrollbar draws over its content, so the key column keeps clear
        // of it (the settings page's lesson).
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_right", 18);
        scroll.AddChild(pad);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        pad.AddChild(body);

        _mouseHeading = Heading(MouseHeading(Settings.Scheme));
        body.AddChild(_mouseHeading);
        _mouseGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _mouseGrid.AddThemeConstantOverride("h_separation", 24);
        _mouseGrid.AddThemeConstantOverride("v_separation", 3);
        body.AddChild(_mouseGrid);
        FillMouse();

        body.AddChild(Heading("KEYS"));
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 3);
        body.AddChild(grid);
        foreach (var (action, label) in Settings.Bindable)
        {
            var l = new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", UplinkUi.Bone);
            grid.AddChild(l);
            var k = new Label { HorizontalAlignment = HorizontalAlignment.Right };
            k.AddThemeFontSizeOverride("font_size", 12);
            k.AddThemeColorOverride("font_color", UplinkUi.FerriteGold);
            grid.AddChild(k);
            _rows.Add((action, l, k));
        }
        AddChild(UplinkUi.Note(
            "hold CTRL (or CMD) with a group key to put the selection in that group, or with a bookmark key to store "
            + "the view there; hold ALT with a group key to build that slot of the open sidebar tab. "
            + "Change any key, or the mouse scheme, under SETTINGS.", 11));
        Refresh();
    }

    /// <summary>The mouse rows of the scheme in force, built afresh.</summary>
    private void FillMouse()
    {
        foreach (var child in _mouseGrid.GetChildren()) { _mouseGrid.RemoveChild(child); child.QueueFree(); }
        _mouseRows.Clear();
        _mouseScheme = Settings.Scheme;
        _mouseHeading.Text = MouseHeading(_mouseScheme);
        foreach (var (gesture, does) in MouseGestures(_mouseScheme))
        {
            var gl = new Label { Text = gesture };
            gl.AddThemeFontSizeOverride("font_size", 12);
            gl.AddThemeColorOverride("font_color", UplinkUi.FerriteGold);
            _mouseGrid.AddChild(gl);
            var dl = new Label
            {
                Text = does,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(260, 0),
            };
            dl.AddThemeFontSizeOverride("font_size", 12);
            dl.AddThemeColorOverride("font_color", UplinkUi.Bone);
            _mouseGrid.AddChild(dl);
            _mouseRows.Add((gl, dl));
        }
    }

    public override void _EnterTree() => Settings.BindsChanged += Refresh;
    public override void _ExitTree() => Settings.BindsChanged -= Refresh;

    /// <summary>Every key column back from the live bindings, and (P8-36) the
    /// mouse rows again if the scheme has changed under them.</summary>
    private void Refresh()
    {
        foreach (var (action, _, key) in _rows)
            if (IsInstanceValid(key)) key.Text = Settings.KeyName(Settings.BindOf(action));
        if (IsInstanceValid(_mouseGrid) && _mouseScheme != Settings.Scheme) FillMouse();
    }

    /// <summary>P8-36 verification read: the heading over the mouse rows.</summary>
    public string MouseHeadingShown() => _mouseHeading.Text;

    /// <summary>Verification read: each row as SHOWN, label and key, in
    /// screen order.</summary>
    public List<(string Label, string Key)> RowsShown()
    {
        var l = new List<(string, string)>(_rows.Count);
        foreach (var (_, label, key) in _rows) l.Add((label.Text, key.Text));
        return l;
    }

    /// <summary>P8-45 verification read: each mouse row as SHOWN.</summary>
    public List<(string Gesture, string Does)> MouseRowsShown()
    {
        var l = new List<(string, string)>(_mouseRows.Count);
        foreach (var (g, d) in _mouseRows) l.Add((g.Text, d.Text));
        return l;
    }

    private static Label Heading(string text)
    {
        var h = new Label { Text = text };
        h.AddThemeFontSizeOverride("font_size", 13);
        h.AddThemeColorOverride("font_color", UplinkUi.Dim);
        return h;
    }
}
