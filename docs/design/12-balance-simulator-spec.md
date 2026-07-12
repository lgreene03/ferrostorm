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
