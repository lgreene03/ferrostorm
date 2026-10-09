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
- ADR-025: RATIFIED 2026-07-25 - ADR-025-destroyable-bridges.md (destroyable
  bridges, doc 24's ratification-gated sketch formalised; P6 Wave C6a). Number
  claimed here per the numbering law. Ratified under the 2026-07-17 directive by
  the ADR-012 precedent (a doc 24 sketch formalised into an ADR, then built).
  Hash impact NEUTRAL via a NEW grid character: the existing 'B' cells stay
  inert client dressing, so skirmish-01 (which has twelve bridge rows and IS the
  skirmish golden's map) and skirmish-03 and every mission map are untouched,
  and only skirmish-04 gains destroyable crossings. Corrects doc 24's claim that
  bridges need incremental flow repair: they do not, a bridge dies once.
  GATES ARE NOT IN THIS ADR and are NOT authorised: ADR-005 clause 6 deferred
  them with an explicit revisit precondition (per-player flow fields existing
  for another reason) which is still unmet. See TICKET-P6-C6b.
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
- ADR-022: RATIFIED 2026-07-25 - ADR-022-lan-setup-exchange.md (the host's
  match setup rides in the lockstep Hello so a joiner builds the identical
  world; P6 Wave C7b, first slice). Ratified under the standing directive. The
  blob is OPAQUE to the net layer, which keeps Ferrostorm.Net free of any
  knowledge of maps, factions or seeds; hash impact NEUTRAL (net layer, outside
  the state hash). Originally reserved 2026-07-24 as: a
  host-supplied match-setup blob appended to the lockstep Hello frame so a
  joiner builds the identical world. A WIRE-FORMAT change, so it takes this
  ADR before code; the non-blocking poll half of Q002's remainder shipped
  without it as Wave C7a (additive TryAdvanceTick + the lanpoll chaos gate,
  no wire change).
- ADR-021: RATIFIED 2026-07-24 - ADR-021-neutral-outpost.md (the neutral
  capturable Outpost, GDD line 41 / doc 22 P5-ECON-14; P6 Wave C4). Number
  claimed here per the numbering law. Design ratified; BUILT AND SHIPPED 2026-07-25 (C4, C4b, C4c);
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
- ADR-071: RATIFIED 2026-10-02 - ADR-071-a-refinery-docks-from-every-face.md
  (P8-15, decision D4, finding ML-01). Number claimed here per the numbering
  law before drafting. Harvester docking and every walk onto a building route
  to a multi-source flow field seeded at every open cell round the footprint;
  a harvester with no route to its refinery falls back to the nearest one it
  can reach; the commander keeps a one-cell apron round its refineries.
  Goldens MOVE, measured and attributed one by one in the ADR. Ratified by the
  orchestrator under the owner's standing authority of 2026-10-02, citing D4.
- ADR-072: RATIFIED 2026-10-02 - ADR-072-the-commander-answers-air.md
  (P8-17, decision D10, findings AI-01 and PV-06). Number claimed here per the
  numbering law before drafting. The commander counts the enemy aircraft it
  can see, keeps one Flak Track more than the most it has seen at once (capped
  by anti_air_cap in data/ai), holds two at home and sends the rest to escort
  its harvesters, and pulls the radar forward. aiairgate and cheesegate bind.
  Goldens NEUTRAL, measured; catalogue checksum MOVES
  (0xB4E6F043C4A872CC to 0xF384205E0D0BF2D2). Ratified under the owner's
  standing authority of 2026-10-02, citing D10.
- ADR-073: RATIFIED 2026-10-02 - ADR-073-the-superweapon-is-a-climax.md
  (P8-18, decisions D1, D2, D15 and D33, findings ML-05, OJ-02, BAL-07 part and
  OJ-06). Number claimed here per the numbering law before drafting. The
  support powers are decoupled to absolutes (500, 166, 300); the superweapon's
  charge, the strike damage and the scan's reveal move into /data as structure
  columns; the charge goes to 5400, the commander buys the weapon from two
  refineries and a wave of army, the cannon goes to 2500 and the scan to 150.
  Supersedes ADR-044's refusal section and clause 1's 900. Goldens MOVE
  (skirmish, superweapon, aisuper), each attributed by step in the ADR;
  catalogue checksum MOVES (0xF384205E0D0BF2D2 to 0x1255012DCF3D3A68).
  Ratified under the owner's standing authority of 2026-10-02, citing D1, D2,
  D15 and D33. D1 as written left F8's median first launch at 8701 against
  10800; D33 (the commander does not buy the weapon before one full charge
  has elapsed), taken on the ADR's measured recommendation, brings it to
  11536, and pillargate binds both F8 halves.
- ADR-074: PROPOSED 2026-10-04 - ADR-074-a-boarding-walk-closes-to-reach.md
  (P8-56, found by P8-7). Number claimed here per the numbering law, in the
  same commit as the draft; ADR-073 is held by P8-18 (the superweapon), so
  074 is the next free number. A walk to board a Carrier is marked in
  ExplicitTarget's negative range (no new hashed or saved state), closes to
  the two-cell reach and boards on arrival through one Board() that ends
  every order the unit held, and gives up by ADR-014's no-progress deadline
  when the squad cannot gain on its Carrier. Goldens NEUTRAL, measured (24 of
  24 byte-identical); catalogue checksum unchanged; no save or wire change.
  NOT RATIFIED: it changes what a recorded LoadTransport does, so a replay
  from a build carrying ADR-073 but not this ADR ends DIVERGED and such a
  mixed-build LAN game desyncs within 30 ticks of the command (neither is
  refused up front, as the ADR's compatibility section records, and Q024 asks
  whether to; builds from before ADR-073 are refused by its catalogue move).
  Routed by the orchestrator for Architect sign-off, and signed off with
  conditions by the Architect on 2026-10-04; its before-merge conditions C1
  to C5 are met in the pull request that lands it.
- ADR-075: RATIFIED 2026-10-05 - ADR-075-placement-is-oriented-to-the-map-centre.md
  (P8-21, decision D5, findings AI-04, BAL-01 map half, OJ-01 (3)). Number
  claimed here per the numbering law, in the same commit as the draft; 074 is
  held by P8-56, so 075 is the next free number. The commander's placement
  scan is walked in one canonical frame, the side facing the map centre
  first, reflected per anchor along each axis on which the anchor stands
  short of the centre, so the bases on a rotation pair are rotations of each
  other; mirrorprobe bisects what remains. F5 is NOT met (income within 15
  per cent in 18 of 32, start split 21/3), so seatfairgate stays
  non-binding, and the residue is traced to the opening hand, the free
  harvester's fixed offset, Fix64's flooring multiplication (P8-53), the
  production exit order and the flow field's tie-break, none of them this
  row's to fix. Goldens MOVE (skirmish, expansion, aisuper, mission, and
  airanswer, appended on main meanwhile), one cause, attributed in the ADR;
  catalogue checksum unchanged; no save or wire change. It changes which
  commands a commander issues, so a mixed-build LAN game with a commander
  seat desyncs within 30 ticks of the first reflected placement rather than
  being refused (Q024); old replays, which record the commander's commands,
  play back unchanged. Signed off with conditions by the Architect on
  2026-10-05; its before-merge conditions C1 to C11 are met in the pull
  request that lands it, and it is ratified under D5 and D36 (D36: it lands
  before F5 is met). Its frame clause was amended on 2026-10-08 under its C7
  and decision D38: once P8-62, P8-63, P8-53 and P8-64 had landed, the
  sheltered frame scored 28 against 26 of 32, so the canonical frame is now
  the sheltered one, judged jointly with ADR-077's multiplication. The
  amendment is covered by the Architect's sign-off with conditions of
  2026-10-08 on D38's pull request (branch claude/p8-53-trunc-shelter-on;
  ADR-077's section "Architect sign-off", where the pull request's number is
  recorded when it opens), and the comparison was read again at that pull
  request's final head (condition C3): still 28 against 26 of 32 for the
  sheltered frame.
- ADR-076: RATIFIED 2026-10-06 - ADR-076-the-start-is-a-true-half-turn.md
  (rows P8-62 and P8-63, ADR-075's causes 1 and 2; row P8-64, cause 4, held
  back under D37). Number claimed here per the numbering law, in the same
  commit as the draft; 075 is held by P8-21, so 076 is the next free number.
  Each rule is laid out in the structure's own frame, an axis short of the
  map centre keeping the authored layout and an axis past it reflected: the
  opening hand (the yard covers its start cell and grows towards the centre,
  the force mirrored on both axes) and the free harvester (the cell beside
  the refinery's centre-facing corner). Lands as two staged rule changes, one
  cause each, with every golden move attributed per stage in the ADR (skirmish
  in stage 1; skirmish, expansion, aisuper, mission and airanswer in stage 2);
  catalogue checksum unchanged; no save or wire change. These are sim rules a
  human plays under, so old replays diverge and a mixed-build LAN game
  desyncs rather than being refused (Q024). The production exit (clause 3)
  was built and measured as a third stage and is HELD: with ADR-075's shipped
  frame it takes F8's first-launch half under its bar (32 of 72 matches
  launching against 36), and the frames are level on ADR-075's C7 measure at
  that stage, so it waits for the frame judgement P8-53's landing triggers.
  Signed off with conditions by the Architect on 2026-10-06 (the ADR's
  "Architect sign-off" section); C1 to C7 are met by route A in pull request 166
  (branch claude/p8-62-63-start, rebased from claude/p8-62-63-half-turn),
  and it is ratified under D5, D36 and D37. Clause 3 re-lands on 2026-10-08
  as stage 3 of D38's pull request (branch claude/p8-53-trunc-shelter-on),
  with the Architect's C6 (spawngate's single-axis pair) and C7 (pillarprobe
  prints its reversal measurement) met there; three goldens move in that
  stage. Its own reversal read met on that head's sample (one skirmish-08
  game, traced to SpawnOffsets' blind spots about an even footprint; the
  ADR's section "Clause 3 re-landed (D38)"). The Architect's sign-off with
  conditions of 2026-10-08 on that pull request (ADR-077's section
  "Architect sign-off", where the pull request's number is recorded when it
  opens) took the course that keeps the clause, as its condition C1: clause 3
  is amended in the same pull request as a fourth staged commit, so that after
  its eleven probes the exit search tries the cells they miss and the set it
  searches is closed under reflection through the footprint's centre (no
  golden moves; pillarprobe's held producer ticks fall from 6144 to 0), and
  its reversal is restated under C2 (the ADR's section "Clause 3 amended
  under D38"), reading 0 against 0 at the final head.
- ADR-077: RATIFIED 2026-10-09 - ADR-077-multiplication-truncates-toward-zero.md
  (row P8-53, ADR-075's cause 3, decision D38). Number claimed here per the
  numbering law, in the same commit as the draft; 076 is held by P8-62 and
  P8-63, so 077 is the next free number. `Fix64` multiplication truncates
  toward zero (`p >= 0 ? p >> 32 : -((-p) >> 32)` on the Int128 product,
  integer only), so `(-x)*y == -(x*y)` exactly, as division already rounds.
  Proposed 2026-10-08. Lands as stage 2 of D38's one pull request (branch
  claude/p8-53-trunc-shelter-on), after ADR-075's frame flip and before
  ADR-076 clause 3, judged jointly with them on P8-53's measured matrix:
  13 goldens move in this stage, one cause, attributed in the ADR; catalogue
  checksum unchanged; no save or wire change; `lanaiseatsgate`'s control pin
  re-pinned with a note. A sim rule, so old replays diverge and a mixed-build
  LAN game desyncs rather than being refused (Q024). It records that F11
  gets worse (Directorate 6/6 against Sodality 0/6), that the green
  `mission` golden depends on the sheltered frame avoiding an exact movement
  tie (row P8-66 filed), and that the F5 gain is one deterministic sample
  while `mirrorprobe`'s loss of the multiplication split is the robust
  result. Signed off with conditions by the Architect on 2026-10-08 (the
  ADR's section "Architect sign-off", where the pull request's number is
  recorded when it opens); conditions C1 to C5 are met in that pull request,
  which carries four staged commits (the fourth amends ADR-076 clause 3 and
  moves no golden) and a goldens-neutral commit, and D38, read again at its
  final head under condition C3, stands. Ratified under D38; C6 is that pull
  request's CI and merge hygiene.
