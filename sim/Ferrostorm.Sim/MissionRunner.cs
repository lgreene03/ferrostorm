using System.Globalization;

namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-SIM-20: the campaign trigger engine. Like SkirmishAI, it is a
/// deterministic sim-adjacent driver: it reads world state and acts through
/// legitimate scripting entry points (spawns, grants, DeclareWinner). Each
/// trigger fires exactly once. Mission state (fired flags, messages) lives
/// here, not in the world; campaign saves therefore pair a world save with
/// mission state - ticketed as P2-SIM-21.
/// </summary>
public sealed class MissionRunner
{
    private readonly MapData _map;
    private readonly IReadOnlyDictionary<string, List<int>> _tags;
    private readonly bool[] _fired;
    public readonly List<string> Messages = new();

    public MissionRunner(MapData map, Dictionary<string, List<int>> tags)
    {
        _map = map;
        _tags = tags;
        _fired = new bool[map.Triggers.Count];
    }

    // --- Campaign save (TICKET-P2-SIM-21) ---------------------------------
    // A campaign save = World.Save + this. The map file itself is NOT stored:
    // on load the mission is rebuilt from the same map (entity ids and tags
    // are deterministic from map order), then this state is restored on top.
    private const uint MissionMagic = 0x4D534E31; // "MSN1"

    public void Save(BinaryWriter w)
    {
        w.Write(MissionMagic);
        w.Write(_fired.Length);
        foreach (bool f in _fired) w.Write(f);
        w.Write(Messages.Count);
        foreach (string m in Messages) w.Write(m);
    }

    public void LoadState(BinaryReader r)
    {
        if (r.ReadUInt32() != MissionMagic) throw new InvalidDataException("not a mission save");
        int n = r.ReadInt32();
        if (n != _fired.Length) throw new InvalidDataException($"mission save has {n} triggers, map has {_fired.Length}");
        for (int i = 0; i < n; i++) _fired[i] = r.ReadBoolean();
        Messages.Clear();
        int msgs = r.ReadInt32();
        for (int i = 0; i < msgs; i++) Messages.Add(r.ReadString());
    }

    /// <summary>Evaluate triggers. Actions that command units (assault)
    /// append to <paramref name="output"/> exactly as the skirmish AI does -
    /// the mission is a player, not a god. Pass null if the mission uses no
    /// commanding actions.</summary>
    public void Tick(World w, List<Command>? output = null)
    {
        for (int i = 0; i < _map.Triggers.Count; i++)
        {
            if (_fired[i]) continue;
            var t = _map.Triggers[i];
            if (!Holds(w, t.When)) continue;
            _fired[i] = true;
            Execute(w, t.Do, output);
        }
    }

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private bool Holds(World w, string[] cond) => cond[0] switch
    {
        "elapsed" => w.Tick >= I(cond[1]),
        "credits" => w.Credits(I(cond[1])) >= I(cond[2]),
        "destroyed" => _tags.TryGetValue(cond[1], out var ids)
                       && ids.TrueForAll(id => !w.Entities[id].Alive),
        "entered" => AnyEntityIn(w, I(cond[1]), I(cond[2]), I(cond[3]), I(cond[4])),
        "owned" => _tags.TryGetValue(cond[1], out var owned)
                   && owned.TrueForAll(id => w.Entities[id].Alive && w.Entities[id].PlayerId == I(cond[2])),
        _ => throw new FormatException($"unknown trigger condition '{cond[0]}'"),
    };

    private static bool AnyEntityIn(World w, int player, int cx, int cy, int radius)
    {
        Fix64 px = Map.CellCentre(cx), py = Map.CellCentre(cy);
        Fix64 rr = Fix64.FromInt(radius * radius);
        for (int i = 0; i < w.Entities.Count; i++)
        {
            var e = w.Entities[i];
            if (!e.Alive || e.PlayerId != player) continue;
            if (e.Kind is not (EntityKind.Unit or EntityKind.Harvester)) continue;
            if (Fix64.DistSq(e.X - px, e.Y - py) <= rr) return true;
        }
        return false;
    }

    private void Execute(World w, string[] act, List<Command>? output)
    {
        switch (act[0])
        {
            case "grant":
                w.GrantCredits(I(act[1]), I(act[2]));
                break;
            case "spawn":
            {
                int player = I(act[1]), type = I(act[2]), cx = I(act[3]), cy = I(act[4]);
                int count = act.Length > 5 ? I(act[5]) : 1;
                var def = w.GetUnitType(type);
                for (int k = 0; k < count; k++)
                {
                    int ox = cx + k % 3, oy = cy + k / 3; // deterministic little grid
                    if (def.Kind == EntityKind.Harvester)
                        w.SpawnHarvester(player, Map.CellCentre(ox), Map.CellCentre(oy));
                    else
                        w.SpawnUnit(player, Map.CellCentre(ox), Map.CellCentre(oy), def.Speed, def.Hp,
                            def.Armour, def.WeaponId, def.SightCells, def.Stealth, def.Detector, def.Veterancy, type);
                }
                break;
            }
            case "win":
                w.DeclareWinner(I(act[1]));
                break;
            case "assault":
            {
                // Every mobile unit of P attack-moves on the cell: the verb
                // that turns spawned garrisons into an offensive. Issued as
                // commands - the mission is a player, not a god.
                if (output is null) throw new InvalidOperationException("assault trigger requires a command output list");
                int player = I(act[1]);
                Fix64 ax = Map.CellCentre(I(act[2])), ay = Map.CellCentre(I(act[3]));
                for (int i = 0; i < w.Entities.Count; i++)
                {
                    var e = w.Entities[i];
                    if (!e.Alive || e.PlayerId != player || e.Kind != EntityKind.Unit) continue;
                    output.Add(new Command(w.Tick, player, CommandType.AttackMove, i, ax, ay));
                }
                break;
            }
            case "message":
                Messages.Add(act[1]);
                break;
            default: throw new FormatException($"unknown trigger action '{act[0]}'");
        }
    }
}
