# Project FERROSTORM (internal codename)

Modern classic-style RTS. Provisional public title: **Ferrostorm** (pending Stage B/C clearance, see docs/design/10-stage-a-report.md). Provisional resource name: **Ferrite**.

This repository is pre-implementation: full design package, agent charters, ADRs, data schemas, and Phase 1 backlog are in place. No production code exists yet by design.

## Start here
1. `CLAUDE.md` - operating rules for all agents
2. `docs/design/00-project-overview.md` - vision and doc index
3. `docs/adr/ADR-001-engine-choice.md` - the first decision to ratify
4. `docs/tickets/phase-1-backlog.md` - first work items

## Layout
- `/sim` - pure C# deterministic simulation library (empty until ADR-001/002 ratified)
- `/game` - Godot presentation project
- `/data` - YAML definitions (units, buildings, weapons, AI doctrines)
- `/tools` - balance simulator, map editor, replay inspector
- `/services` - relay, matchmaking, ladder
- `/docs` - design package, ADRs, open questions, tickets
