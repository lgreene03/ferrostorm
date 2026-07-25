using Godot;
using Ferrostorm.Sim;
using System.Collections.Generic;

namespace Ferrostorm.Client;

/// <summary>
/// The headless CLIENT harness this repo has never had.
///
/// The sim is verified exhaustively - twenty-four golden hashes, a dozen gates,
/// a five-seed determinism suite - and the client has been verified by nothing
/// but "it compiles" and a structural grep. That asymmetry is not academic: it
/// is why the same defect shape has shipped four times, each time code that
/// LOOKED implemented and was dead. A ferrite field that scaled by an Hp that
/// was always 1, so no field ever visibly drained. A packaged build containing
/// zero managed assemblies. An Outpost no map placed. An Outpost no player could
/// select or learn about. Every one of them was client-side, and every one would
/// have died the first time anything drove the scene and looked at the result.
///
/// Run it:
///     Godot --headless --audio-driver Dummy --path game res://scenes/Verify.tscn
///
/// It boots the real Skirmish scene, drives it through the same public hooks an
/// offscreen check has always had, asserts, prints one line per check and a
/// verdict, and EXITS NONZERO on failure so CI can fail on it.
///
/// Adding a check is the point: if a client wave cannot be checked here, that is
/// worth knowing before it ships rather than after.
/// </summary>
public partial class VerifyRunner : Node
{
    private readonly List<string> _failures = new();
    private SkirmishLive _game = null!;
    private int _frame;

    private void Check(bool ok, string what)
    {
        GD.Print(ok ? $"  ok    {what}" : $"  FAIL  {what}");
        if (!ok) _failures.Add(what);
    }

    public override void _Ready()
    {
        GD.Print("verify: headless client harness");
        // The seat and the step mode must both be set before the scene loads.
        // AutoStep off means the sim only advances when StepTicks says so, which
        // is what lets a check measure state at an exact tick instead of racing
        // the frame clock.
        SkirmishLive.AutoStep = false;
        SkirmishLive.LocalSeat = 1;          // THE JOINER'S SEAT: the whole point
        // Leave MapPath NULL: CurrentSetup() defaults to skirmish-01 as an
        // ABSOLUTE path and then relativises it. Handing it a relative path
        // makes that round trip produce nonsense, the match refuses, and the
        // scene defers a change back to the menu - which frees this node and
        // hangs the run with no output. That cost one debugging cycle; the
        // timeout below means it can only ever cost one.
        // skirmish-02 rather than the default skirmish-01, because it carries
        // OUTPOSTS and skirmish-01 does not. Everything else a check needs (the
        // opening hand, a Construction Yard, ferrite) is on both, so this is
        // free coverage rather than a compromise. Absolute, because
        // CurrentSetup() relativises what it is given.
        MatchConfig.MapPath = GameFiles.Abs("data/maps/skirmish-02.fmap");
        MatchConfig.AiPreset = 0;

        var scene = GD.Load<PackedScene>("res://scenes/Skirmish.tscn");
        _game = scene.Instantiate<SkirmishLive>();
        AddChild(_game);
    }

    public override void _Process(double delta)
    {
        // One frame for the scene's own _Ready to finish building the world,
        // the HUD and the actors before anything is asserted about them.
        // A hard frame budget. If the scene refused to assemble it defers a
        // change back to the menu, this node is freed and nothing ever asserts;
        // without a ceiling that is an infinite hang in CI with no diagnosis.
        if (_frame++ > 600)
        {
            GD.Print("verify: FAIL - the battle scene never became drivable. "
                     + $"Refusal notice: '{MainMenu.BattleRefusedNotice}'");
            GetTree().Quit(1);
            return;
        }
        if (!IsInstanceValid(_game) || !_game.IsInsideTree()) return;
        if (_frame < 3) return;
        SetProcess(false);
        RunChecks();

        GD.Print(_failures.Count == 0
            ? "verify: PASS - the client was driven from the player-1 seat and read player 1 throughout"
            : $"verify: FAIL - {_failures.Count} check(s) failed");
        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }

    private void RunChecks()
    {
        // --- The seat itself -------------------------------------------------
        // C7b slice 2 plumbed LocalPlayerId through ninety-three sites and could
        // not verify any of them, because nothing could drive the scene. This is
        // that owed verification.
        Check(_game.LocalPlayerId == 1, "the scene took the seat it was given (player 1)");

        // --- Selection reads MY units, not the host's ------------------------
        // The failure this catches is the joiner's whole screen being inert:
        // a selection filter still asking for player 0 selects nothing when you
        // are player 1, and that is invisible in single player.
        int mine = _game.SelectAllOwn();
        Check(mine > 0, $"select-all-own found the seat's own units ({mine})");
        Check(_game.SelectionCount == mine, "the selection holds what select-all-own returned");

        // --- The treasury is MINE --------------------------------------------
        // Both players start with the same opening credits, so a bare equality
        // would pass even if this read the wrong seat. Assert it tracks the
        // seat's own spending instead, which only the right seat can do.
        long before = _game.CreditsNow;
        Check(before > 0, $"the seat has a treasury ({before})");

        // --- The sim advances under an explicit step -------------------------
        int t0 = _game.CurrentTick;
        _game.StepTicks(30);
        Check(_game.CurrentTick == t0 + 30, $"StepTicks advanced exactly 30 ticks ({t0} -> {_game.CurrentTick})");

        // --- Orders from this seat actually reach the sim --------------------
        // A command carries a PlayerId and ApplyCommand REFUSES any whose
        // carrier is not that player's. So if the seat were wrong, orders would
        // be silently dropped and the units would simply never move - the exact
        // shape of bug that survives review.
        _game.SelectAllOwn();
        var (bx, bz) = _game.FirstSelectedPosition();
        _game.OrderMoveTo(bx + 6f, bz);
        _game.StepTicks(90);
        var (ax, az) = _game.FirstSelectedPosition();
        Check(Mathf.Abs(ax - bx) + Mathf.Abs(az - bz) > 0.5f,
              $"an order issued from the seat moved its unit ({bx:0.0},{bz:0.0} -> {ax:0.0},{az:0.0})");

        // --- Fog is computed for MY seat -------------------------------------
        // A joiner whose fog was still built for player 0 would see the host's
        // vision: their own base shrouded and the enemy's revealed.
        Check(_game.FogRevealsOwnBase(), "fog reveals the seat's own base");

        // ================= BACKFILL =================
        // Everything below guards a feature that SHIPPED with no way to check
        // it. Each was believed to work; four such beliefs have already turned
        // out to be wrong, so they are asserted now rather than trusted.

        // --- The Outpost explains itself (C4 legibility fix) -----------------
        // The mechanic was unreachable twice over: no map placed one, and then
        // a placed one could not be selected because selection is own-only, so
        // nothing in the game ever told a player it pays or how to take it.
        int outpost = _game.FindEntity(EntityKind.Outpost, -1);
        Check(outpost >= 0, "the map carries a neutral outpost to inspect");
        if (outpost >= 0)
        {
            _game.InspectForTest(outpost);
            string readout = _game.ReadoutText();
            Check(_game.InspectedId == outpost, "an unowned outpost can be inspected without being selected");
            Check(readout.Contains("OUTPOST"), $"the readout names it (\"{readout}\")");
            Check(readout.Contains("cr/s"), "the readout says it PAYS, which is the whole mechanic");
            Check(readout.Contains("engineer"), "the readout says HOW to take it");
            Check(_game.SelectionCount == 0, "inspecting does not put a foreign entity in the selection");
        }

        // --- Formation slots (C1b) -------------------------------------------
        // Slot assignment must be distinct per unit and STABLE: the same group
        // ordered to the same point twice must get the same slots, or units
        // shuffle every time an order is repeated.
        _game.SelectAllOwn();
        var slotsA = _game.ResolveFormationSlots(60f, 40f);
        var slotsB = _game.ResolveFormationSlots(60f, 40f);
        Check(slotsA.Count >= 2, $"a group move resolves into formation slots ({slotsA.Count})");
        var distinct = new HashSet<(long, long)>();
        foreach (var kv in slotsA) distinct.Add((kv.Value.RawX, kv.Value.RawY));
        Check(distinct.Count == slotsA.Count, "every unit gets its OWN slot, none stacked");
        bool stable = slotsA.Count == slotsB.Count;
        foreach (var kv in slotsA)
            if (!slotsB.TryGetValue(kv.Key, out var other) || other != kv.Value) stable = false;
        Check(stable, "the same group ordered to the same point twice gets the SAME slots");

        // --- Sidebar cancel and refund (C3) ----------------------------------
        // The client could queue but never cancel, at all, until C3. The refund
        // is pay-as-you-build, so cancelling the head returns exactly what was
        // drained - asserted to the credit, because "roughly right" is how a
        // refund bug hides.
        int yard = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        Check(yard >= 0, "the seat owns a Construction Yard to queue at");
        if (yard >= 0)
        {
            long creditsBefore = _game.CreditsNow;
            _game.QueueStructure(1);              // power plant
            _game.StepTicks(20);                  // pay-as-you-build drains a little
            Check(_game.QueuedAt(yard) == 1, $"the order reached the yard's line ({_game.QueuedAt(yard)})");
            long midway = _game.CreditsNow;
            Check(midway < creditsBefore, $"building drained the treasury ({creditsBefore} -> {midway})");
            _game.CancelStructure(1);
            _game.StepTicks(1);
            Check(_game.QueuedAt(yard) == 0, "cancelling cleared the line");
            Check(_game.CreditsNow == creditsBefore,
                  $"the refund was EXACT, to the credit ({midway} -> {_game.CreditsNow}, started {creditsBefore})");
        }

        RunLanChecks();
    }

    /// <summary>
    /// C7b-iii acceptance: TWO REAL BATTLE SCENES playing each other over an
    /// in-process relay. Not the net layer in isolation, which the sim runner
    /// already soaks - the actual SkirmishLive frame path, both seats, through
    /// the lockstep poll.
    ///
    /// This is the check the whole LAN wave exists to satisfy, and until the
    /// harness landed there was no way to write it at all.
    /// </summary>
    private void RunLanChecks()
    {
        GD.Print("  --    LAN: two battle scenes over an in-process relay");
        // The host's setup blob carries the seed, so the joiner builds the
        // host's world rather than one it was told about out of band (ADR-022).
        const ulong seed = 4242UL;
        var setup = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(setup, seed);
        var relay = new Ferrostorm.Net.Relay(playerCount: 2, setup: setup);
        relay.Start();
        var relayThread = new System.Threading.Thread(relay.Run) { IsBackground = true };
        relayThread.Start();

        Ferrostorm.Sim.World BuildFrom(ulong s)
        {
            var map = Ferrostorm.Sim.MapData.Load(GameFiles.Abs("data/maps/skirmish-02.fmap"));
            var w = map.BuildWorld(s, players: 2, out _, SkirmishLive.RegisterCatalogue);
            map.PlaceSkirmishStart(w, 8000);
            return w;
        }

        SkirmishLive Seat(int seat, Ferrostorm.Net.LockstepClient client)
        {
            SkirmishLive.AutoStep = false;
            SkirmishLive.LocalSeat = seat;
            SkirmishLive.PendingNet = client;
            MatchConfig.MapPath = GameFiles.Abs("data/maps/skirmish-02.fmap");
            var sc = GD.Load<PackedScene>("res://scenes/Skirmish.tscn").Instantiate<SkirmishLive>();
            AddChild(sc);
            return sc;
        }

        try
        {
            // Both clients are constructed CONCURRENTLY, on their own threads.
            // The relay accepts every player before it sends a single Hello, and
            // a LockstepClient's constructor blocks reading that Hello, so
            // building them one after another on this thread deadlocks: the
            // first waits for a Hello that cannot come until the second
            // connects. Worth knowing for the real Host and Join flow too - a
            // host cannot construct its own client inline and then wait for a
            // joiner on the same thread.
            Ferrostorm.Net.LockstepClient? hostClient = null, joinClient = null;
            System.Exception? connectError = null;
            var hostThread = new System.Threading.Thread(() =>
            {
                try { hostClient = new Ferrostorm.Net.LockstepClient(relay.Port, BuildFrom, seed); }
                catch (System.Exception e) { connectError = e; }
            });
            var joinThread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Handed a DELIBERATELY WRONG seed: it must build from the
                    // Hello's setup blob instead (ADR-022).
                    joinClient = new Ferrostorm.Net.LockstepClient(relay.Port, BuildFrom, 999999UL, null,
                        blob => BuildFrom(System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(blob)));
                }
                catch (System.Exception e) { connectError = e; }
            });
            hostThread.Start(); joinThread.Start();
            hostThread.Join(15000); joinThread.Join(15000);
            if (connectError != null) throw connectError;
            if (hostClient == null || joinClient == null) throw new System.Exception("clients did not connect in time");
            var host = Seat(0, hostClient);
            var join = Seat(1, joinClient);

            Check(host.IsNetworked && join.IsNetworked, "both scenes are running as lockstep clients");
            Check(host.LocalPlayerId == 0 && join.LocalPlayerId == 1, "the two scenes took OPPOSITE seats");
            Check(!host.CanSave && !join.CanSave, "saving is refused in a LAN match");

            hostClient.Prime();
            joinClient.Prime();

            // Drive both scenes the way the frame loop does: each polls, and a
            // tick only lands once BOTH have submitted for it. Interleaved on
            // one thread precisely because neither call may block.
            int spins = 0;
            while (host.CurrentTick < 60 && spins++ < 4000)
            {
                int before = host.CurrentTick + join.CurrentTick;
                host.StepTicks(1);
                join.StepTicks(1);
                // Yield when neither could advance. The merged batch arrives on
                // the client's reader THREAD, so a spin loop that never gives
                // the scheduler a chance simply burns its whole budget before a
                // single batch lands - which is what a first attempt did, and it
                // read exactly like a broken lockstep rather than an impatient
                // test. A real frame loop gets this for free by rendering.
                if (host.CurrentTick + join.CurrentTick == before)
                    System.Threading.Thread.Sleep(1);
            }
            Check(host.CurrentTick >= 60, $"the host advanced under lockstep ({host.CurrentTick} ticks)");
            Check(join.CurrentTick == host.CurrentTick,
                  $"both seats advanced in lockstep ({host.CurrentTick} vs {join.CurrentTick})");
            Check(host.StateHash == join.StateHash,
                  $"the two seats hold IDENTICAL worlds (0x{host.StateHash:X16} vs 0x{join.StateHash:X16})");
            Check(!relay.DesyncDetected, "the relay saw no desync");

            host.QueueFree();
            join.QueueFree();
        }
        catch (System.Exception ex)
        {
            Check(false, $"the LAN match threw: {ex.Message}");
        }
    }
}
