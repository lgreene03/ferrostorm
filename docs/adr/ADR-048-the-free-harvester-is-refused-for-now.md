# ADR-048: the free harvester is written, was built, and is REFUSED for now
- Status: Ratified (as a refusal)
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s4's "includes one free harvester"; P7-7b

## Context

GDD s4 carries a clause the sim has never implemented:

> **Refinery:** 2,000 credits, **includes one free harvester**. 700 HP...

`World.SpawnRefinery` creates a refinery and nothing else. ADR-047 identified
this as the reason the commander settles at two harvesters where GDD s4 writes
three: the designed three is **two delivered plus one bought**, and ADR-047 could
only reach the bought one.

**This row was built, measured, and is being withdrawn rather than shipped.** The
implementation was correct and the design was defensible; what it exposed was
not.

## What was built

The free harvester was placed in the **purchase path** (`PlaceStructure`), not in
`SpawnRefinery`, and that part of the design still looks right:

- GDD s4's sentence is a **price-list line**. It says what two thousand credits
  buys, so the harvester belongs to the transaction, not to the building.
- `SpawnRefinery` has **29 call sites**, most of them fixtures that spawn a
  refinery only to satisfy a prerequisite. Several count units and would have
  failed for reasons unrelated to what they test.
- It would have handed a harvester to every **map-placed** refinery, which is a
  balance change to every map in the game smuggled in under a clause about a
  purchase.
- A consequence that reads correctly either way: an **engineer-captured**
  refinery brings no harvester, because a capture is not a purchase.

The AI's target was raised by one bought harvester per base, derived rather than
invented: GDD s4 floats at 3 over 2 refineries and says each refinery delivers
one, so two are delivered and the third is bought.

## What the measurement found, and why it is a refusal

The commander did reach GDD s4's float - **2 refineries, 3 harvesters** - and
then three things went wrong, each worse than the last.

**1. The treasury ran away.** Seat 1 banked **38,823 credits** over a 9000-tick
match, climbing monotonically: 6200, 14209, 19196, 25575, 26586, 38823. Income
roughly tripled and the spending ladder did not move, so the commander had
nothing to do with the money.

**2. It was not a finished game.** The obvious reading was that the match had
ended and a winner was banking unopposed. `economyprobe` could not say, so a
`match` column was added to find out - and it **refuted** that reading. The match
was still RUNNING at tick 9000, with seat 0 at **0 refineries, 0 harvesters and
0 credits** and no victory declared. One commander economically dead, the other
unable to spend, and neither able to finish.

**3. The commander stopped being able to fight.** `mission` (mission-01, a
camp-clearing strike) had already slowed from tick 3688 to 4946 under ADR-047.
With the free harvester it **never cleared the camp inside 9000 ticks at all**.
`expansion` then failed a second, different way ("no harvester working at the
end").

## The root cause, which is not the free harvester

The army rung stands aside "while the yard still wants a structure it cannot yet
afford - infrastructure before army, always". **That rule does not terminate when
the infrastructure list grows.** Two economy rows in a row lengthened the ladder,
so the commander spends longer and longer in its infrastructure phase, and a
richer economy makes it *less* able to fight rather than more.

That is a real defect, it is now well evidenced, and **it is the row that has to
land before the free harvester can.** Shipping the free harvester first would
mean shipping a commander that cannot complete a campaign mission, in exchange
for a GDD clause no player has asked for.

## Decision

**Refused for now**, with the work described here rather than discarded, and with
the reversal condition stated plainly.

### The argument that would have to be overturned

The free harvester ships the moment **the commander converts income into army**.
Concretely, any one of:

1. **The infrastructure-before-army rule gains a termination condition** - build
   army once the economy is sufficient rather than once the ladder is complete.
   This is the direct fix and it is the next row.
2. **A human playtest says the free harvester is wanted regardless**, on the
   grounds that it is a player-facing convention and the AI's inability to spend
   is not a reason to withhold it from players. This is a legitimate position and
   it is Luke's to take; the measurement above is what it would be taken against.
3. **The AI's spending ladder grows** - faction defences, the Veil Projector,
   wall tiers, a larger wave size - enough that tripled income has somewhere to
   go. Several of those are already open rows.

## What DID ship from this wave

Two things, both earned by the failed experiment rather than by the feature:

**`economyprobe` gained a `match` column.** Without it the table above reads as a
runaway stockpile, which is ADR-041's own stated condition for reconsidering a
silo. The first guess was that the match had ended; the column proved it had not.
Two very different conclusions, one of which would have reopened a settled ADR
for the wrong reason, and nothing in the table could tell them apart.

**`expansion`'s refinery assertion is a range rather than an exact count.**
ADR-047 wrote it as an equality, which asserts the commander's *speed* inside a
fixed horizon - so any change to how fast it expands breaks a scenario that is
actually succeeding. It now asserts what the scenario's own report line claims:
the second base got a refinery, and the per-base rule was not exceeded.

## Consequences

No behaviour changed. All 24 goldens byte-identical, the catalogue checksum
unmoved, and all 18 local CI gates green.

The honest state: **GDD s4's economy is still two-thirds implemented**, and the
reason is now understood rather than suspected. The next row is the commander's
spending, not its income.
