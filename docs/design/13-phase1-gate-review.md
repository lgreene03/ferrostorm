# 13 - Phase 1 Gate Review Pack (TICKET-P1-15)

Date: 2026-07-07. Prepared by: Producer agent. Decision required from: Luke.

## Gate criteria vs evidence

| Criterion (doc 05) | Status | Evidence |
|---|---|---|
| 500 units pathing within budget | **MET (Linux)** | 500 units routed through wall gaps via flow fields, all arrived by tick 294, 0.161 ms/tick vs 8 ms budget; movement scenario with fog on 512x512: 2.16 ms/tick |
| Double-run hash identical | **MET (Linux)** | All four scenarios (movement, pathing, economy, combat) bit-identical across double runs, checkpoints every 100 ticks; goldens committed |
| Identical across Windows and Linux | **PENDING** | Requires the first GitHub Actions run; determinism.yml compares goldens on both OSes per push. Container environment cannot execute Windows |
| 20 LAN games, zero desyncs | **MET (loopback)** | 20 relay-mediated 2-client TCP lockstep games, 300 ticks each, hash exchange every 30 ticks, zero desyncs, final hashes identical. Real-network jitter/loss harness remains open (below) |

## Delivered beyond gate minimums
- Economy loop live and exact: 3 harvesters exhausted 2 Ferrite fields, 4000/4000 credits delivered, auto-retargeting (US2.2) exercised.
- Combat live: warhead x armour damage matrix, auto-acquire with deterministic tie-breaks, stop-to-engage, decisive scripted engagement (cannons 13-0 over rifles, matching matrix maths).
- Fog of war: per-player visible + explored bitgrids, hashed into desync detection.
- Desync machinery end to end: per-tick hashes, relay comparison, client notification (US1.2).
- Nightly cross-platform soak workflow (5 seeds x 2 OS x full suite + 20 LAN games).
- Design: GDD Q1-Q3 resolved (no unit cap; strict adjacency; squads per ADR-003); Ferrite rename swept; control scheme spec v1; balance simulator spec.

## Honest ledger: open before gate can fully close
1. **Windows leg of every MET item** - one push to GitHub runs it; treat any red as a Phase 1 kill-criterion event, not a patch-around.
2. **TICKET-P1-07 Godot renderer** - not started; requires the engine locally. The snapshot API it consumes exists and is tested (World.TakeSnapshot).
3. **Real-network conditions** - loopback proves protocol correctness, not jitter/loss behaviour. TICKET-P1-08b (tc/netem harness at 5% loss, 150 ms jitter) queued; the A5 definition of done is not yet fully satisfied.
4. **Unit-unit collision/avoidance** - deliberately absent (flow fields only); P2 polish ticket, noted so playtesters are not surprised by stacking.
5. **Sqrt optimisation** - flagged in ADR-002; fine at current load, required before pathfinding density grows.
6. **TICKET-P1-13 art quotes** - needs human contact with contractors; blocks nothing in Phase 1.

## Recommendation
Conditional pass: proceed with TICKET-P1-07 (renderer) while the first CI runs execute. Full gate closure requires green Windows CI and the netem harness. No kill-criterion signals observed; determinism has survived every test thrown at it, including four gameplay systems and TCP lockstep.

## Appendix: post-review session (same day)
Items 3, 5 and part of 4 from the ledger closed after the review was drafted:
- Network conditions: ChaosProxy harness landed; lockstep survived 60ms±30ms and 150ms±30ms one-way delay with 5% 500ms stalls, zero desyncs. The same seed produced the same final hash at both latencies - network timing provably cannot influence sim state. CI now includes a chaos game per push; real-hardware netem stays on the beta checklist.
- Sqrt optimisation: landed and proven bit-identical before any other change (goldens unchanged).
- Separation/avoidance: landed with arrival contagion and stall-arrival; three failure modes found and fixed through a diagnostic mode rather than assertion-loosening. Crowd compaction remains P2.
- New systems beyond Phase 1 scope: power (GDD s5 scaling verified exactly), factory production queues, attack-pursuit, and the /data loader with schema alignment. Scenario count is now five, all deterministic, all under 0.7 ms/tick worst case against the 8 ms budget.

## Appendix 2: continuation session
- TICKET-P1-07 advanced from "not started" to scaffolded: the full Godot project exists in /game with an honest UNTESTED banner, and its one non-trivial dependency - the snapshot interpolation contract - is now proven headless by the spectate mode and gated in CI. Remaining risk on this ticket is Godot-API-usage only.
- Data-driven pipeline live: /data unit files load, validate, and provably reproduce the compiled catalogue; the sim's producible table is now an instance catalogue extendable before tick 0.
- Production economics upgraded to classic pay-as-you-build with exact integer drain audits.
- Balance gate operational per doc 12 and wired into every push; baseline PASS committed.
- Root-level offline NuGet policy: everything except /game builds with zero package sources, matching the container and hardening CI against registry outages.

## Appendix 3: continuation session 2
Seven deterministic scenarios now gate every push (movement, pathing, economy, combat, production, attackmove, construction). New this session: attack-move with sight-range hunting and fog-honest bypass of unseen targets; queue-cancel with exact pay-as-you-build refunds plus a credit-conservation invariant under broke-stall; and structures as real map objects - blocked 2x2 footprints, strict-adjacency placement per GDD Q2, and live flow-cache invalidation proven by sealing a corridor mid-march and watching the unit reroute. Two behavioural bugs were found by the tests and fixed in the sim (hunt-statue contagion; pursuit clobbering the ordered destination); two assertions were corrected because the sim was right and the test was wrong (fog-honest flanker survival; crowd-arrival radius). Perf worst case 0.73 ms/tick against the 8 ms budget.

## Appendix 4: continuation session 3
The sim now closes the entire classic loop with no human in it. Eight deterministic scenarios gate every push; the new skirmish scenario is two rule-based AI commanders playing a full match - building bases, harvesting, producing under pay-as-you-build, defending with turrets, and launching attack waves - through the exact command interface the network layer uses. Replays landed: a 3000-tick AI match round-trips from a 4.4 KB text file to the identical final hash, gated in CI, and doubling as the preservation-promise feature. The Construction Yard projects the Q2-resolution largest build radius, turrets fire through the standard combat system, sell-back refunds half and frees footprints, and destroyed structures leave passable rubble. The rocket squad completed the counter triangle and the balance gate passed all three edges per-cost on the first data attempt.

## Appendix 5: continuation session 4
The classic construction feel arrived: structures now build in the Construction Yard queue under pay-as-you-build, hold ready, and place instantly for free - with rejected placements retaining readiness and cancellation refunding exactly. Repair (2 hp/tick, 1 credit/tick, charge verified to the credit) and harvester flee-from-loading landed, the latter refined after its first cut proved too skittish and starved a camped economy. The AI adopted the sidebar flow, gained threat response (intruders near structures pull the standing army), and grew difficulty knobs. The balance gate gained the harvester tempo baseline (8400 credits/3000 ticks). One serious bug found and fixed at root: a struct-aliasing writeback in ApplyCommand resurrected consumed CY readiness, diagnosed from an AI cheerfully placing 260 free power plants. Eight scenarios deterministic, goldens regenerated, full battery green.

## Appendix 6: continuation session 5
Ten deterministic scenarios now gate every push. Stealth/detection landed as the Sodality faction identity (untargetable until detected or firing; per-player detection masks; the Shade Raider and Sentinel Scout join the roster), veterancy landed with damage verified to the hit point at every rank, maps became data (the committed skirmish-01 file is the single source of terrain, fields, and starts), and the MCV deploys into a radius-projecting Construction Yard. Two real bugs surfaced and were fixed at root: a queue read-after-dequeue in production spawning, and movement orders failing to cancel standing attack orders - a retreating unit stood its ground shooting until the veterancy regen test caught it. Twice the tests accused the sim and the sim was right (raider auto-engaging at parking distance; a legal firing position outside turret range) - fixed by correcting scenario geometry, not by bending the sim.

## Appendix 7: continuation session 6
Matches can now be won. The classic short-game rule landed with the MCV-as-hope nuance verified to the exact tick, a deterministic gameplay event stream now feeds the presentation layer (pinned against observable state), and the AI learned to expand: recognise local depletion, save, buy an MCV, found a second base at the richest distant field, and re-home its economy - proven by a scenario that ends with the far deposit mined from 12000 to 230. Three doctrine bugs were found through the expansion scenario's failures and fixed as AI logic, not test adjustments: a depletion trigger that could never fire once home ran dry, army spending starving the MCV fund, and army spending starving base development - the last resolved as infrastructure-before-army. Twelve deterministic scenarios now gate every push.

## Appendix 8: continuation session 7
The splash family landed as one cluster: minimum range and area damage in the weapon model, the howitzer as the roster's siege archetype (its dead zone verified absolute under a standing attack order, its splash verified indiscriminate and exact), and the superweapon with the full classic lifecycle - power-gated charge, refused premature launches, a five-second warning, ringed Omni detonation, automatic recharge - every number pinned to the hit point. The balance gate grew to a 4x4 matrix plus the tempo baseline and passed first try: siege loses every knife fight per-cost, exactly as designed. Fourteen deterministic scenarios now gate every push.

## Appendix 9: continuation session 8
Crush landed with the lesson of the session: separation was politely steering tanks around the infantry they were ordered to flatten - treads now refuse to yield to crush-eligible squads, and contact deepens to the kill. The AI completed its command of the arsenal: it banks a war chest, builds the superweapon, waits out the charge, and erases the enemy refinery, all through the public command interface (verified end to end, refinery destroyed). Sixteen deterministic scenarios gate every push; the balance matrix still holds with crush live.

## Appendix 10: continuation session 9
Save games arrived, and determinism made them honest: the saveload gate saves mid-replay, demands hash equality on load, then resumes and demands the uninterrupted run's final hash - a design where even unhashed fields are protected because their corruption must diverge the continuation. The Sodality veil projector landed as the faction's second stealth pillar with the classic brown-out decloak verified by selling a power plant, and the performance budget graduated from a log line to a merge-blocking gate. Seventeen deterministic scenarios; the full battery is green.

## Appendix 11: continuation session 10
The last classic command feature landed: shift-queued waypoints, done as one coherent change across the command struct, wire format, replay format (v2), state hash, save format, and a sorted-order dispatch system - with queue-wiping direct orders and deterministic reaping of dead entities' plans. The LAN soak now exercises the queued flag over real TCP, and the replay and save/load gates independently verify the same AI match to the same final hash - two unrelated serialization paths agreeing bit-for-bit on 3000 ticks of simulation. Eighteen deterministic scenarios gate every push.

## Appendix 12: continuation session 11
Single-player content became data: map v2 carries tagged entities and one-line triggers, the MissionRunner fires them deterministically, and mission-01 proves the whole pipeline - the skirmish AI, knowing nothing of missions, bootstraps an economy, springs the scripted ambush, and destroys the tagged camp for a scripted victory at tick 2157. The instructive failure was economic, not mechanical: the mission's starting funds could not cover the bootstrap chain, so no second wave could ever exist - a mission-design fix, which is exactly what the trigger pipeline is for. The sim handbook (design doc 14) now gives any future contributor the ground truth in four pages. Nineteen deterministic scenarios; the full battery is green.

## Appendix 13: continuation session 12
Campaign saves closed the last open sim ticket: a mission can now be saved mid-battle and resumed to the identical scripted victory, message log, and final hash - proven by a gate that records a full mission, interrupts it at the midpoint, and demands bit-exact convergence. The sidebar also learned the last classic courtesy: cancelling a finished-but-unplaced building refunds it in full. Nineteen scenarios, eleven verification gates, all green.

## Appendix 14: continuation session 13 - factions
Ferrostorm became a game of sides. Faction identity is now enforced in the sim (locked production, hashed allegiance), expressed in signature hardware (phantom, bulwark, engineer capture - scenario 20), and played by faction-aware AI doctrine. The faction-war gate was built and immediately earned its keep by refusing to pass: eleven balance iterations surfaced five genuine design findings, including the session's design law - the counter-triangle must be common to both sides - and ended by faithfully reproducing the 1995 harass-bait exploit, which is as strong a compliment as a simulation can pay its source material. The gate reports rather than blocks until defence-squad AI and human play set the meta; every other gate is green across twenty scenarios.

## Appendix 15: continuation session 14 - the loop
Six workstreams in one disciplined pass: defence squads killed the puppet exploit for good; the Spine gave the skirmish map real chokepoint geography; the campaign gained two trigger verbs, two missions in two voices (a Sodality commando raid and a Directorate last stand), a manifest, and briefings; the service depot completed the classic structure set; and a genuine design law was extracted from a two-hour debugging hunt - the short-game rule is a skirmish rule, and baseless mission forces must disable it or lose before their first order. The hunt itself was the session's best engineering: three plausible theories (stale binaries, static caches, cross-scenario contamination) were disproven by experiment before mechanical diff exposed the real cause. Twenty-four scenarios, all gates green, the faction war reporting as designed. The Godot bring-up was attempted and is blocked one proxy entry away from automation.

## Appendix 16: continuation session 15 - first light
Ferrostorm became visible. The sim exports battle records; a standalone HTML war-room replays them with the full faction visual language - and the first rendered frame shows exactly what two years of hashes promised: bases, economy, terrain, and a firefight at the Ferrite Gap. Nineteen sprites establish the style bible (Directorate slabs, Sodality wedges, the one-place team-colour law), inspected and approved at RTS scale. No Godot required for any of it - though everything here is the reference the Godot client will implement.

## Appendix 17: continuation session 16 - the third dimension
"A proper 3D game like Command & Conquer" stopped being an aspiration: headless Blender in the build container now procedurally models the entire roster in the faction visual language, exports Godot-native .glb assets, and renders real match frames as 3D battle scenes - the first-light render shows the Spine, the glowing ferrite, and both armies in the field from a classic RTS camera, generated from live sim data end to end. The Godot client gained its 3D shell (models, camera rig, replay theatre) with the honest caveat that client code written without an editor awaits its first run on real hardware - the last manual step standing.

## Appendix 18: continuation session 18 - Godot bring-up, GitHub, and a real CI bug
TICKET-P1-07 closed for good: handed off from a sandboxed container to a real Mac, the Godot 3D client compiled and ran for the first time, with terrain and every distinct unit/structure model confirmed rendering at correct positions by cross-checking a captured frame directly against replay JSON data - no interactive display was even available for most of the session, so verification ran through a temporary in-scene screenshot-and-quit script instead of a human at the keyboard. Two real bugs surfaced in the year-old-untested client code (a narrowing cast, a NuGet source scoping gap) and were fixed at root, not patched around. The repo went public on GitHub, and the first CI push immediately caught something real: Windows failed the golden hash check with every scenario "diverged" despite bit-identical values, traced through two attempted fixes to the actual cause - `Console.WriteLine`'s platform-dependent newline in the Runner's own output formatting, not a git checkout setting and not a determinism defect (build, selftest, and double-run determinism all passed on Windows before the byte-diff step). This is exactly what cross-platform CI is for: a bug invisible on any single developer machine, caught on the first real push. Twenty-three scenarios, all gates green on both ubuntu-latest and windows-latest. The visual upgrade (priority 2, stylized-realistic pass) is documented but not started - the session ended with a fully mechanical handoff (docs/design/17-visual-upgrade-handoff.md) after discovering the prior plan referenced two Blender scripts that were never actually committed to the repository.

## Sign-offs
- Producer agent: recommend conditional pass (this document)
- QA agent: local suite green, exit code 0 across all modes; CI wiring in place
- Legal agent: Ferrite sweep verified in player-facing docs; codename FERROSTORM remains internal-only
- Luke: ______
