# Q018: the commander who strikes first pins the other for the rest of the match

Labels: persona:p2, gdd:s9, phase:6, owner:ai-engineer + game-designer
Raised by: map-design, during P6 wave DR-18 (authoring skirmish-05).
Revised: after measuring the mechanism rather than reasoning about it. The
first version of this file named a cause that the measurement DISPROVED; that
version's claim is kept below under "the theory that was wrong", per the
standing rule that a disproved theory is recorded so nobody retries it.
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

So the army is neither idle nor destroyed. It is held. The two candidates that
survive the evidence, and which this question needs someone to choose between:

1. **Prosecution.** AttackMove engages what it passes. With enemy units near
   its own base - the opposing commander closed to 11 cells on this map - the
   whole army keeps finding local targets and never travels. Under this reading
   the behaviour is correct in the small and pathological in the large.
2. **Order thrashing.** The wave re-issues every 300 ticks and defence
   re-issues on a 60 tick cadence. A unit re-ordered before it has travelled
   far may be restarting its approach repeatedly, which would look exactly like
   this. Under this reading it is a defect with a cheap fix.

Separating them needs a per-unit order-and-position trace inside the sim, which
a command-stream probe cannot see. The instrument for the first half is
committed as the runner's `pinprobe` mode so nobody has to rebuild it.

## The question

1. **Is the shipped behaviour acceptable?** A commander that cannot disengage
   is a legitimate difficulty flavour, and doc 28's ladder now has somewhere to
   put it. But it is currently every commander at every rung, chosen by nobody.
2. **If not, which of the two mechanisms above is it, and what is the remedy?**
   Any answer changes AI behaviour on maps the battery pins, so it carries a
   golden regeneration and needs the standing authorisation.

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
