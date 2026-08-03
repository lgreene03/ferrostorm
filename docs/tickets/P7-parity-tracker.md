# P7 parity tracker: close the gap to the benchmark games

Authority: Luke, 2026-07-30, "update that documentation and set that as a goal",
against the rewritten parity analysis in docs/design/24-classic-parity-roadmap.md.
That document is the ANALYSIS; this file is the PLAN, and it is the resume point
if a session dies.

**This phase is not like P6.** Every P6 wave could be made hash-neutral or
carried a single sanctioned regeneration, so waves could be picked up in any
order. Nothing in P7 is hash-neutral: every row below is a sim or catalogue
change that moves goldens. That is the argument for sequencing rather than
picking items off, and it is why the ordering column exists.

Standing law per wave, unchanged from P6: full battery exit 0, goldens measured
and not assumed, an ADR where a hash moves, a gate that proves the behaviour,
tracker updated, PR with green CI on both platforms.

## Ordering principle

Rows are ordered by PLAY IMPACT per unit of work, not by list position in doc 24.
A defect that makes one faction unable to defend outranks a feature nobody has
asked for. Whole missing systems outrank roster breadth, because a missing
system removes a category of decision while a missing unit removes an option.

| # | Row | Doc 24 | Blocked on | Hash | Status |
|---|-----|--------|-----------|------|--------|
| P7-1 | A building's faction comes from /data instead of a hardcoded name (the reported "Sodality cannot defend" was a WRONG premise: nothing enforced the field) | B1 | - | **NEUTRAL, measured** | **DONE** - factiongate; checksum and 24 goldens unmoved |
| P7-2 | Defensive variety: the Emplacement, the anti-infantry leg, so defence is a CHOICE rather than a ladder | B1 | - | goldens NEUTRAL; catalogue checksum MOVES (a new building changes it by construction) | **DONE** - emplacementgate; 32 ticks vs infantry against the turret's 91, 632 vs armour against its 143 |
| P7-2b | A distinctive defence per SIDE: the Directorate's Bastion and the Sodality's Shroud Nest, both from WRITTEN GDD s3 doctrine | B1/C | - (no new doctrine invented, so Q017 is untouched) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - factiondefencegate (4 stages incl. the cloak and its decloak-on-firing) |
| P7-3 | Transports: the Carrier, the first unit that exists to move OTHER units | A2 | - | goldens NEUTRAL; catalogue checksum MOVES; save format v9 | **DONE** - transportgate (6 stages incl. save round-trip and cargo dying with its carrier) |
| P7-4 | The air layer: Airfield, Strike Flyer, Flak Track | A1 | ADR-028 (ratified under the standing directive) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - airgate (5 stages, both halves of clause 4); no reload cycle and the AI does not fly, both stated in the ADR |
| P7-5a | Faction identity, DR-02: power economics, the Directorate centralised and the Sodality decentralised | C | - (authorised 2026-08-01; GDD s3 writes BOTH halves, so only the numbers are invented, and they are recorded reversibly in ADR-042) | goldens NEUTRAL and **measured, against a tracker prediction that they would MOVE**; catalogue checksum MOVES (0x2495D0E393438B38 to 0x64768008B78985FB) | **DONE** - ADR-042; factionpowergate (5 stages) and ferritefieldgate (3 stages), 4 of them proved to bite. Carried the enabling refactor (a prerequisite is a CAPABILITY, not a named building) and three defects: the placement switch handed a Sodality player a Directorate plant, `StructureTypeDef.Faction` was missing from the catalogue checksum, and ONE RIFLE SHOT deleted a whole ferrite field |
| P7-5b | Faction identity, DR-03: a Sodality stealth-detection answer | C | - (GDD line 56 requires it: "every stealth tool has a public counter"; WHICH thing carries it is invention, recorded reversibly in ADR-043 with both rejected alternatives) | goldens NEUTRAL, measured; catalogue checksum MOVES (0x64768008B78985FB to 0x2CADF63D66912E62) | **DONE** - ADR-043; sodalitydetectorgate (5 stages, 3 proved to bite). A STRUCTURE, not a unit, because every Sodality unit is itself cloaked and a cloaked detector contradicts the same GDD line that requires one. The justification Q017 missed is stronger than the mirror match it named: `com_mine` is faction COMMON and its own /data notes name a Directorate-only unit as its counter. Also fixed: `StructureTypeDef.Equals` omitted `Faction`, so the /data round-trip was blind to a side drifting between a yaml and its compiled reference |
| P7-5c | Faction identity, DR-04: faction superweapons | C | - (GDD s8 writes both precisely; only three numbers invented, recorded with their alternatives in ADR-044) | goldens NEUTRAL, measured; catalogue checksum MOVES (0x2CADF63D66912E62 to 0xD2B80B9B8E87A2CA) | **DONE** - ADR-044; factionsuperweapongate (6 stages, 2 proved to bite). The pair is identical in cost, build time, draw and charge and differs ONLY in effect, which is what GDD s8's "one superweapon per faction" asks for. **P7-5a's placement-switch defect arrived a second time** and was pre-empted rather than found. Two things FILED not fixed: the sim charges 3.6x faster than GDD s8's "~6 minute" (refused under charter A11, reversal conditions in the ADR), and a Sodality AI can no longer queue a superweapon because the ladder names type 6 by number |
| **Q017 COMPLETE** | All three of DR-02/03/04 shipped 2026-08-01 | C | - | 3 rows, goldens NEUTRAL on all three | The asymmetry pillar, which doc 27 called the least delivered of the GDD's five, now has the two sides differing in their power grid, their answer to cloak and their superweapon |
| P7-5d | The commander builds its own SIDE's hardware, not the type ids the ladder named as literals | C | - | goldens NEUTRAL and measured; catalogue checksum UNMOVED (no def changed, this is AI behaviour only) | **DONE** - ADR-045; aifactiongate (3 stages, both new rungs proved to bite). Fixes a defect P7-5c CREATED: a Sodality commander could not queue a superweapon AT ALL, because the rung named struct type 6 and that had just become Directorate-only. Also makes the Watch Post reachable, so the counter ADR-043 shipped is no longer one no AI can use. **Still not built by any commander: the faction DEFENCES (Bastion, Shroud Nest) and the Veil Projector** |
| P7-7a | The commander runs GDD s4's economy: TWO refineries per base, not one | B2 | - (GDD s4 states the equilibrium outright; A11 checked and does not bind, this is AI behaviour and no stat moved) | **FOUR goldens REGENERATED, measured** (`skirmish`, `expansion`, `aisuper`, `mission` - the four that run a commander building an economy); the other 20 byte-identical; catalogue checksum UNMOVED | **DONE** - ADR-047; economyfloatgate, proved to bite. The row ADR-041's refusal pointed at: that ADR refused a credit ceiling and measured the opposite problem in the same breath. Before/after tables in the ADR - the treasury went from touching **0, 2, 19, 1** to floating between **1300 and 4000**. **THIRD HARVESTER NOT DONE and the cause is identified: GDD s4 also says "Refinery: 2,000 credits, includes one free harvester" and the sim has NEVER implemented it**, so the designed 3 is two free plus one bought. Own row, far wider blast radius (affects players as much as the AI) |
| P7-7b | GDD s4's "Refinery: 2,000 credits, INCLUDES ONE FREE HARVESTER", never implemented | B2 | - | no behaviour change; goldens byte-identical; checksum unmoved | **REFUSED FOR NOW, and MEASURED rather than argued** - ADR-048. It was BUILT (purchase path, not SpawnRefinery, so map-placed refineries are untouched) and the commander did reach GDD s4's 2/3 float. Then: seat 1 banked **38,823 credits** with the match still RUNNING, seat 0 was left at 0/0/0 with no victory declared, and `mission` **never cleared its camp in 9000 ticks** where ADR-047 left it winning at 4946. Root cause is NOT the free harvester: "infrastructure before army, always" has no termination condition, so a longer ladder means a commander that fights less. Three reversal conditions recorded |
| ~~P7-7c~~ | ~~The commander does not convert income into army~~ | B2 | - | - | **WITHDRAWN: the premise was FALSE and never measured.** ADR-048 read "infrastructure before army, always" in the code and diagnosed a missing termination condition without checking. Army columns added to `economyprobe` disproved it in one run: on main the commander goes 3 -> 6 -> 9 -> 12 units while credits oscillate 1292-4018, and the seat that banked 38,823 had the BIGGEST army on the board (22). The stockpile is a production-throughput ceiling (one factory, one barracks, queue depth 2), not a build-order stall. See ADR-049 |
| P7-15 | GDD s6's damage matrix was the LAST gameplay number outside /data | ADR-006 / CLAUDE.md data conventions | - (no design call: CLAUDE.md says every gameplay number lives in /data and "hand-editing stats in code is forbidden"; `Combat.cs` had promised the wiring since Phase 1) | goldens byte-identical, measured; **catalogue checksum MOVES to 0x48C6C9C2604BD3DE**, deliberately | **DONE** - ADR-057; `damagedatagate` (5 stages), proved to bite FOR THE RIGHT REASON. Rows are NAMED not positional so a 4x4 cannot transpose silently. One percentage point moves the checksum, and the case is the strongest yet: every other section decides what a player may BUILD, these sixteen decide what every shot DOES. **Defect found on the way: `schemagate`'s directory list had already fallen behind the loader** - `data/combat` was registered, loaded and played while validated against no schema at all. Now `CatalogueFiles.RegisteredKinds()` is asserted to have a schema row, so the class is closed rather than the instance |
| P7-16 | GDD s5 line 47 gates replacement MCVs behind a "Tech Centre" that does not exist, and `com_mcv`'s prerequisite named the FACTORY IT IS PRODUCED AT - a tautology that cannot refuse anything | GDD s5 line 47; Q006 (open since 2026-07-17); ADR-009 clauses 2 and 7 | **Q006 answered, option 1**: the Radar Uplink absorbs the Tech Centre role. Line 47's intent is that MCV replacement is TIER-GATED, not that a building of that NAME must exist, and the radar is the tier gate the tree already has (superweapon, airfield and five units wait behind it). Rejected: building a real Tech Centre - it satisfies a name rather than a need, and a roster addition is C9/Luke's | **`expansion 2026` MOVES, measured: 0x7A6AA4D3238DF294 -> 0xCCF833DEB3E10B68**; other 23 byte-identical. Q006 predicted exactly this. Cause measured: tech gate at tick 1860, MCV at 2379, a 34s gap where it used to buy on affordability. Regenerated under the standing authorisation | **DONE** - ADR-058; `mcvtechgate` (4 stages), proved to bite for the right reason (reverting the prerequisite fails stage 1 naming the tautology). Stage 1 asserts the PROPERTY (a factory alone refuses an MCV) so it would pass unchanged had Q006 resolved the other way. The AI's expansion gate now ASKS THE CATALOGUE rather than keeping a hand-kept copy of the prerequisite, so the two cannot drift again. **TWO findings left behind, both filed:** stage 3 derives the produced-at tautology list and found World.cs's hand-written comment had been wrong since ADR-028 added the strike flyer, and mis-attributed the rest to Q007 (which is about the ENGINEER) - now **Q020**. And the AI half of this row is **NOT PROVED**: four fixtures failed to make `canBuyMcv` bite, because a commander that loses its Construction Yard freezes completely on `wanted == 0` first. Measured with a control (yard present: 9000 credits spent; yard absent: ZERO, forever) and filed as `DEFECT-AI-yard-loss-freeze.md`, severity high |
| P7-17 | DR-10's comeback rule keyed on OWNING an MCV and never asked whether one could be BOUGHT, so a commander holding a Factory, the tier gate the MCV waits behind and 20000 credits SOLD ITS WHOLE BASE - that tier gate included - for a 2850 credit consolation | GDD s7; DR-10's last stand; ADR-058 | Beaten means CANNOT REBUILD, not HAS NOT YET BUILT. Rejected: selling all but the rebuild prerequisites (fiddly, and credits are not the constraint at 20000 vs 3000); rejected outright: removing the Fire Sale, which is a good ending DR-10 added on purpose - gate stage 2 exists to stop this row quietly becoming that change | **All 24 byte-identical, measured.** DR-10's own neutrality argument holds: the block lives inside `cy < 0` and no golden loses a yard | **DONE** - ADR-059; `comebackgate` (2 stages), proved to bite for the right reason. **The fix failed its own first measurement**: the order went out, and on the next beat the producer's queue was non-empty so the search skipped it and fell through to the sale - selling the very factory building the comeback. An MCV already on order now counts. **TWO CORRECTIONS OWED AND PAID:** P7-16 filed this defect with a real measurement and a WRONG diagnosis (`Act` returns at `cy < 0` a hundred lines before the army block; the observed silence was a fixture with no enemy structures), and its recommended fix would have changed nothing while appearing to work - ticket rewritten in place with its own error first. And ADR-058 overstated the canBuyMcv debt: a fifth state (tier gate destroyed, yard alive) DOES discriminate, so the guard is reachable, though still not carrying much |
| P7-18 | Both commanders built one COMMON turret and stopped, so a Sodality base and a Directorate base were defensively IDENTICAL while the Bastion and the Shroud Nest sat in the catalogue as dead hardware. `!hasTurret ? 5` was the LAST hardcoded type id in the ladder, left behind when P7-5d converted the plant, detector and superweapon rungs | GDD s5 faction identity; GDD s3 doctrine; ADR-045 | ADD a rung rather than SWAP the turret (it is anti-armour and cheap, neither faction defence is either); choose it by CATALOGUE QUERY (Defence tab + armed + faction-exclusive) not by naming type ids; EXCLUDE the Veil Projector by having no weapon - a defence rung must not answer an attack with a cloak field. Rejected: replacing the turret (a balance change to the opening dressed as a fix); rejected: building several (density is balance, this row is identity) | **FOUR move, measured** - every AI-driven scenario and no others: skirmish 0x2DC6B7CC141FC20A, expansion 0xEECA2D1C61A23359, aisuper 0x6A39F0D6EFA0B8BC, mission 0x6D491D77B5C4FD6D. Expected, not discovered: the commander buys one more building | **DONE** - ADR-060; `aidefenceladdergate` (3 stages), both the rung AND the control proved to bite. Not a duplicate of `FactionDefenceGate` (P7-2b): that asserts the CATALOGUE and always passed because it was right; this asserts the COMMANDER, which nothing checked and which was false for both sides. **BOUGHT FROM SURPLUS, and the measurement is why:** ungated, the rung FLIPPED THE FACTION WAR from Directorate 6-0 to 0-6, because the Bastion costs 1400 behind a radar and the Shroud Nest 400 behind a plant. Gated on cost+1500 it returns to 6-0 and still binds (the `mission` golden differs between gated and ungated). **FILED not fixed:** `BALANCE-bastion-value-vs-shroud-nest.md` (A11, needs co-sign or a playtest), and the Veil Projector, which is support machinery and belongs with GDD s8's support powers |
| P7-19 | Wall TIERS (doc 24 B7: "one wall type at a flat 100 credits, where the benchmarks tiered barriers by cost and durability") | doc 24 B7; ADR-005; doc 29 s3.1 | **REFUSED.** Re-derived rather than deferring to doc 29, because doc 29 filed the Tech Centre as missing when Q006 had already answered it and ADR-048's refusal was itself wrong. **The GDD says NOTHING about walls - zero occurrences, searched in full** - so every property of a tier would be invention on top of a feature that already works. Rejected: tiering by DURABILITY (the measurement says durability is the variable that matters LEAST). Recorded but NOT taken: tiering by RULE (a cheap fast low-HP barrier blocking infantry not vehicles), because that is still invention with no GDD sentence | **All 24 byte-identical, measured.** Only a derived column in a reporting tool changed; nothing in /sim moved | **DONE (refusal)** - ADR-061. Overturning conditions recorded: a GDD sentence about walls, the measurement changing, or a playtest saying bases feel unfortifiable. **NO GATE, deliberately** - a wall's worth in ticks is BALANCE, so it is reported not asserted, per the method's own gate-vs-probe rule; the correctness half (artillery must raze, non-pyrrhically, breaching a segment) was already asserted and stays. **THE ROW'S REAL FIND:** the siege table had been printing, for many waves, that against rifle squads a GAPPED wall makes the yard fall SOONER than no wall (1722 vs 2046, **-324 ticks**) - invisible because only the howitzer had a derived figure and seeing it meant subtracting two non-adjacent rows. A signed `Ticks bought` column now shows it for every besieger. Cause HYPOTHESISED AND EXPLICITLY UNVERIFIED (army retained rises 73%->86%, so the doorway looks safer than the open field), not filed as a defect. Also corrected doc 29's quoted 235 ticks to the measured 229, and deleted four unused constants P7-16 left under a comment claiming one was "the one place a reader must look" |
| P7-21 | GDD s8's SUPPORT POWERS had no machinery in /sim at all - "3-4 minor support powers per faction on shorter timers, unlocked by structures", plus "every power has counterplay (spread out, scout the structure, kill it)" | GDD s8 lines 71-72; GDD s3 lines 25 and 30; TDD line 11; ADR-044 | **The power lives on the STRUCTURE DEF**, so s8's counterplay FALLS OUT of the data model rather than being arranged - the permission IS the building. Charge **DERIVED as a third of the superweapon's**, not invented, so "shorter" is true by construction and survives ADR-044's refusal ever being overturned. A SEPARATE command, not a widened LaunchSuper (ADR-044 clause 4 applied twice). No warning and no strike delay - s8 gives those to the superweapon specifically, and a dirty trick the victim is warned about has no surprise left | **All 24 byte-identical, measured** (nothing in the catalogue carries a power, so it is inert); save UNCHANGED at v12 (charge reuses the already-serialised ChargeTicks); **catalogue checksum MOVES to 0x42CE3A6F39C31A9C**, deliberately | **DONE** - ADR-062; `supportpowergate` (4 stages), and it catches what no existing gate can: aisupergate and both superweapon scenarios assert the SUPERWEAPON path and cannot see a minor power. **NO POWER HAS AN EFFECT YET, deliberately** - GDD s3 NAMES FIVE (orbital scan, precision strike, radar jamming, decoy army, tunnel deployment), which doc 29 missed when it recorded support powers as "nothing at all", but not one has a radius or duration written anywhere. Filed as **Q021** with the three numbers each needs. **My first version failed its own measurement**: a freshly built power building fired on the tick it landed, because ChargeTicks defaults to 0 and 0 means ready; fixed at `Add`, the one funnel every spawn passes. **The counterplay bite test PASSED while broken** - measured cause: `Alive` is checked TWICE and I broke the redundant copy; with both removed it fails correctly, so the counterplay is defence-in-depth (not known before). **Corrects ADR-060**: the Veil Projector is NOT support machinery, it is a working persistent aura |
| P7-22 | The first SUPPORT POWER with an effect: GDD s3 line 25's ORBITAL SCAN (Directorate, doctrine word "surgical"). ADR-062 shipped machinery with no power; Q021 recommended this one because per-player fog already exists | GDD s3 line 25; GDD s8; TDD line 11; ADR-062; Q021 | **All three numbers DERIVED, none invented.** Unlocked by the BASTION - the Directorate's widest-sighted building and a forward hardpoint, so s8's "scout the structure, kill it" is true by construction. Radius = **the building's own SightCells** (the scan shows what its sensors would see, projected); duration = **the superweapon's warning window**, this game's own "long enough to notice and act". Rejected: the Orbital Cannon, **technically impossible** (it already uses ChargeTicks for its own cycle and one entity cannot hold two charges); the power plant (first building raised, so not "unlocked" by anything) | **All 24 byte-identical, measured** - reveals live in a side collection folded only when non-empty. **Save bumps to v13** deliberately (a save resumed with the ground dark is a DIVERGENCE); **catalogue checksum moves to 0x905DDBBD71F7973D** | **DONE** - ADR-063; `orbitalscangate` (6 stages, CONTROL FIRST). Complementarity PROVED by bite test: disable the effect and this gate fails while `supportpowergate` still passes. Stage 6 catches what `saveload` cannot - it never fires a scan, so a v13 block written and never read would pass it silently. **FOUND A DEFECT IN ADR-062, ONE WAVE OLD:** its stage 1 compared two compile-time CONSTANTS, so the compiler folded the branch away and emitted a CS0162 unreachable-code warning nobody read - ADR-047's self-measuring gate, in the gate meant to be careful about it. It now OBSERVES both timers from a running world. **A build warning is a gate telling you something.** Also walked the save-surgery helper to v13, caught in the battery exactly where its own comment predicted, for the fifth time |
| P7-14 | Do units end up permanently STRANDED? Movement gates prove a unit REACHES a point | C9 | - | no behaviour change; goldens byte-identical; checksum unmoved | **DONE, NO DEFECT, and NO GATE ADDED** - ADR-056; `idleprobe`. Three units idle over 3000 ticks and the longest **16,885** - but the column that decided it says **ZERO are far from home**: every one is the garrison, sitting where the design says. **Deliberately no gate**: by ADR-054's own rule it would catch nothing `arrivalgate` does not, and unlike ADR-054 there is no versioned format to future-proof. **This ADR also CALLS THE METHOD'S DIMINISHING RETURNS** - first three outings found four defects, last four found one, and the remaining candidates are each half-covered by an existing gate. Third time a confusing table was settled by adding a COLUMN rather than a theory |
| P7-13 | Does the CLIENT harness check the building faction gate at seat 1? | C7b / C9 | - | no sim behaviour change; goldens byte-identical; checksum unmoved | **DONE, and the largest find since ADR-050** - ADR-055. `Sidebar.StructButtonVisible` has existed since P5 and the harness **had never called it once**, while `UnitButtonVisible`'s own comment cites it as the precedent for the unit check that WAS written - so six faction-locked buildings shipped unchecked. **Worse: every faction check was VACUOUS.** The bite test (seat-0 hardcode) PASSED, because `VerifyRunner` never set the factions and both seats defaulted to 0 - so a rule keyed on the wrong seat read correct, and the pre-existing UNIT check has been vacuous since TICKET-P6-FACTION-01. Fixture now gives the seats different factions, asserted; the same bite test reports **8 buildings disagree**. Harness 194 -> 201 checks. **Rule earned: a bite test that passes when you break the rule is telling you about the FIXTURE, not the rule** |
| P7-12 | Does the save format hold at SCALE? `saveload` tests a small hand-built world | TDD s6 | - | no behaviour change; goldens byte-identical; checksum unmoved | **DONE, NO DEFECT** - ADR-054; `savescalegate`, proved to bite, 4.5s. **226 entities, 81,215 bytes**, loaded hash-exact and resumed bit-for-bit. Honest about its own value: the bite test (dropping harvester `Carry`) is caught by `saveload` TOO, and every narrow write in the format is an enum bounded by its own definition, so **no current defect class needs this gate**. Kept because it establishes a size figure nobody had, guards the next narrow type somebody adds across a format already on v12, and costs 4.5 seconds. Rejected keeping it outside the battery: a gate nobody runs is a gate that rots |
| P7-11 | Does an attack wave ARRIVE? `aitargetgate` only proves where it is AIMED | C9 | - (bound derived from `World.CyBuildRadius`; alternatives in ADR-053) | no behaviour change; goldens byte-identical; checksum unmoved | **DONE, and NO DEFECT FOUND** - ADR-053; `arrivalgate`, proved to bite. Both commanders close skirmish-07's **269-cell** gap to within **4-5 cells**, with units standing inside the enemy base at tick 18000. Shipped as a GATE rather than a probe because arrival is CORRECTNESS, not balance - the three previous hunts produced probes because what they found were balance questions. Bite proof disabled attack waves: closest approach **139 cells**, and the failure message is literally true in that state, since `aitargetgate` still passes. **A hunt that finds nothing has bought a guarantee, provided it leaves an assertion behind** |
| P7-10b | Dead entities accumulate without bound, and the big map does not resolve in GDD pillar 2's window | TDD s6 / GDD pillar 2 | - | no behaviour change; goldens byte-identical; checksum unmoved | **TWO FINDINGS, both REFUSED with conditions** - ADR-052; `churnprobe` (takes a map name). On skirmish-07 over 30 minutes the entity list grows **211 -> 558 while ALIVE stays flat at ~200**, so two thirds of what every system walks is a corpse; and the match is **still RUNNING at 27000 ticks** with both armies healthy. skirmish-01 hides both by ENDING at 13500. The free-list fix is refused because the sim still runs at ~1ms/tick against TDD s6's 8ms budget, and the non-resolving map is refused as a balance question needing the playtest. **`basingate` plays this same map and asks whether it is a STALEMATE, never whether it ENDS** - third wave running that a defect sat beside a gate asking a neighbouring question |
| P7-9 | A refinery has NO throughput limit, so GDD s4's second refinery buys nothing | B2 | - | no behaviour change; goldens byte-identical; checksum unmoved | **REFUSED FOR NOW, and MEASURED** - ADR-051; `dockprobe`. Found by asking what nothing had ever asserted (the ADR-050 method). `UnloadTicks`' own comment says "**refinery** processes a load in 8s" and the code applies it PER HARVESTER with no occupancy check: measured, 6 harvesters unload 5-at-once at one refinery, and **a second refinery earns 0% more at 3 harvesters and 1% at 6**. So GDD s4's "floats at 2 refineries" has no explanation in the sim as built - a refinery is a LICENCE TO OWN MORE HARVESTERS, not a station. **This is also why ADR-047 worked, and not for the reason that ADR gave.** Serialisation refused because three economy rows have landed unplayed and this would be a 5x cut on top; three reversal conditions recorded |
| P7-8 | A commander's base is a CLUSTER around its yard, not a trail walking off the map | C9 | - (no GDD line on base shape; the bound is a design default sourced from `World.CyBuildRadius` and recorded with its rejected alternatives in ADR-050) | **FOUR goldens REGENERATED, measured** (`skirmish`, `expansion`, `aisuper`, `mission`); other 20 byte-identical; checksum unmoved | **DONE** - ADR-050; baseshapegate (both sides, proved to bite at 31 cells against a bound of 14). **The filed premise was WRONG and measuring came first**: generators were not clustered, they were STRUNG IN A CHAIN to the map corner, because `TryFindPlacement` walked entities BACKWARDS and anchored on the newest building. Walking forwards anchors on the yard. Directorate 11 -> 4 cells, Sodality 31 -> 5. DR-02 did not create the drift, it multiplied it 2.5x. **Trade recorded not hidden**: a compact base takes 9 of 12 power buildings in one seismic blast against 5 of 12 before - ADR-042's SINGLE-loss claim is untouched, but deliberate spreading against area weapons is a filed balance question |
| P7-7d | GDD s4's "Refinery: 2,000 credits, INCLUDES ONE FREE HARVESTER" | B2 | - (written; the purchase-path design is recorded with its rejected alternative in ADR-049) | **FOUR goldens REGENERATED, measured** (`skirmish`, `expansion`, `aisuper`, `mission`); other 20 byte-identical; checksum unmoved | **DONE** - ADR-049; freeharvestergate (3 stages, proved to bite). **ADR-048 refused this and blamed the wrong half**: the failures came from a `+1 bought harvester` I derived and shipped beside it, not from the GDD clause. Isolated, the free harvester alone clears mission-01 at tick **3462**, FASTER than the 3688 baseline, where the pair never cleared it in 9000. The `+1` was never needed - the skirmish start already provides one harvester, so **2 refineries + 2 delivered = GDD s4's 3** |
| P7-5e | The seismic charge is AIMED at what it is for: enemy ferrite, not buildings | C | - (GDD s8 writes the weapon's purpose; the three targeting rules are invented and recorded with their alternatives in ADR-046) | goldens NEUTRAL, measured; catalogue checksum MOVES (0xD2B80B9B8E87A2CA to 0xDBC4C027FB1EAB73) | **DONE** - ADR-046; seismicaimgate (5 stages, 2 proved to bite). Carried the fix for a defect P7-5c created: the impact site selected the effect with a TYPE-ID LITERAL, which left the AI no question it could ask. Now an authored `destroys_fields:` key drives BOTH the effect and the aim. The aim scores the CLUSTER (the blast kills every field within 6 cells) and refuses to deny its own ground |
| P7-6 | Storage and a credit ceiling (silo) | B2 | - (authorised 2026-08-01) | no code, no hash | **REFUSED, and MEASURED rather than argued** - ADR-041; `economyprobe` shows the treasury oscillating near zero, so a ceiling would constrain something that never approaches a limit. Reversal conditions recorded |
| P7-7 | Infiltration: the Sodality's Infiltrator, from GDD s7's named roster | B5 | - (the unit is written; only the 20 per cent share is my call) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - infiltratorgate (4 stages incl. conservation and an engineer regression check) |
| P7-8 | ~~More than two player seats~~ - **SPLIT, 2026-08-01.** GDD s9 makes TWO promises of very different sizes and one row cannot hold both | D2 | - | - | **SPLIT**, see "What P7-8 turned out to be" |
| P7-8a | The engine becomes N-player, free-for-all: GDD s9's "skirmish vs AI, 1-7 opponents" | D2 | - (written unhedged as a mode spec, unlike the "(sample)" roster lines - so it is a promise to keep, not a design to invent) | goldens NEUTRAL, measured, and asserted IN the gate rather than left to the golden file | **DONE** - ADR-031; multiseatgate (7 stages); client harness 128 -> 130 checks |
| P7-8b | Maps that can HOST more than two: the mapgen symmetry group, and the first multi-start map | D2 | - | no hash; all 10 committed maps re-generate BYTE-IDENTICAL, which is the inertness proof | **DONE** - `mirror2` orbit group; skirmish-09 "Kilnmoor Quarters" 160x120, four starts, 9.15% density; multiseatgate stage 7 |
| P7-8d | The lobby seats what the map declares: N commanders, one per non-local seat | D2 | - | goldens NEUTRAL, measured; NO sidecar or wire format change, because the seat count is DERIVED from the map rather than stored | **DONE** - `SeatsFor(map)`; client harness 133 -> 138 checks |
| P7-8c | Teams and alliances: GDD s9's "custom lobbies up to 4v4" | D2 | - (authorised 2026-08-01; design recorded reversibly in ADR-038) | goldens NEUTRAL, measured; catalogue checksum UNMOVED; save format v11 | **DONE** - ADR-038 (sim) and ADR-039 (lobby); teamgate 8 stages, client harness 174 -> 189 checks |
| P7-9 | Campaign missions 4 to 6 | D1 | ~~Q012/Q016~~ - both ANSWERED and closed under the standing directive | goldens NEUTRAL and checksum UNMOVED, measured (unusual for P7: `MissionRunner` state has always been outside the world hash) | **DONE** - ADR-029; campaigngate (5 stages) |
| P7-9a | ~~and onto self-declared setup, retiring `switch (setup.MissionIndex)`~~ **DONE** (ADR-034). Still owed: bring missions 01 to 03 under the GENERATOR, which is content rather than a defect | D1 | - | ONE golden regenerated (`mission`), measured; `mission03` was predicted to move and did not, for a reason worth reading | **PART DONE** - campaigngate proves every mission's setup comes from its own file |
| P7-10 | Wall GATES (the "wall tiers" half of this row is untouched and still open) | B7 | ~~C6b~~ - resolved WITHOUT an override: ADR-005 clause 6 stands, and the P7-10 amendment records that its blocker is scoped to SIMULTANEOUS per-player passability, which a single global open state does not need | goldens NEUTRAL, measured (predicted to move, and did not); catalogue checksum MOVES; save format v12 | **PART DONE** - ADR-005 amendment; wallgategate (8 stages incl. the enemy walking through an open gate, asserted as the DESIGN); client harness 194 checks |
| P7-11 | ~~Hero unit, mines, support infantry~~ - **SPLIT, 2026-08-01.** One row bundling one thing that is written, one the project has already ruled is a sample, and one that appears in no design document at all | B3/B4/B6 | - | - | **SPLIT** - see "What P7-11 turned out to be" below |
| P7-11a | The Sodality's Saboteur: temporarily disables a building | B3 | - (GDD s7 names the unit AND its effect; only the duration is my call) | goldens NEUTRAL, measured; catalogue checksum MOVES; save format v10 | **DONE** - ADR-030; saboteurgate (6 stages) |
| P7-7a | **Client defect carried by P7-7:** the Infiltrator's theft raised `GameEventType.Captured`, which the client reads as an ownership CHANGE, so robbing a building told its owner "STRUCTURE LOST TO CAPTURE" about a building they still held, klaxon and all | - | - | goldens NEUTRAL, measured (the event enum is neither hashed nor saved) | **DONE** - `GameEventType.Robbed`; infiltratorgate gained the stage that catches it (proved by restoring the defect and watching it fail); client harness 130 -> 133 checks |
| P7-11b | Hero unit: Commando and Shadow Commando | B4 | - (authorised 2026-08-01; the design is INVENTED and recorded reversibly in ADR-035) | goldens NEUTRAL, measured; catalogue checksum MOVES | **DONE** - ADR-035; herogate (7 stages incl. the no-cap control that protects the goldens) |
| P7-11c | Mines | B6 | - (authorised 2026-08-01; the design is INVENTED and recorded reversibly in ADR-037) | goldens NEUTRAL, measured; catalogue checksum MOVES | **DONE** - ADR-037; minegate (8 stages) |

Out of scope until a GDD amendment with Producer sign-off: naval, FMV briefings,
crates, a map editor. Recorded in doc 24 so the comparison stays honest.

## What P7-1 turned out to be

It was filed as a defect - the Sodality unable to build base defence - and the
premise was wrong. Nothing enforced `faction:` on a building at all: the field
was parsed, validated, and dropped, and the sim hardcoded one expression naming
the Veil. Both sides could always build the turret.

The real defect was better and is now fixed: authored data that did not drive
the runtime, the ADR-006 class. StructureTypeDef carries a Faction, the loader
passes what the file declares, the hardcoded predicate is gone, and the turret
and superweapon declare `common` - preserving what play always did rather than
silently taking a capability away. Neutral: catalogue checksum and all 24
goldens unmoved, because no golden scenario plays a Sodality commander building
a Directorate building.

Recorded because the lesson generalises: **a claim read off a data file is a
claim about the file, not about the game.** The duplicated-rule audit had
already filed the missing Faction column as the permanent fix; checking there
first would have caught the premise before it was written down.

Left undone deliberately: `dir_turret` and `dir_superweapon` keep their ids
although they are now `common`, which contradicts the repo's own prefix
convention. Renaming them cascades into art (art/png/dir_turret.png,
art/sprites/dir_turret.svg and the model library key), so it is a wave of its
own rather than a rider on this one.

## What P7-9 turned out to be

Filed as **data only**. It was not, and the reason generalises.

Three things stood between the ticket and three new files. Two were the open
questions the row already named. The third was in nobody's ticket: missions 01
to 03 are hand-typed 64x48 grids, written before doc 26 existed, at a moment
when the skirmish pool had just been regenerated at 96x64 to 256x192 with a
decorative layer on the finding that the old maps were "not big and detailed
enough". Three more hand-typed missions would have shipped that same complaint
into the campaign, and no row said so.

Writing them properly then surfaced three defects that had been sitting in the
tree, all of the same shape - **a rule keyed on an instance where it should key
on a property**, now the ninth, tenth and eleventh instances this phase:

- `MapLoader`'s structure switch had no arm for Emplacement, Airfield, Bastion
  or Shroud Nest. Those kinds have been spawnable, buildable and GATED for
  weeks; a map simply could not place one. The switch's own comment records
  this happening before (PROD-D7, the service depot).
- `campaign.txt`'s id legend had gone stale by six structure types and six
  units, because nothing read it except a running client's sidebar.
- `SkirmishLive.cs` sets missions up with `switch (setup.MissionIndex)`, so a
  new mission needs a C# edit or it silently has no base.

The first two are fixed and now guarded in CI by campaigngate stage 1, which
loads every mission the manifest names and refuses ids that do not resolve. The
third is P7-9a above, deliberately deferred because fixing it moves goldens.

**The lesson worth keeping:** a row estimated as "data only" was carrying three
code defects, and none of them were found by reading the tickets. They were
found by trying to use the feature end to end for the first time.

## What P7-11 turned out to be

Three rows in a trenchcoat, and only one of them is mine to take. The bar is the
one P7-7 set and P7-6 was refused by: **is the thing written down, with what it
does, or would I be inventing it?**

- **The Saboteur is written**, in the same GDD line and the same form as the
  Infiltrator I shipped in P7-7: `Saboteur (disables buildings)` (GDD s7,
  line 64). Unit named, faction assigned, effect stated. Only the duration is a
  judgement call, exactly as only the Infiltrator's 20 per cent share was. Taken,
  as P7-11a.

- **The hero is named and nothing else.** GDD line 62 gives `Commando (hero, one
  at a time)` and line 64 `Shadow Commando (hero)`. No ability, no stats, no tier,
  and "one at a time" has no machinery in the sim at all - there is no per-unit-
  type build cap anywhere, only ADR-005's `MaxBarriersPerPlayer`. Every
  interesting thing about a hero would be invention. And the project has already
  ruled on this exact line, in doc 23 at 142 and again at 599: the GDD "mandates
  the Repair Vehicle exactly as much as it mandates the Commando, which is to
  say it is a sample, not a system statement." Refusing it here is consistency
  with a decision already taken, not caution.

- **Mines are written nowhere.** Not in the GDD, not in ADR-005, not in any ADR,
  and nothing resembling the mechanic exists in `sim/`: no dormant entity, no
  proximity trigger, no hidden-but-not-stealthed state. The damage half would be
  nearly free (splash exists, `ApplyAreaDamage` exists, the superweapon is a
  countdown precedent) and the TRIGGER is the whole feature. B6 is one line of
  text in doc 24 with no design behind it.

Doc 24's B3 also names a medic, a field mechanic and a scout animal - as
ABSENCES. None appears in the GDD, and B3's own text concedes the repair vehicle
already covers the mechanic's role. A gap analysis noticing something is missing
is not the same as a design document asking for it, and this row is where that
distinction has to be made rather than blurred.

**Also worth recording, because it was found looking for something else:**
`data/schema.unit.json` declared `"additionalProperties": false` and did not
list the `air` key that `com_strike_flyer.yaml` authors and `DataLoader` reads.
**FIXED 2026-08-01**, and the fix is the guard rather than the key.

CLAUDE.md says gameplay numbers "live in /data as YAML **validated against**
/data/schema.unit.json". The first half was true and the second half was not:
the schemas declare `additionalProperties: false` and **nothing anywhere read
them**. There is no JSON-schema validator in the tree, so they were
documentation, and documentation drifts. It had already drifted for four waves,
and under the schema as written `com_strike_flyer.yaml` was invalid the whole
time.

`schemagate` is that sentence enforced: 36 authored definitions and 540 keys
checked against the three schemas every build. It checks the DATA against the
SCHEMA rather than trying to prove the loader and schema agree statically,
because a key the loader reads that nothing authors is harmless, while a key a
file authors that the schema forbids is either a typo the loader is silently
ignoring or a schema that has fallen behind. Both branches were proved to bite
by breaking them.

**And a second gap it surfaced: `data/weapons/` was EMPTY.** Every weapon number
in the game lived compiled in `Combat.cs`, which contradicted "all gameplay
numbers live in /data" as plainly as the unenforced schema did. **CLOSED
2026-08-01**: nine authored files, `data/schema.weapon.json`, and a fourth
directory in schemagate's walk (36 definitions and 540 keys became 45 and 597).

The part that mattered was not authoring the files. It was making them **drive
the runtime**. Writing the yaml while leaving `Combat.Weapons.Get(id)`
authoritative would have reproduced P7-1's defect exactly - authored data that
is parsed, validated and then dropped while the sim uses a hardcoded rule - and
it would have looked complete. `World` now holds a registered weapon table, the
runtime call sites read it, and weapons are folded into the catalogue checksum.

`weapondatagate` makes that mechanical rather than promised. Its second stage
registers a ten-cell gun where the compiled table says five, and asserts a
TURRET dealt damage at seven cells: a mobile shooter would have walked into
compiled range and passed the stage for the wrong reason, which is the kind of
false pass that has cost this project several waves. Measured 840 damage against
the control's 0, and proved to bite by reverting one call site.

Goldens byte-identical, because the transcription is exact and stage 1 asserts
it field by field. The catalogue checksum moved from `0x374FDD8212234CB2` to
`0x73326A3FF8AEA4D1`, which is expected: a new catalogue section changes it by
construction, on the same pre-first-public-build argument as P7-2/3/4 and P7-11a.

**That fan-out was the next wave, and it is CLOSED.** `CatalogueFiles.Register*`
was a per-kind opt-in, so `RegisterWeapons` had to be added beside all nine
`RegisterFields` sites plus the client's.

The name was the tell: **`RegisterAll` did not register all**, and had not since
fields were added. A caller who forgot a kind got a world with a partial
catalogue and NO error, silently falling back to the compiled defaults. The
recurring shape again, a rule keyed on an instance.

`RegisterAll(world, dataRoot)` is now the single honest entry point, and the
three-argument one is renamed `RegisterUnitsAndStructures` because that is what
it does. Thirty-two calls across eleven clusters became eleven.

**The guard is the point, not the tidying.** One table lists every `/data`
subdirectory as either a catalogue kind with its registrar or a known
non-catalogue, and both the registration loop and the guard read that ONE table
so they cannot drift. An unrecognised directory is refused by name:

> unrecognised /data directory 'zzz_probe'. Every directory under /data is either
> a catalogue kind this loader registers or one recorded as holding no defs; an
> unknown one would be authored, validated and then silently ignored.

So the next `/data` kind cannot be silently forgotten in one of ten places.

Checksum measured unchanged at `0x73326A3FF8AEA4D1` before and after, and the
gate asserts it three ways at once: the single call, the old per-kind sequence
run explicitly beside it, and a bare compiled world all agree.

One deliberate narrowing, recorded because it is a real semantic change: a
`/data` present but missing `weapons` now throws where it once silently compared
a partial checksum. That case cannot arise in this repo, and refusing it is
exactly the point of the wave.

## What P7-8 turned out to be

The row assumed the sim was the hard part. **It was not.** `VictorySystem` is
already a last-one-standing rule over N seats with a per-seat announcement
latch; the save format already writes the seat count and loops it; the LAN relay
already takes `playerCount` as a real parameter and sizes everything from it;
and `SkirmishAI` already holds only its own seat and picks hostiles with "anyone
who is not me and not neutral". Three of the four two-player assumptions were in
the CLIENT.

The worst was silent and inverted the result. The client reconstructed the
winner by flipping a seat number, in three places, including deriving it from
the LOSER (`_winner = player == 0 ? 1 : 0`). With three seats a `Winner` of 2
called `OnEliminated(0)`, which set `_winner = 1`: **player 1 shown VICTORY and
the actual winner shown DEFEAT**, no crash, no log, as the last thing the match
says. A second defect ended everybody's match on the FIRST elimination event,
when the sim emits one per seat and plays on.

Two things about that are worth keeping:

1. **The CI guard meant to prevent exactly this could not see it.** Its regexes
   keyed on the literals `[01]`, so `PlayerId == 2` passed untouched, the
   `== 0 ? 1 : 0` ternary form evaded it entirely, and its remedy message
   actively recommended `EnemyPlayerId`, which IS `1 - LocalPlayerId`. A guard
   that teaches the assumption it is guarding against has to be changed in the
   same wave as the code, or it pulls new work straight back into the old shape.
2. **The headless client harness caught it, again.** The file's own comment
   records this class shipping once before and being found only by driving the
   client from seat 1. It was found the same way this time. That harness has now
   caught eleven defects the sim battery is structurally blind to.

**And one hard ceiling found while looking for something else:** `DetectedMask`
is a `byte`, so eight seats is the limit - exactly GDD s9's maximum of you plus
seven, with zero margin. A ninth seat shifts out of the byte and that player's
detectors silently stop revealing stealth. It is hashed state, so widening it
later is expensive. Recorded in ADR-031 rather than left to be discovered.

## What P7-8b turned out to be

A refactor from a PAIR to an ORBIT, and the orbit found coverage the pair never
had. `mapgen.py` wrote every feature as "a cell and its one 180-degree image",
and `validate()` compared `starts[0]` against `starts[1]` seven times over. Both
are now the general form: a feature is written as its whole orbit under the
map's symmetry group, and every fairness check runs over all starts.

Two decisions inside it:

- **`mirror2` (double mirror), not a quarter turn.** 90-degree rotation requires
  a SQUARE map and not one of the nine maps in the pool is square, so adopting
  it would have meant the first four-player map also being the first square one.
  The double mirror works on any rectangle.
- **Seats 0 and 1 must be the 180-degree pair**, asserted by the generator.
  `rot180` is a member of the `mirror2` group, so ordering the starts this way
  makes a TWO-player game on a four-start map exactly as fair as on any existing
  two-start map. That is what lets skirmish-09 be offered in the menu today,
  while the lobby still only expresses two seats.

**The generalisation was tested by breaking it**, five ways, each refused with a
specific message. The one worth keeping: closing every crossing but leaving one
pass unrecorded was caught with "starts 0 and 2 stay connected" - **a pair the
old `starts[0]` versus `starts[1]` check never looked at**. The refactor did not
merely tolerate more starts, it closed a hole that existed at two.

Two honest costs of the group, both documented in doc 26 rather than left to be
found: a feature sitting on a mirror axis cannot wander across it without being
reflected into two features, so the dykes vary in width rather than position;
and decoration is placed as an orbit too, so the four quarters cannot be dressed
differently the way skirmish-02 distinguishes its two lands. The Kiln is the
map's only landmark.

One thing deliberately NOT done: the default start-separation floor,
`int(0.7 * max(w, h))`, cannot be met by any four-quadrant layout, because it
exceeds `min(w, h)` on anything wider than 1.43:1 while a four-quadrant map's
closest pair faces across the SHORT axis. skirmish-09 passes an explicit
`min_separation` with its reasoning. Making the default aware of the seat count
is the right fix and would change what the floor means for two-start maps, so it
is a wave of its own.

## What P7-8d turned out to be

The obvious shape was to grow `MatchSetup` per-seat fields and version both
codecs that carry it, the save sidecar and the LAN blob. That is a compatibility
break and a migration, and it turned out to be unnecessary.

**The seat count does not need to be stored, because it is derivable from an
input the sidecar already names.** `SeatsFor(map)` reads the map's start count,
so a save or a replay written before multi-seat existed rebuilds the identical
world with no format version and no migration. Every map but skirmish-09
declares two starts, so every existing match is unchanged by construction.

The ceiling of 8 in that function is not a taste call: `Entity.DetectedMask` is
a byte, so a ninth seat shifts out of it and that player's detectors would
silently stop revealing stealth. ADR-031 recorded that ceiling; this enforces it,
which turns a silent wrong answer into a map that seats eight.

**One defect this created and caught before it shipped.** LAN seats exactly two
humans and builds its relay with `playerCount: 2`, but the world's seat count
now comes from the map. A LAN match on skirmish-09 would have seated two humans
and left two bases with NO controller: they would never act, and VictorySystem
would refuse to end the match until somebody walked over and razed them. A match
that cannot finish. It was refused loudly in `Lan.BuildFrom`, and I said lifting
the refusal needs LAN seat negotiation. **Lifted by P7-8f, and it needed no
negotiation at all**: the spare seats are played by commanders that each peer
generates locally and folds into the same tick, which is safe because the
commander is deterministic and its tuning rides the catalogue checksum the hello
already refuses on. `lanaiseatsgate` measures it, including that a peer running a
different commander is caught rather than played on.

**And one the harness caught.** The opponent-faction rule was written as
"alternate between the player's pick and the opponent's pick", which reads
sensibly and is wrong: both default to the same faction, so all three opponents
came out identical. Alternating between the two FACTIONS holds whatever the two
menu picks happen to be.

~~Still not done, and deliberately: there is no opponent-count control in the
menu.~~ **Done as P7-8e, 2026-08-01.** I called the picker "menu work with no
new capability behind it" and that was wrong in one specific way worth
recording: filling every seat the map declares means a player who wants a DUEL
on skirmish-09 cannot have one. P7-8d did not add a capability, it removed a
choice, and GDD s9's wording is "1-7 opponents", which is a choice.

`MatchSetup.Seats` carries it, optional in the sidecar exactly as
`ai_difficulty` is, so zero means "fill the map" and every sidecar written
before the field resumes against the opposition it actually played. No format
version and no migration, the same trick P7-8d used to avoid one.

**The map remains the ceiling and always wins.** A hand-edited or corrupt
sidecar asking for nine seats on a four-start map gets four rather than a
`PlaceSkirmishStart` refusal, which is the "a corrupt sidecar must not take the
menu down with it" posture the difficulty rung already takes. Asserted both
ways in the harness, which went 138 to 145 checks.

The control's RANGE comes from the selected map, because a count the map cannot
seat is not an option, it is a crash waiting for a player to find it. On a
two-start map that leaves exactly one option, which is correct rather than a
special case, and the control says so instead of hiding.

## /data finally holds what the project says it holds

Four consecutive waves, each one surfaced by the last, and worth reading as one
thing because the shape repeats: **a claim that was true when written and
quietly false since.**

1. `schemagate` found the schemas were never validated by anything at all, and
   `schema.unit.json` had been four waves behind the loader on the `air` key.
2. Which surfaced that `data/weapons/` was EMPTY, every weapon number compiled.
3. Which surfaced that `RegisterAll` did not register all, and had not since
   fields were added.
4. Which surfaced that `data/ai/` was empty too (ADR-032).

`/data` now holds every gameplay number the project claims it does: units,
structures, fields, weapons and the AI's tuning. `schemagate` walks five
schemas, 52 definitions and 640 keys.

**The lesson that generalises, and it is the one the whole session keeps
producing:** every one of these was a rule keyed on an instance rather than a
property, or a sentence that documented an intention rather than a mechanism.
None was found by reading a ticket. Each was found by trying to use the thing
end to end and noticing the claim did not hold.

**And one genuinely new rule, from ADR-032 clause 2**, because the next authored
kind will face it: moving a number from code into `/data` moves it from "agreed
by construction" to "agreed only if checked". The AI's numbers being compiled
was an unwritten safety property - two LAN peers agreed on them because they
could not disagree. Anything that can differ between peers and change the
command stream must be in the catalogue checksum. The gate proves the fold is
real by moving the checksum on one unit of wave size.

## The harness gap ADR-033 left, closed

ADR-033 recorded honestly that the client-side rule about WHICH seats get
commanders rested on reasoning and a blob round-trip check, not on the harness,
whose LAN stage built a two-seat world. That gap is closed.

Two things now hold it. The lobby stage runs a second time on **skirmish-09
across a real socket**, and asserts the joiner took the host's seat count, that
both peers built a FOUR-seat world, and that the two are byte-identical before
tick 0. The two-seat stage could not see this class at all: on a two-start map
both sides answer 2 whatever either believes, so a peer that DISAGREES about how
many seats exist is invisible there.

And `LanCommandedSeats` is asserted **from seat 1**, which is the seat this
harness drives and the seat where the old rule's absence shows: "every seat that
is not the local one" read from seat 1 returns seat 0, the human on the other end
of the socket, and would have handed Brutal's handicap to a person.

The method exists at all because the rule was **two loops sharing a bound** - one
building commanders, one granting the handicap. They are the same rule, and the
two drifting apart would be a desync that reads correct on either machine alone,
which is the exact species of defect that preceded it.

Client harness 146 to 153 checks. Also corrected a message from the previous wave
that contradicted itself, reporting "came back 3, not the host's 3" on success.

## The AI aimed at whoever spawned first

Fixed 2026-08-01, and it is the clearest example this phase produced of a defect
that no amount of testing was going to find.

`SkirmishAI` picked the enemy REFINERY as "the first one in entity order", and
that pick beats the nearest-production-structure one at both use sites, so it
decided where every wave and every superweapon went. With ONE opponent, first
and nearest are the same refinery. With three, it means the commander attacks
whichever player happens to sit earliest in the entity array, for the whole
match, **deterministically and reproducibly** - which is precisely why it would
never have been reported as a bug. An AI attacking someone always looks like
intended behaviour.

It is now nearest by the same measure the structure pick uses.

**Measured NEUTRAL: no golden moved.** That is the point rather than a relief -
no golden scenario distinguishes the two rules, so nothing existing proved the
old behaviour and nothing existing would have proved the new. `aitargetgate`
spawns a FAR enemy refinery BEFORE a near one and asserts the first wave goes
near, with both still standing so it is a choice between live targets. Proved to
bite by restoring the old line.

**One thing the gate got wrong first, worth keeping**: it asserted that EVERY
wave order went to the near refinery, and read 17 of 34 as a failure. It was the
commander correctly moving on: the near refinery had fallen, so the far one WAS
then the nearest. A gate demanding every order go to one place asserts that the
AI never finishes anything. The claim is about the FIRST wave.

## The hero, and the two defects adding it exposed

P7-11b is the first row this phase where the design is INVENTED rather than
implemented, and ADR-035 records the alternatives beside each choice so
overturning one is an edit rather than an excavation. The three that matter:
demolition is DAMAGE rather than deletion, so the hit-point column still decides
who dies; the hero SURVIVES its own act where the other three contact units are
consumed, which is what gives "one at a time" something to protect; and "one at
a time" is BUILT, as a general `max_alive` column that is a no-op at 0.

**Adding a fourth effect to a method that already had three is what exposed what
the existing three assumed**, and both findings are worse than the feature:

- **`UnitTypeDef.Air` was in neither `Equals` nor `CatalogueChecksum`**, since
  ADR-028. A drifting `air:` key was invisible to the /data round-trip selftest
  AND to the LAN desync guard, so two peers could disagree about which units FLY
  while every unit, building and gun matched. Worse than the usual case: ADR-028
  clause 3 makes engagement an equality between a weapon's anti-air flag and its
  target's airborne one, so the peers would disagree about what can be SHOT.
- **The Infiltrator crashed on a neutral outpost.** `CanBeActedOn` admits one
  deliberately (capturing a neutral outpost is ADR-021's feature) and the theft
  branch then indexed `_credits[-1]`. An index-out-of-range reachable by
  right-clicking an outpost, latent since P7-7, proved by removing the guard and
  watching the gate throw.

## Seven units were unbuildable, and one still is

Found 2026-08-01 while giving the Infiltrator and the heroes sidebar buttons.
The panel had a **hand-maintained thirteen-entry table** against a catalogue of
twenty, so the Carrier, the Strike Flyer, the Flak Track, the Infiltrator, the
Saboteur and both heroes had no button at all. **P7-3 and P7-4 were reported as
DONE with their units unreachable by any player.**

The list is derived from the catalogue now, and the refactor is provably inert:
all thirteen hand-written labels are EXACTLY what stripping the faction prefix
and upper-casing produces, with zero mismatches. Nineteen buttons where there
were thirteen.

**The same defect had a second instance**, which the first one's own comment
predicted. `SkirmishLive` carried another thirteen-entry name table with a
length guard returning "UNIT" past its end, so every unit from 14 up read as
"UNIT" in the selection readout and in every toast. That comment records the
table falling behind once already and being fixed BY ADDING ENTRIES, which
treats the symptom: a hand-maintained list of something the catalogue already
knows will fall behind again, and the length guard is what makes it silent. One
derivation now, shared, throwing on an unknown type rather than shrugging.

**FIXED 2026-08-01, the wave after.** `IsProducer` admits the Airfield, the
sidebar has an AIRCRAFT tab, and `airgate` gained the stage it should always have
had: one that ORDERS a flyer rather than spawning it. **All 20 units now carry a
button.** Measured neutral: no golden stands an airfield and the queue fold is
`TryGetValue`-guarded, so an empty one contributes nothing.

Both comments were to-do notes that outlived what they waited for.
`IsProducer` said "the Airfield joins when it exists (it is a slot-model producer
and waits on the air-layer ADR)"; the sidebar said "Four, not five - AIRCRAFT
waits for the air ADR with the airfield it would build". ADR-028 shipped both and
nobody came back to either. **A to-do in a comment is invisible to every gate in
the project**, which is the reusable lesson: the note was accurate, prominent, and
did nothing.

"Slot-model producer" is left UNBUILT rather than invented - aircraft occupying
pads, with capacity limiting how many fly, is a real design and is not what this
fixed. The Airfield queues like every other producer, the smallest thing that
makes the tier reachable.

**The original finding, kept for the record:**
`World.IsProducer` is `Factory or ConstructionYard or Barracks`. **The Airfield
is not in it.** `Produce` breaks on that predicate before reading anything else,
so **the Strike Flyer cannot be built by anybody, in any mode, and never could
be.** ADR-028 shipped an air layer whose aircraft is unreachable.

`airgate` never caught it because it spawns flyers with `SpawnUnit` directly and
never ORDERS one - the same shape as P7-7a, where a gate proved the sim's
behaviour and said nothing about what the game does. The sidebar wave therefore
ships 19 buttons rather than 20, deliberately: a button the sim silently drops
would break the one property that panel guarantees. Fixing `IsProducer` is a sim
change touching the four sites that predicate's comment names, including a
queue-hash fold, so it is its own wave and it is next.

## Reachability, proved systematically rather than row by row

Three defects this phase were one shape: something existed in the sim and no
player could reach it. Seven units had no button; the Strike Flyer had no
producer at all; a robbery announced itself as a capture. **All three passed
every gate, because the gates CONSTRUCTED the outcome instead of asking for it.**
`airgate` spawned its flyers with `SpawnUnit` and was green for months over an
aircraft nobody could build.

`reachabilitygate` is the systemic guard. It orders **every** registered unit
with a real `Produce` command at a producer the ordering player has BUILT, and
every buildable structure with `BuildStructure` then `PlaceStructure`, from one
spawned Construction Yard per player and nothing else. **20 of 20 units and 14 of
14 buildable structures.** The three excluded types are checked BOTH ways: named
here with a reason, and confirmed to have no build time in `/data`, so a fourth
map-placed building has to be documented rather than absorbed.

It found nothing further unreachable, which is the answer to "is there more of
this" and is worth as much as a finding would have been.

**The tech tree closes in three rounds from a bare yard with no authored build
order**, which is a stronger statement about the tree than the gate set out to
make.

**And it found a different defect of the same family.** `World.SpawnHarvester`
predates the catalogue and ignores it: it never sets `UnitType`, so every
harvester in the game stands as type 0 and its authored def cannot be read back
off the entity - `AtMaxAlive`, `IsAirborne` and the client's name and model
lookups are all blind to it. It also hardcodes hp, armour, sight and speed, and
**the speed diverges: 1/5 in code against `speed: 18` in `com_harvester.yaml`, so
every harvester moves at 0.20 where the data says 0.18.** P7-1's defect exactly,
in the oldest spawner in the file. Fixing it moves every golden with a harvester
in it, so it is its own wave and it is next.

## Mines, and the fourth path to forget the air layer

P7-11c was refused three times, and the refusal named the reason exactly: the
damage half was nearly free because splash, `ApplyAreaDamage` and the
superweapon's countdown all exist, and **the trigger was the entire feature**.

Modelling a mine as a STRUCTURE YOU PLACE is what made it small. It inherits the
placement command path, ownership, cost, prerequisites, the catalogue, the save
format and `reachabilitygate`'s coverage - that gate's count moved from 14
buildable structure types to 15 without being told mines existed. It is hidden by
the SAME stealth flag a Phantom Tank uses, so the detector rule already IS the
public counter GDD line 56 demands.

Three findings worth more than the feature:

- **A blocking mine would have corrupted the map, not merely leaked.** The flow
  field is shared ground truth, so a blocking mine leaks its position to enemy
  pathing while cloaked. Worse, `ValidPlacement` skips structure cells as
  already-blocked, which a mine's are not, so a 2x2 building can legally sit over
  a live mine - and unblocking on detonation would have cleared a cell that
  building occupies. Skipping both block and unblock is a correctness
  requirement, not symmetry.
- **AIRCRAFT SET OFF GROUND MINES in the first draft.** ADR-028 records its own
  first pass guarding two of THREE target-selection paths and shooting an
  aircraft down with a rifle. This is the fourth path and it was missed again.
  **Three misses across four paths says the omission is structural**: nothing in
  the codebase makes "is this target airborne" a question a new path has to
  answer. Fixed here; the general problem is a row of its own.
- **The sidebar's STRUCTURE lists are still hand-kept arrays.** The unit list was
  derived after seven units were found unbuildable; the two structure tables were
  not, and the mine needed a hand-added entry. It is GUARDED rather than fixed -
  the harness asserts every registered structure carries a button, so the next
  building fails CI rather than shipping unreachable - but deriving it properly
  is owed.

## Naming the two questions the sim conflated, before teams

Done as its own wave, deliberately, because P7-8c would otherwise have been
nineteen hand-edits of a question that must be asked everywhere - **and that is
exactly how the air layer was handled, which went wrong three times out of
four.**

The sim conflated TWO questions behind one expression: **ownership** ("is this
mine", for commanding, loading, repairing) and **hostility** ("is this an
enemy", for targeting, contact effects, the mine trigger, the AI's scans).
Today both read `PlayerId != mine`, and **teams is precisely what splits them**:
a teammate is neither mine nor an enemy. Left as one expression, teams could not
have been added correctly at all.

**The inventory is the deliverable, and it reshapes the row.** 41 sites: **9
hostility, 32 ownership, 0 ambiguous.** The question teams changes is far smaller
than it looked.

But it is not one expression full stop. **Six sites are written as NOT-MINE on
purpose and will not follow `IsEnemyOf`**, so teams is one expression plus six
explicit design decisions, each now carrying a comment saying so:

- `CanBeActedOn` - a neutral outpost is nobody's enemy, and capturing one is the
  outpost's whole point (ADR-021)
- the detector sweep - uncloaks anything not its own, including a neutral
- the separation yield
- `HasPrereqs` - does an ally's radar unlock your tech?
- `HasHope` - is a team eliminated only when every member is?
- the veil projector

**And the airborne question is now structurally unavoidable rather than
remembered.** `CanBeEngagedBy(byPlayer, antiAir, target)` takes the flag as a
REQUIRED argument with no default, and the three paths that pick a victim go
through it, including the mine trigger - so the shared gate absorbed a fourth
path rather than tidying three. A comment saying "remember the air rule" was
already present and was forgotten twice; an argument with no default cannot be
left off. The residue is named rather than claimed away: somebody could still
write a scan calling `IsEnemyOf` directly, so the doc comment enumerates all six
selection paths and what each asks, including the three that deliberately stay
out and why forcing them through would change behaviour.

**Provably inert**: all 24 goldens byte-identical and the catalogue checksum
unmoved, which is the entire proof that 41 sites were re-routed without one
being sent to the wrong question.

## Teams, and the four things being allied does NOT imply

The refactor is what made this small. 41 sites, 9 of them hostility, and a team
is a per-player id **defaulting to the player's own** - so a free-for-all is
unchanged BY CONSTRUCTION, which is the mechanism that keeps all 24 goldens
byte-identical rather than a happy result.

**Teams change four things**: `IsEnemyOf` (and everything routed through it
follows for free), victory counting living TEAMS rather than players, contact
effects refusing a teammate's building while still taking a neutral outpost
(ADR-021 untouched), and detectors not bothering to reveal an ally.

**And four things being allied deliberately does NOT imply**, each left unchanged
with a comment at the site saying it is a decision: tech does not flow, vision
does not flow, the veil hides its owner only, and splash still hurts allies
exactly as it already hurts your own men. Turning all four on at once, unmeasured,
in a game nobody has played, would be four untested claims wearing one feature's
name. Each is a one-line change and a gate when wanted.

**A client defect fixed with it.** `World.Winner` is a player id and the sim names
the last standing seat of the winning team, so the client's `winner ==
LocalPlayerId` would have shown **the winner's own teammate a DEFEAT banner**.
Unreachable today because no lobby calls `SetTeam` - which is exactly why it was
fixed now rather than when a 4v4 lobby lands, since it is the same shape as the
seat inversion the harness caught twice: a comparison right at one seat by luck.

Two findings banked: **`DowngradeSave` is a third edit site for a save-format
bump** and it fails in the battery rather than at the change; and **`SetFaction`
does not actually refuse after tick 0** despite being the obvious model for
`SetTeam` - a bare array write with no guard.

## The lobby can express a team, and that closed a gap I had recorded

Taken immediately after teams rather than moving on, because teams reachable only
from a gate is precisely the "exists in the sim, unreachable in the game" pattern
this phase spent five waves eliminating. Shipping it and moving on would have
created a sixth instance.

**A team MODE, not per-seat assignment**: `FREE FOR ALL` (every seat its own team,
today's behaviour exactly, and it calls `SetTeam` not at all - which is the
mechanism for hash neutrality rather than a resemblance to it) and `EVEN SIDES`
(`SetTeam(p, p % 2)`). On a two-start map the two are **measurably** identical,
both hashing `0x0E3B3689A8833245`, and that is now a harness check rather than a
paragraph. On skirmish-09 it is 2v2, which is GDD s9's promise with one field and
no per-seat UI.

The sidecar takes it optionally, so every sidecar written before this loads as
free-for-all, which is what those matches were. **The LAN blob had to bump to v4**,
and the asymmetry is the same one ADR-038 recorded for saves: both peers must
agree before tick 0 or they build different worlds. Pleasant consequence worth
knowing: the relay seats peers by arrival, so on a four-seat map the two humans
take seats 0 and 1, land on opposite sides, and each gets a commander ally.

**And it closed the coverage gap ADR-038 recorded honestly.** That ADR admitted
the client's team-victory banner was not harness-covered, because `SetTeam` is
refused after tick 0 and a mid-run call hung the Verify scene. With a team mode in
`MatchSetup` a **teamed world can be built from the start**, so the harness now
drives a second real scene and asserts the winner's TEAMMATE reads VICTORY, with
an enemy control proving the pair discriminates. Proved to bite: reverting the
comparison gives `my TEAMMATE being named the winner reads as VICTORY at seat 1
("DEFEAT")`.

**One trap banked**: `LaunchNetBattle` copies the lobby's setup into `MatchConfig`
field by field and copies neither `Seats` nor `TeamMode`. Harmless today because
the LAN scene discards its own build for the lobby's, so the blob is the only
carrier that matters - but it is a trap for whoever next reads `_setup` in a LAN
branch.

## Three owed items, closed together

All three were the same defect class this phase kept finding: **a rule that
should be enforced is not, or a hand-maintained copy is lagging its source.**
Bundled deliberately, because items like these are never the biggest row on the
list and so never get done.

**`SetFaction` had no tick-0 guard**, while `SetTeam` and the catalogue
registrars all do. The faction is HASHED state, so a call after tick 0 would
change the state hash mid-match: every replay of that match would diverge at the
tick it happened, and in LAN the two peers would part company the instant one of
them made the call. Nothing does today, which is why it cost nothing and why it
was a trap rather than a bug. The battery stayed green after guarding it, which
is the proof no path was relying on the freedom.

**The sidebar's two STRUCTURE lists are derived now**, the last hand-maintained
catalogue copy. Two facts made it possible: every structure label is EXACTLY the
id derivation, as every unit label was; and the BUILDINGS/DEFENCE split is
genuinely **not** derivable, being editorial - the Airfield sits in DEFENCE
despite being a producer, and the wall, veil, superweapon and mine carry no
weapon. So the split is AUTHORED as `build_tab` in each building's yaml, and the
loader refuses a file whose tab disagrees with the sim's own queueability rule,
which is the same equivalence `reachabilitygate` asserts from the other side.

`build_tab` deliberately does NOT ride the catalogue checksum: the sim reads it
nowhere, so two peers holding different tabs still accept the same commands and
build the same army, which is the test ADR-032 sets.

**One player-visible consequence, accepted rather than hidden**: buttons within a
tab now sit in ascending type id, matching the unit list and its stated reason,
where the arrays were in rough tech order. Label, tab and icon are unchanged and
asserted so; POSITION is not, and DR-08's hotkey slots shift with it. Authoring
an explicit order is the alternative and is scope creep for a game nobody has
played.

**Icons still cannot be derived**, and that is worth knowing rather than
rediscovering: six structures deliberately wear another building's PNG while
their own is owed to art-pipeline, and `com_wall_straight.png` does not exist at
all. A bare `icon = id` would silently drop the four that render today. The
placeholder map is explicit art debt and is deliberately not part of deciding
which buttons EXIST, so a new building gets no icon rather than no button.

**`LaunchNetBattle` copied the lobby setup field by field** and carried neither
`Seats` nor `TeamMode`. There is one setup-to-config copy in the client now where
there were two, so a new `MatchSetup` field cannot be forgotten there. Proved by
deleting a field and watching the harness go red.

## The commander knows it has allies

ADR-038 and ADR-039 both ended by naming this gap. "Co-operate" is not a
specification, so the first job was deciding what it means, and **the constraint
shaped the answer**: `SkirmishAI` instances are independent, hold only their own
seat, mutate nothing and have no channel to each other. That is deliberate and
load-bearing - it is what makes an AI match replayable, since a replay re-runs the
bare command stream with no AI attached. So co-operation cannot be negotiated. It
has to be DERIVED FROM WORLD STATE, which every commander already reads and which
is identical on every machine.

That rules out the obvious ideas - agree a target, divide the map, request help -
and points at the ones where shared ground truth suffices.

**Co-operation means defending the team.** One predicate: the threat scan asked
"is one of MY things being walked on" and now asks "is one of MY SIDE'S". One line,
because the P7-8g refactor had already separated ownership from hostility - the
question was always *whose ground is this*, and the answer widened from a seat to
a side. Hash-neutral by construction, since the default team map makes
`IsAlliedTo` reduce to `PlayerId == _player`.

**Measured as a behaviour rather than asserted as a predicate**: the same fixture
runs twice, an enemy overrunning seat 1's base while seat 0's garrison idles with
nothing of its own at risk. Allied, 6 orders sent. Not allied, 0. The difference
IS the assertion, and the control is there because a stage running only the allied
case would pass on a commander that charges at everything.

**What it deliberately does NOT mean yet**, recorded so the row is not read as
more than it is: no shared targeting (allies pick waves independently and will
often hit different bases), no economy courtesy (allied harvesters compete for the
same nearest field), no tech or resource sharing (ADR-038 already decided that),
no formation or timing. And an ally that is losing will still be allowed to lose,
because the responder only reacts inside its own guard radius of a team structure
- a commander will not cross the map to help. That is a number in the same scan
rather than a policy.

## Wall gates: the blocker was narrower than it read

ADR-005 clause 6 deferred gates and stated the blocker precisely: passability is
ONE global grid and `FlowFieldCache`'s only invalidation is `Clear()`, so **"a
gate that is passable to its owner AND solid to the enemy"** needs per-player flow
fields or incremental flow repair, and neither exists.

**That is entirely correct, and it is scoped to SIMULTANEOUS per-player
passability.** It does not consider a gate with a single GLOBAL open/closed state,
which needs neither mechanism: an open gate is passable to everyone and a closed
one is solid to everyone, and toggling uses the `_flow.Clear()` that already fires
whenever a wall goes up or a bridge falls.

So clause 6 was **sidestepped, not overridden.** It stands untouched, with an
amendment recording the distinction and the price: **an enemy can follow you
through an open gate.** That is the design rather than an oversight, and the gate
asserts it deliberately so it cannot later be "fixed" into the per-player
passability clause 6 refused.

The hysteresis is load-bearing rather than polish, and the numbers say why: **1
grid flush** for an ally parked beside a gate for 900 ticks and **12** for one
crossing repeatedly, against the roughly 900 a per-tick answer would have cost.

**Two findings worth more than the row:**

- **A gate stage was green while proving nothing.** The enemy ordered across a
  shut gate detoured round the wall long before entering the three-cell radius,
  so "it stayed shut" was measured on a gate nothing approached. The sabotage
  probe exposed it; the replacement walks an enemy PAST the gate, with a control
  asserting it really entered the radius.
- **`ValidPlacement` skipped every structure's cells as "already blocked", which
  is false for a mine, a live bridge and an open gate.** Left in, a player could
  open their own gate, drop a wall segment on its cell, and the gate's next
  opening would unblock the wall. Removing the skip is provably inert for anything
  that blocks, and the goldens confirm it.

**One thing deliberately NOT done, measured and reported rather than asserted:** a
shut gate is not pathable, so an order straight ACROSS one detours round the wall
instead of waiting for it to open. Making that order path through would be exactly
the per-player passability clause 6 refused, so a later wave answering clause 6 on
its own terms is not blocked by this one.

## P7-6 refused, and the measurement found the opposite problem

GDD s4 specifies the economy in full and never mentions storage, a cap or
overflow. What it does specify is an intent, and **one word decides the row**:

> A player **floats** at 2 refineries / 3 harvesters on one base.

The economy s4 describes is a FLOW, not a stockpile. A ceiling is a rule about a
stockpile, so it is not a missing piece of that design - it is a different one.

Refusing on a reading alone would be an opinion, so `economyprobe` measures what
s4 actually claims, which nothing had ever checked. Over 9000 ticks of real
AI-versus-AI on skirmish-01:

```
   tick   credits0   credits1   refineries0   harvesters0
   1500       3329       4029             1             1
   4500          0          2             1             1
   9000          1       2172             1             1
```

**The treasury does not run away. It oscillates near zero**, because credits are
spent as fast as they are earned. A silo would have been machinery, a build
option, a schema key, a hash fold and a gate, all to make a decision about a
stockpile that does not exist.

**And the measurement found something that matters more than the answer.** Those
last two columns are 1 refinery and 1 harvester, sustained for the whole run,
where GDD s4 specifies **two and three**. The design intent is not being met, and
it is not being met in the direction OPPOSITE to the one P7-6 assumed: the economy
is not overflowing, it is undersized. The commander builds one refinery and one
harvester and stops.

That is a row of its own, it is measurable by the probe that found it, and it is
filed rather than fixed here because this wave's question was whether a ceiling is
wanted.

The refusal is reversible and its conditions are written down: the treasury
running away on a re-run, a Producer amendment changing s4's intent from a float
to a stockpile, or **a human playtest finding late-game banking the AI does not
exhibit** - which is the likeliest of the three, since the probe measures two
commanders that spend continuously and a human who turtles may bank in a way it
cannot see.

## What this phase does NOT do

It does not chase the unit counts in doc 24's table. Thirteen units against
twenty or thirty is a real gap, but parity by headcount is the wrong target: the
benchmarks' rosters carried duplicated roles, and this project has spent P6
building things they never had. The goal is that no CATEGORY of decision is
missing, not that the lists are the same length.

## Prerequisites carried from P6

These block rows above and are not P7's to solve:

- ~~**Q017**, the faction-identity sequencing question, blocks P7-5.~~ **Answered
  2026-08-01 by taking its own first candidate.** Q017 asked which identity step
  comes first and listed four; DR-02 (power economics) shipped as P7-5a under
  ADR-042, on the question's own argument that it is "the most thematic option
  and the one the GDD already designed in prose". DR-03 and DR-04 are now
  ordinary open rows (P7-5b, P7-5c) rather than blocked ones, because both are
  WRITTEN in the GDD - line 56 requires that every stealth tool have a public
  counter, and s8 specifies both superweapons precisely. What Q017 called a
  roster call turned out to be a roster call only for candidate 4
  (per-faction refinery economics), which nothing has asked for.
- **Q014**, the second-resource question, is unrelated to parity and stays in P6.
- **ADR-027**, the crowd-aware movement decision, blocks nothing here directly
  but distorts every AI-vs-AI measurement any of these rows would be judged by,
  so it should be answered before P7-2's balance work.
- ~~**Q012/Q016**, win and loss semantics, block P7-9.~~ Both answered and closed
  2026-08-01 under the standing directive, having passed their decide-by dates.
  Q012 took fork 3 (elimination and a scripted objective are both wins); Q016
  took option 1 (you have lost when you hold nothing that counts), which needed
  a new `eliminated P` trigger condition because the obvious fix - stop
  suppressing short game - hands the player an instant win against an attacker
  that owns no buildings.

## Changed / Assumed / Needed next

**Changed.** New file: the plan doc 24's analysis asks for.

**Assumed.** That "set that as a goal" means the parity gap becomes the project's
next phase after P6's remaining decision-gated rows, not that P6 is abandoned.
P6's tracker stays authoritative for its own rows.

**Needed next, and from whom.** Luke, three decisions, in this order: is P7-1 a
defect or intent; does the air layer or transport lead Tier A; and is D2's
player-count promise kept or the GDD amended to match what shipped. The first
unblocks a small fix immediately; the other two decide the shape of the phase.
