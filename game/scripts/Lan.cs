using Godot;
using Ferrostorm.Net;
using Ferrostorm.Sim;
using System.Collections.Generic;
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

    public static void Reset()
    {
        Active = false;
        Desynced = false;
        DesyncTick = -1;
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
/// WHAT IT DOES NOT PROVE: that two machines can play. They cannot, today, and
/// the reason is one line rather than a shrug: Relay binds IPAddress.Loopback
/// and LockstepClient connects to IPAddress.Loopback (Lockstep.cs:93 and :241),
/// taking a port and no address, so nothing off this machine can reach the relay
/// and JOIN BY IP has no address to dial. That is a netcode ticket, not a client
/// one, and it is filed rather than quietly patched here.
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
