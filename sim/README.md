# /sim

The deterministic simulation, and the only thing in this repo whose output is
contractual. Four projects, none of which may reference an engine:

- **Ferrostorm.Sim** - the world. `World.Step(commands)` runs at 15 Hz, and its
  system order is a determinism-schema change if reordered. The `Step` method
  itself is the authority on that order; docs/design/14-sim-handbook.md is a
  guide and has drifted from it before.
- **Ferrostorm.Presentation** - the snapshot contract the client renders from
  (`SnapshotInterpolator`, `ViewEntity`). Outside the state hash by
  construction, so widening it moves no golden.
- **Ferrostorm.Net** - the lockstep relay and client. Also outside the state
  hash. Knows about frames, ticks and hashes; deliberately knows nothing about
  maps, factions or seeds (ADR-022).
- **Ferrostorm.Sim.Runner** - the gate. `dotnet run --project
  sim/Ferrostorm.Sim.Runner -c Release` runs the whole battery and must exit 0.
  Its `Program.cs` header lists every mode.

## The rules that are not negotiable

No `float`, `double`, `System.Random`, wall clock, locale or engine reference
anywhere in `Ferrostorm.Sim`. Fixed-point only (`Fix64`, ADR-002). CI greps for
all of it, and the golden-hash check runs on Windows AND Linux, because
"deterministic on my machine" is not the claim being made.

`sim/golden-hashes.txt` holds 24 scenario hashes. Changing one is a
replay-compatibility break and needs an ADR plus Architect sign-off. In practice
most waves are hash-NEUTRAL by construction: a new unit or structure type that
no golden scenario spawns is byte-identical dead code, which is how the repair
vehicle, the outpost and destroyable bridges all landed without moving a hash.

## Reading the sim from the client

The client may CALL a sim rule but must never copy one. `World.AtLeast75`,
`HasPrereqs`, `StructureAllowedForFaction` and `CountBarriers` are public for
exactly that reason: each was duplicated client-side, and each pair agreed right
up until one of them was edited. See docs/tickets/P6-duplicated-rule-audit.md.
