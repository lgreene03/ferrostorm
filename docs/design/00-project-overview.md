# Project IRONHARVEST
## Modern Classic-Style RTS - Project Overview and Documentation Index

**Working title:** Ironharvest (placeholder, original IP required - see Legal Constraints)
**Genre:** Real-time strategy, classic base-building style (Command & Conquer lineage)
**Status:** Pre-production / design phase
**Owner:** Luke (solo developer, agent-assisted development model)

---

## 1. Vision Statement

Build a modern RTS that recaptures the *feel* of the original Command & Conquer and Red Alert: fast to learn, fast to play, one resource, one sidebar, big explosions, asymmetric factions with personality - delivered with modern quality-of-life, deterministic multiplayer, first-class replays, and open modding.

The one-line pitch: **"The 1995 RTS you remember, built the way you'd build it in 2026."**

## 2. What "C&C-style" Means (Mechanical Definition)

The gameplay style is defined by mechanics, which are not protectable IP. The specific mechanics we are preserving:

1. **Sidebar construction.** Buildings and units are queued from a persistent sidebar, not by clicking on structures. Construction happens "off-map" and the completed building is placed anywhere in your build radius.
2. **Single-resource economy.** One harvested field resource (ours: "Ferrite", an industrial crystal (cleared of the compromised Cinder word-space, doc 10 s2)) collected by harvesters and returned to refineries. Credits tick in as loads are processed.
3. **Construction Yard + MCV.** The base grows from one deployable vehicle. Losing the Construction Yard is a strategic crisis, not instant defeat.
4. **Power as a system.** Buildings consume power; low power slows production and disables defences. Power plants are soft targets that create attack incentives.
5. **Tech tree via structures.** Building X unlocks units Y and Z. Capturable tech with Engineers.
6. **Asymmetric factions.** Two launch factions with genuinely different toolkits (one brute-force conventional, one stealth/unconventional), not palette swaps.
7. **Superweapons on timers.** Visible countdowns that force map action.
8. **Fast, lethal combat.** Units die quickly, aggression is rewarded, games resolve in 15-30 minutes.
9. **Shroud and fog of war.** Exploration matters; scouting is a skill.

## 3. Modernisation Pillars (what we change)

- **Quality of life:** multi-building queues, waypoints, attack-move, control group management, rally points, unlimited selection, smart-cast support powers, build hotkeys, camera bookmarks.
- **Determinism-first architecture:** lockstep simulation, replays and observer mode for free, server-relayed multiplayer, desync detection from day one.
- **Onboarding:** interactive tutorial, skirmish AI difficulty ladder, optional "commander assists" (auto-harvester replacement, low-power warnings).
- **Accessibility:** remappable everything, colourblind-safe faction palettes, scalable UI, screen-shake and flash toggles.
- **Modding and community:** data-driven unit/building definitions (YAML/JSON), map editor shipped at launch, Steam Workshop.
- **Spectator/creator features:** replays, observer UI with production tabs, casting tools.

## 4. Non-Goals (explicit scope exclusions for v1.0)

- No naval combat at launch.
- No FMV campaign at launch (motion-comic briefings instead; FMV is a stretch goal).
- No mobile or console ports in v1.0.
- No free-to-play economy, no microtransactions. Premium purchase, cosmetic DLC later at most.
- No 3+ factions at launch. Two factions, deeply asymmetric, properly balanced.

## 5. Legal Constraints

Command & Conquer, Red Alert, Tiberium, GDI, Nod, and all associated names, characters, logos, music, and art are EA intellectual property. **Mechanics and genre conventions are fair game; expression is not.** Hard rules for every contributor and agent:

- No C&C names, faction names, unit names, or story elements anywhere in the product.
- No assets, sounds, or music derived from C&C games, including the freeware releases.
- OpenRA's *source code* (GPLv3) may be studied for architectural reference; copying code imposes GPL obligations on our codebase - treat it as read-only research unless we deliberately choose GPL.
- All art, audio, and writing must be original or properly licensed.

## 6. Documentation Index

| Doc | File | Purpose |
|---|---|---|
| Project Overview | 00-project-overview.md | This document. Vision, scope, index. |
| Personas & Stakeholders | 01-personas-and-stakeholders.md | Who we build for, who has a stake, what each needs. |
| Game Design Document | 02-game-design-document.md | Full gameplay specification. |
| Technical Design Document | 03-technical-design-document.md | Architecture, engine, netcode, data model. |
| Agent Team Definitions | 04-agent-team.md | Every AI agent role, prompt charter, inputs/outputs, handoffs. |
| Production Plan | 05-production-plan.md | Phases, milestones, risk register, definition of done. |
| Backlog: Epics & User Stories | 06-backlog-epics-user-stories.md | Story-level breakdown ready for sprint planning. |

## 7. How to Use This Package

1. Read 00 → 01 → 02 in order to internalise the *why* and *what*.
2. Doc 03 is the contract every engineering agent works against.
3. Doc 04 is loaded as agent charters (e.g. Claude Code subagent definitions or CLAUDE.md sections).
4. Docs 05-06 drive scheduling. Nothing enters the backlog that doesn't trace to a persona need (doc 01) and a GDD section (doc 02).
