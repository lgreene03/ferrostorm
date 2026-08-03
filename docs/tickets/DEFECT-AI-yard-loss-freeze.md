# DEFECT: a commander that loses its Construction Yard stops playing entirely

Labels: `persona:ai-engineer` `gdd:s7` `phase:P7` `owner:ai-engineer`
Found: 2026-08-03, P7-16 (ADR-058), while building a fixture for something else
Severity: **high** - it is an AI that stops responding to the game
Confidence: **measured**, by a controlled comparison (below), not read off the source

## The behaviour

A commander whose Construction Yard is destroyed, but whose Factory, Barracks,
Refinery and harvester are all alive, **spends nothing and builds nothing for
the rest of the match**, however many credits it holds.

This is not an edge case. Killing the yard is the single most common thing that
happens to a base in an RTS, and the benchmark games all expect the loser of
that exchange to keep fighting with what it has left.

## The measurement

Identical worlds, one variable, 1500 ticks, `SkirmishAI.Standard(0)`, 20000
credits, a thin home field, and a Power Plant, Factory, Refinery, Barracks and
harvester in both:

| Construction Yard | credits at tick 1500 | radar | army |
|-------------------|----------------------|-------|------|
| present (control) | 11006                | 1     | 0    |
| absent            | **20000, untouched** | 0     | 0    |

The control spends nine thousand credits climbing its ladder. The yard-less
commander issues no spending command at all. Not a slow start: a full stop.

## The cause, also measured

`SkirmishAI.Act` computes `wanted`, the next structure the yard ladder desires.
The army production block is guarded by:

```
if (factory >= 0 && harvesters >= 1 && !expansionDesired && ownMcv < 0
    && wanted == 0)
```

`wanted == 0` means "the yard wants nothing further" and exists to put
infrastructure before army, which is correct and load-bearing (the comment
above it records a measured deadlock on mission-01 that inserting a rung
fixed).

But with no Construction Yard the ladder can **never** be satisfied, so
`wanted` is never 0, so the army block is never reached. The guard that says
"finish your base before building an army" becomes "never build an army again"
the moment the base cannot be finished.

## The defect class

The same one P7 has now found around sixteen times: **a rule keyed on a
condition that is normally transient and is assumed to always resolve.**
`wanted == 0` is a fine gate while a yard exists to resolve it, and an
unconditional freeze once one does not.

## Candidate fixes, not chosen here

1. Gate on `wanted == 0 || cy < 0` - the smallest change, and it reads as the
   rule it means: defer to the base ladder only while there *is* a base ladder.
2. Gate on whether the wanted structure is **affordable and buildable**, which
   also covers a commander saving forever for a rung it cannot reach for other
   reasons. Broader, and more likely to move goldens.
3. Treat yard loss as a distinct posture (rebuild via MCV if one is owned, else
   fight with what stands), which is the richest and the largest.

Option 1 is the recommendation. It is one condition and it targets exactly the
measured cause.

## Why P7-16 did not take it

One change per wave. P7-16 was answering Q006 and this was found in a fixture
built to test something else. It also **moves goldens** in any scenario where a
commander loses a yard, so it wants its own measurement and its own ADR.

## What this cost P7-16

The fixture that found it was built to prove the AI's new `canBuyMcv` guard
bites, and it could not: this freeze masks that guard completely. ADR-058
records the guard as correct but **currently unobservable** for this reason.
Fixing this defect is what makes that guard testable, so the two are worth
taking in order.
