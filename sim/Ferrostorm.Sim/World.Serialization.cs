namespace Ferrostorm.Sim;

/// <summary>
/// TICKET-P2-SIM-17: mid-game save and load. A save captures EVERY field of
/// world state in a fixed order; loading reconstructs a World whose state
/// hash equals the saved one and whose future evolution is bit-identical to
/// the uninterrupted run - the saveload runner mode proves both, which also
/// makes this the safety net for fields not covered by the hash (their
/// corruption diverges the continued replay instead).
/// Binary, little-endian, versioned, with a trailing magic to catch truncation.
/// Format v2 (Q001): the per-player faction array is stored, because
/// ComputeStateHash hashes it and a save that drops a hashed field cannot
/// honour the contract above. v1 saves are still accepted and load with
/// every faction defaulted to Directorate, exactly as v1 always behaved.
/// Format v3 (ADR-006): the catalogue checksum rides immediately after the
/// magic, because /data is now the runtime source and a save resumed under a
/// different catalogue is a different game wearing the same entities. v1 and
/// v2 carry no checksum; its ABSENCE means do not check, never refuse.
/// Format v4 (ADR-007): every entity carries RallyX/RallyY/HasRally/Departing
/// appended after FieldCloaked in hash order, because rally is now sim state
/// and a save that drops hashed fields cannot honour the contract above.
/// v1/v2/v3 entities predate the fields and load with rally unset and
/// Departing false, which is exactly what those saves meant: rally was client
/// state and was already lost on every save. (The ADR as written named "v3"
/// for this bump; Wave B1 took v3 for the catalogue checksum first, so this
/// is v4 - see the dated deviation note on ADR-007.)
/// </summary>
public sealed partial class World
{
    private const uint SaveMagicV1 = 0x534C4131; // original format: no faction array (Q001)
    private const uint SaveMagicV2 = 0x534C4132; // v2 adds the per-player faction array
    private const uint SaveMagicV3 = 0x534C4133; // v3 adds the catalogue checksum (ADR-006)
    private const uint SaveMagicV4 = 0x534C4134; // v4 adds the per-entity rally fields (ADR-007)
    private const uint SaveTrailer = 0x454E4453; // "SDNE"

    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(SaveMagicV4);
        w.Write(CatalogueChecksum); // v3: the catalogue this match was played against
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
            w.Write(_playerFaction[p]); // v2: hashed state, so saved state (Q001)
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

    /// <summary>
    /// registerCatalogue (ADR-006) lets the caller register its /data catalogue
    /// into the loaded world. It runs while the world's Tick is still 0, so
    /// RegisterUnitType's own Tick != 0 guard is honoured rather than bypassed.
    /// A v3 save then asserts its recorded checksum against the world's and a
    /// mismatch REFUSES the load with both checksums named: resuming a save
    /// into a different catalogue is a silent divergence wearing a save file.
    /// v1 and v2 saves recorded no checksum, so they are never refused.
    /// </summary>
    public static World Load(Stream stream, Action<World>? registerCatalogue = null)
    {
        using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        uint magic = r.ReadUInt32();
        if (magic != SaveMagicV1 && magic != SaveMagicV2 && magic != SaveMagicV3 && magic != SaveMagicV4)
            throw new InvalidDataException("not a ferrostorm save");
        // v3 introduced the checksum and every later format keeps it (the
        // B1-era regression was conditioning a v3+ field on one magic alone).
        bool hasCatalogue = magic != SaveMagicV1 && magic != SaveMagicV2;
        bool hasRallyFields = magic == SaveMagicV4; // ADR-007: v4 entities carry rally state
        ulong recordedCatalogue = hasCatalogue ? r.ReadUInt64() : 0;
        int tick = r.ReadInt32();
        int winner = r.ReadInt32();
        bool shortGame = r.ReadBoolean();
        int players = r.ReadInt32();
        int mw = r.ReadInt32(), mh = r.ReadInt32();
        var world = new World(0, mw, mh, players) { Winner = winner, ShortGameEnabled = shortGame };
        registerCatalogue?.Invoke(world); // Tick is still 0: the legal registration window
        if (hasCatalogue && world.CatalogueChecksum != recordedCatalogue)
            throw new InvalidDataException(
                $"catalogue mismatch: this save was recorded with catalogue checksum 0x{recordedCatalogue:X16} " +
                $"but the game is running catalogue 0x{world.CatalogueChecksum:X16}. " +
                "The save is refused rather than resumed into a different game (ADR-006). " +
                "Restore the /data files the save was made with, or start a fresh battle.");
        world.Tick = tick;
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
            // v1 saves predate the faction field; the constructor default
            // (everyone Directorate) is exactly what v1 always produced.
            // v2 introduced the byte and every later format keeps it.
            if (magic != SaveMagicV1) world._playerFaction[p] = r.ReadByte();
            world._credits[p] = r.ReadInt64();
            world._eliminatedAnnounced[p] = r.ReadBoolean();
            int words = r.ReadInt32();
            for (int i = 0; i < words; i++) world._explored[p][i] = r.ReadUInt64();
        }
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) world._entities.Add(ReadEntity(r, hasRallyFields));
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
        // v4 (ADR-007): the rally fields, appended after FieldCloaked in hash
        // order exactly as ComputeStateHash appends them.
        w.Write(e.RallyX.Raw); w.Write(e.RallyY.Raw); w.Write(e.HasRally); w.Write(e.Departing);
    }

    private static Entity ReadEntity(BinaryReader r, bool hasRallyFields)
    {
        var e = ReadEntityV3(r);
        if (hasRallyFields)
        {
            // v4 fields in write order. Pre-v4 entities keep the struct
            // defaults: no rally, not departing - what those saves meant.
            e.RallyX = new Fix64(r.ReadInt64());
            e.RallyY = new Fix64(r.ReadInt64());
            e.HasRally = r.ReadBoolean();
            e.Departing = r.ReadBoolean();
        }
        return e;
    }

    private static Entity ReadEntityV3(BinaryReader r) => new()
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
