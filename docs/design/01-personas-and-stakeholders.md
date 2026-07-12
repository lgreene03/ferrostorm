# 01 - Personas and Stakeholder Analysis

Every feature must trace back to at least one persona need. Every decision with cost, legal, or community impact must identify affected stakeholders.

---

## Part A: Player Personas

### P1 - "The Veteran Commander" (primary persona)
- **Profile:** 38-55, played C&C/Red Alert/Dune 2000 in the 90s. Plays 3-6 hours a week, mostly evenings. Mid-range PC, sometimes a laptop.
- **Motivation:** Nostalgia with respect. Wants the *feel* back: the sidebar, the harvester rhythm, the tank rush. Deeply allergic to anything that feels like a lane-pusher or mobile game.
- **Skill:** Rusty but knowledgeable. Knows build orders conceptually, APM around 40-70.
- **Needs:** Skirmish vs AI as a first-class mode, classic control scheme available, pause-capable single player, readable UI at 100% scale, soundtrack that slaps.
- **Frustrations:** Grindy metaprogression, forced online, tutorials that patronise, tiny fonts.
- **Success signal:** Plays skirmish weekly for months; buys the soundtrack DLC.

### P2 - "The Ladder Climber"
- **Profile:** 18-30, comes from StarCraft II / AoE4 / Beyond All Reason. 10+ hours a week. High-refresh monitor, cares about input latency.
- **Motivation:** Competitive mastery. Wants a fair ladder, good matchmaking, deep but legible balance.
- **Skill:** 150-300 APM, learns the entire tech tree in a weekend.
- **Needs:** Deterministic netcode with low input delay, ranked 1v1 ladder, replays with full production data, hotkey editor, precise patch notes, no pay-for-power ever.
- **Frustrations:** Desyncs, balance patches that arrive quarterly, maphacks, smurfs.
- **Success signal:** Streams ladder sessions; files detailed balance feedback.

### P3 - "The Weekend Skirmisher"
- **Profile:** 25-45, plays comp-stomps and casual 2v2 with friends on Discord. 2-4 hours a week, always in a group.
- **Motivation:** Social fun and spectacle. Superweapons, big armies, dumb strategies that sometimes work.
- **Needs:** Reliable lobby system, drop-in co-op vs AI, adjustable AI difficulty, game speed options, reconnect support (a dropped player ruins the evening).
- **Frustrations:** Getting stomped by ladder players in quick match, lobbies that take longer than the game, friends on different patch versions.
- **Success signal:** Their four-person group makes it their default Friday game.

### P4 - "The Modder / Mapmaker"
- **Profile:** Any age, technical hobbyist. Made maps in the Red Alert 2 editor or mods for OpenRA.
- **Motivation:** Creation. Wants to add a unit, rebalance the game, build a tower-defence map.
- **Needs:** Data-driven unit definitions in plain text, documented file formats, shipped map editor, Steam Workshop publishing, stable mod API across patches.
- **Frustrations:** Binary-only formats, undocumented breaking changes, mods disabled in multiplayer with no alternative.
- **Success signal:** Workshop has 500+ maps within six months of launch.

### P5 - "The Newcomer"
- **Profile:** 16-28, never played a classic RTS; arrives via a streamer or a friend (often a P1 parent - the "dad game" pipeline).
- **Motivation:** Curiosity. Willing to give it one evening.
- **Needs:** A tutorial that teaches the loop in under 15 minutes, an AI that ramps sensibly, tooltips everywhere, "what beats what" reference in-game, low shame losing experience.
- **Frustrations:** Being expected to know genre conventions, opaque defeat ("what killed me?"), hostile ranked chat.
- **Success signal:** Completes the campaign's first act; queues a second session voluntarily.

### P6 - "The Creator / Caster"
- **Profile:** Streamer or YouTuber covering strategy games, audience 1k-100k.
- **Motivation:** Content that performs. Watchable games, story moments, tournament potential.
- **Needs:** Observer mode with production/economy tabs, replay downloads from match IDs, clean HUD toggle, no music-DMCA landmines (licence music for streaming explicitly), stable build for events.
- **Success signal:** Runs a community tournament without asking us for tooling favours.

---

## Part B: Stakeholder Analysis

### S1 - Owner / Solo Developer (Luke)
- **Stake:** Time, money, reputation, opportunity cost against other projects.
- **Needs:** Scope that a solo dev orchestrating AI agents can actually ship; ruthless prioritisation; costs (assets, infrastructure, tools) tracked from day one; documented upgrade paths rather than upfront spend - free-tier and open tooling first.
- **Risks to manage:** Burnout, scope creep, sunk-cost attachment to features.

### S2 - Players (all personas above)
- **Stake:** Money and time. Trust that the game will be finished and supported.
- **Needs:** Honest marketing, visible roadmap, patches that respect their saves and replays.

### S3 - Modding Community
- **Stake:** Their creative labour lives or dies on our format stability and API decisions.
- **Needs:** Deprecation policy, changelogs for data formats, a pinned communication channel.
- **Influence:** High leverage on longevity - OpenRA and the SC2 arcade prove community content extends game lifespan by a decade.

### S4 - Distribution Platform (Steam, likely GOG later)
- **Stake:** Revenue share, platform policy compliance.
- **Needs:** Store policy compliance (AI-asset disclosure requirements included), review-score health, Workshop integration done properly, Steam Deck verification as a marketing lever.

### S5 - Rights Holders (EA) - negative stakeholder
- **Stake:** Their IP. They do not need to approve mechanics, but they will act on trademark or asset infringement.
- **Mitigation:** Legal constraints in doc 00 are non-negotiable; trademark search on the final title; no nostalgic marketing that names their products in a confusing way ("inspired by classic 90s RTS" is fine, trade-dress mimicry is not).

### S6 - Contributors and Contractors (artists, composer, voice actors)
- **Stake:** Fair pay, credit, clear licences.
- **Needs:** Written contracts with IP assignment or licence terms; a style bible so work isn't wasted; disclosure policy on AI-generated content (some artists will decline mixed pipelines; Steam requires disclosure).

### S7 - Community Moderators (post-launch)
- **Stake:** Volunteer time and emotional labour.
- **Needs:** Moderation tooling, code of conduct they didn't have to write themselves, an escalation path to the developer.

### S8 - Infrastructure Providers
- **Stake:** Commercial relationship (relay servers, matchmaking, stats DB).
- **Needs/Constraints:** Architecture must keep server costs near zero at low population (relay-only lockstep, no authoritative simulation servers) - the game must remain playable via direct/LAN even if services shut down. This is both a cost decision and a preservation promise to S2.

---

## Part C: Persona → Feature Priority Matrix (summary)

| Feature area | P1 Vet | P2 Ladder | P3 Social | P4 Modder | P5 New | P6 Caster |
|---|---|---|---|---|---|---|
| Skirmish AI | ★★★ | ★ | ★★★ | ★★ | ★★★ | ★ |
| Ranked ladder | ★ | ★★★ | ★ | - | - | ★★ |
| Lobby/co-op | ★★ | ★ | ★★★ | ★ | ★★ | ★ |
| Replays/observer | ★ | ★★★ | ★ | ★ | ★ | ★★★ |
| Map editor/modding | ★ | ★ | ★★ | ★★★ | - | ★ |
| Campaign/tutorial | ★★★ | ★ | ★ | - | ★★★ | ★★ |
| QoL controls | ★★ | ★★★ | ★★ | ★ | ★★★ | ★ |

Reading: skirmish AI, QoL controls, and the tutorial serve the most personas and anchor the vertical slice. Ranked ladder is P2-critical but can follow at beta.
