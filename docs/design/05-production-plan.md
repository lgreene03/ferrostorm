# 05 - Production Plan, Milestones, and Risk Register

Solo-developer, agent-assisted schedule. Durations are effort-honest estimates with 30% buffer already included; calendar time depends on Luke's weekly hours. Every phase ends with a gate: written go/no-go with criteria checked, signed by Producer + QA + Legal agents and Luke.

---

## Phase 0 - Pre-production (this package) - DONE when:
- Docs 00-06 reviewed and accepted by Luke.
- ADR-001 (engine) and ADR-002 (fixed-point) resolved.
- Working title trademark-searched.
- Repo skeleton + agent charters committed.

## Phase 1 - Deterministic Core Prototype (≈6-8 weeks effort)
**Goal:** Prove the hard technical bet before any content investment.
**Scope:** Sim library with movement, one weapon type, harvesting loop, fog; headless runner; determinism CI green; ugly-box renderer in Godot consuming snapshots; 2-client LAN lockstep demo.
**Gate criteria:** 500 units pathing at budget; double-run hash identical across Windows and Linux; LAN match completes with zero desyncs across 20 test games.
**Kill criterion (honest):** If determinism across platforms can't be stabilised in this phase, the architecture is rethought before proceeding - not patched around later.

## Phase 2 - Vertical Slice (≈10-12 weeks effort)
**Goal:** One map, both factions with ~6 units each, full core loop, real (placeholder-tier) art and audio, one AI difficulty. The "is this actually fun?" gate.
**Scope adds:** Sidebar UI, power system, construction, superweapon (one), skirmish AI Normal, alerts, save/load, replay recording.
**Gate criteria:** 10 external playtesters (recruit from RTS Discords); ≥7 finish a skirmish unaided; P1-persona testers spontaneously describe it as feeling "like the classics"; fun verdict from Luke.
**Kill/pivot criterion:** If the loop isn't fun with 6 units/faction, more units won't fix it - redesign at GDD level.

## Phase 3 - Alpha, feature-complete (≈16-20 weeks effort)
**Scope:** Full 12-unit rosters, both superweapons + support powers, all AI difficulties, map editor v1, 4v4 lobbies + relay service, reconnect, tutorial mission, 3 campaign missions per side, observer mode, settings/accessibility complete.
**Gate criteria:** All GDD v1 features present (quality may be rough); desync rate <0.5% of online matches; balance simulator green across roster; Steam page live with honest demo plan.

## Phase 4 - Beta / Early Access decision (≈10-12 weeks effort)
**Scope:** Content complete (both 8-mission campaigns), ranked ladder + matchmaking, Steam Workshop, localisation (EN first, FIGS if budget), polish pass, performance certification incl. Steam Deck, closed beta → open beta.
**Gate criteria:** Crash-free sessions >99%; ladder faction winrate within 45-55% band; moderation tooling + code of conduct live; refund-risk review of first-hour experience.

## Phase 5 - Launch and Live (ongoing)
- 6-weekly balance patches with published notes.
- Post-launch roadmap: map packs, co-op commander challenges, Faction C evaluation at +12 months based on sales.
- Preservation promise honoured: direct-connect play documented and tested every release.

---

## Budget lines to track from day one (S1 stakeholder requirement)

| Line | Nature | Notes |
|---|---|---|
| Art contracting | Largest spend | Style bible first so no wasted commissions |
| Music + VO | Fixed commissions | Streaming-safe licence written into contracts |
| Infra (relay, ladder, Postgres) | Small monthly | Free/cheap tiers first, documented upgrade path |
| Tools/licences | Minimal | Godot free; CI minutes; trademark search fee |
| Marketing | Deliberate | Steam fest demos > paid ads at this scale |

---

## Risk Register

| ID | Risk | Likelihood | Impact | Mitigation | Owner |
|---|---|---|---|---|---|
| R1 | Cross-platform determinism proves unstable | Med | Critical | Phase 1 is entirely this bet; fixed-point + analyser + CI from first commit; kill criterion defined | Architect |
| R2 | Scope creep (the RTS disease) | High | High | Non-goals list in doc 00; Producer veto; feature freeze at Alpha gate | Producer |
| R3 | IP/trade-dress infringement claim | Low | Critical | Legal agent sweeps every gate; original names/art; no EA references in marketing | Legal agent |
| R4 | Solo-dev burnout / stall | Med | Critical | Effort-honest schedule; phases end in playable states (motivation checkpoints); permission to pause pre-Alpha | Luke |
| R5 | "Fun gap": faithful but flat | Med | High | Vertical slice fun-gate with external testers; pivot budgeted | Game Designer |
| R6 | RTS market too niche for ROI | Med | Med | Costs staged behind gates; Steam wishlist targets checked at Alpha before big art spend | Producer |
| R7 | Asset pipeline (contractor) delays | Med | Med | Placeholder-complete policy: no feature blocks on final art | Art Director |
| R8 | Netcode fine on LAN, poor on real internet | Med | High | Jitter/loss harness in Phase 1; public beta stress test | Netcode |
| R9 | Mod API promises constrain engine evolution | Low | Med | Format versioning + deprecation policy published before Workshop opens | Tools |
| R10 | AI-generated asset disclosure/platform policy issues | Low | Med | Disclosure log maintained by Art Director; policy re-checked each gate | Art Director |
| R11 | Balance perception crisis post-launch | Med | Med | Telemetry + published reasoning; 6-weekly cadence commitment | Balance |
| R12 | Godot performance ceiling at 4v4 unit counts | Low-Med | High | Perf budget tested in Phase 1 at 500 units; sim/render split makes engine swap survivable | Architect |

---

## Definition of Done (project-wide)

A feature is done when: it traces to a GDD/persona requirement; sim parts pass determinism CI; it has tests; it works on Windows + Linux; UX copy reviewed; no placeholder legal-risk names remain; and its player-facing behaviour is documented.
