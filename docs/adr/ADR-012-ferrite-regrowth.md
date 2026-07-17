# ADR-012: ferrite fields regrow

- Status: Ratified (Architect authored 2026-07-17; ratified by Luke 2026-07-17 under the directive "design out and build all these", whose doc 24 sketch this ADR formalises)
- Date: 2026-07-17
- Deciders: Architect agent + Luke, with Balance under A11 for the numbers
- GDD/TDD feature served: GDD s5 (economy); doc 24's classic-parity finding that permanent depletion caps match length and starves long sieges

## Context

`grep -n regrow sim/Ferrostorm.Sim/World.cs` returns nothing: a ferrite
field spawns with a fixed amount and only ever decreases. The classic genre
regrows its resource, which keeps long games solvent and makes territory
control an income question rather than a countdown. Ferrostorm matches end
when the map is stripped, which no design document chose on purpose.

## Decision

Each FerriteField entity regrows a fixed amount on a fixed interval, up to
its spawn amount, deterministically, in entity-index order inside the
existing per-tick systems (a new small RegrowthSystem step ordered
explicitly in World.Step, never iterating a dictionary).

Two constraints make it a strategy rather than a faucet:

1. **Growth needs a living neighbour.** A field regrows only while its
   remaining amount is above zero. A field stripped to zero is dead ground
   forever. Harvesting territory you do not intend to hold to exhaustion is
   therefore a choice with a cost, and denying the enemy a field remains
   possible by stripping it.
2. **The rate is a trickle, not an income.** The numbers (amount per
   interval, interval length) live in /data once ADR-006's runtime loading
   is the truth, are set by Balance under A11, and start deliberately low:
   the placeholder is 1 unit per 5 seconds (75 ticks) against harvest rates
   an order of magnitude higher, so regrowth extends the late game without
   funding a turtle.

Numbers are data: a `regrow_amount` and `regrow_interval_ticks` pair on the
field definition, defaulting to the placeholder above, validated by schema.

## Alternatives rejected

**Spreading growth (new crystal in adjacent cells).** The classic feel, but
it changes passability over time (fields block or slow nothing today, but
scatter and visuals track amounts), complicates the flow field story for
zero strategic gain at this stage, and makes map authorship unstable: a
map's ferrite geography should be the author's, not the simulation's drift.
Revisit only if a design need appears.

**Unlimited regrowth to spawn cap regardless of remaining amount.** Removes
the strip-to-deny play and makes territory loss recoverable for free;
rejected for flattening a real decision.

**No regrowth (status quo).** Rejected by the parity finding: match length
is capped by an accident nobody chose.

## Consequences

Easier: long games stay solvent; sieges are winnable by economy rather than
by whoever banked first; the AI's late game stops starving.

Harder: Balance owns two new numbers; the battery's long scenarios may see
economies that no longer flatline, which is the point.

Hash impact: MOVES golden hashes (a new system mutates hashed state on a
schedule). One regeneration under this ADR's authority, with the diff
explained scenario by scenario in the implementing wave's delivery notes,
per the doc 23 section 6 discipline. Scenarios whose worlds contain no
ferrite fields must NOT move; that asymmetry is part of the explanation.

Gates: the implementing wave adds a regrowth assertion to the battery (a
field harvested below spawn recovers at the placeholder rate; a stripped
field stays at zero across the same window).
