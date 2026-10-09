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
/// both. Presentation only: it reads Settings and changes nothing.
/// </summary>
public partial class ControlsPage : VBoxContainer
{
    private readonly List<(string Action, Label Label, Label Key)> _rows = new();

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
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 3);
        pad.AddChild(grid);
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
            + "Change any key under SETTINGS.", 11));
        Refresh();
    }

    public override void _EnterTree() => Settings.BindsChanged += Refresh;
    public override void _ExitTree() => Settings.BindsChanged -= Refresh;

    /// <summary>Every key column back from the live bindings.</summary>
    private void Refresh()
    {
        foreach (var (action, _, key) in _rows)
            if (IsInstanceValid(key)) key.Text = Settings.KeyName(Settings.BindOf(action));
    }

    /// <summary>Verification read: each row as SHOWN, label and key, in
    /// screen order.</summary>
    public List<(string Label, string Key)> RowsShown()
    {
        var l = new List<(string, string)>(_rows.Count);
        foreach (var (_, label, key) in _rows) l.Add((label.Text, key.Text));
        return l;
    }
}
