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
// Command wire layout: int playerId, byte type, int entityId, int auxId, long x, long y

public static class Wire
{
    public const byte Hello = 1, Batch = 2, Merged = 3, Hash = 4, Desync = 5;
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

    /// <summary>
    /// bind selects the local address the relay listens on. The default,
    /// loopback, is the configuration every soak has run; pass
    /// IPAddress.Any to accept players from another machine (Q002, first
    /// half). No existing caller passes it, so behaviour is unchanged
    /// everywhere until a host screen chooses to.
    /// </summary>
    public Relay(int playerCount, int port = 0, IPAddress? bind = null)
    {
        _playerCount = playerCount;
        _listener = new TcpListener(bind ?? IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    /// <summary>Accept players and pump frames until every client disconnects. Blocking; run on its own thread.</summary>
    public void Run()
    {
        var clients = new TcpClient[_playerCount];
        var streams = new NetworkStream[_playerCount];
        for (int p = 0; p < _playerCount; p++)
        {
            clients[p] = _listener.AcceptTcpClient();
            clients[p].NoDelay = true;
            streams[p] = clients[p].GetStream();
        }
        for (int p = 0; p < _playerCount; p++)
        {
            int pid = p;
            Wire.SendFrame(streams[p], Wire.Hello, w => { w.Write(pid); w.Write(_playerCount); });
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
            finally { if (Interlocked.Decrement(ref liveClients) == 0) _listener.Stop(); }
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
    public int PlayerId { get; private set; } = -1;
    public int PlayerCount { get; private set; }
    public bool DesyncNotified { get; private set; }

    /// <summary>
    /// address is the relay to dial. The default, loopback, is the
    /// configuration every soak has run; a join screen passes the host's
    /// address (Q002, first half). No existing caller passes it, so
    /// behaviour is unchanged everywhere until one does.
    /// </summary>
    public LockstepClient(int port, Func<ulong, World> worldFactory, ulong seed, IPAddress? address = null)
    {
        _tcp = new TcpClient();
        _tcp.Connect(address ?? IPAddress.Loopback, port);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();

        var (type, body) = Wire.ReadFrame(_stream);
        if (type != Wire.Hello) throw new InvalidDataException("expected Hello");
        PlayerId = BitConverter.ToInt32(body, 0);
        PlayerCount = BitConverter.ToInt32(body, 4);
        World = worldFactory(seed);

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
            }
        }
        catch (Exception) { lock (_gate) Monitor.PulseAll(_gate); }
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
        World.Step(cmds);
        if (World.Tick % HashInterval == 0)
        {
            ulong h = World.ComputeStateHash();
            int t = World.Tick;
            Wire.SendFrame(_stream, Wire.Hash, w => { w.Write(t); w.Write(h); });
        }
        return true;
    }

    public void Dispose() { try { _tcp.Close(); } catch { } }
}
