# 03 - Technical Design Document (TDD)

Version 0.1. This is the contract engineering agents build against. Changes require an ADR (Architecture Decision Record) in `/docs/adr/`.

---

## 1. Guiding Technical Principles

1. **Determinism is the product.** Replays, observers, cheap multiplayer, and desync-free play all fall out of one property: identical inputs produce identical simulation on every machine. Every technical choice is subordinate to this.
2. **Simulation and presentation are separate programs that happen to share a process.** The sim never reads the renderer; the renderer never writes the sim.
3. **Data-driven everything.** Units, buildings, weapons, armour matrices, support powers, AI build orders - all plain-text data files. Modding support is a side effect of our own iteration speed.
4. **Playable forever.** LAN/direct-IP works with zero backend. Online services (matchmaking, ladder, relay) are additive.

## 2. Engine Decision

Candidates evaluated: Unity (DOTS), Unreal, Godot 4 (C#), custom engine, Bevy (Rust).

**Recommendation: Godot 4 with C# for presentation, plus a pure-C# deterministic simulation library with zero engine dependencies.**

Rationale:
- The sim being a plain .NET library means: unit-testable without an editor, runnable headless on CI and on balance-simulation farms, portable if the engine choice ever changes, and agent-friendly (AI coding agents iterate far faster on a plain library with tests than inside an engine editor).
- Godot: open source (no licence-fee stakeholder risk), light, good 2D/3D hybrid, C# support, shippable to Steam/Steam Deck.
- Unity DOTS was the runner-up (better perf ceiling) but licence trust and editor-centric workflow score worse for a solo, agent-driven pipeline.
- Custom engine rejected: renderer and tooling costs would consume the entire budget of a solo project.

An ADR must be written before any code confirms or overturns this (ADR-001).

## 3. Simulation Architecture

- **Model:** Fixed-timestep lockstep. Sim ticks at 15 Hz (classic RTS rate; interpolated rendering at display rate). Every tick consumes an ordered list of player commands.
- **Determinism rules (enforced by CI):**
  - Fixed-point maths (Q32.32) throughout the sim. No `float`/`double` in simulation code - a Roslyn analyser fails the build if found.
  - Single seeded PRNG owned by the sim; no `System.Random` elsewhere.
  - Deterministic collections only (no unordered dict iteration affecting outcomes).
  - No wall-clock, locale, or platform API access inside the sim.
- **Structure:** Lightweight ECS-style layout (struct arrays, systems in fixed order): Movement → Combat → Harvesting → Production → Power → Triggers. Full sim state serialisable for save games and desync forensics (per-tick state hash).
- **Pathfinding:** Hierarchical flow fields over a grid for group movement, with local steering/avoidance. Harvester pathing gets a dedicated planner (harvester traffic jams killed the feel of several classic titles). Path results must be deterministic (no time-sliced completion affecting order).
- **Fog of war:** Per-player visibility grid updated incrementally; shroud (never seen) vs fog (seen, stale).

## 4. Networking

- **Topology:** Lockstep with a relay server (or one client as host for LAN). The relay orders commands into ticks and rebroadcasts; it runs no simulation - costs stay near zero.
- **Latency handling:** Command delay of 2-4 ticks with adaptive scheduling; optional local input prediction for UI feedback only (unit acks play instantly, sim acts on schedule).
- **Desync handling:** State hash exchanged every N ticks. On mismatch: match flagged, both sim states dumped for diffing, players offered save-and-continue from host state. Desync telemetry is a launch-blocking dashboard.
- **Reconnect:** Rejoining client fast-forwards from the last snapshot + command log (P3 persona hard requirement).
- **Anti-cheat posture:** Lockstep means full-information clients; maphacks are the realistic threat. Mitigation: fog-relevant data minimisation where feasible, replay-based reporting, ranked ladder server-side stat validation. We do not promise kernel anti-cheat.

## 5. Data Model and Modding

- Unit/building/weapon definitions: YAML in `/data/`, hot-reloadable in dev builds. Schema-validated (JSON Schema) so the Balance and Modding agents get machine-checkable errors.
- Maps: open documented format (tile grid + entity list + trigger script), editor shipped with game.
- Scripting: Lua sandbox for map triggers and campaign missions - deterministic subset only (no os/io/time), same VM version pinned forever per map format version.
- Mod loading: content mods (data + assets) allowed in lobbies when all players have the mod hash; format versioning with a published deprecation policy (stakeholder S3).

## 6. Rendering and Presentation

- Godot scene layer consumes sim snapshots via an interpolation buffer (render state = lerp between tick N-1 and N).
- Team colour via palette-mask shader; all units authored with a mask channel.
- Performance budget: 600 active units + 200 structures at 60 fps on a GTX 1060-class GPU / 4-core CPU; sim tick under 8 ms at that load on one core.

## 7. Game Services (post-vertical-slice)

- Accounts: Steam identity only at launch (no bespoke auth system).
- Matchmaking + ladder: small stateless service (Glicko-2), Postgres for ladder/stats - Railway-class hosting is sufficient at beta scale, with a documented upgrade path.
- Replay storage: client-side always; cloud upload for ranked matches (command streams are tiny - kilobytes).
- Telemetry: opt-in, aggregate balance metrics (winrates by faction/map/skill band, unit usage), never chat content.

## 8. AI (skirmish opponent)

- Layered: strategic layer picks a doctrine (rush / tech / turtle / raid) from scripted-but-parameterised build orders in data files; tactical layer handles army composition targets, attack timing, retreat thresholds; micro layer is deliberately restrained (no superhuman splits) with difficulty scaling reaction times, not information.
- Easy/Normal/Hard use no hidden information; Brutal receives a labelled resource multiplier.
- The AI must run inside the deterministic sim (it is just another command source) so AI games are replayable and testable.

## 9. Testing and CI Strategy

- **Determinism CI:** every merge runs N seeded headless matches twice and diffs final state hashes; any mismatch blocks merge.
- **Balance simulator:** headless engagement runner (X units vs Y units per-cost) producing regression reports per data change.
- **Replay corpus:** curated replays re-simulated on every engine change; divergence = failed build (protects the replay compatibility promise within a major version).
- **Perf gates:** benchmark scene must hold budget on CI hardware profile.
- Unit tests target the sim library (aim: sim is 100% testable without opening the editor).

## 10. Repository and Environment Layout

```
/sim            pure C# deterministic simulation library + tests
/game           Godot project (presentation, UI, audio)
/data           YAML unit/building/weapon/AI definitions (schema-validated)
/tools          balance simulator, map editor, replay inspector, asset pipeline
/services       matchmaking, relay, ladder (deploy independently)
/docs           this package, ADRs, format specs, style bible
```

## 11. Architecture Decision Records (initial queue)

| ADR | Decision needed | Status |
|---|---|---|
| 001 | Engine choice (Godot+C# sim library) | Ratified (2026-07-07) |
| 002 | Fixed-point library selection/benchmark | Ratified (2026-07-07) |
| 003 | Infantry: squads vs individuals (perf + design) | Ratified (2026-07-07); pairs with GDD Q3 |
| 004 | Lua sandbox implementation for triggers | Open |
| 005 | Tile size / grid resolution / footprint rules | Open |
| 006 | Save format = snapshot vs command-log replay | Open |
