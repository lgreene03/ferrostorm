# 12 - Balance Simulator Specification (TICKET-P1-14)

Owner: Balance agent. Reviewed by: Architect. Status: accepted; implementation is TICKET-P2-BAL-01 against the existing headless runner.

## Purpose
Catch balance regressions from /data changes before human testing (GDD s12): every data PR gets a machine-generated engagement report.

## Architecture
A /tools console app referencing Ferrostorm.Sim (same determinism guarantees). Inputs: /data unit definitions (once the YAML loader lands; compiled tables until then) plus a matchup manifest. Outputs: markdown + JSON report per run, archived per commit.

## Core test: per-cost engagement matrix
For every ordered pair (A, B) of combat units: spawn equal-credit armies of A vs B on a flat arena at engagement range, three seeds, fixed formation. Record winner, survivor value percentage, time-to-resolution. A matchup flips status (win<->loss) or shifts survivor value by more than 10 points vs the previous baseline => report flags it and CI marks the data PR "needs Balance sign-off".

## Secondary tests
- Harvester tempo: credits gathered in 3000 ticks for each faction's standard opening layout; drift >5% flags.
- Counter-triangle audit: the GDD s6 intended counters must hold per-cost (rockets beat tanks, anti-infantry beats infantry, raiders beat artillery); any inversion is a hard failure.
- Time-to-kill sanity bounds: no combat unit pair may resolve in under 2 seconds or over 90 seconds at equal cost.

## Determinism and reporting rules
Seeds fixed and committed; reports reproducible; the tool exits nonzero on hard failures so CI can gate. Report header records data-file hashes so any result is traceable to exact numbers.

## Amendment 2026-10-02 (P8-14: the tool rebuilt to measure the shipped game)

The text above is kept as written; where it and this amendment disagree, this amendment is what the tool does. The reasons are recorded in `tools/Ferrostorm.Balance/Program.cs` beside each line.

**Inputs.** /data only, registered through `CatalogueFiles.RegisterAll` exactly as the runner and the client register it; the compiled tables are never the source. The report header prints the catalogue checksum.

**Seeds.** One seed, not three. The sim draws no random numbers, so three seeds were one run counted three times; the tool now checks, on every run, that the RNG state is still the seed when the run ends, and says so in the report. If a later row starts drawing it, the report warns that each cell has become a sample.

**The matrix.** All 20 unit types, at four budgets (1800, 4800, 9000 and 12600). Equal credits FIELDED, within one unit's cost, with each side's unspent remainder printed; a hero is capped at its `max_alive` and its opponent matched to the hero's value. A pair is a matchup only when each side can engage the other (ADR-028 clause 3), so the strike flyer meets the flak track and nothing else, and the eight unarmed types are listed as non-combatants rather than dropped. The arena is mirrored across the diagonal, because a left-right mirror cannot be exact in Fix64, and every uncloaked mirror is asserted to end in mutual annihilation. Every pair is fought from both seats and in a second, axis-aligned arena; a row whose winner changes with the seat or with the angle of approach is printed as such and counts for neither unit. The counter-triangle audit and the time-to-kill bounds are reported, not asserted.

**Secondary tests.** The static-defence gate (DEF-17) and the harvester tempo floor still assert. An equal-credit siege row (tracker D19) sets each faction's own defence against the same credits of each besieger, reporting only.

**The faction war.** Each faction in each seat on skirmish-01, -02 and -04, with the shipped opening hand (`PlaceSkirmishStart(8000)`), played to the sim's own victory or 27000 ticks. Nothing is adjudicated, and banked credits decide nothing. The report prints faction share and seat share per map and overall, and a stuck-harvester share per seat.

**Gating.** Exit 1 on a failed self-check (fielded credits, mirror annihilation, reproducible tempo) or a failed DEF-17, wall or tempo gate; exit 2 when the tool cannot run. P8's F10 and F11 are reported and become binding when rows P8-32 and P8-33 flip `F10Binding` and `F11Binding`. `full` is the default mode and what CI runs; `quick` drops skirmish-04 for local iteration. A `--report` path inside the repository is refused.
