# Open ADR queue

Numbering law: a number is claimed HERE before an ADR is drafted. Reservations
made only in a design document do not appear in anyone's grep of this file,
which is how ADR-010 was nearly drafted as ADR-007 (doc 23 had reserved 007
in prose). If a document reserves a number, mirror the reservation here in
the same commit.

- ~~ADR-002: fixed-point library~~ CLOSED by ADR-002-fixed-point.md
- ~~ADR-003: Infantry squads vs individuals~~ CLOSED by ADR-003-infantry-squads.md
- ~~ADR-004~~ NUMBER TAKEN by ADR-004-engine-strategy.md (2026-07-14). The
  topic this line used to hold (Lua sandbox implementation for map triggers,
  deterministic subset, decide by Phase 3 start) is still open and is
  re-queued below as ADR-011 so it cannot collide again.
- ~~ADR-005: Tile size, grid resolution, footprint rules~~ CLOSED by
  docs/adr/ADR-005-footprints-and-barriers.md (RATIFIED 2026-07-15)
- ADR-006: RATIFIED 2026-07-17 - ADR-006-data-is-the-runtime-source.md (shipped, P6 Wave B1)
  (doc 23's claimant took the number). This queue's original topic for the
  slot, the save format decision (snapshot vs command-log replay, decide by
  Phase 2), was overtaken by events: the shipped save/load work and Q001's v2
  format are snapshot-shaped in practice. If that choice is ever to be made
  deliberately rather than inherited, it takes ADR-012.
- ADR-007: RATIFIED 2026-07-17 - ADR-007-rally-in-the-sim.md (shipped, P6 Wave B2).
- ADR-008: RATIFIED 2026-07-17 - ADR-008-power-gets-teeth.md (shipped, P6 Wave B3).
- ADR-009: RATIFIED 2026-07-17 - ADR-009-the-production-roster.md (shipped, P6 Wave B4).
- ADR-010: RATIFIED 2026-07-17 - ADR-010-attack-move-arrival.md (attack-move
  arrival semantics; regenerated four golden hashes).
- ~~ADR-011~~ NUMBER TAKEN by ADR-011-the-starting-hand-enters-the-sim.md
  (2026-07-17, RATIFIED; resolves Q005). The topic this line held (Lua
  sandbox implementation for map triggers, deterministic subset, decide by
  Phase 3 start) is still open and is re-queued below as ADR-013 so it
  cannot collide again, exactly as the old ADR-004 line was.
- ~~ADR-012~~ NUMBER TAKEN by ADR-012-ferrite-regrowth.md (RATIFIED
  2026-07-19). The topic this line held, the deliberate save-format decision,
  is still open and is re-queued below as ADR-017 so it cannot collide again.
- ~~ADR-013~~ NUMBER TAKEN by ADR-013-skirmish-map-redesign.md (RATIFIED
  2026-07-20). The topic this line held (Lua sandbox implementation for map
  triggers, deterministic subset, decide by Phase 3 start) is still open and
  is re-queued below as ADR-016 so it cannot collide again.
- ADR-014: RATIFIED 2026-07-20 - ADR-014-no-progress-settle-backstop.md
  (no-progress crowd-settle backstop; Q013 nightly-soak fix; regenerated all
  24 goldens; save format v6).
- ADR-015: RATIFIED 2026-07-21 - ADR-015-unit-command-stances.md (unit
  command stances: hold-fire, guard, patrol; resolves Q003; save format v7;
  regenerated all 24 goldens; shipped on main, P6 Wave C1a). Formations are
  split to a filed C1b follow-up.
- ADR-016: Lua sandbox implementation for map triggers (deterministic subset;
  decide by Phase 3 start). Re-queued from the ADR-013 line above.
- ADR-017: reserved for the deliberate save-format decision if it is ever
  reopened (see the ADR-006 line). Re-queued from the ADR-012 line above.
- ADR-024: PROPOSED 2026-07-25 - ADR-024-multi-resource.md (a second resource
  type, P4-PORT-04; P6 Wave C8). Number claimed here per the numbering law.
  NOT RATIFIED and deliberately not self-ratified: unlike every other C wave,
  the GDD is SILENT on a second resource and doc 21's own ticket says "GDD
  decision first", so this is a design authority that does not exist yet rather
  than a design to be implemented. Q014 asks for it. The ADR carries the
  decisive engineering fact: YIELD into the one treasury is hash-NEUTRAL, while
  separate currency POOLS move all 24 goldens by construction (the per-player
  fold in ComputeStateHash cannot be guarded), so the cheap option and the
  design-correct option are the same one.
- ADR-023: RATIFIED 2026-07-25 - ADR-023-parallel-build-lanes.md (parallel
  structure/defence build lanes at the Construction Yard, GDD line 45's
  remainder; P6 Wave C3b). Number claimed here per the numbering law. Hash
  impact NEUTRAL, overturning the C3b ticket's assumption: the lane rule is
  OVERFLOW (lane 1 whenever idle, lane 2 only when lane 1 is busy), not
  category, and no golden ever overflows because SkirmishAI is strictly serial
  and the one scripted turret order lands in an idle yard. The second lane is a
  pruned side collection with a guarded hash fold (the _orderQueues precedent),
  never an Entity tail append. Save goes to v8, which costs no goldens because
  the magic is not hashed. Ratified under Luke's directive to continue building
  out the C-series.
- ADR-022: RESERVED 2026-07-24 for the LAN setup exchange (TICKET-P6-C7b): a
  host-supplied match-setup blob appended to the lockstep Hello frame so a
  joiner builds the identical world. A WIRE-FORMAT change, so it takes this
  ADR before code; the non-blocking poll half of Q002's remainder shipped
  without it as Wave C7a (additive TryAdvanceTick + the lanpoll chaos gate,
  no wire change).
- ADR-021: RATIFIED 2026-07-24 - ADR-021-neutral-outpost.md (the neutral
  capturable Outpost, GDD line 41 / doc 22 P5-ECON-14; P6 Wave C4). Number
  claimed here per the numbering law. Design ratified and READY TO IMPLEMENT;
  hash impact NEUTRAL (EntityKind.Outpost = 17 already reserved and inert, all
  behaviour Kind==Outpost gated, no golden scenario spawns one, income reuses
  the already-hashed _credits pool, no new hashed Entity field), so the existing
  24 goldens stay byte-identical, proven by an additive OutpostGate rather than a
  golden regeneration. Ratified under Luke's 2026-07-24 directive to implement
  out the C-series.
- ADR-020: RATIFIED 2026-07-24 - ADR-020-sidebar-cancel-and-queue-scope.md
  (the four-queue sidebar, GDD line 45; P6 Wave C3). Number claimed here per the
  numbering law before drafting. Scope decision: C3 ships the client-only,
  hash-NEUTRAL half - right-click cancel/refund on every sidebar build item
  (the client never issued CancelProduce at all) over the existing QueueContents
  and CancelProduce - and confirms infantry/vehicles are already two parallel
  queues. The literal GDD-45 remainder, TWO parallel structure/defence queues on
  one Construction Yard, needs a second build-progress head and ready slot =
  hashed sim state and a save bump, so it is deferred to TICKET-P6-C3b with its
  own golden-move ADR. Ratified under Luke's 2026-07-24 directive to implement
  out the C-series.
- ADR-019: RATIFIED 2026-07-24 - ADR-019-repair-vehicle.md (the repair
  vehicle: a mobile field-repair unit reusing the Service Depot heal loop as a
  moving aura; P6 Wave C2, GDD line 62). Number claimed here per the numbering
  law before drafting. Hash impact NEUTRAL: a new unit type id (13) whose heal
  branch fires only for that type, which no golden scenario spawns, so all 24
  goldens stay byte-identical; no new EntityKind, no new hashed Entity field, no
  schema change, save stays v7. Ratified under Luke's 2026-07-24 directive to
  implement out the C-series.
- ADR-018: RATIFIED 2026-07-24 - ADR-018-formations.md (formations:
  deterministic slot assignment on group move orders; the C1b follow-up
  ADR-015 promised, P4-PORT-05; shipped as P6 Wave C1b, client-only). The
  client-side transient model, so hash impact is NEUTRAL (no Entity field, no
  wire command, no save bump; all 24 goldens byte-identical). Cohesive
  formation movement, the part that would need hashed sim state and a golden
  regeneration, is deferred to a future ADR when a concrete need appears.
