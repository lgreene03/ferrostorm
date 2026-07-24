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
| B5 | Starting hand into the sim + CellCentre decision | ADR-011 (ratified); Balance note on the 550-tick shift | regeneration (skirmish golden) | DONE (e814e10) |
| B6 | Ferrite regrowth | ADR-012: formalise from doc 24 sketch, then implement | regeneration | DONE (d7ff34c) |
| C1a | Unit command stances: hold-fire, guard, patrol | ADR-015 (ratified); resolves Q003 and P4-PORT-01 | ONE regeneration | DONE (a9d041a) |
| C1b | Formations: deterministic slot assignment on group orders | ADR-018 (ratified): client-side slot layer, sim unchanged | DONE (client-side); cohesion deferred |
| C2 | Repair vehicle | ADR-019 (ratified): reuses the depot heal loop as a mobile aura | DONE (NEUTRAL, no regen) |
| C3 | Four-queue sidebar (GDD line 45): the client cancel/refund | ADR-020 (ratified): client-only right-click cancel | DONE (client half, NEUTRAL) |
| C3b | Parallel structure/defence queues (GDD line 45 remainder) | split out by ADR-020; second build head + ready slot | pending (golden move) |
| C4 | Neutral outposts | ADR-021 (ratified): capturable income Outpost, struct type 13 | DONE (NEUTRAL, no regen) |
| C5 | Air layer: airfield-slot model, strike aircraft, transport heli | new ADR required (P4-PORT-02; ADR-009 exclusion) | regeneration | pending |
| C6 | Wall gates + destroyable bridges | ADR-005 clause 6 revisit; needs incremental flow repair | regeneration | pending |
| C7a | Non-blocking lockstep poll (TryAdvanceTick + lanpoll chaos gate) | Q002 remainder, first half | neutral (net layer) | DONE |
| C7b | The LAN battle scene: SkirmishLive integration, LocalPlayerId, Host/Join, Hello setup exchange | ADR-022 reserved (wire change); ticket filed | neutral but WIDE | pending |
| C8 | Multi-resource fields | P4-PORT-04, new ADR | regeneration | pending |
| C9 | Faction recipe deepening | P4-PORT-06 | depends | pending |

Phase B is complete: with B5 landed (2026-07-20), every B row (B1 through B6) is
DONE. The sim is now the authority on the runtime /data source, rally and spawn,
power, the barracks and tech tree, the skirmish opening hand, and ferrite
regrowth. The C series has opened: C1a (unit command stances, 2026-07-21) is DONE
under ADR-015, so the sim now owns hold-fire, guard and patrol as hashed per-unit
state, resolving Q003 and P4-PORT-01. C1b (formations, P4-PORT-05, 2026-07-24) is
DONE as a client-side slot layer under ADR-018: the design pass found that a
resolved slot is not per-tick behaviour and the sim has no selection, so
formations live client-side (sorted-id box lattice resolved to per-unit
destinations over the existing move commands) with NEUTRAL hash impact, no
regeneration. Cohesive formation movement, the part that would need hashed sim
state, is deferred to a future ADR on evidence of need. C2 (repair vehicle,
2026-07-24) is DONE under ADR-019, also NEUTRAL: the design pass found the Service
Depot already heals friendly units, so the repair vehicle is unit type 13 running
that same heal loop as a mobile aura (not power gated, excludes itself, mobile
units only), which no golden scenario spawns, so no regeneration. Bespoke model
and icon owed to art-pipeline (interim: the MCV model). C3 (four-queue sidebar,
2026-07-24) is DONE for its client-only half under ADR-020, also NEUTRAL: the
design pass found the client never issued CancelProduce at all, so it wired
right-click cancel/refund on every sidebar item over the existing sim command, and
confirmed infantry/vehicles are already two parallel queues. The literal GDD-45
remainder, two parallel structure/defence queues on one yard (a second build head
and ready slot = hashed state), is a golden move split out to C3b (pending). C4
(neutral outposts, 2026-07-24) is DONE under ADR-021, also NEUTRAL: the Outpost is
struct type 13 / EntityKind 17, map-placed neutral, engineer-captured through the
untouched CaptureSystem, paying 15/s while owned; excluded from victory hope; an
OutpostGate proves capture, the exact income beat, neutral inertness and the
elimination rule, with all 24 goldens byte-identical. No shipped map places one
yet: that is a tools + balance map-design pass. C7a (2026-07-24) is DONE:
LockstepClient.TryAdvanceTick is the non-blocking poll Q002's remainder asked
for, and the lanpoll gate proves two clients complete 300 ticks hash-identical
clean AND under 60ms+stall chaos with the poll missing tens of thousands of
times and no call ever blocking - the frame-loop property the battle scene
needs. C7b (the SkirmishLive integration, LocalPlayerId plumbing, Host/Join and
the ADR-022 Hello setup exchange) is filed pending with the full design in its
ticket; it is the widest client change since Phase A and carries the real
two-machine human verification.

Excluded from the directive, needing separate sign-off: naval combat and FMV
briefings (GDD amendments); crates and a map editor (GDD-silent, Producer
rule). The VO clip set is placeholder TTS pending the legal-review check
recorded in doc 24.
