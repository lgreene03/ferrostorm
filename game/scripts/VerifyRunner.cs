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

        // --- ...and so is WHAT IS DRAWN UNDER IT -----------------------------
        // The check above passes on the shroud TEXTURE, which has always used
        // LocalPlayerId. The actor loop and the minimap feed were a different
        // matter: both still asked player 0's eyes, so at seat 1 a joiner's own
        // army was drawn only where the HOST had vision and the host's whole
        // army was drawn through the fog. Two checks, because the two halves
        // fail in opposite directions and one alone would look fine.
        int ownYard = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        Check(ownYard >= 0 && _game.DrawnForLocalSeatForTest(ownYard),
              "my own base is DRAWN for me (not gated on the other player's vision)");
        int enemyYard = _game.FindEntity(EntityKind.ConstructionYard, _game.EnemyPlayerId);
        Check(enemyYard >= 0, "the opposition owns a base to hide");
        if (enemyYard >= 0)
            Check(!_game.DrawnForLocalSeatForTest(enemyYard),
                  "an enemy in unseen fog is NOT drawn for me");

        // --- Placement asks about MY base, not the other player's ------------
        // ValidPlacement's player argument selects WHOSE structures anchor the
        // build radius. The client asked as player 0 and then issued the command
        // as LocalPlayerId: the same rule, two different players. At seat 1 the
        // ghost was green only inside the HOST'S base and red inside the
        // joiner's own, and since the commit gates on it, a joiner could not
        // build anywhere at all. Two directions, because a seat that inverts
        // passes either check alone.
        const int powerPlant = 1;
        int ownYard2 = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        int foeYard = _game.FindEntity(EntityKind.ConstructionYard, _game.EnemyPlayerId);
        if (ownYard2 >= 0 && foeYard >= 0)
        {
            int nearMine = _game.PlaceableCellsNearForTest(ownYard2, powerPlant, 4);
            int nearTheirs = _game.PlaceableCellsNearForTest(foeYard, powerPlant, 4);
            Check(nearMine > 0, $"I can build in MY OWN base ({nearMine} cells accept a power plant)");
            Check(nearTheirs == 0, $"I canNOT build inside the OPPOSITION'S base ({nearTheirs} cells)");
        }

        // --- Stop disarms EVERY armed order ----------------------------------
        // IssueStop disarmed attack-move and not patrol, and it was the only
        // site in the family that treated them separately. Arm a patrol, change
        // your mind, press stop: the units halted and the patrol stayed armed,
        // so the next left click issued it to the whole selection and marched
        // them back out. Both directions, since the pair must clear together.
        _game.SelectAllOwn();
        _game.PressKey(Settings.BindOf("patrol"));   // the LIVE binding, not a guessed letter
        Check(_game.PatrolArmed, "a patrol can be armed (the precondition)");
        _game.PressStop();
        Check(!_game.PatrolArmed, "STOP disarms an armed patrol");
        _game.PressKey(Settings.BindOf("attack_move"));
        Check(_game.AttackMoveArmed, "an attack-move can be armed (the precondition)");
        _game.PressStop();
        Check(!_game.AttackMoveArmed, "...and still disarms an armed attack-move");
        _game.ClearSelectionForTest();

        // --- A harvester says what it is CARRYING (P5-ECON-08) ---------------
        // The readout showed "700/700", which is HIT POINTS, so a full hopper
        // and an empty one were indistinguishable - the one number a harvester
        // exists to produce was the one number the game would not show. Driven
        // through a REAL load: select the harvester, read the line, put ore
        // aboard, read it again.
        int harv = _game.FindEntity(EntityKind.Harvester, _game.LocalPlayerId);
        Check(harv >= 0, "the seat owns a harvester to inspect");
        if (harv >= 0)
        {
            _game.SelectOnlyForTest(harv);
            string empty = _game.ReadoutText();
            Check(empty.Contains("LOAD 0/"), $"an empty harvester reads LOAD 0 (\"{empty}\")");
            _game.SetCarryForTest(harv, 350);
            string half = _game.ReadoutText();
            Check(half.Contains("LOAD 350/"), $"a half-full one reads its actual load (\"{half}\")");
            // The distinction that was missing: the load line must not be the
            // hit-point line. Both are present and they must differ.
            Check(half.Contains("700/700") && half.Contains("LOAD 350/700"),
                  "hit points and cargo are BOTH shown, and are different numbers");
            _game.SetCarryForTest(harv, 0);
            _game.ClearSelectionForTest();
        }

        // --- A ferrite field can be interrogated at all ----------------------
        // The deposit a player is about to send a harvester across the map for
        // could not be inspected: no readout, and its only tell was the node's
        // size, which is floor-clamped and so cannot separate "nearly spent"
        // from "spent".
        int fieldId = _game.FindEntity(EntityKind.FerriteField, -1);
        Check(fieldId >= 0, "the map carries a ferrite field to inspect");
        if (fieldId >= 0)
        {
            // Reached by a REAL CLICK, not by setting the id. The readout being
            // correct is worth nothing if the pick never returns a field, and a
            // check that calls InspectForTest directly would pass either way -
            // the same call-site gap that let a seat-relative team colour
            // survive a check of the colour law itself.
            var (fcx, fcy) = _game.CellOfForTest(fieldId);
            var at = _game.ScreenOf(fcx + 0.5f, fcy + 0.5f);
            _game.ClearSelectionForTest();
            _game.BoxSelect(at, at);            // from == to, so the click branch runs
            // Asserted on the KIND, not the instance: deposits sit adjacent and
            // the pick radius is 1.4 cells, so a click aimed at one field
            // legitimately lands on its neighbour. Demanding the exact id would
            // be a check that fails for being right.
            Check(_game.InspectedId >= 0
                  && _game.EntityKindForTest(_game.InspectedId) == EntityKind.FerriteField,
                  $"CLICKING a ferrite field inspects a field (id {_game.InspectedId})");
            string fr = _game.ReadoutText();
            Check(fr.Contains("FERRITE FIELD"), $"a field names itself when inspected (\"{fr}\")");
            Check(fr.Contains("cr"), "...and states the stock in credits, which is what the trip is worth");
            Check(_game.SelectionCount == 0, "inspecting a field does not put it in the selection");
            _game.ClearSelectionForTest();
        }

        // --- The end-of-match banner tells ME what happened ------------------
        // _winner is an ABSOLUTE player id and the banner asked whether it was
        // zero, so at seat 1 it was exactly inverted: the LAN joiner who had
        // just won was shown DEFEAT and played the failure line, while the host
        // who lost was congratulated. The last thing a match says, saying the
        // opposite of what happened. Two directions, because a banner that
        // inverts passes either check alone.
        _game.EliminateForTest(_game.EnemyPlayerId);           // the OTHER player is out: I won
        Check(_game.BannerVisibleForTest, "the match banner is raised on elimination");
        Check(_game.BannerTextForTest.Contains("VICTORY"),
              $"eliminating the OPPOSITION reads as VICTORY at seat 1 (\"{_game.BannerTextForTest.Split('\n')[0]}\")");
        _game.ResetVictoryForTest();
        _game.EliminateForTest(_game.LocalPlayerId);           // I am out: I lost
        Check(_game.BannerTextForTest.Contains("DEFEAT"),
              $"being eliminated MYSELF reads as DEFEAT (\"{_game.BannerTextForTest.Split('\n')[0]}\")");
        _game.ResetVictoryForTest();

        // --- A captured structure changes colour -----------------------------
        // DressStructure ran only at actor creation, and only a wall-mask change
        // or death rebuilds an actor - so a captured building kept the colour of
        // the player who LOST it, and a claimed neutral outpost never grew a
        // strip at all, which put the worst case on the newest mechanic.
        int capturable = _game.FindEntity(EntityKind.Outpost, -1);
        if (capturable >= 0)
        {
            Check(_game.ActorTeamOwnerForTest(capturable) == -1,
                  "an unclaimed outpost is painted for nobody (the precondition)");
            _game.SetOwnerForTest(capturable, _game.LocalPlayerId);
            _game.PumpActorsForTest();
            Check(_game.ActorTeamOwnerForTest(capturable) == _game.LocalPlayerId,
                  $"a captured outpost is repainted for its NEW owner ({_game.ActorTeamOwnerForTest(capturable)})");
            // HAND IT BACK. An owned outpost pays 15 cr/s, and leaving it
            // claimed made the treasury RISE during the later build-and-refund
            // checks - income masked the pay-as-you-build drain and the exact
            // refund arithmetic came out 30 credits high. A check that changes
            // the world the next check reads is not a check, it is a fixture
            // bug waiting to be blamed on the product.
            _game.SetOwnerForTest(capturable, -1);
            _game.PumpActorsForTest();
            Check(_game.ActorTeamOwnerForTest(capturable) == -1,
                  "...and reverts to nobody when it changes hands back");
        }

        // --- The cursor never promises a verb the click refuses --------------
        // CursorFor's own header claims it "runs the exact picks IssueOrder
        // runs". The refinery precondition was missing, so with no refinery
        // standing the cursor showed the harvest verb over every deposit and the
        // click yielded a NO REFINERY toast. Asserted as the invariant rather
        // than as a screen position: whatever the cursor offers, the order path
        // must agree it is possible.
        Check(!_game.RefineryLive, "no refinery stands at the opening hand (the precondition)");
        Check(!_game.HarvestVerbOfferedForTest(),
              "with no refinery, the harvest verb is NOT offered over a deposit");

        // --- The fog hides the SHOOTING too ----------------------------------
        // CombatEffects gated only on the actor EXISTING, and a fog-hidden
        // enemy's actor exists with Visible = false. So an unseen turret firing
        // out of unexplored fog drew its muzzle flash and tracer: the fog hid
        // the shooter and the effects layer painted a bright arrow at it.
        // The second check is the control - it is what catches a "fix" that
        // simply turned all effects off.
        // A shot has two ends and the fog can hide either, so the rule is per
        // EFFECT: muzzle and report at the shooter, tracer between them, impact
        // and flinch at the target. Three checks, because conflating them was
        // wrong in BOTH directions - drawing everything gave the shooter away,
        // and drawing nothing meant a unit shot from the dark took damage
        // without reacting at all.
        int hiddenFoe = _game.FindEntity(EntityKind.ConstructionYard, _game.EnemyPlayerId);
        int hiddenFoe2 = _game.FindEntity(EntityKind.Harvester, _game.EnemyPlayerId);
        int myUnit = _game.FindEntity(EntityKind.Harvester, _game.LocalPlayerId);
        if (hiddenFoe >= 0 && hiddenFoe2 >= 0 && myUnit >= 0)
        {
            Check(!_game.DrawnForLocalSeatForTest(hiddenFoe) && !_game.DrawnForLocalSeatForTest(hiddenFoe2),
                  "both ends of the enemy exchange are in fog (the precondition)");
            int leaked = _game.EffectNodesFromFiredForTest(hiddenFoe, hiddenFoe2);
            Check(leaked == 0,
                  $"an unseen shot at an unseen target draws NOTHING ({leaked} effect nodes)");
            int onMe = _game.EffectNodesFromFiredForTest(hiddenFoe, myUnit);
            Check(onMe > 0,
                  $"...but a round LANDING on my own visible unit still strikes it ({onMe} nodes)");
            int mine2 = _game.EffectNodesFromFiredForTest(myUnit, myUnit);
            Check(mine2 > 0, $"...and my own visible unit still draws its muzzle flash ({mine2} nodes)");
        }

        // --- A wall run the player cannot afford SAYS SO ---------------------
        // The single click tested the treasury and the drag did not, so the two
        // paths disagreed about one rule and the drag was the one that never
        // mentioned money: a run drawn with 300 credits tinted entirely green,
        // sent every segment, and the sim silently dropped each one past the
        // money while the readout quoted a total it could not pay.
        //
        // Invisible at the opening treasury, which is why it lasted: 8000
        // credits buys exactly the 80-segment cap, so the two limits bite on the
        // same segment until the player has spent something. The check therefore
        // spends first.
        const int wallType = 9;
        int wallYard = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        if (wallYard >= 0)
        {
            var (wx, wy) = _game.CellOfForTest(wallYard);
            _game.EnterPlacement(wallType);
            _game.BeginWallDragAtCell(wx + 3, wy - 4);
            _game.DragToCellForTest(wx + 3, wy + 7);          // a 12-cell run
            int rich = _game.DragGhostsAcceptedForTest();
            Check(rich >= 6, $"a long wall run is drawable with a full treasury ({rich} segments accept)");
            Check(!_game.WallDragSummaryForTest().Contains("TRUNCATED"),
                  $"...and the readout does not cry truncation (\"{_game.WallDragSummaryForTest()}\")");

            // Down to 500 credits: five segments at 100 each, and no more.
            long had = _game.CreditsNow;
            _game.GrantCreditsForTest(500 - had);
            _game.DragToCellForTest(wx + 3, wy + 7);          // redraw the same run
            int poor = _game.DragGhostsAcceptedForTest();
            Check(poor == 5, $"only what the treasury covers tints green ({poor} of {rich}, at 500 credits)");
            string say = _game.WallDragSummaryForTest();
            Check(say.Contains("ONLY 5 AFFORDABLE"), $"and the readout SAYS so (\"{say}\")");
            _game.GrantCreditsForTest(had - 500);             // put the treasury back
            _game.CancelPlacementForTest();
        }

        // --- The ferrite drain reaches the renderer (P5-ECON-01) -------------
        // The first of the five defects of this shape, and the last to get a
        // check. The fix shipped; nothing asserted it, which is exactly how it
        // came to ship dead in the first place.
        var (famount, fcap) = _game.FieldViewForTest();
        Check(famount > 0 && fcap > 0,
              $"a ferrite field reaches the VIEW carrying its real stock ({famount}/{fcap})");
        // The regression that actually happened: the old expression was
        // constant, so a mined-out field drew exactly as large as a full one.
        Check(SkirmishLive.FieldFullness(fcap, fcap) > SkirmishLive.FieldFullness(fcap / 4, fcap),
              "a mined-out field draws SMALLER than a full one (the dead expression did not)");
        // The repair vehicle reached the catalogue, the sidebar and the model
        // library, and not the name table, so it already read "UNIT".
        Check(_game.UnitNameForTest(World.RepairVehicleType) == "REPAIR VEHICLE",
              $"unit type {World.RepairVehicleType} has a NAME (\"{_game.UnitNameForTest(World.RepairVehicleType)}\")");
        Check(_game.UnitNameForTest(World.RepairVehicleType + 1) != "",
              "and a type beyond the table still falls back rather than throwing");

        // --- The brown-out boundary is where ADR-008 says ---------------------
        // Four implementations of this threshold became one (the client now
        // calls the sim's), so there is nothing left to pin against. What a
        // future edit CAN still move silently is the boundary itself, which
        // ADR-008 clause 1 makes inclusive: at exactly 75 per cent the grid is
        // healthy. That is one integer away from wrong in both directions, so
        // both sides of it are asserted.
        Check(!SkirmishLive.BrownedOut(75, 100), "exactly 75 per cent is NOT a brown-out (the boundary is inclusive)");
        Check(SkirmishLive.BrownedOut(74, 100), "one unit below 75 per cent IS a brown-out");
        Check(!SkirmishLive.BrownedOut(0, 0), "a grid with no draw at all is not browned out");

        // --- Team colour is a property of the PLAYER, not of the viewer ------
        // The minimap held a rival copy keyed on "me versus them", so at seat 1
        // a joiner's own army was orange on the minimap and teal on the
        // battlefield. Asserted from the seat that inverts: my own colour must
        // be the SODALITY mark here, because I am player 1 - if this reads the
        // Directorate mark, the "me versus them" copy is back.
        Check(BattlefieldView.MarkFor(_game.LocalPlayerId) == BattlefieldView.SodalityMark,
              "at seat 1 my own mark is Sodality's, not 'whoever is looking' orange");
        Check(BattlefieldView.MarkFor(_game.EnemyPlayerId) == BattlefieldView.DirectorateMark,
              "and the opposition at seat 0 wears Directorate's");
        Check(BattlefieldView.MarkFor(-1) == BattlefieldView.NeutralMark,
              "an unowned entity wears neither side's mark");
        // ...and the same question asked of what the minimap will ACTUALLY
        // draw, because the two checks above pin the law and a call site can
        // still grow its own copy. At tick 0 the opposition is entirely in fog,
        // so every dot on this minimap is mine or neutral. If any dot wears the
        // OTHER side's mark, the feed is colouring by "me versus them" again.
        var dots = _game.MinimapView.DotColoursForTest();
        int mineOnMap = 0, foeOnMap = 0;
        foreach (var c in dots)
        {
            if (c == BattlefieldView.SodalityMark) mineOnMap++;
            else if (c == BattlefieldView.DirectorateMark) foeOnMap++;
        }
        Check(mineOnMap > 0, $"the minimap draws my own army in MY side's colour ({mineOnMap} dots)");
        Check(foeOnMap == 0,
              $"no dot wears the other side's mark while they are all in fog ({foeOnMap})");

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
            //
            // BOTH seats are driven to EXACTLY the same tick, and each is capped
            // rather than the loop exiting on the host's count alone. That was a
            // latent flaw in this test, not in the game: the seats advance
            // independently once their merged batches land, so stepping both and
            // then exiting on the host could leave them one tick apart - and the
            // hash comparison below would then compare two DIFFERENT ticks and
            // report a desync that had not happened. It passed locally every
            // time and failed the first time a loaded CI runner changed the
            // interleaving (60 vs 61).
            const int lanTicks = 60;
            int spins = 0;
            while ((host.CurrentTick < lanTicks || join.CurrentTick < lanTicks) && spins++ < 4000)
            {
                int before = host.CurrentTick + join.CurrentTick;
                if (host.CurrentTick < lanTicks) host.StepTicks(1);
                if (join.CurrentTick < lanTicks) join.StepTicks(1);
                // Yield when neither could advance. The merged batch arrives on
                // the client's reader THREAD, so a spin loop that never gives
                // the scheduler a chance simply burns its whole budget before a
                // single batch lands - which is what a first attempt did, and it
                // read exactly like a broken lockstep rather than an impatient
                // test. A real frame loop gets this for free by rendering.
                if (host.CurrentTick + join.CurrentTick == before)
                    System.Threading.Thread.Sleep(1);
            }
            Check(host.CurrentTick >= lanTicks, $"the host advanced under lockstep ({host.CurrentTick} ticks)");
            Check(join.CurrentTick == host.CurrentTick,
                  $"both seats advanced in lockstep ({host.CurrentTick} vs {join.CurrentTick})");
            Check(host.StateHash == join.StateHash,
                  $"the two seats hold IDENTICAL worlds (0x{host.StateHash:X16} vs 0x{join.StateHash:X16})");
            Check(!relay.DesyncDetected, "the relay saw no desync");

            // --- C7b-iv: the pause that must NOT pause ----------------------
            // _paused stops the accumulator drain, and the drain is the only
            // thing that submits this client's batch - so a LAN pause stops the
            // OTHER player's world too, with nothing on their screen to explain
            // it. Asserted through the real TogglePause, not a flag.
            host.TogglePause();
            int atPause = host.CurrentTick;
            int spun = 0;
            while (host.CurrentTick == atPause && spun++ < 2000)
            {
                host.StepTicks(1);
                join.StepTicks(1);
                if (host.CurrentTick == atPause) System.Threading.Thread.Sleep(1);
            }
            Check(host.PauseOpen, "the operations menu opens in a LAN match");
            Check(host.CurrentTick > atPause,
                  $"pausing does NOT stall the lockstep ({atPause} -> {host.CurrentTick})");
            host.ClosePause();

            // --- C7c: the joiner walks out --------------------------------
            // The last unexplained state in LAN. Lockstep starves when a player
            // goes - the relay never gets their batch, so it never broadcasts
            // another merged one - and the survivor's world stopped dead with
            // nothing on screen, because nothing had DESYNCED either. A game
            // that stops for no stated reason reads as a crash.
            //
            // Driven by disposing the joiner's real client, which is what
            // closing the window does to the socket.
            Check(!host.MatchNoticeVisible, "no notice while both commanders are present (the precondition)");
            joinClient.Dispose();
            int waitLeft = 0;
            while (!host.MatchNoticeVisible && waitLeft++ < 5000)
            {
                host.StepTicks(1);            // the drain keeps polling; it just never advances
                host.PumpFrameForTest();      // the notice is raised from the frame, not the tick
                System.Threading.Thread.Sleep(1);
            }
            Check(host.MatchNoticeVisible, "the survivor is TOLD the other commander left");
            Check(host.MatchNoticeText.Contains("LEFT"),
                  $"...and the notice says so plainly (\"{host.MatchNoticeText.Replace("\n", " / ")}\")");
            // The distinction that matters: a departure is not a desync, and
            // saying "you no longer share a world" to someone whose opponent
            // simply quit would be a lie about their match.
            Check(!host.MatchNoticeText.Contains("DESYNC"), "...and does NOT call it a desync");
            Check(!Ferrostorm.Client.NetSession.Desynced, "the session records a departure, not a divergence");

            host.QueueFree();
            join.QueueFree();
        }
        catch (System.Exception ex)
        {
            Check(false, $"the LAN match threw: {ex.Message}");
        }

        RunLobbyChecks();
    }

    /// <summary>
    /// C7b-iv acceptance: the REAL lobby, both ends, in this process.
    ///
    /// The lobby is deliberately not part of MainMenu, so it can be driven with
    /// no scene at all - which matters because the thing worth proving is not
    /// the buttons but the handshake behind them: that a host opens a port and
    /// blocks, that a joiner dialling that port lands on the opposite seat, and
    /// above all that the joiner ends up with THE HOST'S SETUP rather than its
    /// own menu's. Everything a real join does except the typing.
    /// </summary>
    private void RunLobbyChecks()
    {
        GD.Print("  --    LAN: the host and join lobby");

        // The codec first, on its own. A field the encoder writes and the
        // decoder does not read is a joiner-only divergence, and finding it here
        // is the difference between a one-line fix and a desync hunt.
        var original = new MatchSetup
        {
            MapPath = "data/maps/skirmish-04.fmap",
            MissionIndex = 0,
            AiPreset = 2,
            StartCredits = 12345,
            Seed = 987654321UL,
            Faction = 1,
            OppFaction = 0,
        };
        var round = MatchSetupBlob.Decode(MatchSetupBlob.Encode(original));
        Check(round.MapPath == original.MapPath && round.MissionIndex == original.MissionIndex
              && round.AiPreset == original.AiPreset && round.StartCredits == original.StartCredits
              && round.Seed == original.Seed && round.Faction == original.Faction
              && round.OppFaction == original.OppFaction,
              "every setup field survives the wire round trip");

        // A host running a build the joiner cannot read must be told so in the
        // lobby. The alternative is building a world from a misread blob and
        // discovering it as a desync at the first order.
        bool refusedEmpty = false;
        try { MatchSetupBlob.Decode(System.Array.Empty<byte>()); }
        catch (System.Exception) { refusedEmpty = true; }
        Check(refusedEmpty, "a setup blob that is absent is REFUSED, not guessed at");

        try
        {
            // The host's match is skirmish-04 with distinctive options, and the
            // joiner is never told any of it. If the joiner comes back holding
            // these values, they can only have arrived over the wire.
            var hosted = new MatchSetup
            {
                MapPath = "data/maps/skirmish-04.fmap",
                AiPreset = 1,
                StartCredits = 5000,
                Seed = 31337UL,
                Faction = 1,
                OppFaction = 0,
            };
            // Port 0 is unusable for a real lobby (nobody can dial an ephemeral
            // port) but it is exactly right here: a fixed port would make this
            // check fail against a stale relay left by an earlier run rather
            // than against anything it is testing.
            var host = LanLobby.Host(hosted, port: 0);
            // The host's relay must be listening before anything dials it, and
            // it binds on the connect thread. Waiting for the port is the
            // handshake's real precondition, so wait for it rather than sleeping
            // a guessed interval.
            int waited = 0;
            while (host.RelayPortForTest <= 0 && host.State == LanLobby.Phase.Connecting && waited++ < 5000)
                System.Threading.Thread.Sleep(1);
            Check(host.RelayPortForTest > 0, $"the host opened a lobby port ({host.RelayPortForTest})");
            Check(host.State == LanLobby.Phase.Connecting,
                  "the host WAITS rather than starting alone (nobody has joined yet)");

            var join = LanLobby.Join("127.0.0.1", host.RelayPortForTest);

            waited = 0;
            while ((host.State == LanLobby.Phase.Connecting || join.State == LanLobby.Phase.Connecting)
                   && waited++ < 15000)
                System.Threading.Thread.Sleep(1);

            Check(host.State == LanLobby.Phase.Ready, $"the host's lobby became ready ({host.Status})");
            Check(join.State == LanLobby.Phase.Ready, $"the join lobby became ready ({join.Status})");

            if (host.State == LanLobby.Phase.Ready && join.State == LanLobby.Phase.Ready)
            {
                Check(host.Seat == 0 && join.Seat == 1,
                      $"the relay seated them opposite (host {host.Seat}, joiner {join.Seat})");
                var got = join.Setup!;
                Check(got.MapPath == hosted.MapPath,
                      $"the joiner took the HOST'S map, never its own menu's (\"{got.MapPath}\")");
                Check(got.Seed == hosted.Seed && got.StartCredits == hosted.StartCredits
                      && got.Faction == hosted.Faction && got.OppFaction == hosted.OppFaction,
                      "the joiner took the host's seed, treasury and sides");
                // The claim that actually matters. Two worlds built independently
                // on two ends of a socket, identical before a single tick runs -
                // which is the precondition every later tick depends on.
                Check(host.Client!.World.ComputeStateHash() == join.Client!.World.ComputeStateHash(),
                      $"both lobbies built the IDENTICAL world before tick 0 "
                      + $"(0x{host.Client!.World.ComputeStateHash():X16})");
            }

            host.Cancel();
            join.Cancel();
        }
        catch (System.Exception ex)
        {
            Check(false, $"the lobby threw: {ex.Message}");
        }
    }
}
