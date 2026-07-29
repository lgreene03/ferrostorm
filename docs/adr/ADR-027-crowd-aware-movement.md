# ADR-027: crowd-aware movement
- Status: **Proposed** (needs Luke + Architect; nothing has been implemented)
- Date: 2026-07-29
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD pillar 2 "fast, decisive, generous"; raised by Q018

## Context

Q018 measured, and then traced to the code, a defect that penalises building an
army: past roughly seventeen units around a production cluster, a commander's
units hold live orders, are flagged moving, and travel nought to one cell in
six thousand ticks. The side with the LARGER army is the side that cannot
attack, twenty three against thirteen on the map where it is worst.

Three facts in the movement code produce it, and none is a bug alone.

The flow field is Dijkstra over terrain and structures only (Map.cs
FlowField.Build) and never consults entity positions, so it always returns a
clean route through a crowd it cannot perceive. Unit-on-unit is a soft mutual
push with no swap, slide or avoidance (World.cs SeparationSystem), so a unit
that cannot advance simply does not. And the two backstops that bench a stuck
unit - StallTicks at four seconds pressed against a stationary neighbour, and
ADR-014's monotone no-progress watchdog at fourteen - work exactly as designed.

The interaction is the defect. A benched unit is a STATIONARY unit, and
pressing against a stationary unit is what feeds the next unit's stall counter,
so the first commander's unit to give up converts its neighbours into units
that give up. A production cluster freezes solid from the inside out. The code
even anticipates the shape, noting that "a jam 19 cells short of the objective
is traffic, not arrival", but acts on it only by dropping the attack-move
stance; the unit is still benched where it stands.

This is not a skirmish curiosity. Every AI-vs-AI number the balance tool
produces is measured through it, including two of doc 27's three headline
convictions, so DR-12's rebalance cannot be trusted until it is fixed.

## Decision

**None yet. This ADR exists to make the decision cheap, not to pre-empt it.**
What follows is the option set with the cost of each measured rather than
guessed, and one of them already eliminated by experiment.

### Option A: stop the AI re-issuing identical orders

The AI's quiet-watch block re-issued an identical PathMove home on every beat,
and ApplyCommandCore zeroes both backstop counters on any move order without
checking whether the destination changed, so the counters were re-armed
forever. Two units collected 237 and 153 orders while moving three and six
cells net.

**Implemented, measured, REJECTED.** Rate-limiting the re-issue leaves the jam
byte-identical (147 orders over 26 units, 6 lost, 20 alive, closed to 53) and
moves the skirmish golden. It buys nothing and costs a replay break. The
re-arming trap is real and worth tidying inside whatever change wins, but it is
not the cause.

### Option B: bench only near the objective

If benching far from the objective is what freezes the crowd, do not bench
there: keep pressing, and let the unit settle only when it is near where it was
sent.

**Implemented, measured, REJECTED, and the failure is the most useful result
here.** It works, partially: on skirmish-02 the pinned commander improved from
closing to 29 cells to closing to 8, losing fewer units and keeping more alive.
On skirmish-05 it changed nothing, because those units are attack-moving and
fall outside the stall block's own guard. And then it fails the battery
outright: `pathing: units failed to settle within budget ticks`, exit 134, with
23 of 24 goldens moved.

That is precisely the regression ADR-014 was written to prevent, and it is the
finding that should shape the answer: **the bench is not gratuitous, it is what
guarantees settling.** Removing it trades a freeze for a never-settles. Any
viable fix must let a unit make PROGRESS through a crowd, not merely refuse to
give up inside one.

### Option C: give movement awareness of other units

The only option that addresses the cause. Candidates, none prototyped:

- a deterministic sidestep, where a blocked unit tries a fixed-order set of
  alternative headings before it counts as blocked at all;
- yielding, where a stationary unit ordered nowhere steps aside for a moving
  one that presses it, which is the classic-genre answer and turns a permanent
  obstacle back into traffic;
- crowd cost in the field, where occupied cells raise the Dijkstra cost so
  routes flow around a jam instead of into it, at real per-tick cost since the
  field is currently built once per destination and cached.

All three change how EVERY unit steps, so all three move all 24 goldens, and
all three must hold the determinism rules: fixed point only, no unordered
iteration, and a tie-break that is stable by entity id.

## Alternatives rejected

Leaving it. Defensible only if the jam is considered a legitimate skill test,
and it is not, because it is invisible, it punishes the correct play of
building an army, and it silently biases every balance measurement the project
takes.

Tuning the maps so the effect does not show. This was explicitly declined while
authoring skirmish-05: fitting map geometry to a movement defect hides the
defect in a map file and leaves it live everywhere else.

## Consequences

Whatever wins, the acceptance bar is now known and should be stated in the
ticket rather than discovered again: the jam must clear (Q018's pinprobe and
pintrace are committed and measure it directly), AND the pathing settle gate
must stay green, which Option B proves is not automatic. The two together are
the real specification, and no fix that satisfies only one of them is done.

A golden regeneration is certain, and it is a large one - Option B moved 23 of
24. That is a replay-compatibility break needing sign-off, and it should be
taken ONCE, for the fix that satisfies both halves of the bar, rather than
spent on a partial improvement.

ADR-018 deferred cohesive formation movement to "a future ADR on evidence of
need". This is that evidence, and if Option C's yielding candidate wins, the
two decisions likely want to be taken together.

After the fix lands, the balance tool's engagement matrix and siege table
should be re-run before DR-12 uses them, because the numbers now in them were
measured through this.
