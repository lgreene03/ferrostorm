# DEFECT: the AI fire-sales its base without checking whether it could rebuild

Labels: `persona:ai-engineer` `gdd:s7` `phase:P7` `owner:ai-engineer`
Found: 2026-08-03, P7-16 (ADR-058) - **filed wrong**, corrected 2026-08-03 by P7-17
Severity: **high**
Confidence: **measured**, four fixtures with columns (below)

> **RESOLVED 2026-08-03 by P7-17 (ADR-059), verified still fixed 2026-08-07.**
> `SkirmishAI.Act` now takes the exact fix "The fix" prescribes below: before the
> Fire Sale it asks whether an MCV is BUILDABLE - prerequisites met
> (`w.HasPrereqs(_player, mcvDef.Prereqs)`), a producer of the right kind alive
> (keyed on `mcvDef.ProducedAt`, not the factory by name), and the credits to pay
> - and orders one and returns rather than selling. An MCV already on order also
> counts, so the order does not get fire-sold on the next beat. `comebackgate`
> (sim battery) proves it bites, and the full local gate passes green at
> catalogue checksum 0xFA2DFE52192660BA. Left in place as history because the
> "filed wrong" lesson below is worth more than the ticket. The record below is
> the state BEFORE the fix.

## THIS TICKET WAS FILED WRONG, AND THAT IS THE FIRST THING IN IT

P7-16 filed this as *"a commander that loses its Construction Yard stops playing
entirely, because the army block is gated on `wanted == 0` and the yard ladder
can never be satisfied without a yard"*.

**The measurement was real. The diagnosis was wrong**, and it was wrong in
exactly the way this project's standing rules warn about: the freeze was
observed, and then the cause was *read off the source* rather than measured.
The army block is never reached at all. `SkirmishAI.Act` returns at
`if (cy < 0)` (line 310), a hundred lines earlier, into DR-10's deliberate
"last stand" handling.

And the specific thing measured - credits sitting untouched at exactly 20000,
no commands at all - was an **artefact of the fixture**. DR-10's fire sale is
guarded by `enemyStructure < 0`, and that fixture had no enemy structures
anywhere on the map. A commander with nothing to attack-move towards correctly
does nothing.

So: no freeze, no `wanted == 0` bug, and option 1 (`wanted == 0 || cy < 0`)
would have changed nothing while appearing to fix something.

**The lesson, which is worth more than the ticket.** "Measure before
diagnosing" is not satisfied by measuring the *symptom*. The symptom was
measured correctly and the cause was still guessed. What was missing was one
more column: *which branch returned*. A fixture that cannot distinguish
"deliberately silent" from "stuck" reports a freeze either way.

## The real defect, measured

Same shape of commander in every row: **no Construction Yard**, but a live
Factory, Barracks, Refinery, harvester, a ferrite field and 20,000 credits.
1500 ticks, `SkirmishAI.Standard(0)`.

| fixture | commands | sells | structures left | new CY | credits |
|---|---|---|---|---|---|
| no enemy on the map | 0 | 0 | 4 | 0 | 20000 |
| enemy present | 5 | 4 | 0 | 0 | 22400 |
| enemy + **radar standing** | 6 | **5** | **0** | 0 | 22850 |
| enemy + radar + **MCV in hand** | 10 | 0 | **9** | **1** | 11907 |

Read row 3 against row 4. Both commanders can come back. The only difference is
whether the MCV is **already built** or merely **affordable and buildable**.

Row 4 deploys the MCV, founds a yard and rebuilds to nine structures. Row 3
holds a Factory, holds the Radar Uplink that gates the MCV, holds 20,000
credits against an MCV costing 3,000 - and **sells its entire base**, radar
included, for a 2,850-credit consolation.

## The cause

DR-10's last stand, in `SkirmishAI.Act`:

```csharp
if (ownMcv >= 0) { /* deploy it, comeback */ return; }
if (_lastStandMade || enemyStructure < 0) return;
_lastStandMade = true;
/* sell everything, attack-move everything, once */
```

The comeback rule keys on **owning an MCV**. It never asks whether one could be
**bought**. A commander one production order away from a full rebuild is
classified as beaten.

This is the defect class P7 has now found around seventeen times: **a rule keyed
on an instance where it should key on a capability.** "Do I have an MCV" should
be "can I get an MCV".

## P7-16 made it materially worse, which is worth stating plainly

Before ADR-058 the MCV's prerequisite was `com_factory`, so a yard-less
commander holding a factory could always buy one - the capability and the
instance nearly coincided. ADR-058 moved the MCV behind the Radar Uplink, which
is correct, and in doing so widened the gap between "has an MCV" and "can build
an MCV". Row 3 is a commander that satisfies the new prerequisite and still
sells.

## The fix

Before the fire sale, ask whether an MCV is **buildable**: prerequisites met
(`HasPrereqs` on the MCV's own authored prereqs), a producer of the right kind
alive, and the credits to pay. If so, order one and return, rather than selling.

This is where P7-16's `canBuyMcv` guard finally becomes load-bearing, which
retires the debt ADR-058 recorded when four fixtures failed to make it bite.

### Rejected: sell everything except what is needed to rebuild

Keep the factory and radar, sell the rest, then buy. More credits in hand, and
much more fiddly: it has to reason about which structures are prerequisites for
the thing it is about to buy. The simple rule is better, and credits are not the
constraint at 20,000 against 3,000.

### Rejected: never fire-sale

The Fire Sale is a good ending and DR-10 was right to add it. It should fire
when the commander is **actually** beaten, which is the whole point of this
change.
