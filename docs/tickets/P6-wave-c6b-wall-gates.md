# TICKET-P6-C6b: wall gates (deferred, and the deferral is the finding)

Labels: persona:p2, gdd:s5, phase:6, owner:architect + sim-engineer,
gdd:ADR-005-clause-6

Status: **CLOSED 2026-08-01 by P7-10, and NOT by the override this ticket asked
for.** Everything below still describes the situation accurately and is left
standing, because the argument it makes is the one that had to be worked around
rather than beaten. What it did not consider is a gate with a SINGLE GLOBAL
open/closed state, which needs neither per-player flow fields nor an incremental
repair: an open gate is passable to everybody and a closed one is solid to
everybody, and the one global grid says exactly that. ADR-005 clause 6 is
therefore not overturned, and its own revisit precondition is still unmet; see
the P7-10 amendment appended to ADR-005 for the distinction and for what was
traded away (an enemy can follow you through). The two paragraphs below on the
"global auto-open approximation" are the closest anybody came to it, and both of
their objections were answered rather than dismissed: the cache-clear thrash by a
45-tick hysteresis, measured; and the tailgating exploit by accepting it as the
design and asserting it in `wallgategate` so it cannot be quietly removed. The
goldens were predicted here to be a live regeneration risk and were measured
byte-identical.

Was: DEFERRED, and deliberately not merely "pending". Split out of C6 by
ADR-025, which took the bridges half and left this one where ADR-005 put it.

## Why this is not simply the next wave

ADR-005 clause 6 did not forget gates. It deferred them, recorded the blocker,
and named its own revisit condition:

> Passability is one global grid and FlowFieldCache's only invalidation is
> Clear(). A gate that is passable to its owner and solid to the enemy therefore
> requires either per-player flow fields or an incremental flow repair. Neither
> exists, and inventing one to ship a gate would be the tail wagging the dog.
> Walls without gates are a complete feature; the player leaves a gap, as
> players did in 1995.

and

> if per-player flow fields are ever built for another reason, clause 6 is
> revisited with the blocker already gone.

**That precondition is still unmet.** The C6 design pass re-verified it against
the current code: passability is one global `bool[]` with no player dimension,
`FlowField.Build` takes no player argument, and the flow cache is keyed on the
target cell alone. Nothing built since ADR-005 has supplied per-player flow or
incremental repair.

So building gates now means **overriding clause 6**, not satisfying it. That is a
decision for Luke and the Architect. A wave should not assume it, which is why
this ticket exists rather than a ratified ADR.

## Why it is a different risk class from every recent wave

C2, C4, C3b and C6a were all hash-neutral **by construction**: new behaviour
keyed on a new type or kind that no golden ever spawns is byte-identical dead
code. A gate cannot use that argument, because its enabling work is not the gate
entity. It is the passability layer, which every golden exercises.

- **Per-player flow fields** mean `FlowField.Build` takes a per-player
  passability view and the cache key becomes (player, target). Each miss is a
  full Dijkstra over the map (6144 cells at 96x64, 24576 at 192x128), and the
  number of live fields multiplies by the player count. The determinism-critical
  heap tie-break survives, but the overlay itself must be deterministic.
- **A global auto-open approximation** (the cell unblocks when an owner is near)
  dodges per-player fields but mutates the shared grid potentially every tick,
  which under today's code means a full cache clear per toggle. It therefore
  needs the same incremental repair it was meant to avoid, and it ships a
  tailgating exploit.

And the sting, which any implementer must accept before starting: **an
incremental flow repair is a determinism obligation, not an optimisation.** It
must produce fields bit-identical to a full rebuild in every case, or the goldens
move and lockstep desyncs. Repair after an *unblock* is tractable; repair after a
*block* requires invalidating and re-relaxing an unbounded descendant set with
bit-identical results, which is the hard direction.

So this wave's acceptance criterion cannot be "goldens byte-identical by
construction". It must be measured, and a regeneration is a live possibility
rather than something to be engineered away.

## What already exists, so nothing is re-derived

- `World.GateStructType = 10`, reserved with no def; `DefaultStructureType` has
  no case 10 and `SeedStructureTypes` skips it explicitly; `MaxStructType` is 13,
  so 10 is inside the bound and the skip is what keeps it out.
- `FootprintOf` already special-cases the gate as 1x1 before the catalogue
  lookup, and `AnchorOf` already handles size 1.
- `IsBarrier` is `k is EntityKind.Wall` and would gain the gate.
- `EntityKind` next free value is 18, but ADR-025 takes 18 for the bridge, so a
  gate takes the next one after that.
- Doc 22 restates "10 gate (RESERVED by ADR-005, do not take)".

## Needed from whom

- **Luke + Architect:** a decision to override clause 6, or to leave gates
  deferred indefinitely. Leaving them deferred is a perfectly good answer:
  ADR-005's own argument that "walls without gates are a complete feature" has
  not weakened.
- **If overridden, architect + sim-engineer:** an ADR choosing per-player flow
  fields versus incremental repair, with the determinism obligation stated and a
  measured (not assumed) golden impact.
