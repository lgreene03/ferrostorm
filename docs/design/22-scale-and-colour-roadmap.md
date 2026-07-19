# 22. Scale and Colour Roadmap: maps, material, bases, barriers, colour

Status: RATIFIED PLAN, implementation contract
Date: 2026-07-15
Owner: design-review (A16), for execution by sim-engineer + client-engineer + art-pipeline + game-designer agents
Inputs: five parallel dimension analyses (map scale and layout; resource economy and harvesting; base building depth; defensive structures and barriers; colour, palette and texture richness), judged against the shipped code at HEAD, docs/design/02-game-design-document.md, docs/design/03-technical-design-document.md, docs/design/16-visual-style.md, docs/design/18-game-review-roadmap.md, docs/design/20-visual-aaa-roadmap.md and docs/design/21-ue5-portability-audit.md.

**AMENDMENT, 2026-07-19, doc 25 LOOK-03. THE CAPTURE-VERIFICATION RULE.** A visual ticket in this document is not closeable without a before-and-after contact sheet at the three reference cameras defined in `game/scripts/LookDev.cs`, and a change that is invisible at all three is a FAILED ticket regardless of its numeric criterion. Wave C's tickets already carried capture-based acceptance criteria, which was the right instinct and was unexecutable because no capture harness existed; it exists now, as `tools/lookdev/capture.sh` and `tools/lookdev/contact.py`, on branch `ticket/p6-visual-v0-v1`.

**WAVE C PROGRESS, same date.** C-08 has SHIPPED, scheduled as doc 25's V1-02, with its ratified values used unmodified: MAP-07 has not landed, so it edits Colors rather than bytes. C-01, C-05, C-06, C-07, C-10 and C-11 are still unstarted. Two notes for whoever picks them up. C-10 raises total directional energy by fourteen per cent and names `TonemapExposure` as its single compensating knob: that value is now 2.9 rather than 1.35, having been used for exactly that purpose by doc 25's V1-07, and it is the number to bring back down if the histogram overshoots. C-08's acceptance clause about explored-region saturation should be read alongside doc 25's finding that mean HSV saturation in this frame was measuring the volumetric fog's blue cast rather than any chroma in the art.

This document answers five direct questions from the owner and then carries the complete ticket register that follows from them. It is written to be executed by a cheaper model: every ticket carries its full mechanical specification, file list, acceptance criteria and touchesSim flag exactly as produced by the dimension analyses. Nothing has been summarised away.

Three editorial notes on the verbatim rule, each a place where preserving the source text exactly would have broken one of the project's own absolute rules.

First, em dashes and en dashes in the source ticket text have been normalised to hyphens, because CLAUDE.md makes their absence an absolute rule for every document in this repository and doc 22 lives in docs/design/. One acceptance criterion (DEF-02's) greps for those characters literally; it is rendered here as an equivalent PCRE codepoint class, noted at the point of use.

Second, three passages of ticket prose named the specific 90s RTS franchise, its publisher or its resource as shorthand in their reasoning (DEF-02's context, BD-20's queue-cap justification, P5-ECON-12's heritage argument). CLAUDE.md's Legal rule forbids exactly those terms in this repository and fixes the approved formulation as "inspired by the classic RTS games of the 90s", and two tickets in this very document (DEF-02, DEF-07) carry acceptance criteria that grep for them. The reasoning is preserved in full and the trademarks are not; the substitutions are flagged in place. The terms still appear where they are the subject of a rule rather than a use, which is correct and must stay.

Third, where two analyses produced the same ticket, one is marked canonical and the other is preserved verbatim in Appendix A with a do-not-implement note, because several duplicate pairs carry conflicting values that would corrupt each other if a model implemented both. The deduplication ledger in section 3 records every such decision and its reason.

---

## 1. Straight answers

### Are the maps big enough?

No, and the reason they are not is worth stating precisely, because the brief's assumption and the popular hypothesis are both wrong. First a correction: the three skirmish maps are all 96x64, but the missions are not. mission-01 and mission-03 are 64x48 and mission-02 is 56x40, so "all maps are 96x64" is true only of skirmish. Second, and more importantly, the sim is not what holds map size down. Measured on this machine, a full Step() with 500 units is flat in map size at 0.741 ms per tick at 96x64 and 0.283 ms at 192x128, against an 8 ms budget, because every per-tick system is O(entities) rather than O(cells). ScenarioMovement has been constructing a 512x512 world since Phase 1 and passes the gate today. Map.cs carries no size assumption anywhere. The fog was the briefed suspect and it is innocent: FogOfWar.UpdateFrom really is a per-cell SetPixel loop, but it costs 0.558 ms per tick at 192x128, which is 0.8 percent of a 15 Hz tick, so rewriting it is worth doing for the cheap headroom and is not worth doing to unblock anything. The actual wall is terrain draw calls. BattlefieldView.BuildTerrain emits between one and ten individual MeshInstance3D nodes per blocked cell, which measures 1420 nodes on skirmish-03 and 6114 nodes on a 192x128 map at the same terrain density, each one its own draw call, tripled by the shadow passes, against a TDD s6 target of 600 units and 200 structures at 60 fps on a GTX 1060-class GPU. Scatter density is the second client-side problem: the rubble, tuft, rock and plate counts are hard-coded absolute numbers tuned at 96x64, so a 192x128 map renders at a quarter of the intended density and reads as bare ground. The honest envelope is therefore that 192x128 is safe on the sim side today with zero changes and four times the headroom under the gate, and becomes safe on the client side once the terrain draw calls are collapsed into MultiMeshes and the scatter is density-scaled. Both fixes are mechanical, the codebase already contains the exact MultiMesh pattern from W4-12, and neither touches the sim. The recommended targets are 160x120 and 192x128; beyond roughly 256x192 the flow-field rebuild cost starts eating the tick budget on cache-clear storms and would need real work.

### Is there material to harvest?

Yes, the harvest loop is real, complete and correct, and on the default map there is almost nothing to harvest. Those two facts are both true and the second is a bug, not a design choice. The state machine is sound, retargeting to the nearest live field works, fleeing under fire with a part-load works, refinery loss mid-haul re-targets, and credits are conserved exactly: a 12000-ferrite field yields 12000 credits banked across 18 deliveries with zero leakage, and the gated economy scenario already asserts this. The constants match GDD s4 exactly. The problem is the field count on skirmish-01, which is the default map the client boots into. It has three ferrite cells totalling 36,000 credits, against skirmish-02 and skirmish-03's twenty cells totalling 240,000. Measured consequence: skirmish-01's home economy dries out at 1.65 minutes with three harvesters, against the GDD's own stated intent to "force expansion by minute ~8", while skirmish-03's five-field home cluster hits 8.47 minutes and lands on that target. The per-field amount of 12000 is correct and should not move; the field count is the defect. skirmish-01 is also not symmetric: sixteen terrain cells and six ferrite cells fail a 180 degree rotation test that skirmish-02 and skirmish-03 both pass perfectly, and player 0's home field sits 15.2 cells from its start while player 1's sits 13.4, a permanent haul advantage of roughly 12 percent handed to the AI on the default one-versus-one map. Beyond the map, four things are wrong with the economy as shipped. The field drain visual is dead in live play, because SpawnFerriteField sets Hp to 1 and MaxHp to 1 while only FerriteAmount decrements, and the client scales the field node off Hp, so a field pinned at a constant 0.78 scale for its entire life is what the player actually sees; the 2D viewer honours doc 16's draining-glow signature and the 3D game does not. Harvesters move exactly twice per tick, measured at a 2.00x ratio, because MovementSystem steps them and then HarvestSystem calls MoveTo and steps them again, making the unarmed economy unit faster than every tank in the roster. SpawnHarvester hard-codes its speed and ignores the /data catalogue entirely, so com_harvester.yaml's speed value is dead data, which CLAUDE.md forbids. And ordering a harvest with no refinery built is a silent no-op that still mutates state: the command is accepted, FieldId is assigned, the refinery lookup fails, and the harvester sits idle forever with no feedback, because only a fresh Harvest command ever sets the state again. The AI papers over this by re-issuing the command every tick; a human gets nothing. Two ratified GDD s4 clauses are also simply unimplemented: regrowth from seed nodes does not exist, so every match is an irreversible race to a dead map on which comebacks are mathematically impossible, and the refinery's promised free harvester does not exist either. Finally, mission-02 contains zero ferrite cells, which needs a design decision rather than a fix.

### Can you properly build bases?

Yes. The classic loop is genuinely there and honestly implemented, and this is the strongest of the five answers. A Construction Yard queues, ProductionSystem drains credits pay-as-you-build in integer percent-ticks, completion parks the type in a ready slot and pauses that yard's line, placement validates and spawns, a rejected placement retains readiness, cancelling refunds exactly what was paid, and cancelling a ready structure refunds full cost. Placement enforces GDD Q2 strict adjacency properly, an MCV founds a base from nothing, destroyed structures unblock their footprint and clear the flow cache, selling returns half, and repair is a toggle that costs a credit a tick and halts while broke. The client has the sidebar, the queue badges, the progress fill, the pulsing place button, a validity-tinted ghost, repair and sell keys, production toasts and base-under-attack audio. What is missing is not the loop, it is every layer of depth above it. Power is not a constraint: the Construction Yard and the Refinery both set no power draw at all, so a yard plus two refineries plus a factory draws forty total and one 300-credit plant covers an entire economy plus a turret and a depot, which makes the GDD's power economy decorative. There is no tech tree of any kind; BuildStructure checks cost, build time and a single hard-coded faction test, so a bare yard with 4000 credits can queue a superweapon at tick one, while every unit YAML already declares a prerequisites list that DataLoader parses and then silently drops on the floor because the type definition has no field to hold it. Only one Construction Yard and one factory are reachable from the client, because the client finds the first match and rebuilds that reference every frame, so a second factory is inert scenery even though the sim supports N independent queues keyed per producer. Rally points are factory-only, client-only, and attributed by guessing which factory a new unit came from by position proximity while iterating a dictionary in non-deterministic order. /data/buildings is an empty directory while the eight-type catalogue lives in a hard-coded switch and the stats are scattered across eight spawn methods, which CLAUDE.md explicitly forbids. Structures have no health bars at all. The sidebar gates harder than the sim does, blocking the classic queue-it-and-let-it-drip opening the sim would happily allow. And two real sim defects turned up in passing: Construction Yard build queues are not included in the state hash even though factory queues are, which is a desync-detection hole that would hide a divergence for up to forty seconds, and selling a Construction Yard silently burns any fully-paid structure sitting in its ready slot, while cancelling the same structure refunds it in full.

### Are there barriers and turrets?

Turrets yes, barriers no, and no gates either. A grep for wall, barrier, sandbag or gate across /sim, /game and /data returns nothing but flavour text and comments. The only fence in the project is a terrain visual class that aliases to a permanently blocked cell and is drawn as posts and rails: scenery, not a buildable. The turret exists and is honest, firing through the ordinary CombatSystem with no special case, at cost 600, 400 hit points, and a weapon of range 5, damage 35, AntiArmour, cooldown 12. But it is a roster of one. AntiArmour against infantry is a 40 percent multiplier, so the turret deals 17.5 damage per second to squads while three rifle squads costing exactly one turret deal 16.9 back and the turret wins by a hair; there is no cheap anti-infantry emplacement and no tier two. GDD s5's promise that turrets go offline below 75 percent power is unimplemented, because CombatSystem never reads power at all and a turret at zero supply fires happily. The good news is that the 2x2 assumption is far shallower than it looks. FootprintSize appears in only six loops, the anchor-recovery idiom appears at exactly five sites, and the generalisation is provably bit-identical for size two, which means a full wall system can ship without regenerating any of the twenty-three golden hashes; that is an acceptance criterion in the tickets below, not a hope. Three things break if walls are added naively and all three are verified: a player with one surviving 100-credit wall segment would never be eliminated because walls would satisfy the victory test, attack-move would die against a sealed base because an unreachable flow field sets Moving false while the attack-move branch sets it true again and the unit vibrates in place forever, and an uncapped wall system would blow the ratified 200-structure budget that CI already gates at 8 ms per tick. The tickets exclude walls from the victory test, add an attack-move breach behaviour that must ship in the same batch as the wall itself, and cap barriers at eighty per player, which lands two players at roughly 192 structures by arithmetic rather than by hope. Gates are deliberately deferred and the reason is honest rather than lazy: passability is one global blocked grid and the flow-field cache's only invalidation is a full clear, so a per-player-passable or auto-opening gate needs either per-player flow fields or incremental flow repair, and neither exists. Anti-air is not ticketed at all, because there is no air layer: no entity kind flies, no weapon has an anti-air flag, and the sim has no concept of altitude, so an AA turret today would be an anti-ground turret with a misleading name. The trigger condition is recorded instead of a fake ticket.

### Do units and the landscape have enough colour?

No, and the diagnosis matters more than the verdict, because the frame's problem is not that it is too dark. It is that it is achromatic, and doc 16's narrow palette is being blamed for what is largely a set of encoding and content bugs. Three defects are arithmetically provable. The ground has no hue, only value: all six colour uniforms in the splat shader sit within 12 percent of neutral grey and the largest red-to-blue ratio in the entire ground pass is 1.11, so a three-layer splat that costs real GPU time is painting a very expensive greyscale. Brightness is already roughly on target, which means lifting the base value would produce lighter mud rather than colour; the fix is hue separation between the layers, not more light. Next, every emissive surface in the game bakes clamped, because the bake target is created as an 8-bit buffer with no float flag while every emissive in the roster exceeds 1.0, and clipping one channel of a saturated colour is a hue shift rather than a brightness cap. The superweapon core bakes yellow instead of signal orange, the Sodality veil orb bakes cyan-white instead of corroded teal, the ferrite tips bake pure white, and the team band itself clips its red channel, which means doc 16's law that team colour appears in exactly one place is currently delivering that one place as a white-hot blob. Third, the palette in code is not the palette in the doc and the drift direction is desaturation: the builder's palette holds sRGB-looking fractions in Blender's scene-linear Base Color slots, so gunmetal renders at HSV saturation 0.134 against the bible's 0.188, rust at 0.390 against 0.623, teal at 0.385 against 0.571, and the shipped models come out 50 to 60 sRGB units lighter and 30 to 40 percent less saturated than the bible specifies. Pale washed-out hulls over a near-neutral ground is the exact recipe for mud. Two further findings compound it. The one-place team-colour law does not merely starve readability, it delivers zero team colour for five structures and five common units, because ModelLibrary ships one model per type shared by both players and structures get no team dressing at all, so player 0's refinery and player 1's refinery are pixel-identical olive boxes; the shipped client already breaks the one-place law anyway by adding a team ground ring, and nobody wrote that down. And the bible's only warm accent is statistically absent: ferrite occupies three cells in 6144 on the default map, which is 0.05 percent of the ground, so a palette whose sole warm note covers a twentieth of one percent is a monochrome palette in practice. Underneath all of it sits a content problem that no shader can fix: five of the six maps are one material. skirmish-01 and skirmish-02 contain zero water, hills, ruins, fences or bridges, and the missions are 99.0, 98.2 and 94.8 percent bare open ground. The client can already render all of that vocabulary. The maps simply never ask it to. Part of that is fixable with zero sim risk by reclassifying existing blocked cells, and the rest is map authoring that needs an ADR.

---

## 2. What to do about it, in one paragraph

Four waves, in dependency order. Wave A is the data and scale work: fix the default map's ferrite starvation and its asymmetry, collapse the terrain draw calls, scale the scatter, author a real 192x128 map, and land the biome palette system. Wave B is the wall system, which is the largest coherent sim ticket set in the project and is constructed so that it regenerates no existing golden hash. Wave C is colour and readability: the palette chroma pass, the emissive bake fix, the second team-colour location, and the style-bible amendment that Luke must sign. Wave D is base depth and economy: power as a real constraint, the tech tree the data already carries, primary buildings, rally attribution, the harvester quality-of-life the AI already has and the player does not, and the two real sim defects found in passing. Wave A carries one sim change and Wave B carries several; both are enumerated in section 4 so that every ADR, sign-off and golden regeneration in the plan is visible in one place rather than scattered across eighty tickets.

---
## 3. The register: deduplication ledger, waves, integration rules

Seventy-nine tickets came back across the five dimensions: 10 on map scale, 16 on the economy, 22 on base depth, 18 on defence, 13 on colour. Eleven are duplicates or direct conflicts and are superseded outright. One more (DEF-13) is split, because half of it duplicates a better ticket and half of it is a genuinely new structure. That leaves **68 tickets in the register and 12 preserved in Appendix A** (the 11 superseded, plus DEF-13's superseded half). Each resolution below names a canonical ticket and a reason; the superseded variants are preserved verbatim with do-not-implement notes, because several carry values that contradict the canonical work.

### 3.1 Deduplication ledger

1. **skirmish-01 ferrite starvation.** Three tickets fix the same bug three incompatible ways. **MAP-02 is canonical.** P5-ECON-03 proposes the same fix but mirrors on x to 95-x and moves start 1 from x=86 to x=87; MAP-02's own measurement proves 95-x hands player 1 a one-cell advantage on every field (summed distance-to-own-ten-nearest of 201 versus 191) while the true start midpoint gives x to 94-x a delta of exactly zero, and 94-x additionally preserves the existing (47,31) selftest assertion and leaves Blocked.Count at 248 so one fewer constant moves. P5-ECON-02 proposes not touching skirmish-01 at all and instead shipping a new 96x64 map and repointing the client default; it is hash-free and it ships today, and it is recorded as the **fallback** if the Architect refuses the golden regeneration, but it leaves the broken default in the theatre picker and is therefore not the recommendation. C-13 wants to author water, hills and ruins into skirmish-01 and skirmish-02 and needs the same regeneration. **Resolution: MAP-02 and C-13 land in ONE commit under ONE ADR with ONE golden regeneration.** The project should pay that cost once, not three times. Appendix A carries P5-ECON-02 and P5-ECON-03.
2. **skirmish-04 filename collision.** MAP-04 authors a 192x128 map called data/maps/skirmish-04.fmap; P5-ECON-02 authors a different 96x64 map at the same path. Since MAP-02 makes P5-ECON-02 a fallback that should not ship, **MAP-04 keeps the name skirmish-04**. If the fallback is ever invoked it takes skirmish-05.
3. **Construction Yard queues missing from the state hash.** BD-04 and TICKET-P5-DEF-16 are the same bug with the same one-condition fix, found independently. **DEF-16 is canonical** (it carries the fuller desync-detection analysis and the negative-control assertion). Appendix A carries BD-04.
4. **Walls.** BD-21 and the TICKET-P5-DEF-02 through DEF-08 set are the same feature. **The DEF set is canonical**: it carries the ADR, the bit-identical footprint proof, the attack-move breach dependency, the scenario, the art, the client and the balance gate, where BD-21 is a single ticket sketching the same ground. BD-21's insights (victory-test exclusion, build-radius exclusion, the anchor-recovery trap) are all present in the DEF set. Appendix A carries BD-21.
5. **Neutral capturable income structure (GDD s4 line 41).** BD-22 ("Ferrite Cache", new grid char 'D', map format bumped to v3) and P5-ECON-14 ("Outpost", existing structure-line syntax, no version bump). **P5-ECON-14 is canonical** on two grounds: it needs no map-format version bump, and it isolates the latent neutral-structure crash as an independently shippable fix. The naming question goes to the Game Designer with both candidate names on the table. Appendix A carries BD-22.
6. **Refinery ships a free harvester (GDD s4 line 39).** BD-16 and P5-ECON-13. **P5-ECON-13 is canonical**: it flags the AI interaction (the AI gates harvester production on `harvesters < refineryCount`, so a free harvester per refinery is a stealth AI nerf) and the hard dependency on auto-resume. Appendix A carries BD-16.
7. **Harvester auto-harvest quality of life.** BD-15 and P5-ECON-07. **P5-ECON-07 is canonical**: it respects an explicit Stop, rate-limits the re-issue, and mirrors the sim's tie-break exactly. BD-15's clause that an explicit rally must beat auto-harvest is folded into P5-ECON-07 as clause 2a. Appendix A carries BD-15.
8. **Turrets go offline below 75 percent power (GDD s5 line 48).** BD-08 and TICKET-P5-DEF-10. **DEF-10 is canonical** because it found the consequence BD-08 missed: ScenarioVeil spawns its turret for a player who owns no power plant and would throw, turning CI red, and ScenarioStealth spawns its turret 100 ticks before its plant so its Rule 1 assertion would keep passing for the wrong reason. Appendix A carries BD-08.
9. **Tech tree / prerequisites.** BD-17 (honour the prerequisites /data already carries, dropped by ToTypeDef) and TICKET-P5-DEF-13 (a hard-coded PrereqOf switch plus a tier-2 turret). **BD-17 is canonical for the mechanism**, because CLAUDE.md requires gameplay numbers to live in /data and the prerequisite lists are already authored there; a hard-coded switch would be a second source of truth. DEF-13's bastion turret and BastionGun are additive and survive as **DEF-13b** in Wave D, rebased onto BD-17's data path. Appendix A carries DEF-13's PrereqOf clauses.
10. **Structure catalogue into /data.** BD-06 (full YAML catalogue plus schema) and TICKET-P5-DEF-11 (extend the record only). **BD-06 is canonical**; DEF-11 is a strict subset of it. Appendix A carries DEF-11.
11. **docs/design/16-visual-style.md amendment.** C-12 rewrites the whole bible including a two-place team-colour law; DEF-18 amends the same law for chained barriers. These conflict as written and must be one amendment. **Merged into section 5 of this document**, which is the single proposed text and needs Luke's sign-off. Neither C-12 nor DEF-18 may land independently.

### 3.2 Wave summary

- **Wave A, the data fixes.** The map ferrite bug, the draw-call collapse, scatter density, a real big map, the biome palette system. Everything here is presentation or data except one bundle: MAP-02 plus C-13, which is map data but regenerates a golden hash. The owner's framing of Wave A as "no sim risk" holds for every ticket in it except that bundle, and the bundle is placed here anyway because it is map-data work and belongs conceptually with its neighbours. It is flagged at every mention and enumerated in section 4.
- **Wave B, the wall system.** The largest coherent sim ticket set in the project, and the one with the best-constructed safety property: DEF-03, DEF-04 and DEF-05 are built so that all twenty-three existing golden hashes stay byte-identical, and that is the acceptance criterion rather than an aspiration. DEF-04 and DEF-05 must merge in the same batch; shipping the wall without the breach behaviour is shipping a bug.
- **Wave C, colour and readability.** The palette chroma pass, the emissive bake fix, the light rig spread, the second team-colour location, the style-bible amendment. Wave C is where the answer to question five actually gets delivered; Wave A's biome work is the ground half of it and lands first because MAP-01 depends on the same function.
- **Wave D, base depth and economy.** Everything else: power as a constraint, the tech tree, radar, primary buildings, rally attribution, harvester quality of life, the queue cap, the two real sim defects, and the GDD clauses that were ratified and never built.

### 3.3 Integration rules (read before implementing any wave)

1. **BuildTerrain is edited by three tickets and the order matters.** C-02 changes how the terrain materials are constructed and adds the six shader-uniform calls; MAP-01 changes node emission from AddChild to a batched Emit; MAP-03 changes the scatter counts. Land them in the order **C-02, then MAP-01, then MAP-03**. MAP-01 batches by Material reference, so C-02's biome-derived materials must exist before the Emit call sites are written, or every call site is touched twice.
2. **The scatter MultiMeshes are edited by two tickets.** MAP-03 changes the instance counts (lines 784/787, 884/886, 928/930, 985/987); C-01 changes the per-instance colours (lines 807, 903, 960, 1015). They are compatible and orthogonal. Land C-01 first (it is the cheapest win in the project), then MAP-03, and re-check MAP-03's talus-fold index arithmetic afterwards because it is the one place a naive edit breaks.
3. **The placement ghost is edited by two tickets.** DEF-01 sizes the ghost from the footprint and attaches a range ring as its child; BD-11 replaces the box ghost with the real structure model. Land **DEF-01 first** (it is an S-effort win that improves the shipped turret today), then BD-11, which must preserve DEF-01's ring child and its cached-material fix rather than reintroducing the per-frame allocation.
4. **MAP-02 and C-13 are one commit.** Both edit skirmish-01, both regenerate the `skirmish` golden, and both need the same ADR. Landing them separately means two ADRs, two regenerations and two Balance re-runs for one map.
5. **C-09 before C-13.** C-09 reclassifies existing blocked cells and is hash-neutral by construction; C-13 adds new cells and is not. They compose (C-09 changes characters, C-13 changes counts) and C-09 ships today for free, so it should not wait behind an ADR.
6. **DEF-04 and DEF-05 ship in one batch.** DEF-05 is not a nice-to-have. Without it, a walled base makes the flow field return -1 for attackers across the whole map and the AI's waves halt at their own base.
7. **DEF-02 (ADR-005) gates DEF-03, DEF-04 and DEF-05.** None of them may merge before it is ratified. DEF-02 closes the ADR-005 slot already reserved in docs/adr/ADR-open-queue.md for "Tile size, grid resolution, footprint rules" rather than minting a new number.
8. **DEF-13b's struct-type number must agree with DEF-03's FootprintOf.** DEF-03 reserves struct types 9 and 10 for wall and gate; DEF-12's emplacement and DEF-13b's bastion must be numbered around that reservation and FootprintOf must agree. A footprint mismatch is silent and fatal. State the final numbering in ADR-005.
9. **C-05 and C-06 rebake the same roster.** Do them in one session so the bake runs once. C-06 must land with or before C-05, or the chroma C-05 restores is immediately clipped away by the 8-bit bake.
10. **C-12 and DEF-18 are superseded by section 5 of this document.** Do not implement either. The merged amendment is the only text, and it is blocked on Luke.
11. **BD-06 before BD-07, DEF-12 and BD-17.** BD-06 moves the structure catalogue to /data hash-neutrally; doing it first turns those three tickets into data edits plus behaviour code rather than more hard-coding.
12. **P5-ECON-04 and P5-ECON-05 land in one golden regeneration.** They overlap on harvester speed; separately they cost two ADRs and two regenerations for one behaviour change.
13. **MAP-04 depends on MAP-01 and MAP-03.** Merging the big map first ships it at roughly 6000 draw calls and a quarter of the intended scatter density.
14. **MAP-06 depends on MAP-04.** The gate loads the map the ticket authors.
15. **P5-ECON-13 depends on P5-ECON-07.** A free harvester that spawns and sits idle forever is worse than no free harvester.

---
## Wave A: the data fixes

Everything in this wave is presentation or data with one exception, flagged at every mention: the **MAP-02 plus C-13 bundle** edits skirmish-01, regenerates the `skirmish` golden hash, and needs an ADR with Architect sign-off. It sits in Wave A because it is map-data work, not because it is free.

Suggested landing order: C-09 (free, today), C-02, C-04, C-03, MAP-01, MAP-03, C-01 (from Wave C, it is the cheapest win in the project), MAP-05, MAP-07, MAP-09, then the MAP-02 plus C-13 bundle once the ADR is signed, then MAP-04, MAP-06, MAP-08. MAP-10 is conditional and must not be built.

### A.1 The default map bundle (ONE ADR, ONE golden regeneration, ONE commit)

#### MAP-02. Fix skirmish-01 ferrite starvation: 3 cells to 20, mirror-symmetric on the true start axis

- Dimension: map scale and layout
- Impact: HIGH | Effort: S | cheapModelSafe: true | **touchesSim: TRUE**
- Files: data/maps/skirmish-01.fmap; sim/Ferrostorm.Sim.Runner/Program.cs (lines 1371, 1375); sim/golden-hashes.txt
- Spec:

TOUCHES SIM: skirmish-01.fmap feeds BuildSkirmishWorld (Program.cs:464), which backs the `skirmish` golden (currently 0xDF844901214FD6C5) and ReplayCheck. This regenerates that golden and needs an ADR + Architect sign-off before merge. No other golden is affected (mission-01/02/03 use data/missions/*.fmap).

WHY: 3 F cells = 36,000 credits vs skirmish-02/03's 20 cells = 240,000. Measured: 5000 AI-vs-AI ticks yield only 28 live entities on skirmish-01 vs 45 on skirmish-03. With this fix: 47.

DESIGN RULE (do not deviate): starts are (8,30) and (86,30), midpoint x=47, so the FAIR mirror axis is x -> 94-x, NOT the grid centre 95-x. Mirroring on 95-x hands P1 a 1-cell advantage on every field (summed distance-to-own-10-nearest P0=201 vs P1=191). On 94-x the delta is exactly 0. x=47 is self-mirror, which is why the two centre cells sit at x=47 and the existing (47,31) assertion survives.

FINAL FIELD SET (20 cells, verified all land on open '.' ground, verified mirror-closed under x->94-x):
- P0 side (9): (20,29) (21,29) (20,30) (21,31) | (30,14) (31,14) (30,15) | (30,47) (31,47)
- P1 side (9): (74,29) (73,29) (74,30) (73,31) | (64,14) (63,14) (64,15) | (64,47) (63,47)
- contested centre (2, self-mirror, in the Ferrite Gap): (47,30) (47,31)

Remove the two old off-axis cells (22,24) and (74,24). Keep (47,31). Blocked cells are NOT touched - the count stays 248, so Program.cs:1370 needs no edit.

EXACT FILE EDIT. Grid row y lives on 1-based file line 6+y. Replace exactly these 7 rows, byte for byte (96 chars each); every other line stays identical:

```
line 20 (y=14): ..............................FF..............####.............FF...............................
line 21 (y=15): ..............................F...............####..............F...............................
line 30 (y=24): ..............................................####..............................................
line 35 (y=29): ....................FF...................................................FF.....................
line 36 (y=30): ....................F..........................F..........................F.....................
line 37 (y=31): .....................F.........................F.........................F......................
line 53 (y=47): ..............................FF..............####.............FF...............................
```

CODE EDITS (two integer constants only):
- Program.cs:1371  `if (md.Fields.Count != 3 || !md.Fields.Contains((47, 31)))`  ->  `!= 20` (leave the (47,31) check and the message alone; it still holds)
- Program.cs:1375  `if (mw.EntityCount != 3)`  ->  `!= 20`

THEN regenerate the golden: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release golden 2026`, and paste the new `skirmish 2026 0x...` value over the existing line in sim/golden-hashes.txt. Commit the ADR alongside.

OUT OF SCOPE, REPORT DO NOT DO: the starts themselves are asymmetric (P1 at x=86; the exact mirror of x=8 is x=87) and the central wall's gap rows 28-33 are not symmetric about y=31.5 (16 cells at x=46-49, rows 28,29,34,35 break 180-deg rotational symmetry). Both are pre-existing. This ticket deliberately achieves exact ferrite fairness WITHOUT touching them. If the Architect wants full map symmetry, that is a separate ticket and a second golden regen.

- Acceptance: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 (selftest passes with the new 20 constants, determinism double-run identical, perf gate green). MapData.Load on the file reports Width=96 Height=64 Fields.Count=20 Blocked.Count=248 and Fields contains (47,31). Machine-checkable fairness invariant: for every field (x,y) in the set, (94-x, y) is also in the set; and the multiset of Chebyshev distances from (8,30) to all 20 fields equals the multiset from (86,30) to all 20 fields (both are [12,12,13,13,22,22,22,23,23,39,...] with summed-nearest-10 = 201 each; delta must be 0). A 5000-tick AI-vs-AI match on the map ends with >=40 live entities (was 28). sim/golden-hashes.txt has exactly one changed line (`skirmish`); every other hash byte-identical. ADR present and signed off.

#### C-13. Author real terrain and ferrite into skirmish-01 and skirmish-02 (ADR + golden regeneration)

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Files: data/maps/skirmish-01.fmap, data/maps/skirmish-02.fmap, data/missions/mission-01.fmap, data/missions/mission-02.fmap, data/missions/mission-03.fmap, sim/golden-hashes.txt, docs/adr/
- Spec:

TOUCHES SIM BEHAVIOUR - flagged accordingly. Adding, removing or moving any blocked cell or any 'F' changes the pathing and economy inputs, so every affected golden hash in sim/golden-hashes.txt regenerates. Per CLAUDE.md that is a replay-compatibility break requiring an ADR in docs/adr/ (use ADR-000-template.md) and Architect sign-off. DO NOT START without that sign-off. Bundle it with the already-known skirmish-01 ferrite bug (3 deposit cells against skirmish-02/03's 20) so the project pays the regeneration cost once, not twice.

Why it is needed even after C-09: C-09 can only reclassify existing characters, so it cannot add water, ferrite or new ridge mass. After C-09, skirmish-01 is still 5893 open cells of one ground shader with 3 ferrite cells (0.05% of the map) and no water; skirmish-02 is still 5806 open cells with no water. Both are landscapes with nothing in them, and no amount of shader work fixes an empty map. Only skirmish-03 currently uses the terrain vocabulary the client already supports (360 'w', 74 'h', 54 'f', 44 'r', 24 'B').

Scope for the authoring pass, per map, preserving the existing mirror symmetry exactly (both maps are symmetric by design and the balance depends on it):

1. skirmish-01: raise 'F' from 3 to 20 cells placed symmetrically (this is the known design bug, and it is the reason the regeneration is being paid for); add a water feature of 40 to 80 'w' cells with 4 to 8 'B' bridge cells so the map has a crossing and the water/foam/shore materials have somewhere to exist; add 30 to 60 'h' cells as rolling high ground away from the start positions at (8,30) and (86,30).
2. skirmish-02: add 20 to 40 'r' ruin cells as a contested centre feature and 20 to 40 'f' fence cells as field boundaries near the start positions at (10,10) and (85,53). Keep 'F' at 20.
3. mission-02 currently has ZERO ferrite cells - decide with the Game Designer whether that is intentional for its scenario before touching it; if it is, leave it and record why.
4. Every start position and every existing chokepoint must be re-validated for pathability after the edits: run the pathing and expansion scenarios and confirm the AI still reaches every ferrite field.

Why not cheap-model-safe: map layout is balance design, not colour work. This ticket exists in this dimension's list because the palette work has a ceiling that map content sets, and that ceiling must be named rather than papered over with shaders.

Suggested owner: Game Designer with Balance co-sign, not the art track.

**Doc 22 integration note:** MAP-02 supplies the exact 20-cell ferrite set and the correct mirror axis (x -> 94-x, not 95-x). C-13's clause 1 ferrite work is therefore MAP-02's field set verbatim; do not re-derive it. C-13's remaining scope (water, bridges, hills on skirmish-01; ruins and fences on skirmish-02) lands in the same commit.

- Acceptance: ADR filed in docs/adr/ and marked Ratified by Luke, with Architect sign-off recorded, BEFORE any map byte changes. After the edits: dotnet run --project sim/Ferrostorm.Sim.Runner -c Release exits 0; sim/golden-hashes.txt regenerated in the same commit as the map changes and never separately; the determinism CI (Windows and Linux) is green on the regenerated hashes. Balance check: each player reaches an equal count of ferrite cells at equal path cost from their start (verifiable from the pathing scenario); mirror symmetry holds (the grid is invariant under the map's existing symmetry transform). Offscreen captures of skirmish-01 and skirmish-02 show water, bridges, hills and ruins in frame; mean HSV saturation over the full frame rises by at least 0.03 absolute on each versus post-C-09.

### A.2 Free terrain variety today (hash-neutral)

#### C-09. Hash-neutral terrain-class dressing: reclassify existing ridge cells on the five undressed maps

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: data/maps/skirmish-01.fmap, data/maps/skirmish-02.fmap, data/missions/mission-01.fmap, data/missions/mission-02.fmap, data/missions/mission-03.fmap
- Spec:

Verified grid census: skirmish-01 is 5893 open / 248 '#' / 3 'F' with ZERO water, hills, ruins, fences or bridges; skirmish-02 is 5806 / 318 / 20, also zero; mission-01/02/03 are 99.0% / 98.2% / 94.8% bare open ground. The client can already render hills, ruins and fences (BattlefieldView.BuildTerrain cases 'h', 'r', 'f') - the maps simply never ask it to, so five of six maps are one ground material plus grey ridge boxes. This ticket is HASH-NEUTRAL BY CONSTRUCTION: MapLoader.cs lines 95-98 add '#', 'h', 'r' and 'f' to the SAME `blocked` list in the same row-major order, and only 'h'/'r'/'f' additionally populate the client-only `Visual` dictionary. Reclassifying an existing '#' to 'h'/'r'/'f' therefore produces a byte-identical blocked set and cannot move a hash.

HARD RULE: reclassify existing characters in place ONLY. Never add a blocked cell, never remove one, never add or move an 'F', never change a grid row's length, never touch a header or a v2 mission section. Adding or removing blocked cells changes pathing and IS a hash change - that work is C-13, not this ticket.

1. Write a one-shot script (put it in the scratchpad, do not commit it) that, per map, does 4-connectivity connected-component labelling over '#' cells and rewrites each component by area: area >= 50 stays '#'; 20 <= area < 50 becomes 'h'; area < 20 becomes 'r'. Deterministic, order-independent, no judgement.
2. Expected results, verify against these exact numbers before writing anything (component areas measured from the current files): skirmish-01 has 6 components sized 64, 56, 32, 32, 32, 32 -> two stay '#' (mesas), four become 'h' (184 cells changed). skirmish-02 has 12 components sized 62, 62, 34, 34, 27, 27, 12, 12, 12, 12, 12, 12 -> two stay '#', four become 'h', six become 'r' (194 cells changed). mission-01 has one component of 28 -> 'h'. mission-02 has one component of 41 -> 'h'. mission-03 has two components of 80 -> unchanged. If your component census does not match these counts exactly, stop: the map files have changed under this ticket and it must be re-specced.
3. Both skirmish maps are mirror-symmetric by design (component sizes come in identical pairs) - the area rule preserves that automatically. Confirm the pairs still match after rewriting.
4. Why these classes: 'r' (ruinMat, dark 0.16,0.155,0.145 / light 0.28,0.27,0.25) is the warmest terrain material in the game and currently appears on 44 cells across the entire project; 'h' renders as squashed spheres that merge into rolling ground and gives hillMat somewhere to exist outside skirmish-03. Both also feed BuildScatter: 'r' cells attract the plate-debris MultiMesh (50% of 250 instances cluster on ruins), so the reclassification pays out in scatter variety as well as material variety.
5. Note the ceiling honestly in the commit message: this cannot add water, ferrite or new ridge mass, so skirmish-01 and skirmish-02 remain feature-poor. C-13 covers that and needs Architect sign-off.

- Acceptance: dotnet run --project sim/Ferrostorm.Sim.Runner -c Release exits 0 with EVERY hash in sim/golden-hashes.txt byte-identical to before the change - this is the load-bearing check and any delta means a blocked cell moved. A diff of each .fmap shows changes to grid characters only, no line-count or line-length change, no header change, and the multiset of {blocked characters} per row is preserved (count of '#'+'h'+'r'+'f' per row is unchanged, and count of '.' , 'F', 'w', 'B' per row is unchanged). Offscreen captures of skirmish-01 and skirmish-02 show visible hills and (on 02) ruin stubs where there were only ridge boxes; mean HSV saturation over the full frame rises by at least 0.02 absolute on each. game/ builds clean.

### A.3 The client scaling fixes (no sim risk)

#### MAP-01. Collapse per-cell terrain dressing into per-material MultiMeshes (6114 draw calls -> ~12)

- Dimension: map scale and layout
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/BattlefieldView.cs (BuildTerrain, lines 332-822)
- Spec:

This is the single change that makes large maps renderable. It is a mechanical transform: SAME meshes, SAME transforms, SAME materials, SAME visual result - only the node topology changes. Follow the pattern already proven at BattlefieldView.cs:766-819 (W4-12 rubble MultiMesh).

STEP 1. Add a local helper before the `foreach (var (bx, by) in blockedCells)` loop:

```csharp
var box = new BoxMesh { Size = Vector3.One };      // UNIT box; per-instance scale encodes real size
var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 14, Rings = 7 }; // UNIT sphere
var batches = new Dictionary<Material, List<Transform3D>>();
void Emit(Material mat, Vector3 size, Vector3 pos, Vector3 rotEuler)
{
    if (!batches.TryGetValue(mat, out var l)) batches[mat] = l = new List<Transform3D>();
    l.Add(new Transform3D(Basis.FromEuler(rotEuler).Scaled(size), pos));
}
```

STEP 2. Replace every `parent.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = S }, Position = P, Rotation = R, MaterialOverride = M })` inside BuildTerrain with `Emit(M, S, P, R)` (pass Vector3.Zero for R where the original had no Rotation). Convert exactly these call sites, all currently inside the blockedCells switch and the bridge loop:
- water slab (line ~533): waterMat, size (1.02,0.1,1.02), pos (bx+0.5,-0.12,by+0.5), rot zero
- 4x shore ramp (lines ~546,563,578,594): shoreMat, sizes (0.36,0.02,1.04) for W/E and (1.04,0.02,0.36) for N/S, keep each existing Position and Rotation EXACTLY (Z rot -0.33/+0.33 for W/E; X rot +0.33/-0.33 for N/S)
- 4x foam strip (lines ~553,569,585,601): foamMat, sizes (0.07,0.012,1.04) W/E and (1.04,0.012,0.07) N/S, keep Positions, rot zero
- ruin main box (line ~624): ruinMat, size (0.9,wh,0.9), keep Position and the Y rotation
- ruin stub box (line ~632): ruinMat, size (0.4,wh*1.6,0.3), keep Position, rot zero
- fence posts (line ~642) and rails (line ~649): fenceMat, sizes (0.07,0.5,0.07) and (1.0,0.05,0.05), keep Positions, rot zero
- ridge box (line ~667): ridgeMat, size (1.18,jh,1.18), keep Position and the 3-axis Rotation
- cliff strata base (line ~679): cliffBaseMat, size (1.30,jh*0.38,1.30), keep Position and Y rotation
- cliff strata upper (line ~686): ridgeMat, size (1.22,jh*0.30,1.22), keep Position and Y rotation
- mesa dust cap (line ~693): groundShader, size (1.08,0.07,1.08), keep Position, rot zero
- bridge deck (line ~728): deckMat, size (1.04,0.1,1.04)
- bridge rails (line ~735): fenceMat, size (0.08,0.16,1.04)
- bridge piers (line ~744): ridgeMat, size (0.14,0.55,0.14)
- bridge cross-beam (line ~750): fenceMat, size (1.04,0.06,0.2)
- bridge kerb posts (line ~758): fenceMat, size (0.06,0.22,0.06)

HILL (line ~613) is the ONLY non-box: keep a separate `hillXforms` List<Transform3D>. Original is SphereMesh{Radius=hr, Height=hh} at pos P where hr=1.05+rng*0.25 and hh=1.5+rng*0.7. Against the UNIT sphere above (radius 1, height 2) the equivalent instance scale is (hr, hh/2f, hr). Emit Transform3D(Basis.Identity.Scaled(new Vector3(hr, hh/2f, hr)), P).

STEP 3. After the blockedCells loop AND after the bridge loop (so both have contributed), materialise each batch:

```csharp
foreach (var (mat, xforms) in batches) {
    var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = box, InstanceCount = xforms.Count };
    for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
    parent.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, Name = "TerrainBatch" });
}
```

and the same for hillXforms with Mesh = sphere, MaterialOverride = hillMat, Name = "Hills".

Note MultiMesh needs ONE Mesh, so every box-shaped batch shares `box` and only the material differs - that is why batching by Material is correct and sufficient.

STEP 4 (CRITICAL, do not skip). Do NOT set CastShadow = Off on these batches. The existing per-cell terrain nodes cast shadows by default and the doc-20 look depends on ridge/hill shadows. Leave CastShadow at its default (On). Only the pre-existing scatter MultiMeshes (Rubble/Tufts/Rocks/Plates) keep CastShadow = Off, as they already do.

STEP 5. Leave UseColors unset (false) on these batches - unlike rubble/talus, none of the per-cell terrain code tints instances. Do not add VertexColorUseAsAlbedo to these materials.

DO NOT TOUCH: the ArrayMesh heightfield Ground (lines 382-429) - it is already one mesh and one draw call, and its ~96 ms build at 192x128 is a one-off load cost, not a per-frame cost. Do not touch GroundHeight, _flatCells, _basinCells, the talus fold into the rubble MultiMesh, or BuildScatter.

- Acceptance: Add a throwaway headless probe (delete before commit) that calls BattlefieldView.BuildTerrain on each map and counts children: `int n = 0; foreach (Node c in root.GetChildren()) if (c is MeshInstance3D) n++;`. Required results: skirmish-03 (96x64, 548 blocked) drops from 1420 top-level MeshInstance3D to 0 MeshInstance3D + at most 15 total children (Ground + <=10 terrain batches + Hills + Rubble + Tufts + Rocks + Plates). A 192x128 map at 8.9% terrain density drops from 6114 to the same <=15. BuildTerrain wall time must not regress beyond 120 ms at 192x128. Visually: an offscreen capture of skirmish-03 before and after must show the same water slabs, shore ramps, foam lines, hills, ruins, fences, ridges with strata and dust caps, and bridges with piers/rails/kerbs, in the same places, still casting shadows. `dotnet build sim/Ferrostorm.Sim.Runner -c Release` unaffected; sim goldens byte-identical (this ticket touches no sim file).

#### MAP-03. Scale ground scatter density with map area (fixed 2200/900/350/250 go 4x sparse on a big map)

- Dimension: map scale and layout
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/BattlefieldView.cs (lines 784, 787, 884, 886, 928, 930, 985, 987)
- Spec:

The four scatter MultiMeshes use absolute instance counts that ignore map area, so a 192x128 map renders at 1/4 the intended density and reads as bare ground. All four are already MultiMesh (1 draw call each), so raising the counts costs draw calls nothing.

Add once, at the top of BuildTerrain (it must be visible to BuildScatter, so pass it as a parameter or compute it again there):

```csharp
// 96x64 = 6144 cells is the density reference the W4-12/W4-15 counts were tuned at.
float densityScale = (w * h) / 6144f;
int Scaled(int baseCount) => Mathf.RoundToInt(baseCount * densityScale);
```

Apply, keeping the two-site pattern (InstanceCount and the for-loop bound) consistent at each:
- rubble: line 784 `InstanceCount = 2200 + talus.Count` -> `InstanceCount = Scaled(2200) + talus.Count`; line 787 `for (int i = 0; i < 2200; i++)` -> `for (int i = 0; i < Scaled(2200); i++)`; and the talus fold at lines 809-812 currently indexes `2200 + i` -> must become `Scaled(2200) + i`. THIS IS THE ONE PLACE A NAIVE EDIT BREAKS: get all three 2200s.
- tufts: lines 884/886, 900 -> Scaled(900)
- rocks: lines 928/930, 350 -> Scaled(350)
- plates: lines 985/987, 250 -> Scaled(250)

Hoist `Scaled(...)` into a local (e.g. `int rubbleN = Scaled(2200);`) at each site rather than calling it in the loop condition.

Do not change the rejection logic (blockedSet / noise-threshold / zeroScale) or the placement maths - only the counts. At 96x64 densityScale is exactly 1.0 so the existing three maps must render bit-identically.

- Acceptance: At 96x64 densityScale == 1.0f and the instance counts are exactly 2200/900/350/250, so an offscreen capture of skirmish-03 is unchanged from before this ticket. At 192x128 the counts are 8800/3600/1400/1000 and a capture shows scatter at visibly the same per-cell density as the 96x64 maps (spot-check: count rubble pieces in a 10x10-cell crop; must match within 20% across the two map sizes). Draw calls do not increase (still 4 MultiMeshInstance3D). No index-out-of-range from the talus fold: assert `mm.InstanceCount == rubbleN + talus.Count`.

#### MAP-05. Scale camera zoom ceiling with map size, and raise the coupled shadow/fog distances

- Dimension: map scale and layout
- Impact: MEDIUM | Effort: S | cheapModelSafe: false | touchesSim: false
- Files: game/scripts/RtsCamera.cs (line 15); game/scripts/SkirmishLive.cs (lines 201-209); game/scripts/BattlefieldView.cs (lines 78, 112)
- Spec:

RtsCamera.MaxHeight = 42f is a fixed zoom-out ceiling tuned for 96x64. On a 192x128 map the same ceiling shows 1/4 the proportional view, so crossing the map is 4x the panning. Raise it with map size - but the raise is COUPLED to two lighting distances and must not be done alone.

Step 1. In SkirmishLive._Ready, after `_cam.BoundsMax = new Vector2(map.Width, map.Height);` (line 208), add:

```csharp
// 42 was tuned against 96x64; scale the ceiling by the map's linear extent
// (sqrt of area) so a 4x-area map zooms out 2x, not 4x.
float linear = Mathf.Sqrt((map.Width * map.Height) / 6144f);
_cam.MaxHeight = Mathf.Clamp(42f * linear, 42f, 84f);
```

At 96x64 linear == 1 so MaxHeight stays exactly 42 (no change to shipped maps). At 192x128 linear == 2 -> MaxHeight 84.

Step 2 (MANDATORY, this is the coupling). At -50 deg pitch the camera-to-ground distance is height/sin(50 deg) ~= height*1.305. At MaxHeight 42 that is ~55 units, comfortably inside DirectionalShadowMaxDistance = 90f (BattlefieldView.cs:112). At MaxHeight 84 it is ~110 units, so the far shadow split falls SHORT and shadows visibly pop out when zoomed out. Give BuildLightRig and BuildEnvironment an optional max-camera-height argument (default 42f, so ReplayTheater and every existing caller is unchanged) and derive:

```csharp
DirectionalShadowMaxDistance = Mathf.Max(90f, maxCamHeight * 1.305f * 1.6f)   // 1.6 = ground coverage beyond the look-at point
VolumetricFogLength         = Mathf.Max(110f, maxCamHeight * 1.305f * 1.9f)
```

At maxCamHeight=42 these evaluate to 90 and 110 exactly - the shipped values, unchanged. At 84 they give ~175 and ~208. SkirmishLive must compute `linear`/MaxHeight BEFORE it calls BuildLightRig (currently line 211) and BuildEnvironment (line 193) and pass the value through. Reorder those calls if needed.

cheapModelSafe=false: the 84 ceiling, the 1.6/1.9 coverage factors and the sqrt (rather than linear) scaling are feel calls. A designer must eyeball a 192x128 map at full zoom-out and confirm the view is useful, the shadows do not pop at the far split, and the volumetric fog still closes the horizon rather than revealing the void past the map edge. Tune the constants against the capture, not against this text.

- Acceptance: On all three shipped 96x64 maps, MaxHeight is exactly 42f, DirectionalShadowMaxDistance exactly 90f and VolumetricFogLength exactly 110f - an offscreen capture is pixel-identical to before this ticket. On a 192x128 map MaxHeight is 84f and the derived distances are >=170 and >=200. Zoomed fully out on the 192x128 map, an offscreen capture shows terrain shadows still present at the far edge of the view (no shadow cut-off line) and no view of the void beyond the map boundary. RtsCamera's zoom-along-cursor-ray maths still clamps exactly to MinHeight/MaxHeight.

#### MAP-07. Rewrite the fog texture update as a byte[] fill (7.5x faster; NOT the wall the brief assumed)

- Dimension: map scale and layout
- Impact: LOW | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/FogOfWar.cs (lines 20-57)
- Spec:

HONEST FRAMING, READ FIRST: this was flagged as the likely scaling wall. It is not. MEASURED in-engine, the shipped per-cell SetPixel loop costs 0.143 ms/tick at 96x64 and 0.558 ms/tick at 192x128 = 0.8% of a 15 Hz tick (66.7 ms); at 256x192 it is 1.120 ms = 1.7%. It does not block any map size under consideration. Do this ticket for the cheap 7.5x headroom, not to unblock anything. If time is short, MAP-01 and MAP-03 are what actually matter - deprioritise this.

Replace Image.SetPixel per cell + ImageTexture.Update with a persistent byte[] + Image.SetData + ImageTexture.Update. Measured 0.558 -> 0.075 ms at 192x128.

In Init(int w, int h): keep the Image/ImageTexture/material/MeshInstance3D setup exactly as-is (the PlaneMesh size, the y=3.2 shroud height, Linear filtering for the soft edge, and the alpha values are all load-bearing - do not touch them). Additionally allocate `_buf = new byte[w * h * 4];` and create the image via `_img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, _buf);` after filling _buf with the unexplored colour.

In UpdateFrom(World world, int player): keep the identical three-branch logic and the identical colours, but write bytes. The shipped Colors are float RGBA; their Rgba8 byte equivalents (round(c*255)) are:
- visible          Color(0,0,0,0)                      -> 0, 0, 0, 0
- explored-unseen  Color(0.012f,0.018f,0.030f,0.38f)   -> 3, 5, 8, 97
- unexplored       Color(0.008f,0.012f,0.022f,0.985f)  -> 2, 3, 6, 251

Loop y then x (row-major, matching the buffer stride), index `int i = (y * _w + x) * 4;`, write the four bytes, then after the loop call `_img.SetData(_w, _h, false, Image.Format.Rgba8, _buf);` and `_tex.Update(_img);` exactly once.

Keep the public surface identical: `public Image FogImage => _img;` must still return the same Image instance, because Minimap.Init (Minimap.cs:64) builds an ImageTexture from it and Minimap.Refresh (line 78) calls Update on it every frame. Do not swap the Image instance on update or the minimap shroud silently freezes.

Do NOT attempt a dirty-region or lower-update-rate optimisation. Measured, the full-image rebuild at 192x128 is 0.1 ms after this change; a dirty-region scheme would add real bookkeeping and correctness risk (stale shroud edges) to save nothing.

**Doc 22 integration note:** C-08 also edits the explored-unseen colour, changing it to Color(0.020f, 0.030f, 0.052f, 0.30f). If C-08 lands first, the byte equivalents above become 5, 8, 13, 77. Land C-08 first and recompute, or land MAP-07 first and have C-08 edit bytes rather than Colors. Do not let the two tickets disagree on the shroud colour.

- Acceptance: In-engine timing of UpdateFrom over 60 calls at 192x128 is under 0.15 ms/tick (was 0.558). An offscreen capture of the shroud on skirmish-03, at a fixed tick with a fixed unit layout, is pixel-identical to a pre-change capture (the byte constants above are exact). The minimap shroud still tracks visibility live (walk a unit, confirm the minimap fog opens). FogOfWar.FogImage returns the same Image reference across ticks (assert reference stability across two UpdateFrom calls).

#### MAP-09. Stop the minimap aliasing single-cell terrain away on maps wider than 168 cells

- Dimension: map scale and layout
- Impact: LOW | Effort: S | cheapModelSafe: false | touchesSim: false
- Files: game/scripts/Minimap.cs (lines 14, 37, 43-61)
- Spec:

Minimap draws its terrain base Image (w x h pixels, one pixel per cell) into a rect fixed at SizePx = 168 wide with TextureFilterEnum.Nearest (line 57). At 96 cells wide that is 1.75 px/cell and every ridge survives. At 192 cells wide it is 0.875 px/cell, so with Nearest sampling isolated single-cell features drop out entirely and thin one-cell ridge lines break into dashes. The minimap is the primary navigation aid on a big map (registered BLOCKER B11 in docs/design/18-game-review-roadmap.md:29), so it must stay legible.

Pick ONE of these two, not both:
- Option A (cheapest, recommended): change the terrain base rect's TextureFilter from Nearest to Linear (line 57) so downsampling averages rather than drops. Cells then blur together instead of vanishing. Leave the fog rect alone - it already has no explicit filter and inherits the default.
- Option B: raise SizePx (line 14) to 192 so a 192-wide map maps 1:1 and keep Nearest. This grows the minimap panel; it must be checked against the sidebar layout and the OffsetTop = -(SizePx + 44) anchoring at line 36, and it makes the 96x64 maps' minimap larger too.

Whichever is chosen, the aspect maths at line 37 (`CustomMinimumSize = new Vector2(SizePx, SizePx * h / w)`) already handles non-96x64 shapes correctly and needs no change: at 192x128 it yields 168 x 112, the same shape as 96x64. Do not touch the Ping/_dots/_frustum normalisation (they divide by _w/_h and scale by Size, which stays correct).

cheapModelSafe=false: this is a legibility judgement. Option A trades crispness for coverage and a designer should confirm the blurred base still reads as terrain against the cinder palette rather than turning to mush, and that the amber frustum polyline and the 3x3 entity dots still pop against it.

- Acceptance: On a 192x128 map the minimap base shows every ridge mass from the .fmap with no single-cell feature fully absent (compare a 4x-upscaled screenshot of the minimap rect against a directly-rendered 192x128 image of the blocked set; no blocked cluster present in the source may be entirely missing from the minimap). On the three shipped 96x64 maps the minimap remains legible and the panel does not overlap the sidebar. Entity dots, alert pings and the camera frustum polyline all still draw on top of the base (the ShowBehindParent ordering at lines 59/69 is unchanged).

### A.4 The big map

#### MAP-04. Author skirmish-04, a 192x128 two-player map (data only; MainMenu auto-discovers it)

- Dimension: map scale and layout
- Impact: HIGH | Effort: M | cheapModelSafe: false | touchesSim: false
- Files: data/maps/skirmish-04.fmap (new); tools/ (new generator script)
- Spec:

DEPENDS ON MAP-01 AND MAP-03 - do not merge this before both, or the map ships at ~6000 draw calls and 1/4 scatter density.

No code change is needed anywhere: MainMenu.cs:57 globs data/maps/*.fmap into the THEATRE picker, and SkirmishLive reads Width/Height/Blocked/Visual/Starts straight from MapData. Measured: a 192x128 map runs a full 5000-tick AI match at mean 0.040 ms/tick, p99 1.525, max 3.332 (budget 8), zero ticks over budget.

FORMAT (sim/Ferrostorm.Sim/MapLoader.cs is the spec): header `ferrostorm-map v1`, `size 192 128`, one `start P CX CY` per player, `grid:`, then exactly 128 rows of exactly 192 chars. Chars: '.' open, '#' blocked, 'F' ferrite (12000 each), 'w' water (blocked), 'h' hill (blocked), 'r' ruin (blocked), 'f' fence (blocked), 'B' bridge (OPEN to the sim - the pathable crossing). Any other char throws.

HARD CONSTRAINTS (mechanical, non-negotiable):
1. size 192 128; every grid row exactly 192 chars; exactly 128 rows.
2. Starts (12,20) and (179,107). Their midpoint is x=95.5, y=63.5 -> the fair symmetry is 180-deg ROTATION (x,y) -> (191-x, 127-y), which maps (12,20) <-> (179,107) exactly. EVERY terrain and ferrite feature must be placed as a rotation-symmetric pair under that map. This is the fairness invariant and it is machine-checkable.
3. Keep a 9x9 fully-open apron ('.') centred on each start so the 2x2 CY footprint and the MCV always fit.
4. Ferrite budget: 60 F cells (720,000 credits), i.e. 3x skirmish-02/03's 20 for ~4x the area - a deliberate slight tightening per cell of area to keep expansion contested. Lay them as 12 clusters of 5 in the (0,0),(1,0),(0,1),(2,1),(1,2) pattern used by skirmish-02, as 6 rotation-symmetric cluster pairs: 1 base + 2 near + 2 mid + 1 contested-centre per player.
5. Terrain density: target 8-10% blocked (matches skirmish-03's 8.9%). Below that the map reads as an empty field; above it the draw-call budget and pathing suffer.
6. Every F cell and every start apron cell must be reachable from both starts - no ferrite walled off behind an unbridged river.

DESIGN INTENT (this is why cheapModelSafe=false - the shape is a taste call, hand it to a designer or the game-designer agent): a river valley reading north-south at roughly x=94..97 as 'w', crossed by exactly three 'B' bridges at roughly y=20-24, y=60-68 and y=104-108. Three crossings on a 128-tall map is the classic tempo lever: it forces committed attack routes, makes bridge control the map's central question, and gives the defender something to hold that is not their own base. Hills frame each base, ruins litter the midfield, fences give light cover.

A WORKING REFERENCE GENERATOR (produces a valid, symmetric, loading map; verified in-sim) is at /private/tmp/claude-501/-Users-lgreene/3720c1c7-f32d-428a-9e1c-67ea0ec44f1a/scratchpad/mapscale-bench/gen_big_map.py - that path is EPHEMERAL scratch, so copy the script into tools/ and commit it alongside the .fmap (the repo already keeps map/asset generators under tools/). Its census on the reference run: 23342 '.', 448 'w', 64 'B', 384 'h', 210 'r', 68 'f', 60 'F' -> 1110 blocked = 4.5% density, which is BELOW the 8-10% target and must be raised before this ships.

Name the map in the fiction of the world (British spelling, no em/en dashes, and the word 'Cinder' must never appear per CLAUDE.md).

- Acceptance: MapData.Load('data/maps/skirmish-04.fmap') succeeds and reports Width=192, Height=128, Starts[0]=(12,20), Starts[1]=(179,107), Fields.Count=60. Machine-checkable symmetry: for every blocked cell (x,y), (191-x,127-y) is blocked; for every field (x,y), (191-x,127-y) is a field; and the multiset of Chebyshev distances from (12,20) to all 60 fields equals that from (179,107) to all 60 fields. Blocked.Count/(192*128) is between 0.08 and 0.10. A flood fill from each start over non-blocked cells (treating 'B' as open, 'w' as closed) reaches all 60 F cells and the other player's start. A 5000-tick AI-vs-AI match (BuildSkirmishWorld pattern, 8000 credits, CY at each start) completes with zero ticks over 8 ms and >=60 live entities. The map appears in the MainMenu THEATRE picker with no code change. sim goldens byte-identical (adding a map file changes no existing scenario).

#### MAP-06. Add a large-map perf gate scenario so the measured 192x128 headroom cannot silently regress

- Dimension: map scale and layout
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: false (adds one golden line, additive)
- Files: sim/Ferrostorm.Sim.Runner/Program.cs (Match/perf section near line 1415-1420); sim/golden-hashes.txt (one ADDED line)
- Spec:

The finding that 192x128 is safe is only true as long as nothing regresses it. Today the only perf gate is `movement` (Program.cs:1419-1420: 1000 ticks x 500 units, fail if >8 ms/tick) and it runs on an EMPTY 512x512 world with no terrain, no structures and no flow-field churn - so it would not catch a flow-field or fog regression at all.

Add a scenario `bigmap` alongside the existing ones, following the exact shape of ScenarioSkirmish (Program.cs:477):
- Load data/maps/skirmish-04.fmap (DEPENDS ON MAP-04; if that map is not merged yet, gate this ticket behind it).
- Build via MapData.BuildWorld(seed, players: 2); GrantCredits(0/1, 8000); SpawnConstructionYard at each start - identical to BuildSkirmishWorld but with the big map.
- Two SkirmishAI commanders, 5000 ticks, checkpoint every 250 ticks via cp?.Invoke, exactly as ScenarioSkirmish does.
- Return world.ComputeStateHash().

Register it in the `scenarios` collection so determinism/golden pick it up automatically.

In the Match/perf path add a hard gate mirroring line 1420, but measure the MAX tick and not just the mean - the flow-field storm is a spike, and a mean would hide it:

```csharp
Stopwatch per Step; track max and mean.
if (maxMs > 8.0) return Fail($"PERF GATE: bigmap worst tick {maxMs:F3} ms exceeds the 8 ms budget (TDD s6)");
```

IMPORTANT: exclude tick 0 from the max. Measured, tick 0 is 9.9-12.6 ms of JIT warm-up on EVERY scenario including the shipped 96x64 skirmish-01 - it is not a real spike and gating on it would fail spuriously. Start the max window at tick 1 and say so in a comment.

MEASURED BASELINE to expect (M4, Release): mean 0.040 ms, p50 0.018, p99 1.525, max 3.332 ms over ticks 1..4999. Budget 8. If an implementation lands far off this, something regressed.

touchesSim=false: this ADDS a scenario, it does not alter any existing sim behaviour, so every existing golden stays byte-identical. It does append ONE new line to sim/golden-hashes.txt (`bigmap 2026 0x...`), which is additive and is not a replay-compatibility break. Generate it with `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release golden 2026`.

- Acceptance: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 and prints a `bigmap` perf line with worst-tick under 8 ms. `determinism` double-run reports bigmap identical. sim/golden-hashes.txt gains exactly one line (`bigmap 2026 0x...`) and every pre-existing hash is byte-identical. Negative control: temporarily hack FlowFieldCache.Get to bypass its cache (always rebuild) and confirm the gate FAILS - proving the gate actually watches the flow-field cost. Revert the hack.

#### MAP-08. Record the measured map-size envelope in the TDD so the next map author has a number

- Dimension: map scale and layout
- Impact: LOW | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: docs/design/03-technical-design-document.md (near line 59, the perf budget section)
- Spec:

No design doc states a map size anywhere - not the TDD, not the GDD, not the style bible. 96x64 is convention, never a decision, so nobody knows what is safe. Record the measured envelope next to the existing perf budget line (TDD line 59: '600 active units + 200 structures at 60 fps on a GTX 1060-class GPU / 4-core CPU; sim tick under 8 ms at that load on one core').

Add a short subsection stating, as measurements with their date and hardware (M4, .NET 8 Release, Godot 4.7):
- Supported map sizes: 96x64 to 192x128. 192x128 is the tested ceiling; 256x192 is plausible but ungated.
- Sim cost is independent of map size: Step() with 500 units measures 0.741 ms/tick at 96x64 and 0.283 ms/tick at 192x128 (budget 8). Every per-tick system is O(entities). ScenarioMovement has run on a 512x512 world since Phase 1.
- The only map-size-dependent sim cost is FlowField.Build on a cache miss: 0.363 ms at 96x64, 1.548 ms at 192x128, linear in cells. World clears the whole flow cache on every structure placement and every structure death (World.cs:365,373), so worst-case ticks rebuild several fields at once: the 8 ms budget affords 20 rebuilds/tick at 96x64 but only 6 at 192x128. A real 2-player match measures p99 = 1.5 ms (about one build per tick), so this is headroom, not a limit - but it is the thing that breaks first if map size grows or player count rises.
- The client, not the sim, sets the ceiling: terrain draw calls scale with blocked-cell count (see MAP-01), and scatter density must be scaled by map area (MAP-03).
- Reference: a 5000-tick AI-vs-AI match on 192x128 measures mean 0.040 ms, p99 1.525 ms, max 3.332 ms, zero ticks over budget.

British spelling; no em dashes or en dashes (CLAUDE.md); restructure sentences rather than reaching for a dash.

- Acceptance: docs/design/03-technical-design-document.md contains an explicit supported map-size range and the flow-field rebuild figures, cross-referencing the MAP-06 `bigmap` gate as the thing that keeps the claim true. A reader can answer 'how big may a map be, and what breaks first?' from the TDD alone without reading any source file. No code changes; sim goldens byte-identical.

#### MAP-10. CONDITIONAL, DO NOT BUILD YET: guard the flow-field rebuild storm if it ever trips the gate

- Dimension: map scale and layout
- Impact: LOW | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/World.cs (lines 362-374, BlockFootprint/UnblockFootprint); sim/Ferrostorm.Sim/Map.cs (FlowFieldCache, lines 125-141)
- Spec:

DO NOT IMPLEMENT THIS UNTIL THE MAP-06 `bigmap` GATE ACTUALLY FAILS. It is recorded so the cliff is known and nobody is surprised by it, and because it is the thing that breaks first if map size or player count grows. Building it speculatively costs an ADR, a golden regen across every scenario that moves a unit, and real desync risk, to fix a spike that measurement says does not currently occur.

THE MEASURED CLIFF. World.BlockFootprint (World.cs:373) and UnblockFootprint (line 365) each call `_flow.Clear()`, discarding EVERY cached flow field on every structure placement and every structure death. The next tick, every distinct move target must rebuild from scratch at O(cells). Distinct rebuilds affordable within the 8 ms budget: 96x64 -> 20 (8.62 ms at 20); 160x120 -> 6 (8.08 ms); 192x128 -> 6 (10.12 ms at 6, 13.36 at 8, 34.75 at 20). So the same code that tolerates a 20-field storm at 96x64 tolerates only 5 at 192x128. A real 2-player AI match never approaches this (measured p99 1.5 ms = about one build/tick), which is why this is deferred. The risk surfaces at 4-8 players, or with a human issuing many distinct move orders across a base-building fight.

THE TWO HONEST OPTIONS, both touchesSim=true (ADR + Architect sign-off + full golden regeneration; every scenario that moves a unit changes hash):
- Option A - surgical invalidation. Instead of clearing everything, drop only fields whose routes could change. A footprint edit touches exactly 4 cells; a field is affected only if some route passes through or adjacent to them. Determining that cheaply and DETERMINISTICALLY is the hard part, and getting it wrong means units path through walls - a correctness bug, not a perf bug. High risk.
- Option B - amortised rebuilds. Cap distinct FlowField.Build calls per tick (e.g. 3) behind a deterministic FIFO queue keyed on target cell, ordered by cell index so pop order is platform-identical. Units awaiting a field hold their previous field (or hold position). Lower risk than A and bounded by construction, but it changes when units start moving, so it is squarely a behaviour change.

DO NOT reach for a third option that has already been tested and rejected: rewriting FlowField.Build with pooled scratch buffers and a packed long[] heap is NOT faster. Measured 0.72x / 0.70x / 1.28x across 96x64 / 160x120 / 192x128 (mostly slower, within noise), while reproducing the shipped routes bit-identically across 284,628 checked cells. The cost is the Dijkstra expansion, not allocation. Any fix must reduce the NUMBER of builds, not the cost of one.

If Option A is taken, add an eviction cap to FlowFieldCache at the same time: it currently never evicts (Map.cs:127 `_fields`), and today only the aggressive global Clear keeps it small. Make invalidation surgical and the cache grows unbounded at 24 KB per field at 192x128.

cheapModelSafe=false: choosing between A and B, and picking the per-tick cap, is an architecture decision with desync consequences. Architect only.

- Acceptance: Precondition for starting work: the MAP-06 `bigmap` gate reports a worst tick over 8 ms, with the overage attributable to FlowField.Build (profile it, do not assume). Then: an ADR in docs/adr/ records the chosen option and its determinism argument; the `bigmap` worst tick returns under 8 ms; `determinism` double-run is identical for every scenario on Windows and Linux; every golden in sim/golden-hashes.txt is regenerated in one commit alongside the ADR; the `pathing` scenario still settles all 500 units within 22 cells of the target with none stranded across the wall (Program.cs:87-98), proving routes stayed correct and no unit paths through a wall.

### A.5 The biome ground

#### C-02. Biome palette system: three named biomes with real hue separation, superseding doc 20 W5-02

- Dimension: colour, palette and texture richness
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/BattlefieldView.cs, game/scripts/SkirmishLive.cs, game/scripts/ReplayTheater.cs, docs/design/20-visual-aaa-roadmap.md
- Spec:

This ticket SUPERSEDES doc 20 W5-02. Do not implement W5-02 as written: its ground values are all within 15% of neutral grey (it re-specs the current mud in three flavours), and its integration note routes FogCol values authored for a pre-W1-05 fog-colour property into VolumetricFogAlbedo, which would set fog albedo to ~0.05 and extinguish the volumetric fog. Also reassigning the biome-to-map mapping: doc 20 gives 'The Crossing' to skirmish-01 (which has zero water cells) and the warm rust 'Cauldron' to skirmish-03 (which has 360 water cells and 24 bridge cells); that is backwards. Corrected mapping is below.

STEP 0 (MANDATORY MEASUREMENT, do this first and record the number in the ledger). Take an offscreen capture at the terrain-v2.png viewpoint and measure the mean luminance of a 20x20 px patch of open ground. If it lands in 90-130/255, Godot 4.7 is using the shader's source_color defaults verbatim (as linear) and you pass Colors through SetShaderParameter unchanged. If it lands below ~40/255, Godot is applying an sRGB-to-linear conversion to the source_color defaults, and every value in the table below must be pre-converted with `Mathf.Pow((c + 0.055f) / 1.055f, 2.4f)` per channel before being passed. Record which branch is live in a code comment above the Biome record. This ambiguity is the single most likely cause of 'the whole frame is dark' and must not be assumed away.

STEP 1. Add to BattlefieldView:

```csharp
public sealed record Biome(string Name, Color DustDark, Color DustLight, Color RockDark, Color RockLight, Color GravelDark, Color GravelLight, Color Ochre, Color RidgeDark, Color RidgeLight, Color Ambient, Color FogAlbedo, Color SkyTop, Color SkyHorizon);
```

STEP 2. Three static Biome instances. Every value is linear, and each biome carries a mandatory hue spread: the dust layer is warm (R/B >= 1.35) and the rock layer is cool (R/B <= 0.80) so the splat mask paints COLOUR, not value.

- ASHFIELD (default; skirmish-01, all missions, ReplayTheater): DustDark (0.105,0.088,0.068), DustLight (0.185,0.160,0.125), RockDark (0.072,0.082,0.100), RockLight (0.140,0.158,0.190), GravelDark (0.058,0.062,0.058), GravelLight (0.125,0.132,0.118), Ochre (0.240,0.155,0.055), RidgeDark (0.130,0.150,0.180), RidgeLight (0.235,0.255,0.290), Ambient (0.34,0.38,0.46), FogAlbedo (0.55,0.62,0.75), SkyTop (0.015,0.020,0.032), SkyHorizon (0.100,0.085,0.075).
- THE CAULDRON (skirmish-02; barren, most ridge mass, no water - warm rust ash): DustDark (0.125,0.092,0.062), DustLight (0.205,0.155,0.105), RockDark (0.098,0.086,0.078), RockLight (0.175,0.152,0.130), GravelDark (0.072,0.058,0.048), GravelLight (0.140,0.115,0.090), Ochre (0.280,0.170,0.050), RidgeDark (0.175,0.150,0.125), RidgeLight (0.295,0.255,0.215), Ambient (0.44,0.40,0.36), FogAlbedo (0.78,0.66,0.52), SkyTop (0.030,0.022,0.018), SkyHorizon (0.150,0.095,0.060).
- THE FLOODPLAIN (skirmish-03; 360 water, 74 hills, 54 fences - cool wet banks): DustDark (0.088,0.090,0.078), DustLight (0.158,0.162,0.130), RockDark (0.068,0.080,0.102), RockLight (0.132,0.152,0.192), GravelDark (0.052,0.060,0.070), GravelLight (0.108,0.124,0.148), Ochre (0.200,0.160,0.070), RidgeDark (0.120,0.145,0.175), RidgeLight (0.220,0.250,0.290), Ambient (0.33,0.39,0.48), FogAlbedo (0.48,0.60,0.82), SkyTop (0.012,0.018,0.038), SkyHorizon (0.070,0.080,0.115).

STEP 3. `public static Biome ForMap(string? path)`: if path is null return Ashfield; take System.IO.Path.GetFileName(path); if it contains "skirmish-02" return Cauldron; if it contains "skirmish-03" return Floodplain; otherwise return Ashfield. Filename keying only - no sim dependency (C-03 adds an optional map-header key on top).

STEP 4. Signatures: `BuildEnvironment(Node3D parent, Biome? biome = null)` and `BuildTerrain(Node3D parent, int w, int h, IEnumerable<(int,int)> blockedCells, IReadOnlyDictionary<(int,int),char>? visual = null, Biome? biome = null)`. Both start with `biome ??= Ashfield;` so ReplayTheater compiles unchanged.

STEP 5. BuildEnvironment wiring: AmbientLightColor = biome.Ambient; VolumetricFogAlbedo = biome.FogAlbedo; SkyTopColor = biome.SkyTop; SkyHorizonColor = biome.SkyHorizon; GroundBottomColor = biome.SkyTop * 0.6f; GroundHorizonColor = biome.SkyHorizon * 0.85f. (These ratios reproduce the current relationships: shipped SkyTop (0.015,0.020,0.032) vs GroundBottom (0.01,0.012,0.016), and SkyHorizon (0.10,0.085,0.075) vs GroundHorizon (0.09,0.075,0.065).) Leave AmbientLightEnergy, SkyCurve, all Ssao/Ssil/Ssr/Glow and Adjustment values untouched.

STEP 6. BuildTerrain wiring: after the three existing SetShaderParameter calls at lines 373-375 add `groundShader.SetShaderParameter("dust_dark", biome.DustDark); groundShader.SetShaderParameter("dust_light", biome.DustLight); groundShader.SetShaderParameter("rock_dark", biome.RockDark); groundShader.SetShaderParameter("rock_light", biome.RockLight); groundShader.SetShaderParameter("gravel_dark", biome.GravelDark); groundShader.SetShaderParameter("gravel_light", biome.GravelLight);` (plus `ochre` once C-04 has landed). Feed biome.RidgeDark/RidgeLight into the ridgeMat GrainTex dark/light arguments (line 434-436). Derive the other terrain-class materials from the biome so nothing stays hard-coded: cliffBaseMat dark/light = biome.RidgeDark * 0.75f / biome.RidgeLight * 0.70f; hillMat dark/light = biome.DustDark * 1.25f / biome.DustLight * 1.30f (hills are earth, not stone); ruinMat dark/light = biome.Ochre * 0.62f / biome.Ochre * 1.05f (ruins are the warmest thing on the field, keep it); rubbleMat and plate GrainTex dark/light = biome.RidgeDark * 0.90f / biome.RidgeLight * 0.85f. Leave water, foam, deck and fence materials alone (C-04 and C-09 do not depend on them).

STEP 7. SkirmishLive._Ready: after mapPath is resolved, `var biome = BattlefieldView.ForMap(MatchConfig.MissionPath ?? MatchConfig.MapPath);` and pass it to both BuildEnvironment (line 193) and BuildTerrain (line 194). ReplayTheater passes nothing (defaults to Ashfield).

STEP 8. In docs/design/20-visual-aaa-roadmap.md, mark W5-02 as SUPERSEDED BY C-02 with a one-line reason (values lacked hue separation; FogCol/FogAlbedo property mismatch; biome-to-map assignment inverted) and leave the original text in place for the record.

- Acceptance: dotnet build game/Ferrostorm.Game.csproj clean. The Step 0 measurement is recorded in a code comment and in the ledger. Three offscreen captures, one per skirmish map, same relative viewpoint: (a) mean frame RGB of skirmish-02 has R > B by at least 6/255 and skirmish-03 has B > R by at least 6/255; (b) for each capture, sample a 40x40 px open-ground patch and assert the mean R/B ratio differs between skirmish-02 and skirmish-03 by at least 0.25; (c) mean open-ground luminance stays inside 90-135/255 on all three; (d) VolumetricFogAlbedo is verifiably non-black - the far map edge in each capture is hazed, not hard-cut, and its luminance is at least 12/255 above the sky top row. ReplayTheater capture is pixel-comparable to pre-ticket (defaults to Ashfield, whose values are the only ones allowed to differ from the current shipped ground, and only in hue). sim golden-hash battery exits 0.

#### C-03. Optional 'biome' map-header key (hash-neutral sim parser addition)

- Dimension: colour, palette and texture richness
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false (edits a /sim file; hash-neutral by construction)
- Files: sim/Ferrostorm.Sim/MapLoader.cs, game/scripts/BattlefieldView.cs, data/maps/skirmish-02.fmap, data/maps/skirmish-03.fmap
- Spec:

Depends on C-02. This edits a file under /sim but does NOT change sim behaviour and does NOT regenerate golden hashes - the new field is parsed, stored on MapData, read only by the client, and never reaches World.BuildWorld. This is exactly the existing precedent of MapData.Visual (MapLoader.cs line 41-42: 'Client-only terrain dressing per cell; the sim never reads this'). Verify by inspection that BuildWorld (lines 150-190) does not reference the new property, then prove it with the golden battery.

1. In MapData add `/// <summary>Client-only biome key; the sim never reads this.</summary>` + `public string? Biome { get; init; }`.
2. In Parse, declare `string? biome = null;` alongside `bool shortGame = true;` (line 55).
3. In the header switch (lines 62-80) add before `default:`: `case "biome": biome = p[1]; break;`. This is required because the parser currently throws FormatException on any unknown header (line 79), so a biome line would hard-fail today. No test asserts that throw (verified: the only occurrence of the string 'unknown header' in /sim is the throw itself).
4. Add `Biome = biome,` to the returned MapData initialiser (lines 139-143).
5. Keep the format-doc comment block at the top of MapLoader.cs accurate: add `///   biome NAME               (optional; client-only, sim ignores it)` under the `start` line.
6. Do NOT use float, double, System.Random, wall-clock or locale APIs - a string assignment only, so the determinism grep stays green.
7. In BattlefieldView, change ForMap into `public static Biome ForMap(string? path, string? key = null)`: if key is non-null, match case-insensitively against "ashfield", "cauldron", "floodplain" and return that biome, throwing nothing on an unknown key (fall through to the filename logic). Filename logic stays as C-02 specced it, so behaviour is unchanged for maps with no header.
8. SkirmishLive passes `map.Biome` as the key.
9. Add `biome cauldron` to data/maps/skirmish-02.fmap and `biome floodplain` to data/maps/skirmish-03.fmap, each on its own line after the `start` lines and before `grid:`. Leave skirmish-01 and the missions with no header (they default to Ashfield). Do not touch any grid row.

- Acceptance: dotnet build sim/Ferrostorm.Sim.Runner -c Release clean; dotnet run --project sim/Ferrostorm.Sim.Runner -c Release exits 0 with EVERY hash in sim/golden-hashes.txt unchanged (this is the whole point of the ticket - a single hash delta means the field leaked into World and the ticket is wrong). The CI sim purity grep stays green. game/ builds clean. Loading skirmish-02 with the header present produces the Cauldron biome and loading it with the header line deleted still produces Cauldron via the filename fallback (both verifiable from a capture or a debug print).

#### C-04. Ground splat shader: low-frequency hue drift, ferrite-ochre patches, wider value and roughness range

- Dimension: colour, palette and texture richness
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/shaders/ground_splat.gdshader, game/scripts/BattlefieldView.cs
- Spec:

Depends on C-02 (the ochre uniform comes from the Biome record). The current shader produces one hue across all 96x64 cells; the only large-scale variation is `alb *= mix(0.85, 1.12, ...)` on line 25, which is pure value. Add hue variation at a scale bigger than a unit and smaller than the map, plus a warm ferrite-bearing earth layer that gives the bible's 'ferrite gold is the light of this world' something to say across more than 0.05% of the ground.

1. Add uniforms after line 10: `uniform vec3 ochre : source_color = vec3(0.240, 0.155, 0.055);` `uniform float ore_amount = 0.55;` `uniform float hue_drift = 0.5;` `uniform float base_gain = 1.06;`.
2. In fragment(), after line 24 (`alb = mix(alb, gravel, w_gravel);`) insert:

```glsl
// Large-scale hue drift: warm and cool zones ~10 cells across, so the
// field reads as terrain rather than one painted tone.
float d = texture(noise_b, wpos.xz * 0.011).r;
vec3 warm = alb * vec3(1.30, 1.02, 0.74);
vec3 cool = alb * vec3(0.80, 0.96, 1.26);
alb = mix(alb, mix(cool, warm, smoothstep(0.30, 0.70, d)), hue_drift);
// Ferrite-bearing earth: broad ochre stains, the one warm accent the
// palette law allows, finally present at map scale.
float ore = smoothstep(0.72, 0.88, texture(noise_a, wpos.xz * 0.006).r);
alb = mix(alb, ochre * (0.65 + 0.75 * g1), ore * ore_amount);
```

3. Replace line 25 with `alb *= base_gain * mix(0.82, 1.22, texture(noise_b, wpos.xz * 0.003).r);` (wider value range, modest gain).
4. Replace line 27 with `ROUGHNESS = 0.96 - 0.05 * w_gravel - 0.16 * ore;` so ochre patches carry a faint sheen the surrounding dust does not - texture richness that costs one multiply.
5. In BattlefieldView.BuildTerrain, add `groundShader.SetShaderParameter("ochre", biome.Ochre);` alongside the C-02 uniform block. Leave hue_drift, ore_amount and base_gain on their shader defaults (they are the named knobs for the W5-05 taste pass; do not tune them here).
6. Sampling-scale note for the implementer, do not change it: the noise textures are 512px seamless, so `wpos.xz * 0.011` tiles once per ~91 world units on a 96-wide map, and FastNoiseLite frequency 0.02 across 512px yields roughly 10 blobs per tile - about one hue zone per 10 cells, which is the intended scale. `* 0.006` gives ore patches roughly twice that size.
7. The mesa-cap boxes at BuildTerrain line 697 share groundShader, so ridge tops inherit the same drift automatically. Do not give them a separate material.

- Acceptance: game/ builds clean and the shader compiles with no errors in the Godot output. Offscreen capture at the terrain-v2.png viewpoint, fully zoomed out so most of the map is in frame: (a) sample the R/B ratio of open ground at 12 points spread across the map and assert max ratio minus min ratio >= 0.30 (pre-change this is < 0.05 - the ground is one tone); (b) at least 4% and no more than 22% of open-ground pixels have hue in the 25-50 degree band (the ochre patches) where pre-change it is 0%; (c) mean open-ground luminance stays inside 90-135/255; (d) frame time at 1600x900 within 0.5 ms of the pre-change baseline. sim golden-hash battery exits 0.

---
## Wave B: the wall system

The largest coherent sim ticket set in the project, and the best-constructed: DEF-03, DEF-04 and DEF-05 are built so that all twenty-three existing golden hashes stay byte-identical, and that is the acceptance criterion rather than an aspiration. They still touch the sim and still need the ADR and Architect sign-off, but the law's "replay-compatibility break" becomes a machine-checkable non-event.

Landing order is not negotiable: **DEF-01** (independent quick win, ships today) -> **DEF-02** (ADR-005, ratified before anything else merges) -> **DEF-03** -> **DEF-04 and DEF-05 in one batch** -> **DEF-06** -> **DEF-07** -> **DEF-08** -> **DEF-09** -> **DEF-17** -> **DEF-14**. Shipping DEF-04 without DEF-05 ships a bug: a walled base makes the flow field return -1 for attackers across the whole map and the AI's waves halt at their own base.

Gates are deferred. The reason is recorded in DEF-02 clause 6 and is architectural rather than lazy.

#### TICKET-P5-DEF-01. Placement range-preview rings and correctly sized ghost/selection rings

- Dimension: defensive structures and barriers
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs; game/scripts/BattlefieldView.cs
- Spec:

THE S-EFFORT QUICK WIN. Pure presentation; no sim file is touched.

1. In BattlefieldView.cs add `public static MeshInstance3D MakeRangeRing(float radius, Color c)` returning a MeshInstance3D with `Mesh = new TorusMesh { InnerRadius = radius - 0.08f, OuterRadius = radius }`, `Position = new Vector3(0, 0.04f, 0)`, and a StandardMaterial3D with `AlbedoColor = c`, `EmissionEnabled = true`, `Emission = c`, `EmissionEnergyMultiplier = 1.1f`, `Transparency = BaseMaterial3D.TransparencyEnum.Alpha`, `ShadingMode = ShadingModeEnum.Unshaded`. Use ferrite gold `new Color(0.79f, 0.63f, 0.36f, 0.35f)` for the ghost's own ring and a dimmer `new Color(0.79f, 0.63f, 0.36f, 0.18f)` for existing structures' rings.
2. In SkirmishLive.cs add `private static int WeaponOfStruct(int structType) => structType == 5 ? 4 : 0;` with a comment stating this mirrors the WeaponId hardcoded in World.SpawnTurret and must be updated alongside it.
3. Add `private static float RangeOf(int weaponId) => weaponId == 0 ? 0f : (float)(Weapons.Get(weaponId).Range.Raw / 4294967296.0);` - Fix64.Raw is public and FracBits is 32, so this is the same conversion SnapshotInterpolator.ToDouble uses.
4. Add fields `private Node3D _ghostRing = null!; private readonly List<MeshInstance3D> _coverageRings = new();`. In _Ready, after the existing `_ghost` creation at line 222, create `_ghostRing` via MakeRangeRing(1f, ghostColour), add it as a CHILD of _ghost, and set `_ghostRing.Visible = false`.
5. In EnterPlacement(int structType) (line 271): set `_ghost.Mesh = new BoxMesh { Size = new Vector3(f, 0.5f, f) }` where `float f = World.FootprintOf(structType)` if DEF-03 has landed, else 2f; set `float r = RangeOf(WeaponOfStruct(structType)); _ghostRing.Visible = r > 0; if (r > 0) _ghostRing.Scale = new Vector3(r, 1, r);` (the ring mesh is built at radius 1 so Scale is the radius). Then, for every entity in _view with `v.Alive && v.PlayerId == 0 && !Mobile(v.Kind)` whose sim WeaponId (`_world.Entities[v.Id].WeaponId`) is non-zero, instantiate a coverage ring at that entity's ground position scaled to RangeOf(that WeaponId), add it to the scene and to _coverageRings. This is the coverage-gap read that makes turret placement a decision instead of a guess.
6. On leaving placement mode - both the Escape branch at line 929-931 and the successful commit in PlaceAtCell at line 1020 - call a new `private void ExitPlacement()` that sets `_placingType = 0; _ghost.Visible = false;`, frees every node in _coverageRings via QueueFree and clears the list. Replace the duplicated two-line teardown at both sites with the call.
7. Ghost position: the existing line 550 `_ghost.Position = new Vector3(ax + 1f, 0.25f, ay + 1f)` hardcodes the 2x2 centre offset of +1. Change to `float half = f / 2f; _ghost.Position = new Vector3(ax + half, 0.25f, ay + half);` so a 1x1 ghost sits at cell centre.
8. Fix the selection ring size at line 991: replace the hardcoded `BattlefieldView.AddSelRing(n, 1.5f)` with `BattlefieldView.AddSelRing(n, _latest.TryGetValue(hit, out var hv) && hv.Kind == EntityKind.Turret ? 1.2f : 1.5f)` - and if DEF-03 has landed, derive from `World.FootprintOf(_world.Entities[hit].StructType) * 0.75f` instead.
9. The per-frame material allocation at lines 552-556 rebuilds a StandardMaterial3D EVERY frame while placing; hoist the two materials (valid green `new Color(0.3f, 0.9f, 0.4f, 0.4f)`, invalid red `new Color(0.9f, 0.25f, 0.2f, 0.4f)`) to static readonly fields and assign the reference instead.

This ships independently of every other ticket in this set and improves the existing turret today.

- Acceptance: `dotnet build game/Ferrostorm.Game.csproj -c Release` exits 0. Godot headless (`godot --headless --audio-driver Dummy`) runs the Battle3D scene 1000 frames with no error. A scripted headless assertion: after calling EnterPlacement(5), the node tree under the ghost contains a visible child named for the range ring whose Scale.X equals 5.0 within 0.001 (Weapons.TurretGun.Range is Fix64.FromInt(5)); after EnterPlacement(1) (power plant, weapon 0) that ring is not visible. After ExitPlacement, GetChildren of the scene contains zero coverage-ring nodes. Sim gate `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 and `golden 2026` output is byte-identical to sim/golden-hashes.txt (this ticket touches no sim file - assert it by `git diff --stat sim/` being empty).

#### TICKET-P5-DEF-02. ADR-005: variable structure footprints and barrier placement rules

- Dimension: defensive structures and barriers
- Impact: TRANSFORMATIVE | Effort: S | cheapModelSafe: false | touchesSim: false (it is the gate for the ones that do)
- Files: docs/adr/ADR-005-footprints-and-barriers.md (new); docs/adr/ADR-open-queue.md
- Spec:

TASTE AND AUTHORITY REQUIRED - THIS IS THE GATE FOR DEF-03/04/05 AND MUST BE RATIFIED BY THE ARCHITECT AND LUKE BEFORE ANY OF THEM MERGE. ADR-005 is already reserved for exactly this in docs/adr/ADR-open-queue.md line 6 ("Tile size, grid resolution, footprint rules (decide before first map)"); this ticket closes that open item rather than minting a new number.

Author docs/adr/ADR-005-footprints-and-barriers.md from docs/adr/ADR-000-template.md, British spelling, no em or en dashes anywhere.

Context section must state: the sim assumes every structure is 2x2 (World.cs:354 `FootprintSize`, World.cs:376 `FootprintCentre`, and the anchor-recovery idiom `Map.CellOf(e.X) - 1` at World.cs:699, 768, 1081, 1500 and SkirmishAI.cs:385); the classic-90s-RTS base-building idiom requires cheap 1x1 chained barrier segments; the GDD already commits to "strict classic adjacency" (GDD Q2, RESOLVED) and to "artillery beats static defence" (GDD s6 line 53) and to a Defence sidebar tab (GDD s7 line 86); no barrier entity of any kind exists today.

Decision section must ratify, each as a numbered clause:
1. Footprint size becomes a per-structure-type property `World.FootprintOf(int structType)` returning 2 for types 1-8 and 1 for barrier types, with the entity position remaining the footprint centre and the anchor recovered as `Map.CellOf(X) - (size - 1)`; the const `FootprintSize = 2` is retained as the default so no existing call site changes meaning.
2. Barriers are structures for blocking, selling, repairing and damage, but are EXCLUDED from the victory test (World.cs:1553), from engineer capture (World.cs:921), and from combat auto-acquisition (World.cs:1000).
3. Barriers are bought per segment at placement time with no Construction Yard ready slot and no build time (BuildTicks 0, which World.cs:661 already refuses to queue), reviving the upfront-cost model that World.cs:339 documents as the pre-SIM-05 behaviour.
4. Barriers project a build radius of 2 for OTHER BARRIERS ONLY and never anchor a non-barrier structure, so a player may crawl a wall outward at 2 cells and 100 credits a step but can never crawl a factory to the enemy base.
5. A per-player barrier cap of 80 segments, justified against the TDD s6 ratified budget of "600 active units + 200 structures" (03-technical-design-document.md:59): 2 players x 80 barriers + ~16 real buildings each = ~192 structures, inside budget.
6. Gates are DEFERRED, with the blocker recorded verbatim: passability is one global grid (Map.cs:7) and FlowFieldCache's only invalidation is Clear() (Map.cs:140), so a per-player-passable or auto-opening gate requires either per-player flow fields or an incremental flow repair, and neither exists.

Consequences section must state the golden-hash position explicitly: clauses 1-5 are constructed to be behaviour-neutral in the absence of barriers (append-only EntityKind values, no new Entity field, `FromFraction(2,2) == Fix64.One` so size-2 arithmetic is bit-identical), and therefore ALL 23 existing golden hashes must remain byte-identical - this is the acceptance criterion of DEF-03/04/05, not an aspiration.

Finally, update docs/adr/ADR-open-queue.md: mark ADR-005 RESOLVED with a pointer to the new file, and note that the queue's ADR-004 line ("Lua sandbox") is stale because docs/adr/ADR-004-engine-strategy.md occupies that number.

End with the standard footer: Changed / Assumed / Needed next (from whom).

**Doc 22 integration note:** ADR-005 must also fix the final struct-type numbering across the whole defence roster in one place, because DEF-03 reserves 9 and 10 for wall and gate while DEF-12's emplacement and DEF-13b's bastion both want numbers in that range. A footprint mismatch between GetStructureType and FootprintOf is silent and fatal. Recommended: 9 wall, 10 emplacement, 11 bastion, 12 gate (reserved, unbuilt), with FootprintOf returning 1 for 9 and 12 only.

- Acceptance: docs/adr/ADR-005-footprints-and-barriers.md exists and follows every heading of ADR-000-template.md. Status line reads `Ratified` with Luke and a date, or the dependent tickets stay blocked. `grep -nP '[\x{2014}\x{2013}]' docs/adr/ADR-005-footprints-and-barriers.md` returns nothing (no em or en dashes, CLAUDE.md absolute rule). NOTE: the source ticket wrote this grep with the literal characters in the bracket expression; it is rendered here as a PCRE codepoint class because this document may not itself contain them. The two forms are equivalent, and `grep -nP` is required for the `\x{...}` escape. `grep -nE '\b(Command & Conquer|C&C|Red Alert|Tiberium|GDI|Nod|Westwood|EA)\b' docs/adr/ADR-005-footprints-and-barriers.md` returns nothing (Legal absolute rule; the approved formulation is "inspired by the classic RTS games of the 90s"). docs/adr/ADR-open-queue.md no longer lists ADR-005 as open. Every one of the six decision clauses is numbered and present. Producer and Architect sign-off recorded in the file.

#### TICKET-P5-DEF-03. Sim: variable footprint support with provably zero behaviour change

- Dimension: defensive structures and barriers
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (hash-identical; the goldens are the proof)**
- Files: sim/Ferrostorm.Sim/World.cs; sim/Ferrostorm.Sim/SkirmishAI.cs
- Spec:

TOUCHES SIM: this edits deterministic sim code and requires Architect sign-off per the project law, BUT it is constructed so that ALL 23 GOLDEN HASHES MUST REMAIN BYTE-IDENTICAL - the diff is a pure refactor and the goldens are the proof. Blocked on DEF-02 (ADR-005) being Ratified.

Do not add any field to `struct Entity` - the state hash (World.cs:1577-1596) and the save format (World.Serialization.cs:135-175) both enumerate fields explicitly and any addition regenerates goldens.

1. In World.cs, immediately after `public const int FootprintSize = 2;` (line 354), add: `/// <summary>Cells per side of a structure type's square footprint (ADR-005). Barriers are 1x1; everything else is the 2x2 default.</summary>` then `public static int FootprintOf(int structType) => structType switch { 9 => 1, 10 => 1, _ => 2 };` (9 = wall, 10 = gate, both reserved here and populated by DEF-04/DEF-09).
2. Add: `/// <summary>Recover a structure's footprint anchor from its centre. Exact for both sizes: a 2x2 centre is anchor+1 so CellOf gives anchor+1, a 1x1 centre is anchor+0.5 so CellOf gives anchor.</summary>` then `public static int AnchorOf(Fix64 centre, int structType) => Map.CellOf(centre) - (FootprintOf(structType) - 1);`.
3. Change `private void UnblockFootprint(int ax, int ay)` (line 360) to `private void UnblockFootprint(int ax, int ay, int size)` and both loop bounds from `FootprintSize` to `size`. Change `private void BlockFootprint(int ax, int ay)` (line 368) the same way.
4. Change `private static Fix64 FootprintCentre(int anchor)` (line 376) to `private static Fix64 FootprintCentre(int anchor, int size) => Fix64.FromInt(anchor) + Fix64.FromFraction(size, 2);`. THIS IS BIT-IDENTICAL FOR size==2: Fix64.FromFraction(2,2) computes ((Int128)2 << 32)/2 == 1L<<32 == Fix64.One, and FromInt(a).Raw + One.Raw == (long)(a+1) << 32 == FromInt(a+1).Raw. Do not substitute any other formula.
5. Update all eight Spawn* methods (World.cs lines 250-261, 378-389, 391-402, 405-417, 422-433, 436-447, 449-460, 462-473): each currently calls `BlockFootprint(ax, ay)` then `Fix64 x = FootprintCentre(ax), y = FootprintCentre(ay);`. Replace with `BlockFootprint(ax, ay, 2); Fix64 x = FootprintCentre(ax, 2), y = FootprintCentre(ay, 2);` - literal 2, because every one of these eight is a 2x2 type.
6. Replace all four in-file anchor-recovery sites with AnchorOf: World.cs:699 becomes `UnblockFootprint(AnchorOf(e.X, e.StructType), AnchorOf(e.Y, e.StructType), FootprintOf(e.StructType));`; World.cs:1081 and World.cs:1500 become the same expression on `t`; World.cs:768 becomes `int oax = AnchorOf(o.X, o.StructType), oay = AnchorOf(o.Y, o.StructType);`. Every one of these is guarded by an `IsStructure` test, and every Spawn* method sets StructType, so StructType is always valid at these sites.
7. SkirmishAI.cs:385 becomes `int oax = World.AnchorOf(s.X, s.StructType), oay = World.AnchorOf(s.Y, s.StructType);` - it is already filtered to CY/PowerPlant/Factory/Refinery at line 384, all 2x2, so this is identity.
8. Change the public signature `public bool ValidPlacement(int player, int ax, int ay)` (line 749) to `public bool ValidPlacement(int player, int ax, int ay, int structType = 0)`; inside, add `int size = FootprintOf(structType);` and replace `FootprintSize` with `size` at lines 751-752 and 762. FootprintOf(0) hits the `_ => 2` arm, so all six existing callers (SkirmishLive.cs:551, 1017, 1036; SkirmishAI.cs:392; Program.cs:383, 384, 398, 446) are bit-identical without edit.
9. Leave `ValidFoundation` (line 725) alone: only MCV deploy calls it and a Construction Yard is always 2x2. Add a one-line comment saying so.
10. Update the stale comment block at World.cs:336-340 to describe the per-type footprint and to keep its (still accurate) note that the upfront-cost path is what barriers will use.

Determinism rules still bind: no float, no double, no System.Random, no Godot, no wall-clock, no unordered iteration - the CI grep at .github/workflows/determinism.yml enforces the first four.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- golden 2026 > got.txt; grep -v '^#' sim/golden-hashes.txt > want.txt; diff got.txt want.txt` produces NO output - all 23 hashes byte-identical. sim/golden-hashes.txt is UNMODIFIED in the diff (`git diff --exit-code sim/golden-hashes.txt` returns 0); a changed hash means the refactor was not behaviour-neutral and must be reworked, not blessed. Full gate `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 (selftest + determinism + match + lan + lanchaos + spectate + replay + saveload + campaignsave + balance). The CI purity grep passes: `grep -rnE '\b(float|double|System\.Random|Godot)\b' sim/Ferrostorm.Sim/` returns nothing. A new unit assertion in the runner's selftest: `World.AnchorOf(Fix64.FromInt(9), 4) == 8` (2x2 centred at 9 anchors at 8) and `World.AnchorOf(Fix64.FromInt(8) + Fix64.Half, 9) == 8` (1x1 centred at 8.5 anchors at 8), and `FootprintCentre`-equivalence checked as `(Fix64.FromInt(8) + Fix64.FromFraction(2,2)).Raw == Fix64.FromInt(9).Raw`. `grep -rn 'CellOf(.*) - 1' sim/` returns nothing (every anchor-recovery site migrated). Architect sign-off recorded.

#### TICKET-P5-DEF-04. Sim: wall segment - the barrier entity, upfront-pay placement, cap, and the three exclusions

- Dimension: defensive structures and barriers
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: true | **touchesSim: TRUE (hash-identical by construction)**
- Files: sim/Ferrostorm.Sim/World.cs; sim/Ferrostorm.Sim/MapLoader.cs
- Spec:

TOUCHES SIM: new entity kind and new reachable behaviour, so Architect sign-off is required per the project law. It is nonetheless designed so the 23 existing goldens stay byte-identical, because no existing scenario can place a wall and every new branch is gated on the new kind. Blocked on DEF-02 (Ratified) and DEF-03 (merged). DEF-05 must merge in the same batch or attack-move is broken against any walled base - do not ship DEF-04 alone.

1. EntityKind (World.cs:39): APPEND `Wall = 11` at the end. NEVER renumber an existing value - the hash stores `(int)e.Kind` (World.cs:1580) and the save writes `(byte)e.Kind` (World.Serialization.cs:137), so appending is invisible to both for existing kinds.
2. GetStructureType (World.cs:342-353): add `9 => new StructureTypeDef(100, EntityKind.Wall, 0),` before the `_ => default` arm. BuildTicks 0 means World.cs:661 (`if (bd.Cost <= 0 || bd.BuildTicks <= 0) break;`) already refuses to queue it at a Construction Yard, exactly as it already refuses type 4. Add no other type here.
3. Add two predicates next to IsStructure (World.cs:719): extend IsStructure's kind list with `or EntityKind.Wall` (walls must block, sell, repair and take damage as structures), and add `private static bool IsBarrier(EntityKind k) => k is EntityKind.Wall;` (DEF-09 later appends `or EntityKind.Gate`).
4. Add `public const int BarrierBuildRadius = 2;` and `public const int MaxBarriersPerPlayer = 80;` beside BuildRadius (World.cs:357). The 80 is derived from the TDD s6 ratified budget of 200 structures (03-technical-design-document.md:59): 2 x 80 + ~32 real buildings = ~192.
5. Add SpawnWall modelled exactly on SpawnTurret (World.cs:449-460):

```csharp
public int SpawnWall(int player, int ax, int ay) { BlockFootprint(ax, ay, 1); Fix64 x = FootprintCentre(ax, 1), y = FootprintCentre(ay, 1); return Add(new Entity { Id = _entities.Count, Alive = true, PlayerId = player, Kind = EntityKind.Wall, X = x, Y = y, TargetX = x, TargetY = y, StructType = 9, Hp = 500, MaxHp = 500, Armour = ArmourClass.Structure, WeaponId = 0, ExplicitTarget = -1, Sight = Fix64.Zero, FieldId = -1, RefineryId = -1, PowerDraw = 0 }); }
```

Sight = Fix64.Zero is deliberate and load-bearing: FogSystem (World.cs:1512) skips zero-sight entities, so 80 walls per player cost nothing in the fog pass and grant no vision.

6. Add `private int CountBarriers(int player) { int n = 0; for (int i = 0; i < _entities.Count; i++) { var e = _entities[i]; if (e.Alive && e.PlayerId == player && IsBarrier(e.Kind)) n++; } return n; }`.
7. Rework the PlaceStructure case (World.cs:617-656). After `var sd = GetStructureType(c.AuxId); if (sd.Cost <= 0) break;` insert `bool barrier = sd.Kind == EntityKind.Wall; int ax = Map.CellOf(c.X), ay = Map.CellOf(c.Y);` then branch: if `barrier`, require `_credits[c.PlayerId] >= sd.Cost` else break, and require `CountBarriers(c.PlayerId) < MaxBarriersPerPlayer` else break, and SKIP the readyCy scan entirely; if not `barrier`, run the existing readyCy scan and `if (readyCy < 0) break;` unchanged. Then `if (!ValidPlacement(c.PlayerId, ax, ay, c.AuxId)) break;` (note the new fourth argument; DEF-03 made it default to size 2 so non-barrier behaviour is identical). Then commit: if `barrier`, `_credits[c.PlayerId] -= sd.Cost;` else run the existing readiness consumption at lines 636-643 verbatim INCLUDING the `if (readyCy == c.EntityId) e = cyEnt;` resync (that line prevents the epilogue writeback at World.cs:716 from resurrecting the consumed readiness - do not drop it). Then `_events.Add(new GameEvent(GameEventType.StructurePlaced, _entities.Count, c.AuxId));` unchanged, and add `case EntityKind.Wall: SpawnWall(c.PlayerId, ax, ay); break;` to the spawn switch. Order matters: validate before charging, charge before spawning.
8. ValidPlacement adjacency loop (World.cs:764-771): add before the loop `bool candidateIsBarrier = GetStructureType(structType).Kind == EntityKind.Wall;` and inside, after the existing own-structure filter, add `bool anchorIsBarrier = IsBarrier(o.Kind); if (anchorIsBarrier && !candidateIsBarrier) continue;` and change the radius line to `int radius = anchorIsBarrier ? BarrierBuildRadius : o.Kind == EntityKind.ConstructionYard ? CyBuildRadius : BuildRadius;`. This is ADR-005 clause 4: walls chain from walls at 2 cells, walls never anchor a real building, so the base-crawl exploit cannot smuggle a factory across the map. With no walls present this loop is identical to today.
9. THE THREE EXCLUSIONS, each one word of guard, each verified as a real break:
   - VictorySystem (World.cs:1553) becomes `if ((IsStructure(e.Kind) && !IsBarrier(e.Kind)) || e.UnitType == 7) hasHope[e.PlayerId] = true;` - WITHOUT THIS a player with one surviving 100-credit wall is never eliminated and matches never end.
   - CaptureSystem (World.cs:921) becomes `if (!t.Alive || !IsStructure(t.Kind) || IsBarrier(t.Kind) || t.PlayerId == e.PlayerId)` - engineers do not capture fences.
   - CombatSystem auto-acquire (World.cs:1000) becomes `if (!t.Alive || t.PlayerId < 0 || t.PlayerId == e.PlayerId || t.Kind == EntityKind.FerriteField || IsBarrier(t.Kind)) continue;` - same shape as the FerriteField skip already there; without it your tanks stop to plink at a wall instead of the turret behind it, and the O(n) inner scan grows by 160 entities for every armed unit every tick.
10. DO NOT exclude barriers from splash (World.cs:1022-1030) or from ApplyAreaDamage (World.cs:1487-1503). Walls taking splash is the entire counter: the howitzer (AntiBuilding, 100% vs Structure, splash 1.5) and the superweapon must chew them. Verified arithmetic for Balance co-sign: vs 500 hp Structure, rifle squad = 12 x 25% x 1.875/s = 5.6 dps (89 s per segment, effectively immune); cannon tank = 30 x 50% x 1/s = 15 dps (33 s); howitzer = 60 x 100% / 3 s = 20 dps plus half-damage splash across ~3 segments, so three howitzers (2700 cr) breach a 3-wide gap in ~25 s from range 9, outside every turret's range 5; superweapon = 900 Omni x 80% = 720 inner / 360 outer, an instant ~6-cell hole.
11. MapLoader.cs BuildWorld structure switch (line 165-175): add `EntityKind.Wall => world.SpawnWall(st.Player, st.Ax, st.Ay),` so mission maps can pre-place walls.
12. Sell (World.cs:693-701) and Repair (World.cs:678-683, 1366-1376) need no edit: IsStructure now includes Wall, sell refunds 50, repair mends at 2 hp per credit per tick, and DEF-03 already made the unblock anchor-correct.

Add no Entity field. Do not touch World.Serialization.cs: the format is field-enumerated and unchanged; a Kind byte of 11 reads back fine.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. THE CENTRAL CRITERION: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- golden 2026 > got.txt; grep -v '^#' sim/golden-hashes.txt > want.txt; diff got.txt want.txt` produces NO output and `git diff --exit-code sim/golden-hashes.txt` returns 0 - all 23 existing hashes byte-identical, because no existing scenario can reach a barrier branch. Full gate `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0. Purity grep `grep -rnE '\b(float|double|System\.Random|Godot)\b' sim/Ferrostorm.Sim/` returns nothing. Behaviour assertions land in DEF-06's new scenario, which must prove: a wall placed with no ready slot succeeds and deducts exactly 100; a wall placed with 99 credits is refused and deducts nothing; the 81st barrier for one player is refused while the 81st across two players is not; a wall anchors a second wall at Chebyshev 2 but not at 3; a wall does NOT anchor a power plant at Chebyshev 1; a Construction Yard still anchors a wall at Chebyshev 7; a cannon tank ordered to auto-acquire beside an enemy wall does not fire at it while an explicit Attack order does; a player reduced to exactly one wall emits PlayerEliminated and the opponent's Winner is set; an engineer ordered onto a wall is not consumed and the wall does not change owner; a sold wall refunds exactly 50 and its single cell becomes placeable again (`ValidPlacement(p, ax, ay, 9)` true); a howitzer shell landing on a wall segment damages its two orthogonal neighbours. World.Serialization save/load round trip (`saveload` mode) exits 0 with walls present. Balance + Game Designer co-sign recorded for cost 100 / hp 500 / Structure armour / cap 80 per CLAUDE.md A11. Architect sign-off recorded.

#### TICKET-P5-DEF-05. Sim: attack-move breach - without it, walls delete attack-move and freeze the AI

- Dimension: defensive structures and barriers
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (hash-identical by construction)**
- Files: sim/Ferrostorm.Sim/World.cs
- Spec:

TOUCHES SIM: Architect sign-off required. MUST MERGE IN THE SAME BATCH AS DEF-04 - shipping walls without this is shipping a bug.

THE FAILURE, TRACED IN THE SHIPPED CODE: StepToward (World.cs:822-824) gets `int next = field.NextCell(Map, cx, cy); if (next < 0) { e.Moving = false; return; }` when no route exists. CombatSystem's attack-move branch (World.cs:1044-1057) then sets `e.Moving = true; e.UseFlow = true;` again on the same entity. The unit oscillates in place forever and never fires. Worse, a fully enclosed base makes the flow field return -1 for attackers ANYWHERE ON THE MAP, so SkirmishAI waves (SkirmishAI.cs:327-343, which issue AttackMove) halt at their own base.

1. Add two private helpers to World.cs beside CombatSystem. First:

```csharp
/// <summary>Is there any flow route from this entity's cell to its ordered attack-move point? Reads the deterministic flow cache; FlowField.Build is pure, so building here or in MovementSystem yields the same field.</summary>
private bool RouteExists(in Entity e) { int cx = Map.CellOf(e.X), cy = Map.CellOf(e.Y); int tcx = Map.CellOf(e.AMoveX), tcy = Map.CellOf(e.AMoveY); if (cx == tcx && cy == tcy) return true; return _flow.Get(Map, tcx, tcy).NextCell(Map, cx, cy) >= 0; }
```

Second:

```csharp
/// <summary>Nearest living enemy barrier by squared distance; ties break to the lower id via strict less-than in entity index order (the FindNearestRefinery precedent, World.cs:775-786).</summary>
private int NearestEnemyBarrier(in Entity e) { int best = -1; Fix64 bestD = Fix64.MaxValue; for (int j = 0; j < _entities.Count; j++) { var t = _entities[j]; if (!t.Alive || !IsBarrier(t.Kind) || t.PlayerId < 0 || t.PlayerId == e.PlayerId) continue; Fix64 d = Fix64.DistSq(t.X - e.X, t.Y - e.Y); if (d < bestD) { bestD = d; best = j; } } return best; }
```

2. In CombatSystem's `else if (e.AMove && e.Kind == EntityKind.Unit)` block (World.cs:1036), insert a new branch AFTER `if (sightTarget >= 0) { ... }` and BEFORE the existing `else if (Fix64.DistSq(e.AMoveX - e.X, e.AMoveY - e.Y) <= Fix64.FromInt(16))` arrival test:

```csharp
else if (!RouteExists(in e) && NearestEnemyBarrier(in e) is int wid && wid >= 0) { var wall = _entities[wid]; if (Fix64.DistSq(wall.X - e.X, wall.Y - e.Y) <= e.Sight * e.Sight) { e.ExplicitTarget = wid; e.Moving = false; _entities[i] = e; continue; } e.TargetX = wall.X; e.TargetY = wall.Y; }
```

and let control fall through to the shared `e.Moving = true; e.UseFlow = true;` at line 1057 for the pathing case.

RATIONALE FOR THE TWO-STAGE RULE, and the point the Architect must rule on: pathing toward the nearest enemy barrier is chosen over sight-limited targeting because a fully enclosed base severs the route for units on the far side of the map, which are nowhere near a wall and would otherwise stand still forever; but ExplicitTarget is set ONLY once the barrier is within the unit's own Sight, so the unit never shoots a wall it cannot see. Pathing toward an unseen wall while already ordered to attack-move at a point behind it reveals no information the player did not already act on. Flag this fog nuance explicitly for sign-off rather than burying it.

3. SELF-HEALING, no cleanup code needed, verified against the shipped paths: when the wall dies, CombatSystem's stale-target guard (World.cs:966-968) clears ExplicitTarget on the next tick; AMove is still true so the unit resumes marching; and the death path already calls UnblockFootprint which calls `_flow.Clear()` (World.cs:1081, 366), so the next route query rebuilds against the breach.
4. Pursuit into the wall is safe: the ExplicitTarget branch at World.cs:984-989 paths toward the wall's centre, but the range check at line 979 stops the unit at weapon range 3 to 5 cells, far outside the 1-cell blocked footprint - so the latent no-blocked-check in StepToward (World.cs:830-843) is not reached. Record that latent bug for the Architect; do not fix it here.
5. Add no Entity field. Do not touch the hash or the save format. Determinism: entity-index scan, strict less-than tiebreak, integer and Fix64 maths only.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. `golden 2026` diffs clean against sim/golden-hashes.txt and `git diff --exit-code sim/golden-hashes.txt` returns 0 - the new branch is unreachable without barriers, so all 23 hashes stay byte-identical. Full gate exits 0; purity grep clean. DEF-06's scenario must assert: a cannon tank attack-moved at a point behind a solid 5-segment wall acquires the nearest wall segment as ExplicitTarget within 30 ticks, destroys it, and then reaches within 4 cells of its original AMoveX/AMoveY without a further order; a unit attack-moved at a reachable point with an enemy wall elsewhere on the map never targets that wall (RouteExists short-circuits); the same tank issued the same order from a fixed seed twice produces an identical state hash. A regression assertion that the SkirmishAI still fights: run ScenarioSkirmish's world with an added 10-segment wall across the choke and assert both players' Winner resolves within the scenario tick cap rather than deadlocking. Architect sign-off recorded, explicitly covering the fog nuance in clause 2.

#### TICKET-P5-DEF-06. Runner: walls scenario, its golden line, and the 200-structure perf gate

- Dimension: defensive structures and barriers
- Impact: HIGH | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (adds one golden line; must prove the other 23 are untouched)**
- Files: sim/Ferrostorm.Sim.Runner/Program.cs; sim/golden-hashes.txt
- Spec:

TOUCHES SIM: adds one line to sim/golden-hashes.txt (the new scenario only) and must prove the other 23 are untouched. Merge in the same batch as DEF-04 and DEF-05.

1. Author `ulong ScenarioWalls(ulong seed, Action<int, ulong>? cp = null, Action<string>? report = null)` modelled structurally on ScenarioConstruction (Program.cs:334-462): same `var world = new World(seed, 64, 64, players: 2)`, same `world.GrantCredits(0, 20000)`, same local `void StepN(int n)` helper using `System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cmds)` then `cmds.Clear()`, same throw-on-failure assertion style with a descriptive message prefixed `walls:`, same `report?.Invoke(...)` one-paragraph summary at the end, same `return world.ComputeStateHash();`. Spawn a Construction Yard at (8,8) and a `runner` unit as the command carrier exactly as line 342 does.

Phases, each asserting one clause of ADR-005:
- PHASE A upfront pay - place type 9 at a legal anchor with NOTHING ready at the CY; assert EntityCount rose by 1, the new entity's Kind is EntityKind.Wall and StructType is 9, and Credits(0) fell by exactly 100 (this is the inverse of the construction scenario's line 369 assertion that a non-barrier placement with nothing ready is refused - keep that one passing too).
- PHASE B affordability - drain the treasury to 99 via GrantCredits with a negative amount, attempt a wall, assert EntityCount unchanged and credits unchanged.
- PHASE C chaining - restore funds, place walls at Chebyshev 2 from an existing wall (legal) and at Chebyshev 3 (illegal), asserting each; then assert `world.ValidPlacement(0, wallAnchorX + 1, wallAnchorY, 1) == false` (a wall does not anchor a power plant) while `world.ValidPlacement(0, 12, 8, 9) == true` (the CY still anchors a wall at its radius 7).
- PHASE D cap - loop placing walls until refusal; assert the count of living barriers for player 0 is exactly World.MaxBarriersPerPlayer (80) and that a placement for player 1 still succeeds.
- PHASE E exclusions - reduce player 1 to exactly one wall and assert a PlayerEliminated GameEvent for player 1 is emitted and world.Winner == 0; separately assert an engineer (unit type 11) with ExplicitTarget set to a wall is still Alive after 60 ticks and the wall's PlayerId is unchanged.
- PHASE F auto-acquire - park a cannon tank 2 cells from an enemy wall with no other target, step 60, assert the wall's Hp is unchanged; then issue CommandType.Attack at the wall and assert Hp falls.
- PHASE G the counter (the anti-turtle proof) - build a 5-segment wall line plus a turret behind it, place a howitzer at range 8 (outside the turret's range 5, beyond the howitzer's own MinRange 3), step 900, and assert at least one wall segment died and the howitzer's Hp is unchanged - GDD s6 line 53 made machine-checkable.
- PHASE H breach - attack-move a cannon tank at a point behind a solid wall line, step 600, assert it destroyed a segment and ended within 4 cells of the ordered point (DEF-05).
- PHASE I sell and rubble - sell a wall, assert exactly +50 credits and `ValidPlacement(0, ax, ay, 9)` true again.

2. Register the scenario in the dispatch table beside its siblings at Program.cs:1248-1270: add `("walls", (s, cp) => ScenarioWalls(s, cp)),` after the `depot` entry, and add `ScenarioWalls(seed, null, Console.WriteLine);` to the match-mode report list beside the others near line 1430.
3. Append exactly one line to sim/golden-hashes.txt: `walls 2026 0x................` with the hash the runner emits. The file has a committed LF convention (Program.cs:1403 notes CRLF breaks the byte diff) - do not introduce CRLF.
4. PERF GATE. Extend the existing movement perf gate pattern (Program.cs:1415-1420, `budget 8`, which fails via `return Fail(...)` above 8.0 ms/tick) with a defence-load gate: build a world holding 600 units and 200 structures of which 160 are walls (80 per player, the DEF-04 cap), step 1000 ticks, and `return Fail(...)` if ms/tick exceeds 8.0. This is the TDD s6 budget verbatim (03-technical-design-document.md:59: "600 active units + 200 structures ... sim tick under 8 ms") and it is the ticket that proves the cap is the right number rather than a guess. Report the figure with the same `{ms:F3} ms/tick (budget 8)` formatting the existing gate uses.

- Acceptance: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- golden 2026 > got.txt; grep -v '^#' sim/golden-hashes.txt > want.txt; diff got.txt want.txt` produces no output. `git diff sim/golden-hashes.txt` shows EXACTLY ONE added line (`walls 2026 0x...`) and zero modified lines - any change to an existing hash means DEF-03/04/05 were not behaviour-neutral and the batch is rejected. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- determinism 2026` exits 0 (in-process double run over all 24 scenarios). Full gate exits 0 on both ubuntu-latest and windows-latest per .github/workflows/determinism.yml - cross-platform hash equality is the point. The defence-load perf gate prints a figure and exits 0 under the 8 ms budget at 600 units + 200 structures. Every phase A through I asserts and the report string names all nine.

#### TICKET-P5-DEF-07. Art: six auto-connecting wall meshes in the Directorate and Sodality languages

- Dimension: defensive structures and barriers
- Impact: HIGH | Effort: M | cheapModelSafe: false | touchesSim: false
- Files: art/3d/builder.py; art/3d/export_glb.py; game/assets/models/ (generated)
- Blocked on: the doc 16 amendment in section 5 (its team-band rule is the amendment's mechanical form)
- Spec:

TASTE UNAVOIDABLE: silhouette quality at 40-pixel scale is exactly what doc 16 governs, and a cheap model will produce mush. Give this to art-pipeline with the numbers below as a floor, not a ceiling.

Add six builder functions to art/3d/builder.py following the existing structure conventions verbatim (see dir_turret at line 427 and com_service_depot at line 476): compose from the module's own `box(name, sx, sy, sz, x, y, z, m, bevel)` and `cyl(name, r, h, x, y, z, m, vs, rx, ry)` helpers, use only palette keys from PAL (line 4), and end with `return join(parts, '<name>')`.

CRITICAL SCALE NOTE: existing structures are 2x2 cells and use `pad()` = `box('pad', 1.9, 1.9, 0.08)` (line 335-336), i.e. one cell equals one Blender unit and the model origin is the footprint CENTRE. A wall segment is 1x1, so it must span 0.95 units and be centred on its own origin - do NOT call pad().

Functions, all named com_ per the CLAUDE.md shared-hardware prefix convention:
- `com_wall_post` (isolated, mask 0): a single squat block `box('post', 0.5, 0.5, 0.55, m='olived', bevel=0.04)` on a `box('foot', 0.85, 0.85, 0.06, m='gundark', bevel=0.02)` footing.
- `com_wall_straight` (mask 5 / 10): the footing plus a `box('span', 0.95, 0.34, 0.5, m='olived', bevel=0.04)` running along +X, with two `cyl` stiffener ribs at x = -0.3 and +0.3.
- `com_wall_cap` (masks 1/2/4/8): straight but spanning only from the origin to +X 0.475, with a thicker `box('cap', 0.42, 0.42, 0.6)` terminating block at the open end.
- `com_wall_corner` (masks 3/6/12/9): two half-spans meeting at the origin, one along +X and one along +Y, with a corner `cyl('knuckle', 0.22, 0.62, vs=8)` at the joint.
- `com_wall_tee` (masks 7/14/13/11): three half-spans plus the knuckle.
- `com_wall_cross` (mask 15): four half-spans plus the knuckle.

ORIENTATION CONTRACT, load-bearing for DEF-08: build every variant in its mask-canonical rotation - straight runs along +X, cap opens toward +X, corner joins +X and +Y, tee omits the -X arm - and document this in a comment above the six functions, because the client rotates a single mesh by yaw rather than shipping 16 meshes. Note the axis conversion, Blender +Y forward becomes glTF -Z, stated outright in the DEF-08 comment block above WallVariant in ModelLibrary.cs (an earlier draft of this sentence cited it by SkirmishLive.cs line number, which rotted within a wave; cite that file by greppable anchor only): state the contract in Blender axes and let DEF-08 own the yaw table.

TEAM COLOUR, and this is the doc 16 amendment section 5 ratifies: do NOT put a team band on com_wall_straight. Put a `box('bd', 0.34, 0.08, 0.06, m='orange', bevel=0.015)` band ONLY on post, cap, corner, tee and cross - the run's end caps and junctions - so a 40-segment wall wears marks at its silhouette's actual extremities rather than forty times over.

Register all six in the BUILDERS dict (line 585-596). Export via art/3d/export_glb.py, which iterates BUILDERS and writes to game/assets/models/ - it needs no edit.

Then render the contact sheet and LOOK AT IT: the run must read as one continuous barrier at RTS camera distance (the camera sits at y=22 per SkirmishLive.cs:205), corners must not gap, and the team marks must read as punctuation rather than a stripe.

- Acceptance: Blender headless runs art/3d/export_glb.py to completion printing `GLB EXPORT DONE`. Six new .glb files exist under game/assets/models/ (com_wall_post, com_wall_straight, com_wall_cap, com_wall_corner, com_wall_tee, com_wall_cross) and each is under 40 KB (the 20 existing models total 620 KB per the phase-1 backlog, so budget proportionally). Godot imports all six without error and generates .glb.import siblings. A programmatic bounds assertion per mesh: the AABB fits inside 1.0 x 1.0 x 0.8 Blender units - a 1x1 segment that overhangs its cell will visibly intersect its neighbours. `grep -n 'orange' art/3d/builder.py` shows a team band inside com_wall_post/cap/corner/tee/cross and NOT inside com_wall_straight. NEEDS A HUMAN: a rendered contact-sheet strip showing a 10-segment straight run, a corner, a tee and a cross at RTS camera distance, inspected by Luke against docs/design/16-visual-style.md for the silhouette test and the amended team-colour law. Legal check: no Command & Conquer, Red Alert, Tiberium, GDI, Nod, Westwood or EA reference in any name, comment or asset.

#### TICKET-P5-DEF-08. Client: drag-a-line wall placement and neighbour-mask mesh selection

- Dimension: defensive structures and barriers
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs; game/scripts/ModelLibrary.cs; game/scripts/Sidebar.cs
- Spec:

PURE PRESENTATION - no sim file is touched, and the chaining needs NO new sim command because each segment is an independent PlaceStructure the sim validates on its own (DEF-04 clause 7). Blocked on DEF-04 (sim accepts type 9) and DEF-07 (meshes exist).

1. ModelLibrary.cs: the KindModel dict (line 19-25) maps EntityKind to one model name; a wall needs one of six by neighbour mask, so add a separate path: `private static readonly string[] WallVariant = { "com_wall_post", "com_wall_cap", "com_wall_cap", "com_wall_corner", "com_wall_cap", "com_wall_straight", "com_wall_corner", "com_wall_tee", "com_wall_cap", "com_wall_corner", "com_wall_straight", "com_wall_tee", "com_wall_corner", "com_wall_tee", "com_wall_tee", "com_wall_cross" };` indexed by the 4-bit mask, and `private static readonly float[] WallYaw = { 0, 90, 0, 0, 270, 90, 270, 0, 180, 90, 0, 90, 180, 180, 270, 0 };` in degrees. Mask bits: N=1 (cell y-1), E=2 (x+1), S=4 (y+1), W=8 (x-1). Add `public Node3D InstantiateWall(int mask, out float yawDeg)` loading `res://assets/models/{WallVariant[mask]}.glb` through the same `_cache` dictionary and returning WallYaw[mask].

WHICH mesh follows from the neighbour count alone and needs no axis reasoning, so the WallVariant row above can be typed as written. WHICH YAW does not. **The authority for the yaw table is the ORIENTATION CONTRACT comment block above the six com_wall_* functions in art/3d/builder.py, not this document.** Re-derive from that comment; do not copy a table. This spec previously carried a DRAFT yaw row, `{ 0, 0, 90, 0, 180, 0, 90, 0, 270, 270, 90, 180, 180, 90, 270, 0 }`, which was wrong on ten of its sixteen entries because it assumed a north-facing cap and the opposite rotation sense. It has been replaced above by the derived table, and the derivation is recorded here so the next reader can check it rather than trust it. DEF-07's ledger entry (finding (d) in docs/tickets/phase-1-backlog.md) proposes a different correction to the tee row alone, [7]=90 [11]=0 [13]=270 [14]=180; that correction is ALSO wrong and would break [7] and [14], which the draft happened to have right. Do not apply it.

The derivation, in three steps:

- AXIS CONVERSION. The glTF exporter maps Blender (x,y,z) to glTF (x,z,-y), and the client maps sim X to world X and sim Y to world Z (SkirmishLive.SyncActors). Therefore Blender +X is EAST, Blender +Y is NORTH, Blender -X is WEST, Blender -Y is SOUTH. The draft's north-facing cap came from reading the contract's "+X" as north; it is east.
- CANONICAL MASK of each mesh at yaw 0, reading the contract (straight along +X, cap's ARM toward +X, corner joining +X and +Y, tee omitting the -X arm) through that conversion: straight {E,W} = 10, cap {E} = 2, corner {N,E} = 3, tee {N,E,S} = 7. The tee omits -X, and -X is WEST, so the canonical tee is the one missing its west arm. Verified against the exported vertex data rather than assumed, measured above the symmetric 0.95 footing which would otherwise drown the signal: straight X[-0.475,+0.475] against Z[-0.170,+0.170] (it runs east-west), cap X[-0.210,+0.475] (arm east, block on the origin), corner reaching +X and -Z (east and north), tee X[-0.220,+0.475] with Z reaching both -0.475 and +0.475 and nothing west of the knuckle.
- ROTATION SENSE. Godot's yaw about +Y sends +X to -Z, i.e. E->N->W->S for increasing yaw. This is not a guess: the shipped hull-yaw line `Mathf.Atan2(-to.X, -to.Z)` in SkirmishLive.cs is only correct under this sense, and it visibly orients every unit in the game. (Grep for the expression; SkirmishLive.cs grows too fast for a line number to survive, which is how this ticket's original axis citation rotted in the first place.) The draft's N->E->S->W is the opposite sense.

Applying R(90): E->N, N->W, W->S, S->E to each canonical gives every entry:

| mask | arms | mesh | yaw |
| --- | --- | --- | --- |
| 0 | none | post | 0 (rotationally irrelevant) |
| 1 | N | cap | 90 |
| 2 | E | cap | 0 (canonical) |
| 3 | N,E | corner | 0 (canonical) |
| 4 | S | cap | 270 |
| 5 | N,S | straight | 90 |
| 6 | E,S | corner | 270 |
| 7 | N,E,S | tee | 0 (canonical) |
| 8 | W | cap | 180 |
| 9 | N,W | corner | 90 |
| 10 | E,W | straight | 0 (canonical) |
| 11 | N,E,W | tee | 90 |
| 12 | S,W | corner | 180 |
| 13 | N,S,W | tee | 180 |
| 14 | E,S,W | tee | 270 |
| 15 | all | cross | 0 (rotationally irrelevant) |

A wrong yaw entry is invisible until a corner faces the wrong way in a screenshot, which is exactly why this table is derived and shown rather than asserted, and why the whole chain is now machine-checked: `python3 art/3d/wall-yaw-gate.py` (stdlib only, no Blender, no Godot) re-measures each mesh's arms from the exported GLB vertex data, proves every ModelLibrary.cs entry against them, and hard-fails if this spec's tables or the ledger's drift from the code. The gate is proven to bite: perturbing corner mask 6 from 270 to 90 in a scratch copy makes it exit 1 naming the mask, and it correctly distinguishes a genuinely wrong yaw from a merely non-canonical one on the rotationally symmetric meshes. If the meshes are ever rebuilt in a different canonical rotation, builder.py's contract moves, the gate fails on its contract check, and this table must be re-derived, not patched.

2. SkirmishLive.cs SyncActors (line 640): walls must not go through the generic `_models.Instantiate((int)v.Kind, v.UnitType)` path at line 655. Before the actor loop, build `var wallCells = new Dictionary<(int, int), int>();` mapping (cellX, cellY) to PlayerId for every `v` in _view with `v.Alive && v.Kind == EntityKind.Wall`, using `((int)v.X, (int)v.Y)` (walls sit at cell centre so the floor is the cell). Then for a wall actor compute `int mask = 0; if (wallCells.TryGetValue((cx, cy-1), out var p0) && p0 == v.PlayerId) mask |= 1; if (... (cx+1, cy) ...) mask |= 2; if (... (cx, cy+1) ...) mask |= 4; if (... (cx-1, cy) ...) mask |= 8;` - same-owner neighbours only, so two players' walls meeting do not fuse into one run.
3. REBUILD ONLY ON CHANGE, not per frame: keep `private readonly Dictionary<int, int> _wallMask = new();` and a `private bool _wallsDirty;` set true whenever a StructurePlaced or Died GameEvent names a wall (the event stream is already consumed around SkirmishLive.cs:406-451). When dirty, recompute masks for every wall and, for any wall whose mask changed, free its actor and re-instantiate via InstantiateWall, setting `node.RotationDegrees = new Vector3(0, yawDeg, 0)`. Placing one segment changes at most its four neighbours, so this is cheap and correct; recomputing every frame for 160 walls is not.
4. Wall actors get `BattlefieldView.AddContactBlob(node, 1.0f)` not the 2.6f at line 661 - that blob is sized for a 2x2 building. Skip the W2-05 rise tween at line 680-686 for walls: twenty segments popping out of the ground simultaneously reads as an earthquake; walls should simply appear.
5. DRAG-LINE PLACEMENT. Add `private bool _wallDrag; private (int X, int Y) _wallDragStart;` and `private readonly List<MeshInstance3D> _dragGhosts = new();`. In _UnhandledInput (line 922), the existing `case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } pm when _placingType > 0: TryPlace(pm.Position); break;` must special-case barriers: if `World.GetStructureType(_placingType).Kind == EntityKind.Wall`, set `_wallDrag = true` and record the start cell instead of committing. Add `case InputEventMouseMotion when _wallDrag:` to recompute the ghost line, and `case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _wallDrag:` to commit it. THE LINE RULE, mechanical: from start cell (sx, sy) to current cell (cx, cy), take `int dx = cx - sx, dy = cy - sy;` and emit a strictly ORTHOGONAL run - if `Math.Abs(dx) >= Math.Abs(dy)` walk x from sx to cx at y = sy, else walk y from sy to cy at x = sx. No diagonals, no L-bends; the player draws two lines for a corner. This matches the idiom and keeps the ghost unambiguous.
6. On commit, for each cell in the run in walk order, emit `new Command(0, 0, CommandType.PlaceStructure, _yardId, Fix64.FromInt(x), Fix64.FromInt(y), _placingType)` into `_pending`. Do NOT client-side-filter on ValidPlacement for the commit - the sim rejects illegal ones silently and per-segment, and a client filter would desync from the sim's own view under lockstep. Do use `_world.ValidPlacement(0, x, y, _placingType)` for the GHOST TINT only (green valid / red invalid, the existing colours at lines 554), exactly as the single-placement ghost already does at line 551.
7. STAY IN MODE: PlaceAtCell currently clears `_placingType = 0; _ghost.Visible = false;` at line 1020. For barriers, do not clear - the classic loop is draw, draw, draw, Escape. Keep the existing clear for every non-barrier type. Escape (line 929) exits and must also free every node in _dragGhosts.
8. Cap and cost feedback: read `World.MaxBarriersPerPlayer` and the player's live barrier count; while dragging, tint the ghost run red past the cap and show the running cost (`run.Count * World.GetStructureType(9).Cost`) in the existing _selInfo label (line 528).
9. Sidebar.cs: add `new("WALL", 9, 100, "com_wall_straight")` to the Structures array (line 18-26). Its Refresh method disables structure buttons when `readyStructureType > 0` (line 190) - walls bypass the ready slot entirely, so exempt type 9: `b.Disabled = !hasYard || credits < def.Cost || (typeId != 9 && readyStructureType > 0);`. Wire the button to `_game.EnterPlacement(9)` directly rather than to QueueStructure, because a wall is never queued at the yard.
10. Free every ghost node on scene exit; the existing _trackPool pattern (line 96) is the recycling precedent if the drag ghosts churn.

- Acceptance: `dotnet build game/Ferrostorm.Game.csproj -c Release` exits 0 and Godot headless runs the scene 1000 frames without error. `git diff --stat sim/` is empty - this ticket touches no sim file. Headless scripted assertions driving the same public command path PlaceAtCell already exposes for verification: a drag from (20,20) to (25,20) emits exactly 6 PlaceStructure commands with AuxId 9 at y=20 and x=20..25 in walk order; a drag from (20,20) to (23,26) emits 7 commands along x=20 (the |dy| > |dx| branch), none diagonal; after the sim steps, the actor for the middle segment of that east-west straight run resolves to com_wall_straight with RotationDegrees.Y == 0 (mask 10 = E|W is the straight's CANONICAL orientation, so it takes no yaw; the perpendicular mask 5 = N|S is the one at 90, and a run drawn along y is the case that catches a transposed table), the two ends resolve to com_wall_cap, and an isolated segment resolves to com_wall_post; placing a segment adjacent to an existing run re-instantiates exactly the changed neighbours and no others (assert on instantiation count, not appearance); a wall owned by player 1 adjacent to a wall of player 0 does not raise either one's mask bit. After a wall placement `_placingType` is still 9 and the ghost is visible; after a turret placement it is 0 and hidden. NEEDS A HUMAN: whether drawing a wall line feels like drawing rather than clicking; whether the mask table produces correct corners in a real screenshot (the single most likely defect in this ticket).

#### TICKET-P5-DEF-09. Client: box-select and mass sell or repair for barrier runs

- Dimension: defensive structures and barriers
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs
- Spec:

PURE PRESENTATION.

THE FINDING: FinishSelect's box-select loop (SkirmishLive.cs:998-1003) filters to `if (!v.Alive || v.PlayerId != 0 || !Mobile(v.Kind)) continue;` where `Mobile(k) => k is EntityKind.Unit or EntityKind.Harvester` (line 638). Structures are UNSELECTABLE by drag - only by single click (lines 984-993). So selling a 40-segment wall is 40 individual clicks, and the R (repair, line 935-940) and X (sell, line 941-946) hotkeys, which already loop over `_selection` and already filter to `!Mobile(rv.Kind)`, can never receive more than one structure at a time. Walls make this intolerable; the fix is small and improves every structure today.

1. In the box-select loop, admit barriers: change the filter to `if (!v.Alive || v.PlayerId != 0) continue; if (!Mobile(v.Kind) && v.Kind != EntityKind.Wall) continue;` - barriers only, deliberately NOT all structures, because box-dragging across your base and accidentally including the Construction Yard in an X (sell) is a catastrophic misclick with no undo. State that reasoning in the comment.
2. MIXED SELECTION RULE, mechanical: if a drag captures both mobile units and walls, keep ONLY the mobile units - a drag across the battlefield is an army selection and must never silently include the fence behind the enemy. Implement by collecting into two lists and preferring the mobile one when non-empty. This mirrors the existing single-click precedence at lines 984-986, which tries mobile at radius 0.9 first and falls back to structures at 1.4 only on a miss.
3. The R and X handlers need NO change - they already iterate `_selection` and filter on `!Mobile`, so multi-wall selection makes them work by construction. Verify by reading rather than assuming.
4. Add a confirmation guard on X when the selection contains more than 8 structures: show the count in the existing _banner or _toast (ShowToast, line 567) and require a second X press within 2 seconds. Selling 40 walls for 2000 credits by a stray keypress with no undo is a match-losing misclick, and the sim has no cancel for it.
5. SelectionSummary (line 583-602) shows a per-entity readout for a single selection and `"{n} SELECTED"` otherwise; for a multi-wall selection show `"{n} WALL SEGMENTS   R repair  X sell   {refund} cr"` where refund is `n * World.GetStructureType(9).Cost / 2` - the sell value is the number the player actually needs.
6. The on-demand SelRing at line 990-991 is added per actor at radius 1.5; for walls use a smaller radius (DEF-01 clause 8 already parameterises this) and add it for every newly selected wall in the box path, not just the click path.

- Acceptance: `dotnet build game/Ferrostorm.Game.csproj -c Release` exits 0; Godot headless runs 1000 frames without error. `git diff --stat sim/` is empty. Headless assertions: a drag rectangle enclosing 10 own wall segments and nothing else selects exactly 10; the same drag over an enemy's walls selects 0; a drag enclosing 3 own units and 5 own walls selects exactly the 3 units; a drag enclosing a Construction Yard and 4 walls selects exactly the 4 walls; with 10 walls selected, one X press shows the confirmation toast and emits ZERO SellStructure commands, a second X within 2 seconds emits exactly 10; with 8 or fewer, a single X emits immediately; with 10 walls selected, R emits exactly 10 Repair commands with no confirmation (repair is reversible and cheap, sell is not). SelectionSummary with 10 walls selected contains the string "10 WALL SEGMENTS" and the refund 500.

#### TICKET-P5-DEF-17. Balance gate: prove turtling is beatable, in CI

- Dimension: defensive structures and barriers
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: tools/Ferrostorm.Balance/Program.cs; docs/balance/ (new dated report)
- Spec:

The one ticket that stops "walls must not make turtling unbeatable" from being a paragraph nobody can check.

tools/Ferrostorm.Balance/Program.cs today runs a per-cost engagement matrix over UNITS only (its `types` array at line 14 is seven unit ids) with a hard-fail `expectedWinners` table (lines 18-38) that already encodes GDD s6 intent and exits nonzero on inversion - CI runs it as the last determinism job. Extend it, do not replace it.

1. Add a second section, STATIC DEFENCE, after the existing counter-triangle matrix. Build a fixed world per seed from the committed seeds `{ 11, 22, 33 }` with an ArmyCredits budget of 3000 as the existing harness uses: defender gets a fortified position - 1 Construction Yard, 1 power plant sized so `supply >= draw` (so DEF-10's 75% rule does not silently disarm the test), 2 turrets (type 5) and a 12-segment wall line (type 9) placed via world.SpawnWall in front of them; attacker gets 3000 credits of a single unit type placed at the far side.
2. Assert an `expectedBreachers` table with the same hard-fail shape as expectedWinners: howitzer (type 8) MUST breach - defined machine-checkably as at least one wall segment destroyed AND at least one turret destroyed AND the attacker retaining more than 30% of its starting army value, within MaxTicks (3000, the existing cap). This is GDD s6 line 53 ("artillery beats static defence") turned into a gate. Rifle squads (type 2) MUST NOT breach - assert zero wall segments destroyed, which pins the wall's 500 hp against small arms (12 damage x 25% AntiInfantry-vs-Structure x 1.875/s = 5.6 dps = 89 seconds per segment). Cannon tanks (type 1) at equal credits MUST NOT breach cleanly - assert they either fail to kill both turrets or retain under 30% army value, which is what makes artillery a real purchase decision rather than a flavour.
3. Add a COST-EFFICIENCY assertion with an explicit stated bound rather than a vibe: hit points per credit for a wall (500/100 = 5.0) versus a turret (400/600 = 0.67) versus a cannon tank (300/600 = 0.5). Hard-fail if a wall's hp-per-credit exceeds 8.0 - the wall is deliberately the cheapest hit points in the game, which is what a wall IS, and it is paid for by having no gun, no vision (Sight = Fix64.Zero), no adjacency for real buildings, an 80-segment cap, and splash vulnerability. The bound stops a later tuning pass from quietly making walls free.
4. Emit the results into the existing StringBuilder report in the same markdown table format (lines 40-45) and write a dated file into docs/balance/ following the existing naming convention there (2026-07-08-baseline.md, 2026-07-14-vanguard.md, etc).
5. Exit nonzero on any hard failure, matching the existing `hardFail` flag at line 47, so the determinism workflow's Balance gate step catches it.

- Acceptance: `dotnet build tools/Ferrostorm.Balance -c Release` exits 0 and `dotnet run --project tools/Ferrostorm.Balance -c Release --no-build` exits 0, both as the .github/workflows/determinism.yml Balance gate step already invokes them. The generated docs/balance report contains a STATIC DEFENCE table naming all three attacker types across all three seeds with a stated breach verdict each. Deliberately regressing the wall's hp to 5000 in a scratch build makes the howitzer row hard-fail and the tool exit nonzero - prove the gate bites rather than trusting it. Deliberately dropping the wall's cost to 10 makes the hp-per-credit bound hard-fail. Results are deterministic: two consecutive runs produce byte-identical reports apart from the date line. Balance + Game Designer co-sign on the three expectedBreachers verdicts per CLAUDE.md A11.

#### TICKET-P5-DEF-14. AI: a defensive doctrine, so the Turtle preset actually turtles

- Dimension: defensive structures and barriers
- Impact: MEDIUM | Effort: M | cheapModelSafe: false | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/SkirmishAI.cs; sim/golden-hashes.txt
- Spec:

TASTE UNAVOIDABLE (this is AI tuning, and the numbers below are a starting hypothesis for ai-engineer plus Balance, not a spec to type in blind). TOUCHES SIM - SkirmishAI lives in the deterministic sim library and the `skirmish` and `aisuper` goldens will move. Blocked on DEF-04 and DEF-05.

THE FINDING: the three personality presets (SkirmishAI.cs:29-31) differ ONLY in actEvery and waveSize - Standard(15, 6), Rusher(15, 4), Turtle(15, 10). The Turtle does not turtle; it attacks in bigger waves. Its build ladder (line 94-101) reaches `!hasTurret ? 5` and then never builds another turret for the rest of the match, forever, on any preset, on any map. There is no defensive doctrine in the codebase at all.

1. Give SkirmishAI two new readonly knobs alongside _actEvery and _waveSize: `_turretTarget` (how many turrets the doctrine wants) and `_wallSegments` (how many barrier segments it will lay, 0 to disable). Presets become Standard(turretTarget: 2, wallSegments: 0), Rusher(turretTarget: 1, wallSegments: 0), Turtle(turretTarget: 5, wallSegments: 40). Keep every existing knob value unchanged so only the new behaviour moves.
2. Replace the `!hasTurret ? 5` rung of the `wanted` ladder (line 99) with a count-based test: tally `turretCount` in the same entity sweep that already sets hasTurret (line 62) and use `turretCount < _turretTarget ? 5`. Leave the ladder ORDER alone - infrastructure before army is a deliberate and stated doctrine (line 162-163) and the `supply < draw + 40` rung at line 97 must stay ABOVE turrets so the AI never builds a gun it cannot power under DEF-10's 75% rule.
3. Wall laying, gated on `_wallSegments > 0` and on the ladder being satisfied (`wanted == 0`) so walls never starve the economy: barriers bypass the Construction Yard ready slot entirely (DEF-04 clause 7), so the AI emits PlaceStructure with AuxId 9 directly, not BuildStructure. Site selection must be deterministic and must reuse the existing precedent: TryFindPlacement (line 378-397) already does an outward Chebyshev ring scan, newest structure first, and DEF-03 fixed its anchor recovery to World.AnchorOf. Add a sibling `TryFindWallCell` that walks the ring shell at exactly Chebyshev radius 4 around the Construction Yard's anchor in the same fixed dx/dy order and returns the first cell where `w.ValidPlacement(_player, cx, cy, 9)` holds - a perimeter ring, not a random scatter. Emit at most ONE segment per decision beat so wall laying costs the AI real tempo (40 segments at actEvery 15 is 600 ticks and 4000 credits, a genuine strategic commitment rather than a free wall). Respect World.MaxBarriersPerPlayer.
4. DO NOT let the AI wall itself in: before emitting, require that the candidate cell is not the last opening - cheapest deterministic guard is to skip any cell within Chebyshev 2 of the AI's own factory anchor so produced units always have an exit (units spawn on the SpawnOffsets ring, World.cs:1339-1340, and a factory ringed by walls would strand every unit it builds). State this as a hypothesis for ai-engineer to verify by watching a match, not as a proven rule.
5. The AI's wave targeting (line 77-88) already filters enemy structures to ConstructionYard/Factory/Refinery/PowerPlant, so walls can never become wave targets - no change needed, and this is why DEF-05 rather than AI logic is what gets waves through a wall. Confirm this by reading, do not assume.
6. The AI's intruder guard (line 243-253) lists own structures worth defending and does not include walls - correct, leave it.
7. Regenerate the `skirmish` and `aisuper` goldens; diff and explain any others.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0; full gate exits 0; `determinism 2026` exits 0 on ubuntu-latest and windows-latest (an AI that reads a dictionary in iteration order or breaks a tie non-deterministically fails here - that is the point of the gate). sim/golden-hashes.txt changes only for `skirmish` and `aisuper` unless a diff proves otherwise, each explained. New runner assertions: a Turtle AI given 20000 credits and 3000 ticks ends with at least 4 living turrets and at least 20 living barrier segments; a Rusher AI under the same conditions ends with at most 1 turret and zero barriers; a Turtle AI never exceeds World.MaxBarriersPerPlayer; a Turtle AI's factory can still produce and release a unit at tick 3000 (assert a unit exists that was spawned after tick 2000 and has moved more than 3 cells from the factory - the wall-in guard, made checkable). A full Standard-versus-Turtle match from a fixed seed resolves to a Winner within the scenario cap rather than deadlocking - if the Turtle becomes unbeatable, DEF-17's gate is the arbiter and the numbers here change, not the wall's stats. NEEDS A HUMAN: whether a Turtle match is interesting to play against or merely slow. ai-engineer plus Balance co-sign.

---
## Wave C: colour and readability

Wave A's biome work is the ground half of the colour answer and lands first because MAP-01 shares its function. Wave C is the rest: the roster's chroma, the bake that was destroying it, the light that models it, and the second team-colour location that makes half the roster tell the players apart at all.

Landing order: **C-01** (the cheapest win in the project, ship it first), **C-10** (light rig, free chroma on grey objects), **C-06 and C-05 in one Blender session** (C-06 must land with or before C-05, or the chroma C-05 restores is immediately clipped away by the 8-bit bake), **C-08**, **C-07** (blocked on the section 5 amendment), **C-11** (taste, last).

The doc 16 amendment that C-07 and DEF-07 both depend on is **section 5 of this document** and is blocked on Luke. C-12 and DEF-18 are superseded by it and must not be implemented.

#### C-01. Per-instance hue jitter for scatter, rubble, rocks, plates and tufts

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/BattlefieldView.cs
- Spec:

All five MultiMeshes already set VertexColorUseAsAlbedo = true and already call SetInstanceColor, but pass only grey. Add hue.

1. Add this private static helper to BattlefieldView: `private static Color Jitter(System.Random r, float rr, float gg, float bb, float lo, float hi) { float sh = lo + (float)r.NextDouble() * (hi - lo); return new Color(sh * rr, sh * gg, sh * bb); }`.
2. Add a private static readonly array of mineral tints: `private static readonly (float R, float G, float B)[] MineralTints = { (1.14f, 1.00f, 0.82f), (1.05f, 1.02f, 0.97f), (0.86f, 0.96f, 1.16f), (1.20f, 0.96f, 0.74f), (0.92f, 1.00f, 1.05f) };`.
3. RUBBLE (line 806-807): replace `float sh = 0.8f + (float)rng.NextDouble() * 0.4f; mm.SetInstanceColor(i, new Color(sh, sh, sh));` with `var t = MineralTints[rng.Next(MineralTints.Length)]; mm.SetInstanceColor(i, Jitter(rng, t.R, t.G, t.B, 0.78f, 1.18f));`.
4. TALUS (BuildTerrain line 708 and 714): replace `float tsh = 0.8f + (float)crng.NextDouble() * 0.4f;` / `new Color(tsh, tsh, tsh)` with `var tt = MineralTints[crng.Next(MineralTints.Length)];` / `Jitter(crng, tt.R, tt.G, tt.B, 0.78f, 1.18f)`.
5. ROCKS (line 959-960): same substitution using `rng`, but band 0.82f..1.12f.
6. PLATES (line 1014-1015): these are armour debris, so use a dedicated array `private static readonly (float,float,float)[] PlateTints = { (1.02f, 1.06f, 1.16f), (1.22f, 0.94f, 0.76f), (0.90f, 0.94f, 1.00f) };` (steel, rusted, painted) with band 0.80f..1.20f.
7. TUFTS (line 902-903): vegetation gets three species colours. Add `private static readonly (float,float,float)[] TuftTints = { (1.00f, 0.94f, 0.62f), (0.80f, 0.98f, 0.74f), (1.26f, 0.86f, 0.58f) };` (dead straw, ash-green scrub, rust-brown bracken) and replace with `var vt = TuftTints[rng.Next(TuftTints.Length)]; tufts.SetInstanceColor(i, Jitter(rng, vt.Item1, vt.Item2, vt.Item3, 0.80f, 1.15f));`.

Do NOT change any AlbedoTexture, mesh, instance count, transform, or the RNG call ORDER other than the extra Next() calls noted (the scatter is cosmetic and reseeded per build, so instance reshuffling is acceptable). Zero new draw calls, zero new textures, no shader work.

- Acceptance: dotnet build game/Ferrostorm.Game.csproj is clean. Offscreen capture (temporary autoload pattern, --audio-driver Dummy, per game/README-GODOT.md) at the terrain-v2.png viewpoint on skirmish-03: convert the capture to HSV and compute mean saturation over the full frame; it must rise by at least 0.04 absolute versus a pre-change capture at the identical viewpoint. Additionally, sample 200 random pixels that land on scatter geometry and assert at least 3 distinct hue clusters (hue values spanning >= 40 degrees total range) where the pre-change capture spans < 10 degrees. sim golden-hash battery (dotnet run --project sim/Ferrostorm.Sim.Runner -c Release) exits 0 - this is a client-only change and any hash delta is a defect.

#### C-05. builder.py PAL chroma pass: hold luminance, restore saturation, add an srgb() entry helper

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: L | cheapModelSafe: true | touchesSim: false
- Files: art/3d/builder.py, art/3d/materials2.py, game/assets/models/*.glb, docs/design/16-visual-style.md
- Spec:

Do C-06 first (or in the same session) so the rebake is done once.

The PAL dict at builder.py line 4 holds sRGB-looking fractions in Blender Base Color slots, which are scene-linear, so every model renders 50-60 sRGB units lighter and 30-40% less saturated than doc 16's hexes (PAL gun renders #95A2AC at HSV S 0.134 against doc's #5b6770 at 0.188; PAL rust renders #B6866F at 0.390 against doc's #8a4a34 at 0.623; PAL teal renders #86DAD0 at 0.385 against doc's #4fb8a8 at 0.571). DO NOT 'fix' this by adopting doc 16's hexes as linear values: rust would drop from 0.283 to 0.106 relative luminance (-62%) and Sodality would disappear against the ground. The shipped VALUE is fine; the missing CHROMA is the defect. The table below raises HSV saturation to or past doc 16's intent while holding relative luminance within roughly 15%.

1. Add above PAL:

```python
def srgb(h):
    """Hex as seen on screen -> scene-linear, which is what Blender Base
    Color slots hold. PAL is authored in linear; the hexes in doc 16 are the
    rendered result of these triples, not the entry format."""
    def c(v):
        v = int(h[v:v+2], 16) / 255.0
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return (c(0), c(2), c(4), 1)
```

(call as srgb('e8762c')).

2. Replace PAL wholesale with these exact linear triples; the trailing comment on each line is the sRGB hex it renders as and is what doc 16's table must be reconciled to (section 5):

```python
cinder=(0.048,0.052,0.062,1),   # #3E4046
gun=(0.20,0.34,0.50,1),         # #7C9EBC  S 0.34 (was 0.134)
plate=(0.34,0.48,0.66,1),       # #9EB8D4  S 0.26
gundark=(0.105,0.185,0.28,1),   # #5B7790  S 0.37
orange=(0.807,0.181,0.025,1),   # #E8762C  S 0.81 - doc 16's signal orange, correctly encoded
rust=(0.55,0.185,0.075,1),      # #C4774D  S 0.61, hue 20deg, luminance -10%
rustp=(0.68,0.26,0.115,1),      # #D78B5F  S 0.56
rustd=(0.32,0.105,0.045,1),     # #995B3C  S 0.61
teal=(0.078,0.480,0.392,1),     # #4FB8A8  S 0.57 - doc 16's corroded teal, correctly encoded
olive=(0.30,0.33,0.185,1),      # #959C77  S 0.24 - a real olive drab, not grey-beige
olived=(0.185,0.205,0.105,1),   # #777D5B  S 0.27
ferrite=(0.82,0.55,0.175,1),    # #EAC474  S 0.50
fhi=(0.92,0.72,0.30,1),         # #F6DD95  S 0.39
bone=(0.83,0.81,0.75,1),        # unchanged
glow=(1.0,0.78,0.42,1),         # warmer lamp
beacon=(1.0,0.16,0.10,1))       # unchanged
```

3. materials2.wmat line 91: the Directorate chip colour `(0.50,0.53,0.56,1)` is a neutral grey that fights the new steel blue. Change to `(0.42,0.50,0.60,1)`. The Sodality chip `(0.24,0.13,0.09,1)` stays (salvage chips darker, not brighter - that comment is correct).
4. Rebake and re-export the full roster: `blender -b -P bake.py` (all 20 models). Do not hand-edit any .glb.
5. Do not change any geometry, emit strength, roughness, metallic, bevel, UV or bake resolution in this ticket. Colour values only.
6. Constraint to hold: the two faction body colours must differ in hue by at least 90 degrees. gun renders at hue 208 and rust at hue 20 - a 188-degree separation, versus 202 vs 24 today, so this is maintained, but re-check it if any value is adjusted.

- Acceptance: `blender -b -P bake.py` completes for all 20 models with no errors and prints BAKED for each. Automated check over the re-exported .glb diffuse textures (numpy over the packed images, or over the intermediate bake buffers): for a dir_cannon_tank hull texel sample, mean HSV saturation >= 0.24 and hue in 195-225; for a sod_phantom_tank hull sample, saturation >= 0.40 and hue in 10-32; for a com_harvester olive, saturation >= 0.18 and hue in 60-85. Mean relative luminance of each model's diffuse texture is within 20% of its pre-change value (nothing went dark). Offscreen capture at the slice-v5.png viewpoint: mean HSV saturation over pixels belonging to unit geometry rises by at least 0.08 absolute. game/ builds clean, the models load with no import errors. sim golden-hash battery exits 0 (asset-only change).

#### C-06. Fix the 8-bit emissive bake clamp: normalise emit at bake, carry intensity in KHR_materials_emissive_strength

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: art/3d/bake.py, game/assets/models/*.glb, docs/design/20-visual-aaa-roadmap.md, docs/design/16-visual-style.md
- Spec:

bake.py line 36 creates the bake target as `bpy.data.images.new(img_name, size, size, alpha=False)` with no float_buffer, so it is an 8-bit byte buffer and everything above 1.0 clips. EVERY emissive in the roster exceeds 1.0 on at least one channel, and clipping one channel of a saturated colour is a hue shift, not just a brightness cap. Provable examples from builder.py: the superweapon core (line 453, orange (0.91,0.42,0.13) at emit 2.2) bakes to (1.0 clipped, 0.924, 0.286) = yellow, not signal orange. The Sodality veil orb (line 468, teal (0.24,0.70,0.63) at emit 1.8) bakes to (0.432, 1.0 clipped, 1.0 clipped) = cyan-white; the faction's signature colour is destroyed. The ferrite tips (line 512, fhi at 2.4) bake to pure white. The team band itself (line 92, emit 1.2 on orange) clips red. Note that doc 20's W5-07 remedy ('lower emit strength on tip materials to 1.2-1.6') does not work: fhi's max channel is 0.92, so anything above emit 1.087 still clips. The fix is to normalise at bake time and put the energy back at export.

1. In bake.py, add before the per-object loop body that bakes EMIT:

```python
def emissive_scale(obj):
    """Largest emission colour*strength product on this object. The EMIT
    bake target is 8-bit, so anything over 1.0 clips a channel and shifts
    hue; scale every emissive material down by M before baking and hand M
    back to the exported emissive strength."""
    m = 1.0
    for slot in obj.material_slots:
        nt = slot.material.node_tree
        if 'Principled BSDF' not in nt.nodes: continue
        b = nt.nodes['Principled BSDF']
        s = b.inputs['Emission Strength'].default_value
        c = b.inputs['Emission Color'].default_value
        m = max(m, s * max(c[0], c[1], c[2]))
    return m
```

2. Immediately before the `emit_img = bake_pass(ob, 'EMIT', ...)` call (line 121), insert:

```python
M = emissive_scale(ob)
saved_es = []
if M > 1.0:
    seen_es = set()
    for slot in ob.material_slots:
        mm = slot.material
        if mm.name in seen_es: continue
        seen_es.add(mm.name)
        nt = mm.node_tree
        if 'Principled BSDF' not in nt.nodes: continue
        inp = nt.nodes['Principled BSDF'].inputs['Emission Strength']
        saved_es.append((inp, inp.default_value))
        inp.default_value = inp.default_value / M
```

Materials are cached and shared across slots and objects (see the existing note in bake_value_pass), so the `seen_es` de-duplication is mandatory - without it a shared material is divided twice.

3. Immediately after that bake_pass call, restore: `for inp, v in saved_es: inp.default_value = v`.
4. Change the emissive wiring at line 197 from `bsdf.inputs['Emission Strength'].default_value = 2.0` to `bsdf.inputs['Emission Strength'].default_value = 2.0 * M`. This preserves the final result exactly ((colour/M) * 2M = colour * 2) while the baked map keeps full chroma. glTF exports it as KHR_materials_emissive_strength, which Godot 4 imports (already relied on by the current code).
5. Add to the print at line 210: `, emit_scale={M:.3f}, emit_max={earr.reshape(-1,4)[:, :3].max():.3f}`.
6. The emit image is sRGB-tagged (line 121 passes 'sRGB'), so post-normalisation values as low as 0.06 still encode to ~69/255 - there is no banding concern. Do not switch the bake target to float_buffer: the glTF emissive texture is LDR regardless, so normalise-and-carry is the correct fix, not a wider buffer.
7. Rebake the full roster: `blender -b -P bake.py`.
8. In docs/design/20-visual-aaa-roadmap.md mark W5-07 RESOLVED BY C-06 and record that its proposed 1.2-1.6 range would still have clipped (0.92 * 1.2 = 1.104 > 1.0).

- Acceptance: `blender -b -P bake.py` completes for all 20 models. The printed emit_max is < 1.0 for EVERY model that reports glow=True (pre-change it is 1.0 for all of them - that is the clamp). Automated check on the re-exported emissive textures: dir_superweapon's core texels have R > G by at least 0.25 and R > B by at least 0.55 (currently R and G are within 0.08 - it bakes yellow); sod_veil_projector's orb texels have G > R by at least 0.30 AND B < 0.95 (currently B is clamped at 1.0); ferrite_cluster tip texels have R minus B >= 0.35 (currently 0.0 - pure white). In-game offscreen capture at the slice-v5.png viewpoint: the superweapon core reads orange with a distinct bright core and coloured halo, not a white blob; team rings still show no bloom halo (glow HdrThreshold 1.0 behaviour unchanged). game/ builds clean; sim golden-hash battery exits 0.

#### C-07. Second team-colour location: structure footprint strips and a wider unit ground ring

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Blocked on: the doc 16 amendment in section 5 (land them together)
- Files: game/scripts/BattlefieldView.cs, game/scripts/SkirmishLive.cs
- Spec:

This ticket implements the doc 16 amendment written in section 5; land them together.

Current state, verified: ModelLibrary.cs ships one model per unit type shared by both players, and every structure except dir_turret/dir_superweapon/sod_veil_projector is a `com_` model. SkirmishLive.cs line 660-661 dresses non-mobile actors with `AddContactBlob(node, 2.6f)` and nothing else. Consequence: player 0's refinery and player 1's refinery are pixel-identical, as are both players' harvesters, MCVs, rifle squads, rocket squads and engineers. The one-place law is also already broken in shipped code without being written down - DressMobile (line 1049) adds a team ground ring ON TOP of the model's baked band, which is a second place.

1. Add to BattlefieldView:

```csharp
/// <summary>The SECONDARY team mark (doc 16, amended): a claimed-ground
/// strip around a structure footprint. Common hardware ships one model
/// per type shared by both players, so without this a Directorate
/// refinery and a Sodality refinery are the same pixels.</summary>
public static void DressStructure(Node3D node, int player, float span = 2.6f)
{
    AddContactBlob(node, span);
    if (player < 0) return;
    var mark = player == 0 ? DirectorateMark : SodalityMark;
    var mat = new StandardMaterial3D
    {
        AlbedoColor = mark,
        EmissionEnabled = true,
        Emission = mark,
        EmissionEnergyMultiplier = 0.9f,
    };
    float half = span * 0.44f;      // 1.144 at span 2.6: just outside a 2x2 footprint
    float t = 0.14f;                // strip thickness
    foreach (var (sx, sz, lx, lz) in new[]
    {
        (0f, -half, half * 2f + t, t), (0f, half, half * 2f + t, t),
        (-half, 0f, t, half * 2f + t), (half, 0f, t, half * 2f + t),
    })
        node.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(lx, 0.03f, lz) },
            Position = new Vector3(sx, 0.02f, sz),
            MaterialOverride = mat,
            Name = "TeamStrip",
        });
}
```

A square outline, not a torus: structure footprints are square and a square strip reads as claimed ground rather than a decorative halo.

2. SkirmishLive.cs line 660-661: replace `else if (v.Kind != EntityKind.FerriteField) BattlefieldView.AddContactBlob(node, 2.6f);` with `else if (v.Kind != EntityKind.FerriteField) BattlefieldView.DressStructure(node, v.PlayerId, 2.6f);`. Ferrite fields keep the existing stain-decal branch untouched.
3. Widen the unit ground ring so the secondary mark survives the RTS camera band (RtsCamera clamps 8..42): in DressMobile line 1055 change `new TorusMesh { InnerRadius = 0.30f, OuterRadius = 0.36f }` to `new TorusMesh { InnerRadius = 0.27f, OuterRadius = 0.40f }`.
4. DO NOT raise EmissionEnergyMultiplier above 0.9f on either the ring or the strip. Doc 20 W1-08 deliberately keeps team rings below the glow HdrThreshold of 1.0 so they do not bloom; 0.9 is that budget and the strips must respect it.
5. The W2-05 rise tween (SkirmishLive line 682-685) scales the whole node from y 0.05, so strips added as children rise with the structure automatically - no extra work, do not special-case it.
6. Presentation layer only. No sim reference, no gameplay coupling, nothing that a UE5 renderer swap (ADR-004) could not reimplement from the same GameEvent/snapshot data.

**Doc 22 integration note:** walls are the one exception. Per the section 5 amendment, a chained barrier's silhouette is the RUN, not the segment, so wall actors must NOT get DressStructure's team strip. DEF-08 clause 4 already routes them to AddContactBlob(node, 1.0f) instead; keep that branch ahead of this one.

- Acceptance: game/ builds clean. Offscreen capture of a skirmish with both players' bases in frame: (a) a 10x10 px patch on the ground strip at a player-0 refinery has hue in 15-35 degrees (signal orange) and the same patch at a player-1 refinery has hue in 165-185 (corroded teal) - pre-change both are the same cinder ground; (b) counting connected orange-hued and teal-hued pixel regions, each structure contributes at least one region of >= 200 px; (c) no bloom halo appears around any strip or ring (compare the glow-pass contribution against the pre-change capture - team marks must stay under the HdrThreshold); (d) the strips sit under, not through, the structure geometry at every zoom level between 8 and 42. sim golden-hash battery exits 0.

#### C-08. Reduce the explored-shroud chroma wash

- Dimension: colour, palette and texture richness
- Impact: LOW | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/FogOfWar.cs
- Spec:

FogOfWar.cs line 52 paints explored-but-unseen cells with an unshaded alpha-blended near-black at alpha 0.38. At the RTS camera band, most of the frame on a 96x64 map is explored-but-unseen at any moment, so 38% of the ground's already-scarce chroma is blended toward (0.012,0.018,0.030) before the player ever sees it. Every chroma gain from C-02 and C-04 is taxed by this.

1. Change line 52 from `new Color(0.012f, 0.018f, 0.030f, 0.38f)` to `new Color(0.020f, 0.030f, 0.052f, 0.30f)` - lower alpha so less of the ground hue is painted out, and a stronger blue lift so what remains reads as distance haze rather than black paint. This keeps the direction doc 20's W1-09 already chose (it moved from 0.55 to 0.38 alpha for exactly this reason) and takes one more step along it.
2. Leave the unexplored case (lines 24 and 54) alone at alpha 0.985 - unexplored must stay opaque or it becomes an information leak.
3. Do not touch the Init fill (line 24), which is the unexplored colour and must keep matching line 54.
4. Do not change the shroud plane height, mesh, filter mode or the minimap's use of the same image.

**Doc 22 integration note:** MAP-07 rewrites this same function as a byte[] fill and hard-codes the byte equivalents of these Colors. Land C-08 FIRST and give MAP-07 the new numbers: (0.020, 0.030, 0.052, 0.30) rounds to bytes 5, 8, 13, 77. If MAP-07 lands first, C-08 edits bytes rather than Colors. The two tickets must not disagree on the shroud colour.

- Acceptance: game/ builds clean. Offscreen capture at the terrain-v2.png viewpoint with a limited-vision skirmish state: (a) mean HSV saturation of pixels in explored-but-unseen regions rises by at least 0.02 absolute versus a pre-change capture; (b) the visible/explored boundary is still clearly readable - mean luminance of explored regions is at least 18/255 below adjacent currently-visible regions; (c) unexplored regions are unchanged, within 1/255 mean luminance of the pre-change capture. The minimap still renders three distinguishable states. sim golden-hash battery exits 0 (the sim visibility read API is untouched).

#### C-10. Widen the warm/cool spread in the light rig

- Dimension: colour, palette and texture richness
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/BattlefieldView.cs
- Spec:

BuildLightRig (lines 95-129) gives the key an R/B ratio of only 1.22 (1.0, 0.93, 0.82) at energy 1.7, against cool fill 0.5 + cool rim 0.6 + cool ambient 0.4. Lit and shadowed faces of the same grey plate therefore differ almost entirely in brightness rather than hue, which is why grey models on grey ground read as one mass. Widening the spread produces chromatic modelling on every surface in the game without touching a single albedo - the cheapest colour in the project after C-01. Exact values:

1. KeySun (line 99-113): LightColor (1.0f, 0.93f, 0.82f) -> (1.0f, 0.855f, 0.62f); LightEnergy 1.7f -> 1.85f. R/B rises 1.22 -> 1.61.
2. FillSky (line 114-121): LightColor (0.55f, 0.65f, 0.85f) -> (0.42f, 0.56f, 0.95f); LightEnergy 0.5f -> 0.6f. R/B falls 0.65 -> 0.44.
3. RimNorth (line 122-129): LightColor (0.75f, 0.85f, 1.0f) -> (0.62f, 0.78f, 1.0f); LightEnergy 0.6f -> 0.75f.
4. BuildEnvironment AmbientLightColor (line 42): (0.34f, 0.38f, 0.46f) -> (0.30f, 0.335f, 0.44f); AmbientLightEnergy 0.4f -> 0.38f. (Once C-02 lands, this value lives in Biome.Ambient for Ashfield - update it there instead and give Cauldron (0.44,0.40,0.36) and Floodplain (0.33,0.39,0.48) the same treatment: keep the energies as C-02 specced.)
5. Do NOT touch shadow settings, RotationDegrees, LightAngularDistance, split distances, or any Ssao/Ssil/Glow value. Angles are W1-01 territory and are not in scope.
6. Total directional energy rises from 2.8 to 3.2 (+14%), which is intentional given the frame is dark, but it is bounded: if the acceptance histogram overshoots, the single permitted compensating knob is TonemapExposure (line 45, currently 1.35f), adjusted once, and the new value recorded in the ledger. Do not touch any other knob to compensate.
7. Note for whoever lands doc 20 W5-01 later: these are the DUSK values. The Dawn and Overcast tables in W5-01 compose on top and must be re-derived from these, not from the pre-change numbers.

**Doc 22 integration note:** MAP-05 also edits BuildLightRig, adding an optional max-camera-height argument that drives DirectionalShadowMaxDistance. The two are orthogonal (C-10 changes colours and energies, MAP-05 changes distances) but they touch the same initialiser. Land either order; do not let MAP-05's `Mathf.Max(90f, ...)` derivation overwrite C-10's LightColor values.

- Acceptance: game/ builds clean. Offscreen capture zoomed to one dir_cannon_tank at the slice-v5.png viewpoint: (a) sample a key-lit hull face and a fill-lit hull face; the key-lit sample's R/B ratio minus the fill-lit sample's R/B ratio must be >= 0.45 (pre-change this gap is under 0.20 - the two faces are the same hue at different brightness); (b) mean open-ground luminance stays inside 90-135/255, adjusting TonemapExposure once if not, and recording the value; (c) no clipped whites outside emissive effects. ReplayTheater capture shows the same directional warm/cool modelling (it shares the rig). sim golden-hash battery exits 0.

#### C-11. Split-tone colour-correction LUT in the environment grade

- Dimension: colour, palette and texture richness
- Impact: MEDIUM | Effort: S | cheapModelSafe: false | touchesSim: false
- Files: game/scripts/BattlefieldView.cs
- Spec:

TASTE REQUIRED - this is a grade, and the exact ramp is a judgement call that belongs in the W5-05 taste pass; a cheap model can wire it but must not choose the final stops.

The environment currently leans on AdjustmentSaturation 1.12f (line 49) as its only chroma tool, which multiplies existing chroma and therefore does nothing where there is none. A colour-correction LUT ADDS chromatic separation between shadow and highlight, which is exactly what a frame made of grey objects on grey ground lacks.

1. In BuildEnvironment, build a GradientTexture1D with 5 stops: 0.00 -> Color(0.04f, 0.06f, 0.12f) (cool shadows), 0.25 -> Color(0.20f, 0.22f, 0.28f), 0.50 -> Color(0.50f, 0.50f, 0.50f) (neutral pivot, must stay neutral or midtones shift), 0.75 -> Color(0.80f, 0.76f, 0.68f), 1.00 -> Color(1.0f, 0.96f, 0.86f) (warm highlights). Set `Width = 256`.
2. Assign `env.AdjustmentColorCorrection = <that texture>` (Godot 4.7 C# property on Godot.Environment; GDScript name adjustment_color_correction, which accepts a GradientTexture1D or a Texture3D LUT). AdjustmentEnabled is already true at line 46.
3. If the property is unavailable or the 1D sampling semantics in 4.7 do not produce a split-tone (Godot samples the gradient per channel by that channel's own intensity, which yields a per-channel curve - verify against a capture before committing), delete the block, record the finding in the ledger, and close the ticket as WONTFIX rather than substituting something else.
4. Consider lowering AdjustmentSaturation from 1.12f to 1.06f once the LUT is in, so the two tools are not both fighting the AgX chroma compression - but only as a second capture, one knob, and only if the frame reads oversaturated.
5. Interaction to watch: AgX (TonemapMode, line 44) compresses chroma near clipping, so the LUT's top stop mostly affects emissives; the shadow stops do the real work here.
6. Zero runtime cost beyond one texture fetch in the post pass.

- Acceptance: game/ builds clean. Offscreen capture at the terrain-v2.png viewpoint: (a) bucket pixels into the darkest and brightest luminance quartiles; the mean B-minus-R of the dark quartile must exceed the mean B-minus-R of the bright quartile by at least 10/255 (pre-change the two are within 4/255 - no tonal colour separation exists); (b) mean open-ground luminance stays inside 90-135/255; (c) frame time at 1600x900 within 0.3 ms of baseline. Luke signs off the ramp by eye at all three capture viewpoints, or the block is deleted per step 3 and the numbers are recorded. sim golden-hash battery exits 0.

---
## Wave D: base depth and economy

The biggest wave and the least urgent, because the loop underneath it already works. It is grouped into five blocks. Block D.1 is the quick-win shelf: six S-effort presentation tickets, none of which touches the sim, several of which are the difference between a player understanding the game and guessing at it. Block D.2 is the two real sim defects found in passing. Block D.3 is power and tech, which is where the base-building depth actually lives. Block D.4 is production and placement UX. Block D.5 is the economy.

Sequencing that matters: **BD-06 before BD-07, DEF-12 and BD-17**, so those become data edits rather than more hard-coding. **P5-ECON-01 before P5-ECON-08, P5-ECON-09 and P5-ECON-10**, which all need the ViewEntity fields it adds. **P5-ECON-07 before P5-ECON-13**. **P5-ECON-04 and P5-ECON-05 in one golden regeneration**. **DEF-01 before BD-11**. **BD-09 before BD-17**, or the AI stalls forever queueing a superweapon it can never build.

### D.1 The quick-win shelf

#### BD-01. Health bars for structures (S quick win)

- Dimension: base building depth
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs
- Spec:

Pure client. The billboard health-bar block at SkirmishLive.cs:817 is gated `if (_latest.TryGetValue(id, out var hv) && Mobile(hv.Kind))`, so buildings never get a bar.

1. Add two statics beside HpBackMesh/HpFillMesh (lines 107-108): `private static readonly QuadMesh HpBackMeshBig = new() { Size = new Vector2(1.8f, 0.16f), CenterOffset = new Vector3(0.9f, 0, 0) };` and `HpFillMeshBig` with Size (1.8f, 0.12f), same CenterOffset (the offset is always Size.X/2, matching the existing mobile pair).
2. Change the line-817 condition to `if (_latest.TryGetValue(id, out var hv) && hv.Kind != EntityKind.FerriteField)`.
3. Inside, add `bool big = !Mobile(hv.Kind);` and select mesh via `Mesh = big ? HpBackMeshBig : HpBackMesh` (same for fill) when constructing at lines 822-823.
4. Position: replace the fixed `new Vector3(-0.45f, 1.15f, 0)` at lines 833-834 with `new Vector3(big ? -0.9f : -0.45f, big ? 1.9f : 1.15f, 0)`.
5. Visibility rule for structures differs from mobiles: `bool show = node.Visible && hv.MaxHp > 0 && (_selection.Contains(id) || hv.Hp < hv.MaxHp);` already reads correctly for both - keep it unchanged. Reuse FillGreen/FillAmber/FillRed and the existing >0.5 / >0.25 thresholds untouched.

- Acceptance: Start skirmish-01. Select the starting Construction Yard: a wide bar appears above it reading full. Let the AI damage any own structure below full HP with nothing selected: its bar appears without selection. Ferrite fields never show a bar. `dotnet build game` clean; no change to sim/ files.

#### P5-ECON-01. Extend ViewEntity with Ferrite/Carry/HState and fix the dead field-drain visual

- Dimension: resource economy and harvesting
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: sim/Ferrostorm.Presentation/SnapshotInterpolator.cs; game/scripts/SkirmishLive.cs
- Spec:

THE S-EFFORT QUICK WIN. Ferrite fields never visibly drain in live play.

1. SnapshotInterpolator.cs lines 18-20, extend the record struct with three trailing defaulted params (additive, no caller breaks):

```csharp
public readonly record struct ViewEntity(int Id, bool Alive, int PlayerId, EntityKind Kind, double X, double Y, int Hp, int UnitType = 0, int MaxHp = 0, int Ferrite = 0, int Carry = 0, HarvestState HState = HarvestState.Idle);
```

2. In TrySample, at BOTH output.Add call sites (lines 71 and 75), append `e0.FerriteAmount, e0.Carry, e0.HState` as the three new args. Take them from e0 (the earlier snapshot) only - never interpolate. This matches the documented rule at lines 45-51 ('everything discrete snaps to the earlier snapshot').
3. SkirmishLive.cs lines 724-728, replace the body with:

```csharp
if (v.Kind == EntityKind.FerriteField)
{
    float g = Mathf.Clamp(v.Ferrite / (float)MapData.StandardFieldAmount, 0f, 1f);
    node.Scale = Vector3.One * (0.35f + g * 0.65f);
}
```

Use MapData.StandardFieldAmount, not the 12000 literal (Main.cs:41 spawns a 4000 field, so the literal is already wrong there).

4. SkirmishLive.cs line 711, replace the direct live-sim read `_world.Entities[v.Id].HState == HarvestState.Loading` with `v.HState == HarvestState.Loading`. Delete the now-stale 'post-Step read of HState' comment at 707-708. Same behaviour, and it puts the harvest FX back on the ADR-001 snapshot contract instead of reaching into live sim state.

WHY (verified): SpawnFerriteField (World.cs:263-270) sets Hp=1/MaxHp=1; HarvestSystem (1125) only decrements FerriteAmount. So `v.Hp/12000f` is 0.0000833 for the field's whole life and `Mathf.Max(0.2f, ...)` pins the scale to a constant 0.78. Probed: t600/1200/1800/2400/3000 -> Hp=1/1 while amt=9900/7800/5700/3600/1500. ReplayTheater.cs:122-126 already does this right because the runner exports FerriteAmount (Program.cs:2064) - the live client is the only broken path. doc 16 calls the draining glow the viewer's signature.

NOTE: Ferrostorm.Presentation is not in the sim hash path. Golden hashes are unaffected. Carry/HState are also the prerequisite for P5-ECON-08 and P5-ECON-10.

- Acceptance: dotnet build of the solution is clean. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 and sim/golden-hashes.txt is byte-identical. A headless probe that steps the economy scenario and samples the interpolator asserts: (a) ViewEntity.Ferrite == World.Entities[id].FerriteAmount at every sampled tick; (b) Ferrite strictly decreases while any harvester is in HarvestState.Loading; (c) the field node's Scale is >= 0.99 at FerriteAmount==12000 and <= 0.40 at FerriteAmount==600.

#### BD-02. Sidebar: build-time readout and remaining-seconds on the queue head

- Dimension: base building depth
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/Sidebar.cs, game/scripts/SkirmishLive.cs
- Spec:

Pure client. No build time is shown anywhere.

1. `World.GetStructureType(id).BuildTicks` is static and reachable from Sidebar; `GetUnitType` is an INSTANCE method on World, so add a parameter to `Sidebar.Init(SkirmishLive game, System.Func<int,int> unitBuildTicks)` and pass `t => _world.GetUnitType(t).BuildTicks` from SkirmishLive.cs:346.
2. In `MakeButton`, the caller now supplies build ticks; format the base text as `$"{it.Label}  {it.Cost}  {ticks / 15f:0.#}s"` (15 = World.TicksPerSecond). Keep `_baseText[b] = b.Text;` as the badge base (Sidebar.cs:154) so the `x{n}` suffix still appends correctly.
3. In `Refresh`, for the queue head only, append remaining time. The head fraction is already passed as `yardProgress`/`facProgress`; compute `int remain = Mathf.CeilToInt(totalTicks * (1f - progress) / 15f)` and render the head's text as `_baseText[b] + $"  {remain}s"` instead of the `x{n}` badge when n == 1, or `$"  {remain}s  x{n}"` when n > 1.
4. Do not touch the ColorRect Fill logic at lines 193-194 / 203-204.

**Doc 22 integration note:** BD-06 converts GetStructureType from static to instance. If BD-06 lands first, this ticket's step 1 must pass a second delegate for structure build ticks rather than calling the static. Land BD-02 first if you want to avoid that; it is a five-minute difference either way.

- Acceptance: Queue a POWER PLANT (BuildTicks 100 => label reads `POWER PLANT  300  6.7s`). While it builds, the button shows a decreasing seconds count that reaches 0 exactly as the PLACE button appears. Queue 3 rifle squads: head shows `Ns  x3`. Build clean.

#### BD-03. Sidebar gating must match the pay-as-you-build sim

- Dimension: base building depth
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/Sidebar.cs
- Spec:

Pure client; the sidebar is stricter than the sim and removes a classic play. Sidebar.cs:190 reads `b.Disabled = !hasYard || readyStructureType > 0 || credits < def.Cost;`. Both extra clauses are wrong: (a) `ProductionSystem` (World.cs:1428-1446) drains credits progressively and halts-then-resumes when broke, so a full-cost check up front is unnecessary and blocks the classic queue-it-and-let-it-drip opening; (b) `ProductionSystem` line 1424 only PAUSES the line while `ReadyStructure != 0` - the sim accepts further `BuildStructure` commands into the queue.

1. Replace with `b.Disabled = !hasYard;`.
2. Add affordability as a DIM, not a block: `b.Modulate = credits < def.Cost ? new Color(1,1,1,0.55f) : Colors.White;` (do not use the disabled stylebox, which now means 'no yard').
3. Leave the unit loop's `b.Disabled = !hasFactory;` (line 199) as is, and add the same Modulate dim there using the cost from the BuildItem record.
4. Do not add any new pre-validation: the sim already refuses illegal commands silently.

**Doc 22 integration note:** BD-20 adds a queue cap of 9 and requires the sidebar to disable a producer's buttons at the cap; DEF-08 requires type 9 (wall) to be exempt from the ready-slot gate this ticket is deleting. All three edit the same line. Final form after all three: `b.Disabled = !hasYard || queueLen >= World.MaxQueue;` with the Modulate dim for affordability and no ready-slot clause at all.

- Acceptance: With a CY and 100 credits, REFINERY (2000) is clickable and dimmed; clicking queues it and the head's progress fill advances as harvester income arrives, matching World.BuildPaid drain. With a structure sitting in the ready slot, further structures can still be queued and the queue badge increments; the line resumes automatically once the ready one is placed. No structure ever gets built without full payment (assert `Credits` never goes negative across a 3000-tick run).

#### P5-ECON-06. Client-side guard: Harvest with no refinery is a silent no-op

- Dimension: resource economy and harvesting
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs lines 1103-1122
- Spec:

Right-clicking a ferrite field with a harvester selected and no refinery built does NOTHING - no movement, no message, no sound. The player concludes harvesting is broken.

VERIFIED: World.cs:581-588 accepts the command, sets e.FieldId = c.AuxId, calls FindNearestRefinery which returns -1, and leaves HState at Idle. Probe result: HState=Idle, Moving=False, FieldId=1, RefineryId=-1, zero events emitted. The command also silently MUTATES FieldId even though it accomplished nothing.

FIX (client-side only; the sim is untouched, no hash change):

1. Add a helper next to FindOwnStructure: `private bool HasLiveRefinery() { foreach (var e in _world.Entities) if (e.Alive && e.PlayerId == 0 && e.Kind == EntityKind.Refinery) return true; return false; }`
2. In the right-click order path, guard the Harvest branch. Replace line 1110-1111's `else if (field >= 0 && me.Kind == EntityKind.Harvester)` body so that when HasLiveRefinery() is false the command is NOT queued. Compute the check ONCE before the `foreach (int id in _selection)` loop at 1105 (not per unit).
3. When suppressed, fire feedback exactly once for the whole click: ShowToast("NO REFINERY - BUILD ONE FIRST") and _audio.Play("ui_click", -12). This is the established denial pattern - line 1017 uses the same ui_click at -12 for an invalid structure placement, and no ui_deny asset exists (game/audio/ has 13 wavs, none of them a denial buzz).
4. Suppress the gold Harvest order marker in the denied case: at line 1117, `int mk = enemy >= 0 ? 1 : (field >= 0 && hasRef ? 2 : 0);` so the player is not told the order landed.

DO NOT fix this by making the sim reject the command - that changes ApplyCommandCore's state mutation and every AI-driven golden. The honest sim-side version is P5-ECON-07's note.

- Acceptance: A scripted-input harness with a harvester selected, a live field, and zero refineries issues a right-click on the field and asserts: _pending contains no CommandType.Harvest; after 10 ticks World.Entities[harvester].FieldId is still -1 and HState is still Idle; the toast label is visible with the expected text. The same harness with a refinery present asserts the Harvest command IS queued and HState becomes ToField within 2 ticks. Golden hashes unchanged. The Godot scene runs 1000 headless frames without error.

#### P5-ECON-08. Harvester load and field-remaining readouts

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false
- Depends on: P5-ECON-01 (needs ViewEntity.Carry and .HState)
- Files: game/scripts/SkirmishLive.cs lines 583-602 (SelectionSummary), 1101-1122 (pick path)
- Spec:

DEPENDS ON P5-ECON-01 (needs ViewEntity.Carry and .HState).

The player cannot see how full a harvester is or how much ferrite a field has left. SelectionSummary (583-602) prints `HARVESTER 700/700` - that is HP, and a full-health empty harvester reads identically to a full-health loaded one. Fields cannot be selected at all. doc 18 M12 already logged this ('no load indicator ... HState and Carry exist sim-side but are not exported') and doc 18's Phase A planned exactly the ViewEntity extension that never landed.

1. SelectionSummary, single-selection branch: when v.Kind == EntityKind.Harvester, append the load and the state. Build:

```csharp
string load = $"  LOAD {v.Carry}/{World.HarvesterCapacity}";
string st = v.HState switch { HarvestState.ToField => "  [TO FIELD]", HarvestState.Loading => "  [LOADING]", HarvestState.ToRefinery => "  [RETURNING]", HarvestState.Unloading => "  [UNLOADING]", _ => "  [IDLE]" };
return name + hp + load + st;
```

Use World.HarvesterCapacity, not a 700 literal.

2. Make ferrite fields selectable for READOUT ONLY. In the left-click pick path, if nothing else was hit, allow PickEntity to return a FerriteField (radius 1.1f, same as the existing Harvest pick at line 1104) and put its id in _selection. In SelectionSummary, when v.Kind == EntityKind.FerriteField return `$"FERRITE FIELD  {v.Ferrite:N0} / {MapData.StandardFieldAmount:N0}"`. Guard every own-unit action path against a field being in _selection: the drag-select filter (SelectAllOwn line 1132 already filters on PlayerId==0 && Mobile), the order paths at 1105-1114 (already gated on _latest lookups plus Mobile/Harvester checks - verify), and the R repair / X sell keys.
3. Do NOT render a SelRing on a field: line 729 sets `sel.Visible = _selection.Contains(v.Id)` and ferrite_cluster has no SelRing child, so this is already inert - confirm, do not add one.
4. Multi-selection is unchanged ('N SELECTED').

- Acceptance: A harness selects a harvester mid-Loading and asserts the _selInfo label matches the regex `HARVESTER \d+/\d+  LOAD \d+/700  \[(TO FIELD|LOADING|RETURNING|UNLOADING|IDLE)\]`, and that the LOAD numerator equals World.Entities[id].Carry. It selects a field and asserts the label matches `FERRITE FIELD  [\d,]+ / 12,000` with the value equal to FerriteAmount. It asserts that with a field in _selection, a right-click on the ground queues zero commands and R/X produce no Repair/Sell command. Golden hashes unchanged.

### D.2 The two real sim defects

#### TICKET-P5-DEF-16. Sim: Construction Yard build queues are missing from the state hash

- Dimension: defensive structures and barriers (found in passing)
- Impact: HIGH | Effort: S | cheapModelSafe: true | **touchesSim: TRUE (regenerates goldens)**
- Canonical; supersedes BD-04 (Appendix A), which is the same bug and the same fix.
- Files: sim/Ferrostorm.Sim/World.cs; sim/golden-hashes.txt
- Spec:

TOUCHES SIM, REGENERATES GOLDENS. A real defect found while tracing the barrier build path; it matters more once walls make defensive build orders long.

THE BUG, at World.cs:1594 inside ComputeStateHash: `if (e.Kind == EntityKind.Factory && _queues.TryGetValue(e.Id, out var q)) { h.Add(q.Count); foreach (int t in q) h.Add(t); }`. The `_queues` dictionary is keyed by producer id and is populated for Construction Yards too (World.cs:664, the BuildStructure case) and consumed by ProductionSystem (World.cs:1423-1425, `if (e.Kind != EntityKind.Factory && e.Kind != EntityKind.ConstructionYard) continue;`). CY queue contents are cross-tick state that drives future evolution and are correctly serialised (World.Serialization.cs:47-53 writes all of `_queues` via OrderedQueues), but they are NOT hashed.

CONSEQUENCE: under lockstep (sim/Ferrostorm.Net/Lockstep.cs), a divergence in a Construction Yard's queue is invisible to desync detection until the queue head completes, which is up to BuildTicks later - 600 ticks, i.e. 40 seconds, for a superweapon. This is a detection hole, not a desync cause, since commands are identical by construction; it delays the alarm rather than sounding a false one.

THE FIX, one condition: `if ((e.Kind == EntityKind.Factory || e.Kind == EntityKind.ConstructionYard) && _queues.TryGetValue(e.Id, out var q)) { h.Add(q.Count); foreach (int t in q) h.Add(t); }`.

Determinism is preserved without further work because the iteration is over `_entities` in fixed index order and the inner loop walks a List<int> in insertion order - no dictionary iteration is introduced (the CLAUDE.md unordered-iteration rule and the Save path's OrderedQueues helper at World.cs:72-77 both exist for that reason; note in a comment that this site is safe because it is keyed access from an ordered entity walk, matching the pattern the _queues field comment at line 309 already claims).

Update the comment above ComputeStateHash if it implies only factories produce.

Regenerate the affected goldens: any scenario holding a non-empty CY queue at a hash checkpoint moves - expect `construction` (Program.cs:348-459 queues repeatedly at cy1), `skirmish`, `aisuper`, and possibly the mission scenarios. Do not guess which; diff and explain each. Keep LF endings.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. Full gate exits 0; `determinism 2026` exits 0 on ubuntu-latest and windows-latest. sim/golden-hashes.txt changes only for scenarios that genuinely hold a CY queue at a checkpoint, each named in the commit message. `saveload` and `campaignsave` modes exit 0 - the save format is untouched, so a save taken before this change and loaded after must still resume bit-exact within a single build. A new assertion in ScenarioConstruction: two Worlds built from the same seed, one given a BuildStructure command at a Construction Yard and one not, produce DIFFERENT ComputeStateHash values on the tick after the command - today they are identical, which is the bug made visible. `lan 5` and `lanchaos 1 60 30` exit 0. Architect sign-off recorded, noting the replay-compatibility break.

#### BD-05. Selling a Construction Yard must refund its ready structure

- Dimension: base building depth
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/World.cs, sim/golden-hashes.txt, sim/Ferrostorm.Sim.Runner/Program.cs
- Spec:

REAL DEFECT; changes sim behaviour => golden regeneration + Architect sign-off.

`CancelProduce` (World.cs:598-603) correctly refunds `GetStructureType(e.ReadyStructure).Cost` in full because a ready structure is fully paid. `SellStructure` (World.cs:693-701) does not: it credits `sold.Cost / 2` and sets `e.Alive = false`, silently burning a fully-paid structure held in the ready slot.

Fix, inside the SellStructure case before `_credits[e.PlayerId] += sold.Cost / 2;`: `if (e.Kind == EntityKind.ConstructionYard && e.ReadyStructure != 0) { _credits[e.PlayerId] += GetStructureType(e.ReadyStructure).Cost; e.ReadyStructure = 0; }`. Full (not half) refund is the correct rule and matches CancelProduce - the ready structure was never built, only paid for.

Note the adjacent case: engineer capture (World.cs:929 `t.ReadyStructure = 0;`) DESTROYS the ready slot without refund; that is intentional flavour ('the crews flee with the blueprints') and must stay.

Add a scenario near the existing production scenario: CY, GrantCredits 5000, BuildStructure type 1, step until `ReadyStructure == 1`, record credits C, SellStructure the CY, assert credits == C + 300 (full plant refund) + 1500 (half CY cost).

- Acceptance: New scenario passes. Full runner gate exits 0. Golden hashes regenerated on both platforms via CI. Capture behaviour unchanged (existing capture scenario still passes untouched).

### D.3 Power and tech: where base-building depth actually lives

#### BD-06. Move the structure catalogue into /data (hash-neutral)

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false (edits /sim; MUST be hash-neutral)
- Canonical; supersedes TICKET-P5-DEF-11 (Appendix A), which is a strict subset.
- Files: data/buildings/*.yaml (new), data/schema.structure.json (new), sim/Ferrostorm.Sim/DataLoader.cs, sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim.Runner/Program.cs
- Spec:

TOUCHES /sim BUT MUST BE HASH-NEUTRAL: every value ported byte-identical, so golden hashes MUST NOT change - that is the acceptance test.

`/data/buildings/` is an empty directory; the catalogue is a hardcoded switch (World.cs:342-353) and the per-structure HP/draw/sight live scattered across SpawnPowerPlant/SpawnFactory/SpawnRefinery/SpawnConstructionYard/SpawnTurret/SpawnSuperweapon/SpawnVeilProjector/SpawnServiceDepot (World.cs:250-473). CLAUDE.md forbids hand-editing stats in code. DO THIS FIRST, before BD-07/BD-08/BD-12, so those become data edits.

1. Write data/schema.structure.json mirroring schema.unit.json's shape with keys: id, name, faction, cost, build_time_ticks, hp, power_supply, power_draw, sight_range, footprint, prerequisites, notes.
2. Write eight files using the EXACT current values, ids per the dir_/sod_/com_ convention already used by the sidebar icons: com_power_plant (type 1, cost 300, ticks 100, hp 150, supply 100, draw 0, sight 4), com_factory (2, 2000, 300, hp 1500, draw 40, sight 5), com_refinery (3, 2000, 300, hp 2000, draw 0, sight 6), com_construction_yard (4, 3000, ticks 0, hp 3000, draw 0, sight 6), dir_turret (5, 600, 150, hp 400, draw 20, sight 6), dir_superweapon (6, 4000, 600, hp 1200, draw 100, sight 4), sod_veil_projector (7, 1500, 250, hp 900, draw 60, sight 6), com_service_depot (8, 1200, 200, hp 1000, draw 30, sight 4). Note ticks 0 on the CY is load-bearing: World.cs:661 uses `BuildTicks <= 0` to refuse queueing it (MCV-deployed only).
3. Extend DataLoader with `StructureData` + `ParseStructure` reusing ParseFlatYaml/ParseInlineList exactly as ParseUnit does (culture-invariant integer maths only, no float/double - CLAUDE.md determinism rule).
4. Add `World.RegisterStructureType(int, StructureTypeDef)` mirroring RegisterUnitType including the `if (Tick != 0) throw` guard, convert `GetStructureType` from static to instance backed by a Dictionary seeded with the same compiled defaults (keep a static fallback or update the two external callers: Sidebar.cs:189 and SkirmishLive.cs:539, plus SkirmishAI.cs:110).
5. Extend StructureTypeDef to `(int Cost, EntityKind Kind, int BuildTicks, int Hp, int PowerSupply, int PowerDraw, int SightCells)` and have each SpawnX read from it instead of literals. Keep the `SpawnPowerPlant(supply, hp)` and `SpawnFactory(draw)` optional overrides - the runner scenarios pass them (Program.cs:586, 696).

**Doc 22 integration note:** DEF-11 (Appendix A) carries the same value table read independently from the same eight spawn methods and agrees on every number, plus a `Footprint = 2` field and a `WeaponId` field this ticket omits. Take DEF-11's two extra fields into BD-06's StructureTypeDef so DEF-03's FootprintOf and DEF-01's WeaponOfStruct can both source from the def rather than from a second hard-coded switch. DEF-11's warning is also worth carrying: every public Spawn* signature and every default value must be preserved exactly, because the runner scenarios pass named overrides (`hp: 1500` at Program.cs:586, `hp: 150` at 696, `chargeTicks: 90` at 818) and a silently changed default moves a golden with no diff to point at.

- Acceptance: THE GOLDEN HASHES ARE UNCHANGED - `git diff sim/golden-hashes.txt` is empty and determinism.yml is green on both platforms. Full runner gate exits 0 with zero scenario edits. A new round-trip test (mirroring the existing TICKET-P2-DATA-02 unit test at Program.cs:1322) asserts every loaded /data/buildings value equals the compiled default. The sim purity grep still passes (no float/double, no engine reference).

#### BD-07. Power becomes a real constraint: honest draw values

- Dimension: base building depth
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | **touchesSim: TRUE**
- Files: data/buildings/*.yaml (after BD-06), sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt
- Spec:

CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. Also a >15% stat change: CLAUDE.md charter A11 requires Balance + Game Designer co-sign before merge. DO BD-06 FIRST so this is a data edit.

Today a CY + 2 refineries + a factory draw 40 total (CY and Refinery set NO PowerDraw at all - World.cs:391-402 and 250-261), so one 300-credit plant powers an entire economy and power is decorative.

New draws: com_construction_yard 20 (was 0), com_refinery 40 (was 0), dir_superweapon 150 (was 100). Unchanged: com_factory 40, dir_turret 20, com_service_depot 30, sod_veil_projector 60, com_power_plant supply 100 / cost 300.

Resulting curve: opening base (CY+refinery+factory) = 100 draw => 1 plant; two refineries + 4 turrets = 220 => 3 plants; plus superweapon = 370 => 4 plants. That turns a 300-credit line item into a ~1200-credit commitment and makes plant-sniping and the GDD's 'sell power to sneak a superweapon' play real.

Set `PowerDraw` in SpawnConstructionYard and SpawnRefinery from the def (BD-06 wires this). Then re-derive the runner's power arithmetic: Program.cs ~195-277 (production rate scenario: a refinery is not present, so the 75/150-tick assertions should survive - verify, do not assume), ~811-840 (superweapon charge scenario reasons about EXACT supply/draw boundary equality with comments; its constants must be recomputed for draw 150), ~953-979 (veil brown-out), ~1214 (depot). SkirmishAI.cs:97 `supply < draw + 40 ? 1` is the AI's plant trigger and still holds with a +40 margin, but confirm the AI does not brown-out-lock in the ai-macro scenario.

- Acceptance: Full runner gate exits 0. NO scenario assertion may be deleted or loosened - only power-arithmetic constants may change; a reviewer diffs Program.cs to confirm this. Golden hashes regenerated on both platforms via determinism.yml. A new scenario asserts: CY+refinery+factory with one plant runs at rate 100; destroying the plant drops the rate to exactly 50 (World.cs:1428-1430). AI still reaches >= 4 structures by tick 3000 in the ai-macro scenario. Balance + Game Designer co-sign recorded before merge.

#### TICKET-P5-DEF-10. Sim: defensive turrets go offline below 75% power (GDD s5, unimplemented) and two scenarios it exposes

- Dimension: defensive structures and barriers
- Impact: HIGH | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (genuinely regenerates goldens; the only ticket in the defence set that cannot be hash-neutral)**
- Canonical; supersedes BD-08 (Appendix A). DEF-10 found the two shipped scenarios BD-08 would have broken.
- Files: sim/Ferrostorm.Sim/World.cs; sim/Ferrostorm.Sim.Runner/Program.cs; sim/golden-hashes.txt
- Spec:

TOUCHES SIM AND THIS ONE GENUINELY REGENERATES GOLDENS - it is the only ticket in this set that cannot be hash-neutral, because it changes behaviour in scenarios that already exist. Architect sign-off required.

THE GAP: docs/design/02-game-design-document.md line 48 states "defensive turrets go offline at <75%" power. CombatSystem (World.cs:945-1060) contains no power check of any kind; a turret at zero supply fires happily. The GDD's power coupling is implemented for production speed (World.cs:1428-1430), for the superweapon charge (World.cs:1404), for the veil (World.cs:874) and for the service depot (World.cs:1386) - the turret is the one place it was missed.

1. At the top of CombatSystem, before the main loop, add the same supply and draw tally the other two systems already use verbatim (World.cs:853-859 and 1349-1357): `Span<int> supply = stackalloc int[_players]; Span<int> draw = stackalloc int[_players]; for (int i = 0; i < _entities.Count; i++) { var e = _entities[i]; if (!e.Alive || e.PlayerId < 0) continue; supply[e.PlayerId] += e.PowerSupply; draw[e.PlayerId] += e.PowerDraw; }`. One extra O(n) pass per tick; measure it against the 8 ms budget rather than assuming.
2. In the main loop, immediately after the existing `if (!e.Alive || e.WeaponId == 0) continue;` at line 957, add: `// GDD s5: a browned-out base cannot power its guns. Integer maths only. if (e.Kind == EntityKind.Turret) { int p = e.PlayerId; int pct = draw[p] <= 0 ? 100 : 100 * supply[p] / draw[p]; if (pct < 75) { _entities[i] = e; continue; } }`. Do NOT tick the cooldown down while offline - a dead turret does not reload; the `continue` above the cooldown decrement at line 958 achieves this. Do NOT generalise to all armed structures: the GDD says defensive turrets, and DEF-12/DEF-13's new emplacements should be added to this guard by kind as they land.
3. TWO SHIPPED SCENARIOS BREAK, BOTH VERIFIED BY HAND - fix both in this ticket. ScenarioVeil (Program.cs:950-981) does `world.SpawnTurret(1, 20, 20)` at line 955 for player 1, who owns NO power plant (the only plant, line 956, belongs to player 0). Player 1's pct is 0, the turret goes offline, the baseline rifle at line 961-963 is never engaged, and `throw new Exception("veil: baseline rifle was never engaged")` fires - CI goes red. FIX: add `world.SpawnPowerPlant(1, 24, 24);` (or any legal anchor for player 1 clear of the turret's 2x2 at 20,20 and the projector at 26,20) before line 955, and update the scenario's report string to note the turret is powered. ScenarioStealth (Program.cs:561-610) is subtler and worse: `world.SpawnTurret(0, 20, 20)` at line 575 runs Rule 1 for 100 ticks BEFORE the plant arrives at line 586, so player 0's pct is 0 and the turret is dead. Rule 1's assertion ("undetected raider was shot") would still PASS - for entirely the wrong reason, because the turret is offline rather than blind, silently voiding the test. FIX: move a power plant spawn above line 575 (e.g. `world.SpawnPowerPlant(0, 24, 24);`) so the turret is powered throughout, keeping the existing line 586 plant for its Rule 2 geometry. Audit every other scenario that calls SpawnTurret for its owner's supply-versus-draw balance at every tick range and record the audit in the report strings.
4. Regenerate goldens: run `golden 2026`, replace the affected lines in sim/golden-hashes.txt (expect at minimum `stealth` and `veil`; `skirmish`, `aisuper`, `mission02` and `mission03` may move if any turret ever browns out in them - do not guess, diff). Keep LF line endings (Program.cs:1403).
5. The AI already builds exactly one turret regardless of power (SkirmishAI.cs:99, `!hasTurret ? 5`) and its `wanted` ladder does check `supply < draw + 40` at line 97 before turrets, so it will usually be fine; DEF-14 tightens this.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. Full gate `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 - specifically ScenarioVeil and ScenarioStealth must PASS, not merely run. `dotnet run ... -- determinism 2026` exits 0 on both ubuntu-latest and windows-latest. sim/golden-hashes.txt shows ONLY modified lines for scenarios whose turret power state actually changed, each change explained in the commit message with the tick at which the brown-out occurs - an unexplained hash change is a defect, not a rebase. A new assertion inside ScenarioWalls or ScenarioStealth: a turret whose owner has supply 0 and draw 20 does not damage a stationary enemy unit parked inside its range 5 across 100 ticks; the same turret with a power plant added damages it within 30 ticks; a turret whose owner has supply 15 and draw 20 (pct 75) DOES fire, and at supply 14 (pct 70) does not - the boundary is tested on both sides. The `match` perf report stays under the 8 ms budget with the extra O(n) power pass. Purity grep clean (integer maths only: `100 * supply[p] / draw[p]`, no float). ADR or Architect sign-off recorded, noting the replay-compatibility break.

#### BD-09. Radar Uplink structure; the minimap goes dark without it

- Dimension: base building depth
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: true | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/World.cs, data/buildings/com_radar_uplink.yaml (new), game/scripts/Sidebar.cs, game/scripts/SkirmishLive.cs, game/scripts/Minimap.cs, game/scripts/ModelLibrary.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt
- Spec:

CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. GDD s5 line 48 says 'radar goes dark' but no radar concept exists, and the minimap is unconditional (SkirmishLive.cs:527). Adding radar completes GDD s5, grows the catalogue to 9, and makes killing power plants blinding rather than merely slowing.

1. Sim: add `Radar = 11` to EntityKind (World.cs:39 - append, do not renumber: EntityKind is serialized at World.Serialization.cs and cast from `(int)v.Kind` at SkirmishLive.cs:655). Add `9 => new StructureTypeDef(900, EntityKind.Radar, 200)` to GetStructureType. Add `SpawnRadar(int player, int ax, int ay)` copying SpawnServiceDepot's shape with StructType = 9, Hp 800, PowerDraw 80, Sight 10. Add `EntityKind.Radar` to `IsStructure` (World.cs:719-722) - MISSING THIS SILENTLY BREAKS sell, repair, capture, rubble-unblock, placement adjacency AND the VictorySystem short-game rule. Add the `case EntityKind.Radar: SpawnRadar(...)` arm to the PlaceStructure switch (World.cs:644-654).
2. Data: com_radar_uplink.yaml per BD-06's schema.
3. Client minimap gating: in SkirmishLive._Process, compute `bool radarLive = <own living Radar exists> && supply >= draw;` reusing the supply/draw already tallied at lines 499-501; pass it to `_minimap.Refresh`. When false, Minimap._Draw renders only the cinder panel plus centred bone text 'RADAR OFFLINE' - no terrain, no dots, no frustum, no fog. Pings (Minimap.Ping) still render, so the base-under-attack ping survives a blackout (this is deliberate: the classic never hides the attack warning).
4. Sidebar: add `new("RADAR UPLINK", 9, 900, "com_radar_uplink")` to the Structures array.
5. ModelLibrary/Sidebar icon: no com_radar_uplink.glb exists - use sod_veil_projector.glb as the interim model (it already carries a `dish` child that ScanRig at SkirmishLive.cs:132 spins for free) and raise a separate art ticket for the real asset.
6. SkirmishAI.cs:94-101: insert radar into the `wanted` chain before the superweapon - `: !hasRadar && w.Credits(_player) >= 1500 ? 9` - and add a `hasRadar` flag to the entity scan at lines 55-66, or the AI will never build one.

**Doc 22 CONFLICT, resolve before implementing:** this ticket claims `EntityKind.Radar = 11` and struct type 9. DEF-04 claims `EntityKind.Wall = 11` and struct type 9 for the wall. Both cannot be right. ADR-005 (DEF-02) owns the final numbering for the whole roster; whichever of Wave B and BD-09 lands second must take the next free numbers. Recommended allocation, to be ratified in ADR-005: EntityKind Wall = 11, Emplacement = 12, Bastion = 13, Radar = 14, Outpost = 15; struct types 9 wall, 10 emplacement, 11 bastion, 12 gate (reserved), 13 radar, 14 outpost. An EntityKind collision is silent in the hash and fatal in the save format.

> **RESOLUTION (2026-07-17).** Settled in compiled code; the paragraph above is kept as history. The shipped enum (World.cs:46) runs Wall = 11, Barracks = 12, RadarUplink = 13, Airfield = 14, Emplacement = 15, Bastion = 16, Outpost = 17, and the compiled struct types are 9 wall, 10 gate (reserved, deliberately no def), 11 barracks, 12 radar uplink (World.cs:447-459). The allocation recommended above was overtaken. ADR-009 (Proposed) carries the roster design and supersedes this site's numbering.

- Acceptance: New scenario: player with CY+plant+refinery+factory queues type 9, places it, radar exists with draw 80. Full runner gate exits 0; goldens regenerated on both platforms. In-game: with no radar the minimap reads RADAR OFFLINE; building one restores it; selling the plants (supply < draw) blanks it again; a base-under-attack ping still shows while blanked. AI builds a radar before a superweapon in a 5000-tick ai-macro run.

#### BD-10. Segmented power bar in the sidebar with brown-out state

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/Sidebar.cs, game/scripts/SkirmishLive.cs
- Spec:

Pure client. GDD s5: 'total supply vs draw shown as a bar'. Reality: a text string at SkirmishLive.cs:502 and a field literally named `_power` that renders `CREDITS {n}  CY Q1  FAC Q2` (Sidebar.cs:207), duplicating the HUD's own credits readout.

1. Change `Sidebar.Refresh` to take `int supply, int draw` and pass the values already tallied at SkirmishLive.cs:499-501.
2. Replace the `_power` Label with a VBox: a Label reading `$"POWER {supply} / {draw}"` in bone (#d6d2c4), above a 174x8 Control drawing two stacked ColorRects. Scale: `int span = Mathf.Max(supply, draw, 1);` background = cinder (#16181a) full width; supply rect width = `174f * supply / span`; a 2px seam-coloured (#2e3236) vertical tick at `174f * draw / span` marks the draw line - the classic supply-bar-with-demand-marker.
3. Colour the supply rect by headroom, using existing doc-16 tokens only: ferrite gold (0.79,0.63,0.36) when `supply >= draw`; amber (0.90,0.68,0.22 - the existing FillAmber value) when `supply * 4 >= draw * 3` (>=75%, still powering turrets after DEF-10); the existing FillRed (0.85,0.28,0.20) below 75%.
4. When below 75%, pulse the label's modulate alpha 1.0<->0.45 on a 0.8s looping Tween created once and killed on recovery - copy the exact lifecycle pattern of `_placePulse` (Sidebar.cs:171-184), including nulling the field on kill, or the tween leaks across state changes.
5. Delete the credits duplication from line 207; move the `CY Q{n} FAC Q{n}` counters onto the section headers instead.

No change to docs/design/16-visual-style.md is needed: every colour above is an existing token.

- Acceptance: Build a factory (draw 40) with one plant (supply 100): bar shows gold fill at ~100/100 of span with a draw tick at 40%. Add turrets past 100 draw: fill turns red, label pulses, tick sits past the fill end. Sell a turret back under 75%: pulse stops and the tween is killed (assert no orphan tween by toggling 20 times without leaking). Credits appear exactly once on screen.

#### BD-17. Honour the tech tree that /data already carries

- Dimension: base building depth
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Canonical for the prerequisite mechanism; supersedes TICKET-P5-DEF-13's PrereqOf clauses (Appendix A). DEF-13's bastion survives as DEF-13b below.
- Files: sim/Ferrostorm.Sim/DataLoader.cs, sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim/SkirmishAI.cs, data/buildings/*.yaml, data/units/*.yaml, docs/questions/ (new), sim/golden-hashes.txt, sim/Ferrostorm.Sim.Runner/Program.cs
- Spec:

CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. Also needs Game Designer sign-off on the prereq table (it IS the tech tree). Do BD-06 and BD-09 first.

Today there is no tech tree of any kind: `BuildStructure` checks only cost/ticks plus one hardcoded Sodality veil check (World.cs:657-666), `Produce` checks only cost and faction (World.cs:702-714) - you can queue a 4000-credit superweapon from a bare CY with no power plant. Yet every /data/units/*.yaml carries `prerequisites:`, DataLoader parses it (DataLoader.cs:123), and `UnitCatalogue.ToTypeDef` (DataLoader.cs:145-152) silently DROPS it because `World.UnitTypeDef` has no such field.

1. Add `int[] Prereqs` (structure type ids, default empty) to both `UnitTypeDef` (World.cs:278) and `StructureTypeDef` (World.cs:341) as a trailing defaulted parameter.
2. Add a string->type-id map in UnitCatalogue: com_power_plant=1, com_factory=2, com_refinery=3, com_construction_yard=4, dir_turret=5, dir_superweapon=6, sod_veil_projector=7, com_service_depot=8, com_radar_uplink=9; throw FormatException on an unknown id, exactly as WeaponIdOf does (DataLoader.cs:142). Wire it through ToTypeDef.

   **RESOLUTION (2026-07-17).** The type-id map above predates the shipped catalogue and its radar number is stale: com_radar_uplink compiled as struct type 12, not 9, alongside com_wall at 9 and com_barracks at 11 (World.cs:447-459); EntityKind is World.cs:46's enum. Rebuild this map against ADR-009 (Proposed), which carries the roster design.
3. `private bool HasPrereqs(int player, int[] req)`: for each required type id, scan `_entities` in index order for a living entity with `PlayerId == player && IsStructure(Kind) && StructType == id`; all must be present. StructType is reliably set on all 8 spawns (verified World.cs:258/385/398/412/429/443/456/470). Iteration is index-ordered so it is deterministic.
4. Gate both `Produce` (after the faction check, World.cs:707) and `BuildStructure` (after the veil check, World.cs:663) with `if (!HasPrereqs(c.PlayerId, def.Prereqs)) break;` - a silent refusal, matching every other invalid command (ADR-001: the sim must not gain a rejection event; doc 18 N9 assigns denial feedback to client-side pre-validation).
5. Structure prereq table (GAME DESIGNER MUST SIGN OFF - this is the tech tree, not a mechanical value): power_plant [], refinery [power_plant], factory [refinery], turret [power_plant], service_depot [factory], veil_projector [power_plant], radar_uplink [factory], superweapon [radar_uplink].
6. SkirmishAI.cs:94-101 `wanted` chain is plant->refinery->factory->plant->refinery->turret->superweapon, which satisfies this table EXCEPT the superweapon: with BD-09's radar inserted the chain is legal, but WITHOUT BD-09 the AI stalls forever queueing a superweapon it can never build. BD-09 is therefore a hard dependency, not a nice-to-have.
7. GDD CONFLICT, DO NOT SILENTLY RESOLVE: GDD s5 line 47 says MCVs need a 'Tech Centre', which does not exist; data/units/com_mcv.yaml says `prerequisites: [com_factory]`. Implement the data as it stands (com_factory) and file a question in docs/questions/ with owner game-designer and a decide-by date, per CLAUDE.md workflow rule 5 - the candidate resolution is that the Radar Uplink absorbs the Tech Centre role.
8. Client (doc 18 N9): Sidebar hides prereq-unmet items rather than greying them - the exact precedent already in place at Sidebar.cs:80-84 ('disallowed items are absent, not greyed - progression should read as the tree growing'). Extend that to a live prereq check, not just MatchConfig.

cheapModelSafe=false: the prereq table is design authority, the AI chain interacts with it non-obviously, and the GDD conflict needs a human decision rather than a guess.

**Doc 22 integration note:** DEF-13 (Appendix A) independently proposed a hard-coded `PrereqOf` switch with a slightly different table (superweapon [factory], veil [factory], depot [factory], bastion [factory, service_depot]). Its table is superseded by clause 5 above, but two of its findings are worth carrying: (a) its warning that `return` rather than `break` in the BuildStructure case silently skips the shared entity writeback at World.cs:716, so use `break` and wrap the prereq loop in a local function returning bool; (b) its observation that golden-neutrality is NOT automatic here and each of ScenarioSuperweapon, ScenarioAiSuper, ScenarioVeil and ScenarioDepot must be read first to see whether it builds a gated structure through BuildStructure or spawns it directly (a direct spawn never runs the gate and its golden is safe).

- Acceptance: Full runner gate exits 0; goldens regenerated on both platforms. New scenario: a bare CY queueing type 6 (superweapon) is refused - QueueLength stays 0 and credits are untouched; after plant+refinery+factory+radar exist, the same command is accepted. Unit round-trip test asserts every /data/units prerequisites list survives into UnitTypeDef.Prereqs (currently it is dropped). AI reaches a superweapon within a 6000-tick ai-macro run and never deadlocks. docs/questions/ carries the Tech Centre question with an owner and decide-by date.

#### TICKET-P5-DEF-12. Sim: anti-infantry emplacement - the missing half of static defence

- Dimension: defensive structures and barriers
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (constructed hash-neutral)**
- Files: sim/Ferrostorm.Sim/Combat.cs; sim/Ferrostorm.Sim/World.cs; sim/Ferrostorm.Sim.Runner/Program.cs; art/3d/builder.py; game/scripts/ModelLibrary.cs; game/scripts/Sidebar.cs
- Spec:

TOUCHES SIM: new structure type and new reachable behaviour, Architect sign-off required; constructed to leave all existing goldens byte-identical because nothing existing can build it.

THE GAP, measured: the single turret carries WeaponId 4 (TurretGun, Combat.cs:49 - range 5, damage 35, AntiArmour, cooldown 12). AntiArmour versus None armour is 40% (Combat.cs:17-18), so it deals 14 x 1.25/s = 17.5 dps to infantry, while three rifle squads costing exactly one turret's 600 credits deal 16.9 dps back and the turret wins by a hair. There is no cheap anti-infantry defence and no reason to want one; the defensive roster is a roster of one.

1. Combat.cs: add `public static readonly WeaponDef EmplacementGun = new(Fix64.FromInt(4), 22, Warhead.AntiInfantry, 6);` after VanguardGun (line 58) and `8 => EmplacementGun,` to the Weapons.Get switch (line 60-70). ADDING A SWITCH ARM IS GOLDEN-NEUTRAL: existing ids resolve identically, and the World.cs:276 comment already establishes that the weapon and unit catalogues are static config and "not part of the state hash". Numbers: 22 x 100% AntiInfantry-vs-None x 2.5/s = 55 dps to infantry (three times the turret's), but 22 x 25% AntiInfantry-vs-Heavy x 2.5/s = 13.75 dps to armour (versus the turret's 43.75), and range 4 versus the turret's 5 - it shreds squads and is nearly useless against tanks, which is the whole point and is the mirror of the vanguard car's design (dir_vanguard_car, unit type 12, weapon 7).
2. World.cs: append `Emplacement = 12` to EntityKind (after DEF-04's `Wall = 11`; NEVER renumber). Add `10 => new StructureTypeDef(400, EntityKind.Emplacement, 100),` to GetStructureType - cost 400 versus the turret's 600, build 100 ticks versus 150, so it is the cheap early answer to an infantry rush. NOTE: DEF-03 reserved struct type 10 for a gate in FootprintOf; renumber the gate to 11 there and use 10 here, OR give the emplacement type 11 - pick one, state it in ADR-005, and make FootprintOf agree, because a footprint mismatch is silent and fatal. Add SpawnEmplacement modelled exactly on SpawnTurret (World.cs:449-460): Hp 250, Armour Structure, WeaponId 8, Sight 5, PowerDraw 15, StructType per the choice above, 2x2 footprint. Add its `case EntityKind.Emplacement: SpawnEmplacement(...)` arm to the PlaceStructure spawn switch (World.cs:644-654) and its arm to MapLoader.cs BuildWorld (line 165-175).
3. Add EntityKind.Emplacement to IsStructure (World.cs:719) - it is a real building: blocks, sells for 200, repairs, gives hope in VictorySystem, is capturable by engineers, is auto-acquired. It is NOT a barrier, so it must NOT go into IsBarrier.
4. If DEF-10 has landed, add it to the 75% power guard: `if (e.Kind == EntityKind.Turret || e.Kind == EntityKind.Emplacement)` - a defensive turret by any name.
5. art/3d/builder.py: add `com_emplacement` following the dir_turret precedent (line 427-441) but visibly SHORTER and WIDER - a squat sandbagged pit rather than a tower, so the silhouette test (doc 16) tells the player which gun they are walking into at 40 pixels. Use the com_ prefix and field olive per the shared-hardware convention. Register in BUILDERS, export via export_glb.py.
6. ModelLibrary.cs KindModel dict (line 19-25): add the new kind id to `com_emplacement`.
7. Sidebar.cs Structures array (line 18-26): add `new("EMPLACEMENT", 10, 400, "com_emplacement")` between POWER PLANT and TURRET, and add its name to SkirmishLive.cs StructNames (line 563) - that array is indexed by struct type and currently reads `{ "", "POWER PLANT", "FACTORY", "REFINERY", "STRUCTURE", "TURRET", "SUPERWEAPON", "STRUCTURE", "SERVICE DEPOT" }`, so it must be extended in index order or the toast shows the wrong name. Note in passing that indices 4 and 7 read "STRUCTURE" where they should read CONSTRUCTION YARD and VEIL PROJECTOR - fix while you are there.
8. Add an emplacement phase to a runner scenario asserting the counter shape holds.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. `golden 2026` diffs clean against sim/golden-hashes.txt and `git diff --exit-code sim/golden-hashes.txt` returns 0 - nothing existing can build type 10, and adding a Weapons.Get arm and an appended EntityKind value are both invisible to the hash. Full gate exits 0; purity grep clean. Runner assertions: an emplacement kills a rifle squad (100 hp, None armour) in under 30 ticks at range 3 and fails to kill a cannon tank (300 hp, Heavy) in 300 ticks; a turret does the reverse; both are refused placement outside build radius and accepted inside; an emplacement sells for exactly 200; a player whose last structure is an emplacement is NOT eliminated (it is a real building, unlike a wall). Godot imports com_emplacement.glb and the sidebar shows it. tools/Ferrostorm.Balance's static-defence section (DEF-17) gains an emplacement row asserting rifle squads lose to it per-cost and cannon tanks beat it per-cost. NEEDS A HUMAN: the silhouette read at RTS distance against docs/design/16-visual-style.md. Balance + Game Designer co-sign on cost 400 / hp 250 / 55 dps versus infantry per CLAUDE.md A11. Architect sign-off.

#### DEF-13b. Tier-2 turret (the Bastion), rebased onto BD-17's data-driven prereqs

- Dimension: defensive structures and barriers
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | **touchesSim: TRUE (constructed hash-neutral)**
- Derived from TICKET-P5-DEF-13 clause 4; its clauses 1-3 (the hard-coded PrereqOf switch) are superseded by BD-17 and preserved in Appendix A.
- Depends on: BD-06 (catalogue in /data), BD-17 (prereq mechanism), DEF-02 (final struct-type numbering)
- Files: sim/Ferrostorm.Sim/Combat.cs; sim/Ferrostorm.Sim/World.cs; data/buildings/dir_bastion.yaml (new); sim/Ferrostorm.Sim.Runner/Program.cs; game/scripts/Sidebar.cs; art/3d/builder.py
- Spec:

TIER-2 TURRET. There is no progression in the defensive roster: one turret, buildable at tick one, forever. The Bastion is the upgrade, and the upgrade path is the classic one and needs no new command verb: sell the turret for 300 and build a bastion for 1200, gated behind factory plus service depot. That gating is the first thing that gives the service depot (1200 credits, currently a pure field-repair convenience) a reason to exist in a build order.

1. Combat.cs: add `public static readonly WeaponDef BastionGun = new(Fix64.FromInt(7), 60, Warhead.AntiArmour, 15);` and `9 => BastionGun,` to Weapons.Get - range 7 (outranges every unit except the howitzer's 9), 60 x 100% versus Heavy x 1/s = 60 dps. Adding a Weapons.Get arm is golden-neutral (World.cs:276 establishes the catalogues as static config outside the hash).
2. World.cs: append `Bastion = 13` to EntityKind (or the next free value per ADR-005's ratified allocation - do NOT renumber), add `11 => new StructureTypeDef(1200, EntityKind.Bastion, 250),` to GetStructureType (again, per ADR-005's allocation), add SpawnBastion on the SpawnTurret pattern with Hp 700, Armour Structure, WeaponId 9, Sight 8, PowerDraw 50; add it to IsStructure, to the PlaceStructure spawn switch, to MapLoader's switch, and to DEF-10's 75% power guard.
3. Prerequisites come from data, not from a switch: dir_bastion.yaml declares `prerequisites: [com_factory, com_service_depot]` and BD-17's HasPrereqs enforces it. This is the whole reason DEF-13 was split.
4. The howitzer (range 9) still outranges the bastion (range 7), so GDD s6 line 53 ("artillery beats static defence") survives - assert that in DEF-17's balance gate rather than asserting it in prose.
5. Sidebar.cs: add `new("BASTION", 11, 1200, "dir_bastion")` and hide it until buildable, matching the existing tech-gating comment at Sidebar.cs:80-82 ("disallowed items are absent, not greyed - progression should read as the tree growing") - this is the first time that stated principle gets real prerequisites to express. Requires the client-side prereq read BD-17 clause 8 adds.
6. Art for dir_bastion follows DEF-07/DEF-12's builder.py conventions: dir_ prefix, Directorate slab language, visibly taller and heavier than dir_turret so the silhouette says "this one outranges you".
7. SEMANTICS TO STATE EXPLICITLY rather than leave emergent (carried from DEF-13's acceptance): killing the prerequisite mid-build does NOT cancel an already-queued item. The gate is on queueing. Record that in ADR-005 or a docs/questions entry.

- Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0; full gate exits 0; purity grep clean. `git diff sim/golden-hashes.txt` is EMPTY - nothing existing can build the bastion, and the Weapons.Get arm plus the appended EntityKind are both invisible to the hash. Runner assertions: a bastion requires both a factory and a service depot (refused with either missing, accepted with both); a bastion at range 6 damages a cannon tank that cannot reach it (tank range 4); a howitzer at range 8 kills a bastion without being hit; a bastion whose owner is below 75% power does not fire (DEF-10 guard). Sidebar assertion: the BASTION button is invisible until both prerequisites stand. DEF-17's static-defence gate gains a bastion row proving the howitzer still breaches. Balance + Game Designer co-sign per CLAUDE.md A11. Architect sign-off.

---
### D.4 Production and placement UX

#### BD-11. Placement ghost uses the real structure model, not a grey box

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Depends on: DEF-01 (land DEF-01 first; preserve its ring child and its cached-material fix)
- Files: game/scripts/SkirmishLive.cs
- Spec:

Pure client. `_ghost` is `new BoxMesh { Size = new Vector3(2, 0.5f, 2) }` (SkirmishLive.cs:222-227) - the player places blind, with no idea what the building looks like or which way it faces. There are 21 baked .glb models in game/assets/models and every structure has one. Also fixes a per-frame allocation bug: SkirmishLive.cs:552-556 constructs a NEW StandardMaterial3D every single frame while the ghost is up.

1. Cache two statics: `GhostOk` = StandardMaterial3D { AlbedoColor = new Color(0.3f,0.9f,0.4f,0.4f), Transparency = Alpha, ShadingMode = Unshaded, NoDepthTest = false } and `GhostBad` with (0.9f,0.25f,0.2f,0.4f).
2. In `EnterPlacement(int structType)` (line 271): free any existing ghost node, then `_ghost = _models.Instantiate((int)World.GetStructureType(structType).Kind, 0);` - the same call SyncActors uses at line 655 - add it as a child, and recursively walk its children setting `MaterialOverride` on every MeshInstance3D found (reuse the ScanRig recursion shape at lines 125-137 as the pattern). Also add a 2x2 footprint quad at y=0.02 under it so the exact occupied cells are unambiguous.
3. In _Process (544-557) replace the per-frame material construction with a cached `bool _ghostOk` and only reassign MaterialOverride across the whole subtree WHEN VALIDITY CHANGES.
4. On place/cancel (lines 930 and 1020) free the model ghost and null the field; recreate on the next EnterPlacement.
5. The ghost must not cast shadows: set `CastShadow = Off` on each MeshInstance3D, or a green transparent building throws a solid shadow.

**Doc 22 integration note:** DEF-01 already hoists the two ghost materials to statics (its clause 9) and attaches a range ring as a child of `_ghost` (its clause 4). Landing DEF-01 first makes step 1 here a no-op and step 3 half-done. Do not reintroduce the per-frame allocation, and do not free the ring child when swapping the ghost mesh: re-parent it onto the new model node. DEF-01 clause 7 also generalises the ghost position to `ax + f/2f`; the 2x2 footprint quad in step 2 must use the same `f = World.FootprintOf(structType)` so a 1x1 wall ghost shows a 1x1 quad.

- Acceptance: Click PLACE for a REFINERY: the actual com_refinery model follows the cursor, green over legal cells and red over illegal ones, with a visible 2x2 footprint quad. Placing it spawns a building in the exact pose the ghost showed. Escape cancels and frees the node (assert `GetChildCount` returns to its pre-placement value). Profile: zero StandardMaterial3D allocations per frame while the ghost is up. The ghost casts no shadow.

#### BD-12. Build-radius overlay during placement

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs, game/scripts/BattlefieldView.cs
- Spec:

Pure client, read-only sim queries. The ghost tints red outside the radius but the player must hunt for the edge by sweeping the mouse - the strict-adjacency rule (GDD Q2, the core of base layout as strategic expression) is invisible. Show the buildable region while placing.

1. Do NOT call `ValidPlacement` per cell - it is O(entities) and 96x64 cells would be ~1.2M ops. Compute the RADIUS UNION only, which is exactly what 'build radius' means: for each own living structure, mark the Chebyshev square of `o.Kind == ConstructionYard ? World.CyBuildRadius : World.BuildRadius` around its anchor `(Map.CellOf(o.X) - 1, Map.CellOf(o.Y) - 1)` - mirroring World.cs:764-771 exactly.
2. Rasterise into an `Image` of Format.R8, size map.Width x map.Height (96x64), 255 inside the union and 0 outside; wrap in an ImageTexture.
3. Render as ONE unshaded transparent quad the size of the map at y = 0.05, with a shader that samples the mask, multiplies by ferrite gold (#c9a86a) at alpha 0.10, and adds a brighter 1-cell rim where the mask transitions (sample the 4 neighbours in the fragment shader) - the rim IS the readable part.
4. Rebuild the mask only when `_placingType` transitions to > 0 and thereafter every 15 ticks while placing (structures can complete mid-placement); hide the quad when `_placingType == 0`.
5. The mask deliberately shows RADIUS, not full validity - blocked cells and standing units still show red on the ghost. That is the classic behaviour and keeps this cheap.

Colour is an existing doc-16 token; no style-bible amendment needed.

**Doc 22 integration note:** DEF-03 replaces the `Map.CellOf(o.X) - 1` idiom with `World.AnchorOf(o.X, o.StructType)`; step 1 must use AnchorOf once DEF-03 has landed, or the overlay silently misplaces the radius square for any 1x1 structure. DEF-04 clause 8 also adds the barrier radius rule (walls project BarrierBuildRadius 2 for other barriers only, and never anchor a real building); once walls exist, the mask must be computed for the CANDIDATE type, not globally, or the overlay will promise a factory can go where only a wall can.

- Acceptance: Enter placement with only the starting CY: a gold-rimmed region exactly 15x15 cells (CyBuildRadius 7 => anchor +/-7) appears centred on the CY anchor. Place a power plant at the edge: the region grows by that plant's 11x11 (BuildRadius 5) square within one second. Cancel: the overlay disappears. Frame time impact under 0.5ms at 96x64 (mask rebuild at 1Hz, one draw call).

#### BD-13. Primary building: make second and third factories actually work

- Dimension: base building depth
- Impact: TRANSFORMATIVE | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs, game/scripts/Sidebar.cs, game/scripts/BattlefieldView.cs
- Spec:

Pure client - the sim already supports N independent queues (`_queues` is keyed per producer id, World.cs:309), the client just cannot reach them. `FindOwnStructure` returns the FIRST match (SkirmishLive.cs:244-249) and `_yardId`/`_factoryId` are recomputed from it every frame (530-531), so `QueueUnit` always targets factory #1 and a second factory is inert scenery. This is the single biggest base-building gap: there is currently no reason to build a second production structure.

1. Add `private int _primaryYard = -1, _primaryFactory = -1;`.
2. In `FinishSelect` (line 979), when the click selects exactly one own structure of Kind Factory or ConstructionYard, set the matching primary field to that id and play `ui_confirm`.
3. Replace lines 530-531 with: keep the primary if `_latest.ContainsKey(_primaryX)` and it is alive and the right kind; otherwise fall back to `FindOwnStructure(kind)` (the current behaviour) so a destroyed primary self-heals.
4. Mark the primary visually with a ferrite-gold (#c9a86a) ground ring - reuse `BattlefieldView.AddSelRing`'s construction but with a distinct node name 'PrimaryRing' and radius 1.8; add on promotion, free on demotion. DELIBERATELY NOT team colour: doc 16's one-place law reserves that for the silhouette band, so gold (the UI/instrument token) is correct here and no style-bible amendment is needed.
5. Sidebar: show which producer is targeted - on the UNITS header, append `$" -> FACTORY {n}"` where n is the primary's 1-based index among own factories in id order.
6. `Refresh` already takes `facQ`/`yardQ`; they now come from the primary, so per-factory queues display correctly for free.
7. Rally (BD-14) is per-structure and independent of primary - do not couple them.

**Doc 22 note on clause 4's reasoning:** the section 5 amendment moves doc 16 from a one-place law to a two-place law (silhouette plus ground plate). That does not change this ticket's conclusion - gold is still correct for PRIMARY, because the two permitted places are both *team* marks and PRIMARY is not a team mark, it is an instrument readout. Keep the gold ring and keep the reasoning, but update the comment to cite the amended law rather than the old one.

- Acceptance: Build a second factory. Select it: a gold PRIMARY ring appears on it, the ring leaves factory #1, the sidebar UNITS header reads `-> FACTORY 2`. Queue a CANNON TANK: it exits factory #2 (assert the spawn position is within 3 cells of factory #2, matching World.SpawnOffsets). Sell factory #2: the primary silently falls back to factory #1 and queueing still works. Same flow for two Construction Yards after an MCV expansion.

#### BD-14. Rally points: correct producer attribution, per-structure markers

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false (edits /sim; HASH-NEUTRAL - GameEvents are not hashed)
- Files: sim/Ferrostorm.Sim/World.cs, game/scripts/SkirmishLive.cs
- Spec:

TOUCHES /sim BUT HASH-NEUTRAL - GameEvents are explicitly NOT part of the state hash (World.cs:1569-1620 never reads `_events`; the header comment at World.cs:146-151 states this), so NO golden regeneration is needed. Verify with `git diff sim/golden-hashes.txt` being empty. This is the honest solution doc 18 M5 asks for, without extending the wire/replay command format.

Today: rally exists only for Factory (SkirmishLive.cs:1090), and because `ProductionComplete` carries no producer id the client guesses by POSITION PROXIMITY within 4 cells while iterating a Dictionary in nondeterministic order (SkirmishLive.cs:426-441) - two factories within 4 cells cross-wire, and one global `_rallyMarker` node means only the last rally is ever visible.

1. Sim: add a trailing field to the record struct at World.cs:166 - `public readonly record struct GameEvent(GameEventType Type, int A, int B, Fix64 X = default, Fix64 Y = default, int C = -1);` (trailing default = every existing construction site compiles unchanged). Set `C = i` (the producer's entity index) at the two ProductionComplete emissions: World.cs:1454 (CY, where B is the struct type) and World.cs:1470 (Factory, where B is the unit type). Update the doc comment at World.cs:151 to document C = producing structure.
2. Client: delete the proximity loop at SkirmishLive.cs:426-441 and replace with `if (ev.Type == ProductionComplete && _rally.TryGetValue(ev.C, out var rp) && ev.A >= 0 && ev.A < _world.EntityCount)` then issue the same PathMove - exact attribution, no Dictionary iteration, deterministic.
3. `_rally` stays keyed by structure id and is client-side (lockstep-legal: it emits an ordinary PathMove through `_pending`, the existing precedent).
4. Replace the single `_rallyMarker` with `Dictionary<int, MeshInstance3D> _rallyMarkers`: one marker per rallied structure, visible ONLY while that structure is selected (classic behaviour), freed when the structure dies (hook the existing cleanup at SkirmishLive.cs:770).
5. Widen the eligible kinds at line 1090 from `sv.Kind == EntityKind.Factory` to `sv.Kind is EntityKind.Factory or EntityKind.ServiceDepot` - the depot rally is where repaired units gather, and it is free once attribution is right. Leave ConstructionYard out: structures are placed, not rallied.
6. KNOWN LIMITATION TO RECORD, NOT FIX HERE: `_rally` is client state, so it does not survive save/load and would not replicate in a future networked game. The durable fix is a sim-side SetRally command, which extends the wire and replay format and needs an ADR (doc 18 M5). File that as a question in docs/questions/ rather than resolving it here.

**Doc 22 integration note:** P5-ECON-13 clause 4 also emits a ProductionComplete for the refinery's free harvester. That emission is not from a producer queue, so its `C` must be set to the refinery's entity index for BD-15/P5-ECON-07's rally-precedence check to work. State it in whichever ticket lands second.

- Acceptance: `git diff sim/golden-hashes.txt` is EMPTY and the full runner gate exits 0 - proving the event change is hash-neutral. Build two factories 3 cells apart, rally each to opposite corners: every unit goes to its own factory's point, with zero cross-wiring over 20 productions. Markers show only for the selected structure. Selling a rallied factory frees its marker (no orphan nodes).

#### BD-18. Base-under-attack UX: per-category alerts and jump-to-alert

- Dimension: base building depth
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs, game/scripts/Minimap.cs
- Spec:

Pure client. Today there is exactly ONE alert: any own non-mobile taking a Fired event plays `alert_attack` with a 12-second global cooldown and a red minimap ping (SkirmishLive.cs:475-488). GDD s10 line 85 specifies four distinct categories with distinct audio and a jump-to-event key.

1. Add an alert service with per-category state `(string sound, double lastAt, double cooldown, Vector2 at)`: BASE_UNDER_ATTACK (own structure hit, 12s), HARVESTER_UNDER_ATTACK (own Harvester hit, 8s), LOW_POWER (supply < draw crossing edge-triggered, 20s), SUPERWEAPON (SuperweaponLaunched/SuperweaponReady, no cooldown). Per-category cooldowns, not one global - today a harvester raid and a base assault suppress each other.
2. Derivation: BASE and HARVESTER come from `GameEventType.Fired` with `ev.B` resolving to an own entity (extend the existing check at line 475). CAVEAT DOCUMENTED IN doc 18 M6 AND CONFIRMED IN CODE: crush kills emit only `Died`, never `Fired` (World.cs:1254), so infantry flattened by a tank raises no alert - also derive HARVESTER_UNDER_ATTACK from an Hp decrease across `_latest` snapshots, which catches both.
3. LOW_POWER is edge-triggered off the supply/draw already tallied at SkirmishLive.cs:499-501 - fire on the false->true transition only, never per frame.
4. Store `_lastAlertAt` (world position) on every fire; bind Space to `_cam.FlyTo(new Vector3(x, 0, y))` - reuse the existing glide at SkirmishLive.cs:363. Add 'space: jump to alert' to the hint label at line 292.
5. Each category gets its own minimap ping colour: red (0.85,0.25,0.2) base, amber (0.90,0.68,0.22) harvester, gold (0.79,0.63,0.36) low power at the primary CY, orange (0.91,0.42,0.13) superweapon - the last already exists at line 490-493.
6. Toast text via the existing ShowToast (line 567). Audio: if a per-category sound is missing from AudioDirector, fall back to `alert_attack` and file an audio ticket rather than shipping silence.

**Doc 22 integration note:** BD-09 blanks the minimap when radar is offline but deliberately keeps pings rendering. That interaction is correct and load-bearing: LOW_POWER is exactly the alert most likely to fire while the radar is dark, and it must still be visible. Verify it after both land.

- Acceptance: Raid a harvester while the base is also under fire: BOTH alerts fire (today one suppresses the other for 12s). Crush a harvester with a tank: the alert still fires via the Hp-delta path despite zero Fired events. Cross into brown-out: LOW POWER fires exactly once, not once per frame; recovering and re-crossing fires again. Space glides the camera to the most recent alert position. Hint label lists the key.

#### BD-19. Repair and sell need visible feedback

- Dimension: base building depth
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/SkirmishLive.cs, game/scripts/CombatEffects.cs
- Spec:

Pure client. R and X (SkirmishLive.cs:935-946) fire commands into the void: `Repair` is a TOGGLE (World.cs:681 `e.Repairing = !e.Repairing;`) with no visual state at all, so a player cannot tell whether they just turned repair on or off, or that it is quietly draining 1 credit/tick (World.cs:1371). Selling is equally silent - the building simply vanishes via the generic death path. Note `ViewEntity` (SnapshotInterpolator.cs:18) does NOT carry `Repairing`; read `_world.Entities[id].Repairing` post-Step, the precedent already used for HState at SkirmishLive.cs:711.

1. Repair on: attach a looping ferrite-gold spark emitter named 'RepairFx' at the structure's top (copy the lifecycle of the W3-09 HarvestFx block at 709-723, including the null-check-then-create and the Emitting toggle) plus a slow gold pulse on the selection ring. Remove both when `Repairing` goes false - which the sim does automatically at full HP (World.cs:1368/1373), so the visual must poll, not latch.
2. `_selInfo` (line 594) currently reads `R repair  X sell` unconditionally; make it reflect live state: `REPAIRING (-1cr/tick)` in gold when on, `R repair` otherwise, and show the projected total cost `(MaxHp - Hp) / 2` credits so the player can decide.
3. Sell: because SellStructure is instant in the sim, the client cannot animate a delay without desyncing. Instead, on the `X` keypress show an immediate gold credit-gain toast `+{cost/2}` via ShowToast and let the existing structure sink tween (line 757-762) play.
4. Guard: X currently sells EVERY selected structure with no confirmation (line 941-945) - a box-select over a base plus a stray X is catastrophic. Require the selection to be exactly one structure, or a shift+X for multi-sell. State the chosen rule in the hint label at line 292.

**Doc 22 CONFLICT, resolve before implementing:** clause 4 here and DEF-09 clause 4 both add a sell guard, and they disagree. BD-19 wants "exactly one structure, or shift+X"; DEF-09 wants "more than 8 structures requires a confirming second X within 2 seconds" because mass-selling a wall run is a legitimate and frequent action. DEF-09's rule is the better one once walls exist (a 40-segment wall must be sellable without 40 keypresses) and the worse one before (nothing else is ever selected in bulk). Recommended resolution: implement DEF-09's threshold rule, and set the threshold to 1 until DEF-09's box-select lands, at which point it becomes 8. One rule, one code path, one hint-label string.

- Acceptance: Damage a turret, select it, press R: gold sparks appear, `_selInfo` reads REPAIRING with a per-tick cost and a projected total, credits tick down at 1/tick. Press R again: sparks stop within one frame. Let a repair complete: the sim auto-clears Repairing and the sparks stop without a keypress. Press X on one structure: `+{n}` toast, building sinks. Box-select five structures and press X: nothing is sold (or all five are, only under the documented modifier).

#### BD-20. Production queue cap of 9 per producer

- Dimension: base building depth
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt, game/scripts/Sidebar.cs
- Spec:

CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. `Produce` (World.cs:711-712) and `BuildStructure` (World.cs:664-665) both `q.Add(...)` unconditionally - a stuck mouse button or a spam-click queues thousands of items, each hashed every tick (World.cs:1594-1595) and serialized (World.Serialization.cs:47). No classic RTS allows this and it is an unbounded-growth hazard in a hashed structure.

1. Add `public const int MaxQueue = 9;` beside FootprintSize (World.cs:354).
2. In both command handlers, guard with `if (q.Count >= MaxQueue) break;` before the Add. Silent refusal, consistent with every other invalid command (ADR-001 forbids a rejection event; doc 18 N9 assigns denial feedback to the client).
3. The ready slot is separate from the queue and is NOT counted - a CY holding a ready structure can still queue 9 (matches the classic and the paused-line semantics at World.cs:1424).
4. Client (Sidebar.cs:190/199): once a producer's queue hits `World.MaxQueue`, disable that producer's buttons and dim the badge - client-side pre-validation, the doc 18 N9 pattern, so the silent sim refusal is never visible as a dead click.
5. 9 not 99: it is the classic-90s-RTS convention and keeps the `x{n}` badge single-digit, which the existing badge formatting at Sidebar.cs:192 assumes.

**Doc 22 note:** DEF-16 makes CY queues hashed. Landing DEF-16 first means this ticket's cap is genuinely protecting a hashed structure on both producer kinds rather than only on factories, which is the framing the spec above assumes. Either order works; the goldens move for both regardless, so bundle them into one regeneration if they land together.

- Acceptance: New scenario: issue 20 Produce commands to one factory in a single tick; QueueLength is exactly 9 and credits are untouched by the 11 refused. Same for BuildStructure at a CY. A CY with a ready structure still accepts 9 queued. Full runner gate exits 0; goldens regenerated on both platforms. In-game, buttons disable at 9 and re-enable as the head completes.

#### TICKET-P5-DEF-15. Client: the DEFENCE sidebar tab the GDD has always specified

- Dimension: defensive structures and barriers
- Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: false
- Files: game/scripts/Sidebar.cs
- Spec:

PURE PRESENTATION. GDD s5 line 45 specifies "Two parallel building queues (structures / defences)" and GDD s7 line 86 specifies a sidebar "tabbed (Buildings / Defence / Infantry / Vehicles / Aircraft)". Sidebar.cs today is one flat VBoxContainer with a STRUCTURES header and a UNITS header (lines 76-97), 190 pixels wide (line 59). Adding walls (DEF-08), an emplacement (DEF-12) and a bastion (DEF-13b) takes the structures list from 6 items to 9 and the flat list stops scanning.

SCOPE DISCIPLINE: this ticket delivers the TAB SPLIT ONLY - a presentation regrouping of buttons that already exist. It does NOT deliver the GDD's parallel QUEUES, which are a genuine sim change (the `_queues` dictionary is one list per producer id, World.cs:309, and a second concurrent queue per Construction Yard would change ProductionSystem, the hash and the save format). Say so in the ticket's Changed/Assumed/Needed-next footer and file the parallel-queue question in docs/questions/ with an owner and a decide-by date rather than smuggling it in. Do not add an Aircraft tab: no air layer exists (EntityKind has no aircraft, no unit type flies) and an empty tab is worse than no tab.

1. Split the Structures array (line 18-26) into BUILDINGS (power plant, refinery, factory, service depot, superweapon) and DEFENCE (wall, emplacement, turret, bastion), keeping the same BuildItem record and the same icon name convention (`res://ui/icons/{it.Icon}.png`, line 115).
2. Replace the two static headers with a TabContainer or a row of toggle buttons in the existing uplink style - reuse the exact palette constants already at the top of the file (Cinder 0.086/0.094/0.102, Seam 0.18/0.196/0.21, Bone 0.84/0.82/0.77, FerriteGold 0.79/0.63/0.36, Dim 0.45/0.44/0.42) and the existing StyleBoxFlat construction in MakeButton (lines 123-142) including its normal/hover/pressed/disabled states. Do not introduce a new colour; doc 16 is a closed palette.
3. Refresh (line 162-208) currently iterates _structButtons and _unitButtons dictionaries and drives per-button count badges plus the W3-15 progress fill (`((ColorRect)b.GetNode("Fill")).OffsetRight = ... * yardProgress`). It must keep working across tabs: a build finishing on a hidden tab must still show its count badge and progress when the tab is revealed, so keep updating every button regardless of tab visibility and let the container handle showing.
4. The PLACE button (line 86-88) and its W3-16 ready-pulse tween (lines 171-184) belong to the Buildings/Defence flow, not to Units - place it below the tab body so it is visible from either structure tab, since a ready structure is the single most important prompt in the loop and hiding it behind a tab would be a regression.
5. The MatchConfig.AllowedStructures/AllowedUnits campaign gating at lines 82 and 94 must keep working per tab: a tab whose every item is disallowed hides entirely rather than showing empty (mission 1 grants only a subset).
6. Keep CustomMinimumSize at 190 unless the tab row genuinely does not fit; widening the sidebar changes every screenshot and the camera's usable viewport.

- Acceptance: `dotnet build game/Ferrostorm.Game.csproj -c Release` exits 0; Godot headless runs the scene 1000 frames without error. `git diff --stat sim/` is empty. Headless assertions: the DEFENCE tab contains exactly the defensive types present in the build (wall/emplacement/turret/bastion, whichever have shipped) and BUILDINGS contains the rest, with no type appearing in both and no type missing from both (assert the union equals the full Structures catalogue); a build queued on BUILDINGS and left to finish while DEFENCE is fronted still shows its count badge and correct progress fill when BUILDINGS is re-fronted; the PLACE button is visible from both structure tabs when readyStructureType > 0 and pulses; with MatchConfig.AllowedStructures set to a Buildings-only subset the DEFENCE tab is hidden entirely rather than empty; there is no Aircraft tab. `grep -c 'new Color' game/scripts/Sidebar.cs` shows no colour constants added beyond those already defined. A docs/questions/ entry exists for the parallel-queue decision with an owner and a decide-by date. NEEDS A HUMAN: whether tabbing costs more clicks than it saves at this roster size - if the answer is no, the honest outcome is to close this ticket unshipped and keep the flat list, and that is an acceptable result.

### D.5 The economy

#### P5-ECON-04. Harvesters move twice per tick (measured 2.00x): HarvestSystem double-steps what MovementSystem already moved

- Dimension: resource economy and harvesting
- Impact: HIGH | Effort: S | cheapModelSafe: false | **touchesSim: TRUE**
- Land in ONE golden regeneration with P5-ECON-05.
- Files: sim/Ferrostorm.Sim/World.cs lines 1101-1158 (HarvestSystem), 1184-1188 (MoveTo), 480-495 (Step order)
- Spec:

TOUCHES SIM - flagged. cheapModelSafe=false: the CODE change is three lines, but whether harvesters SHOULD be the fastest vehicle in the game is a Balance/Design call, and it is a >15% effective stat change (charter A11 requires Balance + Game Designer co-sign). Do not let an implementer pick.

THE DEFECT (measured, not inferred): Step (480-495) runs MovementSystem at 485 and HarvestSystem at 490. HarvestSystem's ToField (1116) and ToRefinery (1141) branches call MoveTo, which sets Moving=true and immediately calls StepToward (1184-1188). So on every tick after the first, a travelling harvester is stepped once by MovementSystem and again by HarvestSystem.

Probe: harvester declared Speed 0.2 -> dX per tick = 0.1897, 0.3795, 0.3795, 0.3890, 0.3984. Plain Unit, same declared 0.2 -> 0.1897 every tick. Ratio exactly 2.00. (Tick 1 is single because the Harvest command lands after MovementSystem has already skipped the not-yet-Moving harvester.)

Effective harvester speed is ~5.7 cells/s vs the 2.7 cells/s com_harvester.yaml declares - faster than every tank in the roster, on the unarmed economy unit.

THE FIX (mechanical once the decision is made): in HarvestSystem, set the destination but let MovementSystem own the step. Add alongside MoveTo:

```csharp
private static void AimAt(ref Entity e, Fix64 x, Fix64 y)
    { e.TargetX = x; e.TargetY = y; e.Moving = true; e.UseFlow = true; }
```

and replace the MoveTo calls at 1116 and 1141 with AimAt. Leave MoveTo in place if other callers exist (grep first). One-tick latency is introduced on the first tick of each leg - that is correct and matches how every other unit already behaves.

Note StepToward's crowd-arrival early-out at 814 is gated on `e.Kind == EntityKind.Unit`, so harvesters are unaffected by it either way.

BALANCE CONSEQUENCE TO PUT IN FRONT OF THE SIGN-OFF: income per harvester at a 15-cell haul drops from a measured 2342 cr/min to roughly 1885 cr/min (cycle 269 -> ~334 ticks). That is a ~20% economy nerf across the board and moves every field-lifetime number in the findings. It also lands Ferrostorm within a few percent of the classic-RTS harvester tempo it is aiming at. Land this together with P5-ECON-05 in one golden regen.

- Acceptance: A probe asserts a travelling harvester's per-tick displacement equals a plain Unit's at the same declared Speed, within 1 Fix64 raw unit, on every tick of a 200-tick leg. The gated 'economy' scenario still asserts 4000 ferrite -> exactly 4000 credits. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 with regenerated goldens. Determinism CI green on Windows and Linux. The Balance tool's harvester-tempo line is re-baselined and its committed number recorded in the ADR. ADR in docs/adr/ carries Architect + Balance + Game Designer sign-off.

#### P5-ECON-05. SpawnHarvester hard-codes stats and ignores the /data catalogue

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | **touchesSim: TRUE**
- Land in ONE golden regeneration with P5-ECON-04.
- Files: sim/Ferrostorm.Sim/World.cs lines 241-248 (SpawnHarvester), 286 (type-4 def), 1466-1467 (production path); data/units/com_harvester.yaml
- Spec:

TOUCHES SIM - flagged. Both changes below alter hashed state.

DEFECT: SpawnHarvester (241-248) hard-codes Speed = Fix64.FromFraction(1,5) = 0.20 and Hp = 700, and never assigns UnitType. The catalogue's type-4 def (World.cs:286) says Speed = Fix64.FromFraction(9,50) = 0.18, which is exactly what DataLoader produces from com_harvester.yaml's `speed: 18` (DataLoader.cs:106 documents the hundredths encoding). The production path (1466-1467) branches to SpawnHarvester and discards def entirely. Result: com_harvester.yaml's speed is dead data whose only consumer is a self-consistency assertion (Program.cs:1343). CLAUDE.md: 'All gameplay numbers live in /data ... Hand-editing stats in code is forbidden.'

FIX:

1. Change the signature to `public int SpawnHarvester(int player, Fix64 x, Fix64 y, int unitType = 4)`. Inside, resolve `var def = GetUnitType(unitType);` and use def.Speed, def.Hp (for both Hp and MaxHp), def.Armour, and def.SightCells instead of the literals. Set `UnitType = unitType` on the entity. Keep WeaponId = 0 and ExplicitTarget = -1.
2. Guard the fallback: if `def.Cost <= 0` (GetUnitType returns default for unknown ids, World.cs:302), throw InvalidOperationException rather than silently spawning a zero-speed harvester.
3. World.cs:1466-1467, pass `queuedType` through: `SpawnHarvester(p, Map.CellCentre(nx), Map.CellCentre(ny), queuedType)`.
4. Leave every existing 3-arg call site alone; the default covers them.

HASH IMPACT: Speed 0.20 -> 0.18 and UnitType 0 -> 4 are both hashed (World.cs:1585 region serialises UnitType). Every economy-touching golden moves. LAND THIS IN THE SAME GOLDEN REGEN AS P5-ECON-04 - they overlap on harvester speed and doing them separately means two ADRs and two regens for one behaviour.

SIDE BENEFIT: with UnitType==4 set, FacDebug's `if (e.UnitType > 0)` tally (Program.cs:1790) starts counting harvesters, and SkirmishLive's UnitNames[4]="HARVESTER" lookup becomes reachable.

- Acceptance: A probe asserts a spawned harvester's Speed, Hp, MaxHp, Armour and SightCells each equal World.GetUnitType(4)'s corresponding field exactly, and that UnitType == 4. A probe asserts a harvester produced from a Factory queue has UnitType == 4. Program.cs:1343's catalogue round-trip (harv == refWorld.GetUnitType(4)) still passes. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 with regenerated goldens; determinism CI green on Windows and Linux; ADR carries Architect sign-off.

#### P5-ECON-07. Harvesters never auto-resume once Idle: re-issue Harvest from the client

- Dimension: resource economy and harvesting
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
- Canonical; supersedes BD-15 (Appendix A). BD-15's rally-precedence rule is folded in as clause 2a.
- Files: game/scripts/SkirmishLive.cs (tick loop near lines 470-500); sim/Ferrostorm.Sim/World.cs:1101-1173 (reference only)
- Spec:

Once a harvester reaches HarvestState.Idle it stays Idle FOREVER. Only a fresh CommandType.Harvest sets HState (World.cs:581-588). This bites in three ordinary situations: (a) the player ordered Harvest before building a refinery (P5-ECON-06), then builds one - the harvester never starts; (b) every field the harvester could reach ran dry, then an expansion opens a new one - RetargetField (1171) already parked it Idle and nothing wakes it; (c) the player's last refinery died and a replacement is built. The AI hides all three by re-issuing Harvest every tick (SkirmishAI.cs:150-157); the human has no equivalent.

FIX (client-side, through the normal command path - deterministic, lockstep-safe, ADR-001-clean):

1. Add fields: `private readonly HashSet<int> _manuallyStopped = new();`
2. In the input path, add the harvester's id to _manuallyStopped when the player issues Stop (CommandType.Stop) or a bare PathMove on it, and REMOVE it when the player issues an explicit Harvest. This is the line that keeps the feature QoL rather than strategy automation (GDD s1: 'Every QoL feature that doesn't change strategy is in. Anything that automates strategy is out.') - an explicitly parked harvester stays parked.
2a. (FOLDED IN FROM BD-15) An explicit rally beats auto-harvest. On the ProductionComplete path, if `_rally` (BD-14) holds an entry for the producing structure `ev.C`, skip the auto-harvest for that new harvester and let the rally PathMove stand - a player who set a rally meant it.
3. Once per sim tick, inside the existing accumulator loop and BEFORE Step, for each own entity with Kind==Harvester && HState==Idle && !_manuallyStopped.Contains(id): if HasLiveRefinery() (P5-ECON-06's helper) and a live field with FerriteAmount > 0 exists, append `new Command(_world.Tick, 0, CommandType.Harvest, id, Fix64.Zero, Fix64.Zero, nearestFieldId)` to _pending.
4. Pick nearestFieldId by squared distance with ties broken to the LOWER entity id - mirror RetargetField (World.cs:1160-1173) exactly so client and sim agree.
5. Rate-limit to one re-issue attempt per harvester per 15 ticks. Without this you reproduce SkirmishAI's flood (see P5-ECON-15) through the lockstep relay.

PORTABILITY NOTE for the reviewer: the honest home for this is the sim - HarvestSystem's ToRefinery branch (1134-1138) already retries FindNearestRefinery, it just gives up permanently instead of parking a retry, and RetargetField already auto-reassigns fields. A sim-side fix would be ~5 lines, would fix the AI's flood for free, and would survive the ADR-004 UE5 renderer swap without being re-implemented. It is deferred here only because it is touchesSim=true and this ships today. File it as the follow-up.

- Acceptance: A scripted harness asserts: (a) harvester Idle with no refinery, then a refinery is placed -> within 20 ticks HState != Idle and Carry rises within 400 ticks; (b) harvester Idle with all fields dry, then a fresh field is spawned -> within 20 ticks HState == ToField; (c) a harvester given an explicit Stop stays HState == Idle for 600 ticks with a live refinery and live field present; (d) across a 3000-tick run, no harvester receives more than one Harvest command per 15 ticks. Golden hashes unchanged. Godot scene runs 1000 headless frames without error.

#### P5-ECON-13. GDD s4 'Refinery includes one free harvester' is unimplemented

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | **touchesSim: TRUE**
- Canonical; supersedes BD-16 (Appendix A). Depends on P5-ECON-07.
- Files: sim/Ferrostorm.Sim/World.cs lines 250-261 (SpawnRefinery), 640-660 (BuildStructure path); sim/Ferrostorm.Sim/SkirmishAI.cs lines 143-148
- Spec:

TOUCHES SIM - flagged.

GDD s4 line 39, ratified: 'Refinery: 2,000 credits, includes one free harvester.' SpawnRefinery (250-261) spawns only the refinery entity, and the BuildStructure path (line 648: `case EntityKind.Refinery: SpawnRefinery(c.PlayerId, ax, ay); break;`) adds nothing. The free harvester does not exist. This is why the opening is slower than the GDD describes and why the client has to hand-place a starting harvester (SkirmishLive.cs:180).

1. Add a bool parameter: `public int SpawnRefinery(int player, int ax, int ay, bool withHarvester = false)`. Default false so all existing call sites - including the scenario fixtures and the Balance tool's Tempo() at tools/Ferrostorm.Balance/Program.cs:127 - stay bit-identical.
2. When withHarvester is true, after the refinery is added, spawn the harvester at the first passable cell from the existing SpawnOffsets table (World.cs:1339-1340), using the same scan the production path uses at 1462-1465 (`Map.InBounds && !Map.IsBlocked`). Reuse SpawnOffsets - do not invent a second placement rule; determinism depends on the order being fixed.
3. Pass `withHarvester: true` ONLY from the BuildStructure path at line 648. Map-authored refineries (MapLoader.cs:169) must stay explicit so mission designers keep control.
4. Emit a ProductionComplete GameEvent for the spawned harvester (mirror line 1470: `_events.Add(new GameEvent(GameEventType.ProductionComplete, spawned, 4))`) so the client toast and audio fire and the player understands where it came from.
5. If no offset is passable, spawn nothing rather than stacking on a blocked cell.

INTERACTIONS TO CHECK BEFORE SIGN-OFF:
- SkirmishAI.cs:145 gates production on `harvesters < refineryCount`. A free harvester per refinery satisfies that rule immediately, so the AI will stop building harvesters entirely. Verify the AI still reaches its intended harvester count, or the free harvester is a stealth AI nerf.
- Combined with P5-ECON-07 the free harvester auto-starts; without it, it spawns and sits Idle forever, which is worse than not having it. LAND P5-ECON-07 FIRST.
- Effective refinery cost drops from 2000 to 2000-for-3400-of-value. Balance must confirm 2000 is still the right price or the GDD's own number needs revisiting.

**Doc 22 integration note:** if BD-14 has landed, clause 4's GameEvent must set `C` to the refinery's entity index, or P5-ECON-07 clause 2a cannot tell whether a rally applies to this harvester.

- Acceptance: A probe asserts: BuildStructure of a Refinery spawns exactly one extra entity with Kind==Harvester, PlayerId equal to the builder, at a passable cell, and emits one ProductionComplete event; SpawnRefinery called with the default (no flag) spawns exactly one entity, and the gated 'economy' and 'construction' scenario hashes are UNCHANGED by that path. The AI reaches >= 2 harvesters within 3000 ticks of the skirmish scenario. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 with regenerated goldens for AI-driven scenarios only. Determinism CI green on Windows and Linux. ADR + Architect + Balance sign-off.

#### P5-ECON-09. Ferrite fields drain by losing shards, not by uniform scale

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: M | cheapModelSafe: false | touchesSim: false
- Depends on: P5-ECON-01
- Files: art/3d/builder.py lines 488-536 (ferrite_cluster); game/scripts/SkirmishLive.cs lines 660-673, 724-728; game/scripts/ReplayTheater.cs lines 67-76, 122-126; game/assets/models/ferrite_cluster.glb (re-export)
- Spec:

DEPENDS ON P5-ECON-01. cheapModelSafe=false: the depletion curve and the emission ramp are silhouette/taste calls that doc 16's 'silhouette-first' law makes load-bearing.

Today builder.py:536 does `join(objs, 'ferrite_cluster')`, collapsing 7 shards + 7 tips + 5 rubble cubes into ONE mesh. The client's only lever is scaling the whole node (SkirmishLive.cs:727), which shrinks the rubble ring too - so a nearly-spent field reads as a small full field, not a picked-over one. doc 16: 'the viewer's signature is the ferrite-seam glow draining with the deposit.' The 2D viewer honours it (tools/viewer/viewer-template.html:158-166 - 'Ferrite seams: the signature - glow drains with the deposit'); the 3D game does not.

1. builder.py ferrite_cluster: stop joining everything. Join the 5 rubble cubes into a hull named 'ferrite_cluster'. Then, for each i in 0..6, use the existing child_part(hull, obj, name) helper (builder.py:539-551) to attach shard i as a child named f'shard{i}' and its tip as f'tip{i}'. child_part is the established de-merge pattern from Wave 2 and preserves the named glTF node. Keep the existing materials, bevel/subdivide _facet treatment, and the 'bodies non-emissive, tips emissive' rule at 519/524 - that fix is why the resource reads gold at all (see the W4-07 comment at 489-492).
2. Re-export ferrite_cluster.glb via art/3d/export_glb.py and re-import in Godot.
3. SkirmishLive.cs: replace the uniform node.Scale drain (724-728) with shard removal. With g = Ferrite / (float)MapData.StandardFieldAmount, show shard i iff i < Mathf.CeilToInt(g * 7). Hide the matching tip with its shard. Never scale the hull - the rubble ring stays put, which is what sells 'this ground has been worked'.
4. Dim the remaining tips as the last shard empties: on the visible tips' material, set emission energy to Mathf.Lerp(0.35f, 1.0f, g). Cache the material lookup per actor in the existing _rigs dictionary rather than walking the tree every frame.
5. Keep the W4-17 amber Decal stain (SkirmishLive.cs:665-673) at CONSTANT size. The stain is the permanent scar; the shards are the resource. This is the visual grammar that makes P5-ECON-11's regrowth legible later - a spent-but-alive field becomes 'stain + rubble, no shards'.
6. Mirror the same shard logic in ReplayTheater.cs:122-126, which already has the correct FerriteAmount in e[6].
7. Extend ScanRig (SkirmishLive.cs:125-137) with `else if (nm.StartsWith("shard") || nm.StartsWith("tip")) rig.Shards.Add(t3);` and add `public readonly List<Node3D> Shards = new();` to ActorRig. Sort by name so index order is stable.

**Doc 22 integration note:** P5-ECON-01 clause 3 sets a uniform node.Scale drain; this ticket deletes that and replaces it with shard hiding. If both are in the plan, P5-ECON-01's scale line is deliberately temporary - it is the S-effort fix that makes the drain visible at all, and this is the M-effort fix that makes it read correctly. Do not treat the deletion as a regression.

- Acceptance: A glTF node dump of the rebuilt ferrite_cluster.glb lists child nodes shard0..shard6 and tip0..tip6 under a hull named ferrite_cluster (a python glb-json probe; the pattern exists at scratchpad/glbjson.py). A headless harness asserts: at FerriteAmount==12000 all 7 shards are Visible; at 6000, 4 are Visible (ceil(0.5*7)); at 1, exactly 1; and the hull's Scale is Vector3.One at every one of those points. The Decal named 'Stain' has identical Size at 12000 and at 1. Godot scene runs 1000 headless frames without error. Golden hashes unchanged. Needs a human: whether the depletion reads at gameplay camera distance and whether 7 steps is the right granularity.

#### P5-ECON-10. The harvester's ore heap is always full: de-merge it and drive it from Carry

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false
- Depends on: P5-ECON-01 (needs ViewEntity.Carry)
- Files: art/3d/builder.py lines 267-288 (com_harvester); game/scripts/SkirmishLive.cs lines 76-90 (ActorRig), 125-137 (ScanRig), 709-723; game/assets/models/com_harvester.glb (re-export)
- Spec:

DEPENDS ON P5-ECON-01 (needs ViewEntity.Carry).

builder.py:277-283 builds an emissive ico-sphere named 'heap' and line 288 joins it into com_harvester. So the ore heap is welded on: an empty harvester driving out to the field glows with a full hopper. W4-09's own comment at 275-276 says 'at night the glowing full hopper is the economy telling its own story' - as built, it tells the story wrong 50% of the time, on every harvester, permanently.

1. builder.py com_harvester(): remove the heap from the `parts` list before the `join(parts, 'com_harvester')` at 288. After the join, attach it with the existing helper: `child_part(hull, heap, 'heap')` (builder.py:539-551). Keep its material (mat('fhi', emit=1.6, rough=0.4)), its (0, 0.02, 0.60) position and its (1,1,0.5) flattened scale - child_part bakes rotation/scale into the mesh and leaves a pure-translation node, which is what we want.
2. Re-export com_harvester.glb via art/3d/export_glb.py; re-import in Godot.
3. ActorRig (SkirmishLive.cs:76-90): add `public Node3D? Heap;` and `public Vector3 HeapRest;`.
4. ScanRig (125-137): add `else if (nm.StartsWith("heap")) { rig.Heap = t3; rig.HeapRest = t3.Scale; }`. Lower-case prefix matches the existing turret/wheel/dish/intake/door convention.
5. In SyncActors' harvester block (709-723), after the existing HarvestFx handling, add:

```csharp
if (_rigs.TryGetValue(v.Id, out var hr) && hr.Heap is { } heap)
{
    float f = Mathf.Clamp(v.Carry / (float)World.HarvesterCapacity, 0f, 1f);
    heap.Visible = f > 0.02f;
    heap.Scale = new Vector3(hr.HeapRest.X, hr.HeapRest.Y * Mathf.Max(f, 0.05f), hr.HeapRest.Z);
}
```

Scale the vertical axis only - the hopper rim (builder.py:274) is a fixed cylinder, so a heap that grows upward inside it is the correct read.

6. Use World.HarvesterCapacity, never a 700 literal.

PAYOFF: the classic RTS tell - you can see at a glance which harvesters are worth intercepting. It also makes the existing W3-09 gold intake particles (CombatEffects.cs:766) mean something, since the heap now visibly grows while they play.

**Doc 22 integration note:** C-06 changes how emissive materials bake. The heap uses `mat('fhi', emit=1.6, ...)`, whose max channel is 0.92, so 1.6 x 0.92 = 1.47 and it currently clips to white. After C-06 it will bake at full chroma with the intensity carried in emissive strength. Rebake this model in the same Blender session as C-05/C-06 rather than three times.

- Acceptance: A glTF node dump of the rebuilt com_harvester.glb lists a child node named 'heap' under the com_harvester hull. A headless harness asserts: with Carry==0 the heap node's Visible is false; with Carry==700 its Scale.Y equals HeapRest.Y within 1%; with Carry==350 its Scale.Y is between 0.45x and 0.55x of HeapRest.Y; and Scale.X/Scale.Z are unchanged at every point. Godot scene runs 1000 headless frames without error. Golden hashes unchanged.

#### P5-ECON-11. Ferrite regrowth from seed nodes: GDD s4 is ratified and unimplemented

- Dimension: resource economy and harvesting
- Impact: TRANSFORMATIVE | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Files: sim/Ferrostorm.Sim/World.cs lines 39-40 (EntityKind/HarvestState), 51-75 (Entity), 263-270 (SpawnFerriteField), 1101-1173 (HarvestSystem/RetargetField), 1580-1590 (hash); sim/Ferrostorm.Sim/MapLoader.cs lines 43, 87-103, 156; sim/Ferrostorm.Sim.Runner/Program.cs (new gated scenario + golden row); sim/golden-hashes.txt
- Spec:

TOUCHES SIM - flagged. cheapModelSafe=false: the regen rate, the cap and the seed policy ARE the economy design; Balance + Game Designer must co-sign the numbers. The IMPLEMENTATION below is mechanical once they are chosen.

GDD s4 line 37, ratified: 'Ferrite crystal fields. Regrow slowly from seed nodes; fields near spawns are finite enough to force expansion by minute ~8.' The sim implements the second clause and not the first. FerriteAmount only ever decreases (World.cs:1125) and the field is destroyed at zero (1126: `if (f.FerriteAmount <= 0) f.Alive = false;`). A mined-out map stays mined out forever. Consequence: every long match converges on a dead board where all harvesters sit permanently Idle (RetargetField line 1171) and no economy can ever recover - turtling and comebacks are both mathematically impossible.

IMPLEMENTATION:

1. Entity: add `public int Regen;` (per-tick regrowth) and `public int Cap;` (regrowth ceiling). Both hashed - add to ComputeStateHash next to FerriteAmount at 1585, and to World.Serialization with a format-version bump (doc 14 spec).
2. SpawnFerriteField signature: `SpawnFerriteField(Fix64 x, Fix64 y, int amount, int regen = 0, int cap = 0)`. Default 0/0 keeps every existing call site inert.
3. STOP KILLING SPENT FIELDS. World.cs:1126, change `if (f.FerriteAmount <= 0) f.Alive = false;` to only kill fields with Regen == 0. A seeded field at 0 stays Alive as a spent husk. RetargetField (1167) and the AI's NearestField/RichestDistantField (SkirmishAI.cs:354, 367) already skip `FerriteAmount <= 0`, so a live-but-empty field is correctly ignored by every consumer today - verify this before relying on it.
4. New regrowth pass. Add a `FerriteRegenSystem()` call in Step AFTER HarvestSystem (490) and before ProductionSystem, so a tick's mining always precedes its regrowth: iterate entities, and for Alive FerriteField with Regen > 0 && FerriteAmount < Cap, do `f.FerriteAmount = Math.Min(f.Cap, f.FerriteAmount + f.Regen);`. Integer only. No RNG - determinism forbids it.
5. Map format: add grid char 'S' = seed field. MapLoader.cs:87-103 add `case 'S': fields.Add((x, y)); seeds.Add((x, y)); break;`. Add `public IReadOnlyList<(int Cx, int Cy)> Seeds` to MapData. BuildWorld (156) passes regen/cap for seed cells and 0/0 for plain 'F'. Keep 'F' meaning exactly what it means today so no existing map changes behaviour.
6. Add MapData constants next to StandardFieldAmount (43): `public const int SeedRegenPerTick` and `public const int SeedFieldCap`.

STARTING NUMBERS FOR THE SIGN-OFF (derived from measured rates, not invented): one harvester at a 15-cell haul consumes 700 ferrite per 269 ticks = 2.6 ferrite/tick. SeedRegenPerTick = 1 makes a seed sustain ~38% of ONE harvester - meaningful as a trickle, useless as an economy, which is exactly the 'regrow slowly' brief. SeedFieldCap = StandardFieldAmount (12000) means a seed left alone for 12000 ticks (13.3 min) refills fully. Recommend seeding ONLY contested/midfield clusters, never home clusters, so the GDD's 'fields near spawns are finite enough to force expansion' stays literally true.

COMPOSES WITH P5-ECON-09: a spent seed renders as stain + rubble + zero shards, and shards grow back as it refills. The visual grammar is already paid for.

- Acceptance: A new gated scenario 'regrowth' in the scenarios table (Program.cs:1246-1271) with a committed row in sim/golden-hashes.txt. It asserts: a seed field mined to 0 stays Alive; its FerriteAmount rises by exactly SeedRegenPerTick per tick and never exceeds SeedFieldCap; a plain 'F' field with Regen==0 mined to 0 still sets Alive=false (no behaviour change); a harvester parked Idle beside a spent seed re-targets it once FerriteAmount > 0; and total credits banked equals total ferrite removed, exactly, over 20000 ticks. Double-run determinism identical; cross-platform golden CI green on Windows and Linux. A save written pre-change fails to load with a clear version error rather than silently mis-parsing. ADR in docs/adr/ carries Architect + Balance + Game Designer sign-off.

#### P5-ECON-14. GDD s4 secondary income (neutral capturable +15 cr/tick) is unimplemented and its name is taken

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Canonical; supersedes BD-22 (Appendix A). BD-22's "Ferrite Cache" name is a live candidate in the naming question; its 'D' grid char and v3 map-format bump are NOT adopted, because this ticket reaches the same result through the existing structure-line syntax with no version bump.
- Files: docs/design/02-game-design-document.md s4 line 41; sim/Ferrostorm.Sim/World.cs lines 39 (EntityKind), 342-353 (GetStructureType), 435-447 (SpawnServiceDepot), 719-722 (IsStructure), 913-943 (CaptureSystem), 1347-1400 (ProductionSystem); sim/Ferrostorm.Sim/MapLoader.cs lines 163-177
- Spec:

TOUCHES SIM - flagged. cheapModelSafe=false: a naming collision in ratified GDD text has to be resolved by the Game Designer before code.

TWO DEFECTS IN ONE PLACE:
(a) GDD s4 line 41 - 'Secondary income: Capturable neutral Depot structures on the map grant +15 credits/tick - the map-control incentive, replacing oil derricks in spirit' - does not exist. There is no neutral income structure of any kind, and no map syntax to place one (doc 18 N16 flagged this: 'neutral capturable Depots have no map syntax').
(b) THE NAME IS ALREADY SPENT. Structure type 8 'depot' (cost 1200) is EntityKind.ServiceDepot: a repair building that COSTS 1 credit/tick per unit healed (World.cs:435, 1381-1397). It is the opposite of an income building and it has its own golden scenario ('depot', hash 0x89A3AE97B98154B5). Anyone reading the GDD and the catalogue together gets the wrong structure.

STEP 1 - RESOLVE THE NAME (docs/questions/, owner game-designer). Recommend: rename the GDD's income building to 'Outpost' and leave ServiceDepot alone. The repair building is shipped, gated, modelled (com_service_depot.glb) and named in the client (StructNames[8]='SERVICE DEPOT'); the income building is vapour. Rename the thing that does not exist yet.

STEP 2 - LATENT CRASH TO FIX REGARDLESS OF (1). Neutral structures are currently unsafe. CaptureSystem's guard at 921 is `t.PlayerId == e.PlayerId`, which a PlayerId==-1 structure passes - so an engineer can already capture a neutral structure IF a map spawns one (MapLoader.cs:163-177 takes a Player int and would accept -1). But ProductionSystem's second loop (1359-1362) only guards `if (!e.Alive) continue;` and then indexes `supply[e.PlayerId]` at 1386 and `_credits[e.PlayerId]` at 1369/1393 - a stackalloc Span at index -1 throws IndexOutOfRangeException. Fix independently of the rest: add `if (e.PlayerId < 0) { _entities[i] = e; continue; }` at the top of that loop, matching the guard the first loop already has at 1354.

STEP 3 - IMPLEMENTATION (post-decision):

1. Add EntityKind.Outpost = 11 and structure type 9 (`new StructureTypeDef(0, EntityKind.Outpost, 0)` - cost 0, unbuildable; it exists only on maps). Add it to IsStructure (719-722) so CaptureSystem accepts it.
2. SpawnOutpost(int player, int ax, int ay) following SpawnServiceDepot's shape: 2x2 BlockFootprint, Hp/MaxHp 800, ArmourClass.Structure, PowerDraw 0 (a captured income building must not brown out the grid it funds), Sight 5.
3. Add `public const int OutpostIncomePerTick = 15;` next to the economy constants at World.cs:189-192 (GDD's number). In ProductionSystem's per-entity loop, after the PlayerId<0 guard: if Kind==Outpost, `_credits[e.PlayerId] += OutpostIncomePerTick;`. No power gate - GDD does not specify one; if Balance wants one, it goes in the ADR, not the implementation.
4. MapLoader BuildWorld (163-177): add `EntityKind.Outpost => world.SpawnOutpost(st.Player, st.Ax, st.Ay)` to the switch. Player -1 = neutral. Document 'structure -1 9 X Y tag' in the MapLoader header comment (lines 8-20), which is the canonical format spec.
5. Client: ModelLibrary KindModel needs an entry for kind 11 or Instantiate falls back to com_power_plant (ModelLibrary.cs:30). Add StructNames[9]. Per doc 16's one-place team-colour law, a captured Outpost shows the owner's mark in exactly one band.

SCALE CHECK FOR THE SIGN-OFF: 15 cr/tick = 900 cr/min - roughly 40% of one harvester's measured 2342 cr/min at a 15-cell haul, with zero micro and zero risk. Two Outposts out-earn a harvester. That is a big number for a free income source; Balance should confirm 15 or move it.

**Doc 22 integration notes, three of them.** (1) NUMBERING: this ticket claims EntityKind 11 and struct type 9, which collide with DEF-04's Wall and BD-09's Radar. ADR-005 owns the allocation; take the next free values. (2) THE RATE: BD-22 (Appendix A) independently read the same GDD line and reached a sharper conclusion worth carrying into the sign-off - 15 credits/tick at 15 Hz is 225 credits/SECOND, which is roughly ten times a harvester, and the GDD almost certainly means 15 per second in the classic idiom. BD-22's recommendation is `if (Tick % 15 == 0)` giving 15 cr/s or about 45% of a harvester. Put both readings in front of Balance; do not ship 225/s. (3) VICTORY: BD-22 also flags that a captured Outpost satisfies the VictorySystem hasHope test, so a player with one captured cache and no base is never eliminated. Exclude it from hasHope exactly as DEF-04 clause 9 excludes walls, and state the decision in the ADR.

- Acceptance: A question doc in docs/questions/ resolves the Depot/Outpost naming and GDD s4 is updated, BEFORE code. The PlayerId<0 guard lands with a probe asserting a neutral structure in the world survives 1000 ticks of ProductionSystem without throwing. Post-decision: a new gated scenario 'outpost' with a committed golden row asserts a neutral Outpost grants 0 credits to anyone; after engineer capture it grants exactly 15 credits/tick to the capturer and the engineer is consumed; a recaptured Outpost switches payee on the exact tick of the Captured event. Existing 'depot' and 'capture' golden hashes are unchanged. Determinism CI green on Windows and Linux. ADR + Architect + Balance + Game Designer sign-off.

#### P5-ECON-12. P4-PORT-04 multi-resource: field type id in the map grid, one credit pool, per-type yield

- Dimension: resource economy and harvesting
- Impact: MEDIUM | Effort: L | cheapModelSafe: false | **touchesSim: TRUE**
- Files: docs/questions/ (new GDD question); docs/design/02-game-design-document.md s4; sim/Ferrostorm.Sim/MapLoader.cs lines 8-14, 43, 87-103, 156; sim/Ferrostorm.Sim/World.cs lines 51-75, 263-270, 1119-1131; game/scripts/ModelLibrary.cs line 24; art/3d/builder.py
- Spec:

TOUCHES SIM - flagged. cheapModelSafe=false: the GDD decision must land before any code. This ticket SPECIFIES P4-PORT-04 (docs/design/21-ue5-portability-audit.md:27, 65-66: 'Multiple resource types | GAP -> P4-PORT-04 (single ferrite today; loader is type-ready)').

STEP 1 - FILE THE GDD QUESTION FIRST (docs/questions/, owner game-designer, with a decide-by date; CLAUDE.md source-of-truth rule 5). The question is NOT 'should we have two resources' but 'per-type POOLS or per-type YIELD'.

MY RECOMMENDATION, with reasoning - adopt YIELD, reject POOLS:
- Per-type pools is a multi-resource-economy shape borrowed from a different lineage. Ferrostorm's stated heritage (GDD s1/s2, 'inspired by the classic RTS games of the 90s') is the single-credit-pool tradition, in which the two grades of the same harvestable crystal fed ONE pool at different yields rather than two separate treasuries. Pools would contradict the design pillars, not extend them. (Legal note: the source ticket named the specific 90s franchise and its resource here; per CLAUDE.md the approved formulation is 'inspired by the classic RTS games of the 90s', so the reasoning is preserved and the trademark is not.)
- Cost of pools: every one of the 12 data/units/*.yaml needs cost_a/cost_b, plus data/schema.unit.json, plus GetStructureType (World.cs:342-353), plus pay-as-you-build's `owed` arithmetic (1442), plus the head refund (610), the ready-slot refund (600), sell-back (697), structure repair (1371), depot repair (1394), the AI's ~8 `w.Credits(_player) >= X` gates (SkirmishAI.cs:100, 110, 147, 164, 194), Sidebar affordability, and the HUD. That is a rewrite of the economy for a mechanic the GDD never asked for.
- Cost of yield: one int on the field entity and one multiply in the Loading branch. The map-control decision ('is the rich field worth contesting?') is delivered identically.

STEP 2 - IMPLEMENTATION (only after sign-off), specified for YIELD:

1. Entity: add `public int FieldType;` and `public int YieldPerUnit;` (percent, 100 = baseline). Both hashed - add beside FerriteAmount at World.cs:1585; bump the save format (doc 14).
2. SpawnFerriteField signature: `(Fix64 x, Fix64 y, int amount, int fieldType = 0, int yieldPct = 100)`. Defaults keep every existing call site bit-identical.
3. World.cs Loading branch (1119-1131): `take` still decrements FerriteAmount by the raw amount (so field lifetime is honest), but Carry accrues `take * yieldPct / 100` with integer maths in that exact order (multiply before divide - Fix64/rounding discipline per ADR-002). HarvesterCapacity still caps Carry.
4. MapLoader.cs:87-103: add `case 'D': fields.Add((x,y)); fieldTypes.Add(1); break;` for the dense/rich type. Keep 'F' as type 0 at yield 100. Update the format doc-comment at lines 8-14 - it is the canonical map-format spec. Extend MapData.Fields to carry the type; the only consumers are MapLoader.BuildWorld (156) and the two selftest assertions (Program.cs:1373-1374), so this is a contained change.
5. Add per-type constants beside StandardFieldAmount (43): type 1 = amount 12000, yield 150.
6. Client: ModelLibrary.cs:24 maps kind 3 -> 'ferrite_cluster'. Key the model on FieldType, not Kind - add a 'ferrite_cluster_dense' variant in builder.py using the fhi highlight palette entry, per doc 16's ferrite-gold law. Requires ViewEntity.FieldType (extend alongside P5-ECON-01's fields).

NOTE: the audit's claim that 'the loader is type-ready' is optimistic - MapData.Fields is currently a bare IReadOnlyList<(int Cx, int Cy)> with no type channel. It is a small change, but it is a change.

**Doc 22 CONFLICT:** this ticket claims grid char 'D' for the dense field; BD-22 (Appendix A, superseded) claimed 'D' for the neutral capturable cache. P5-ECON-14 supersedes BD-22 and uses the structure-line syntax instead, so 'D' is free - but only as long as BD-22 stays superseded. If the Game Designer reinstates BD-22's grid-char approach, one of the two must take a different character. Record the grid-character allocation in the MapLoader header comment, which is the canonical format spec.

- Acceptance: A question doc exists in docs/questions/ with owner and decide-by date, and GDD s4 records the resolution, BEFORE any code lands. Post-decision: a new gated scenario 'multires' with a committed golden row asserts a harvester on a type-1 field banks exactly 150% of the ferrite removed while the field's FerriteAmount decrements by the raw amount; a type-0 field banks exactly 100% and its golden hash is bit-identical to the pre-change 'economy' scenario. MapData.Parse accepts 'D' and rejects unknown grid chars with a line number (existing FormatException contract at MapLoader.cs:101). Double-run determinism identical; golden CI green on Windows and Linux. ADR + Architect sign-off.

#### P5-ECON-15. SkirmishAI floods the command stream with Harvest no-ops when it has no refinery

- Dimension: resource economy and harvesting
- Impact: LOW | Effort: S | cheapModelSafe: true | **touchesSim: TRUE (counter-intuitively; read the hash note)**
- Files: sim/Ferrostorm.Sim/SkirmishAI.cs lines 150-158
- Spec:

TOUCHES SIM - flagged, and counter-intuitively so: read the hash note below before assuming this is free.

SkirmishAI.cs:150-158 issues a fresh CommandType.Harvest for EVERY own harvester with HState==Idle, EVERY tick, with no refinery check. When the AI's last refinery dies, World.cs:581-588 accepts each command, sets FieldId, finds no refinery, and leaves HState==Idle - so next tick the AI sees Idle again and re-issues. Forever, at 15 Hz, per harvester. Every one of those commands is serialised through the lockstep relay (sim/Ferrostorm.Net/Lockstep.cs) and written into the replay stream.

FIX: line 145 already computes `refinery` (the AI's nearest own refinery id, -1 if none). Gate the loop:

```csharp
if (refinery >= 0)
    for (int i = 0; i < w.Entities.Count; i++) { ...existing body... }
```

That is the whole change. `refinery` is already in scope from the scan at 37-67.

WHY THIS IS touchesSim=true DESPITE ONLY TOUCHING THE AI: the no-op command is not actually a no-op. World.cs:585 executes `e.FieldId = c.AuxId;` BEFORE the refinery lookup fails at 586-587. FieldId is hashed (World.cs:1585: `h.Add(e.FieldId)`). So suppressing these commands changes hashed state in any scenario where an AI loses its last refinery while holding harvesters. Expect movement in the 'skirmish', 'expansion' and 'aisuper' goldens; confirm by running the gate before writing the ADR.

SEVERITY IS LOW, honestly: it only fires in a losing position, and the AI does recover correctly once a refinery exists (which is the behaviour P5-ECON-07 gives human players). It is filed because it is a real waste in the netcode hot path and because the FieldId-mutation-on-failure is a genuine surprise worth recording. Bundle it into whichever golden regen P5-ECON-04/05 trigger rather than spending an ADR on it alone.

- Acceptance: A probe runs an AI player to the loss of its last refinery while it holds >= 2 harvesters and asserts zero CommandType.Harvest commands are emitted for 500 subsequent ticks, and that once a refinery is rebuilt the AI resumes issuing Harvest within 2 ticks and Carry rises within 400. `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0; any golden row that moves is named in the ADR beforehand and no other row moves. Determinism CI green on Windows and Linux.

#### P5-ECON-16. Harvester tempo gate is a tripwire, not a test

- Dimension: resource economy and harvesting
- Impact: LOW | Effort: S | cheapModelSafe: true | touchesSim: false
- Files: tools/Ferrostorm.Balance/Program.cs lines 7-8, 117-144
- Spec:

The Balance tool's only economy check asserts `tempoA >= 1400` - 'tempo collapsed below 2 delivered loads' (line 144). The measured value on that layout is far above it, so the economy could regress by 60% and the gate would pass. Meanwhile the file header (lines 7-8) still says 'Harvester tempo test awaits the second faction's opening layout', which is stale: the layout is committed and running at 117-137, and factions share one economy (noted at 118-119), so there is nothing left to wait for.

This matters because P5-ECON-04 and P5-ECON-05 both move harvester tempo, and there is currently no gate that would notice.

1. Run the tool, record the exact tempo integer, and commit it as `const long TempoBaseline = <measured>;`.
2. Replace the `< 1400` floor with a two-sided band, matching the discipline the counter matrix already uses: hard-fail if tempo differs from TempoBaseline by more than 3% in either direction. Message: `HARD FAIL: harvester tempo moved {pct}% ({tempo} vs baseline {TempoBaseline}) - Balance + Game Designer co-sign required (CLAUDE.md data conventions, charter A11).` Over-performance is as much a regression as under-performance; a one-sided floor cannot see the double-move bug in P5-ECON-04 at all.
3. Update the stale header comment at lines 7-8 to describe what the test now does.
4. Add a second measurement at a long haul to catch travel-speed regressions specifically, which the current single-distance layout cannot separate from load/unload changes: same Tempo() shape but with the field at Fix64.FromInt(45) instead of 20. Commit its baseline the same way. (Measured reference: cycle time scales 234 -> 389 ticks across an 8 -> 39 cell haul, so a speed change shows up ~3x more strongly at long range.)
5. Keep the existing reproducibility check (tempoA != tempoB) exactly as-is.

NOTE: Tempo() calls SpawnHarvester(0, ...) with the 3-arg form, so P5-ECON-05's default parameter keeps it compiling untouched - but its baseline WILL move when P5-ECON-04/05 land. Re-baselining is part of those tickets' ADRs, not a reason to skip this one; without the band there is nothing to re-baseline against.

- Acceptance: `dotnet run --project tools/Ferrostorm.Balance -c Release` exits 0 on unmodified main and prints both tempo figures with their baselines and deltas. Artificially editing World.HarvesterCapacity from 700 to 650 (a 7% change) makes it exit non-zero with the tempo message; reverting restores exit 0. Artificially DOUBLING harvester speed also makes it exit non-zero (proving the band is two-sided). Sim golden hashes unchanged - the Balance tool is not in the hash path.

---
## 4. Sim-change register

Every ticket in this roadmap that touches the sim, in one place, with what it costs. This section exists so the Architect can see the whole bill at once rather than discovering it one ticket at a time, and so the golden regenerations can be batched instead of paid for individually.

The headline: **28 of the 68 tickets touch /sim, but only 18 of them regenerate a golden hash, and one of those 18 must never be built.** Eight edit sim files while remaining provably hash-neutral (four of them hash-identical refactors whose goldens are the proof), and two are additive-only. So the real bill is 17 regenerations, which batch down to six ADRs.

### 4.1 The tickets that edit /sim but do NOT regenerate any hash

These need review but not an ADR, and their acceptance criteria all assert `git diff sim/golden-hashes.txt` is empty. A changed hash on any of them means the ticket was implemented wrongly, not that the baseline moved.

| Ticket | Why it is hash-neutral | Proof gate |
|---|---|---|
| C-03 biome map header | Parsed into MapData, read only by the client, never reaches BuildWorld. Exact precedent: MapData.Visual. | All 23 hashes byte-identical |
| BD-06 structure catalogue to /data | Every value ported byte-identical; a pure relocation. | All 23 hashes byte-identical |
| BD-14 rally attribution | Adds a trailing field to GameEvent. GameEvents are explicitly not in the hash (World.cs:1569-1620 never reads `_events`). | All 23 hashes byte-identical |
| DEF-03 footprint refactor | `FromFraction(2,2) == Fix64.One`, so size-2 arithmetic is bit-identical. No Entity field added. | All 23 hashes byte-identical |
| DEF-04 wall entity | Appended EntityKind, no Entity field, every new branch gated on a kind no existing scenario can create. | All 23 hashes byte-identical |
| DEF-05 attack-move breach | New branch unreachable without barriers. | All 23 hashes byte-identical |
| DEF-12 emplacement | New struct type nothing existing can build; a new Weapons.Get arm is static config, outside the hash. | All 23 hashes byte-identical |
| DEF-13b bastion | Same construction as DEF-12. | All 23 hashes byte-identical |

DEF-11 (Appendix A) belongs to this class too and is superseded by BD-06.

### 4.2 The tickets that regenerate goldens

Each row is a real cost: an ADR in docs/adr/, Architect sign-off, a regeneration run on both platforms via .github/workflows/determinism.yml, and a replay-compatibility break recorded. The extra sign-offs column is where CLAUDE.md charter A11 bites (a stat change over 15 percent needs Balance and Game Designer co-sign).

| Ticket | Rows expected to move | ADR | Extra sign-offs beyond Architect | Balance re-run |
|---|---|---|---|---|
| MAP-02 skirmish-01 ferrite | `skirmish` only | Yes, shared with C-13 | Game Designer | Yes (Balance tool's faction-war gate uses skirmish-01) |
| C-13 skirmish-01/02 terrain | `skirmish` (same commit as MAP-02) | Yes, shared with MAP-02 | Game Designer + Balance | Yes |
| DEF-16 CY queues hashed | `construction`, `skirmish`, `aisuper`, possibly missions. Diff, do not guess. | Yes | none | No |
| BD-05 sell CY refunds ready | production/construction rows | Yes | none | No |
| BD-07 honest power draw | Broad; every power-touching scenario | Yes | **Balance + Game Designer (A11)** | Yes |
| DEF-10 turrets offline <75% | `stealth`, `veil` at minimum; diff for more | Yes | none | No |
| BD-09 radar uplink | AI scenarios | Yes | Game Designer | No |
| BD-17 tech tree | Broad; read each gated scenario first | Yes | **Game Designer (the prereq table IS the tech tree)** | No |
| BD-20 queue cap 9 | production rows | Yes | none | No |
| DEF-14 AI defensive doctrine | `skirmish`, `aisuper` | Yes | **ai-engineer + Balance** | Yes |
| P5-ECON-04 harvester double-move | Broad; every economy scenario | Yes, shared | **Balance + Game Designer (A11, ~20% economy nerf)** | Yes, re-baseline |
| P5-ECON-05 SpawnHarvester /data | Broad; same batch as 04 | Yes, shared | none | Yes, re-baseline |
| P5-ECON-15 AI harvest flood | `skirmish`, `expansion`, `aisuper` | Bundle into the 04/05 ADR | none | No |
| P5-ECON-11 ferrite regrowth | New `regrowth` row; **save format version bump** | Yes | **Balance + Game Designer (the rates ARE the economy)** | Yes |
| P5-ECON-13 refinery free harvester | AI-driven rows only | Yes | Balance | Yes |
| P5-ECON-14 Outpost income | New `outpost` row | Yes | **Balance + Game Designer (naming question first)** | Yes |
| P5-ECON-12 multi-resource yield | New `multires` row; **save format version bump** | Yes | **Game Designer question BEFORE code** | No |
| MAP-10 flow-field guard | Every scenario that moves a unit | Yes | Architect only, and **DO NOT BUILD** until MAP-06's gate fails | No |

Additive-only, no ADR and no changed row, but they do append to sim/golden-hashes.txt: **MAP-06** (`bigmap`) and **DEF-06** (`walls`). Appending a row for a new scenario is not a replay-compatibility break.

### 4.3 Recommended batching

Seventeen regenerating tickets is not seventeen ADRs. Batched by what genuinely shares a decision:

1. **The map bundle** (MAP-02 + C-13). One ADR, one row, one Balance re-run. Do not let these land apart; the whole argument for paying the cost is paying it once.
2. **The wall batch** (DEF-02 ratified, then DEF-03 + DEF-04 + DEF-05 + DEF-06 in one merge). One ADR (ADR-005), zero changed rows, one added row. This is the cheapest large sim change in the plan and the batching is mandatory rather than advisory: DEF-04 without DEF-05 is a bug, and DEF-06 is what proves the other three did not move a hash.
3. **The economy batch** (P5-ECON-04 + P5-ECON-05 + P5-ECON-15). One ADR. 04 and 05 overlap on harvester speed; 15 is a small AI gate that regenerates the same AI rows anyway. Re-baseline the Balance tempo gate once, at the end, and land P5-ECON-16 first so there is a two-sided band to re-baseline against.
4. **The defect batch** (DEF-16 + BD-05 + BD-20). One ADR. Three small, independent, uncontroversial correctness fixes that all touch production and queues; they will collide in the same rows if landed separately.
5. **The power and tech batch** (BD-06 hash-neutral first, then BD-07 + DEF-10 + BD-09 + BD-17). One ADR if they land together, four if they do not. BD-17 hard-depends on BD-09, and DEF-10 is what gives BD-07's numbers teeth, so they belong together anyway.
6. **Standalone, each its own decision**: DEF-14, P5-ECON-11, P5-ECON-13, P5-ECON-14, P5-ECON-12. Each is a design question before it is a code change.
7. **Never**: MAP-10, until MAP-06's `bigmap` gate actually fails with the overage attributable to FlowField.Build by profile rather than by assumption.

### 4.4 The numbering collision, called out once

Four tickets independently claim `EntityKind = 11` and three claim struct type 9: DEF-04 (Wall), BD-09 (Radar), P5-ECON-14 (Outpost), plus DEF-12 (Emplacement) and DEF-13b (Bastion) contending for 12 and 13, and DEF-03 reserving struct type 10 for a gate that DEF-12 wants to take. An EntityKind collision is invisible in the hash and fatal in the save format, which writes `(byte)e.Kind`.

**ADR-005 owns the allocation for the whole roster.** Recommended and to be ratified there:

| EntityKind | Value | Struct type | Ticket |
|---|---|---|---|
| Wall | 11 | 9 | DEF-04 |
| Emplacement | 12 | 10 | DEF-12 |
| Bastion | 13 | 11 | DEF-13b |
| Gate | (reserved, unbuilt) | 12 | DEF-02 clause 6 |
| Radar | 14 | 13 | BD-09 |
| Outpost | 15 | 14 | P5-ECON-14 |

`World.FootprintOf` returns 1 for struct types 9 and 12 only; everything else takes the `_ => 2` default. Whichever ticket lands second takes the ratified number and does not renumber the first.

**RESOLUTION (2026-07-17).** The collision is settled in compiled code and the table above was overtaken; it is kept as history. The shipped enum (World.cs:46) runs Wall = 11, Barracks = 12 (which the table never anticipated), RadarUplink = 13, Airfield = 14, Emplacement = 15, Bastion = 16, Outpost = 17; the compiled struct types are 9 wall, 10 gate (reserved, deliberately no def), 11 barracks and 12 radar uplink (World.cs:447-459). ADR-009 (Proposed) carries the roster design and owns any future additions to the numbering.

---

## 5. Proposed amendment to docs/design/16-visual-style.md

**Status: PROPOSED. Blocked on Luke.** The style bible is his, and this is an explicit request to change three of its clauses, not a reinterpretation. It is quoted in full below so it can be read as the finished document rather than as a diff.

Two tickets are blocked on it and must not land ahead of it: **C-07** (the second team-colour location) and **DEF-07** (the wall meshes, whose team-band rule is this amendment's mechanical form). **C-12** and **DEF-18**, the two source tickets that proposed overlapping and conflicting versions of this text, are superseded by it and are preserved in Appendix A.

### 5.1 What is changing, and why

Three clauses are wrong. Everything else in doc 16 is good and is preserved verbatim: the cinder family, ferrite gold as the one warm signal, silhouette-first, the 40-pixel-blob test, the two shape languages, and the reference-implementation pointer.

**Clause 1, the ground is specified as one swatch.** The bible says cinder is #16181a. The ground has been a three-layer splat shader across the whole map since W4-11, and Wave A gives it three biomes. More importantly, a single swatch is what produced the current defect: all six colour uniforms sit within 12 percent of neutral grey and the largest red-to-blue ratio in the entire ground pass is 1.11, so an expensive three-layer splat paints a greyscale. The amendment makes cinder a family and makes hue separation a stated law with a measurable floor.

**Clause 2, the one-place team-colour law.** This is the substantive change and it deserves the honest framing. The law is correct for units and I would not touch it if units were all there were. But ModelLibrary ships one model per type shared by both players, and every structure except three is a `com_` model, so under the one-place law five structures and five common units carry no team colour at all: a Directorate refinery and a Sodality refinery are the same pixels. Worse, the shipped client already breaks the law and nobody wrote it down, because DressMobile adds a team ground ring on top of the model's baked band. So the choice is not between one place and two. It is between two places written down and two places pretended away. And at the camera band the game actually uses, height 8 to 42, a 6-pixel band on a 40-pixel silhouette is under the readable threshold anyway.

The barrier sub-clause comes from the same reasoning applied at a different scale. Forty chained wall segments each wearing a band is forty orange slashes in a row: a solid glowing line, the loudest object on screen, and a violation of the law's own intent, which is that the mark be a readable accent on a readable silhouette. For a chained barrier the silhouette is the run, not the segment. The mechanical rule is that the mark appears where the 4-bit neighbour mask has a neighbour count other than 2, which is exactly the end caps, corners, tees, crosses and isolated posts, and never on straight mid-run segments. There is a second-order effect worth ruling on deliberately: at 40 pixels a marked run reads as a dotted team-coloured perimeter rather than a line, which is arguably a better battlefield and minimap read than the literal law would give.

**Clause 3, the hex table describes nothing that renders.** The builder's palette holds sRGB-looking fractions in Blender's scene-linear Base Color slots, so gunmetal is on screen at #95A2AC, not the bible's #5b6770. The fix is not to adopt the bible's hexes as linear values, which would drop Sodality rust by 62 percent luminance and make the faction vanish; the shipped value is defensible and the missing chroma is the defect. C-05 restores chroma while holding luminance, and the table below is the rendered result of C-05's palette. The amendment also states, for the first time, that the palette of record is the code and the hexes are review aids, so the two cannot drift again silently.

**One clause is added rather than amended: the emissive rule.** It is new because the bug it prevents was invisible until this review measured it. Every emissive in the roster bakes clamped into an 8-bit buffer, and clipping one channel of a saturated colour is a hue shift, not a brightness cap, so the bible's one permitted team mark is currently rendered as a white-hot blob and the Sodality signature colour is destroyed at bake time. A style bible that mandates saturated self-lit marks has to say how they are encoded.

### 5.2 The proposed text, in full

Replace the whole of docs/design/16-visual-style.md with the following. British spelling; no em dashes or en dashes; the hexes assume C-05's palette has landed.

---

```
# Ferrostorm Visual Style Guide

## The world's materials decide the palette
Cooled cinder is the ground of every screen, but cinder is a FAMILY, not one
swatch. Each map declares a biome and the ground shader paints three layers
(dust, rock, gravel) whose HUES differ, not merely their values. A biome must
carry a measurable hue spread: within one biome the dust layer runs warm (red
to blue ratio at or above 1.35) and the rock layer runs cool (at or below
0.80). A ground assembled from three near-neutral greys is a defect, not a
mood, and produces mud at the only camera the game actually uses. Ferrite
gold (#EAC474, highlight #F6DD95) is the light of this world: the resource,
the instrument trim, the thing everyone is fighting over. It must be present
at map scale as ferrite-bearing ochre in the ground, not only as the handful
of deposit cells. Plating (#232629) and seams (#2e3236) build the interface
from the same foundry. Text is unbleached document bone (#d6d2c4).

The palette of record is art/3d/builder.py PAL, authored as scene-linear
triples. The hexes in this document are the rendered sRGB result of those
triples: they are authoritative for review, never for entry. Changing one
without the other is a defect.

## Two visual languages, one law
DIRECTORATE - the wall: slab-sided, symmetric, issued. Gunmetal #7c9ebc,
plate #9eb8d4, shadow #5b7790. Team mark: signal orange #e8762c.
SODALITY - the shadow: angular, asymmetric, welded-from-salvage. Rust
#c4774d, plate #d78b5f, shadow #995b3c. Team mark: corroded teal #4fb8a8.
COMMON hardware: field olive #959c77 with ferrite-gold marks.

The law, amended: team colour appears in exactly TWO places per actor and
never more. The PRIMARY mark is on the silhouette itself, the band or slash,
self-lit so it survives night and shroud. The SECONDARY mark is the ground
plate: a team ring under every mobile unit, a team strip around the footprint
of every structure. Common hardware carries no faction geometry, so for
common hardware the ground plate is the ONLY mark and it is mandatory.

Why the amendment. The roster ships one model per unit type, shared by both
players, so under the one-place law five structures and five common units
carried no team colour at all: a Directorate refinery and a Sodality refinery
were the same pixels. The shipped client had already added a second place, a
team ground ring, without the law being updated to admit it. And at the
camera band the game actually uses, height 8 to 42, a 6-pixel band on a
40-pixel silhouette is below the readable threshold. Two places is the
minimum that works; three would be a colouring book.

Chained barriers are the one exception, and they are an exception because
the law's unit of measure is the silhouette rather than the entity. For a run
of wall segments the silhouette is the RUN, not the segment. The team mark
therefore appears only at a run's terminations and junctions: mechanically,
where the segment's 4-bit neighbour mask has a neighbour count other than 2,
which is the isolated posts, end caps, corners, tees and crosses, and on
gates when they exist. Straight mid-run segments carry no mark and barriers
take no ground plate. Forty segments each wearing a band is a solid glowing
line, which is the opposite of what one readable accent per readable
silhouette was ever meant to produce.

Silhouette-first still holds, unamended: every unit must be identifiable as a
40-pixel blob. The bulwark is a slab, the phantom a faceted wedge, the
harvester a fat beetle with a gold hopper, squads are dot-clusters.

Saturation floor: every faction body colour must render at HSV saturation
0.24 or above, and the two faction bodies must differ in hue by at least 90
degrees. Gunmetal sits at hue 208 and rust at hue 20, a separation of 188.

## The emissive rule
Baked emission maps are 8-bit. An emissive material's colour multiplied by
its emission strength must not exceed 1.0 in any channel at bake time. Clip
one channel of a saturated colour and you have not dimmed it, you have
changed its hue: signal orange at strength 2.2 bakes yellow, corroded teal at
1.8 bakes cyan-white. Carry intensity in the exported emissive strength
(KHR_materials_emissive_strength), never in the baked map.

## Reference implementations
art/sprites/*.svg (19 sprites, rendered PNGs in art/png/, contact sheet at
art/contact-sheet.png) and the match viewer (tools/viewer/) share one shape
language; the viewer's signature is the ferrite-seam glow draining with the
deposit. Blender/contractor work later should treat these plus the
post-Wave-4 captures as the style bible: stylized low-poly, silhouettes,
saturation floor and the two-place team-colour law as above.
```

---

### 5.3 Acceptance for the amendment

- docs/design/16-visual-style.md contains the text above verbatim.
- The file contains no em dash (U+2014) and no en dash (U+2013): `grep -nP '[\x{2014}\x{2013}]' docs/design/16-visual-style.md` returns nothing.
- Every hex in the Directorate, Sodality and common lines matches the rendered sRGB output of the corresponding C-05 PAL entry to within 2/255 per channel (compute from PAL with the linear-to-sRGB transform). This check must be run, not assumed: the whole reason for the amendment is that these two drifted apart unnoticed.
- The phrase 'exactly one place' does not appear anywhere in docs/design/. Note there is one other live occurrence today, at docs/tickets/phase-1-backlog.md:126, in a completed ticket's historical record; leave it alone, it is a log of what was true at the time.
- Game Designer sign-off recorded, and Luke's sign-off recorded on the amendment specifically. C-07 and DEF-07 are blocked until it lands.
- If Luke rejects the barrier sub-clause, DEF-07's team-band rule and DEF-08's mask table both change; do not ship them ahead of the decision. If Luke rejects the two-place law, C-07 does not ship and the honest consequence must be recorded: half the roster stays visually unattributable, and DressMobile's existing ground ring should then be removed to bring the shipped client back into compliance with the law as written.

### 5.4 The anti-air deferral, recorded rather than ticketed

Doc 22 carries this rather than inventing a ticket for it. Anti-air defence is not in this roadmap and the reason is that there is no air layer at all: EntityKind has no aircraft, no entry in the 12-type unit catalogue flies, no WeaponDef carries an anti-air flag, and the sim has no concept of altitude. An AA turret today would be an anti-ground turret with a misleading name and a lie in the sidebar. GDD s7 line 86 does reserve an "Aircraft" sidebar tab, so air is planned.

The trigger condition rather than a date: **AA becomes ticketable the moment an ADR ratifies an air layer, and it should be specified inside that ADR rather than bolted on afterwards**, because whether AA is a separate structure, a turret upgrade, or a targeting property of existing weapons is a decision the air layer's design makes and cannot sensibly be pre-empted. File this as a docs/questions/ entry with an owner and a decide-by date, per the CLAUDE.md workflow rule, rather than leaving it as a comment in a roadmap nobody greps.

---
## Appendix A: superseded tickets, preserved verbatim

**DO NOT IMPLEMENT ANYTHING IN THIS APPENDIX.** Each ticket here is a duplicate or a conflicting variant of a canonical ticket in the register above. They are preserved for three reasons: several carry values that would corrupt the canonical work if a model implemented both, several carry findings the canonical ticket does not, and the deduplication decisions in section 3.1 should be auditable rather than taken on trust.

Every entry names its canonical replacement.

---

### A-1. P5-ECON-02 (SUPERSEDED by MAP-02; retained as the FALLBACK if the Architect refuses the golden regeneration)

> **Ship skirmish-04.fmap as the default skirmish map: symmetric, GDD-calibrated economy**
> Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
> Files: data/maps/skirmish-04.fmap (new); game/scripts/SkirmishLive.cs lines 166-167

**Why superseded:** it fixes the default by routing around skirmish-01 rather than repairing it, leaving the broken map in the theatre picker. MAP-02 repairs it for the price of one golden row, and C-13 needs that same regeneration anyway. Its filename also collides with MAP-04's 192x128 map; if this fallback is ever invoked it takes skirmish-05.

**Why retained:** it is the only route that ships today with zero sim risk. If the Architect refuses the MAP-02 regeneration, this is the plan.

> Spec: The default map starves. Its home economy dies at 1.65 min against the GDD's stated ~8, and it is measurably unfair. Fix by ADDING a map, not editing skirmish-01 (see P5-ECON-03 for why that is not free).
>
> A verified candidate file already exists at /private/tmp/claude-501/-Users-lgreene/3720c1c7-f32d-428a-9e1c-67ea0ec44f1a/scratchpad/skirmish-04.fmap - copy it to data/maps/skirmish-04.fmap. If regenerating from scratch, the exact spec is:
>
> HEADER: `ferrostorm-map v1` / `size 96 64` / `start 0 8 30` / `start 1 87 30` / `grid:`
> (starts are 8 and 87 so the mirror axis x' = 95-x pairs every one of the 96 columns; skirmish-01's 8/86 leaves a column unpaired, which is part of why it is asymmetric.)
>
> TERRAIN (write the left half, then mirror every '#' via x' = 95-x):
> - Central spine: '#' at x=46..49 for all y EXCEPT the three corridors y=8..13, y=28..34, y=50..54.
> - Upper-left ridge: '#' block at x=5..12, y=5..8.
> - Lower-left ridge: '#' block at x=5..12, y=55..58.
> Total blocked cells: 304.
>
> FERRITE (write these 11 'F' cells, then mirror each via x' = 95-x for 22 total):
> - Home cluster (5): (20,23) (22,24) (19,26) (21,27) (23,25)
> - Midfield cluster (4): (30,40) (32,41) (31,43) (33,42)
> - Gap edges (2): (44,10) (44,52)
>
> CLIENT: SkirmishLive.cs line 167, change the fallback filename in the default mapPath from "skirmish-01.fmap" to "skirmish-04.fmap". Do not touch MatchConfig.MapPath - the menu already enumerates data/maps/*.fmap (MainMenu.cs:60), so skirmish-04 appears automatically and skirmish-01 stays selectable.
>
> MEASURED RESULT (probed against the real sim): parses clean; 96x64; 304 blocked; 22 fields; both starts passable with ValidFoundation true; 0 asymmetric terrain cells and 0 asymmetric ferrite cells under x'=95-x; home cluster 5 fields at 11.7/13.3/13.9/15.2/15.8 cells, IDENTICAL multiset for both players; home economy (60000 ferrite, 3 harvesters) dries at tick 6849 = 7.61 min for p0 and 6922 = 7.69 min for p1 - landing on GDD s4's 'force expansion by minute ~8'.
>
> Acceptance: MapData.Load("data/maps/skirmish-04.fmap") parses without exception and yields Width==96, Height==64, Blocked.Count==304, Fields.Count==22, Starts[0]==(8,30), Starts[1]==(87,30). A test asserts full mirror symmetry: for every (x,y), grid[y][x]=='#' iff grid[y][95-x]=='#' AND grid[y][x]=='F' iff grid[y][95-x]=='F' (0 violations each). BuildWorld(1,2).ValidFoundation is true at both start anchors. The sorted multiset of Euclidean distances from start 0 to all 22 fields equals that from start 1 (exact equality). `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 with golden-hashes.txt unchanged (skirmish-01 untouched). The Godot battle scene instantiates headless on the new default and runs 1000 frames without error.

---

### A-2. P5-ECON-03 (SUPERSEDED by MAP-02)

> **Re-field and re-symmetrise skirmish-01 (golden regen + selftest updates)**
> Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: TRUE
> Files: data/maps/skirmish-01.fmap; sim/Ferrostorm.Sim.Runner/Program.cs lines 1371-1375; sim/golden-hashes.txt

**Why superseded, and this one matters:** it proposes the same fix on the WRONG mirror axis. It mirrors on x' = 95-x and moves start 1 from x=86 to x=87. MAP-02 measured that axis and found it hands player 1 a one-cell advantage on every field (summed distance-to-own-ten-nearest: P0=201 versus P1=191), because the true start midpoint is x=47, not x=47.5. MAP-02's x -> 94-x gives a delta of exactly 0, additionally preserves the `md.Fields.Contains((47,31))` selftest assertion, and leaves Blocked.Count at 248 so Program.cs:1370 needs no edit at all. **Implementing this ticket's axis after MAP-02's would reintroduce the unfairness it was written to remove.**

**Findings worth keeping:** its measured asymmetry census (16 asymmetric terrain cells and 6 asymmetric ferrite cells under 180-degree rotation; P0's home field 15.2 cells from start against P1's 13.4, a permanent ~12% haul advantage to the AI; skirmish-02 and skirmish-03 both score a perfect 0/0 on the same test) and its full list of downstream consumers.

> Spec: TOUCHES SIM - flagged. Editing skirmish-01.fmap changes hashed state and REQUIRES Architect sign-off + golden regeneration. Schedule this only when a golden regen is already happening for another reason; P5-ECON-02 delivers the player-facing win without it.
>
> Why it is not a free data change: skirmish-01.fmap backs (a) BuildSkirmishWorld (Program.cs:464-476), which is the world for the gated 'skirmish' golden scenario (current hash 0xDF844901214FD6C5); (b) two hard-coded selftest assertions at Program.cs:1371-1375; (c) the Balance tool's 6-seed faction-war gate (tools/Ferrostorm.Balance/Program.cs, `mapPath` near the faction section). More fields changes AI harvest targeting, hence the command stream, hence the state hash.
>
> Steps:
> 1. Rewrite data/maps/skirmish-01.fmap using the P5-ECON-02 terrain and ferrite layout, keeping `start 0 8 30` / `start 1 86 30` if the starts must be preserved for continuity - but note 8/86 cannot be perfectly mirror-symmetric on a 96-wide grid; prefer moving start 1 to 87 and accepting the change.
> 2. Update Program.cs:1373 from `md.Fields.Count != 3 || !md.Fields.Contains((47, 31))` to the new count and a cell that actually exists. Update the failure string - 'the prize sits in the Ferrite Gap' must still be true of whatever cell is asserted.
> 3. Update Program.cs:1374 `mw.EntityCount != 3` to the new field count.
> 4. Update Program.cs:1371 `md.Blocked.Count != 248` to the new blocked count.
> 5. Regenerate sim/golden-hashes.txt for the 'skirmish' row (and any other row that moves).
> 6. Re-run the Balance tool; the faction-war gate must still show neither faction taking more than 4 of 6 seeds.
> 7. Write the ADR recording the replay-compatibility break.
>
> CURRENT ASYMMETRY BEING FIXED (measured): 16 asymmetric terrain cells and 6 asymmetric ferrite cells under 180-degree rotation; all 3 fields fail. Player 0's home field is 15.2 cells from start, player 1's is 13.4 - a permanent ~12% haul advantage to the AI. skirmish-02 and skirmish-03 both score a perfect 0/0 on the same test, so this is skirmish-01 alone.
>
> Acceptance: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 (selftest + double-run determinism + golden). Determinism CI is green on Windows and Linux. The symmetry test from P5-ECON-02's acceptance passes on skirmish-01. sim/golden-hashes.txt shows exactly the rows the ADR predicted and no others. The Balance tool exits 0 with no faction taking >4 of 6 seeds. An ADR in docs/adr/ records the break and carries Architect sign-off.

---

### A-3. BD-04 (SUPERSEDED by TICKET-P5-DEF-16)

> **Fix ComputeStateHash omitting Construction Yard build queues**
> Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs, sim/golden-hashes.txt, sim/Ferrostorm.Sim.Runner/Program.cs

**Why superseded:** identical bug, identical one-condition fix, found independently by two dimensions. DEF-16 carries the fuller desync-detection analysis (the up-to-40-second detection delay) and a stronger negative-control assertion. This ticket's assertion is a useful addition to DEF-16's.

> Spec: REAL DEFECT, sim behaviour unchanged but HASH CHANGES => golden regeneration + Architect sign-off per ADR/CLAUDE.md. World.cs:1594 reads `if (e.Kind == EntityKind.Factory && _queues.TryGetValue(e.Id, out var q))`. Construction Yards use the same `_queues` dictionary (World.cs:664) and `World.Serialization.cs:47-48` persists all of them, but the CY's queued structure-type list is never hashed - so a divergence in a player's STRUCTURE queue would not be caught by the cross-platform golden-hash desync gate. Fix: change the condition to `if ((e.Kind == EntityKind.Factory || e.Kind == EntityKind.ConstructionYard) && _queues.TryGetValue(e.Id, out var q))`. Nothing else changes; `_queues` iteration here is already keyed off the ordered entity loop, so determinism is preserved. Then regenerate sim/golden-hashes.txt on BOTH Windows and Linux via the CI workflow (.github/workflows/determinism.yml) - do not hand-edit the file from one platform. Add an assertion to the production scenario (Program.cs ~line 195-277): after queueing two structures at a CY, `ComputeStateHash()` must differ from the same world with a one-item CY queue.
>
> Acceptance: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0 (selftest + double-run determinism + benchmark). New assertion proves CY queue contents affect the hash. determinism.yml green on Windows AND Linux with the regenerated hashes. No scenario assertion deleted or loosened.

---

### A-4. BD-21 (SUPERSEDED by the TICKET-P5-DEF-02..08 set)

> **Walls: 1x1 footprints and defensive base geometry**
> Impact: HIGH | Effort: L | cheapModelSafe: false | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs, data/buildings/com_wall.yaml (new), game/scripts/SkirmishLive.cs, game/scripts/Sidebar.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt

**Why superseded:** one ticket sketching what the DEF set specifies across seven. The DEF set carries the ADR, the bit-identical footprint proof, the attack-move breach (which this ticket does not mention and without which walls delete attack-move), the scenario, the art, the client drag-line and the CI balance gate. Every insight here is present there: the victory-test exclusion, the build-radius exclusion, and the anchor-recovery trap.

**Note the disagreements, both resolved in the DEF set's favour:** this ticket proposes wall cost 50 / hp 300 / BuildTicks 15 and EntityKind.Wall = 12; DEF-04 proposes cost 100 / hp 500 / BuildTicks 0 and EntityKind.Wall = 11, with the upfront-pay model ADR-005 clause 3 ratifies. Implementing both value sets would produce a wall that is neither.

> Spec: CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. There is no wall, barrier, or sandbag entity of any kind, so base layout has no defensive geometry and the strict-adjacency rule (GDD Q2) expresses nothing but reach. THE BLOCKER IS `FootprintSize = 2` (World.cs:354), a global const that every placement path assumes. 1) Add `int Footprint = 2` as a trailing defaulted field on StructureTypeDef; wall = 1. 2) Generalise the four footprint helpers (World.cs:360-376): `BlockFootprint(ax, ay, size)`, `UnblockFootprint(ax, ay, size)`, and `FootprintCentre(anchor, size)` => `Fix64.FromInt(anchor) + Fix64.FromFraction(size, 2)` (size 2 => anchor+1, preserving today's exact value; size 1 => anchor+0.5). 3) ANCHOR RECOVERY IS THE TRAP - three sites currently hardcode `Map.CellOf(e.X) - 1` (World.cs:699 SellStructure, 1081 combat death, 1500 ApplyAreaDamage). The general form is `Map.CellOf(e.X) - GetStructureType(e.StructType).Footprint / 2`, which yields -1 for size 2 and -0 for size 1. Getting this wrong unblocks the WRONG CELLS on death and silently corrupts the map with no test failure - add an explicit round-trip assertion (step 7). 4) Generalise the footprint loops in `ValidPlacement` (749-756) and `ValidFoundation` (725-731) to take the size from the type being placed. 5) Wall stats (Game Designer sign-off): com_wall type 10, EntityKind.Wall = 12 (APPEND to the enum - EntityKind is serialized and cast at SkirmishLive.cs:655), cost 50, BuildTicks 15, hp 300, ArmourClass.Structure, footprint 1, no power draw, no weapon, Sight 0. MUST be added to `IsStructure` (World.cs:719-722) or sell/repair/rubble/adjacency all silently misbehave. 6) DESIGN DECISION REQUIRING SIGN-OFF: walls in `IsStructure` means they satisfy the VictorySystem short-game rule (World.cs:1553) and project a BuildRadius - a 50-credit wall would let a player survive with no real base and creep their radius across the map. Recommendation: exclude Kind == Wall from both the VictorySystem `hasHope` test and the ValidPlacement radius-projection loop (World.cs:764-771), i.e. walls consume adjacency but never grant it. That is the classic rule and it needs an explicit decision, not a default. 7) New scenario: place walls at several anchors, destroy them, assert `Map.IsBlocked` is false for exactly the 1 cell each occupied and true for no neighbour - this is the anchor round-trip that catches the step-3 trap. 8) Client: sidebar entry, a placement mode that supports click-drag to lay a LINE of walls (issue one PlaceStructure per cell along the dragged axis), and a wall model (none exists - interim: a scaled BoxMesh in the doc-16 Directorate gunmetal #5b6770; file an art ticket). cheapModelSafe=false: the drag-to-lay placement UX is genuinely taste, the VictorySystem/build-radius interaction is a design decision, and the anchor refactor silently corrupts state when wrong.
>
> Acceptance: Full runner gate exits 0 with the new anchor round-trip scenario passing for BOTH footprint sizes; goldens regenerated on both platforms. Existing 2x2 structures are bit-identical in behaviour - verified by the fact that no existing placement/sell/death assertion needed changing. A player with only walls and no other structure is still eliminated by the short-game rule. Walls do not extend the build radius. Drag-laying 10 walls costs exactly 500 credits and blocks exactly 10 cells.

---

### A-5. BD-22 (SUPERSEDED by P5-ECON-14)

> **Neutral capturable Depots granting +15 credits/tick (GDD s4)**
> Impact: HIGH | Effort: L | cheapModelSafe: false | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim/MapLoader.cs, data/maps/*.fmap, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt, game/scripts/SkirmishLive.cs

**Why superseded:** P5-ECON-14 reaches the same feature through the existing `structure -1 9 X Y tag` syntax with no map-format version bump, where this ticket adds a 'D' grid char and bumps the format to v3. P5-ECON-14 also isolates the latent neutral-structure crash as an independently shippable fix.

**Three findings carried into P5-ECON-14 rather than lost:** (1) the "Ferrite Cache" name, which is a live candidate in the naming question alongside "Outpost"; (2) the rate analysis, which is sharper than P5-ECON-14's - 15 credits/tick at 15 Hz is 225 credits/second, roughly ten times a harvester, and the GDD almost certainly means 15 per second, so `if (Tick % 15 == 0)`; (3) the victory-test interaction, which P5-ECON-14 does not raise: a captured cache satisfies hasHope, so a player with one and no base is never eliminated.

> Spec: CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. GDD s4 line 41 specifies capturable neutral 'Depot' structures granting +15 credits/tick as the map-control incentive ('replacing oil derricks in spirit'). Unbuilt. This is the strongest single addition to base-building depth because it is the only mechanic that pulls a base OUTWARD onto the map - currently the only reason to leave home is ferrite, and skirmish-01 has just 3 ferrite cells. 1) NAME COLLISION, RESOLVE FIRST: `EntityKind.ServiceDepot` (type 8) is the repair depot and is player-built. The GDD's 'Depot' is a different, neutral, capturable thing. Propose the in-fiction name 'Ferrite Cache' (id com_ferrite_cache) and file the naming question in docs/questions/ with owner game-designer per CLAUDE.md workflow rule 5 - do not silently overload 'Depot'. 2) Sim: EntityKind.FerriteCache = 13 (APPEND), StructType 11, `SpawnFerriteCache(int ax, int ay)` with PlayerId = -1 (neutral), Hp 800, Armour Structure, no weapon, Sight 4, BuildTicks 0 and Cost 0 so `BuildStructure` refuses it (World.cs:661 already gates on `Cost <= 0 || BuildTicks <= 0`). Add to `IsStructure` so `CaptureSystem` (World.cs:913-943) accepts it - capture already works on any structure with `t.PlayerId != e.PlayerId`, and neutral (-1) satisfies that with no change. 3) Income: in ProductionSystem's entity loop, `if (e.Kind == EntityKind.FerriteCache && e.PlayerId >= 0) { _credits[e.PlayerId] += 15; continue; }` - 15/tick at 15Hz is 225 credits/second, which is HUGE (a harvester round-trip is ~700 credits per ~30s). GDD says '+15 credits/tick' but almost certainly means per-second in the classic idiom. FLAG FOR GAME DESIGNER: recommend +15 per SECOND, i.e. `if (Tick % 15 == 0)`, giving 15 cr/s or ~45% of a harvester's rate - a real prize that does not replace mining. Do not ship 225/s. 4) VICTORY INTERACTION, NEEDS A DECISION: a captured cache is an own structure and would satisfy the VictorySystem `hasHope` test (World.cs:1553), so a player with one captured cache and no base is not eliminated. Recommend excluding it, same as walls in BD-21. 5) Map format: MapLoader.cs (v1/v2) parses grid chars `. # F` plus visual classes `w h r f B`. Add a `D` grid char spawning a cache at that cell, bumping the map version to v3 with a fallback so v1/v2 maps still load unchanged. Place 2 caches on skirmish-02 and skirmish-03 (20 ferrite cells each) at contested mid-map positions; leave skirmish-01 alone until its separate 3-ferrite-cell bug is fixed. 6) Client: neutral team colour (ferrite gold, per BattlefieldView's existing neutral case at SkirmishLive.cs:512), a capture-flash on the `Captured` event (already emitted, World.cs:934), a minimap dot, and an income readout in `_selInfo`. Model: none exists - interim reuse com_service_depot.glb; file an art ticket. 7) SkirmishAI does not know caches exist and will never contest them; file an AI ticket rather than leaving the player unopposed on the only map-control mechanic. cheapModelSafe=false: the income rate is a balance decision the GDD states ambiguously, the naming collision needs a human, and the victory interaction is a design call.
>
> Acceptance: Full runner gate exits 0; goldens regenerated on both platforms. New scenario: neutral cache exists at tick 0; an engineer captures it (`Captured` event fires, PlayerId flips to the engineer's owner, engineer is consumed); credits then accrue at exactly the signed-off rate and stop when the cache is destroyed. A v1 and a v2 .fmap still load byte-identically (assert the golden hash of a loaded skirmish-01 world is unchanged). BuildStructure for the cache type is refused. A player whose only structure is a captured cache is still eliminated.

---

### A-6. BD-16 (SUPERSEDED by P5-ECON-13)

> **Refinery ships a free harvester (GDD s4)**
> Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt

**Why superseded:** same feature, same GDD line, same SpawnOffsets approach. P5-ECON-13 additionally flags the AI interaction (the AI gates harvester production on `harvesters < refineryCount`, so a free harvester per refinery stops the AI building harvesters at all) and the hard dependency on P5-ECON-07 (a free harvester that spawns idle forever is worse than none). Its clause 4 warning about broad scenario churn from shifted entity ids is worth carrying.

> Spec: CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. GDD s4 line 39: 'Refinery: 2,000 credits, includes one free harvester.' `SpawnRefinery` (World.cs:250-261) spawns nothing, so a refinery is 2000 credits of pure overhead and the GDD's intended '2 refineries / 3 harvesters' float (s4 line 40) costs 2000 more than designed. This is a real economic-pacing bug, not a feature request. 1) In `SpawnRefinery`, after Add(...) returns the refinery id, spawn a harvester using the SAME deterministic offset ring the factory uses - iterate `SpawnOffsets` (World.cs:1339-1340) from the refinery's anchor and take the first in-bounds unblocked cell, exactly as ProductionSystem does at World.cs:1461-1472. Do NOT invent a new offset scheme; determinism depends on this being identical. 2) Set the new harvester's `HState = HarvestState.Idle` and `FieldId = -1` - do not auto-task it in the sim. Player-side auto-harvest is BD-15's client concern and SkirmishAI already handles its own (SkirmishAI.cs:153-157); putting it in the sim would duplicate that logic and change AI behaviour twice. 3) Emit `new GameEvent(GameEventType.ProductionComplete, harvesterId, 4)` so BD-15's client handler auto-tasks it and the toast fires - type 4 is com_harvester in the unit catalogue (World.cs:286). 4) CAUTION: `SpawnRefinery` is called from scenario scripting in Program.cs and from MapData.BuildWorld; every scenario that spawns a refinery now gains an entity, shifting all subsequent entity ids. Expect broad but mechanical scenario churn.
>
> Acceptance: Full runner gate exits 0 with no assertion deleted or loosened - only entity-id and count expectations may shift. Goldens regenerated on both platforms via determinism.yml. New assertion: `SpawnRefinery` raises EntityCount by exactly 2 and the second is a Harvester owned by the same player within 3 cells. In-game, placing a refinery yields a harvester that drives to ferrite unprompted (with BD-15).

---

### A-7. BD-15 (SUPERSEDED by P5-ECON-07; its rally-precedence rule is folded in as P5-ECON-07 clause 2a)

> **Harvesters auto-harvest on production and on refinery completion**
> Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false
> Files: game/scripts/SkirmishLive.cs

**Why superseded:** P5-ECON-07 covers the same three situations plus the idle-forever case, respects an explicit Stop (which this ticket explicitly declines to do), and rate-limits the re-issue so it does not reproduce the AI's command flood through the lockstep relay. This ticket's clause 2 (an explicit rally beats auto-harvest) is the better rule and has been folded into P5-ECON-07.

**Note the direct disagreement:** this ticket says "Do NOT auto-order a harvester the player has explicitly Stopped this tick; the 15-tick poll naturally re-tasks it, which matches the classic". P5-ECON-07 says an explicitly parked harvester stays parked forever, citing GDD s1's "anything that automates strategy is out". P5-ECON-07's reading is adopted. This is a genuine design disagreement between two analyses and the Game Designer may overrule it.

> Spec: Pure client, lockstep-legal (same command path as the existing rally code). The AI auto-tasks every idle harvester (SkirmishAI.cs:153-157); the human must hand-order every single one, forever - the AI literally has QoL the player does not. GDD pillar 5: 'Every QoL feature that doesn't change strategy is in.' 1) In the event loop beside the rally handler (SkirmishLive.cs:426), add: on `ProductionComplete` where the new entity `_world.Entities[ev.A]` is `Kind == EntityKind.Harvester && PlayerId == 0`, find the nearest living FerriteField with `FerriteAmount > 0` to that harvester and emit `new Command(0, 0, CommandType.Harvest, ev.A, Fix64.Zero, Fix64.Zero, fieldId)`. Mirror SkirmishAI's selection exactly - nearest by `Fix64.DistSq`, ties to the lower id - so player and AI behave identically. 2) Ordering: this must LOSE to an explicit rally. If `_rally` (BD-14) has an entry for `ev.C`, skip the auto-harvest - a player who set a rally meant it. 3) Also cover the idle case: every 15 ticks, for each own harvester with `HState == HarvestState.Idle && !Moving`, issue the same Harvest command (mined-out field, or a manual Stop). Read `_world.Entities[...].HState` post-Step - the exact precedent already used for the W3-09 harvest FX at SkirmishLive.cs:711. 4) Do NOT auto-order a harvester the player has explicitly Stopped this tick; the 15-tick poll naturally re-tasks it, which matches the classic and is what SkirmishAI does. Note that behaviour in the code comment.
>
> Acceptance: Build a harvester with no rally set: it drives to the nearest ferrite field and begins loading with zero player input. Mine a field to exhaustion: its harvesters retask to the next nearest within 1 second. Set a factory rally, then build a harvester: it goes to the rally, not the field. Player and AI harvester throughput match within 5% over a 3000-tick mirrored run.

---

### A-8. BD-08 (SUPERSEDED by TICKET-P5-DEF-10)

> **Turrets go offline below 75% power (GDD s5)**
> Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs, sim/Ferrostorm.Sim.Runner/Program.cs, sim/golden-hashes.txt, game/scripts/SkirmishLive.cs

**Why superseded:** same GDD clause, same integer 75% test, but DEF-10 found what this ticket missed - ScenarioVeil spawns its turret for a player with no power plant and would throw, turning CI red, and ScenarioStealth spawns its turret 100 ticks before its plant so its Rule 1 assertion would keep passing for entirely the wrong reason. Implementing this ticket as written breaks the build.

**One clause worth carrying into DEF-10:** its step 1 proposes factoring the duplicated supply/draw tally at World.cs:853-859 and 1349-1357 into a shared `ComputePower(Span<int>, Span<int>)` called from all three systems, keeping three independent calls because CombatSystem can kill a plant and ProductionSystem must see post-combat totals. DEF-10 inlines a third copy instead. The refactor is cleaner and the reasoning about call ordering is correct; take it.

> Spec: CHANGES SIM BEHAVIOUR => golden regeneration + Architect sign-off. GDD s5 line 48 promises 'defensive turrets go offline at <75%'; `CombatSystem` (World.cs:945-1099) never reads power, so turrets fire at zero supply. This is the clause that makes plant-sniping a real tactic and gives BD-07 teeth. 1) Factor the supply/draw tally duplicated at World.cs:853-859 and 1349-1357 into `private void ComputePower(Span<int> supply, Span<int> draw)` iterating `_entities` in index order (identical loop, no behaviour change). Call it from DetectionSystem, ProductionSystem, and now CombatSystem. Keep three independent calls - CombatSystem can kill a plant, so ProductionSystem must see the post-combat totals, which is the existing behaviour. 2) At the top of the CombatSystem entity loop, after `if (!e.Alive || e.WeaponId == 0) continue;` (World.cs:957), insert: `if (e.Kind == EntityKind.Turret && e.PlayerId >= 0 && draw[e.PlayerId] > 0 && supply[e.PlayerId] * 4 < draw[e.PlayerId] * 3) { _entities[i] = e; continue; }` - integer 75% test, no division, no Fix64 needed. The `draw > 0` guard preserves the existing 'no draw = full power' semantics used at World.cs:1428. 3) Do NOT emit a new GameEvent for this; presentation derives it (step 4). 4) Client feedback (SkirmishLive.cs, near the damage-smoke block at 692-706): when the local player's `supply * 4 < draw * 3`, set every own Turret actor's MaterialOverride emission energy to 0 and add a slow amber blink on the HUD PWR string; restore on recovery. Reuse the already-computed supply/draw at SkirmishLive.cs:499-501.
>
> Acceptance: New scenario: player 0 gets 1 plant (supply 100), 1 turret (draw 20) and 3 more turrets (draw 80 total = 100); enemy rifle walks into range and is shot. Sell the plant: supply 0, draw 80 => turrets stop firing (assert zero `Fired` events from turret ids over 100 ticks and the rifle survives at full HP). Re-place a plant: firing resumes on the next tick. Boundary: supply 75 vs draw 100 fires (75% inclusive), supply 74 vs draw 100 does not. Full runner gate exits 0; goldens regenerated on both platforms.

---

### A-9. TICKET-P5-DEF-13, clauses 1 to 3 and 8 (SUPERSEDED by BD-17; its clause 4 survives as DEF-13b)

> **Sim: structure prerequisites and a tier-2 turret - the missing progression**
> Impact: MEDIUM | Effort: M | cheapModelSafe: true | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs; sim/Ferrostorm.Sim/Combat.cs; sim/Ferrostorm.Sim.Runner/Program.cs; game/scripts/Sidebar.cs

**Why the prereq clauses are superseded:** this ticket implements prerequisites as a hard-coded `PrereqOf` switch in World.cs. Every unit YAML already declares a prerequisites list that DataLoader already parses and ToTypeDef silently drops, and CLAUDE.md requires gameplay numbers to live in /data. A hard-coded switch would be a second source of truth beside the one that already exists. BD-17 wires the existing data path instead. The two prereq tables also disagree (this one: superweapon [factory]; BD-17: superweapon [radar_uplink]); BD-17's is the one the Game Designer signs.

**Two findings carried into BD-17 rather than lost:** its warning that `return` rather than `break` in the BuildStructure case silently skips the shared entity writeback at World.cs:716, and its observation that golden-neutrality is not automatic and each gated scenario must be read first to see whether it builds through BuildStructure or spawns directly.

> Spec (clauses 1-3, superseded): TOUCHES SIM, Architect sign-off; constructed to be golden-neutral. THE FINDING: there is NO structure tech gating anywhere. BuildStructure (World.cs:657-667) checks cost, checks BuildTicks > 0, and contains exactly one hardcoded faction test - `if (c.AuxId == 7 && _playerFaction[c.PlayerId] != FactionSodality) break;` for the veil. A player with 4000 credits and a bare Construction Yard can build a superweapon at tick 1. Meanwhile the unit side already models prerequisites properly: data/schema.unit.json and DataLoader.UnitData carry a `Prerequisites` list (DataLoader.cs:18, 123) and every unit YAML declares one. The structure side has neither the field nor the check, and data/buildings/ is EMPTY. 1) Add to World.cs beside GetStructureType: `/// <summary>Structure kinds that must already stand, alive and owned, before a type may be queued (GDD s5 tech progression). Empty means no gate.</summary> public static EntityKind[] PrereqOf(int structType) => structType switch { 6 => new[] { EntityKind.Factory }, 7 => new[] { EntityKind.Factory }, 8 => new[] { EntityKind.Factory }, 11 => new[] { EntityKind.Factory, EntityKind.ServiceDepot }, _ => Array.Empty<EntityKind>() };` - superweapon, veil, depot and the tier-2 turret all now require a factory, and the tier-2 turret additionally requires a service depot, which finally gives the depot (1200 credits, currently a pure field-repair convenience) a reason to exist in a build order. 2) In BuildStructure, after the existing veil faction check, add: `foreach (var need in PrereqOf(c.AuxId)) { bool have = false; for (int i = 0; i < _entities.Count; i++) { var o = _entities[i]; if (o.Alive && o.PlayerId == c.PlayerId && o.Kind == need) { have = true; break; } } if (!have) return; }` - note `return`, not `break`, is wrong here: the case ends with the shared `_entities[c.EntityId] = e;` epilogue at World.cs:716, so use `break` out of the switch case exactly as every other rejection in that method does, and wrap the prereq loop in a local function returning bool to keep the break semantics clean. Getting this wrong silently skips the entity writeback; read World.cs:555-717 before typing. 3) GOLDEN-NEUTRALITY IS NOT AUTOMATIC HERE and this is the ticket's real risk: ScenarioSuperweapon (Program.cs:809-865) and ScenarioAiSuper (Program.cs:905-948) and ScenarioVeil (950) and ScenarioDepot (1209) may build gated structures without a factory standing. Read each one first. Where a scenario spawns its superweapon or veil DIRECTLY via SpawnSuperweapon/SpawnVeilProjector rather than through BuildStructure, the gate never runs and the golden is safe. Where it goes through BuildStructure, either add a factory to the scenario (which changes that golden) or accept the regeneration - decide per scenario, diff, and explain. Also check SkirmishAI.cs:100 (`!hasSuper && w.Credits(_player) >= 4500 ? 6`): the AI's ladder reaches superweapons only after `factory < 0 ? 2` has been satisfied at line 96, so the AI already respects this gate by construction - verify by reading, then say so in the commit.
>
> Clause 4 (SURVIVES as DEF-13b, rebased onto BD-17's data path): TIER-2 TURRET. Combat.cs: add `public static readonly WeaponDef BastionGun = new(Fix64.FromInt(7), 60, Warhead.AntiArmour, 15);` and `9 => BastionGun,` to Weapons.Get - range 7 (outranges every unit except the howitzer's 9), 60 x 100% versus Heavy x 1/s = 60 dps. World.cs: append `Bastion = 13` to EntityKind, add `11 => new StructureTypeDef(1200, EntityKind.Bastion, 250),` to GetStructureType, add SpawnBastion on the SpawnTurret pattern with Hp 700, Armour Structure, WeaponId 9, Sight 8, PowerDraw 50; add it to IsStructure, to the PlaceStructure spawn switch, to MapLoader's switch, and to DEF-10's 75% power guard. The upgrade path is the classic one and needs no new command verb: sell the turret for 300 and build a bastion for 1200, gated behind factory plus depot. The howitzer (range 9) still outranges it, so GDD s6 line 53 survives - assert that in DEF-17.
>
> Clause 5 (SURVIVES in DEF-13b): Sidebar.cs: add `new("BASTION", 11, 1200, "dir_bastion")` and hide unbuildable items rather than greying them, matching the existing tech-gating comment at Sidebar.cs:80-82 ("disallowed items are absent, not greyed - progression should read as the tree growing") - this is the first time that stated principle gets real prerequisites to express. Requires a client read of PrereqOf against live entities in Refresh. Art for dir_bastion follows DEF-07/DEF-12's builder.py conventions.
>
> Acceptance (retained for DEF-13b's portion): `git diff sim/golden-hashes.txt` is either EMPTY or changes only scenarios proven by reading to build a gated structure through BuildStructure without its prerequisite, each named and explained in the commit message - a blanket regeneration is a rejection. Runner assertions: a BuildStructure command for type 6 (superweapon) with no factory standing is refused and the queue stays empty; the same command with a factory alive is accepted; killing the factory mid-build does NOT cancel an already-queued item (the gate is on queueing, state this explicitly as the chosen semantics in ADR-005 or a docs/questions entry rather than leaving it emergent); a bastion requires both a factory and a service depot; a bastion at range 6 damages a cannon tank that cannot reach it (tank range 4); a howitzer at range 8 kills a bastion without being hit. Sidebar assertion: the BASTION button is invisible until both prerequisites stand. DEF-17's static-defence gate gains a bastion row proving the howitzer still breaches. Balance + Game Designer co-sign per CLAUDE.md A11. Architect sign-off.

---

### A-10. TICKET-P5-DEF-11 (SUPERSEDED by BD-06)

> **Sim: extend StructureTypeDef so new defensive types stop costing a bespoke Spawn method**
> Impact: LOW | Effort: M | cheapModelSafe: true | touchesSim: TRUE
> Files: sim/Ferrostorm.Sim/World.cs

**Why superseded:** BD-06 does everything this ticket does and then finishes the job by moving the values into /data/buildings/*.yaml with a schema, which CLAUDE.md requires and which this ticket explicitly defers. This ticket's own closing note asks for exactly that follow-up.

**Two things carried into BD-06:** its `Footprint = 2` and `WeaponId` fields, which BD-06's record omits and which DEF-03's FootprintOf and DEF-01's WeaponOfStruct both need in order to source from the def rather than a second switch; and its clause-4 trap warning, which is the single most useful paragraph in it and is reproduced in BD-06's integration note.

> Spec: OPTIONAL AND DELIBERATELY RANKED LAST - it is a refactor that buys future cheapness, not player-facing value, and it carries real regression risk for zero visible gain. Ship it only if DEF-12, DEF-13 and DEF-09 are all going ahead; if the defence roster stops at walls plus one turret, close this unshipped. Do NOT batch it with DEF-04. THE PROBLEM IT SOLVES: `public readonly record struct StructureTypeDef(int Cost, EntityKind Kind, int BuildTicks)` (World.cs:341) carries three fields, so every other property of a structure is hardcoded inside a bespoke Spawn method - eight near-identical methods spanning World.cs:250-473, each repeating BlockFootprint, FootprintCentre, and an Entity initialiser. Walls make nine, the emplacement ten, the bastion eleven, a gate twelve. 1) Extend the record: `public readonly record struct StructureTypeDef(int Cost, EntityKind Kind, int BuildTicks, int Hp = 0, int PowerSupply = 0, int PowerDraw = 0, int SightCells = 0, int WeaponId = 0, int Footprint = 2);`. GetStructureType is `public static` and is NOT hashed (it is static config, exactly as the unit catalogue comment at World.cs:276 establishes), so extending it is golden-neutral by construction. 2) Populate from the values I read out of the shipped Spawn methods - these are exact, do not re-derive them: type 1 PowerPlant Cost 300 BuildTicks 100 Hp 150 Supply 100 Draw 0 Sight 4; type 2 Factory 2000/300 Hp 1500 Draw 40 Sight 5; type 3 Refinery 2000/300 Hp 2000 Draw 0 Sight 6; type 4 ConstructionYard 3000/0 Hp 3000 Draw 0 Sight 6; type 5 Turret 600/150 Hp 400 Draw 20 Sight 6 Weapon 4; type 6 Superweapon 4000/600 Hp 1200 Draw 100 Sight 4; type 7 VeilProjector 1500/250 Hp 900 Draw 60 Sight 6; type 8 ServiceDepot 1200/200 Hp 1000 Draw 30 Sight 4; type 9 Wall 100/0 Hp 500 Draw 0 Sight 0 Footprint 1. 3) Add ONE generic `private int SpawnStructure(int player, int structType, int ax, int ay, int hpOverride = -1, int supplyOverride = -1)` that reads the def, calls BlockFootprint and FootprintCentre with `def.Footprint`, and builds the Entity. 4) THE TRAP, and the reason this ticket is risky: the existing public Spawn* signatures carry optional parameters that live callers actually pass. `SpawnPowerPlant(int player, int ax, int ay, int supply = 100, int hp = 150)` is called as `SpawnPowerPlant(0, 22, 20, hp: 1500)` at Program.cs:586 and `SpawnPowerPlant(1, 40, 30, hp: 150)` at Program.cs:696; `SpawnSuperweapon(..., int chargeTicks = 1500)` is called as `chargeTicks: 90` at Program.cs:818; `SpawnFactory(..., int draw = 40)` and `SpawnVeilProjector(..., int hp = 900)` likewise carry overridable params. KEEP EVERY PUBLIC SIGNATURE AND EVERY DEFAULT VALUE EXACTLY AS IT IS - each becomes a thin wrapper forwarding to SpawnStructure with its override. Changing a default, or dropping a parameter, silently moves a golden hash and the diff will not tell you which line did it. 5) Do NOT source Hp from the def inside the wrappers in a way that changes any spawned value: the acceptance criterion is bit-identical hashes, so every field of every spawned Entity must be byte-for-byte what it is today, including the ones the def now duplicates. The def is the single source of truth for NEW types only until a separate, ticketed migration says otherwise. 6) Leave the ChargeTicks/StrikeTicks superweapon fields and the Refinery's FieldId/RefineryId = -1 initialisers exactly as they are - they are per-kind and do not belong in the def. 7) This unblocks the natural follow-on that data/buildings/ (currently EMPTY, while CLAUDE.md says "All gameplay numbers live in /data as YAML" and "Hand-editing stats in code is forbidden") finally gets a schema and a loader mirroring DataLoader.ParseUnit and UnitCatalogue.ToTypeDef. Do not attempt that here; file it as a separate ticket with its own schema.building.json.
>
> Acceptance: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` exits 0. THE ONLY CRITERION THAT MATTERS: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- golden 2026 > got.txt; grep -v '^#' sim/golden-hashes.txt > want.txt; diff got.txt want.txt` produces NO output and `git diff --exit-code sim/golden-hashes.txt` returns 0. A single moved hash means a default or a field drifted and the refactor is rejected outright - there is no player-facing benefit here to trade against a replay break. Full gate exits 0 on ubuntu-latest and windows-latest; `saveload` and `campaignsave` exit 0. Purity grep clean. Every public Spawn* signature is unchanged: `git diff sim/Ferrostorm.Sim/World.cs | grep '^-.*public int Spawn'` returns nothing. Program.cs and game/scripts/ are UNMODIFIED (`git diff --stat sim/Ferrostorm.Sim.Runner/ game/` empty) - if any caller needed a change, the signatures were not preserved. Architect sign-off.

---

### A-11. C-12 and TICKET-P5-DEF-18 (both SUPERSEDED by section 5 of this document)

**C-12: "Amend docs/design/16-visual-style.md: cinder as a family, the two-place team-colour law, the reconciled hex table, the emissive rule"** (Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false | Files: docs/design/16-visual-style.md)

**TICKET-P5-DEF-18: "Docs: amend doc 16's team-colour law for chained barriers, write doc 22's defence section, record the AA deferral"** (Impact: MEDIUM | Effort: S | cheapModelSafe: false | touchesSim: false | Files: docs/design/16-visual-style.md; docs/design/22-*.md (new); docs/questions/)

**Why both are superseded:** they propose overlapping and directly conflicting rewrites of the same law in the same file. C-12 replaces the whole bible and makes the law two-place (silhouette plus ground plate). DEF-18 amends the law for chained barriers (mark only where the neighbour count is not 2). Implemented independently, whichever landed second would silently delete the other, because both are whole-clause replacements rather than additions. Section 5 of this document is the merged text and is the only version to implement.

**What section 5 takes from each:** C-12's structure, its ground-as-family clause, its two-place law and reasoning, its saturation floor, its emissive rule, its palette-of-record clause, and its reconciled hex table (which depends on C-05). DEF-18's barrier sub-clause, its neighbour-mask mechanical rule, its "the silhouette is the RUN" framing, and its dotted-perimeter second-order note.

**What section 5 resolves that neither did:** C-12 makes the ground plate mandatory for all structures; DEF-18 requires barriers to carry marks only at run terminations. A wall is a structure, so under C-12 alone every wall segment gets a team strip and DEF-18's whole argument is defeated by the secondary mark rather than the primary one. Section 5 states the barrier exception against both marks: no strip, and no band on straight segments.

**DEF-18's other two deliverables are already discharged.** Its part 2 asked for doc 22's defence section: that is this document, and specifically section 1's fourth answer plus Wave B. Its part 3 asked for the AA deferral to be recorded with a trigger condition rather than faked as a ticket: that is section 5.4, and the docs/questions/ entry it requires is still owed.

**Both tickets' legal and typographic acceptance criteria carry forward into section 5.3** and are not weakened: no em or en dashes, no Command and Conquer / Red Alert / Tiberium / GDI / Nod / Westwood / EA reference, British spelling, and every factual claim citing a file and line a reader can open.

---

## Appendix B: what was measured, and where

Every number in section 1 came from a bench or a probe rather than from reading. The scratch locations are ephemeral and are recorded only so the next reviewer knows what was actually run rather than reasoned.

- Map scale benches: `mapscale-bench/` and `fogbench/` under the session scratchpad. Sim Step() timings at 96x64 / 192x128 / 256x192 / 512x512; a real 5000-tick AI-vs-AI match on a generated 192x128 map; in-engine FogOfWar.UpdateFrom timings; BuildTerrain node counts and wall times per map; FlowField.Build timings and the rebuild-storm cliff; a rejected pooled-buffer FlowField rewrite (measured 0.72x / 0.70x / 1.28x, i.e. mostly slower, reproducing shipped routes bit-identically across 284,628 checked cells - **do not write that ticket**). The repo tree was left clean; probe files added to game/scripts and game/scenes were removed.
- Economy probe: `econprobe/Program.cs` under the session scratchpad, referencing sim/Ferrostorm.Sim.csproj and stepping real Worlds. Field lifetimes at 1/3/5 fields against 1/3 harvesters; income per harvester at 15-cell and 39-cell hauls; the harvester double-move ratio (exactly 2.00x); credit conservation; the Hp=1/1 field-drain probe at t600/1200/1800/2400/3000; the no-refinery Harvest no-op state dump.
- Grid censuses and symmetry tests: run directly over data/maps/*.fmap and data/missions/*.fmap. Confirmed at time of writing by re-running against HEAD: skirmish-01/02/03 are `size 96 64` with F counts 3/20/20; mission-01 is 64x48 with 2, mission-02 is 56x40 with **0**, mission-03 is 64x48 with 1. The brief's "all maps are 96x64" holds for skirmish only.
- Palette and bake arithmetic: computed from art/3d/builder.py PAL and art/3d/bake.py by hand, then cross-checked against the shipped shader uniforms in game/shaders/ground_splat.gdshader. The 8-bit bake clamp at bake.py:36 (`bpy.data.images.new(img_name, size, size, alpha=False)`, no float_buffer) and the six near-neutral ground uniforms were both re-verified against HEAD.
- Structure catalogue, turret stats, and the absence of any wall/barrier/gate entity: re-verified against HEAD by grep over sim/Ferrostorm.Sim, game/scripts and data. The catalogue is eight types in a hard-coded switch at World.cs:342-353; the grep for wall/barrier/sandbag returns only comments and briefing prose.
- ADR-005 is confirmed still open in docs/adr/ADR-open-queue.md, reserved for "Tile size, grid resolution, footprint rules (decide before first map)". sim/golden-hashes.txt currently holds 23 rows.

---

## Changed / Assumed / Needed next

**Changed.** Nothing in the codebase. This document is a plan, not an implementation.

**Assumed.** That the five dimension analyses' measurements hold at HEAD. The load-bearing ones were re-verified while writing this document and are listed in Appendix B; the rest (in-engine timings, probe state dumps) are trusted as reported and are cheap to re-run if a number looks wrong. That the eighty-five tickets' file-and-line references are accurate to the commit the analyses ran against; a line number that has drifted is a nuisance rather than a defect, and every ticket names the symbol as well as the line.

**Needed next, and from whom.**
1. **Luke: the docs/design/16-visual-style.md amendment in section 5.** It is the only item in this roadmap that cannot be delegated, and it blocks C-07 and DEF-07. If the answer is no, say so plainly and the honest consequence gets recorded rather than worked around.
2. **Architect: ADR-005** (DEF-02), which gates the entire wall system and which also has to settle the EntityKind and struct-type allocation in section 4.4 before four tickets collide on the same numbers.
3. **Architect: the MAP-02 plus C-13 golden regeneration.** One ADR, one row, one Balance re-run, and it fixes the default map's ferrite starvation and its asymmetry together. If the answer is no, P5-ECON-02 in Appendix A is the fallback and it ships today.
4. **Game Designer: three questions that must be filed in docs/questions/ before their tickets can start** - the Depot/Outpost naming collision (P5-ECON-14), the per-type pools versus per-type yield decision (P5-ECON-12), and the Tech Centre that GDD s5 names and that does not exist (BD-17). Plus the AA deferral entry from section 5.4, which needs an owner and a decide-by date rather than a resolution.
5. **Balance plus Game Designer: the A11 co-signs**, which are concentrated in five tickets and nowhere else: BD-07 (power draw), P5-ECON-04 (a measured ~20 percent economy nerf), P5-ECON-11 (regrowth rates), P5-ECON-14 (an income rate the GDD states ambiguously by a factor of fifteen), DEF-04 (wall cost, hit points and cap).
6. **Nobody, yet: MAP-10.** It is recorded so the cliff is known. It must not be built until MAP-06's `bigmap` gate actually fails with the overage attributable to FlowField.Build by profile rather than by assumption.
