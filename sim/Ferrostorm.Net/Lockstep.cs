using System.Net;
using System.Net.Sockets;
using Ferrostorm.Sim;

namespace Ferrostorm.Net;

// TICKET-P1-08 (TDD s4). Lockstep over TCP with a relay that runs NO simulation:
// it assigns player ids, gathers each player's command batch per tick, and
// rebroadcasts the merged, player-ordered batch. Clients advance tick T only
// once the relay's merged batch for T arrives. Commands are scheduled
// CommandDelay ticks ahead. State hashes ride along every HashInterval ticks;
// the relay compares them and broadcasts a desync notice on mismatch (US1.2).
//
// Wire format (little-endian, length-prefixed):
//   [int frameLen][byte msgType][payload]
//   msgType 1 = Hello (relay->client): int playerId, int playerCount
//   msgType 2 = Batch (client->relay): int tick, int count, count * Command
//   msgType 3 = Merged (relay->client): int tick, int count, count * Command
//   msgType 4 = Hash  (client->relay): int tick, ulong hash
//   msgType 5 = Desync (relay->client): int tick
//   msgType 6 = Check  (client->relay): ulong catalogue checksum (ADR-006)
//   msgType 7 = Start  (relay->client): all catalogues agree; the game may begin
//   msgType 8 = Refuse (relay->client): ulong yours, ulong theirs; the game is off
//   msgType 9 = Left   (relay->client): int playerId; that player is gone for good
// Command wire layout: int playerId, byte type, int entityId, int auxId, long x, long y
//
// ADR-006: the hello is a handshake, not a greeting. Each client sends its
// world's catalogue checksum after building it; the relay compares all of
// them BEFORE the first batch is accepted, so two machines with different
// /data refuse the game before tick 0 instead of desyncing after it.

public static class Wire
{
    public const byte Hello = 1, Batch = 2, Merged = 3, Hash = 4, Desync = 5, Check = 6, Start = 7, Refuse = 8,
                      Left = 9;
    public const int CommandBytes = 4 + 1 + 4 + 4 + 8 + 8 + 1;

    public static void WriteCommand(BinaryWriter w, in Command c)
    {
        w.Write(c.PlayerId); w.Write((byte)c.Type); w.Write(c.EntityId); w.Write(c.AuxId);
        w.Write(c.X.Raw); w.Write(c.Y.Raw); w.Write(c.Queued);
    }

    public static Command ReadCommand(BinaryReader r, int tick)
    {
        int player = r.ReadInt32();
        var type = (CommandType)r.ReadByte();
        int entity = r.ReadInt32();
        int aux = r.ReadInt32();
        var x = new Fix64(r.ReadInt64());
        var y = new Fix64(r.ReadInt64());
        bool queued = r.ReadBoolean();
        return new Command(tick, player, type, entity, x, y, aux, queued);
    }

    public static void SendFrame(NetworkStream s, byte type, Action<BinaryWriter> payload)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);
        payload(w);
        w.Flush();
        var body = ms.ToArray();
        Span<byte> len = stackalloc byte[4];
        BitConverter.TryWriteBytes(len, body.Length);
        lock (s) { s.Write(len); s.Write(body); s.Flush(); }
    }

    public static (byte Type, byte[] Body) ReadFrame(NetworkStream s)
    {
        var lenBuf = ReadExact(s, 4);
        int len = BitConverter.ToInt32(lenBuf);
        if (len <= 0 || len > 1 << 20) throw new InvalidDataException($"bad frame length {len}");
        var body = ReadExact(s, len);
        return (body[0], body[1..]);
    }

    private static byte[] ReadExact(NetworkStream s, int n)
    {
        var buf = new byte[n];
        int got = 0;
        while (got < n)
        {
            int r = s.Read(buf, got, n - got);
            if (r <= 0) throw new EndOfStreamException();
            got += r;
        }
        return buf;
    }
}

/// <summary>Stateless-per-tick relay. Holds no simulation, only per-tick command batches in flight.</summary>
public sealed class Relay
{
    private readonly TcpListener _listener;
    private readonly int _playerCount;
    public int Port { get; private set; }
    public bool DesyncDetected { get; private set; }
    /// <summary>ADR-006: the hello found two catalogues that disagree and the
    /// game was refused before tick 0. Distinct from DesyncDetected on
    /// purpose: a refusal is the mitigation WORKING, a desync is it failing.</summary>
    public bool CatalogueRefused { get; private set; }
    /// <summary>A player's connection ended after the match began. Latched, and
    /// distinct from CatalogueRefused (a refusal happens BEFORE tick 0) and from
    /// DesyncDetected (nothing diverged; someone simply left).</summary>
    public bool PeerLeft { get; private set; }

    /// <summary>
    /// bind selects the local address the relay listens on. The default,
    /// loopback, is the configuration every soak has run; pass
    /// IPAddress.Any to accept players from another machine (Q002, first
    /// half). No existing caller passes it, so behaviour is unchanged
    /// everywhere until a host screen chooses to.
    /// </summary>
    /// <summary>ADR-022: the host's match setup, broadcast in every Hello so a
    /// joiner can build the identical world without being told the seed out of
    /// band. OPAQUE here on purpose: the net layer knows nothing about maps,
    /// factions or seeds, and never needs to.</summary>
    private readonly byte[] _setup;

    public Relay(int playerCount, int port = 0, IPAddress? bind = null, byte[]? setup = null)
    {
        _playerCount = playerCount;
        _listener = new TcpListener(bind ?? IPAddress.Loopback, port);
        _setup = setup ?? System.Array.Empty<byte>();
    }

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// C7b-iv: stand the relay down before anyone has joined. A host who opens
    /// the lobby and then backs out leaves Run blocked in AcceptTcpClient, and
    /// on a FIXED port (which a LAN host must use, or nobody can dial it) that
    /// holds the port bound: hosting, backing out and hosting again would refuse
    /// with "address already in use". Stopping the listener is what unblocks the
    /// accept, so Run below treats a failed accept as a stand-down rather than
    /// letting it escape as an unhandled exception on a background thread.
    /// </summary>
    public void Stop()
    {
        try { _listener.Stop(); } catch (Exception) { /* never started, or already down */ }
    }

    /// <summary>Accept players and pump frames until every client disconnects. Blocking; run on its own thread.</summary>
    public void Run()
    {
        var clients = new TcpClient?[_playerCount];
        var streams = new NetworkStream[_playerCount];
        try
        {
            for (int p = 0; p < _playerCount; p++)
            {
                var c = _listener.AcceptTcpClient();
                clients[p] = c;
                c.NoDelay = true;
                streams[p] = c.GetStream();
            }
        }
        catch (Exception)
        {
            // Stop() was called, or the socket failed. Either way no match
            // begins; close whoever did arrive and leave quietly.
            foreach (var c in clients) c?.Close();
            return;
        }
        for (int p = 0; p < _playerCount; p++)
        {
            int pid = p;
            // ADR-022: pid, player count, then the host's setup blob as a
            // length-prefixed tail. A relay started without one writes a zero
            // length, so every pre-ADR caller is byte-compatible.
            Wire.SendFrame(streams[p], Wire.Hello, w =>
            {
                w.Write(pid); w.Write(_playerCount);
                w.Write(_setup.Length);
                if (_setup.Length > 0) w.Write(_setup);
            });
        }

        // ADR-006: gather every player's catalogue checksum before a single
        // batch is accepted. The client's first frame after Hello is always
        // its Check (the constructor sends it and blocks for the verdict), so
        // a synchronous read per stream is safe and keeps the handshake out of
        // the pump threads entirely.
        var checksums = new ulong[_playerCount];
        try
        {
            for (int p = 0; p < _playerCount; p++)
            {
                var (type, body) = Wire.ReadFrame(streams[p]);
                if (type != Wire.Check) throw new InvalidDataException("expected catalogue Check in the hello");
                checksums[p] = BitConverter.ToUInt64(body, 0);
            }
        }
        catch (Exception)
        {
            // A client fell over before completing the hello (its world
            // factory threw, or it disconnected). The match never starts; the
            // relay stands down quietly rather than taking the process with it
            // via an unhandled exception on this background thread.
            foreach (var c in clients) c?.Close();
            _listener.Stop();
            return;
        }
        bool allAgree = true;
        for (int p = 1; p < _playerCount; p++) if (checksums[p] != checksums[0]) allAgree = false;
        if (!allAgree)
        {
            CatalogueRefused = true;
            for (int p = 0; p < _playerCount; p++)
            {
                // Name BOTH values for every player: yours, then the first one
                // that disagrees with yours, so each side's message is about
                // its own machine.
                ulong yours = checksums[p], theirs = checksums[p];
                foreach (ulong c in checksums) if (c != yours) { theirs = c; break; }
                try { Wire.SendFrame(streams[p], Wire.Refuse, w => { w.Write(yours); w.Write(theirs); }); }
                catch (Exception) { /* already gone; the refusal stands regardless */ }
            }
            foreach (var c in clients) c?.Close();
            _listener.Stop();
            return;
        }
        foreach (var s in streams)
        {
            try { Wire.SendFrame(s, Wire.Start, _ => { }); }
            catch (Exception) { /* a vanished client surfaces in its own pump below */ }
        }

        var pendingBatches = new Dictionary<int, List<byte[]>>();   // tick -> raw batch bodies received
        var pendingHashes = new Dictionary<int, List<ulong>>();     // tick -> hashes received
        object gate = new();
        int liveClients = _playerCount;

        void Pump(int p)
        {
            try
            {
                while (true)
                {
                    var (type, body) = Wire.ReadFrame(streams[p]);
                    if (type == Wire.Batch)
                    {
                        int tick = BitConverter.ToInt32(body, 0);
                        List<byte[]>? complete = null;
                        lock (gate)
                        {
                            if (!pendingBatches.TryGetValue(tick, out var list))
                                pendingBatches[tick] = list = new List<byte[]>();
                            list.Add(body);
                            if (list.Count == _playerCount) { complete = list; pendingBatches.Remove(tick); }
                        }
                        if (complete != null) BroadcastMerged(streams, tick, complete);
                    }
                    else if (type == Wire.Hash)
                    {
                        int tick = BitConverter.ToInt32(body, 0);
                        ulong hash = BitConverter.ToUInt64(body, 4);
                        bool desync = false;
                        lock (gate)
                        {
                            if (!pendingHashes.TryGetValue(tick, out var list))
                                pendingHashes[tick] = list = new List<ulong>();
                            list.Add(hash);
                            if (list.Count == _playerCount)
                            {
                                for (int i = 1; i < list.Count; i++)
                                    if (list[i] != list[0]) desync = true;
                                pendingHashes.Remove(tick);
                            }
                        }
                        if (desync)
                        {
                            DesyncDetected = true;
                            foreach (var s in streams)
                                Wire.SendFrame(s, Wire.Desync, w => w.Write(tick));
                        }
                    }
                }
            }
            catch (Exception) { /* client disconnected: match over from relay's perspective */ }
            finally
            {
                // TELL THE SURVIVORS. Without this the departure is silent on
                // the wire and lockstep simply starves: the relay never gets
                // this player's batch again, so it never broadcasts another
                // merged batch, so every remaining client's poll returns false
                // forever. Their world stops dead with nothing on screen, and
                // nothing has desynced, so the desync notice is (correctly)
                // silent too. A game that stops for no stated reason reads as a
                // crash; this makes the wire say what happened.
                //
                // Best effort by construction: a stream that is itself gone
                // throws here, which is exactly the case where nobody is left
                // to tell.
                PeerLeft = true;
                for (int q = 0; q < _playerCount; q++)
                {
                    if (q == p) continue;
                    try { Wire.SendFrame(streams[q], Wire.Left, w => w.Write(p)); }
                    catch (Exception) { /* also gone; nothing owed to them */ }
                }
                if (Interlocked.Decrement(ref liveClients) == 0) _listener.Stop();
            }
        }

        var threads = new Thread[_playerCount];
        for (int p = 0; p < _playerCount; p++)
        {
            int pid = p;
            threads[p] = new Thread(() => Pump(pid)) { IsBackground = true };
            threads[p].Start();
        }
        foreach (var t in threads) t.Join();
    }

    private void BroadcastMerged(NetworkStream[] streams, int tick, List<byte[]> batchBodies)
    {
        // Merge preserving player order: parse each batch (playerId is inside
        // every command), sort batches by their player id, concatenate.
        var parsed = new List<(int PlayerId, List<Command> Cmds)>();
        foreach (var body in batchBodies)
        {
            using var ms = new MemoryStream(body);
            using var r = new BinaryReader(ms);
            int t = r.ReadInt32();
            int count = r.ReadInt32();
            var cmds = new List<Command>(count);
            int player = -1;
            for (int i = 0; i < count; i++)
            {
                var c = Wire.ReadCommand(r, t);
                player = c.PlayerId;
                cmds.Add(c);
            }
            parsed.Add((player, cmds));
        }
        parsed.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId)); // empty batches (player -1) sort first, harmless
        foreach (var s in streams)
            Wire.SendFrame(s, Wire.Merged, w =>
            {
                w.Write(tick);
                w.Write(parsed.Sum(p => p.Cmds.Count));
                foreach (var (_, cmds) in parsed)
                    foreach (var c in cmds) Wire.WriteCommand(w, in c);
            });
    }
}

/// <summary>
/// Lockstep client: owns a World, schedules local commands CommandDelay ticks
/// ahead, and advances only when the merged batch for the current tick has
/// arrived from the relay.
/// </summary>
public sealed class LockstepClient : IDisposable
{
    public const int CommandDelay = 3;
    public const int HashInterval = 30;

    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly Dictionary<int, Command[]> _merged = new();
    private readonly object _gate = new();
    private readonly Thread _reader;
    public readonly World World;

    /// <summary>P7-8f: the commanders playing the seats no peer holds, in
    /// ascending seat order. Empty in every two-seat match, which is every LAN
    /// match that has ever run.</summary>
    private SkirmishAI[] _ai = System.Array.Empty<SkirmishAI>();
    private int[] _aiCommandCounts = System.Array.Empty<int>();
    /// <summary>Reused across ticks so a commanded tick allocates nothing beyond
    /// what it must. Touched only from the thread that drives the ticks.</summary>
    private readonly List<Command> _tickCommands = new();
    public int PlayerId { get; private set; } = -1;
    public int PlayerCount { get; private set; }
    public bool DesyncNotified { get; private set; }
    /// <summary>Another player's connection ended after the match began, and
    /// this client will therefore never receive another merged batch. Latched.
    /// Deliberately SEPARATE from DesyncNotified: nothing diverged, so a match
    /// that ends this way is not void in the way a desynced one is, and telling
    /// the survivor "you no longer share a world" would be a lie.</summary>
    public bool PeerLeft { get; private set; }
    /// <summary>Which player left, or -1.</summary>
    public int PeerLeftId { get; private set; } = -1;
    /// <summary>ADR-022: the host's match setup as broadcast in the Hello, or
    /// empty. The joiner builds its world from THIS rather than from a seed it
    /// was told separately, which is what makes a join reproduce the host's
    /// world exactly.</summary>
    public byte[] Setup { get; private set; } = System.Array.Empty<byte>();

    /// <summary>
    /// address is the relay to dial. The default, loopback, is the
    /// configuration every soak has run; a join screen passes the host's
    /// address (Q002, first half). No existing caller passes it, so
    /// behaviour is unchanged everywhere until one does.
    /// </summary>
    /// <param name="worldFromSetup">ADR-022: when supplied, the world is built
    /// from the HOST'S setup blob (read out of the Hello) instead of from the
    /// seed argument. That is what a joiner passes: it cannot know the host's
    /// map, factions or seed until the Hello arrives, and building from
    /// anything else is a guaranteed divergence wearing a lobby.</param>
    public LockstepClient(int port, Func<ulong, World> worldFactory, ulong seed, IPAddress? address = null,
        Func<byte[], World>? worldFromSetup = null)
    {
        _tcp = new TcpClient();
        _tcp.Connect(address ?? IPAddress.Loopback, port);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();

        var (type, body) = Wire.ReadFrame(_stream);
        if (type != Wire.Hello) throw new InvalidDataException("expected Hello");
        PlayerId = BitConverter.ToInt32(body, 0);
        PlayerCount = BitConverter.ToInt32(body, 4);
        // ADR-022: the host's setup blob, if this relay sent one. Read
        // defensively on length so a relay that predates the ADR (or one
        // started without a setup) still hands back an empty array rather than
        // throwing on a short body.
        Setup = body.Length >= 12 && BitConverter.ToInt32(body, 8) is int n && n > 0 && body.Length >= 12 + n
            ? body[12..(12 + n)]
            : System.Array.Empty<byte>();
        World = worldFromSetup != null ? worldFromSetup(Setup) : worldFactory(seed);

        // ADR-006: the catalogue checksum rides in the hello and the verdict
        // is waited for HERE, before the constructor returns, so a mismatched
        // game cannot reach tick 0 by construction. The checksum is the sim's
        // own canonicalised-def hash, never file bytes.
        ulong mine = World.CatalogueChecksum;
        Wire.SendFrame(_stream, Wire.Check, w => w.Write(mine));
        var (verdict, verdictBody) = Wire.ReadFrame(_stream);
        if (verdict == Wire.Refuse)
        {
            ulong yours = BitConverter.ToUInt64(verdictBody, 0);
            ulong theirs = BitConverter.ToUInt64(verdictBody, 8);
            throw new InvalidDataException(
                $"catalogue mismatch: this machine's catalogue checksum is 0x{yours:X16} " +
                $"but another player's is 0x{theirs:X16}. " +
                "Every player must run identical /data files; the game is refused before tick 0 (ADR-006).");
        }
        if (verdict != Wire.Start) throw new InvalidDataException("expected Start or Refuse after the catalogue Check");

        _reader = new Thread(ReadLoop) { IsBackground = true };
        _reader.Start();
    }

    private void ReadLoop()
    {
        try
        {
            while (true)
            {
                var (type, body) = Wire.ReadFrame(_stream);
                if (type == Wire.Merged)
                {
                    using var ms = new MemoryStream(body);
                    using var r = new BinaryReader(ms);
                    int tick = r.ReadInt32();
                    int count = r.ReadInt32();
                    var cmds = new Command[count];
                    for (int i = 0; i < count; i++) cmds[i] = Wire.ReadCommand(r, tick);
                    lock (_gate) { _merged[tick] = cmds; Monitor.PulseAll(_gate); }
                }
                else if (type == Wire.Desync)
                {
                    DesyncNotified = true;
                    lock (_gate) Monitor.PulseAll(_gate);
                }
                else if (type == Wire.Left)
                {
                    // Latched like the desync, and for the same reason: a
                    // player who has gone is not coming back, so a notice that
                    // fades is a notice the survivor can miss while looking at
                    // the map. The pulse wakes any blocking AdvanceTick, which
                    // would otherwise sit out its full timeout waiting for a
                    // batch that can no longer arrive.
                    PeerLeftId = BitConverter.ToInt32(body, 0);
                    PeerLeft = true;
                    lock (_gate) Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception) { lock (_gate) Monitor.PulseAll(_gate); }
    }

    /// <summary>
    /// P7-8f: attach the commanders that play the seats no peer holds, on a map
    /// that seats more players than the relay carries.
    ///
    /// THEIR COMMANDS DO NOT TRAVEL THE WIRE, and cannot: the relay gathers one
    /// batch per PLAYER per tick and its players are peers, so a seat with no
    /// peer has no batch to be counted in. Each client generates them locally
    /// instead and folds them into the same tick, which is sound because the
    /// world at tick T is identical on both peers by the lockstep guarantee,
    /// SkirmishAI is deterministic and reads nothing but world state, and its
    /// tuning is catalogue data that rides World.CatalogueChecksum - which the
    /// hello compares BEFORE tick 0 and refuses on. Two peers therefore provably
    /// run the same commander, which is the whole argument for doing it this way
    /// rather than nominating one peer to issue the AI's orders over the relay.
    ///
    /// Sorted by SEAT here rather than trusted to arrive sorted, because the
    /// order the commanders act in is part of the tick and a caller-side
    /// convention is not something the other peer can check. Attaching is refused
    /// once the match has begun, for the same reason: a commander that appeared
    /// mid-match on one peer only is a desync with a plausible-looking cause.
    /// </summary>
    public void SetAiCommanders(IReadOnlyList<SkirmishAI> commanders)
    {
        if (World.Tick != 0)
            throw new InvalidOperationException(
                $"commanders must be attached before the match begins; this world is already at tick {World.Tick}. "
                + "A commander attached mid-match exists on one peer and not the other, which is a desync.");
        _ai = commanders.OrderBy(a => a.Seat).ToArray();
        _aiCommandCounts = new int[_ai.Length];
    }

    /// <summary>The attached commanders, in the ascending seat order they act
    /// in.</summary>
    public IReadOnlyList<SkirmishAI> AiCommanders => _ai;

    /// <summary>How many commands each attached commander has issued so far,
    /// indexed alongside AiCommanders. Diagnostic: a seat whose count is stuck at
    /// zero is a commander that is present and doing nothing, which reads
    /// identically to a healthy match from every other angle.</summary>
    public IReadOnlyList<int> AiCommandsIssued => _aiCommandCounts;

    /// <summary>
    /// THE ONLY PLACE THIS CLIENT STEPS ITS WORLD. Both TryAdvanceTick and
    /// AdvanceTick come through here, deliberately: they differ solely in how
    /// they wait for the merged batch, and a rule about what a tick CONTAINS that
    /// was stated twice would eventually be fixed once.
    ///
    /// THE ORDER IS LOAD-BEARING and must be identical on both peers: the relay's
    /// merged batch first, exactly as it arrived (the relay has already ordered it
    /// by player id), then each commander in ascending seat order. Any other order
    /// is a different sequence of commands into World.Step, and two peers that
    /// disagreed about it would desync while every catalogue, map and seed still
    /// matched.
    /// </summary>
    private void StepAndReport(Command[] merged)
    {
        if (_ai.Length == 0)
        {
            // The overwhelmingly common case, and byte-identical to what shipped
            // before commanders existed: the merged batch is stepped as it
            // arrived, with nothing appended and nothing copied.
            World.Step(merged);
        }
        else
        {
            _tickCommands.Clear();
            _tickCommands.AddRange(merged);
            for (int i = 0; i < _ai.Length; i++)
            {
                int mark = _tickCommands.Count;
                _ai[i].Act(World, _tickCommands);
                _aiCommandCounts[i] += _tickCommands.Count - mark;
            }
            World.Step(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_tickCommands));
        }
        if (World.Tick % HashInterval == 0)
        {
            ulong h = World.ComputeStateHash();
            int t = World.Tick;
            Wire.SendFrame(_stream, Wire.Hash, w => { w.Write(t); w.Write(h); });
        }
    }

    /// <summary>Seed empty batches for ticks 0..CommandDelay-1 so the match can start. Call once before the loop.</summary>
    public void Prime()
    {
        for (int t = 0; t < CommandDelay; t++)
        {
            int tick = t;
            Wire.SendFrame(_stream, Wire.Batch, w =>
            {
                w.Write(tick);
                w.Write(0);
                // Batch must carry its player id even when empty so the relay
                // can order batches; encode as a zero-count batch and rely on
                // arrival - ordering of empty batches cannot affect the sim.
            });
        }
    }

    /// <summary>Submit this player's commands for execution at currentTick + CommandDelay.</summary>
    public void SubmitCommands(IReadOnlyList<Command> commands)
    {
        int execTick = World.Tick + CommandDelay;
        Wire.SendFrame(_stream, Wire.Batch, w =>
        {
            w.Write(execTick);
            w.Write(commands.Count);
            foreach (var c in commands)
                Wire.WriteCommand(w, new Command(execTick, PlayerId, c.Type, c.EntityId, c.X, c.Y, c.AuxId, c.Queued));
        });
    }

    /// <summary>
    /// The non-blocking half of Q002's remainder (P6 Wave C7a): step the current
    /// tick if its merged batch has ALREADY arrived, else return false
    /// immediately, never waiting. desynced reports the latched relay verdict
    /// either way. This is the frame-loop shape SkirmishLive needs: the
    /// accumulator drain calls this once per due tick and, on false, simply
    /// stops draining for the frame - the render goes on from the last
    /// snapshot, the sim waits, and the frame never blocks on a socket.
    /// Purely additive beside AdvanceTick, which the headless soaks keep.
    /// </summary>
    public bool TryAdvanceTick(out bool desynced)
    {
        Command[]? cmds;
        lock (_gate)
        {
            desynced = DesyncNotified;
            if (desynced || !_merged.TryGetValue(World.Tick, out cmds!)) return false;
            _merged.Remove(World.Tick);
        }
        StepAndReport(cmds);
        return true;
    }

    /// <summary>Block until the merged batch for the current tick arrives, then step. Returns false on desync/disconnect.</summary>
    public bool AdvanceTick(int timeoutMs = 10_000)
    {
        Command[]? cmds;
        lock (_gate)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (!_merged.TryGetValue(World.Tick, out cmds!) && !DesyncNotified)
            {
                long remaining = deadline - Environment.TickCount64;
                if (remaining <= 0) throw new TimeoutException($"tick {World.Tick} batch never arrived");
                Monitor.Wait(_gate, (int)Math.Min(remaining, 250));
            }
            if (DesyncNotified) return false;
            _merged.Remove(World.Tick);
        }
        StepAndReport(cmds);
        return true;
    }

    public void Dispose() { try { _tcp.Close(); } catch { } }
}
