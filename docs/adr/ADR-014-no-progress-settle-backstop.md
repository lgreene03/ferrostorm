# ADR-014: a no-progress backstop settles rim-locked crowd units

- Status: Ratified (sim-engineer authored 2026-07-20; ratified by Luke via the Q013 fix directive, which mandates the measure-then-fix wave and, for the CASE B outcome it anticipates, a golden regeneration under this ADR)
- Date: 2026-07-20
- Deciders: Architect + sim-engineer + Luke
- GDD/TDD feature served: TDD s3 crowd movement; Q013 (the nightly soak has been red for over a week because ScenarioPathing fails to settle at seed 900913)

## Context

The `nightly-soak` workflow has failed every night for over a week. It runs
the determinism suite across five seeds and only seed 900913 aborts, in
ScenarioPathing, with "pathing: units failed to settle within budget ticks".
The push gate runs seed 2026 alone, which packs cleanly, so the failure was
invisible on pull requests. Q013 required measuring option 3 (is the 3000-tick
budget simply too tight?) BEFORE assuming a convergence defect.

Measurement (recorded on Q013, instrumented on a scratch copy of the scenario,
not committed). At seed 900913 exactly two of the 500 units, ids 27 and 151,
NEVER settle, even out to 40000 ticks; the other 498 settle by tick 365.
Unit 27 sits in a clean three-tick position cycle at cell (42,21), about 20.8
cells short of the target; unit 151 orbits cell (41-42,23) at a similar
distance. Both keep the World.cs stall-drop's `StallTicks` accumulator below
its four-second (60-tick) threshold forever, because that accumulator leaks
`+2` on a blocked tick and `-1` on a progressing one and their blocked and
progressing ticks balance exactly. This is CASE B in Q013's framing: a genuine
convergence defect, not a too-tight budget. Raising the budget does nothing.

Across all five soak seeds only 900913 fails. The longest LEGITIMATE plateau
(consecutive ticks a unit that ultimately settles goes without bettering its
nearest approach to the target) is 132 ticks, at seed 424242; seed 2026's
worst is 120. Any backstop deadline must clear that so a unit still filtering
through the wall gap is never benched early.

## Decision

Add a monotone no-progress backstop to SeparationSystem, additive to and
independent of the existing leaky `StallTicks` net, which is left untouched.
A flow-pathing Unit that fails to better its NEAREST APPROACH to the current
destination for `NoProgressDeadline` ticks is benched where it stands
(`Moving = false`), under the same near-destination attack-move rule the
`StallTicks` path already uses.

- `NoProgressDeadline = 14 * TicksPerSecond` (210 ticks, 14s): 3.5x the
  existing four-second net and a 59% margin above the measured 132-tick
  worst-case legitimate queue plateau, so it only ever catches a genuine
  limit-cycle orbit, never a queueing unit.
- Two new per-entity fields carry the state: `NearestApproachSq` (the closest
  squared distance to the destination this walk has reached) and
  `NoProgressTicks` (ticks since it last improved). A `Raw`-zero
  `NearestApproachSq` is the "unseeded" sentinel; the first eligible tick after
  a fresh order seeds it from the live distance, so a re-order re-arms simply by
  zeroing it, done in the one command handler that issues Move/PathMove/
  AttackMove. The spawn-exit path needs no touch: a freshly spawned entity is
  the zero sentinel already.

Nearest-approach, not raw displacement, is the signal because the orbiters DO
move every tick (0.2 to 0.4 cells); they simply never get closer. A monotone
best-distance can never be bettered once an orbit locks, so it catches an orbit
of any period regardless of its per-tick churn, which is exactly where the
leaky net's balance point hides it.

Both fields are hashed (ComputeStateHash) and serialised (save format v6),
appended after the ADR-012 ferrite-cap tail, following the ADR-007 rally-field
precedent for mutable per-entity movement state.

## Alternatives rejected

**Raise the scenario budget above 3000 (Q013 option 3).** Rejected by
measurement: the crowd never settles at 40000 ticks, so the budget is not the
cause. This would have been a one-line test-harness change had the crowd
settled late, and it was checked first precisely to rule it out.

**Two-tick oscillation detection (Q013 option 2).** Rejected: unit 27 is a
THREE-tick cycle and unit 151 does not repeat a position exactly at all, so a
period-2 detector misses both. The nearest-approach watchdog is period-agnostic.

**Change the existing `StallTicks` leak (make it monotone, or reset only on a
new best).** Rejected for blast radius: it alters when EVERY rim-locked unit
settles, across skirmish and construction crowds too, widening the behavioural
delta well beyond the two defective units. The additive backstop with a
generous deadline leaves every unit the existing net already catches settling
on the identical tick, so only genuine limit cycles change.

**Leave the fields unhashed (serialise only, like ADR-012's FerriteCap), which
would keep every seed-2026 golden byte-identical.** Tempting, and the fields
are derivable from already-hashed positions, but FerriteCap's exemption is for
IMMUTABLE spawn-time state. These are mutable and gate when a unit stops
moving, so the rally precedent applies: mutable movement state is hashed, for
desync-detection defence in depth. Serialisation alone would also fail the
save/load contract had a later design read the counter across a mid-walk save
without hashing it. We accept moving all 24 goldens and prove the move is
purely mechanical.

## Consequences

Easier: crowd pathing converges for every seed; the soak goes green; a whole
class of balanced-leak limit cycles can no longer wedge a unit for a match.

Harder: two more per-entity fields to carry in the hash and the save; save
format is v6 (v1..v5 load with both fields defaulted to the unseeded sentinel,
which re-arms the backstop on resume, the sane meaning of a save that never
held the counter).

Hash impact: MOVES all 24 golden hashes. Unlike ADR-012's selective move, a
per-entity HASHED field shifts every scenario's fingerprint mechanically, the
ADR-007 rally pattern. The neutralisation proof is therefore by identity, not
by which rows move: with the backstop deadline set absurdly high (disabled) and
at 210 (enabled), the seed-2026 goldens are BYTE-IDENTICAL across all 24
scenarios, which proves the backstop never fires at seed 2026 and every golden
move is the mechanical field-append alone, zero behavioural change at the
golden seed. The only behavioural change anywhere is at seed 900913, where
units 27 and 151 now settle (the whole crowd is down by tick 499). One
regeneration under this ADR, cross-platform proven by the determinism CI on
Windows and Linux, per the doc 23 section 6 discipline.

Gates: the existing ScenarioPathing assertion (every unit settles, none
stranded across the wall, all within 22 cells) now passes at seed 900913 as
well as 2026; the full five-seed determinism suite plus the 20-game LAN soak
exit 0; saveload and campaignsave round-trips prove the v6 serialiser order.
