# ADR-049: the free harvester ships, and ADR-048 blamed the wrong thing
- Status: Ratified (supersedes ADR-048's refusal)
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s4's "includes one free harvester"; P7-7d

## Context

ADR-048 built GDD s4's free harvester, measured three failures, and refused it.
**Two of the three conclusions in that ADR were wrong**, and this one corrects
them rather than quietly shipping over the top.

The refusal named a root cause: the army rung stands aside "while the yard still
wants a structure it cannot yet afford - infrastructure before army, always", and
that rule has no termination condition. It filed P7-7c to fix that first.

## What the measurement actually showed

`economyprobe` gained **army columns** for this wave, for the reason ADR-048's own
table could not settle: a seat banking 38,823 credits might have a huge army it
could not use, or no army at all, and those are opposite defects with opposite
fixes.

**On main, before any change:**

```
   tick   credits0   credits1   refineries0   harvesters0   army0   army1   match
   1500       2350       3050             2             1       3       3   running
   3000       2870       3103             2             2       6       8   running
   4500       1992       3022             2             2       9      12   running
   6000       1292       1802             2             2      12      12   running
```

**The commander converts income into army perfectly well.** Army climbs 3, 6, 9,
12 while credits oscillate between 1292 and 4018. **P7-7c's premise was false**,
and it is withdrawn rather than built.

**With the free harvester, the banked seat has the biggest army on the board:**

```
   9000          0      36727             0             0       0      22   running
```

22 units at 36,727 credits. That commander is not failing to spend; it is
**out-earning one factory and one barracks**, whose queues are capped at two
items each. The stockpile is a production-throughput ceiling, not a build-order
stall - a different finding, and a much less alarming one.

## Which half was actually to blame

ADR-048 shipped two things together and blamed the wrong one.

- **GDD s4's free harvester** - written, a price-list line.
- **A derived "+1 bought harvester per base"** - *my* arithmetic, from reading
  s4's float of 3 against a delivery of 2.

Isolated:

| | mission-01 clears its camp |
|---|---|
| baseline (ADR-047) | tick 4946 |
| free harvester ALONE | **tick 3462** |
| free harvester + the derived +1 | **never, in 9000 ticks** |

**The free harvester alone makes the commander faster than the baseline**, and
faster than it was before ADR-047 raised refineries to two (3688). The `+1` was
the whole problem: it bought a 1400-credit harvester the commander did not need,
delaying its army and over-mining the map.

**And the `+1` was never necessary.** The skirmish start already provides one
harvester, so two purchased refineries deliver two more and the commander reaches
**2 refineries / 3 harvesters** - GDD s4's float exactly - with no derived
addition at all. The arithmetic that produced the `+1` was right about the
numbers and wrong about where the third harvester comes from.

## Decision

**The free harvester ships**, in the purchase path, exactly as ADR-048 designed
it. That part of ADR-048 was sound and is kept verbatim:

- GDD s4's sentence is a **price-list line**, so the harvester belongs to the
  transaction, not the building. `SpawnRefinery` has 29 call sites, most of them
  fixtures satisfying a prerequisite; and delivering there would hand a free unit
  to every map-placed refinery, a balance change to every map in the game
  smuggled in under a clause about two thousand credits.
- An **engineer-captured** refinery delivers nothing, which reads correctly and
  now has a gate stage: you took a building, not a delivery.

**The derived `+1` is dropped and not replaced.** GDD s4's float is reached
without it.

## Hash and format

**Four goldens regenerated, measured** - the same four as ADR-047, and for the
same reason: they are the scenarios whose commander buys refineries.

| scenario | before | after |
|---|---|---|
| `skirmish` | `0x19228D6E6E605554` | `0x946956C509A353B8` |
| `expansion` | `0x762BE98AE6C0E86F` | `0x1E49D8E5E01F6376` |
| `aisuper` | `0x10456F2FE00DE33E` | `0x2BEF8B6865D0C25D` |
| `mission` | `0x1E0F30CF25385501` | `0x15BEBB861BD37F1A` |

The other twenty are byte-identical and the catalogue checksum is unmoved - no
definition changed.

## One scenario assertion corrected, again for the same reason

`expansion` asserted "a harvester is working at the end". A faster economy
**mines the map out**: that fixture lays 14,500 ferrite and runs 7000 ticks, and
the far field now finishes at **0**. Every harvester is then correctly idle, and
the assertion reported the economy succeeding as the economy failing.

It now asserts what it means - no harvester working **while ferrite remains** -
and the far field being drawn down is asserted separately, so the economy's work
is still proved. This is the second assertion in three waves found to be testing
the commander's *speed* rather than its behaviour.

## What this ADR is really about

ADR-048 is left in the tree, wrong conclusions and all, because the correction is
worth more than a tidy record. Three things it got wrong are worth naming:

1. **It blamed a written GDD clause for a failure caused by an undeclared
   derivation shipped beside it.** Two changes went in together and only one was
   isolated afterwards.
2. **It diagnosed a root cause it never measured.** "Infrastructure before army
   has no termination condition" was a plausible reading of the code that the
   army columns disproved in one run.
3. **It filed a row (P7-7c) to fix a defect that does not exist.**

What it got right, and why the refusal was still the correct call at the time:
**stopping.** The evidence was genuinely confusing, three distinct failures had
appeared, and shipping into that would have been worse than pausing. The fix for
a wrong diagnosis is a better measurement, which is what this wave bought.

## Consequences

GDD s4's economy is now fully implemented: **2 refineries, 3 harvesters**, the
float that section specifies. `freeharvestergate` (3 stages) pins it and was
proved to bite. All 18 local CI gates green.

`economyprobe` keeps its army columns. They are what turned a confident wrong
diagnosis into a measured right one, and they cost two integers.
