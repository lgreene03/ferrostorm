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
    /// lobby. It rode along because the blob carries the setup whole and a
    /// joiner's saved sidecar should describe the match it actually played.</summary>
    /// <summary>Version 3 (P7-8f): the SEAT COUNT joined the setup, and it is
    /// load-bearing rather than a rider. The host's LAN screen has an
    /// opponent-count picker, so MatchSetup.Seats is a genuine host choice, and a
    /// joiner that could not read it defaulted to zero - which SeatsFor reads as
    /// "fill the map". On a two-start map both answers are 2 and the omission was
    /// invisible; on the four-start map this wave exists to allow, a host asking
    /// for two seats would build a two-seat world while the joiner built a
    /// four-seat one. That is not a desync at the first order, it is two different
    /// worlds at tick 0, and the rung above is exactly why: the difficulty the
    /// commanders play at is now LAN gameplay too.</summary>
    /// <summary>Version 4 (P7-8h): the TEAM MODE joined the setup, and it is
    /// load-bearing for the same reason the seat count is. BuildStartingWorld
    /// reads it before tick 0, so a joiner that decoded zero would build a
    /// free-for-all against the host's 2v2 - two different worlds at tick 0
    /// rather than a desync anyone could trace back. Bumped rather than
    /// appended silently: a version-3 joiner reading a version-4 blob is refused
    /// in the lobby, where the sentence is readable.
    ///
    /// The pleasant consequence, worth stating because it is what makes the mode
    /// worth having in LAN at all: the relay seats peers by arrival order, so on
    /// a four-seat map the two humans hold seats 0 and 1 and EVEN SIDES puts them
    /// on OPPOSITE sides, each allied with the commander at seat 2 or 3. Two
    /// people plus two commanders is a 2v2 with no per-seat UI at either
    /// end.</summary>
    public const int Version = 4;

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
        w.Write(s.Seats);
        w.Write(s.TeamMode);
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
            Seats = r.ReadInt32(),
            TeamMode = r.ReadInt32(),
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

    /// <summary>P7-8f: how many seats a LAN match seats PEERS in. Named because
    /// it is now two facts rather than one - it is the relay's player count AND
    /// it is the boundary above which a seat is played by a commander instead -
    /// and those two must agree or a seat is either played twice or not at all.
    /// The relay assigns seats by arrival order, so the peers are always the
    /// LOWEST seats and the commanders always take the seats above them.</summary>
    public const int HumanSeats = 2;

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
    /// once the lobby has PUBLISHED it, and 0 until then. Needed because the
    /// harness hosts on port 0 (ephemeral) so a stale relay from an earlier run
    /// cannot make the check fail against something it is not testing.
    /// P8-67: published only once the host's own seat is claimed (HostSeat), so
    /// a joiner that dials the instant this turns nonzero is second.</summary>
    public int RelayPortForTest => _publishedPort;

    /// <summary>P8-67: true once a host lobby has published its port, which is
    /// only once its own client holds seat 0. The menu's line naming the port
    /// waits for this rather than going up the moment HOST GAME is pressed.
    /// Never true for a join lobby.</summary>
    public bool PortPublished => _publishedPort > 0;

    private volatile Ferrostorm.Net.Relay? _relay;
    private volatile int _publishedPort;
    private volatile HostSeat? _seat;
    private Thread? _thread;
    private volatile bool _cancelled;

    private LanLobby() { }

    /// <summary>Build the world a MatchSetup describes, exactly as the battle
    /// scene will. One function, used by both seats, so there is no second
    /// definition of "the starting world" to drift.</summary>
    private static World BuildFrom(MatchSetup s)
    {
        var map = MapData.Load(GameFiles.Abs(s.MapPath));
        // P7-8f: THE SEATS NO PEER HOLDS ARE PLAYED BY COMMANDERS, and this is
        // where P7-8d's refusal used to stand. That refusal existed because the
        // relay seats exactly HumanSeats peers while the world's seat count comes
        // from the map and the setup, so on a four-start map two bases had no
        // controller at all: they would never act, and VictorySystem would refuse
        // to end the match until somebody walked over and razed them.
        //
        // They have a controller now. SkirmishLive builds one SkirmishAI per seat
        // at HumanSeats and above and hands them to the lockstep client, which
        // generates their commands LOCALLY on each peer and folds them into the
        // same tick - the relay cannot carry them, because it counts one batch per
        // peer and those seats have no peer. Both peers stay on one world because
        // the commander is deterministic, reads only world state, and its tuning
        // is /data that rides the catalogue checksum this very hello compares and
        // refuses on (ADR-032). `lanaiseatsgate` measures both halves, including
        // that a peer running a different commander is CAUGHT.
        //
        // NO REFUSAL REPLACES IT, because there is no case left to refuse.
        // SeatsFor clamps the seat count to the map's starts and to [2,8], the
        // first HumanSeats of those are the peers and every seat above them takes
        // a commander, so every seat count the world can hold is filled. A map
        // with fewer starts than the relay's peers cannot arise either: the floor
        // is 2 and PlaceSkirmishStart refuses louder than this could.
        return SkirmishLive.BuildStartingWorld(s, map, out _);
    }

    /// <summary>
    /// Open a lobby on a fixed port and wait for one player. Returns immediately;
    /// State stays Connecting for as long as nobody has joined, which is the
    /// honest state to show, and Ready the moment the handshake completes.
    ///
    /// P8-67: THE HOST SEATS ITSELF FIRST. The relay seats peers in the order it
    /// accepts them, and this used to publish the relay's port (RelayPortForTest
    /// and the status line) the moment the relay bound, start the relay and only
    /// then dial its own client, so a joiner that dialled the published port
    /// inside that window took seat 0 and left the host seat 1. The client
    /// harness saw it once in 14 runs. Now the host's seat is claimed by a
    /// connection this lobby makes itself (HostSeat says why it must be), and the
    /// port is published only once that connect has returned.
    /// portPublishedForTest, when given, is called on the connect thread at the
    /// moment of publication with the published port, before anything else
    /// happens on that thread; the harness uses it to dial a joiner and hold the
    /// host's thread there, which is the widest that window can be made.
    /// relayBoundForTest, when given, is called on the connect thread once the
    /// relay has bound and before the host claims its seat, with the bound port:
    /// the window a joiner could take seat 0 in, held open by the harness while
    /// it reads what this lobby's status and the menu's line say there.
    /// </summary>
    public static LanLobby Host(MatchSetup setup, int port = DefaultPort,
        System.Action<int>? portPublishedForTest = null, System.Action<int>? relayBoundForTest = null)
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

        // P8-67: no port is named until the host holds seat 0, the status line
        // included; a line naming a port before then is an invitation to take it.
        lobby._status = "opening the lobby...";
        lobby._thread = new Thread(() =>
        {
            HostSeat? seat = null;
            try
            {
                // The splice's own loopback listener goes up FIRST, so nothing
                // slower than one loopback connect stands between the relay
                // binding below and the host claiming its seat.
                seat = HostSeat.Open();
                lobby._seat = seat;
                var relay = new Ferrostorm.Net.Relay(playerCount: HumanSeats, port: port,
                    bind: IPAddress.Any, setup: blob);
                lobby._relay = relay;
                relay.Start();
                if (lobby._cancelled) { seat.Close(); relay.Stop(); return; }
                relayBoundForTest?.Invoke(relay.Port);
                // Seat 0 is the host's from the moment this returns (HostSeat
                // says why), and not one instruction earlier.
                seat.Claim(relay.Port);
                // Restated from the port the relay ACTUALLY bound, not the one
                // it was asked for. They are the same for a real lobby on the
                // fixed port, and different whenever 0 was passed to get an
                // ephemeral one - and a status line that names a port nobody can
                // dial is worse than no status line.
                lobby._status = $"waiting for a player on port {relay.Port}...";
                lobby._publishedPort = relay.Port;
                portPublishedForTest?.Invoke(relay.Port);
                new Thread(relay.Run) { IsBackground = true }.Start();
                seat.Splice();
                // Loopback, deliberately: the host's own client is on the host's
                // own machine, and dialling its LAN address would fail on a
                // machine whose firewall allows inbound but not hairpin. It
                // dials the splice, which carries it onto the seat claimed above.
                var client = new Ferrostorm.Net.LockstepClient(
                    seat.Port, _ => BuildFrom(wire), wire.Seed, IPAddress.Loopback);
                if (lobby._cancelled) { client.Dispose(); return; }
                lobby.Client = client;
                lobby.Setup = wire;
                lobby._state = Phase.Ready;
            }
            catch (System.Exception e)
            {
                seat?.Close();
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
        _seat?.Close();
    }

    /// <summary>
    /// P8-67: THE HOST'S OWN SEAT, claimed by a connection the lobby can see land.
    ///
    /// The relay seats peers in the order Relay.Run accepts them, and a listening
    /// socket hands connections to accept in the order their handshakes completed
    /// (its accept queue is first in, first out, on every platform this ships on).
    /// So whoever's connect returns first is seat 0. The host's LockstepClient
    /// connects inside a constructor that does not return until the joiner has
    /// arrived, so the moment its connect returns cannot be seen from outside, and
    /// Ferrostorm.Net is sim code this client may not change. Instead the lobby
    /// makes the connection itself (Claim): when that connect returns the host is
    /// first in the relay's queue, and the port can be published. The host's
    /// LockstepClient then dials this object's loopback listener, and two pump
    /// threads carry every byte between that connection and the claimed one,
    /// unchanged, for the rest of the match. Either end closing closes both, so the
    /// relay sees the host leave exactly when its client does, and the client sees
    /// the relay go exactly when it does.
    ///
    /// The hop is a loopback copy with Nagle off on both of its sockets, so it adds
    /// no wait to a frame. What it cannot close: a joiner who dials the fixed port
    /// blind, without reading it off the host's screen, in the microseconds
    /// between the relay binding and Claim returning, is still first. Only a relay
    /// that seats a named peer first could close that, and that is sim code.
    /// </summary>
    private sealed class HostSeat
    {
        private readonly TcpListener _listener;
        private TcpClient? _up;
        private TcpClient? _down;
        private readonly object _gate = new();
        private bool _closed;

        /// <summary>The loopback port the host's own client dials.</summary>
        public int Port { get; }

        private HostSeat(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public static HostSeat Open()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new HostSeat(listener);
        }

        /// <summary>Dial the relay. Returns once the handshake has completed,
        /// which is the moment this connection is first in the relay's accept
        /// queue. Throws if the relay cannot be reached.</summary>
        public void Claim(int relayPort)
        {
            var up = new TcpClient { NoDelay = true };
            lock (_gate)
            {
                if (_closed) { up.Close(); throw new System.OperationCanceledException("the lobby was closed"); }
                _up = up;
            }
            up.Connect(IPAddress.Loopback, relayPort);
        }

        /// <summary>Carry the host's client onto the claimed connection: accept
        /// its one connection and pump both ways until either end closes.</summary>
        public void Splice() => new Thread(Run) { IsBackground = true }.Start();

        private void Run()
        {
            try
            {
                var down = _listener.AcceptTcpClient();
                TcpClient up;
                lock (_gate)
                {
                    if (_closed || _up is null) { down.Close(); return; }
                    _down = down;
                    up = _up;
                }
                // One connection is all this ever carries; nothing else on the
                // machine gets to dial into the host's seat.
                try { _listener.Stop(); } catch (System.Exception) { /* already down */ }
                down.NoDelay = true;
                NetworkStream fromHost = down.GetStream(), toRelay = up.GetStream();
                new Thread(() => Pump(toRelay, fromHost)) { IsBackground = true }.Start();
                Pump(fromHost, toRelay);
            }
            catch (System.Exception)
            {
                Close();
            }
        }

        private void Pump(NetworkStream from, NetworkStream to)
        {
            try { from.CopyTo(to); }
            catch (System.Exception) { /* either end went; closing both below is the answer */ }
            Close();
        }

        /// <summary>Idempotent, and safe from any thread: the lobby's Cancel,
        /// a failed host thread and both pumps may all call it.</summary>
        public void Close()
        {
            TcpClient? up, down;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                up = _up;
                down = _down;
            }
            try { _listener.Stop(); } catch (System.Exception) { /* never started, or already down */ }
            down?.Close();
            up?.Close();
        }
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
