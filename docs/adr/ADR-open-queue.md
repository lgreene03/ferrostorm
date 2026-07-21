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
- ADR-015: NUMBER CLAIMED by the in-flight unit-stance wave on branch
  ticket/p6-wave-c1 (ADR-015-unit-command-stances.md: hold-fire, guard,
  patrol; resolves Q003). Not yet on main.
- ADR-016: Lua sandbox implementation for map triggers (deterministic subset;
  decide by Phase 3 start). Re-queued from the ADR-013 line above.
- ADR-017: reserved for the deliberate save-format decision if it is ever
  reopened (see the ADR-006 line). Re-queued from the ADR-012 line above.
