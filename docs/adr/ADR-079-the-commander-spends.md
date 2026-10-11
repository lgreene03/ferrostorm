# ADR-079: the commander spends: a sustained surplus buys production up to its rung's cap, every order takes the shortest line, and a beat places as many orders as its reserve allows
- Status: **Proposed** 2026-10-10 (row P8-22 of docs/tickets/P8-formidable-tracker.md, decisions D6 and D7). Built on P8-66 (ADR-078, pull request 172, branch `claude/p8-66-tie-break` at ef45c9e, which carries main's merge of #171), because the Architect ruled that P8-66 lands before any commander row; every figure below is measured against that head. It moves goldens, which is a replay-compatibility break needing an ADR and the Architect (CLAUDE.md), and it changes what the commander does, so a replay of an AI match recorded before it diverges (Q024). It moves the catalogue checksum (data/ai). It lands as six commits: three goldens-neutral ones (the probe, the four numbers in data/ai, the window and the probe's fixed-time reading), the behaviour, the harvester rebuy, and the proxy budget re-baselined; this ADR, its queue entry, ADR-060's forward pointer and the tracker row follow in one docs commit. Drafted by the implementer agent.
- Date: 2026-10-10
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD s9 line 76 (Normal "competent build orders", Hard "strong macro"); GDD s4 (the floating economy); GDD pillar 2 (matches of 15 to 30 minutes); TDD s1 ("determinism is the product"); criteria F4, F5 (its split by start), F6 and F8 of the P8 tracker; decisions D6, D7, D16, D32 and D33; findings AI-03, ML-04 and OJ-04; ADR-032 (the AI's tuning is authored data), ADR-045 (ask for a capability, not a type id), ADR-060 (a side's own defence, bought from surplus, whose refusal to build several this ADR amends), ADR-072 (which left the harvester rebuy to this row), ADR-073 (D33's purchase floor), ADR-076 and ADR-078 (the half turn and the reflections the commander must stay exact under)

## Context

D6 records a Hard commander holding 16401 credits at 540 s on one factory, and
P8-22 is the row that spends it. `spendprobe`, added for this row (a probe by
ADR-061's rule: rung mirrors at Normal and Hard on every shipped map in the
four faction pairings, from the authored starts, in the shipped setup, each
seat's treasury read every 15 ticks), measures the base at seed 2026:

| rung and side (36 seats each) | mean banked | by 5-minute window | at or above 4500 | peak, median and most | most barracks, factories |
|---|---|---|---|---|---|
| Normal Directorate | 29725 | 10429/22633/29991/40358/43815/51487 | 93 per cent of seconds | 39108, 146820 | 1, 1 |
| Normal Sodality | 15931 | 10603/17138/15907/16956/21453/18698 | 77 per cent | 22476, 69179 | 1, 1 |
| Hard Directorate | 47560 | 11822/31707/50726/70969/79296/91465 | 94 per cent | 71862, 198123 | 1, 1 |
| Hard Sodality | 26633 | 11801/24957/33202/38761/31560/25032 | 86 per cent | 43252, 120173 | 1, 1 |

The treasury rises for the whole match because one barracks and one factory,
each held to two orders, cannot spend the income, and one order a beat cannot
fill even those two lines. 45 of the 72 matches were decided by 27000 ticks.
The ladder could not tell its rungs apart: `laddergate` failed five of its six
halves, Brutal over Hard at 50 per cent from both seats, because a rung that
mines twice as much has nowhere to put it.

## Decision

### 1. A sustained surplus, defined and authored

The commander reads its treasury on every decision beat. The surplus is
SUSTAINED once the treasury has stood at or above the rung's `surplus_reserve`
on every beat for `surplus_window_ticks`, unbroken; a beat below the reserve
starts the window again. The moment it rose is AI-internal state, like every
field of the commander: never hashed, never saved, and a loaded match's
commander starts the window again, exactly as it relearns what air it has
seen (ADR-072).

**The reserve is 4500**, the superweapon rung's own credit test and the
dearest in the ladder, so nothing bought out of surplus can starve a rung.
It equals that test by transcription, not by reference: the rung reads a
literal 4500 in `SkirmishAI.cs`, and the reserve is a copy of it in data/ai,
so a row that moves either re-reads the other.

**The window is 4500 ticks, five minutes, and it was measured into shape.** At
the base, of the 423 stretches a Normal or Hard treasury spent at or above
4500 and then lost before its match ended, 357 lasted under two minutes, one
between two and three, none between three and five, and 65 five minutes or
more. Any window inside that gap tells a passing bank from income. Two minutes,
its near edge, was built and measured with the commander reading it and failed
two of this row's bars (section "Alternatives rejected"); five minutes, its far
edge, passes them. The same at every rung; whether a rung should differ is
P8-26's.

### 2. More production, up to the rung's cap, when production is the bottleneck

Once the yard's ladder wants nothing, something is mining and the surplus is
sustained, the yard buys, in this order, the first that applies:

1. another FACTORY, while fewer than the rung's `producer_cap` stand and every
   factory standing already has an order on its line as the beat opens;
2. another BARRACKS, on the same two conditions;
3. another of its side's own defence (section 5).

Each is asked of the catalogue as a capability (`BuildableStructOfKind`,
`BuildableFactionDefence`, ADR-045), each needs its prerequisites standing
(a building the yard is refused stalls it forever), and each is bought only
while its price still leaves the reserve. Its price joins the beat's
commitments, and those bind only the army's orders after the beat's first.
Nothing else in the beat reads them: the building's own test reads the
treasury alone, so it does not count an engineer ordered earlier in the beat;
the anti-air answer tests its own price, the MCV a floor of 3500 credits and
the harvester no price at all; and the first army order stands on its own
price (section 4). So a beat that buys a surplus building can end below the
reserve by the first army order's price, and by that of any engineer,
anti-air answer or harvester the beat also ordered. The factory
comes first because its units are the dear ones; a producer before a defence
because a gun spends once and a producer for the rest of the match. "Every
line busy" is what makes another producer the answer: a line that sits empty
says that kind is not the bottleneck. The caps are D7's: Easy 1, Normal 2,
Hard 3, Brutal 3. Easy's 1 is the ladder's own barracks and factory and no
more. D6's identity rule holds, because `difficultygate`'s identity claim is
that the default commander IS the Normal row, whatever that row reads.

### 3. Every order takes the shortest line

Every Produce goes to the producer of its kind with the fewest orders on its
line, ties to the OLDEST of that kind, which is the first in entity order,
because entities are only ever appended and a commander's producers are its
own build order. A queue's depth counts the orders this beat has already
placed on it, so the depth guard of two (ADR-009 clause 7) reads each line as
it will stand. The harvester, MCV, engineer and anti-air guards count what ANY
line of their kind holds, which the one-line guards implied while there was
one line to be on. The commander used to name the LAST producer of a kind it
found, which with two would have left the other idle.

**While the anti-air answer is short, every line of its kind holds its army
orders**, not only the line the answer takes, which is all P8-17's hold
covered while there was one producer. Under unanswered air a second
factory's tank is fed to the flyers just as the first's would be. It is inert
until air is seen.

**A unit produced anywhere but a barracks or a factory gets no line**
(`LineFor`). If a unit cycle ever holds one, the army loop ends every beat at
it without advancing the cycle, and the army stops. Shipped cycles hold no
such unit (only aircraft are produced elsewhere, and D10 keeps the commander
out of the air in P8), so the row that adds one, P8-28 or a flying commander
after D10, extends `LineFor` in the same change.

### 4. Several orders a beat, the later ones only above the reserve

The unit cycle is walked in its own order, one unit after the next, for as
long as each can be placed; the first that cannot ends the beat, so the
doctrine's mix is kept rather than topped up with whatever has room. The first
order of a beat stands on its own price, exactly as the one order always did;
every later one only while the beat's orders so far still leave the reserve.
A beat that opens at or above the reserve can therefore end below it by one
unit's price, plus that of any engineer, anti-air answer or harvester placed
earlier in the beat (section 2), whether or not it bought a surplus building.
The depth guard bounds how many a beat can place.

### 5. Further faction defences from surplus (amends ADR-060)

ADR-060 bought one of a side's own defences from `Cost + 1500` and refused
several, "to overturn: a playtest showing one faction defence is decorative".
Row P8-22 names "further faction defences from surplus" from findings AI-03,
ML-04 and OJ-04 under decisions D6 and D7, so this ADR overturns that refusal
on the tracker's authority and records it there. The first is bought exactly
as ADR-060 bought it. Every one after it is bought only from a sustained
surplus, after any producer the rung's cap still allows, while fewer than the
personality's `faction_defence_cap` stand: Standard 3, Rusher 2, Turtle 5,
each the personality's own ground garrison (half its wave, with a floor of
two), so a base keeps as many signature guns as fighters at home. Each cap is
transcribed from that rule, not derived from the wave size when loaded, so a
row that moves a personality's wave size re-reads its cap in data/ai. It is a
personality number, beside the wave size, because how many guns guard a base
is taste (DR-14). ADR-060's measurement, that an ungated rung flipped the
faction war, is why these are gated on the window and the reserve: they never
reach the opening.

**The faction war is not flipped.** The Architect ran the balance tool at the
base and at this ADR's head. It exits 0 with a PASS verdict at both, and its
mirror self-check is unchanged (28 annihilations, 12 cloaked runs identical).
Only its faction war and its checksum line differ, in cells of six:

| | Directorate | Sodality | Undecided | Seat 0 | Seat 1 |
|---|---|---|---|---|---|
| base | 3 | 1 | 2 | 3 | 1 |
| head | 4 | 2 | 0 | 5 | 1 |

The Directorate leads at both, so ADR-060's measured harm does not recur; the
undecided cells resolve, and the credits banked at the end of the base's
undecided or late cells (up to 49150) are gone at the head (at most 10087).
The seat split of 5 to 1 is read as chaotic at six cells, and `seatfairgate`
stays the measure of seat fairness. F11's faction balance (Directorate 4 of 6
cells, Sodality 2) goes to P8-33.

### 6. The harvester rebuy takes the shortest factory line with room

The harvester was bought only when the factory's line was EMPTY. The army
keeps every line one or two deep, so a commander that lost harvesters waited
for an empty line for the rest of the match; ADR-072 measured 17000 credits
banked on one harvester for 4000 ticks that way and left the rebuy to this
row. The harvester now takes the shortest factory line while it holds fewer
than two orders, this beat's own included, and it is placed before the army in
the beat. The MCV keeps the empty-line guard: the army stands aside while an
expansion is wanted, so the line empties of itself. It is its own commit, so
it can be judged, and reverted, alone (section "What reverses it").

### 7. The numbers are authored data (ADR-032)

`producer_cap`, `surplus_reserve` and `surplus_window_ticks` are RUNG keys and
`faction_defence_cap` a PERSONALITY key, split by DR-14's rule (production and
patience are macro; how many guns guard a base is taste). Each has a schema
entry in `data/schema.ai.json`; a compiled reference in `AiTuning` that the
files reproduce (`aituninggate` stage 1); a loader that demands it in its
family and refuses it in the other, through the one key table per family;
registration that refuses a rung producer cap below 1; and a place in
`World.CatalogueChecksum`, which moves on one unit of each (`aituninggate`
stage 3), because two LAN peers holding different files would build different
bases from the same bank (ADR-032 clause 2). **`aituninggate` stage 2d proves
the data wins over the compiled default**: every run registers data/ai from the
files first, and then moves one number on the row the files registered. A
finished Directorate base orders a second factory at the authored producer cap
of 2 and none at 1, a second Bastion at the authored faction defence cap of 3
and none at 1, nothing 30 ticks into the authored window and a Bastion with a
window of 30, and one army order on 3000 credits under the authored reserve
against two at a reserve of 0. Proved to bite: making the commander read any
one of the four from the compiled reference instead of the registered row
fails the stage, each on its own check (the window's on the producer cap's
control, which needs the window moved to 0).

**`aituninggate` stage 2e guards the four conditions the spending rests on**
(the Architect's condition 2). The verifier had removed each in a scratch build
and found every golden identical and `match` green, so each was a decision in
this ADR that nothing held. Stage 2e runs on stage 2d's fixture, with the
authored Normal reserve read from the registered files, and puts each check
beside a control that differs from it only in what the condition reads:

- (a) **No surplus building while nothing is mining.** With the window at 0
  and both producers idle, the base with its two harvesters orders the second
  Bastion and the same base with none orders no building at all.
- (b) **The surplus building's own reserve test.** With the factory busy and
  the window at 0, a treasury of the reserve plus a factory's price orders the
  second factory, and one credit less orders none. The busy factory draws its
  tank's price as it builds, so the treasury at the beat is measured and the
  check asserts it is exactly those two sums.
- (c) **The surplus building's price joins the beat's commitments.** On a beat
  that buys the Bastion, a treasury of the reserve plus the Bastion, a rifle
  squad and a rocket squad places both squads, and one credit less places only
  the rifle.
- (d) **The first order's price counts against the later ones.** With the
  authored window, so no surplus building on the beat, the reserve plus the
  rifle and the rocket places both, and the reserve plus the rocket alone
  places only the rifle.

Each was bitten by removing its condition from `SkirmishAI.cs` and running the
gate, and each fails on its own named check: dropping `harvesters > 0` from the
surplus branch fails 2e(a) (one building ordered with no harvester alive);
dropping the building's `Credits >= reserve + price` test fails 2e(b) (one
factory ordered one credit short); dropping `committed += extraCost` fails
2e(c) (two orders one credit short); dropping `committed` from the later-order
test fails 2e(d) (two orders). (d) is checked before (c), because (c)'s later
order reads the same commitments and would also catch (d)'s removal under the
wrong name. All four are reachable through the public surface, so none is
left unguarded. Unbitten, the gate passes and `golden 2026` is byte-identical.

### 8. Mirror-exact by construction

Every new choice reads only this commander's own state: queue lengths,
counts of its own buildings, its treasury, and the order its own producers
were built in. Nothing reads a position, a world axis, or an id compared across
seats. A commander and its twin on the opposite start see the same queues and
built their producers in the same sequence, so the oldest is the twin of the
oldest under the half turn and under either centre-line reflection, and where
a new building stands is `TryFindPlacement`'s, already oriented (ADR-075,
ADR-076). `mirrorprobe` confirms what it can see: no first break moves
(below). It cannot see the tie between two lines, because every first break
is a walker's by t=2668 and the earliest median second factory is t=4815, so
that tie's exactness rests on the construction above rather than on the probe.

"The oldest" is the first in entity order only while producers are appended
and never change hands. A captured producer keeps the index it had on the
other seat, which twin commanders would sort differently. The commander never
captures a producer (its only capture order is the neutral Outpost's), so AI
mirrors cannot reach this; a row that teaches it to capture one judges this
tie first.

## What it measured (seed 2026, macOS)

Three columns: the base (ef45c9e); the behaviour commit (sections 1 to 5);
and this ADR's head, with the harvester rebuy (section 6).

Every figure was taken on one machine, the owner's Apple M4 (10 cores, 24 GB,
macOS), which parallel agent runs share. Tick counts and every figure a
deterministic tool prints do not depend on the machine; the wall-time figures
do. The implementer's load was not recorded, and the verifier read load
averages of 25 to 50 during its runs.

**What the commander banks and fields (`spendprobe`, the 72 rung mirrors):**

| | base | sections 1 to 5 | head |
|---|---|---|---|
| mean banked, Normal Directorate | 29725 | 10687 | **11533** |
| mean banked, Normal Sodality | 15931 | 8049 | **7653** |
| mean banked, Hard Directorate | 47560 | 16573 | **15771** |
| mean banked, Hard Sodality | 26633 | 9948 | **8908** |
| units fielded by t=9000 per seat (N Dir, N Sod, H Dir, H Sod) | 52, 75, 47, 69 | 61, 97, 59, 105 | **60, 97, 59, 107** |
| units fielded by t=4500 per seat | 22, 33, 22, 33 | 23, 33, 22, 33 | 23, 33, 22, 33 |
| most barracks and factories held | 1 and 1 | 2 and 2 (Normal), 3 and 3 (Hard) | 2 and 2, 3 and 3 |
| harvesters delivered per seat, whole match | 3, 8, 6, 16 | 2, 3, 9, 7 | 3, 2, 11, 10 |
| matches decided of 72 | 45 | 68 | 69 |

At the head, by 5-minute window: Normal Directorate 10422/17765/8699/6745/10358/4312,
Normal Sodality 10593/8382/3616/1535 (no seat's match runs past 20 minutes),
Hard Directorate 11845/27384/12746/2439/23/0, Hard Sodality
11797/14903/3298/982/238/242. The share of seconds at or above 4500 falls from
93, 77, 94 and 86 per cent to 71, 65, 76 and 61; the median peak from 39108,
22476, 71862 and 43252 to 24255, 17778, 35541 and 23548. The second factory
comes at a median t=6900 (Normal Directorate, 34 of 36 seats), 4815 (Normal
Sodality, 36), 6900 (Hard Directorate, 36) and 4815 (Hard Sodality, 36), and
the second barracks at 7598, 4995, 8520 and 4995. The first five minutes
barely move (the first window's mean is within 25 credits of the base's in
every pool, and units fielded by t=4500 within one a seat): the second orders a
beat places early are the only change there, and the window keeps the surplus
buildings out of it. Whole-match harvester totals are flattered or shrunk by
how long a match runs, which is why the probe also reads what each seat
fields by a fixed tick.

**The binding gates** (all green at the head): `pillargate --bind` both halves,
median first launch 11536 at every stage, launching in 64 of 72 matches at the
base and 56 of 72 at both later stages (half is 36), at most 3.5, 3.6 and 3.2
launches per seat per 30 minutes (bar 5); `aiairgate` PASS, every last flyer
down by t=4965; `cheesegate` 10 of 10; `powerdatagate`, `difficultygate`,
`aituninggate` and the rest of `match` green; `golden 2026` gives the
committed lines; `determinism`, `selftest`, the LAN, replay and save modes and
the balance gate's mirror self-check, all through `tools/ci-local.sh`.

**`seatfairgate`** (F5, not binding): income within 15 per cent in 28, 27 and
25 of 32 matches; the split by start **8/10, 14/16 and 14/18**, inside 60/40
at every stage (18 of 32 is 56 per cent).

**`frozenprobe`**: held by an exact cancellation **0** at every stage (sidestepped
0, left on a line 0); walkers in one on 450 or more consecutive ticks 0, in a
cycle 0. Frozen walkers fall from 2 harvesters and 17 units to 2 harvesters and
0 units. The two harvesters at the head are not the base's. At the base they
are P8-75's witness, harvester #59 in skirmish-08's mixed pairing (DS o0 and
SD o1), and that freeze is gone at the head. At the head they are one new
freeze seen from both orientations: harvester #208 in skirmish-01's Sodality
mirror (SS o0 and o1), owned by the seat at start 1, heading to its refinery
at (87, 57) and standing at (85.072629, 49.627714) from t=7500 to its match's
end at t=10861, 3361 ticks. It stands at the north end of a one-cell corridor
between its own turret (centre 84, 51) and its second refinery (centre 87,
51); no seat placed a structure from t=7300 to t=9560, the probe finds a route
from its cell, and it returns to exactly the same position every tick, which
is the signature of P8-75's triangle. It is **not attributed** to this row's
mechanism, and whether this row's denser base closed a wider route is not
measured. Row P8-75 carries it as a candidate second witness for its detector.
`frozenprobe` reproduces it, and the absence of #59, at this branch's head on
main d295931.

**A held yard** (a risk, measured as unreached). A finished surplus building
that cannot be placed holds the yard through the `ready` path, which retries
the placement and queues nothing else, so the ladder could never rebuild while
it holds. The Architect's scratch probe, not committed, counted yards holding
a finished structure unplaced for 450 or more consecutive ticks over
`pillargate`'s 72 matches: 0 at the base and 0 at the head, with a positive
control at a threshold of one tick that sees ordinary one-beat holds of 14
ticks. The risk grows with every structure a base holds.

**`mirrorprobe`**: every first break is the one the base prints, at the same
tick with the same treasuries, on all 16 two-seat cells (from skirmish-02 DD
at t=988 to skirmish-02 SS and skirmish-03 SS at t=2493) and on all six of
`seats=4`'s seat pairs (t=2388, 2388 and 2563 in the Directorate mirror, 2493,
2493 and 2668 in the Sodality one), each printing that no walker in an exact
cancellation is among what differs. The commander's new choices do not break
a mirror first; the first break is still a walker's.

**`endgate` and the F6 window** (P8-24's input, not binding), over the same 72
shipped-setup matches as `pillargate`:

| | base | sections 1 to 5 | head |
|---|---|---|---|
| no result at 27000 | 24 | 8 | **3** |
| ended between 13500 and 27000 (F6 asks 70 per cent) | 32 (44 per cent) | 35 (49 per cent) | **27 (38 per cent)** |
| ended before 13500 | 16 | 29 | **42** |

The head resolves almost every match, and resolves too many of them before
minute 15: the harvester rebuy trades eight matches inside the window for five
more resolutions and thirteen earlier ends. That is a pacing problem for
P8-23 (staging and a growing launch threshold) and P8-24 to judge, and it is
recorded here rather than tuned away, because this row's bars do not include
F6 and its window was chosen on F8, which binds.

**`laddergate`** (F4, P8-26's input, not binding):

| stronger rung, seat | base | sections 1 to 5 | head |
|---|---|---|---|
| Normal over Easy, seat 0 | 76.9 | 93.8 | 91.2 |
| Normal over Easy, seat 1 | 57.1 | 93.9 | 90.6 |
| Hard over Normal, seat 0 | 58.6 | 75.0 | 80.6 |
| Hard over Normal, seat 1 | 56.7 | 73.5 | 73.5 |
| Brutal over Hard, seat 0 | 50.0 | 75.0 | 82.9 |
| Brutal over Hard, seat 1 | 50.0 | 65.7 | **71.4** |

Per cent of decided games; undecided matches 53, 14 and 10 of 216. At the head
every half clears F4's 70 per cent: five failures at the base, one after
sections 1 to 5, none with the rebuy.

**D32's no-raid control** (`aiairgate`, printed, not asserted): harvesters alive
at the window's end in the four cells (Normal Directorate, Normal Sodality,
Hard Directorate, Hard Sodality) read 5, 0, 7 and 0 at the base and 2, 3, 0 and
0 at both later stages. Seat 1 is a Normal commander that now spends too, and
both Hard cells lose the ground war before the window closes. D32's reversal
asks for a harvester in every cell, so it is **not met**, and the gate keeps
counting at the answer tick.

**`longmatchperf`** (P8-31's input, not binding): the proxy moved, so its budget
is re-baselined in this row as its comment requires: p999 cells relaxed per
tick 313148, 54894 and 87023 to **357451, 65757 and 171729** on skirmish-07, 08
and 09. Recorded with it: the wall-time p999 on skirmish-09 rises from 7.324
to about 13 ms a tick (field builds per tick 0.0898 to 0.2234, the four-seat
match ending at t=9736 rather than t=21331), so F12's 8 ms bar now fails there
as well as on skirmish-07 (23.7 to about 25.6). These wall times are the
implementer's, on the machine named above with its load unrecorded. The
verifier, under load averages of 25 to 50, read 40.8, 17.1 and 22.8 ms on
skirmish-07, 08 and 09, so whether F12 also fails on skirmish-08 is
**unsettled**; P8-31 settles it on a calm machine.

**The catalogue checksum** moves `0x1255012DCF3D3A68` to `0x35327CDBF04A326F`
over this row (`0xB1AFE8990A02ABBB` at the commit that authored the numbers with
a two-minute window). Row P8-61 also moves it; the integrator re-measures on
rebase, and no new gate pins an absolute value: `aituninggate` prints the
checksum it computes from both sources and asserts that they agree and that
each number moves it.

## Golden hashes

`golden 2026`, diffed in order against the base's `sim/golden-hashes.txt`.
**Three move and twenty-two are byte-identical.** A traced build that printed
the first beat each new branch of the commander fired (a second order in one
beat, a surplus building, an order held by the beat's own earlier order on
its line, a harvester queued behind an order) prints one before exactly these
three and none before the other twenty-two.

- `skirmish` **0x14812D51448A0082 to 0x53C683530CBFEDCD**, at the behaviour
  commit. First at t=2310: both seats, on the same beat, place a rocket squad
  behind a rifle squad at their barracks, with 7900 credits (7900 less the
  rifle's 200 still leaves the reserve and the rocket). Report line: 37
  entities destroyed and treasuries 14571/12861 at the base, 39 and 15211/12345
  at the head.
- `aisuper` **0x6C0A2027A2575357 to 0x5F3111F1CAD98D45**, at the behaviour
  commit. First at t=1320: seat 0 places a rocket squad behind a rifle squad
  with 14600 credits; at t=4500 its surplus, standing since t=0, buys a second
  Bastion. At the head its report reads the superweapon placed at t=6001 and
  fired at the enemy refinery by t=11521, destroyed.
- `airanswer` **0x23A616103F612EFB to 0x556DE88A61FAAE54**, at the harvester
  commit; the behaviour commit leaves it byte-identical. First at t=465: seat 0,
  with no harvester alive after the raid, two refineries and 3100 credits,
  queues a harvester behind the one order on its factory's line, where the base
  waited for the line to empty. Every assertion ADR-072 gave it holds: first
  sighting at t=194, radar placed at t=466, flak ordered at t=480, first Flak
  Track at t=611, first flyer down at t=667, 3 of 3 down.

`expansion` and `mission` carry a commander and do not move, nor does
`airanswer` at the behaviour commit: in them no beat places a second order,
buys a surplus building or holds an order for one the beat placed earlier.

**The isolating measurement.** The goldens-neutral commit before the behaviour
(2859bf8) carries every number, schema entry, loader rule, checksum fold and
probe, and the commander reads none of the four: its `golden 2026` gives all 25
of the base's hashes. The behaviour commit moves `skirmish` and `aisuper` and
nothing else; the harvester commit then moves `airanswer` and nothing else.

**The new line, `aispend`** (the Architect's condition 3): `aispend 2026
0x6FF9A772D908875A`, appended after `airanswer`, so every line above it is
byte-identical and in order and CI's ordered diff gains one line. Before it no
golden reached a second producer, although every LAN peer runs its AI seats
itself (ADR-033). A Normal Standard commander plays a finished Directorate
base with a purse that keeps its treasury above the reserve through the
window, the rival yard sealed behind a blocked column as `aisuper`'s is, for
the compiled Normal window plus 3000 ticks. Measured: a later army order on
the first beat (t=0), orders routed over both factory lines by t=4815, and
two factories and two barracks held at once from t=6181. The second barracks
comes late because the barracks line is empty as each beat opens, so the yard
first buys the two further Bastions the faction defence cap allows and then
the ladder's superweapon. The scenario asserts all three states and fails by
name when one is not reached: a producer cap of one fails the two-of-each
check, `Route` always taking the first producer fails the routing check, and
one army order a beat fails the later-order check.

## Compatibility

This changes what the commander orders, so it is the class Q024
(docs/questions/Q024-a-sim-rules-epoch-for-replays-and-the-lan-hello.md) asks
about, but unlike ADR-078 it also moves the catalogue checksum.

**Old replays are refused, not diverged.** A replay carries the catalogue
checksum, and the four new numbers move it, so a replay recorded before this
ADR is refused up front. Replays never run a commander (they re-run the bare
command stream), so the command change alone would not have diverged one; the
refusal comes from the data.

**Saves made before this ADR are refused** on the same checksum. No save
field, version or format changes, and nothing of the commander is saved: a
loaded match's commander starts its surplus window again.

**A mixed-build LAN game is refused at the hello**, which compares the
catalogue checksum. Q024's open question is the case this ADR does not reach:
a later change to the commander's logic that moves no data would pass the
hello and desync, and the relay's state-hash comparison would catch it.

## Alternatives rejected

**A two-minute window (1800 ticks), the near edge of the measured gap.** Built,
and measured with the commander reading it: banking falls furthest (mean banked
5336, 3939, 6529 and 4408), but the second factory comes at a median of
t=3180 to t=3765 by pool and the commander ends matches so early that only **20 of `pillargate`'s 72
launch a superweapon**, which fails F8's binding half (D33's reversal), and
`seatfairgate`'s split by start goes to **21/7**, outside 60/40, with 20 of 32
inside 15 per cent on income. At five minutes both hold.

**No window: buy whenever the treasury covers the reserve and the price.** The
opening purse alone stands above 4500 for the first five minutes of most
matches, so this would buy the second factory in the first minute, earlier
than the two-minute window that already fails F8. Not built.

**One cap over all producers together.** A commander short of infantry lines
and long on vehicle lines needs one kind, not one more of either; per kind,
with "every line busy" as the trigger, buys what is the bottleneck.

**Round robin over the producers instead of the shortest line.** It needs a
remembered pointer, which is state a loaded commander would not have, and it
ignores what each line already holds, so a line that a long order holds up
would still be handed the next one.

**A world axis or an entity id to break a tie between lines** (the producer
furthest north, say, or the lowest id). Each is a seat split by construction,
exactly as ADR-078 found for its tie: a world axis picks the same way in world
terms for a commander and its rotated twin, and the two seats' ids differ. The
oldest producer of a kind is the twin of the oldest.

**Later orders with no reserve.** The beat would spend down to zero, and the
superweapon rung's 4500 test, which D33 relies on, could not pass while any
line had room.

**Keeping ADR-060's one faction defence.** The row names further defences, and
a base that keeps one gun however rich it grows is the commander AI-03 and
OJ-04 describe. The cap keeps it bounded by taste.

**The faction defence cap on the rung.** DR-14 keeps the rung for strength
(beat, mining, production) and the personality for shape; a Turtle that wants
five guns is a Turtle at every rung.

**Leaving the harvester rebuy on the empty line.** ADR-072 left it to this row
by name, and with it in place every `laddergate` half clears 70 per cent and
`endgate` falls from 8 unresolved matches to 3. Its price is F6's window share
(35 to 27 of 72), recorded above.

**Different numbers per rung now.** P8-26 owns what makes one rung better than
another beyond D7's caps; this row authors one reserve and one window for all
four and leaves the judgement there.

## What reverses it

- **The row has failed its purpose** if, at a head carrying it, `spendprobe`'s
  pooled mean banked for any of its four rung-and-side pools is not below the
  base's (29725, 15931, 47560 and 26633), or the units fielded by t=9000 per
  seat are not above the base's (52, 75, 47 and 69).
- **The window has cost the climax** if `pillargate --bind` fails either half;
  the launch half is the one the window holds (two minutes failed it). Then the
  window lengthens before anything else changes.
- **It has broken a symmetry** if `seatfairgate`'s split by start leaves 60/40,
  if a `mirrorprobe` first break, two-seat or `seats=4`, moves earlier than the
  base's, or if the balance gate's mirror self-check fails.
- **The harvester rebuy is reverted alone** (commit f0c9b74, its own) if P8-23
  or P8-24 finds that F6's window share cannot reach 70 per cent with it and can
  without it, measured at that row's head.
- **A cross-platform golden mismatch in CI** stops it before merge, as it would
  any golden move.

## Consequences

The commander now builds the production its income can pay for, and the ladder
separates: at the head every rung beats the rung below in more than 70 per cent
of decided games from both seats, which is F4's ladder clause (P8-26 binds it).
Matches resolve: 69 of 72 rung mirrors and 69 of the 72 shipped-setup matches
end with a result. What it leaves, each recorded with its owner:

- **F6's window share is 38 per cent** at the head, 42 of 72 matches ending
  before minute 15. P8-23's staging and growing threshold are the first place
  to look, then P8-24.
- **F12's wall bar now fails on skirmish-09** as well as skirmish-07, from the
  extra field builds of a larger army, and skirmish-08's reading is unsettled
  (P8-31, on a calm machine).
- **D32's reversal is not met**: two Hard cells end the control with no
  harvester (P8-23's waves, P8-26's ladder).
- **P8-26 inherits the producer caps already authored** (D7's numbers, its row's
  "rung producer caps"), so it judges them rather than adding them, and owns
  any per-rung reserve or window.
- **P8-23 inherits a commander that fields more**: its waves, staging and Turtle
  defences start from a base that already holds two or three producers and up to
  its personality's cap of faction defences, so "Turtle walls and extra
  defences" means walls and any defence beyond `faction_defence_cap`.
- The `longmatchperf` proxy budget is a day's figure again (P8-31).
