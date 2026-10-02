# ADR-072: the commander answers air
- Status: Ratified under decision D10 of docs/tickets/P8-formidable-tracker.md ("the AI answers air (flak, earlier radar, escorts) but does not fly in P8"), taken under the owner's standing authority of 2026-10-02 for the orchestrator to integrate
- Date: 2026-10-02
- Deciders: orchestrator (owner's standing authority, D10) + implementer agent
- GDD/TDD feature served: GDD s9 line 76 (Normal "competent build orders", Hard "strong macro, honest information"); GDD line 55 (air is a scalpel, not an army); ADR-028 clause 4 and its closing note that "the AI does not build or answer them"; P8-17; findings AI-01 and PV-06; criterion F3

## Context

ADR-028 shipped the air layer with its answer, the Flak Track, and recorded
that the commander neither flies nor answers aircraft. AI-01 measured what
that costs, and `aiairgate` (P8-13) reproduces it on the main this row
started from (905b6f0). Three Strike Flyers hunt the commander's harvesters
from t=4500 on skirmish-01 while seat 1 is played by a Normal commander:

| Cell | Flyers alive at the end | Harvesters lost | Flak Tracks built | End |
|---|---|---|---|---|
| Normal Directorate | 3 of 3 | 5 | 0 | seat 1 won at t=9586 |
| Normal Sodality | 3 of 3 | 6 | 0 | seat 1 won at t=7171 |
| Hard Directorate | 3 of 3 | 9 | 0 | seat 1 won at t=8881 |
| Hard Sodality | 3 of 3 | 8 | 0 | seat 1 won at t=9136 |

The commander rebought harvesters that the flyers killed, reached 0 credits
by t=7500, and lost the match. No flyer was ever shot down. It is the
cheapest exploit in the game.

`cheesegate`'s four flyer raids on the base also FAILED at 905b6f0, although
P8-13 recorded the gate passing. That pass was never an answer to air:
measured at the P8-16 merge (098e70c), the commander won each of those
matches outright with all three flyers still alive, which the gate counts as
answered. P8-15 gave seat 1 back the economy ML-01 had frozen, the commander
stopped winning inside the window, and the flyers lived through it. The only
change between those two commits outside `/game` is the presentation
interpolator, so the attribution is exact.

## Decision

### 1. The census: an enemy aircraft counts only if the commander can see it

`SkirmishAI.SeesAircraft` states the rule once. An entity counts when it is
an enemy (`World.IsEnemyOf`), airborne (`World.IsAirborne`, the catalogue's
`air` flag), its cell is lit in the commander's own fog this tick
(`World.IsVisible`, the per-player bitset the fog pass writes from that
player's sight and scans), and it is not an undetected cloaked unit (the
sim's own targeting rule, `World.CanTarget`, restated because it is private).
The commander never reads through fog to learn that the enemy flies, which is
D9's spirit applied to the census this row adds. No flyer cloaks today, so
the cloak half costs nothing and is there for the day one does.

The commander remembers the MOST enemy aircraft it has seen at once this
match (`_airSeen`), a high-water mark of what it saw and never a read of what
it did not. Like every field the commander keeps, it is AI-internal: never
hashed, never saved, and relearnt from the next sighting after a load, which
is the precedent `_produced` and the wave timers already set.

### 2. Two numbers, authored in data/ai on the personality rows

| Key | Standard | Rusher | Turtle | Meaning |
|---|---|---|---|---|
| `anti_air_cap` | 6 | 4 | 10 | the most anti-air units the commander builds; 0 means never |
| `anti_air_garrison` | 2 | 2 | 2 | how many of them are held at home |

They live on the PERSONALITY rows beside `wave_size` because a home guard's
size is a commander's shape, not its strength, and F3 asks Normal and Hard to
answer air alike. Both are derived rather than chosen: the cap is each
personality's own wave size, so no commander holds more anti-air than one
wave's worth of army, and the garrison is 2, the floor the ground garrison
already keeps. `schema.ai.json` describes both, `DataLoader.ParseAiTuning`
requires both in a personality file and refuses both in a rung file (adding
them to `PersonalityKeys` is what does the refusing), `AiTuning` holds the
compiled reference the files reproduce, and `AiTuningDef` carries them as two
appended, defaulted fields so every rung row is unchanged.

They ride `World.CatalogueChecksum` on ADR-032 clause 2's argument unchanged:
they decide which Produce and move orders a commander issues once it sees air,
so two LAN peers holding different files would answer the same raid with
different armies while every def matched. They are appended to the AI section
of the fold, so the older sections contribute exactly what they did.

### 3. Anti-air into the production cycle

The anti-air unit is asked of the catalogue (`AntiAirUnitType`): the lowest
unit type id that has a price, is not airborne (D10: the AI does not fly), is
common or the commander's side, and carries an anti-air weapon. Today that is
the Flak Track for both factions, because it is common. Anti-air is likewise
recognised by its weapon (`IsAntiAir`), never by naming the type.

The target is one more than the most flyers seen at once, capped by
`anti_air_cap`. Units on the producer's line count toward it.

WHEN the commander orders is a rule measured both ways. The first build-up
runs from memory until the answer stands (`_antiAirStood`), because a
commander may glimpse the flyers once and not again until they are over its
harvesters. After that, losses are replaced only while an aircraft is in
sight, and a larger sighting than any before reopens the build-up.

While the answer is short and an order is due, it takes its producer's turn:
any other entry on that producer's queue is cancelled (back to front, so each
index is still the one meant; the head's refund is exact under
pay-as-you-build, so the cost is build progress only), the anti-air unit is
queued, and the harvester purchase, the MCV purchase and the army block all
yield that producer for the beat.

### 4. The garrison and the escort

The first `anti_air_garrison` anti-air units, lowest id first, are the
garrison. They close on any flyer seen within 14 cells of a structure on the
commander's side and drift home when the sky is quiet. Every anti-air unit
beyond them is an escort. The k-th escort shadows the k-th harvester, round
robin in id order, and when a flyer is seen within 8 cells of any harvester
all escorts close on it. With no harvester alive an escort guards the
lowest-id refinery, where the next one docks. The radii and the 60-tick
re-order cadence are the intruder census's own, now named constants shared
by both (`EconomyGuardSq`, `BaseGuardSq`, `HomeLeashSq`, `EscortLeashSq`,
`DefendCadenceTicks`, each the value its site always used).

Anti-air is not line strength: it cannot shoot the ground (ADR-028 clause 3).
So it is left out of the `army` count that times waves, out of the ground
garrison and out of every wave.

### 5. The anti-air tier, pulled forward

Once air has been seen, `MissingTierStruct` asks which building the anti-air
unit waits behind that the commander lacks and can build now (a missing
prerequisite KIND, answered with the commander's side's buildable structure
of that kind, provided its own prerequisites stand). That rung sits straight
after the factory, ahead of the economy rung, and waits only for its price.
For the Flak Track it is the Radar Uplink, which the ordinary ladder builds
only after its defences and at 1500 credits.

### 6. The gate counts the harvester that survives the raid

`aiairgate` is binding. A surviving harvester is now counted when the raid is
answered (the tick the last raider falls, or the match's end if the commander
won with raiders standing) rather than at the end of the 6000-tick window,
and the window's end is still printed beside it. Each cell also plays the
same match with no raid and prints it, asserting nothing.

The reason is the control itself. With no flyer anywhere, the Normal
Sodality commander ends the window with no harvester, to seat 1's ground
units at t=6073 and t=7301 and the orbital cannon at t=7801, and never
rebuys one. A reading that fails with no raid at all measures the ground war,
not the raid. F3 says the flyers die "and at least one AI harvester survives",
so the harvester is counted against the flyers. The same rule applies to
`cheesegate`'s Vanguard Car raids, which pass either way. `cheesegate` is
binding too, as the tracker says it binds with this row.

## Alternatives rejected

**The numbers on the rung rows.** That would make answering air a strength
knob, and F3 asks Normal and Hard to answer alike. It would also hand P8-26
a ladder decision this row has no measurement for.

**A third AI tuning family (a "doctrine" row).** One more kind, id range,
loader branch and checksum section for two numbers that sit naturally beside
`wave_size`. Refused as indirection the row does not need.

**Ordering only while an aircraft is in sight.** Measured: the Normal
Directorate commander glimpsed the flyers at their spawn, lost sight of them,
and placed its first order 195 ticks later (t=4845 against t=4650). All three
harvesters died before the last flyer fell.

**Ordering from memory alone.** Measured: a Normal Sodality commander whose
flak had died to ground fire and an orbital strike spent 2200 credits
rebuilding it against an empty sky while its harvester line starved.

**Queueing behind whatever the producer holds.** Measured: a Normal Sodality
commander saw the flyers at t=4522 and ordered flak at t=4545, behind a
phantom tank that had just started. 210 ticks of phantom stood between it and
its first gun, and all three harvesters died before the last flyer fell.

**Treating a seen enemy Airfield as the threat.** It would answer earlier,
but the row's census counts aircraft, and building flak against a building
that may never launch is a scouting and strategy question beyond this row
rather than an answer to air.

**Fixing the harvester rebuy here.** The harvester purchase waits for an
empty factory queue that the army block keeps at one or two orders, so a
commander can sit on 17000 credits with one harvester for 4000 ticks. That is
general, it moves ground-only matches, and P8-22 ("orders routed to the
shortest queue, several Produce per beat") owns it.

**Keeping the end-of-window reading.** It cannot pass the Normal Sodality
cell with any air answer, as its no-raid control proves, so binding it would
have bound F3 to the Directorate-Sodality ground balance and the orbital
cannon's pacing (P8-18, P8-21) instead of to air.

**Taking airborne enemies out of the ground intruder census.** The ground
garrison still walks after flyers it cannot shoot. Not in the row, and it
changes no figure here; recorded below.

## The measurement, before and after

**`aiairgate`** (F3, binding), per cell. Harvesters lost to the raid are read
at t=5500, after every flyer has fallen in every cell.

| Cell | Before: end | After: last flyer falls | Harvesters alive then | Lost by t=5500, before / after | Window's end, after | Control, no raid |
|---|---|---|---|---|---|---|
| Normal Directorate | flyers alive, lost 5, seat 1 won t=9586 | t=4875 | 2 | 3 / 1 | 2 harvesters, no winner | 4 harvesters |
| Normal Sodality | flyers alive, lost 6, seat 1 won t=7171 | t=4847 | 3 | 3 / 1 | seat 1 won t=8401, 0 harvesters | 0 harvesters |
| Hard Directorate | flyers alive, lost 9, seat 1 won t=8881 | t=4905 | 3 | 4 / 1 | 7 harvesters, no winner | 7 harvesters |
| Hard Sodality | flyers alive, lost 8, seat 1 won t=9136 | t=4852 | 4 | 4 / 0 | 8 harvesters, no winner | 2 harvesters |

Every cell now answers the raid within about 400 ticks with a harvester
standing: PASS. Read at the window's end, three of four would pass, and the
fourth fails exactly as its no-raid control does.

**`cheesegate`** (binding): 4 of 10 stages failed before, 10 of 10 pass
after. The four flyer raids on the base now end with the last flyer down at
t=4875, t=4926, t=4905 and t=4947 (Normal and Hard, Directorate and
Sodality), each with 3 or 4 harvesters alive. The Vanguard Car raids and both
tower creep stages are unchanged.

**`ladderprobe`**: all 360 per-match lines and every summary line
byte-identical before and after (179 undecided; seat 0 took 106 of 164
decided unique matches). No aircraft flies in a ladder match, so the air
answer never wakes, which is the claim measured rather than assumed.

**The radar pulled forward** (a scratch measurement, not a gate): raiding at
t=1200, before the commander owns a radar, it first saw the flyers at t=1370,
queued the Radar Uplink at t=1425 and placed its first flak order at t=1590
as the radar stood. It still lost all three harvesters by t=1780; the last
flyer fell at t=1886. An air rush that early outruns a radar, a factory slot
and a 130-tick build, and F3 does not ask for it.

### The gates bite

- **Census disabled** (the airborne count never incremented): `aiairgate`
  FAILS binding in all four cells (exit 1) with figures identical to the
  baseline above (seat 1 wins at t=9586, 7171, 8881 and 9136; 5, 6, 9 and 8
  harvesters lost). With no air seen the commander is the old commander to
  the order. `cheesegate` FAILS its four flyer raids.
- **Flak queueing disabled** (census live): `aiairgate` FAILS all four
  cells, worse than the baseline, with 9, 8, 15 and 11 harvesters lost and
  seat 1 winning at t=9151, 7366, 9931 and 7951, because the army still
  yields a factory to an answer that never comes.
- **Garrison removed** (`anti_air_garrison: 0` authored in ai_standard.yaml,
  so all anti-air units escort): neither gate's verdict changes. The raid is
  answered at the same ticks with the same harvesters standing, because the
  garrison and the escorts close on the same flyer when it strikes near the
  refinery. What changes is the rest of the match, in both directions: in
  `aiairgate` Normal Directorate goes from holding the window to losing at
  t=10216 and Hard Sodality to losing at t=9961, while Normal Sodality goes
  from losing at t=8401 to standing at t=10500 with no harvester. These gates
  therefore do not show the garrison to be load-bearing, and it stays at the
  authored 2 on its derivation, not on a measurement.

## Hash and format

**All 24 goldens byte-identical, measured**: `golden 2026` diffs empty
against `sim/golden-hashes.txt`, in order. No golden holds an aircraft, so
the census never counts one, and every air branch is gated on that count.

**The catalogue checksum moves**, from `0xB4E6F043C4A872CC` to
`0xF384205E0D0BF2D2` (the same from /data and from the compiled table), by
construction: two numbers were appended to the AI section of the fold.
Pre-existing saves and replays refuse, on the same pre-first-public-build
trade every catalogue wave has taken. `aituninggate` proves the transcription
of both new keys, proves each REGISTERED value drives the commander (a cap of
0 orders no Flak Track where the authored 6 orders one; a garrison of 0 sends
two idle Flak Tracks to guard the refinery where the authored 2 sends them at
the flyer), and proves a one-unit change to either moves the checksum.

No new Entity field, no save change and no wire change. The commander's new
state is AI-internal and never hashed.

## Consequences

The cheapest exploit in the game is closed at Normal and Hard for both
factions: a raid of three flyers is shot down within about 400 ticks and
costs the commander at most one harvester.

The answer has a price, paid in the ground war: four Flak Tracks (2200
credits) and whatever production the answer cancelled. Where the commander
was already losing on the ground, it loses sooner: the Normal Sodality cell
falls at t=8401, where its no-raid control still stands at the window's end
and the same match in the ladder baseline falls at t=10711.

Found and left to their owners:

- **The harvester rebuy starves** behind army orders at a busy factory
  (P8-22). It is why a commander that loses harvesters to anything recovers
  slowly.
- **The orbital cannon strikes the commander's refinery** roughly every 1600
  to 2000 ticks from t=4621, killing harvesters and the escorts beside them
  (P8-18's pacing).
- **The ground garrison chases flyers** it cannot shoot, because the ground
  intruder census counts airborne enemies. It changes no figure here.
- **An air rush before the radar** still costs every harvester (the t=1200
  measurement above).
- **The Sodality loses the ground war to the Directorate** on skirmish-01
  with or without air, which is P8-21's and P8-53's residue.

**What would reverse it.** D10's condition: a playtest that finds the air
layer one-sided, whether the commander's answer makes air useless or air is
still the exploit. And the gate's reading: once P8-22 and P8-21 land, if the
no-raid control keeps a harvester at the window's end in all four cells, the
gate should go back to counting at the window's end, because it would then
measure the raid rather than the ground war.
