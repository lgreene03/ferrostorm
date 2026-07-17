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
- ADR-006: DRAFTED to Proposed as ADR-006-data-is-the-runtime-source.md
  (doc 23's claimant took the number). This queue's original topic for the
  slot, the save format decision (snapshot vs command-log replay, decide by
  Phase 2), was overtaken by events: the shipped save/load work and Q001's v2
  format are snapshot-shaped in practice. If that choice is ever to be made
  deliberately rather than inherited, it takes ADR-012.
- ADR-007: DRAFTED to Proposed as ADR-007-rally-in-the-sim.md.
- ADR-008: DRAFTED to Proposed as ADR-008-power-gets-teeth.md.
- ADR-009: DRAFTED to Proposed as ADR-009-the-production-roster.md.
- ADR-010: RATIFIED 2026-07-17 - ADR-010-attack-move-arrival.md (attack-move
  arrival semantics; regenerated four golden hashes).
- ~~ADR-011~~ NUMBER TAKEN by ADR-011-the-starting-hand-enters-the-sim.md
  (2026-07-17, Proposed; resolves Q005). The topic this line held (Lua
  sandbox implementation for map triggers, deterministic subset, decide by
  Phase 3 start) is still open and is re-queued below as ADR-013 so it
  cannot collide again, exactly as the old ADR-004 line was.
- ADR-012: reserved for the deliberate save-format decision if it is ever
  reopened (see the ADR-006 line).
- ADR-013: Lua sandbox implementation for map triggers (deterministic
  subset; decide by Phase 3 start). Re-queued from the ADR-011 line above.
