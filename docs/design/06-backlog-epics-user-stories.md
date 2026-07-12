# 06 - Backlog: Epics and User Stories

Stories are written from persona viewpoints (doc 01) with acceptance criteria (AC). Priority: P0 = vertical slice, P1 = alpha, P2 = beta, P3 = post-launch. This is a seed backlog; the Producer agent decomposes further into tickets.

---

## EPIC 1 - Deterministic Simulation Core (P0)

- **US1.1** As the developer, I need identical sim results across machines so that multiplayer and replays are trustworthy.
  AC: 20 seeded headless matches produce identical final hashes on Windows and Linux; CI blocks merges on mismatch.
- **US1.2** As a Ladder Climber, I want the game to detect desyncs immediately so a broken match never silently decides my rank.
  AC: hash check every 60 ticks; mismatch surfaces in-game within 5 seconds and dumps forensic state.
- **US1.3** As the developer, I need a headless match runner so agents can test gameplay without the engine.
  AC: CLI runs a scripted match from a command file and emits stats + final hash.

## EPIC 2 - Economy and Construction (P0)

- **US2.1** As a Veteran Commander, I want to queue buildings from a sidebar and place them when ready, so the game feels like the classics.
  AC: sidebar with tabbed queues; placement preview with valid/invalid feedback; build radius enforced.
- **US2.2** As any player, I want harvesters to behave sensibly without babysitting.
  AC: auto-return to nearest refinery; auto-reassign on refinery loss; no two harvesters deadlock on the same node in the standard test map.
- **US2.3** As a Weekend Skirmisher, I want a clear low-power state so I understand why my base slowed down.
  AC: power bar UI, audio alert, affected buildings visibly dimmed; production scaling per GDD §5.
- **US2.4** As a Newcomer, I want the game to warn me when I have no harvesters or refineries so I can recover.
  AC: distinct advisory alert with jump-to key; can be disabled in settings (P2 respects Vets who hate hand-holding).

## EPIC 3 - Combat Feel (P0)

- **US3.1** As a Veteran Commander, I want units to acknowledge orders with personality so my army feels alive.
  AC: ≥3 bark variants per order type per unit; instant local playback independent of command delay.
- **US3.2** As a Ladder Climber, I want attack-move, control groups, and remappable hotkeys so my hands aren't the bottleneck.
  AC: attack-move; groups 0-9 with add/steal; full remap UI persists across updates.
- **US3.3** As any player, I want to understand what beats what.
  AC: unit tooltips show strong/weak vs categories; in-game reference panel; damage matrix in shipped docs.

## EPIC 4 - Skirmish AI (P0 Normal, P1 full ladder)

- **US4.1** As a Veteran Commander, I want a Normal AI that plays a recognisable game (expands, techs, attacks) so skirmish feels like the old days but smarter.
  AC: AI executes doctrine build orders; attacks with composed armies; passes the benchmark in agent A7's DoD.
- **US4.2** As a Newcomer, I want an Easy AI I can beat while learning.
  AC: Easy loses to the tutorial-taught strategy ≥80% of test runs.
- **US4.3** As a Weekend Skirmisher, I want AI allies/enemies in team games that don't wander off.
  AC: AI honours team assignments, responds to ally-under-attack pings.

## EPIC 5 - Multiplayer (P1)

- **US5.1** As a Weekend Skirmisher, I want to host a lobby my friends can join in under a minute.
  AC: Steam invite → in lobby ≤60 s; map/faction/colour/team selection; host migration or graceful abort.
- **US5.2** As a Weekend Skirmisher, I want a dropped friend to rejoin the match.
  AC: reconnect within 3 minutes restores play from live state; others experience ≤5 s stall.
- **US5.3** As a Ladder Climber, I want ranked 1v1 with visible MMR so improvement is measurable. (P2)
  AC: Glicko-2 ladder; placement matches; seasonal reset policy published.

## EPIC 6 - Campaign and Onboarding (P1)

- **US6.1** As a Newcomer, I want a 15-minute interactive tutorial that teaches build, power, harvest, fight.
  AC: ≥80% of fresh testers complete unaided and then win vs Easy.
- **US6.2** As a Veteran Commander, I want campaign missions with scripted set-pieces and briefings so there's a reason to play solo beyond skirmish.
  AC: 3 missions/side at alpha with motion-comic briefings; objectives, triggers, and mission-complete flow.

## EPIC 7 - Replays, Observers, Creators (P1-P2)

- **US7.1** As a Ladder Climber, I want to rewatch any match with full information so I can study my losses.
  AC: every match auto-saves a replay; seek/speed controls; player-perspective toggle.
- **US7.2** As a Caster, I want an observer UI with economy and production tabs so broadcasts are informative.
  AC: live observer slots in lobbies; overlay panels; HUD-hide hotkey.

## EPIC 8 - Modding and Map Editor (P2)

- **US8.1** As a Modder, I want unit stats in readable text files so I can rebalance the game.
  AC: YAML data with schema validation and a docs page; invalid files produce line-level errors.
- **US8.2** As a Modder, I want a shipped map editor and Workshop publishing so my maps reach players.
  AC: editor creates valid maps incl. triggers; one-click Workshop upload; in-game browser.

## EPIC 9 - Accessibility and Settings (P1)

- **US9.1** As a colourblind player, I want distinguishable team colours.
  AC: colourblind-safe palette option passes design:accessibility-review checklist.
- **US9.2** As a Veteran Commander on a laptop, I want UI scaling and a performance preset so the game runs where I play.
  AC: 100-200% UI scale; low preset holds 60 fps on min spec.

## EPIC 10 - Services and Live Ops (P2-P3)

- **US10.1** As the developer, I want relay + ladder services deployable at hobby cost with a documented upgrade path.
  AC: infra-as-code; monthly cost estimate doc; load test at 200 concurrent matches.
- **US10.2** As any player, I want the game playable if the studio disappears.
  AC: direct-connect/LAN documented and release-tested (the preservation promise, doc 01 S8).

---

## Traceability rule

Every ticket derived from these stories must carry: `persona:`, `gdd:` (section), and `phase:` labels. The Producer agent rejects untraceable work.
