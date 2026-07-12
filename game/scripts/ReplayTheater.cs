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
/// </summary>
public partial class ReplayTheater : Node3D
{
    [Export] public string ReplayPath = "res://replay.json";
    [Export] public float TicksPerSecond = 15f;

    private JsonElement _replay;
    private JsonElement[] _frames = System.Array.Empty<JsonElement>();
    private readonly Dictionary<int, Node3D> _actors = new();
    private readonly Dictionary<int, Vector3> _targets = new();
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
        BuildTerrain();
    }

    private void BuildTerrain()
    {
        var map = _replay.GetProperty("map");
        int w = map.GetProperty("w").GetInt32(), h = map.GetProperty("h").GetInt32();
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w + 8, h + 8) },
            Position = new Vector3(w / 2f, 0, h / 2f),
            MaterialOverride = Flat(new Color(0.055f, 0.06f, 0.065f)),
        };
        AddChild(ground);
        var ridgeMat = Flat(new Color(0.17f, 0.20f, 0.23f));
        foreach (var b in map.GetProperty("blocked").EnumerateArray())
        {
            var ridge = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.02f, 0.85f, 1.02f) },
                Position = new Vector3(b[0].GetInt32() + 0.5f, 0.42f, b[1].GetInt32() + 0.5f),
                MaterialOverride = ridgeMat,
            };
            AddChild(ridge);
        }
    }

    private static StandardMaterial3D Flat(Color c) => new() { AlbedoColor = c, Roughness = 0.95f };

    public override void _Process(double delta)
    {
        if (_frames.Length == 0) return;
        _clock += (float)delta * TicksPerSecond / 2f; // frames are 2-tick samples
        while (_frame < _frames.Length - 1 && _clock >= 1f) { _clock -= 1f; ApplyFrame(_frames[++_frame]); }
        foreach (var (id, node) in _actors)
            if (_targets.TryGetValue(id, out var t))
                node.Position = node.Position.Lerp(t, (float)delta * 10f);
    }

    private void ApplyFrame(JsonElement f)
    {
        var seen = new HashSet<int>();
        foreach (var e in f.GetProperty("e").EnumerateArray())
        {
            int id = e[0].GetInt32(), kind = e[1].GetInt32(), ut = e[2].GetInt32();
            float x = e[4].GetInt32() / 100f, z = e[5].GetInt32() / 100f;
            seen.Add(id);
            if (!_actors.TryGetValue(id, out var node))
            {
                node = _models.Instantiate(kind, ut);
                node.Position = new Vector3(x, 0, z);
                AddChild(node);
                _actors[id] = node;
            }
            _targets[id] = new Vector3(x, 0, z);
            if (kind == 3) // ferrite fades as it drains
            {
                float g = Mathf.Max(0.2f, e[6].GetInt32() / 12000f);
                node.Scale = Vector3.One * (0.6f + g * 0.9f);
            }
        }
        foreach (var id in new List<int>(_actors.Keys))
            if (!seen.Contains(id)) { _actors[id].QueueFree(); _actors.Remove(id); _targets.Remove(id); }
    }
}
