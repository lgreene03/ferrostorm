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
| C3b | Parallel build lanes (GDD line 45 remainder) | ADR-023 (ratified): overflow rule, pruned side lane | DONE (NEUTRAL, save v8) |
| C4 | Neutral outposts | ADR-021 (ratified): capturable income Outpost, struct type 13 | DONE (NEUTRAL, no regen) |
| C4b | Outposts placed on skirmish-02/-04 + the mapgate harness | under ADR-021; skirmish-01 (golden) and -03 (look-dev) untouched | DONE (NEUTRAL) |
| C4c | The AI captures neutral outposts | under ADR-021; inert without an outpost, so no golden moves | DONE (NEUTRAL) |
| C5 | Air layer: airfield-slot model, strike aircraft, transport heli | new ADR required (P4-PORT-02; ADR-009 exclusion) | regeneration | pending |
| C6a | Destroyable bridges | ADR-025 (ratified): new grid char 'b', skirmish-04's central ford | NEUTRAL | DONE |
| C6b | Wall gates | ADR-005 clause 6 DEFERRED them; its revisit precondition (per-player flow fields) is still unmet | measured, not neutral by construction | DEFERRED: needs Luke to override clause 6 |
| C7a | Non-blocking lockstep poll (TryAdvanceTick + lanpoll chaos gate) | Q002 remainder, first half | neutral (net layer) | DONE |
| C7b-i | LAN setup exchange (the Hello carries the host's match setup) | ADR-022 (ratified) | NEUTRAL (net layer) | DONE |
| C7b-ii | LocalPlayerId through the battle scene (the joiner's seat) | C7b slice 2; CI seat-check guards it | NEUTRAL (client only) | DONE |
| VERIFY | Headless client harness (tools/verify-client.sh) | closes the repo's oldest verification gap | NEUTRAL (client only) | DONE - 21 checks; found the AI-shares-the-joiner's-seat bug |
| C7b-iii | LAN battle scene: lockstep-driven frame loop | verified by two real scenes playing each other in-process | NEUTRAL (client only) | DONE |
| C7b-iv | Host/Join menu flow (the last mile) | the scene is ready; only the lobby UI remains | NEUTRAL (client + net) | DONE - LAN is reachable; only the human two-machine session remains |
| FOG | Two hardcoded seats that SURVIVED the C7b-ii sweep (joiner fog) | found by audit, not by a check; ticket P6-joiner-fog-survivors | NEUTRAL (client only) | DONE - 45 harness checks; CI seat guard widened |
| DUP | Duplicated-rule audit: 3 more seat bugs fixed, rest filed | ticket P6-duplicated-rule-audit (full findings + severities) | NEUTRAL (client; one pure sim method exported) | DONE - 55 harness checks |
| DUP2 | The audit's last HIGH + the 4 most player-visible MEDIUMs | same ticket; NO HIGH FINDINGS REMAIN | NEUTRAL (client only) | DONE - 64 harness checks |
| DUP3 | The audit's ENTIRE remainder: every MEDIUM and actionable LOW | same ticket, now CLOSED | NEUTRAL (client + 4 pure sim methods exported) | DONE - 67 harness checks |
| CI-HARNESS | verify-client.sh runs in CI on every push | closes "it protects the repo as often as someone remembers to run it" | NEUTRAL (CI + wrapper only) | DONE - 67 checks on every push; found the fresh-checkout import gap |
| C7c | Dropped-peer handling: the survivor is TOLD | the last unexplained state in LAN; match RESULT filed as Q015, not guessed | NEUTRAL (net + client) | DONE - 72 harness checks |
| VERDICT | The end-of-match banner told the winning JOINER they had lost (10th seat bug) + captured structures never changed colour | found by a verified survey of docs 18/22, not by the docs' own claims | NEUTRAL (presentation only) | DONE - 78 harness checks |
| ECON-08 | Harvester load readout + ferrite field inspect | P5-ECON-08, verified outstanding in the PR#30 survey | NEUTRAL (presentation only) | DONE - 87 harness checks |
| M02-DEFEAT | Mission 02 could not be lost, and ran forever if the engineer died | forced by the mission's own objective logic; mission 03's half filed as Q016 | NEUTRAL (data + gate; goldens byte-identical) | DONE |
| BD-05 | Selling a producer burned everything already PAID into it | wider than filed: 4 slots on a yard since ADR-023, not 1 | NEUTRAL (proven by a golden run, not assumed) | DONE |
| CANCEL+STOP | Lane-2 cancel destroyed a DIFFERENT finished building; Stop left a patrol armed | found by a verified survey; the survey REFUTED my assumption that the queue was empty | NEUTRAL (client only) | DONE - and the cancel guard is now CHECKED too (95 checks, see CANCEL-CHECK) |
| CANCEL-CHECK | Closed the stated gap: the lane-2 cancel guard has a check | SpawnPowerPlant opens the tech tree, which is what made the two-lane state buildable | NEUTRAL (client only) | DONE - 95 harness checks |
| REVIEW | Design review as designer + reviewer: gaps and features register | docs/design/27 (evidence: balance tool, full inventory, cited genre canon); Q017 filed as its principal recommendation | NEUTRAL (docs only) | DONE |
| C8 | Multi-resource fields | ADR-024 PROPOSED; blocked on Q014 (the GDD names one resource) | NEUTRAL as yield, regeneration as pools | BLOCKED: needs Luke's decision |
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
elimination rule, with all 24 goldens byte-identical. C4b (2026-07-24) then made
it REACHABLE: outposts are placed through the committed generators on skirmish-02
(one pair) and skirmish-04 (two pairs), leaving skirmish-01 (the golden's map) and
skirmish-03 (the frozen look-dev reference) untouched so no hash moves, and a new
mapgate harness plays AI-vs-AI on every committed map. C4c then taught the AI to
take the free income: it notices the nearest neutral outpost, buys an engineer at
the barracks and walks it in, all of it inert on a map with no outposts (every
golden scenario), so the 24 goldens stayed byte-identical through an AI change,
proven additionally by the five-seed determinism suite and by the skirmish
scenario reporting an identical match summary. mapgate's assertion flipped from
"outposts stay neutral" to "the AI captures at least one", which it does 2 of 2
on skirmish-02 and 3 of 4 on skirmish-04. C7a (2026-07-24) is DONE:
LockstepClient.TryAdvanceTick is the non-blocking poll Q002's remainder asked
for, and the lanpoll gate proves two clients complete 300 ticks hash-identical
clean AND under 60ms+stall chaos with the poll missing tens of thousands of
times and no call ever blocking - the frame-loop property the battle scene
needs. C7b was, AT THE TIME THIS PARAGRAPH WAS WRITTEN (the completion note below
supersedes it): the SkirmishLive integration, LocalPlayerId plumbing, Host/Join and
the ADR-022 Hello setup exchange) is filed pending with the full design in its
ticket; it is the widest client change since Phase A and carries the real
two-machine human verification.

C7b IS COMPLETE (2026-07-25), across four slices, and with it every line of code
between a player and a two-machine match. C7b-i put the host's match setup in the
Hello as an opaque blob (ADR-022); C7b-ii plumbed LocalPlayerId through the
ninety-three sites that hardcoded player 0, which is what lets a joiner select,
see fog and read power at all; C7b-iii made the battle scene's frame loop
lockstep-driven, gating the accumulator drain on the non-blocking poll with a
once-per-tick submit guard, and proved it by playing TWO REAL SCENES against each
other in one process to identical state hashes; C7b-iv built the lobby, which is
the host's relay on a fixed port, a joiner that dials an address and builds its
world from the host's blob rather than its own menu, and the connect-off-the-main-
thread discipline the harness discovered was mandatory (a host cannot construct
its own client inline and then wait for a joiner on the same thread). C7b-iv also
closed three defects that only became reachable once LAN was: pausing stalled the
PEER, because the accumulator drain is the only thing that submits a batch; the
pause menu told a LAN player their live match was a replay; and ModeLine did not
say the battle was still running. All of it NEUTRAL, sim and data untouched, 24
goldens byte-identical, with the client harness at 40 checks AT THAT POINT (78 now; the status table
above is authoritative). What is left of
Q002 is Luke, two machines and a network: no in-process test can provide it, and
the question has said so since it was filed. Dropped-peer handling was filed here as unbuilt and
HAS SINCE SHIPPED as C7c: the relay announces a departure and the survivor is
told, with the match RESULT question filed as Q015 rather than guessed.

THE JOINER FOG FIX (2026-07-25) is filed separately as
docs/tickets/P6-joiner-fog-survivors.md and is worth reading, because it was
found by AUDIT rather than by a failing check. Two lines still read a literal
seat after C7b-ii swept ninety-three sites and CI grew a guard: the actor loop
said "PlayerId != 1 or IsVisible(0, ...)" and the minimap feed asked player 0's
eyes. Both are correct at seat 0 BY LUCK. At seat 1 the first clause inverts, so
a joiner's own army was drawn only where the HOST had vision while the host's
whole army was drawn through the shroud, and the minimap became a maphack. The
existing fog check could not see any of it and was not lying: the shroud TEXTURE
has always used LocalPlayerId, and what was wrong was the filtering of the actors
drawn underneath it. Three separate reads of "who can see this" that nothing
forced to agree are now ONE predicate, DrawnForLocalSeat, and the CI guard bans
the literal seat in both directions and inside IsVisible/IsExplored. Two new
harness checks fail in OPPOSITE directions against the old expression, which a
single check would have missed. Also landed: the ferrite drain (P5-ECON-01, fixed
in 1aa5e5b) finally has checks, including the factored FieldFullness that guards
against the constant expression which shipped dead. This is the SIXTH defect of
the shape "client-side, invisible from the seat the developer sat in".

Excluded from the directive, needing separate sign-off: naval combat and FMV
briefings (GDD amendments); crates and a map editor (GDD-silent, Producer
rule). The VO clip set is placeholder TTS pending the legal-review check
recorded in doc 24.
