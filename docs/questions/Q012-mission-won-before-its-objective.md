# Q012: a scripted mission can be won by elimination before its stated objective completes

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
