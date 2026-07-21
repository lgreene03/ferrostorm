using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SET-01: the three named audio buses, created once per process.
///
/// The client shipped with no bus layout at all: every voice AudioDirector owns
/// played on Master, which is why there was nothing for a volume slider to hold
/// on to. Built in code rather than as a default_bus_layout.tres because the
/// layout is three sends and a name each, and a hand-authored binary resource
/// is a thing that can rot silently against the code that indexes it.
/// </summary>
public static class AudioBuses
{
    // TICKET-P6-MUSIC-01 adds the Music bus beside the original three.
    public const string Master = "Master", Sfx = "Sfx", Ui = "Ui", Ambient = "Ambient", Music = "Music";

    private static bool _built;

    public static void Ensure()
    {
        if (_built) return;
        _built = true;
        foreach (string name in new[] { Sfx, Ui, Ambient, Music })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            int idx = AudioServer.BusCount;
            AudioServer.AddBus(idx);
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, Master);
        }
    }

    /// <summary>Linear 0..1 onto a bus. Zero mutes rather than passing
    /// LinearToDb(0) = -inf into the mixer.</summary>
    public static void SetVolume(string bus, float linear)
    {
        int idx = AudioServer.GetBusIndex(bus);
        if (idx < 0) return;
        bool silent = linear <= 0.0005f;
        AudioServer.SetBusMute(idx, silent);
        if (!silent) AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(linear));
    }
}

/// <summary>
/// TICKET-P5-SET-01: player preferences, persisted to user://settings.cfg via
/// ConfigFile, and the one place that applies them.
///
/// Deliberately a static class called from the two scene roots (MainMenu and
/// SkirmishLive) rather than the GameFlow autoload doc 18 sketches. Two reasons,
/// both practical: a scene launched directly (which is how this client is
/// verified offscreen, and how Skirmish.tscn is driven scene-direct) never runs
/// an autoload's peer, so an autoload would leave the settings unapplied in
/// exactly the configuration the tests use; and the offscreen pattern appends
/// its own temporary [autoload] to project.godot, so keeping that section empty
/// in the shipped file keeps the verification harness honest.
///
/// Defaults are CAPTURED from the engine on first load rather than restated
/// here: the InputMap defaults come from project.godot's [input] block and the
/// video defaults from the live viewport, so RESET DEFAULTS restores what the
/// project actually ships instead of a second opinion about it that drifts.
/// </summary>
public static class Settings
{
    private const string ConfigPath = "user://settings.cfg";

    // ---- audio: linear 0..1, one per bus ----
    public static float MasterVolume = 0.9f;
    public static float SfxVolume = 0.9f;
    public static float UiVolume = 0.8f;
    public static float AmbientVolume = 0.6f;
    public static float MusicVolume = 0.7f;   // TICKET-P6-MUSIC-01

    // ---- video ----
    public static bool Fullscreen;
    public static bool VSync = true;
    public static int MsaaIndex;         // into MsaaOptions
    public static int RenderScaleIndex;  // into RenderScaleOptions

    public static readonly (string Label, Viewport.Msaa Value)[] MsaaOptions =
    {
        ("OFF", Viewport.Msaa.Disabled),
        ("2X", Viewport.Msaa.Msaa2X),
        ("4X", Viewport.Msaa.Msaa4X),
        ("8X", Viewport.Msaa.Msaa8X),
    };

    public static readonly (string Label, float Value)[] RenderScaleOptions =
    {
        ("50%", 0.5f), ("75%", 0.75f), ("100%", 1.0f),
    };

    /// <summary>The rebindable surface, in the order the settings scene lists
    /// it. Every literal keycode SkirmishLive and RtsCamera used to match is
    /// here and nowhere else.</summary>
    public static readonly (string Action, string Label)[] Bindable =
    {
        ("attack_move", "ATTACK-MOVE"),
        ("stop", "STOP"),
        // ADR-015 / TICKET-P6-C1a: the three unit command stances. Hold-fire is
        // a toggle (aggressive when already held), guard sets a leashed hold in
        // place, and patrol arms a two-point cycle whose far point the next click
        // supplies - each a presentation-only issue of the one SetStance command.
        ("hold_fire", "HOLD FIRE"),
        ("guard", "GUARD"),
        ("patrol", "PATROL"),
        ("repair", "REPAIR"),
        ("sell", "SELL"),
        ("deploy", "DEPLOY"),   // TICKET-P5-SPAWN-03: unpack the selected MCV
        // TICKET-P5-ALERT-02: fly the camera to the most recent alert
        // (GDD s7 line 85's "jump-to-event key"). Space, which the [input]
        // block had free.
        ("jump_to_event", "JUMP TO EVENT"),
        ("pause_menu", "OPERATIONS MENU"),
        ("cancel", "CANCEL / BACK"),
        ("camera_left", "CAMERA LEFT"),
        ("camera_right", "CAMERA RIGHT"),
        ("camera_forward", "CAMERA FORWARD"),
        ("camera_back", "CAMERA BACK"),
        ("group_1", "GROUP 1"), ("group_2", "GROUP 2"), ("group_3", "GROUP 3"),
        ("group_4", "GROUP 4"), ("group_5", "GROUP 5"), ("group_6", "GROUP 6"),
        ("group_7", "GROUP 7"), ("group_8", "GROUP 8"), ("group_9", "GROUP 9"),
    };

    private static readonly Dictionary<string, Key> _defaultBinds = new();
    private static readonly Dictionary<string, Key> _binds = new();
    private static Viewport.Msaa _defaultMsaa = Viewport.Msaa.Msaa4X;
    private static bool _loaded;

    public static IReadOnlyDictionary<string, Key> Binds => _binds;

    /// <summary>Called from every scene root. Idempotent per process: the
    /// capture of engine defaults must happen exactly once, before any override
    /// has been written over the top of them.</summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        CaptureDefaults();
        AudioBuses.Ensure();
        Load();
        ApplyAll();
    }

    private static void CaptureDefaults()
    {
        foreach (var (action, _) in Bindable)
        {
            if (!InputMap.HasAction(action))
            {
                // A missing action means project.godot's [input] block and this
                // table have drifted apart. Say so rather than binding into the
                // void and leaving a dead row in the settings scene.
                GD.PushError($"Settings: project.godot defines no input action '{action}'");
                continue;
            }
            _defaultBinds[action] = FirstKeyOf(action);
        }
        var root = Root();
        if (root != null) _defaultMsaa = root.Msaa3D;
        MsaaIndex = System.Array.FindIndex(MsaaOptions, o => o.Value == _defaultMsaa);
        if (MsaaIndex < 0) MsaaIndex = 2;
        RenderScaleIndex = System.Array.FindIndex(RenderScaleOptions, o => o.Value == 1.0f);
        foreach (var kv in _defaultBinds) _binds[kv.Key] = kv.Value;
    }

    private static Window? Root() => (Engine.GetMainLoop() as SceneTree)?.Root;

    private static Key FirstKeyOf(string action)
    {
        foreach (var ev in InputMap.ActionGetEvents(action))
            if (ev is InputEventKey k)
                return k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode;
        return Key.None;
    }

    // ---------------- persistence ----------------

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok) return;   // no file yet: defaults stand

        MasterVolume = (float)cfg.GetValue("audio", "master", MasterVolume);
        SfxVolume = (float)cfg.GetValue("audio", "sfx", SfxVolume);
        UiVolume = (float)cfg.GetValue("audio", "ui", UiVolume);
        AmbientVolume = (float)cfg.GetValue("audio", "ambient", AmbientVolume);
        MusicVolume = (float)cfg.GetValue("audio", "music", MusicVolume);

        Fullscreen = (bool)cfg.GetValue("video", "fullscreen", Fullscreen);
        VSync = (bool)cfg.GetValue("video", "vsync", VSync);
        MsaaIndex = Mathf.Clamp((int)cfg.GetValue("video", "msaa", MsaaIndex), 0, MsaaOptions.Length - 1);
        RenderScaleIndex = Mathf.Clamp((int)cfg.GetValue("video", "render_scale", RenderScaleIndex),
            0, RenderScaleOptions.Length - 1);

        foreach (var (action, _) in Bindable)
        {
            if (!cfg.HasSectionKey("keys", action)) continue;
            var key = (Key)(int)cfg.GetValue("keys", action);
            if (key != Key.None) _binds[action] = key;
        }
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ui", UiVolume);
        cfg.SetValue("audio", "ambient", AmbientVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("video", "fullscreen", Fullscreen);
        cfg.SetValue("video", "vsync", VSync);
        cfg.SetValue("video", "msaa", MsaaIndex);
        cfg.SetValue("video", "render_scale", RenderScaleIndex);
        // Only rebound actions are written. A settings file that restates every
        // default is a settings file that silently pins them: change a default
        // in project.godot and every existing player keeps the old one forever.
        foreach (var (action, _) in Bindable)
            if (_binds.TryGetValue(action, out var k)
                && _defaultBinds.TryGetValue(action, out var d) && k != d)
                cfg.SetValue("keys", action, (int)k);
        var err = cfg.Save(ConfigPath);
        if (err != Error.Ok) GD.PushError($"Settings: could not write {ConfigPath}: {err}");
    }

    // ---------------- applying ----------------

    public static void ApplyAll()
    {
        ApplyAudio();
        ApplyVideo();
        ApplyBinds();
    }

    public static void ApplyAudio()
    {
        AudioBuses.Ensure();
        AudioBuses.SetVolume(AudioBuses.Master, MasterVolume);
        AudioBuses.SetVolume(AudioBuses.Sfx, SfxVolume);
        AudioBuses.SetVolume(AudioBuses.Ui, UiVolume);
        AudioBuses.SetVolume(AudioBuses.Ambient, AmbientVolume);
        AudioBuses.SetVolume(AudioBuses.Music, MusicVolume);
    }

    public static void ApplyVideo()
    {
        var root = Root();
        if (root != null)
        {
            root.Msaa3D = MsaaOptions[MsaaIndex].Value;
            root.Scaling3DScale = RenderScaleOptions[RenderScaleIndex].Value;
        }
        DisplayServer.WindowSetMode(Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetVsyncMode(VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    /// <summary>Rewrite the InputMap from _binds. Every action is rebuilt from
    /// its stored key rather than patched, so applying twice is applying once.</summary>
    public static void ApplyBinds()
    {
        foreach (var (action, _) in Bindable)
        {
            if (!InputMap.HasAction(action)) continue;
            if (!_binds.TryGetValue(action, out var key) || key == Key.None) continue;
            InputMap.ActionEraseEvents(action);
            InputMap.ActionAddEvent(action, new InputEventKey { Keycode = key });
        }
    }

    // ---------------- rebinding ----------------

    /// <summary>The action already holding this key, or null. This is the whole
    /// of bind-time conflict detection: a key belongs to at most one action, and
    /// the second claim on it is refused rather than accepted and left for the
    /// player to discover mid-battle when two things happen at once.</summary>
    public static string? ConflictFor(string action, Key key)
    {
        foreach (var (other, _) in Bindable)
        {
            if (other == action) continue;
            if (_binds.TryGetValue(other, out var k) && k == key) return other;
        }
        return null;
    }

    /// <summary>Bind if free. Returns the conflicting action on refusal, null on
    /// success. Rebinding an action to the key it already holds is a success and
    /// not a conflict with itself.</summary>
    public static string? TryRebind(string action, Key key)
    {
        var clash = ConflictFor(action, key);
        if (clash != null) return clash;
        _binds[action] = key;
        ApplyBinds();
        Save();
        return null;
    }

    public static Key BindOf(string action) =>
        _binds.TryGetValue(action, out var k) ? k : Key.None;

    public static Key DefaultBindOf(string action) =>
        _defaultBinds.TryGetValue(action, out var k) ? k : Key.None;

    public static string LabelOf(string action)
    {
        foreach (var (a, label) in Bindable) if (a == action) return label;
        return action;
    }

    public static string KeyName(Key k) => k == Key.None ? "UNBOUND" : OS.GetKeycodeString(k);

    /// <summary>Back to what the project ships: the captured InputMap defaults
    /// and the captured viewport state, not a restatement of them.</summary>
    public static void ResetDefaults()
    {
        MasterVolume = 0.9f; SfxVolume = 0.9f; UiVolume = 0.8f; AmbientVolume = 0.6f;
        MusicVolume = 0.7f;
        Fullscreen = false;
        VSync = true;
        MsaaIndex = System.Array.FindIndex(MsaaOptions, o => o.Value == _defaultMsaa);
        if (MsaaIndex < 0) MsaaIndex = 2;
        RenderScaleIndex = System.Array.FindIndex(RenderScaleOptions, o => o.Value == 1.0f);
        _binds.Clear();
        foreach (var kv in _defaultBinds) _binds[kv.Key] = kv.Value;
        ApplyAll();
        Save();
    }
}
