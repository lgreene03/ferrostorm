using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace Ferrostorm.Client;

/// <summary>
/// First light: plays a sim export (the runner's `export` mode JSON) as a 3D
/// battle - terrain from map data, model instances tracking entity frames
/// with positional smoothing. This is the presentation contract made visible
/// before the live-sim binding (SnapshotInterpolator) is wired underneath;
/// both consume the same shapes.
/// Put a replay at res://replay.json (or set ReplayPath) and press play.
///
/// Presentation-only embellishments (ADR-001: the sim stays untouched):
/// units yaw smoothly into their direction of travel, mobile units carry a
/// team-colour ground ring, the terrain is noise-textured cinder with
/// rubble scatter, and a WorldEnvironment supplies ACES tonemapping, glow
/// (which blooms the emissive ferrite) and distance fog. The client may use
/// System.Random freely - it renders, it never simulates.
/// </summary>
public partial class ReplayTheater : Node3D
{
    [Export] public string ReplayPath = "res://replay.json";
    [Export] public float TicksPerSecond = 15f;
    [Export] public float TurnSpeed = 6f;          // rad/s toward travel heading
    [Export] public float InfantryTurnSpeed = 10f;

    private JsonElement _replay;
    private JsonElement[] _frames = System.Array.Empty<JsonElement>();
    private readonly Dictionary<int, Node3D> _actors = new();
    private readonly Dictionary<int, Vector3> _targets = new();
    private readonly Dictionary<int, int> _kinds = new();
    private ModelLibrary _models = null!;
    private float _clock;
    private int _frame;

    private static readonly Color DirectorateMark = new(0.91f, 0.42f, 0.13f);
    private static readonly Color SodalityMark = new(0.24f, 0.70f, 0.63f);

    public override void _Ready()
    {
        _models = new ModelLibrary();
        AddChild(_models);
        using var file = FileAccess.Open(ReplayPath, FileAccess.ModeFlags.Read);
        if (file is null) { GD.PushError($"No replay at {ReplayPath} - run the sim's export mode."); return; }
        _replay = JsonDocument.Parse(file.GetAsText()).RootElement;
        var frames = new List<JsonElement>();
        foreach (var f in _replay.GetProperty("frames").EnumerateArray()) frames.Add(f);
        _frames = frames.ToArray();
        BuildEnvironment();
        BuildTerrain();
    }

    private void BuildEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.028f, 0.036f, 0.050f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.36f, 0.40f, 0.47f),
            AmbientLightEnergy = 0.7f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.15f,
            GlowEnabled = true,
            GlowIntensity = 0.4f,
            GlowBloom = 0.03f,
            FogEnabled = true,
            FogLightColor = new Color(0.045f, 0.055f, 0.075f),
            FogDensity = 0.0035f,
            FogSkyAffect = 0.45f,
        };
        AddChild(new WorldEnvironment { Environment = env, Name = "Env" });
    }

    private static NoiseTexture2D GrainTex(int seed, float freq, bool normalMap = false,
        Color? dark = null, Color? light = null)
    {
        var noise = new FastNoiseLite
        {
            Seed = seed,
            Frequency = freq,
            FractalOctaves = 4,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
        };
        var tex = new NoiseTexture2D
        {
            Noise = noise, Width = 512, Height = 512,
            Seamless = true, AsNormalMap = normalMap,
            BumpStrength = normalMap ? 6f : 8f,
        };
        if (!normalMap)
        {
            // Raw noise spans black..white - far too harsh for terrain. A
            // colour ramp pins the palette to a narrow band of the world's
            // materials instead.
            var grad = new Gradient();
            grad.SetColor(0, dark ?? new Color(0.10f, 0.105f, 0.12f));
            grad.SetColor(1, light ?? new Color(0.17f, 0.18f, 0.20f));
            tex.ColorRamp = grad;
        }
        return tex;
    }

    private void BuildTerrain()
    {
        var map = _replay.GetProperty("map");
        int w = map.GetProperty("w").GetInt32(), h = map.GetProperty("h").GetInt32();

        // Cinder ground: noise-broken albedo + noise normal so light rakes
        // across something, not a snooker table.
        var groundMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(7, 0.012f,
                dark: new Color(0.085f, 0.09f, 0.10f),
                light: new Color(0.145f, 0.15f, 0.165f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(11, 0.05f, normalMap: true),
            NormalScale = 0.55f,
            Roughness = 0.96f,
            Uv1Scale = new Vector3(10, 10, 10),
        };
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w + 24, h + 24) },
            Position = new Vector3(w / 2f, 0, h / 2f),
            MaterialOverride = groundMat,
            Name = "Ground",
        };
        AddChild(ground);

        // The Spine: ridge cells merged visually by overlap, rock material,
        // deterministic per-cell height/rotation jitter so the wall reads as
        // geology rather than boxes.
        var ridgeMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(23, 0.03f,
                dark: new Color(0.15f, 0.165f, 0.19f),
                light: new Color(0.26f, 0.28f, 0.31f)),
            NormalEnabled = true,
            NormalTexture = GrainTex(29, 0.09f, normalMap: true),
            NormalScale = 1.0f,
            Roughness = 0.92f,
            Uv1Scale = new Vector3(3, 3, 3),
        };
        var rng = new System.Random(2026);
        foreach (var b in map.GetProperty("blocked").EnumerateArray())
        {
            float jh = 0.75f + (float)rng.NextDouble() * 0.55f;
            var ridge = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.18f, jh, 1.18f) },
                Position = new Vector3(b[0].GetInt32() + 0.5f, jh / 2f - 0.06f, b[1].GetInt32() + 0.5f),
                Rotation = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 0.10f,
                    ((float)rng.NextDouble() - 0.5f) * 0.5f,
                    ((float)rng.NextDouble() - 0.5f) * 0.10f),
                MaterialOverride = ridgeMat,
            };
            AddChild(ridge);
        }

        // Rubble scatter: presentation clutter only, kept clear of ridge cells.
        var blocked = new HashSet<(int, int)>();
        foreach (var b in map.GetProperty("blocked").EnumerateArray())
            blocked.Add((b[0].GetInt32(), b[1].GetInt32()));
        var rubbleMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            AlbedoTexture = GrainTex(37, 0.05f,
                dark: new Color(0.13f, 0.14f, 0.16f),
                light: new Color(0.22f, 0.235f, 0.26f)),
            Roughness = 0.95f,
        };
        for (int i = 0; i < 220; i++)
        {
            float rx = (float)rng.NextDouble() * w, rz = (float)rng.NextDouble() * h;
            if (blocked.Contains(((int)rx, (int)rz))) continue;
            float s = 0.05f + (float)rng.NextDouble() * 0.16f;
            var rock = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(s * (1f + (float)rng.NextDouble()), s * 0.7f, s) },
                Position = new Vector3(rx, s * 0.2f, rz),
                Rotation = new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * 0.6f,
                    (float)rng.NextDouble() * Mathf.Tau,
                    ((float)rng.NextDouble() - 0.5f) * 0.6f),
                MaterialOverride = rubbleMat,
            };
            AddChild(rock);
        }
    }

    private static bool Mobile(int kind) => kind == 0 || kind == 1; // units + harvesters

    private Node3D Spawn(int id, int kind, int ut, int player, Vector3 at)
    {
        var node = _models.Instantiate(kind, ut);
        node.Position = at;
        if (Mobile(kind) && player >= 0)
        {
            // Team ring: the one place team colour lives on the battlefield
            // (doc 16's one-place law, applied at the presentation layer).
            var mark = player == 0 ? DirectorateMark : SodalityMark;
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.36f },
                Position = new Vector3(0, 0.02f, 0),
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = mark,
                    EmissionEnabled = true,
                    Emission = mark,
                    EmissionEnergyMultiplier = 0.9f,
                },
                Name = "TeamRing",
            };
            node.AddChild(ring);
        }
        AddChild(node);
        _actors[id] = node;
        _kinds[id] = kind;
        return node;
    }

    public override void _Process(double delta)
    {
        if (_frames.Length == 0) return;
        _clock += (float)delta * TicksPerSecond / 2f; // frames are 2-tick samples
        while (_frame < _frames.Length - 1 && _clock >= 1f) { _clock -= 1f; ApplyFrame(_frames[++_frame]); }
        float dt = (float)delta;
        foreach (var (id, node) in _actors)
        {
            if (!_targets.TryGetValue(id, out var t)) continue;
            var to = t - node.Position;
            node.Position = node.Position.Lerp(t, dt * 10f);
            // Movement feel: mobile units yaw into their travel direction at a
            // finite turn rate instead of sliding sideways. Model forward is
            // -Z (Blender +Y forward through the glTF axis conversion).
            if (Mobile(_kinds.GetValueOrDefault(id)) && to.LengthSquared() > 0.003f)
            {
                float desired = Mathf.Atan2(-to.X, -to.Z);
                float rate = _kinds.GetValueOrDefault(id) == 0 ? TurnSpeed : TurnSpeed * 0.7f;
                var r = node.Rotation;
                r.Y = Mathf.LerpAngle(r.Y, desired, 1f - Mathf.Exp(-rate * dt));
                node.Rotation = r;
            }
        }
    }

    private void ApplyFrame(JsonElement f)
    {
        var seen = new HashSet<int>();
        foreach (var e in f.GetProperty("e").EnumerateArray())
        {
            int id = e[0].GetInt32(), kind = e[1].GetInt32(), ut = e[2].GetInt32(), pl = e[3].GetInt32();
            float x = e[4].GetInt32() / 100f, z = e[5].GetInt32() / 100f;
            seen.Add(id);
            if (!_actors.TryGetValue(id, out var node))
                node = Spawn(id, kind, ut, pl, new Vector3(x, 0, z));
            _targets[id] = new Vector3(x, 0, z);
            if (kind == 3) // ferrite fades as it drains
            {
                float g = Mathf.Max(0.2f, e[6].GetInt32() / 12000f);
                node.Scale = Vector3.One * (0.6f + g * 0.9f);
            }
        }
        foreach (var id in new List<int>(_actors.Keys))
            if (!seen.Contains(id))
            { _actors[id].QueueFree(); _actors.Remove(id); _targets.Remove(id); _kinds.Remove(id); }
    }
}
