# ADR-002: Fixed-point maths implementation
- Status: Ratified (with ADR-001, per owner go-ahead 2026-07-07)
- Date: 2026-07-07
- Deciders: Architect agent + Luke
- TDD feature served: s3 determinism rules

## Context
The sim bans float/double. A Q-format fixed-point type is required for positions, speeds, and combat maths, with bit-identical results on every platform.

## Decision
Custom Q32.32 struct (`Fix64`) in Ferrostorm.Sim, backed by long raw values, using .NET Int128 intermediates for overflow-safe multiply/divide, plus integer Newton-Raphson Sqrt and a splitmix64 `DeterministicRandom`. Zero external dependencies.

## Alternatives rejected
FixedMath.Net and similar NuGet libraries: adds a dependency for ~200 lines of well-understood code, complicates offline/agent builds, and third-party trig tables would still need auditing for determinism. Q16.16: insufficient positional precision at large map sizes over long games (error accumulation risk). Raw integer millimetres: workable but pushes unit conversions into every formula; a typed Q32.32 is safer against unit mistakes.

## Evidence (container-class CPU, Release build)
- Fix64 multiply+add: ~624 Mops/s - arithmetic will not be the bottleneck.
- Benchmark scenario (500 units, 1000 ticks, movement with per-entity sqrt): 0.87 ms/tick against the 8 ms budget (TDD s6).
- Sqrt: RESOLVED - bit-length initial guess landed (5.5x: 0.4 -> 2.2 Mops/s), verified bit-identical via unchanged goldens before other changes. DistSq comparisons already used in hot paths.

## Consequences
Easier: no dependencies, full audit control, serialisable raw values for saves/replays.
Harder: trig (needed later for turrets/facing) must be added as deterministic lookup tables with committed golden values.
Committed to: golden-hash file discipline - the seed 2026 benchmark hash 0x1843398AA97354AC is now a replay-compatibility contract enforced by CI on Windows and Linux.
