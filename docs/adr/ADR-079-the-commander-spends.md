# ADR-079: the commander spends: a sustained surplus buys production up to its rung's cap, every order takes the shortest line, and a beat places as many orders as its reserve allows
- Status: **Ratified under D40** of docs/tickets/P8-formidable-tracker.md (row P8-22, decisions D6 and D7), 2026-10-11, by the orchestrator under the owner's standing authority of 2026-10-02, after the Architect's sign-off with conditions of 2026-10-10 (section "Architect sign-off" below), whose conditions 1 to 3 the Architect confirmed met in its addendum of 2026-10-11 (that section's "Addendum: conditions 1 to 3 checked"), in pull request 175, from branch `worktree-wf_ae038544-456-1` (pushed as `claude/p8-22-ai-spends`) into main. Proposed on 2026-10-10. Built on P8-66 (ADR-078, pull request 172, branch `claude/p8-66-tie-break` at ef45c9e, which carries main's merge of #171), because the Architect ruled that P8-66 lands before any commander row; every figure below is measured against that head unless it says it was re-measured on main at d295931, where the branch now stands. It moves goldens, which is a replay-compatibility break needing an ADR and the Architect (CLAUDE.md), and it changes what the commander does, so a replay of an AI match recorded before it diverges (Q024). It moves the catalogue checksum (data/ai). It lands as six commits: three goldens-neutral ones (the probe, the four numbers in data/ai, the window and the probe's fixed-time reading), the behaviour, the harvester rebuy, and the proxy budget re-baselined; this ADR, its queue entry, ADR-060's forward pointer and the tracker row follow in one docs commit; then one commit for each of the Architect's conditions 1 to 5 and one for condition 7's re-measurement on main, and condition 6 is this status and its records. Drafted by the implementer agent.
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
dearest in the ladder. No surplus purchase is ordered on credits the reserve
keeps, but a beat can still end below the reserve (section 2). The reserve
equals that test by transcription, not by reference: the rung reads a
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
on the tracker's authority and records it there (D40). The first is bought exactly
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
registration that refuses what the loader refuses, a rung producer cap below
1, a negative `surplus_reserve` or `surplus_window_ticks` on a rung and a
negative `faction_defence_cap` on a personality, the last three in the
loader's words while 0 still registers (the Architect's condition 1,
`aituninggate` stage 4, each refusal beside its control at 0); and a place in
`World.CatalogueChecksum`, which moves on one unit of each (`aituninggate`
stage 3), because two LAN peers holding different files would build different
bases from the same bank (ADR-032 clause 2). The surplus building's reserve
test compares in `long`, as the treasury is held, `(long)_surplusReserve +
extraCost`, so a reserve near the top of `int` plus a building's price cannot
wrap negative and buy while ignoring the reserve (also condition 1).
`aituninggate` holds it in stage 2e beside a control: a reserve of
`int.MaxValue` keeps a treasury half a Bastion above it off the Bastion, and a
reserve of `int.MaxValue` less the Bastion's price buys one. In the Architect's
own build, dropping the cast fails the gate on "a reserve of int.MaxValue must
keep a treasury of 2147484347 off the Bastion". **`aituninggate` stage 2d proves
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

**Re-measured on main at d295931** (the Architect's condition 7), which
carries P8-61's charge and jam in /data and so moves the checksum too. Main
reads `0x0DB4F466E14C9D42`; this branch's head reads `0x652D8C7E6D1C847D`,
from `aituninggate` (both sources agree) and from `catalogueloadgate` (the
single call, the per-kind sequence and the compiled table agree). Both rows'
folds are kept: P8-61's columns and this row's four numbers each still move
it. `powerdatagate` and `aituninggate` pass at that head. `golden 2026` at
that head gives main's line for each of the 22 goldens this row does not
move, reproduces the three moves below exactly, and adds the `aispend` line.

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
  without it, measured at that row's head. In behaviour, reverting it changes
  one term, the harvester's guard in `SkirmishAI.cs`: the commander orders a
  harvester on the shortest factory line while that line has room
  (`shortestDepth < 2`), and the reversal returns it to an empty line
  (`shortestDepth == 0`), the guard the MCV beside it keeps. A squash merge
  leaves neither f0c9b74 nor its cherry-pick 8e2308d reachable from main, so
  the clause is carried out by that change, whatever the merge style.
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

## Architect sign-off

- Date: 2026-10-09, as the review brief dates it. The seven commits reviewed are dated 2026-10-09 (7a7cc01, 9c2594e) and 2026-10-10 (the other five), and the status line says Proposed on 2026-10-10, so the orchestrator confirms this date when it ratifies.
  - Corrected at ratification (D40): **2026-10-10**. The last commit reviewed, be8b1a8, was committed at 22:12 on 2026-10-10, and this sign-off was in the review's journal by 23:04 that evening, so it was given on 2026-10-10 and not on the brief's 2026-10-09.
- Reviewer: Systems Architect (A3).
- Pull request: the P8-22 pull request from branch `worktree-wf_70980100-66f-1` into main. It is not yet pushed or opened, and its number is recorded here when it opens. I reviewed its seven commits, 7a7cc01 to be8b1a8, on ef45c9e. That is the head of `claude/p8-66-tie-break` (pull request 172), which carries main's merge of pull request 171. A determinism audit and an adversarial verifier each reviewed it independently.
  - Recorded at ratification (D40). Pull request 172 merged first, as condition 7 asks, and the seven commits reviewed were cherry-picked unchanged onto main at 4568553 and then, with that pass's commits for conditions 1 and 2, onto main at d295931 (P8-78, P8-61 and P8-70, pull request 173), without conflict, on branch `worktree-wf_ae038544-456-1`. The reviewed hashes this ADR names are, on that branch: 7a7cc01 is ff1849a, 9c2594e is 456ef22, 2859bf8 is b019e0b, 57853cd is 77f4cc8, f0c9b74 is 8e2308d, f4c0e82 is 21ead37 and be8b1a8 is b30fa71. Conditions 1 to 5 are met in it by one commit each (92b40f9, 01b49dc, 170d988, ef149c0 and d848054), condition 7's re-measurement on main by 571ae17, and condition 6 by the ratifying commit. The pull request is the one from that branch into main, pushed as `claude/p8-22-ai-spends`: pull request 175. Conditions 1 to 3 come back to the Architect for a check.
- Verdict: **signed off with conditions.** The design is approved:
  - the sustained surplus;
  - the four numbers and their split between rung and personality;
  - routing to the shortest line, ties to the oldest producer;
  - several orders a beat above the reserve;
  - further faction defences from surplus, and with them the amendment to ADR-060;
  - the harvester rebuy as a commit that can be reverted on its own.

  The three golden moves and the catalogue checksum move are approved as attributed. The pull request merges once conditions 1 to 7 below are met in it.

**Reproduced, on macOS only.** Most of the figures below come from the audit's and the verifier's builds, each in a tree of its own. The three marked as mine I measured myself, in trees exported from ef45c9e and be8b1a8.

- **Goldens.** `golden 2026` gives the committed lines. `skirmish`, `aisuper` and `airanswer` move, and the other 22 are byte-identical.
  - 2859bf8 gives the base's hashes. It carries the data, schema, loader, checksum fold and probe, but the commander reads none of the four numbers.
  - 57853cd moves only `skirmish` and `aisuper`.
  - Reverting f0c9b74 alone at the head gives 57853cd's hashes, so the rebuy reverts on its own.
- **CI.** `tools/ci-local.sh` is green at the head on all 20 steps. It ends with `pillargate --bind`, which passes both halves: median first launch 11536, 56 of 72 matches launching, at most 3.2 launches a seat per 30 minutes. `aiairgate`, `cheesegate`, `powerdatagate`, `difficultygate` and `aituninggate` pass in `match`, and the balance gate passes.
- **The data wins.**
  - `aituninggate` stage 2d fails on its own check for each key when the commander reads that key from the compiled reference.
  - Stage 3 fails when the `FactionDefenceCap` fold is removed.
  - Raising `producer_cap` to 3 in `data/ai/ai_normal.yaml` gives three barracks and three factories at Normal in `spendprobe`.
- **Measurements and mirrors.** `spendprobe`, `endgate`, `laddergate`, `seatfairgate`, `frozenprobe`, `mirrorprobe` (two-seat and `seats=4`) and the D32 control reproduce every figure in "What it measured". The verifier also built a half-turn pair with two barracks a seat. It was a mirror in 12 cases of 12, including one with seat 1's barracks in reversed spatial order, so the tie follows build order, not position.
- **Measured by me: the balance tool.** It exits 0 with a PASS verdict at the base and at the head. Its mirror self-check is unchanged (28 annihilations, 12 cloaked runs identical). Only section 6, the faction war, and the checksum line differ:

  | | Directorate | Sodality | Undecided | Seat 0 | Seat 1 |
  |---|---|---|---|---|---|
  | base | 3 | 1 | 2 | 3 | 1 |
  | head | 4 | 2 | 0 | 5 | 1 |

  Figures are cells of 6. The credits banked at the end of the base's undecided or late cells (up to 49150) are gone at the head (at most 10087).
- **Measured by me: held yards.** A scratch probe, not committed, counts yards that hold a finished structure unplaced for 450 or more consecutive ticks over `pillargate`'s 72 matches. It reads 0 at the base and 0 at the head. Its positive control, at a threshold of 1 tick, sees ordinary one-beat holds of 14 ticks.
- **Measured by me: frozen harvesters.** At the base, `frozenprobe`'s two frozen harvesters are P8-75's witness, harvester #59 in skirmish-08's mixed pairing (DS o0 and SD o1). The verifier wrote skirmish-09, which was a slip. At the head that freeze is gone and a new one appears, traced below.

Windows and Linux are left to CI.

**The break is justified.** D6 names this row and D7 sets its caps. The bank the row was written to spend is measured, and it is spent:

| pool | mean banked, base | head | units by t=9000, base | head |
|---|---|---|---|---|
| Normal Directorate | 29725 | 11533 | 52 | 60 |
| Normal Sodality | 15931 | 7653 | 75 | 97 |
| Hard Directorate | 47560 | 15771 | 47 | 59 |
| Hard Sodality | 26633 | 8908 | 69 | 107 |

The commander now holds two producers of each kind at Normal and three at Hard. The ladder separates as a consequence rather than by tuning: `laddergate`'s failing halves fall from 5 to 0. Matches resolve: `endgate`'s unresolved matches fall from 24 to 3 of 72. The binding gates hold at every stage.

The checksum move is that mechanism doing its job: old replays, saves and mixed-build LAN games are refused, not desynced. The window was measured into shape, and the alternative at the near edge of the gap was built and failed two named bars. That is the method I want for every number in this phase.

**The design holds.**
- **Determinism.** Everything new in `/sim` is integer arithmetic over the commander's own counts, queue lengths and treasury. `pending` is looked up and never enumerated. Nothing reads a position, a world axis or an id compared across seats. `_surplusFrom` is AI state, like `_produced` and `_airSeen`: it is never hashed and never saved. That is safe across peers, because LAN commanders attach only at tick 0 and replays carry the commands.
- **Mirror exactness.** The tie to the oldest producer is the right rule. Entities are only appended, so a commander's producers in entity order are its own build order. That order is the twin of its twin's under the half turn and both centre-line reflections. ADR-078's argument against breaking a tie by world axis or by id applies unchanged, and this ADR cites it.
- **Data.** The rung and personality split follows DR-14. Each family has one key table, so each key is refused in the other family. All four keys are folded into `CatalogueChecksum` inside the sorted-id loop. ADR-032 clause 2 and ADR-033 require that now that every peer runs its AI seats locally.
- **Isolation.** The window commit, the behaviour and the rebuy are separate commits, and each moves what it should and nothing else. The rebuy is the decision I would most expect a later row to reverse, so I approve keeping it in its own commit with its own reversal clause.
- **ADR-060.** Its refusal is overturned on row P8-22's authority rather than on its own playtest clause. The owner's standing instruction permits that, with the reason recorded. ADR-060's measured harm was a faction war flipped by an ungated rung. Here the extra defences are gated on the window and the reserve, and my balance runs show the war is not flipped: 4 to 2 at the head against 3 to 1 at the base. That reading has to be in the record (condition 4).

**What is wrong.**

- **The new routing is outside the cross-platform gate.** No golden reaches `Route` over two producers, or a surplus factory or barracks:
  - `skirmish` stops at 5000 ticks;
  - the LAN modes end before the window;
  - `aisuper` reaches the surplus branch only for its second Bastion.

  Every LAN peer runs its AI seats locally (ADR-033), so this code runs on both platforms in any long LAN game with an AI seat. The standing rule is that the golden gate proves such code agrees across platforms. Without coverage, a later row that changes this code (P8-23, P8-26, P8-27) can call itself goldens neutral and be right only by accident.
- **Four live conditions are guarded by nothing.** The verifier removed each of these in a scratch build:
  - the surplus branch's `harvesters > 0`;
  - the surplus building's own reserve test;
  - `committed += extraCost`;
  - `committed` in the later-order test.

  Each removal left all goldens identical and `match` green, and each changed `spendprobe`'s output. The ADR presents each as a decision, but stage 2d bites only the four key reads.
- **A latent wrap, and a guard weaker than the loader's.**
  - `_surplusReserve + extraCost` is computed in `int`. The sustained test keeps the wrap out of reach, because the treasury would have to pass 2^31 less the building's price.
  - `World.RegisterAiTuning` refuses a producer cap below 1, but it accepts a negative reserve, window or faction defence cap. The loader refuses all three, so a def registered directly can pass what a file cannot.
- **The record is wrong or silent in several places.**
  - Section 2 says the surplus building's price stops the army promising the same credits again. That holds only for orders after the beat's first. The first army order stands on its own price, so a beat that buys a surplus building can end up to one unit's price below the reserve.
  - Section 2 also omits that the building's own test ignores the engineer order placed earlier in the beat.
  - While the anti-air answer is short, every line of its kind now holds its army orders, not only the line the answer takes. That is a behaviour change, and the ADR does not state it.
  - The frozen harvesters are counted correctly but identified wrongly: the head's two are a new freeze, not the base's.
  - The faction war that ADR-060 rested on is not reported.
  - The wall-time claims rest on one loaded machine. The verifier read skirmish-08 at 17.1 ms under load averages of 25 to 50, so whether F12 also fails there is unsettled.
- **The new freeze.** Harvester #208 is in skirmish-01's Sodality mirror, owned by the seat at start 1 in both orientations.
  - It is heading to its refinery at (87, 57) and stands at (85.072629, 49.627714) from t=7500 to the end of its match at t=10861.
  - It sits at the north end of a one-cell corridor between its own turret (centre 84, 51) and its second refinery (centre 87, 51).
  - No structure was placed by any seat from t=7300 to t=9560, and the probe finds a route from its cell.
  - It returns to exactly the same position each tick, which is the signature of P8-75's triangle.

  It is not attributed to this row's mechanism. Whether this row's denser base closed a wider route is not measured.
- **Records lag.**
  - ADR-072 still names P8-22 as the owner of the starving rebuy that f0c9b74 delivers.
  - ADR-073 still says D32 waits on P8-22.
  - D32's reversal falls due when this row lands, and this row reads it as not met, but D32's entry does not say so. Neither do D33's or D7's entries.
  - The pending cells of P8-23, P8-24 and P8-26 do not say what they now inherit.

**Conditions before merge.**

1. **Close the wrap and match the registration guard to the loader (goldens neutral).**
   - The surplus branch compares in `long`: `(long)_surplusReserve + extraCost`.
   - `World.RegisterAiTuning` refuses a rung whose `SurplusReserve` or `SurplusWindowTicks` is negative, and a personality whose `FactionDefenceCap` is negative, with messages in the loader's words.
   - `aituninggate` shows each refusal beside its existing registration checks.
   - `golden 2026` stays byte-identical to be8b1a8's.
2. **Bite the four unguarded conditions (goldens neutral).** Add a stage 2e to `aituninggate` on stage 2d's fixture, with one check per condition, each failing by name when its condition is removed:
   - a finished base with no harvester alive and a sustained surplus orders no surplus building;
   - a treasury above the reserve by less than a factory's price orders no factory;
   - on a beat that buys a surplus building, no army order after the first is placed once what is left would breach the reserve;
   - with credits equal to the reserve plus the second unit's price, the second order is refused, because the first order's price is counted.

   ADR-079 records each bite as the verifier ran it. If a check cannot be built through the public surface, the ADR names that condition as unguarded and says why.
3. **Put the new routing under the cross-platform gate.** Append one golden scenario after `airanswer`, so the existing 25 lines stay byte-identical and in order.
   - It plays commanders until one seat holds two factories and two barracks, has routed orders over both lines of one kind, and has placed a later order on a beat. `spendprobe`'s Normal Sodality pool reaches its second barracks by a median t=4995.
   - The cheapest map, pairing and horizon that reach this state are the implementer's choice.
   - The scenario asserts that it reached the state, so a later row that stops reaching it fails rather than silently losing the coverage.
   - ADR-079's "Golden hashes" and the tracker cell record the new line without writing a line count into prose.
   - Any existing golden this moves comes back to me.
4. **Correct the record** in ADR-079, and wherever the `SkirmishAI.cs` comments, the schema descriptions or the tracker cell repeat it.
   - **Committed credits.** The surplus building's price binds only orders after the beat's first. The first army order stands on its own price, so a beat can end up to one unit's price below the reserve. The building's own test ignores the engineer order placed earlier in the beat, and the harvester, MCV and anti-air guards test only their own price.
   - **The anti-air hold.** While the answer is short, every line of its kind holds its army orders. Give the reason: under unanswered air, a second factory's tank is fed to the flyers just as the first's would be.
   - **Section 8.** "The oldest" is the first in entity order only while producers are appended and never change hands. A captured producer keeps the other seat's index, which twin commanders would sort differently. The commander never captures a producer, so AI mirrors cannot reach this; a row that teaches it to capture judges this first.
   - **`LineFor`.** A cycle unit produced anywhere but a barracks or a factory gets no line. The army loop then ends every beat without advancing the cycle, and the army stops. Shipped cycles hold no such unit, and D10 keeps the commander out of the air in P8. The row that adds one (P8-28, or a flying commander after D10) extends `LineFor` in the same change.
   - **Transcribed numbers.** The reserve equals the superweapon rung's 4500 by transcription, not by reference, because that rung reads a literal in `SkirmishAI.cs`. The faction defence caps equal half the wave with a floor of two, also by transcription. A row that moves either source re-reads its copy in `data/ai`.
   - **`frozenprobe`.** The head's two frozen harvesters are not the base's. Name harvester #208 as above, unattributed. Note that P8-75's witness, skirmish-08's #59, is gone at the head. Add #208 to row P8-75's cell as a candidate second witness for its detector.
   - **The faction war.** Section 5 gains the balance tool's faction war at the base and the head, as tabled above. The seat split is read as chaotic at six cells, and `seatfairgate` stays the measure of seat fairness.
   - **Wall time.** The wall-time figures name their machine. skirmish-08's F12 reading is unsettled, and P8-31 settles it on a calm machine.
   - **The held yard.** "What it measured" gains this risk. A finished surplus building that cannot be placed holds the yard through the `ready` path, so the ladder could never rebuild. The Architect's scratch probe read 0 holds of 450 or more ticks at the base and at the head over `pillargate`'s sweep, with a positive control. The risk grows with every structure a base holds.
5. **Records and seams (docs only).**
   - ADR-072 gains forward pointers at "Fixing the harvester rebuy here" and at "The harvester rebuy starves", naming ADR-079 section 6 and f0c9b74.
   - ADR-073's "It waits on P8-22 in any case" gains a pointer: read at P8-22's head, not met (2, 3, 0 and 0).
   - D32 records its reversal as read at this head and not met (2, 3, 0 and 0), so the gate keeps counting at the answer tick.
   - D33's and D7's reversals are recorded as read and not met: median first launch 11536 against D33's 14400, 56 of 72 launching, and no `laddergate` inversion.
   - The pending cells of the downstream rows say what they inherit, keeping the history already there:
     - **P8-23** inherits two or three producers, several orders a beat, and `faction_defence_cap`, which is a personality number it may retune. f0c9b74's reversal clause names it.
     - **P8-24** gets the position at this head: 3 of 72 unresolved, 27 of 72 in the F6 window and 42 before minute 15, plus f0c9b74's reversal clause.
     - **P8-26** inherits producer caps already authored at D7's values, so it judges and binds them rather than adding them. The reserve and window are rung keys it owns per rung. Easy reaches further faction defences from surplus because that cap is a personality key; Easy's reserve or window can switch it off. Its goldens-neutral expectation is re-measured against condition 3's golden.
     - **P8-31** records that F12's wall bar now fails on skirmish-09 too, and that the proxy was re-baselined again.
6. **Status and ratification.**
   - The orchestrator ratifies ADR-079 under a new decision of the P8 tracker. That decision also records that ADR-060's refusal of several faction defences is overturned on row P8-22's authority, citing the faction-war reading from condition 4 as the evidence that ADR-060's measured harm does not recur.
   - ADR-079's status, its open-queue entry, row P8-22 and ADR-060's amendment banner stop calling it Proposed, and they cite this section and the pull request's number.
   - This section's date is confirmed or corrected in the same commit.
7. **Integration, CI and hygiene.**
   - Pull request 172 merges first. My D38 carry stands until it does.
   - This branch is then brought onto main. If the result differs from ef45c9e under `sim/`, `data/` or the goldens, re-measure: the 22 unmoved goldens must equal main's, the three moved values must reproduce, and condition 3's line is recorded. Any other difference comes back to me.
   - P8-61 also moves the catalogue checksum. Whichever of the two merges second re-measures the checksum and records it; no gate pins an absolute value.
   - All three CI jobs are green on the pull request. The determinism job reproduces the three moved hashes and condition 3's new one on Windows and Linux, and `pillargate --bind` passes in the ubuntu leg. `tools/ci-local.sh` is run again at the final head.
   - Nothing under `.claude/agent-memory` is committed, and no attribution trailer is added.
   - Conditions 1 to 3 change code, so they come back to me for a check, as ADR-078's did. Any commit beyond these conditions, or any golden other than those named, also comes back to me.

**Carried.**
- **The window is not saved.** `_surplusFrom` restarts on a single-player resume, so a player who saves and reloads more often than every five minutes holds the commander off its surplus buildings. This is the same class as `_airSeen` and `_produced`. If commander state ever enters the save, `_surplusFrom` joins it. The playtest notes whether anyone finds this.
- **Seat fairness, not determinism, and non-binding.**
  - `seatfairgate`'s income clause falls from 28 to 25 of 32.
  - `mirrorprobe seats=4`'s widest income gap at t=9000 grows from 14.5 to 16.0 per cent (DD) and from 4.6 to 9.9 per cent (SS).
  - The balance tool's seat split is 5 to 1 over six cells.

  Each is chaotic at its sample size, and all go to F5's owner and Balance. F11 (Directorate 4 of 6 cells, Sodality 2 of 6) goes to P8-33.
- **F6 is now a pacing problem.** Matches end early rather than not at all. P8-23 looks first, then P8-24. f0c9b74's reversal clause is the measured lever.
- **`longmatchperf`.** My ADR-078 carry on P8-31 stands: the budget is set from a stated reason, not re-baselined to each row's reading, and this row re-baselines it again.
- **Credit thresholds in code.** The commander's own credit thresholds (1500, 3500 and the superweapon's 4500) are still literals in `SkirmishAI.cs`. P8-26, which owns rung shape, moves them to `data/ai` beside the reserve, or records why not.
- **Q024** is settled before the first published build.

### Addendum: conditions 1 to 3 checked

- Date: 2026-10-11. This adds to the sign-off above, which is dated 2026-10-10. I confirm that date correction.
- Reviewer: Systems Architect (A3).
- Pull request: pull request 175, the P8-22 pull request from branch `worktree-wf_ae038544-456-1` into main, on main after pull requests 172 and 173. This check applies to that pull request's head, not to any commit it carries.

**Conditions 1 to 3 are met as I worded them.** Condition 1: the surplus building's reserve test compares in long. Registration refuses a negative `surplus_reserve` or `surplus_window_ticks` on a rung, and a negative `faction_defence_cap` on a personality, in the loader's words, while 0 registers. In my own build, dropping the long cast fails `aituninggate` by name. Apart from the seven commits I reviewed, the pull request changes behaviour only by that cast and those three refusals. Condition 2: stage 2e puts one check and one control on each of the four conditions, through the public surface. Each removal fails its own named check in the gate's order, and the gate's comments disclose that two of them also trip a later check. Condition 3: the scenario reaches and asserts all three states, a later order at t=0, both factory lines routed by t=4815, and two factories and two barracks from t=6181, against a horizon of t=7500.

**I approve the new golden line `aispend 2026 0x6FF9A772D908875A`**, appended after `airanswer`. In my build, `golden 2026` matches the committed file in an ordered diff. Against main, only `skirmish`, `aisuper` and `airanswer` move, to the values I approved, and `aispend` is added last. It earns its place: with `Route` made to take the first producer, every other golden stays identical and only `aispend` fails, by name. No existing golden moved.

**Before merge.** First, what remains of condition 7: the number above, and all three CI jobs green with the three moved hashes and `aispend` reproduced on Windows and Linux. Second, one new condition:

8. **The record, finished (docs and comments only, goldens neutral; it does not come back to me).**
   - "Nothing bought out of surplus can starve a rung" still stands in section 1, in the `surplus_reserve` schema description, in the army loop's comment in `SkirmishAI.cs`, in `AiTuning.cs` and in `data/ai/ai_easy.yaml`, beside the correction that says a beat can end below the reserve. Each should say what holds instead: no surplus purchase is ordered on credits the reserve keeps.
   - Section 7 gains condition 1's three refusals and the long comparison.
   - "What reverses it" also names the rebuy by what it changes: the harvester's guard in `SkirmishAI.cs`, from a factory line with room (`shortestDepth < 2`) back to an empty one (`== 0`). A squash merge leaves the reviewed commit unreachable from main.

   The proof for this commit is `golden 2026` byte-identical, `schemagate` green and the catalogue checksum unchanged.
