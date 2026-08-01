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
/// Format v5 (ADR-012): every entity carries FerriteCap appended after the
/// rally tail, because a field harvested below its spawn amount must know that
/// amount to regrow towards it. FerriteCap is not hashed (it is immutable
/// spawn-time state, like Sight), so a save is the only channel that carries
/// it. v1..v4 entities predate the field: they load with FerriteCap defaulted
/// to their stored FerriteAmount, which treats the saved level as the cap so a
/// pre-v5 field never regrows above where it was saved. That is regrowth
/// resuming sanely on an old save rather than inventing a ceiling the save
/// never held.
/// Format v6 (Q013, ADR-014): every entity carries NearestApproachSq and
/// NoProgressTicks appended after the ferrite cap, because the no-progress
/// settle backstop is now hashed sim state and a save that drops hashed fields
/// cannot honour the resume-bit-identical contract above. v1..v5 entities
/// predate the fields and load with both zero, which is the "unseeded" sentinel:
/// the first eligible tick after the resume reseeds NearestApproachSq from the
/// live distance, so a mid-walk pre-v6 save resumes with the backstop simply
/// re-armed rather than mid-count, which is the sane meaning of a save that
/// never recorded the counter.
/// Format v7 (ADR-015): every entity carries the command stance tail - Stance
/// (byte), PostX/PostY, PatrolX/PatrolY (Fix64) and PatrolOutbound (bool) -
/// appended after the no-progress tail, because a stance is now hashed sim
/// state and a save that drops hashed fields cannot honour the contract above.
/// v1..v6 entities predate the fields and load with Stance defaulted to
/// Aggressive and every position field zero, which is exactly what those saves
/// meant (no stance existed): those units resume with today's auto-acquire
/// behaviour, so old saves and replays continue identically.
/// Format v11 (P7-8c): the per-player TEAM id joins the faction byte in the
/// per-player block, because the team map is hashed state and a save that drops
/// a hashed field cannot honour the resume-bit-identical contract above.
///
/// It is written UNCONDITIONALLY, unlike the guarded fold ComputeStateHash uses
/// for the same data, and the asymmetry is the point rather than an
/// inconsistency. A hash is an accumulator, so an unexecuted fold costs nothing
/// and the reader is a comparison that never has to find its place. A save is a
/// strictly positional byte stream, so a conditional field needs a discriminator
/// IN the stream for the reader to find the next field - which is itself new
/// bytes, so it buys nothing - and, decisively, there is no golden save file to
/// keep still: the goldens are state hashes, and no golden scenario saves. So
/// the bump is real, four bytes a seat, and honest.
/// v1..v10 saves predate the field and load with every player on their own team,
/// which is exactly what those saves meant (no teams existed), so they resume as
/// the free-for-alls they were recorded as.
/// </summary>
public sealed partial class World
{
    private const uint SaveMagicV1 = 0x534C4131; // original format: no faction array (Q001)
    private const uint SaveMagicV2 = 0x534C4132; // v2 adds the per-player faction array
    private const uint SaveMagicV3 = 0x534C4133; // v3 adds the catalogue checksum (ADR-006)
    private const uint SaveMagicV4 = 0x534C4134; // v4 adds the per-entity rally fields (ADR-007)
    private const uint SaveMagicV5 = 0x534C4135; // v5 adds the per-entity ferrite cap (ADR-012)
    private const uint SaveMagicV6 = 0x534C4136; // v6 adds the no-progress backstop state (Q013, ADR-014)
    private const uint SaveMagicV7 = 0x534C4137; // v7 adds the per-entity command stance tail (ADR-015)
    private const uint SaveMagicV8 = 0x534C4138; // v8 adds the second build lane block (ADR-023)
    private const uint SaveMagicV9 = 0x534C4139; // v9 adds the transport cargo block (P7-3)
    // v10 adds the sabotage disable block (P7-11a). The tag byte runs on from
    // '9' (0x39) to ':' (0x3A) because the family is one ASCII byte wide and
    // there is no two-digit slot in it; the numbering keeps incrementing, which
    // is what every comparison here relies on, and the constant is what the
    // code reads rather than the character.
    private const uint SaveMagicV10 = 0x534C413A;
    // v11 adds the per-player team id (P7-8c); the tag byte runs on from ':'
    // (0x3A) to ';' (0x3B) on the note above.
    private const uint SaveMagicV11 = 0x534C413B;
    private const uint SaveTrailer = 0x454E4453; // "SDNE"

    public void Save(Stream stream)
    {
        using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        w.Write(SaveMagicV11);
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
            w.Write(_playerTeam[p]);    // v11: hashed state, so saved state (P7-8c)
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
        // ADR-023 (v8): the second build lanes, sorted by yard id for the same
        // reason the queues are. Written unconditionally as a count so the
        // format stays strictly positional like every block before it; an
        // inert world writes a zero and costs four bytes.
        var lanes = new List<int>(_lanes.Keys);
        lanes.Sort();
        w.Write(lanes.Count);
        foreach (int id in lanes)
        {
            var l = _lanes[id];
            w.Write(id);
            w.Write(l.Progress); w.Write(l.Paid); w.Write(l.Ready);
            w.Write(l.Queue.Count);
            foreach (int t in l.Queue) w.Write(t);
        }
        // P7-3 (v9): what each transport is carrying, sorted by transport id
        // for the same reason the lanes and queues are - dictionary iteration
        // order must never leak into a serialized artefact. Written
        // unconditionally as a count, so the format stays strictly positional
        // and a world with no transport costs four bytes. Without this block a
        // player who saved with troops aboard would load to find them gone,
        // which is data loss rather than a missing feature.
        var carriers = new List<int>(_cargo.Keys);
        carriers.Sort();
        w.Write(carriers.Count);
        foreach (int id in carriers)
        {
            var hold = _cargo[id];
            w.Write(id);
            w.Write(hold.Count);
            foreach (var cu in hold) { w.Write(cu.UnitType); w.Write(cu.Hp); w.Write(cu.Rank); }
        }
        // P7-11a (v10): which buildings are switched off and when they come
        // back, sorted by entity id for the reason every block above is sorted -
        // dictionary iteration order must never leak into a serialized artefact.
        // Written unconditionally as a count, so the format stays strictly
        // positional and a world with no sabotage costs four bytes. A tick stamp
        // survives the round trip with nothing to rewind, because Tick is saved
        // beside it. Without this block a player who saved during a brown-out
        // would load with the lights back on, which is a divergence rather than
        // a missing feature.
        var disabled = new List<int>(_disabledUntil.Keys);
        disabled.Sort();
        w.Write(disabled.Count);
        foreach (int id in disabled)
        {
            w.Write(id);
            w.Write(_disabledUntil[id]);
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
        if (magic != SaveMagicV1 && magic != SaveMagicV2 && magic != SaveMagicV3 && magic != SaveMagicV4 && magic != SaveMagicV5 && magic != SaveMagicV6 && magic != SaveMagicV7 && magic != SaveMagicV8 && magic != SaveMagicV9 && magic != SaveMagicV10 && magic != SaveMagicV11)
            throw new InvalidDataException("not a ferrostorm save");
        // v3 introduced the checksum and every later format keeps it (the
        // B1-era regression was conditioning a v3+ field on one magic alone).
        bool hasCatalogue = magic != SaveMagicV1 && magic != SaveMagicV2;
        // ADR-007: v4 and every later format carry rally state (the B1-era
        // regression was conditioning a later field on one magic alone; do not
        // repeat it - every new format MUST be listed in each tail's predicate
        // or that tail misreads and the whole entity record misaligns).
        bool hasRallyFields = magic == SaveMagicV4 || magic == SaveMagicV5 || magic == SaveMagicV6 || magic == SaveMagicV7 || magic == SaveMagicV8 || magic == SaveMagicV9 || magic == SaveMagicV10 || magic == SaveMagicV11;
        // ADR-012: v5 and every later format carry the cap (do not condition a
        // later field on one magic alone - the B1-era regression this guards).
        bool hasFerriteCap = magic == SaveMagicV5 || magic == SaveMagicV6 || magic == SaveMagicV7 || magic == SaveMagicV8 || magic == SaveMagicV9 || magic == SaveMagicV10 || magic == SaveMagicV11;
        // Q013/ADR-014: v6 and every later format carry the backstop state.
        bool hasNoProgress = magic == SaveMagicV6 || magic == SaveMagicV7 || magic == SaveMagicV8 || magic == SaveMagicV9 || magic == SaveMagicV10 || magic == SaveMagicV11;
        bool hasStance = magic == SaveMagicV7 || magic == SaveMagicV8 || magic == SaveMagicV9 || magic == SaveMagicV10 || magic == SaveMagicV11; // ADR-015: v7+ entities carry the command stance tail
        // P7-3: every version from v8 ONWARD carries the lane block, not v8
        // alone. Written as an equality it silently skipped the block on a v9
        // save and the reader then fell out of step with the writer, which
        // surfaced as "save truncated or corrupt" - a version test that names
        // one version is the same enumeration trap as a rule that names one
        // kind, and it will bite again at v10 unless it is written as a floor.
        bool hasBuildLanes = magic is SaveMagicV8 or SaveMagicV9 or SaveMagicV10 or SaveMagicV11; // ADR-023
        // P7-11a: the warning above came true at v10 exactly as written. The
        // cargo block's own test was `magic == SaveMagicV9`, an equality, so
        // adding v10 skipped it on a save that HAD written it and every later
        // read fell out of step with the writer. Both tests are floors now.
        bool hasCargo = magic is SaveMagicV9 or SaveMagicV10 or SaveMagicV11;   // P7-3
        bool hasSabotage = magic is SaveMagicV10 or SaveMagicV11;               // P7-11a
        // P7-8c: v11 and every later format carry the per-player team id. A
        // floor from the first line it is written on, for the reason the two
        // comments above record twice over.
        bool hasTeams = magic is SaveMagicV11;
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
            // v11 (P7-8c): the team. A pre-v11 save carries none and keeps the
            // constructor default - this player on their own team - which is
            // exactly what those saves meant, so they resume as free-for-alls.
            // An out-of-range id from a hand-edited file is REFUSED rather than
            // clamped: it would index VictorySystem's living-team span out of
            // bounds, and a silently corrected alliance is a desync wearing a
            // save file.
            if (hasTeams)
            {
                int team = r.ReadInt32();
                if ((uint)team >= (uint)players)
                    throw new InvalidDataException(
                        $"save carries team {team} for seat {p}, which is not a seat in this {players}-player match");
                world._playerTeam[p] = team;
            }
            world._credits[p] = r.ReadInt64();
            world._eliminatedAnnounced[p] = r.ReadBoolean();
            int words = r.ReadInt32();
            for (int i = 0; i < words; i++) world._explored[p][i] = r.ReadUInt64();
        }
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++) world._entities.Add(ReadEntity(r, hasRallyFields, hasFerriteCap, hasNoProgress, hasStance));
        // P7-11c: restore MineSystem's fast path from the entities themselves
        // rather than spending a save field and a format version on it. The flag
        // is not state (see World._minesInPlay: with no living mine the system
        // writes nothing either way), so a save carries no bit for it and every
        // save written before the mine existed resumes correctly by construction.
        foreach (var e in world._entities)
            if (e.Kind == EntityKind.Mine) { world._minesInPlay = true; break; }
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
        // ADR-023: the second build lanes. A pre-v8 save has no block and so no
        // lanes, which is exactly what those saves meant (no second lane
        // existed), so they resume identically.
        if (hasBuildLanes)
        {
            int laneCount = r.ReadInt32();
            for (int i = 0; i < laneCount; i++)
            {
                int id = r.ReadInt32();
                var l = new BuildLane { Progress = r.ReadInt32(), Paid = r.ReadInt32(), Ready = r.ReadInt32() };
                int n = r.ReadInt32();
                for (int k = 0; k < n; k++) l.Queue.Add(r.ReadInt32());
                // An inert lane was never written, but refuse to resurrect one
                // if a hand-edited file carries it: an inert entry would break
                // the "present means active" invariant the hash guard rests on.
                if (!l.Inert) world._lanes[id] = l;
            }
        }
        // P7-3 (v9): the transport holds. A pre-v9 save carries no block and
        // loads with nothing aboard, which is exactly what those saves meant,
        // so they resume identically.
        if (hasCargo)
        {
            int carrierCount = r.ReadInt32();
            for (int i = 0; i < carrierCount; i++)
            {
                int id = r.ReadInt32();
                int n = r.ReadInt32();
                var hold = new List<CargoUnit>(n);
                for (int k = 0; k < n; k++)
                    hold.Add(new CargoUnit(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
                // An empty hold was never written, but refuse to resurrect one
                // from a hand-edited file: an empty entry would break the
                // "present means carrying" invariant the hash guard rests on,
                // the same reason an inert lane is refused above.
                if (hold.Count > 0) world._cargo[id] = hold;
            }
        }
        // P7-11a (v10): the switched-off buildings. A pre-v10 save carries no
        // block and loads with everything running, which is exactly what those
        // saves meant, so they resume identically.
        if (hasSabotage)
        {
            int disabledCount = r.ReadInt32();
            for (int i = 0; i < disabledCount; i++)
            {
                int id = r.ReadInt32();
                int until = r.ReadInt32();
                // A lapsed stamp was never written, but refuse to resurrect one
                // from a hand-edited file: an entry that gates nothing would
                // still fold into the state hash, breaking the "present means
                // switched off" invariant the guarded fold rests on - the same
                // reason an inert lane and an empty hold are refused above.
                if (until > world.Tick) world._disabledUntil[id] = until;
            }
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
        // v5 (ADR-012): the ferrite cap, appended after the rally tail. Not
        // hashed, so a save is the only channel that carries it.
        w.Write(e.FerriteCap);
        // v6 (Q013, ADR-014): the no-progress backstop state, appended after the
        // ferrite cap in hash order exactly as ComputeStateHash appends it.
        w.Write(e.NearestApproachSq.Raw); w.Write(e.NoProgressTicks);
        // v7 (ADR-015): the command stance tail, appended after the no-progress
        // tail in hash order exactly as ComputeStateHash appends it.
        w.Write((byte)e.Stance);
        w.Write(e.PostX.Raw); w.Write(e.PostY.Raw);
        w.Write(e.PatrolX.Raw); w.Write(e.PatrolY.Raw);
        w.Write(e.PatrolOutbound);
    }

    private static Entity ReadEntity(BinaryReader r, bool hasRallyFields, bool hasFerriteCap, bool hasNoProgress, bool hasStance)
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
        // v5 field in write order. A pre-v5 entity has no stored cap: default it
        // to the loaded FerriteAmount so the field is treated as full at its
        // saved level and never regrows above where the old save left it (the
        // sane resume ADR-012's save-compat clause names). A non-field entity's
        // cap is meaningless and stays zero, matching a fresh spawn.
        e.FerriteCap = hasFerriteCap ? r.ReadInt32() : e.FerriteAmount;
        // v6 fields in write order. A pre-v6 entity has neither, so both default
        // to zero: NearestApproachSq zero is the "unseeded" sentinel the backstop
        // reseeds on the next eligible tick, and NoProgressTicks zero is a fresh
        // count. That resumes an old save with the backstop re-armed rather than
        // mid-count, which is exactly what a save that never held the counter meant.
        if (hasNoProgress)
        {
            e.NearestApproachSq = new Fix64(r.ReadInt64());
            e.NoProgressTicks = r.ReadInt32();
        }
        // v7 fields in write order. A pre-v7 entity has none, so Stance defaults
        // to Aggressive and every position field to zero: exactly what those
        // saves meant, since no stance existed. The unit resumes with today's
        // auto-acquire behaviour and the save is hash-identical to a fresh
        // Aggressive spawn (ADR-015 save-compat clause).
        if (hasStance)
        {
            e.Stance = (Stance)r.ReadByte();
            e.PostX = new Fix64(r.ReadInt64());
            e.PostY = new Fix64(r.ReadInt64());
            e.PatrolX = new Fix64(r.ReadInt64());
            e.PatrolY = new Fix64(r.ReadInt64());
            e.PatrolOutbound = r.ReadBoolean();
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
