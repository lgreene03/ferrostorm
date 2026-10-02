using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// The POWERS strip: one button per (owned structure, power that structure
/// grants), each reading READY or a seconds countdown off the structure's own
/// ChargeTicks.
///
/// CommandType.UseSupportPower has existed since P7-21, the battery asserts
/// every power and SkirmishAI fires them, and the client issued it nowhere. So
/// all five powers P7 shipped were reachable by the AI and the test battery
/// and by no human at all, and the playtest brief's Match 3 ("build the
/// unlocking building and use it") could not be played. This is the surface
/// that closes that gap.
///
/// It is a separate Control rather than more of SkirmishLive for the reason
/// the Sidebar and Minimap are: it reads nothing on its own and holds no
/// command logic. The scene hands it the entries each frame (the Minimap
/// idiom) and is handed back a press through one callback, so every order and
/// every refusal stays in the one place that issues Commands with
/// LocalPlayerId.
///
/// A charging power is GREYED, not disabled. A disabled Godot button swallows
/// a click without a word, and a power that silently ignores a click reads as
/// a bug; an enabled grey one lets the scene explain itself with a toast.
/// </summary>
public partial class SupportPowerBar : VBoxContainer
{
    /// <summary>One button's worth: which building, which power, and that
    /// building's live charge. Powers on one building SHARE its single charge
    /// (ADR-064), so the two Bastion entries always carry the same number, and
    /// firing either greys both - which is what makes "scan OR strike" read as
    /// a choice rather than as a bug.</summary>
    public readonly record struct Entry(int StructureId, int PowerId, int ChargeTicks)
    {
        public bool Ready => ChargeTicks == 0;
    }

    /// <summary>The display name of a power, keyed on the sim's own constants
    /// (World.OrbitalScanPowerId and siblings), using the five names GDD s3
    /// gives them. An unknown id THROWS rather than shrugging, the
    /// UnitNameOf rule: a power the client cannot name is a power added to the
    /// sim and not to here, and a generic fallback is what would let that pass
    /// unnoticed. The harness proves every power any registered building grants
    /// has a name, so the throw is unreachable for the shipped catalogue.</summary>
    public static string NameOf(int powerId) => powerId switch
    {
        World.OrbitalScanPowerId => "ORBITAL SCAN",
        World.PrecisionStrikePowerId => "PRECISION STRIKE",
        World.RadarJammingPowerId => "RADAR JAMMING",
        World.TunnelDeploymentPowerId => "TUNNEL DEPLOYMENT",
        World.DecoyArmyPowerId => "DECOY ARMY",
        _ => throw new System.ArgumentOutOfRangeException(nameof(powerId), powerId,
            "a support power the client has no name for"),
    };

    /// <summary>Does this power fire at a map point? Every power but RADAR
    /// JAMMING, whose sim handler ignores X/Y outright because "a radar is not
    /// somewhere you aim" (P7-24, ADR-065). So jamming is fired by its button
    /// alone, and the other four arm and wait for the ground.</summary>
    public static bool IsTargeted(int powerId) => powerId != World.RadarJammingPowerId;

    // The doc 16 palette is closed, so the colours come from UplinkUi's public
    // copy rather than a fourth one. The greyed text is the Sidebar's disabled
    // font colour, the same "not now" grey a player already reads there.
    private static readonly Color Bone = UplinkUi.Bone;
    private static readonly Color FerriteGold = UplinkUi.FerriteGold;
    private static readonly Color Seam = UplinkUi.Seam;
    private static readonly Color GreyedText = new(0.35f, 0.34f, 0.32f);

    private enum Look { None, Ready, Charging, Armed }

    private System.Action<int, int>? _onPress;
    private Label _header = null!;
    private readonly List<Entry> _entries = new();
    private readonly List<Button> _buttons = new();
    private readonly List<Look> _looks = new();
    private StyleBoxFlat _readyBox = null!, _chargingBox = null!, _armedBox = null!, _hoverBox = null!;

    /// <summary>Build the strip. `onPress` receives (structure id, power id),
    /// and the scene decides what a press means: arm, fire, or refuse.</summary>
    public void Init(System.Action<int, int> onPress)
    {
        _onPress = onPress;
        Name = "SupportPowerBar";
        // Top left, under the status line. The sidebar owns the right edge,
        // the minimap and the selection readout own the bottom left, and the
        // objective, desync notice and banner own the centre, so this corner is
        // the one stretch of the HUD nothing else claims. It is also where the
        // classic sidebar-era games kept their support powers.
        Position = new Vector2(16, 40);
        // The strip itself must not eat clicks meant for the battlefield; only
        // its buttons stop the mouse.
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        AddThemeConstantOverride("separation", 3);

        _header = new Label { MouseFilter = MouseFilterEnum.Ignore };
        _header.AddThemeFontSizeOverride("font_size", 12);
        _header.AddThemeColorOverride("font_color", FerriteGold);
        AddChild(_header);

        // Sidebar.MakeButton's four boxes: normal for ready, its disabled look
        // for charging, its pressed look for armed, and its gold hover.
        _readyBox = new StyleBoxFlat { BgColor = new Color(0.12f, 0.13f, 0.14f), BorderColor = Seam };
        _readyBox.SetBorderWidthAll(1);
        _readyBox.ContentMarginLeft = 8; _readyBox.ContentMarginRight = 8;
        _readyBox.ContentMarginTop = 4; _readyBox.ContentMarginBottom = 4;
        _hoverBox = (StyleBoxFlat)_readyBox.Duplicate();
        _hoverBox.BgColor = new Color(0.16f, 0.17f, 0.19f);
        _hoverBox.BorderColor = FerriteGold;
        _chargingBox = (StyleBoxFlat)_readyBox.Duplicate();
        _chargingBox.BgColor = new Color(0.075f, 0.08f, 0.085f);
        _chargingBox.BorderColor = new Color(0.12f, 0.13f, 0.14f);
        _armedBox = (StyleBoxFlat)_readyBox.Duplicate();
        _armedBox.BgColor = new Color(0.23f, 0.19f, 0.11f);
        _armedBox.BorderColor = FerriteGold;
        _armedBox.SetBorderWidthAll(2);
    }

    /// <summary>
    /// The per-frame handover. `takesOrders` is false in a replay, where the
    /// strip is hidden for the sidebar's reason: it is a command surface, and
    /// lit buttons that do nothing are the "silently ignores orders" read.
    /// Hidden entirely, too, when the player owns no power building, so a
    /// faction or an opening without one carries no empty furniture.
    /// </summary>
    public void Refresh(IReadOnlyList<Entry> entries, (int Structure, int Power)? armed, bool takesOrders)
    {
        bool sameSet = entries.Count == _entries.Count;
        for (int i = 0; sameSet && i < entries.Count; i++)
            sameSet = entries[i].StructureId == _entries[i].StructureId && entries[i].PowerId == _entries[i].PowerId;
        _entries.Clear();
        _entries.AddRange(entries);
        // Rebuilt only when the SET of buttons changes (a building rises, dies
        // or changes hands), never per frame: a button freed and remade every
        // frame cannot be clicked, because the press lands on a node that is
        // already gone.
        if (!sameSet) Rebuild();

        Visible = takesOrders && _entries.Count > 0;
        if (!Visible) return;
        // The live binding rather than the default, the hint line's rule: a
        // strip that says V to a player who rebound it is worse than silence.
        _header.Text = $"SUPPORT POWERS   [{Settings.KeyName(Settings.BindOf("support_power"))}]";
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            bool isArmed = armed is { } a && a.Structure == e.StructureId && a.Power == e.PowerId;
            var look = isArmed ? Look.Armed : e.Ready ? Look.Ready : Look.Charging;
            var b = _buttons[i];
            b.Text = look switch
            {
                Look.Armed => $"{NameOf(e.PowerId)}   ARMED",
                Look.Ready => $"{NameOf(e.PowerId)}   READY",
                _ => $"{NameOf(e.PowerId)}   {Mathf.CeilToInt(e.ChargeTicks / (float)World.TicksPerSecond)}s",
            };
            // Theme overrides are written on a CHANGE of look only. Each one
            // raises a theme-changed notification and a relayout, which is not
            // a cost to pay sixty times a second for a strip that changes state
            // once a minute.
            if (_looks[i] == look) continue;
            _looks[i] = look;
            b.AddThemeStyleboxOverride("normal", look switch
            {
                Look.Armed => _armedBox,
                Look.Ready => _readyBox,
                _ => _chargingBox,
            });
            b.AddThemeColorOverride("font_color", look == Look.Charging ? GreyedText : Bone);
            b.AddThemeColorOverride("font_hover_color", look == Look.Charging ? GreyedText : Bone);
        }
    }

    private void Rebuild()
    {
        foreach (var old in _buttons)
        {
            RemoveChild(old);
            old.QueueFree();
        }
        _buttons.Clear();
        _looks.Clear();
        foreach (var e in _entries)
        {
            int structureId = e.StructureId, powerId = e.PowerId;
            var b = new Button
            {
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(210, 0),
                // NO keyboard focus. A clicked button keeps focus by default,
                // and a focused button answers ui_accept - so the next press of
                // Space (jump_to_event) would fire the power again instead of
                // reaching the battlefield.
                FocusMode = FocusModeEnum.None,
            };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeStyleboxOverride("hover", _hoverBox);
            b.AddThemeStyleboxOverride("pressed", _armedBox);
            // The binding is captured per button at build time, and that is
            // safe because any change to the set rebuilds every button.
            b.Pressed += () => _onPress?.Invoke(structureId, powerId);
            AddChild(b);
            _buttons.Add(b);
            _looks.Add(Look.None);
        }
    }

    // ---- Verification surface: what the strip SHOWS, never a recomputation.

    public int EntryCount => _entries.Count;
    public int IndexOf(int structureId, int powerId)
    {
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].StructureId == structureId && _entries[i].PowerId == powerId) return i;
        return -1;
    }
    public string EntryText(int i) => _buttons[i].Text;
    public bool EntryGreyed(int i) => _looks[i] == Look.Charging;
    /// <summary>Press a button through its own Pressed signal, the
    /// Sidebar.TriggerSlot idiom, so a check drives the wiring a mouse click
    /// drives rather than calling the scene's handler directly.</summary>
    public void PressEntryForTest(int i) => _buttons[i].EmitSignal(BaseButton.SignalName.Pressed);
}
