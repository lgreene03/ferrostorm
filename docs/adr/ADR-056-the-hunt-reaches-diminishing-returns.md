# ADR-056: nothing is stranded, and the hunt reaches diminishing returns
- Status: Ratified
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: doc 24 C9; P7-14

## Context

Seventh outing of the ADR-050 method. This one records a clean result **and
calls the method's diminishing returns**, which is the more useful half.

The question: movement gates prove a unit REACHES a point. **Nothing proves it
never stops trying.** A unit that finishes an order and idles forever is one its
owner paid for and cannot use, and it is invisible to every gate.

## The measurement, and the column that decided it

`idleprobe` over a full 20000-tick match on skirmish-07:

```
   tick   units   idle>900t   idle>3000t   longest   FAR from home (>20 cells)
   5000      31           1            0      1885                           0
  10000      20           4            1      6885                           0
  15000      39           5            3     11885                           0
  20000      32           3            3     16885                           0
```

The first four columns look alarming: three units idle for over 3000 ticks, the
longest **16,885 ticks** - eighteen minutes of a twenty-two minute match.

**The last column is the answer, and it was added rather than reasoned about.**
Zero of them are far from home. Every long-idle unit sits within twenty cells of
its own Construction Yard, which is the garrison doing exactly what the garrison
is for. **Nothing is stranded.**

That is the third time a confusing table was settled by adding a column instead
of a theory (ADR-048's diagnosis, ADR-052's winner column, this one).

## Decision: ship the probe, and DO NOT ship a gate

By ADR-054's own rule - measure what a new gate catches that an existing one does
not - the answer here is **nothing, with no future-proofing argument either**.

`arrivalgate` already proves the field army crosses a 269-cell map and reaches
the enemy base, so a unit stranded en route would fail there. A "no unit idles
far from home" assertion would restate that from the other side and cost another
long match in the battery.

ADR-054 shipped a thin gate because it guarded a **versioned format** that keeps
changing. There is no equivalent here: "garrison units sit still" is the design,
not a property at risk of regressing into a defect.

So `idleprobe` ships as a probe - it answers the question in seconds for whoever
asks it next - and no assertion is added.

## The method reaches diminishing returns, and the evidence for saying so

Seven outings, and the ledger is worth reading as a curve rather than a total:

| | question | outcome |
|---|---|---|
| ADR-050 | what SHAPE is a base? | **defect** - buildings walked 31 cells to the map corner |
| ADR-051 | is a refinery a BOTTLENECK? | **defect** - a second refinery earns 0-1% |
| ADR-052 | does a match ACCUMULATE, and does it END? | **two defects** - 65% dead weight, basin never resolves |
| ADR-053 | does a wave ARRIVE? | clean; gated anyway, arrival is correctness |
| ADR-054 | does the save hold at SCALE? | clean; gate is thin and says so |
| ADR-055 | is the HARNESS itself checked? | **defect** - every faction check vacuous |
| ADR-056 | do units end up STRANDED? | clean; no gate, would duplicate ADR-053 |

The first three outings found four defects. The last four found one, and that one
came from turning the method on the **tooling** rather than the game.

**That is the signal to stop, and the reason is specific rather than a feeling:**
the remaining candidates (harvester re-targeting, production drain) are each
already half-covered by an existing gate - `expansion` now asserts no harvester
idles while ferrite remains, and `prodgate` covers queue acceptance and refusal
with `reachabilitygate` proving every buildable thing is orderable end to end. A
seventh probe against a half-covered question is how a suite gets expensive
without getting stronger.

## What this means for P7

The honest state, recorded because it is what a human picking this up needs:

**The remaining rows are mostly not mine to take.**

- **Wall tiers**, the last untouched feature row, is pure invention: the GDD says
  **nothing about walls at all** - no wall, no barrier, no tier - so B7 is a
  comparison against benchmark games rather than a written design. Adding tiers
  to a working feature on that basis is a design decision, not an engineering one.
- **The commander's faction defences and Veil Projector** are balance additions
  where the common turret already works for both sides.
- **Five ADRs are waiting on one playtest**, and three economy rows have landed
  unplayed.

## Consequences

`idleprobe` ships. No behaviour changed: all 24 goldens byte-identical, catalogue
checksum unmoved, all 18 local CI gates green.

The method's honest final ledger: **five defects and two guarantees from seven
questions**, plus the rule that outlives it - *a bite test that passes when you
break the rule is telling you about the fixture, not the rule* - and the habit
that produced three of the corrections: **when a table confuses you, add a column,
not a theory.**
