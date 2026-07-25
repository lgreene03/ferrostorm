# ADR-023: parallel build lanes at the Construction Yard

- Status: Ratified (Architect + sim-engineer + game-designer drafted 2026-07-25;
  ratified the same day under Luke's standing directive to continue designing
  and building out the C-series, the pattern the P6 tracker records for every
  B and C wave)
- Date: 2026-07-25
- Deciders: Architect + sim-engineer + game-designer + Luke
- GDD/TDD feature served: GDD line 45, "two parallel building queues (structures
  / defences)"; P6 campaign tracker wave C3b, split out by ADR-020

## Context

ADR-020 shipped the client half of GDD line 45 (the missing cancel and refund)
and deferred the literal remainder: a Construction Yard holds ONE queue, ONE
BuildProgress head and ONE ready slot, so a structure and a defence cannot build
at the same time. TICKET-P6-C3b filed that remainder and assumed it costs a
golden regeneration plus a save bump, on the grounds that a second head is new
hashed per-entity state.

The design pass overturns the first half of that assumption, which is the third
time a tracker regeneration guess has been wrong (ADR-019 and ADR-021 were the
others). Three facts carry it:

1. **SkirmishAI is strictly serial.** It only ever queues at the yard when
   `QueueLength(cy) == 0` and nothing is ready (SkirmishAI.cs), so it never
   creates a second concurrent order, on any golden it drives (skirmish,
   expansion, aisuper, victory, the missions).
2. **The one scripted defence order in a golden lands in an idle yard.** The
   construction scenario queues a turret at a yard whose queue and ready slot
   are both empty.
3. **A guarded, non-empty hash fold is already shipped and golden-covered.**
   `_orderQueues` folds nothing at all when the dictionary is empty, so an
   unexecuted fold contributing literally nothing is an established, gated
   technique here, not a new trick.

Together those mean a second lane that is only ever reached by OVERFLOW is dead
code on every golden, and the hashes cannot move.

## Decision

The Construction Yard gains a second build lane, reached by overflow, held in a
pruned side collection, hashed under a guard, and serialised behind a v8 magic.

1. **The lane rule is OVERFLOW, not category.** A queued structure goes to lane 1
   whenever lane 1 is fully idle (empty queue, zero BuildProgress, no ready
   structure) and to lane 2 only when lane 1 is occupied. This is the load-bearing
   choice and it is what makes the wave neutral.

   The rejected alternative, "defences always go to lane 2", is the obvious
   reading of GDD line 45 and it moves goldens twice over: the construction
   scenario's turret would leave lane 1 (changing the queue fold and which head
   carries it), and `QueueLength(cy)` would read 0 while a turret built in lane 2,
   so the AI would queue extra buildings and the whole skirmish, aisuper and
   mission line of play would DIVERGE BEHAVIOURALLY, not merely mechanically.

   Overflow delivers the same thing a player observes: order a structure, then
   order a defence while the first is building, and the two now build
   simultaneously instead of the second waiting. What it does not do is
   reserve a permanently idle second line for a category, which nothing in the
   GDD actually asks for.

2. **The second lane is a side collection, never an Entity tail.** A
   `Dictionary<int, BuildLane>` keyed by yard entity id, where a lane carries its
   own queue list, BuildProgress, BuildPaid and ReadyStructure. Appending those
   four to `Entity` instead would be the ADR-014/015 mechanical move of all 24
   goldens for nothing.

3. **The lane entry is PRUNED the moment it is inert.** Whenever a lane's queue is
   empty and its progress, paid and ready are all zero, the dictionary entry is
   removed. This is deliberately unlike `_queues`, whose entries are sticky, and
   it is what makes "entry present" mean "lane active" by construction. The guard
   in the hash is then provably equivalent to inertness rather than merely
   correlated with it, which is the difference between a sound optimisation and a
   silent desync.

4. **The hash fold is conditional and entity-scoped.** One fold inside the entity
   loop, immediately after the existing per-producer queue fold, guarded by the
   lane lookup, adding progress, paid, ready, count and the queued type ids. It is
   placed inside the entity loop rather than as a second top-level block beside
   `_orderQueues` so that two adjacent variable-length untagged folds can never
   present an ambiguous int sequence.

5. **Save format v8, which costs no goldens.** The lane block is appended before
   the trailer behind `SaveMagicV8`, with a `hasBuildLanes` predicate in the
   established style and every later magic listed in every earlier predicate (the
   B1 regression the serialiser's own comments warn about). The runner's
   DowngradeSave learns to strip the block. This is separable from the hash
   question and the ticket wrongly bundled them: `ComputeStateHash` never reads
   the magic, so a version bump moves nothing.

6. **CancelProduce addresses a lane without a wire bump.** The lane rides in the
   high bits of the existing `AuxId`, with lane 0 encoded as the unchanged small
   integer, so every command and every recorded replay written before this ADR
   decodes identically and the .frep format is untouched.

7. **The client reads both lanes.** Because the SIM decides the lane at order
   time, the client cannot statically know where an item went and must not guess:
   it searches both lanes for a type, takes head progress from whichever lane
   holds it as head, and offers a PLACE prompt per ready slot. The yard is only
   fully disabled when BOTH lanes are blocked.

## Alternatives rejected

**Defences always in lane 2 (the literal GDD reading).** Rejected above: it moves
goldens behaviourally through the AI's `QueueLength` gate, and it buys nothing a
player can see that overflow does not.

**A second head appended to Entity.** The straightforward implementation and the
one the C3b ticket assumed. Rejected because it is a mechanical move of all 24
goldens and a permanent per-entity cost paid by every entity in the game
(harvesters, infantry, ferrite fields) for a feature only the Construction Yard
uses.

**N general lanes rather than exactly two.** More flexible and no harder to hash,
but GDD line 45 asks for two, the client surface is built around two tabs, and
an unbounded lane count invites an unbounded ready-slot problem in the UI.
Deferred until something asks for it.

**A new wire field for the cancel lane.** Cleaner to read than bit packing, but it
is a wire and replay format change requiring its own compatibility story, for a
value that fits in the spare high bits of a field that is already there.

## Consequences

Easier: GDD line 45 is met; a player can keep a defence building while a
structure is on the line, which is the actual tempo complaint the ticket exists
to fix; the yard stops being a single-file bottleneck in the late game.

Harder: two ready slots mean the PLACE prompt is no longer single-valued, and the
client carries a little more state; the save format is v8; and there is now a
second place production can be paused, so future production work must remember
both lanes. The pruning rule is load-bearing and must not be relaxed.

Hash impact: NEUTRAL. No Entity field, no new struct or unit type (so the
catalogue checksum is unchanged too), and every new branch is reached only by an
overflow that no golden scenario ever performs. All 24 golden hashes stay
byte-identical. The save moves to v8, which the goldens do not read.

Gates: a LaneGate joins the battery (additive, standalone mode plus a Match
stage, never a golden scenario, so the golden list stays 24). It proves that a
second order at a busy yard overflows into lane 2 and that BOTH build
simultaneously (the point of the wave); that a second order at an IDLE yard still
lands in lane 1 (the neutrality rule itself, asserted rather than assumed); that
each lane's ready slot and refund are independent; that a pruned lane leaves the
hash identical to a world that never had one (the guard proven equivalent to
inertness); and that a v8 save round-trips both lanes bit-exactly while a v7
downgrade of a single-lane world loads hash-identically.
