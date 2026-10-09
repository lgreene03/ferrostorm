# ADR-077: Fix64 multiplication truncates toward zero, so a negative product is the exact negation of its positive twin
- Status: **Ratified under D38** of docs/tickets/P8-formidable-tracker.md (row P8-53), 2026-10-09, by the orchestrator under the owner's standing authority of 2026-10-02, after the Architect's sign-off with conditions of 2026-10-08 (section "Architect sign-off" below), in D38's pull request 169 (branch `claude/p8-53-trunc-shelter-on`). Proposed on 2026-10-08 on the measured matrix and its independent check. It moves goldens, which is a replay-compatibility break needing an ADR and the Architect (CLAUDE.md). It lands as stage 2 of the four staged commits D38 takes in that one pull request: stage 1 flips ADR-075's frame (its amendment of 2026-10-08), this stage changes the multiplication, stage 3 re-lands ADR-076 clause 3 (P8-64), and stage 4 amends that clause so the exit search has no blind spot (the Architect's condition C1). Conditions C1 to C5 are met in that pull request and D38 is read again at its final head (section "At the final head: D38 read again"); C6 is its CI and merge hygiene. Drafted by the implementer agent.
- Date: 2026-10-08
- Deciders: Architect agent + Luke
- GDD/TDD feature served: TDD s1 ("determinism is the product") and s9's replay compatibility promise; ADR-002 (the fixed-point library, whose multiply this is); doc 26 section 2 ("rotational symmetry for fairness"); criterion F5 and decisions D5, D36, D37 and D38 of the P8 tracker; ADR-075's cause 3 and its C7 frame clause

## Context

`Fix64` is a Q32.32 number on a signed 64-bit raw value (ADR-002). Its multiplication took the exact Int128 product of the two raw values and shifted it right by 32 bits. An arithmetic shift right is a FLOOR, so a negative product was rounded one unit in the last place (2^-32) further from zero than its positive twin: `(-x)*y` was not `-(x*y)`. Division, by contrast, uses Int128 `/`, which truncates toward zero and so is symmetric. ADR-002 fixes the representation and the intermediates, not a rounding mode.

That one-unit asymmetry matters wherever two computations are meant to be mirror images. P8-14 found it when the balance tool's left-right mirror self-check failed until the arena was mirrored across y = x, and ADR-075's bisection of the seat residue found it as cause 3: two mirrored harvesters on skirmish-01 stood at x=11.4057 and 11.4058 in seat 0's frame at t=711, a difference that exists from tick 2 and becomes visible at the probe's four decimals much later. Row P8-53's rule was to measure first and to fix only if a fix moves the seat split, and only under its own ADR.

The measurement was made on PR 166's head (816fc3b, whose tree is main's 5f01ce6) as an 18-cell matrix at seed 2026: three multiplications (flooring as on main, truncating toward zero, rounding half away from zero), three placement frames (ADR-075's centre-facing frame, the sheltered frame, and the old scan from before P8-21) and P8-64 off or on. An independent check rebuilt eight of the cells in private worktrees and reproduced every deterministic figure, ran the CI battery on three and the client harness twice, and corrected two claims of the measurement's prose (recorded below). Decision D38 takes the one cell that met every bar.

## Decision

**Multiplication truncates toward zero.** `Fix64.operator *` keeps the exact Int128 product `p = a.Raw * b.Raw` and returns `p >> 32` when `p >= 0` and `-((-p) >> 32)` when `p < 0`. It is integer arithmetic only. `|a.Raw * b.Raw|` is at most 2^126, so `-p` cannot overflow Int128, and no project builds in a checked context. `operator *` is the only raw multiplication in `sim/Ferrostorm.Sim` (checked by grep, with a positive control), so the rule is in one place.

What it changes: for every product at or above zero, nothing (the shift is the same); for a negative product, the result is one unit in the last place nearer zero whenever the shifted-out bits are not all zero. So `(-x)*y == -(x*y)` and `x*(-y) == -(x*y)` hold exactly, `x*y == y*x` holds as before, and the multiplication now rounds the way division always has.

It lands as stage 2 of D38's configuration, after ADR-075's frame flip and before ADR-076 clause 3, and it is judged with them, because the matrix shows that neither the rounding nor the frame can be chosen alone (section "The coupling").

## What the matrix measured

F5 asks, over Normal mirrors of both factions on every two-seat map with the starts swapped (32 matches), for each seat's income within 15 per cent of the other's in every match and a pooled win split by start no worse than 60/40. The mean income gap is over all 32 matches (ADR-076's Architect condition C3). `pillargate` counts the matches of 72 that launch a superweapon (the bar is 36, D33). Every figure is one deterministic sample: the world seed drives no gameplay, so no resample exists.

| Multiplication, frame, P8-64 | Within 15 per cent | Mean gap | Start split | `pillargate` launching | `mission` |
|------------------------------|--------------------|----------|-------------|------------------------|-----------|
| floor, centre, off (main) | 22 of 32 | 14.77 | 10/14 | 42 | passes |
| truncate, centre, off | 24 | 13.06 | 12/18 | 43 | THROWS |
| half away, centre, off | 24 | 10.33 | 15/11 | 40 | THROWS |
| floor, sheltered, off (stage 1) | 20 | 16.07 | 12/4, outside | 66 | passes |
| truncate, sheltered, off (stage 2) | 24 | 10.38 | 12/6, outside | 60 | passes |
| half away, sheltered, off | 24 | 11.24 | 16/7, outside | 62 | passes |
| floor, centre, on (ADR-076's held stage 3) | 26 | 8.04 | 16/12 | 32, FAIL | passes |
| truncate, centre, on | 26 | 10.78 | 15/11 | 40 | THROWS |
| half away, centre, on | 28 | 6.68 | 13/12 | 27, FAIL | THROWS |
| floor, sheltered, on | 26 | 8.03 | 6/15, outside | 58 | passes |
| **truncate, sheltered, on (D38, the merged head)** | **28** | **9.06** | **12/10** | **60** | passes |
| half away, sheltered, on | 24 | 10.09 | 4/15, outside | 62 | passes |
| truncate, old scan, off | 20 | 17.97 | 12/10 | 56 | passes |
| truncate, old scan, on | 18 | 14.95 | 16/10, outside | 58 | passes |

`aiairgate` and `cheesegate` pass in every cell listed. "THROWS" is a binding failure: the `mission` scenario's own assertion throws ("mission: the ambush zone was never sprung"), so `golden`, `determinism` and `match` go red. The configuration D38 takes is the only cell with 28 of 32, the start split inside 60/40 and every binding gate green.

**Why truncation and not round-half-away.** Both are symmetric. Truncation scored at least as many matches within 15 per cent as flooring in each of the four oriented settings (centre and sheltered, P8-64 off and on: +2, +4, 0 and +2; 102 against 94 of 128); with the old scan it scored fewer (20 against 22 without P8-64, 18 against 22 with it), so over all six measured settings it is 140 against 138 of 192, and the claim is about the oriented scan only. Round-half-away's best count (28, centre frame with P8-64) fails `pillargate`'s first-launch half at 27 of 72 and throws in `mission`, and in the sheltered frame with P8-64 its start split falls to 4/15. Truncation also matches the convention division already uses, and because it equals flooring for every product at or above zero it moves 13 of 25 goldens where round-half-away moves 18 or 19.

**The robust result is `mirrorprobe`'s, not the count's.** The F5 gain from flooring to truncation in D38's configuration is two matches, one pair, in one deterministic sample (floor-shelter-on reads 26 of 32 against 28), chosen as the best of an 18-cell matrix; the start split is a chaotic readout (it ranges from 4/15 to 16/7 across these builds). What does not depend on chaos: with flooring, P8-53's one-unit multiplication split is `mirrorprobe`'s first break on 2, 7, 4 and 15 of 16 mirrors in the four oriented settings; with truncation it is the first break on none of them, in any setting, and with round-half-away on none either. At the merged head the first breaks are boundary-cell ownership on eight mirrors, the flow field's tie-break on six and the sub-cell step on two (ADR-075's class 5), and neither a production exit nor a multiplication split anywhere.

## The coupling, stated plainly

**The green `mission` golden depends on the sheltered frame.** In the centre-facing frame, both symmetric multiplications make the `mission` scenario throw. Traced in the measurement and reproduced by the check: mission-01's only harvester freezes from t<=1000 at (10.203015, 22.796984), reporting Moving and ToField towards (14.5, 20.5). That point lies on the diagonal through an idle opening squad standing at (10.5, 22.5) (to six decimals), so the harvester's step towards its target and the separation push away from the squad cancel exactly, every tick. Credits hold at 1900 from t=1500 to t=9000 and the mission is never won. The flooring multiplication's asymmetry had been breaking that exact tie by accident, one unit in the last place at a time. The sheltered frame lays mission-01's base out differently and never reaches that configuration, which is the only reason `mission` passes in D38's configuration.

So the rounding choice is coupled to ADR-075's frame: with this ADR landed, ADR-075's frame clause cannot be reversed back to centre-facing without the mission fixture failing, and ADR-075's amendment of 2026-10-08 records the same coupling from its side. The defect underneath is movement's, not the multiplication's, and it is filed as **row P8-66**: movement needs a deterministic tie-break when a step and a separation push cancel exactly, so that the sim stops relying on rounding asymmetry to break such ties. It is an existing class, not one this ADR creates. Walkers that report Moving but stay put for 450 or more ticks occur in flooring builds too: 4 to 14 harvesters and 10 to 42 units per 72-match `pillargate` sweep, the longest 14834 ticks at PR 166's head. Truncation does not lower that count everywhere: at stage 2 (the sheltered frame, P8-64 off) it raised it from 26 to 51 units and from 10 to 12 harvesters, against flooring in the same frame; over the four oriented settings in aggregate it is 97 units and 24 harvesters against flooring's 114 and 34. At the final head it reads 14 units and 6 harvesters, against 10 and 12 with flooring alone at that head (section "At the final head: D38 read again", the Architect's C4). ADR-014's no-progress backstop does not reach a harvester reporting Moving in ToField, which is why nothing self-corrects.

## Golden hashes, stage 2

Measured at seed 2026 with `golden 2026`, diffed in order against stage 1's file (ADR-075's amendment). **Thirteen move and twelve are byte-identical.** One cause moves all thirteen: a negative product somewhere in the scenario now rounds one unit in the last place nearer zero, and the scenario's state hash folds the positions (or anything downstream of them) it touches. The twelve that do not move (production, stealth, veterancy, victory, artillery, superweapon, crush, veil, capture, mission03, depot, walls) compute no negative product whose low bits are not zero, or none that reaches their hashed state. The catalogue checksum (0x1255012DCF3D3A68) does not move: no /data value changes.

- `movement` **0x4A89345B63E65BFC to 0x2E7A3BF6119F631B**
- `pathing` **0xD326420AAB33934F to 0xA82464A3EA43C9C7**
- `economy` **0xE370F4976CB58BA0 to 0xE155C66883BBED48**
- `combat` **0x3B781BC0FB9C05AC to 0xC3820F155FDBF637**
- `attackmove` **0x4F3627D659BA4AD8 to 0x9714A5CE442E610D**
- `construction` **0x7C7527C2CF6CFE53 to 0x7C02B6A7D63B5A26**
- `skirmish` **0x77CAA01CB04BD1A1 to 0xB4923CF063897591**
- `expansion` **0xC16CE00AD483FC3E to 0xF63BE246FF03034F**
- `aisuper` **0x943D5348DB2CD716 to 0x6E243A5771AD2204**
- `waypoints` **0x37FE989BE6AF7DFC to 0x883A99A916C334B4**
- `mission` **0xB6B1A68A15B4FCBB to 0x20E040A2BA041007**
- `mission02` **0x7A6D404998519EED to 0xF16548B1A4B48DFF**
- `airanswer` **0x156FD74602D13215 to 0xE8611D74C7680CF7**

Report lines (`match 2026`), stage 1 then stage 2: `skirmish` from 37 entities destroyed and treasuries 14227/13801 to 34 destroyed and 13383/15211; `mission` from victory at t=3451 to victory at t=3291; `pathing` from every unit settled by t=363 to every unit settled by t=327. Every other report line reads as it did, `airanswer`'s included (the first flyer down at t=683, 3 of 3 down, every assertion ADR-072 gave it passing), so the other ten moves are in positions and timings the report lines do not print.

**The isolating measurement.** With `sim/Ferrostorm.Sim/Fix64.cs` restored to stage 1 and nothing else changed, `golden 2026` gives all 25 of stage 1's hashes, because it is the stage's only sim file. The same restoration fails `selftest` by name ("Fix64 mul: a negative product truncates toward zero (ADR-077)"), the assertion this stage adds: one unit in the last place times a half is -2^31 before the shift, which a floor rounds to -1 and truncation to 0, and negating either factor of a product whose low bits are not zero (7/3 times -5/11) must negate the product exactly.

**A pin re-pinned.** `lanaiseatsgate`'s no-commander control on test-4seat is an absolute hash of 400 ticks, so any rule change moves it: 0x1377C844B399DD6D to **0x8CE212EBF6C1FBFB**, with a note naming this row (the same class ADR-036 and P8-62 re-pinned). It was the battery's only failure before the re-pin, and once re-pinned every later stage of that gate passes (the commanded run agrees across peers, and the divergent commander is caught). Stage 1 left the pin alone (no commander plays there) and stage 3 leaves it alone too.

**The proxy budget re-measured.** `longmatchperf rebaseline=1`: 268174, 43896 and 138439 to 313102, 54855 and 155297 (skirmish-07, 08 and 09). The proxy is not binding (P8-31), so the figures are recorded rather than judged.

## What stage 2 did to F5 and the other gates

Measured at stage 2 (the sheltered frame and truncation, P8-64 not yet in), reproducing the matrix's "truncate, sheltered, off" cell exactly:

- `seatfairgate` (non-binding until F5 is met): income within 15 per cent in **24 of 32** matches (stage 1: 20), over the bar on skirmish-01 DD and SS, 04 SS and 07 SS, both orientations each; mean gap over all 32 **10.38 per cent** (o0 10.38, o1 10.38; stage 1: 16.07); win split by start **12/6 of 18 decided, outside 60/40** (stage 1: 12/4), by seat 9/9; Directorate mirrors by start 4/4, Sodality 8/2. So this stage alone fails F5's split clause, which is one reason it never lands without stage 3.
- `pillargate --bind` (ADR-073's carried C8): "pillargate (F8 rate): PASS (binding). At most 3.5 launches per seat per 30 minutes (bar 5)." and "pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 60 of 72 matches launched (at least half)."
- `aiairgate` (binding) PASS: the raid answered with 2, 1, 4 and 4 harvesters alive when the last flyer fell (Normal then Hard, Directorate then Sodality); D32's no-raid control 2, 0, 8 and 0, so D32's reversal is still not met. `cheesegate` (binding) PASS, 10 of 10.
- `endgate` (non-binding until P8-24): 19 of 72 unresolved (stage 1: 22).
- `mirrorprobe`: P8-53's one-unit multiplication split is a first break on **no** mirror (stage 1: seven of sixteen). The first breaks are the production exit on fifteen mirrors (t=730 to 2490) and the flow field's tie-break on skirmish-01 SS (t=1875).
- Walkers that report Moving but stay put for 450 or more ticks, over `pillargate`'s 72 matches (the measurement-only counter of P8-53's matrix, reproduced by its independent check): 12 harvesters and 51 units at this stage, against 10 and 26 at stage 1. That is the rise P8-66 is filed against.
- `match 2026`, `determinism 2026`, the CI battery and the balance gate: see "Verification" below.

## At the merged head (all three stages)

The matrix's "truncate, sheltered, on" cell, which the independent check reproduced exactly: income within 15 per cent in **28 of 32** matches, over the bar only on the Sodality mirrors of skirmish-01 and 03 (both orientations each); mean gap over all 32 **9.06 per cent** (o0 9.06, o1 9.06); win split by start **12/10 of 22 decided, inside 60/40**, by seat 11/11 (Directorate mirrors by start 0/6, Sodality 12/4); `pillargate --bind` both halves PASS with 60 of 72 matches launching, rate at most 3.3; `aiairgate`, `cheesegate` and the balance gate PASS; `endgate` 14 of 72 unresolved; `mirrorprobe` finds neither a multiplication split nor a production exit as a first break on any mirror. Re-measured at this pull request's head (stage 3), every one of those figures reproduces exactly, and `golden 2026` gives the cell's 25 hashes. `pillargate --bind` at the head (ADR-073's carried C8): "pillargate (F8 rate): PASS (binding). At most 3.3 launches per seat per 30 minutes (bar 5)." and "pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 60 of 72 matches launched (at least half)."

**This configuration rests on ADR-076 clause 3, whose own reversal reads met at the head.** The measurement C7 asked for, now printed by `pillarprobe`, finds one game on skirmish-08's Directorate mirror in which both commanders' barracks are held at 100 per cent with every own-frame exit cell blocked where the authored order would have released them (6144 producer ticks, against 0 with clause 3 reverted and 0 on main). ADR-076 ("Clause 3 re-landed (D38)") traces it to `SpawnOffsets`' blind spots about an even footprint and records the decision owed before merge. Without clause 3, this pull request's configuration is stage 2's, whose start split (12/6) is outside 60/40, so the choice of truncation in D38 is taken with clause 3 and would be judged again without it. **Resolved in the same pull request** (the Architect's condition C1): a fourth staged commit amends clause 3 so that the exit search has no blind spot, moving no golden, and at the final head the held count reads 0 (section "At the final head: D38 read again").

**F11 gets worse, and that is a measured input, not an improvement.** The balance gate (`tools/Ferrostorm.Balance`) passes, and its faction war reads F11 NOT MET at **Directorate 6/6 against Sodality 0/6** of (map x seat) cells, against 5/6 and 1/6 on main. It already reads 6/6 and 0/6 after stage 1 alone (measured: "F11 NOT MET: Directorate 6/6 (100%), Sodality 0/6 (0%) of cells"), and 4/6 against 1/6 at stage 2, so the faction war moves with every stage of this pull request and is a chaotic readout at this sample, like the start split. F11 binds only when P8-33 flips its switch, so nothing goes red, and the figure is P8-33's input for its balance pass. The measurement's report once had this direction inverted; the check found it in the measurement's own raw files, and it is recorded here as the check found it.

**F5 is not met.** 28 of 32 is not every match: skirmish-01 and skirmish-03 Sodality mirrors keep an income gap over 15 per cent in both orientations. `MeasurementHarness.SeatFairGateBinding` stays false.

## At the final head: D38 read again (the Architect's condition C3)

The Architect's sign-off (below) required D38 to be read again at the pull request's final head, which adds to the three stages above a fourth staged commit (ADR-076 clause 3 amended so that the exit search has no blind spot, condition C1, moving no golden) and the goldens-neutral changes of condition C5. Every figure is at seed 2026 on macOS at that head. Every figure but `pillarprobe`'s equals stage 3's, because the fourth commit changes play only where a producer has all eleven exit cells blocked: no golden reaches that, and on the head's sweeps only skirmish-08's Directorate mirror does, from t=8840, too late to move its income to t=9000 or its match line. D38 stands only if all five of the Architect's readings hold, and all five do.

- **(a) Every binding gate is green.** `pillargate --bind`: "pillargate (F8 rate): PASS (binding). At most 3.3 launches per seat per 30 minutes (bar 5)." and "pillargate (F8 first launch): PASS (binding). Median first launch 11536 (band 10800 to 14400), 60 of 72 matches launched (at least half)." `aiairgate` PASS (3, 3, 3 and 4 harvesters alive when the last flyer fell, Normal then Hard, Directorate then Sodality; D32's no-raid control 5, 0, 8 and 8). `cheesegate` PASS, 10 of 10. `golden 2026` gives the 25 lines of `sim/golden-hashes.txt` in order, stage 3's unchanged; `determinism 2026` double-runs all 25 identically; `match 2026` exits 0, with `mission` won at t=3291 and `spawngate`'s new tenth stage and `lanaiseatsgate` (its pin 0x8CE212EBF6C1FBFB holding) inside it.
- **(b) The start split is inside 60/40.** `seatfairgate`: 12/10 of 22 decided matches by start, 11/11 by seat; income within 15 per cent in 28 of 32 matches, over the bar only on the Sodality mirrors of skirmish-01 and 03 (both orientations each); mean gap over all 32 9.06 per cent (o0 9.06, o1 9.06); the swapped match the exact relabelling in 15 of 16 pairs. Its output is byte-identical to stage 3's.
- **(c) ADR-075's C7 comparison still favours the sheltered frame**, both arms carrying truncation and the final exit search: the sheltered frame (the head) 28 of 32 and 9.06 per cent, the centre-facing frame (the head with `TryFindPlacement`'s two comparisons reversed and nothing else changed) 26 of 32 and 10.78 per cent (o0 10.82, o1 10.73). The count decides and the mean gap agrees. Stated beside it and not used: the centre-facing arm's start split is 15/11, its `pillargate --bind` passes both halves with 40 of 72 launching, and its `aiairgate` and `cheesegate` pass; its `mission` scenario throws, the coupling above.
- **(d) This ADR's first reversal is not met.** With the flooring multiplication restored alone at the head: 26 of 32 within 15 per cent (mean gap 8.03 per cent, o0 7.84, o1 8.22), start split 6/15, outside 60/40, and `pillargate --bind` passing both halves (rate at most 3.6, 58 of 72 launching). The reversal needs at least 28 of 32 with the start split inside 60/40, so it is not met on either count. Restated with the final head's figures, it is unchanged: the head reads 28 of 32 and 9.06 per cent, as stage 3 did.
- **(e) `mirrorprobe` finds neither a production exit nor a one-ulp multiplication split as a first break** on any mirror. The first breaks are boundary-cell ownership on eight mirrors, the flow field's tie-break on six and the sub-cell step on two, byte-identical to stage 3's output.

The rest of the head's readings: `pillarprobe`'s clause 3 line reads 0 held producer ticks (stage 3: 6144), and ADR-076's restated reversal reads 0 against 0 with `ProductionSystem` asking the authored order, not met; its clause 2 line reads 0 still Idle and 0 stranded with a field left on the map, and 6 counted apart with no field left anywhere (all on skirmish-03's Directorate mirror), the split the Architect's C5 asked for, which confirms the earlier diagnosis from the counter itself. `endgate` 14 of 72 unresolved (non-binding). The `longmatchperf` proxy reads 223650, 54910 and 138277, exactly stage 3's budget, so stage 4 needs no re-baseline. The balance gate: see "Verification".

**The frozen-walker figure at the final head (the Architect's condition C4).** Units and harvesters that report Moving but stay at one position for 450 or more consecutive ticks, over `pillargate`'s 72-match sweep, counted by the measurement-only counter of P8-53's matrix (seats 0 and 1, each walker counted once), applied unchanged to each build below. The counter only reads the world: its builds' 72 match lines are identical to `pillargate`'s.

| Build | Frame and exit search | Harvesters | Units | Longest |
|-------|-----------------------|------------|-------|---------|
| stage 1 (flooring) | sheltered, P8-64 not in | 10 | 26 | 7153 ticks |
| stage 2 (truncation) | sheltered, P8-64 not in | 12 | 51 | 8668 ticks |
| **the final head (truncation)** | **sheltered, clause 3 as amended** | **6** | **14** | **10814 ticks** |
| the final head with flooring alone | sheltered, clause 3 as amended | 12 | 10 | 4528 ticks |

So in the final head's own frame truncation leaves fewer frozen harvesters than flooring (6 against 12) and more frozen units (14 against 10), 20 walkers against 22 in all; against stage 2, the head reads lower on both. The longest freeze at the head is a unit on skirmish-09's Directorate mirror standing at (114, 19) from t=16186. The final head reads exactly what the matrix's "truncate, sheltered, on" cell read at stage 3, while flooring at the head differs from the matrix's "floor, sheltered, on" cell (14 harvesters) because under flooring the fourth commit does change four matches (skirmish-07's DS in one orientation and SD in the other, and skirmish-08's Directorate mirror in both), and the two harvesters fewer are skirmish-08's, one in each orientation. The class is P8-66's, and nothing here changes its done condition.

## Verification

**At stage 2.** `dotnet build sim/Ferrostorm.Sim.Runner -c Release` with 0 warnings; `golden 2026` gives the 25 lines of the stage's `sim/golden-hashes.txt` in order; `tools/ci-local.sh` green on every step (the purity, portability, seat, team-colour, sidebar and legal greps, the build, `selftest`, `determinism 2026`, the ordered golden diff, `match 2026` with `lanaiseatsgate` inside it, `lan 5`, `lanchaos 1 60 30`, `spectate`, `replay`, `saveload`, `campaignsave` and the balance gate, whose verdict is PASS). The sim purity grep finds nothing in `sim/Ferrostorm.Sim`; the change adds no type, API or call it would catch, and `Int128` was already the multiplication's intermediate.

**At the merged head (stage 3).** Each stage was rebuilt from its own commit and its `golden 2026` diffed in order against its committed file (stage 1 the matrix's "floor, sheltered, off" cell, stage 2 "truncate, sheltered, off", the head "truncate, sheltered, on", each exact), and every `seatfairgate`, `pillargate --bind`, `aiairgate`, `cheesegate`, `endgate`, `lanaiseatsgate` and balance-gate figure this ADR and ADR-075's amendment quote for stages 1 and 2 was reproduced. At the head the runner builds with 0 warnings, `golden 2026` gives the 25 lines of `sim/golden-hashes.txt` in order, `tools/ci-local.sh` is green on all 19 steps, and the game builds with 0 warnings and `tools/verify-client.sh` passes (568 ok lines, no FAIL). Windows and Linux are CI's.

**At the final head (stage 4 and the goldens-neutral conditions, 2026-10-09).** Every figure in "At the final head: D38 read again" was measured at that head, each variant (the eleven-cell search, each one-axis closure, `ProductionSystem` asking the authored order, the centre-facing frame, flooring, and the frozen-walker counter at the head, with flooring at the head, at stages 1 and 2, and at stage 3 with each multiplication, where it reads the head's 6 and 14 under truncation and the matrix cell's 14 and 10 under flooring) built in its own scratch worktree with its one mutation asserted to apply exactly once; the frozen-walker counter's match lines equal `pillargate`'s in each build that carries it. A scratch build printing every release through a missed cell prints none over `golden 2026` and ten over `pillarprobe`'s sweep, all in skirmish-08's Directorate mirror, five per orientation, the first at the step to t=8840 into (116,12), as ADR-076 records. Stage 3 rebuilt from its own commit gives the head's 25 lines and `pillarprobe`'s 6144; its `seatfairgate`, `pillargate --bind`, `aiairgate`, `cheesegate`, `endgate` and `mirrorprobe` outputs equal the head's but for elapsed times, and so do `longmatchperf`'s proxy figures. At the head the runner builds with 0 warnings, `golden 2026` gives the 25 lines of `sim/golden-hashes.txt` in order, `determinism 2026` double-runs all 25, `match 2026` exits 0, `tools/ci-local.sh` is green on every step with the balance gate's verdict PASS (F11 NOT MET at Directorate 6/6 against Sodality 0/6, unchanged), and the game builds with 0 warnings and `tools/verify-client.sh` passes (568 ok lines, no FAIL). Windows and Linux are CI's (the Architect's C6).

## Compatibility

This is a rule of the sim, not a commander's choice, and it moves no catalogue value, so it is the class Q024 (docs/questions/Q024-a-sim-rules-epoch-for-replays-and-the-lan-hello.md) asks about: a code-only change that neither the replay header nor the LAN hello can see. Q024 now names this ADR.

**Old replays diverge, and are not refused.** A replay re-simulates its command stream from the opening hand, and nearly every recorded skirmish computes a negative product within its first ticks (any unit walking up or left of where it stands), so a replay recorded before this ADR replays a different match and ends REPLAY DIVERGED with both hashes. Its catalogue line matches, so it is not refused up front.

**Saves load and continue.** No save field, version or format changes, so a save written before this ADR loads into this build unchanged and the match continues under truncation. Nothing compares the continuation with what the old build would have played.

**A mixed-build LAN game desyncs, and is not refused.** Both peers pass the hello, because it compares only the catalogue checksum, which does not move, and their states diverge from the first negative product whose low bits are not zero; the relay's state-hash comparison every 30 ticks flags the desync within two seconds. Builds from before ADR-073 are refused by its catalogue move, as before. This is acceptable only because no build has been published, and Q024's decide-by ("before the first published build") stands.

## Alternatives rejected

**Keep flooring and close P8-53 as measured-not-fixed.** The fallback if the owner's bar were a robust seat-split change rather than a single sample. Rejected because the best flooring build that is green on every binding gate reads 22 of 32 (main) or 26 with the start split failing (6/15, floor-shelter-on), and flooring's one-unit split stays `mirrorprobe`'s first break on up to 15 of 16 mirrors, so every later symmetry row would be judged through it.

**Round half away from zero.** Symmetric too, and its best count (28 of 32, centre frame with P8-64) ties this configuration's. Rejected because that cell fails `pillargate`'s first-launch half (27 of 72) and throws in `mission`, the sheltered-frame cell with P8-64 fails the start split (4/15), it moves 18 or 19 goldens rather than 13, and it matches no convention the sim already has.

**Truncation alone, in ADR-075's shipped centre-facing frame.** The smallest change. Rejected because the `mission` scenario throws (the movement freeze above), which is a binding CI failure; with P8-64 as well it still throws.

**Symmetrising only the sites that mirror** (taking the absolute value before a multiply where a computation is meant to be mirrored, for example). Rejected because there is no list of such sites: any position, velocity or distance computation can be one half of a mirror, and a per-site fix leaves every other site asymmetric and adds a rule each new site has to remember. One operator is the right place.

**Making division floor to match multiplication.** Consistent, but it moves the asymmetry rather than removing it: a flooring division is just as asymmetric under negation, so mirrored computations would still split.

## What reverses it

- **Truncation is withdrawn** if, at the merged head carrying all four of D38's changes, restoring the flooring multiplication alone scores at least as well by ADR-075's C7 measure (at least 28 of 32 matches with each seat's income within 15 per cent and, on a tie, a mean gap over all 32 no higher than 9.06 per cent) with `pillargate --bind` green and the start split inside 60/40: then the replay break bought nothing. Read at the final head (the Architect's C3): flooring alone scores 26 of 32 with the start split at 6/15, so it is not met.
- **It is also withdrawn** if `mirrorprobe` ever finds a one-ulp multiplication split as a first break in a truncating build, which would mean the symmetry it was taken for does not hold.
- **A cross-platform golden mismatch in CI** (Windows or Linux disagreeing with these hashes) stops it before merge, as it would any golden move.
- If it is withdrawn, ADR-075's frame is judged again in the multiplication that replaces it, since the frame verdict depends on the rounding (ADR-075's amendment of 2026-10-08).

## Consequences

**Easier.** `(-x)*y == -(x*y)` holds exactly, so mirrored computations stay mirrored: `mirrorprobe` no longer finds a multiplication split as a first break on any map, and every later symmetry row (P8-66, the flow field's tie-break, boundary-cell ownership, the sub-cell step) is judged against arithmetic that is symmetric under negation. Multiplication and division now round the same way. In D38's configuration F5's income clause reads 28 of 32 and its split clause 12/10, every binding gate green.

**Harder.** Every replay recorded before this ADR diverges, and a mixed-build LAN pair desyncs rather than being refused (Q024). The sim no longer breaks an exact movement tie by rounding accident, so a walker whose step and separation push cancel exactly stays put until P8-66 lands, and the `mission` fixture's green depends on the sheltered frame until then. F11 reads worse (6/6 against 0/6). The F5 gain is one sample.

**Committed to.** Multiplication truncating toward zero is part of the sim's rules. A change back moves at least the thirteen goldens listed above and every replay, and is judged by the reversal above, not by preference.

## Architect sign-off

- Date: 2026-10-08
- Reviewer: Systems Architect (A3).
- Pull request: the D38 pull request from branch `claude/p8-53-trunc-shelter-on` into main. It carries three staged commits: ADR-075's amendment of 2026-10-08, ADR-077, and ADR-076 clause 3. It opened as pull request 169. A determinism audit and an adversarial verifier each reviewed it independently.
- Verdict: **signed off with conditions.** ADR-077's multiplication is approved, and so are the golden moves of all three stages as attributed. The pull request does not merge while ADR-076 clause 3's own reversal reads met, and conditions 1 to 6 below must be met in it first.

**Reproduced, on macOS only.** Each stage reproduces its committed 25 lines. The frame flip moves 5 goldens, the multiplication 13 and the exit 3, which is 13 against main in all, with one cause per stage. Undoing a stage restores the previous stage's 25: for stages 1 and 2 by restoring the stage's one sim file, for stage 3 by switching the exit back to the authored order. With stage 1's `Fix64.cs`, `selftest` fails by name. `tools/ci-local.sh` is green on 19 steps and `tools/verify-client.sh` passes. The catalogue checksum, the save format and the wire format do not move. Every gate figure quoted for the three stages reproduces. Windows and Linux are left to CI.

**The break is justified by the arithmetic, not by the count.** Truncation makes `(-x)*y` equal `-(x*y)` exactly and rounds the way division already does. It is integer only, cannot overflow, and sits in the one operator every sim multiplication goes through. The result that does not depend on chaos is this: `mirrorprobe` no longer finds the one-ulp split as a first break in any oriented setting. The F5 gain is one pair in one deterministic sample, taken as the best of 18 cells, and ADR-077 says so.

Q024 is stated plainly. Old replays diverge and are not refused. Saves load and continue. A mixed-build LAN pair desyncs and is not refused. All of that is acceptable only because no build has been published.

The rest of the record holds up:
- The three rejected alternatives (round half away from zero, closing P8-53 as measured but not fixed, and truncation in the centre-facing frame) each have a paragraph, and the matrix supports each reason.
- F11's worsening (6/6 against 0/6) and the `mission` coupling are recorded for what they are.
- P8-66 is filed with a done condition that judges the frame again.
- ADR-077's reversals are testable. The first was tested and is not met: flooring alone at the head reads 26 of 32.

**What blocks the merge.** Clause 3's reversal, made measurable by ADR-076's C7, reads met at the head that re-lands it. Measured: 6144 producer ticks held a paid unit that the authored order would have released, against 0 without the clause and 0 on main. All of them are both commanders' barracks on skirmish-08's Directorate mirror, from t=8840.

That is the harm the reversal was written to catch, so it is not restated now that it has tripped. Withdrawing the clause would be expensive. It leaves stage 2's start split (12/6) outside 60/40, and ADR-075's frame trigger names P8-64, so D38 would have to be taken again from the start.

The trace puts the fault in the list, not the frame. `SpawnOffsets` probes two sides of an even footprint at the adjacent cell and the other two sides one cell clear, and reflecting it turns the blind side towards the centre. So the remedy is to remove the blind spots in this pull request, under row P8-64. Old replays already diverge, so this adds no further replay break.

**Conditions before merge.**

1. **Remove the blind spots.**
   - The rule: the exit search keeps its first eleven probes in each frame. After them it tries the cells its own frame misses, in an order fixed in the producer's own frame, so that the set it searches is closed under reflection through the footprint's centre on each axis.
   - Change `FindProductionExit` only, not the shared `SpawnOffsets`.
   - Land it as a fourth staged commit, or on main before this pull request, as an amendment to clause 3.
   - Add a `spawngate` stage that builds the skirmish-08 trap: the adjacent centre-facing cell open and all eleven own-frame cells blocked. It releases the unit on the tick it finishes, on the half turn and on each single axis, and fails by name against the eleven-cell search.
   - At the final head, `pillarprobe` reads 0 for "the unreflected order alone".
   - Its golden moves are approved in advance only if each moved golden names the first held unit the new cells released, and restoring the eleven-cell search alone restores stage 3's 25. Any other move comes back to me.
2. **Restate clause 3's reversal** so it still guards something once condition 1 makes "the unreflected order alone" zero by construction. New wording: clause 3 reverses if `pillarprobe`'s total held producer ticks at a head exceed the same head's total with `ProductionSystem` asking the authored order. The 2026-10-08 reading and its trace stay in the ADR as the reason the search changed.
3. **Read D38 again at the final head.** It stands only if all of these hold there:
   - every binding gate is green: `pillargate --bind` on both halves, `aiairgate`, `cheesegate`, and `golden`, `determinism` and `match` with `mission` passing;
   - the start split is inside 60/40;
   - ADR-075's C7 comparison, with truncation and the final search in both arms, still favours the sheltered frame;
   - ADR-077's first reversal, restated with the final head's figures, is not met;
   - `mirrorprobe` finds neither a production exit nor a multiplication split as a first break.

   Wherever the final figures differ from stage 3's, they replace them in ADR-075, ADR-076, ADR-077, D38 and rows P8-53 and P8-64. If any check fails, D38 goes back to the orchestrator before merge.
4. **State the frozen-walker figure.** ADR-077 gives the final head's own figure, measured there: units and harvesters that report Moving but stay stationary for 450 ticks or more over `pillargate`'s sweep. Give it beside stage 2's figure and flooring's in the same frame.
5. **Goldens-neutral fixes.**
   - `lanaiseatsgate` stops calling its pin the figure from before commanders existed.
   - `pillarprobe`'s clause 2 line counts a harvester with no field left on the map separately from one that is stranded.
   - `ProductionExitOpen` returns false for any id that is not a live producer.
   - The rows carried from ADR-076's first sign-off are filed with owners: the unload, the decoy army and the tunnel deployment still on the authored order; both asymmetric clamps; and the client's door faces against the exit side, now that clause 3 lands.
6. **CI and status.**
   - All three CI jobs are green on the pull request, and the determinism job reproduces the final head's 25 goldens on Windows and Linux.
   - `tools/ci-local.sh` and `tools/verify-client.sh` are run again at the final head.
   - Nothing under `.claude/agent-memory` is committed.
   - ADR-077's status reads Ratified under D38 and cites this section by pull request number. The open-queue entries for ADR-075, ADR-076 and ADR-077 cite it the same way.

**Carried.**
- P8-66 lands before the next movement or commander-frame row, because until it does, `mission`'s green depends on where the sheltered frame puts a base.
- `pillargate --bind` still has to join `tools/ci-local.sh`.
- F11 goes to P8-33.
- Q024 is settled before the first published build.

**The pull request this section signs off.** The review above was of the branch's three staged commits at 84a64ce. What merges is D38's pull request from `claude/p8-53-trunc-shelter-on`, those three commits cherry-picked unchanged onto main at 9316aec, then two more: the fourth staged commit (condition C1, which also restates clause 3's reversal under C2) and a goldens-neutral commit meeting conditions C3 to C5 and recording this ratification. It is pull request 169.

**Conditions C1 to C5 are met in that pull request (2026-10-09); C6 is its CI and merge hygiene.**

- C1: the fourth staged commit amends ADR-076 clause 3 under row P8-64. `FindProductionExit` keeps its first eleven probes in each frame unchanged, then tries the cells they miss, in an order fixed in the authored frame and reflected with them, so the set it searches is closed under reflection through the footprint's centre on each axis; only `FindProductionExit` changed, not `SpawnOffsets`. `spawngate` stage 10 builds the skirmish-08 trap and releases the unit on the tick it finishes, on the half turn and on each single axis, with the open cell on the centre-facing side in x and then in y, and it fails by name against the eleven-cell search. At the final head `pillarprobe` reads 0 for "the unreflected order alone", and 0 held in all (stage 3: 6144). No golden moved, so neither advance-approval condition had a move to cover; restoring the eleven-cell search alone gives stage 3's 25, which are also the head's. ADR-076, section "Clause 3 amended under D38".
- C2: clause 3's reversal is restated in ADR-076's "What reverses it" as worded here, and at the final head it reads 0 against 0, not met. The 2026-10-08 reading (6144 against 0) and its trace stay there as the reason the search changed.
- C3: D38 is read again at the final head and all five readings hold (section "At the final head: D38 read again"). The final figures equal stage 3's except `pillarprobe`'s, which replace stage 3's in ADR-076, this ADR, D38 and rows P8-53 and P8-64; ADR-075 quotes no `pillarprobe` figure, and its amendment of 2026-10-08 records its comparison read again at the final head.
- C4: the final head's own frozen-walker figure, 6 harvesters and 14 units over `pillargate`'s sweep, stands beside stage 2's (12 and 51), flooring's in stage 2's frame (stage 1: 10 and 26) and flooring alone at the head (12 and 10).
- C5: `lanaiseatsgate`'s printed sentence, and its failure message, call the pin what it is: first measured before commanders existed and re-pinned since only for named rule changes. `pillarprobe`'s clause 2 line counts a harvester Idle or stranded when no field with ferrite is left anywhere on the map apart from the Idle and stranded readings (at the head 0 and 0, with 6 counted apart). `World.ProductionExitOpen` returns false for an id out of range, a dead entity, a non-producer, or a Construction Yard, which places what it builds and so has no exit. Rows P8-68 (the unload, the decoy army and the tunnel deployment), P8-69 (`SetExitMove`'s clamp), P8-70 (`SpawnFreeHarvester`'s clamp) and P8-71 (the factory door faces) are filed with owners, and P8-72 (`pillargate --bind` in tools/ci-local.sh) for the item this section carries.
- C6: open until the pull request's CI. `tools/ci-local.sh` and `tools/verify-client.sh` are re-run at the final head ("Verification"), nothing under `.claude/agent-memory` is committed, this ADR's status reads Ratified under D38, and the open-queue entries for ADR-075, ADR-076 and ADR-077 cite this section. All three CI jobs green on the pull request, with the determinism job reproducing the final head's 25 goldens on Windows and Linux, is still owed; the pull request is 169.

### Addendum, 2026-10-09: the pull request confirmed

- Date: 2026-10-09
- Reviewer: Systems Architect (A3).
- Pull request: 169, D38's pull request from `claude/p8-53-trunc-shelter-on` into main. I confirmed it at the reviewed head 42076eb, which carries four staged commits and one goldens-neutral commit. This confirmation covers that pull request, not any single commit on it.
- Verdict: **conditions C1 to C5 are met, and D38 stands at the final head.** The pull request merges once C6 is met and the ADR-076 correction below is in it.

I read the fourth staged commit and the goldens-neutral commit against the conditions as I worded them. An independent verifier measured every figure again in builds of its own.

**C1 is met.** `FindProductionExit` keeps its eleven probes, then tries the cells they miss. That list is derived from `SpawnOffsets` when the type initialises, so the set searched is the eleven closed under both reflections through the footprint's centre. I recomputed the even-footprint list myself. It is the 25 offsets ADR-076 lists, and it covers every cell beside a 2x2 footprint and none inside it. `SpawnOffsets` is untouched.

`spawngate` stage 10 builds the trap on the half turn and on each single axis, once with the open cell in x and once in y. It fails by name against the eleven-cell search and against either one-axis closure. Stage 6's wall now covers more cells, not fewer, and it still asserts the release at the mouth. `pillarprobe` reads 0 held, and 0 for the authored order alone. No golden moved, and restoring the eleven-cell search alone gives stage 3's 25.

**C2, C4 and C5 are met.** C2's wording is in ADR-076 and reads 0 against 0. C4 gives the head's own figure (6 harvesters and 14 units) beside stage 2's and flooring's, in the same frame. C5's three fixes and its rows are in.

**C3 justifies D38 at the final head.** All five readings hold:
- every binding gate is green, with `mission` won;
- the start split is 12/10;
- the sheltered frame scores 28 of 32 at 9.06 per cent, against the centre-facing frame's 26 at 10.78;
- flooring alone scores 26 of 32 with the start split at 6/15, so ADR-077's first reversal is not met;
- `mirrorprobe` finds neither a production exit nor a multiplication split as a first break.

Every figure except `pillarprobe`'s equals stage 3's. The margin is still one pair in one deterministic sample, and the firm ground is still the arithmetic.

**One correction before merge (documentation only, goldens neutral).** ADR-076's "Clause 2's measurement, read again at the head" still quotes stage 3's 6 Idle and 6 stranded. It also still says the counter does not ask whether any field remains, which C5 has made false. It must give the final reading: 0 Idle and 0 stranded with a field left, and 6 counted apart with no field left, all on skirmish-03's Directorate mirror. Clause 2's reversal must say that it reads the first two figures, not the third. No re-measurement is needed.

**Still owed (C6).** Nothing has run on Windows or Linux yet. All three CI jobs must go green, and the determinism job must reproduce the 25 goldens on both platforms. Write the pull request's number into this addendum, into ADR-077's status, and into the open-queue entries for ADR-075, ADR-076 and ADR-077. A golden mismatch in CI stops the merge under ADR-077's third reversal. Any commit beyond the correction above and those number edits comes back to me.

**Note, 2026-10-09: the merge of main and the harness fix.** After the reviewed head 42076eb, pull request 169 took b1cf42c, a merge of main at 7659dbb (pull request 168, client only), then d8141b7. The merge leaves sim and data exactly as at 42076eb, keeps every other file byte for byte from one parent, and resolves the tracker by keeping D38's P8-33 row and main's P8-34 and P8-35 rows, adding nothing of its own. It was still a semantic conflict: the client harness failed at it on denygate/hold, because P8-42's fixture walled only the cells two and three from the barracks centre, and the exit search as amended under C1 also tries the cells beside the footprint and reaches four out. That is C1 doing its job, not a sim defect. d8141b7 changes only game/scripts/VerifyRunner.cs: it walls every open cell within four of the centre, which covers the amended search in every frame, and asserts through ProductionExitOpen that the sim's own search finds no exit, so a later widening fails by name. No sim, data or golden changed. Both commits are confirmed, provided all three CI jobs pass at the final head.
