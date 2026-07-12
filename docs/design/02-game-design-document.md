# 02 - Game Design Document (GDD)

Version 0.1 - pre-production. All names are placeholders. All numbers are starting values for the balance simulator, not commitments.

---

## 1. Design Pillars

1. **Readable in one glance.** Any unit, building, or effect is identifiable at default zoom in under a second. If art and clarity conflict, clarity wins.
2. **Fast, decisive, generous.** Games resolve in 15-30 minutes. Losses feel explainable. Comebacks are possible (Construction Yard loss hurts but you can rebuild an MCV) but not free.
3. **Asymmetry with personality.** Factions differ in *how they think*, not just stat lines.
4. **The economy is the battlefield.** Harvesters are the most important units in the game. Protecting and raiding them is the core strategic conversation.
5. **Modern hands, classic heart.** Every QoL feature that doesn't change strategy is in. Anything that automates strategy (auto-micro, auto-build) is out.

## 2. Core Loop

Scout → expand economy (harvesters/refineries) → choose tech path → apply pressure or defend → convert an economic or positional lead into a base kill.

Second-to-second loop: watch sidebar queues, place buildings, group and move units, respond to alerts (harvester under attack, low power, superweapon countdown).

## 3. Factions

### Faction A - The Directorate (conventional superpower)
- **Fantasy:** Steel, doctrine, overwhelming firepower. The "GDI/Allies-feel" slot without any of their expression.
- **Identity mechanics:** Strongest armour and artillery; buildings are tough but expensive; power grid is centralised (fewer, bigger power plants = juicier targets); support powers are surgical (orbital scan, precision strike).
- **Weakness:** Slow, expensive, telegraphed. Poor at map control early.

### Faction B - The Sodality (insurgent network)
- **Fantasy:** Stealth, subversion, asymmetric warfare. The "Nod-feel" slot.
- **Identity mechanics:** Cloaked units and structures, hit-and-run vehicles, cheap infantry swarms, capture and sabotage tools (Engineers, saboteurs), decentralised power (many small generators). Support powers are dirty tricks (radar jamming, decoy army, tunnel deployment).
- **Weakness:** Fragile units, weak in open head-on fights, economy more raid-dependent.

Faction C (a tech-cult "act 3" faction) is a post-launch expansion candidate only. Documented so the data model reserves room for it; not designed further in v1.

## 4. Economy

- **Resource:** Ferrite crystal fields. Regrow slowly from seed nodes; fields near spawns are finite enough to force expansion by minute ~8.
- **Harvester:** 1,400 credits, unarmed, 700 HP. Carries 700 credits per load. Auto-returns to nearest refinery; player-assignable.
- **Refinery:** 2,000 credits, includes one free harvester. Processes a load in 8 seconds.
- **Design intent:** A player floats at 2 refineries / 3 harvesters on one base; expansion or raiding decides who out-produces whom. Harvester kills are worth roughly a light tank in tempo.
- **Secondary income:** Capturable neutral "Depot" structures on the map grant +15 credits/tick - the map-control incentive, replacing oil derricks in spirit.

## 5. Construction and Power

- **Sidebar build:** Two parallel building queues (structures / defences) and two unit queues (infantry / vehicles) per production-structure type, C&C3-style multi-queue with per-structure rally points.
- **Placement:** Completed structures placed within build radius of existing structures; Construction Yard projects the largest radius.
- **MCV:** Both factions can build replacement MCVs at the Factory once a Tech Centre exists.
- **Power:** Each structure lists draw; total supply vs draw shown as a bar. Below 100%: production speed scales down linearly to 50%, radar goes dark, defensive turrets go offline at <75%. Deliberate "sell power to sneak a superweapon" plays should be possible.

## 6. Combat Model

- **Damage system:** Warhead types (anti-infantry, anti-armour, anti-building, omni) × armour classes (none, light, heavy, structure) with a percentage matrix. Classic, legible, moddable.
- **Counters (soft, not hard):** Rocket infantry beat tanks per-cost; anti-infantry vehicles beat infantry; artillery beats static defence; fast raiders beat artillery and harvesters. Nothing is immune to anything.
- **Veterancy:** Three ranks from damage dealt; +HP/+damage, rank 3 self-heals. Rewards preserving units without snowballing hard.
- **Air:** Limited-count strike aircraft (airfield-slot model) plus a transport helicopter each. Air is a scalpel, not an army.
- **Stealth rules:** Cloaked units decloak on firing and near detectors; detectors are visible and killable. Every stealth tool has a public counter.

## 7. Units (launch roster - 12 per faction + shared)

Shared: Engineer (capture), Harvester, MCV, Transport helicopter.

**Directorate (sample):** Rifle Squad, Rocket Squad, Grenadier, Scout Car, Main Battle Tank, Heavy Tank (tier 3, slow monster), Artillery, AA Track, Strike Jet, Repair Vehicle, Commando (hero, one at a time), Mammoth-slot superheavy (tier 3 capstone, distinct original design).

**Sodality (sample):** Militia Squad, Rocket Cell, Flame Trooper, Raider Buggy, Scorpion-slot light tank, Stealth Tank (tier 3), Mobile Rocket Launcher, AA Trike, Venom-slot gunship, Saboteur (disables buildings), Infiltrator (steals intel/credits), Shadow Commando (hero).

Full stat sheets live in `/data/units/*.yaml` once the data format exists; the GDD defines role, tier, and counter relationships only.

## 8. Superweapons and Support Powers

- One superweapon per faction, ~6 minute charge, global map ping and audio warning at launch: Directorate orbital cannon (huge single-point damage), Sodality seismic charge (wide, lower-damage area denial that also destroys resource fields - economic warfare flavour).
- 3-4 minor support powers per faction on shorter timers, unlocked by structures.
- Design rule: every power has counterplay (spread out, scout the structure, kill it).

## 9. Modes

1. **Skirmish** vs AI, 1-7 opponents, difficulty ladder (Easy: no cheats, slow; Normal: competent build orders; Hard: strong macro, honest information; Brutal: resource handicap, clearly labelled as cheating).
2. **Campaign:** Two 8-mission campaigns (one per faction) with motion-comic briefings, scripted objectives, and gradual mechanic introduction. Mission 1 of each doubles as extended tutorial.
3. **Multiplayer:** 1v1 ranked ladder, unranked quick match, custom lobbies up to 4v4, co-op vs AI.
4. **Observer/replay:** every match recorded as a command stream; observer UI with economy/production tabs.

## 10. UX and Controls

- Grid hotkeys default, fully remappable; classic C&C left-click-select/left-click-order scheme AND modern RTS scheme both offered at first launch.
- Control groups 0-9 with steal/add-to modifiers, camera bookmarks F1-F4, select-all-military key.
- Alert system: harvester under attack, base under attack, low power, superweapon detected/launched - each with distinct audio and a jump-to-event key.
- Sidebar always visible on the right; tabbed (Buildings / Defence / Infantry / Vehicles / Aircraft). Radar minimap top-right above sidebar (the classic silhouette).
- Colourblind-safe player colours; team colours applied to consistent trim areas on every asset.

## 11. Art and Audio Direction (brief)

- **Camera:** Fixed-angle top-down with modest zoom range. 3D renderer, but composed and lit to read like idealised memory of 2D isometric sprites (strong silhouettes, painterly palettes, minimal visual noise on terrain).
- **Faction palettes:** Directorate = gunmetal/gold/blue glow. Sodality = dark red/black/sand with green glow accents. Never resembling GDI gold-on-black or Nod red-scorpion trade dress.
- **Audio:** Unit acknowledgements with personality (memorable barks are half the nostalgia), EVA-style faction announcer voices (original scripts), industrial/electronic hybrid soundtrack commissioned with explicit streaming-safe licence.

## 12. Balance Philosophy

- Patch cadence: monthly during beta, 6-weekly post-launch, with published notes and reasoning.
- Data-driven: all combat stats in text data files; a headless simulator (see TDD) runs scripted engagements per-cost to catch regressions before human testing.
- Asymmetry target: 48-52% faction winrate band at each skill quartile, measured from ladder telemetry.

## 13. Design Questions - Resolutions (Phase 1)

| # | Question | Resolution |
|---|---|---|
| Q1 | Unit cap | RESOLVED: no hard cap (classic). The economy and production speed are the throttle; revisit only if beta telemetry shows pathological blob play. Rationale: caps read as modern-RTS friction to the P1 persona, and the sim holds the perf budget with headroom. |
| Q2 | Build-radius rules | RESOLVED: strict classic adjacency - structures must be placed within the build radius projected by existing structures, Construction Yard largest. Rationale: base layout as strategic expression is core to the fantasy; loose RA2-style rules dilute expansion risk decisions. |
| Q3 | Infantry squads vs individuals | RESOLVED: squads - see ADR-003. |
| Q4 | Classic control scheme default on/off at first boot | OPEN: A/B in beta (unchanged). |
| Q5 | Campaign co-op | OPEN: decide at Alpha; cut unless free from architecture (unchanged). |
