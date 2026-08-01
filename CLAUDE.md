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
- Current phase: **P7, closing the parity gap to the benchmark games.** The analysis is docs/design/24-classic-parity-roadmap.md and **the plan and resume point is docs/tickets/P7-parity-tracker.md**, whose status table is authoritative; prefer it over any prose in the design docs, several of which lag by whole waves. That tracker also records, for every row it REFUSED, the argument that would have to be overturned to take it - so disagreeing with a refusal does not mean reconstructing why.
- P6 is behind this and its tracker (docs/tickets/P6-campaign-tracker.md) is history rather than a resume point. Of the items it listed as open, the air layer shipped under ADR-028 (bespoke art still owed), and the rest still need a human: C8 multi-resource (Q014). **C9's Q017 is answered** (P7-5a, ADR-042): the sides no longer share a power grid, and the sequencing question closed by taking its own first candidate. The useful part is why it need not have been a question at all - three of its four candidates were already WRITTEN in the GDD, and only per-faction refinery economics was genuinely unspecified. **C6b wall gates shipped** (P7-10): ADR-005 clause 6's blocker turned out to be scoped to SIMULTANEOUS per-player passability, which a gate with one global open/closed state does not need - so it was sidestepped rather than overridden, and clause 6 stands untouched with an amendment recording the distinction and its price (an enemy can follow you through an open gate).
- **What P7 needs from a human is now ONE thing, and the list shrinking is the useful part.** It used to name three: the design sentences for P7-8c, P7-11b and P7-11c, and a decision on regenerating golden hashes. All were authorised on 2026-08-01, every design call was taken and recorded reversibly in an ADR with its alternatives beside it, and in the event **no row has yet needed a regeneration** - eleven consecutive rows landed byte-identical across all 24 goldens, including three that the tracker predicted would move. What remains is **a PLAYTEST**: four-player free-for-all, six campaign missions, a LAN match on a four-seat map and two genuinely different faction economies all work, and nobody has played any of it. Every balance number in `/data` is therefore a guess that has passed a gate rather than a game.
- **A claim this file carried and that turned out to be false**, left visible because the correction is the useful part: it said "NOTHING in P7 is hash-neutral, because every row is a sim or catalogue change". Eleven rows have since landed byte-identical across all 24 goldens. The technique is nearly always the same and is worth knowing before starting a row: read a rule as the PROPERTY it means rather than the instance it names, and put optional per-entity state in a side collection folded into the hash only when present, so an absent entry contributes zero bytes. Assume a row moves hashes only after measuring it.
- Standing determinism rule: cross-platform determinism is enforced by the golden-hash CI gate. If it ever cannot be held, halt and rethink rather than patch around it.

## Build commands
- Build sim + runner: `dotnet build sim/Ferrostorm.Sim.Runner -c Release` (requires .NET 8 SDK; NuGet sources disabled by design - zero package dependencies)
- Full local gate: `dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` (selftest + double-run determinism + scenario battery + lockstep soak; exit 0 required)
- Modes: `selftest`, `determinism [seed]`, `golden [seed]`, `match [seed]`, `lan [games]`, `bench`, and more (see the header of sim/Ferrostorm.Sim.Runner/Program.cs)
- Client harness: `tools/verify-client.sh` drives the REAL battle scene headless from the joiner's seat and asserts on what it does (game/scripts/VerifyRunner.cs). **Run it for any /game change.** It keeps catching a class the sim battery is structurally blind to, and the reason is worth knowing rather than the running total, which only rots: it drives the real scene FROM SEAT 1, so any rule that is written as "me versus the other one" and happens to be right at seat 0 fails here. That class has included an inverted victory banner, a capture alert fired for a robbery, and a Brutal handicap that granted itself to a different seat on each LAN peer. The CI seat grep cannot see them, because `seat != LocalPlayerId` is exactly the shape it wants. Needs a Godot 4.7 mono editor; set `GODOT=` if yours is not at the default path.
- CI: .github/workflows/determinism.yml, three jobs, and ANY of them red blocks the merge:
  - `banned-tokens`: the sim purity grep, the ADR-004 portability grep, the hardcoded-seat guard (a literal seat in SkirmishLive.cs, which is invisible in single player and inverted for a LAN joiner) and the team-colour guard.
  - `determinism` (Windows + Linux): selftest, double-run determinism, the cross-platform golden-hash check (sim/golden-hashes.txt), scenario assertions, `lan 5`, `lanchaos`, `spectate`, `replay`, `saveload`, `campaignsave` and the balance gate.
  - `client-harness` (Linux): installs the Godot mono editor and runs the client harness above, ~2 minutes.
- Changing a golden hash is a replay-compatibility break: ADR + Architect sign-off required.

## Data conventions
- All gameplay numbers live in /data as YAML: units, buildings, fields, weapons and the AI's tuning. Hand-editing stats in code is forbidden.
- **This sentence was aspirational for a long time and is now enforced, which is the only reason it is safe to rely on.** Nothing read the schemas at all until `schemagate`, which now checks every authored key against `/data/schema.*.json` on each build; `data/weapons` and `data/ai` were empty while their numbers sat compiled; and `RegisterAll` did not register all, so a caller who forgot a kind got a partial catalogue and no error. If you add a `/data` kind: put it in `CatalogueFiles.DataDirs` (one table, read by both the registration loop and the unknown-directory guard, so an unrecognised directory is refused by name), give it a schema, and make the runtime READ it. Authored data that does not drive the runtime is this project's most-repeated defect, and a gate should prove the data wins over the compiled default rather than merely matching it.
- The sim keeps a COMPILED REFERENCE for each kind that the /data files must reproduce exactly. That is what lets a bare `World` with no /data behave identically, which roughly 138 runner scenarios depend on, and it is why authoring a kind is hash-neutral when transcribed correctly.
- **Anything that can differ between two LAN peers and change the command stream must be in `World.CatalogueChecksum`.** Moving a number from code into /data moves it from "agreed by construction" to "agreed only if checked" (ADR-032).
- Keys: lower_snake_case. IDs: faction prefix `dir_` (Directorate) or `sod_` (Sodality), shared `com_`.
- Any stat change >15% requires Balance + Game Designer co-sign (charter A11).

## Agent roster
Charters live in .claude/agents/. Roles: producer, game-designer, architect, sim-engineer, netcode, client-engineer, ai-engineer, ux, art-pipeline, audio, balance, qa, tools, docs-community, legal-review. Full definitions: docs/design/04-agent-team.md.
