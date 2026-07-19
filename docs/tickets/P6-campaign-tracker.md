# P6 campaign tracker: design out and build all these

Authority: Luke's directive of 2026-07-17, "design out and build all these",
issued against the classic-parity gap analysis (doc 24). The directive
ratified ADR-006, 007, 008, 009 and 011 as drafted (each Status line cites
it). Every wave below ships under the standing law: full battery exit 0,
goldens byte-identical except where the wave's own ratified ADR authorises a
regeneration, both client builds clean, CI green on both platforms before
merge, ledger entry appended. Waves run sequentially (one agent at a time;
the repo is one working tree). Update the status column here as each wave
lands; this file is the resume point if a session dies.

| # | Wave | Authority | Hash impact | Status |
|---|------|-----------|-------------|--------|
| A | Doc 24 Tier 3: faction picker, music, VO, cursors | doc 24 tickets P6-FACTION-01, MUSIC-01, VO-01, CURSOR-01 | neutral | DONE (343c482) |
| B1 | /data becomes the runtime source | ADR-006 (ratified) | neutral | DONE (8e375ce) |
| B2 | Rally into the sim, THEN spawn occupancy and the refund | ADR-007 (ratified); doc 23 Wave 4 order is load-bearing | ONE regeneration | DONE (c5b2f90) |
| B3 | Power gets teeth: turret gate, draws, radar blackout | ADR-008 (ratified); walls-gate phase G amendment mandatory | ONE regeneration | DONE (52004ee) |
| B4 | Barracks split, tabbed sidebar, tech tree, AI learns barracks | ADR-009 (ratified); doc 23 Wave 6 | regenerations per doc 23 s6 | DONE (2fbfcc0) |
| B5 | Starting hand into the sim + CellCentre decision | ADR-011 (ratified); Balance note on the 550-tick shift | regeneration (skirmish golden) | pending |
| B6 | Ferrite regrowth | ADR-012: formalise from doc 24 sketch, then implement | regeneration | pending |
| C1 | Unit command layer: hold-fire, guard, patrol, formations | new ADR required (resolves Q003, P4-PORT-01, P4-PORT-05) | regeneration | pending |
| C2 | Repair vehicle | GDD line 62; data-driven, post-B1 | depends on design | pending |
| C3 | Four-queue sidebar (GDD line 45 in full) | post-B4 design | likely neutral (client) | pending |
| C4 | Neutral outposts | new ADR (EntityKind 17 reserved; doc 22 P5-ECON-14) | regeneration | pending |
| C5 | Air layer: airfield-slot model, strike aircraft, transport heli | new ADR required (P4-PORT-02; ADR-009 exclusion) | regeneration | pending |
| C6 | Wall gates + destroyable bridges | ADR-005 clause 6 revisit; needs incremental flow repair | regeneration | pending |
| C7 | Two-machine LAN: non-blocking frame-loop integration | Q002 remainder | neutral (net layer) | pending |
| C8 | Multi-resource fields | P4-PORT-04, new ADR | regeneration | pending |
| C9 | Faction recipe deepening | P4-PORT-06 | depends | pending |

Excluded from the directive, needing separate sign-off: naval combat and FMV
briefings (GDD amendments); crates and a map editor (GDD-silent, Producer
rule). The VO clip set is placeholder TTS pending the legal-review check
recorded in doc 24.
