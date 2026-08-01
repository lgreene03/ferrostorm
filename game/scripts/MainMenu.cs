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

    /// <summary>ADR-006 commitment 2: a battle that could not be assembled
    /// (missing or malformed /data, a save or replay recorded against a
    /// different catalogue) stands down to this menu and leaves its message
    /// here. Static because the refusing scene is being torn down as it
    /// writes; consumed and cleared by the next _Ready, so it describes one
    /// refusal and nothing after it.</summary>
    public static string? BattleRefusedNotice;
    /// <summary>Verification surface: the last notice actually shown.</summary>
    public string NoticeShownForTest { get; private set; } = "";

    private OptionButton _factionPick = null!;
    private OptionButton _mapPick = null!;
    private OptionButton _aiPick = null!;
    private OptionButton _diffPick = null!;   // DR-14b: doc 28's ladder
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
            // TICKET-P6-FACTION-01 added the FACTION row: one more growth.
            OffsetLeft = -220, OffsetRight = 220, OffsetTop = -308, OffsetBottom = 308,
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

        // TICKET-P6-FACTION-01: the side is the first choice, because that is
        // where the classic genre puts it. Indices are World's own faction
        // constants (0 Directorate, 1 Sodality), so Selected IS the faction.
        _factionPick = Row(v, "FACTION");
        _factionPick.AddItem("DIRECTORATE"); _factionPick.AddItem("SODALITY");
        _mapPick = Row(v, "THEATRE");
        // Through GameFiles.RepoRoot, not the res://-parent idiom open-coded:
        // that idiom is true only when running from source, so a packaged build
        // listed no maps at all and the theatre picker came up empty.
        // P7-8a: "skirmish-*.fmap" rather than "*.fmap". data/maps now also
        // holds a four-start TEST FIXTURE for the runner's multiseatgate, and a
        // fixture carries no fairness proof by construction - an unfiltered
        // listing would have offered it here as a theatre to play, which is
        // exactly what its own header says must never happen.
        foreach (var f in System.IO.Directory.GetFiles(
            System.IO.Path.Combine(GameFiles.RepoRoot, "data", "maps"), "skirmish-*.fmap"))
        {
            _maps.Add(f);
            _mapPick.AddItem(System.IO.Path.GetFileNameWithoutExtension(f).ToUpperInvariant());
        }
        _aiPick = Row(v, "OPPOSITION");
        _aiPick.AddItem("STANDARD"); _aiPick.AddItem("RUSHER"); _aiPick.AddItem("TURTLE");
        // DR-14b / doc 28: strength, on its own axis from the taste above.
        // BRUTAL names its handicap in the item itself, because GDD line 76
        // requires the cheat be clearly labelled and a player choosing it is
        // entitled to know the opponent is being given money rather than
        // playing better. Normal is preselected: it is the opponent every
        // match before this picker existed was played against.
        _diffPick = Row(v, "DIFFICULTY");
        _diffPick.AddItem("EASY"); _diffPick.AddItem("NORMAL"); _diffPick.AddItem("HARD");
        _diffPick.AddItem("BRUTAL (+5000 CR HANDICAP)");
        _diffPick.Select(1);
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

        // ADR-006: a refused battle explains itself here, in the overlay
        // chrome everything else wears, and the menu behind it stays fully
        // usable. The message names files, lines and checksums as the failure
        // provides them; UNDERSTOOD dismisses and the player tries again.
        if (BattleRefusedNotice is { } refused)
        {
            BattleRefusedNotice = null;
            NoticeShownForTest = refused;
            var overlay = FullOverlay();
            var v2 = OverlayBox(overlay, "BATTLE REFUSED", 320, 190);
            var text = new Label
            {
                Text = refused,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(560, 0),
            };
            text.AddThemeFontSizeOverride("font_size", 13);
            text.AddThemeColorOverride("font_color", Bone);
            v2.AddChild(text);
            v2.AddChild(new HSeparator());
            v2.AddChild(MenuButton("UNDERSTOOD", () => overlay.QueueFree()));
        }
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
    /// C7b-iv: HOST and JOIN, live at last.
    ///
    /// These two buttons carried an honest disabled label for four waves, naming
    /// what was missing: the battle scene did not drive its ticks from the
    /// lockstep client. It does now (C7b-iii, proven by two real scenes playing
    /// each other to identical hashes), so the only thing that was left was this
    /// screen. The match options above the LAN button are the HOST'S: the joiner
    /// gets the host's map, treasury and sides through the setup blob (ADR-022)
    /// and its own selections are ignored, which is the only way two machines
    /// can build the same world.
    /// </summary>
    private void ShowLan()
    {
        var overlay = FullOverlay();
        var v = OverlayBox(overlay, "LAN", 380, 300);

        v.AddChild(UplinkUi.Note(
            "two machines, one relay, lockstep. the HOST chooses the theatre, treasury and sides above and opens a lobby; the other player joins by address and receives that setup over the wire, so both build the identical world. there is no computer opponent in a LAN match, and saving is off: a save is one machine's snapshot and the other player's battle carries on without it.", 12));
        v.AddChild(new HSeparator());

        _lanStatus = UplinkUi.Note("", 12);

        v.AddChild(UplinkUi.MenuButton("HOST GAME", StartHost));

        // The address field and its own button, because a join needs a value
        // typed before it can start and every other row on this screen acts on
        // press alone.
        var row = new HBoxContainer();
        var addr = new LineEdit
        {
            PlaceholderText = "host address, e.g. 192.168.1.20",
            Text = _lastJoinAddress,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddChild(addr);
        v.AddChild(row);
        v.AddChild(UplinkUi.MenuButton("JOIN BY ADDRESS", () => StartJoin(addr.Text)));
        // Enter in the address field joins, because that is what every other
        // address box in the world does.
        addr.TextSubmitted += t => StartJoin(t);

        v.AddChild(_lanStatus);
        v.AddChild(new HSeparator());

        // Kept: the transport driven inside this one binary, which is still the
        // fastest way to tell "the network is wrong" from "the game is wrong"
        // when a real join fails.
        var report = UplinkUi.Note(
            "runs a relay and two lockstep clients inside this process over a real TCP socket, on the map and treasury selected above, and compares both worlds at the end.", 12);
        v.AddChild(UplinkUi.MenuButton("RUN THE TWO-CLIENT SMOKE TEST", () => StartSmoke(report)));
        v.AddChild(report);
        v.AddChild(new HSeparator());
        v.AddChild(MenuButton("BACK", () =>
        {
            _smoke = null;
            // Stand the lobby down rather than orphaning it: a host that backs
            // out leaves a relay bound to the fixed port, and the next HOST
            // press would refuse with "address already in use".
            _lobby?.Cancel();
            _lobby = null;
            _lanStatus = null;
            overlay.QueueFree();
        }));
    }

    private LanLobby? _lobby;
    private Label? _lanStatus;
    private static string _lastJoinAddress = "";

    /// <summary>The match the host is about to open, taken from the same rows a
    /// skirmish reads, so hosting "this map with these sides" means what the
    /// screen above says it means.</summary>
    private MatchSetup LanSetupFromMenu()
    {
        MatchConfig.MissionPath = null;
        MatchConfig.AllowedStructures = null;
        MatchConfig.AllowedUnits = null;
        MatchConfig.MapPath = _maps.Count > 0 ? _maps[_mapPick.Selected] : null;
        MatchConfig.AiPreset = _aiPick.Selected;
        MatchConfig.AiDifficulty = _diffPick.Selected;   // DR-14b
        MatchConfig.StartCredits = long.Parse(_creditPick.GetItemText(_creditPick.Selected));
        MatchConfig.Faction = _factionPick.Selected;
        MatchConfig.OppositionFaction = 1 - _factionPick.Selected;
        return MatchConfig.CurrentSetup();
    }

    private void StartHost()
    {
        if (_lobby is { State: LanLobby.Phase.Connecting }) return;   // one at a time
        _lobby = LanLobby.Host(LanSetupFromMenu());
        var addresses = LanLobby.LocalAddresses();
        string where = addresses.Count > 0
            ? string.Join(" or ", addresses) + $":{LanLobby.DefaultPort}"
            : $"this machine's LAN address, port {LanLobby.DefaultPort}";
        SetLanStatus($"lobby open. the other player joins at {where}\nwaiting for them to connect...");
    }

    private void StartJoin(string address)
    {
        if (_lobby is { State: LanLobby.Phase.Connecting }) return;
        _lastJoinAddress = address;
        _lobby = LanLobby.Join(address);
        SetLanStatus(_lobby.Status);
    }

    private void SetLanStatus(string text)
    {
        if (_lanStatus != null) _lanStatus.Text = text;
    }

    /// <summary>The lobby connects on a background thread (its header says why:
    /// a host's own client blocks until the joiner arrives, which may be never).
    /// This is the main-thread poll that turns Ready into a scene change.</summary>
    private void PollLobby()
    {
        if (_lobby is not { } lobby) return;
        if (lobby.State == LanLobby.Phase.Failed)
        {
            SetLanStatus(lobby.Status);
            _lobby = null;
            return;
        }
        if (lobby.State != LanLobby.Phase.Ready || lobby.Client is null || lobby.Setup is null) return;
        _lobby = null;
        LaunchNetBattle(lobby);
    }

    /// <summary>
    /// Hand the connected session to the battle scene.
    ///
    /// The setup applied here is the LOBBY'S, not this menu's: a joiner's own
    /// rows are meaningless, and applying them would render one map while the
    /// adopted world ran another. The seat comes from the relay, which is the
    /// only thing that knows it.
    /// </summary>
    private void LaunchNetBattle(LanLobby lobby)
    {
        var s = lobby.Setup!;
        MatchConfig.MissionPath = null;
        MatchConfig.AllowedStructures = null;
        MatchConfig.AllowedUnits = null;
        MatchConfig.MapPath = GameFiles.Abs(s.MapPath);
        MatchConfig.AiPreset = s.AiPreset;
        MatchConfig.StartCredits = s.StartCredits;
        MatchConfig.Faction = s.Faction;
        MatchConfig.OppositionFaction = s.OppFaction;
        MatchConfig.LoadPath = null;
        MatchConfig.ReplayPath = null;

        SkirmishLive.LocalSeat = lobby.Client!.PlayerId;
        SkirmishLive.PendingNet = lobby.Client;
        // Reset FIRST, then raise: Reset clears the desync latch, so setting
        // Active before it would arm a session and immediately disarm it.
        NetSession.Reset();
        NetSession.Active = true;
        GetTree().ChangeSceneToFile("res://scenes/Skirmish.tscn");
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
        PollLobby();
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
            // TICKET-P6-FACTION-01: a mission's map declares its own factions
            // and BuildStartingWorld's mission branch never reads these, but a
            // value left standing by an earlier skirmish would still be
            // written into the mission's sidecar as if it meant something.
            MatchConfig.Faction = 0;
            MatchConfig.OppositionFaction = 0;
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
        MatchConfig.AiDifficulty = _diffPick.Selected;   // DR-14b
        MatchConfig.StartCredits = long.Parse(_creditPick.GetItemText(_creditPick.Selected));
        // TICKET-P6-FACTION-01: the chosen side, and the opponent takes the
        // other one (doc 24). Selected is the faction constant by construction.
        MatchConfig.Faction = _factionPick.Selected;
        MatchConfig.OppositionFaction = 1 - _factionPick.Selected;
        LaunchBattle();
    }

    /// <summary>Offscreen verification hooks (the RunSmokeForTest precedent):
    /// drive the REAL row widget and the REAL start path, not a copy.</summary>
    public void SelectFactionForTest(int faction) => _factionPick.Select(faction);
    public void StartSkirmishForTest() => StartSkirmish();
}

/// <summary>Match options carried from the menu into the battle scene.</summary>
public static class MatchConfig
{
    public static string? MapPath;
    public static int AiPreset;         // 0 standard, 1 rusher, 2 turtle
    /// <summary>DR-14b: doc 28's rung - 0 easy, 1 normal, 2 hard, 3 brutal.
    /// Defaults to Normal so a scene-direct launch (how the client is verified
    /// offscreen) and any path that never touches the picker get the opponent
    /// the game has always shipped.</summary>
    public static int AiDifficulty = 1;
    public static long StartCredits = 8000;
    // TICKET-P6-FACTION-01: the sides. Defaults are the legacy pairing (both
    // Directorate), which is what a scene-direct launch and every pre-P6
    // sidecar mean; StartSkirmish overwrites both for a fresh menu match.
    public static int Faction;
    public static int OppositionFaction;
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
            AiDifficulty = AiDifficulty,
            StartCredits = StartCredits,
            Faction = Faction,
            OppFaction = OppositionFaction,
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
        AiDifficulty = s.AiDifficulty;
        StartCredits = s.StartCredits;
        MissionIndex = s.MissionIndex;
        // TICKET-P6-FACTION-01: restore the recorded sides. A pre-P6 sidecar
        // decodes to 0/0, the pairing its match actually played.
        Faction = s.Faction;
        OppositionFaction = s.OppFaction;
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
