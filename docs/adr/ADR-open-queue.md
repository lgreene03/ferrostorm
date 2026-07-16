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
- ADR-006: CONTESTED, needs Luke. Two claimants: this queue's original entry
  (save format: snapshot vs command-log replay, decide by Phase 2) and doc 23
  section 9 (data is the runtime source, file name
  ADR-006-data-is-the-runtime-source.md). The save/load work on
  feat/save-load-replays presumably wants the former. One of them must take
  ADR-012.
- ADR-007: RESERVED by doc 23 (rally and spawn: wire and save format, resolves
  Q004). File name planned there: ADR-007-rally-and-spawn.md.
- ADR-008: RESERVED by doc 23 (power's teeth plus radar; includes an ADR-005
  amendment for the radar's struct-type numbering).
- ADR-009: RESERVED by doc 23 (production roster and tech tree; supersedes
  doc 22 lines 1435/1471/2062).
- ADR-010: CLAIMED - attack-move arrival semantics, drafted as
  ADR-010-attack-move-arrival.md on branch fix/amove-prosecute-base
  (Proposed, awaiting Architect + Luke; moves 3 of 24 golden hashes).
- ADR-011: Lua sandbox implementation for map triggers (deterministic subset;
  decide by Phase 3 start). Re-queued from the old ADR-004 line above.
- ADR-012: reserved for whichever ADR-006 claimant loses the number.
