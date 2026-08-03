# ADR-059: beaten means cannot rebuild, not has not yet built

- Status: Ratified
- Date: 2026-08-03
- Deciders: AI Engineer agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s7; DR-10's last stand; ADR-058; P7-17

## Context

DR-10 gave the commander an ending. Lose your Construction Yard and it does one
of two things: deploy a rebuild MCV if it has one, or sell everything still
standing and throw one final wave - the classic Fire Sale.

The comeback half keyed on **owning** an MCV:

```csharp
if (ownMcv >= 0) { /* deploy, comeback */ return; }
if (_lastStandMade || enemyStructure < 0) return;
/* sell everything, once */
```

It never asked whether one could be **bought**. Measured, a commander holding a
Factory, the Radar Uplink that gates the MCV, and twenty thousand credits
against a three thousand credit unit **sold its entire base, radar included**,
for a 2,850 credit consolation - one production order from a full rebuild.

**The seventeenth instance of one defect class in this phase: a rule keyed on an
INSTANCE where it means a CAPABILITY.**

**ADR-058 widened the gap it hid in**, and that is worth stating plainly rather
than leaving for someone to find. While the MCV's prerequisite was the factory
it is produced at, "has an MCV" and "can buy an MCV" nearly coincided. Moving it
behind the Radar Uplink made them genuinely different states, and this row is
the second half of that one.

## Decision

Before the Fire Sale, ask whether an MCV is **reachable**: prerequisites met on
the MCV's own authored `Prereqs`, a producer of its own `ProducedAt` kind alive,
and the credits to pay. If so, order one and return. Selling is for a commander
that is actually beaten.

The producer is found by `ProducedAt` rather than by naming the factory, so this
keeps working if Q020 ever moves it.

### Rejected: sell everything except what is needed to rebuild

Keep the factory and the tier gate, sell the rest, then buy - more credits in
hand. Rejected as fiddly for no gain: it has to reason about which structures
are prerequisites of the thing it is about to buy, and credits are not the
constraint at 20,000 against 3,000. **To overturn:** a measured match where the
commander cannot afford the MCV without liquidating.

### Rejected: remove the Fire Sale

Rejected outright. DR-10 added a good ending on purpose and stage 2 of the gate
exists to stop this row quietly becoming that change.

## Hash and format

**All 24 goldens byte-identical, measured.** DR-10's own neutrality argument
holds unchanged: the whole block lives inside the `cy < 0` state, and no golden
scenario has a commander lose its yard.

## The fix failed its own first measurement, which is the useful part

The first version ordered the MCV and returned. The sale count **did not move at
all**. Measured cause rather than a reread of the source: the order went out on
one beat, and on the next the producer's queue was no longer empty, so the
search skipped it and fell straight through to the Fire Sale - **selling the
very factory that was building the comeback**.

An MCV already on order now counts as not being beaten. Waiting for one to
finish is not defeat.

## Proved to bite, and for the right reason

`comebackgate`, two stages. Disabling the capability check:

> `comeback: a commander that can BUY an MCV sold 5 structures instead ... it was
> one production order from a full rebuild and called itself beaten`

Stage 1 asserts an **outcome** (did it sell, did a base come back) against the
GDD s7 reading of "beaten", never against a constant in `SkirmishAI`. Stage 2 is
the control: a commander with no tier gate, which genuinely cannot reach an MCV
at any price, **must still fire-sale**. Without stage 2 this row would be
satisfied by deleting the last stand, which is a different bug wearing the same
passing test.

## Two corrections this row owes

### The ticket it was filed under was wrong

P7-16 filed this as *"a commander that loses its Construction Yard stops playing
entirely, because the army block is gated on `wanted == 0`"*. **The measurement
was real and the diagnosis was wrong.** `Act` returns at `if (cy < 0)` a hundred
lines before the army block; the freeze observed was DR-10's deliberate silence,
and the specific measurement - no commands at all, credits untouched - was an
artefact of a fixture with **no enemy structures**, since the Fire Sale is
guarded on having something to throw a last wave at.

The recommended fix in that ticket, `wanted == 0 || cy < 0`, would have changed
nothing while appearing to fix something.

**The lesson is sharper than the correction.** "Measure before diagnosing" is
not satisfied by measuring the *symptom*. The symptom was measured correctly and
the cause was still guessed. The missing column was *which branch returned*. A
fixture that cannot distinguish deliberately-silent from stuck reports a freeze
either way. The ticket is rewritten in place, with its own error first.

### ADR-058 overstated the `canBuyMcv` debt

ADR-058 recorded that the guard was correct but unprovable, four fixtures having
failed to make it bite. A fifth state - **the tier gate destroyed while the yard
survives** - does discriminate, measured: guard on, 9 units and 9,227 credits;
guard off, 10 units and 9,159 credits.

So the guard is **reachable**, which ADR-058 doubted. It is still not carrying
much: the yard ladder rebuilds the radar quickly, so its window is small and no
fixture shows it preventing a real failure. Recorded as measured rather than
claimed as retired, and no gate asserts on a one-unit difference that is
timing rather than correctness.

## An observation deliberately not filed as a defect

A **produced** MCV spawns beside its factory, and in a tightly packed base there
may be no legal cell to deploy into. DR-10 re-issues Deploy every beat, and its
comment says "the MCV keeps trying as it moves" - but nothing orders it to move.
Measured: in a packed fixture the MCV sat undeployed for 6,000 ticks; spread the
same base out and the comeback completes in full.

Not filed, because it is not yet known whether a real base is ever packed
tightly enough, and because P7-16's mis-filed ticket is a fresh reminder of what
a fixture artefact looks like. Whoever measures this on a real map should file it
then. `comebackgate` spreads its base out on purpose so it never accidentally
measures this instead.

## Consequences

A commander that can come back does. One that cannot still goes out with a bang.
All 18 local CI gates green; `yardlossprobe` keeps the four-row table this row
was decided from; client harness PASS.
