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
        BattlefieldView.BuildEnvironment(this);
        BattlefieldView.BuildLightRig(this);
        var map = _replay.GetProperty("map");
        var blocked = new List<(int, int)>();
        foreach (var b in map.GetProperty("blocked").EnumerateArray())
            blocked.Add((b[0].GetInt32(), b[1].GetInt32()));
        BattlefieldView.BuildTerrain(this, map.GetProperty("w").GetInt32(), map.GetProperty("h").GetInt32(), blocked);
    }

    private static bool Mobile(int kind) => kind == 0 || kind == 1; // units + harvesters

    private Node3D Spawn(int id, int kind, int ut, int player, Vector3 at)
    {
        var node = _models.Instantiate(kind, ut);
        node.Position = at;
        if (Mobile(kind) && player >= 0)
        {
            BattlefieldView.DressMobile(node, player);
        }
        if (kind == 3) // W4-17: amber ground stain under ferrite deposits
            node.AddChild(new Decal
            {
                TextureAlbedo = BattlefieldView.FerriteStainTex(),
                TextureEmission = BattlefieldView.FerriteStainTex(),
                EmissionEnergy = 0.6f,
                Size = new Vector3(3.2f, 1.2f, 3.2f),
                Position = new Vector3(0, 0.3f, 0),
                Name = "Stain",
            });
        AddChild(node);
        _actors[id] = node;
        _kinds[id] = kind;
        return node;
    }

    public override void _Process(double delta)
    {
        BattlefieldView.TickWater(delta);
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
            // W4-10: actors ride the ground undulation.
            float gy = BattlefieldView.GroundHeight(x, z);
            if (!_actors.TryGetValue(id, out var node))
                node = Spawn(id, kind, ut, pl, new Vector3(x, gy, z));
            _targets[id] = new Vector3(x, gy, z);
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
