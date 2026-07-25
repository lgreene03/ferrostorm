# Godot client bring-up

**STATUS, 2026-07-25: this document's original banner is obsolete and the
sections below the bring-up steps are a historical session log.** It used to
open "UNTESTED IN AUTOMATION ... nothing in /game has been compiled or run by
CI", which was true when written and is now false: the `client-harness` job in
.github/workflows/determinism.yml installs the Godot mono editor, builds this
project and drives the real battle scene headless on every push
(`tools/verify-client.sh`, 78 checks). The snapshot interpolation contract is
still independently verified by the runner's `spectate` mode as well.

## Bring-up on your machine (~10 minutes)
1. Install the Godot **4.7.x .NET** editor and the .NET 8 SDK. (Earlier
   revisions of this file said 4.2.x; the project targets 4.7 - see
   `Ferrostorm.Game.csproj`, `Sdk="Godot.NET.Sdk/4.7.0"`, and the version CI
   pins.)
2. Open this folder as a project; let Godot generate the solution (Project >
   Tools > C# > Create C# solution) if prompted.
3. Build (the hammer icon) - Godot.NET.Sdk restores from nuget.org here.
4. Run. Expected: blue Directorate structures top-left, a harvester you can
   left-click select and right-click order about, a goldenrod ferrite field,
   red rifle squads to the east, and a state hash in the corner that advances
   every tick.

## Definition of done for the ticket
Order the harvester onto the field and watch credits rise; path units around;
confirm the on-screen hash matches a headless `match` run driven by the same
commands. Any divergence between this client and `spectate` behaviour is a bug
in this client, not the sim.

## Historical session log below this line (2026-07-11)

Kept as a record of how the automation gap was diagnosed. It is no longer the
current state: the gap was closed, and the proxy problem described below turned
out not to be the real obstacle. See the STATUS note at the top.

## Headless validation attempt (2026-07-11)

An automated bring-up was attempted from the build container: Godot 4.2.2
downloads from GitHub releases redirect to release-assets.githubusercontent.com,
which the container's egress proxy rejects despite appearing in the session
allowlist (403 "Host not in allowlist"). If that proxy entry is fixed, a
headless editor can import this project and validate scenes automatically:
  ./Godot_v4.2.2-stable_linux.x86_64 --headless --path game --import
Note the C# scripts additionally need the .NET editor build plus nuget.org
access for Godot.NET.Sdk - the sim itself deliberately needs neither.

## 3D first light (2026-07-12)

The client is now a 3D game shell: 20 .glb models (assets/models/, exported
from the Blender asset library that also produced art/lineup renders), an
RTS camera rig, and a ReplayTheater that plays sim exports as a 3D battle.
First run:
  1. Open the project in Godot 4.7 .NET, let it import the .glb files.
  2. Copy a replay next to project.godot:
     dotnet run --project ../sim/Ferrostorm.Sim.Runner -c Release -- export 2026 game/replay.json
  3. Set scenes/Battle3D.tscn as the main scene and press play: the Spine,
     the ferrite, and the war, in 3D, camera on WASD/edge/wheel.
The live-sim binding replaces ReplayTheater's JSON source with
SnapshotInterpolator later; both speak the same shapes.

## Bring-up validated (2026-07-12, dev machine)

Godot 4.7.stable.mono confirmed on macOS/Apple Silicon. Two real bugs found
and fixed in the blind client code:
  - Main.cs: Fix64.FromFraction takes int, not long; the mouse-to-cell cast
    needed narrowing.
  - game/NuGet.config added: the repo-root NuGet.config clears all package
    sources for the offline-first sim, which also blocked /game from
    reaching nuget.org for Godot.NET.Sdk. /game now carries its own
    NuGet.config that re-adds nuget.org (NuGet.config resolution stops at
    the nearest config walking up from the build directory, so this shadows
    the root one for anything built from within game/).
Godot's own headless `--import` also silently bumped Ferrostorm.Game.csproj
from Godot.NET.Sdk/4.2.2 to 4.7.0 to match the installed editor - expected,
not a bug.
Verified end to end offscreen (no interactive display available in that
session): headless asset import (20/20 .glb), full C# build via
`dotnet build Ferrostorm.Game.csproj`, and a real windowed run capturing the
framebuffer to PNG confirmed terrain (ground plane + ridge blocks) and
multiple distinct unit/structure models rendering at their correct replay
coordinates (verified against game/replay.json frame data directly). Note
for headless/background runs: Godot's CoreAudio init can hang indefinitely
waiting on a permission prompt with no visible window - pass
`--audio-driver Dummy` to skip it; and macOS App Nap can freeze an unfocused
windowed GUI app's main loop entirely - launch via `open -a` rather than a
backgrounded shell if it looks stalled at 0% CPU.
Not yet done on an interactive display: manual mouse/keyboard verification
of unit selection, right-click orders, and the on-screen hash matching a
headless `match` run (see "Definition of done" above) - that still needs a
human at the keyboard or a session with Screen Recording permission granted.
