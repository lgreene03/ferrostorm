# ADR-051: a refinery is not a bottleneck, and GDD s4's second one buys nothing
- Status: Ratified (as a refusal, with the finding recorded)
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s4; P7-9

## Context

This wave went looking for the class of defect ADR-050 found: something invisible
because **nothing had ever asked the question**. Base shape was one. This is
another, and it sits under three rows already shipped.

GDD s4 says two things about a refinery that only make sense together:

> **Refinery:** 2,000 credits, includes one free harvester. **Processes a load in
> 8 seconds.**
>
> **Design intent:** A player **floats at 2 refineries / 3 harvesters** on one
> base.

The second is only a design if the first is a **building rate**. If a refinery
can serve any number of harvesters at once, nobody would ever buy the second one.

## The finding

`HarvestState.Unloading` counts down `e.StateTicks`, a **per-harvester** timer,
and `Docked` is a proximity test with **no occupancy check**. So any number of
harvesters can dock at one refinery and unload in parallel, each running its own
eight seconds.

The constant's own comment has claimed otherwise the whole time:

```csharp
public const int UnloadTicks = 8 * TicksPerSecond;   // refinery processes a load in 8s
```

**It names a building property and implements a unit one.**

Measured by `dockprobe`:

```
   harvesters   max unloading at ONE refinery
            2                               2
            4                               3
            6                               5
            8                               6
```

Concurrency tracks the harvester count. A refinery is not a bottleneck.

And the consequence, which is the number that matters:

```
   harvesters   credits w/ 1 refinery   credits w/ 2   gain
            3                   35000          35000     0%
            6                   66500          67200     1%
```

**A second refinery earns nothing.** Two thousand credits for one per cent, and
nothing at all at the harvester count GDD s4 actually specifies.

## What this means for ADR-047, stated plainly

ADR-047 made the commander build two refineries, citing GDD s4's float, and
measured a real improvement: the treasury went from touching zero to floating
between 1300 and 4000. **That improvement was real but the mechanism was not what
the GDD describes.**

In this sim a refinery is a **licence to own more harvesters** - the AI's target
is `refineryCount * harvestersPerRefinery` - rather than a throughput station.
The second refinery helped because it let the commander run a second harvester,
not because it processed anything.

That does not make ADR-047 wrong; the commander does now play GDD s4's stated
economy. It does mean **the reason it works is not the reason the GDD gives**,
and if serialisation ever lands, ADR-047's numbers must be re-measured rather
than assumed to hold.

## Decision

**Refused for now: unloading is NOT serialised per refinery.**

The fix is small and obvious - hold a dock slot on the refinery and queue behind
it - and I am not taking it, for one reason that outweighs the others.

**Three economy rows have landed this session and none has been played.** ADR-047
doubled the refineries, ADR-049 added the free harvester, ADR-050 changed where
buildings go. Serialising unloading would cut effective throughput by up to five
times on top of those, unplayed. That is four untested balance claims stacked,
which is precisely the "several untested claims wearing one feature's name" this
project keeps warning itself about.

Charter A11 reserves stat changes above fifteen per cent for Balance and Game
Designer co-sign. A five-fold concurrency cut is far past that line, and it is
the whole economy rather than one unit.

### The argument that would have to be overturned

Serialisation ships when any of these is true:

1. **A human playtest is run on the current economy.** This is the likeliest and
   the cheapest: it is the same playtest three ADRs have now asked for, and it
   would tell us whether the economy is already too fast before making it slower.
2. **Balance and Game Designer co-sign the change**, which is the charter's own
   route.
3. **The GDD's s4 is amended** to say a refinery serves any number of harvesters
   at once, which would make the current sim correct and the "floats at 2
   refineries" line the thing that needs rewriting. Worth considering on its
   merits rather than dismissing: a refinery that is only a harvester licence is
   a legitimate design, it is simply not the one s4 describes.

## What ships

`dockprobe`, a non-asserting probe, for the reason `economyprobe` exists: the
next person to ask "should a refinery have a dock queue" can answer it in thirty
seconds with evidence rather than an argument. It reports both halves - the
concurrency and the second refinery's actual earnings - because either alone
invites the wrong conclusion.

No behaviour changed. All 24 goldens byte-identical, catalogue checksum unmoved,
all 18 local CI gates green.

## Consequences

The honest state of GDD s4: the commander now reaches the **shape** s4 specifies
(2 refineries, 3 harvesters) while the second refinery does not do the **job** s4
gives it. That gap is measured, recorded, and reversible.

And the method is worth keeping. This wave opened by asking what nothing had ever
asserted, exactly as ADR-050 suggested, and the answer was sitting under three
rows that had already shipped. **A defect that nothing tests does not announce
itself; it has to be gone looking for.**
