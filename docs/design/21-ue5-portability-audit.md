# 21. UE5 Portability Audit: the studio spec vs what is built

Audit date 2026-07-14, against the commercial-RTS architecture spec
ratified alongside ADR-004. Verdict up front: the spec's core demand -
an engine-agnostic deterministic simulation with a disposable
presentation layer - has been Ferrostorm law since ADR-001 and is
CI-enforced. Zero engine references exist outside /game today. What
follows maps every spec section to reality, then lists the genuine gaps
as tickets so no future work designs a blocker.

## Spec-to-reality map

| Spec section | Status | Where |
|---|---|---|
| Three-layer split, sim knows no engine | DONE since ADR-001 | sim/ vs game/; CI purity + portability greps |
| Deterministic ticks/entities/commands/events | DONE | World.Step 15Hz, golden hashes, 24 gated scenarios |
| Data-oriented entities (ID/pos/owner/hp/state) | DONE | Entity struct array, fixed iteration order (TDD s3) |
| Component system | PARTIAL BY DESIGN | one struct carries component fields at this scale; the ADR queue holds the SoA/ECS split for when variety demands it. Components exist at the DATA level (UnitTypeDef/WeaponDef); a churn refactor now would risk determinism for dogma. UE5 mapping is unaffected - the sim moves wholesale. |
| Commands: move/attack/attack-move/stop/harvest/build/repair/capture, queued, shift | DONE | CommandType enum, Queued flag, wire+replay formats |
| Commands: patrol, guard | GAP -> P4-PORT-01 | |
| Units data-driven, no hardcoded stats | DONE | /data YAML validated against schema, selftest proves parity with compiled defs |
| Infantry/vehicles/workers/heroes | DONE (heroes = specials) | catalogue types 1-12 |
| Aircraft | GAP -> P4-PORT-02 (design ticket; movement layer is ground-grid) | |
| Buildings: construction/placement/footprints/power/queues/damage states | DONE except damage-state visuals (doc 18 Phase C) | |
| Upgrades / tech tree / research buildings | GAP -> P4-PORT-03 (sim tiers; client gating shipped; GDD sketches tiers) | |
| Resources: worker loop find/harvest/return | DONE | harvest state machine |
| Multiple resource types | GAP -> P4-PORT-04 (single ferrite today; loader is type-ready) | |
| Combat: damage/armour types, range, cooldowns, splash, death events; calc separated from FX | DONE | warhead x armour matrix; events feed FX |
| Projectiles as entities | DELIBERATE DIFFERENCE | resolution is hitscan-with-cooldown inside the deterministic step; visible projectiles are presentation (doc 20 Wave 3). Revisit only if gameplay needs dodgeable shells. |
| Pathfinding: click/groups/avoidance/separation | DONE | Dijkstra flow fields + separation buckets |
| Formations | GAP -> P4-PORT-05 | |
| "Future Unreal NavMesh replacement" | DELIBERATE REFUSAL | movement IS the deterministic sim; it must never be replaced by engine navigation, in Godot or UE5. The renderer only interpolates. |
| Fog of war, stealth, not tied to rendering | DONE | hashed bitgrids, public read API |
| AI through the command system, no cheating | DONE | SkirmishAI issues Commands only |
| Factions data-defined | DONE (locks, doctrine); adding a faction still needs AI doctrine code -> P4-PORT-06 documents the recipe | |
| Save portable, no engine serialisation | DONE | versioned custom binary + trailing magic, spec in doc 14; readable from C++ |
| Multiplayer prep: determinism, IDs, replays, server authority | DONE | lockstep relay, 20-game soaks, replay round-trips |
| Godot only renders/inputs/UI/audio | DONE | SnapshotInterpolator + Events are the whole contract |
| Migration mapping doc | THIS DOCUMENT + ADR-004 | |

## Godot-to-UE5 system mapping

Actor sync (SkirmishLive.SyncActors) -> AActor pool driven by a
UFerroSimSubsystem ticking the same C# world via .NET hosting or IPC;
ViewEntity rows map to actor transforms exactly as today. Models: the
baked glTF set imports directly (Interchange); turret child nodes arrive
as components, so ANIM tickets port unchanged. UI (Sidebar/Minimap/menus)
-> UMG widgets consuming the same public reads (Credits, QueueContents,
IsVisible). Fog shroud image -> a render-target material sampling the
same bitgrid reads. CombatEffects -> Niagara systems subscribed to the
same GameEvent stream. AudioDirector -> MetaSounds fed by the same WAVs.
Terrain visual classes -> landscape/static-mesh dressing from the same
map files. RtsCamera -> a Pawn with identical maths. Nothing in the
mapping requires a sim change: that is the whole point.

## Gap tickets (none are migration blockers; all are portable-surface work)

- P4-PORT-01 Patrol + Guard commands (sim: two CommandTypes + dispatch
  handling; wire/replay formats bump; goldens regenerate with sign-off).
- P4-PORT-02 Aircraft design study (separate movement plane over the
  grid; big; Architect ADR required before any code).
- P4-PORT-03 Sim tech tree per GDD tiers (prerequisites on
  Produce/BuildStructure from /data; replaces client-side gating as the
  authority; AI must respect it automatically).
- P4-PORT-04 Multi-resource support (field type id in map format,
  per-type credit pools or exchange rates; GDD decision first).
- P4-PORT-05 Formations (deterministic slot assignment on group orders).
- P4-PORT-06 "New faction from data" recipe doc + doctrine plug-point in
  SkirmishAI so faction addition is data + one doctrine class.
