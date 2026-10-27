# ADR-080: the waves escalate and close: a wave gathers at a staging point and launches at a threshold that grows, a beaten wave comes home on the rungs that retreat, and an army that outnumbers the enemy's goes all-in
- Status: **Ratified under D41** of docs/tickets/P8-formidable-tracker.md, 2026-10-27, by the orchestrator under the owner's standing authority of 2026-10-02, after the Architect's sign-off with nine conditions of 2026-10-27 (section "Architect sign-off" below, its date confirmed), every condition met in the pull request from branch `claude/p8-23-attack-waves` into main (pull request number recorded when it opens). Proposed 2026-10-14. Row P8-23 of docs/tickets/P8-formidable-tracker.md (decisions D6 and D7; findings AI-07, AI-06 (2), BAL-11 and ML-04 (part)). Built on P8-22's branch, `claude/p8-22-ai-spends` at 1ad6cb1 (main at 9670e04 plus P8-22 under ADR-079), while pull request 175 was open; that pull request has since squash-merged as 4076626, whose tree equals 1ad6cb1's, and this row was rebased onto it byte for byte. Every base figure below is measured at 1ad6cb1, and so holds for main at 4076626. It moves goldens, which is a replay-compatibility break needing an ADR and the Architect (CLAUDE.md), and it changes what the commander does, so it is the class Q024 asks about; it also moves the catalogue checksum (data/ai). It lands as four commits: the measurement (`waveprobe`, and `pathdebug map=`), the seven numbers in data/ai, the behaviour with its regenerated goldens, and `longmatchperf`'s proxy budget re-baselined, all goldens-neutral but the behaviour; this ADR, its queue entry and the tracker rows follow in one docs commit. After the Architect's sign-off, one commit follows for each of its conditions 1 to 7 and one for 8 and 9. Drafted by the implementer agent.
- Date: 2026-10-14
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD s9 line 76 (Normal "competent build orders", Hard "strong macro"); GDD pillar 2 (matches of 15 to 30 minutes); TDD s1 ("determinism is the product"); criteria F3, F4, F5 (its split by start), F6 and F8 of the P8 tracker; decisions D6, D7, D9, D12 and D32; ADR-005 (walls bought as they land), ADR-032 (the AI's tuning is authored data), ADR-053 (the wave arrives, and its arrival bound), ADR-056 (nothing is stranded), ADR-075, ADR-076 and ADR-078 (the frame, the half turn and the reflections the commander must stay exact under), ADR-079 (the commander that spends, whose harvester rebuy's reversal this ADR reads)

## Context

Row P8-23 asks the commander's waves to escalate and close: a staging point, a
launch at a growing threshold, a retreat below 30 per cent, an all-in when the
enemy army is outnumbered, the Rusher's push of 4 to 6 infantry near 90
seconds, the Turtle's walls and extra defences, and skirmish-04's mirror stall
re-probed with `pathdebug`. It inherits ADR-079's commander, which holds two
or three producers of each kind, places several orders a beat and keeps up to
its personality's `faction_defence_cap` of its own side's defences.

Until this row a wave was a STREAM. Once the army stood at a wave plus the
garrison, every fighter outside the garrison was re-sent at the target every
300 ticks, so each new fighter walked out alone behind the last. `waveprobe`,
added for this row (a probe by ADR-061's rule: it reads every launch from the
orders a commander gives, follows it for two minutes and classes it), measures
the base at seed 2026 over `pillarprobe`'s sweep: 3438 launches over 144
seats, a median of 21.5 a seat, a median launch of 4 units, a median largest
of 7, the first at a median t=2805; of them 34 per cent broke in (reached the
enemy's buildings and razed one), 36 arrived, 24 were lost on the way, 4
stalled and 2 were recalled. The Rusher's first launch came at a median
t=2580, a stream of 3; there was no push.

The same base resolves too early for F6. `pillarprobe` puts 27 of 72 matches
(38 per cent) between 13500 and 27000 ticks, 42 before 13500 and 3 with no
result at 27000 (`endgate`: skirmish-09 DD both orientations and SS o1), the
decided matches' end ticks at quartiles of 11611, 12871 and 16066. ADR-079
named this row the first place to look, and its harvester rebuy (commit
f0c9b74) carries a reversal this row must read: the rebuy is reverted alone if
F6's window share cannot reach 70 per cent with it and can without it,
measured at this row's head.

**skirmish-04's mirror stall, re-probed at the base.** P8-21 recorded
skirmish-04's Directorate mirror reaching 27000 with no result from both
orientations. At the base it does not: `pathdebug map=04 orient=both`
(extended for this row to trace a mirror in ADR-053's arrival columns plus the
fighters parked between the bases) shows the DD mirror resolved at t=17518
from both orientations and the SS mirror at t=11176. It was a slow CLOSE
rather than a park: the losing seat's army stood at 0 from t=12000, the
winner's grew from 15 to 53 with 3 to 29 of them within the arrival bound,
never more than 2 parked, and the last building fell 5500 ticks after the
last defender.

## Decision

Seven numbers and one doctrine. The numbers are authored in data/ai (section
9); the doctrine reads them, and every choice in it reads this commander's own
state, counts and hit points, and the twin-exact staging point (section 10).

### 1. A wave gathers at a staging point

The STAGING POINT is `staging_cells` (10) out from the commander's own yard
along the line to its target (the nearest enemy refinery, else the nearest
production structure, as the wave has always aimed), measured as the larger
axis distance so no square root is taken, snapped to the cell it falls in and
walked back towards the yard, a cell at a time, to the first open cell; never
at or past the target, and the yard itself when no cell is open. 10 is
ADR-053's arrival bound, `World.CyBuildRadius` plus 3: a wave gathers where an
enemy wave would count as having arrived. It is inside `idleprobe`'s 20-cell
home bound (ADR-056), so a gathering wave reads as home, never stranded:
`idleprobe` finds no unit far from home and none idle over 3000 ticks at this
head (4, 0, 0 and 0 idle over 900 ticks at 5000, 10000, 15000 and 20000,
against 4, 3, 0 and 0 at the base).

The FIELD is every fighter that may join a wave (the filter the garrison and
the wave always shared, now one predicate, `WaveEligible`) that is neither
garrison nor in flight. On the garrison's cadence (60 ticks) an idle fighter of
the field standing outside the garrison's home leash (12 cells) of the staging
point is sent there. A fighter in flight is never garrison: the garrison is
chosen from the lowest ids that are not, so a garrison death no longer drafts
a fighter out of a wave halfway across the map.

### 2. It launches at a threshold that grows

The first wave launches once `wave_size` fighters of the field stand gathered,
within the home leash of the staging point; each later one waits for
`wave_size` plus `wave_growth` for every wave already launched, never more than
`wave_cap`. Two fallbacks keep a wave from waiting for ever. A threshold that
has stood a whole wave cadence (300 ticks) launches however it stands, so a
staging point no fighter can reach never holds a wave back. A field of at
least `wave_size` that has made no new high for a wave cadence launches below
the threshold: it has reached the army this commander can field now. That
second one was added for a measurement on an earlier build of this row, in
which the `mission` golden's Rusher, its harvester dead and its income gone,
held four fighters below a grown threshold of six for the rest of the match
and never took the camp the scenario asserts it takes. **That reason does not
hold at this head** (Architect condition 5, corrected): with the stalled-field
launch switched off, `mission` is byte-identical and only `skirmish` moves, to
0xB888EA387D513752 (the verifier's measurement, reproduced). The fallback is
live, and it is kept for the property it states, that a commander whose army
cannot grow still attacks, not for that golden. Launches keep the 300-tick
spacing the stream always had. `wave_growth` is the personality's own ground garrison (half its
wave, floor 2: 3, 2 and 5) and `wave_cap` twice its wave (12, 8 and 20), each
transcribed from that rule rather than derived when loaded, as ADR-079 did for
`faction_defence_cap`.

### 3. The wave in flight is re-aimed, every member

A launched fighter joins THE WAVE IN FLIGHT, which the commander holds as a
set of entity ids used for membership only: every walk over it is a walk over
the entity list in index order, so no order is read from a hash set. Every
member is re-aimed at the current target the beat the target changes and on
the wave cadence, as the stream re-aimed every fighter it had sent. That it is
every member is measured: the first build re-aimed only the idle ones, and
any member that had closed on a building in sight (an attack-move's hunt aims
at the nearest enemy it can see, and a building's centre is a blocked cell
with no route to it) stood reporting Moving with no route for the rest of the
match. `frozenprobe` counted 197 such units in 13 of its 72 matches; with
every member re-aimed, 6 (section "What it measured"). A fresh order sends the
member back to the wave's aim, whose route the sim finds to an open face (D4).

### 4. A beaten wave comes home on the rungs that retreat

Below `retreat_percent` of the hit points it was sent with (joiners counted),
the wave in flight is called home to the staging point by PathMove, and its
survivors become field again, to join the next wave. A wave sent all-in never
turns back. `retreat_percent` is a RUNG key, because whether a commander saves
its beaten army is how good it is (DR-14), and its values are decision D7's:
30 for Hard and Brutal, 0 (never) for Easy and Normal. That is the precedent
ADR-079 set for D7's producer caps: this row authors the retreat D7 names, and
P8-26 judges and binds it with the rest of the rung shape. D7's
counter-composition stays P8-26's, and so does any retreat of a single unit by
its own hit points, which is a different mechanism from a wave's.

### 5. The commander goes all-in when the enemy is outnumbered

When its armed ground army is at least `wave_size` strong and its hit points
are at least `all_in_percent` (200) of the hit points of the enemy's armed
ground army and armed defences together, the commander sends every fighter it
has, its garrison included, without waiting for the threshold, and the wave in
flight is marked all-in and never retreats. Hit points rather than heads, so
ten rifle squads do not read as outnumbering three heavy tanks; the enemy's
defences count because they stand between the army and the base. Twice is the
reading of "the enemy army is outnumbered" under which the outcome is not a
coin toss. The enemy is counted as this commander already counts the enemy's
buildings, through fog; P8-29 (D9) narrows the count, with the target picks, to
what it may see, and `IsGroundFighter` is the one place to do it for units.

### 6. The gathering field defends its team's base

When the intruder census finds an enemy among its team's buildings (the base
guard, not a harvester's), the field answers it with the garrison, on the
garrison's cadence. A wave gathering at home is home, and before staging the
field stood among the buildings anyway; staged 10 cells out it would otherwise
idle while the base beside it burned. An intruder found only at a harvester
still draws the garrison alone, so a harasser cannot pull the gathering wave
across the map (TICKET-AI-05's reason, kept).

**Its team's, not its own alone** (Architect condition 5, corrected: this
section first said "its own base"). The census asks `IsAlliedTo`, P7-8i's team
widening, and the field reads the census rather than a narrower test of its
own, so an enemy nearest to an ally's yard, plant, refinery, factory or turret
draws this commander's gathering field as well as its garrison. The reason it
stands: the garrison already answers at an ally's base by P7-8i's design, and
a field that stayed home while its garrison went would split the defence
rather than narrow it; with the default team map every seat is its own team,
so a two-seat match is unaffected. The harasser reason above still holds,
because an intruder nearest to a harvester draws the garrison alone. Narrowing
the field to this commander's own buildings would be a later row's behaviour
change, measured as one.

### 7. The Rusher pushes at 90 seconds

`rush_tick` (1350, the row's 90 seconds, for the Rusher; 0, none, for the
others) gives a personality one early infantry push. The push is not a new
size: it goes once `wave_size` (4) armed barracks units stand, and takes at
most `wave_size` and its garrison (6), which is the row's "4 to 6 infantry"
read off the Rusher's own two numbers. Two measured additions make it land at
90 seconds. Without an opening the Rusher's army waits for the factory and a
finished ladder, so its push, which waits for four infantry, went at a median
t=2648; so a personality with a push fills its barracks line with rifle squads
(the opening cycle's own first unit) from the moment the barracks stands,
ahead of the rest of the ladder, until a wave and its garrison of infantry
stand or the push has gone. With that, its ordinary first wave left at a median
t=1035 and mooted the push; so a personality with a push holds every other
launch, the all-in included, until its rush tick, and the push is its first
wave. Any launch made moots it, and it is never made after one.

### 8. The Turtle walls its defences; its defence cap is not retuned

`walls_per_defence` (4 for the Turtle, 0 for the others) raises walls round
each of the commander's armed defences (turret, Bastion, emplacement or Shroud
Nest), oldest first, on the side of the defence's one-cell ring that faces the
target (the side whose axis carries the larger offset, ties to the x side):
that side's two cells, then the corner before and the corner after it. A wall
is bought outright as it lands (ADR-005 clause 3), one a beat, only from a
sustained surplus (ADR-079's reserve and window) and only once the surplus has
nothing else to buy, so a wall never jumps a producer or a gun. **Its credit
test reads the treasury alone** (Architect condition 5, recorded): the
treasury must hold the reserve plus the wall's price, and what the beat has
already committed (an engineer or a harvester ordered earlier in it) is not
counted, the same class ADR-079 section 2 recorded for the surplus building.
The wall's price does join the beat's commitments, so no army order after it
promises the same credits again; a beat that walls can still end below the
reserve by what it committed before the wall, as ADR-079's beat can, and the
`surplus_reserve` description in `data/schema.ai.json` lists the wall's test
beside the building's. **`mission03` runs a Turtle** (`SkirmishAI.Turtle(0)`),
and the wall branch is quiet there only because the surplus window cannot
complete inside that scenario's 5000-tick horizon from the mission's
4000-credit opening, so a change to either moves that golden (Architect
condition 5 corrected the code comment that called the branch inert in every
golden); `aiwalls` is the golden that reaches it. A cell is taken
only where a wall may legally stand (`World.ValidPlacement`), outside the
one-cell apron of every other building of the commander's and not beside a
ferrite field, so a wall never seals a refinery, a producer's exit or a
harvester's field. The wall type is asked of the catalogue as a capability
(the Wall kind with a price that the side may build), not through
`World.BuildableStructOfKind`, whose test of a build time excludes a wall.

`faction_defence_cap` stays at the Turtle's 5, which the row allowed this ADR
to retune. Measured over `waveprobe p0=turtle p1=turtle`, the surplus reaches
walls in 102 of 144 Turtle seats, a median of 4 walls a seat and at most 12.
Walls are bought only once the surplus has nothing else to buy, the faction
defences the cap allows among it, so the cap decides how much of the surplus
becomes guns before it becomes walls: raising it would hold the walls this
ADR adds behind a sixth gun in most of those seats, and lowering it would cut
the Turtle's identity, which ADR-079 transcribed from its garrison of five.
"Extra defences" is therefore the cap ADR-079 already gave the Turtle (5
against Standard's 3) and the walls this ADR puts round them.

### 9. The numbers are authored data (ADR-032)

`wave_growth`, `wave_cap`, `staging_cells`, `all_in_percent`, `rush_tick` and
`walls_per_defence` are PERSONALITY keys and `retreat_percent` a RUNG key, split
by DR-14's rule (how a wave gathers, grows and commits is shape; whether a
beaten wave is saved is strength). Each has a schema entry in
`data/schema.ai.json`; a compiled reference in `AiTuning` that the files
reproduce (`aituninggate` stage 1); a loader that demands it in its family,
refuses it in the other through the one key table per family, and refuses an
out-of-range value (a negative growth, all-in share or rush tick, a staging
distance outside 0 to 20, a cap below the wave, walls outside 0 to 4, a retreat
outside 0 to 100);
registration that refuses what the loader refuses, in its words, while the edge
it accepts registers (`aituninggate` stage 4, which also has the loader refuse
a staging distance of 21 read from the shipped Standard file and load 20); and
a place in
`World.CatalogueChecksum`, which moves on one unit of each (`aituninggate` stage
3), because two LAN peers holding different files would launch different waves
(ADR-032 clause 2).

**`staging_cells` is bounded above (Architect condition 4).** Its ceiling is 20
(`AiTuning.MaxStagingCells`), `idleprobe`'s home bound (ADR-056), the number
already in the game for the property section 1 claims: beyond 20 cells from
its yard an idle unit reads as stranded rather than home. It also bounds the
commander's own work, because `StagingPoint` walks from `staging_cells` down
to 1 on every beat, and an unbounded value would hang every commander, on both
peers alike. Read on the larger axis, as `staging_cells` is, a point at the
ceiling can lie up to about 28 cells out on a diagonal, past the bound's
straight-line 20; the authored 10 lies within about 14 on any line. Stage 2f's
moved value, 20, sits on the ceiling. **`rush_tick` holds every other launch**:
while the push is unmade, every launch, the all-in included, waits for it, so a
`rush_tick` past the end of a match disarms the commander, which never launches
at all; the schema's description says so.

**`aituninggate` stage 2f proves the data wins over the compiled default.**
Every world registers data/ai from the files first and then moves one number
on the row the files registered, and each check stands beside its control.
Against an enemy of two thirds its hit points the garrison stays home at the
authored all-in share and goes at 150; six fighters gathered after a first
wave of six stay home at the authored growth and launch at a growth of 0 and at
a cap of 6; fighters behind the yard are sent to gather at x 18.5 at the
authored staging distance and at x 28.5 at 20; a Rusher pushes no squad at
t=15 at the authored rush tick and all four at a rush tick of 15; a Turtle
with nothing else to buy places one wall on the enemy's side of a defence at
the authored walls and none at 0; a Hard wave down to one hit point a unit is
called home, all six, at the authored retreat and none at 0. **Each bites by
name**: making the commander read any one of the seven from the compiled
reference instead of the registered row fails the stage on that number's own
check. The all-in is checked first, because the growth fixtures switch it off
by registration and a commander reading the compiled share would otherwise
fail them under the wrong name.

**`aituninggate` stage 2g bites the two live guards nothing else did
(Architect condition 2).** Each check stands beside a control that differs
from it only in what the guard reads. The launch for a threshold that has
stood ungathered for a wave cadence: six fighters sealed in a pocket behind
the yard, with no route to the staging point, are held to t=300 (the control)
and sent, seven of them with one that joined at t=210 as a new high, on the
beat the threshold has stood a whole cadence, t=315; the new high keeps the
stalled-field launch from falling due before t=510, so nothing else can send
them. The wall's exclusion: on stage 2f's Turtle base the wall goes beside the
turret, at (20, 24), when its enemy-facing side is clear (the control), and
past it to the Bastion's side, at (20, 37), when a power plant's one-cell apron
covers that side (the apron check) or ferrite fields' neighbouring cells cover
it (the field check). Removing the stood launch fails "aituning 2g(a)" with
none of the seven sent; removing the apron exclusion alone fails "aituning
2g(b) apron", and the field exclusion alone "aituning 2g(b) field", each with
the wall at (20, 24). Every check before stage 2g stays green under each
removal, which is the gap the condition named.

### 10. Mirror-exact by construction

Every new choice reads this commander's own state: counts and hit points of
its own units and the enemy's, its own launches, and positions only through
the staging point and the wall cells. The staging point's offset is a product
and a quotient, both truncating toward zero (ADR-077), so a twin's offset is
the exact negation of its own; a point exactly on a cell boundary, which on
the dominant axis it always is (the yards' centres are whole numbers), takes
the cell on the home side, and on an axis the target does not move along the
side towards the map centre, so the two twins test twin cells. The wall cells
are written in the canonical frame `TryFindPlacement` uses and reflected along
each axis on which the defence stands past the map's centre, so a defence and
its twin pick twin cells in twin order. A centre exactly on the map's centre
line is the one tie, as in `TryFindPlacement` (ADR-075). `mirrorprobe` confirms
what it can see (below): no first break moves. The `skirmish` golden's first
divergence is itself a twin pair: both seats, on one beat, send a fighter to
(17.5, 14.5) and (78.5, 49.5), each the half turn of the other on its 96 by 64
map.

### 11. Order precedence within one beat

Recorded under Architect condition 5 from the determinism audit's finding 8.
One beat can give a unit two orders, and **the later order in the beat's list
wins**, because the sim applies a beat's commands in order and each replaces
the unit's order. Two cases are known, both deterministic and both the same on
every peer:

- **A base defence, then a staging order.** The field's answer to an intruder
  at its team's base (section 6) is an AttackMove; the staging step later in
  the same beat sends an idle field fighter outside the leash to the staging
  point by PathMove, and it tests `Moving` as the unit stood before the beat,
  so a fighter that has just been told to defend still reads as idle and is
  sent to stage instead. The defence waits for the next defence beat, up to
  one 60-tick cadence. This happens only on a beat on which both cadences fall
  and only for a field fighter outside the leash.
- **A retreat, then an all-in.** The retreat (section 4) orders the wave in
  flight home by PathMove and empties it; its survivors are field again, so an
  all-in later in the same beat sends them back at the enemy, its AttackMove
  winning, and the wave is then all-in and never retreats. A launch whose
  threshold stands on that beat would send them likewise.

Neither is changed here. Choosing which order should win is a behaviour change
for a later row, measured as one.

## What it measured (seed 2026, macOS)

Two columns: the base (1ad6cb1; `waveprobe` from the measurement commit's
build, whose commander is the base's) and this ADR's head. Every figure was
taken on the owner's Apple M4, which parallel agent runs share; tick counts and
every figure a deterministic tool prints do not depend on the machine, the
wall times do.

**What the waves do (`waveprobe`, `pillarprobe`'s 72 Normal Standard matches):**

| | base | head |
|---|---|---|
| launches over 144 seats | 3438 | 1381 |
| launches per seat, median | 21.5 | 8.5 |
| launch size, median; the largest per seat, median | 4; 7 | 9; 13 |
| first launch, median tick (its size) | 2805 | 2925 (6) |
| broke in, arrived, stalled, lost, recalled (per cent of launches) | 34, 36, 4, 24, 2 | 44, 35, 6, 15, 0 |

The stream is gone: under half as many launches, each more than twice the
size, more of them breaking in and fewer lost on the way. Nothing is recalled at Normal,
whose retreat is 0 (D7).

**The Rusher and the Turtle (`waveprobe` with p0= and p1= set, the same 72
matches as mirrors of the personality):**

| | base | head |
|---|---|---|
| Rusher: first launch, median tick | 2580 (a stream of 3) | **1350 in all 144 seats** (5 to 6, median 5) |
| Rusher: launches per seat, median launch size | 22.5, 3 | 9, 6 |
| Turtle: seats placing walls; walls a seat, median and most | 0 of 144 | **102 of 144; 4 and 12** |
| Turtle: matches with no result at 27000 | 18 of 72 | 4 of 72 |

**F6 and `endgate` (`pillarprobe`, P8-24's input, not binding):**

| | base | head | head, harvester rebuy reverted |
|---|---|---|---|
| ended between 13500 and 27000 (F6 asks 70 per cent) | 27 (38 per cent) | **22 (31 per cent)** | 26 (36 per cent) |
| ended before 13500 | 42 | 44 | 42 |
| no result at 27000 | 3 | **6** | 4 |
| end tick quartiles, decided matches | 11611, 12871, 16066 | 9841, 11956, 15256 | 10186, 11791, 15931 |

The head does not move F6 towards its bar, and it moves `endgate` away from
its own: three more matches reach 27000 with no result. They are the
Directorate mirrors of skirmish-03, -04 and -07, each from both orientations,
and they are three different things, re-probed with `pathdebug` below. The
window share's fall is within the chaos this sweep shows between
near-identical commanders ("Alternatives rejected"); the direction recorded
here is that the doctrine does not by itself buy the 15-minute floor, and that
ending a contested match is a rule P8-24 and P8-81 own.

**f0c9b74's reversal clause, read at this head.** ADR-079's "What reverses it":
the harvester rebuy is reverted alone if F6's window share cannot reach 70 per
cent with it and can without it. With it, 22 of 72; reverted alone (its guard
back from `shortestDepth < 2` to `shortestDepth == 0`, built in a scratch copy
of this head and nowhere committed), 26 of 72. Neither reaches 70 per cent (51
of 72), so **the clause is not met** and the rebuy stays. P8-24 reads it again
at its head.

**skirmish-04's mirror stall, re-probed at the head (`pathdebug map=04
orient=both`).** The Directorate mirror reaches 27000 with no result again,
from both orientations, and the trace says why: it is a TRADE, not a park.
From t=6000 to the cap both armies rebuild after every clash, between 1 and
29 strong; never more than 4 fighters stand within the other's arrival bound
and never more than 3 park; at 27000 they stand 22 against 20. At t=10500 one
seat's army is down to 1 against 8, but its Bastions and turret hold more hit
points than the other's eight tanks, so by the all-in's measure it is not
outnumbered, and the next wave dies on the defences while it rebuilds. The
Sodality mirror resolves, at t=13411 from both orientations. Of the other two
stalls, skirmish-03's armies stand static from t=24000, 8 against 2, which D12's
stalemate draw (P8-24) catches; skirmish-07's winner is razing the last
buildings at the cap, 42 against 0 with 15 within the arrival bound. D12 does
not catch skirmish-04's trade, because damage keeps being dealt; F6's last
clause needs a rule for it, filed as P8-81.

**F4 (`laddergate`, P8-26's input, not binding):**

| stronger rung, seat | base | head |
|---|---|---|
| Normal over Easy, seat 0 | 91.2 | 94.3 |
| Normal over Easy, seat 1 | 90.6 | 83.3 |
| Hard over Normal, seat 0 | 80.6 | 71.4 |
| Hard over Normal, seat 1 | 73.5 | **69.4** |
| Brutal over Hard, seat 0 | 82.9 | 82.9 |
| Brutal over Hard, seat 1 | 71.4 | 73.5 |

Per cent of decided games; undecided 10 and 5 of 216. One half falls below 70
per cent at the head, Hard over Normal from seat 1, by 0.6 of a point (25 won,
11 lost). Normal gained the staging, the growing threshold and the all-in as
much as Hard did, which D6 asks for, so the gap between them narrows; Hard's
retreat (D7) is the one rung difference this row adds, and P8-26, which binds
F4, judges it with D7's counter-composition.

**D32's reversal, read at this head, and met on its letter.** D32 has
`aiairgate` count the commander's harvesters at the answer tick and reverses
"if P8-18 and P8-22 land and the no-raid control keeps a harvester to the end
of the window in every cell", in which case the gate asserts the end-of-window
count as well. The no-raid control reads 2, 3, 0 and 0 harvesters at the base
(Normal Directorate, Normal Sodality, Hard Directorate, Hard Sodality) and **5,
3, 4 and 7 at this head**, the last in a control match the commander won at
t=7426, before the window closes. Asserted at the head, the end-of-window count
would read 2, 0, 0 and 0 in the raided matches (Normal Sodality and Hard
Sodality lose those matches on the ground, Hard Directorate its eleven
harvesters), and fail `aiairgate`, a binding gate, in three of four cells. This
row does not enact it: turning F3's binding gate red is the orchestrator's
call, not a commander row's, and the reading is filed as P8-82 with these
figures. `aiairgate` passes as it stands, every last flyer down by t=5022 with
a harvester alive. **Decided 2026-10-27** (Architect condition 7, recorded in
D32's entry): the orchestrator enacts the reversal through P8-82 as a
reported, non-binding end-of-window half of `aiairgate`, binding in P8-26, and
reopens F3's aftermath clause until then, because the Hard Sodality control
that ends early is a won match holding seven harvesters, which strengthens the
reversal's premise. This ADR's pull request does not change `aiairgate`.

**The binding gates** (all green at the head): `pillargate --bind` both halves,
median first launch 11536, launching in 56 of 72 matches at the base and 42 of
72 at the head (half is 36), at most 3.2 and 3.5 launches per seat per 30
minutes (bar 5); `aiairgate` as above; `cheesegate` 10 of 10; `powerdatagate`;
`aituninggate` with its new stage 2f; `match`, `determinism`, `selftest` and
`golden 2026`; and through `tools/ci-local.sh` the LAN, replay, save and
campaign modes and the balance gate's mirror self-check.

**`seatfairgate`** (F5, not binding): income within 15 per cent in 25 of 32
matches at the base and 22 at the head; the split by start **14/18 and 11/15**,
inside 60/40 at both (15 of 26 is 58 per cent); by seat 16/16 and 14/12.

**`frozenprobe`**: held by an exact cancellation **0** at both, and no walker in
a cycle. Frozen walkers rise from 2 harvesters and 0 units to 2 harvesters and
16 units. The harvesters are not the base's: the base's #208 in skirmish-01's
Sodality mirror is gone, and the head's are one new freeze seen from both
orientations, #43 and #45 in skirmish-03's Directorate mirror, heading to its
refinery at (10, 13) and standing at (11.84, 15.34) from t=10990, the pattern
of P8-75's witnesses, unattributed. Of the 16 units, 10 stand on the very point
they were sent to, attack-moving, with an enemy near it they can neither reach
nor hit, and 6 have no route from their cell to a sighted enemy, in 11 of the
72 matches; none is held by the commander, which re-aims them every 300 ticks,
and both causes are the attack-move's own completion and hunt rules, filed as
P8-80. The first build of this row re-aimed only idle members and left 197
such units in 13 matches (section 3).

**`mirrorprobe`**: every first break is the one the base prints, at the same
tick with the same treasuries, on all 16 two-seat cells (from skirmish-02 DD
at t=988 to skirmish-02 SS and skirmish-03 SS at t=2493) and on all six of
`seats=4`'s seat pairs, each printing that no walker in an exact cancellation
is among what differs. The commander's new choices do not break a mirror
first. `seats=4`'s widest income gap at t=9000 moves from 16.0 to 16.1 per cent
(DD) and from 9.9 to 23.2 per cent (SS), chaotic at its size, recorded for F5's
owner.

**`spendprobe`** (ADR-079's measure, not binding): mean banked 11533, 7653,
15771 and 8908 at the base and 12098, 8165, 17086 and 10880 at the head
(Normal Directorate, Normal Sodality, Hard Directorate, Hard Sodality); units
fielded by t=9000 a seat 60, 97, 59 and 107 against 61, 88, 61 and 107. ADR-079's
reversal (means not below 29725, 15931, 47560 and 26633, or fielded counts not
above 52, 75, 47 and 69) is not met.

**`longmatchperf`** (P8-31's input, not binding): its comment requires a row
that changes the commander's matches to re-baseline the proxy budget in the
same change, so it is: p999 cells relaxed per tick 357451, 65757 and 171729 to
**313409, 54865 and 172228** on skirmish-07, 08 and 09. ADR-079's Architect carry
stands: the budget should be set from a stated reason rather than re-baselined
to each row's reading, and this row re-baselines it again because the comment
says to. The wall-time p999 read 40.2, 11.1 and 19.8 ms on a shared, loaded
machine, so whether F12 moves is unsettled; P8-31 settles it on a calm one.

## Golden hashes

`golden 2026`, diffed in order against the base's `sim/golden-hashes.txt`
(1ad6cb1, whose last line is `aispend`). **Four move and twenty-two are
byte-identical.** Attributed by a traced build of the base and of the head,
each logging every command every commander issues in each golden scenario: the
first tick on which the two logs differ is the first divergence, since a world
given the same commands from the same start is the same world. In the 22
unmoved goldens, `expansion` and `airanswer` among them, the commanders'
command streams are identical to the base's to the last command.

- `skirmish` **0x53C683530CBFEDCD to 0x131014255DA2C334.** First at t=2580:
  both seats, on one beat, send their first field fighter to their staging
  points, (17.5, 14.5) and (78.5, 49.5), each the half turn of the other,
  where the base's commanders issued nothing on that beat. Report line: 39
  entities destroyed and treasuries 15211/12345 at the base, 36 and
  13741/13831 at the head.
- `aisuper` **0x5F3111F1CAD98D45 to 0x46E046432B68B79E.** First at t=1800:
  seat 0 sends its first field fighter to its staging point (18.5, 31.5). The
  report still reads the superweapon placed at t=6001 and fired at the enemy
  refinery by t=11521, destroyed.
- `mission` **0x50C5916BA6AD026A to 0xE93FE639D99E0339.** First at t=675: the
  Rusher's opening. On the beat its ladder queues the factory, it orders a
  rifle squad at the barracks it has just placed. Every assertion the scenario
  makes holds: the timed message fires, the ambush springs, and the camp falls
  at t=2123 with every tagged camp entity dead and seat 0 the winner.
- `aispend` **0x6FF9A772D908875A to 0x649854BB0A3F8D89.** First at t=525: seat
  0 goes all-in. Its six fighters face a rival with no army and no armed
  defence (the yard sealed behind a blocked column, as in `aisuper`), which any
  army outnumbers. **The scenario still reaches and asserts all three states**
  ADR-079's condition 3 named: a later army order on a beat at t=0, orders
  routed over both factory lines by t=4815, and two factories and two barracks
  held at once from t=6181.

**Two lines appended after `aispend` (Architect condition 1).** The retreat
and the walls run on every LAN peer that hosts an AI seat (ADR-033), and no
golden reached either: every golden commander played the Normal rung, whose
retreat is 0, and `mission03`'s Turtle never completes its surplus window. So
two scenarios are appended, each asserting the state it exists to reach, so
that a later row which stops reaching it fails by name. Every line above them
stays byte-identical and in order.

- `airetreat` **0xD73E853E831C4494**. A Hard Standard commander (the shipped
  rung, whose `retreat_percent` is D7's 30) holds a yard, its garrison of
  three cannon tanks at home and a wave of six gathered at its staging point,
  and no credits, so the beat's only orders are the wave's. The rival's yard
  is ringed by four Bastions on a powered grid, holding more than half the
  hit points of the commander's whole army, so it is never all-in. The wave
  launches at t=0, is beaten on the Bastions and, below 30 per cent of the hit
  points it was sent with, is called home at t=480, three living members
  ordered back to the staging point. The scenario asserts the launch and the
  recall. **With the retreat switched off alone** it fails by name,
  "airetreat: the wave launched at t=0 (6 fighters) was never called home in
  1800 ticks", with every line above it unchanged. It plays the shipped Hard
  rung, so P8-26's counter-composition (D7) will move this line under its own
  ADR, which P8-26's row records.
- `aiwalls` **0x49A974BFD74F4497**. A Normal Turtle holds a finished
  Directorate base already at every cap its surplus reads (two factories and
  two barracks, the Normal `producer_cap`, and five Bastions, the Turtle's
  `faction_defence_cap`), its rival sealed behind a blocked column as in
  `aispend`. Its surplus window runs out at t=4500 and its first wall lands at
  t=4501, at (20, 24), on the enemy's side of its turret; the horizon is read
  off the compiled Normal window plus 1500 ticks. The scenario asserts a wall
  placed by the commander and that every wall it places stands on a defence's
  enemy-facing side (24 by t=6000, four round each of its six defences).
  **With the wall branch switched off alone** it fails by name, "aiwalls: the
  Turtle placed no wall in 6000 ticks (window 4500)", with every line above
  it, `airetreat`'s included, unchanged.

**The isolating measurement.** The two goldens-neutral commits before the
behaviour (the measurement, and the data: every number, schema entry, loader
and registration rule and checksum fold) give all 26 of the base's lines in
order. The behaviour commit moves these four and nothing else, and the
re-baseline after it moves nothing.

**Gate fixtures whose premise the doctrine changed**, each adjusted at its site
with the reason, none of them the thing its gate tests: `teamgate`'s
defence-of-an-ally stage switches the all-in off, because six tanks against a
lone rifle squad rightly go all-in at the very base the stage watches;
`campaigngate`'s mission 04 stage switches the Rusher's push off, because the
push took the camp before the column reached the field and the stage proves a
win by ARRIVING; `aituninggate`'s stage 2a acts at the Rusher's rush tick and
stands its six at the staging point, because a Rusher now gathers and holds
its launches until its push.

## Compatibility

This changes what the commander orders, so it is the class Q024
(docs/questions/Q024-a-sim-rules-epoch-for-replays-and-the-lan-hello.md) asks
about; like ADR-079, and unlike ADR-078, it also moves the catalogue checksum,
`0x652D8C7E6D1C847D` to `0x64E6ED1AC64F2076`, read by `aituninggate` and
`catalogueloadgate` from the files and from the compiled table alike.

**Old replays are refused, not diverged.** A replay carries the catalogue
checksum and the seven new numbers move it. Replays never run a commander, so
the command change alone would not have diverged one; the refusal is the
data's. **Saves made before this ADR are refused** on the same checksum. No
save field, version or format changes, and nothing of the commander is saved:
a loaded match's commander starts with no wave in flight, a launch count of 0
and its push unmade, as it starts ADR-079's surplus window again. **Every
effect of a resume mid-match** (Architect condition 5, completed from the
determinism audit's finding 3):

- **The garrison refills from wave members in flight.** With the wave in
  flight empty, the garrison is chosen again from the lowest ids among all
  eligible fighters, which can be units out on the attack; the next intruder
  then pulls them home out of the fight, and an idle one is called home from
  across the map. This brings back, for that save, the defect section 1
  fixed.
- **Idle former members are sent back to stage**: they are field again, so
  the staging step walks them to the staging point.
- **The threshold resets** to `wave_size`, since the launch count is 0.
- **The all-in flag and the launch hit points are lost**, so a beaten Hard or
  Brutal wave is not recalled (its launch hit points read 0).
- **A Rusher resumed after its rush tick restarts its opening** (its barracks
  filled with rifle squads again) **and may push again**, up to a wave and its
  garrison of infantry.
- **A launch is due on the first beat**, since the last launch tick starts
  far in the past.

None of this is a lockstep, replay or hash break: LAN cannot save, commanders
attach only at tick 0, replays carry the commands and run no commander, and a
given save always resumes the same way. `saveload` replays recorded streams
after loading, so it cannot see any of it. Row P8-87 owns commander state in
the save. **A mixed-build LAN game is refused at the hello.**
Q024's open question stands for a later change to the commander's logic that
moves no data: it would pass the hello and desync, and the relay's state-hash
comparison would catch it.

## Alternatives rejected

Each was built and measured unless it says otherwise; the F6 figures are
`pillarprobe`'s window share over its 72 matches. Read them with this caution,
measured along the way: the sweep is deterministic (the commander draws no
randomness), so its only spread is chaos between near-identical commanders,
and one minor rule moved the share by up to 8 matches. A difference smaller
than that is not evidence of a direction, so none of the choices below rests
on one alone.

**Keeping the stream.** It is the defect the row names: a median launch of 4
units, 21.5 a seat, walking out one behind another.

**An all-in on heads rather than hit points.** Ten rifle squads would read as
outnumbering three heavy tanks. Built with heads, with and without the enemy's
defences counted, it closed matches at least as early as the hit-point rule
(F6 16 to 26 of 72 across four variants), and it is the less honest measure of
an army.

**An all-in held until the threshold has grown to its cap ("escalate, then
close"), or until the enemy has been outnumbered for a whole wave cadence.**
The first read better on one run (32 of 72 at a cap of four waves) but starved
the `mission` golden: its Rusher, its economy dead, never grew to its cap, so a
commander outnumbering the camp five to one never closed, and the scenario's
assertion that the camp falls within 9000 ticks failed. A commander that will
not commit against a beaten enemy is the defect "close" exists to fix. The
second moved nothing (20 of 72 with it and without).

**No all-in.** F6 read higher on some runs (25 to 35 of 72), but the row
requires the all-in, and without it matches the stream used to close reached
27000 with no result (up to 10 of 72).

**A retreat at Normal.** Measured with the all-in, a Normal wave that came home
at 30 per cent ended more matches before 13500, not fewer (F6 12 of 72), and D7
gives the retreat to Hard and Brutal. Normal stays at 0.

**A cap of four waves (24, 16 and 40).** No better than two on any reading that
survives the caution above (20 of 72 against 26 on the same build), and twice
the wave is the plainer rule.

**Re-aiming only the idle members of a wave.** It left whole waves standing
within sight of a building they could not route to (section 3).

**Re-aiming an arrived member at the building nearest itself.** Built to free
the same waves: it froze more units (236 against 199) and held one walker in an
exact cancellation, which `frozenprobe` must read 0.

**The Rusher's push without an opening, or without the hold.** Without the
opening the push, which waits for four infantry, went at a median t=2648;
without the hold the ordinary first wave left at a median t=1035 and mooted it.

**Walls on every side of a defence, or walls from the ladder.** A full ring is
twelve cells round a 2x2 defence, three times the walls on sides no attack
comes from first, closing the routes beside the defence that the base's own
fighters walk; a wall in the ladder would spend credits a producer or a
harvester needs, where the surplus spends only what ADR-079 already judged
spare.

**Staging at a fixed fraction of the way to the target.** A quarter of the way
gathers a wave 67 cells out on skirmish-07 and about 20 on skirmish-01, where
what a staging point means, the edge of one's own base, is the same distance
on both.

## What reverses it

- **The row has failed its purpose** if, at a head carrying it, `waveprobe`'s
  pooled median launch is not above the base's 4 units, or its pooled launches
  per seat are not below the base's 21.5: the stream would be back.
- **The climax has gone** if `pillargate --bind` fails either half; the launch
  half is the one this ADR thinned (42 of 72 launching, half is 36). Then the
  all-in's share rises before anything else changes.
- **It has broken a symmetry** if `seatfairgate`'s split by start leaves 60/40,
  if a `mirrorprobe` first break, two-seat or `seats=4`, moves earlier than the
  base's, or if the balance gate's mirror self-check fails.
- **The retreat is wrong at the rungs that have it** if P8-26, judging D7,
  finds a `laddergate` half of Hard or Brutal below 70 per cent with
  `retreat_percent` at 30 and at or above it with that rung's retreat at 0.
- **The Rusher's push is not at 90 seconds** if `waveprobe p0=rusher p1=rusher`
  puts the median first launch more than one wave cadence (300 ticks) from
  `rush_tick`, or its sizes outside a wave to a wave and its garrison.
- **The Turtle's walls are not built** if fewer than half the Turtle seats in
  `waveprobe p0=turtle p1=turtle` place any, or **they seal a base** if
  `frozenprobe p0=turtle p1=turtle` reports any walker with no route from its
  cell whose route exists once its own side's walls are open (its line
  "walkers with no route from their cell whose route exists once their own
  side's walls are open"). Reworded under Architect condition 3, because the
  clause as first written named arguments `frozenprobe` refused: the probe now
  takes `p0=` and `p1=` as `waveprobe` does, and asks that question of every
  walker it finds with no route, on a copy of the map with the walker's own
  side's walls unblocked, beside two printed controls (a walker ringed by its
  own side's walls has a route once they are open, and one ringed by the
  other side's has none). **Baseline at this head: 0, in 0 of 72 matches**;
  the Turtle mirrors freeze 6 units (2 at their target, 2 with no route, 2
  other) and no harvester, and both controls answer as they should. The
  default sweep, `p0=` and `p1=` unset, still reads 2 harvesters and 16 units
  frozen and 0 sealed.
- **A walker is held** if `frozenprobe` reads any walker in an exact
  cancellation (it reads 0), or more than the 16 units it finds frozen at this
  head; P8-80 owns those 16.
- **A cross-platform golden mismatch in CI** stops it before merge, as it would
  any golden move.

## Consequences

The commander's waves now gather, grow and arrive as bodies; the Rusher pushes
at 90 seconds in every seat of its mirror sweep, and the Turtle walls its
defences in most. The binding gates are green. What it leaves, each recorded
with its owner:

- **F6 is not met and moves away**: 22 of 72 in the window and 6 with no
  result, against 27 and 3. The rebuy's reversal is read and not met. The
  stalls are a stalemate D12 catches (skirmish-03 DD), a slow close at the cap
  (skirmish-07 DD) and a contested trade D12 does not catch (skirmish-04 DD,
  P8-81). P8-24 inherits all three.
- **F4 has one failing half** at the head, Hard over Normal from seat 1 at
  69.4 per cent. P8-26 inherits D7's retreat, authored here, to judge and bind
  with the rest of the rung shape.
- **D32's reversal is met on its letter**, and enacted through P8-82 as a
  reported, non-binding end-of-window half of `aiairgate` that binds in P8-26;
  F3's aftermath clause is reopened until then (D32, decided 2026-10-27).
- **16 frozen walkers**, all the attack-move's own completion and hunt rules:
  P8-80. The skirmish-03 harvester freeze is a candidate witness for P8-75.
- **P8-29** narrows `IsGroundFighter`'s enemy count, with the target picks, to
  what the commander may see (D9). The enemy's armed defences are buildings,
  which this commander counts through fog today; P8-29 decides whether they
  join the cloak filter.
- **P8-27** owns openings, wave jitter and weighted targets. The Rusher's
  opening here is the one its push needs, and P8-27's per-personality
  openings absorb it or record why not.
- **P8-26** owns any first-attack tick and early wave scale (D7: Easy 7200 and
  0.5): the launch decision is one block (the threshold, the all-in, the push
  and the hold), so a rung's first-attack tick joins the hold and its wave
  scale multiplies the threshold.
- The commander's wave state is not saved, the same class as `_surplusFrom`
  and `_airSeen`; if commander state ever enters the save, the wave in flight,
  the launch count and the push join it. **Row P8-87**, owned by the sim
  engineer, now carries commander state in the save (Architect condition 6):
  the wave in flight, the launch count, the push, the all-in flag, the surplus
  window and the air census. The effects of a resume are listed under
  Compatibility.
- **Seams to the rows that follow** (Architect condition 6), each recorded in
  that row's cell of the tracker: P8-24 inherits the six unresolved matches
  and f0c9b74's reversal; P8-25, P8-26 and P8-27 read or extend the launch
  decision, and the first of them to touch it extracts the wave doctrine into
  a unit of its own first, in a goldens-neutral commit proven byte-identical;
  P8-26 inherits the retreat and the failing `laddergate` half, and its
  counter-composition will move `airetreat`; P8-29 inherits `IsGroundFighter`
  as the place to narrow the all-in's count; P8-31 inherits the third
  consecutive re-baseline of `longmatchperf`'s proxy.

## Architect sign-off

- Date: 2026-10-27.
- Reviewer: Systems Architect (A3).
- Pull request: the P8-23 pull request from branch `worktree-wf_0cca15af-a25-1` into main. It is not yet pushed or opened, and its number is recorded here when it opens. I reviewed its five commits, ec7202e, c1b2bf3, 8d7a389, 700c8c1 and 0b51678, on 1ad6cb1, the head of P8-22's branch before the squash. Pull request 175 has since squash-merged as 4076626, whose tree is identical to 1ad6cb1's. A determinism audit and an adversarial verifier each reviewed the range independently.
- Verdict: **signed off with conditions.** The design is approved:
  - the staging point at ADR-053's arrival bound, inside `idleprobe`'s home bound;
  - the growing threshold with its two fallbacks;
  - the wave in flight as a membership-only set, every member re-aimed on the cadence;
  - the retreat as a rung key at D7's values;
  - the all-in in hit points, counting the enemy's armed defences;
  - the field defending its base;
  - the Rusher's opening and hold;
  - the Turtle's walls from surplus, with `faction_defence_cap` not retuned;
  - the split of the seven numbers between personality and rung under DR-14.

  The four golden moves, `skirmish`, `aisuper`, `mission` and `aispend`, are approved as attributed. So is the catalogue checksum move, `0x652D8C7E6D1C847D` to `0x64E6ED1AC64F2076`. The pull request merges once conditions 1 to 9 below are met in it.

**Reproduced, on macOS only.** Most figures come from the audit's and the verifier's builds, each in a tree of its own. The four below I measured myself, in trees exported from 0b51678.

- **Goldens.** `golden 2026` at the head matches the committed `sim/golden-hashes.txt` in an ordered diff. Against 1ad6cb1, exactly `skirmish`, `aisuper`, `mission` and `aispend` move.
- **ADR-079 condition 3's coverage survives the move of `aispend`.** I made `Route` take the first producer at this head. The other 25 lines stay at the head's values, and `aispend` fails by name: "no kind had orders routed over two of its lines in 7500 ticks (factories ordered 1, barracks 1)". `aispend` is still the routing's cross-platform coverage, and it still asserts all three of its states (t=0, 4815 and 6181).
- **The retreat and the walls are outside the golden gate.** With the retreat forced off and the wall branch disabled together, all 26 lines are byte-identical to the committed file.
- **Integration.** `git merge-tree --write-tree --merge-base=1ad6cb1 origin/main 0b51678` gives ab49c6d, identical to the head's tree. A plain merge conflicts in 16 files.

From the verifier, each reproduced:
- `tools/ci-local.sh` is green on 20 of 20 steps, ending with `pillargate --bind`.
- The isolation holds: c1b2bf3 alone, and the head with 8d7a389 reverted, each give the base's 26 lines.
- The first divergences hold, from traced command streams: t=2580, 1800, 675 and 525. The other 22 scenarios have identical streams.
- `aituninggate` stage 2f bites 7 of 7 numbers by name.
- `mirrorprobe` breaks are unmoved, and the reflection test finds 70660 twin staging pairs and 960 twin wall calls, with no non-twins.

Windows and Linux are left to CI.

**The break is justified.** D6 names staging, escalation and closing at Normal, and says the commander goldens regenerate under each AI row's ADR. D7 names the retreat. The row's own measure says the stream is gone:

| `waveprobe`, 144 seats | base | head |
|---|---|---|
| launches per seat, median | 21.5 | 8.5 |
| launch size, median | 4 | 9 |
| broke in, lost (per cent) | 34, 24 | 44, 15 |
| Rusher first launch | median t=2580, a stream of 3 | t=1350 in all 144 seats, 5 to 6 |
| Turtle seats walling | 0 | 102 |

The checksum move is the mechanism working: old replays, saves and mixed-build LAN games are refused, not desynced.

**The design holds.**
- **Determinism.** Everything new in `/sim` is integer or Fix64 arithmetic over the commander's own counts, hit points and positions. Every product that could overflow is computed in `long`. `_wave` is only asked Contains, Add, Remove, Clear and Count; every walk over it is a walk over the entity list in index order. Entity indices are only appended, so wave ids stay stable. The new state is AI state, like `_surplusFrom`: never hashed and never saved. That is safe across peers, because LAN commanders attach only at tick 0 and replays carry the commands.
- **Mirror exactness.** The staging offset is a product and a quotient, both truncating toward zero (ADR-077), so twins negate exactly. The boundary rule takes the home side. The wall cells use `TryFindPlacement`'s canonical frame. The one tie in each is a centre exactly on the map's centre line, as in ADR-075. The positive controls bite.
- **Data.** Each of the seven keys has a schema entry, a compiled reference, a refusal in the other family through one key table, refusals in the loader matched by registration, and a fold into `CatalogueChecksum` inside the sorted-id loop. Stage 2f proves that the authored value wins, which is the standard ADR-032 asks for.
- **Isolation.** The measurement, the data and the re-baseline commits are goldens neutral, and only the behaviour commit moves goldens.

**What is wrong.**
- **The retreat and the walls run on LAN peers and nothing cross-platform sees them.** I confirmed this above. ADR-079 closed the same gap for the routing.
- **Two live guards are tested by nothing.** One is the launch when a threshold has stood ungathered for a cadence. The other is the wall's exclusion from the apron and from the field. The second is the safety property section 8 states, and its reversal clause names a `frozenprobe` argument the tool refuses.
- **`staging_cells` has no ceiling.** A huge value hangs every commander, on both peers alike. And a `rush_tick` past the end of a match silently disarms one, because the hold waits for it.
- **The record is wrong or silent in several places.**
  - The stalled-field launch's stated reason, the `mission` golden, no longer holds at the head. Only `skirmish` moves without it.
  - The effects of a resume are under-recorded. A resume mid-wave refills the garrison from units in flight, which brings back the defect section 1 fixed.
  - The wall's credit test ignores what the beat has already committed.
  - The wall branch's comment says it is inert in "every golden", but `mission03` runs a Turtle.
  - Section 6 says the field defends its own base; the code answers any allied base.
  - Order precedence within one beat is unstated.
- **D32's reversal is met on its letter and not enacted.** That is a decision for the orchestrator, and it is due before merge.
- **Records lag.** ADR-079's D32, rebuy, F6 and ladder lines do not point here. D7 and D33 are not re-read at this head; D33's launching margin is now six matches. Downstream pending cells do not say what they inherit. PR 175's squash merge has made three sentences stale.

**Conditions before merge.**

1. **Cross-platform coverage of the retreat and the walls** (code; comes back to me).
   - Append one golden scenario, or two, after `aispend`.
   - It must reach a wave called home by the retreat on a rung whose `retreat_percent` is above 0, and a wall placed by `TryFindWallCell`. It asserts both states, so a later row that stops reaching them fails by name.
   - The existing 26 lines stay byte-identical and in order.
   - Switching off the retreat alone, and the walls alone, each fails the new scenario by name.
   - Stage 2f's fixtures may be the starting point. The map, horizon and rung are the implementer's choice. If it uses the shipped Hard rung, P8-26's cell records that its counter-composition will move this line.
   - "Golden hashes" records the line without writing a count into prose.
2. **Bite the two unguarded guards** (goldens neutral; comes back to me). Add an `aituninggate` stage with one check and one control for each, failing by name when its guard is removed:
   - the launch when a threshold has stood ungathered for a wave cadence;
   - the wall's exclusion from the apron and the field: a defence whose enemy-facing cells all lie in another building's apron, or beside a ferrite field, gets no wall there, and the same defence clear of them gets one.

   If either check cannot be built through the public surface, ADR-080 names that guard as unguarded and says why.
3. **A runnable wall-sealing clause** (runner only, goldens neutral; comes back to me). Do one of these, then run it at the head with Turtle mirrors and record the reading as the clause's baseline:
   - extend `frozenprobe` to take `p0=` and `p1=` as `waveprobe` does, and to report a walker with no route whose route exists once its own side's walls are treated as open; or
   - reword the clause to a measure an existing tool prints.
4. **Bound `staging_cells`** (goldens neutral; comes back to me).
   - The loader, registration and schema refuse a `staging_cells` above a ceiling derived from a number already in the game. I suggest `idleprobe`'s 20-cell home bound (ADR-056): it is the property section 1 claims, and stage 2f's moved value already sits on it.
   - Registration refuses in the loader's words, and stage 4 shows both the refusal and that the edge registers.
   - The `rush_tick` description states the hold.
5. **Correct the record** in ADR-080, and wherever the tracker row, the schema descriptions or the `SkirmishAI.cs` comments repeat it:
   - **The stalled-field launch.** Its reason was measured on an earlier build. At the head it moves `skirmish` (to 0xB888EA387D513752), not `mission`.
   - **Resume.** Compatibility lists every effect: the garrison refills from wave members in flight; idle former members are re-staged; the threshold resets; the all-in flag and the launch hit points are lost; a Rusher resumed after its tick restarts its opening and may push again; a launch is due on the first beat.
   - **The wall's credit test** ignores committed credits, the class ADR-079 recorded. Section 8 and the `surplus_reserve` description list it.
   - **The wall branch's comment.** `mission03` runs `SkirmishAI.Turtle(0)`. It stays quiet only because its surplus window cannot complete within its horizon, so a change to either moves that golden.
   - **Section 6.** The field answers an intruder at its team's base. Record that with the reason; any narrowing is a later row's behaviour change.
   - **Order precedence within one beat.** A base-defence order followed by a staging order, where the defence waits up to one 60-tick cadence. A retreat followed by an all-in.
6. **Records and seams** (docs only).
   - ADR-079 gains forward pointers in four places: at its D32 consequence; at the rebuy clause (read at P8-23's head, not met, 22 and 26 of 72); at its F6 consequence (27 to 22, and 3 to 6 unresolved); and at its ladder sentence (one half below 70 per cent at this head).
   - D7 and D33 record their reversals as read at this head and not met: no inversion; a median of 11536; 42 of 72 launching against 36.
   - The pending cells of P8-24, P8-25, P8-26, P8-27, P8-29 and P8-31 say what each inherits from ADR-080.
   - A row is filed, owned by the sim engineer, for commander state in the save. That state is now the wave in flight, the launch count, the push, the all-in flag, `_surplusFrom` and `_airSeen`.
7. **D32** (the orchestrator, before merge).
   - The orchestrator records a decision on P8-82. D32's entry records the reading at this head, and F3's status reflects it.
   - My recommendation is to enact the reading through P8-82 as a reported, non-binding end-of-window half of `aiairgate` that binds in a named commander row, and to reopen F3's aftermath clause until then.
   - "The letter does not apply" is the weaker reading. The Hard Sodality control that ends early is a won match holding seven harvesters, which strengthens the reversal's premise.
   - This pull request does not change `aiairgate`.
8. **Status and ratification.**
   - The orchestrator ratifies ADR-080 under a new decision.
   - Its status, its open-queue entry and row P8-23 stop calling it Proposed, and cite this section and the pull request's number.
   - This date is confirmed or corrected in the same commit.
9. **Integration, CI and hygiene.**
   - Run `git rebase --onto origin/main 1ad6cb1`. The tree must equal ab49c6d.
   - If main has moved past 4076626, re-measure: the 22 unmoved goldens equal main's, the four moved values reproduce, and the checksum is recorded. Any other difference comes back to me.
   - Fix "pull request 175 being unmerged" in row P8-23 and the open queue, and "origin/main carries no ADR-079 yet" in ADR-080's status.
   - All three CI jobs are green. The determinism job reproduces the four moved hashes and condition 1's line on Windows and Linux, and `pillargate --bind` passes in the ubuntu leg.
   - `tools/ci-local.sh` is run again at the final head.
   - Nothing under `.claude/agent-memory` is committed, no attribution trailer is added, and no em or en dash is introduced.
   - Conditions 1 to 4 come back to me for a check. So does any other commit beyond these conditions, and any golden other than those named.

**Carried.**
- **The wave doctrine should be extracted** into a unit of its own that holds its own state, behind a narrow surface. Three rows will read or extend the launch decision: P8-25's scan before a wave and jam during a push, P8-26's first-attack tick and wave scale, and P8-27's jitter. The first of them to touch it extracts it first, in a goldens-neutral commit proven byte-identical.
- **F6, F4 and D33.**
  - F6 moves away from its bar (22 of 72, 6 unresolved): P8-24 and P8-81.
  - F4 has one failing half: P8-26 judges it with D7's retreat.
  - The next commander row that thins launching reads D33 first.
- **`longmatchperf`.** It was re-baselined for the third consecutive commander row. My carry on P8-31 stands: set the budget from a stated reason.
- **Seat fairness, non-binding.**
  - `seatfairgate`'s income clause falls from 25 to 22 of 32.
  - `mirrorprobe seats=4`'s Sodality gap widens from 9.9 to 23.2 per cent.

  Both are chaotic at their sample sizes, and both go to F5's owner.
- **Frozen walkers.** The 16 units go to P8-80. The skirmish-03 harvester pair is a candidate witness for P8-75.
- **Q024** is settled before the first published build.

### Ratification record (D41)

- **Date confirmed: 2026-10-27.** The section names pull request 175's
  squash merge, 4076626, committed at 23:53 UTC on 2026-10-26 (00:53 on
  2026-10-27 in the owner's time zone), so it was given after that, on the
  date it carries. The five commits it reviewed are dated 2026-10-14.
- **The pull request.** The section above names the branch the lane was built
  on, `worktree-wf_0cca15af-a25-1`. Under condition 9 its five commits were
  rebased onto main from 1ad6cb1, with main at 4076626 (pull request 175's
  squash merge, whose tree equals 1ad6cb1's), as branch
  `claude/p8-23-attack-waves`; the rebased head's tree was ab49c6d, equal to
  the reviewed head's. The pull request number is recorded when it opens.
- **Conditions 1 to 9**, one commit each for 1 to 7 and one for 8 and 9:
  1. `airetreat` and `aiwalls`, appended after `aispend` ("Golden hashes"),
     each failing by name with its branch switched off alone, every line above
     unchanged; P8-26's row records that its counter-composition will move
     `airetreat`.
  2. `aituninggate` stage 2g (section 9), each removal failing its own check by
     name.
  3. `frozenprobe` takes `p0=` and `p1=` and reports a walker with no route whose
     route exists once its own side's walls are open; with Turtle mirrors it
     reads 0 in 0 of 72 matches, recorded as the clause's baseline ("What
     reverses it").
  4. `staging_cells` bounded at 20, `idleprobe`'s home bound, in the loader,
     registration and schema, with stage 4 showing the refusal and the edge;
     the `rush_tick` description states the hold.
  5. The record corrected: section 2's stalled-field reason, Compatibility's
     resume effects, section 8's credit test and `mission03`, section 6's team
     base, and section 11's order precedence.
  6. ADR-079's four forward pointers; D7 and D33 read at this head; the pending
     cells of P8-24, P8-25, P8-26, P8-27, P8-29 and P8-31; row P8-87.
  7. D32 decided by the orchestrator: enacted through P8-82 as a reported,
     non-binding end-of-window half of `aiairgate` that binds in P8-26, with
     F3's aftermath clause reopened meanwhile; `aiairgate` is unchanged.
  8. Ratified under D41; this status line, the open-queue entry and row P8-23
     no longer say Proposed.
  9. The stale wording PR 175's squash merge left ("pull request 175 being
     unmerged", "origin/main carries no ADR-079 yet") is gone; CI's three jobs
     and the re-run of `tools/ci-local.sh` at the final head are the pull
     request's own.
