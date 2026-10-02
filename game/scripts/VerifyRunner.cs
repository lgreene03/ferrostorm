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
        // P7-13: THE TWO SEATS MUST HOLD DIFFERENT FACTIONS, and this was never
        // set, so both defaulted to 0.
        //
        // Every faction-gate check in this harness was therefore VACUOUS - the
        // unit one that has shipped since TICKET-P6-FACTION-01 as much as the
        // building one added beside it. With both seats Directorate, a gate that
        // reads FactionOf(0) and a gate that reads FactionOf(LocalPlayerId)
        // return the same answer, so a rule keyed on the WRONG SEAT reads
        // correct. Proved by breaking the rule that way and watching every check
        // still pass.
        //
        // That is this harness's own defect shape turned on itself: right at
        // seat 0 by luck, in the thing whose job is to catch exactly that.
        // MEASURED which way round these land rather than assumed: this gives
        // seat 0 the Sodality and seat 1 (the joiner, the seat this harness
        // drives) the Directorate. Which seat gets which does not matter; that
        // they DIFFER is the whole point.
        MatchConfig.Faction = 1;
        MatchConfig.OppositionFaction = 0;

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

        // --- P7-32: a faction's building renders as ITS OWN --------------------
        // The model library resolved structures by EntityKind alone, so two
        // faction variants sharing a kind were indistinguishable at the last
        // step of the pipeline. This is the client harness's own class of
        // defect: everything upstream was right and the picture was wrong.
        //
        // THE CONTROL FIRST. Both superweapons must share a kind, or the rest of
        // this proves nothing - it would be asserting a distinction the sim had
        // already made for us.
        // A bare World is enough: this asks the CATALOGUE what kind each
        // building is, not what any match is doing.
        var cat = new World(1, 8, 8, players: 2);
        World.StructureTypeDef _catFor(int t) => cat.GetStructureType(t);
        var dirSuper = cat.GetStructureType(6);
        var sodSuper = cat.GetStructureType(22);
        Check(dirSuper.Kind == sodSuper.Kind,
              $"the two superweapons SHARE a kind ({dirSuper.Kind}), which is why kind alone cannot tell them apart");
        Check(ModelLibrary.NameForTest((int)dirSuper.Kind, 0, 6) == "dir_superweapon",
              "the Directorate superweapon renders as the orbital cannon");
        Check(ModelLibrary.NameForTest((int)sodSuper.Kind, 0, 22) == "sod_seismic_charge",
              "and the SODALITY superweapon renders as its own seismic charge, not its enemy's cannon");
        // The Sodality generator: a mesh P7-31 built that nothing could reach.
        var sodGen = cat.GetStructureType(20);
        var dirPlant = cat.GetStructureType(1);
        Check(sodGen.Kind == dirPlant.Kind,
              $"the two power buildings also share a kind ({sodGen.Kind})");
        Check(ModelLibrary.NameForTest((int)sodGen.Kind, 0, 20) == "sod_generator",
              "the Sodality generator reaches its own mesh at last");
        Check(ModelLibrary.NameForTest((int)dirPlant.Kind, 0, 1) == "com_power_plant",
              "and the Directorate plant is unmoved, so the fallback still works");
        // P7-33: the SHROUD NEST is the second faction variant that can only be
        // reached by struct type - it shares EntityKind.Emplacement with the
        // common one, exactly as the superweapons share theirs.
        var sodNest = _catFor(18);
        var comEmp = _catFor(15);
        Check(sodNest.Kind == comEmp.Kind,
              $"the Shroud Nest and the common emplacement SHARE a kind ({sodNest.Kind})");
        Check(ModelLibrary.NameForTest((int)sodNest.Kind, 0, 18) == "sod_shroud_nest",
              "the Sodality Shroud Nest renders as its own lean-to, not the common emplacement");
        Check(ModelLibrary.NameForTest((int)comEmp.Kind, 0, 15) == "com_emplacement",
              "and the common emplacement is unmoved");
        // P7-33: three buildings that all rendered as a WALL SEGMENT.
        Check(ModelLibrary.NameForTest((int)EntityKind.Mine, 0, 19) == "com_mine",
              "a mine no longer renders as a wall segment");
        Check(ModelLibrary.NameForTest((int)EntityKind.Gate, 0, 21) == "com_gate",
              "a gate renders as a gate rather than the wall it sits in");
        Check(ModelLibrary.NameForTest((int)EntityKind.Bridge, 0, 16) == "com_bridge",
              "and a bridge renders as a span");

        // P7-34: every unit type has its own mesh. The table used to stop at 12,
        // so eight distinct units - including the game's only AIRCRAFT - all
        // fell through to the rifle-squad default and drew as infantry.
        //
        // THE CONTROL FIRST: an unknown type must still fall back, or this is
        // asserting that the fallback was removed rather than that the table
        // was filled.
        Check(ModelLibrary.NameForTest(0, 999) == "com_rifle_squad",
              "an unknown unit type still falls back to the rifle squad rather than throwing");
        foreach (var (ut, want) in new[] {
                     (13, "com_repair_vehicle"), (14, "com_carrier"),
                     (15, "com_strike_flyer"), (16, "com_flak_track"),
                     (17, "sod_infiltrator"), (18, "sod_saboteur"),
                     (19, "dir_commando"), (20, "sod_shadow_commando") })
            Check(ModelLibrary.NameForTest(0, ut) == want,
                  $"unit type {ut} renders as {want}, not as a squad of riflemen");

        // A struct type with no entry of its own must still resolve by kind.
        Check(ModelLibrary.NameForTest((int)EntityKind.Refinery, 0, 3) == "com_refinery",
              "a building with no struct-type entry falls back to its kind rather than throwing");

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
        // be seat 1's teal here, because I am player 1. If this reads seat 0's
        // orange, the "me versus them" copy is back.
        //
        // The marks are SEAT colours (BattlefieldView.SeatMarks, indexed by
        // player id), and SodalityMark and DirectorateMark are only the names
        // of the teal and the orange. They say nothing about which faction a
        // seat plays. In this harness seat 1 plays the DIRECTORATE (see
        // _Ready: MatchConfig.Faction goes to seat 0, the opposition faction
        // to seat 1, and the faction-gate check below prints "1 and 0") and
        // still wears the teal.
        Check(BattlefieldView.MarkFor(_game.LocalPlayerId) == BattlefieldView.SodalityMark,
              "at seat 1 my own mark is seat 1's teal (SodalityMark, a seat colour, though this seat plays the Directorate), "
              + "not 'whoever is looking' orange");
        Check(BattlefieldView.MarkFor(_game.EnemyPlayerId) == BattlefieldView.DirectorateMark,
              "and the opposition at seat 0 wears seat 0's orange (DirectorateMark)");
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

        // 3b. THE SAME CHECK FOR BUILDINGS, which had never been written.
        //
        //     Sidebar.StructButtonVisible has existed since P5 and this harness
        //     had never called it once; UnitButtonVisible's own comment cites it
        //     as the precedent for the unit check above. So the hook was built
        //     for the building side, the unit side got written citing it, and
        //     the building side never was - while six faction-locked buildings
        //     shipped (Bastion, Shroud Nest, Veil Projector, the Sodality
        //     generator, the Watch Post and the seismic charge) and two common
        //     ones became Directorate-only.
        //
        //     This is the class this harness exists for: the rule reads
        //     LocalPlayerId and is therefore RIGHT AT SEAT 0 whether or not it
        //     is right at all, and the sim battery cannot see the panel.
        int structGateWrong = 0, structChecked = 0;
        foreach (int t in _game.LiveWorld.StructureTypeIds())
        {
            if (_game.LiveWorld.GetStructureType(t).Tab == BuildTab.None) continue;
            structChecked++;
            int f = _game.LiveWorld.GetStructureType(t).Faction;
            bool shouldShow = f == World.FactionCommon || f == myFaction;
            if (sb.StructFixedGateForTest(t) != shouldShow) structGateWrong++;
        }
        // THE FIXTURE MUST DISCRIMINATE, and this check is here because without
        // it the two above pass vacuously.
        //
        // Found the hard way: breaking FixedGatesAllow to read FactionOf(0)
        // instead of FactionOf(LocalPlayerId) - the exact "right at seat 0 by
        // luck" defect this harness exists to catch - and the new checks still
        // passed. They passed because in this fixture the two seats can hold the
        // SAME faction, so seat 0's answer and the local seat's answer are
        // identical and the hardcode is invisible.
        //
        // So the seats must differ, or a faction gate keyed on the wrong seat
        // reads correct. This is the same trap one level up: a CHECK that is
        // right at seat 0 by luck.
        Check(_game.FactionOf(0) != _game.FactionOf(1),
              $"the two seats hold DIFFERENT factions ({_game.FactionOf(0)} and {_game.FactionOf(1)}), without "
              + "which every faction-gate check above passes whatever seat the rule reads");
        Check(structChecked > 0, $"the panel offers buildings to gate ({structChecked} with a tab)");
        Check(structGateWrong == 0,
              $"every BUILDING button's faction gate agrees with the sim's own BuildStructure refusal across "
              + $"{structChecked} buildings ({structGateWrong} disagree)");

        // 3c. And the control, the shape the unit check above uses: a building
        //     of the OTHER side must be absent here AND refused by the sim if
        //     the order is sent past the panel. Picked from the catalogue at run
        //     time so it does not care which seat it is sitting in.
        int foreignStruct = -1;
        foreach (int t in _game.LiveWorld.StructureTypeIds())
        {
            var d = _game.LiveWorld.GetStructureType(t);
            if (d.Tab == BuildTab.None || d.Faction == World.FactionCommon || d.Faction == myFaction) continue;
            foreignStruct = t;
            break;
        }
        Check(foreignStruct > 0,
              $"the catalogue carries a building of the other side to control against (type {foreignStruct})");
        if (foreignStruct > 0)
        {
            Check(!sb.StructFixedGateForTest(foreignStruct),
                  $"building type {foreignStruct} is gated OUT at this seat");
            Check(!sb.StructButtonVisible(foreignStruct),
                  "...and its button is genuinely not visible, not merely gated in principle");
            Check(!_game.LiveWorld.StructureAllowedForFaction(foreignStruct, myFaction),
                  "...and the SIM refuses it too, so the panel and the sim agree rather than both being wrong");
        }

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

        // --- ...and every STRUCTURE the catalogue registers, too --------------
        // The unit half above is what caught seven unbuildable units. The
        // building half was checked BY HAND at the time and found complete,
        // which is precisely how the unit gap survived two waves: a list that
        // agrees with the catalogue today agrees with it only until the next
        // building lands. So it is a check.
        //
        // Three types are legitimately absent, and each for a recorded decision
        // rather than an oversight. Keyed by KIND, not by type id, so a
        // renumbering cannot silently widen the exemption, and stated here in
        // the same shape as reachabilitygate's own exclusion list.
        int sRegistered = 0, sButtoned = 0, sMapPlaced = 0;
        string sMissing = "";
        foreach (int t in _game.LiveWorld.StructureTypeIds())
        {
            sRegistered++;
            var kind = _game.LiveWorld.GetStructureType(t).Kind;
            bool exempt = kind is EntityKind.ConstructionYard   // MCV-deployed: it is what an MCV becomes
                               or EntityKind.Outpost            // ADR-021: map-placed and captured, never built
                               or EntityKind.Bridge;            // ADR-025: map-placed terrain, never built
            if (sb.StructButtonText(t).Length > 0) sButtoned++;
            else if (exempt) sMapPlaced++;
            else sMissing += (sMissing.Length > 0 ? ", " : "") + $"type {t} ({kind})";
        }
        Check(sMissing.Length == 0,
              "every registered STRUCTURE carries a BUILDINGS or DEFENCE button, or is one of the three map-placed "
              + $"exceptions (missing: {(sMissing.Length > 0 ? sMissing : "none")})");
        Check(sButtoned + sMapPlaced == sRegistered,
              $"...{sButtoned} of {sRegistered} registered structure types are buttoned and {sMapPlaced} are "
              + "map-placed or MCV-deployed, which accounts for all of them");

        // --- ...and DERIVING those buttons changed none of them ---------------
        // The two hand-kept BuildItem arrays are gone: the label now comes off
        // the /data id through the catalogue's own derivation and the tab comes
        // off the authored build_tab key. This is the inertness proof, and it is
        // deliberately a FROZEN TRANSCRIPT of the two arrays as they last stood
        // rather than anything recomputed - a check that derived its expectation
        // the same way the code does would pass however wrong both were.
        //
        // Position is NOT asserted, and that is the one thing the change moves:
        // the walk is in ascending type id (so both seats and both machines see
        // one order), where the arrays were in rough tech order. Label, tab and
        // icon are what a player reads, and all three are unchanged.
        {
            var wasListed = new (int Type, string Label, int Tab, bool Icon)[]
            {
                (1,  "POWER PLANT",    Sidebar.TabBuildings, true),
                (3,  "REFINERY",       Sidebar.TabBuildings, true),
                (11, "BARRACKS",       Sidebar.TabBuildings, false),  // no sprite cut yet
                (2,  "FACTORY",        Sidebar.TabBuildings, true),
                (8,  "SERVICE DEPOT",  Sidebar.TabBuildings, true),
                (12, "RADAR UPLINK",   Sidebar.TabBuildings, false),  // no sprite cut yet
                (5,  "TURRET",         Sidebar.TabDefence,   true),
                (15, "EMPLACEMENT",    Sidebar.TabDefence,   true),   // wears the turret's
                (16, "AIRFIELD",       Sidebar.TabDefence,   true),   // wears the factory's
                (17, "BASTION",        Sidebar.TabDefence,   true),   // wears the turret's
                (18, "SHROUD NEST",    Sidebar.TabDefence,   true),   // wears the veil's
                (9,  "WALL",           Sidebar.TabDefence,   false),  // com_wall_straight is not cut
                (7,  "VEIL PROJECTOR", Sidebar.TabDefence,   true),
                (6,  "SUPERWEAPON",    Sidebar.TabDefence,   true),
                (19, "MINE",           Sidebar.TabDefence,   false),  // wears the wall's, also not cut
            };
            string drift = "";
            foreach (var (type, label, tab, icon) in wasListed)
            {
                // The button text carries cost and build time after the label
                // (BD-02/ADR-006), so the label is its opening.
                if (!sb.StructButtonText(type).StartsWith(label))
                    drift += (drift.Length > 0 ? "; " : "") + $"type {type} reads \"{sb.StructButtonText(type)}\", wanted \"{label}\"";
                else if (sb.TabOfStruct(type) != tab)
                    drift += (drift.Length > 0 ? "; " : "") + $"{label} is in tab {sb.TabOfStruct(type)}, wanted {tab}";
                else if (sb.StructButtonHasIcon(type) != icon)
                    drift += (drift.Length > 0 ? "; " : "") + $"{label} icon {sb.StructButtonHasIcon(type)}, wanted {icon}";
            }
            Check(drift.Length == 0,
                  $"all {wasListed.Length} structures the two hand-kept arrays listed still produce the IDENTICAL "
                  + $"label, tab and icon now that both lists are derived ({(drift.Length > 0 ? drift : "no drift")})");
            // The transcript is FROZEN, and buildings keep arriving, so this
            // stopped being an equality against its length the moment one did.
            // It is the transcript PLUS whatever has been registered since,
            // NAMED here rather than left as a number nobody can check: the
            // point of the check is that a new button cannot appear by accident,
            // and a name is what makes the difference reviewable. The live
            // guarantee is the stronger check above (every registered type is
            // buttoned or map-placed); this one is the history.
            var addedSince = new[]
            {
                "com_gate",        // P7-10: struct type 10, ADR-005's reservation filled
                // P7-5 (DR-02): struct type 20, the Sodality's own power grid.
                // It carries no icon of its own and wears the plant's, exactly
                // as the Bastion wears the turret's - art is owed, and the
                // button is real either way. Note this button is faction-gated,
                // so a Directorate player never sees it; the count here is of
                // buttons BUILT, before FixedGatesAllow hides any.
                "sod_generator",
                // P7-5b (DR-03): struct type 21, the Sodality's Watch Post and
                // the first detector that side has ever had. Faction-gated the
                // same way, and it wears the veil projector's model and icon.
                "sod_watch_post",
                // P7-5c (DR-04): struct type 22, the Sodality's seismic charge.
                // It SHARES EntityKind.Superweapon with the orbital cannon, so
                // it needs no model entry of its own and already wears that one.
                "sod_seismic_charge",
            };
            Check(sButtoned == wasListed.Length + addedSince.Length,
                  $"...and the derivation dropped none of the {wasListed.Length} it transcribed, adding only the "
                  + $"{addedSince.Length} registered since ({string.Join(", ", addedSince)}): {sButtoned} buttons");
        }

        // --- One MatchSetup becomes one MatchConfig, and carries every field --
        // LaunchNetBattle used to copy the lobby's setup by hand, five fields of
        // ten, carrying neither Seats nor TeamMode. It routes through
        // MatchConfig.ApplyFrom now, and this is the check that keeps the one
        // remaining copy honest: a fully-populated setup goes in, CurrentSetup
        // comes back, and the two are compared AS ENCODED BLOBS. The blob walks
        // every field of MatchSetup and its version constant must be bumped
        // whenever one is added, so a field that reaches the wire and not
        // ApplyFrom fails here rather than in a LAN match.
        //
        // A SKIRMISH setup, because a mission's is deliberately not a round
        // trip: CurrentSetup zeroes Seats and TeamMode for a mission, whose map
        // declares its own sides. The LAN lobby only ever carries a skirmish.
        {
            var restore = MatchConfig.CurrentSetup();
            string? beforeMission = MatchConfig.MissionPath, beforeMap = MatchConfig.MapPath;
            var beforeStructs = MatchConfig.AllowedStructures;
            var beforeUnits = MatchConfig.AllowedUnits;
            try
            {
                // Every field set to something that is NOT the default, so a
                // dropped one shows up as a difference rather than as a
                // coincidence.
                var probe = new MatchSetup
                {
                    MapPath = "data/maps/skirmish-09.fmap",
                    MissionIndex = 0,
                    AiPreset = 2,
                    AiDifficulty = 3,
                    StartCredits = 12345,
                    Seed = 4242,
                    Faction = 1,
                    OppFaction = 0,
                    Seats = 3,
                    TeamMode = MatchSetup.TeamsEvenSides,
                };
                MatchConfig.ApplyFrom(probe);
                // The two fields the hand-written copy dropped, named outright:
                // a blob comparison alone would report "something differs".
                Check(MatchConfig.Seats == 3,
                      $"the seat count reaches MatchConfig from a setup ({MatchConfig.Seats}), which the LAN launch dropped");
                Check(MatchConfig.TeamMode == MatchSetup.TeamsEvenSides,
                      $"...and so does the team mode ({MatchConfig.TeamMode}), the other field it dropped");
                var back = MatchConfig.CurrentSetup();
                // The seed is the one MatchSetup field MatchConfig has no home
                // for: it travels in the blob and the lobby's world is built
                // from it, never from MatchConfig. Carried across by hand so the
                // comparison is about the fields this copy is responsible for.
                back.Seed = probe.Seed;
                string a = System.Convert.ToBase64String(MatchSetupBlob.Encode(probe));
                string b = System.Convert.ToBase64String(MatchSetupBlob.Encode(back));
                Check(a == b,
                      "a MatchSetup survives ApplyFrom and CurrentSetup byte-identical as a blob, so no field of it "
                      + $"can be dropped by the one copy that remains{(a == b ? "" : $" (got {b}, wanted {a})")}");
            }
            finally
            {
                MatchConfig.ApplyFrom(restore);
                MatchConfig.MissionPath = beforeMission;
                MatchConfig.MapPath = beforeMap;
                MatchConfig.AllowedStructures = beforeStructs;
                MatchConfig.AllowedUnits = beforeUnits;
            }
        }

        // --- The superweapon can actually be fired ---------------------------
        // CommandType.LaunchSuper has existed since TICKET-P2-SIM-15, the
        // battery asserts its whole lifecycle and SkirmishAI fires one at the
        // player; in game/scripts the identifier appeared only inside a
        // COMMENT, so a player could spend 4000 credits, watch the structure
        // charge, and have no way to use it. These run LAST among the
        // live-world checks because the spawn below mutates the shared world.
        Check(Settings.BindOf("launch_super") != Key.None,
            "the superweapon has a binding (project.godot and the Bindable table agree)");
        _game.PressKey(Settings.BindOf("launch_super"));
        Check(!_game.SuperArmed, "with no superweapon standing, the key refuses rather than arming");

        var lw = _game.LiveWorld;
        int yx = -1, yy = -1;
        for (int i = 0; i < lw.EntityCount; i++)
        {
            var e = lw.Entities[i];
            if (e.Alive && e.PlayerId == _game.LocalPlayerId && e.Kind == EntityKind.ConstructionYard)
            { yx = e.X.ToIntFloor(); yy = e.Y.ToIntFloor(); break; }
        }
        Check(yx >= 0, "the seat owns a Construction Yard to site a superweapon beside");
        lw.SpawnSuperweapon(_game.LocalPlayerId, yx + 3, yy + 3, chargeTicks: 0);
        // The client resolves its own structures through the INTERPOLATED VIEW,
        // not the world, and the view is refreshed on the render path. Stepping
        // ticks advances the sim but cannot refresh the view inside one
        // synchronous pass, which is what made the first version of this check
        // fail against a correct implementation. PumpActorsForTest is the
        // established hook for exactly this (the outpost capture checks use it).
        _game.PumpActorsForTest();
        _game.PressKey(Settings.BindOf("launch_super"));
        Check(_game.SuperArmed, "a CHARGED superweapon ARMS on the key, so the player can finally fire one");
        _game.PressStop();
        Check(!_game.SuperArmed, "STOP clears an armed superweapon too, because stop means stop");

        // After the superweapon, for its reason: these spawn into the shared
        // world, so they run last among the live-world checks.
        RunSupportPowerChecks();
        RunArmedOrderMatrixChecks();
        // P8-1: in a scene of its own, so the spawns above cannot reach it and
        // its own cannot reach anything after it.
        RunInputGate();

        RunLanChecks();
        // P8-4: after the LAN scenes on purpose. The joiner's scene was handed
        // seat 1, and the scene this boots is handed nothing, as a
        // single-player match started from the menu is.
        RunFourSeatHostilityChecks();
    }

    /// <summary>
    /// The armed-order family, proved as a matrix rather than pair by pair.
    /// #143 claimed arming any one armed order cleared the others, and it did
    /// not: ArmAttackMove and ArmPatrol never cleared the superweapon, and
    /// because the click switch tests the superweapon first, the next click
    /// launched it. The checks that existed tested the pairs someone thought
    /// of. These walk all twelve ordered pairs of the four orders, so a missed
    /// pair names itself, and then prove stop, guard and cancel each leave
    /// nothing armed.
    /// </summary>
    private void RunArmedOrderMatrixChecks()
    {
        GD.Print("  --    the armed-order matrix: arming any one clears the other three");
        var lw = _game.LiveWorld;
        int me = _game.LocalPlayerId;
        var (yx, yy) = _game.CellOfForTest(_game.FindEntity(EntityKind.ConstructionYard, me));
        // Preconditions. Every power building the support-power checks stood
        // up is now charging, so a fresh Shroud Nest is stood up READY (it
        // spawns uncharged, World.Add). The superweapon those checks inherited
        // is still charged, and nothing here launches it. Attack-move and
        // patrol need the seat's own combat units selected.
        int nest = lw.SpawnFactionDefence(me, 18, yx - 3, yy + 8);   // 18: the Shroud Nest
        var ne = lw.Entities[nest];
        ne.ChargeTicks = 0;
        lw.SetEntityForTest(nest, ne);
        _game.PumpActorsForTest();
        _game.SelectAllOwn();

        var orders = new (string Name, System.Action Arm, System.Func<bool> Armed)[]
        {
            ("attack-move", () => _game.PressKey(Settings.BindOf("attack_move")), () => _game.AttackMoveArmed),
            ("patrol", () => _game.PressKey(Settings.BindOf("patrol")), () => _game.PatrolArmed),
            ("superweapon", () => _game.PressKey(Settings.BindOf("launch_super")), () => _game.SuperArmed),
            ("support power", () => _game.PressKey(Settings.BindOf("support_power")), () => _game.SupportPowerArmed != null),
        };
        string Standing()
        {
            var up = new List<string>();
            foreach (var o in orders) if (o.Armed()) up.Add(o.Name);
            return up.Count == 0 ? "nothing" : string.Join(" + ", up);
        }

        // Each order arms on its own from a clean slate, so a pair that fails
        // below fails for the pair and not for a missing precondition.
        string wontArm = "";
        foreach (var o in orders)
        {
            _game.PressStop();
            o.Arm();
            if (!o.Armed() || _game.ArmedOrderCountForTest != 1) wontArm += $" {o.Name} ({Standing()});";
        }
        Check(wontArm.Length == 0,
              $"each of the four armed orders arms on its own from a clean slate (the precondition){(wontArm.Length > 0 ? $": failed{wontArm}" : "")}");

        foreach (var a in orders)
            foreach (var b in orders)
            {
                if (a.Name == b.Name) continue;
                _game.PressStop();
                a.Arm();
                bool aUp = a.Armed();
                b.Arm();
                int n = _game.ArmedOrderCountForTest;
                Check(aUp && b.Armed() && n == 1,
                      $"arm {a.Name}, then {b.Name}: only {b.Name} is armed (standing: {Standing()}; {n} flag(s) set)");
            }

        // Stop, guard and cancel each clear the whole family, whichever order
        // happens to be standing.
        var clearers = new (string Name, System.Action Clear)[]
        {
            ("STOP", () => _game.PressStop()),
            ("GUARD", () => _game.PressKey(Settings.BindOf("guard"))),
            ("CANCEL", () => _game.PressKey(Settings.BindOf("cancel"))),
        };
        foreach (var c in clearers)
        {
            string survived = "";
            foreach (var o in orders)
            {
                _game.PressStop();
                o.Arm();
                bool up = o.Armed();
                c.Clear();
                if (!up || _game.ArmedOrderCountForTest != 0) survived += $" {o.Name}{(up ? "" : " (never armed)")};";
            }
            Check(survived.Length == 0,
                  $"{c.Name} leaves NOTHING armed, whichever of the four was standing{(survived.Length > 0 ? $" (left:{survived})" : "")}");
        }
        _game.PressStop();
        _game.ClearSelectionForTest();
    }

    // ===================== INPUTGATE (P8-1) =====================

    /// <summary>The inputgate's own battle scene. A fresh match rather than
    /// the shared one, because everything above has spawned into that world
    /// and every stage here spawns into this one.</summary>
    private SkirmishLive _gate = null!;
    /// <summary>The verbs a stage has driven through a real gesture, for the
    /// coverage check that closes the gate.</summary>
    private readonly HashSet<CommandType> _gateCovered = new();
    private readonly HashSet<string> _gateEffectsCovered = new();
    /// <summary>Set by RunSupportPowerChecks when a real left click fired a
    /// power and the sim accepted it: the stage the inputgate folds in.</summary>
    private bool _supportPowerFiredByClick;

    /// <summary>The recorded exceptions: verbs that deliberately have no
    /// player gesture and never will. The tracker's F1 names exactly one.</summary>
    private static readonly (CommandType Verb, string Why)[] InputGateExceptions =
    {
        (CommandType.Move, "superseded by PathMove: every move a player orders is flow-field pathed, and only the "
                           + "runner's scenario tests issue the bare point move"),
    };

    /// <summary>
    /// THE KNOWN-MISSING TABLE: every verb, or verb on a particular target,
    /// that a player cannot yet issue through any gesture. One table, printed
    /// line by line as KNOWN-MISSING rather than as a failure, so the harness
    /// stays green while the gap stays visible. Each entry names the P8 row
    /// that owns it, and that row's job is to DELETE its entry here and add a
    /// real stage in its place. WholeVerb marks an entry that is the verb's
    /// only story (no gesture at all); the coverage check accepts it in place
    /// of a stage, and refuses it if a stage for that verb now exists.
    /// </summary>
    private static readonly (string Stage, CommandType Verb, bool WholeVerb, string Gap, string Owner)[] InputGateKnownMissing =
    {
        // P8-7 found this one, and it is the SIM's half, not a missing gesture:
        // the right click is sent and re-sent, and the sim never lets it land.
        ("LoadTransport/out-of-reach", CommandType.LoadTransport, false,
            "infantry ordered onto a Carrier from more than two cells away is walked towards it by the sim and settles "
            + "at the four-cell crowd-arrival radius (World.StepToward), outside LoadTransport's two-cell reach, so it "
            + "never boards however often the order is re-sent (measured from 3, 4, 6 and 10 cells)",
            "P8-56 (found by P8-7; the fix is in sim/Ferrostorm.Sim, outside the client lane)"),
    };

    /// <summary>The sim's contact effects. ContactEffect is private to World,
    /// so the four are named here; a fifth added there without a stage here is
    /// the gap this list exists to make someone notice.</summary>
    private static readonly (string Effect, int UnitType)[] InputGateContactUnits =
    {
        ("Capture", World.EngineerUnitType),
        ("Theft", World.InfiltratorUnitType),
        ("Sabotage", World.SaboteurUnitType),
        ("Demolition", World.CommandoUnitType),
    };

    private void Gate(bool ok, string stage, string what) => Check(ok, $"inputgate/{stage}: {what}");

    /// <summary>A unit of a catalogue type, spawned with its authored def the
    /// way the producers spawn one. A FIXTURE: what a stage tests is the
    /// gesture that orders the unit, never how the unit came to stand there.</summary>
    private static int SpawnOfType(World w, int player, int unitType, int cx, int cy)
    {
        var d = w.GetUnitType(unitType);
        return d.Kind == EntityKind.Harvester
            ? w.SpawnHarvester(player, Map.CellCentre(cx), Map.CellCentre(cy))
            : w.SpawnUnit(player, Map.CellCentre(cx), Map.CellCentre(cy), d.Speed, d.Hp, d.Armour, d.WeaponId,
                d.SightCells, d.Stealth, d.Detector, d.Veterancy, unitType);
    }

    private static float Fx(Fix64 v) => (float)(v.Raw / 4294967296.0);

    /// <summary>
    /// P8-1: THE INPUTGATE. P7 proved every system with a gate that tested SIM
    /// ACCEPTANCE, a Command built in C# and handed to World, and five headline
    /// systems shipped that no human could reach. This drives every CommandType
    /// verb (values 2 to 20) and every contact effect through the gesture a
    /// player actually uses, a key, a left or right click through
    /// _UnhandledInput, or a sidebar button's own signal, then asserts what the
    /// gesture queued AND what the sim did with it. Verbs with no gesture yet
    /// are in the KNOWN-MISSING table above; the coverage check at the end
    /// refuses any verb that is in neither place.
    /// </summary>
    private void RunInputGate()
    {
        GD.Print("  --    inputgate (P8-1): every verb and contact effect through the gesture a player uses");
        SkirmishLive.AutoStep = false;
        SkirmishLive.LocalSeat = 1;          // the joiner's seat, as everywhere in this harness
        SkirmishLive.PendingNet = null;      // offline
        _gate = GD.Load<PackedScene>("res://scenes/Skirmish.tscn").Instantiate<SkirmishLive>();
        AddChild(_gate);
        var g = _gate;
        var lw = g.LiveWorld;
        var sb = g.SidebarView;
        int me = g.LocalPlayerId, foe = g.EnemyPlayerId;
        Check(me == 1 && lw.PlayerCount == 2,
              $"inputgate: a fresh two-seat match driven from seat 1 (seat {me} of {lw.PlayerCount})");
        // The first frame gives the view a snapshot to sample; the second runs
        // the frame half that resolves the yard and refreshes the sidebar.
        // Nothing a player does is possible before both.
        g.StepOneTick();
        g.StepOneTick();

        int Pending(CommandType t)
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == t) n++;
            return n;
        }
        int PendingAt(CommandType t, int aux)
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == t && c.AuxId == aux) n++;
            return n;
        }
        (float X, float Z) PosOf(int id) => (Fx(lw.Entities[id].X), Fx(lw.Entities[id].Y));
        void Aim(float x, float z) => g.FocusCameraOn(x, z, 22f);
        Vector2 At(float x, float z) => g.ScreenOf(x, z);
        // Selection by a real click (FinishSelect), never by setting the set.
        bool ClickSelect(int id)
        {
            var (x, z) = PosOf(id);
            Aim(x, z);
            g.ClearSelectionForTest();
            g.BoxSelect(At(x, z), At(x, z));
            return g.SelectionCount == 1 && g.IsSelected(id);
        }
        int CountOwn(EntityKind k)
        {
            int n = 0;
            for (int i = 0; i < lw.EntityCount; i++)
                if (lw.Entities[i].Alive && lw.Entities[i].PlayerId == me && lw.Entities[i].Kind == k) n++;
            return n;
        }
        bool Crowded(int x, int y)
        {
            for (int i = 0; i < lw.EntityCount; i++)
            {
                var e = lw.Entities[i];
                if (!e.Alive || !SkirmishLive.Mobile(e.Kind)) continue;
                if (System.Math.Abs(Map.CellOf(e.X) - x) <= 1 && System.Math.Abs(Map.CellOf(e.Y) - y) <= 1) return true;
            }
            return false;
        }
        // An open, unblocked cell with no unit within one cell of it, nearest
        // the asked-for cell, so a spawned unit can be clicked on its own.
        (int X, int Y) OpenCellNear(int cx, int cy)
        {
            for (int r = 0; r < 12; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 1 || y < 1 || x >= lw.Map.Width - 1 || y >= lw.Map.Height - 1) continue;
                        if (lw.Map.IsBlocked(x, y) || Crowded(x, y)) continue;
                        return (x, y);
                    }
            return (cx, cy);
        }
        // Open ground: no blocked cell within two of the centre, so a whole
        // formation's slots land on cells a unit can reach. A slot on rock is
        // refused by the sim, which is the sim being right and a fixture being
        // careless.
        bool OpenArea(int x, int y, int r)
        {
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ax = x + dx, ay = y + dy;
                    if (ax < 1 || ay < 1 || ax >= lw.Map.Width - 1 || ay >= lw.Map.Height - 1) return false;
                    if (lw.Map.IsBlocked(ax, ay)) return false;
                }
            return true;
        }
        (float X, float Z) OpenGroundNear(float x, float z)
        {
            int cx = (int)x, cy = (int)z;
            for (int r = 0; r < 16; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) == r && OpenArea(cx + dx, cy + dy, 2))
                            return (cx + dx + 0.5f, cy + dy + 0.5f);
            return (x, z);
        }
        void HoldFire(int id)
        {
            var e = lw.Entities[id];
            e.Stance = Stance.HoldFire;
            lw.SetEntityForTest(id, e);
        }
        float ClampX(float x) => Mathf.Clamp(x, 1.5f, lw.Map.Width - 2.5f);
        float ClampZ(float z) => Mathf.Clamp(z, 1.5f, lw.Map.Height - 2.5f);

        int yard = g.FindEntity(EntityKind.ConstructionYard, me);
        Check(yard >= 0 && g.YardIdForTest == yard,
              "inputgate: the frame half has resolved the seat's Construction Yard (every sidebar order needs it)");

        // --- BuildStructure and CancelProduce: the BUILDINGS tab ------------
        const int plantType = 1;
        int Queued() => lw.QueueLength(yard) + lw.LaneContents(yard).Count;
        int q0 = Queued();
        bool firstPress = sb.PressStructButton(plantType);
        bool secondPress = sb.PressStructButton(plantType);
        g.StepTicks(1);
        Gate(firstPress && secondPress && Queued() == q0 + 2, "BuildStructure",
             $"pressing the POWER PLANT button twice queues two at the yard, the second in its second lane ({q0} -> {Queued()})");
        _gateCovered.Add(CommandType.BuildStructure);
        bool cancelClick = sb.RightClickStructButton(plantType);
        int cancelCmds = Pending(CommandType.CancelProduce);
        g.StepTicks(1);
        Gate(cancelClick && cancelCmds == 1 && Queued() == q0 + 1, "CancelProduce",
             $"a RIGHT click on the same button cancels the later one ({q0 + 2} -> {Queued()})");
        _gateCovered.Add(CommandType.CancelProduce);

        // --- PlaceStructure: the PLACE prompt, then a left click -----------
        for (int i = 0; i < 1500 && g.ReadyStructureForTest != plantType; i++) g.StepTicks(1);
        g.StepOneTick();                     // the frame half raises PLACE for what finished
        int plantsBefore = CountOwn(EntityKind.PowerPlant);
        var ownPlants = new HashSet<int>();
        for (int i = 0; i < lw.EntityCount; i++)
            if (lw.Entities[i].Alive && lw.Entities[i].PlayerId == me && lw.Entities[i].Kind == EntityKind.PowerPlant)
                ownPlants.Add(i);
        bool placePressed = sb.PressPlaceButton();
        var plantSite = g.FindPlacementCell(plantType);
        int placeCmds = 0;
        if (placePressed && plantSite is { } ps)
        {
            Aim(ps.X + 1f, ps.Y + 1f);
            g.PressLeftClick(At(ps.X + 0.5f, ps.Y + 0.5f));
            placeCmds = Pending(CommandType.PlaceStructure);
        }
        g.StepTicks(1);
        int placed = -1;
        for (int i = 0; i < lw.EntityCount; i++)
            if (lw.Entities[i].Alive && lw.Entities[i].PlayerId == me && lw.Entities[i].Kind == EntityKind.PowerPlant
                && !ownPlants.Contains(i)) placed = i;
        Gate(placePressed && placeCmds == 1 && placed >= 0 && CountOwn(EntityKind.PowerPlant) == plantsBefore + 1
             && g.ReadyStructureForTest == 0, "PlaceStructure",
             $"the finished plant's PLACE prompt and a left click on open ground stand it there (plants {plantsBefore} -> "
             + $"{CountOwn(EntityKind.PowerPlant)}, ready slot now {g.ReadyStructureForTest})");
        _gateCovered.Add(CommandType.PlaceStructure);

        // --- Repair and SellStructure: keys on a selected building ----------
        if (placed >= 0)
        {
            var pe = lw.Entities[placed];
            pe.Hp -= 40;
            lw.SetEntityForTest(placed, pe);
            g.PumpActorsForTest();
            bool selPlant = ClickSelect(placed);
            g.PressKey(Settings.BindOf("repair"));
            int repairCmds = Pending(CommandType.Repair);
            g.StepTicks(1);
            Gate(selPlant && repairCmds == 1 && lw.Entities[placed].Repairing, "Repair",
                 "a click selects the damaged plant and the repair key switches its repair ON in the sim");
            long creditsBefore = lw.Credits(me);
            g.PressKey(Settings.BindOf("sell"));
            int sellCmds = Pending(CommandType.SellStructure);
            g.StepTicks(1);
            Gate(sellCmds == 1 && !lw.Entities[placed].Alive && lw.Credits(me) > creditsBefore, "SellStructure",
                 $"the sell key sells it: the plant is gone and the treasury rose ({creditsBefore} -> {lw.Credits(me)})");
        }
        else
        {
            Gate(false, "Repair", "no placed plant to repair (the PlaceStructure stage above failed)");
            Gate(false, "SellStructure", "no placed plant to sell (the PlaceStructure stage above failed)");
        }
        _gateCovered.Add(CommandType.Repair);
        _gateCovered.Add(CommandType.SellStructure);

        // --- PathMove, Stop, AttackMove, SetStance: the army ----------------
        g.ClearSelectionForTest();
        g.PressKey(Settings.BindOf("select_all_army"));
        var army = g.SelectedIdsForTest();
        Check(army.Count >= 2, $"inputgate: the army key selects the opening squads ({army.Count})");
        var (sx, sz) = g.FirstSelectedPosition();
        // Towards the middle of the map, so the destination is open ground.
        float towardX = lw.Map.Width / 2f - sx, towardZ = lw.Map.Height / 2f - sz;
        float len = Mathf.Max(0.001f, Mathf.Sqrt(towardX * towardX + towardZ * towardZ));
        // Ten cells, not five: the sim settles a plain move within four cells
        // of its destination (the crowd-arrival rule, StepToward) and completes
        // an all-clear attack-move inside the same radius, so a nearer click
        // would read as units that never moved.
        var (mx, mz) = OpenGroundNear(ClampX(sx + towardX / len * 10f), ClampZ(sz + towardZ / len * 10f));
        Aim(mx, mz);
        g.PressRightClick(At(mx, mz));
        int moveCmds = Pending(CommandType.PathMove), strayAttacks = Pending(CommandType.Attack);
        g.StepTicks(1);
        string ArmyState()
        {
            var parts = new List<string>();
            foreach (int id in army)
            {
                var u = lw.Entities[id];
                parts.Add($"#{id} at {Fx(u.X):0.0},{Fx(u.Y):0.0} to {Fx(u.TargetX):0.0},{Fx(u.TargetY):0.0}"
                          + $"{(u.Moving ? " moving" : " still")}{(u.UseFlow ? " flow" : "")}{(u.AMove ? " amove" : "")}");
            }
            return string.Join("; ", parts);
        }
        var lead = lw.Entities[army[0]];
        bool allMoving = army.TrueForAll(id => lw.Entities[id].Moving && lw.Entities[id].UseFlow);
        bool towardClick = Mathf.Abs(Fx(lead.TargetX) - mx) + Mathf.Abs(Fx(lead.TargetY) - mz) < 4f;
        Gate(moveCmds == army.Count && strayAttacks == 0 && allMoving && towardClick, "PathMove",
             $"a right click on open ground sends every selected unit there by the flow field ({moveCmds} PathMove, "
             + $"{strayAttacks} Attack, click at {mx:0.0},{mz:0.0}: {ArmyState()})");
        _gateCovered.Add(CommandType.PathMove);

        g.PressKey(Settings.BindOf("stop"));
        int stopCmds = Pending(CommandType.Stop);
        g.StepTicks(1);
        Gate(stopCmds == army.Count && army.TrueForAll(id => !lw.Entities[id].Moving), "Stop",
             $"the stop key halts every one of them in the sim ({stopCmds} Stop)");
        _gateCovered.Add(CommandType.Stop);

        g.PressKey(Settings.BindOf("attack_move"));
        bool amArmed = g.AttackMoveArmed;
        g.PressLeftClick(At(mx, mz));
        int amCmds = Pending(CommandType.AttackMove);
        g.StepTicks(1);
        Gate(amArmed && amCmds == army.Count && army.TrueForAll(id => lw.Entities[id].AMove && lw.Entities[id].Moving),
             "AttackMove", $"the attack-move key and a left click put every unit on an attack-move ({amCmds} AttackMove: {ArmyState()})");
        _gateCovered.Add(CommandType.AttackMove);

        bool AllStance(Stance s) => army.TrueForAll(id => lw.Entities[id].Stance == s);
        g.PressKey(Settings.BindOf("hold_fire"));
        g.StepTicks(1);
        bool held = AllStance(Stance.HoldFire);
        g.PressKey(Settings.BindOf("hold_fire"));
        g.StepTicks(1);
        bool freed = AllStance(Stance.Aggressive);
        g.PressKey(Settings.BindOf("guard"));
        g.StepTicks(1);
        bool guarded = AllStance(Stance.Guard);
        g.PressKey(Settings.BindOf("patrol"));
        g.PressLeftClick(At(mx, mz));
        g.StepTicks(1);
        bool patrolling = AllStance(Stance.Patrol);
        Gate(held && freed && guarded && patrolling, "SetStance",
             $"hold-fire, hold-fire again, guard, and patrol with a left click set each stance in the sim (hold {held}, "
             + $"weapons free {freed}, guard {guarded}, patrol {patrolling})");
        _gateCovered.Add(CommandType.SetStance);
        g.PressKey(Settings.BindOf("stop"));     // hand the army back, standing
        g.StepTicks(1);

        // --- Attack: a right click on an enemy in sight ---------------------
        var (lx, lz) = PosOf(army[0]);
        var (ecx, ecy) = OpenCellNear((int)lx + 3, (int)lz);
        int target = SpawnOfType(lw, foe, 2, ecx, ecy);      // 2: com_rifle_squad
        HoldFire(target);                                    // a fixture that does not start the fight itself
        g.StepTicks(1);
        g.PumpActorsForTest();
        Check(g.DrawnForLocalSeatForTest(target), "inputgate: the enemy squad stands in sight (the precondition)");
        var (tx, tz) = PosOf(target);
        Aim(tx, tz);
        string attackCursor = g.CursorNameAt(At(tx, tz));
        g.PressRightClick(At(tx, tz));
        int attackCmds = PendingAt(CommandType.Attack, target);
        g.StepTicks(1);
        bool engaged = army.Exists(id => lw.Entities[id].ExplicitTarget == target);
        Gate(attackCursor == "Attack" && attackCmds == army.Count && engaged, "Attack",
             $"over an enemy in sight the cursor reads {attackCursor}, and a right click orders the attack, which the sim takes "
             + $"({attackCmds} Attack, explicit target held: {engaged})");
        _gateCovered.Add(CommandType.Attack);
        g.ClearSelectionForTest();

        // --- Harvest: a right click on a field with the harvester selected --
        int harv = g.FindEntity(EntityKind.Harvester, me);
        var refSite = g.FindPlacementCell(3);
        if (refSite is { } rs) lw.SpawnRefinery(me, rs.X, rs.Y);
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool selHarv = harv >= 0 && ClickSelect(harv);
        int field = -1;
        {
            Fix64 best = Fix64.MaxValue;
            for (int i = 0; harv >= 0 && i < lw.EntityCount; i++)
            {
                var f = lw.Entities[i];
                if (!f.Alive || f.Kind != EntityKind.FerriteField || f.FerriteAmount <= 0) continue;
                Fix64 d = Fix64.DistSq(f.X - lw.Entities[harv].X, f.Y - lw.Entities[harv].Y);
                if (d < best) { best = d; field = i; }
            }
        }
        int harvestAt = -1;
        if (field >= 0)
        {
            var (fx, fz) = PosOf(field);
            Aim(fx, fz);
            g.PressRightClick(At(fx, fz));
            foreach (var c in g.PendingForTest)
                if (c.Type == CommandType.Harvest && c.EntityId == harv) harvestAt = c.AuxId;
        }
        g.StepTicks(1);
        bool mining = harvestAt >= 0 && lw.Entities[harvestAt].Kind == EntityKind.FerriteField
                      && lw.Entities[harv].FieldId == harvestAt && lw.Entities[harv].HState != HarvestState.Idle;
        Gate(selHarv && g.RefineryLive && mining, "Harvest",
             $"with a refinery standing, a click selects the harvester and a right click on a deposit sets it harvesting "
             + $"there (field {harvestAt}, state {(harv >= 0 ? lw.Entities[harv].HState : HarvestState.Idle)})");
        _gateCovered.Add(CommandType.Harvest);

        // --- SetRally: a right click with a Factory selected ----------------
        var facSite = g.FindPlacementCell(World.FactoryStructType);
        int factory = facSite is { } fsx ? lw.SpawnFactory(me, fsx.X, fsx.Y) : -1;
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool selFactory = factory >= 0 && ClickSelect(factory);
        bool rallied = false;
        int rallyCmds = 0;
        if (factory >= 0)
        {
            var (fcx, fcz) = PosOf(factory);
            float rx = ClampX(fcx + 3f), rz = ClampZ(fcz + 3f);
            Aim(rx, rz);
            g.PressRightClick(At(rx, rz));
            rallyCmds = Pending(CommandType.SetRally);
            g.StepTicks(1);
            var fe = lw.Entities[factory];
            rallied = fe.HasRally && Mathf.Abs(Fx(fe.RallyX) - rx) + Mathf.Abs(Fx(fe.RallyY) - rz) < 1.5f;
        }
        Gate(selFactory && rallyCmds == 1 && rallied, "SetRally",
             "a click selects a Factory and a right click on the ground sets its rally point in the sim");
        _gateCovered.Add(CommandType.SetRally);

        // --- Produce and CancelProduce (unit): the VEHICLES tab -------------
        g.StepOneTick();                     // the frame half sees the factory and lights its tab
        g.StepOneTick();
        int vehicle = -1;
        foreach (int t in lw.UnitTypeIds())
        {
            var d = lw.GetUnitType(t);
            if (d.ProducedAt != World.FactoryStructType) continue;
            if (d.Faction != World.FactionCommon && d.Faction != lw.FactionOf(me)) continue;
            if (!lw.HasPrereqs(me, d.Prereqs) || !sb.UnitButtonVisible(t)) continue;
            vehicle = t;
            break;
        }
        int fq = factory >= 0 ? lw.QueueLength(factory) : -1;
        bool produced = vehicle > 0 && sb.PressUnitButton(vehicle);
        g.StepTicks(1);
        Gate(produced && factory >= 0 && lw.QueueLength(factory) == fq + 1, "Produce",
             $"the {(vehicle > 0 ? g.UnitNameForTest(vehicle) : "(none)")} button queues one at the factory "
             + $"({fq} -> {(factory >= 0 ? lw.QueueLength(factory) : -1)})");
        _gateCovered.Add(CommandType.Produce);
        bool unitCancel = vehicle > 0 && sb.RightClickUnitButton(vehicle);
        g.StepTicks(1);
        Gate(unitCancel && factory >= 0 && lw.QueueLength(factory) == fq, "CancelProduce",
             "...and a right click on the unit's button cancels it again, the unit half of the same verb");

        // --- Deploy: the deploy key on a selected MCV -----------------------
        var (ycx, ycy) = g.CellOfForTest(yard);
        (int X, int Y)? foundation = null;
        for (int r = 4; r < 16 && foundation == null; r++)
            for (int dy = -r; dy <= r && foundation == null; dy++)
                for (int dx = -r; dx <= r && foundation == null; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                    int x = ycx + dx, y = ycy + dy;
                    if (x < 1 || y < 1 || x >= lw.Map.Width - 2 || y >= lw.Map.Height - 2) continue;
                    if (lw.ValidFoundation(x, y) && !Crowded(x, y) && !Crowded(x + 1, y + 1)) foundation = (x, y);
                }
        int mcv = foundation is { } fo ? SpawnOfType(lw, me, World.McvUnitType, fo.X, fo.Y) : -1;
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool selMcv = mcv >= 0 && ClickSelect(mcv);
        int yardsBefore = CountOwn(EntityKind.ConstructionYard);
        g.PressKey(Settings.BindOf("deploy"));
        int deployCmds = Pending(CommandType.Deploy);
        g.StepTicks(1);
        Gate(selMcv && deployCmds == 1 && !lw.Entities[mcv].Alive && CountOwn(EntityKind.ConstructionYard) == yardsBefore + 1,
             "Deploy", $"a click selects an MCV and the deploy key unpacks it into a Construction Yard (yards {yardsBefore} -> "
             + $"{CountOwn(EntityKind.ConstructionYard)})");
        _gateCovered.Add(CommandType.Deploy);
        g.ClearSelectionForTest();

        // --- LaunchSuper: the superweapon key and a left click --------------
        // #143 proved the key ARMS; nothing proved a click then FIRES.
        var swSite = g.FindPlacementCell(6);
        int sw = swSite is { } s6 ? lw.SpawnSuperweapon(me, s6.X, s6.Y, chargeTicks: 0) : -1;
        g.PumpActorsForTest();
        g.PressKey(Settings.BindOf("launch_super"));
        bool swArmed = g.SuperArmed;
        // Aimed at whichever map corner lies farthest from both yards, so the
        // strike that lands 75 ticks later falls on nothing a later stage reads.
        int foeYard = g.FindEntity(EntityKind.ConstructionYard, foe);
        float bestCorner = -1f, aimX = 2.5f, aimZ = 2.5f;
        foreach (var (cxk, czk) in new[] { (2.5f, 2.5f), (lw.Map.Width - 3.5f, 2.5f),
                                           (2.5f, lw.Map.Height - 3.5f), (lw.Map.Width - 3.5f, lw.Map.Height - 3.5f) })
        {
            float nearest = float.MaxValue;
            foreach (int y in new[] { yard, foeYard })
            {
                if (y < 0) continue;
                var (yx, yz) = PosOf(y);
                nearest = Mathf.Min(nearest, Mathf.Abs(yx - cxk) + Mathf.Abs(yz - czk));
            }
            if (nearest > bestCorner) { bestCorner = nearest; aimX = cxk; aimZ = czk; }
        }
        Aim(aimX, aimZ);
        g.PressLeftClick(At(aimX, aimZ));
        int launchCmds = Pending(CommandType.LaunchSuper);
        g.StepTicks(1);
        Gate(sw >= 0 && swArmed && launchCmds == 1 && lw.Entities[sw].StrikeTicks >= 0, "LaunchSuper",
             $"the superweapon key arms it and a left click FIRES it: the strike is in flight in the sim "
             + $"({(sw >= 0 ? lw.Entities[sw].StrikeTicks : -1)} ticks to impact)");
        _gateCovered.Add(CommandType.LaunchSuper);

        // --- UseSupportPower: folded in, not repeated -----------------------
        Gate(_supportPowerFiredByClick, "UseSupportPower",
             "fired by the POWERS strip, the support-power key and a real left click, and accepted by the sim: the "
             + "support-power stages above, folded in here rather than spawning every power building twice");
        _gateCovered.Add(CommandType.UseSupportPower);

        // --- The contact effects: a right click on an enemy building --------
        // Each contact unit is ordered the way a player orders it, a click to
        // select and a right click on an enemy structure in sight, and the
        // effect is read off the sim. The structure is an enemy power plant
        // stood up inside this seat's base, in sight and away from the army.
        // THE SITE AND THE APPROACH ARE CHOSEN TOGETHER. A walk onto a building
        // is routed at ONE cell, its centre cell (ax+1, ay+1), so a building
        // whose centre cell is boxed in cannot be reached from any side: the
        // single-cell destination D4 records for refineries and P8-15
        // replaces. Measured twice, not guessed: a saboteur stood still for
        // 120 ticks beside a plant whose centre cell had the yard below it and
        // another building beside it, and a commando stood still in a pocket
        // the earlier stages' buildings had walled in. So the site is taken
        // only with an open three-cell corridor running east from the centre
        // cell, and the unit starts at its far end: a two-cell walk to a cell
        // orthogonally beside the one the route is built to, then the act.
        (int X, int Y, int UX, int UY)? VictimSite()
        {
            var (ycx0, ycy0) = g.CellOfForTest(yard);
            for (int r = 2; r <= World.CyBuildRadius; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                        int x = ycx0 + dx, y = ycy0 + dy;
                        if (x + 5 >= lw.Map.Width || !lw.ValidPlacement(me, x, y, plantType)) continue;
                        bool corridor = true;
                        for (int c = 2; c <= 4; c++)
                            if (lw.Map.IsBlocked(x + c, y + 1) || Crowded(x + c, y + 1)) corridor = false;
                        if (!corridor || !lw.IsVisible(me, x + 1, y + 1)) continue;
                        return (x, y, x + 4, y + 1);
                    }
            return null;
        }
        foreach (var (effect, unitType) in InputGateContactUnits)
        {
            var site = VictimSite();
            if (site is not { } vs)
            {
                Gate(false, effect, "no open site for the enemy structure (a fixture failure, not a product one)");
                continue;
            }
            int victim = lw.SpawnPowerPlant(foe, vs.X, vs.Y);
            var (vx, vz) = PosOf(victim);
            int agent = SpawnOfType(lw, me, unitType, vs.UX, vs.UY);
            // HOLD FIRE, because the commando is armed: left aggressive it
            // shoots the plant on the tick it lands and a demolition check
            // would pass on rifle damage. The walk-in is unaffected, since the
            // contact rule takes an explicit order whatever the stance.
            HoldFire(agent);
            g.StepTicks(1);
            g.PumpActorsForTest();
            bool selAgent = ClickSelect(agent);
            Aim(vx, vz);
            string cursor = g.CursorNameAt(At(vx, vz));
            g.PressRightClick(At(vx, vz));
            int walkIn = PendingAt(CommandType.Attack, victim);
            bool robbed = false, done = false;
            int ticks = 0, prevHp = lw.Entities[victim].Hp;
            for (; ticks < 120 && !done; ticks++)
            {
                g.StepTicks(1);
                foreach (var ev in lw.Events)
                    if (ev.Type == GameEventType.Robbed && ev.A == victim) robbed = true;
                var v = lw.Entities[victim];
                done = effect switch
                {
                    "Capture" => v.PlayerId == me,
                    "Theft" => robbed,
                    "Sabotage" => lw.IsDisabled(victim),
                    // A charge, not attrition: a third of the demolition figure
                    // in ONE tick is more than any gun here deals, and a sale
                    // (which kills without touching Hp) cannot meet it either.
                    _ => prevHp - v.Hp >= World.DemolitionDamage / 3,
                };
                prevHp = v.Hp;
            }
            var ag = lw.Entities[agent];
            string agentState = $"agent #{agent} {(ag.Alive ? "alive" : "consumed")} at {Fx(ag.X):0.0},{Fx(ag.Y):0.0}"
                                + $"{(ag.Moving ? " moving" : " still")} speed {Fx(ag.Speed):0.00}, "
                                + $"target {ag.ExplicitTarget}, victim #{victim} at {vx:0.0},{vz:0.0} "
                                + $"{(lw.Entities[victim].Alive ? "standing" : "destroyed")} owned by {lw.Entities[victim].PlayerId}";
            if (!done)
            {
                // The ground round the victim, for a failure that is about a
                // route: X blocked, A the agent's cell, a dot open.
                var rows = new List<string>();
                for (int y = vs.Y - 4; y <= vs.Y + 5; y++)
                {
                    var row = new System.Text.StringBuilder();
                    for (int x = vs.X - 4; x <= vs.X + 5; x++)
                        row.Append(x == Map.CellOf(ag.X) && y == Map.CellOf(ag.Y) ? 'A'
                                   : x < 0 || y < 0 || x >= lw.Map.Width || y >= lw.Map.Height || lw.Map.IsBlocked(x, y) ? 'X' : '.');
                    rows.Add(row.ToString());
                }
                agentState += $"; ground from {vs.X - 4},{vs.Y - 4}: {string.Join("/", rows)}";
            }
            string reads = effect switch
            {
                "Capture" => "the plant changes hands",
                "Theft" => "the owner's treasury is robbed",
                "Sabotage" => "the plant is switched off",
                _ => "the plant takes the demolition charge",
            };
            // P8-8: every contact unit wears the walk-in verb over a target it
            // can act on. Until P8-8 only the engineer did and the other three
            // read Attack, which for an unarmed infiltrator promised a fight.
            bool cursorOk = cursor == "Enter";
            Gate(selAgent && walkIn == 1 && done && cursorOk, effect,
                 $"a click selects the {g.UnitNameForTest(unitType)} and a right click on an enemy building sends it in: "
                 + $"{reads} after {ticks} ticks (cursor {cursor}, {walkIn} Attack queued{(done ? "" : $"; {agentState}")})");
            _gateEffectsCovered.Add(effect);
        }

        // --- P8-5: an enemy under the shroud is not a target -----------------
        // The pick read the whole snapshot list, so the Attack glyph lit over
        // every hidden enemy and a right click sent the army to it. The
        // opposition's yard is the fixture: it stands in this seat's fog for
        // the whole match. The control is the Attack stage above, where the
        // same gesture on an enemy in sight IS an attack.
        GD.Print("  --    inputgate (P8-5): target picking honours the fog");
        g.ClearSelectionForTest();
        g.PressKey(Settings.BindOf("select_all_army"));
        var fogArmy = g.SelectedIdsForTest();
        if (foeYard >= 0 && fogArmy.Count > 0)
        {
            var (hx, hy) = g.CellOfForTest(foeYard);
            Check(!lw.IsVisible(me, hx, hy) && !g.DrawnForLocalSeatForTest(foeYard),
                  "inputgate/fog-pick: the opposition's yard stands in this seat's fog and is not drawn (the precondition)");
            var (hxf, hzf) = PosOf(foeYard);
            Aim(hxf, hzf);
            string overShroud = g.CursorNameAt(At(hxf, hzf));
            g.PressRightClick(At(hxf, hzf));
            int shroudAttacks = PendingAt(CommandType.Attack, foeYard);
            int shroudMoves = Pending(CommandType.PathMove);
            g.StepTicks(1);
            bool noneTargeted = fogArmy.TrueForAll(id => lw.Entities[id].ExplicitTarget != foeYard);
            Gate(overShroud == "Move" && shroudAttacks == 0 && shroudMoves == fogArmy.Count && noneTargeted, "fog-pick",
                 $"over a shrouded enemy the cursor reads {overShroud} and a right click is a MOVE ({shroudMoves} PathMove, "
                 + $"{shroudAttacks} Attack), so no unit is handed a target it cannot see");
            g.PressKey(Settings.BindOf("stop"));
            g.StepTicks(1);
        }
        else
            Check(false, $"inputgate/fog-pick: an enemy yard and an army to test with (the precondition; yard {foeYard}, army {fogArmy.Count})");

        RunStealthStages(g, foe);
        // P8-7, P8-8 and P8-9 spawn fixtures of their own, so they run after
        // every earlier stage has read its world, and the coverage check closes
        // the gate once every stage has had its say.
        RunCarrierStages(g);
        RunNeutralTargetStages(g);
        RunAirfieldStages(g);
        RunInputGateCoverage();
        g.QueueFree();
    }

    /// <summary>The inputgate's closing check: every verb is staged, the
    /// recorded exception, or KNOWN-MISSING, and the table is printed. Run
    /// last, after every stage has marked what it covered.</summary>
    private void RunInputGateCoverage()
    {
        // --- Coverage: nothing is in neither place ---------------------------
        string uncovered = "", stale = "";
        int verbs = 0;
        foreach (CommandType t in System.Enum.GetValues<CommandType>())
        {
            if (t == CommandType.None) continue;
            verbs++;
            bool excepted = System.Array.Exists(InputGateExceptions, x => x.Verb == t);
            bool wholeMissing = System.Array.Exists(InputGateKnownMissing, x => x.Verb == t && x.WholeVerb);
            if (!_gateCovered.Contains(t) && !excepted && !wholeMissing) uncovered += $" {t}";
            if (_gateCovered.Contains(t) && wholeMissing) stale += $" {t}";
        }
        Check(uncovered.Length == 0,
              $"inputgate/coverage: all {verbs} CommandType verbs have a gesture stage, are the recorded Move exception, or "
              + $"are in the KNOWN-MISSING table{(uncovered.Length > 0 ? $" (in neither:{uncovered})" : "")}");
        Check(stale.Length == 0,
              $"inputgate/coverage: no verb with a stage is still listed as wholly KNOWN-MISSING{(stale.Length > 0 ? $" (delete the entry for:{stale})" : "")}");
        string noEffect = "";
        foreach (var (effect, _) in InputGateContactUnits)
            if (!_gateEffectsCovered.Contains(effect)) noEffect += $" {effect}";
        Check(noEffect.Length == 0,
              $"inputgate/coverage: all {InputGateContactUnits.Length} contact effects have a gesture stage{(noEffect.Length > 0 ? $" (missing:{noEffect})" : "")}");
        foreach (var (verb, why) in InputGateExceptions)
            GD.Print($"  EXCEPTION      inputgate/{verb}: {why}");
        foreach (var k in InputGateKnownMissing)
            GD.Print($"  KNOWN-MISSING  inputgate/{k.Stage} ({k.Verb}{(k.WholeVerb ? ", the whole verb" : " on this target")}): "
                     + $"{k.Gap}; owner {k.Owner}");
    }

    /// <summary>
    /// A quiet patch of open ground for a stealth fixture: every cell from -3
    /// to +5 of it unblocked and on the map, and no living entity other than a
    /// ferrite field within two cells of that box, so nothing already standing
    /// can detect, dot or be mistaken for what the stage spawns. Searched
    /// outward from a cell, at least `minFrom` cells from `avoid`.
    /// </summary>
    private static (int X, int Y)? QuietGround(World lw, int fromX, int fromY, (int X, int Y)? avoid = null, int minFrom = 0)
    {
        for (int r = 4; r < 40; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != r) continue;
                    int x = fromX + dx, y = fromY + dy;
                    if (avoid is { } a && System.Math.Max(System.Math.Abs(x - a.X), System.Math.Abs(y - a.Y)) < minFrom) continue;
                    bool ok = true;
                    for (int by = y - 3; by <= y + 5 && ok; by++)
                        for (int bx = x - 3; bx <= x + 5 && ok; bx++)
                            if (bx < 1 || by < 1 || bx >= lw.Map.Width - 1 || by >= lw.Map.Height - 1 || lw.Map.IsBlocked(bx, by)) ok = false;
                    for (int i = 0; i < lw.EntityCount && ok; i++)
                    {
                        var e = lw.Entities[i];
                        if (!e.Alive || e.Kind == EntityKind.FerriteField) continue;
                        int ex = Map.CellOf(e.X), ey = Map.CellOf(e.Y);
                        if (ex >= x - 5 && ex <= x + 7 && ey >= y - 5 && ey <= y + 7) ok = false;
                    }
                    if (ok) return (x, y);
                }
        return null;
    }

    /// <summary>
    /// P8-6, decision D24, from the seat a Directorate player sits in against
    /// a Sodality opponent. The client read no stealth at all: every cloaked
    /// unit, Sodality building and mine was drawn for its enemies, and a
    /// detector changed nothing on screen, so the Sodality's defining mechanic
    /// did nothing on the opponent's screen and players saw targets their units
    /// refused to shoot. Every enemy fixture holds fire, because a cloaked thing
    /// that fires is revealed to everyone (the sim's rule, and drawn rightly).
    /// </summary>
    private void RunStealthStages(SkirmishLive g, int foe)
    {
        GD.Print("  --    inputgate (P8-6): stealth on screen, decision D24");
        var lw = g.LiveWorld;
        int me = g.LocalPlayerId;
        int phantomType = UnitCatalogue.TypeIdOf("sod_phantom_tank");
        int scoutType = UnitCatalogue.TypeIdOf("dir_sentinel_scout");
        Color foeMark = BattlefieldView.MarkFor(foe);
        void Hold(int id)
        {
            var e = lw.Entities[id];
            e.Stance = Stance.HoldFire;
            lw.SetEntityForTest(id, e);
        }
        (float X, float Z) PosOf(int id) => (Fx(lw.Entities[id].X), Fx(lw.Entities[id].Y));
        bool TeamDetects(int id) => (lw.Entities[id].DetectedMask & (1 << me)) != 0;
        string Over(int id)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            return g.CursorNameAt(g.ScreenOf(x, z));
        }
        bool ClickSelect(int id)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            g.ClearSelectionForTest();
            g.BoxSelect(g.ScreenOf(x, z), g.ScreenOf(x, z));
            return g.SelectionCount == 1 && g.IsSelected(id);
        }
        int AttacksOn(int id)
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == CommandType.Attack && c.AuxId == id) n++;
            return n;
        }
        // Two frames, the radar check's reason: the frame half samples the view
        // a tick behind, so the second is the one that shows what the first
        // tick did. Then the actor sync, so the nodes are current too.
        void Settle()
        {
            g.StepOneTick();
            g.StepOneTick();
            g.PumpActorsForTest();
        }

        var (ycx, ycy) = g.CellOfForTest(g.FindEntity(EntityKind.ConstructionYard, me));
        var q = QuietGround(lw, ycx, ycy);
        if (q is not { } s)
        {
            Check(false, "inputgate/stealth: open, quiet ground for the fixture (none found: a fixture failure, not a product one)");
            return;
        }

        // --- Undetected, in sight: not drawn, not dotted, not pickable ------
        int rifle = SpawnOfType(lw, me, 2, s.X, s.Y);                  // my eyes, and later my gun
        int ownPhantom = SpawnOfType(lw, me, phantomType, s.X - 2, s.Y);
        int phantom = SpawnOfType(lw, foe, phantomType, s.X + 3, s.Y);
        Hold(phantom);
        int mine = lw.SpawnMine(foe, s.X + 3, s.Y + 3);
        int nest = lw.SpawnFactionDefence(foe, 18, s.X, s.Y + 3);     // 18: the Shroud Nest, a cloaked building
        Hold(nest);
        Settle();
        var (px, pz) = PosOf(phantom);
        bool inSight = lw.IsVisible(me, Map.CellOf(lw.Entities[phantom].X), Map.CellOf(lw.Entities[phantom].Y))
                       && lw.IsVisible(me, s.X + 3, s.Y + 3) && lw.IsVisible(me, s.X + 1, s.Y + 4);
        bool allCloaked = lw.Entities[phantom].Stealth && lw.Entities[mine].Stealth && lw.Entities[nest].Stealth;
        bool undetected = !TeamDetects(phantom) && !TeamDetects(mine) && !TeamDetects(nest)
                          && lw.Entities[phantom].RevealTicks == 0 && lw.Entities[nest].RevealTicks == 0;
        Check(inSight && allCloaked && undetected,
              "inputgate/stealth: an enemy Phantom Tank, mine and Shroud Nest stand cloaked and undetected in cells this seat "
              + $"can SEE (the precondition: in sight {inSight}, cloaked {allCloaked}, undetected {undetected})");
        Check(!g.DrawnForLocalSeatForTest(phantom) && !g.ActorShownForTest(phantom),
              "inputgate/stealth: the undetected Phantom Tank in a visible cell is NOT drawn");
        Check(!g.ActorShownForTest(mine) && !g.ActorShownForTest(nest),
              "inputgate/stealth: nor are the undetected mine and the Shroud Nest beside it");
        Check(!g.MinimapView.HasDotNearForTest(px, pz, foeMark),
              "inputgate/stealth: and the minimap gives the Phantom Tank no dot");
        bool selRifle = ClickSelect(rifle);
        string overHidden = Over(phantom);
        g.PressRightClick(g.ScreenOf(px, pz));
        int hiddenAttacks = AttacksOn(phantom);
        g.StepTicks(1);
        Check(selRifle && overHidden == "Move" && hiddenAttacks == 0 && lw.Entities[rifle].ExplicitTarget != phantom,
              $"inputgate/stealth: over the undetected Phantom Tank the cursor reads {overHidden} and a right click is a move "
              + $"({hiddenAttacks} Attack), so it cannot be picked");
        g.PressKey(Settings.BindOf("stop"));
        g.StepTicks(1);

        // --- My own cloaked unit says so ------------------------------------
        Check(g.ActorShownForTest(ownPhantom) && g.ActorTranslucentForTest(ownPhantom) && !g.ActorTranslucentForTest(rifle),
              "inputgate/stealth: my own Phantom Tank is drawn TRANSLUCENT, so I can see it is cloaked, and my uncloaked squad is not");

        // --- A Sentinel Scout in range: drawn, tinted, attackable -----------
        int scout = SpawnOfType(lw, me, scoutType, s.X + 1, s.Y - 2);
        Settle();
        Check(TeamDetects(phantom) && TeamDetects(mine) && TeamDetects(nest),
              "inputgate/stealth: a Sentinel Scout in range detects all three (the precondition: the sim set this seat's bit)");
        Check(g.ActorShownForTest(phantom) && g.ActorDetectedTintForTest(phantom),
              "inputgate/stealth: the detected Phantom Tank is drawn, wearing the detected tint");
        Check(g.ActorShownForTest(mine) && g.ActorShownForTest(nest),
              "inputgate/stealth: and so are the detected mine and Shroud Nest");
        Check(g.MinimapView.HasDotNearForTest(Fx(lw.Entities[phantom].X), Fx(lw.Entities[phantom].Y), foeMark),
              "inputgate/stealth: and the detected Phantom Tank has its minimap dot");
        selRifle = ClickSelect(rifle);
        string overDetected = Over(phantom);
        var (dx, dz) = PosOf(phantom);
        g.PressRightClick(g.ScreenOf(dx, dz));
        int detectedAttacks = AttacksOn(phantom);
        g.StepTicks(1);
        Gate(selRifle && overDetected == "Attack" && detectedAttacks == 1 && lw.Entities[rifle].ExplicitTarget == phantom,
             "stealth-detected", $"with a Sentinel Scout in range the Phantom Tank is attackable: cursor {overDetected}, "
             + $"{detectedAttacks} Attack queued, and the sim holds it as the squad's target");

        // --- A Watch Post in range: the structure detector does the same -----
        var q2 = QuietGround(lw, ycx, ycy, (s.X, s.Y), 14);
        if (q2 is not { } w)
        {
            Check(false, "inputgate/stealth: a second patch of quiet ground, clear of the scout (none found: a fixture failure)");
            return;
        }
        int rifle2 = SpawnOfType(lw, me, 2, w.X, w.Y);
        int phantom2 = SpawnOfType(lw, foe, phantomType, w.X + 3, w.Y);
        Hold(phantom2);
        Settle();
        Check(!TeamDetects(phantom2) && lw.Entities[phantom2].RevealTicks == 0 && !g.ActorShownForTest(phantom2),
              "inputgate/stealth: a second Phantom Tank, far from the scout, is undetected and not drawn (the precondition)");
        lw.SpawnWatchPost(me, w.X + 3, w.Y + 2);
        Settle();
        bool selRifle2 = ClickSelect(rifle2);
        string overPost = Over(phantom2);
        var (p2x, p2z) = PosOf(phantom2);
        g.PressRightClick(g.ScreenOf(p2x, p2z));
        int postAttacks = AttacksOn(phantom2);
        g.StepTicks(1);
        Gate(TeamDetects(phantom2) && g.ActorShownForTest(phantom2) && g.ActorDetectedTintForTest(phantom2)
             && selRifle2 && overPost == "Attack" && postAttacks == 1 && lw.Entities[rifle2].ExplicitTarget == phantom2,
             "stealth-detected", $"with a Watch Post in range it is drawn, tinted and attackable: cursor {overPost}, "
             + $"{postAttacks} Attack queued, and the sim holds it as the squad's target");
        g.ClearSelectionForTest();
    }

    /// <summary>
    /// P8-7: the Carrier's verbs. The 600-credit transport had none: nothing in
    /// the client issued LoadTransport or UnloadTransport, so P7-3's carrier
    /// was a vehicle that could only drive about empty. Every order here goes
    /// through the gesture a player uses (a drag, a right click, the unload
    /// key), and every check reads the hold off the sim.
    /// </summary>
    private void RunCarrierStages(SkirmishLive g)
    {
        GD.Print("  --    inputgate (P8-7): the Carrier boards, sets down and reads its hold");
        var lw = g.LiveWorld;
        int me = g.LocalPlayerId;
        (float X, float Z) PosOf(int id) => (Fx(lw.Entities[id].X), Fx(lw.Entities[id].Y));
        Vector2 ScreenAt(int id) { var (x, z) = PosOf(id); return g.ScreenOf(x, z); }
        bool ClickSelect(int id)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            g.ClearSelectionForTest();
            g.BoxSelect(g.ScreenOf(x, z), g.ScreenOf(x, z));
            return g.SelectionCount == 1 && g.IsSelected(id);
        }
        int Loads(int carrierId)
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == CommandType.LoadTransport && c.AuxId == carrierId) n++;
            return n;
        }
        bool InReach(int a, int b)
            => Fix64.DistSq(lw.Entities[a].X - lw.Entities[b].X, lw.Entities[a].Y - lw.Entities[b].Y) <= Fix64.FromInt(4);
        int OwnRiflesNear(int id)
        {
            int n = 0;
            for (int i = 0; i < lw.EntityCount; i++)
            {
                var e = lw.Entities[i];
                if (e.Alive && e.PlayerId == me && e.Kind == EntityKind.Unit && e.UnitType == 2
                    && Fix64.DistSq(e.X - lw.Entities[id].X, e.Y - lw.Entities[id].Y) <= Fix64.FromInt(9)) n++;
            }
            return n;
        }

        // --- The key exists, and agrees with the rebind table ----------------
        // The support-power key's proof: BindOf answers only for an action in
        // BOTH the Bindable table and project.godot's [input] block.
        Key unload = Settings.BindOf("unload");
        Check(unload != Key.None && Settings.ConflictFor("unload", unload) == null,
              $"inputgate/UnloadTransport: the unload key has a binding of its own ({Settings.KeyName(unload)}): "
              + "project.godot and the Bindable table agree, and no other action holds it");

        var (ycx, ycy) = g.CellOfForTest(g.FindEntity(EntityKind.ConstructionYard, me));
        var q = QuietGround(lw, ycx, ycy);
        if (q is not { } s)
        {
            Check(false, "inputgate/LoadTransport: open, quiet ground for the fixture (none found: a fixture failure, not a product one)");
            return;
        }

        // --- LoadTransport: a drag on the squads, a right click on the Carrier
        // Both squads stand within the sim's two-cell reach; the walk-in from
        // farther is the KNOWN-MISSING line above, a sim defect.
        int carrier = SpawnOfType(lw, me, World.CarrierUnitType, s.X + 1, s.Y);
        int r1 = SpawnOfType(lw, me, 2, s.X, s.Y);
        int r2 = SpawnOfType(lw, me, 2, s.X, s.Y + 1);
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool inReach = InReach(r1, carrier) && InReach(r2, carrier);
        var (cxf, czf) = PosOf(carrier);
        // The camera's own closest zoom (RtsCamera.MinHeight), because the
        // headless viewport is small and a cell is a few pixels across at the
        // usual height. The squads stand in one column with the Carrier a cell
        // to the east, so the drag is a tall, narrow box: its sides sit a third
        // of the way to the Carrier, and its height makes it a drag rather than
        // a click (FinishSelect's 8-pixel rule).
        g.FocusCameraOn(cxf, czf, 8f);
        var a = ScreenAt(r1);
        var b = ScreenAt(r2);
        float side = Mathf.Abs(g.ScreenOf(cxf, czf).X - a.X) / 3f;
        g.ClearSelectionForTest();
        g.BoxSelect(new Vector2(Mathf.Min(a.X, b.X) - side, Mathf.Min(a.Y, b.Y) - 6f),
                    new Vector2(Mathf.Max(a.X, b.X) + side, Mathf.Max(a.Y, b.Y) + 6f));
        bool selSquads = g.SelectionCount == 2 && g.IsSelected(r1) && g.IsSelected(r2);
        string selDiag = $"selected [{string.Join(",", g.SelectedIdsForTest())}] of r1 #{r1} at {a}, r2 #{r2} at {b}, "
                         + $"carrier #{carrier} at {g.ScreenOf(cxf, czf)}";
        string over = g.CursorNameAt(g.ScreenOf(cxf, czf));
        int hold0 = lw.CargoOf(carrier).Count;
        g.PressRightClick(g.ScreenOf(cxf, czf));
        int loads = Loads(carrier);
        int strayMoves = 0;
        foreach (var c in g.PendingForTest) if (c.Type == CommandType.PathMove) strayMoves++;
        g.StepTicks(1);
        bool aboard = !lw.Entities[r1].Alive && !lw.Entities[r2].Alive && lw.CargoOf(carrier).Count == hold0 + 2;
        Gate(inReach && selSquads && over == "Enter" && loads == 2 && strayMoves == 0 && aboard, "LoadTransport",
             $"a drag selects two rifle squads beside an own Carrier, the cursor over it reads {over}, and a right click "
             + $"boards both: {loads} LoadTransport queued, {strayMoves} PathMove, hold {hold0} -> {lw.CargoOf(carrier).Count}"
             + $" (in reach {inReach}{(selSquads ? "" : $"; {selDiag}")})");
        _gateCovered.Add(CommandType.LoadTransport);

        // --- ...and the client keeps the order until it can land ------------
        // A squad three cells off is out of reach: the right click starts the
        // boarding and the client re-sends it the tick the Carrier comes within
        // reach (a fixture stands it there, the way a player drives it up), so
        // it boards with no second click.
        int r3 = SpawnOfType(lw, me, 2, s.X + 4, s.Y);
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool selR3 = ClickSelect(r3);
        bool r3OutOfReach = !InReach(r3, carrier);
        (cxf, czf) = PosOf(carrier);
        g.FocusCameraOn(cxf, czf, 22f);
        g.PressRightClick(g.ScreenOf(cxf, czf));
        int r3Loads = Loads(carrier);
        bool tracked = g.PendingBoardingsForTest == 1;
        for (int i = 0; i < 10 && lw.Entities[r3].Alive; i++) g.StepTicks(1);
        if (lw.Entities[r3].Alive)
        {
            var ce = lw.Entities[carrier];
            var re = lw.Entities[r3];
            ce.X = re.X - Fix64.One; ce.Y = re.Y;
            ce.PrevX = ce.X; ce.PrevY = ce.Y; ce.TargetX = ce.X; ce.TargetY = ce.Y;
            ce.Moving = false;
            lw.SetEntityForTest(carrier, ce);
        }
        for (int i = 0; i < 5 && lw.Entities[r3].Alive; i++) g.StepTicks(1);
        g.StepTicks(1);                      // the tick after: the client lets the finished boarding go
        Gate(selR3 && r3OutOfReach && r3Loads == 1 && tracked && !lw.Entities[r3].Alive
             && lw.CargoOf(carrier).Count == hold0 + 3 && g.PendingBoardingsForTest == 0, "LoadTransport",
             $"a squad ordered aboard from out of reach boards, with no second click, once its Carrier is beside it: the "
             + $"client kept the order and re-sent it (selected {selR3}, out of reach {r3OutOfReach}, {r3Loads} "
             + $"LoadTransport, held {tracked}, hold {lw.CargoOf(carrier).Count}, boardings still held "
             + $"{g.PendingBoardingsForTest})");

        // --- The readout: the hold and the key that empties it --------------
        g.PumpActorsForTest();               // the view catches up with the Carrier the fixture moved
        bool selCarrier = ClickSelect(carrier);
        g.StepOneTick();                     // the frame half writes the readout
        string readout = g.SelInfoText;
        // Read against the sim's hold as it stands, so a failure upstream is
        // reported there rather than again here.
        int holdNow = lw.CargoOf(carrier).Count;
        string wantHold = $"CARGO {holdNow}/{World.CarrierCapacity}";
        Check(selCarrier && holdNow > 0 && readout.Contains(wantHold) && readout.Contains($"{Settings.KeyName(unload)} unload"),
              $"inputgate/UnloadTransport: a selected Carrier reads its hold and names the unload key (\"{readout}\")");

        // --- The army key leaves the transport out ---------------------------
        g.ClearSelectionForTest();
        g.PressKey(Settings.BindOf("select_all_army"));
        var army = g.SelectedIdsForTest();
        Check(army.Count > 0 && !army.Contains(carrier),
              $"inputgate/LoadTransport: the army key selects the army ({army.Count}) and leaves the Carrier out, a transport and not army");

        // --- UnloadTransport: the unload key on a selected Carrier ----------
        selCarrier = ClickSelect(carrier);
        int riflesBefore = OwnRiflesNear(carrier);
        int heldBefore = lw.CargoOf(carrier).Count;
        g.PressKey(unload);
        int unloads = 0;
        foreach (var c in g.PendingForTest) if (c.Type == CommandType.UnloadTransport && c.EntityId == carrier) unloads++;
        g.StepTicks(1);
        int riflesAfter = OwnRiflesNear(carrier);
        Gate(selCarrier && unloads == 1 && heldBefore > 0 && lw.CargoOf(carrier).Count == 0
             && riflesAfter == riflesBefore + heldBefore, "UnloadTransport",
             $"a click selects the Carrier and the unload key sets its hold down beside it: {unloads} UnloadTransport, "
             + $"hold {heldBefore} -> {lw.CargoOf(carrier).Count}, rifle squads beside it {riflesBefore} -> {riflesAfter}");
        _gateCovered.Add(CommandType.UnloadTransport);
        g.PressKey(unload);
        int emptyUnloads = 0;
        foreach (var c in g.PendingForTest) if (c.Type == CommandType.UnloadTransport) emptyUnloads++;
        Check(emptyUnloads == 0 && g.ToastText.Contains("NOTHING ABOARD"),
              $"inputgate/UnloadTransport: on an empty Carrier the key sends nothing and says so (\"{g.ToastText}\")");
        g.StepTicks(1);
        g.ClearSelectionForTest();
    }

    /// <summary>
    /// P8-8: neutral targets. Neutral outposts pay their owner and stand on
    /// seven of the nine skirmish maps, and the AI claims them, but the attack
    /// pick took hostile seats only, so a human's engineer right-clicked onto
    /// one was sent a move. ADR-025's bridge spans on skirmish-04 and -05 could
    /// not be felled by any human at all. Decision D23 rules how: by the
    /// force-attack gesture alone, Ctrl plus right click, never by an ordinary
    /// right click, because a felled bridge does not come back. Both fixtures
    /// are spawned the way the maps place them (owner -1), so the stage is
    /// about the gesture, not about which map happens to carry one.
    /// </summary>
    private void RunNeutralTargetStages(SkirmishLive g)
    {
        GD.Print("  --    inputgate (P8-8): neutral targets, the Enter cursor for every contact unit, and decision D23");
        var lw = g.LiveWorld;
        int me = g.LocalPlayerId;
        (float X, float Z) PosOf(int id) => (Fx(lw.Entities[id].X), Fx(lw.Entities[id].Y));
        bool ClickSelect(int id)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            g.ClearSelectionForTest();
            g.BoxSelect(g.ScreenOf(x, z), g.ScreenOf(x, z));
            return g.SelectionCount == 1 && g.IsSelected(id);
        }
        string Over(int id, bool ctrl)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            return g.CursorNameAt(g.ScreenOf(x, z), ctrl);
        }
        int AttacksOn(int id)
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == CommandType.Attack && c.AuxId == id) n++;
            return n;
        }
        int Moves()
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == CommandType.PathMove) n++;
            return n;
        }
        void Hold(int id)
        {
            var e = lw.Entities[id];
            e.Stance = Stance.HoldFire;
            lw.SetEntityForTest(id, e);
        }

        var (ycx, ycy) = g.CellOfForTest(g.FindEntity(EntityKind.ConstructionYard, me));
        var q = QuietGround(lw, ycx, ycy);
        if (q is not { } s)
        {
            Check(false, "inputgate/Capture/neutral-outpost: open, quiet ground for the fixture (none found: a fixture failure, not a product one)");
            return;
        }

        // --- An unclaimed outpost, and every contact unit's cursor over it ---
        // The engineer stands at the far end of an open corridor east of the
        // footprint's centre cell, the contact stages' approach (D4's
        // single-cell walk-in). The other four contact units stand west of it,
        // unordered, for the cursor alone.
        int outpost = lw.SpawnOutpost(-1, s.X, s.Y);
        int eng = SpawnOfType(lw, me, World.EngineerUnitType, s.X + 4, s.Y + 1);
        var others = new (string Name, int Id, string Want)[]
        {
            ("saboteur", SpawnOfType(lw, me, World.SaboteurUnitType, s.X - 2, s.Y - 2), "Enter"),
            ("commando", SpawnOfType(lw, me, World.CommandoUnitType, s.X - 2, s.Y), "Enter"),
            ("shadow commando", SpawnOfType(lw, me, World.ShadowCommandoUnitType, s.X - 2, s.Y + 2), "Enter"),
            // The sim turns an infiltrator away from a neutral: no treasury to rob.
            ("infiltrator", SpawnOfType(lw, me, World.InfiltratorUnitType, s.X - 2, s.Y + 4), "Move"),
        };
        foreach (var o in others) Hold(o.Id);
        g.StepTicks(1);
        g.PumpActorsForTest();
        Check(lw.Entities[outpost].PlayerId < 0 && g.DrawnForLocalSeatForTest(outpost),
              "inputgate/Capture/neutral-outpost: an unclaimed outpost stands, owned by no seat and drawn (the precondition)");
        string cursors = "";
        bool cursorsOk = true;
        foreach (var o in others)
        {
            bool sel = ClickSelect(o.Id);
            string got = Over(outpost, false);
            cursors += $" {o.Name} {got};";
            if (!sel || got != o.Want) cursorsOk = false;
        }
        Gate(cursorsOk, "Capture/neutral-outpost",
             $"over an unclaimed outpost the saboteur, commando and shadow commando each read Enter, and the infiltrator, "
             + $"whom the sim refuses a treasury-less target, reads Move ({cursors.Trim().TrimEnd(';')})");

        // --- The engineer claims it ------------------------------------------
        bool selEng = ClickSelect(eng);
        string overOutpost = Over(outpost, false);
        var (ox, oz) = PosOf(outpost);
        g.PressRightClick(g.ScreenOf(ox, oz));
        int walkIn = 0;
        foreach (var c in g.PendingForTest)
            if (c.Type == CommandType.Attack && c.EntityId == eng && c.AuxId == outpost) walkIn++;
        bool captured = false;
        int ticks = 0;
        for (; ticks < 150 && !captured; ticks++)
        {
            g.StepTicks(1);
            foreach (var ev in lw.Events)
                if (ev.Type == GameEventType.Captured && ev.A == outpost && ev.B == me) captured = true;
        }
        Gate(selEng && overOutpost == "Enter" && walkIn == 1 && captured && lw.Entities[outpost].PlayerId == me,
             "Capture/neutral-outpost",
             $"a click selects an engineer, the cursor over the unclaimed outpost reads {overOutpost}, and a right click "
             + $"sends it in: {walkIn} Attack queued, Captured raised {captured} after {ticks} ticks, owner now "
             + $"{lw.Entities[outpost].PlayerId}");
        g.ClearSelectionForTest();

        // --- D23: a bridge span is felled by force-attack, and only by it ---
        var q2 = QuietGround(lw, ycx, ycy, (s.X, s.Y), 14);
        if (q2 is not { } w)
        {
            Check(false, "inputgate/Attack/neutral-bridge: a second patch of quiet ground (none found: a fixture failure)");
            return;
        }
        int span = lw.SpawnBridge(w.X + 3, w.Y);
        // An eighth of its hit points, so the fell takes seconds rather than a
        // minute: the gesture is under test here, not the damage model.
        var se = lw.Entities[span];
        se.Hp = System.Math.Max(1, se.MaxHp / 8);
        lw.SetEntityForTest(span, se);
        int tank = SpawnOfType(lw, me, UnitCatalogue.TypeIdOf("dir_cannon_tank"), w.X, w.Y);
        g.StepTicks(1);
        g.PumpActorsForTest();
        bool selTank = ClickSelect(tank);
        int hp0 = lw.Entities[span].Hp;
        string plainCursor = Over(span, false);
        var (bx, bz) = PosOf(span);
        g.PressRightClick(g.ScreenOf(bx, bz));
        int plainAttacks = AttacksOn(span), plainMoves = Moves();
        g.StepTicks(90);
        bool untouched = lw.Entities[span].Alive && lw.Entities[span].Hp == hp0 && lw.Entities[tank].ExplicitTarget != span;
        Gate(selTank && plainCursor == "Move" && plainAttacks == 0 && plainMoves == 1 && untouched, "Attack/neutral-bridge",
             $"an ORDINARY right click on a neutral bridge span is ground: cursor {plainCursor}, {plainMoves} PathMove, "
             + $"{plainAttacks} Attack, and 90 ticks on the span is {(untouched ? "untouched" : "STRUCK")} "
             + $"({lw.Entities[span].Hp}/{hp0} hp)");
        g.PressKey(Settings.BindOf("stop"));
        g.StepTicks(1);
        g.PumpActorsForTest();
        selTank = ClickSelect(tank);
        string forceCursor = Over(span, true);
        (bx, bz) = PosOf(span);
        g.PressRightClickWithCtrl(g.ScreenOf(bx, bz));
        int forceAttacks = AttacksOn(span);
        int fell = 0;
        for (; fell < 450 && lw.Entities[span].Alive; fell++) g.StepTicks(1);
        int scx = Map.CellOf(lw.Entities[span].X), scy = Map.CellOf(lw.Entities[span].Y);
        bool blocked = lw.Map.IsBlocked(scx, scy);
        Gate(selTank && forceCursor == "Attack" && forceAttacks == 1 && !lw.Entities[span].Alive && blocked,
             "Attack/neutral-bridge",
             $"Ctrl plus right click on the same span is the force-attack D23 rules: cursor {forceCursor}, "
             + $"{forceAttacks} Attack queued, the span "
             + $"{(lw.Entities[span].Alive ? "still STANDS after" : "falls after")} {fell} ticks, and its cell is "
             + $"{(blocked ? "now impassable" : "still open")}");
        g.ClearSelectionForTest();
    }

    /// <summary>
    /// P8-9: the Airfield. The sim has accepted a Produce at an airfield since
    /// ADR-028's follow-up and the panel has had an AIRCRAFT tab, but the tab's
    /// buttons read the Factory's line: with only an Airfield standing the
    /// Strike Flyer had no button, and with both standing its button showed
    /// the Factory's queue while its order went to the Airfield. And the
    /// client offered a rally on the Factory and Barracks only. The Factory the
    /// SetRally stage stood up is still standing here, which is the case that
    /// tells the two lines apart.
    /// </summary>
    private void RunAirfieldStages(SkirmishLive g)
    {
        GD.Print("  --    inputgate (P8-9): the Airfield's production line and its rally point");
        var lw = g.LiveWorld;
        var sb = g.SidebarView;
        int me = g.LocalPlayerId;
        int flyer = UnitCatalogue.TypeIdOf("com_strike_flyer");
        (float X, float Z) PosOf(int id) => (Fx(lw.Entities[id].X), Fx(lw.Entities[id].Y));
        bool ClickSelect(int id)
        {
            var (x, z) = PosOf(id);
            g.FocusCameraOn(x, z, 22f);
            g.ClearSelectionForTest();
            g.BoxSelect(g.ScreenOf(x, z), g.ScreenOf(x, z));
            return g.SelectionCount == 1 && g.IsSelected(id);
        }
        int ProducesAt(int producer)
        {
            int n = 0;
            foreach (var c in g.PendingForTest)
                if (c.Type == CommandType.Produce && c.AuxId == flyer && c.EntityId == producer) n++;
            return n;
        }
        int ProducesAnywhere()
        {
            int n = 0;
            foreach (var c in g.PendingForTest) if (c.Type == CommandType.Produce && c.AuxId == flyer) n++;
            return n;
        }

        int factory = g.FindEntity(EntityKind.Factory, me);
        if (factory < 0)
        {
            Check(false, "inputgate/Produce/Airfield: the SetRally stage's Factory still stands (the precondition)");
            return;
        }
        g.StepOneTick();                     // the frame half refreshes the panel
        string emptyNote = sb.TabEmptyNote(Sidebar.TabAircraft);
        bool hiddenWithFactory = factory >= 0 && g.FindEntity(EntityKind.Airfield, me) < 0 && !sb.UnitButtonVisible(flyer);
        g.QueueUnit(flyer);                  // the button's own handler, pressed with no Airfield
        int strayProduces = ProducesAnywhere();
        string toast = g.ToastText;
        Gate(hiddenWithFactory && emptyNote == "REQUIRES AN AIRFIELD" && strayProduces == 0 && toast.StartsWith("NO AIRFIELD"),
             "Produce/Airfield",
             $"with a Factory standing and no Airfield the Strike Flyer has no button, the AIRCRAFT tab says \"{emptyNote}\", "
             + $"and its handler pressed anyway queues nothing ({strayProduces} Produce) and says so (\"{toast}\")");

        // --- An Airfield beside the Factory: the button reads ITS line ------
        var site = g.FindPlacementCell(World.AirfieldStructType);
        if (site is not { } a)
        {
            Check(false, "inputgate/Produce/Airfield: a site for the Airfield (none found: a fixture failure, not a product one)");
            return;
        }
        int airfield = lw.SpawnAirfield(me, a.X, a.Y);
        // Power and credits enough that the line runs: the stage is about
        // which line the button reads, not about a brown-out or a treasury.
        if (g.FindPlacementCell(1) is { } pp) lw.SpawnPowerPlant(me, pp.X, pp.Y, supply: 1000);
        if (lw.Credits(me) < 3000) lw.GrantCredits(me, 3000 - lw.Credits(me));
        g.StepTicks(1);
        g.PumpActorsForTest();
        g.StepOneTick();
        g.StepOneTick();
        int fq0 = lw.QueueLength(factory), aq0 = lw.QueueLength(airfield);
        bool shown = sb.UnitButtonVisible(flyer) && sb.TabOfUnit(flyer) == Sidebar.TabAircraft;
        bool pressed = sb.PressUnitButton(flyer);
        int atAirfield = ProducesAt(airfield), atFactory = ProducesAt(factory);
        g.StepTicks(1);
        bool onAirfield = lw.QueueLength(airfield) == aq0 + 1 && lw.QueueLength(factory) == fq0;
        g.StepTicks(30);
        g.StepOneTick();                     // the frame half writes the button and the tab
        string suffix = sb.UnitButtonQueueSuffixForTest(flyer);
        string tab = sb.TabTitle(Sidebar.TabAircraft);
        bool progressing = lw.Entities[airfield].BuildProgress > 0;
        // The head's suffix is its seconds left, "  Ns", which only the line
        // that holds the flyer can give.
        bool readsLine = suffix.Trim().EndsWith("s") && suffix.Trim().Length > 1 && tab == "AIRCRAFT 1";
        Gate(shown && pressed && atAirfield == 1 && atFactory == 0 && onAirfield && progressing && readsLine,
             "Produce/Airfield",
             $"with an Airfield beside the Factory the Strike Flyer's button shows under AIRCRAFT and queues at the "
             + $"AIRFIELD ({atAirfield} Produce there, {atFactory} at the Factory; queues airfield {aq0} -> "
             + $"{lw.QueueLength(airfield)}, factory {fq0} -> {lw.QueueLength(factory)}), and shows that line's progress "
             + $"(button \"{suffix.Trim()}\", tab \"{tab}\", progress {lw.Entities[airfield].BuildProgress})");

        // --- The Airfield takes a rally --------------------------------------
        bool selAirfield = ClickSelect(airfield);
        var (afx, afz) = PosOf(airfield);
        float rx = Mathf.Clamp(afx + 3f, 1.5f, lw.Map.Width - 2.5f), rz = Mathf.Clamp(afz + 3f, 1.5f, lw.Map.Height - 2.5f);
        g.FocusCameraOn(rx, rz, 22f);
        g.PressRightClick(g.ScreenOf(rx, rz));
        int rallyCmds = 0;
        foreach (var c in g.PendingForTest) if (c.Type == CommandType.SetRally && c.EntityId == airfield) rallyCmds++;
        g.StepTicks(1);
        var ae = lw.Entities[airfield];
        bool rallied = ae.HasRally && Mathf.Abs(Fx(ae.RallyX) - rx) + Mathf.Abs(Fx(ae.RallyY) - rz) < 1.5f;
        g.StepOneTick();
        string readout = g.SelInfoText;
        Gate(selAirfield && rallyCmds == 1 && rallied && readout.Contains("right-click: rally"), "SetRally/Airfield",
             $"a click selects the Airfield, its readout offers the rally (\"{readout}\"), and a right click on the ground "
             + $"sets its rally point in the sim ({rallyCmds} SetRally, rally held {rallied})");
        g.ClearSelectionForTest();
    }

    /// <summary>
    /// The support powers can actually be used by a player. CommandType
    /// .UseSupportPower (P7-21) was issued by SkirmishAI and the battery and by
    /// nothing in game/scripts, so all five powers P7 shipped had no human
    /// trigger and the playtest brief's Match 3 could not be played. Each power
    /// building is spawned for this seat and stood up READY, and every order
    /// goes through the strip's own buttons, the real key, a real left click,
    /// or the commit a click runs, never through a hand-built Command.
    /// </summary>
    private void RunSupportPowerChecks()
    {
        GD.Print("  --    support powers: the player's fire control");
        var lw = _game.LiveWorld;
        int me = _game.LocalPlayerId;
        var bar = _game.SupportPowerView;
        Key key = Settings.BindOf("support_power");
        // A power building starts UNCHARGED (World.Add, P7-21: a timer you could
        // skip by rebuilding is not a timer), so each one is zeroed through the
        // sim's own scenario hook to stand it up READY.
        int SpawnCharged(int id)
        {
            var e = lw.Entities[id];
            e.ChargeTicks = 0;
            lw.SetEntityForTest(id, e);
            return id;
        }
        void Pump()
        {
            // The view first (the superweapon lesson: StepTicks cannot refresh
            // it inside one synchronous pass), then the strip's frame handover.
            _game.PumpActorsForTest();
            _game.PumpSupportPowersForTest();
        }
        int OwnUnits()
        {
            int n = 0;
            for (int i = 0; i < lw.EntityCount; i++)
            {
                var u = lw.Entities[i];
                if (u.Alive && u.PlayerId == me && u.Kind == EntityKind.Unit) n++;
            }
            return n;
        }
        // The sim sets the charge to full when it applies the order and its
        // charge loop may count one tick off it in the same Step, so "back to
        // full" is the full charge or one less. Anything lower means the order
        // never landed.
        bool Recharged(int id) => lw.Entities[id].ChargeTicks >= World.SupportPowerChargeTicks - 1;
        string Countdown(string text) => text.Substring(text.LastIndexOf(' ') + 1);

        // --- The key exists, and agrees with the rebind table ----------------
        // BindOf only answers for an action Settings captured, and it captures
        // only actions that are in BOTH the Bindable table and project.godot's
        // [input] block, so one non-empty answer proves the two agree.
        Check(key != Key.None && Settings.ConflictFor("support_power", key) == null,
              $"the support-power key has a binding of its own ({Settings.KeyName(key)}): project.godot and the "
              + "Bindable table agree, and no other action holds it");
        // NameOf throws on an id it cannot name (the UnitNameOf rule), so this
        // is what makes that throw unreachable for everything that ships.
        string unnamed = "";
        foreach (int t in lw.StructureTypeIds())
        {
            var granted = lw.GetStructureType(t).SupportPowerIds;
            if (granted == null) continue;
            foreach (int p in granted)
            {
                try { SupportPowerBar.NameOf(p); }
                catch (System.ArgumentOutOfRangeException) { unnamed += $" type {t} power {p};"; }
            }
        }
        Check(unnamed.Length == 0,
              $"every power any registered building grants has a name on the strip{(unnamed.Length > 0 ? $" (unnamed:{unnamed})" : "")}");

        // --- No power building: no strip, and the key refuses ----------------
        Pump();
        Check(!bar.Visible && bar.EntryCount == 0, "with no power building standing, the POWERS strip is hidden");
        _game.PressKey(key);
        Check(_game.SupportPowerArmed == null && _game.ToastText.StartsWith("NO SUPPORT POWERS"),
              $"...and the key refuses rather than arming, and says why (\"{_game.ToastText}\")");

        var (yx, yy) = _game.CellOfForTest(_game.FindEntity(EntityKind.ConstructionYard, me));

        // --- RADAR JAMMING: the untargeted power fires from its button -------
        int post = SpawnCharged(lw.SpawnWatchPost(me, yx - 6, yy + 4));
        Pump();
        int jam = bar.IndexOf(post, World.RadarJammingPowerId);
        Check(bar.Visible && jam >= 0 && bar.EntryText(jam).Contains("RADAR JAMMING")
              && bar.EntryText(jam).Contains("READY") && !bar.EntryGreyed(jam),
              $"a charged Watch Post puts RADAR JAMMING on the strip as READY (\"{(jam >= 0 ? bar.EntryText(jam) : "absent")}\")");
        if (jam >= 0)
        {
            bar.PressEntryForTest(jam);
            Check(_game.SupportPowerArmed == null && _game.PendingSupportPowerCommandsForTest == 1,
                  "pressing it FIRES at once, because a jam has no target, and queues exactly one UseSupportPower");
            _game.StepTicks(1);
            Check(Recharged(post) && _game.PendingSupportPowerCommandsForTest == 0,
                  $"the sim ACCEPTED it: the post's charge went back to full ({lw.Entities[post].ChargeTicks} of {World.SupportPowerChargeTicks})");
            Check(lw.IsRadarJammed(_game.EnemyPlayerId), "...and the opposition's radar is jammed, so the order did what it says");
            Pump();
            Check(bar.EntryGreyed(jam) && !bar.EntryText(jam).Contains("READY"),
                  $"...and the strip greys the button with a countdown (\"{bar.EntryText(jam)}\")");
        }

        // --- DECOY ARMY: the key arms it, the whole family clears it ---------
        int nest = SpawnCharged(lw.SpawnFactionDefence(me, 18, yx - 3, yy + 4));   // 18: the Shroud Nest
        Pump();
        _game.PressKey(key);
        Check(_game.SupportPowerArmed == (nest, World.DecoyArmyPowerId),
              "the key ARMS the first READY power (DECOY ARMY on the Shroud Nest; the jam is still charging)");
        Check(_game.CursorNameAt(new Vector2(400, 300)) == "Attack", "...and while armed the cursor wears the attack glyph");
        _game.PressStop();
        Check(_game.SupportPowerArmed == null, "STOP disarms an armed support power, because stop means stop");
        _game.PressKey(key);
        _game.PressKey(Settings.BindOf("cancel"));
        Check(_game.SupportPowerArmed == null && _game.ToastText.Contains("CANCELLED"),
              "...and so does the cancel key, saying so");
        _game.PressKey(key);
        _game.PressKey(Settings.BindOf("guard"));
        Check(_game.SupportPowerArmed == null, "...and so does guard");
        // The armed orders are exclusive both ways round, superweapon included
        // (the superweapon checks above left one standing, charged).
        _game.PressKey(key);
        _game.PressKey(Settings.BindOf("launch_super"));
        Check(_game.SuperArmed && _game.SupportPowerArmed == null, "arming the superweapon clears an armed support power");
        _game.PressKey(key);
        Check(_game.SupportPowerArmed != null && !_game.SuperArmed, "...and arming a support power clears the superweapon");
        _game.SelectAllOwn();
        _game.PressKey(Settings.BindOf("attack_move"));
        Check(_game.AttackMoveArmed && _game.SupportPowerArmed == null, "arming attack-move clears it");
        _game.PressKey(key);
        Check(_game.SupportPowerArmed != null && !_game.AttackMoveArmed, "...and arming it clears attack-move");
        _game.PressKey(Settings.BindOf("patrol"));
        Check(_game.PatrolArmed && _game.SupportPowerArmed == null, "arming patrol clears it");
        _game.PressKey(key);
        Check(_game.SupportPowerArmed != null && !_game.PatrolArmed, "...and arming it clears patrol");
        _game.PressStop();
        _game.ClearSelectionForTest();
        // Fired by a REAL left click through the real input path, so the
        // armed case in _UnhandledInput is proved wired, not just the commit.
        _game.PressKey(key);
        float dx = yx - 4 + 0.5f, dz = yy - 3 + 0.5f;
        _game.FocusCameraOn(dx, dz, 22f);
        int unitsBefore = OwnUnits();
        _game.PressLeftClick(_game.ScreenOf(dx, dz));
        Check(_game.SupportPowerArmed == null && _game.PendingSupportPowerCommandsForTest == 1,
              "a real left click on the ground fires the armed DECOY ARMY");
        _game.StepTicks(1);
        int decoys = OwnUnits() - unitsBefore;
        // P8-1: this is the stage the inputgate folds in for UseSupportPower, a
        // real left click through _UnhandledInput accepted by the sim, rather
        // than spawning every power building a second time.
        _supportPowerFiredByClick = Recharged(nest) && decoys > 0;
        Check(_supportPowerFiredByClick,
              $"the sim ACCEPTED it: the nest's charge went back to full and {decoys} decoys stand");

        // --- TUNNEL DEPLOYMENT: refused on ground the player cannot see ------
        int veil = SpawnCharged(lw.SpawnVeilProjector(me, yx - 9, yy + 2));
        Pump();
        int tunnel = bar.IndexOf(veil, World.TunnelDeploymentPowerId);
        Check(tunnel >= 0, "a charged Veil Projector puts TUNNEL DEPLOYMENT on the strip");
        if (tunnel >= 0)
        {
            bar.PressEntryForTest(tunnel);
            Check(_game.SupportPowerArmed == (veil, World.TunnelDeploymentPowerId),
                  "pressing a TARGETED power's button arms it rather than firing it");
            var (ex, ey) = _game.CellOfForTest(_game.FindEntity(EntityKind.ConstructionYard, _game.EnemyPlayerId));
            Check(!lw.IsVisible(me, ex, ey) && lw.IsVisible(me, yx, yy),
                  "the opposition's yard is out of sight and this seat's own yard is in it (the precondition)");
            _game.FireArmedSupportPowerAtForTest(ex + 0.5f, ey + 0.5f);
            Check(_game.SupportPowerArmed != null && _game.PendingSupportPowerCommandsForTest == 0
                  && lw.Entities[veil].ChargeTicks == 0 && _game.ToastText.Contains("SEE"),
                  $"a tunnel aimed at unseen ground is refused, says why and stays armed, so no charge is wasted (\"{_game.ToastText}\")");
            _game.FireArmedSupportPowerAtForTest(yx + 0.5f, yy + 0.5f);
            Check(_game.SupportPowerArmed == null && _game.PendingSupportPowerCommandsForTest == 1,
                  "...and aimed at ground in sight, it fires");
            _game.StepTicks(1);
            Check(Recharged(veil), "the sim ACCEPTED the tunnel: the projector's charge went back to full");
        }

        // --- The Bastion: two powers, ONE charge -----------------------------
        int bastion = SpawnCharged(lw.SpawnFactionDefence(me, 17, yx - 9, yy - 2));   // 17: the Bastion
        Pump();
        int scan = bar.IndexOf(bastion, World.OrbitalScanPowerId);
        int strike = bar.IndexOf(bastion, World.PrecisionStrikePowerId);
        Check(scan >= 0 && strike >= 0 && bar.EntryText(scan).Contains("READY") && bar.EntryText(strike).Contains("READY"),
              "a charged Bastion offers BOTH ORBITAL SCAN and PRECISION STRIKE, each READY");
        if (scan >= 0 && strike >= 0)
        {
            // Everything else is charging now, so the key walks the Bastion's
            // pair and wraps.
            _game.PressKey(key);
            Check(_game.SupportPowerArmed == (bastion, World.OrbitalScanPowerId), "the key arms ORBITAL SCAN first");
            _game.PressKey(key);
            Check(_game.SupportPowerArmed == (bastion, World.PrecisionStrikePowerId),
                  "...a second press while armed cycles to PRECISION STRIKE");
            _game.PressKey(key);
            Check(_game.SupportPowerArmed == (bastion, World.OrbitalScanPowerId), "...and a third wraps round to the scan");
            bar.PressEntryForTest(scan);
            Check(_game.SupportPowerArmed == null, "pressing the ARMED power's own button stands it down");
            bar.PressEntryForTest(scan);
            _game.FireArmedSupportPowerAtForTest(yx + 0.5f, yy + 0.5f);
            Check(_game.SupportPowerArmed == null && _game.PendingSupportPowerCommandsForTest == 1,
                  "the scan's button arms it and the ground pick fires it");
            _game.StepTicks(1);
            Pump();
            Check(Recharged(bastion), "the sim ACCEPTED the scan: the Bastion's charge went back to full");
            Check(bar.EntryGreyed(scan) && bar.EntryGreyed(strike)
                  && Countdown(bar.EntryText(scan)) == Countdown(bar.EntryText(strike)),
                  $"firing ONE spends the SHARED charge: both Bastion buttons grey out on the same countdown "
                  + $"(\"{bar.EntryText(scan)}\", \"{bar.EntryText(strike)}\")");
            bar.PressEntryForTest(strike);
            Check(_game.SupportPowerArmed == null && _game.ToastText.Contains("CHARGING"),
                  $"...so the strike now refuses, and says it is charging (\"{_game.ToastText}\")");
        }
        _game.PressKey(key);
        Check(_game.SupportPowerArmed == null && _game.ToastText.StartsWith("NO SUPPORT POWER READY"),
              $"with every power charging, the key refuses and names the soonest (\"{_game.ToastText}\")");
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
        RunTeamChecks();
    }

    /// <summary>
    /// P7-8h acceptance: the lobby can express a team, and the client reads one.
    ///
    /// ADR-038 recorded a coverage gap rather than claiming it away: the client's
    /// team-aware victory banner was NOT harness-covered, because SetTeam is
    /// refused after tick 0 and the harness world has already ticked by the time
    /// the banner checks run, so a check that called SetTeam mid-run hung the
    /// scene. The ADR named the two ways out and rejected the first: a
    /// guard-bypassing hook would weaken the tick-0 rule the sim relies on, and a
    /// second scene built TEAMED FROM THE START was "real work".
    ///
    /// A team MODE in MatchSetup is what makes the second one cheap, so it is
    /// done here. The scene below is a real Skirmish.tscn on the four-start map
    /// with EVEN SIDES in its setup, driven from seat 1 like everything else in
    /// this file - and seat 1 is exactly where getting the comparison wrong
    /// shows, because seat 1's teammate is a seat it is not.
    /// </summary>
    private void RunTeamChecks()
    {
        GD.Print("  --    P7-8h: teams reach the lobby");

        // 1. THE SIDECAR, backwards first. A file written before the mode
        //    existed describes a free-for-all, and that is what zero means -
        //    the same trap the difficulty rung documents, where decoding an
        //    absent field to the wrong default silently resumes a different
        //    match and reports DIVERGED with nothing in the diff to explain it.
        string legacy = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ferrostorm-verify-noteams.json");
        System.IO.File.WriteAllText(legacy,
            "{\"map\":\"skirmish-01\",\"map_path\":\"data/maps/skirmish-01.fmap\",\"mission\":0,"
            + "\"tick\":120,\"saved_at\":\"\",\"credits\":8000,\"ai_preset\":0,"
            + "\"ai_difficulty\":1,\"start_credits\":8000,\"seed\":2026,\"faction\":0,"
            + "\"opp_faction\":0,\"seats\":0}");
        var old = MatchMeta.Read(legacy);
        Check(old != null && old.Setup.TeamMode == MatchSetup.TeamsFreeForAll,
              "a sidecar written before the team mode decodes to FREE FOR ALL, which is what it was");
        System.IO.File.Delete(legacy);

        string sidecar = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "ferrostorm-verify-teams.json");
        MatchMeta.For(new MatchSetup { TeamMode = MatchSetup.TeamsEvenSides }, tick: 7, credits: 99)
                 .Write(sidecar);
        var back = MatchMeta.Read(sidecar);
        Check(back != null && back.Setup.TeamMode == MatchSetup.TeamsEvenSides,
              "a chosen team mode survives the sidecar round trip");
        System.IO.File.Delete(sidecar);

        // 2. THE WIRE, asserted ALONE for the reason RunLobbyChecks states about
        //    its own two: a new field bolted into an existing conjunction is how
        //    a field the encoder writes and the decoder never reads slips
        //    through, because one true clause among nine reads as green. Both
        //    peers must agree on this before tick 0 or they build different
        //    worlds, which is why the blob version moved to 4 rather than the
        //    field being appended quietly.
        var wired = MatchSetupBlob.Decode(MatchSetupBlob.Encode(
            new MatchSetup { TeamMode = MatchSetup.TeamsEvenSides }));
        Check(wired.TeamMode == MatchSetup.TeamsEvenSides,
              $"the team mode survives the wire round trip (came back {wired.TeamMode})");

        // 3. EVEN SIDES BUILDS THE WORLD IT PROMISES, read through the sim's own
        //    accessor rather than through the field that produced it: a mode
        //    that set its own flag and never called SetTeam would pass any check
        //    written against the setup.
        var map4 = MapData.Load(GameFiles.Abs("data/maps/skirmish-09.fmap"));
        var even = SkirmishLive.BuildStartingWorld(new MatchSetup
        {
            MapPath = "data/maps/skirmish-09.fmap",
            TeamMode = MatchSetup.TeamsEvenSides,
        }, map4, out _);
        Check(even.PlayerCount == 4, $"the four-start map seats four ({even.PlayerCount}) - the precondition");
        Check(even.TeamOf(0) == even.TeamOf(2),
              $"under EVEN SIDES seats 0 and 2 fight for ONE team ({even.TeamOf(0)} and {even.TeamOf(2)})");
        Check(even.TeamOf(0) != even.TeamOf(1),
              $"...and seats 0 and 1 do NOT ({even.TeamOf(0)} against {even.TeamOf(1)})");
        Check(even.TeamOf(1) == even.TeamOf(3),
              $"...which leaves seats 1 and 3 as the other side ({even.TeamOf(1)} and {even.TeamOf(3)})");

        // The control, without which the three above would read the same over a
        // world where every seat happened to share one team: the default mode
        // must still be four seats fighting four different wars.
        var ffa = SkirmishLive.BuildStartingWorld(new MatchSetup
        {
            MapPath = "data/maps/skirmish-09.fmap",
            TeamMode = MatchSetup.TeamsFreeForAll,
        }, map4, out _);
        bool allAlone = true;
        for (int p = 0; p < ffa.PlayerCount; p++) if (ffa.TeamOf(p) != p) allAlone = false;
        Check(allAlone, "FREE FOR ALL leaves every seat on a team of its own, which is the sim's own default");

        // 4. AND ON A TWO-START MAP THE TWO MODES ARE THE SAME MATCH, measured
        //    rather than argued. SetTeam(0, 0) and SetTeam(1, 1) write the
        //    identity map the world was built with, so the claim is testable as
        //    a hash rather than as a paragraph.
        var map2 = MapData.Load(GameFiles.Abs("data/maps/skirmish-02.fmap"));
        var duelFfa = SkirmishLive.BuildStartingWorld(new MatchSetup
        {
            MapPath = "data/maps/skirmish-02.fmap",
            TeamMode = MatchSetup.TeamsFreeForAll,
        }, map2, out _);
        var duelEven = SkirmishLive.BuildStartingWorld(new MatchSetup
        {
            MapPath = "data/maps/skirmish-02.fmap",
            TeamMode = MatchSetup.TeamsEvenSides,
        }, map2, out _);
        Check(duelFfa.ComputeStateHash() == duelEven.ComputeStateHash(),
              $"on a two-start map EVEN SIDES is byte-identical to FREE FOR ALL "
              + $"(0x{duelFfa.ComputeStateHash():X16} both)");

        // 5. THE BANNER, WHICH IS THE CHECK ADR-038 WANTED AND COULD NOT WRITE.
        //    A real scene, teamed before its first tick, driven from seat 1.
        //    World.Winner is a player id and the sim names the LAST STANDING SEAT
        //    of the winning team, so `winner == LocalPlayerId` shows the winner's
        //    own teammate a DEFEAT banner - right at every seat that happens to
        //    be the named one, and wrong for every ally.
        string? wasMap = MatchConfig.MapPath;
        int wasTeamMode = MatchConfig.TeamMode;
        SkirmishLive teamed;
        try
        {
            MatchConfig.MapPath = GameFiles.Abs("data/maps/skirmish-09.fmap");
            MatchConfig.TeamMode = MatchSetup.TeamsEvenSides;
            MatchConfig.Seats = 0;                 // fill the map: four seats
            SkirmishLive.AutoStep = false;
            SkirmishLive.LocalSeat = 1;
            SkirmishLive.PendingNet = null;        // offline: this is not a LAN scene
            teamed = GD.Load<PackedScene>("res://scenes/Skirmish.tscn").Instantiate<SkirmishLive>();
            AddChild(teamed);
        }
        finally
        {
            MatchConfig.MapPath = wasMap;
            MatchConfig.TeamMode = wasTeamMode;
        }

        Check(teamed.LiveWorld.PlayerCount == 4,
              $"the teamed scene came up on four seats ({teamed.LiveWorld.PlayerCount})");
        Check(teamed.LocalPlayerId == 1, $"...and took seat 1 ({teamed.LocalPlayerId})");
        // The preconditions, stated as measurements: seat 3 is my ally and seat 2
        // is not. Without these the two banner checks below would prove only that
        // a banner can say two things.
        Check(teamed.LiveWorld.TeamOf(3) == teamed.LiveWorld.TeamOf(teamed.LocalPlayerId),
              "seat 3 is my TEAMMATE in this world (the precondition)");
        Check(teamed.LiveWorld.TeamOf(2) != teamed.LiveWorld.TeamOf(teamed.LocalPlayerId),
              "...and seat 2 is an ENEMY (the control's precondition)");

        teamed.DeclareWinnerForTest(3);
        Check(teamed.BannerTextForTest.Contains("VICTORY"),
              $"my TEAMMATE being named the winner reads as VICTORY at seat 1 "
              + $"(\"{teamed.BannerTextForTest.Split('\n')[0]}\")");
        teamed.ResetVictoryForTest();
        teamed.DeclareWinnerForTest(2);
        Check(teamed.BannerTextForTest.Contains("DEFEAT"),
              $"...and an ENEMY winning is still DEFEAT (\"{teamed.BannerTextForTest.Split('\n')[0]}\")");
        teamed.ResetVictoryForTest();
        RunTeamHostilityChecks(teamed);
        RunTeamStealthChecks(teamed);
        teamed.QueueFree();
    }

    /// <summary>
    /// P8-6, decision D24, in the teamed world from seat 1 (ally seat 3, enemy
    /// seat 2): a cloaked enemy counts as DETECTED when any seat on my team has
    /// it in DetectedMask, so an ally's Sentinel Scout puts it on my screen.
    ///
    /// The half D24 could not deliver without a sim change is asserted too,
    /// because the cursor must not lie about it. The sim's CanTarget reads the
    /// attacker's OWN bit alone, so an Attack on an enemy only my ally has
    /// found is dropped the tick it lands. Drawn, tinted, and a move target
    /// until a detector of mine finds it; then attackable.
    /// </summary>
    private void RunTeamStealthChecks(SkirmishLive teamed)
    {
        GD.Print("  --    inputgate (P8-6): team detection, decision D24");
        var lw = teamed.LiveWorld;
        int me = teamed.LocalPlayerId;
        const int ally = 3, enemy = 2;
        int phantomType = UnitCatalogue.TypeIdOf("sod_phantom_tank");
        int scoutType = UnitCatalogue.TypeIdOf("dir_sentinel_scout");
        void Settle()
        {
            teamed.StepOneTick();
            teamed.StepOneTick();
            teamed.PumpActorsForTest();
        }
        int ownYard = teamed.FindEntity(EntityKind.ConstructionYard, me);
        if (ownYard < 0) { Check(false, "inputgate/stealth-team: seat 1 owns a yard (the precondition)"); return; }
        var (ycx, ycy) = teamed.CellOfForTest(ownYard);
        var q = QuietGround(lw, ycx, ycy);
        if (q is not { } s) { Check(false, "inputgate/stealth-team: open, quiet ground for the fixture (none found)"); return; }

        int rifle = SpawnOfType(lw, me, 2, s.X, s.Y);
        int phantom = SpawnOfType(lw, enemy, phantomType, s.X + 3, s.Y);
        var pe = lw.Entities[phantom];
        pe.Stance = Stance.HoldFire;
        lw.SetEntityForTest(phantom, pe);
        Settle();
        Check(lw.IsVisible(me, s.X + 3, s.Y) && lw.Entities[phantom].DetectedMask == 0 && !teamed.ActorShownForTest(phantom),
              "inputgate/stealth-team: an enemy Phantom Tank in a cell seat 1 can see, detected by nobody, is not drawn (the precondition)");

        SpawnOfType(lw, ally, scoutType, s.X + 1, s.Y - 2);       // my ALLY's detector
        Settle();
        byte mask = lw.Entities[phantom].DetectedMask;
        Check((mask & (1 << ally)) != 0 && (mask & (1 << me)) == 0,
              $"inputgate/stealth-team: my ALLY's Sentinel Scout detects it and I do not (the precondition: mask {mask})");
        Gate(teamed.DrawnForLocalSeatForTest(phantom) && teamed.ActorShownForTest(phantom) && teamed.ActorDetectedTintForTest(phantom),
             "stealth-team", "D24: a cloaked enemy my ally detects is DRAWN on my screen, wearing the detected tint");

        var (px, pz) = (Fx(lw.Entities[phantom].X), Fx(lw.Entities[phantom].Y));
        var (rx, rz) = (Fx(lw.Entities[rifle].X), Fx(lw.Entities[rifle].Y));
        teamed.FocusCameraOn(rx, rz, 22f);
        teamed.ClearSelectionForTest();
        teamed.BoxSelect(teamed.ScreenOf(rx, rz), teamed.ScreenOf(rx, rz));
        bool selRifle = teamed.SelectionCount == 1 && teamed.IsSelected(rifle);
        teamed.FocusCameraOn(px, pz, 22f);
        string overAllyFound = teamed.CursorNameAt(teamed.ScreenOf(px, pz));
        Gate(selRifle && overAllyFound == "Move", "stealth-team",
             $"...but the cursor over it offers {overAllyFound}, because Attack would be a promise the sim breaks: its "
             + "CanTarget takes only my own seat's detection");
        // The claim the line above rests on, measured rather than read: the
        // same Attack sent past the cursor is cleared by the sim the tick it
        // lands, because CombatSystem asks CanTarget for seat 1 alone.
        teamed.QueueCommandForTest(CommandType.Attack, rifle, phantom);
        teamed.StepTicks(1);
        Check(lw.Entities[rifle].ExplicitTarget != phantom && (lw.Entities[phantom].DetectedMask & (1 << me)) == 0,
              "inputgate/stealth-team: ...and the sim bears it out: an Attack sent past the cursor on the ally-detected "
              + $"Phantom Tank is dropped the tick it lands (target now {lw.Entities[rifle].ExplicitTarget})");

        SpawnOfType(lw, me, scoutType, s.X + 2, s.Y - 2);         // and now one of my own
        Settle();
        teamed.FocusCameraOn(px, pz, 22f);
        string overMineFound = teamed.CursorNameAt(teamed.ScreenOf(px, pz));
        teamed.PressRightClick(teamed.ScreenOf(px, pz));
        int attacks = 0;
        foreach (var c in teamed.PendingForTest) if (c.Type == CommandType.Attack && c.AuxId == phantom) attacks++;
        teamed.StepTicks(1);
        Gate((lw.Entities[phantom].DetectedMask & (1 << me)) != 0 && overMineFound == "Attack" && attacks == 1
             && lw.Entities[rifle].ExplicitTarget == phantom, "stealth-team",
             $"once my own Sentinel Scout detects it too, it is attackable: cursor {overMineFound}, {attacks} Attack queued, "
             + "and the sim holds it as the squad's target");
        teamed.ClearSelectionForTest();
    }

    /// <summary>
    /// P8-4, in the teamed world: from seat 1, seat 3 is the ally and seats 0
    /// and 2 the enemies. `1 - LocalPlayerId` named seat 0 alone, so seat 2,
    /// an enemy, was drawn through the fog. The ally is the control: never
    /// hidden, never offered as a target.
    /// </summary>
    private void RunTeamHostilityChecks(SkirmishLive teamed)
    {
        GD.Print("  --    inputgate (P8-4): an ally is not an enemy, and every enemy obeys the fog");
        var lw = teamed.LiveWorld;
        int me = teamed.LocalPlayerId;
        teamed.StepOneTick();
        teamed.StepOneTick();
        teamed.PumpActorsForTest();
        Check(!teamed.IsHostileSeat(me) && !teamed.IsHostileSeat(3) && teamed.IsHostileSeat(0) && teamed.IsHostileSeat(2)
              && !teamed.IsHostileSeat(-1),
              "inputgate/hostility: from seat 1 under EVEN SIDES, seats 0 and 2 are hostile, seat 3 (the ally), seat 1 and "
              + "a neutral are not");
        int allyYard = teamed.FindEntity(EntityKind.ConstructionYard, 3);
        int foeYard = teamed.FindEntity(EntityKind.ConstructionYard, 2);
        if (allyYard < 0 || foeYard < 0)
        {
            Check(false, $"inputgate/hostility: seats 2 and 3 each own a yard (the precondition; found {foeYard}, {allyYard})");
            return;
        }
        var (ax, ay) = teamed.CellOfForTest(allyYard);
        var (fx, fy) = teamed.CellOfForTest(foeYard);
        Check(!lw.IsVisible(me, ax, ay) && !lw.IsVisible(me, fx, fy),
              "inputgate/hostility: both yards stand in seat 1's fog (the precondition)");
        Check(!teamed.DrawnForLocalSeatForTest(foeYard) && !teamed.ActorShownForTest(foeYard),
              "inputgate/hostility: seat 2's yard, an ENEMY in fog, is not drawn (it was, because it is not 1 - LocalPlayerId)");
        Check(teamed.DrawnForLocalSeatForTest(allyYard) && teamed.ActorShownForTest(allyYard),
              "inputgate/hostility: seat 3's yard, my TEAMMATE's, is drawn wherever it stands");
        teamed.SelectAllOwn();
        var (axf, azf) = (Fx(lw.Entities[allyYard].X), Fx(lw.Entities[allyYard].Y));
        teamed.FocusCameraOn(axf, azf, 22f);
        string overAlly = teamed.CursorNameAt(teamed.ScreenOf(axf, azf));
        Check(overAlly == "Move", $"inputgate/hostility: over the ally's yard, with my own units selected, the cursor offers Move, never Attack ({overAlly})");
        teamed.ClearSelectionForTest();
    }

    /// <summary>
    /// P8-4 on skirmish-09, four seats, free for all, from seat 0. Two things
    /// the tracker names: hostility by team reaches every seat, so a seat 3
    /// unit in fog is hidden and, once in sight, a right click attacks it;
    /// and LocalSeat, set only by the LAN join path, is consumed by the scene
    /// that reads it, so this scene, handed no seat after the LAN joiner's
    /// scene was handed seat 1, comes up in seat 0.
    /// </summary>
    private void RunFourSeatHostilityChecks()
    {
        GD.Print("  --    inputgate (P8-4): hostility by team on the four-seat map, and the seat a single-player match takes");
        Check(SkirmishLive.LocalSeat == 0,
              $"inputgate/hostility: the seat handoff was consumed by the scene that took it (LocalSeat {SkirmishLive.LocalSeat})");
        string? wasMap = MatchConfig.MapPath;
        int wasTeamMode = MatchConfig.TeamMode, wasSeats = MatchConfig.Seats;
        SkirmishLive ffa;
        try
        {
            MatchConfig.MapPath = GameFiles.Abs("data/maps/skirmish-09.fmap");
            MatchConfig.TeamMode = MatchSetup.TeamsFreeForAll;
            MatchConfig.Seats = 0;                 // fill the map: four seats
            SkirmishLive.AutoStep = false;
            SkirmishLive.PendingNet = null;
            // LocalSeat deliberately NOT set: the menu's single-player road
            // never sets it, which is the whole of the defect.
            ffa = GD.Load<PackedScene>("res://scenes/Skirmish.tscn").Instantiate<SkirmishLive>();
            AddChild(ffa);
        }
        finally
        {
            MatchConfig.MapPath = wasMap;
            MatchConfig.TeamMode = wasTeamMode;
            MatchConfig.Seats = wasSeats;
        }
        var lw = ffa.LiveWorld;
        int me = ffa.LocalPlayerId;
        Check(me == 0, $"inputgate/hostility: a single-player scene booted after the LAN joiner's seat-1 scene, handed no seat, "
                       + $"takes seat 0 (took {me})");
        bool ffaTeams = lw.PlayerCount == 4;
        for (int p = 0; p < lw.PlayerCount; p++) if (lw.TeamOf(p) != p) ffaTeams = false;
        Check(ffaTeams, $"inputgate/hostility: skirmish-09 seats four, each its own team ({lw.PlayerCount} seats)");
        Check(!ffa.IsHostileSeat(0) && ffa.IsHostileSeat(1) && ffa.IsHostileSeat(2) && ffa.IsHostileSeat(3) && !ffa.IsHostileSeat(-1),
              "inputgate/hostility: in a free-for-all every other seat is hostile, and a neutral is not");
        ffa.StepOneTick();
        ffa.StepOneTick();
        ffa.PumpActorsForTest();

        // --- Seats 2 and 3 obey the fog --------------------------------------
        int y2 = ffa.FindEntity(EntityKind.ConstructionYard, 2);
        int y3 = ffa.FindEntity(EntityKind.ConstructionYard, 3);
        if (y2 < 0 || y3 < 0)
        {
            Check(false, $"inputgate/hostility: seats 2 and 3 each own a yard (the precondition; found {y2}, {y3})");
            ffa.QueueFree();
            return;
        }
        var (c2x, c2y) = ffa.CellOfForTest(y2);
        var (c3x, c3y) = ffa.CellOfForTest(y3);
        Check(!lw.IsVisible(me, c2x, c2y) && !lw.IsVisible(me, c3x, c3y),
              "inputgate/hostility: seats 2 and 3's yards stand in seat 0's fog (the precondition)");
        Check(!ffa.DrawnForLocalSeatForTest(y2) && !ffa.ActorShownForTest(y2)
              && !ffa.DrawnForLocalSeatForTest(y3) && !ffa.ActorShownForTest(y3),
              "inputgate/hostility: seat 2's and seat 3's yards in fog are NOT drawn (both were, because neither is 1 - LocalPlayerId)");
        int hostileDots = 0;
        foreach (var c in ffa.MinimapView.DotColoursForTest())
            for (int p = 1; p < lw.PlayerCount; p++)
                if (c == BattlefieldView.MarkFor(p)) hostileDots++;
        Check(hostileDots == 0,
              $"inputgate/hostility: no minimap dot wears seat 1's, 2's or 3's mark while all three are in fog ({hostileDots})");

        // --- The brown-out table answers for every seat ----------------------
        string grids = "";
        bool gridsAgree = true;
        for (int p = 0; p < lw.PlayerCount; p++)
        {
            int supply = 0, draw = 0;
            for (int i = 0; i < lw.EntityCount; i++)
            {
                var e = lw.Entities[i];
                if (e.Alive && e.PlayerId == p) { supply += e.PowerSupply; draw += e.PowerDraw; }
            }
            bool shown;
            try { shown = ffa.BrownedOutForTest(p); }
            catch (System.IndexOutOfRangeException) { gridsAgree = false; grids += $" seat {p}: no entry;"; continue; }
            if (shown != SkirmishLive.BrownedOut(supply, draw)) { gridsAgree = false; grids += $" seat {p}: {shown};"; }
        }
        Check(gridsAgree, $"inputgate/hostility: the per-owner brown-out table has an entry for all four seats and each matches "
                          + $"that seat's own grid{(grids.Length > 0 ? $" (wrong:{grids})" : "")}");

        // --- P8-5: seat 3 in fog is not pickable either --------------------
        // With hostility by team alone, seat 3 became pickable everywhere, fog
        // or not; the fog half of the pick is what keeps it a fair fight.
        {
            ffa.SelectAllOwn();
            int fogOwn = ffa.SelectionCount;
            var (fx3, fz3) = (Fx(lw.Entities[y3].X), Fx(lw.Entities[y3].Y));
            ffa.FocusCameraOn(fx3, fz3, 22f);
            string overFog3 = ffa.CursorNameAt(ffa.ScreenOf(fx3, fz3));
            ffa.PressRightClick(ffa.ScreenOf(fx3, fz3));
            int fogAttacks = 0, fogMoves = 0;
            foreach (var c in ffa.PendingForTest)
            {
                if (c.Type == CommandType.Attack && c.AuxId == y3) fogAttacks++;
                if (c.Type == CommandType.PathMove) fogMoves++;
            }
            Check(fogOwn > 0 && overFog3 == "Move" && fogAttacks == 0 && fogMoves > 0,
                  $"inputgate/fog-pick: on skirmish-09, seat 3's yard in fog is not a target: the cursor reads {overFog3} and a "
                  + $"right click is a move ({fogMoves} PathMove, {fogAttacks} Attack)");
            ffa.StepTicks(1);
            ffa.ClearSelectionForTest();
        }

        // --- Seat 3 in sight is attackable by right click -------------------
        // A seat-0 squad stood three cells from seat 3's yard, towards the
        // middle of the map: its sight puts the yard in seat 0's view.
        var (y3x, y3z) = (Fx(lw.Entities[y3].X), Fx(lw.Entities[y3].Y));
        float tx = lw.Map.Width / 2f - y3x, tz = lw.Map.Height / 2f - y3z;
        float tl = Mathf.Max(0.001f, Mathf.Sqrt(tx * tx + tz * tz));
        int scout = SpawnOfType(lw, me, 2, (int)(y3x + tx / tl * 3f), (int)(y3z + tz / tl * 3f));   // 2: com_rifle_squad
        ffa.StepTicks(1);
        ffa.PumpActorsForTest();
        Check(ffa.DrawnForLocalSeatForTest(y3) && ffa.ActorShownForTest(y3),
              "inputgate/hostility: with a seat-0 squad beside it, seat 3's yard is drawn");
        var (sxf, szf) = (Fx(lw.Entities[scout].X), Fx(lw.Entities[scout].Y));
        ffa.FocusCameraOn(sxf, szf, 22f);
        ffa.ClearSelectionForTest();
        ffa.BoxSelect(ffa.ScreenOf(sxf, szf), ffa.ScreenOf(sxf, szf));
        bool selScout = ffa.SelectionCount == 1 && ffa.IsSelected(scout);
        ffa.FocusCameraOn(y3x, y3z, 22f);
        string overSeat3 = ffa.CursorNameAt(ffa.ScreenOf(y3x, y3z));
        ffa.PressRightClick(ffa.ScreenOf(y3x, y3z));
        int attacks = 0;
        foreach (var c in ffa.PendingForTest) if (c.Type == CommandType.Attack && c.AuxId == y3) attacks++;
        ffa.StepTicks(1);
        Check(selScout && overSeat3 == "Attack" && attacks == 1 && lw.Entities[scout].ExplicitTarget == y3,
              $"inputgate/hostility: seat 3's yard in sight is an attack target: the cursor reads {overSeat3}, the right click "
              + $"queues {attacks} Attack and the sim holds it as the squad's target (it could not be picked at all: "
              + "it was never 1 - LocalPlayerId)");
        ffa.QueueFree();
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
