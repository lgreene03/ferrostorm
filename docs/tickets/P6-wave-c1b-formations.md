# TICKET-P6-C1b: formations (filed pending)

Labels: persona:p2, gdd:s10, phase:6, owner:architect + sim-engineer, gdd:P4-PORT-05

Status: FILED, pending. Split out of Wave C1 by ADR-015, which shipped the three
unit command stances as C1a and deferred formations to its own ticket rather than
land them half-built. This file is the placeholder the ADR promised ("TICKET-P6-C1b,
filed alongside this wave"); it is not started.

## Why it is its own wave

Formations (P4-PORT-05 in doc 21's portability audit) are a different problem from
the per-unit stances of C1a: deterministic slot assignment and a group-order layer
ABOVE the per-unit command, not a per-unit behaviour. Building it inside C1a would
have either bloated that wave or shipped it half-done. The crowd-arrival settle
(units freeze within four cells of a shared destination) already gives massed
orders a coherent enough body for now; formations tightens that into assigned slots
and is worth its own design pass and its own golden regeneration.

## What it must decide (for the design pass, not now)

- **Deterministic slot assignment.** A group order must map units to slots in a way
  that is identical on every client, order-independent and stable, so two lockstep
  clients agree. Sorting by entity id into slot offsets is the obvious candidate;
  the design pass owns the choice and its hash cost.
- **The group-order layer.** Whether a formation is transient (computed per move
  order) or persistent per-entity state (a formation id plus a slot offset that
  hashes and serialises, the rally and stance precedent). Persistent state is a
  golden move and a save-format bump; transient is neither but re-solves each order.
- **Interaction with the C1a stances.** A formation move is a group Move, so under
  ADR-015's transition rules it cancels Guard and Patrol and preserves HoldFire.
  The design pass should confirm that is the wanted behaviour for a formation.
- **Fixed-point geometry only.** Slot offsets are Fix64, no float, per the sim
  determinism rule.

## Needed from whom

- **architect + game-designer:** a formations ADR (deterministic slot assignment,
  the group-order layer, the hash and save cost) before any code.
- **sim-engineer:** the implementation once the ADR is ratified, with its own gate
  and golden regeneration.

The C1a stances did not build any of this; they are self-contained and complete
without it.
