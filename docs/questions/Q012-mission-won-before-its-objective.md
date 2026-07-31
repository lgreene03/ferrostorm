# Q012: a scripted mission can be won by elimination before its stated objective completes

**ANSWERED AND CLOSED, 2026-08-01: fork 3, both are wins.** Decided under the
standing P7 directive rather than by the Producer, because the question was
blocking P7-9 and had passed its decide-by date. See "The answer" below.

Labels: persona:p1, gdd:s8, phase:2, owner:producer, qa
Raised by: sim-engineer, during the ADR-010 attack-move fix (fix/amove-prosecute-base).
Decide-by: 2026-07-31 (before mission-02/03 acceptance tests are written, or they inherit the same ambiguity).

## Question

Mission-01's objective is `trigger destroyed camp -> win 0`, where the tagged
camp is three structures AND two rifle squads. But `VictorySystem` declares the
same winner the moment player 1's last STRUCTURE dies, regardless of the two
squads. Which of these is the mission's actual win condition?

## How it surfaced

ADR-010 made attack-move prosecute bases properly. The AI's waves now focus
structures instead of milling among the camp units, so the last camp structure
dies while a rifle squad still lives on 10 hp, and `VictorySystem` ends the
mission with the scripted trigger never having fired. The gate's ScenarioMission
asserted "winner implies every camp entity dead", which had only ever held by
accident of kill order. The assertion has been narrowed to the provable
invariant (winner 0 and every camp STRUCTURE razed); this question owns the
rest.

## The design fork

1. **Elimination is a legitimate mission win.** Then mission-01's trigger should
   tag only the structures, and briefing text should not imply the squads
   matter. One line in mission-01.fmap; no sim change; no hash move.
2. **A scripted mission's objective is the only win.** Then mission worlds
   should run with the victory test suppressed (the balance tool and the walls
   gate already set `ShortGameEnabled = false` for exactly this reason) and
   only `MissionRunner` may call `DeclareWinner`. Sim-visible change: the
   mission and campaignsave hashes move, so it needs an ADR and sign-off, and
   the player-loss path (yard dies) must be re-specified explicitly.
3. **Both are wins** (current de-facto behaviour). Then the fmap trigger should
   still drop the two `unit` lines from the camp tag, because as written the
   trigger describes a condition that can never decide the mission when
   elimination gets there first.

## Needed from whom

- **producer:** which fork; it is a player-facing promise question.
- **qa:** the acceptance invariant for mission-02/03 tests, which are currently
  unwritten and will copy whatever mission-01 does.
- **sim-engineer:** the ADR if fork 2 is chosen.


## The answer

**Fork 3. Elimination and a scripted objective are BOTH legitimate wins**, and
mission-01's camp tag now covers its three structures only.

Fork 2 was the tempting one and it is wrong for a reason worth writing down: it
would make a mission unwinnable by force. No mission in either benchmark game
worked that way - you could always finish a scripted objective by simply
destroying everything, and the objective existed to tell you the SHORT way, not
the only way. Suppressing the victory test would have turned every mission into
a puzzle with one solution, and it would have moved two golden hashes to do it.

Fork 1 and fork 3 differ only in whether the trigger is *also* allowed to win.
Fork 3 keeps it, costs nothing, and is what the code already did.

### What changed

`data/missions/mission-01.fmap`: the two `unit` lines dropped out of the `camp`
tag. They are still there, still fight, still have to be dealt with if they get
in the way - they are simply no longer part of a condition that could never
decide the mission, because elimination always got there first.

The gate assertion in `ScenarioMission` was NARROWED once to work around this
(winner 0 and every camp STRUCTURE razed). It is now widened back to the full
invariant - winner 0 and every tagged camp entity dead - because with the tag
describing what the objective actually waits for, the full statement is provable
again. **Measured neutral:** all 24 goldens byte-identical, because the trigger
and elimination fire on the same tick and `DeclareWinner` latches the first.

### What this unblocked

Mission 05 ("Skyfall") is the mission this answer makes writable. Its objective
is a single tagged AIRFIELD which is a strict subset of the enemy's holdings, so
it fires while the base still stands. Under fork 2 that mission could not exist;
under fork 1 it would be a lie. It is worth noticing that the answer to a
question filed as a defect turned out to be a feature the campaign needed.
