# Ferrostorm

Internal codename: Project FERROSTORM. Provisional public title: **Ferrostorm** (pending Stage B/C clearance, see docs/design/10-stage-a-report.md). Provisional resource name: **Ferrite**.

A modern real-time strategy game inspired by the classic RTS games of the 90s, built as two strictly separated layers (ADR-001): a deterministic, fixed-point C# lockstep simulation with zero package dependencies, and a Godot 4.7 .NET client that is presentation only. The sim is the authority on all gameplay; the client renders it and submits commands.

This is an in-development repository for a playable game, public on GitHub with green CI. The game is playable from source, and `tools/package.sh [macos|linux|windows|all]` produces self-contained builds for all three desktop platforms (see docs/tickets/P6-packaging.md); the public title is still provisional.

## What exists today

- Playable skirmish against an AI (three temperaments: standard, rusher, turtle) on four committed maps
- A three-mission campaign with briefings, driven by data-defined triggers
- Binary save/load, and replays with hash-verified bit-exact playback
- A settings scene: every key rebindable with conflict detection, audio buses, applied video options
- Walls and barrier mechanics (ADR-005), and the full alert set (base and harvester attack warnings, low power, superweapon launch detection, jump-to-event)
- A full visual overhaul (docs/design/25-visual-overhaul-roadmap.md): natural terrain shaders (ground biome, grass, foliage, water), re-baked materials, fog and ambient fixes, a camera FOV fix that closed the off-map void, and faction colour
- Thirteen unit types and ten buildable buildings authored as YAML in /data, plus wall segments and two map-placed structures (the capturable neutral outpost and the destroyable bridge). All ten are buildable in a match through the tabbed production sidebar with prerequisites enforced; the barracks has been buildable since ADR-009 and the radar uplink since ADR-008
- Unit command stances (hold-fire, guard, patrol; ADR-015), client-side formations (ADR-018), a mobile repair vehicle (ADR-019), right-click cancel and refund on every sidebar item (ADR-020), two parallel build lanes at the Construction Yard (ADR-023), capturable neutral outposts that pay their owner (ADR-021) and bridges that can be felled to cut a crossing (ADR-025)
- **LAN play, reachable from the menu.** The relay and clients are soak-tested with zero desyncs, the battle scene's frame loop is lockstep-driven, HOST and JOIN are live, the host's match setup rides the handshake so a joiner builds the identical world (ADR-022), and a player who leaves is announced to the survivor. Two in-process battle scenes play each other to identical state hashes as a gate. **The one thing still owed is a real two-machine session**, which no in-process test can provide (docs/questions/Q002)

## Build and run

Requires the .NET 8 SDK. NuGet package sources are disabled by design; the sim has zero package dependencies.

- Build sim + runner: `dotnet build sim/Ferrostorm.Sim.Runner -c Release`
- Full local gate: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` (selftest + double-run determinism + scenario battery + lockstep soak; exit 0 required)
- Individual modes: `selftest`, `determinism [seed]`, `match [seed]`, `bench`, and more (see the header of sim/Ferrostorm.Sim.Runner/Program.cs)
- The client: open `game/` in Godot 4.7 (the .NET build) and run
- **The client harness: `./tools/verify-client.sh`.** It boots the real battle scene headless from the joiner's seat and asserts on what it does. Run it for any `/game` change - it has caught ten defects the sim battery cannot see, and CI runs it on every push, so a failure blocks the merge either way

## The determinism story

Determinism is the project's law, not an aspiration:

- No `float`/`double`, no `System.Random`, no engine references anywhere in the sim library; CI greps for banned tokens on every push
- Fixed-point maths throughout (ADR-002)
- 24 gated scenarios, each with a golden state hash in `sim/golden-hashes.txt`, verified byte-identical on Windows and Linux in CI (.github/workflows/determinism.yml)
- Changing a golden hash is a replay-compatibility break and requires an ADR plus Architect sign-off
- Replays and saves round-trip bit-exactly, and the lockstep soak runs full games with the relay comparing state hashes every 30 ticks
- The client is gated too, by a headless harness that drives the real battle scene (`tools/verify-client.sh`, run in CI). The sim was always verified exhaustively while the client had nothing but "it compiles", and that asymmetry is why four features once shipped looking implemented and entirely dead

## Layout

- `/sim` - the deterministic core: `Ferrostorm.Sim` (the simulation library), `Ferrostorm.Net` (lockstep relay and client), `Ferrostorm.Presentation` (the snapshot contract), `Ferrostorm.Sim.Runner` (the gate battery)
- `/game` - the Godot 4.7 .NET client (presentation only)
- `/data` - YAML unit and building definitions validated against the schemas here, `.fmap` maps and missions, and the campaign script
- `/tools` - the balance simulator (`Ferrostorm.Balance`), a replay viewer, the look-dev harness (`tools/lookdev`), and map generation scripting
- `/art` - source art: the 3D model builder, the audio synthesis pipeline, sprites and reference sheets
- `/services` - placeholder only; no code yet (relay-as-a-service, matchmaking and ladder are future work)
- `/docs` - the design package, ADRs, open questions, tickets and balance reports

## Where the truth lives

1. `CLAUDE.md` - operating rules for everyone working in this repository
2. `docs/tickets/P6-campaign-tracker.md` - the CURRENT state of play, wave by
   wave, and the resume point if a session dies
3. `docs/design/` - the design package. Note that `18-game-review-roadmap.md` is
   a superseded 2026-07-14 snapshot whose plan has shipped, not the current
   roadmap; it carries a status banner saying so. Read it as history
4. `docs/adr/` - architecture decisions and their status
5. `docs/questions/` - open cross-team questions with owners and decide-by dates
6. `docs/tickets/phase-1-backlog.md` - the work ledger, entry by entry
