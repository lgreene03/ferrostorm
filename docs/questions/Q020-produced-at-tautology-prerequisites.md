# Q020: four units carry a prerequisite that cannot refuse anything

> **Filed as Q018 by P7-16 and renumbered to Q020 on 2026-08-03**, because Q018
> was already taken by "an army large enough to attack jams itself in its own
> base" (raised during P6 wave DR-18, still open, cited by ADR-027 and docs 26
> and 27). P7-16 filed a question without reading the questions directory, which
> is the same rule it had just watched doc 29 break when that filed a Tech Centre
> Q006 had already answered. Renumbered rather than merged: the two are unrelated
> and the older one has citations.

Owner: game-designer
Raised by: P7-16 (ADR-058), 2026-08-03, while answering Q006
Decide by: unset (not blocking; the gate holds the line meanwhile)

> **ANSWERED AND CLOSED 2026-08-07 (Luke, Game Designer authority; ADR-069).**
> ALL FOUR emptied. The reading taken: a prerequisite that names the building a
> unit is produced AT can never refuse anything, so it is not a tier gate the
> Game Designer chose to keep - it is a fake gate, and removing it removes a rule
> that misleads a reader into thinking the tier is gated when it is not. The four
> units (howitzer, phantom, bulwark, strike flyer) are available as soon as their
> producer stands, which is what the sim already did. Goldens byte-identical
> (the clause never refused an order), catalogue checksum moved by construction.
> `mcvtechgate` stage 3's count is retired to ZERO and now guards the CLOSED
> class: any new produced-at tautology, or the MCV regressing into one, fails by
> name. The schema's own note already said "the producer IS a prerequisite; do
> not repeat it" - the data was authored against its own advice.

## The question

A unit's `prerequisites` list names structures that must stand before it can be
ordered. Its `produced_at` names the structure it comes out of. When a unit's
prerequisite names its own `produced_at`, the clause **can never refuse
anything**: the order is only accepted at that building in the first place.

Four units carry that shape today:

| id | unit             | produced_at    | prerequisite   |
|----|------------------|----------------|----------------|
| 8  | dir_howitzer     | com_factory    | com_factory    |
| 9  | sod_phantom_tank | com_factory    | com_factory    |
| 10 | dir_bulwark_tank | com_factory    | com_factory    |
| 15 | com_strike_flyer | com_airfield   | com_airfield   |

For each: is the unit **meant** to be tier-gated (in which case the
prerequisite should name a real gate, as Q006 has just done for the MCV), or is
it meant to be available as soon as its producer stands (in which case the
prerequisite should be **empty**, and saying so removes a rule that reads as a
gate and is not one)?

These are the same shape, not the same question. The howitzer, phantom and
bulwark are the expensive end of the factory roster and a tier gate is at least
arguable. The strike flyer is the airfield's only unit, so an empty
prerequisite is almost certainly the honest answer there.

## Why this is not P7-16's to take

P7-16 answered Q006, which owned the MCV specifically. Taking four more
because they share a shape with the one that was answered is the scope creep
ADR-009's gates clause reserved for the Game Designer. The tier a unit sits at
is a balance and pacing decision, not a tidy-up.

## What P7-16 did instead

`mcvtechgate` stage 3 **derives** this list from the catalogue and asserts the
count is four. So:

- a fifth cannot appear unnoticed,
- the MCV cannot regress into the shape it just left,
- and answering this question must retire the count deliberately rather than
  letting it drift down.

## The defect this replaced, which is the useful part

`World.cs` carried a hand-written comment naming the tautologies. It said
there were four - `mcv, howitzer, bulwark, phantom` - which was true when it
was written and stopped being true when ADR-028's air layer added the **strike
flyer** (`Prereqs: [16], ProducedAt: 16`) and nobody updated it. It also said
Q007 owned them; Q007 is about where the **engineer** is built, and in fact
nothing owned them at all.

So a comment documenting this project's most-repeated defect - a hand-kept list
lagging the catalogue it mirrors - had quietly become an instance of it. The
list is now derived and the comment is no longer the count.

## What would have to change to close this

A Game Designer sentence per unit: tier-gated behind what, or not tier-gated.
Then the `/data` prerequisite is either moved to a real gate or emptied, the
compiled reference follows, and `mcvtechgate`'s count drops by that many in the
same change.
