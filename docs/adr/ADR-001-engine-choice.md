# ADR-001: Engine and simulation split
- Status: Ratified (owner go-ahead 2026-07-07)
- Date: 2026-07-07
- Deciders: Architect agent + Luke
- TDD feature served: s1 (determinism), s2, s3

## Context
The project needs a renderer, tooling, and platform export without a custom engine budget, while the deterministic lockstep simulation (the core technical bet) must be testable headless, on CI, and by AI agents without an editor in the loop.

## Decision
Godot 4 with C# for presentation in /game. The simulation is a pure .NET class library in /sim with zero engine dependencies, consumed by the client through a snapshot API and command queue.

## Alternatives rejected
Unity DOTS: better performance ceiling, but licence trust history and editor-centric iteration are worse for a solo, agent-driven pipeline.
Unreal: heavyweight for stylised top-down 2.5D; C++ iteration cost high for this team model.
Bevy (Rust): attractive ECS purity, but ecosystem maturity for shipped desktop UI plus one-person Rust velocity judged riskier.
Custom engine: renderer and tooling cost would consume the project.

## Consequences
Easier: headless testing, CI determinism gates, agent iteration, potential engine swap survivability.
Harder: interop layer between .NET sim and Godot scenes must be designed once, carefully (Architect owns the snapshot API spec).
Committed to: sim purity rules in CLAUDE.md; performance validation at 500+ units in Phase 1 before content investment.
