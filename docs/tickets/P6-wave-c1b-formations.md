# TICKET-P6-C1b: formations (filed pending)

Labels: persona:p2, gdd:s10, phase:6, owner:architect + sim-engineer, gdd:P4-PORT-05

Status: DONE (client-side slot layer), 2026-07-24, under ADR-018 (ratified).
Cohesive formation movement deferred to a future ADR. Split out of Wave C1 by
ADR-015, which shipped the three unit command stances as C1a and deferred
formations to its own ticket rather than land them half-built.

The design pass this ticket asked for is docs/adr/ADR-018-formations.md
(ratified 2026-07-24), which decides all four questions below. Its headline
decision is that formations ship as CLIENT-COMPUTED deterministic slot offsets
resolved to per-unit destinations over the existing per-entity
Move/PathMove/AttackMove commands, because the sim has no selection concept and a
resolved slot is not per-tick behaviour. That makes the "identical on every
client" requirement automatic (only the issuing client computes; the wire carries
the resolved commands), so hash impact is NEUTRAL: no Entity field, no wire
command, no save bump, no golden regeneration.

Delivered in game/scripts/SkirmishLive.cs (presentation only, ADR-001 intact): a
BuildFormation helper resolves the selected COMBAT units (EntityKind.Unit) into a
forward-facing box lattice, slot assignment by SORTED ENTITY ID (order-independent
and stable), spacing a tunable presentation constant. It is wired into every
bare-ground group move path (the right-click IssueOrder move branch, the mouse
CommitAttackMove, and the programmatic OrderMoveTo/OrderAttackMoveTo verification
hooks); harvesters and other non-combat mobiles are never members and keep the
plain anchor, and an attack on an enemy under the cursor is unchanged. Stance
interaction falls out for free because each resolved order is an ordinary move, so
ADR-015's per-unit rules apply (cancels Guard/Patrol, preserves HoldFire). A public
ResolveFormationSlots hook returns the raw Fix64 slots for offscreen assertion. No
new key or binding: formations are emergent on any group move of two or more
combat units.

PROOF: both Godot client builds 0 warnings (Debug and ExportRelease); the full sim
battery exits 0 and all 24 goldens are BYTE-IDENTICAL to the committed file (the
NEUTRAL hash impact ADR-018 promised, since no sim code changed); five-seed
determinism and the LAN soak unaffected. Needs a human: whether the box shape,
spacing and facing feel right in the running client, and whether a nearest-slot
assignment or line/wedge shapes are wanted (all later client-only tuning, no ADR).

Cohesive formation MOVEMENT (speed-matching, hold-formation, continuous re-facing,
re-form after combat) is the part that would need hashed sim state and a golden
regeneration, and stays deferred to a future ADR on evidence of need, per ADR-018.

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
