# ADR-018: formations - deterministic slots on group move orders

- Status: Ratified (Architect + game-designer drafted 2026-07-24 per
  TICKET-P6-C1b, which names them the ADR authors; ratified the same day by
  Luke's directive "do the client side first" against this ADR's client-side
  recommendation, the standing-directive ratification pattern ADR-012/014/015
  use. The client-side formation layer shipped under it as P6 Wave C1b; cohesive
  formation movement remains deferred to a future ADR)
- Date: 2026-07-24
- Deciders: Architect + game-designer + Luke, with Balance consulted on lattice
  spacing if a persistent cohesion layer is ever built
- GDD/TDD feature served: doc 21 UE5 portability audit gap P4-PORT-05
  (formations), the follow-up ADR-015 split out as its own design pass and
  filed as TICKET-P6-C1b; GDD s5 massed-order legibility

## Context

Formations are the last open row of wave C1. ADR-015 shipped the three per-unit
stances (hold-fire, guard, patrol) as C1a and deferred formations to C1b on the
grounds that formations are "a different problem: deterministic slot assignment
and a group-order layer above the per-unit command", worth its own design pass
and its own regeneration. TICKET-P6-C1b is that placeholder and lists the four
things this ADR must decide: deterministic slot assignment, the transient versus
persistent group-order layer, the interaction with the C1a stances, and the
fixed-point geometry rule. This ADR decides all four.

The code read reframes the hardest of those four. The sim has no concept of a
selection or a group: every `Command` carries a single `EntityId`
(World.cs:25-39), and a group move is already N separate per-entity Move,
PathMove or AttackMove commands, all with the same clamped X/Y, applied one at a
time in `ApplyCommandCore` (World.cs:1098-1126). The movement code names this gap
directly, deferring "Formation offsets are a P2 ticket" at World.cs:1619, and
today leans on two crowd mechanics instead: the crowd-arrival shortcut freezes a
flow-pathing unit within four cells of the shared destination
(World.cs:1624-1626) and `SeparationSystem` pushes the pile apart
(World.cs:2352-2382). So a massed order lands as a clump that settles into a rough
blob, not assigned slots.

The decisive observation is that the C1b ticket's stated hard requirement,
"deterministic slot assignment identical on every client", is a requirement only
if the SIM expands a single group order into per-unit targets. In lockstep only
the issuing player's client computes a command; that command is broadcast and
every other client applies it verbatim (World.cs:920-937 applies whatever
commands the tick carries). So if the client resolves each unit's slot to a
concrete destination and issues the existing per-unit Move with that destination,
the wire carries the resolved targets and cross-client agreement is automatic and
total. The determinism problem the ticket treats as central does not arise at all
in that model; it arises only in the sim-expansion model, which is the more
expensive one. Fixed-point reinforces the same split: Fix64 has `Sqrt` and
`DistSq` but no trig, no rotation and no normalise (Fix64.cs), so a rotated slot
lattice computed in the sim would be new fixed-point geometry, whereas the same
lattice computed client-side is ordinary presentation maths outside the hash.

## Decision

Formations ship as CLIENT-COMPUTED deterministic slot offsets on group move
orders, resolved to per-unit destinations and issued through the existing
per-entity Move, PathMove and AttackMove commands. There is no new Entity field,
no new wire command, no save-format bump and no golden regeneration. The
group-order layer lives on the client side of the ADR-001 line, where the
selection already lives; the sim stays per-entity and unchanged. Cohesive
formation MOVEMENT, the part that would need hashed sim state, is deferred (see
below), because there is no cohesion behaviour to host in the sim yet.

1. **The group layer is the client's, because the selection is the client's.**
   The client already owns the selection set and issues one command per selected
   unit. A formation order takes that set of movable combat units, computes a
   slot offset per unit, and issues each unit its own destination equal to the
   order's anchor point plus that unit's rotated offset, using the same command
   verb the order modifier already selects (right-click PathMove, A attack-move,
   plain Move). Nothing new reaches the sim; the resolved destinations are
   ordinary clamped command coordinates, converted to Fix64 at the command
   boundary exactly as a single click is today.

2. **Deterministic, stable slot assignment by sorted entity id.** The client
   sorts the selected unit ids ascending and maps sorted rank to a slot in a
   fixed lattice. Sorting by id is order-independent (the selection order does
   not change the result), stable (the same group re-issued gives each unit the
   same slot), and cheap. This is the "sort by entity id into slot offsets" the
   C1b ticket names as the obvious candidate. Because only the issuing client
   computes and the resolved commands are what the wire and the replay record
   carry, the assignment need not be bit-exact across platforms; it need only be
   sensible and repeatable on the issuer, which sorted-id assignment is. A
   travel-minimising assignment (the Hungarian match) is rejected below as
   machinery ahead of the requirement.

3. **The lattice and facing.** The default shape is a box grid sized to the group
   count, oriented so its forward axis points from the group centroid to the
   order anchor, so a moving group faces its destination. Slot spacing is a
   client constant tuned to unit footprint and the four-cell settle radius so
   slots do not collide with the crowd shortcut. The rotation of each offset by
   the facing angle is client presentation maths and MAY use float and Godot
   vector helpers; it never touches the sim, so the sim's no-float rule does not
   bind it. This is a direct benefit of placing the layer client-side, given
   Fix64 carries no trig. Line and wedge shapes are a later client-only
   extension over the same mechanism and need no ADR.

4. **Interaction with the C1a stances falls out for free, and is the wanted
   behaviour.** Each resolved order is a normal Move, PathMove or AttackMove, so
   ADR-015's transition rules apply per unit unchanged: the order cancels Guard
   and Patrol back to Aggressive (a formation move is a new activity that
   supersedes a standing positional stance) and PRESERVES HoldFire (fire
   discipline is a persisting preference). The C1b ticket asked the design pass
   to confirm this is wanted for a formation move; it is. A player forming up a
   hold-fire column to reposition past a sentry keeps its fire discipline, which
   is the same engineer-past-the-sentry intent ADR-015 protected. No special
   casing is needed anywhere.

5. **Fixed-point rule, satisfied by construction.** The only fixed-point values
   that reach the sim are the resolved per-unit destination coordinates, already
   Fix64 like every command coordinate and clamped by the existing handler
   (World.cs:1105-1109). Slot geometry itself is client presentation and is not
   bound by the determinism rule, which is why this model sidesteps the absence
   of trig in Fix64 rather than having to add it.

6. **Replays and saves are untouched and correct.** A replay records the resolved
   per-unit commands, so a recorded formation move re-simulates bit-exactly with
   no formation concept in the sim at all; this is the same reason client-side
   rally and harvest replay correctly today (Q004). Saves carry no formation
   state because there is none to carry; the save format stays v7.

## What is deferred, and to what

Cohesive formation MOVEMENT is explicitly out of scope and deferred to a future
ADR, to be raised when a concrete need appears: units moving at the speed of the
slowest, waiting for stragglers, holding relative positions continuously en
route, re-forming after combat, or continuous re-facing as the group turns. That
behaviour is per-tick simulation that changes each unit's movement from the group
state, so it must be hashed per-entity state that two clients agree on and that
survives a save, precisely the FormationId-plus-slot-offset shape the C1b ticket
sketched as the persistent option. Building it means a hashed Entity tail append
(moving all 24 goldens, a save bump to v8, and the DowngradeSave surgery at
Program.cs:2256-2307 learning the new tail), which is the regeneration the tracker
anticipated. We do not build it now because the crowd-arrival settle already gives
a massed order a coherent body, slot resolution already gives it assigned
positions, and there is no cohesion requirement in the GDD or the portability
audit beyond "formations", which the slot layer satisfies. Building cohesion now
would be machinery ahead of the requirement and a golden move spent on behaviour
no scenario yet needs, the same discipline ADR-015 applied when it deferred the
multi-waypoint patrol list.

## Alternatives rejected

**Sim-side group expansion: a new GroupMove command the sim expands into
per-unit slot targets, with FormationId and a slot offset as hashed per-entity
state.** This is the persistent option the C1b ticket names, and it is the more
faithful "group-order layer above the per-unit command". Rejected as the near-term
shape for three reasons. First, it manufactures the very determinism problem the
client model dissolves: the sim would have to assign slots identically on every
client in fixed-point, and Fix64 has no rotation or trig, so the lattice geometry
would be new and delicate sim code. Second, it is a golden move and a v8 save bump
for a feature whose entire observable effect, units standing in assigned slots, is
already achieved by resolving targets client-side at zero hash cost. Third, and
decisively, a slot is not per-tick behaviour: once resolved to a target there is
nothing for the sim to do each tick that it does not already do for any move, so
hosting the slot in the sim buys no behaviour, only cost. The moment there IS
per-tick behaviour to host (cohesion, hold-formation), this alternative becomes
the right answer, which is why it is deferred rather than dismissed.

**Travel-minimising slot assignment (Hungarian or greedy-nearest) instead of
sorted id.** A better-looking initial shuffle, each unit walking to its nearest
free slot rather than its id-rank slot. Rejected for now as machinery ahead of the
requirement: it is more code, and sorted-id assignment is stable and legible and
meets the "deterministic and identical" bar the ticket set. A nearest-slot
refinement is a client-only change over the same mechanism if playtests show the
id-rank walk looks poor, and needs no ADR to adopt later.

**Do nothing and lean on the crowd-arrival settle alone.** The status quo: a
massed order clumps and settles into a blob within four cells. Rejected because
P4-PORT-05 is a genuine command-surface gap against the commercial-RTS bar and a
blob reads worse than assigned slots for the same order; slot resolution is cheap
and closes the gap without touching the sim.

**Put slot geometry in the sim in Fix64 anyway, for a single authority.** Rejected
because it gains nothing the client model lacks (the resolved targets are already
the single authority once on the wire) while forcing new fixed-point trig or a
lattice built from Sqrt-normalised vectors, for presentation-shaped maths that has
no reason to be deterministic across platforms.

## Consequences

Easier: P4-PORT-05 closes and wave C1 completes with formations that place units
in assigned, forward-facing slots; the command surface matches the portable-RTS
spec; the client gains a formation input over machinery it already has (a
selection and per-unit command issue), so the change is contained to one input
path and the slot maths; stance interaction is automatic and correct; replays and
saves need no thought because nothing sim-side changed. Line, wedge and
nearest-slot refinements are later client-only tuning with no ADR cost.

Harder, or rather deferred: cohesive formation movement (speed-matching,
hold-formation, continuous re-facing, re-form after combat) does not exist and,
when wanted, is a separate ADR carrying hashed per-entity state, a save bump to
v8, the DowngradeSave tail surgery, and one golden regeneration. Recording that
boundary here is half the value of this ADR: it stops a later contributor adding a
FormationId field and moving all 24 goldens without realising the slot feature
never needed it.

Hash impact: NEUTRAL. No Entity field, no CommandType addition, no save-format
change, no golden move. The determinism CI stays green untouched and no
regeneration or Architect hash sign-off is required for this wave, unlike C1a.
The standing gates (full battery exit 0, golden hashes byte-identical) must remain
green through the client change exactly as for any presentation work.

Gates: the implementing wave adds no sim gate because there is no sim change. The
client acceptance is a scripted formation order over a known selection asserting
that each unit receives a distinct resolved destination equal to anchor plus its
sorted-id slot offset, that re-issuing the same group reproduces the same
assignment, that the order verb (Move/PathMove/AttackMove) is preserved per the
modifier, and that a hold-fire unit in the group keeps HoldFire while a guarding
one drops to Aggressive, per ADR-015. The full battery and both client builds stay
green.

## Changed / Assumed / Needed next (from whom)

**Changed.** Added this ADR and claimed ADR-018 in docs/adr/ADR-open-queue.md
(numbering law: the number is claimed before drafting). TICKET-P6-C1b updated to
record the ADR drafted and Proposed, pending ratification.

**Assumed.** That "formations" in P4-PORT-05 and the GDD means slot placement on
group orders, not continuous cohesive movement, which no design doc requires
beyond the word; cohesion is deferred to its own ADR on evidence of need. That the
client owning the group layer is consistent with ADR-001, since a resolved
per-unit destination is the same class of thing the client already issues and the
sim remains the sole authority on the resulting movement. That sorted-id slot
assignment meets the "deterministic and stable" bar; a nearest-slot refinement is
a later client-only option.

**Needed next (from whom).** Architect + game-designer: ratify or amend this ADR;
in particular confirm the client-side placement over the sim-expansion
alternative, since that is the load-bearing choice and the one that diverges from
the tracker's anticipated regeneration. Luke: gate sign-off to move C1b from
Proposed to build, per the C1b ticket's "no code until ratified". On ratification,
sim-engineer has nothing to build; client-engineer implements the formation input
and slot resolution with the acceptance above, and the tracker row C1b flips to
DONE with cohesion filed as a new deferred ADR row.
