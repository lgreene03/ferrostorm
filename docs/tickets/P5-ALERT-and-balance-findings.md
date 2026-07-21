# Play-test findings, 2026-07-16: alerts, AI aggression, superweapon economics

Source: a live play-test of skirmish-04 (Tarnwater Crossing, STANDARD, 8000)
followed by a multi-agent root-cause investigation. Six independent harnesses
(four investigating lenses, three adversarial sceptics) reproduced every number
below by execution. Seed-invariant across seeds 1, 2, 3, 7, 42, 1337, 99991 and
the default 2026.

The headline of the investigation is a NEGATIVE result worth recording so it is
not re-opened: **the DEFEAT was not a bug.** The STANDARD AI legitimately built
an army, walked it 187 cells and destroyed an idle player 0. VictorySystem,
SkirmishAI and the client DEFEAT path are all correct as written. Ablation
proves it: disable the AI's wave block alone and player 0's yard is still alive
at tick 6000; give player 0 the same AI and player 0 wins decisively. Idleness
was the deciding variable.

---

## TICKET-P5-ALERT-01 (DONE, 2026-07-16) - harvester under attack

labels: persona:commander gdd:s7-85 phase:5 owner:client-engineer

GDD s2 line 19 and s7 line 85 both name "harvester under attack" as an alert in
its own right. `SkirmishLive.cs` excluded the harvester from the only alert it
had, by the same test that (correctly) skips combat units. Measured consequence:
on skirmish-04 an idle player lost a harvester and three squads to nine AI waves
and the game never said a word, because the first alert that could fire needed a
STRUCTURE to be hit.

FIXED. Separate cooldown, distinct pitch, amber minimap ping, and a visible
toast (the previous alert was audio plus a ping only, with no on-screen text).
Verified live: "HARVESTER UNDER ATTACK" fires at tick 2668 with the harvester
still alive; "BASE UNDER ATTACK" fires at tick 3095. Client-only; the full
battery exits 0 and all 24 golden hashes are byte-identical.

## TICKET-P5-ALERT-02 - the rest of the GDD s7 line 85 alert set

labels: persona:commander gdd:s7-85 phase:5 owner:ux + audio

GDD s7 line 85 specifies four alerts, "each with distinct audio and a
jump-to-event key". State after the completion wave of 2026-07-17:

- harvester under attack: DONE (ALERT-01).
- base under attack: present.
- low power: DONE. P5-PWR-01 built the edge-triggered warning; this wave
  gave it its own cue (below).
- superweapon detected/launched: DONE (2026-07-17). The client alerts on
  `SuperweaponLaunched` by an enemy: "SUPERWEAPON LAUNCH DETECTED" toast, the
  hard klaxon, an orange minimap ping at the LAUNCH SITE, and the position
  recorded for jump-to-event. The event names the launching structure and the
  aim point and is emitted identically to every client, so a launch is global
  knowledge today - the classic-genre convention for superweapons, said in a
  comment at the handler. Verified live on skirmish-04: the STANDARD AI's
  launch raised the toast and the ping at tick ~3210, five seconds before the
  strike it warns of.
- distinct audio: DONE (2026-07-17). Two purpose-made cues from the audio
  pipeline (art/audio/synth.py): `alert_harvester`, a rising two-blip motif
  (335 ms, RMS -9.1 dBFS), and `alert_low_power`, a sagging descent (900 ms,
  RMS -11.7 dBFS), both peak-normalised to -3 dBFS like the rest of the set.
  The pitch-shift stopgap (1.28 and 0.82 on `alert_attack`) is gone. Base
  attack and launch detection deliberately share the hard klaxon: both mean
  drop everything, and a fifth voice would dilute the register rather than
  sharpen it.
- jump-to-event key: DONE (2026-07-17). New rebindable InputMap action
  `jump_to_event`, default Space (verified free in the [input] block), the
  21st entry in Settings.Bindable, so the settings scene lists it and the
  conflict detector covers it. Every alert site records the position it
  pinged; the key flies the camera there via `RtsCamera.FlyTo` (the
  minimap-jump idiom), the most recent alert wins, and with no alert yet
  this match the key does nothing and is not consumed. Verified offscreen:
  rebind round-trip Space to Home to Space through the shipping _Input path,
  and the camera arrived at the recorded launch site to within a tenth of a
  cell.

What remained of GDD s7 line 85's alert set was ONLY "radar goes dark",
and Wave B3 delivered it with the blackout it belongs to (2026-07-18,
ADR-008 clause 4): the minimap blanks to RADAR OFFLINE when no living
uplink stands with supply covering draw; the alert is edge-triggered on
the LOSS crossing only (the low-power pattern, flag starting false, so
the radarless opening is silent), with a toast, its own synthesised
`alert_radar` cue (a carrier fracturing into static - signal loss, not a
siren, audibly distinct from the whole set), the placeholder
vo_radar_offline line, a gold ping at the uplink, and the jump-to-event
record. Recovery relights the minimap. Verified live in the wave's
offscreen run. GDD s7 line 85's alert set is now complete.

## TICKET-P5-ALERT-03 - the toast is peripheral

labels: persona:commander phase:5 owner:ux

`ShowToast` renders top-right at OffsetTop 92, small and dim, for 2.85s. During
the play-test the tester was actively watching the screen and still missed it
until the capture cadence was tightened to 2s. Worth a UX judgement on placement
and weight for ALERTS specifically, as distinct from confirmations like
"ATTACK-MOVE ORDERED" which are correctly quiet.

## TICKET-P5-BAL-01 - STANDARD aggression curve on large maps

labels: persona:commander gdd:s6 phase:5 owner:balance + game-designer

Measured on skirmish-04 with zero player input: first wave launches tick 1725,
first contact 2466, starting units all dead by 3458, construction yard destroyed
and match over at 4192 (280 seconds). The AI reaches 7 structures by tick 1801
and earns 11919 credits by legitimate harvesting; it never cheats (audited, all
commands carry PlayerId 1).

This is not a defect. It is a question for Balance and the Game Designer:
is 9 units at tick 1725 the intended STANDARD curve, given the same AI would
land the kill far sooner on a 64x48 map, and given a new player's first act on
a big map is to look around?

## TICKET-P5-BAL-02 - the AI's superweapon purchase is anti-load-bearing

labels: persona:commander gdd:s6 phase:5 owner:balance + ai-engineer

STANDARD banks the 8000 start grant and buys a SUPERWEAPON at ~tick 1450 via
`SkirmishAI.cs:100`, striking at tick 3285 for 720 damage (24 per cent of the
yard's 3000 hp). Measured on all seven seeds: superStrikes = 1 every run.

The interesting part, found by ablation: **disable the superweapon purchase and
the AI kills player 0 550 ticks EARLIER** (3642 versus 4192), because it
reinvests the 4000 credits in units. The AI's own superweapon is a net loss for
it. Two separate questions fall out: whether a STANDARD AI should field a
superweapon at 96 seconds at all, and whether `SkirmishAI` should buy it when
measurement says it is worse than the units it displaces.

Also note: `SkirmishAI.cs:98`'s `refineryCount < cyCount` branch never fires at
cyCount 1, so STANDARD builds exactly ONE refinery, not the two an earlier
reading of the build order assumed.

## Recorded so they are not re-opened

- **Player 0 fired 9 shots to player 1's 289 during the match.** This is NOT
  gun suppression. A symmetric duel probe (one squad each, no AI, no orders)
  returned 9 shots each, both dead on the same tick. 9 is exactly one squad's
  shot budget before dying; the whole-match figure is an outnumbering artefact.
- **Splash is friend-or-foe by design** (`World.cs:1205-1221`, TICKET-P2-SIM-14).
  It contributed 9 of 4081 damage and none of it was player-0-on-player-0.
- **The yard "still on screen" at the DEFEAT frame is not a client bug.**
  `SkirmishLive.cs:1585` plays a 1.1s sink tween on a dead structure before
  QueueFree, and `OnEliminated` fires on the same tick the yard dies, so the
  actor is necessarily still visible at the DEFEAT frame. Working as designed.
- **Harness trap, for anyone investigating this sim in future.** `World.Step`
  calls `_events.Clear()` at `World.cs:601`, so a harness that records
  `evBefore = w.Events.Count` and iterates from there silently skips the first N
  events of every tick. One lens did exactly this and manufactured 68 phantom
  "hp changed with no combat event" ticks, which is precisely what an attrition
  or decay bug would look like. Iterate from 0.
- **Only two damage sites exist in the whole sim** (`World.cs:1284` pendingDamage
  and `World.cs:1717` ApplyAreaDamage). Both were instrumented and together they
  account for every hp point player 0 lost. That is a good structural property
  and worth keeping.

See also docs/questions/Q005-starting-hand-lives-outside-the-sim.md, which is
the one genuinely structural finding of the investigation.

---

**Changed:** SkirmishLive.cs harvester-under-attack alert (ALERT-01).
**Assumed:** combat units stay excluded from the alert deliberately; a player is
expected to be watching an army they sent somewhere, whereas a harvester is sent
away and forgotten. If Balance disagrees, ALERT-01's test is one line.
**Needed next (from whom):** Architect on Q005 (DONE: Q005 resolved by ADR-011 / Wave B5); Balance and Game Designer on
BAL-01/BAL-02; UX on ALERT-03. (The ALERT-02 cues Audio owed were delivered
2026-07-17; see the section above.)
