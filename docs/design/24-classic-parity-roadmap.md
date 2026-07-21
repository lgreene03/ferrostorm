# 24. Classic Parity Roadmap: what the genre's benchmark games have that Ferrostorm does not

Author: game-designer + producer agents, 2026-07-17. Source: Luke's question
"what features are we missing?" against the classic RTS games of the 90s,
answered from the codebase as it stands at main ca481d3 and executed under
Luke's directive of 2026-07-17: "design out and build all these".

**STATUS, 2026-07-21.** Much of this roadmap has been built. Tier 3 (the faction picker, music, placeholder VO and contextual cursors) shipped as P6 Wave A. The Tier 1 ADRs were ratified under the directive and built as the P6 Phase B campaign (ADR-006 the /data runtime, ADR-007 rally, ADR-008 power, ADR-009 the production roster, ADR-011 the starting hand, ADR-012 ferrite regrowth). Read the tiers below with that delivery in mind; the out-of-scope list at the end still stands.

## How to read this

Three tiers plus an out-of-scope list. Tier 1 is designed and carried by an
ADR; building it is a matter of the ADR being ratified, which the directive
above supplies. Tier 2 is known and pointed at, but the design work is still
owed. Tier 3 is what THIS document exists to file: gaps that were on no list
until the parity comparison surfaced them. Every claim of absence below was
made by grep against the working tree, not by memory.

What already matches the classics, so nobody rebuilds it: the harvester
economy, construction yard with build radius, MCV deploy, per-building queues
with ready-and-place, selling, structure and vehicle repair, walls, partial
power rules, engineer capture, stealth and detection, veterancy with rank
pips, the superweapon with charge, launch-detection and impact alerts,
infantry crushing, attack-move (ADR-010), fog of war, minimap with pings and
jump-to-event, control groups, shift-queued orders, a three-mission campaign
with tech gating, skirmish against three AI presets, save/load (format v6),
and hash-verified replays, which the classics never had.

## Tier 1: designed, ratification supplied by the directive

Aircraft and the airfield (GDD line 55; sequenced behind the air-layer ADR
that Phase C of the campaign authors, per ADR-009's own exclusion). The
barracks split and the tabbed sidebar (ADR-009). Turrets offline below 75 per
cent power and the radar blackout (ADR-008). Sim-side rally (ADR-007). The
/data runtime source (ADR-006). The starting hand into the sim (ADR-011).

## Tier 2: known, design owed

Guard and patrol stances (P4-PORT-01). Formations (P4-PORT-05). A hold-fire
stance, which the sim cannot express at all: there is no way to tell a unit
not to shoot, and Q003 records the engineer-walking-past-a-sentry scenario
that needs it. Wall gates (ADR-005 clause 6, blocked on per-player flow
fields). Multi-resource fields (P4-PORT-04). Two-machine LAN (Q002's
remainder: the client frame loop blocks on AdvanceTick's Monitor.Wait).
The repair vehicle (GDD line 62; blocked on ADR-006 per doc 23). The
four-queue sidebar promise of GDD line 45, which even doc 23 Wave 6 does not
fully discharge.

## Tier 3: newly filed gaps

### TICKET-P6-FACTION-01. The faction picker

labels: persona:commander gdd:s4 phase:6 owner:client-engineer

`grep -in faction game/scripts/MainMenu.cs` returns nothing. The skirmish
menu offers theatre, opposition and treasury; no shipped map declares
factions (Q001); therefore no human being has ever played the Sodality in a
live skirmish, despite the sim gating its whole roster correctly and the
Veil Projector having a button since doc 23 Wave 1. In the classic genre the
faction choice is the first screen.

Design. A FACTION row in the skirmish menu (DIRECTORATE / SODALITY),
carried on MatchSetup, applied in BuildStartingWorld via `World.SetFaction`
for player 0 before tick 0, with the opponent's faction defaulting to the
other side. The Sidebar's structure AND unit buttons gain faction-gated
visibility mirroring the sim's own Produce and BuildStructure faction tests,
exactly as the Wave 1 veil button already does; the sim's checks remain the
authority and are unchanged. Client-only, hash-neutral: the golden scenarios
never touch the menu.

Acceptance: a Sodality skirmish shows the Sodality roster and builds it; a
Directorate skirmish is byte-for-byte today's game; the sim still refuses a
hand-crafted wrong-faction command; goldens byte-identical.

### TICKET-P6-MUSIC-01. The score

labels: persona:commander gdd:s7 phase:6 owner:audio + client-engineer

`grep -rin music game/scripts/` returns nothing; game/audio holds effects
and one wind bed. Half the identity of the classic genre is its soundtrack;
Ferrostorm is silent.

Design. A Music bus beside Sfx, Ui and Ambient (the AudioBuses idiom). Two
procedural layers synthesised through art/audio/synth.py, which already has
the primitives (sine, noise, band-pass, envelopes, the loudness discipline):
a calm bed and a combat layer, crossfaded by a combat-intensity signal the
client already possesses (recent Fired events involving player 0, decayed
over a few seconds). A MUSIC volume slider joins the settings scene. This
ticket ships the SYSTEM and an understated placeholder score; a composed
soundtrack is a later art decision and is said here so nobody mistakes the
placeholder for the destination.

Acceptance: music plays in a live skirmish, swells under fire, recedes in
peace; the slider moves the bus; muting it silences music and nothing else;
goldens byte-identical.

### TICKET-P6-VO-01. The battlefield voice

labels: persona:commander gdd:s7 phase:6 owner:audio + client-engineer + legal-review

The spoken battlefield computer is the classic genre's most recognisable
feel element and Ferrostorm has none of it: alerts are toasts and cues.

Design. A VO clip set in game/audio/vo/, played on the Ui bus ALONGSIDE the
existing toasts and cues, never instead of them: construction complete, unit
ready, unit lost, base under attack, harvester under attack, low power,
superweapon launch detected, silos needed nothing (omitted, no silo system),
mission accomplished, mission failed. A per-line cooldown map (the alert
cooldown idiom in SkirmishLive) so the voice never stacks or machine-guns.
Generation is a regenerable script (art/audio/make_vo.sh) using macOS `say`
to AIFF then afconvert to wav, so replacing the voice later is one command.
LEGAL CAVEAT, stated rather than buried: system text-to-speech output is a
PLACEHOLDER; redistribution licensing for Apple voices must be cleared by
legal-review before any public release build ships these clips, and the
regeneration script is the mitigation.

Acceptance: queueing a power plant ends in a spoken "construction complete";
losing a unit says so once, not eleven times for eleven walls; every line is
also still a toast; goldens byte-identical.

### TICKET-P6-CURSOR-01. Contextual cursors

labels: persona:commander gdd:s7 phase:6 owner:client-engineer + ux

Doc 18 N13 recorded it and nobody ticketed it: one OS cursor for every
context. The classic genre reads its whole verb system off the cursor.

Design. Small cursor PNGs (select, move, attack, harvest, enter, repair,
sell, invalid) generated procedurally by a python script in art/ (stdlib
only, matching the repo's tooling discipline), applied with
Input.SetCustomMouseCursor from a hover test the client already computes
pieces of (own unit, enemy in range, ferrite field, own structure, blocked
ground). Hotspot at the point. The sell and repair cursors show only while
the respective mode or key context is live.

Acceptance: hovering an enemy with combat units selected shows the attack
cursor; hovering ferrite with a harvester selected shows harvest; hovering
open ground shows move; the OS default never appears in a live battle;
goldens byte-identical.

## Ratification-gated sketches (sim changes, golden moves, NOT in Phase A)

### ADR-012 sketch: ferrite regrowth

`grep -n regrow sim/Ferrostorm.Sim/World.cs` returns nothing: fields deplete
permanently, capping match length and starving long sieges. Sketch: each
field regrows a small fixed amount per interval up to its spawn cap,
deterministically, in field-index order, only when a neighbouring cell
within Chebyshev 1 still holds ferrite (growth spreads from remaining
crystal, it does not resurrect a stripped field). Numbers to Balance under
A11. Moves goldens; one regeneration; formal ADR before implementation.

### Neutral tech structures

EntityKind.Outpost = 17 is already reserved (World.cs enum, Wave 2) and doc
22's P5-ECON-14 sketched capturable map structures. Sim change, ADR-gated,
Phase C.

### Destroyable bridges

Bridges are permanently open cells ('B' in the map grid, MapLoader). Making
them destroyable is a map-format and passability change; ADR-gated, Phase C,
and worth pairing with the gate work since both need incremental flow-field
repair.

### Transports, acknowledgement voices, crates, map editor

The transport helicopter rides with the air-layer ADR (GDD line 55). Unit
acknowledgement voices ride on P6-VO-01's pipeline once it exists. Crates
are GDD-silent and therefore need Producer sign-off before any ticket
exists. A map editor is GDD-silent; tools/ holds generators and the modder
persona's need is recorded; it stops there.

## Deliberately out of scope, so the comparison is honest

Naval combat, entirely: the GDD does not contain it, and adding it is a GDD
amendment with Producer sign-off, not a ticket. Full-motion-video briefings:
same. Neither is pursued under the current directive.

## Hash impact

Every Tier 3 ticket (P6-FACTION-01, P6-MUSIC-01, P6-VO-01, P6-CURSOR-01) is
client-only and hash-neutral; the acceptance for the wave is
`git diff sim/golden-hashes.txt` EMPTY. The ratification-gated sketches all
move goldens and each needs its formal ADR and its own regeneration under
the doc 23 section 6 discipline. Nothing in this document touches the
current baseline, which already includes ADR-010's legitimate regeneration.

## Changed / Assumed / Needed next

Changed: this document only.

Assumed: the directive "design out and build all these" covers the four
Tier 3 tickets immediately and supplies ratification for the Tier 1 ADRs;
GDD-silent items (crates, map editor) and GDD amendments (naval, FMV) stay
outside it. The VO voice is a placeholder pending the legal check.

Needed next (from whom): Phase A implementation of the four tickets
(client-engineer + audio, one wave); the formal ADR-012 text before any
regrowth code (architect); the legal-review check on TTS redistribution
before a public build carries the VO set; Balance numbers for regrowth under
A11 when ADR-012 is written.
