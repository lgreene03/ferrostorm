# P8 tracker: a formidable, playable game

Authority: Luke, 2026-10-02, directed that the project "continue on and set up a
goal to make the game a formidable playable game on par with the classic RTS games
of the 90s", and that where anything is needed from the owner, a recommendation is
defined and taken without waiting for approval. This file is the PLAN and the resume
point if a session dies. The ANALYSIS is the eight adversarially verified lens
reports of 2026-10-02 (player-verb parity, opponent strength, match flow, balance,
feel, first session, stability and performance, open judgements); every row below
cites the finding ids it closes.

**This phase is not like P7.** P7 built systems sim-first and proved each one with a
gate that tested SIM ACCEPTANCE: a command constructed in C# and handed to `World`.
P7 is code-complete and every row is DONE or REFUSED, yet the verified lenses found
that a human cannot reach five of its headline systems (support powers, transports,
neutral capture, bridge felling, multi-seat targeting), that stealth does nothing on
screen, that the opponent is beaten by three aircraft, and that 42 per cent of AI
matches never end. P8 is the first phase judged FROM THE PLAYER'S CHAIR. A row is
DONE only when its behaviour is proved through the real input path (the gesture, key
or sidebar slot a human uses) or by a match-level measurement, never by sim
acceptance alone. The second difference is the hash profile: P8 opens with a long
run of client-only, hash-neutral rows (Wave A) that can be picked up in any order,
then moves into AI and economy rows that move the four commander goldens
(`skirmish`, `expansion`, `aisuper`, `mission`) and must be sequenced.

Standing law per wave, unchanged from P6 and P7: full battery exit 0, goldens
measured and not assumed, an ADR where a hash moves, a gate that proves the
behaviour, tracker updated, PR with green CI on both platforms. **Added for P8:** a
row that changes what a player can do also adds an `inputgate` stage that drives the
real gesture, and the row's "Player-reachable" claim is that stage's name.

## What formidable means (the DONE condition)

P8 is complete when every criterion below is met, each proved by the named gate,
probe or harness check. Gates marked (new) are created by the row in brackets.
Probes are non-asserting by ADR-061's gate-vs-probe rule, so a probe criterion is
met when its recorded figure is inside the stated bar and that figure is written
into this tracker.

| # | Criterion | Proved by |
|---|-----------|-----------|
| F1 | Every verb the opponent performs, the player can perform. All 19 CommandType verbs (values 2 to 20) and every ContactEffect are issued by a real gesture, with recorded exceptions only (Move is superseded by PathMove). | `inputgate` (new, P8-1) in tools/verify-client.sh, one stage per verb; "Player-reachable" column filled for every P7 and P8 row |
| F2 | The screen tells the truth. A fog-hidden enemy and an undetected cloaked enemy in a visible cell are neither drawn nor pickable; a detected one is; seats 2 and 3 on skirmish-09 obey fog and are attackable once visible. | `inputgate` stages from P8-4, P8-5, P8-6 |
| F3 | The opponent answers air. Three Strike Flyers raiding harvesters from t=4500 on skirmish-01 die, and at least one AI harvester survives, at Normal and Hard, both factions. | `aiairgate` (new, P8-13, asserting from P8-17) |
| F4 | Difficulty means something. Each rung beats the rung below in at least 70 per cent of decided games from BOTH seats over a fixed map set; an idle player on Easy survives past 8:00 (7200 ticks). | `laddergate` (new, P8-13, asserting from P8-26); `difficultygate` new stage (P8-26) |
| F5 | Start position does not decide the match. Normal mirrors of both factions on every 2-seat map, starts swapped: each seat's income within 15 per cent of the other, and the pooled win split no worse than 60/40. | `seatfairgate` (new, P8-13, asserting from P8-21) |
| F6 | Matches resolve. At least 70 per cent of the shipped-setup sweep (9 maps, 4 faction pairings, both orientations, Normal) end between 13500 and 27000 ticks (15 to 30 minutes, GDD pillar 2); every map outside that carries a recorded exception; no match runs to 27000 without either a result or the stalemate rule firing. | `pillarprobe` (new, P8-13); `endgate` (new, P8-13, asserting from P8-24) |
| F7 | The economy survives the superweapons. At least half of each map's ferrite fields are alive at minute 15 (13500 ticks) in every faction pairing on every standard map; a Sodality mirror keeps a field alive at t=9000. | `fieldsurvivalgate` (new, P8-19) |
| F8 | The superweapon is a climax, not an opening. Median first launch at or after minute 12 (10800 ticks) across `pillarprobe`; at most 5 launches per seat per 30 minutes. | `pillarprobe` figure (P8-18) |
| F9 | The opponent uses the game. In every AI match where a power building stands, support powers used is above 0; the AI fields at least 14 of the 20 unit types across the ladder sweep. | `aisupportgate` (new, P8-25); `ladderprobe` census (P8-28) |
| F10 | No unit is dominated. With the corrected balance tool (equal credits fielded, centred spawns, budgets 1800, 4800, 9000 and 12600, all 20 types, air paired against flak only), no unit loses every matchup at every budget. | `tools/Ferrostorm.Balance` matrix (P8-14, P8-32) |
| F11 | The faction war is a contest. Each faction wins at least 40 per cent of (map x seat) cells across skirmish-01, -02 and -04, played to 27000 ticks or a result, never adjudicated on banked credits. | `tools/Ferrostorm.Balance` faction war (P8-14, P8-33) |
| F12 | It does not break. Zero unhandled exceptions across a full-length (27000-tick) client match on skirmish-07; a sim fault halts with a banner and a fault report rather than a frozen battle; no sim tick over 8 ms at p999 on the large-map perf run, and the deterministic FlowField-build proxy inside its budget. | VerifyRunner long-match stage (P8-11); `longmatchperf` (new, P8-30, asserting from P8-31) |
| F13 | A newcomer's first session has no dead end. The tutorial mission cannot be won without building, powering, harvesting and producing; every mission has a briefing ending on an objective line and an in-game OBJECTIVES panel; no mission except the stealth mission is won by the starting force alone or by an idle player; every match ends on a results screen with RETRY; no main-menu button leads to a scene without an exit. | `campaigngate` new stages (P8-37, P8-38, P8-39); `inputgate` menu stage (P8-11, P8-34) |
| F14 | No protected names or phrasing ship. Zero hits for the banned franchise names and the ten protected announcer phrasings across game/, art/, data/, docs/design and the regenerated VO files. | `legalgrep` CI step (new, P8-2) |
| F15 | Nothing important happens silently. Every GameEventType the sim raises is either consumed by the client or listed in a "silent by design" table; selection and orders acknowledge audibly; the production chime plays only for the local player. | `inputgate` event-coverage stage (P8-10, P8-41) |

## Decisions taken

Each judgement below was surfaced by a lens and is DECIDED under the owner's standing
authority of 2026-10-02. Where a decision moves a gameplay number by more than 15
per cent, CLAUDE.md's rule that such a change needs Balance plus Game Designer
co-sign (charter A11) is **exercised under the owner's authority** and recorded
here; the implementing ADR repeats the record. Every decision states what would
reverse it.

**D1. Superweapon charge (ADR-044's refusal).** Decision: take GDD s8's ~6 minutes,
`SuperweaponChargeTicks` 1500 -> 5400 (+260 per cent, **A11 co-sign exercised**),
moved from a sim constant into /data. The support powers are DECOUPLED FIRST to
absolutes at today's values (charge 500, jam 166) so they do not triple with it. The
AI buys the superweapon only once it holds two refineries and an army of at least
one wave. Prerequisite stays the radar. Reason: built at t=2911 (Directorate) and
t=3016 (Sodality) in all 40 measured matches, first kill at 4621 before first
contact at 4635, 29 to 30 launches per 30-minute match deciding nothing (ML-05,
OJ-02). Reverses if: `pillarprobe` median first launch falls outside minute 10 to
16, or a playtest reads the weapon as absent.

**D2. Orbital cannon damage.** Decision: `OrbitalCannonDamage` 900 -> 2500 (+178 per
cent, **A11 co-sign exercised**), with `PrecisionStrikeDamage` pinned at an absolute
300 so the derived precision strike does not jump to 833. Reason: at 900 the cannon
needs three strikes (300 s today, 18 minutes after D1) to kill a refinery, while the
seismic charge at the same price erased 72k ferrite in one strike (BAL-07). Reverses
if: one strike per 6 minutes ends matches on its own in `pillarprobe`.

**D3. Seismic charge field kill.** Decision: a struck field is DRAINED TO ITS SEED
STOCK, never deleted, and ADR-012 regrowth resumes after one support-power charge
(500 ticks); the full drain applies within the 3-cell inner radius only, and the
outer ring to 6 cells halves the field's stock. The AI aims field denial only at
fields an enemy harvester or refinery is working, never twice running at fields.
ADR supersedes ADR-044 clause 5 and amends ADR-046. Reason: Sodality mirror ferrite
184503 -> 0 by t=6331, fields 16 -> 4 -> 0, 53 of 81 Sodality mirrors undecided
(AI-13, ML-02); GDD s4 says fields "regrow slowly from seed nodes". Reverses if:
`fieldsurvivalgate` passes with deletion restored, or a playtest finds denial
toothless.

**D4. Refinery docking.** Decision: harvester and structure-destination routes use a
multi-source flow field seeded at every passable cell adjacent to the footprint;
with no route, the harvester falls back to the next-nearest refinery; the AI keeps
a one-cell apron round refineries. Reason: one blocked south-east corner gave 0
credits in 3000 ticks with a harvester frozen at carry 700; p1 on skirmish-01
stuck 60 per cent of harvester-ticks (ML-01). Not reversible: this is a defect.

**D5. AI seat bias.** Decision: orient `TryFindPlacement`'s scan relative to the map
centre so seat 1's layout is the rotation of seat 0's, after D4 lands, then bisect
any residue. Reason: seat 0 took 141 of 189 decided unique matches; Directorate
seat 0 took 50 of 50 decided mirrors (AI-04); start swap moves the winner (BAL-01).
Reverses if: `seatfairgate` passes with the old scan restored.

**D6. Normal stays the identity rung of the KNOB TABLE, but the commander itself
improves at every rung.** Decision: producer scaling, staging, escalation and closing
behaviour apply at Normal; rung knobs (multipliers, producer caps, first-attack
tick) keep Normal at identity values so `difficultygate`'s identity assertion holds.
The four commander goldens regenerate, measured, under the ADR of each AI row.
Reason: Hard ties Normal 21 to 21 with 30 undecided; Hard banks 16401 credits at
540 s on one factory (AI-02, AI-03); a frozen Normal cannot be formidable.
Reverses if: never; the identity rule is about knobs, not behaviour.

**D7. Rung shape.** Decision: producer caps Easy 1, Normal 2, Hard 3, Brutal 3 plus
the existing 5000-credit handicap; Easy `first_attack_tick` 7200 and
`early_wave_scale` 0.5 (new schema fields, Normal at identity); Hard and Brutal gain
counter-composition and retreat below 30 per cent hp. Reason: Easy kills an idle
player at 5m55s against Normal's 5m25s (FS-05). Reverses if: `laddergate` shows a
rung inversion, or the playtest finds Easy still a high-shame loss.

**D8. AI variety.** Decision: a deterministic per-seat AI stream seeded from match
seed and seat at setup chooses one of 3 authored openings per personality, jitters
wave timing by up to 20 per cent and weights wave targets (refinery, harvesters,
production, weakest flank); the menu rolls a fresh seed per match; goldens keep
seed 2026 and regenerate once. Reason: seeds 2026, 7 and 99 byte-identical
(AI-05, ML-08); World._rng is never drawn. Reverses if: a replay desync traces to
the stream.

**D9. AI and cloak.** Decision: every rung stops counting undetected cloaked enemies
in its intruder census and target picks (decoys are real units and stay counted, by
ADR-067's own design). The full fog-honest commander with a last-seen table (DR-15)
is NOT in P8. Reason: the Sodality's defining mechanic does nothing against the AI
(AI-09) and the sim already drops explicit attacks on undetected targets
(World.cs:4806), so a census filter is the cheap half that makes cloak work.
Reverses if: the playtest finds the AI visibly aiming at unscouted ground.

**D10. AI and air.** Decision: the AI answers air (flak, earlier radar, escorts) but
does not fly in P8. Reason: AI-01 is the exploit; ADR-028 line 111 already records
the AI not flying. Reverses if: the playtest finds the air layer one-sided.

**D11. Splash and air.** Decision: all splash and every area effect, superweapons and
precision strike included, skip airborne victims, as ADR-028 clause 3 says. Reason:
12-hp half-damage hits on flyers from howitzer and bulwark splash (AI-11). Reverses
if: an ADR deliberately rules superweapons hit air.

**D12. Stalemate.** Decision: after 2700 ticks (3 minutes) in which no seat deals
damage, changes credits or completes production, the match ends as a DRAW, not an
adjudication by value, so banking is never rewarded. Reason: SS-01 last player
death 7921, last credit change 8035, then nothing forever (ML-07). Reverses if: the
rule fires in a match a human was still playing.

**D13. Q015, departure and surrender.** Decision: add `CommandType.Concede`, issued by
a Surrender menu item or a clean quit, applied on the lockstep tick and marking the
seat defeated; a dropped socket keeps today's "cannot continue". Q015 closes.
Reason: no surrender exists and 12 of 36 matches are unresolved (OJ-10). Reverses
if: LAN playtesting shows a clean quit misread as a concede.

**D14. Balance pass (rows after the tool is corrected).** Decisions, each verified by
the verification harness and re-measured with the corrected tool:
com_rocket_squad hp 80 -> 92 (+15.0 per cent, at the A11 line, recorded);
sod_shade_raider weapon -> wpn_vanguard_autocannon at its current cost 500 (a
weapon swap that changes its damage output by well over 15 per cent, **A11 co-sign
exercised**); sod_phantom_tank gains its own weapon at twice the rocket squad's
damage per shot instead of a price cut, because 750/260 measured insufficient
(**A11 co-sign exercised**); dir_bulwark_tank cost 1600 -> 1400 and hp 550 -> 680
(hp +23.6 per cent, **A11 co-sign exercised**), taking BAL-06's variant over
OJ-09's 1100/750 because BAL-06 was measured with centred spawns and OJ-09 by the
square law on the uncorrected tool; wpn_howitzer damage 60 -> 52 and cooldown
45 -> 50 (combined damage per second -22 per cent, **A11 co-sign exercised**);
wpn_commando_rifle damage 45 -> 100 and both commandos hp 200 -> 300 (+122 and +50
per cent, **A11 co-sign exercised**). Measured: rocket fix holds at every budget;
4 rifle squads (800 credits) kill a 1500-credit commando; howitzer balls dominate
at 9000 and 12600 (BAL-04, 05, 06, 09, 10). Reverses if: F10 fails after the pass,
in which case the failing unit is re-measured alone.

**D15. Orbital scan duration.** Decision: 75 -> 150 ticks (+100 per cent, **A11
co-sign exercised**). Reason: the brief itself asks whether 5 s is "a scan or a
flicker", and OJ-06 recommends about 150. Reverses if: the playtest reads 10 s as
a free map reveal.

**D16. Refinery serialisation (ADR-051).** Decision: stays REFUSED in P8; add the
measurement only (a dockprobe row with a one-slot dock at 3 and 6 harvesters).
Reason: the measured banking is the AI's, which P8-22 fixes; serialising an unplayed
economy is a 5x throughput cut stacked on it (OJ-03). Reverses if: after P8-22,
`economyprobe` still shows a second refinery buying under 10 per cent and the
playtest says it feels pointless.

**D17. Silo (ADR-041) stays refused; Q014 (second resource) is closed as considered
and declined.** Reason: banking is the AI's spending defect, and more income would
bury it (OJ-03, OJ-11). Reverses if: the conditions already recorded in ADR-041
and Q014 are met.

**D18. Bastion ticket.** Decision: no change in P8 until the corrected faction war
reports faction and seat shares separately (P8-33); then decide by that table.
Reason: the only evidence is one match counted six times (OJ-07, BAL-08). Reverses
if: P8-33 shows the Directorate under 40 per cent with the Bastion off-tier
mattering.

**D19. Wall tiers (ADR-061) stay refused; static defence is judged by an
equal-credit siege row that includes each side's faction defence.** Reason: the
siege comparison set 2400 credits of defence against 3000 of attackers, and buffing
the common turret against infantry would delete the counter triangle (OJ-08).
Reverses if: the equal-credit row still loses to every non-artillery besieger, in
which case the FACTION defences are re-tuned, never the common turret.

**D20. Q008, radar in missions.** Decision: scripted radar (`structure 0 12 x y`)
near the yard in missions 01, 03 and 04; the blackout stays in 02 and its briefing
says so. Q008 closes. Reason: the minimap is dark for missions 1 to 4 (FS-07).
Reverses if: the playtest wants the blackout as a teaching beat.

**D21. Campaign scope.** Decision: P8 makes the existing six missions honest (a real
tutorial, objectives, AI commanders where the mission claims an opponent, a framed
faction switch) and does NOT grow the count; 16 missions (GDD line 77) is recorded
as a release target. Reason: six missions are about an hour, but four of them are
won by doing nothing or by the starting army (FS-01, FS-04), which is the bigger
gap. Reverses if: the Producer pulls campaign growth into P8.

**D22. The classic control scheme (GDD line 83, doc 11 Scheme A).** Decision: build
it as a first-launch choice and a setting, both schemes routed through
`IssueOrder`. Reason: persona P1 per doc 11; dropped without a record (PV-11).
Not reversible without a GDD amendment.

**D23. Bridge felling gesture.** Decision: neutral bridges are attacked only by an
explicit force-attack gesture (Ctrl plus right-click), never by an ordinary move.
Reason: an accidental fell is irreversible (PV-03).

**D24. Team stealth.** Decision: the client treats a cloaked enemy as visible when ANY
seat on the local player's team has it in `DetectedMask`. Reason: World.cs:4231
discusses the team mask; allies share vision everywhere else (PV-01).

**D25. Replay Theatre.** Decision: the main-menu button is removed (REPLAYS already
plays back in the live view). Reason: it leads to a black scene with no exit (FS-02).

**D26. Voice and music.** Decision: the ten announcer lines are rewritten in original
phrasing and regenerated by `art/audio/make_vo.sh` with unchanged file names, two
synthesised faction timbres replace the one shared voice, and an interim procedural
score of at least six tracks replaces the 64-second loop pair. Commissioned music
and recorded human voice are NOT in P8 because they are SPEND, which the owner's
global rules keep for the owner. Reason: known issue (b), FEEL-11, FEEL-12.

**D27. GDD wording.** Decision: GDD lines 45, 83 and 93 are reworded to the approved
formulation ("the classic RTS games of the 90s"), Producer and Legal agent sign-off
exercised under the owner's authority, and docs/design joins the legal grep.
Reason: CLAUDE.md's legal rule has no exemption for internal documents (PV-ADJ-01,
FEEL-11).

**D28. Perf approach.** Decision: replace `FlowField.Build` with a parity-proven
Dial bucket queue (buckets drained in ascending cell index) and pool its arrays;
a budgeted or deferred rebuild, which moves goldens, is taken only if the
parity-proven version still fails `longmatchperf`. Reason: 152 ticks over 8 ms, max
65.87 ms, 144 of them on a flow-cache change (SP-01). Reverses if: parity cannot be
proved field-for-field on every shipped map.

**D29. Tower creep, the gate trap and formation spacing are MEASURED FIRST.**
Decision: no fix ships for AI-10 or PV-10 until a probe shows the harm; formation
spacing goes to 1.5 with a collision check (OJ-12). Reason: AI-10's claimed impact
did not reproduce; PV-10 is unmeasured end to end.

**D30. A per-faction anti-air defence structure (GDD line 55)** is filed as Q023 for
the Game Designer and is not built in P8; the AI's flak doctrine closes the exploit.

## Ordering principle

Rows are ordered by PLAY IMPACT per unit of work. A missing or unreachable system
outranks a weak one; a weak opponent outranks a missing unit; anything that blocks
release (legal) sits near the top. Client-only, hash-neutral rows that unlock
untestable systems come first, because they also unblock the playtest seventeen
ADRs are waiting on. Measurement comes before the change it judges: the AI harness
and the corrected balance tool land before any AI or balance row, so every later
row is judged by a number rather than an argument.

| # | Row | Source | Blocked on | Hash | Status |
|---|-----|--------|-----------|------|--------|
| P8-1 | `inputgate`: a VerifyRunner stage per CommandType and ContactEffect that drives the real gesture and asserts the sim effect; "Player-reachable" column added to this tracker and to P7's | PV-13, PV-00 | - | client-only, goldens NEUTRAL | pending |
| P8-2 | Legal: rewrite the ten announcer lines and regenerate, scrub synth.py:7 and :361, reword GDD lines 45, 83, 93, add a `legalgrep` CI step over game/, art/, data/, docs/design | known (b), FEEL-11, PV-ADJ-01; D26, D27 | - | none (assets and docs) | pending |
| P8-3 | The player fires support powers: power buttons with charge pips, targeting cursor, `UseSupportPower` issued, SupportPowerReady and SupportPowerUsed toasts | known (a), PV-12, OJ-06 | - (shipped ahead of P8-1 with its own harness stages) | client-only, goldens NEUTRAL | **DONE** (#144) - `SupportPowerBar` strip, one button per (building, power) reading READY, a countdown or ARMED; targeted powers arm a two-step pick, Radar Jamming fires on the press; key V arms and cycles; the Bastion's two buttons share one countdown so the scan-or-strike choice is legible. Also fixed a #143 defect: arming attack-move or patrol left an armed superweapon armed, so the next click LAUNCHED it; `DisarmAllArmedOrders()` now enforces the rule and all twelve ordered pairs are proved. 54 harness stages, proved to bite. The SupportPowerReady and SupportPowerUsed EVENT toasts are not in it and fold into P8-10 |
| P8-4 | Hostility by team, not `1 - LocalPlayerId`: `IsHostileSeat` via `World.TeamOf` in fog, picks, cursor; brown-out arrays sized by `PlayerCount`; `LocalSeat` reset on single-player start | PV-04, FEEL-04, SP-03, AI lens adjacent (MainMenu.cs:413) | P8-1 | client-only, goldens NEUTRAL | pending |
| P8-5 | Target picking honours fog: no Attack cursor or order on shrouded enemies | PV-02 | P8-4 | client-only, goldens NEUTRAL | pending |
| P8-6 | Stealth on screen: undetected cloaked enemies, Sodality buildings and mines hidden; own cloaked units translucent; detected enemies tinted; team detection mask | PV-01; D24 | P8-5 | client-only, goldens NEUTRAL | pending |
| P8-7 | Carrier verbs: right-click own Carrier to load, bindable Unload (U), cargo n/5 readout, Carrier excluded from select-army | PV-05 | P8-1 | client-only, goldens NEUTRAL | pending |
| P8-8 | Neutral targets: engineers claim outposts on 7 of 9 maps; force-attack fells bridges on skirmish-04 and -05; Enter cursor for every contact unit | PV-03, PV-12; D23 | P8-4 | client-only, goldens NEUTRAL | pending |
| P8-9 | Airfield production line, AIRCRAFT tab title and empty note, no-producer toast, Airfield rally point, stale Sidebar comments rewritten | PV-07, PV-08 | P8-1 | client-only, goldens NEUTRAL | pending |
| P8-10 | Critical events speak: superweapon charge bar, ready alert, enemy charge warning, impact reticle; Sabotaged alert and dim; jam distinct from radar loss (read-only `RadarJamEndsAt`); own-only production chime; one alert service with stacked toasts; Promoted and Deployed cues; event-coverage stage | FEEL-02, FEEL-03, FEEL-07, FEEL-09 (client half), FEEL-10, PV-12, PV lens adjacent (a) | P8-3 | client-only; the read-only sim accessor is goldens NEUTRAL, measured | pending |
| P8-11 | Fault containment round the tick drain (halt, banner, fault report with seed, setup and replay); atomic saves (.tmp then move, sidecar last); Replay Theatre button removed; full-length client match stage | SP-05, SP-07, FS-02; D25 | - | client-only, goldens NEUTRAL | pending |
| P8-12 | Records: P8 playtest brief with Match 3 now playable; close Q008 (D20), Q014 (D17), Q015 (D13), Q017, Q018; file Q023 (D30); refresh doc 24's parity table with the player-reachable column | OJ-11, OJ-12, FS-07, OJ-10 | P8-3 to P8-10 | none (docs) | pending |
| P8-13 | AI measurement harness in the runner: `ladderprobe`, `laddergate`, `aiairgate`, `seatfairgate`, `endgate`, `cheesegate`, `pillarprobe`, `fieldsurvivalgate` (gates registered non-asserting until their row lands); churnprobe column relabelled ms/4500 | AI-12, ML-06, OJ-05, ML-V1 | - | goldens NEUTRAL (additive, outside the golden list) | pending |
| P8-14 | Balance tool tells the truth: both orientations on skirmish-01, -02, -04 to 27000 ticks; /data and `PlaceSkirmishStart(8000)`; equal credits fielded; centred spawns; budgets 1800/4800/9000/12600; all 20 types with unitType, stealth and sight; air only against flak; adjudication ignores banked credits; stuck-harvester share; equal-credit siege row; refuses to write a report into the repository | BAL-01, BAL-03, ML-09, ML-V2, OJ-01 (1), OJ-08, BAL-11 (tool half), OJ lens adjacent (Program.cs:443) | - | goldens NEUTRAL (tools only) | pending |
| P8-15 | A refinery docks from every face: multi-source destination flow field, fallback refinery, AI refinery apron | ML-01; D4 | P8-13 | **goldens MOVE** (every harvester scenario); ADR | pending |
| P8-16 | Splash and area damage skip aircraft | AI-11; D11 | - | goldens NEUTRAL expected (no aircraft in any golden), measured | pending |
| P8-17 | The AI answers air: air-threat census, Flak Tracks into the cycle (enemy flyers plus one, capped), two held in garrison, harvester escort, radar pulled forward | AI-01, PV-06; D10 | P8-13, P8-16 | goldens NEUTRAL expected (branch fires only on enemy air), measured | pending |
| P8-18 | Superweapon pacing: support powers decoupled to absolutes (500, 166) first, then charge 5400 in /data, AI purchase gated on economy and army, orbital cannon 2500 with precision strike pinned at 300, scan 150, aisuper re-timed | ML-05, OJ-02, BAL-07 (part), OJ-06 (scan); D1, D2, D15 | P8-13 | decouple step NEUTRAL (values equal); then **goldens MOVE** (`superweapon`, `aisuper`, AI-driven) and **catalogue checksum MOVES**; ADR superseding ADR-044 clause 3 | pending |
| P8-19 | Seismic charge drains to seed and regrows; inner 3-cell full drain, outer ring halves; AI aims only at enemy-worked fields, never twice running | ML-02, AI-13, BAL-07 (part); D3 | P8-18 | **catalogue checksum MOVES**; goldens measured (every golden commander is Directorate); ADR superseding ADR-044 clause 5, amending ADR-046 | pending |
| P8-20 | The AI never goes inert on thin fields: MCV purchase and army hold gated on a distant field existing, MCV timeout (deploy in place or sell), sell lowest-value structure when broke with no harvester | ML-03, AI-06 (3, 4) | P8-15 | **goldens MOVE** if any golden reaches the state (byte-compare); ADR | pending |
| P8-21 | Seat fairness: placement scan oriented to map centre, residual bias bisected, map start asymmetries recorded per map | AI-04, BAL-01 (map half), OJ-01 (3); D5 | P8-15, P8-13 | **goldens MOVE** (four commander goldens); ADR | pending |
| P8-22 | The AI spends: extra barracks or factory under sustained surplus up to the rung cap, orders routed to the shortest queue, several Produce per beat when affordable, further faction defences from surplus, caps and reserve in data/ai | AI-03, ML-04, OJ-04; D6, D7 | P8-21 | **goldens MOVE** (four commander goldens); **catalogue checksum MOVES** (data/ai); ADR | pending |
| P8-23 | Waves escalate and close: staging point, launch at a growing threshold, retreat below 30 per cent, all-in when the enemy army is outnumbered, Rusher push of 4 to 6 infantry near 90 s, Turtle walls and extra defences; skirmish-04 mirror stall re-probed with pathdebug | AI-07, AI-06 (2), BAL-11, ML-04 (part) | P8-22 | **goldens MOVE** (four commander goldens); ADR | pending |
| P8-24 | Stalemate draw after 2700 idle ticks; `CommandType.Concede` from Surrender and clean quit; `endgate` asserting | ML-07, OJ-10; D12, D13 | P8-23 | goldens NEUTRAL expected (defeat folded under a guard), measured; save format bump only if persisted | pending |
| P8-25 | The AI fires support powers: scan before a wave, precision strike on harvesters or a clustered army, jam during a push, tunnel the garrison to a threatened refinery, decoys on the wave's flank; `aisupportgate` | ML-10, OJ-06, FEEL-09 (AI half), PV-09 (1), AI-08 (part) | P8-18 | **goldens MOVE** where a golden commander reaches a power building; ADR | pending |
| P8-26 | Difficulty that changes who wins: rung producer caps, Easy first attack 7200 and wave scale 0.5, Hard and Brutal counter-composition and retreat; `laddergate` and `difficultygate` idle-Easy stage asserting | AI-02, FS-05; D6, D7 | P8-22, P8-23, P8-21 | goldens NEUTRAL (Normal at identity knobs), measured; **catalogue checksum MOVES** (schema and data/ai) | pending |
| P8-27 | AI variety: per-seat deterministic stream, 3 openings per personality, up to 20 per cent wave jitter, weighted targets; menu rolls a fresh seed | AI-05, ML-08, OJ-01 (2); D8 | P8-26 | **goldens MOVE** (every AI-driven scenario, once); ADR | pending |
| P8-28 | The AI fields the roster: hero at radar tier, shade raider and vanguard car in the cycles, infiltrator or saboteur raids from Hard, mines at approaches | PV-09 (3 to 5), AI-08 | P8-26 | **goldens MOVE** unless gated beyond golden horizons (byte-compare); ADR | pending |
| P8-29 | The AI cannot count undetected cloak: census and target picks skip undetected cloaked enemies | AI-09 (part); D9 | P8-23 | goldens measured (move only if a golden holds a cloaked enemy near a commander); ADR | pending |
| P8-30 | `longmatchperf`: skirmish-07, -08, -09 (four seats), mean, p99, p999, max, first 30 ticks skipped, deterministic FlowField-build proxy asserted beside wall time on one CI runner | SP-02 | P8-13 | goldens NEUTRAL | pending |
| P8-31 | Parity-proven `FlowField.Build` replacement (ordered Dial buckets) with pooled arrays | SP-01; D28 | P8-30 | goldens NEUTRAL if parity is proved field-for-field; a budgeted rebuild would MOVE goldens and need an ADR plus Architect sign-off | pending |
| P8-32 | Unit balance pass under the owner's A11 exercise: rocket 92 hp, shade raider autocannon, phantom weapon, bulwark 1400/680, howitzer 52 dmg and 50 cooldown, commandos 100 dmg and 300 hp | BAL-04, BAL-05, BAL-06, BAL-09, BAL-10, OJ-09; D14 | P8-14 | **catalogue checksum MOVES**; **goldens MOVE** (rockets and howitzers appear in AI-driven goldens); ADR | pending |
| P8-33 | Faction war re-measured on the corrected tool; Bastion ticket decided by that table; equal-credit siege verdict recorded | OJ-07, OJ-08, BAL-08, OJ-01; D18, D19 | P8-32, P8-21, P8-14 | goldens NEUTRAL (measurement); any follow-up re-tune is its own row | pending |
| P8-34 | Results screen with stats, RETRY, NEXT MISSION, MAIN MENU; campaign progress persisted to user://; menu remembers the last MatchConfig; banner reads the live cancel binding | FS-09 | P8-11 | client-only, goldens NEUTRAL | pending |
| P8-35 | CONTROLS page generated from `Settings.Bindable` in pause and main menus; SETTINGS in the pause menu; group 0 and bookmarks 1 to 4 rebindable; strong and weak against lines in tooltips | FS-08, PV-14 | P8-1 | client-only, goldens NEUTRAL | pending |
| P8-36 | Classic left-click scheme as a first-launch choice and setting, both schemes through `IssueOrder` | PV-11, FS-08 (part); D22 | P8-35 | client-only, goldens NEUTRAL | pending |
| P8-37 | A real tutorial: MissionRunner built, produced and harvested conditions and a persistent hint action; mission-01 rewritten as stepped teaching; starting cannons removed (off its own allow-list) | FS-01, FS lens adjacent (mission 1 allow-list) | P8-34 | **`mission` golden MOVES**; new conditions NEUTRAL (MissionRunner state is outside the world hash); ADR, Architect sign-off | pending |
| P8-38 | Objectives: briefings 04 to 06 ending on an objective line, OBJECTIVES panel, key-to-text table (incl. ambush_sprung), extraction beacon and ping; scripted radar in 01, 03, 04; `Campaign.Load` keeps the faction column; mission 2 frames the side switch | FS-03, FS-07, FS-10 (framing), FS lens adjacent (GameFiles.cs:338); D20, D21 | P8-37 | **`mission` and `mission03` goldens MOVE** (radar); text NEUTRAL; ADR | pending |
| P8-39 | Campaign enemies fight back: mission-declared AI commander for enemy seats, missions 3 and 6 retuned, mission 3 given a yard or a trimmed allow-list, campaigngate stage failing any idle or starting-force win | FS-04 | P8-23, P8-38 | **`mission03` golden MOVES**; mission 06 has no golden; ADR | pending |
| P8-40 | Map picker shows name, size, seat count and thumbnail (name header for skirmish-03); front door: window title, smoke test behind a debug flag, TEAMS disabled on 2-seat maps, tooltips | FS-06, FS-11 | - | client-only; map checksum confirmed to ignore the header line, measured | pending |
| P8-41 | Units acknowledge: docs/design/19-audio-spec.md, synthesised bark set (3 variants per order per class per faction), `BarkFor` on its own player with debounce, drag-select sound | FEEL-01 | P8-10 | client-only, goldens NEUTRAL | pending |
| P8-42 | Refusals explain themselves: queue when unaffordable with INSUFFICIENT FUNDS, hero button greyed at cap, cap hold told from a blocked exit, Invalid cursor when nothing can engage air, one `Deny(reason)` with a deny cue | FEEL-05 | P8-10 | client-only, goldens NEUTRAL | pending |
| P8-43 | Death effects from sim identity (infantry puff, vehicle husk, multi-stage structure collapse); corpse tumble from a recorded Mobile flag | FEEL-06, SP-08 | - | client-only, goldens NEUTRAL | pending |
| P8-44 | Audio mix: announcer channel with priority queue, positional pool 24 to 32 with per-family caps, WeaponId sound table (rocket, flak airburst, rifle, heavy MG), promotion, deploy and placement cues | FEEL-08, FEEL-14 | P8-10 | client-only, goldens NEUTRAL | pending |
| P8-45 | HUD: themed panel, tick counter behind a dev toggle, F1 help overlay instead of the key strip, 22 missing icons from the Blender meshes, production toast names from the catalogue, only the producing factory animates | FEEL-13, SP-09 | P8-35 | client-only, goldens NEUTRAL | pending |
| P8-46 | Interim score: at least six procedural tracks of at least 3 minutes, shuffled, intensity-aware, crossfade kept | FEEL-12; D26 | - | client-only | pending |
| P8-47 | Warnings as errors under sim/ and the two dead CS0219 constants deleted | SP-11 | - | goldens NEUTRAL | pending |
| P8-48 | Client frame hygiene, MEASURED IN GODOT FIRST: snapshot buffer ring, per-frame allocation and NodePath churn, fog upload only on a new fog tick, effect pooling and camera culling | SP-04, SP-06, SP-10 | P8-31 | client-only (TakeSnapshot is a read, hash-neutral) | pending |
| P8-49 | Gate trap measured, then fixed client-side: route through an own gate when the destination is unreachable; open state drawn from `World.IsGateOpen` | PV-10; D29 | P8-1 | client-only, goldens NEUTRAL | pending |
| P8-50 | `towercreepgate`: powered enemy turrets on a harvester route; fix only if harvesters die | AI-10; D29 | P8-13 | goldens NEUTRAL (probe); any fix measured | pending |
| P8-51 | Formation spacing 1.5 with a collision check against the crowd-arrival settle | OJ-12; D29 | - | client-only | pending |
| P8-52 | P8 CLOSURE AUDIT: every F criterion re-run and its figure recorded here; playtest brief reissued | all | P8-1 to P8-51 | none (measurement and docs) | pending |

## What each wave is for

**Wave A (P8-1 to P8-12): make what exists reachable and truthful.** All client-only
and hash-neutral, so they can land in any order once `inputgate` exists. This wave
alone turns playtest Match 3 from unplayable to playable, removes two maphacks (fog
picking, seats 2 and 3) and one audio maphack (enemy production chime), and makes
the Sodality's cloak exist on the opponent's screen. It also clears the one
release blocker (P8-2).

**Wave B (P8-13, P8-14): measure before changing.** The verification harnesses
already exist in session scratchpads; this wave makes them runner modes so no AI or
balance row is ever judged by argument again.

**Wave C (P8-15 to P8-29): a formidable opponent and matches that play out.** Most
rows move the four commander goldens; each carries its ADR and a measured
regeneration. Order matters: docking (P8-15) before seat fairness (P8-21), because
docking is one cause of the seat effect; spending (P8-22) before waves (P8-23),
because escalation needs an army to escalate; the ladder (P8-26) only after both,
or `laddergate` would measure the old defects.

**Wave D (P8-30, P8-31): the large maps stop hitching.**

**Wave E (P8-32, P8-33): balance, on a tool that tells the truth.**

**Wave F (P8-34 to P8-40): the first session.**

**Wave G (P8-41 to P8-46): feel.**

**Wave H (P8-47 to P8-52): hygiene, measured-first residuals and the closure audit.**

## Deliberately not in P8

Each item is recorded so it is not re-raised; each names what would bring it back.

- **Naval, FMV briefings, crates, a map editor.** Carried from P7: out of scope until
  a GDD amendment with Producer sign-off.
- **Commissioned music and recorded human voice.** Spend, which the owner's global
  rules reserve for the owner. P8 ships an interim procedural score and synthesised
  faction voices (D26). Returns when the owner authorises the spend.
- **Campaign growth from 6 to 16 missions.** A release target, not P8 (D21). Returns
  if the Producer pulls it forward.
- **The fully fog-honest commander (DR-15, last-seen table, scouting).** P8 takes the
  cloak half only (D9). Returns if the playtest sees the AI aiming at unscouted
  ground.
- **AI air doctrine, AI transports, AI stances.** The AI answers air but does not fly
  (D10). Returns if the playtest finds those layers one-sided.
- **A per-faction anti-air defence structure.** Filed as Q023 (D30); the flak
  doctrine closes the exploit.
- **Refinery serialisation (ADR-051), the silo (ADR-041), a second resource (Q014),
  wall tiers (ADR-061).** All stay refused with their recorded overturning
  conditions (D16, D17, D19).
- **A free-list for dead entities (ADR-052).** Refused while `longmatchperf` passes;
  returns if P8-31 cannot meet F12 without it.
- **A budgeted or deferred flow-field rebuild.** Only if the parity-proven
  replacement fails F12 (D28), because it moves goldens.
- **A sim-side fog refusal of commands, and owner-passable gates in the flow field.**
  Both change what commands mean and move goldens; the client-side fixes in P8-5 and
  P8-49 are sufficient.
- **The Directorate's third support power.** Ratified as an asymmetry by ADR-070;
  returns only if a playtest reads the two-power kit as a shortfall.
- **Renaming `dir_turret` and `dir_superweapon` to `com_`.** Cascades into art; a
  wave of its own, as P7-1 recorded.
- **New units, factions or modes.** CLAUDE.md requires Producer sign-off; P8
  re-tunes and surfaces the existing 20 units and 22 structures rather than adding.

## Changed / Assumed / Needed next

**Changed.** New file: the P8 plan, built from eight verified lens reports. No code,
data or hash change.

**Assumed.** That "formidable" is judged against an informed player and reviewer of
the genre's classics, so the bar is behaviour a gate can check (F1 to F15) rather
than headcount parity; that the owner's standing authority of 2026-10-02 covers the
A11 co-signs recorded in D1, D2, D14 and D15 and the GDD rewording in D27; that
spend (music, voice) is not covered and stays the owner's.

**Needed next, and from whom.** Nothing blocks Wave A. The implementer starts at
P8-1. The owner is asked for nothing before P8-12 reissues the playtest brief; after
that, one 90-minute playtest settles the seventeen pending ADRs and checks D7, D9,
D12, D15 and D20, each of which names the playtest as its reversal condition.
