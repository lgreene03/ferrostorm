# ADR-076: the start is a true half turn: the opening hand, the free harvester and the production exit are laid out in each structure's own frame
- Status: Proposed, 2026-10-05, by the implementer agent for rows P8-62, P8-63 and P8-64 of docs/tickets/P8-formidable-tracker.md, landed as three staged commits, one cause each. Goldens move in every stage, so it needs the Architect's sign-off before it merges (CLAUDE.md: a golden change is a replay-compatibility break).
- Date: 2026-10-05
- Deciders: Architect agent + Luke
- GDD/TDD feature served: doc 26 section 2 ("rotational symmetry for fairness": the half turn about the map centre maps each start onto the other); ADR-011 (the skirmish opening hand); TDD s1 ("determinism is the product") and s9's replay compatibility promise; criterion F5 and decisions D5 and D36 of the P8 tracker; ADR-075's causes 1, 2 and 4 and its Architect condition C7

## Context

ADR-075 oriented the commander's placement scan to the map centre and bisected what still kept a Normal mirror from being its own half turn, using `mirrorprobe`, which compares seat 0's half of the world with the rotation of seat 1's after every step and prints the first tick on which they differ. It found four causes before the flow field's tie-breaks, each a rule of the sim that a human plays under as much as the commander does, and filed them as rows: P8-62 (cause 1, the opening hand), P8-63 (cause 2, the free harvester), P8-53 (cause 3, the flooring multiplication) and P8-64 (cause 4, the production exit). Its experiment, `docs/adr/ADR-075-residue-experiment.patch`, neutralised causes 1 to 4 together and brought income parity on `seatfairgate`'s sweep to 13 of 16 pairs in the frame it shipped.

This ADR lands causes 1, 2 and 4 (P8-53 is left to its own row and its own ADR), in that order, one staged commit each, measuring `mirrorprobe`, `seatfairgate` and the other gates after each so that every golden move and every change in the readings has one cause.

All three rules share one convention, chosen so that each is the smallest symmetric rule that keeps the layout a base in the top-left quadrant has always had: **an axis on which the structure (or the start cell) stands short of the map's centre keeps the authored layout; an axis on which it stands past the centre gets that layout reflected.** A structure exactly on a centre line is its own partner on that axis and keeps the authored layout, the one tie. Because the rule reads only a position and the map's size, any two positions related by a reflection in a centre line, or by both (the half turn), get layouts related the same way. The commander's scan (ADR-075) states its canonical frame the other way round (reflected when short of the centre), which is the same symmetry with the other tie convention; a tie is its own partner either way, so the two agree on every pair that is not a tie.

## Decision

### 1. The opening hand is a true half turn (P8-62)

`MapData.PlaceSkirmishStart` used to anchor each 2x2 Construction Yard at the map's `start` cell, so the yard always grew right and down from it. tools/mapgen.py rotates a start as one cell, so on every two-seat map the yard of the start past the centre stood one cell off the exact rotation of its partner's on both axes ((+1,+1) on all eight maps). The opening force was laid out towards the centre left to right only: the harvester always two cells below the start and the three rifle squads always two cells above it.

Now the whole hand is laid out in the start's own frame, per axis. On an axis where the start cell stands short of the centre the layout is as authored: the yard grows from the start cell towards the centre, the harvester stands towards the centre and the squads away from it. On an axis where the start cell stands past the centre the same layout is reflected about the start cell, so the yard covers the start cell and grows towards the centre (anchored `footprint - 1` cells back, read from the yard's registered footprint rather than a literal), the harvester stands two cells towards the centre vertically and the squads two cells away from it. Spawn order (every treasury, then every yard, then every force) and every entity count are unchanged.

**Four-seat maps.** The rule is a property of each start's position, not of a seat number. skirmish-09's four corners, and test-4seat's, are one orbit of the centre-line reflections, so their four hands are exact images of one another: the half turn for the opposite corner and a reflection for each neighbour. What the rule cannot express is a quarter turn alone: starts related only by a 90-degree rotation, such as the middles of a square map's four edges, would get hands that are reflections rather than rotations. No map ships one; a map that does needs a rule of its own. A start cell on a centre line (an odd dimension) keeps the authored layout on that axis; every shipped map has even dimensions.

**What a player notices.** On the five maps whose start 0 is top left (skirmish-01, 03, 04, 05 and 06), start 0's hand is exactly as before. On the start in the opposite corner, the yard now stands one cell up and one cell left of where it did, its harvester begins above the yard rather than below it, and its three squads below it rather than above. On skirmish-02, 07 and 08, whose start 0 is bottom left, start 0's yard moves one cell up, with its harvester above and its squads below, and start 1's yard (top right) moves one cell left. In every case the yard covers the map's start cell and the harvester begins on the side facing the centre.

## Golden hashes, stage by stage

Each stage was measured at seed 2026 with `golden 2026` and diffed in order against the file it started from; only the rows that moved were regenerated, so each move has one cause. The catalogue checksum is 0x1255012DCF3D3A68 before and after every stage (no /data value is added or changed), and the save format is untouched (no field, version or wire change).

**Stage 1, P8-62.** One golden moves, and 24 are byte-identical:

- `skirmish` **0x85DB0AE7C03D779C to 0x3BA3C94AB85DEAFC**. It is the only golden built by `PlaceSkirmishStart` (skirmish-01, two commanders): start 1's yard, harvester and squads move to the exact rotation of start 0's. Its report line goes from 37 entities destroyed and treasuries 16617/7221 at t=5000 to 36 destroyed and 16564/15600.
- The isolating measurement: with `sim/Ferrostorm.Sim/MapLoader.cs` restored to main and nothing else changed, `golden 2026` gives all 25 of main's hashes (an empty ordered diff).

Two absolute pins in the battery that fold an opening hand were re-pinned, each with a note naming this row: `multiseatgate`'s two-player placement pair on skirmish-01 (0x9D3E3D666AE5E693 and 0xAF41FAAB56DE8325 to 0x5D8A10A22C7FA993 and 0x28B6EF3DF9B49E9E) and `lanaiseatsgate`'s no-commander control on test-4seat (0x468099A1430B53FD to 0x1377C844B399DD6D). `longmatchperf`'s flow-field proxy budget belongs to the matches as they play and was re-measured in the same commit: 269005, 44857 and 155592 to 268451, 44232 and 120654 (skirmish-07, 08 and 09).

**The gate that proves it.** `mapgate` (in `match` and CI) now builds every committed map with every seat it declares and asserts, for each start that is the image of start 0 under the centre-line reflections, that every entity of its hand has a twin of the same kind at exactly the reflected position, compared as raw fixed-point values and read off the two start cells, never off the rule. 14 hands are compared (eight two-seat maps, three on skirmish-09, three on test-4seat). With main's `MapLoader.cs` it fails on the first map: `skirmish-01's start 1 at (86,54) is the half turn of start 0 at (9,9), but its opening hand is not`, the yard at (9,9) against (10,10) in start 0's frame and the harvester and squads mirrored on one axis only.

## What each stage did to F5, `mirrorprobe` and the other gates

F5 asks, over Normal mirrors of both factions on every two-seat map with the starts swapped (32 matches), for each seat's income within 15 per cent of the other's in every match and a pooled win split no worse than 60/40. "Pairs" below are the 16 map and faction pairs counted once each (the swapped match is nearly always its exact relabelling); the mean income gap over them is ADR-075's tie-break measure, a separate signal and not an F5 clause.

**Before (main, 95f1f3a).** `mirrorprobe`: first break at t=1 on every map, the opening hand. `seatfairgate`: income within 15 per cent in 18 of 32 matches; pooled split by start 21/3 of 24 decided (by seat 11/13); pairs within 15 per cent 9 of 16, mean income gap 16.8 per cent, decided pairs by start 10/2; the swapped match is the exact relabelling in 14 of 16 pairs. These reproduce ADR-075's figures exactly.

**After stage 1 (P8-62).** `mirrorprobe`: the first break is past t=1 on every two-seat map, at t=526 in every Directorate mirror and t=406 in every Sodality mirror, the free harvester (cause 2), with no command difference before it. `seatfairgate`: income within 15 per cent in **16 of 32**; pooled split by start **9/17** of 26 decided (by seat 14/12); pairs 8 of 16, mean gap 17.3 per cent, decided pairs by start 5/8; exact relabelling in 15 of 16 pairs. WOULD-FAIL on both clauses. These reproduce ADR-075's row "this ADR, plus the exact opening hand" exactly (8, 17.3, 5/8), which is evidence that this stage is that experiment's part 1 and nothing else.

Per map, the income gap (positive when start 0 earned more) and the start that won, before then after stage 1:

| Map | Directorate mirror | Sodality mirror |
|-----|--------------------|-----------------|
| skirmish-01 | +101, start 0; -31, start 1 | +18, start 0; -51, start 1 |
| skirmish-02 | +10, start 0; +5, start 0 | +22, start 0; -41, start 1 |
| skirmish-03 | +5, no result; +3, start 1 | +20, start 0; +34, start 1 |
| skirmish-04 | +2, no result; -24, no result | -21, start 1; -23, start 1 |
| skirmish-05 | +10, start 0; +4, start 1 | -8, start 1; +0, start 0 |
| skirmish-06 | +4, start 0; +4, start 0 | +1, start 0; -8, start 1 |
| skirmish-07 | +0, no result; +2, no result | +23, start 0; +16, start 0 |
| skirmish-08 | -4, no result; +8, no result | +20, start 0; +23, start 0 |

**ADR-075's two re-tests at stage 1** (each row's done condition). With ADR-075's `TryFindPlacement` hunk reverted (the old scan): income within 15 per cent in 19 of 32, start split 20/6, pairs 9 of 16, mean gap 12.9 per cent. The sheltered frame (ADR-075's two comparisons reversed): `seatfairgate` 21 of 32, start split 20/6, pairs 10 of 16, mean gap 14.0 per cent; `aiairgate` PASS; `cheesegate` **FAIL** (the harvester raid by four Vanguard Cars against the Normal Sodality is not answered: no harvester alive when the last raider fell at t=5132).

**The other gates at stage 1**, before then after:

- `aiairgate` (binding): PASS both; harvesters alive when the last raider fell 2, 3, 4, 4 then 3, 3, 4, 4 (Normal then Hard, Directorate then Sodality). D32's no-raid control, harvesters alive at the window's end, 3, 0, 8, 0 then 1, 0, 3, 8.
- `cheesegate` (binding): PASS, 10 of 10 both.
- `endgate` (non-binding until P8-24): 10 then 14 of 72 matches reach 27000 ticks with no result.
- `ladderprobe`: seat 0 took 134 of 237 decided unique matches (57 per cent) then 135 of 253 (53 per cent); undecided 87 then 71 of 324.
- `pillargate --bind` (ADR-073's carried C8), after stage 1: "pillargate (F8 rate): PASS (binding). At most 3.2 launches per seat per 30 minutes (bar 5)." and "pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 38 of 72 matches launched (at least half)." The launch margin narrows from 44 to 38 of 72 against a bar of 36.

## Compatibility

Every clause of this ADR changes a rule of the sim, not only what a commander chooses, so it is the class Q024 (docs/questions/Q024-a-sim-rules-epoch-for-replays-and-the-lan-hello.md) asks about: a code-only change that neither the replay header nor the LAN hello can see, because no catalogue value moves.

**Old replays diverge.** A replay records the command stream and re-simulates it from the opening hand. Stage 1 alone rebuilds that opening differently on every map (start 1's yard and force on the top-left maps, both starts' on skirmish-02, 07 and 08), so a replay recorded before this ADR replays a different match from tick 1 and ends REPLAY DIVERGED with both hashes; it is not refused up front, because its catalogue line matches.

**Saves load and continue.** A save holds the world as it stood, opening hand and all, and no format, version or field changes, so a save written before this ADR loads into this build unchanged; the match then continues under the new rules (for later stages, a refinery bought or a unit produced after the load follows them). Nothing compares the continuation with what the old build would have played.

**A mixed-build LAN game desyncs and is not refused up front.** Both peers build the opening hand themselves from the host's setup, so two builds either side of stage 1 disagree from the first tick on any map where a start stands past the centre (every shipped map); the hello compares only the catalogue checksum, which does not move, so the pair is not refused, and the relay's state-hash comparison every 30 ticks flags the desync within two seconds. Builds from before ADR-073 are refused by its catalogue move, as before.

## Alternatives rejected

**Anchoring the yard so that start 0 never moves.** The yard of every start could keep growing right and down, with only the start past the centre moved, or start 1 alone could be adjusted. Rejected because it is a rule about seat numbers or about one map family rather than a property of a position: on skirmish-02, 07 and 08 start 0 is bottom left, so a start-0-is-fixed rule would leave those maps a cell off, and on a four-seat map it has no answer for seats 2 and 3. The rule taken moves start 0 on those three maps, which is the price of being right on all of them.

**Mirroring the opening force in y but leaving the yard where it was.** The opening force is the visible half of cause 1 and the yard the invisible half. Rejected because the yard's one-cell offset is what put start 1 a cell further from its own ferrite on five maps (ADR-075's per-map record), and the probe's first break stays at t=1 while either half is asymmetric.

**Moving the start cells in the map files instead.** Rewriting each map's `start` line (or tools/mapgen.py's rotation of it) so that the anchor of start 1 is the rotation of start 0's footprint. Rejected because it fixes the data for a 2x2 yard only, so a yard of another size breaks it again, it needs all nine maps regenerated and re-proved by mapgen, and the opening force would still be mirrored on one axis. The start cell is the right datum; the footprint is what needs a frame.

## What reverses it

- **Clause 1 (the opening hand)** reverses if a map is made whose starts are related only by a quarter turn, when it needs a rule of its own (a rotation-aware layout), or if `mapgate`'s half-turn stage ever needs an exception for a shipped map, which would mean the rule no longer describes the maps.

## Consequences

**Easier.** The first break `mirrorprobe` finds is no longer the setup: on every two-seat map the two halves start as exact rotations, so the first difference is now a rule of play, and the next row can be judged against a symmetric opening.

**Harder.** Every replay recorded before this ADR diverges, and a mixed-build LAN pair desyncs rather than being refused (Q024). F5 is still not met after stage 1, and both clauses read worse than before on this build: income within 15 per cent 18 to 16 of 32, and the start split 21/3 to 9/17, now favouring start 1.

**Committed to.** The opening hand is part of the sim's rules, laid out in each start's own frame; a change to it moves the `skirmish` golden and `mapgate`'s half-turn stage names the map it breaks.
