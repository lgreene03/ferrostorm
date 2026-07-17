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

    // TICKET-P5-DEF-08: a barrier is one of six meshes chosen by its 4-bit
    // neighbour mask, so it cannot live in KindModel's one-name-per-kind map.
    // Mask bits: N=1 (cell y-1), E=2 (x+1), S=4 (y+1), W=8 (x-1).
    //
    // WHICH mesh follows from the neighbour count alone and needs no axis
    // reasoning. WHICH YAW is derived below from DEF-07's orientation contract
    // (the ORIENTATION CONTRACT block in art/3d/builder.py, above the six
    // com_wall_* functions), and the derivation is recorded here because
    // a wrong entry is invisible until a corner faces the wrong way in a
    // screenshot, which is exactly what DEF-08's spec warns about.
    //
    // Step 1, the axis conversion. Blender is Z-up, glTF/Godot are Y-up, and
    // the exporter maps Blender (x,y,z) -> glTF (x,z,-y). The client maps sim X
    // to world X and sim Y to world Z (SkirmishLive.SyncActors). Therefore:
    //     Blender +X -> world +X = EAST      Blender +Y -> world -Z = NORTH
    //     Blender -X -> world -X = WEST      Blender -Y -> world +Z = SOUTH
    //
    // Step 2, the canonical mask of each mesh at yaw 0, read off the contract
    // (straight along +X; cap's ARM toward +X; corner joining +X and +Y; tee
    // omitting the -X arm) through step 1:
    //     straight {E,W} = 10    cap {E} = 2    corner {N,E} = 3    tee {N,E,S} = 7
    // VERIFIED against the exported mesh bytes rather than assumed: straight
    // measures X extent 0.950 against Z extent 0.340 (so it runs east-west);
    // cap spans X[-0.210,+0.475] (arm to the east); corner reaches +X and -Z
    // (east and north); tee reaches +X and both +/-Z but NOT -X (so it omits
    // west). Measurements taken above the symmetric 0.95 footing, which would
    // otherwise drown the signal.
    //
    // Step 3, the rotation sense. Godot's yaw about +Y sends +X to -Z, i.e.
    // E->N->W->S for increasing yaw. This is not a guess: the shipped hull-yaw
    // line Mathf.Atan2(-to.X, -to.Z) in SkirmishLive is only correct under this
    // sense, and it visibly orients every unit in the game.
    //
    // Applying R(90): E->N, N->W, W->S, S->E to each canonical gives the table.
    // NOTE: this DELIBERATELY differs from the draft table in doc 22, which is
    // wrong on ten of sixteen entries (it assumes a north-facing cap and the
    // opposite rotation sense), and from the correction proposed in DEF-07's
    // ledger entry, whose tee row does not follow from the contract either.
    private static readonly string[] WallVariant =
    {
        "com_wall_post",     // 0  none
        "com_wall_cap",      // 1  N
        "com_wall_cap",      // 2  E
        "com_wall_corner",   // 3  N|E
        "com_wall_cap",      // 4  S
        "com_wall_straight", // 5  N|S
        "com_wall_corner",   // 6  E|S
        "com_wall_tee",      // 7  N|E|S
        "com_wall_cap",      // 8  W
        "com_wall_corner",   // 9  N|W
        "com_wall_straight", // 10 E|W
        "com_wall_tee",      // 11 N|E|W
        "com_wall_corner",   // 12 S|W
        "com_wall_tee",      // 13 N|S|W
        "com_wall_tee",      // 14 E|S|W
        "com_wall_cross",    // 15 all
    };
    private static readonly float[] WallYaw =
    {
        0,     // 0  post, rotationally irrelevant
        90,    // 1  cap  {E}@0 -> R90 -> {N}
        0,     // 2  cap  canonical
        0,     // 3  corner canonical {N,E}
        270,   // 4  cap  {E}@0 -> R270 -> {S}
        90,    // 5  straight {E,W}@0 -> R90 -> {N,S}
        270,   // 6  corner {N,E}@0 -> R270 -> {E,S}
        0,     // 7  tee canonical {N,E,S}
        180,   // 8  cap  {E}@0 -> R180 -> {W}
        90,    // 9  corner {N,E}@0 -> R90 -> {N,W}
        0,     // 10 straight canonical {E,W}
        90,    // 11 tee {N,E,S}@0 -> R90 -> {N,E,W}
        180,   // 12 corner {N,E}@0 -> R180 -> {S,W}
        180,   // 13 tee {N,E,S}@0 -> R180 -> {N,S,W}
        270,   // 14 tee {N,E,S}@0 -> R270 -> {E,S,W}
        0,     // 15 cross, rotationally irrelevant
    };

    private PackedScene Load(string name)
    {
        if (!_cache.TryGetValue(name, out var scene))
        {
            scene = GD.Load<PackedScene>($"res://assets/models/{name}.glb");
            _cache[name] = scene;
        }
        return scene;
    }

    public Node3D Instantiate(int kind, int unitType)
    {
        string name = kind == 0
            ? UnitModel.GetValueOrDefault(unitType, "com_rifle_squad")
            : KindModel.GetValueOrDefault(kind, "com_power_plant");
        return Load(name).Instantiate<Node3D>();
    }

    /// <summary>DEF-08: the barrier mesh for a 4-bit neighbour mask, plus the
    /// yaw in degrees that turns its canonical orientation into that mask.</summary>
    public Node3D InstantiateWall(int mask, out float yawDeg)
    {
        yawDeg = WallYaw[mask];
        return Load(WallVariant[mask]).Instantiate<Node3D>();
    }

    /// <summary>Verification reads of the mask tables (DEF-08).</summary>
    public static string WallVariantOf(int mask) => WallVariant[mask];
    public static float WallYawOf(int mask) => WallYaw[mask];
}
