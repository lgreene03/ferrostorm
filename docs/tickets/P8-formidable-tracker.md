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
discusses the team mask, and detection is a team asset in the genre's team games.
**Corrected 2026-10-02 (P8-6):** the original reason said allies share vision
everywhere else; they do not (World.cs:6079, P7-8c gives allies no shared sight),
so fog stays the local seat's own and only DETECTION is pooled. P8-6 also found the
sim's `CanTarget` (World.cs:4244) accepts detection by the attacker's OWN seat only,
so an enemy only an ally detects is drawn and tinted but cannot yet be attacked;
offering the attack would recreate "targets my units refuse to shoot". **Decision
D24b, taken by the orchestrator:** make `CanTarget` team-wide (row P8-55, its own
ADR). Reason: it is what D24 meant, and in a two-seat match a team IS a seat, so every
golden is expected to stay byte-identical. Reverses if: P8-55 moves a two-seat
golden, which would mean the change is not the pure team generalisation it claims.

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

**D31. The scope of the legal rule (Q011).** Decision: the prohibition is absolute
for everything that ships or speaks to the player and for every working design
document, enforced by `tools/legalgrep.sh` in CI, with ONE exception: a document
whose purpose is to discuss the marks being avoided (clearance, legal analysis,
competitor research), listed by path with its reason in `tools/legal-allowlist.txt`.
The list holds two documents (doc 07, doc 09), and the check refuses an entry
outside docs/, a missing one and one that no longer names anything, so it cannot
grow quietly or rot. Every other usage is reworded, never listed. F14's phrasing
half is read as what a player hears or reads (the VO generator, data/, string
literals in game/scripts), not design prose, which names the alert features in
order to discuss them. Q011 closes. Reason: a mark cannot be cleared without being
named, every other usage in the inventory rewords at no loss of meaning, and a
rule nothing enforced had already been broken by a shipped asset (known issue (b),
PV-ADJ-01). Reverses if: legal-review finds a document whose function needs a
name, in which case the list grows by that one path with its reason; or counsel
rules the phrasing scope too narrow, in which case the check widens.

## Ordering principle

Rows are ordered by PLAY IMPACT per unit of work. A missing or unreachable system
outranks a weak one; a weak opponent outranks a missing unit; anything that blocks
release (legal) sits near the top. Client-only, hash-neutral rows that unlock
untestable systems come first, because they also unblock the playtest seventeen
ADRs are waiting on. Measurement comes before the change it judges: the AI harness
and the corrected balance tool land before any AI or balance row, so every later
row is judged by a number rather than an argument.

| # | Row | Source | Blocked on | Hash | Status | Player-reachable |
|---|-----|--------|-----------|------|--------|------------------|
| P8-1 | `inputgate`: a VerifyRunner stage per CommandType and ContactEffect that drives the real gesture and asserts the sim effect; "Player-reachable" column added to this tracker and to P7's | PV-13, PV-00 | - | client-only, goldens NEUTRAL | **DONE** - `inputgate`, a stage group in VerifyRunner.cs run by tools/verify-client.sh, in a fresh two-seat battle scene of its own driven from seat 1, so nothing an earlier check spawned can reach it. Fifteen verbs have a stage of their own driven by the gesture a player uses (keys, left and right clicks through `_UnhandledInput`, the sidebar buttons' own signals, the PLACE prompt), and every stage asserts both what the gesture queued and what the sim then did with it; UseSupportPower folds in #144's left-click stage rather than spawning every power building twice, and Move is the recorded exception. All four contact effects (Capture, Theft, Sabotage, Demolition) are driven by a click and a right click on an enemy building and read off the sim. LoadTransport, UnloadTransport, neutral-outpost capture, bridge felling, the Airfield rally and Airfield-only production are the six KNOWN-MISSING lines: one table, printed and never failed, each owned by P8-7, P8-8 or P8-9, whose job is to delete its line and add a stage. A coverage check fails any verb in neither place and any KNOWN-MISSING line a stage has overtaken. Proved to bite: dropping the right-click harvest order failed `inputgate/Harvest` alone (the client's auto-resume still sent the harvester to a field, so only the queued-command assertion caught it), and dropping Deploy's coverage mark failed the coverage check naming Deploy. 28 new ok lines, 282 to 310. Found on the way and left to its owner: a walk-in to a building is routed to the footprint's centre cell alone, so a building whose centre cell is boxed in cannot be reached from any face, and in the first run a saboteur stood still for 120 ticks beside a plant whose far face was open; that is D4's single-cell destination, P8-15 | `inputgate` itself: a stage per verb and contact effect, and the KNOWN-MISSING table |
| P8-2 | Legal: rewrite the ten announcer lines and regenerate, scrub synth.py:7 and :361, reword GDD lines 45, 83, 93, add a `legalgrep` CI step over game/, art/, data/, docs/design | known (b), FEEL-11, PV-ADJ-01; D26, D27 | - | none (assets and docs) | **DONE** (PR pending) - the ten announcer lines are rewritten in original phrasing (D26; vo_unit_ready's draft ended on a stock unit acknowledgement from the classics, so it became "Fresh unit on the field.") and regenerated by `art/audio/make_vo.sh` under unchanged names, 16-bit 44.1 kHz mono; the script's caveat now covers the words as well as the voice. `tools/legalgrep.sh` (patterns in `tools/legalgrep-patterns.txt`, the exception in `tools/legal-allowlist.txt`, D31) runs in the banned-tokens job and in `tools/ci-local.sh`, and fails on a protected name over game/, art/, data/, sim/, docs/ and the README, or on a retired phrasing in the VO script, data/ or a game/scripts string literal. Its first run found 64 hit lines in 19 files, all reworded: GDD lines 24, 29, 45, 83, 92, 93 and 106 (D27's three plus four more), docs 00, 01, 04, 05, 10, 13, 18, 20, 22, 27, 30 and 31, ADR-020, the phase 1 backlog, synth.py, and eight client strings, the five alert toasts among them, now worded to match the new lines. Two documents are allowlisted (07, 09). Proved to bite: a name planted in data/ and a phrase planted in a game/scripts literal both failed it, as did an untracked file, an allowlist entry outside docs/ and one naming nothing, while a comment quoting a phrase passed. ci-local green; verify-client PASS (282 checks); sim/ untouched. Q011 CLOSED. The CLAUDE.md Legal bullet restatement, which the implementing agent rightly declined to make on another agent's instruction, was made by the orchestrator under the owner's standing authority in the same change, and CLAUDE.md's banned-tokens description now names the legal check. Residual for P8-44, low severity: the new construction clip is audible for 1.68 s against its 1.5 s per-line cooldown, so two completions 1.5 to 1.68 s apart overlap by up to 0.18 s | pending |
| P8-3 | The player fires support powers: power buttons with charge pips, targeting cursor, `UseSupportPower` issued, SupportPowerReady and SupportPowerUsed toasts | known (a), PV-12, OJ-06 | - (shipped ahead of P8-1 with its own harness stages) | client-only, goldens NEUTRAL | **DONE** (#144) - `SupportPowerBar` strip, one button per (building, power) reading READY, a countdown or ARMED; targeted powers arm a two-step pick, Radar Jamming fires on the press; key V arms and cycles; the Bastion's two buttons share one countdown so the scan-or-strike choice is legible. Also fixed a #143 defect: arming attack-move or patrol left an armed superweapon armed, so the next click LAUNCHED it; `DisarmAllArmedOrders()` now enforces the rule and all twelve ordered pairs are proved. 54 harness stages, proved to bite. The SupportPowerReady and SupportPowerUsed EVENT toasts are not in it and fold into P8-10 | yes, `inputgate/UseSupportPower` |
| P8-4 | Hostility by team, not `1 - LocalPlayerId`: `IsHostileSeat` via `World.TeamOf` in fog, picks, cursor; brown-out arrays sized by `PlayerCount`; `LocalSeat` reset on single-player start | PV-04, FEEL-04, SP-03, AI lens adjacent (MainMenu.cs:413) | P8-1 | client-only, goldens NEUTRAL | **DONE** - `IsHostileSeat(seat)`, built on the sim's own `World.TeamOf`, is the one predicate the fog, the minimap and a shared `PickHostile` (the attack cursor and the right click alike) now ask; no game path reads `EnemyPlayerId`, which stays as the harness's two-seat convenience. Allies are drawn wherever they stand and are never offered as targets; visibility stays the local seat's own, because the sim gives allies no shared sight (P7-8c). The brown-out table is sized by `PlayerCount`, and `LocalSeat` is consumed by the scene that reads it, exactly as `PendingNet` already was, so a single-player match started after a LAN join takes seat 0 by every road into the scene, not only the menu's. Stages: on skirmish-09, four seats, free for all, seat 2's and seat 3's yards in fog are neither drawn nor dotted on the minimap, and with a seat-0 squad beside it seat 3's yard takes a right-click attack the sim holds; in the teamed world from seat 1, seat 2 (an enemy) is hidden in fog while seat 3 (the ally) is drawn and the cursor over it offers Move; and the four-seat scene, booted after the LAN joiner's seat-1 scene and handed no seat, takes seat 0. Picking an enemy that stands in fog is P8-5's half. Proved to bite: restoring `1 - LocalPlayerId` failed exactly the six hostility checks, and the minimap then drew 10 seat 2 and 3 dots through the fog; dropping the consume and sizing the table for two seats failed the seat checks and named seats 2 and 3 as missing from the table. 15 new ok lines, 310 to 325 | `inputgate/hostility` (the teamed world and skirmish-09) |
| P8-5 | Target picking honours fog: no Attack cursor or order on shrouded enemies | PV-02 | P8-4 | client-only, goldens NEUTRAL | **DONE** - the shared `PickHostile` (the attack cursor and the right click) now takes only an enemy the local seat is SHOWN, and its filter is the drawing predicate itself rather than a second opinion of it, so an enemy is pickable exactly where it is drawn. Under the shroud the cursor reads Move and a right click is a move, which closes the free maphack (and the LAN exploit) of sweeping the cursor over the fog to find an army and then marching straight to it. No sim-side fog refusal: the client fix is the one this tracker's out-of-scope list chose. Stages: in the inputgate scene the opposition's yard in fog draws the Move cursor and a right click queues a PathMove for every selected unit and no Attack, and the sim hands no unit that target; on skirmish-09 seat 3's yard in fog is likewise a move, the half of P8-4's stage that waited for this row; the control is P8-1's `inputgate/Attack`, where the same gesture on an enemy in sight is an attack. Proved to bite: taking the fog clause back out of the pick failed exactly the two fog-pick checks, each showing the Attack cursor and four Attack orders on a shrouded yard. 3 new ok lines, 325 to 328 | `inputgate/fog-pick` (two seats and skirmish-09) |
| P8-6 | Stealth on screen: undetected cloaked enemies, Sodality buildings and mines hidden; own cloaked units translucent; detected enemies tinted; team detection mask | PV-01; D24 | P8-5 | client-only, goldens NEUTRAL | **DONE** - the client draws cloak as the sim rules it. `ViewEntity` (SnapshotInterpolator.cs) carries `Stealth`, `FieldCloaked`, `RevealTicks` and `DetectedMask` under the sim's own names and integer types; nothing under sim/Ferrostorm.Sim changes. An undetected cloaked enemy in a visible cell, whether a unit, a Sodality building or a mine, is neither drawn, dotted on the minimap nor pickable; one that is detected, or has fired, is drawn under a Bone wash (doc 16's own pale) and is attackable; the player's own and an ally's cloaked things are drawn translucent. Detection is team-wide per D24, through one mask of the local team's seats. One half of D24 cannot be delivered without a sim change and is recorded rather than faked: the sim's `CanTarget` reads the attacker's own bit alone (World.cs:4244), so an Attack on an enemy only an ally has detected is dropped the tick it lands, which the harness proves; such an enemy is drawn and tinted as D24 rules, but the cursor offers Move until a detector of the local seat finds it, so the cursor never promises what the sim refuses. D24's premise that allies share vision "everywhere else" is not true of the sim either (World.FogSystem, P7-8c), so the fog stays the local seat's own. Stages: in the inputgate scene an enemy Phantom Tank, mine and Shroud Nest stand cloaked in cells this seat can see and are not drawn, dotted or pickable, and my own Phantom Tank is translucent; with a Sentinel Scout in range all three are drawn and the tank is tinted, dotted and taken by a right-click attack the sim holds; a Watch Post does the same for a second tank; in the teamed world an ally's scout puts an enemy tank on my screen, tinted, with the cursor offering Move and a hand-sent Attack dropped by the sim, and once my own scout detects it the right click attacks. Proved to bite: drawing that ignored cloak failed the six hiding checks, translucency switched off failed its check, reading only the local seat's bit failed the D24 check alone, and a pick without the local-seat rule failed the ally-detected cursor check alone. 19 new ok lines, 328 to 347. Reverses if a sim ADR makes `CanTarget` team-wide, when the pick drops its local-seat rule | `inputgate/stealth`, `inputgate/stealth-detected`, `inputgate/stealth-team` |
| P8-7 | Carrier verbs: right-click own Carrier to load, bindable Unload (U), cargo n/5 readout, Carrier excluded from select-army | PV-05 | P8-1 | client-only, goldens NEUTRAL | pending | pending |
| P8-8 | Neutral targets: engineers claim outposts on 7 of 9 maps; force-attack fells bridges on skirmish-04 and -05; Enter cursor for every contact unit | PV-03, PV-12; D23 | P8-4 | client-only, goldens NEUTRAL | pending | pending |
| P8-9 | Airfield production line, AIRCRAFT tab title and empty note, no-producer toast, Airfield rally point, stale Sidebar comments rewritten | PV-07, PV-08 | P8-1 | client-only, goldens NEUTRAL | pending | pending |
| P8-10 | Critical events speak: superweapon charge bar, ready alert, enemy charge warning, impact reticle; Sabotaged alert and dim; jam distinct from radar loss (read-only `RadarJamEndsAt`); own-only production chime; one alert service with stacked toasts; Promoted and Deployed cues; event-coverage stage | FEEL-02, FEEL-03, FEEL-07, FEEL-09 (client half), FEEL-10, PV-12, PV lens adjacent (a) | P8-3 | client-only; the read-only sim accessor is goldens NEUTRAL, measured | pending | pending |
| P8-11 | Fault containment round the tick drain (halt, banner, fault report with seed, setup and replay); atomic saves (.tmp then move, sidecar last); Replay Theatre button removed; full-length client match stage | SP-05, SP-07, FS-02; D25 | - | client-only, goldens NEUTRAL | pending | pending |
| P8-12 | Records: P8 playtest brief with Match 3 now playable; close Q008 (D20), Q014 (D17), Q015 (D13), Q017, Q018; file Q023 (D30); refresh doc 24's parity table with the player-reachable column | OJ-11, OJ-12, FS-07, OJ-10 | P8-3 to P8-10 | none (docs) | pending | pending |
| P8-13 | AI measurement harness in the runner: `ladderprobe`, `laddergate`, `aiairgate`, `seatfairgate`, `endgate`, `cheesegate`, `pillarprobe`, `fieldsurvivalgate` (gates registered non-asserting until their row lands); churnprobe column relabelled ms/4500 | AI-12, ML-06, OJ-05, ML-V1 | - | goldens NEUTRAL (additive, outside the golden list) | **DONE**: eight runner modes in `sim/Ferrostorm.Sim.Runner/Program.cs`, the investigation's vai and mlens harnesses promoted in the SHIPPED setup (/data through BuildWorld's configure hook, as SkirmishLive builds it), and every cited baseline **reproduced on today's AI**. Probes print and assert nothing; each gate prints PASS or WOULD-FAIL and exits 0 until its row sets its switch in `MeasurementHarness`, and `--bind` makes one run binding (`aiairgate --bind` exits 1). None is in golden, match, determinism, tools/ci-local.sh or CI; each takes key=value options for a subset (`maps=01 orient=0`), refuses an unknown one by name and prints its elapsed time; matches run in parallel and print in a fixed order (one thread and ten byte-identical). **`ladderprobe`**: all 360 per-match lines byte-identical to the recorded tables; 151 of 360 undecided; Hard over Normal 21 to 21 with 30 undecided, Brutal over Normal 25 to 19, Brutal over Easy 26 to 17; seat 0 took 141 of 189 decided unique matches (75 per cent), the Directorate in seat 0 50 to 0 in mirrors and 60 to 4 against the Sodality; seeds 7 and 99 byte-identical to 2026 (130 s on 10 threads). **`laddergate`** (F4, binds at P8-26): WOULD-FAIL over 216 matches to 27000 ticks: every rung clears 70 per cent from seat 0 (76.0, 80.8, 73.1) and none from seat 1 (37.5, 16.0, 33.3) (93 s). **`aiairgate`** (F3, P8-17): three flyers leave Normal and Hard with 0 harvesters, 0 credits and 0 Flak Tracks from t=7500 (6 and 7 lost, AI-01 figure for figure), the Sodality the same; WOULD-FAIL. **`seatfairgate`** (F5, P8-21): WOULD-FAIL; the start-0 side wins all 14 decided Directorate mirrors (pooled by start 14 to 2, by seat 8 to 8) and income stays within 15 per cent in 13 of 32 matches (gaps up to 289 per cent). **`endgate`** (F6, P8-24): WOULD-FAIL; 24 of 72 reach 27000 with no result. **`cheesegate`** (AI-12's flyer raid on the base, Vanguard Car harvester raid and tower creep; the tracker names no row, so it binds with P8-17): PASS today. Tower creep reproduces AI-10 and settles its open caveat: **the creep turrets were DARK** (seat 1 supply 0 against draw 60), so P8-50 must power them. **`pillarprobe`** (F6, F8): 26 of 72 in the window (36 per cent, bar 70); as authored 13 of 36 with 11 early and 12 unresolved (OJ-05 exactly); with the lens's Turtle and Rusher columns 21 of 54 with 14 early and 19 unresolved (ML-06 exactly); all 63 recorded mlens cells match per match on winner, end tick, first contact, build and launch ticks; superweapon built at t=2911 (Directorate) and t=3016 (Sodality) in all 144 seats, median first launch 4546 (bar 10800), launches per seat per 30 minutes median 11.3 and max 15.0 (bar 5), 29 and 30 in the unresolved skirmish-07 and -09 Directorate mirrors (27 s). **`fieldsurvivalgate`** (F7, P8-19): WOULD-FAIL; 16 of 36 keep half their fields at tick 13500; Directorate against Sodality keeps 6, 1, 1, 0, 0 fields at t=9000 on 01, 02, 03, 05, 06 (ML-02); the Sodality mirror holds 184503 ferrite at t=4500, then fields 16 to 4 at t=4741 and 3 to 0 at t=6331 (15 s). churnprobe's column relabelled ms/4500. **Found beyond the baselines**: the swapped orientation is an exact seat relabelling of the authored one in 34 of 36 pairs (both exceptions on skirmish-01, same start winning), so AI-04's seat-0 bias is a START bias, which is D5's diagnosis. Raiders spawn with their full catalogue definition (the vai harness used sight 5 and no veterancy); running both showed that moves no cited figure, only the idle-foe Sodality run's army from 3 to 2. Ground raiders muster on a cell with a route, because spawned blind three of four Vanguard Cars sat out the raid in a pocket seat 1's own base had sealed. ML-02's prose "14 to 16 fields" omits skirmish-02, whose recorded run has 13, as here. Decisions: F5 income is harvest deliveries plus the outpost trickle over the first 9000 ticks, each seat within 15 per cent of the smaller; "standard map" is the shipped skirmish pool; a raid is answered when the raiders die or the commander wins outright; a stage that measured nothing fails. CLAUDE.md's mode list is left to the owner (an agent brief cannot authorise that edit); the Program.cs header lists the modes | pending |
| P8-14 | Balance tool tells the truth: both orientations on skirmish-01, -02, -04 to 27000 ticks; /data and `PlaceSkirmishStart(8000)`; equal credits fielded; centred spawns; budgets 1800/4800/9000/12600; all 20 types with unitType, stealth and sight; air only against flak; adjudication ignores banked credits; stuck-harvester share; equal-credit siege row; refuses to write a report into the repository | BAL-01, BAL-03, ML-09, ML-V2, OJ-01 (1), OJ-08, BAL-11 (tool half), OJ lens adjacent (Program.cs:443) | - | goldens NEUTRAL (tools only) | **DONE** - `tools/Ferrostorm.Balance` rebuilt; CI runs `full`, now the default (17.6 s; `quick` 9.7 s; both measured 2026-10-02 at load average 36), exit 0. **/data** through `CatalogueFiles.RegisterAll` in every world (checksum 0xB4E6F043C4A872CC, identical to the compiled reference today); the faction war plays `PlaceSkirmishStart(8000)`. **Engagements:** all 20 types at 1800, 4800, 9000 and 12600, equal credits FIELDED (224 rows, every pair within one unit's cost, largest gap 300 credits, remainders printed, heroes capped at max_alive 1); a pair is a matchup only when each side can engage the other, so air meets flak only and the 8 unarmed types are listed, not dropped. **THE ROW'S FIND: a left-right mirror cannot be exact in this sim.** `Fix64` multiplication floors towards negative infinity, so the seat marching west diverged by one ulp from tick 2 and large mirrors went to either seat; the arena is mirrored across y = x instead (nothing negated, bit-exact to 63 v 63), and the mirror self-check passes: 28 uncloaked mirror runs ended in mutual annihilation, 12 cloaked ones made no contact. Every pair is also fought from both seats (4 rows SEAT-DECIDED, all commando v shadow commando: the sim's in-place reveal-on-fire favours higher ids) and along an axis (21 rows GEOMETRY-SENSITIVE, 13 of them howitzer rows at 9000 and 12600: the howitzer beats massed cannons along an axis and loses to them along the diagonal at every density tried); both count for neither unit, and idle survivors pursue so remnants cannot walk apart. **Matrix headline:** F10 NOT MET only through com_strike_flyer, 0-1 at every budget because its one matchup is its own counter (F10 as written cannot pass for it while air meets flak only; P8-32 must rule on that); no ground unit loses every matchup; dir_bulwark_tank 5-3, 6-3, 4-3, 4-3 (it loses only to cannon, rifle and rocket); dir_howitzer 0-9 at 1800 and 4800 and unsettled at 9000 and 12600 (7 and 6 rows geometry-sensitive); counter triangle 0 inversions in 52 cells, 11 geometry-sensitive; time-to-kill 0 rows outside 2 to 90 s. **Faction war (each faction in each seat, to a result or 27000 ticks, never adjudicated):** skirmish-01 Directorate (seat 0) beat Sodality t=13516 and Sodality (seat 0) beat Directorate t=11311, a SEAT effect; skirmish-02 Directorate (seat 0) v Sodality undecided, Sodality (seat 0) v Directorate won by the Directorate t=14266; skirmish-04 Directorate (seat 0) beat Sodality t=16696, Sodality (seat 0) v Directorate undecided. **Directorate 3/6, Sodality 1/6, undecided 2/6; seat 0 3/6, seat 1 1/6; F11 NOT MET.** Stuck harvester-ticks 4 to 88 per cent per seat; RNG drawn in 0 of 813 runs, so one run per cell is the population. Equal-credit siege (D19, 2800 credits of each faction's own gun): Bastions held against 9 of 10 besiegers, Shroud Nests against 4. Switches `F10Binding` (P8-32) and `F11Binding` (P8-33); F11 is judged only in full mode. A report path inside the repository is refused, exit 2, symlinks and the main checkout included. Doc 12 amended; doc 29 and the P7 playtest brief corrected with dated notes. **Bears on D14, not changed here:** its howitzer nerf rests on "howitzer balls dominate at 9000 and 12600", which the corrected tool shows depends on the angle of approach, and its bulwark buff answers a unit weak only against the line units | pending |
| P8-15 | A refinery docks from every face: multi-source destination flow field, fallback refinery, AI refinery apron | ML-01; D4 | P8-13 | **goldens MOVE** (every harvester scenario); ADR | **DONE** (PR pending) - ADR-071 (claimed in the open queue, Ratified under D4). `FlowField.BuildToFootprint` seeds every open cell of the one-cell ring round a footprint, in ascending cell order, through `Build`'s own relaxation (moved unchanged into `Relax`: that refactor alone moved no golden). `StepToward` uses it for the only two walks onto a building the sim has, a harvester in ToRefinery and a contact unit through `CanBeActedOn`, both derived from hashed state, so there is no new field and no save change. On a face the unit closes on the nearest footprint point, which never leaves its cell. A harvester with no route falls back to the nearest refinery it can reach (lower id on a tie, keeps its assignment if none), and `SkirmishAI.KeepsApron` keeps a one-cell clear apron round every own refinery without touching the scan (D5 stays P8-21's). There is no repair walk to route: Repair toggles in place and the depot order is a client PathMove. **`dockfacegate`** (new, in `match`): stage 1 is ML-01's fixture, a load banked at t=234 through the north-west corner; stage 2 seals every face, and the harvester falls back and banks at t=267; stage 3 sends a saboteur and an engineer onto a plant with its centre approach walled, and both act at t=58; stage 4, skirmish-01 both starts to t=9000, has a clear apron throughout and a longest freeze of 1 tick. Proved to bite: the old seed fails stage 1 with ML-01's own figure (ToRefinery, moving False, carry 700) and stage 3; no fallback fails stage 2; no apron fails stage 4; the old seed with no apron reproduces seat 1 frozen for 11961 of 19708 harvester-ticks (60 per cent, ML-01) and a 2446-tick freeze. **Seat figures, before and after**: `seatfairgate` income within 15 per cent goes from 13 to 22 of 32, and the pooled split by start from **14/2 to 8/8** (Directorate mirrors 14/0 to 6/6). It still WOULD-FAIL, on income only (10 of 32, worst skirmish-04 SS at 50 per cent). `ladderprobe` seat 0 goes from **141 of 189 decided (75 per cent) to 106 of 164 (65 per cent)**: the Directorate mirror goes from 46-0 to 22-6, the Sodality mirror from 14-14 to 16-8, and the Directorate beats the Sodality from either seat (59-3, 41-9), so the residue is P8-21's and P8-53's. `dockprobe` concurrency goes from 2/3/5/6 to 2/4/5/8 and six-harvester income rises 2 per cent (ADR-051 holds). **Goldens**: 7 of 24 moved, each attributed with its old and new hash in ADR-071, staged so each move has one cause. The routing moved `economy` and the commander goldens `skirmish`, `expansion`, `aisuper` and `mission`, plus the contact walks in `capture` and `mission02` (their engineers now take the west face, 7 ticks sooner). The fallback moved none further. The apron moved the four commander goldens again. The `skirmish` golden had certified seat 1 frozen for 57 per cent of its harvester-ticks (now 43 ticks), and `expansion` 27 per cent. `mission03` holds because its harvester never routes to a refinery and it has no yard. Side effect flagged: mission-01's victory moves from t=3411 to 4541 through the apron's placement shift (routing alone: 3321), still inside campaignsave's horizon. Found and recorded: the `expansion` golden's four refineries all stand at the home base, although its report line says the second base added one. ci-local green, determinism double-run identical | pending |
| P8-16 | Splash and area damage skip aircraft | AI-11; D11 | - | goldens NEUTRAL expected (no aircraft in any golden), measured | **DONE** (PR pending) - every site in World.cs that damages more than one victim now skips an airborne one through the existing `World.IsAirborne` predicate, where D11 is recorded once: weapon splash in the combat pass, `ApplyAreaDamage` (the orbital cannon, and the mine, which shares it), `ApplySeismicCharge` and `ApplyPrecisionStrike`. Combat.cs applies no damage. Each check sits after the radius test, and the aimed shot is untouched. `airgate` gains stages 6 to 9, each area stage standing an unarmed ground twin with the flyer's own armour and hit points at the flyer's own point and requiring it to be hurt, so no stage can pass by the blast missing: one howitzer shell under a Strike Flyer, twin -12 hp, flyer 0 (before the fix the flyer lost 12, AI-11 reproduced exactly); orbital cannon twin -720, flyer 0; seismic charge twin -280, flyer 0; precision strike twin -240, flyer 0; and one ordered flak shot still lands its full 30 on the flyer. Proved to bite at all four sites: removing the check at each made exactly its own stage fail (splash 12, orbital 720, seismic 280, precision 240) with the stages before it still passing. Goldens NEUTRAL, measured: `golden 2026` diffs empty against sim/golden-hashes.txt, all 24 in order; ci-local green. The seismic stage goes beyond the three area stages the row named because its site changed too. AI-11's "bulwark splash" has no source in /data, since the bulwark cannon authors no splash and the howitzer is the only weapon with a radius, so the one splash site covers the finding whatever it observed | pending |
| P8-17 | The AI answers air: air-threat census, Flak Tracks into the cycle (enemy flyers plus one, capped), two held in garrison, harvester escort, radar pulled forward | AI-01, PV-06; D10 | P8-13, P8-16 | goldens NEUTRAL expected (branch fires only on enemy air), measured | pending | pending |
| P8-18 | Superweapon pacing: support powers decoupled to absolutes (500, 166) first, then charge 5400 in /data, AI purchase gated on economy and army, orbital cannon 2500 with precision strike pinned at 300, scan 150, aisuper re-timed | ML-05, OJ-02, BAL-07 (part), OJ-06 (scan); D1, D2, D15 | P8-13 | decouple step NEUTRAL (values equal); then **goldens MOVE** (`superweapon`, `aisuper`, AI-driven) and **catalogue checksum MOVES**; ADR superseding ADR-044 clause 3 | pending | pending |
| P8-19 | Seismic charge drains to seed and regrows; inner 3-cell full drain, outer ring halves; AI aims only at enemy-worked fields, never twice running | ML-02, AI-13, BAL-07 (part); D3 | P8-18 | **catalogue checksum MOVES**; goldens measured (every golden commander is Directorate); ADR superseding ADR-044 clause 5, amending ADR-046 | pending | pending |
| P8-20 | The AI never goes inert on thin fields: MCV purchase and army hold gated on a distant field existing, MCV timeout (deploy in place or sell), sell lowest-value structure when broke with no harvester | ML-03, AI-06 (3, 4) | P8-15 | **goldens MOVE** if any golden reaches the state (byte-compare); ADR | pending | pending |
| P8-21 | Seat fairness: placement scan oriented to map centre, residual bias bisected, map start asymmetries recorded per map | AI-04, BAL-01 (map half), OJ-01 (3); D5 | P8-15, P8-13 | **goldens MOVE** (four commander goldens); ADR | pending | pending |
| P8-22 | The AI spends: extra barracks or factory under sustained surplus up to the rung cap, orders routed to the shortest queue, several Produce per beat when affordable, further faction defences from surplus, caps and reserve in data/ai | AI-03, ML-04, OJ-04; D6, D7 | P8-21 | **goldens MOVE** (four commander goldens); **catalogue checksum MOVES** (data/ai); ADR | pending | pending |
| P8-23 | Waves escalate and close: staging point, launch at a growing threshold, retreat below 30 per cent, all-in when the enemy army is outnumbered, Rusher push of 4 to 6 infantry near 90 s, Turtle walls and extra defences; skirmish-04 mirror stall re-probed with pathdebug | AI-07, AI-06 (2), BAL-11, ML-04 (part) | P8-22 | **goldens MOVE** (four commander goldens); ADR | pending | pending |
| P8-24 | Stalemate draw after 2700 idle ticks; `CommandType.Concede` from Surrender and clean quit; `endgate` asserting | ML-07, OJ-10; D12, D13 | P8-23 | goldens NEUTRAL expected (defeat folded under a guard), measured; save format bump only if persisted | pending | pending |
| P8-25 | The AI fires support powers: scan before a wave, precision strike on harvesters or a clustered army, jam during a push, tunnel the garrison to a threatened refinery, decoys on the wave's flank; `aisupportgate` | ML-10, OJ-06, FEEL-09 (AI half), PV-09 (1), AI-08 (part) | P8-18 | **goldens MOVE** where a golden commander reaches a power building; ADR | pending | pending |
| P8-26 | Difficulty that changes who wins: rung producer caps, Easy first attack 7200 and wave scale 0.5, Hard and Brutal counter-composition and retreat; `laddergate` and `difficultygate` idle-Easy stage asserting | AI-02, FS-05; D6, D7 | P8-22, P8-23, P8-21 | goldens NEUTRAL (Normal at identity knobs), measured; **catalogue checksum MOVES** (schema and data/ai) | pending | pending |
| P8-27 | AI variety: per-seat deterministic stream, 3 openings per personality, up to 20 per cent wave jitter, weighted targets; menu rolls a fresh seed | AI-05, ML-08, OJ-01 (2); D8 | P8-26 | **goldens MOVE** (every AI-driven scenario, once); ADR | pending | pending |
| P8-28 | The AI fields the roster: hero at radar tier, shade raider and vanguard car in the cycles, infiltrator or saboteur raids from Hard, mines at approaches | PV-09 (3 to 5), AI-08 | P8-26 | **goldens MOVE** unless gated beyond golden horizons (byte-compare); ADR | pending | pending |
| P8-29 | The AI cannot count undetected cloak: census and target picks skip undetected cloaked enemies | AI-09 (part); D9 | P8-23 | goldens measured (move only if a golden holds a cloaked enemy near a commander); ADR | pending | pending |
| P8-30 | `longmatchperf`: skirmish-07, -08, -09 (four seats), mean, p99, p999, max, first 30 ticks skipped, deterministic FlowField-build proxy asserted beside wall time on one CI runner | SP-02 | P8-13 | goldens NEUTRAL | pending | pending |
| P8-31 | Parity-proven `FlowField.Build` replacement (ordered Dial buckets) with pooled arrays | SP-01; D28 | P8-30 | goldens NEUTRAL if parity is proved field-for-field; a budgeted rebuild would MOVE goldens and need an ADR plus Architect sign-off | pending | pending |
| P8-32 | Unit balance pass under the owner's A11 exercise: rocket 92 hp, shade raider autocannon, phantom weapon, bulwark 1400/680, howitzer 52 dmg and 50 cooldown, commandos 100 dmg and 300 hp | BAL-04, BAL-05, BAL-06, BAL-09, BAL-10, OJ-09; D14 | P8-14 | **catalogue checksum MOVES**; **goldens MOVE** (rockets and howitzers appear in AI-driven goldens); ADR | pending Before any number moves, two rulings the corrected tool (P8-14) made necessary: the Strike Flyer can never pass F10 as written, because its only pairing is against the flak track, its own counter, so F10 must either exclude a unit whose sole matchup is its designed counter or pair air against something else; and D14's howitzer nerf rested on howitzer balls dominating at 9000 and 12600, which P8-14 measured to depend on the angle of approach, so the nerf must be re-justified on the two-arena matrix before it lands. | pending |
| P8-33 | Faction war re-measured on the corrected tool; Bastion ticket decided by that table; equal-credit siege verdict recorded | OJ-07, OJ-08, BAL-08, OJ-01; D18, D19 | P8-32, P8-21, P8-14 | goldens NEUTRAL (measurement); any follow-up re-tune is its own row | pending | pending |
| P8-34 | Results screen with stats, RETRY, NEXT MISSION, MAIN MENU; campaign progress persisted to user://; menu remembers the last MatchConfig; banner reads the live cancel binding | FS-09 | P8-11 | client-only, goldens NEUTRAL | pending | pending |
| P8-35 | CONTROLS page generated from `Settings.Bindable` in pause and main menus; SETTINGS in the pause menu; group 0 and bookmarks 1 to 4 rebindable; strong and weak against lines in tooltips | FS-08, PV-14 | P8-1 | client-only, goldens NEUTRAL | pending | pending |
| P8-36 | Classic left-click scheme as a first-launch choice and setting, both schemes through `IssueOrder` | PV-11, FS-08 (part); D22 | P8-35 | client-only, goldens NEUTRAL | pending | pending |
| P8-37 | A real tutorial: MissionRunner built, produced and harvested conditions and a persistent hint action; mission-01 rewritten as stepped teaching; starting cannons removed (off its own allow-list) | FS-01, FS lens adjacent (mission 1 allow-list) | P8-34 | **`mission` golden MOVES**; new conditions NEUTRAL (MissionRunner state is outside the world hash); ADR, Architect sign-off | pending | pending |
| P8-38 | Objectives: briefings 04 to 06 ending on an objective line, OBJECTIVES panel, key-to-text table (incl. ambush_sprung), extraction beacon and ping; scripted radar in 01, 03, 04; `Campaign.Load` keeps the faction column; mission 2 frames the side switch | FS-03, FS-07, FS-10 (framing), FS lens adjacent (GameFiles.cs:338); D20, D21 | P8-37 | **`mission` and `mission03` goldens MOVE** (radar); text NEUTRAL; ADR | pending | pending |
| P8-39 | Campaign enemies fight back: mission-declared AI commander for enemy seats, missions 3 and 6 retuned, mission 3 given a yard or a trimmed allow-list, campaigngate stage failing any idle or starting-force win | FS-04 | P8-23, P8-38 | **`mission03` golden MOVES**; mission 06 has no golden; ADR | pending | pending |
| P8-40 | Map picker shows name, size, seat count and thumbnail (name header for skirmish-03); front door: window title, smoke test behind a debug flag, TEAMS disabled on 2-seat maps, tooltips | FS-06, FS-11 | - | client-only; map checksum confirmed to ignore the header line, measured | pending | pending |
| P8-41 | Units acknowledge: docs/design/19-audio-spec.md, synthesised bark set (3 variants per order per class per faction), `BarkFor` on its own player with debounce, drag-select sound | FEEL-01 | P8-10 | client-only, goldens NEUTRAL | pending | pending |
| P8-42 | Refusals explain themselves: queue when unaffordable with INSUFFICIENT FUNDS, hero button greyed at cap, cap hold told from a blocked exit, Invalid cursor when nothing can engage air, one `Deny(reason)` with a deny cue | FEEL-05 | P8-10 | client-only, goldens NEUTRAL | pending | pending |
| P8-43 | Death effects from sim identity (infantry puff, vehicle husk, multi-stage structure collapse); corpse tumble from a recorded Mobile flag | FEEL-06, SP-08 | - | client-only, goldens NEUTRAL | pending | pending |
| P8-44 | Audio mix: announcer channel with priority queue, positional pool 24 to 32 with per-family caps, WeaponId sound table (rocket, flak airburst, rifle, heavy MG), promotion, deploy and placement cues | FEEL-08, FEEL-14 | P8-10 | client-only, goldens NEUTRAL | pending | pending |
| P8-45 | HUD: themed panel, tick counter behind a dev toggle, F1 help overlay instead of the key strip, 22 missing icons from the Blender meshes, production toast names from the catalogue, only the producing factory animates | FEEL-13, SP-09 | P8-35 | client-only, goldens NEUTRAL | pending | pending |
| P8-46 | Interim score: at least six procedural tracks of at least 3 minutes, shuffled, intensity-aware, crossfade kept | FEEL-12; D26 | - | client-only | pending | pending |
| P8-47 | Warnings as errors under sim/ and the two dead CS0219 constants deleted | SP-11 | - | goldens NEUTRAL | **DONE**: `sim/Directory.Build.props` sets TreatWarningsAsErrors for every project under sim/ (Sim, Net, Presentation and the runner, including when game/ or tools/ build them through a ProjectReference). The compiler's own CS0219 proved `Veil` (TunnelDeploymentGate) and `Refinery` (PrecisionStrikeGate) dead, and both are deleted; `Bastion` on the same line is used and stays. All four sim projects build with 0 warnings in Debug and Release, and goldens are byte-identical. Not run here: the Godot build of game/, which compiles the sim projects by reference and so inherits the setting; the client-harness CI job is where a warning there would surface | pending |
| P8-48 | Client frame hygiene, MEASURED IN GODOT FIRST: snapshot buffer ring, per-frame allocation and NodePath churn, fog upload only on a new fog tick, effect pooling and camera culling | SP-04, SP-06, SP-10 | P8-31 | client-only (TakeSnapshot is a read, hash-neutral) | pending | pending |
| P8-49 | Gate trap measured, then fixed client-side: route through an own gate when the destination is unreachable; open state drawn from `World.IsGateOpen` | PV-10; D29 | P8-1 | client-only, goldens NEUTRAL | pending | pending |
| P8-50 | `towercreepgate`: powered enemy turrets on a harvester route; fix only if harvesters die | AI-10; D29 | P8-13 | goldens NEUTRAL (probe); any fix measured | pending | pending |
| P8-51 | Formation spacing 1.5 with a collision check against the crowd-arrival settle | OJ-12; D29 | - | client-only | pending | pending |
| P8-52 | P8 CLOSURE AUDIT: every F criterion re-run and its figure recorded here; playtest brief reissued | all | P8-1 to P8-51 | none (measurement and docs) | pending | pending |
| P8-53 | `Fix64` multiplication FLOORS (`(Int128)a.Raw * b.Raw >> FracBits`), so a negative product rounds one step further from zero than its positive twin and `(-x)*y` differs from `-(x*y)` by one unit in the last place; mirrored computations diverge from tick 2. Division truncates and is symmetric. Found by P8-14, whose left-right mirror self-check failed for this reason until the arena was mirrored across y = x | P8-14 lane, verified by the orchestrator at sim/Ferrostorm.Sim/Fix64.cs:39-40 | P8-15, P8-21 (measure after docking and placement are fixed) | **ALL goldens and every replay move if fixed: ADR and replay-compatibility break** | pending, MEASURE FIRST: a candidate second cause of the seat effect F5 measures. Once P8-15 and P8-21 land, run `seatfairgate` against a build with symmetric rounding; fix only if it moves the seat split, and only under its own ADR | n/a |
| P8-54 | Units step in id order, so a cloak reveal is visible within the same tick only to HIGHER ids; commando against shadow commando is decided by seat at every budget | P8-14 lane | P8-21 | goldens move if fixed | pending, MEASURE FIRST: decide with P8-53 whether same-tick visibility should be snapshotted before the combat pass | n/a |
| P8-55 | `CanTarget` honours TEAM detection (D24b): a cloaked enemy detected by any seat on the attacker's team is targetable, matching what P8-6 now draws; the client's local-seat pick rule is then dropped | P8-6 lane, World.cs:4244 | - | goldens expected NEUTRAL (a team is a seat in every two-seat golden), measured; ADR | pending | n/a |

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
