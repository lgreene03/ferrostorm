# ADR-003: Infantry as squads
- Status: Ratified
- Date: 2026-07-07
- Deciders: Game Designer + Architect agents, Luke
- GDD feature served: s7 unit roster; TDD s6 performance budget

## Context
GDD Q3: infantry as individually simulated soldiers or as squads. Affects entity counts, readability, balance granularity, and art/animation cost.

## Decision
Infantry are squads: one sim entity per squad, rendered as multiple figures by the presentation layer. Squad HP is a single pool; figure count shown scales with remaining HP for readability.

## Alternatives rejected
Individuals: 4-6x entity count for the same army value, worse silhouette readability at fixed zoom (design pillar 1), per-soldier micro rewards APM over strategy contrary to the game's identity, and higher animation cost per roster slot.

## Consequences
Easier: performance headroom, cleaner counters (anti-infantry weapons hit one armour class cleanly), cheaper art.
Harder: no per-soldier positioning flavour; flame/area weapons need a bonus-vs-squad rule instead of true multi-hit (Balance ticket when flame units land).
