# ADR-004: Engine strategy - Godot now, Unreal Engine 5 later

Status: Ratified (Luke, 2026-07-14)

## Decision

Ferrostorm develops on Godot 4 today and will migrate its presentation
layer to Unreal Engine 5 when the design is locked and a commercial push
begins. The migration is a RENDERER SWAP, never a game rewrite: everything
outside /game is the portable product, and everything inside /game is
deliberately disposable. The simulation is never rewritten in C++; at
migration time it runs beside UE5 either as a separate lockstep process or
via .NET hosting, preserving its verified determinism bit for bit (the
Stormgate model: custom deterministic sim beside a UE5 renderer).

## The layer law (enforced by CI)

1. PORTABLE SURFACE - sim/ (Sim, Presentation, Net, Runner), tools/,
   data/, docs/: no engine reference of any kind. The existing purity
   grep bans float/double/System.Random/Godot in the sim library; a
   second CI step now fails the build if "using Godot" appears anywhere
   outside /game. SnapshotInterpolator and the event stream are the ONLY
   contract a renderer may consume; commands are the only way anything
   mutates the world.
2. DISPOSABLE SURFACE - /game: Godot-specific by design. Anything built
   here must route through the portable contracts; if a feature needs
   state the sim does not expose, add a read-only accessor to the
   portable surface (Architect sign-off, as QueueContents was) rather
   than reaching around it.
3. ASSETS - art/ produces engine-neutral outputs (glTF models with baked
   textures, WAV audio, PNG icons). glTF imports into UE5 directly;
   nothing may depend on Godot import metadata (.import sidecars are
   never authored by hand).
4. FORMATS - saves, replays, and maps are hand-specified binary/text
   formats documented in docs/design/14 (sim handbook), readable from
   any language. Godot-only serialisation is forbidden everywhere.

## Migration triggers and plan

Migrate when ALL hold: design locked (roster, mechanics, missions);
commercial/trailer push where UE5 rendering materially sells; a Windows
workstation available (UE5 iteration on the Mac mini is not viable for
development). First step is a two-week SPIKE, not a commitment: port only
the battlefield renderer (terrain + glTF models + one replay file played
through a C++ SnapshotInterpolator port or IPC bridge) and judge by eye.
The full swap then rebuilds /game's roughly 5k lines against doc 21's
system-by-system mapping. Until those triggers hold, Godot-side work
continues at full speed - it is cheap, it is the design-iteration vehicle,
and none of it mortgages the future because none of it is load-bearing.
