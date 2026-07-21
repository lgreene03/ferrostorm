# Q013: the nightly soak has been red for over a week, pathing fails to settle

Owner: sim-engineer + architect
Raised by: CI audit, 2026-07-20
Decide by: before the next milestone gate; the soak is a standing red

## The finding

The `nightly-soak` scheduled workflow has failed EVERY night for at least
eight consecutive days (2026-07-13 through 2026-07-20, runs 29227423466
through 29720218794), and nobody surfaced it. The push-triggered
`determinism` gate is green on every merge, so the failure was invisible to
anyone watching pull requests: the merge gate runs the standard battery at
seed 2026, and the soak is the only job that runs other seeds.

The soak (`.github/workflows/nightly-soak.yml`) runs
`determinism <seed>` across five seeds (2026, 31337, 424242, 777, 900913)
plus a 20-game LAN soak. Seed 900913 aborts. Reproduced locally,
byte-for-byte with CI:

```
determinism [movement]: double-run identical, final=0x7ADCEF1B9B00FA56
Unhandled exception. System.Exception: pathing: units failed to settle within budget ticks
   at Program.<<Main>$>g__ScenarioPathing|0_3 ... Program.cs:line 89
```

Determinism itself is INTACT: the movement double-run is bit-identical. The
crash is not a desync. It is `ScenarioPathing` (Program.cs:65-105): a crowd
is ordered through wall gaps to a target and the scenario throws if not every
unit has stopped moving (`!e.Moving`) within `maxTicks = 3000`. At seed
900913 the crowd never fully settles.

## The likely mechanism

The stall-drop at World.cs (the `if (e.StallTicks >= 4 * TicksPerSecond)`
block) is the safety net that stops a wedged unit after four seconds. Its
leak is deliberately asymmetric, "+2 blocked / -1 progressing", to ride out
rim churn. A unit that alternates evenly between one blocked tick and one
progressing tick therefore keeps `StallTicks` low forever and never trips the
drop, so it can oscillate at a gap mouth indefinitely without ever setting
`Moving = false`. Seed 900913 appears to arrange the crowd so at least one
unit lands in that persistent oscillation; seed 2026 packs cleanly, which is
why the merge gate never sees it. This is a hypothesis from reading, not yet
proven by instrumentation; the fix wave should confirm which units, how many,
and whether it is oscillation or a genuine two-unit cell-swap cycle.

## Why it was not caused by recent work

The run that failed on 2026-07-20 checked out `35d511f`, which predates both
the map redesign and the visual V3 wave. The failure is present across all
eight days at the commits current on each night, so it is a longstanding
convergence weakness in the crowd-settle logic, not a regression from any
2026-07-16-onward wave.

## The decision, and why it is not a quick fix

The fix is in `World.cs`'s stall/settle logic, which is hashed sim state, so
any change to when a unit stops moving WILL move golden hashes and needs a
ratified ADR plus a regeneration under the doc 23 section 6 discipline. It
must not be smuggled into an unrelated wave. Candidate directions for the fix
wave to weigh:

1. A hard settle deadline: past some tick count with no net displacement over
   a window, force `Moving = false` regardless of the +2/-1 leak. Simplest,
   but changes crowd behaviour and moves hashes.
2. Detect the two-tick oscillation directly (position repeats within epsilon
   across a short cycle) and drop it.
3. Raise the scenario budget above 3000 ticks, if measurement shows the crowd
   DOES settle eventually and 3000 is merely too tight. Cheapest if true, and
   it should be checked FIRST: run the scenario to, say, 20000 ticks and see
   whether `arrivedTick` ever resolves. If it does, this is a test-budget
   issue, not a sim bug, and the fix is a one-line budget change plus a note.

The fix wave should start with option 3's measurement, because if the crowd
settles at tick 4000 the whole thing is a too-tight budget rather than a
convergence defect, and that is a very different change.

## Owed

A sim-engineer wave to root-cause by instrumentation (which units, how many,
oscillation versus cell-swap, and whether 3000 ticks is simply too tight),
then fix under its own ADR if the fix moves hashes. Until then the soak stays
red and should be understood as this one known scenario, not a determinism
failure.

## RESOLVED (2026-07-20, branch fix/q013-soak-pathing-settle, ADR-014)

It was CASE B: a genuine convergence defect, not a too-tight budget. Option 3
was measured first and ruled out.

The measurement (scratch copy of ScenarioPathing, not committed; the reported
numbers are on the unfixed sim). At seed 900913 exactly TWO of the 500 units,
ids 27 and 151, never settle, even out to 40000 ticks; the count still moving
holds at exactly 2 from tick 3000 onward, a stable limit cycle rather than slow
convergence. The other 498 settle by tick 365. Unit 27 is a clean THREE-tick
position cycle at cell (42,21), about 20.8 cells short of the target, with
`StallTicks` oscillating 26/27/28 and never reaching its 60-tick drop. Unit 151
orbits cell (41-42,23) at a similar distance with `StallTicks` bouncing 0/1/2.
Both keep the leaky `+2 blocked / -1 progressing` accumulator balanced forever,
exactly the mechanism this ticket hypothesised. Not a two-unit cell-swap; two
independent single-unit orbits. Raising the budget to 6000/12000/20000/40000
changes nothing, which is what makes it CASE B.

Across all five soak seeds only 900913 fails. The longest LEGITIMATE plateau
(consecutive ticks a unit that ultimately settles goes without bettering its
nearest approach) is 132 ticks, at seed 424242 (seed 2026's worst is 120), so
any deadline must clear 132.

The fix (ADR-014). A monotone no-progress backstop in SeparationSystem,
additive to and independent of the existing `StallTicks` net, benches a
flow-pathing unit that has not bettered its nearest approach to the destination
for `NoProgressDeadline` = 14 * TicksPerSecond (210 ticks, 14s: 3.5x the
existing 4s net, a 59% margin over the 132-tick worst-case legitimate plateau).
Nearest-approach, not displacement, because the orbiters keep moving but never
get closer. Two new hashed and serialised per-entity fields (save v6). On the
fixed sim the whole seed-900913 crowd settles by tick 499, and units 27 and 151
are the only two whose plateau reaches the deadline.

Verification. Full standard battery exit 0; the five-seed determinism suite
(2026, 31337, 424242, 777, 900913) plus the 20-game LAN soak all exit 0; both
client builds (Debug and ExportRelease) zero warnings; project.godot and user
data untouched. Goldens regenerated under ADR-014 and byte-identical to the
committed file; neutralisation proved by deadline-disabled vs deadline-210
seed-2026 goldens being identical across all 24 scenarios, so every golden move
is the mechanical field-append with zero behavioural change at the golden seed.

The nightly soak will now go green: the only seed it ran that aborted was
900913, which now settles and exits 0 alongside the other four, and the LAN
20-game soak passes. The push gate's cross-platform golden check passes against
the regenerated file.
