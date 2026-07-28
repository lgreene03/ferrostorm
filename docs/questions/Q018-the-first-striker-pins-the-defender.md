# Q018: the commander who strikes first pins the other for the rest of the match

Labels: persona:p2, gdd:s9, phase:6, owner:ai-engineer + game-designer
Raised by: map-design, during P6 wave DR-18 (authoring skirmish-05).
Decide-by: before the balance conversation in DR-12 is taken, because this
distorts every AI-vs-AI number that conversation will rest on.

## The finding

Measured across the whole committed map pool, an AI-vs-AI match is decided far
more by WHICH commander attacks first than by anything either does afterwards.
Once one side's wave arrives, the other side's garrison answers it, and the
defender never releases: it keeps producing, its army grows, and it stays home
until the match ends.

The measurement, six thousand ticks per map, recording how close each
commander's army came to the other's start and each side's peak army:

| Map | player 0 closed to | player 1 closed to | peak armies |
|-----|--------------------|--------------------|-------------|
| skirmish-01 | 13 | 4 | 11 v 11 |
| skirmish-02 | 7 | 29 | 13 v 10 |
| skirmish-03 | 10 | 7 | 12 v 12 |
| skirmish-04 | 3 | 4 | 22 v 14 |
| skirmish-05 (first draft) | 37 | 2 | 8 v 17 |
| skirmish-05 (as shipped) | 11 | 53 | 13 v 23 |

The two balanced maps sit near parity. skirmish-02 already shows the effect at
7 against 29, and it has shipped unremarked. skirmish-05 shows it in BOTH
directions across a single change: widening the starts did not fix the
asymmetry, it flipped which side suffered it. The pinned commander is not the
weaker one, which is the tell that this is not an economic problem: in the
shipped skirmish-05 reading the side that never marched had the LARGER army,
twenty three against thirteen.

## Why this is not a map question

It travelled. The same shape appears on a diagonal ridge map with no water on
it at all, and reverses on one map under a change that was symmetric by
construction. A map can make it easier or harder to trigger, but nothing about
a map's geometry explains an army of twenty three sitting at home.

The mechanism is visible in SkirmishAI. Defence answers an intruder within a
generous radius and re-issues on a sixty tick cadence; the wave condition needs
army at or above waveSize plus the garrison AND a three hundred tick gap. What
is missing is any rule that ever says "the raid is over, go back to attacking".
A commander under sustained pressure therefore has no path back to offence.

## The question

Two things need deciding, and neither is mine:

1. **Is this a defect or the design?** A defender that commits everything to
   defence is a legitimate difficulty flavour, and doc 28's ladder now has a
   place to put it. But it is currently EVERY commander at EVERY rung, chosen
   by nobody, and it is invisible in the code as an intention.
2. **If it is a defect, what releases the garrison?** The obvious candidates
   are a threat-expiry (no hostile seen near home for N ticks returns the
   garrison to the wave pool) or capping the fraction of the army defence may
   hold. Both change AI behaviour on maps whose goldens the battery pins, so
   whichever is chosen carries a golden regeneration and needs the standing
   authorisation.

## What was NOT done about it

skirmish-05 was NOT tuned until the numbers looked even. Doing that would have
fitted one map's geometry to an AI quirk that outlives it, and would have hidden
the finding in a map file. The map is held to what a map can be held to: its
crossings are proven walkable in both directions under a real flow field
(fordgate), its fairness invariants are proven mechanically (tools/mapgen.py),
and both AIs demonstrably play on it (mapgate). The offensive asymmetry is
recorded here instead.

## Related

DR-12 (docs/design/27) already owns a rebalance conversation resting on
AI-vs-AI evidence from the balance tool. That evidence inherits this bias, so
this question should be answered first or the rebalance will be tuning around
it. DR-15 (fog-honest scouting) touches the same target-selection code and would
be a natural place to land whichever answer wins.
