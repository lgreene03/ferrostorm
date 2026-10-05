# ADR-075: the commander's placement scan is oriented to the map centre, and the seat residue is bisected to its causes
- Status: Proposed (row P8-21, decision D5 of docs/tickets/P8-formidable-tracker.md; drafted by the implementer agent). Not self-ratified: it moves four golden hashes and changes which commands a commander issues, so it needs the Architect's sign-off, and the orchestrator may ratify it under the owner's standing authority of 2026-10-02 citing D5.
- Date: 2026-10-05
- Deciders: Architect agent + Luke
- GDD/TDD feature served: doc 26 section 2 ("rotational symmetry for fairness": the half turn about the map centre maps each start onto the other); GDD s9's skirmish against the commander; TDD s1 ("determinism is the product") and s9's replay compatibility promise; criterion F5 and decision D5 of the P8 tracker; findings AI-04, BAL-01 (map half) and OJ-01 (3)

## Context

**The bias.** AI-04 measured seat 0 taking 141 of 189 decided unique `ladderprobe` matches, and the Directorate in seat 0 taking all 50 decided mirrors. P8-13 then found that swapping the two starts relabels the winner exactly in 34 of 36 pairs, so the advantage belongs to the START, not to the seat. P8-15 (ADR-071) removed the docking freeze that was one cause, and P8-18 (ADR-073) let more mirrors resolve, which exposed what was left: at d9505b9 `seatfairgate` reads a pooled start split of 20/8 of 28 decided and income within 15 per cent in 20 of 32 matches.

**The suspected cause.** `SkirmishAI.TryFindPlacement` rings out from each own structure, oldest first, trying the cells of each ring in one fixed compass order: rows top to bottom, each left to right. On a map that is the half-turn of itself, a base in the top-left corner therefore tries its sheltered corner side first and the base in the opposite corner tries the side facing the centre first, so the two commanders build different bases from the same opening. D5 decided to orient the scan to the map centre so that seat 1's layout is the rotation of seat 0's, and then to bisect whatever residue remained.

## Decision

### 1. The scan is walked in one canonical frame, reflected per anchor and per axis

The ring offsets keep their order and shape but are written in ONE canonical frame and reflected along each axis on which the anchor structure's centre stands short of the map's centre (`2 * anchor + footprint < width`, and the same for the height). A base and its rotated twin then try mirror-image cells in the same order; on a mirrored map (skirmish-09's seats 2 and 3) the reflection is along the one axis that separates them, so their layouts are reflections, matching how that map relates its starts.

The reflection is about the anchor structure's own centre and carries the 2x2 box with it that `ValidPlacement` is asked about (the scan has always asked with the default footprint), so it is exact for any pair of footprint sizes, including the Sodality's 1x1 generators round a 2x2 yard. Reflected, a 1x1 building's anchor no longer coincides with the box's anchor, and the sim's own test on the PlaceStructure command reads the building's anchor for the build radius, so in that case the building's own `ValidPlacement` is asked as well: a placement the sim then refused would be retried every beat and stall the yard. Where the two anchors coincide the box test already implies it. A structure whose centre stands exactly on the map's centre line is not reflected; it is its own rotation partner along that axis, which is the one tie and a recorded asymmetry. ADR-071 clause 3's apron filter is unchanged and is itself symmetric (a footprint overlap test and a ring of cells).

**The canonical frame is the side facing the centre first**: the walk a base right of and below the centre always had. The reason is measured rather than argued: on all eight two-seat maps the home ferrite lies towards the centre from the yard (the mean offset of the eight nearest ferrite cells from start 0's yard, measured along the axis towards the centre, is +18.0 and +14.5 cells on skirmish-01, +17.5 and +10.0 on 02, -0.6 and +12.4 on 03, +13.4 and +14.1 on 04, +20.0 and +7.0 on 05, +16.6 and +20.9 on 06, +17.1 and +29.9 on 07, +15.1 and +20.9 on 08), so this frame builds a base towards its own economy. The other natural frame, the sheltered side first, was measured and refused (see "Alternatives rejected"): it fails the binding `aiairgate`.

**Proved to be the rotation.** `mirrorprobe` (clause 3) compares a Normal mirror with its own half-turn after every step. With the opening hand and three compass-fixed sim rules neutralised in an uncommitted build (section "The residue, bisected"), every placement either commander issues on every two-seat map, in both mirrors, is the exact rotation of the other's until the first break that is not a placement (between t=406 and t=2493). With the OLD scan in the same build, the first placement already differs: on skirmish-01 seat 0 put its first plant at (6,6) and seat 1, in seat 0's frame, at (12,12) at t=210 in the Directorate mirror, and the Sodality's first generator went to (6,6) against (13,13) at t=90.

### 2. F5 is not met, so `seatfairgate` stays non-binding

F5 asks for each seat's income within 15 per cent of the other's in every match and a pooled win split no worse than 60/40, across Normal mirrors of both factions on every two-seat map with starts swapped. Measured at seed 2026, before (d9505b9) and after this ADR:

- **Income within 15 per cent: 20 of 32 before, 18 of 32 after.** Mean gap over the 16 independent pairs 20.5 to 16.8 per cent.
- **Pooled split: by seat 14/14 before and 11/13 after; by start 20/8 of 28 before and 21/3 of 24 after.** Directorate mirrors by start 10/2 to 8/0; Sodality mirrors 10/6 to 13/3.
- Swapping the starts relabels the match exactly (same winner by start, same end tick, same incomes) in 14 of 16 pairs before and 14 of 16 after (one of the two exceptions after changes its winner, see "The residue, bisected").

Per map, the income gap (sign: positive when start 0 earned more) and the start that won, before then after:

| Map | Directorate mirror | Sodality mirror |
|-----|--------------------|-----------------|
| skirmish-01 | -1 per cent, start 0; +101 per cent, start 0 | -2, start 1; +18, start 0 |
| skirmish-02 | +14, start 0; +10, start 0 | +3, start 0; +22, start 0 |
| skirmish-03 | +5, start 0; +5, no result | -83, start 1; +20, start 0 |
| skirmish-04 | -6, no result; +2, no result | -46, start 1; -21, start 1 |
| skirmish-05 | +9, start 0; +10, start 0 | +31, start 0; -8, start 1 |
| skirmish-06 | -17, start 0; +4, start 0 | +12, start 0; +1, start 0 |
| skirmish-07 | +5, no result; +0, no result | +30, start 0; +23, start 0 |
| skirmish-08 | +64, start 1; -4, no result | +1, start 0; +20, start 0 |

So `MeasurementHarness.SeatFairGateBinding` stays false, its comment and the gate's verdict now say it binds when F5 is met rather than naming this row, and the residue is bisected below. The start split moved the wrong way, and that is reported rather than explained away: with today's starts the split is a chaotic readout (section "The residue, bisected" shows it swinging from 5/8 to 10/2 decided pairs between builds that differ only in residual asymmetries), while the income gap, the smoother measure, narrowed.

### 3. `mirrorprobe` bisects the residue, and asserts nothing

A new runner mode, a probe by ADR-061's rule. It plays a Normal mirror in the shipped setup on each two-seat map and, after every step, compares seat 0's half of the world with the half-turn of seat 1's: every entity by what it is and where it stands (kind, type, position, hit points, harvest state, load, motion, cooldown, build state, target, flow walk), never by id; every ferrite field against its rotation partner; and the two treasuries. It compares the commands both commanders issued the same way, a placement's anchor rotated as the far corner of its footprint, and for a walker that differs it prints the next cell the single-target and the 2x2 footprint flow fields give from where it stood a tick earlier, both in seat 0's frame. It refuses a map whose two starts are not a rotation pair. On today's sim its first break on every map is at t=1, the opening hand, so the full run takes about a tenth of a second. It is in neither `match`, `golden` nor CI.

### 4. The flow-field proxy budget is re-measured

`MeasurementHarness.LongMatchProxyBudget` belongs to the matches as they play, and its own note asks a row that moves the commander goldens to re-measure it in the same change. `longmatchperf rebaseline=1` reproduces main's figures on d9505b9 exactly (313713, 77278, 120756) and reads 269005, 44857 and 155592 after (skirmish-07, -08, -09). The gate stays non-binding until P8-31.

## The residue, bisected

**Method.** `mirrorprobe` reports the first tick on which the two halves of a mirror differ. Each break was neutralised in turn in an uncommitted build, and the probe rerun, so each cause below was found as the FIRST break once everything before it was symmetric. None of these experiments ships: every one is a sim rule a human plays under too, moves goldens, and belongs to a row of its own.

1. **The opening hand, t=1 on every map** (`MapData.PlaceSkirmishStart`). A map's `start` line is the yard's footprint ANCHOR, and tools/mapgen.py rotates it as a single cell, so the 2x2 yard of the start right of or below the centre stands one cell further right or down than the exact rotation of the other's: offset (+1,+1) on all eight maps. The opening force leans towards the centre in x only: the harvester spawns two cells below the start and the three squads two cells above it, whichever half of the map the start is in. Neutralised (yard anchored so its footprint is the rotation, opening force mirrored in y), the next break was:
2. **The free harvester, t=526 (Directorate) and t=406 (Sodality) on skirmish-01** (`World.SpawnFreeHarvester`). It spawns at the fixed point (anchor + 2, anchor + 2), the refinery's bottom-right corner, whichever way the base faces. Reflected exactly, that point is the refinery's top-left corner, which `Map.CellOf` (a floor) puts INSIDE the blocked footprint, so the reflected harvester could not route (t=541): the corner-point convention and the floor are a pair. Spawned at the centre of the cell beside the reflected corner instead:
3. **`Fix64` multiplication floors (P8-53), t=711 and t=560 on skirmish-01.** Two mirrored harvesters ToRefinery stood at x=11.4057 and 11.4058 in seat 0's frame: one unit in the last place, from the multiplication's floor towards negative infinity. With multiplication truncating towards zero, as division already does:
4. **The production exit, t=2385 on skirmish-01's Directorate mirror** (`World.ProductionSystem`'s `SpawnOffsets`). A unit leaves its producer at the first open cell of a fixed list that starts two cells SOUTH of the producer's centre cell, and an even footprint's centre cell is its bottom-right cell (`CellOf` again). Reflected, with the centre cell taken on the reflected side:
5. **Two classes remain, first at t=406 to t=2493 on every map.** (a) **The flow field's tie-break** (`FlowField.Relax`, which P8-31's parity-proven Dial queue must reproduce under D28): among equally short routes a cell takes the parent popped first, the lower cell index, which is the north-west one, so from the same cell in seat 0's frame the two halves step to different cells (on skirmish-02's Directorate mirror at t=1426, (9,52) against (9,54)). (b) **Cell ownership of a boundary point**: a unit standing exactly on a cell edge belongs to the cell to its south-east, so mirrored units are in mirrored positions but not mirrored cells (skirmish-01's Directorate mirror at t=2388 after step 4's fix: a fresh rifle squad halted at x=9.0, in cell 9, while its twin's cell was the mirror of cell 8). On five maps' Sodality mirrors at t=1537 the two mirrored harvesters stood in the same cell of seat 0's frame with the same next cell and still stepped about a tenth of a cell apart, a further compass-dependent rule in the step or separation code that this row did not isolate.

The chain was traced on this row's first build, which used the sheltered frame (see "Alternatives rejected"); the probe was rerun with causes 1 to 4 neutralised under the frame this ADR takes, and its first break on every map was one of the classes in step 5 (the t=1537 Sodality cases are from that rerun).

**P8-54 (units step in id order) explains at most one pair in sixteen.** Swapping the starts swaps which side holds the lower entity ids and which commander acts first in the tick's command list, so whatever depends on id or command order, P8-54's same-tick reveal included, shows as a swapped match that is not the relabelling of the authored one. In 14 of 16 pairs, before and after this ADR, the swapped match is the exact relabelling (same start winning, same end tick, same incomes). Before, the other two (the skirmish-02 and skirmish-04 Sodality mirrors) kept the same start winning and only the end tick moved. After, the skirmish-04 Sodality mirror does the same (t=7096 against t=7051), and the skirmish-05 Sodality mirror changes winner: start 1 at t=18811 as authored, start 0 at t=12391 swapped, the two first diverging after first contact at t=3065. So id and command order decide one pair after this ADR and none before; they are not what tilts the start split.

**What each cause is worth, measured on seatfairgate's sweep.** Over the 16 independent pairs (each counted once, since the swapped match is nearly always its relabelling):

| Build | Income within 15 per cent | Mean income gap | Decided pairs by start |
|-------|---------------------------|-----------------|------------------------|
| main, the old scan | 10 of 16 | 20.5 per cent | 10/4 |
| this ADR (centre-facing first) | 9 | 16.8 | 10/2 |
| the sheltered frame instead | 9 | 21.9 | 6/6 |
| sheltered, plus symmetric multiplication (P8-53) | 12 | 18.4 | 7/4 |
| sheltered, plus the exact opening hand | 10 | 14.0 | 10/3 |
| this ADR, plus the exact opening hand | 8 | 17.3 | 5/8 |
| sheltered, plus causes 1 to 4 neutralised | 14 | 9.1 | 6/5 |
| this ADR, plus causes 1 to 4 neutralised | 13 | 10.8 | 8/5 |
| the old scan, plus causes 1 to 4 neutralised | 9 | 14.9 | 8/5 |

Read down the last column and the start split swings from 5/8 to 10/2 between builds that differ only in residual asymmetries: at 11 to 14 decided pairs it is chaotic and cannot rank a single change. Income parity is the steadier signal, and it says three things. No single cause carries the residue: P8-53 alone moves parity from 9 to 12 pairs, the opening hand alone from 9 to 10 or down to 8 depending on the frame. Only with causes 1 to 4 all neutralised does parity reach 13 or 14 of 16 pairs and the mean gap about 10 per cent, and F5's income clause still fails on two or three Sodality mirrors there. And in that symmetric setting the oriented scan, in either frame, is what lifts parity from the old scan's 9 pairs to 13 or 14: the scan is necessary for F5 and not sufficient.

P8-53's own row says to fix it only if it moves the seat split. Measured: on this sweep symmetric multiplication moved income parity from 9 to 12 pairs and the start split from 6/6 to 7/4, which is within the chaotic band; its fix is recommended as one of the four, not alone.

## The start asymmetries of each map

Measured by a scratch script over data/maps. Every two-seat map's grid (blocked cells, ferrite and spans) is exactly closed under the half turn (zero cells break it) and its starts are a rotation pair as cells, so every cell-to-cell property is symmetric and the field counts are equal. What is not symmetric is measured from the YARD, which is what the sim builds round, rather than from the start cell, which is what tools/mapgen.py checks: the yard of start 1 is offset (+1,+1) from the exact rotation of start 0's on every map. Distances are from each yard's centre: the nearest ferrite (Chebyshev), ferrite cells within 10 cells, the walk from the yard's ring to the nearest 1, 8 and 16 ferrite cells, to the nearest crossing, to the open cells nearest the centre, and to the enemy yard, start 0 then start 1.

- **skirmish-01** (96x64, 20 ferrite cells, 60 crossing cells): nearest ferrite 9.5 and 10.5; within 10 cells 2 and 0; walks to ferrite 10/164/528 and 11/172/544; crossing 31 and 32; centre 37 and 38; enemy 81 and 81.
- **skirmish-02** (96x64, 20, no crossings): 7.5 and 8.5; 4 and 4; 6/128/452 and 7/136/468; centre 32 and 33; enemy 72.
- **skirmish-03** (96x64, 20, 24 crossing cells): 9.5 and 10.5; 2 and 0; 8/87/515 and 9/95/531; crossing 18 and 19; centre 31 and 32; enemy 80.
- **skirmish-04** (192x128, 60, 120 crossing cells): 7.5 and 8.5; 5 and 3; 6/103/376 and 7/111/392; crossing 75 and 76; centre 78 and 79; enemy 164.
- **skirmish-05** (96x64, 20, 148 crossing cells): 4.5 and 5.5; 4 and 4; 3/152/539 and 4/160/555; crossing 21 and 22; centre 35 and 36; enemy 78.
- **skirmish-06** (96x64, 20, no crossings): 7.5 and 8.5; 4 and 4; 6/176/542 and 7/184/558; centre 33 and 34; enemy 87.
- **skirmish-07** (256x192, 128, no crossings; start 0 bottom left): 9.5 and 8.5; 2 and 4; 8/230/636 and 7/225/637; centre 108 and 109; enemy 224.
- **skirmish-08** (128x96, 40, no crossings; start 0 bottom left): 9.5 and 8.5; 2 and 4; 8/155/451 and 7/148/448; centre 46 and 47; enemy 100.

On the five maps whose start 0 is top left (01, 03, 04, 05, 06) the offset puts start 1's yard a cell further from everything in the map's interior, its home ferrite included. On skirmish-02, 07 and 08, whose start 1 is top right, the same (+1,+1) moves it a cell towards its own east edge and a cell towards the centre, so it is mixed (and on 07 and 08 start 1's yard ends nearer its first ferrite). Both opening forces lean towards the centre in x and not in y (cause 1). skirmish-09, the four-seat map, is outside F5's two-seat sweep; its seats 0 and 1 are a rotation pair with the same yard offset.

## Golden hashes

Measured at seed 2026; the ordered diff against sim/golden-hashes.txt names exactly these four, and the other 20 are byte-identical. One cause moves all four: each commander's anchors stand short of the centre on both axes, so its scan is now reflected where it was not.

- `skirmish` **0x093568453FAB056D to 0x85DB0AE7C03D779C**: on skirmish-01, seat 0's base in the top-left corner now builds on its side facing the centre first; seat 1's, past the centre, walks exactly as before. Its report line goes from 36 entities destroyed and treasuries 13563/13712 at t=5000 to 37 destroyed and 16617/7221.
- `expansion` **0x1C5EC2136B4DD7D2 to 0x3D59CEBEC984232C**: the lone commander's yard at (8,30) on a 96x64 map stands short of the centre on both axes. Its report line is unchanged (an MCV bought, a second base founded with its refinery, the far field mined to 0).
- `aisuper` **0xF5CF64A3FB56EA21 to 0xCC91B267E08786B2**: the same yard at (8,30). Its report line is unchanged (the weapon placed at t=6001 and fired at the enemy refinery by t=11536, which dies).
- `mission` **0x699787A2B822C29C to 0x001C30B76DD526A8**: the player-side commander's yard at (6,22) on mission-01's 64x48 map. Its victory moves from t=4541 to t=4340, inside `campaignsave`'s horizon.

Every scenario's own assertions still pass (`match` green), and the catalogue checksum does not move: no /data value changed.

## Compatibility

**Old replays are unaffected.** A replay records every command, the commander's included (the client's `RunOneTick` records what the AI and the player decided, and playback attaches no commander), so a recorded match plays back the placements it was recorded with. Nothing this ADR changes is a rule of the sim; it changes which commands a commander chooses.

**Saves keep their format and load unchanged.** A save holds no commander state (SkirmishAI keeps none that is hashed or saved), so a save written before this ADR loads into this build and its commanders, rebuilt at load, choose their next placements by the oriented scan: the match continues differently from how the old build would have continued it, which nothing compares.

**A mixed-build LAN game with a commander seat desyncs, and is not refused up front.** A LAN match on a map that seats more players than the relay carries attaches commanders on EVERY peer, each generating that seat's commands locally (P7-8f, `LockstepClient.SetAiCommanders`), which is sound only while both peers run the same commander. The hello compares only the catalogue checksum, which this ADR does not move, so a pair of builds either side of it is not refused; the first placement by a commander whose anchor stands short of the centre differs between the peers, the states diverge on that tick, and the relay's state-hash comparison every 30 ticks flags the desync within two seconds. A LAN game with no commander seat is unaffected, because the scan is the commander's alone. Builds from before ADR-073 are refused by its catalogue move. This is the class of change question Q024 (docs/questions/Q024-a-sim-rules-epoch-for-replays-and-the-lan-hello.md) asks about: a code-only change the catalogue handshake cannot see.

## Alternatives rejected

**The sheltered side first** (the frame a base left of and above the centre always had). It is D5's words read literally, "seat 1's layout is the rotation of seat 0's", because on the five maps whose start 0 is top left it leaves seat 0 untouched, and it moves only the `skirmish` golden (to 0x0060CD5B55BCF9F4). It evened the start split to 6/6 decided pairs. Rejected because it fails the binding `aiairgate` (F3, ADR-072) for the Normal Sodality: the last flyer fell at t=4980 with no harvester alive, where main kept all three alive to t=4836. Root cause, traced: the commander under test holds seat 0 and is unchanged; the change is seat 1's Normal Directorate opponent, whose reflected base won the ground war earlier. Its units fired on seat 0's harvesters 95 times before the raid at t=4500 (37 times on main), so the flyers found them worn down (harvester 47 at 344 of 700 hit points at t=4237) and killed all three by t=4837, and the same match with no raid lost its economy and the match by t=7786. The air answer did not change; the fixture's ground war reached into the raid window, which is the effect D32 describes, one step further. Changing the binding gate's fixture to admit it would be moving the goalposts. The frame chosen here passes `aiairgate` and `cheesegate` and has the economic reason above. Theory disproved, so it is not retried: that the failure was the commander's air answer slowing (its last flyer fell 144 ticks later, but the harvesters died to the flyers because they arrived damaged).

**Fixing the opening hand in this row.** The yard anchor and the opening force are the first break (cause 1) and the closest thing to "the layout" outside the scan. Rejected for scope and measured insufficient: `PlaceSkirmishStart` builds every skirmish's opening, the player's own included, in the client, the balance tool and some twenty runner gates, and with it this ADR's frame read 8 of 16 pairs within 15 per cent and a start split of 5/8, no nearer F5. It belongs with causes 2 to 4 in a row of its own.

**Orienting by the seat's home yard rather than by each anchor.** One frame per commander, taken from its first yard. It is just as much a rotation (the two commanders' frames are each other's reflection), and it differs only for an anchor across a centre line from its commander's home: per anchor, such an expansion faces the centre from where it stands; per seat, it keeps the home base's frame. Rejected because "home" needs a rule of its own (which yard, and what happens when that yard dies or a second base outlives it), while an anchor's own position needs none. Not measured.

**Ordering the candidates by distance from the map centre.** A rotation-invariant key needs no reflection, but equal distances are common on a grid and their tie-break needs a canonical frame anyway, and it changes the base's shape rather than only its orientation. Not measured.

**Binding `seatfairgate` on its start clause alone.** F5 is one criterion with two clauses, and the start clause is the chaotic one: it is not met after this ADR either.

**Holding the scan back until the residue rows land.** D5 is taken, and the experiment above shows the oriented scan is what lifts income parity from 9 to 13 or 14 pairs once the starts are symmetric; landing it first lets each of the residue rows be judged against a commander that already lays out its base as a rotation.

## Other measurements, before and after

- `aiairgate` (binding, in `match`): **PASS** before and after. The raid answered with 3, 3, 4 and 4 harvesters alive before and 2, 3, 4 and 4 after (Normal then Hard, Directorate then Sodality). D32's no-raid control, harvesters alive at the window's end: 5, 5, 7 and 0 before; after, 3 (at t=7186, where seat 0 won), 0 (lost 8), 8 and 0 (at t=9031, where seat 1 won). The Normal and Hard Sodality cells end with no harvester, so D32's reversal condition is still not met.
- `cheesegate` (binding, in `match`): **PASS**, 10 of 10 before and after.
- `endgate` (non-binding until P8-24): matches reaching 27000 with no result **4 of 72 before, 10 of 72 after** (skirmish-03 DD both orientations, skirmish-04 DD both, DS o1 and SD o0, skirmish-07 DD both, skirmish-08 DD both). A cost of this ADR that P8-24's stalemate rule has to absorb.
- `ladderprobe` (360 matches to 18000 ticks): seat 0 took **133 of 204 decided unique matches (65 per cent) before and 134 of 237 (57 per cent) after**; undecided 120 to 87 of 324. The Directorate mirror 21-9 to 22-7, the Sodality mirror 38-14 to 32-29. Hard over Normal 23-23 to 28-21, Brutal over Normal 28-18 to 31-21, Brutal over Easy 24-15 to 39-18, Normal over Easy 31-17 to 29-25.
- `pillargate --bind` (ADR-073's carried condition C8), before: "pillargate (F8 rate): PASS (binding). At most 3.6 launches per seat per 30 minutes (bar 5)." and "pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 66 of 72 matches launched (at least half)." After: **"pillargate (F8 rate): PASS (binding). At most 3.5 launches per seat per 30 minutes (bar 5)."** and **"pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 44 of 72 matches launched (at least half)."** The launching share fell because more matches are over sooner: all 28 that did not launch had ended before t=11536, the earliest first launch D33 allows, and the half that must launch still does.

## What reverses it

- **D5's own condition: `seatfairgate` passes with the old scan restored.** Tested: with the old scan (d9505b9) it is WOULD-FAIL on both clauses, income within 15 per cent in 20 of 32 and a start split of 20/8. Re-test after each residue row lands, by reverting this ADR's hunk in `TryFindPlacement` and running `seatfairgate`.
- **The frame** reverses if, once causes 1 to 4 have rows that land, the sheltered frame passes `aiairgate` and `cheesegate` and reads better on `seatfairgate` than this one: then the canonical frame flips by changing the two comparisons, and the four goldens move again.
- **The tie** (a structure centred on the map's centre line) reverses if a map is ever made whose start or base straddles that line, when it needs a per-axis rule of its own.

## Consequences

**Easier.** Two commanders on a rotation pair build bases that are rotations of each other about their yards from the first placement, so any remaining start bias is the opening hand's or the sim's, and `mirrorprobe` names the first one. The residue now has measured causes with code locations, each a candidate row (the opening hand, the free harvester's offset, the production exit order, P8-53, the flow field's tie-break and boundary-cell ownership), and P8-53's measure-first question has its first answer.

**Harder.** The commander's layout depends on which half of the map each structure stands in, so a base that grows across a centre line lays its far buildings out in the other frame. F5 is still not met, and the start split on today's starts reads worse than before (21/3); the evidence that this is chaos rather than the frame is the table above, and it stays a claim until the residue rows land and the split is re-measured. `endgate` has six more unresolved matches for P8-24 to resolve.

**Committed to.** The canonical frame is part of the commander's behaviour, so changing it moves the four commander goldens. A mixed-build LAN game with a commander seat across this ADR desyncs within 30 ticks of the first reflected placement rather than being refused, which is Q024's question.
