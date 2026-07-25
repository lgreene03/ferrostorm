# CLAUDE.md: Project FERROSTORM Operating Rules

Internal codename: Project FERROSTORM. Provisional public title: **Ferrostorm** (pending clearance, docs/design/10-stage-a-report.md). Provisional resource name: **Ferrite**. The word "Cinder" must never appear in player-facing content, asset names, or public copy.

## Source of truth (highest wins)
1. docs/design/03-technical-design-document.md (TDD)
2. docs/design/02-game-design-document.md (GDD)
3. docs/design/01-personas-and-stakeholders.md
4. Your agent charter in .claude/agents/
5. Your own judgement. If it conflicts with any of the above, file a question in docs/questions/ instead of silently resolving it.

## Absolute rules (no exceptions, no exemptions for placeholders/tests/comments)
- **Legal:** No Command & Conquer, C&C, Red Alert, Tiberium, GDI, Nod, Westwood, or EA names, assets, sounds, or trade dress anywhere. Approved marketing formulation: "inspired by the classic RTS games of the 90s". OpenRA source is GPL: read for architecture research only; never copy code.
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
- Current phase: **P6 campaign build-out.** The deterministic core prototype shipped long ago: ADR-001 and ADR-002 are ratified, and the game is playable from source with green determinism CI. Phase B is complete (B1 to B6). **Phase C is largely complete and all of it is on main** (2026-07-25): stances, formations, the repair vehicle, sidebar cancel/refund, parallel build lanes, neutral outposts, destroyable bridges and the entire LAN stack (setup exchange, seat plumbing, lockstep-driven frame loop, Host/Join lobby, dropped-peer notice). **docs/tickets/P6-campaign-tracker.md is the resume point** and its status table is authoritative; prefer it over any prose in the design docs, several of which lag by whole waves.
- Still open, and all of it needs a human: C5 (air layer, needs an ADR and art), C6b wall gates (Luke must override ADR-005 clause 6), C8 multi-resource (Q014), C9 faction recipe (Luke's roster pick), plus the playtest of the packaged build and the two-machine LAN session.
- Standing determinism rule: cross-platform determinism is enforced by the golden-hash CI gate. If it ever cannot be held, halt and rethink rather than patch around it.

## Build commands
- Build sim + runner: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` (requires .NET 8 SDK; NuGet sources disabled by design - zero package dependencies)
- Full local gate: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` (selftest + double-run determinism + scenario battery + lockstep soak; exit 0 required)
- Modes: `selftest`, `determinism [seed]`, `golden [seed]`, `match [seed]`, `lan [games]`, `bench`, and more (see the header of sim/Ferrostorm.Sim.Runner/Program.cs)
- Client harness: `tools/verify-client.sh` drives the REAL battle scene headless from the joiner's seat and asserts on what it does (game/scripts/VerifyRunner.cs). **Run it for any /game change** - it has caught ten defects the sim battery is structurally blind to. Needs a Godot 4.7 mono editor; set `GODOT=` if yours is not at the default path.
- CI: .github/workflows/determinism.yml, three jobs, and ANY of them red blocks the merge:
  - `banned-tokens`: the sim purity grep, the ADR-004 portability grep, the hardcoded-seat guard (a literal seat in SkirmishLive.cs, which is invisible in single player and inverted for a LAN joiner) and the team-colour guard.
  - `determinism` (Windows + Linux): selftest, double-run determinism, the cross-platform golden-hash check (sim/golden-hashes.txt), scenario assertions, `lan 5`, `lanchaos`, `spectate`, `replay`, `saveload`, `campaignsave` and the balance gate.
  - `client-harness` (Linux): installs the Godot mono editor and runs the client harness above, ~2 minutes.
- Changing a golden hash is a replay-compatibility break: ADR + Architect sign-off required.

## Data conventions
- All gameplay numbers live in /data as YAML validated against /data/schema.unit.json (and sibling schemas as created). Hand-editing stats in code is forbidden.
- Keys: lower_snake_case. IDs: faction prefix `dir_` (Directorate) or `sod_` (Sodality), shared `com_`.
- Any stat change >15% requires Balance + Game Designer co-sign (charter A11).

## Agent roster
Charters live in .claude/agents/. Roles: producer, game-designer, architect, sim-engineer, netcode, client-engineer, ai-engineer, ux, art-pipeline, audio, balance, qa, tools, docs-community, legal-review. Full definitions: docs/design/04-agent-team.md.
