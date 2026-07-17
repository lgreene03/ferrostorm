using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// Entry point in the uplink style: skirmish setup (map, AI temperament,
/// starting credits), the replay theatre, and quit. Choices flow into
/// SkirmishLive via MatchConfig before the scene change.
/// </summary>
public partial class MainMenu : Control
{
    private static readonly Color Cinder = new(0.055f, 0.06f, 0.065f);
    private static readonly Color Seam = new(0.18f, 0.196f, 0.21f);
    private static readonly Color Bone = new(0.84f, 0.82f, 0.77f);
    private static readonly Color FerriteGold = new(0.79f, 0.63f, 0.36f);

    private OptionButton _mapPick = null!;
    private OptionButton _aiPick = null!;
    private OptionButton _creditPick = null!;
    private readonly List<string> _maps = new();

    public override void _Ready()
    {
        // TICKET-P5-SET-01: the settings are applied at the scene roots rather
        // than by an autoload (Settings' own header states why). Volume, video
        // and every rebound key are live from the first frame of the menu.
        Settings.EnsureLoaded();

        AnchorRight = 1; AnchorBottom = 1;
        var bg = new ColorRect { Color = Cinder, AnchorRight = 1, AnchorBottom = 1 };
        AddChild(bg);

        var panel = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            // TICKET-P5-SAVE-01 grew the menu by two rows; the box grew with it.
            // TICKET-P5-SET-01 added LAN and SETTINGS, so it grew again.
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -292, OffsetBottom = 292,
        };
        var style = new StyleBoxFlat { BgColor = new Color(0.086f, 0.094f, 0.102f), BorderColor = Seam };
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 28; style.ContentMarginRight = 28;
        style.ContentMarginTop = 22; style.ContentMarginBottom = 22;
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);

        var title = new Label { Text = "FERROSTORM", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", FerriteGold);
        v.AddChild(title);
        var sub = new Label { Text = "UPLINK ESTABLISHED", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeFontSizeOverride("font_size", 11);
        sub.AddThemeColorOverride("font_color", new Color(0.45f, 0.44f, 0.42f));
        v.AddChild(sub);
        v.AddChild(new HSeparator());

        _mapPick = Row(v, "THEATRE");
        foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.GetFullPath(
            System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "data", "maps")), "*.fmap"))
        {
            _maps.Add(f);
            _mapPick.AddItem(System.IO.Path.GetFileNameWithoutExtension(f).ToUpperInvariant());
        }
        _aiPick = Row(v, "OPPOSITION");
        _aiPick.AddItem("STANDARD"); _aiPick.AddItem("RUSHER"); _aiPick.AddItem("TURTLE");
        _creditPick = Row(v, "TREASURY");
        _creditPick.AddItem("5000"); _creditPick.AddItem("8000"); _creditPick.AddItem("12000");
        _creditPick.Select(1);

        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("COMMENCE OPERATION", StartSkirmish));
        v.AddChild(MenuButton("CAMPAIGN", ShowCampaign));
        v.AddChild(MenuButton("LOAD GAME", ShowLoad));
        v.AddChild(MenuButton("REPLAYS", ShowReplays));
        v.AddChild(MenuButton("LAN", ShowLan));
        v.AddChild(MenuButton("SETTINGS", () => GetTree().ChangeSceneToFile("res://scenes/Settings.tscn")));
        v.AddChild(MenuButton("REPLAY THEATRE", () => GetTree().ChangeSceneToFile("res://scenes/Battle3D.tscn")));
        v.AddChild(MenuButton("STAND DOWN", () => GetTree().Quit()));
    }

    // ---------------- TICKET-P5-SAVE-01: saves and replays ----------------

    private void ShowLoad()
    {
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, "LOAD OPERATION", 320, 230);
        int found = 0;
        for (int i = 1; i <= GameFiles.SlotCount; i++)
        {
            int slot = i;
            var meta = MatchMeta.Read(GameFiles.SlotMeta(slot));
            bool occupied = meta != null && System.IO.File.Exists(GameFiles.SlotSave(slot));
            if (occupied) found++;
            v.AddChild(UplinkUi.MenuButton(
                $"SLOT {slot}   " + (occupied ? meta!.Line() : "EMPTY"),
                () => { overlay.QueueFree(); StartLoaded(slot, meta!); },
                enabled: occupied));
        }
        v.AddChild(new HSeparator());
        if (found == 0)
            v.AddChild(UplinkUi.Note("no saves yet - save from the operations menu (P) during a battle", 12));
        v.AddChild(MenuButton("BACK", () => overlay.QueueFree()));
    }

    private void StartLoaded(int slot, MatchMeta meta)
    {
        MatchConfig.ApplyFrom(meta);
        MatchConfig.LoadPath = GameFiles.SlotSave(slot);
        LaunchBattle();
    }

    /// <summary>TICKET-P5-SET-01: every road into the battle scene goes through
    /// here. NetSession is the state a NETWORKED battle owns and the HUD's
    /// desync notice reads; a single-player battle must therefore start without
    /// one, or a flag left standing by anything earlier would raise a desync
    /// notice over a game that has no network to desync from.</summary>
    private void LaunchBattle()
    {
        NetSession.Reset();
        GetTree().ChangeSceneToFile("res://scenes/Skirmish.tscn");
    }

    /// <summary>The replay browser over user://replays. Playback re-simulates
    /// the recorded command stream through the live battle pipeline, so a
    /// replay is watched in the same 3D view the match was played in rather
    /// than in the baked-JSON theatre.</summary>
    private void ShowReplays()
    {
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, "REPLAYS", 360, 250);
        var all = GameFiles.Replays();
        if (all.Count == 0)
            v.AddChild(UplinkUi.Note("no recordings yet - every skirmish and mission you play is recorded here automatically", 12));
        int shown = 0;
        foreach (var (path, meta) in all)
        {
            if (shown++ >= MaxReplayRows) break;
            if (meta is null)
            {
                // An orphan .frep is real, so it is listed rather than hidden -
                // but without its sidecar there is no way to know which map or
                // which starting credits built the world it needs, and guessing
                // would produce a confident, silent desync.
                v.AddChild(UplinkUi.MenuButton(
                    $"{System.IO.Path.GetFileNameWithoutExtension(path)}   (no sidecar - cannot rebuild the setup)",
                    () => { }, enabled: false));
                continue;
            }
            string local = path;
            var m = meta;
            v.AddChild(UplinkUi.MenuButton(m.Line(), () => { overlay.QueueFree(); PlayReplay(local, m); }));
        }
        if (all.Count > MaxReplayRows)
            v.AddChild(UplinkUi.Note($"showing the {MaxReplayRows} most recent of {all.Count}", 11));
        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("BACK", () => overlay.QueueFree()));
    }

    private const int MaxReplayRows = 10;

    private void PlayReplay(string path, MatchMeta meta)
    {
        MatchConfig.ApplyFrom(meta);
        MatchConfig.ReplayPath = path;
        MatchConfig.ReplayTicks = meta.Tick;
        LaunchBattle();
    }

    // ---------------- TICKET-P5-SET-01: the LAN front door ----------------

    /// <summary>
    /// HOST, JOIN, and the honest truth between them. The lockstep relay this
    /// screen sits on is real and soak-tested, and since Q002's first half
    /// landed it can bind a LAN address and its client can dial one (both
    /// default to loopback, so nothing shipped changes). What still blocks
    /// these buttons is the battle scene itself: AdvanceTick blocks on a
    /// Monitor.Wait until the relay's merged batch arrives, and SkirmishLive's
    /// 15 Hz accumulator cannot block the frame on a socket. That frame-loop
    /// integration is real design work (Q002, second half). The screen says
    /// so, on the screen, rather than offering a button that fails into a
    /// socket timeout the player has to interpret.
    /// </summary>
    private void ShowLan()
    {
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, "LAN", 380, 260);

        v.AddChild(UplinkUi.Note(
            "the lockstep relay underneath this screen is real: it merges both players' orders per tick, compares their state hashes every 30 ticks, and the sim soaks 20 games against it with zero desyncs. the transport can now bind and dial a real address. what is NOT here is the battle scene driving its ticks from the lockstep client: advancing a tick blocks until the relay's merged batch arrives, and blocking the frame on a socket is how a dropped packet becomes a frozen window. that integration is real design work and this screen will not pretend otherwise.", 12));
        v.AddChild(new HSeparator());

        v.AddChild(UplinkUi.MenuButton("HOST GAME   (needs the lockstep frame loop in the battle scene)", () => { }, enabled: false));
        v.AddChild(UplinkUi.MenuButton("JOIN BY ADDRESS   (needs the lockstep frame loop in the battle scene)", () => { }, enabled: false));
        v.AddChild(new HSeparator());

        // What CAN be honestly offered today: the transport, driven from inside
        // this binary, against the real match world rather than the runner's
        // synthetic fixture.
        var report = UplinkUi.Note(
            "runs a relay and two lockstep clients inside this process over a real TCP socket, on the map and treasury selected above, and compares both worlds at the end.", 12);
        v.AddChild(UplinkUi.MenuButton("RUN THE TWO-CLIENT SMOKE TEST", () => StartSmoke(report)));
        v.AddChild(report);
        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("BACK", () => { _smoke = null; overlay.QueueFree(); }));
    }

    private LanSmokeResult? _smoke;
    private Label? _smokeReport;

    private void StartSmoke(Label report)
    {
        if (_smoke is { Done: false }) return;   // one at a time
        MatchConfig.MapPath = _maps.Count > 0 ? _maps[_mapPick.Selected] : null;
        MatchConfig.MissionPath = null;
        MatchConfig.StartCredits = long.Parse(_creditPick.GetItemText(_creditPick.Selected));
        _smokeReport = report;
        report.Text = $"running {LanSmoke.DefaultTicks} ticks on two clients...";
        _smoke = LanSmoke.Start(MatchConfig.CurrentSetup());
    }

    /// <summary>The smoke runs on background threads because the relay and both
    /// clients block on sockets. Its result is collected here, on the main
    /// thread, because a Godot Label may not be written from any other.</summary>
    public override void _Process(double delta)
    {
        if (_smoke is { Done: true } s && _smokeReport != null)
        {
            _smokeReport.Text = s.Summary;
            _smokeReport = null;
        }
    }

    /// <summary>Offscreen verification hook: the smoke result, once it lands.</summary>
    public LanSmokeResult? SmokeResult => _smoke;
    public void RunSmokeForTest(int ticks)
    {
        MatchConfig.MapPath = _maps.Count > 0 ? _maps[_mapPick.Selected] : null;
        MatchConfig.MissionPath = null;
        _smoke = LanSmoke.Start(MatchConfig.CurrentSetup(), ticks);
    }

    // ---------------- campaign ----------------

    private void ShowCampaign()
    {
        // TICKET-P5-SAVE-01: the manifest parser moved to Campaign, because
        // loading a campaign save needs the same allow-lists and two parsers
        // for one file is how the two drift apart.
        var missions = Campaign.Load();
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, "CAMPAIGN");
        foreach (var m in missions)
        {
            var local = m;
            v.AddChild(MenuButton($"{local.Index:00}  {local.Title.ToUpperInvariant()}", () =>
            {
                overlay.QueueFree();
                ShowBriefing(local.Path, local.Index, local.Title, local.Structs, local.Units);
            }));
        }
        v.AddChild(MenuButton("BACK", () => overlay.QueueFree()));
    }

    private void ShowBriefing(string missionPath, int index, string title,
        HashSet<int>? allowedStructs, HashSet<int>? allowedUnits)
    {
        string briefFile = System.IO.Path.Combine(
            GameFiles.RepoRoot, "data", "campaign", "briefings", $"mission-{index:00}.txt");
        string brief = System.IO.File.Exists(briefFile) ? System.IO.File.ReadAllText(briefFile) : "(no briefing on file)";
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, title.ToUpperInvariant());
        var text = new Label
        {
            Text = brief,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420, 0),
        };
        text.AddThemeFontSizeOverride("font_size", 13);
        text.AddThemeColorOverride("font_color", Bone);
        v.AddChild(text);
        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("COMMENCE MISSION", () =>
        {
            MatchConfig.MissionPath = missionPath;
            MatchConfig.MissionIndex = index;
            MatchConfig.AllowedStructures = allowedStructs;
            MatchConfig.AllowedUnits = allowedUnits;
            LaunchBattle();
        }));
        v.AddChild(MenuButton("BACK", () => overlay.QueueFree()));
    }

    // TICKET-P5-SAVE-01: the overlay chrome these two wrapped now lives in
    // UplinkUi, so the pause menu wears the same clothes instead of a
    // hand-copied second set that drifts.
    private Control FullOverlay() => UplinkUi.FullOverlay(this);

    private VBoxContainer OverlayBox(Control overlay, string heading, int halfW = 240, int halfH = 220)
        => UplinkUi.OverlayBox(overlay, heading, halfW, halfH);

    private static OptionButton Row(VBoxContainer parent, string label)
    {
        var h = new HBoxContainer();
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", new Color(0.45f, 0.44f, 0.42f));
        h.AddChild(l);
        var opt = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        h.AddChild(opt);
        parent.AddChild(h);
        return opt;
    }

    private static Button MenuButton(string text, System.Action onPress)
        => UplinkUi.MenuButton(text, onPress);

    private void StartSkirmish()
    {
        MatchConfig.MissionPath = null;
        MatchConfig.AllowedStructures = null;   // skirmish: full catalogue
        MatchConfig.AllowedUnits = null;
        MatchConfig.MapPath = _maps.Count > 0 ? _maps[_mapPick.Selected] : null;
        MatchConfig.AiPreset = _aiPick.Selected;
        MatchConfig.StartCredits = long.Parse(_creditPick.GetItemText(_creditPick.Selected));
        LaunchBattle();
    }
}

/// <summary>Match options carried from the menu into the battle scene.</summary>
public static class MatchConfig
{
    public static string? MapPath;
    public static int AiPreset;         // 0 standard, 1 rusher, 2 turtle
    public static long StartCredits = 8000;
    public static string? MissionPath;  // set = campaign mission mode
    public static int MissionIndex;
    // Per-mission tech gates (client-side allow-lists per the campaign
    // manifest; null = everything, empty = nothing buildable)
    public static System.Collections.Generic.HashSet<int>? AllowedStructures;
    public static System.Collections.Generic.HashSet<int>? AllowedUnits;

    // TICKET-P5-SAVE-01. Both are consumed once by SkirmishLive._Ready and
    // cleared here, because they describe THIS scene change and nothing after
    // it: a stale ReplayPath would silently turn the next skirmish into a
    // playback of the last one.
    public static string? LoadPath;     // set = resume this save file
    public static string? ReplayPath;   // set = play this recording back
    public static int ReplayTicks;      // the recording's length, from its sidecar

    /// <summary>The setup the battle scene is about to build, in the one shape
    /// saves and replays store. Reading MatchConfig ends here.</summary>
    public static MatchSetup CurrentSetup()
    {
        string map = MissionPath ?? MapPath
            ?? GameFiles.Abs(System.IO.Path.Combine("data", "maps", "skirmish-01.fmap"));
        var s = new MatchSetup
        {
            MapPath = GameFiles.Rel(map),
            MissionIndex = MissionPath != null ? MissionIndex : 0,
            AiPreset = AiPreset,
            StartCredits = StartCredits,
        };
        return s;
    }

    /// <summary>Point the next scene change at the match a sidecar describes.
    /// The campaign allow-lists are re-derived from the manifest rather than
    /// stored in the save: they are content, not state, and a save written
    /// before a manifest edit should honour the edit.</summary>
    public static void ApplyFrom(MatchMeta meta)
    {
        var s = meta.Setup;
        AiPreset = s.AiPreset;
        StartCredits = s.StartCredits;
        MissionIndex = s.MissionIndex;
        if (s.IsMission)
        {
            MissionPath = GameFiles.Abs(s.MapPath);
            MapPath = null;
            var entry = Campaign.ByIndex(s.MissionIndex);
            AllowedStructures = entry?.Structs;
            AllowedUnits = entry?.Units;
        }
        else
        {
            MissionPath = null;
            MapPath = GameFiles.Abs(s.MapPath);
            AllowedStructures = null;
            AllowedUnits = null;
        }
    }
}
