using Godot;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Ferrostorm.Sim;

namespace Ferrostorm.Client;

/// <summary>
/// LOOK-01 (doc 25 Wave V0): the look-development harness. The project shipped
/// four visual waves without anyone looking at a frame of the running client,
/// because there was no way to take one. This is that way, and unlike the
/// throwaway autoloads earlier waves used it is COMMITTED, so the project can
/// look at itself again tomorrow.
///
/// It renders the SKIRMISH path and nothing else. game/scenes/Battle3D.tscn
/// carries its own Sun and Fill while its Theater node runs ReplayTheater,
/// which calls BuildLightRig and adds three more directionals, so a replay
/// renders under five directional lights totalling 4.55 energy. That is not
/// the game and a capture taken through it would be a lie (doc 25 s7 note 5).
/// This harness instantiates res://scenes/Skirmish.tscn, the same scene the
/// menu launches, and captures its viewport.
///
/// SAFETY: this is not an autoload, it is not in any shipped scene tree, and
/// it refuses to run unless --lookdev is on the command line. Three locks,
/// because the failure mode of a capture harness leaking into a player build
/// is a game that pauses itself and writes PNGs into the user's home.
///
/// Invocation (see tools/lookdev/capture.sh, which is the supported entry
/// point and records the exact flags):
///   Godot --path game res://scenes/LookDev.tscn --audio-driver Dummy \
///         --fixed-fps 60 --lookdev --lookdev-out=DIR --lookdev-tag=baseline
/// </summary>
public partial class LookDev : Node
{
    // ---------------- the three frozen cameras ----------------
    //
    // THESE CONSTANTS MUST NEVER CHANGE. The moment a camera moves between two
    // captures the before-and-after comparison is worthless, which is the
    // entire reason doc 25 s3 clause 2 writes them down rather than leaving
    // them to whoever runs the tool. All three sit at the shipped fixed pitch
    // of -50 degrees, which RtsCamera._Ready sets and nothing overrides.
    //
    // X and Z are chosen on skirmish-03 (96x64, starts at 12,10 and 83,53) to
    // put the water, the hills, the ruins and a stretch of explored-but-unseen
    // shroud in shot at every height, with the Directorate base up-left and the
    // Sodality front line down-right.
    //
    // The Z offset is the look-at convention RtsCamera and Minimap.Refresh
    // share: the camera sits 0.55 * height south of the ground point it frames.
    /// <summary>A frozen reference camera: the ground point it frames and the
    /// height it frames it from. Height is the only thing doc 25 fixes; X and
    /// Z were chosen once, against the committed reference state, and are now
    /// as immovable as the heights.</summary>
    private readonly record struct RefCam(string Name, float X, float GroundZ, float Y);

    private static readonly RefCam[] Cameras =
    {
        // CAM-A at Y=22: the height SkirmishLive._Ready actually starts the
        // camera at, and therefore the height the game is played at. Framed on
        // the western bridge with the river across the bottom third, the ruins
        // and the fence lines in the middle distance, the Directorate base on
        // the far left and a stretch of explored-but-unseen shroud through the
        // centre. This is THE reference frame; if only one image is looked at,
        // it is this one.
        new("camA", 40f, 30f, 22f),

        // CAM-B at Y=42: RtsCamera.MaxHeight, the worst case for pixel budget
        // and the widest thing the renderer is ever asked to do. Framed on the
        // map centre so both players' bases are in shot at once.
        new("camB", 48f, 30f, 42f),

        // CAM-C at Y=9: RtsCamera.MinHeight plus one, close on the bridge where
        // a Directorate vehicle and a Sodality vehicle are a few metres apart
        // over water, with open ground, a ferrite cluster and ruins in the same
        // frame. The only view in which material work can be judged at all, and
        // per doc 25 s3 clause 6 the view every wave is tuned at FIRST, before
        // anything goes wide.
        new("camC", 24f, 31f, 9f),
    };

    // ---------------- timing ----------------

    /// <summary>Frames run before anything is captured. Long enough for the
    /// W2-05 structure rise tweens (1.1 s) to finish: SkirmishLive fires one
    /// per structure on the first sync of a resumed save, because its
    /// _world.Tick > 10 guard is true for every save, and a capture taken
    /// mid-tween shows a base half out of the ground.</summary>
    private const int WarmupFrames = 150;

    /// <summary>
    /// MANDATORY, and the reason is not politeness to the GPU. BattlefieldView
    /// sets VolumetricFogTemporalReprojectionEnabled = true, so the fog volume
    /// is an exponential accumulation over many frames: a frame-1 capture is a
    /// visibly DIFFERENT IMAGE from a converged one, and any future temporal AA
    /// makes this worse rather than better.
    ///
    /// doc 25 LOOK-01 clause 2 asks for thirty. Thirty is not enough, and this
    /// was measured rather than assumed. At thirty the harness came back
    /// byte-different between runs about half the time, always by fewer than
    /// fifteen pixels and never by more than 4/255, and always at a high-
    /// contrast edge in the near field. Disabling SSAO, SSIL, SSR, the depth of
    /// field, the glow pass and every particle system in turn changed which
    /// pixels moved but never stopped them moving; capturing the same frozen
    /// camera twice inside ONE run reproduced the difference, which is what
    /// ruled out load order, hashed collections and everything else that varies
    /// per process and pointed at the one thing left that varies per frame.
    /// At 240 frames the accumulation has converged past the point where it
    /// changes a byte. The cost is about four seconds of wall clock per camera,
    /// which is the correct trade for a comparison that can be trusted.
    /// </summary>
    private const int SettleFrames = 240;

    /// <summary>Frames timed at CAM-B for the frame-time number doc 25 s5
    /// requires. Measured as the wall-clock period between rendered frames
    /// with V-Sync forced off, which is the real cost, not a CPU-side monitor
    /// that ignores the GPU.</summary>
    private const int TimedFrames = 240;

    // ---------------- reference scene ----------------

    /// <summary>skirmish-03: the ONLY committed map that exercises the whole
    /// terrain vocabulary (water, hills, ruins, fences, bridges). doc 25 s3
    /// clause 1 picks it for that reason and it is not negotiable.</summary>
    private const string RefMap = "data/maps/skirmish-03.fmap";
    private const string RefSave = "tools/lookdev/reference-state.fsav";

    /// <summary>The state the reference save is generated at. Both sides are
    /// driven by the Standard AI so both bases develop; a live skirmish only
    /// drives player 1, so a save made by playing would have a stub base on
    /// one side. Fixed seed, fixed tick count, fixed credits: the sim is
    /// deterministic, so this file is reproducible from these four numbers
    /// alone, which is why they are here and not in a shell script.</summary>
    private const ulong RefSeed = 2026;
    private const int RefTicks = 3600;
    private const long RefCredits = 12000;

    private string _outDir = "";
    private string _tag = "capture";
    private bool _fogZero, _noShroud, _keepHud;

    /// <summary>--lookdev-off=ssao,ssil,ssr,glow,fog,dof: switch one effect off
    /// for one capture. An A/B against a single screen-space pass is the only
    /// way to attribute a defect to it, and this harness exists precisely so
    /// that questions like that get answered with an image instead of a
    /// paragraph.</summary>
    private string[] _off = System.Array.Empty<string>();

    /// <summary>--lookdev-cam=camA|camB|camC. Empty means all three in one
    /// process, which is faster and NOT reproducible; see the note at the
    /// camera loop. tools/lookdev/capture.sh always passes one.</summary>
    private string _only = "";

    public override void _Ready()
    {
        // Lock 1 of 3. Locks 2 and 3 are that nothing adds this to the shipped
        // scene tree and nothing registers it as an autoload.
        var args = new List<string>(OS.GetCmdlineArgs());
        if (!args.Contains("--lookdev"))
        {
            GD.PrintErr("LookDev: refusing to run without --lookdev. "
                + "This is a development capture harness, not a game scene.");
            GetTree().Quit(2);
            return;
        }

        foreach (string a in args)
        {
            if (a.StartsWith("--lookdev-out=")) _outDir = a["--lookdev-out=".Length..];
            else if (a.StartsWith("--lookdev-tag=")) _tag = a["--lookdev-tag=".Length..];
            else if (a.StartsWith("--lookdev-cam=")) _only = a["--lookdev-cam=".Length..];
            else if (a.StartsWith("--lookdev-off=")) _off = a["--lookdev-off=".Length..].Split(',');
            else if (a == "--lookdev-fog0") _fogZero = true;
            else if (a == "--lookdev-noshroud") _noShroud = true;
            else if (a == "--lookdev-hud") _keepHud = true;
        }

        if (args.Contains("--lookdev-make-save")) { MakeReferenceSave(); return; }

        if (_outDir.Length == 0)
        {
            GD.PrintErr("LookDev: --lookdev-out=DIR is required.");
            GetTree().Quit(2);
            return;
        }
        Directory.CreateDirectory(_outDir);
        _ = CaptureAll();
    }

    // ---------------- the reference state ----------------

    /// <summary>
    /// Write tools/lookdev/reference-state.fsav. Run once; the file is
    /// COMMITTED so that every capture on every machine loads a byte-identical
    /// world, which is the precondition for a before-and-after being about the
    /// rendering rather than about the match.
    /// </summary>
    private void MakeReferenceSave()
    {
        var setup = new MatchSetup
        {
            MapPath = RefMap,
            MissionIndex = 0,
            AiPreset = 0,
            StartCredits = RefCredits,
            Seed = RefSeed,
            Faction = 0,        // Directorate, player 0
            OppFaction = 1,     // Sodality, player 1
        };
        var map = MapData.Load(GameFiles.Abs(setup.MapPath));
        var world = SkirmishLive.BuildStartingWorld(setup, map, out _);

        // Both sides driven, so both bases are real. The client only ever
        // drives player 1; that asymmetry is correct in a game and wrong in a
        // reference frame.
        var ai0 = SkirmishAI.Standard(0);
        var ai1 = SkirmishAI.Standard(1);
        var cmds = new List<Command>();
        for (int t = 0; t < RefTicks; t++)
        {
            cmds.Clear();
            ai0.Act(world, cmds);
            ai1.Act(world, cmds);
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds);
            world.Step(span);
        }

        string savePath = GameFiles.Abs(RefSave);
        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        using (var ms = new MemoryStream())
        {
            world.Save(ms);
            File.WriteAllBytes(savePath, ms.ToArray());
        }
        // The sidecar carries the map and the sides, exactly as a player's save
        // does; MatchConfig.ApplyFrom reads it back in CaptureAll.
        MatchMeta.For(setup, world.Tick, world.Credits(0))
            .Write(Path.ChangeExtension(savePath, ".json"));

        GD.Print($"LookDev: reference save written, tick {world.Tick}, "
            + $"hash 0x{world.ComputeStateHash():X16}, {new FileInfo(savePath).Length} bytes");
        ReportState(world);
        GetTree().Quit(0);
    }

    /// <summary>Where everything is, so the frozen camera constants above can
    /// be chosen against the actual state rather than guessed.</summary>
    private static void ReportState(World world)
    {
        var (_, entities, _) = world.TakeSnapshot();
        var interp = new Ferrostorm.Presentation.SnapshotInterpolator();
        interp.AddSnapshot(world.Tick, entities);
        var view = new List<Ferrostorm.Presentation.SnapshotInterpolator.ViewEntity>();
        interp.TrySample(world.Tick, view);
        var byKind = new SortedDictionary<string, int>();
        foreach (var v in view)
        {
            if (!v.Alive) continue;
            string k = $"p{v.PlayerId} {v.Kind}";
            byKind[k] = byKind.TryGetValue(k, out int n) ? n + 1 : 1;
            GD.Print($"  ENT p{v.PlayerId} {v.Kind} at {v.X:F1},{v.Y:F1} hp {v.Hp}");
        }
        foreach (var kv in byKind) GD.Print($"  COUNT {kv.Key} = {kv.Value}");
        int explored = 0, visible = 0;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 96; x++)
            {
                if (world.IsVisible(0, x, y)) visible++;
                else if (world.IsExplored(0, x, y)) explored++;
            }
        GD.Print($"  SHROUD p0: visible {visible}, explored-but-unseen {explored}, "
            + $"unexplored {96 * 64 - visible - explored}");
    }

    // ---------------- capture ----------------

    private async Task CaptureAll()
    {
        // The sim must not advance from the frame clock: a capture that races
        // the accumulator is a capture of a different battle every run.
        // SkirmishLive.AutoStep is the shipped hook for exactly this and it has
        // to be set BEFORE the scene loads.
        SkirmishLive.AutoStep = false;

        string savePath = GameFiles.Abs(RefSave);
        var meta = MatchMeta.Read(Path.ChangeExtension(savePath, ".json"));
        if (meta == null || !File.Exists(savePath))
        {
            GD.PrintErr($"LookDev: no reference save at {savePath}. "
                + "Run tools/lookdev/make-reference-save.sh first.");
            GetTree().Quit(3);
            return;
        }
        MatchConfig.ApplyFrom(meta);
        MatchConfig.LoadPath = savePath;

        var scene = GD.Load<PackedScene>("res://scenes/Skirmish.tscn").Instantiate<SkirmishLive>();
        AddChild(scene);

        // Pin the viewport to what project.godot ships rather than to whatever
        // the machine's settings.cfg says, so a capture is a property of the
        // tree and not of the operator. Settings.EnsureLoaded (SkirmishLive
        // _Ready) has already applied the player's MSAA and render scale by
        // now; this puts them back.
        var vp = GetViewport();
        vp.Msaa3D = Viewport.Msaa.Msaa4X;      // project.godot msaa_3d=2
        vp.Scaling3DScale = 1.0f;
        // V-Sync off or the frame-time number is a measurement of the monitor.
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;

        // Two ticks, driven explicitly. One is needed at minimum: FogOfWar's
        // texture is only rebuilt inside RunOneTick, so a freshly loaded save
        // renders a fully unexplored shroud until the sim steps once, and the
        // reference frame is supposed to SHOW the explored-but-unseen veil.
        scene.StepTicks(2);

        var cam = FindChild<RtsCamera>(scene);
        var fog = FindChild<FogOfWar>(scene);
        var env = FindChild<WorldEnvironment>(scene);
        if (cam == null || env == null)
        {
            GD.PrintErr("LookDev: the skirmish scene did not produce a camera and an environment.");
            GetTree().Quit(4);
            return;
        }

        if (!_keepHud)
            foreach (var c in scene.GetChildren())
                if (c is CanvasLayer cl) cl.Visible = false;

        // The two day-one experiments (doc 25 s3, "the highest-information
        // action available anywhere in this document"). Both are one line and
        // both are reverted by simply not passing the flag.
        if (_fogZero) env.Environment.VolumetricFogDensity = 0f;
        foreach (string o in _off)
            switch (o.Trim().ToLowerInvariant())
            {
                case "ssao": env.Environment.SsaoEnabled = false; break;
                case "ssil": env.Environment.SsilEnabled = false; break;
                case "ssr": env.Environment.SsrEnabled = false; break;
                case "glow": env.Environment.GlowEnabled = false; break;
                case "fog": env.Environment.VolumetricFogEnabled = false; break;
                case "dof":
                    if (cam.Attributes is CameraAttributesPractical dofOff)
                        dofOff.DofBlurFarEnabled = false;
                    break;
                case "particles":
                    foreach (var p in FindAll<GpuParticles3D>(scene)) p.Visible = false;
                    break;
                default: GD.PrintErr($"LookDev: unknown --lookdev-off item '{o}'"); break;
            }
        if (_noShroud && fog != null)
        {
            var plane = fog.GetNodeOrNull<MeshInstance3D>("Shroud");
            if (plane != null) plane.Visible = false;
        }

        // Let the first actor syncs run. SkirmishLive builds a damage-smoke
        // emitter per wounded structure inside SyncActors, so the tree does not
        // hold its full complement of particle systems until a few frames in
        // and a seeding pass done before this would miss every one of them.
        // Measured: without this the two runs differ by about a thousand pixels
        // around the burning buildings, and nowhere else.
        for (int i = 0; i < 30; i++) await NextProcessFrame();

        // Particle systems pick a RANDOM seed at construction, which puts every
        // dust mote and every smoke puff somewhere different on every run. This
        // is the one genuine source of nondeterminism in the frame; everything
        // else in the client is either seeded (BattlefieldView's scatter uses
        // System.Random(2026) and System.Random(2027)) or frozen by the pause
        // below.
        int seedn = 0;
        foreach (var p in FindAll<GpuParticles3D>(scene))
        {
            p.UseFixedSeed = true;
            p.Seed = (uint)(4242 + seedn++);
            p.Restart();
        }

        // The warm-up is counted in PROCESS frames, not rendered frames, and
        // the distinction is the whole determinism story. Turret sway, track
        // scroll, wheel spin, the infantry stride and the water UV all
        // accumulate the delta SkirmishLive._Process is handed, so the pause
        // below has to land after an exact number of those. Counting rendered
        // frames instead left the pause one process frame either side of where
        // it meant to be, which moved a tank's tracks by a fraction of a texel
        // and came back as a dozen pixels differing by 1/255 about one run in
        // six. Rendered frames are still what the settle loops count, because
        // fog convergence is a render-side property.
        for (int i = 0; i < WarmupFrames; i++) await NextProcessFrame();

        // Everything that moves is now frozen: tweens, particles, the water UV
        // scroll and the camera's own smoothing all run off the scene tree's
        // process time. Rendering carries on, which is what the temporal fog
        // needs. This is the single change that makes two runs byte-identical.
        GetTree().Paused = true;
        ProcessMode = ProcessModeEnum.Always;
        // RtsCamera._Process edge-pans off the mouse position, and an offscreen
        // run has the cursor parked at 0,0, which is inside the left/top edge
        // band. Pausing stops it; disabling it says so out loud.
        cam.ProcessMode = ProcessModeEnum.Disabled;

        // Quiesce. Pausing the tree stops the C# side immediately, but a
        // GpuParticles3D is stepped by the rendering server, which is a frame
        // or two behind the scene tree, so the first capture after the pause
        // could catch the dust field one step further on than the runs either
        // side of it. Measured before this wait existed: CAM-A came back
        // byte-different about one run in six, always by fewer than ten pixels
        // and never by more than 3/255, always along an edge with a dust mote
        // over it; CAM-B and CAM-C, captured later, never varied at all. That
        // asymmetry is what identified the cause.
        for (int i = 0; i < 30; i++) await NextFrame();

        var lines = new List<string>
        {
            $"tag={_tag}",
            $"fog_zero={_fogZero} no_shroud={_noShroud} hud_visible={_keepHud}",
            $"viewport={vp.GetVisibleRect().Size.X}x{vp.GetVisibleRect().Size.Y} msaa=4x scale=1.0",
            $"warmup_frames={WarmupFrames} settle_frames={SettleFrames}",
        };

        foreach (var (name, x, groundZ, y) in Cameras)
        {
            // ONE CAMERA PER PROCESS. The harness is invoked once per reference
            // camera and captures only the one it was asked for, because the
            // volumetric fog's temporal history is a property of every frame
            // the process has ever drawn: capturing CAM-A, then CAM-B, then
            // CAM-C in one process gives CAM-B a history that contains CAM-A
            // and gives CAM-C a history that contains both. Measured directly,
            // by returning to CAM-A at the end of a three-camera run and
            // shooting it again: sixteen and a half thousand pixels differed
            // from the first CAM-A image, by up to 64/255, with the scene
            // frozen and the camera on the same constants. That is not a noise
            // floor, it is a different picture, and it also made the two-runs-
            // byte-identical acceptance flake about one run in four. Three
            // processes cost three cold starts and buy an acceptance criterion
            // that actually holds.
            if (_only.Length > 0 && _only != name) continue;

            // The 0.55 look-at offset is RtsCamera's own convention, shared
            // with Minimap.Refresh: the camera sits 0.55 * height south of the
            // ground point it frames. Reproducing it here rather than inventing
            // a second one keeps the reference frames honest about what a
            // player at that zoom is actually looking at.
            float z = groundZ + y * 0.55f;
            cam.Position = new Vector3(x, y, z);
            cam.RotationDegrees = new Vector3(-50, 0, 0);
            cam.HOffset = 0f;
            cam.VOffset = 0f;
            // RtsCamera._Process normally drives this every frame; it is
            // disabled above, so the harness has to reproduce it exactly or
            // the depth of field would sit at its _Ready value of 55.
            if (cam.Attributes is CameraAttributesPractical at)
                at.DofBlurFarDistance = cam.Position.Y * 2.2f + 12f;

            for (int i = 0; i < SettleFrames; i++) await NextFrame();

            var img = GetViewport().GetTexture().GetImage();
            string path = Path.Combine(_outDir, $"{_tag}-{name}.png");
            img.SavePng(path);
            GD.Print($"LookDev: {path} (frame {Engine.GetFramesDrawn()})");

            if (name == "camB")
            {
                // Frame time, at the worst-case camera, with a full base in
                // view. doc 25 s5: the project has never had this number.
                ulong t0 = Time.GetTicksUsec();
                for (int i = 0; i < TimedFrames; i++) await NextFrame();
                ulong t1 = Time.GetTicksUsec();
                double ms = (t1 - t0) / 1000.0 / TimedFrames;
                long draws = (long)Performance.GetMonitor(
                    Performance.Monitor.RenderTotalDrawCallsInFrame);
                lines.Add($"frame_ms_camB={ms:F3}");
                lines.Add($"draw_calls_camB={draws}");
                GD.Print($"LookDev: CAM-B frame time {ms:F3} ms over {TimedFrames} frames, "
                    + $"{draws} draw calls");
            }
        }

        string metaName = _only.Length > 0 ? $"{_tag}-{_only}-meta.txt" : $"{_tag}-meta.txt";
        File.WriteAllLines(Path.Combine(_outDir, metaName), lines);
        GetTree().Quit(0);
    }

    /// <summary>Wait for one fully rendered frame. FramePostDraw rather than a
    /// process tick, because GetViewport().GetTexture().GetImage() reads what
    /// the GPU last produced and a process-tick counter can be one frame ahead
    /// of it.</summary>
    private SignalAwaiter NextFrame()
        => ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    /// <summary>Wait for one scene-tree process frame, which is the unit every
    /// animated thing in the client accumulates its delta in. Not the same
    /// thing as a rendered frame and not interchangeable with it.</summary>
    private SignalAwaiter NextProcessFrame()
        => ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private static T? FindChild<T>(Node root) where T : Node
    {
        foreach (var c in root.GetChildren())
        {
            if (c is T t) return t;
            if (FindChild<T>(c) is { } deep) return deep;
        }
        return null;
    }

    private static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        void Walk(Node n)
        {
            foreach (var c in n.GetChildren())
            {
                if (c is T t) found.Add(t);
                Walk(c);
            }
        }
        Walk(root);
        return found;
    }
}
