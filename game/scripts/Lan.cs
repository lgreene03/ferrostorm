using Godot;
using Ferrostorm.Net;
using Ferrostorm.Sim;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;

namespace Ferrostorm.Client;

/// <summary>
/// TICKET-P5-SET-01: the state a networked battle would own, and the only thing
/// the HUD's desync notice reads.
///
/// READ THIS BEFORE BELIEVING THE HUD: nothing in a shipped single-player match
/// sets Desynced, because no shipped mode plays a networked match. The notice is
/// wired, proven to raise, and waiting for the mode that drives it (see the
/// LanSmoke summary for exactly what is missing). It is here rather than
/// deferred because the notice is the half of LAN the PLAYER experiences, and
/// building it against a real object beats retrofitting it onto one later.
/// </summary>
public static class NetSession
{
    /// <summary>True while a lockstep session drives the battle's ticks. No
    /// shipped mode sets this today.</summary>
    public static bool Active;
    public static bool Desynced;
    public static int DesyncTick = -1;

    /// <summary>C7c: the other commander's connection ended. Latched, and held
    /// APART from Desynced because the two are different facts with different
    /// consequences - a desync voids the result, a departure does not - and a
    /// notice that conflated them would tell a survivor their match was void
    /// when it was merely over.</summary>
    public static bool PeerLeft;
    public static int PeerLeftId = -1;

    public static void Reset()
    {
        Active = false;
        Desynced = false;
        DesyncTick = -1;
        PeerLeft = false;
        PeerLeftId = -1;
    }

    /// <summary>The relay compared two clients' state hashes for this tick and
    /// they differed. Latched, not a pulse: a desync is not recoverable, and a
    /// notice that fades is a notice the player can miss.</summary>
    public static void NoteDesync(int tick)
    {
        if (Desynced) return;
        Desynced = true;
        DesyncTick = tick;
    }

    /// <summary>The relay reported that a player's connection ended. Latched for
    /// the same reason a desync is: they are not coming back, and the survivor
    /// may be looking at the map when it happens.</summary>
    public static void NotePeerLeft(int playerId)
    {
        if (PeerLeft) return;
        PeerLeft = true;
        PeerLeftId = playerId;
    }
}

/// <summary>
/// C7b-iv: the MatchSetup on the wire.
///
/// ADR-022 put the host's setup in the Hello as a LENGTH-PREFIXED OPAQUE BLOB
/// and left the encoding to the client layer, which is here: Ferrostorm.Net
/// knows about frames, ticks and hashes and gains no reason to learn what a map
/// or a faction is.
///
/// The map travels REPO-RELATIVE, and that is the whole point rather than an
/// implementation detail. Two machines have different repo roots (and one may be
/// a packaged build whose /data sits beside the executable), so an absolute path
/// is the one form guaranteed not to resolve on the other end. The relative path
/// resolves through each machine's own GameFiles.RepoRoot, and ADR-006's
/// catalogue Check independently guarantees that what it resolves TO agrees.
/// </summary>
public static class MatchSetupBlob
{
    /// <summary>Bumped if a field is added, removed or reordered. A joiner that
    /// does not recognise the version refuses in the lobby, where the message is
    /// readable, rather than building a world it has misread and desyncing at
    /// the first order.</summary>
    /// <summary>Version 2 (DR-14b): the difficulty rung joined the setup. Bumped
    /// per the rule above rather than appended silently, because a version-1
    /// joiner reading a version-2 blob would slide one field and misread the
    /// seed, which is a desync at the first order instead of a sentence in the
    /// lobby. There is no AI in a LAN match, so the rung changes no LAN
    /// gameplay; it rides along because the blob carries the setup whole and a
    /// joiner's saved sidecar should describe the match it actually played.</summary>
    public const int Version = 2;

    public static byte[] Encode(MatchSetup s)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write(Version);
        w.Write(s.MapPath);
        w.Write(s.MissionIndex);
        w.Write(s.AiPreset);
        w.Write(s.AiDifficulty);
        w.Write(s.StartCredits);
        w.Write(s.Seed);
        w.Write(s.Faction);
        w.Write(s.OppFaction);
        w.Flush();
        return ms.ToArray();
    }

    public static MatchSetup Decode(byte[] blob)
    {
        if (blob.Length == 0)
            throw new System.IO.InvalidDataException(
                "the host sent no match setup. That host is running a build from before the "
                + "setup exchange (ADR-022); both machines must run the same version.");
        using var ms = new System.IO.MemoryStream(blob);
        using var r = new System.IO.BinaryReader(ms);
        int version = r.ReadInt32();
        if (version != Version)
            throw new System.IO.InvalidDataException(
                $"the host's match setup is version {version} and this build reads version {Version}. "
                + "Both machines must run the same version of the game.");
        return new MatchSetup
        {
            MapPath = r.ReadString(),
            MissionIndex = r.ReadInt32(),
            AiPreset = r.ReadInt32(),
            AiDifficulty = r.ReadInt32(),
            StartCredits = r.ReadInt64(),
            Seed = r.ReadUInt64(),
            Faction = r.ReadInt32(),
            OppFaction = r.ReadInt32(),
        };
    }
}

/// <summary>
/// C7b-iv: the LAN lobby, and the last mile of Q002.
///
/// The battle scene has been lockstep-driven and proven since C7b-iii (two real
/// scenes played each other to identical hashes); the only thing left between a
/// player and a two-machine match was a screen that connects them. This is that
/// screen's machinery, kept out of MainMenu so it can be driven by the headless
/// harness without a scene.
///
/// THE CONSTRAINT THAT SHAPES ALL OF IT: connecting BLOCKS, and a host blocks
/// longest. The relay accepts every player before it sends a single Hello, and
/// LockstepClient's constructor blocks reading that Hello, so a host's own
/// client does not return until the joiner has arrived - which may be never.
/// Constructing it on the main thread would freeze the window with the lobby
/// half-drawn. So every connection runs on a background thread and the menu
/// POLLS State from _Process, which is also how the smoke test above has always
/// worked. The harness found this the hard way: two clients built one after
/// another on one thread deadlock outright.
/// </summary>
public sealed class LanLobby
{
    /// <summary>A FIXED default port, because a joiner has to be able to type an
    /// address and reach it. The relay's ephemeral port 0 (which every soak uses)
    /// is unreachable by definition: nobody can guess it.</summary>
    public const int DefaultPort = 47801;

    public enum Phase { Connecting, Ready, Failed }

    /// <summary>Written by the connect thread, read by the menu's _Process.
    /// Volatile because those are genuinely different threads, and a status the
    /// menu never observes is a lobby that hangs on a spinner forever.</summary>
    private volatile Phase _state = Phase.Connecting;
    private volatile string _status = "";

    public Phase State => _state;
    /// <summary>What to put on the screen right now: the waiting line, or the
    /// reason it failed. Never blank once Failed.</summary>
    public string Status => _status;

    /// <summary>Valid only once State is Ready. The battle scene takes ownership
    /// of the client; the lobby keeps the reference only to hand it over.</summary>
    public Ferrostorm.Net.LockstepClient? Client { get; private set; }

    /// <summary>The setup the match must actually run: the host's own, or - for a
    /// joiner - the one DECODED FROM THE HOST'S BLOB. A joiner launching on its
    /// own menu selections instead of this would render a different map from the
    /// world it adopts, which is the exact failure ADR-022 exists to prevent.</summary>
    public MatchSetup? Setup { get; private set; }

    /// <summary>Which seat the relay gave this machine, or -1 before it has.
    /// The relay decides it, not the lobby: the host connects first and takes
    /// player 0 by arrival order, and nothing else in the client is entitled to
    /// an opinion about it.</summary>
    public int Seat => Client?.PlayerId ?? -1;

    /// <summary>Offscreen verification hook: the port the relay actually bound,
    /// once it has. Needed because the harness hosts on port 0 (ephemeral) so a
    /// stale relay from an earlier run cannot make the check fail against
    /// something it is not testing.</summary>
    public int RelayPortForTest => _relay?.Port ?? 0;

    private volatile Ferrostorm.Net.Relay? _relay;
    private Thread? _thread;
    private volatile bool _cancelled;

    private LanLobby() { }

    /// <summary>Build the world a MatchSetup describes, exactly as the battle
    /// scene will. One function, used by both seats, so there is no second
    /// definition of "the starting world" to drift.</summary>
    private static World BuildFrom(MatchSetup s) =>
        SkirmishLive.BuildStartingWorld(s, MapData.Load(GameFiles.Abs(s.MapPath)), out _);

    /// <summary>
    /// Open a lobby on a fixed port and wait for one player. Returns immediately;
    /// State stays Connecting for as long as nobody has joined, which is the
    /// honest state to show, and Ready the moment the handshake completes.
    /// </summary>
    public static LanLobby Host(MatchSetup setup, int port = DefaultPort)
    {
        var lobby = new LanLobby();
        // The host builds from a ROUND-TRIPPED setup rather than from its own
        // object. The joiner can only ever have what came through the blob, so
        // encoding a field the decoder drops would produce a world only the host
        // has - a desync at tick 0 that the host cannot reproduce alone. Running
        // both seats through the same codec makes any such loss show up here,
        // identically, on both machines.
        byte[] blob = MatchSetupBlob.Encode(setup);
        MatchSetup wire;
        try { wire = MatchSetupBlob.Decode(blob); }
        catch (System.Exception e)
        {
            lobby._status = $"the match setup could not be encoded for the wire: {e.Message}";
            lobby._state = Phase.Failed;
            return lobby;
        }

        lobby._status = $"waiting for a player on port {port}...";
        lobby._thread = new Thread(() =>
        {
            try
            {
                var relay = new Ferrostorm.Net.Relay(playerCount: 2, port: port,
                    bind: IPAddress.Any, setup: blob);
                relay.Start();
                lobby._relay = relay;
                // Restated from the port the relay ACTUALLY bound, not the one
                // it was asked for. They are the same for a real lobby on the
                // fixed port, and different whenever 0 was passed to get an
                // ephemeral one - and a status line that names a port nobody can
                // dial is worse than no status line.
                lobby._status = $"waiting for a player on port {relay.Port}...";
                new Thread(relay.Run) { IsBackground = true }.Start();
                // Loopback, deliberately: the host's own client is on the host's
                // own machine, and dialling its LAN address would fail on a
                // machine whose firewall allows inbound but not hairpin.
                var client = new Ferrostorm.Net.LockstepClient(
                    relay.Port, _ => BuildFrom(wire), wire.Seed, IPAddress.Loopback);
                if (lobby._cancelled) { client.Dispose(); return; }
                lobby.Client = client;
                lobby.Setup = wire;
                lobby._state = Phase.Ready;
            }
            catch (System.Exception e)
            {
                if (lobby._cancelled) return;
                lobby._status = Explain(e, hosting: true, port);
                lobby._state = Phase.Failed;
            }
        })
        { IsBackground = true };
        lobby._thread.Start();
        return lobby;
    }

    /// <summary>
    /// Dial a host. The world is built from the HOST'S blob through
    /// worldFromSetup (ADR-022), never from anything this machine's own menu was
    /// showing, so a joiner reproduces the host's world exactly.
    /// </summary>
    public static LanLobby Join(string address, int port = DefaultPort)
    {
        var lobby = new LanLobby();
        // "1.2.3.4:47801" is what a host reads off its own screen, so accept it
        // as well as a bare address rather than making the player split it.
        string host = address.Trim();
        int colon = host.LastIndexOf(':');
        if (colon > 0 && int.TryParse(host[(colon + 1)..], out int p) && p > 0 && p <= 65535)
        {
            port = p;
            host = host[..colon];
        }
        if (host.Length == 0)
        {
            lobby._status = "enter the host's address, for example 192.168.1.20";
            lobby._state = Phase.Failed;
            return lobby;
        }

        lobby._status = $"connecting to {host}:{port}...";
        lobby._thread = new Thread(() =>
        {
            try
            {
                // Resolved HERE, on the connect thread: a name lookup can block
                // for seconds on a network with no DNS, and this thread is the
                // one place where that is free.
                var ip = IPAddress.TryParse(host, out var parsed)
                    ? parsed
                    : System.Array.Find(Dns.GetHostAddresses(host),
                        a => a.AddressFamily == AddressFamily.InterNetwork)
                      ?? throw new System.Exception($"no IPv4 address found for '{host}'");

                MatchSetup? decoded = null;
                var client = new Ferrostorm.Net.LockstepClient(port,
                    // Never reached: worldFromSetup takes precedence. Present
                    // because the parameter is not optional, and throwing rather
                    // than building something plausible means that if the
                    // precedence ever inverts, it fails loudly here instead of
                    // quietly playing a different world.
                    _ => throw new System.Exception("a joiner must build from the host's setup"),
                    0UL, ip,
                    blob =>
                    {
                        decoded = MatchSetupBlob.Decode(blob);
                        return BuildFrom(decoded);
                    });
                if (lobby._cancelled) { client.Dispose(); return; }
                lobby.Client = client;
                lobby.Setup = decoded;
                lobby._state = Phase.Ready;
            }
            catch (System.Exception e)
            {
                if (lobby._cancelled) return;
                lobby._status = Explain(e, hosting: false, port);
                lobby._state = Phase.Failed;
            }
        })
        { IsBackground = true };
        lobby._thread.Start();
        return lobby;
    }

    /// <summary>Back out of the lobby. The connect thread may be blocked in an
    /// accept or a read that only a closed socket can interrupt, so the relay is
    /// stood down explicitly: without that the fixed port stays bound and the
    /// NEXT host attempt refuses with "address already in use".</summary>
    public void Cancel()
    {
        _cancelled = true;
        _relay?.Stop();
        Client?.Dispose();
        Client = null;
    }

    /// <summary>Socket errors say things like "No connection could be made
    /// because the target machine actively refused it", which tells a player
    /// nothing about what to do. Name the likely cause and the fix.</summary>
    private static string Explain(System.Exception e, bool hosting, int port)
    {
        if (e is SocketException se)
        {
            if (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
                return $"port {port} is already in use on this machine - another Ferrostorm host may still be open.";
            if (!hosting && se.SocketErrorCode is SocketError.ConnectionRefused or SocketError.TimedOut
                or SocketError.HostUnreachable or SocketError.NetworkUnreachable)
                return $"no host answered on port {port}. Check the address, that the host has opened "
                       + "their lobby, and that both machines are on the same network.";
            return $"the network refused the connection: {se.SocketErrorCode}.";
        }
        return e.Message;
    }

    /// <summary>The IPv4 addresses another machine could dial, for the host to
    /// read out. Loopback and down interfaces are excluded because neither is
    /// reachable from the other machine, and a list that includes 127.0.0.1 is a
    /// list that gets typed in and fails.</summary>
    public static List<string> LocalAddresses()
    {
        var found = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(a.Address))
                        found.Add(a.Address.ToString());
            }
        }
        catch (System.Exception) { /* an enumeration this machine will not do; the caller says so */ }
        return found;
    }
}

/// <summary>The outcome of one smoke run, in the shape the LAN screen prints.</summary>
public sealed class LanSmokeResult
{
    public bool Done;
    public bool Passed;
    public string Summary = "";
    public int Ticks;
    public ulong HashA, HashB;
    public bool Desync;
}

/// <summary>
/// TICKET-P5-SET-01: two lockstep clients, one relay, one process, over the REAL
/// starting world the menu would have built.
///
/// WHAT THIS PROVES, precisely, because the distance between this and
/// multiplayer is the whole point of the ticket's caveat. It proves that the
/// shipped Relay and LockstepClient carry the shipped match's commands over a
/// real TCP socket and that both clients' worlds agree bit for bit at the end,
/// with the relay's own hash comparison silent throughout. The runner's `lan`
/// mode already soak-tests the protocol, but against a synthetic 20-unit world
/// built by LanWorldFactory; this runs it against SkirmishLive.BuildStartingWorld
/// on a committed .fmap, which is the world a player actually gets.
///
/// WHAT IT DOES NOT PROVE: that two machines can play. They cannot, today.
/// The transport is no longer the reason: Relay takes a bind address and
/// LockstepClient an address to dial (both defaulting to loopback, Q002's
/// first half). What remains is the client frame loop: AdvanceTick blocks on
/// a Monitor.Wait until the relay's merged batch arrives, and SkirmishLive's
/// 15 Hz accumulator cannot block the frame on a socket. That integration is
/// design work with a feel cost (Q002, second half), and it stays a netcode
/// ticket rather than being quietly patched here.
/// </summary>
public static class LanSmoke
{
    public const int DefaultTicks = 300;

    /// <summary>Run on a background thread; poll Result.Done from _Process. The
    /// relay and both clients block on sockets, and blocking the frame is how a
    /// 300-tick match becomes a hung window.</summary>
    public static LanSmokeResult Start(MatchSetup setup, int ticks = DefaultTicks)
    {
        var result = new LanSmokeResult();
        var thread = new Thread(() => Run(setup, ticks, result)) { IsBackground = true };
        thread.Start();
        return result;
    }

    private static void Run(MatchSetup setup, int ticks, LanSmokeResult result)
    {
        try
        {
            string mapPath = GameFiles.Abs(setup.MapPath);
            // Each client loads its OWN MapData and builds its OWN world from
            // it. Sharing one MapData across two client threads would have them
            // reading the same object concurrently inside BuildWorld, and a
            // lockstep smoke test that races its own fixture proves nothing
            // about lockstep. The menu thread's copy, below, is a third.
            World Factory(ulong seed) =>
                SkirmishLive.BuildStartingWorld(setup, MapData.Load(mapPath), out _);
            var mapForOrders = MapData.Load(mapPath);

            var relay = new Relay(playerCount: 2);
            relay.Start();
            new Thread(relay.Run) { IsBackground = true }.Start();

            var hashes = new ulong[2];
            var errors = new System.Exception?[2];
            var threads = new Thread[2];
            for (int p = 0; p < 2; p++)
            {
                int pid = p;
                threads[p] = new Thread(() =>
                {
                    try
                    {
                        using var client = new LockstepClient(relay.Port, Factory, setup.Seed);
                        // Each client orders only its OWN units, from its own
                        // deterministic stream, exactly as the runner's soak
                        // does: identical streams on both sides would exercise
                        // the merge without ever testing the ordering.
                        var rng = new DeterministicRandom(setup.Seed * 7919UL + (ulong)client.PlayerId);
                        var mine = new List<int>();
                        for (int i = 0; i < client.World.EntityCount; i++)
                        {
                            var e = client.World.Entities[i];
                            // By SPEED rather than by kind, and deliberately: the
                            // smoke driver wants "things I can order to move", and
                            // a zero-speed unit would make the order a no-op and
                            // the soak a weaker test of the merge. Not a fourth
                            // opinion on SkirmishLive.Mobile, which answers a
                            // different question (what gets a ground ring, a
                            // corpse tumble and a unit count).
                            if (e.Alive && e.PlayerId == client.PlayerId && e.Speed != Fix64.Zero) mine.Add(i);
                        }
                        client.Prime();
                        var cmds = new List<Command>();
                        while (client.World.Tick < ticks)
                        {
                            cmds.Clear();
                            if (client.World.Tick % 15 == 0 && mine.Count > 0)
                                for (int k = 0; k < 3; k++)
                                {
                                    int id = mine[rng.NextInt(mine.Count)];
                                    cmds.Add(new Command(0, client.PlayerId, CommandType.PathMove, id,
                                        Fix64.FromInt(2 + rng.NextInt(mapForOrders.Width - 4)),
                                        Fix64.FromInt(2 + rng.NextInt(mapForOrders.Height - 4))));
                                }
                            client.SubmitCommands(cmds);
                            if (!client.AdvanceTick())
                                throw new System.Exception($"relay notified a desync at tick {client.World.Tick}");
                        }
                        hashes[pid] = client.World.ComputeStateHash();
                    }
                    catch (System.Exception ex) { errors[pid] = ex; }
                });
                threads[p].Start();
            }
            foreach (var t in threads) t.Join();

            result.Ticks = ticks;
            result.HashA = hashes[0];
            result.HashB = hashes[1];
            // The relay's own verdict, and deliberately NOT NetSession's: the
            // smoke is a menu-side test and NetSession is a battle's state. One
            // writing the other is how a smoke run that desynced in the menu
            // raises a desync notice over the next single-player skirmish.
            result.Desync = relay.DesyncDetected;

            for (int p = 0; p < 2; p++)
                if (errors[p] != null)
                {
                    result.Summary = $"CLIENT {p} FAILED: {errors[p]!.Message}";
                    result.Done = true;
                    return;
                }
            if (result.Desync) result.Summary = "FAILED: the relay flagged a desync";
            else if (hashes[0] != hashes[1])
                result.Summary = $"FAILED: final hashes differ, 0x{hashes[0]:X16} against 0x{hashes[1]:X16}";
            else
            {
                result.Passed = true;
                result.Summary = $"PASS: 2 clients, {ticks} ticks over a real relay socket, "
                    + $"both worlds on 0x{hashes[0]:X16}, zero desyncs";
            }
        }
        catch (System.Exception ex)
        {
            result.Summary = $"FAILED to start: {ex.Message}";
        }
        finally { result.Done = true; }
    }
}
