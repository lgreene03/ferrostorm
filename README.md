# Ferrostorm

Internal codename: Project FERROSTORM. Provisional public title: **Ferrostorm** (pending Stage B/C clearance, see docs/design/10-stage-a-report.md). Provisional resource name: **Ferrite**.

A modern real-time strategy game inspired by the classic RTS games of the 90s, built as two strictly separated layers (ADR-001): a deterministic, fixed-point C# lockstep simulation with zero package dependencies, and a Godot 4.7 .NET client that is presentation only. The sim is the authority on all gameplay; the client renders it and submits commands.

This is a pre-release, in-development repository. The game is playable from source; there are no packaged builds, and the title is provisional.

## What exists today

- Playable skirmish against an AI (three temperaments: standard, rusher, turtle) on four committed maps
- A three-mission campaign with briefings, driven by data-defined triggers
- Binary save/load, and replays with hash-verified bit-exact playback
- A settings scene: every key rebindable with conflict detection, audio buses, applied video options
- Walls and barrier mechanics (ADR-005), and the full alert set (base and harvester attack warnings, low power, superweapon launch detection, jump-to-event)
- Twelve unit types and ten buildings authored as YAML in /data, plus wall segments. Two of the ten (the barracks and the radar uplink) are catalogued ahead of ADR-009's production roster and are not yet buildable in a match
- A LAN lockstep transport (relay plus clients) that is real and soak-tested over loopback with zero desyncs; play between two machines is not yet possible because the battle scene's frame loop is not lockstep-driven (docs/questions/Q002)

## Build and run

Requires the .NET 8 SDK. NuGet package sources are disabled by design; the sim has zero package dependencies.

- Build sim + runner: `dotnet build sim/Ferrostorm.Sim.Runner -c Release`
- Full local gate: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` (selftest + double-run determinism + scenario battery + lockstep soak; exit 0 required)
- Individual modes: `selftest`, `determinism [seed]`, `match [seed]`, `bench`, and more (see the header of sim/Ferrostorm.Sim.Runner/Program.cs)
- The client: open `game/` in Godot 4.7 (the .NET build) and run

## The determinism story

Determinism is the project's law, not an aspiration:

- No `float`/`double`, no `System.Random`, no engine references anywhere in the sim library; CI greps for banned tokens on every push
- Fixed-point maths throughout (ADR-002)
- 24 gated scenarios, each with a golden state hash in `sim/golden-hashes.txt`, verified byte-identical on Windows and Linux in CI (.github/workflows/determinism.yml)
- Changing a golden hash is a replay-compatibility break and requires an ADR plus Architect sign-off
- Replays and saves round-trip bit-exactly, and the lockstep soak runs full games with the relay comparing state hashes every 30 ticks

## Layout

- `/sim` - the deterministic core: `Ferrostorm.Sim` (the simulation library), `Ferrostorm.Net` (lockstep relay and client), `Ferrostorm.Presentation` (the snapshot contract), `Ferrostorm.Sim.Runner` (the gate battery)
- `/game` - the Godot 4.7 .NET client (presentation only)
- `/data` - YAML unit and building definitions validated against the schemas here, `.fmap` maps and missions, and the campaign script
- `/tools` - the balance simulator (`Ferrostorm.Balance`), a replay viewer, and map generation scripting
- `/art` - source art: the 3D model builder, the audio synthesis pipeline, sprites and reference sheets
- `/services` - placeholder only; no code yet (relay-as-a-service, matchmaking and ladder are future work)
- `/docs` - the design package, ADRs, open questions, tickets and balance reports

## Where the truth lives

1. `CLAUDE.md` - operating rules for everyone working in this repository
2. `docs/design/` - the design package; `18-game-review-roadmap.md` is the current roadmap
3. `docs/adr/` - architecture decisions and their status
4. `docs/questions/` - open cross-team questions with owners and decide-by dates
5. `docs/tickets/phase-1-backlog.md` - the work ledger, entry by entry
