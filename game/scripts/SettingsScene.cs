using Godot;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SET-01: the settings scene (doc 18 Phase D, register item M30),
/// reachable from the main menu and returning to it. Three sections in the
/// uplink idiom: audio sliders straight onto the AudioDirector's buses, video
/// options that apply on the spot, and key rebinding over the InputMap with
/// bind-time conflict detection.
///
/// Everything here applies live and persists immediately. There is no APPLY
/// button and no CANCEL, deliberately: a slider you have to confirm is a slider
/// you cannot hear, and the audio page is the one page whose whole job is to be
/// heard while you drag it.
/// </summary>
public partial class SettingsScene : Control
{
    /// <summary>The action currently listening for a key, or null. While this is
    /// set the scene swallows key events (see _Input) so that binding P does not
    /// also trip the button underneath.</summary>
    private string? _listening;
    private Button? _listeningButton;
    private Label _notice = null!;
    private VBoxContainer _bindRows = null!;

    // Held so RESET DEFAULTS can put the page back in step with Settings without
    // rebuilding the scene under its own button's signal handler.
    private readonly System.Collections.Generic.List<(HSlider S, System.Func<float> Read)> _sliders = new();
    private readonly System.Collections.Generic.List<(OptionButton O, System.Func<int> Read)> _options = new();

    public override void _Ready()
    {
        Settings.EnsureLoaded();

        AnchorRight = 1; AnchorBottom = 1;
        AddChild(new ColorRect { Color = UplinkUi.Cinder, AnchorRight = 1, AnchorBottom = 1 });

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -300, OffsetRight = 300, OffsetTop = -320, OffsetBottom = 320,
        };
        var style = new StyleBoxFlat { BgColor = UplinkUi.Panel, BorderColor = UplinkUi.Seam };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 24; style.ContentMarginRight = 24;
        style.ContentMarginTop = 18; style.ContentMarginBottom = 18;
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var outer = new VBoxContainer();
        outer.AddThemeConstantOverride("separation", 8);
        panel.AddChild(outer);

        var heading = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 24);
        heading.AddThemeColorOverride("font_color", UplinkUi.FerriteGold);
        outer.AddChild(heading);
        outer.AddChild(new HSeparator());

        // The bind list is long enough to need a scroller, and a scroller that
        // only contains half the page scrolls the wrong half; the whole body
        // goes in one.
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        outer.AddChild(scroll);
        // The vertical scrollbar draws OVER the scroller's content, so without
        // this margin it sits on top of the volume readouts and clips "100%" to
        // "100". Caught in a capture, not by an assertion: every assertion about
        // the readouts passed while they were unreadable.
        var pad = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        pad.AddThemeConstantOverride("margin_right", 18);
        scroll.AddChild(pad);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        pad.AddChild(body);

        BuildAudio(body);
        BuildVideo(body);
        BuildBinds(body);

        outer.AddChild(new HSeparator());
        _notice = UplinkUi.Note("", 12);
        outer.AddChild(_notice);
        outer.AddChild(UplinkUi.MenuButton("RESET DEFAULTS", () =>
        {
            Settings.ResetDefaults();
            Refresh();
            Flash("every setting is back to the shipped default");
        }));
        outer.AddChild(UplinkUi.MenuButton("BACK", () =>
        {
            Settings.Save();
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        }));
    }

    // ---------------- audio ----------------

    /// <summary>
    /// The four bus sliders. Each APPLIES on every change, because the whole
    /// point of an audio slider is hearing it move, but it does NOT save on
    /// every change: ValueChanged fires once per frame across a drag, and a
    /// settings page that rewrites its config file sixty times a second while
    /// you drag a slider is a settings page doing sixty times the disk work it
    /// needs to. Saving is left to SaveSoon, which coalesces a drag into one
    /// write, and to BACK, which is the only way out of this scene.
    /// </summary>
    private void BuildAudio(VBoxContainer v)
    {
        v.AddChild(Section("AUDIO"));
        Slider(v, "MASTER", () => Settings.MasterVolume, val =>
        {
            Settings.MasterVolume = val;
            AudioBuses.SetVolume(AudioBuses.Master, val);
            SaveSoon();
        });
        Slider(v, "EFFECTS", () => Settings.SfxVolume, val =>
        {
            Settings.SfxVolume = val;
            AudioBuses.SetVolume(AudioBuses.Sfx, val);
            SaveSoon();
        });
        Slider(v, "INTERFACE", () => Settings.UiVolume, val =>
        {
            Settings.UiVolume = val;
            AudioBuses.SetVolume(AudioBuses.Ui, val);
            SaveSoon();
        });
        Slider(v, "AMBIENCE", () => Settings.AmbientVolume, val =>
        {
            Settings.AmbientVolume = val;
            AudioBuses.SetVolume(AudioBuses.Ambient, val);
            SaveSoon();
        });
        // TICKET-P6-MUSIC-01: the fifth bus slider, in the exact idiom of the
        // four above it.
        Slider(v, "MUSIC", () => Settings.MusicVolume, val =>
        {
            Settings.MusicVolume = val;
            AudioBuses.SetVolume(AudioBuses.Music, val);
            SaveSoon();
        });
    }

    /// <summary>Coalesce a burst of changes into one write a short while after
    /// the last of them. A drag is one save, not one per frame.</summary>
    private void SaveSoon()
    {
        _saveDue = SaveDelay;
        _savePending = true;
    }

    private const double SaveDelay = 0.4;
    private double _saveDue;
    private bool _savePending;

    public override void _Process(double delta)
    {
        if (!_savePending) return;
        _saveDue -= delta;
        if (_saveDue > 0) return;
        _savePending = false;
        Settings.Save();
    }

    /// <summary>Never leave a pending write behind: whatever the player last
    /// dragged must be on disk before this scene stops existing, whether they
    /// left through BACK or the tree came down under them.</summary>
    public override void _ExitTree()
    {
        if (_savePending) { _savePending = false; Settings.Save(); }
    }

    // ---------------- video ----------------

    private void BuildVideo(VBoxContainer v)
    {
        v.AddChild(Section("VIDEO"));

        var display = Options(v, "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" },
            () => Settings.Fullscreen ? 1 : 0);
        display.ItemSelected += idx =>
        {
            Settings.Fullscreen = idx == 1;
            Settings.ApplyVideo();
            Settings.Save();
        };

        var vsync = Options(v, "VSYNC", new[] { "OFF", "ON" }, () => Settings.VSync ? 1 : 0);
        vsync.ItemSelected += idx =>
        {
            Settings.VSync = idx == 1;
            Settings.ApplyVideo();
            Settings.Save();
        };

        var msaaLabels = new string[Settings.MsaaOptions.Length];
        for (int i = 0; i < msaaLabels.Length; i++) msaaLabels[i] = Settings.MsaaOptions[i].Label;
        var msaa = Options(v, "ANTI-ALIASING", msaaLabels, () => Settings.MsaaIndex);
        msaa.ItemSelected += idx =>
        {
            Settings.MsaaIndex = (int)idx;
            Settings.ApplyVideo();
            Settings.Save();
        };

        var scaleLabels = new string[Settings.RenderScaleOptions.Length];
        for (int i = 0; i < scaleLabels.Length; i++) scaleLabels[i] = Settings.RenderScaleOptions[i].Label;
        var scale = Options(v, "RENDER SCALE", scaleLabels, () => Settings.RenderScaleIndex);
        scale.ItemSelected += idx =>
        {
            Settings.RenderScaleIndex = (int)idx;
            Settings.ApplyVideo();
            Settings.Save();
        };
    }

    // ---------------- key rebinding ----------------

    private void BuildBinds(VBoxContainer v)
    {
        v.AddChild(Section("CONTROLS"));
        v.AddChild(UplinkUi.Note(
            "click a key to rebind it, then press the new key. a key already in use is refused, and the binding it would have broken is named.", 11));
        _bindRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _bindRows.AddThemeConstantOverride("separation", 4);
        v.AddChild(_bindRows);
        FillBindRows();
        v.AddChild(UplinkUi.Note(
            "the camera pans on the arrow keys, the screen edge and the minimap. it panned on WASD until this ticket, and A and S are worth more as attack-move and stop; rebind the camera onto them here if you would rather have it back.", 11));
    }

    private void FillBindRows()
    {
        foreach (var child in _bindRows.GetChildren()) child.QueueFree();
        foreach (var (action, label) in Settings.Bindable)
        {
            string a = action;
            var h = new HBoxContainer();
            var l = new Label { Text = label, CustomMinimumSize = new Vector2(200, 0) };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", UplinkUi.Bone);
            h.AddChild(l);
            var b = UplinkUi.MenuButton(Settings.KeyName(Settings.BindOf(action)), () => { });
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            b.Pressed += () => Listen(a, b);
            h.AddChild(b);
            _bindRows.AddChild(h);
        }
    }

    private void Listen(string action, Button button)
    {
        if (_listeningButton != null) _listeningButton.Text = Settings.KeyName(Settings.BindOf(_listening!));
        _listening = action;
        _listeningButton = button;
        button.Text = "PRESS A KEY";
        Flash($"binding {Settings.LabelOf(action)} - press a key, or escape to keep {Settings.KeyName(Settings.BindOf(action))}");
    }

    /// <summary>_Input, not _UnhandledInput: while listening this must see the
    /// key before anything else does, and it must consume it. Bound at the
    /// viewport so the press cannot reach the button that is still focused.</summary>
    public override void _Input(InputEvent ev)
    {
        if (_listening is not { } action) return;
        if (ev is not InputEventKey { Pressed: true, Echo: false } key) return;
        GetViewport().SetInputAsHandled();

        // Escape aborts the capture rather than binding escape. Rebinding CANCEL
        // itself is still possible - press any other key on its row - but the
        // abort has to live somewhere and every other key is a legal bind.
        if (key.Keycode == Key.Escape)
        {
            Flash($"{Settings.LabelOf(action)} left on {Settings.KeyName(Settings.BindOf(action))}");
            StopListening();
            return;
        }

        var clash = Settings.TryRebind(action, key.Keycode);
        if (clash != null)
        {
            Flash($"{Settings.KeyName(key.Keycode)} is already {Settings.LabelOf(clash)} - {Settings.LabelOf(action)} unchanged");
            StopListening();
            return;
        }
        Flash($"{Settings.LabelOf(action)} is now {Settings.KeyName(key.Keycode)}");
        StopListening();
    }

    private void StopListening()
    {
        if (_listeningButton != null && _listening != null)
            _listeningButton.Text = Settings.KeyName(Settings.BindOf(_listening));
        _listening = null;
        _listeningButton = null;
    }

    /// <summary>Drive a rebind the way the player does, for offscreen
    /// verification: arm the row, then feed it a real key event through the
    /// shipping _Input path rather than calling TryRebind behind its back.</summary>
    public bool DriveRebind(string action, Key key)
    {
        foreach (var row in _bindRows.GetChildren())
            if (row is HBoxContainer h && h.GetChild(0) is Label l && l.Text == Settings.LabelOf(action)
                && h.GetChild(1) is Button b)
            {
                b.EmitSignal(Button.SignalName.Pressed);
                _Input(new InputEventKey { Keycode = key, Pressed = true, Echo = false });
                return true;
            }
        return false;
    }

    public string NoticeText => _notice.Text;

    /// <summary>Put the widgets back in step with Settings. Every control is
    /// read back FROM Settings rather than from a remembered default, so this
    /// says the same thing the page would say if it were built fresh. Writing a
    /// slider's Value re-fires its own handler, which writes the value it just
    /// read back where it came from: harmless, and cheaper than a second path
    /// that has to be kept honest against the first.</summary>
    private void Refresh()
    {
        StopListening();
        foreach (var (s, read) in _sliders) s.Value = read();
        foreach (var (o, read) in _options) o.Select(read());
        FillBindRows();
    }

    private void Flash(string msg) => _notice.Text = msg;

    private static Label Section(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 15);
        l.AddThemeColorOverride("font_color", UplinkUi.FerriteGold);
        return l;
    }

    private void Slider(VBoxContainer parent, string label, System.Func<float> read, System.Action<float> onChange)
    {
        float value = read();
        var h = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(200, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", UplinkUi.Bone);
        h.AddChild(l);
        var s = new HSlider
        {
            Name = $"Slider{label}",
            MinValue = 0, MaxValue = 1, Step = 0.01,
            Value = value,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 18),
        };
        h.AddChild(s);
        var readout = new Label { Text = Pct(value), CustomMinimumSize = new Vector2(52, 0),
            HorizontalAlignment = HorizontalAlignment.Right };
        readout.AddThemeFontSizeOverride("font_size", 12);
        readout.AddThemeColorOverride("font_color", UplinkUi.Dim);
        h.AddChild(readout);
        s.ValueChanged += val =>
        {
            readout.Text = Pct((float)val);
            onChange((float)val);
        };
        parent.AddChild(h);
        _sliders.Add((s, read));
    }

    private static string Pct(float v) => $"{Mathf.RoundToInt(v * 100)}%";

    private OptionButton Options(VBoxContainer parent, string label, string[] items, System.Func<int> read)
    {
        int selected = read();
        var h = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(200, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", UplinkUi.Bone);
        h.AddChild(l);
        var opt = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (string i in items) opt.AddItem(i);
        if (selected >= 0 && selected < items.Length) opt.Select(selected);
        h.AddChild(opt);
        parent.AddChild(h);
        _options.Add((opt, read));
        return opt;
    }

    /// <summary>Offscreen verification hooks: reach a control by its row label
    /// and drive the REAL signal, rather than reaching past the widgets into
    /// Settings and proving only that a static field can be assigned.</summary>
    public HSlider? SliderNamed(string label)
    {
        foreach (var (s, _) in _sliders) if (s.Name == $"Slider{label}") return s;
        return null;
    }

    public string BindTextOf(string action)
    {
        foreach (var row in _bindRows.GetChildren())
            if (row is HBoxContainer h && h.GetChild(0) is Label l && l.Text == Settings.LabelOf(action)
                && h.GetChild(1) is Button b)
                return b.Text;
        return "";
    }
}
