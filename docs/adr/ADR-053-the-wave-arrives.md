# ADR-053: the wave arrives, and a negative result worth gating
- Status: Ratified
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: doc 24 C9's AI half; P7-11

## Context

Fourth wave of the ADR-050 method, and the first that found **nothing wrong**.

The method has sharpened across three waves into something more specific than
"look for untested things". Every defect it found sat **beside a gate that asked
a neighbouring question**:

- `basingate` plays skirmish-07 and asks whether it is a *stalemate*, never
  whether it *ends* (ADR-052).
- The load scenario asks what 600 units *cost*, never what a match *accumulates*
  (ADR-052).
- Placement had gates; nothing asked what *shape* a base was (ADR-050).
- Harvesting had gates; nothing asked whether a refinery is a *bottleneck*
  (ADR-051).

The next question in that family was obvious once stated. **`aitargetgate` proves
where an attack wave is AIMED. Nothing had ever asked whether it ARRIVES.**

That failure would be silent and total: a commander that aims perfectly and never
crosses the map has not attacked, and every targeting gate passes regardless.

## The measurement

skirmish-07 is the case that matters, its starts being the furthest apart of any
shipped map precisely so a crossing is a commitment. Measured over a full match:

```
   tick   army0   nearest0   within10   army1   nearest1   within10
   4500      16         31          0      15         13          0
   9000       8         20          0      11         14          0
  13500      18         21          0      16         11          0
  18000      23          5          3       9         78          0
  22500      21          7          2      16         31          0
  27000      13         35          0      16         32          0
```

**Both commanders close a 269-cell gap to within four or five cells**, and at
tick 18000 seat 0 has three units standing inside the enemy base.

**There is no defect here.** The waves arrive.

## Decision

**Ship it as a GATE rather than a probe**, which is the whole decision in this
ADR and the reason it exists at all.

The three previous hunts produced probes, because what they found were balance
questions - whether a refinery should be a bottleneck, whether a long match
should be shortened - and a probe reports where an assertion would be a number
invented to pass itself.

**Arrival is not a balance question. It is correctness.** "The commander's army
reaches the enemy" is either true or the game is broken, and there is no reading
of the design under which a wave that never lands is acceptable. So it earns an
assertion.

### The bound, and where it comes from

`arrived` is `World.CyBuildRadius + 3`. Being within the yard's own build radius
is being **among its buildings**, which is what arriving at a base means, and the
three cells of slack are for a unit that stops at weapon range of the first thing
it meets. A reader can check that against the game's own rule rather than
against a number I liked.

Rejected: **a fraction of the starting gap.** It sounds more general and is
worse - it would accept "got halfway" on a big map and demand pinpoint accuracy
on a small one, when what matters is the same on both.

### Proved to bite

Attack waves were disabled and the gate reported:

> `seat 0 never reached seat 1's base - closest approach 139 cells from a start
> gap of 269 ... Its waves are AIMED correctly and aitargetgate would still pass;
> they simply never get there`

**That message is literally true in that state**, which is the point: with waves
disabled the commander still selects targets, and `aitargetgate` still passes.
139 cells against a bound of 10.

## On negative results

Worth recording because three consecutive hunts finding defects makes a fourth
finding nothing feel like a failure, and it is not.

**A hunt that finds nothing has bought a guarantee**, provided it leaves an
assertion behind. The suspicion is closed, and it is closed permanently rather
than until someone next wonders. The alternative - concluding "waves probably
arrive, no row here" and moving on - buys nothing at all and would have to be
re-asked after any pathfinding change.

The cost is honest: this gate plays a 20000-tick match, matching `basingate`'s
horizon on the same map, and that is real battery time for a property that is
currently true. It is worth it because the failure mode is invisible to
everything else.

## Consequences

`arrivalgate` in the battery, proved to bite. No behaviour changed: all 24
goldens byte-identical, catalogue checksum unmoved, all 18 local CI gates green.

The method's fourth outing and its first clean bill of health. Three defects and
one guarantee from four questions asked one step to the side of an existing gate.
