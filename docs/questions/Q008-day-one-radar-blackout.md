# Q008: is a day-one radar blackout the intended opening experience?

Owner: game-designer
Raised by: ADR-008 drafting, 2026-07-17 (doc 23 s4.2: "a bigger decision than
it looks", and one that must not be smuggled inside an "add a building"
ticket)
Decide by: 2026-07-24

## The question

ADR-008 ties the minimap to the Radar Uplink: no living uplink with supply
covering draw means a blanked minimap reading RADAR OFFLINE (GDD line 48,
"radar goes dark", plus GDD line 86's radar minimap). No shipped map spawns
any structure, so no map spawns a radar, and the opening base is an MCV and a
Construction Yard. The day the blackout lands, every skirmish and all three
campaign missions therefore begin with a permanently blank minimap until the
player builds a 900-credit building (data/buildings/com_radar_uplink.yaml:9)
that appears in no map, no mission script and no opening loadout. Is that
blank-until-you-build opening the intended experience, in every shipped map
and mission?

## Context

- It may well be correct genre behaviour: the classic RTS games of the 90s
  opened without radar and made the minimap a mid-game reward. But it is a
  large change to the opening feel of every mode at once, and it lands
  hardest in mission-01, the extended tutorial, and mission-02, which allows
  NOTHING buildable (campaign.txt:10) and would play its entire stealth
  scenario radarless with no remedy available.
- Saying yes means revisiting the four .fmap files and the three mission
  scripts as part of TICKET-P5-PWR-05, deciding per mission whether to grant
  a radar, script one, or accept the blackout as the mission's texture.
- Saying no has cheap shapes the ticket can implement: a scripted starting
  radar in missions, a grace period, or blackout-only-after-first-uplink.
  Each is a design statement; none should be an implementer's improvisation.
- Two recorded asymmetries ride the decision (ADR-008 clause 4): minimap
  pings still render while blanked, deliberately, so the base-under-attack
  alert is not stealth-nerfed; and the blackout binds only the human,
  because SkirmishAI reads `w.Entities` with no visibility filter,
  consistent with the AI already ignoring fog entirely. A GDD s5 rule that
  applies to one side is a decision and is recorded as one.

## Options considered

1. Yes, everywhere: blank minimap until an uplink stands, all maps, all
   missions. Purest reading of GDD line 48; hardest on the tutorial.
2. Yes in skirmish, curated in campaign: mission scripts grant or script
   radar coverage where the mission needs it (mission-02 almost certainly
   does). The .fmap and script edits land inside PWR-05.
3. No day-one blackout: the minimap blanks only after the player has ever
   owned an uplink (loss aversion instead of a locked feature). Cheapest on
   the opening, weakest reading of the GDD sentence.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** ADR-008's blackout mechanics (client-side gate at the minimap
  refresh, pings exempt, AI exempt) are settled by that ADR; this question
  is only about the opening experience and the mission content that follows
  from it.
- **Needed next (from the Game Designer):** an option by the decide-by date.
  TICKET-P5-PWR-05 ships the uplink and cannot ship the blackout without
  this answer (ADR-008 gates it explicitly).
