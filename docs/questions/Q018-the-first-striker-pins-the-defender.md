# Q018: an army large enough to attack with jams itself in its own base

Filed under the title "the commander who strikes first pins the other", which
is what the symptom looked like, and the filename keeps that slug because
other documents cite it. The title above is what it actually turned out to be.

Labels: persona:p2, gdd:s9, phase:6, owner:architect + sim-engineer
(REASSIGNED from ai-engineer + game-designer: the cause is unit movement, not
AI doctrine, and the section below shows why)
Raised by: map-design, during P6 wave DR-18 (authoring skirmish-05).
Revised twice, both times by measurement overturning what the file said. The
first version blamed a missing AI rule; the command stream disproved it. The
second version offered two mechanisms and asked someone to choose; a per-unit
trace disproved both. Every disproved theory is kept below rather than deleted,
per the standing rule that a theory nobody records is a theory somebody
retries.
Decide-by: before the balance conversation in DR-12 is taken, because this
distorts every AI-vs-AI number that conversation will rest on.

## The finding

Measured across the whole committed map pool, an AI-vs-AI match is decided far
more by which commander gets on top early than by anything either does
afterwards. One side ends up unable to reach the other at all.

Six thousand ticks per map, recording how close each commander's army came to
the other's start, and each side's peak army:

| Map | player 0 closed to | player 1 closed to | peak armies |
|-----|--------------------|--------------------|-------------|
| skirmish-01 | 13 | 4 | 11 v 11 |
| skirmish-02 | 7 | 29 | 13 v 10 |
| skirmish-03 | 10 | 7 | 12 v 12 |
| skirmish-04 | 3 | 4 | 22 v 14 |
| skirmish-05 (first draft) | 37 | 2 | 8 v 17 |
| skirmish-05 (as shipped) | 11 | 53 | 13 v 23 |
| skirmish-06 | 22 | 2 | - |

Two maps sit near parity. skirmish-02 has shipped unremarked at 7 against 29.
skirmish-05 shows the effect in BOTH directions across a single change:
widening its starts did not fix the asymmetry, it flipped which side suffered
it. The pinned commander is not the weaker one, which is the tell that this is
not an economic problem: in the shipped skirmish-05 reading, the side that
never arrived had the LARGER army, twenty three against thirteen.

## The theory that was wrong

This file first asserted, from reading SkirmishAI rather than from measuring
it, that "what is missing is any rule that ever says the raid is over, so a
commander under sustained pressure has no path back to offence". That is
FALSE, and the command stream says so plainly. Counting what each commander
actually issued over the same six thousand ticks:

| Map | p0 attack-moves / distinct units | p1 attack-moves / distinct units |
|-----|----------------------------------|----------------------------------|
| skirmish-01 | 81 / 27 | 56 / 29 |
| skirmish-02 | 60 / 28 | 142 / 38 |
| skirmish-03 | 82 / 29 | 92 / 34 |
| skirmish-04 | 157 / 31 | 126 / 16 |
| skirmish-05 | 20 / 14 | **147 / 26** |
| skirmish-06 | 120 / 33 | 150 / 40 |

On skirmish-05 the commander that never arrived issued the MOST offensive
orders on the map, 147 of them across 26 different units, against 20 orders
from the commander that did arrive. It is not failing to attack. It attacks
almost constantly.

## What the measurement does say

Adding attrition and end positions to the same run narrows it to one shape.
For skirmish-05's stalled commander:

- It lost only **6** of the 26 units it ordered forward; **20 were still
  alive** at the end. The waves are not dying on the way.
- Those survivors stand at (81,57), (82,57), (79,49), (82,44), (85,47) -
  clustered around their OWN start at (85,55), some stationary and some
  moving. They are alive, ordered forward, and at home.
- All **82 destroyable spans were still standing**, so no crossing was lost
  mid-match and ADR-025 rubble is not involved.

So the army is neither idle nor destroyed. It is held.

## The cause, measured: the army jams itself in its own base

This file previously named two candidates here - AttackMove prosecuting local
targets forever, or the wave and defence cadences re-ordering units before they
travelled - and asked someone to choose. **Neither is right.** A per-unit trace
(the runner's `pintrace` mode) ruled both out and found the actual cause.

What the trace measures, on skirmish-05, for the stalled commander's units:

| Reading | Value | What it rules out |
|---------|-------|-------------------|
| Ticks holding an ExplicitTarget | **0 per cent, every unit** | Prosecution. Nothing is fighting anything. |
| Distance travelled vs net displacement | 17 units at **0 to 1 cells travelled**, over 6000 ticks | Thrashing. A unit sent back and forth walks a long way; these walk nowhere at all. |
| Ticks flagged Moving | **15 to 53 per cent** | Idleness. They hold live move orders throughout. |
| Blocked neighbours (terrain and structures) | **0 of 17 units have 5 or more**; most have none | Enclosure by its own buildings. |
| Open cells reachable by flood fill | **5432**, essentially the whole map | Any terrain or map-shape explanation. |
| Friendly bodies within 2 cells | **mean 4.5**, and **zero** sharing a cell | Everything except each other. |

The picture those rows make is one thing: seventeen units standing shoulder to
shoulder around their own production buildings, each holding a live order to
attack, each flagged as moving, none of them able to take a step, on open and
fully connected ground, with no enemy in reach. The sim permits one unit per
cell - zero overlaps across every sample - so a dense cluster around the
barracks and factory can deadlock, every unit's next cell held by a neighbour
who is equally stuck.

**It is caused by SUCCESS, which is why it looks so strange from outside.** The
more units a commander builds, the denser the cluster it produces them into and
the harder the jam. That is the explanation for the reading nobody could make
sense of: the side that never arrives is the side with the LARGER army,
twenty three against thirteen. It out-produced itself into gridlock.

Stated precisely, because the difference matters to whoever fixes it. MEASURED:
every row of the table above. INFERRED: that the mechanism is mutual blocking
between friendly units. The trace shows units with live orders failing to
translate on open ground with no other obstruction present, which leaves only
each other; it does not read the movement system's avoidance logic directly, so
the exact rule that fails - no local avoidance, a stuck-unit backstop that does
not fire, spawn spacing, or something in flow-field following - is one file's
reading away and has not been done here.

Note this is NOT the same defect as ADR-014's no-progress settle backstop or
the spawn occupancy ADR-007 handled: those concern a unit leaving a producer.
These units left their producers long ago and are stuck in the open yard.

## The question

The cause is no longer in doubt, so what is left is a decision about the fix,
and it is not a small one.

1. **Whose defect is it?** It presents as an AI problem and is not one. The
   commander behaves correctly throughout: it produces, it orders attacks, it
   keeps its units alive. What fails is unit movement in a crowd, which is
   sim-side and sits under the determinism rules.
2. **What is the remedy, and how far does it reach?** Local avoidance, a
   stuck-unit nudge, spacing units as they gather, or a formation-aware
   approach are all candidates and they are not equivalent in cost. C1b already
   deferred "cohesive formation movement" to a future ADR on evidence of need
   (docs/design ADR-018); this is that evidence, and it may be the natural
   place for the answer to land.
3. **It carries a golden regeneration either way.** Any change to how units
   move changes every scenario that moves a unit, which is all of them. That
   needs an ADR and the standing authorisation before a line is written.

A thing worth weighing in the design conversation rather than deciding here:
the jam is a real dynamic that a human player also faces, and part of the
answer might legitimately be that armies should be walked out of the yard
rather than left to accumulate in it. What is not defensible is the current
state, where the sim silently punishes production and every AI-vs-AI number in
the balance tool is measured through it.

## Why this matters more than a skirmish oddity

The balance tool's engagement matrix, its siege table, and DR-12's whole
rebalance conversation rest on AI-vs-AI matches. Every one of those matches is
being run through a movement defect that penalises the side that builds more
units. Two of the review's three headline convictions - that the faction war is
0-6 and that massed rifle squads are a universal per-cost answer - were
measured in exactly that environment. Neither is necessarily wrong, but neither
can be trusted until this is fixed and they are re-measured.

## What was NOT done about it

skirmish-05 was not tuned until the numbers looked even. That would have fitted
one map's geometry to a behaviour that outlives it and buried the finding in a
map file. The map is held to what a map can be held to: its crossings are
proven walkable in both directions under a real flow field (fordgate), its
fairness invariants are proven mechanically (tools/mapgen.py), and both AIs
demonstrably play on it (mapgate).

## Related

DR-12 (docs/design/27) owns a rebalance conversation resting on AI-vs-AI
evidence from the balance tool. That evidence inherits this bias, so this
question should be answered first or the rebalance will be tuning around it.
DR-15 (fog-honest scouting) touches the same target-selection code and would be
a natural place to land whichever answer wins.
