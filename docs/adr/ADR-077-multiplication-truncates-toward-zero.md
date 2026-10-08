# ADR-077: Fix64 multiplication truncates toward zero, so a negative product is the exact negation of its positive twin
- Status: Proposed, 2026-10-08, under decision D38 of docs/tickets/P8-formidable-tracker.md (row P8-53), taken by the orchestrator under the owner's standing authority of 2026-10-02 on the measured matrix and its independent check. It moves goldens, so the Architect's sign-off is owed before merge (CLAUDE.md: a golden change is a replay-compatibility break needing an ADR and the Architect). It lands as stage 2 of the three staged commits D38 takes in one pull request (branch `claude/p8-53-trunc-shelter-on`): stage 1 flips ADR-075's frame (its amendment of 2026-10-08), this stage changes the multiplication, and stage 3 re-lands ADR-076 clause 3 (P8-64). Drafted by the implementer agent.
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

So the rounding choice is coupled to ADR-075's frame: with this ADR landed, ADR-075's frame clause cannot be reversed back to centre-facing without the mission fixture failing, and ADR-075's amendment of 2026-10-08 records the same coupling from its side. The defect underneath is movement's, not the multiplication's, and it is filed as **row P8-66**: movement needs a deterministic tie-break when a step and a separation push cancel exactly, so that the sim stops relying on rounding asymmetry to break such ties. It is an existing class, not one this ADR creates. Walkers that report Moving but stay put for 450 or more ticks occur in flooring builds too: 4 to 14 harvesters and 10 to 42 units per 72-match `pillargate` sweep, the longest 14834 ticks at PR 166's head. Truncation does not lower that count everywhere: at stage 2 (the sheltered frame, P8-64 off) it raised it from 26 to 51 units and from 10 to 12 harvesters, against flooring in the same frame; over the four oriented settings in aggregate it is 97 units and 24 harvesters against flooring's 114 and 34. ADR-014's no-progress backstop does not reach a harvester reporting Moving in ToField, which is why nothing self-corrects.

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

The matrix's "truncate, sheltered, on" cell, which the independent check reproduced exactly: income within 15 per cent in **28 of 32** matches, over the bar only on the Sodality mirrors of skirmish-01 and 03 (both orientations each); mean gap over all 32 **9.06 per cent** (o0 9.06, o1 9.06); win split by start **12/10 of 22 decided, inside 60/40**, by seat 11/11 (Directorate mirrors by start 0/6, Sodality 12/4); `pillargate --bind` both halves PASS with 60 of 72 matches launching, rate at most 3.3; `aiairgate`, `cheesegate` and the balance gate PASS; `endgate` 14 of 72 unresolved; `mirrorprobe` finds neither a multiplication split nor a production exit as a first break on any mirror. Stage 3 re-measures it at this pull request's head (ADR-076, "Clause 3 re-landed").

**F11 gets worse, and that is a measured input, not an improvement.** The balance gate (`tools/Ferrostorm.Balance`) passes, and its faction war reads F11 NOT MET at **Directorate 6/6 against Sodality 0/6** of (map x seat) cells, against 5/6 and 1/6 on main. It already reads 6/6 and 0/6 after stage 1 alone (measured: "F11 NOT MET: Directorate 6/6 (100%), Sodality 0/6 (0%) of cells"), and 4/6 against 1/6 at stage 2, so the faction war moves with every stage of this pull request and is a chaotic readout at this sample, like the start split. F11 binds only when P8-33 flips its switch, so nothing goes red, and the figure is P8-33's input for its balance pass. The measurement's report once had this direction inverted; the check found it in the measurement's own raw files, and it is recorded here as the check found it.

**F5 is not met.** 28 of 32 is not every match: skirmish-01 and skirmish-03 Sodality mirrors keep an income gap over 15 per cent in both orientations. `MeasurementHarness.SeatFairGateBinding` stays false.

## Verification

**At stage 2.** `dotnet build sim/Ferrostorm.Sim.Runner -c Release` with 0 warnings; `golden 2026` gives the 25 lines of the stage's `sim/golden-hashes.txt` in order; `tools/ci-local.sh` green on every step (the purity, portability, seat, team-colour, sidebar and legal greps, the build, `selftest`, `determinism 2026`, the ordered golden diff, `match 2026` with `lanaiseatsgate` inside it, `lan 5`, `lanchaos 1 60 30`, `spectate`, `replay`, `saveload`, `campaignsave` and the balance gate, whose verdict is PASS). The sim purity grep finds nothing in `sim/Ferrostorm.Sim`; the change adds no type, API or call it would catch, and `Int128` was already the multiplication's intermediate.

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

- **Truncation is withdrawn** if, at the merged head carrying all three of D38's changes, restoring the flooring multiplication alone scores at least as well by ADR-075's C7 measure (at least 28 of 32 matches with each seat's income within 15 per cent and, on a tie, a mean gap over all 32 no higher than 9.06 per cent) with `pillargate --bind` green and the start split inside 60/40: then the replay break bought nothing.
- **It is also withdrawn** if `mirrorprobe` ever finds a one-ulp multiplication split as a first break in a truncating build, which would mean the symmetry it was taken for does not hold.
- **A cross-platform golden mismatch in CI** (Windows or Linux disagreeing with these hashes) stops it before merge, as it would any golden move.
- If it is withdrawn, ADR-075's frame is judged again in the multiplication that replaces it, since the frame verdict depends on the rounding (ADR-075's amendment of 2026-10-08).

## Consequences

**Easier.** `(-x)*y == -(x*y)` holds exactly, so mirrored computations stay mirrored: `mirrorprobe` no longer finds a multiplication split as a first break on any map, and every later symmetry row (P8-66, the flow field's tie-break, boundary-cell ownership, the sub-cell step) is judged against arithmetic that is symmetric under negation. Multiplication and division now round the same way. In D38's configuration F5's income clause reads 28 of 32 and its split clause 12/10, every binding gate green.

**Harder.** Every replay recorded before this ADR diverges, and a mixed-build LAN pair desyncs rather than being refused (Q024). The sim no longer breaks an exact movement tie by rounding accident, so a walker whose step and separation push cancel exactly stays put until P8-66 lands, and the `mission` fixture's green depends on the sheltered frame until then. F11 reads worse (6/6 against 0/6). The F5 gain is one sample.

**Committed to.** Multiplication truncating toward zero is part of the sim's rules. A change back moves at least the thirteen goldens listed above and every replay, and is judged by the reversal above, not by preference.
