# Q005: the skirmish starting hand lives in the client, outside golden coverage

Owner: Architect
Raised by: play-test root-cause investigation, 2026-07-16
Decide by: before any further work on skirmish balance or the golden scenarios

## The finding

The opening hand that every real skirmish begins with is authored in CLIENT
code and is not covered by any golden hash.

- `game/scripts/SkirmishLive.cs:347-356` (`BuildStartingWorld`) grants credits,
  spawns two construction yards, and then spawns the actual starting force:
  one harvester and three rifle squads per player.
- `sim/Ferrostorm.Sim.Runner/Program.cs:464-476` (`BuildSkirmishWorld`), which
  is what the gated `skirmish` scenario runs, grants credits and spawns two
  construction yards and NOTHING ELSE.
- `MapLoader.BuildWorld` deliberately does not place starting forces; its own
  comment says "the scenario places starting forces at Starts".

So the sim's own gate and the client disagree about what a skirmish start IS.
The golden `skirmish` hash proves the determinism of a world that no player
ever plays.

## Why it matters

1. It is a test-coverage hole in the one place the project is most careful:
   gameplay state that is hashed everywhere else is unhashed here.
2. It already hid a real inconsistency. `SkirmishLive.cs:352-355` places
   starting units with `Fix64.FromInt(cell)` (cell CORNERS, measured at exactly
   (14.000, 18.000)), whereas `MapLoader.BuildWorld` and `World.cs:1688` use
   `Map.CellCentre` (hence the .50 coordinates on every AI-produced unit).
   Nothing caught it because nothing hashes it.
3. ADR-001 makes the sim the authority on gameplay. A starting force is
   gameplay, not presentation, so on the ADR's own terms it is in the wrong
   layer. ADR-004's UE5 renderer-swap plan makes this concrete: a UE5 client
   would have to reimplement the opening hand from scratch, and any divergence
   would be a desync between clients rather than a cosmetic difference.

## The question

Should starting forces move into the sim (MapLoader, or a scenario definition
in /data) so that the golden skirmish scenario exercises the world players
actually play?

## The cost, stated honestly so it is not discovered late

Moving them WILL move the `skirmish` golden hash: the gated scenario's world
would gain a harvester and three squads per side. That is a replay-compatibility
break and per CLAUDE.md needs an ADR plus Architect sign-off. It should be done
as its own change with its own ADR, and NOT bundled with the CellCentre fix
below, or the hash movement will land as an unexamined side effect of a
"tidy-up".

Correcting `Fix64.FromInt` to `Map.CellCentre` at `SkirmishLive.cs:352-355` is
hash-neutral TODAY (the golden scenario never calls `BuildStartingWorld`), but
it is NOT behaviour-neutral: measured, it moves the skirmish-04 idle-player
defeat from tick 4192 to tick 3642, and wipes the starting units at 2719 rather
than 3458. Same winner, 550 ticks earlier. So it is a balance-affecting change
wearing the clothes of a consistency fix, and wants Balance's eyes as well.

## Evidence

Both files read directly during the 2026-07-16 investigation; the coordinates
above are measured from instrumented runs, not inferred.

## RESOLVED 2026-07-20 (P6 Wave B5, ADR-011 ratified)

Both halves are resolved in one change, as ADR-011 decided, with the CellCentre
choice made explicit and Balance co-signed rather than smuggled in as a tidy-up.

The structural half: the skirmish opening hand now lives in the sim, as
MapData.PlaceSkirmishStart in sim/Ferrostorm.Sim/MapLoader.cs. The runner's gated
skirmish scenario and the client's SkirmishLive.BuildStartingWorld both call it,
so the sim's own gate and the client no longer disagree about what a skirmish
start is, and the golden hash covers the world players actually play. A starting
force is gameplay, so it now sits in the sim layer ADR-001 requires, and a future
renderer swap (ADR-004) inherits the hand rather than reimplementing it.

The CellCentre half: the harvester and the three rifle squads are now placed at
Map.CellCentre, the convention of every other sim spawn, ending the corner split
this question measured. It was not bundled as a silent side effect: ADR-011 named
it, measured it and had Balance co-sign it. The measured 550-tick outcome shift
on skirmish-04 is expected and authorised, recorded for Balance in the Wave B5
delivery notes.

The move regenerated exactly one golden (skirmish, 0x4F6252B168468346 to
0x3CE6E400F07A3AA1), the single row ADR-011 authorised; a scratch neutralisation
reverted all twenty-four rows to the pre-wave hashes, proving the hand plus the
centring is the sole cause. Full detail in docs/tickets/P6-wave-b5-starting-hand.md.
