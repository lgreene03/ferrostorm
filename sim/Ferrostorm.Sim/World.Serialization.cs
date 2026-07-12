namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-SIM-17: mid-game save and load. A save captures EVERY field of
/// world state in a fixed order; loading reconstructs a World whose state
/// hash equals the saved one and whose future evolution is bit-identical to
/// the uninterrupted run - the saveload runner mode proves both, which also
/// makes this the safety net for fields not covered by the hash (their
/// corruption diverges the continued replay instead).
/// Binary, little-endian, versioned, with a trailing magic to catch truncation.
/// </summary>
public sealed partial class World
{
    private const uint SaveMagic = 0x534C4131;   // "FER1"
    private const uint SaveTrailer = 0x454E4453; // "SDNE"

    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(SaveMagic);
        w.Write(Tick);
        w.Write(Winner);
        w.Write(ShortGameEnabled);
        w.Write(_players);
        w.Write(Map.Width);
        w.Write(Map.Height);
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x += 8)
            {
                byte packed = 0;
                for (int b = 0; b < 8 && x + b < Map.Width; b++)
                    if (Map.IsBlocked(x + b, y)) packed |= (byte)(1 << b);
                w.Write(packed);
            }
        }
        w.Write(_rng.State);
        for (int p = 0; p < _players; p++)
        {
            w.Write(_credits[p]);
            w.Write(_eliminatedAnnounced[p]);
            w.Write(_explored[p].Length);
            foreach (ulong word in _explored[p]) w.Write(word);
        }
        w.Write(_entities.Count);
        foreach (var e in _entities) WriteEntity(w, in e);
        w.Write(_queues.Count);
        foreach (var (id, q) in OrderedQueues())
        {
            w.Write(id);
            w.Write(q.Count);
            foreach (int t in q) w.Write(t);
        }
        var oq = new List<int>(_orderQueues.Keys);
        oq.Sort();
        w.Write(oq.Count);
        foreach (int id in oq)
        {
            var q = _orderQueues[id];
            w.Write(id);
            w.Write(q.Count);
            foreach (var c in q)
            {
                w.Write(c.Tick); w.Write(c.PlayerId); w.Write((byte)c.Type);
                w.Write(c.EntityId); w.Write(c.AuxId); w.Write(c.X.Raw); w.Write(c.Y.Raw); w.Write(c.Queued);
            }
        }
        w.Write(SaveTrailer);
    }

    // Dictionary iteration order must never leak into a serialized artefact.
    private IEnumerable<(int Id, List<int> Q)> OrderedQueues()
    {
        var keys = new List<int>(_queues.Keys);
        keys.Sort();
        foreach (int k in keys) yield return (k, _queues[k]);
    }

    public static World Load(Stream stream)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (r.ReadUInt32() != SaveMagic) throw new InvalidDataException("not a ferrostorm save");
        int tick = r.ReadInt32();
        int winner = r.ReadInt32();
        bool shortGame = r.ReadBoolean();
        int players = r.ReadInt32();
        int mw = r.ReadInt32(), mh = r.ReadInt32();
        var world = new World(0, mw, mh, players) { Tick = tick, Winner = winner, ShortGameEnabled = shortGame };
        for (int y = 0; y < mh; y++)
            for (int x = 0; x < mw; x += 8)
            {
                byte packed = r.ReadByte();
                for (int b = 0; b < 8 && x + b < mw; b++)
                    if ((packed & (1 << b)) != 0) world.Map.SetBlocked(x + b, y, true);
            }
        world._rng.State = r.ReadUInt64();
        for (int p = 0; p < players; p++)
        {
            world._credits[p] = r.ReadInt64();
            world._eliminatedAnnounced[p] = r.ReadBoolean();
            int words = r.ReadInt32();
            for (int i = 0; i < words; i++) world._explored[p][i] = r.ReadUInt64();
        }
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) world._entities.Add(ReadEntity(r));
        int queues = r.ReadInt32();
        for (int i = 0; i < queues; i++)
        {
            int id = r.ReadInt32();
            int n = r.ReadInt32();
            var q = new List<int>(n);
            for (int k = 0; k < n; k++) q.Add(r.ReadInt32());
            world._queues[id] = q;
        }
        int orderQueueCount = r.ReadInt32();
        for (int i = 0; i < orderQueueCount; i++)
        {
            int id = r.ReadInt32();
            int n = r.ReadInt32();
            var q = new List<Command>(n);
            for (int k = 0; k < n; k++)
            {
                int ct = r.ReadInt32(); int cp = r.ReadInt32(); var type = (CommandType)r.ReadByte();
                int ce = r.ReadInt32(); int ca = r.ReadInt32();
                var cx = new Fix64(r.ReadInt64()); var cy = new Fix64(r.ReadInt64());
                bool cq = r.ReadBoolean();
                q.Add(new Command(ct, cp, type, ce, cx, cy, ca, cq));
            }
            world._orderQueues[id] = q;
        }
        if (r.ReadUInt32() != SaveTrailer) throw new InvalidDataException("save truncated or corrupt");
        return world;
    }

    private static void WriteEntity(BinaryWriter w, in Entity e)
    {
        w.Write(e.Id); w.Write(e.Alive); w.Write(e.PlayerId); w.Write((byte)e.Kind);
        w.Write(e.X.Raw); w.Write(e.Y.Raw); w.Write(e.TargetX.Raw); w.Write(e.TargetY.Raw);
        w.Write(e.Moving); w.Write(e.UseFlow); w.Write(e.Speed.Raw);
        w.Write(e.Hp); w.Write((byte)e.Armour); w.Write(e.WeaponId); w.Write(e.Cooldown); w.Write(e.ExplicitTarget);
        w.Write(e.Sight.Raw);
        w.Write((byte)e.HState); w.Write(e.Carry); w.Write(e.StateTicks);
        w.Write(e.FieldId); w.Write(e.RefineryId); w.Write(e.FerriteAmount);
        w.Write(e.PowerSupply); w.Write(e.PowerDraw); w.Write(e.BuildProgress);
        w.Write(e.PrevX.Raw); w.Write(e.PrevY.Raw); w.Write(e.StallTicks); w.Write(e.BuildPaid);
        w.Write(e.AMove); w.Write(e.AMoveX.Raw); w.Write(e.AMoveY.Raw);
        w.Write(e.StructType); w.Write(e.MaxHp); w.Write(e.Repairing); w.Write(e.ReadyStructure);
        w.Write(e.Stealth); w.Write(e.Detector); w.Write(e.RevealTicks); w.Write(e.DetectedMask);
        w.Write(e.Kills); w.Write(e.Rank); w.Write(e.VetEnabled); w.Write(e.UnitType);
        w.Write(e.ChargeTicks); w.Write(e.StrikeTicks); w.Write(e.StrikeX.Raw); w.Write(e.StrikeY.Raw);
        w.Write(e.FieldCloaked);
    }

    private static Entity ReadEntity(BinaryReader r) => new()
    {
        Id = r.ReadInt32(), Alive = r.ReadBoolean(), PlayerId = r.ReadInt32(), Kind = (EntityKind)r.ReadByte(),
        X = new Fix64(r.ReadInt64()), Y = new Fix64(r.ReadInt64()),
        TargetX = new Fix64(r.ReadInt64()), TargetY = new Fix64(r.ReadInt64()),
        Moving = r.ReadBoolean(), UseFlow = r.ReadBoolean(), Speed = new Fix64(r.ReadInt64()),
        Hp = r.ReadInt32(), Armour = (ArmourClass)r.ReadByte(), WeaponId = r.ReadInt32(),
        Cooldown = r.ReadInt32(), ExplicitTarget = r.ReadInt32(),
        Sight = new Fix64(r.ReadInt64()),
        HState = (HarvestState)r.ReadByte(), Carry = r.ReadInt32(), StateTicks = r.ReadInt32(),
        FieldId = r.ReadInt32(), RefineryId = r.ReadInt32(), FerriteAmount = r.ReadInt32(),
        PowerSupply = r.ReadInt32(), PowerDraw = r.ReadInt32(), BuildProgress = r.ReadInt32(),
        PrevX = new Fix64(r.ReadInt64()), PrevY = new Fix64(r.ReadInt64()),
        StallTicks = r.ReadInt32(), BuildPaid = r.ReadInt32(),
        AMove = r.ReadBoolean(), AMoveX = new Fix64(r.ReadInt64()), AMoveY = new Fix64(r.ReadInt64()),
        StructType = r.ReadInt32(), MaxHp = r.ReadInt32(), Repairing = r.ReadBoolean(), ReadyStructure = r.ReadInt32(),
        Stealth = r.ReadBoolean(), Detector = r.ReadBoolean(), RevealTicks = r.ReadInt32(), DetectedMask = r.ReadByte(),
        Kills = r.ReadInt32(), Rank = r.ReadInt32(), VetEnabled = r.ReadBoolean(), UnitType = r.ReadInt32(),
        ChargeTicks = r.ReadInt32(), StrikeTicks = r.ReadInt32(),
        StrikeX = new Fix64(r.ReadInt64()), StrikeY = new Fix64(r.ReadInt64()),
        FieldCloaked = r.ReadBoolean(),
    };
}
