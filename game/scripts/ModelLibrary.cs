using Godot;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>Maps sim identity (kind, unit type) to the .glb models exported
/// from the Blender asset library. One source of visual truth.</summary>
public partial class ModelLibrary : Node
{
    private readonly Dictionary<string, PackedScene> _cache = new();

    private static readonly Dictionary<int, string> UnitModel = new()
    {
        { 1, "dir_cannon_tank" }, { 2, "com_rifle_squad" }, { 3, "com_rocket_squad" },
        { 5, "sod_shade_raider" }, { 6, "dir_sentinel_scout" }, { 7, "com_mcv" },
        { 8, "dir_howitzer" }, { 9, "sod_phantom_tank" }, { 10, "dir_bulwark_tank" },
        { 11, "com_engineer" }, { 12, "dir_vanguard_car" },
    };
    private static readonly Dictionary<int, string> KindModel = new()
    {
        { 1, "com_harvester" }, { 2, "com_refinery" }, { 4, "com_power_plant" },
        { 5, "com_factory" }, { 6, "com_construction_yard" }, { 7, "dir_turret" },
        { 8, "dir_superweapon" }, { 9, "sod_veil_projector" }, { 10, "com_service_depot" },
        { 3, "ferrite_cluster" },
    };

    public Node3D Instantiate(int kind, int unitType)
    {
        string name = kind == 0
            ? UnitModel.GetValueOrDefault(unitType, "com_rifle_squad")
            : KindModel.GetValueOrDefault(kind, "com_power_plant");
        if (!_cache.TryGetValue(name, out var scene))
        {
            scene = GD.Load<PackedScene>($"res://assets/models/{name}.glb");
            _cache[name] = scene;
        }
        return scene.Instantiate<Node3D>();
    }
}
