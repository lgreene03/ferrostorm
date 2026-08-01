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
        // The verdict is an ABSOLUTE player id and the banner asked whether it
        // was zero, so at seat 1 it was exactly inverted: the LAN joiner who had
        // just won was shown DEFEAT and played the failure line, while the host
        // who lost was congratulated. The last thing a match says, saying the
        // opposite of what happened. Two directions, because a banner that
        // inverts passes either check alone.
        //
        // P7-8a drives the two through the paths that now genuinely differ. A
        // declared winner ends the match; an elimination only ends it when the
        // eliminated seat is MINE, because in a free-for-all somebody else going
        // out leaves the survivors still fighting.
        _game.DeclareWinnerForTest(_game.LocalPlayerId);       // the sim says I won
        Check(_game.BannerVisibleForTest, "the match banner is raised when a winner is declared");
        Check(_game.BannerTextForTest.Contains("VICTORY"),
              $"being DECLARED the winner reads as VICTORY at seat 1 (\"{_game.BannerTextForTest.Split('\n')[0]}\")");
        _game.ResetVictoryForTest();
        _game.EliminateForTest(_game.LocalPlayerId);           // I am out: I lost
        Check(_game.BannerTextForTest.Contains("DEFEAT"),
              $"being eliminated MYSELF reads as DEFEAT (\"{_game.BannerTextForTest.Split('\n')[0]}\")");
        _game.ResetVictoryForTest();
        // ...and somebody ELSE being eliminated is news, not a verdict: with
        // three or more seats the match carries on, and this is the exact site
        // that used to end it and invent a winner by flipping a seat number.
        _game.EliminateForTest(_game.EnemyPlayerId);
        Check(!_game.BannerVisibleForTest,
              "another commander's elimination raises no banner - the match is not over");
        Check(_game.DeclaredWinnerForTest < 0,
              $"...and invents no winner (winner is {_game.DeclaredWinnerForTest})");
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

        // --- DR-20: the capture alert decides correctly, fog included --------
        // GameEventType.Captured was raised by the sim and consumed by nothing,
        // so an outpost changing hands - the entire point of ADR-021 - happened
        // in silence. All four outcomes are checked here rather than staged
        // through a real engineer walk, because the one that matters most is
        // the SILENT one and arranging vision state to prove a negative
        // end-to-end would test the fixture as much as the rule.
        int me = _game.LocalPlayerId, foe = _game.EnemyPlayerId;
        Check(_game.CaptureAlertFor(me, -1, false) == SkirmishLive.CaptureAlertKind.Gained,
              "capturing a neutral outpost tells you so, even with no vision of the cell");
        Check(_game.CaptureAlertFor(foe, me, false) == SkirmishLive.CaptureAlertKind.Lost,
              "LOSING a structure to capture always tells you, vision or not");
        Check(_game.CaptureAlertFor(foe, -1, true) == SkirmishLive.CaptureAlertKind.Witnessed,
              "an enemy taking a neutral outpost you can SEE is reported");
        Check(_game.CaptureAlertFor(foe, -1, false) == SkirmishLive.CaptureAlertKind.None,
              "...and the same capture inside the shroud is SILENT (it would be a maphack)");
        // The re-capture case, which is why Gained is tested before Lost: a
        // structure you once owned and have just taken back is a gain. Ordered
        // the other way this would have reported a loss at the moment of
        // winning it, and no end-to-end check would plausibly have caught it.
        Check(_game.CaptureAlertFor(me, me, true) == SkirmishLive.CaptureAlertKind.Gained,
              "re-taking a structure that was yours reads as a GAIN, not a loss");

        // --- P7-7a: a robbery is not a capture, as far as the ALERT ----------
        // The Infiltrator shipped in P7-7 raising GameEventType.Captured, which
        // this client reads as an ownership change, so being robbed announced
        // "STRUCTURE LOST TO CAPTURE" about a building the player still owned.
        // Every sim-side stage of infiltratorgate passed throughout, because
        // none of them looked at the event. Tested as the pure decision, the
        // same way the four capture outcomes above are, and for the same
        // reason: the case that matters is a NEGATIVE one.
        Check(_game.RobberyAlertFor(me, foe) == SkirmishLive.RobberyAlertKind.Robbed,
              "being robbed tells you so - and says CREDITS, not a structure you still own");
        Check(_game.RobberyAlertFor(foe, me) == SkirmishLive.RobberyAlertKind.Seized,
              "robbing someone confirms the haul to the thief");
        Check(_game.RobberyAlertFor(foe, foe) == SkirmishLive.RobberyAlertKind.None,
              "a robbery between two other commanders is not your alert");

        // --- P7-8d: the MAP decides how many seats a skirmish is played with --
        // Derived rather than stored, which is what keeps it out of the sidecar:
        // a save or replay written before multi-seat existed rebuilds the same
        // world with no format version and no migration.
        {
            var twoStart = MapData.Load(GameFiles.Abs("data/maps/skirmish-01.fmap"));
            var fourStart = MapData.Load(GameFiles.Abs("data/maps/skirmish-09.fmap"));
            Check(SkirmishLive.SeatsFor(twoStart) == 2,
                  "a two-start map still seats exactly two, so no existing match changes");
            Check(SkirmishLive.SeatsFor(fourStart) == 4,
                  "skirmish-09 seats four, which is the whole point of it");

            // The world the menu would actually build, not a constructed one.
            var setup = MatchConfig.CurrentSetup();
            setup.MapPath = "data/maps/skirmish-09.fmap";
            setup.MissionIndex = 0;
            var w4 = SkirmishLive.BuildStartingWorld(setup, fourStart, out _);
            Check(w4.PlayerCount == 4, "the built world has four seats");
            int seated = 0;
            for (int i = 0; i < w4.EntityCount; i++)
                if (w4.Entities[i].Alive && w4.Entities[i].Kind == EntityKind.ConstructionYard) seated++;
            Check(seated == 4, $"all four seats get a construction yard (saw {seated})");
            // The alternating rule: not three of a kind.
            bool mixed = false;
            for (int p = 1; p < 4; p++) if (w4.FactionOf(p) != w4.FactionOf(1)) mixed = true;
            Check(mixed, "the opponents do not all fly the same colours");

            // GDD s9 asks for "1-7 opponents", which is a CHOICE. P7-8d filled
            // every seat the map declared, so a duel on a four-start map was
            // unreachable. The MAP remains the ceiling and always wins, so a
            // corrupt sidecar cannot ask for a seat the map cannot place.
            setup.Seats = 2;
            Check(SkirmishLive.SeatsFor(fourStart, setup) == 2,
                  "a player may ask for a DUEL on a four-start map");
            setup.Seats = 3;
            Check(SkirmishLive.SeatsFor(fourStart, setup) == 3,
                  "...or for three seats, leaving the fourth start unused");
            setup.Seats = 9;
            Check(SkirmishLive.SeatsFor(fourStart, setup) == 4,
                  "a setup asking for more seats than the map declares is CLAMPED, not refused");
            setup.Seats = 4;
            Check(SkirmishLive.SeatsFor(twoStart, setup) == 2,
                  "and the two-start map is still two, whatever the sidecar says");
            setup.Seats = 0;
            Check(SkirmishLive.SeatsFor(fourStart, setup) == 4,
                  "zero seats means fill the map, which is what every pre-P7-8e sidecar carries");

            // The choice has to reach the WORLD, not just the helper.
            setup.Seats = 2;
            var duel = SkirmishLive.BuildStartingWorld(setup, fourStart, out _);
            Check(duel.PlayerCount == 2, "a two-seat setup on a four-start map builds a two-seat world");
            int yards = 0;
            for (int i = 0; i < duel.EntityCount; i++)
                if (duel.Entities[i].Alive && duel.Entities[i].Kind == EntityKind.ConstructionYard) yards++;
            Check(yards == 2, $"...and places exactly two construction yards (saw {yards})");
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

        // --- The selection layer (doc 27 DR-05 and DR-06) --------------------
        // All driven through the REAL input path with the LIVE bindings.

        // Select-army takes the fighters and refuses the workers. Both
        // directions, because an army key that also grabs the harvester is how
        // a harvester walks into a firefight.
        _game.ClearSelectionForTest();
        _game.PressKey(Settings.BindOf("select_all_army"));
        Check(_game.SelectionCount >= 3, $"the army key selects the opening squads ({_game.SelectionCount})");
        int armyHarv = _game.FindEntity(EntityKind.Harvester, _game.LocalPlayerId);
        Check(armyHarv >= 0 && !_game.IsSelected(armyHarv), "...and does NOT select the harvester");

        // The idle-harvester key selects exactly the idle one and none when
        // none is idle. The world is restored afterwards.
        _game.ClearSelectionForTest();
        _game.PressKey(Settings.BindOf("idle_harvester"));
        Check(_game.SelectionCount == 1 && _game.IsSelected(armyHarv),
              "the idle key finds the idle harvester");
        _game.ClearSelectionForTest();
        _game.SetHStateForTest(armyHarv, HarvestState.Loading);
        _game.PressKey(Settings.BindOf("idle_harvester"));
        Check(_game.SelectionCount == 0, "...and finds NOTHING when no harvester is idle");
        _game.SetHStateForTest(armyHarv, HarvestState.Idle);   // hand the world back

        // Double-click type-select: all rifles on screen, nothing else. The
        // camera is parked over them first, because on-screen is the rule.
        // The exemplar is taken from the army selection itself rather than by
        // catalogue type, because the OPENING-HAND squads are spawned with
        // UnitType 0 - the map spawner never sets a type (the DR-19 family).
        // The gesture groups by type either way; the check must not assume a
        // type the data does not carry.
        _game.PressKey(Settings.BindOf("select_all_army"));
        var (sqx, sqz) = _game.FirstSelectedPosition();
        _game.FocusCameraOn(sqx, sqz, 22f);
        _game.ClearSelectionForTest();
        _game.PressDoubleClick(_game.ScreenOf(sqx, sqz));
        Check(_game.SelectionCount >= 3,
              $"double-click selects every squad of the type on screen ({_game.SelectionCount})");
        Check(!_game.IsSelected(armyHarv), "...and type-select does not take the harvester");

        // Group 0, assign AND recall, through a synthetic ctrl press - which
        // only works because the assign path reads the modifier off the EVENT.
        _game.PressKey(Settings.BindOf("select_all_army"));
        int armyCount = _game.SelectionCount;
        _game.PressKeyWithCtrl(Key.Key0);            // assign to group 0
        _game.ClearSelectionForTest();
        _game.PressKey(Key.Key0);                    // recall
        Check(_game.SelectionCount == armyCount && armyCount > 0,
              $"group 0 assigns with ctrl and recalls plain ({_game.SelectionCount} of {armyCount})");
        _game.ClearSelectionForTest();

        // --- Camera bookmarks (doc 27 DR-07, the GDD s10 promise) ------------
        // Assign with ctrl+F1 (the modifier ON the event), move away, recall.
        // Asserted against the camera's own RETAINED target, never its animated
        // position - a same-frame check cannot watch a glide.
        _game.FocusCameraOn(20f, 30f, 22f);
        _game.PressKeyWithCtrl(Key.F1);              // assign bookmark 1 here
        _game.FocusCameraOn(70f, 10f, 22f);          // wander off
        _game.PressKey(Key.F1);                      // recall
        var bmk = _game.CameraGroundTargetForTest;
        Check(Mathf.Abs(bmk.X - 20f) < 0.5f && Mathf.Abs(bmk.Z - 30f) < 0.5f,
              $"F1 recalls the bookmarked ground point ({bmk.X:0.0},{bmk.Z:0.0})");
        // The control: an UNASSIGNED bookmark does nothing rather than snapping
        // to the origin.
        _game.PressKey(Key.F2);
        var still = _game.CameraGroundTargetForTest;
        Check(Mathf.Abs(still.X - 20f) < 0.5f && Mathf.Abs(still.Z - 30f) < 0.5f,
              "an unassigned bookmark key does NOTHING");

        // --- Minimap ping (doc 27 DR-09) -------------------------------------
        // The minimap swallows every click while the radar is dark, pings
        // included, so a radar is stood up first and the blackout lifting is
        // the stated precondition.
        var (pyx, pyy) = _game.CellOfForTest(_game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId));
        _game.SpawnPowerPlantForTest(pyx - 6, pyy - 6);
        _game.SpawnRadarForTest(pyx - 6, pyy - 3);
        _game.StepOneTick();   // the radar gate lives in AfterTicks, the FRAME half - StepTicks alone never runs it
        _game.StepOneTick();
        Check(_game.MinimapRadarShown, "the radar lifts the blackout (the precondition)");
        int pingsBefore = _game.MinimapView.PingCountForTest;
        var mmCentre = _game.MinimapView.Size / 2f;
        _game.MinimapView._GuiInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, AltPressed = true, Position = mmCentre });
        Check(_game.MinimapView.PingCountForTest == pingsBefore + 1,
              $"alt-click on the minimap drops a ping ({_game.MinimapView.PingCountForTest})");
        // The control: a PLAIN click still navigates and does not ping.
        var beforeNav = _game.CameraGroundTargetForTest;
        _game.MinimapView._GuiInput(new InputEventMouseButton
        { ButtonIndex = MouseButton.Left, Pressed = true, Position = mmCentre });
        Check(_game.MinimapView.PingCountForTest == pingsBefore + 1,
              "a plain click does NOT ping");
        Check((_game.CameraGroundTargetForTest - beforeNav).Length() > 1f,
              "...it still navigates the camera, as it always has");

        // --- Grid build hotkeys (doc 27 DR-08, the last tier-2 item) ---------
        // Tab cycles the sidebar tab; alt+digit queues the Nth visible item of
        // the active tab - the digit keys' third meaning, all off the event.
        int tabBefore = _game.SidebarView.CurrentTabForTest;
        _game.PressKey(Settings.BindOf("sidebar_tab"));
        // Tab COUNT is read, not written: this check hardcoded 4 and broke the
        // day an AIRCRAFT tab was added, which is a test failing for a reason
        // that is not a defect. What it means to assert is "one press advances
        // one tab and a full lap returns", and that is true at any count.
        int tabs = Sidebar.TabTitleCount;
        Check(_game.SidebarView.CurrentTabForTest == (tabBefore + 1) % tabs,
              $"Tab cycles the sidebar tab ({tabBefore} -> {_game.SidebarView.CurrentTabForTest})");
        for (int t = 0; t < tabs - 1; t++) _game.PressKey(Settings.BindOf("sidebar_tab"));
        Check(_game.SidebarView.CurrentTabForTest == tabBefore,
              $"...and wraps back around after {tabs} presses");

        // Alt+1 on the BUILDINGS tab queues slot one, the power plant, through
        // the button's own Pressed signal - the same handler the mouse runs.
        while (_game.SidebarView.CurrentTabForTest != Sidebar.TabBuildings)
            _game.PressKey(Settings.BindOf("sidebar_tab"));
        int gridYard = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        int qBefore = _game.QueuedAt(gridYard);
        _game.PressKeyWithAlt(Key.Key1);
        _game.StepTicks(2);
        Check(_game.QueuedAt(gridYard) == qBefore + 1,
              $"alt+1 queues the first BUILDINGS item at the yard ({_game.QueuedAt(gridYard)})");
        // Hand the world back: cancel what the hotkey queued, refund exact.
        _game.CancelStructure(1);
        _game.StepTicks(2);
        Check(_game.QueuedAt(gridYard) == qBefore, "...and the check hands the queue back");
        // The control: an alt+digit past the tab's visible slots does nothing.
        _game.PressKeyWithAlt(Key.Key9);
        _game.StepTicks(2);
        Check(_game.QueuedAt(gridYard) == qBefore, "an empty slot number queues NOTHING");

        // --- Cancel must not destroy a building you did not click ------------
        // The sim's lane branch checks `cl.Ready != 0` BEFORE it looks at the
        // index, so a cancel aimed at a QUEUED item while a DIFFERENT structure
        // sits finished in lane 2 destroyed the finished one and left the queued
        // one alone. Lane 1 has always guarded this; lane 2 did not.
        //
        // LAST in the run, and deliberately: it grants credits and stands a
        // building, and a check that leaves the world changed breaks the ones
        // after it - which is exactly what happened on the first attempt.
        int cyId = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        if (cyId >= 0)
        {
            // A built plant opens the tech tree. Without it the seat may queue
            // NOTHING but a plant, so the two-lane state cannot be reached at
            // all - the reason this check was missing.
            var (cyx, cyy) = _game.CellOfForTest(cyId);
            _game.SpawnPowerPlantForTest(cyx + 6, cyy + 6);
            _game.GrantCreditsForTest(40000);
            _game.QueueStructure(3);              // refinery: slow, holds lane 1
            _game.StepTicks(2);
            _game.QueueStructure(5);              // turret: overflows to lane 2
            _game.StepTicks(2);
            _game.QueueStructure(1);              // plant: queues BEHIND it in lane 2
            for (int i = 0; i < 900 && _game.LaneReadyForTest == 0; i++) _game.StepTicks(1);

            bool plantQueued = false;
            foreach (int q in _game.LaneQueueForTest) if (q == 1) plantQueued = true;
            Check(_game.LaneReadyForTest == 5,
                  $"lane 2 holds a FINISHED turret (ready={_game.LaneReadyForTest})");
            Check(plantQueued, "...with a power plant queued BEHIND it (the precondition)");
            if (_game.LaneReadyForTest == 5 && plantQueued)
            {
                long creditsBefore = _game.CreditsNow;
                _game.CancelStructure(1);         // right-click the QUEUED PLANT
                _game.StepTicks(2);
                Check(_game.LaneReadyForTest == 5,
                      $"cancelling the QUEUED plant leaves the finished turret alone (ready={_game.LaneReadyForTest})");
                // NOT an equality: lane 1's refinery is still draining
                // pay-as-you-build, so the treasury legitimately FALLS during
                // these ticks. The defect ADDS credits - it refunds a turret the
                // player never cancelled - so "did not rise" is the property
                // that actually separates the two, and an equality here failed
                // for the wrong reason.
                Check(_game.CreditsNow <= creditsBefore,
                      $"...and refunds NOTHING for a building never cancelled (delta {_game.CreditsNow - creditsBefore}, a turret would be +{_game.StructCostOf(5)})");
            }
        }

        // --- The sidebar offers every unit the CATALOGUE registers ------------
        // LAST, and after the block above rather than before it, because it
        // stands a factory and queues at it: a check that leaves the world
        // changed breaks the ones after it, so it goes where there are none.
        //
        // The unit list used to be a hand-kept table in Sidebar.cs and had
        // fallen SEVEN units behind the sim - the transport, the flak track, the
        // infiltrator, the saboteur and both heroes were registered, priced and
        // unbuildable, because a unit reached the panel only if whoever added it
        // remembered that file. Both halves are pinned here: that deriving the
        // list is INERT for the thirteen that already had a button, and that it
        // actually REACHES the ones that did not.
        var sb = _game.SidebarView;
        int myFaction = _game.FactionOf(_game.LocalPlayerId);

        // 1. Inertness. The label is derived from the /data id now, so these
        //    read the four id shapes that could break the derivation: two
        //    words, one word, an initialism, and a three-word id.
        Check(sb.UnitButtonText(2).StartsWith("RIFLE SQUAD  "),
              $"the derived label matches the old table for com_rifle_squad (\"{sb.UnitButtonText(2)}\")");
        Check(sb.UnitButtonText(4).StartsWith("HARVESTER  "),
              $"...and for com_harvester (\"{sb.UnitButtonText(4)}\")");
        Check(sb.UnitButtonText(7).StartsWith("MCV  "),
              $"...and for com_mcv, where the whole name is the initialism (\"{sb.UnitButtonText(7)}\")");
        Check(sb.UnitButtonText(World.RepairVehicleType).StartsWith("REPAIR VEHICLE  "),
              $"...and for com_repair_vehicle (\"{sb.UnitButtonText(World.RepairVehicleType)}\")");
        // Tab routing is unchanged too: produced_at still decides, so the
        // infantry stay in INFANTRY and everything else in VEHICLES.
        Check(sb.TabOfUnit(2) == Sidebar.TabInfantry, "a barracks unit still lands in INFANTRY");
        Check(sb.TabOfUnit(1) == Sidebar.TabVehicles, "a factory unit still lands in VEHICLES");

        // 2. The count, measured rather than assumed, plus the PROPERTY that
        //    explains the gap. The third check is the one that never needs
        //    editing: whatever the totals become, no unit may be missing for
        //    any reason but a producer this panel has no tab for.
        int registered = 0, buttoned = 0, offTab = 0;
        foreach (int t in _game.LiveWorld.UnitTypeIds())
        {
            registered++;
            if (sb.UnitButtonText(t).Length > 0) buttoned++;
            else if (_game.LiveWorld.GetUnitType(t).ProducedAt is not (World.FactoryStructType
                     or World.BarracksStructType or World.AirfieldStructType))
                offTab++;
        }
        Check(registered == 20, $"the catalogue registers {registered} unit types");
        // EVERY registered unit now has a button. This asserted 19 while the
        // Strike Flyer had no tab, and 19 was never the goal - it was the
        // symptom of World.IsProducer omitting the Airfield, so the aircraft
        // was unbuildable and a button for it would have been a lie. Both are
        // fixed, so the honest assertion is the whole catalogue.
        Check(buttoned == registered && sb.UnitButtonCount == buttoned,
              $"...and ALL {buttoned} carry a sidebar button (the hand-kept table stopped at 13, and the "
              + "Strike Flyer had no producer until the Airfield joined IsProducer)");
        Check(buttoned + offTab == registered,
              $"every unit without a button is one whose PRODUCER this panel has no tab for ({offTab}), "
              + "never one somebody forgot to list");

        // 3. The faction gate still binds, on every button rather than on the
        //    two it was written for. The sim refuses a Produce whose unit is
        //    neither common nor this seat's side; the panel must hide exactly
        //    those and no others.
        int gateWrong = 0;
        foreach (int t in _game.LiveWorld.UnitTypeIds())
        {
            if (sb.UnitButtonText(t).Length == 0) continue;
            int f = _game.LiveWorld.GetUnitType(t).Faction;
            if (sb.UnitFixedGateForTest(t) != (f == World.FactionCommon || f == myFaction)) gateWrong++;
        }
        Check(gateWrong == 0,
              $"every unit button's faction gate agrees with the sim's own Produce refusal ({gateWrong} disagree)");

        // 4. Reachability, which is the claim worth making: the flak track (type
        //    16, ADR-028's answer to the air layer) had NO button at all before
        //    this change. Its prerequisite is the radar uplink stood earlier, so
        //    a factory is the only thing missing.
        int fyard = _game.FindEntity(EntityKind.ConstructionYard, _game.LocalPlayerId);
        if (fyard >= 0)
        {
            var (fx, fy) = _game.CellOfForTest(fyard);
            int factory = _game.SpawnFactoryForTest(fx + 8, fy - 8);
            // TWO ticks, for the reason the radar check above needs two: the
            // sidebar refresh lives in the FRAME half, and the producer it reads
            // is found through the client's VIEW, which is a tick behind the
            // world a spawn was written into.
            _game.StepOneTick();
            _game.StepOneTick();
            Check(sb.UnitButtonText(16).Length > 0, "the flak track HAS a button (it had none before)");
            Check(factory >= 0 && _game.ProducerForUnitForTest(16) == factory,
                  $"...and the client routes a flak-track order to the factory just stood ({_game.ProducerForUnitForTest(16)} vs {factory})");
            Check(_game.SidebarUnitVisible(16), "...and it is on offer once a factory stands");
            int qFlak = _game.QueueLengthOf(factory);
            Check(sb.PressUnitButton(16), "...and the button can actually be pressed");
            _game.StepTicks(2);
            Check(_game.QueueLengthOf(factory) == qFlak + 1,
                  $"...and the SIM accepted what it sent ({_game.QueueLengthOf(factory)} queued at the factory)");

            // The control, and it is what separates "the gate binds" from "the
            // panel shows everything": a factory unit of the OTHER side must be
            // absent here AND refused by the sim if the command is sent anyway.
            // Chosen from the catalogue at run time, so the check does not care
            // which seat it is sitting in.
            int foreign = -1;
            foreach (int t in _game.LiveWorld.UnitTypeIds())
            {
                var d = _game.LiveWorld.GetUnitType(t);
                if (d.ProducedAt != World.FactoryStructType) continue;
                if (d.Faction == World.FactionCommon || d.Faction == myFaction) continue;
                foreign = t;
                break;
            }
            Check(foreign > 0, $"the catalogue carries a factory unit of the other side to control against (type {foreign})");
            if (foreign > 0)
            {
                Check(!_game.SidebarUnitVisible(foreign), $"unit type {foreign} is ABSENT at this seat, not merely greyed");
                Check(!sb.PressUnitButton(foreign), "...so its button cannot be pressed either");
                int qForeign = _game.QueueLengthOf(factory);
                _game.QueueUnit(foreign);          // the command by hand, past the panel
                _game.StepTicks(2);
                Check(_game.QueueLengthOf(factory) == qForeign,
                      "...and the sim refuses it even when the order is sent past the panel");
            }
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
        RunDifficultyChecks();
    }

    /// <summary>
    /// DR-14b acceptance: the ladder a player can now actually reach (doc 28).
    ///
    /// The checks that matter here are the BACKWARD ones. A difficulty field is
    /// easy to add and easy to get subtly wrong in a way no fresh match ever
    /// shows: a sidecar written before the field existed describes a match
    /// played at Normal, and if it decodes to enum-zero instead then every old
    /// save and replay silently resumes against an EASY commander and a replay
    /// reports DIVERGED with nothing in the diff to explain it. That is the
    /// same shape as the faction-default trap TICKET-P6-FACTION-01 documented,
    /// which is why it is checked rather than trusted.
    /// </summary>
    private void RunDifficultyChecks()
    {
        GD.Print("  --    DR-14b: the difficulty ladder");

        // 1. The legacy sidecar. Written by hand with the field ABSENT, which
        //    is precisely what every file on disk today looks like.
        string legacy = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ferrostorm-verify-legacy.json");
        System.IO.File.WriteAllText(legacy,
            "{\"map\":\"skirmish-01\",\"map_path\":\"data/maps/skirmish-01.fmap\",\"mission\":0,"
            + "\"tick\":120,\"saved_at\":\"\",\"credits\":8000,\"ai_preset\":0,"
            + "\"start_credits\":8000,\"seed\":2026,\"faction\":0,\"opp_faction\":0}");
        var old = MatchMeta.Read(legacy);
        Check(old != null && old.Setup.AiDifficulty == 1,
              "a sidecar written before the ladder decodes to NORMAL, not to enum-zero");
        System.IO.File.Delete(legacy);

        // 2. The round trip through the sidecar, on a rung that is not the
        //    default - a field that is never written would still pass at 1.
        string sidecar = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ferrostorm-verify-diff.json");
        MatchMeta.For(new MatchSetup { AiDifficulty = 3 }, tick: 7, credits: 99).Write(sidecar);
        var back = MatchMeta.Read(sidecar);
        Check(back != null && back.Setup.AiDifficulty == 3,
              "a chosen rung survives the sidecar round trip");
        System.IO.File.Delete(sidecar);

        // 3. The handicap is Brutal's alone, and it is the SIZE the menu item
        //    advertises. A label that promised 5000 while the code granted
        //    something else would be a lie GDD line 76 specifically forbids.
        Check(SkirmishAI.StartingCreditHandicap(AiDifficulty.Brutal) == 5000,
              "BRUTAL grants exactly the 5000 credits its menu label declares");
        Check(SkirmishAI.StartingCreditHandicap(AiDifficulty.Easy) == 0
              && SkirmishAI.StartingCreditHandicap(AiDifficulty.Normal) == 0
              && SkirmishAI.StartingCreditHandicap(AiDifficulty.Hard) == 0,
              "no rung below BRUTAL is given a credit");

        // 4. The rung actually reaches the commander, asserted on the beat
        //    itself. Every one of these is built through the SAME factory the
        //    battle scene calls, so a factory overload that accepted a rung and
        //    then dropped it on the floor - the whole failure mode this wave
        //    could plausibly ship - fails right here.
        //
        //    An earlier version of this check inferred the beat from how many
        //    commands each rung issued. It measured zero at every rung, because
        //    the world it built left the AI unable to afford anything, and no
        //    beat produced a command to count. The lesson is recorded rather
        //    than just fixed: infer nothing you can read directly.
        Check(SkirmishAI.Standard(1, AiDifficulty.Easy).DecisionBeat == 30,
              "EASY reaches the commander as a 30-tick beat (half speed)");
        Check(SkirmishAI.Standard(1, AiDifficulty.Normal).DecisionBeat == 15,
              "NORMAL is the 15-tick beat the game has always shipped");
        Check(SkirmishAI.Standard(1, AiDifficulty.Hard).DecisionBeat == 15,
              "HARD shares NORMAL's beat: it is stronger by macro, not by speed");
        Check(SkirmishAI.Standard(1, AiDifficulty.Brutal).DecisionBeat == 10,
              "BRUTAL reaches the commander as a 10-tick beat");
        // The default overload - what every pre-ladder caller compiles to - must
        // still be Normal, or the ladder would have quietly moved the goldens.
        Check(SkirmishAI.Standard(1).DecisionBeat == 15
              && SkirmishAI.Rusher(1).DecisionBeat == 15
              && SkirmishAI.Turtle(1).DecisionBeat == 15,
              "a personality asked for WITHOUT a rung is still NORMAL (the identity rung)");
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
            AiDifficulty = 3,          // DR-14b: Brutal, the rung that carries a handicap
            StartCredits = 12345,
            Seed = 987654321UL,
            Faction = 1,
            OppFaction = 0,
            Seats = 3,                 // P7-8f: a host choice, not the map's ceiling
        };
        var round = MatchSetupBlob.Decode(MatchSetupBlob.Encode(original));
        Check(round.MapPath == original.MapPath && round.MissionIndex == original.MissionIndex
              && round.AiPreset == original.AiPreset && round.StartCredits == original.StartCredits
              && round.Seed == original.Seed && round.Faction == original.Faction
              && round.OppFaction == original.OppFaction,
              "every setup field survives the wire round trip");
        // DR-14b, asserted SEPARATELY rather than folded into the line above: a
        // new field bolted into an existing conjunction is exactly how a field
        // the encoder writes and the decoder never reads slips through, because
        // one true clause among seven reads as green. This one fails alone.
        Check(round.AiDifficulty == original.AiDifficulty,
              "the difficulty rung survives the wire round trip");
        // P7-8f, asserted alone for the same reason. Zero is the value this field
        // held for every joiner before it travelled, and SeatsFor reads zero as
        // "fill the map": on a four-start map a host asking for two seats would
        // have built a two-seat world against the joiner's four-seat one, which is
        // two different worlds at tick 0 rather than a desync anyone could trace.
        Check(round.Seats == original.Seats,
              $"the seat count survives the wire round trip (came back {round.Seats}, matching the host's {original.Seats})");

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

            // --- ADR-033: the same claim on a FOUR-seat map ------------------
            // The stage above uses a two-start map, where the seat count is 2 on
            // both sides whatever either believes, so it cannot see a peer that
            // DISAGREES about how many seats exist. That is precisely what
            // multi-seat LAN introduces, and it is not a desync at the first
            // order: it is two peers that never shared tick 0.
            var hosted4 = new MatchSetup
            {
                MapPath = "data/maps/skirmish-09.fmap",
                AiPreset = 1,
                StartCredits = 5000,
                Seed = 31337UL,
                Faction = 1,
                OppFaction = 0,
                Seats = 4,
            };
            var host4 = LanLobby.Host(hosted4, port: 0);
            int waited4 = 0;
            while (host4.RelayPortForTest <= 0 && host4.State == LanLobby.Phase.Connecting && waited4++ < 5000)
                System.Threading.Thread.Sleep(1);
            var join4 = LanLobby.Join("127.0.0.1", host4.RelayPortForTest);
            waited4 = 0;
            while ((host4.State == LanLobby.Phase.Connecting || join4.State == LanLobby.Phase.Connecting)
                   && waited4++ < 5000)
                System.Threading.Thread.Sleep(1);
            Check(host4.State == LanLobby.Phase.Ready && join4.State == LanLobby.Phase.Ready,
                  $"a four-seat map is no longer refused in LAN (host {host4.Status}, join {join4.Status})");
            if (host4.State == LanLobby.Phase.Ready && join4.State == LanLobby.Phase.Ready)
            {
                Check(join4.Setup!.Seats == hosted4.Seats,
                      $"the joiner took the host's SEAT COUNT ({join4.Setup!.Seats}), which the blob "
                      + "did not carry until ADR-033 and which decoded as zero, meaning fill the map");
                Check(host4.Client!.World.PlayerCount == 4 && join4.Client!.World.PlayerCount == 4,
                      $"both peers built a FOUR-seat world (host {host4.Client!.World.PlayerCount}, "
                      + $"join {join4.Client!.World.PlayerCount})");
                Check(host4.Client!.World.ComputeStateHash() == join4.Client!.World.ComputeStateHash(),
                      $"and the two four-seat worlds are identical before tick 0 "
                      + $"(0x{host4.Client!.World.ComputeStateHash():X16})");
            }
            host4.Cancel();
            join4.Cancel();

            // The commanded-seat rule, asserted from the seat this harness
            // actually drives. The old rule was "every seat that is not the
            // local one", so read from SEAT 1 it would have returned seat 0 -
            // the human on the other end of the socket - and handed Brutal's
            // handicap to a person. Peer-independence is the property, and this
            // is the seat where its absence would show.
            var commanded = SkirmishLive.LanCommandedSeats(4);
            Check(commanded.Count == 2 && commanded[0] == 2 && commanded[1] == 3,
                  $"LAN commands seats 2 and 3 on a four-seat map, never a human seat "
                  + $"(got [{string.Join(",", commanded)}] while sitting in seat {_game.LocalPlayerId})");
            Check(!commanded.Contains(_game.LocalPlayerId),
                  "...and never the seat this peer is sitting in");
            Check(SkirmishLive.LanCommandedSeats(2).Count == 0,
                  "a two-seat LAN match commands nothing, which is every match before ADR-033");
        }
        catch (System.Exception ex)
        {
            Check(false, $"the lobby threw: {ex.Message}");
        }
    }
}
