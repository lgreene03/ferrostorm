# CLAUDE.md — Project FERROSTORM Operating Rules

Internal codename: Project FERROSTORM. Provisional public title: **Ferrostorm** (pending clearance, docs/design/10-stage-a-report.md). Provisional resource name: **Ferrite**. The word "Cinder" must never appear in player-facing content, asset names, or public copy.

## Source of truth (highest wins)
1. docs/design/03-technical-design-document.md (TDD)
2. docs/design/02-game-design-document.md (GDD)
3. docs/design/01-personas-and-stakeholders.md
4. Your agent charter in .claude/agents/
5. Your own judgement — and if it conflicts with any of the above, file a question in docs/questions/ instead of silently resolving it.

## Absolute rules (no exceptions, no exemptions for placeholders/tests/comments)
- **Legal:** No Command & Conquer, C&C, Red Alert, Tiberium, GDI, Nod, Westwood, or EA names, assets, sounds, or trade dress anywhere. Approved marketing formulation: "inspired by the classic RTS games of the 90s". OpenRA source is GPL — read for architecture research only; never copy code.
- **Determinism:** No `float`/`double`, no `System.Random`, no wall-clock/locale/platform APIs, no unordered-iteration-dependent logic anywhere in `/sim`. No engine (Godot) references in `/sim`. Fixed-point maths only (library per ADR-002).
- **Scope:** If a task grows beyond its ticket, stop and report. Never expand scope silently. New units, factions, or modes require Producer sign-off.
- **British spelling** in all documentation and player-facing text. No em dashes or en dashes in any written content; restructure the sentence instead.

## Workflow
1. Work items are tickets in docs/tickets/ carrying labels: `persona:`, `gdd:`, `phase:`, `owner:`. Untraceable work is rejected.
2. Before starting a ticket, write a plan comment: approach, assumptions, interfaces touched.
3. Every deliverable ends with the standard footer: **Changed / Assumed / Needed next (from whom)**.
4. Architecture decisions require an ADR in docs/adr/ using ADR-000-template.md. Code that contradicts a ratified ADR is a defect.
5. Cross-agent questions go to docs/questions/ with owner and decide-by date.
6. Milestone gates (docs/design/05-production-plan.md) require sign-off from Producer, QA, Legal agents plus Luke.

## Phase gate status
- Current phase: **0 → 1 transition.** Phase 1 (Deterministic Core Prototype) may begin only when ADR-001 and ADR-002 are marked Ratified by Luke.
- Phase 1 kill criterion: if cross-platform determinism cannot be stabilised within the phase, halt and rethink architecture. Do not patch around it.

## Build commands
- Build sim + runner: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` (requires .NET 8 SDK; NuGet sources disabled by design - zero package dependencies)
- Full local gate: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` (selftest + double-run determinism + benchmark match; exit 0 required)
- Modes: `selftest`, `determinism [seed]`, `match [seed]`, `bench`
- CI: .github/workflows/determinism.yml runs the sim purity grep and the cross-platform golden-hash check (sim/golden-hashes.txt) on Windows and Linux. Red determinism CI blocks all merges.
- Changing a golden hash is a replay-compatibility break: ADR + Architect sign-off required.

## Data conventions
- All gameplay numbers live in /data as YAML validated against /data/schema.unit.json (and sibling schemas as created). Hand-editing stats in code is forbidden.
- Keys: lower_snake_case. IDs: faction prefix `dir_` (Directorate) or `sod_` (Sodality), shared `com_`.
- Any stat change >15% requires Balance + Game Designer co-sign (charter A11).

## Agent roster
Charters live in .claude/agents/. Roles: producer, game-designer, architect, sim-engineer, netcode, client-engineer, ai-engineer, ux, art-pipeline, audio, balance, qa, tools, docs-community, legal-review. Full definitions: docs/design/04-agent-team.md.
