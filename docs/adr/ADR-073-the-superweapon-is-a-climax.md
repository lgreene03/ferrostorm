# ADR-073: the superweapon is a climax: a six-minute charge from /data, a one-strike cannon, a ten-second scan, and support powers cut loose from it
- Status: Ratified under decisions D1, D2, D15 and D33 of docs/tickets/P8-formidable-tracker.md, taken under the owner's standing authority of 2026-10-02, for the orchestrator to integrate. D1 as written did not meet F8's first-launch bar (median 8701 against 10800); D33, taken on this ADR's measured recommendation, closes it.
- Date: 2026-10-02
- Deciders: orchestrator (owner's standing authority, D1, D2, D15, D33) + implementer agent
- GDD/TDD feature served: GDD s8 line 70 ("one superweapon per faction, ~6 minute charge"); GDD s8's "3-4 minor support powers per faction on shorter timers"; GDD s3 line 25 (the orbital scan and the precision strike); P8-18; findings ML-05, OJ-02, BAL-07 (part), OJ-06 (scan); criterion F8. Supersedes ADR-044's refusal section and clause 1's 900.

## Context

ML-05 and OJ-02 measured the superweapon as an opening, not a climax, and
`pillarprobe` (P8-13) reproduces it on the main this row started from
(387dc39). In all 72 shipped-setup matches (9 maps, 4 faction pairings, both
start orientations, Normal against Normal):

- the Directorate placed its orbital cannon at t=2911 and the Sodality its
  seismic charge at t=3016, every time;
- the median first launch was **t=4546** (ML-05 recorded the first kill at
  4621, before first contact at 4635);
- a seat launched a median **12.2** and at most **15.0** times per 30 minutes,
  and the busiest match saw 29 launches.

GDD s8 says "~6 minute charge", which is 5400 ticks. The sim charged in 1500.
ADR-044 recorded that 3.6-fold gap and REFUSED to close it as an A11 balance
call awaiting a co-sign. D1 is that co-sign, exercised under the owner's
standing authority.

Two things stood in the way of simply changing the number.

1. **The support powers were derived from it.** ADR-062 made a power's charge
   a third of the superweapon's (500), ADR-065 made the jam a third of that
   (166), and ADR-064 made the precision strike a third of the orbital
   cannon's damage (300). Moving the superweapon would have moved all three:
   an 1800-tick power and, after D2, an 833-damage strike that is more than
   twice the seismic charge.
2. **The numbers lived in code.** The charge was a sim constant, a
   compile-time default of `SpawnSuperweapon`, so no file could change it and
   two LAN peers agreed on it only by construction.

## Decision

### 1. The support powers are decoupled first, at today's values (step 1)

`SupportPowerChargeTicks` is an absolute **500**, `RadarJamTicks` an absolute
**166** and `PrecisionStrikeDamage` an absolute **300**: each the value its
derivation already gave. The divisor constant is gone. "Shorter timers" is no
longer true by construction; `supportpowergate` stage 1 already measures it
by charging a superweapon and a power side by side in a running world, and is
now the only thing holding it. The precision strike is decoupled here rather
than with D2 because it is derived in exactly the same way, so it belongs to
the neutral step.

The support-power charge and the jam stay COMPILED absolutes. D1 decided only
that they stop moving with the superweapon; moving them into /data is a
separate change no decision asks for, and `game/scripts/VerifyRunner.cs`
reads `World.SupportPowerChargeTicks` as a static.

### 2. Three columns join the structure def, read by the runtime (step 2)

| key | on | read where | compiled reference |
|---|---|---|---|
| `charge_ticks` | both superweapons | `SpawnSuperweapon`'s default (now null, meaning the def's) and the recharge at impact | `SuperweaponChargeTicks` |
| `strike_damage` | the orbital cannon; the Bastion | the cannon's blast; the precision strike | `OrbitalCannonDamage`; `PrecisionStrikeDamage` |
| `reveal_ticks` | the Bastion | the orbital scan | `OrbitalScanRevealTicks` |

Each has a schema entry in `data/schema.structure.json`, a compiled reference
in `World.DefaultStructureType` that the file must reproduce (the selftest
round-trip compares them through `StructureTypeDef.Equals`), and a fold into
`World.CatalogueChecksum` beside the other decision-carrying columns, on
ADR-032's rule: a number that moves from "agreed by construction" to "agreed
only if checked" must be checked. Two peers holding different charges would
see one machine's `LaunchSuper` accepted and the other's refused from the
same command stream.

**The loader demands each key exactly where the sim reads it and refuses it
everywhere else.** A superweapon with no `charge_ticks` would be ready the
tick it landed; a cannon with no `strike_damage` would land a harmless flash;
and a value authored where nothing reads it is this project's most-repeated
defect. The seismic charge carries `charge_ticks` but NOT `strike_damage`: its
350 stays the compiled `SeismicDamage`, because its impact is P8-19's to
rework and that row should not inherit a half-moved number.

`ApplyPrecisionStrike` takes its damage as a parameter. `ApplyAreaDamage` is
not widened: it already took one, and the mine still passes its own.

### 3. D1: the charge is six minutes (step 3)

`charge_ticks` goes from 1500 to **5400** on both superweapons (+260 per
cent). `factionsuperweapongate` stage 1 now holds the pair to one charge,
since the charge is a column on each def rather than one shared constant.

### 4. D1: the commander buys the weapon from two refineries and a wave of army (step 4)

The superweapon rung in `SkirmishAI` also waits for `refineryCount >=
RefineriesPerBase` (GDD s4's two) and `army >= the personality's wave size`.
The prerequisite stays the radar and the 4500-credit threshold is unchanged.

### 4b. D33: and not before one full charge has elapsed (step 7)

Measured after step 4, D1's gate alone left the median first launch at 8701,
because every commander meets it by about t=3170 (see "The purchase gate,
measured three ways" below). D33, taken on this ADR's recommendation under
the owner's standing authority, adds `w.Tick >= the weapon's own
charge_ticks` to the rung. The floor is derived from the charge, so the first
launch is at least twice the charge plus the build by construction. It times
the COMMANDER's purchase only: a human may build the weapon whenever the
radar stands and is bounded by the charge alone.

### 5. D2: the orbital cannon kills a refinery in one strike (step 5)

`strike_damage` on the orbital cannon goes from 900 to **2500** (+178 per
cent). At ground zero that is 2000 Omni against a structure, exactly a
refinery's hit points; the outer ring deals 1000. The precision strike stays
at the 300 that step 1 pinned.

### 6. D15: the orbital scan lasts ten seconds (step 6)

`reveal_ticks` on the Bastion goes from 75 to **150** (+100 per cent), which
cuts its derivation from the superweapon's 75-tick warning. Five seconds was
a flicker rather than a scan (OJ-06).

### 7. `aisuper` keeps its purpose under the new charge

Its window was first **3000 ticks plus the def's own charge** (step 3),
exactly the 4500 it ran at 1500, and under D33 became **3000 plus twice the
charge** (13800, step 7a), because the commander now places the weapon about
one charge in and fires it about one charge later. Read off the def either
way, so it exercises a commander deciding, building, waiting out the charge
and firing whatever the charge becomes. It also asserts that the FIRST launch
is aimed at the standing enemy refinery, because a longer window gives the
commander's waves time to reach that refinery, and a refinery a wave had
battered would otherwise pass the damage check with no strike at it.

Under D33 the waves did more than batter it. MEASURED with the floor and no
other change: the commander spent its first 4000 credits on army instead of
the weapon, its waves razed the defenceless enemy refinery at t=2940 and its
yard before t=4000, and the match was won long before the weapon charged
(at about t=11535), so it never fired. The scenario's enemy base is
therefore SEALED from the ground by a blocked column at x=70, so only the
superweapon can reach it. That column alone is goldens-neutral under the
old rung (measured: `aisuper` byte-identical, because there the 4000-credit
purchase at t=2446 keeps the army below wave strength and no wave ever
crosses x=70).

`superweapon`'s recharge check becomes an equality with the def's charge, and
its damage checks are derived from the def through the live matrix.
`aifactiongate`'s commanders now run for at least one charge plus twice the
build (6600 at 5400) and never less than the old 6000: at a flat 6000 the
floor plus the 600-tick build left the Directorate commander without its
cannon at the end, which that gate reads as a regression.

### 8. The gates

**`powerdatagate`** (new, in `match`): the three columns reproduce the
compiled reference on all 22 buildings; with values no file carries
(charge 321, blast 1000, strike 77, reveal 20) REGISTERED, the sim builds,
recharges, strikes and reveals by them, each beside a stock control; each
column moves the checksum; and the loader refuses seven malformed files (a
superweapon with no charge, a cannon or a Bastion with no strike, a Bastion
with no reveal, a turret with a charge, a seismic charge with a strike, a
Watch Post with a reveal) while accepting the shipped three. Since the audit
and the Architect's review it also proves registration's refusals and the
ceilings (stage 5), that every superweapon's registered charge keeps F8
reachable under D33 (stage 6), and that on the /data catalogue the orbital
cannon's strike, through the /data matrix's Omni row against Structure
armour, reaches com_refinery's hit points (stage 7: 2000 against 2000, with
a control showing strike_damage 2499 deals 1999 and fails). Stage 7 is D2's
reason held in CI.

**`pillargate`** (new, on demand): F8 over `pillarprobe`'s sweep, through the
same function (`PillarF8`), which `pillarprobe` now calls too, so the gate's
figure is the probe's by construction. Two halves with their own switches in
`MeasurementHarness`:

- **rate** (at most 5 launches per seat per 30 minutes): **binding** from
  D1. At a 5400-tick charge it holds by construction, and the gate keeps it
  held.
- **first launch**: **binding** from D33. It fails when the median first
  launch is before 10800 (F8), after 14400 (minute 16, the top of D1's and
  D33's band), or when fewer than half the sweep's matches launch at all
  (D33's reversal), so a charge long enough to make the weapon absent cannot
  pass as a climax. The last two clauses are the Architect's condition C4. It
  was registered non-binding between steps 4 and 7, printing WOULD-FAIL at
  8701.

A sweep with no launch fails both halves (P8-13's rule: a stage that measured
nothing fails), so the binding half cannot pass on silence. Not in `match`:
the full sweep is 72 whole matches, 41 to 75 s on ten threads here, minutes
on a CI runner. `maps=`, `pairs=`, `orient=` and `jobs=` run a subset, and
`--bind` makes both halves binding.

## The A11 record

Three changes in this ADR move a gameplay number by more than 15 per cent:
`charge_ticks` 1500 to 5400 (+260, D1), the orbital cannon's `strike_damage`
900 to 2500 (+178, D2) and the scan's `reveal_ticks` 75 to 150 (+100, D15).
CLAUDE.md requires a Balance and Game Designer co-sign for each (charter
A11). **That co-sign is EXERCISED under the owner's standing authority of
2026-10-02**, as decisions D1, D2 and D15 of the P8 tracker record, and this
ADR repeats the record. The support-power charge (500), the jam (166) and the
precision strike (300) do not change value.

## Alternatives rejected

**Change the charge and leave the powers derived.** Rejected by D1 itself: a
power charging in 1800 ticks, two minutes, is not a minor power on a shorter
timer, and an 833-damage strike would out-hit the seismic charge, a
superweapon.

**Put all six numbers in /data, the support-power charge and the jam
included.** That is where they will probably end up, but no decision moves
them, and doing it here would widen the row and the catalogue change for no
behavioural reason. Recorded as tracker row P8-61 (the Architect's carried
condition C6) rather than done.

**A purchase floor enforced in the SIM for every seat** (a minimum match tick
before a superweapon may be queued or launched, or a heavier prerequisite).
It would bind a human exactly as it binds the commander. Rejected for four
reasons. GDD s8's only timing rule for the weapon is its charge, behind the
radar prerequisite, so a timed unlock is a new rule for every player where
D33 is commander doctrine. A sim floor moves what every command does: it
would refuse the `superweapon` golden's 90-tick launch and the early launches
in `factionsuperweapongate`, `seismicaimgate`, `airgate` and the client
harness's LaunchSuper stage, each of which would need re-timing and a
golden or hash move, where D33 moved only the two commander goldens. The
human's head start is a real trade, not a free one: a player who rushes the
radar and buys the weapon as early as the pre-D33 commander did (placed at
t=3166) first fires at about t=8701 (the median that commander measured doing
exactly that), against the commander's 11536 under D33,
which is about 2835 ticks (3.1 minutes; the Architect's estimate was about
2600), and pays 4000 credits and 150 power at minute 3.5 instead of an army.
And D33 names the playtest that would find that early strike decisive as its
own reversal.

**Reading R1 of D1: "a wave beyond the home garrison" (`army >= wave size +
garrison`).** Measured as an experiment on the step-4 build: placed at a
median 3376 (Directorate) and 3451 (Sodality), median first launch **8911**,
at most 4.2 launches per seat per 30 minutes. Rejected: it misses F8 by 1889
ticks, because the commander stands a wave plus its garrison only about 200
ticks after it stands a wave.

**Reading R2 of D1: "after the commander's first wave has gone".** Measured
the same way: placed at a median 3391 and 3466, median first launch
**8956**, at most 4.0 launches per seat per 30 minutes. Rejected: it misses
F8 by 1844 ticks, because every commander launches its first wave at about
t=3400. Any condition met by the first wave lands the first launch near
t=9000; only the charge floor (D33) reaches the band.

**A world-level "powers" file (a new /data kind) rather than columns on the
structure def.** One file would state "one superweapon per faction on the
same charge" by construction. It would also mean a new kind, a new schema, a
new registration step and a new checksum section, for numbers that already
belong to buildings: the charge is the superweapon building's, and each
building that strikes has exactly one strike. Columns reuse the structure
catalogue's whole machinery, and the parity gate holds the two charges equal.

**Give the seismic charge its `strike_damage` now.** Hash-neutral, and it would
tidy the column. Rejected because the seismic charge's impact is P8-19's
(D3), and a number moved into /data one row before its function is
reworked is a number that row has to reason about twice.

## The measurement, before and after

"Before" is 387dc39. "D1, D2, D15" is step 6 (7767366), before D33. "Final"
is this ADR's last commit, with D33. Every figure is from the shipped setup
(/data registered through `BuildWorld`'s configure hook).

### F8: `pillarprobe` and `pillargate`

| | before | D1, D2, D15 | final (D33) |
|---|---|---|---|
| placed, Directorate seats (median) | 2911 | 3166 | 6001 |
| placed, Sodality seats (median) | 3016 | 3196 | 6001 |
| median first launch per match | **4546** | 8701 | **11536** |
| matches that launched | 72 of 72 | 72 of 72 | 66 of 72 |
| launches per seat per 30 minutes, median | 12.2 | 2.4 | 1.8 |
| launches per seat per 30 minutes, max | **15.0** | 4.3 | **3.6** |
| most launches in one match | 29 | 8 | 6 |

`pillargate` final: rate half **PASS (binding)**, first-launch half **PASS
(binding)**. Between steps 4 and 7 the first-launch half was registered
non-binding and printed WOULD-FAIL at 8701. The six matches that never
launch end before the weapon charges, so they are early wins, not an absent
weapon: D33 reverses only if fewer than half the sweep launches.

**Proved to bite.** The final build with `charge_ticks: 1500` restored in its
copy of /data, run `pillargate --bind`, exits 1 on both halves (the floor
then opens at 1500, below the t=3166 economy gate, so it reproduces the old
pacing exactly):

> `FAIL: pillargate (F8 rate) (binding): 1 failure(s): a seat launched 15.0
> times per 30 minutes (skirmish-06 DD o0, launches 15/14 in 27000 ticks),
> over 5`
>
> `FAIL: pillargate (F8 first launch) (binding): 1 failure(s): median first
> launch 4801 is before 10800 (earliest 4726, over 72 of 72 matches)`

**Proved to bite at the other edge** (the Architect's condition C4). The
same build with `charge_ticks: 8000` in a scratch copy of /data, run
`pillargate --bind`, exits 1 on the first-launch half with both new clauses,
while the rate half passes (at most 2.1):

> `median first launch per match: 16756 (earliest 16756, latest 16951; 26 of
> 72 matches launched)`
>
> `FAIL: pillargate (F8 first launch) (binding): 2 failure(s): median first
> launch 16756 is after 14400 (minute 16, the top of D1's and D33's band);
> only 26 of 72 matches launched, fewer than half (D33's reversal)`

### F6, as a side effect (`pillarprobe` and `endgate`, not this row's criterion)

| | before | D1, D2, D15 | final (D33) |
|---|---|---|---|
| in the 15 to 30 minute window | 36 of 72 (50 per cent) | 38 of 72 (53 per cent) | **48 of 72 (67 per cent)** |
| early (before 13500) | 8 | 26 | 20 |
| no result at 27000 (`endgate`) | 28 | 8 | **4** |

Without strikes on both refineries at t=4621, economies grow and matches
resolve. With D33 the commander also spends its first 4000 credits on army
rather than the weapon, so it fights for the middle of the match instead of
sitting behind a charging cannon. The window share reaches 67 per cent
against F6's bar of 70, and `endgate`'s four unresolved matches are
skirmish-04 DD and skirmish-07 DD in both orientations. F6 is P8-24's; it
starts from this base, and it is three matches short.

### F3: `aiairgate` (binding), with D32's no-raid control

| cell | harvesters at the answer: before / D1, D2, D15 / final | no-raid control at t=10500: before / D1, D2, D15 / final |
|---|---|---|
| Normal Directorate | 2 / 3 / 3 | 4 / 5 / 5 |
| Normal Sodality | 3 / 2 / 3 | 0 / 2 / 5 |
| Hard Directorate | 3 / 4 / 4 | 7 / 8 / 7 |
| Hard Sodality | 4 / 4 / 4 | 2 / 8 / **0** |

PASS at every stage; the last flyer falls by t=4950 in every cell. **D32's
reversal condition watches the control**: it asks that P8-18 and P8-22 land
and the control keep a harvester to the window's end in every cell. After
D1, D2 and D15 it did in all four. **After D33 it does not: the Hard
Sodality control ends the window with no harvester** (12 lost to the ground
war; the Normal Sodality cell, which had none before this row, now keeps 5).
So the condition is not met on this base. It waits on P8-22 in any case,
and the gate is not changed here.

### `cheesegate` (binding)

10 of 10 PASS before, after D1, D2 and D15, and final.

### `seatfairgate` (F5, non-binding until P8-21)

| | before | D1, D2, D15 | final (D33) |
|---|---|---|---|
| income within 15 per cent | 22 of 32 | 18 of 32 | 20 of 32 |
| pooled decided | 16 | 28 | 28 |
| pooled, by seat | 8/8 | 14/14 | 14/14 |
| pooled, by start | 8/8 | **20/8** | **20/8** |
| Directorate mirrors, by start | 6/6 of 12 | 10/4 of 14 | 10/2 of 12 |
| Sodality mirrors, by start | 2/2 of 4 | 10/4 of 14 | 10/6 of 16 |

A real shift and a worse one. Before, half the mirrors never resolved; with
both refineries no longer struck at t=4621 they resolve, and a start bias the
stalemates had hidden shows itself, now failing the 60/40 clause as well as
income. That residue is D5's (P8-21), which measures against this base.

### `ladderprobe` (360 matches to 18000 ticks)

| | before | D1, D2, D15 | final (D33) |
|---|---|---|---|
| undecided unique matches | 160 of 324 | 131 of 324 | 120 of 324 |
| seat 0 share of decided | 106 of 164 (65 per cent) | 121 of 193 (63 per cent) | 133 of 204 (65 per cent) |
| Directorate mirror, seat 0 to seat 1 | 22-6 | 18-13 | 21-9 |
| Sodality mirror | 16-8 | 28-13 | 38-14 |
| Directorate seat 0 against Sodality | 59-3 | 63-1 | 67-3 |
| Sodality seat 0 against Directorate | 9-41 | 12-45 | 7-45 |
| Normal over Easy | 23-11 | 26-13 | 31-17 |
| Hard over Normal | 19-20 | 19-24 | 23-23 |
| **Brutal over Normal** | **25-7** | **21-24** | **28-18** |
| Brutal over Easy | 33-9 | 29-15 | 24-15 |

Brutal lost its edge over Normal after D1, D2 and D15 (25-7 to 21-24) and
D33 gives part of it back (28-18). The likeliest reading, not proved here:
Brutal's 5000-credit handicap bought the first superweapon and the first
strike, which stopped converting once the strike came at nine minutes; with
D33 that 4000 credits goes into army instead. F4 is P8-26's to make true,
and it starts from this base. The faction gap is unchanged in kind: the
Directorate beats the Sodality from either seat.

### The purchase gate, measured three ways

D1 says the commander buys the superweapon "only once it holds two refineries
and an army of at least one wave". The literal reading (`army >= wave size`)
places the weapon at t=3166, only 255 ticks later than before, because every
shipped-setup commander already holds two refineries and a wave of army by
then. Three other conditions were measured as experiments on the step-4
build (cannon 900, scan 75), and the third was then taken as D33:

| purchase condition (all also need the radar and 4500 credits) | placed (median, Dir / Sod) | median first launch | max rate |
|---|---|---|---|
| D1 as written: two refineries, army of one wave | 3166 / 3196 | **8701** | 4.3 |
| R1: a wave beyond the home garrison (`army >= wave + garrison`) | 3376 / 3451 | 8911 | 4.2 |
| R2: as written, and after the commander's first wave has gone | 3391 / 3466 | 8956 | 4.0 |
| R3, now D33: as written, and not before one full charge into the match (`Tick >= charge_ticks`) | 6001 / 6001 | **11536** | 3.6 |

R1 and R2 are disproved as fixes, so neither should be retried: any condition
the commander meets with its first wave lands the first launch near t=9000.
Only R3 met F8 (66 of 72 matches launched under it).

## How F8 was closed

**The shortfall.** With D1 as written the median first launch was **8701
(9.7 minutes)** against F8's 10800 (minute 12), which is also outside D1's
own reversal band, "minute 10 to 16".

**The root cause** is the commander's ladder, not the charge. Every
shipped-setup commander reaches the top of its build ladder (radar, two
refineries, a wave of army, 4500 credits) by about t=3200, the weapon takes
its 600-tick build, and the first launch then lands at about placement plus
5400. F8's 10800 is exactly twice 5400, so the bar is equivalent to "placed no
earlier than about one full charge into the match", and nothing in today's
ladder happens that late.

**The decision.** This ADR measured R3 and recommended it without taking it,
because it is a new rule rather than D1's. The orchestrator took it as
**D33** under the owner's standing authority, recorded in the tracker after
D32. Its four edits are steps 7a and 7b: the rung condition; `aisuper`'s
window at 3000 plus twice the charge, with its enemy base sealed from the
ground; `PillarGateFirstLaunchBinding` set; and the two moved goldens
regenerated and attributed above. On the final build (D2 and D15 included)
it measures exactly as the experiment did: placed at 6001, median first
launch 11536, 66 of 72 launching, at most 3.6 launches per seat per 30
minutes.

## Hash and format

**Three goldens move; twenty-one are byte-identical.** Eight steps, `golden
2026` measured after each, so every move has one cause. For each data step
that moved a row (3 and 5), the OLD value was registered over the new def
through a temporary runner hook (never committed, never in /sim), and all 24
rows reproduced the previous step's hashes byte for byte, which is what makes
the attribution exact rather than inferred. The code steps (4, 7a, 7b) each
change one thing, and their causes are traced as build and launch ticks.

| step | change | rows moved |
|---|---|---|
| 1 | support powers decoupled (500, 166, 300) | none |
| 2 | three columns into /data at today's values | none |
| 3 | charge 1500 to 5400, `aisuper` re-timed | `skirmish`, `superweapon`, `aisuper` |
| 4 | the purchase gate | `skirmish`, `aisuper` |
| 5 | cannon 900 to 2500 | `superweapon`, `aisuper` |
| 6 | scan 75 to 150 | none |
| 7a | `aisuper`'s window to 3000 plus twice the charge (ahead of D33) | `aisuper` |
| 7b | D33's purchase floor (with `aisuper`'s sealing column, measured neutral on its own) | `skirmish`, `aisuper` |

| scenario | before | after | cause, measured |
|---|---|---|---|
| `skirmish` | `0xEE4F52D597293114` | `0x093568453FAB056D` | step 3 (`0x09BE1920ABA31742`): both cannons were placed at t=2911 and struck at t=4621; at 5400 neither fires inside the 5000-tick window. Step 4 (`0x56C9BC3B6B6A3742`): both are placed at t=3166 instead of t=2911. Step 7b: no cannon is placed at all inside the 5000 ticks (the floor opens at 5400), so the 4000 credits each commander spent on it at t=3166 go elsewhere; the report goes from 26 to 36 entities destroyed and from treasuries 11583/12256 to 13563/13712. |
| `superweapon` | `0xE11186E5719BADC2` | `0x497113098A9FD259` | step 3 (`0x87697FB3A2C4A70E`): the impact restarts the charge at 5400, not 1500. Step 5: the outer-ring factory takes 1000 instead of 360. |
| `aisuper` | `0x6E4761560D843874` | `0xF5CF64A3FB56EA21` | step 3 (`0x84D33A9DFDAD2692`): placed at t=1921 both ways; the first launch moves from t=3556 of 4500 to t=7456 of 8400, at the refinery both times, which ends at 1280 of 2000. Step 4 (`0xA436C21377C68AB3`): placed at t=2446; first launch t=7981. Step 5 (`0x59B5782EC342FE29`): that launch now destroys the refinery. Step 7a (`0x34767B408E259229`): placement and first launch unchanged; the scenario runs 5400 ticks longer (13800). Step 7b: placed at t=6001, first launch at t=11536 aimed at the refinery, which dies at the impact (t=11611). |

**Why the other commander goldens do not move.** `expansion` and `mission`
did not move at step 3, so no superweapon stands in either when its hash is
taken; nor at steps 4 or 7b, so the gated rung never decided a build there.
No golden fires a support power, which is why steps 1, 2 and 6 moved nothing.

**Catalogue checksum MOVES, `0xF384205E0D0BF2D2` to `0x1255012DCF3D3A68`**,
in four steps: the columns folded at today's values (`0x24347943098C8AE3`),
the charge (`0xCC96BA2C5A4CF833`), the blast (`0xB2BB651A7469F4C1`) and the
reveal (`0x1255012DCF3D3A68`). Steps 1, 4, 7a and 7b change no catalogue
value; D33's floor reads the existing `charge_ticks`.

**Saves, replays and the LAN hello under the moved checksum** (the
Architect's condition C5), the same pre-first-public-build trade earlier
catalogue rows took:

- **Refused.** Every save from format v3 onward (the current format is v14;
  this ADR adds no version) and every v3 replay records the catalogue
  checksum it was made under. One made before this ADR carries
  `0xF384205E0D0BF2D2` (or an older value), and a build at
  `0x1255012DCF3D3A68` refuses it on load with both checksums named, rather
  than resuming it into a different game.
- **Loaded unchecked.** Pre-v3 saves (v1 and v2) and v2 replays carry no
  checksum, so they cannot be checked and load into the new pacing: a
  superweapon in one keeps the `ChargeTicks` it was saved with and takes
  5400 at its next recharge. A missing checksum means do not check, never
  refuse; `catrefuse` stages 4 and 5 assert exactly that (a v2 save loads
  under a foreign catalogue; a v2 replay is never refused).
- **Refused at the hello.** A LAN peer on a build from before this ADR
  holds the old checksum and is refused before tick 0, with both checksums
  named on both sides (`catrefuse` stage 3).

**Format.** No Entity field, no save version, no wire change. The state hash
changes only through the three moved scenarios. Changing their hashes is a
replay-compatibility break, ratified under D1, D2 and D33 as stated above.

## What reverses it

- **D1** reverses if `pillarprobe`'s median first launch falls outside minute
  10 to 16, or a playtest reads the weapon as absent. With D1 alone it fell
  at 9.7 minutes (8701); with D33 it is 12.8 minutes (11536), inside the band.
- **D33** reverses if `pillarprobe`'s median first launch exceeds minute 16
  (14400), fewer than half the sweep's matches launch at all, or a playtest
  reads the AI's superweapon as absent or the human's early first strike as
  decisive. Today: 11536, and 66 of 72 launch.
- **D2** reverses if the figure `pillarprobe` prints as **"loser's last yard
  to a superweapon"** exceeds **25 per cent** of the sweep's decided matches:
  one strike per 6 minutes ending matches on its own. The figure (added for
  the Architect's condition C2, runner only, goldens neutral) counts the
  decided matches in which the loser's last Construction Yard died to a
  superweapon impact, attributing a death to a strike when its Died event
  directly follows the SuperweaponImpact in the tick's event list and lies
  within 6 cells of the impact point. Today: **0 of 68 decided matches**. With
  the old 1500-tick charge and the same cannon it reads 5 of 45 (11 per
  cent), which shows the figure counts and that even four-times-faster firing
  stays well under the threshold. `pillargate` prints the same line.
- **D15** reverses if the playtest reads 10 seconds as a free map reveal.
- The decoupling reverses only if a later decision wants the powers to move
  with the superweapon again, in which case it should say so explicitly.

## Audit follow-ups

A determinism audit of this row found D33 and the sim diff clean for
determinism and save/load, and raised five follow-ups, landed in one
commit. No golden moves and no catalogue value changes.

1. **Registration demands what the loader demands.** `RegisterStructureType`
   now calls `World.ValidatePacing`, which refuses by column name a
   superweapon with `ChargeTicks` below 1, a superweapon that does not destroy
   fields or a building granting the precision strike with `StrikeDamage`
   below 1, and a building granting the orbital scan with `RevealTicks` below
   1. The seismic charge stays legal with no strike, as in the loader. Before
   this, a code-built def took the record's zero defaults unchecked, and
   `supportpowergate`'s carrier (the scan on a Radar Uplink) had been firing
   a scan that lit for at most one tick. The carrier now takes the Bastion's
   registered reveal, and stage 3 asserts the aim cell is still lit two ticks
   after firing (a one-tick reveal fails it, measured). `seismicaimgate`
   stage 5's field-sparing seismic charge carries `SeismicDamage` as its
   strike. `powerdatagate` stage 5 proves eight refusals by name and three
   controls; with the check removed it fails on the first.
2. **Ceilings.** `charge_ticks` at most **108000** (two hours at 15 Hz,
   twenty times GDD s8's six minutes); `strike_damage` at most **100000**
   (forty times the cannon's 2500, so `DamageOf`'s `baseDamage * pct` stays
   exact for any matrix percentage up to 21474, against a shipped maximum of
   100; it would wrap above about 21.47 million at 100); `reveal_ticks` at most
   **27000** (a whole 30-minute match). The same numbers are
   `World.MaxChargeTicks`, `MaxStrikeDamage` and `MaxRevealTicks`, enforced
   by registration, by the loader and stated as schema maxima.
3. **A live superweapon crosses a save in CI.** `saveload` gains a stage
   with the orbital cannon saved with its strike in flight (StrikeTicks 36 at
   (30.5, 30.5)) and the seismic charge saved mid-charge (5360 of 5400 still
   to run). Both round-trip exactly, the loaded hash equals the saved one, and
   the resumed run lands the strike, begins the recharge and reaches the
   uninterrupted hash at tick 200. The golden scenarios are untouched.
4. **F8 holds on the registered charge, in CI.** `powerdatagate` (in
   `match`) asserts for every superweapon def loaded from /data that
   **2 x charge_ticks + build_ticks >= 10800**. Derivation from `pillargate`:
   under D33 the weapon is placed at about charge plus build (6001 = 5400 +
   600 + 1) and first fires about one charge later (11536 = 6001 + 5400 +
   135, the 135 being power and beat slack), so the first launch is at least
   2 x charge + build. Dropping the slack only makes the bound stricter, and
   it assumes D1's economy gate is met before the floor (measured about
   t=3170). Today it reads 11400; it fails below a charge of 5100, and a
   5000-tick charge (10600) is shown to fail it.
5. **The checks re-run** after these fixes. `pillargate` PASS while binding
   on both halves (median first launch 11536, 66 of 72 launching, at most 3.6
   launches per seat per 30 minutes). With `charge_ticks: 1500` in a copy of
   /data, `pillargate --bind` exits 1 on both halves (median 4801, a seat at
   15.0); the repository's files were never changed. `aiairgate`,
   `cheesegate` and `powerdatagate` PASS. `match 2026` exits 0 in 61 s on
   this machine. `aisuper` still tests its name (placed at t=6001, fired at
   the standing refinery at t=11536, aim asserted, refinery destroyed), and
   so does `aifactiongate` (each side's commander reaches its own
   superweapon, types 6 and 22). Goldens 24 of 24 byte-identical to the file.

## Consequences

**What gets better.** The superweapon no longer opens the match: the
commander's first strike lands at a median of 12.8 minutes rather than 5, a
seat fires it at most 3.6 times in 30 minutes rather than fifteen, and when
it fires the orbital cannon ends a refinery in one blow. The scan is long
enough to read. The charge, the blast, the strike and the reveal are /data
that the sim plays, checked by the LAN hello, saves and replays. As a side
effect, matches resolve: 48 of 72 now end in the 15 to 30 minute window
(was 36) and 4 run to 27000 unresolved (was 28).

**What is asymmetric now, deliberately.** D33 times the COMMANDER's
purchase. A human may build the weapon as soon as the radar stands and fire
it one charge later, at about nine or ten minutes against a commander that
holds back until about twelve. D33's reversal names the playtest that would
find that decisive.

**What is now true that was not.** "Shorter timers" is held by a gate rather
than by arithmetic, and the precision strike and the jam no longer follow the
numbers they were derived from. CLAUDE.md's "Support powers" paragraph was
reworded to match on integration.

**What is not done.** The support-power charge and the jam are still compiled
absolutes (tracker row P8-61, the Architect's carried condition C6). The
seismic charge's damage is still compiled; P8-19's row now names moving
`SeismicDamage` (350) into its `strike_damage` (also C6), by changing the strike
rule in the single read predicate `World.PacingColumnsRead`, which the loader
and registration both call since this ADR (carried condition C7, done here).
D32's no-raid control loses its Hard Sodality harvester on this base (above),
and `seatfairgate`'s start split is now visible at 20/8; both are for the rows
that own them (P8-22, P8-21).

**A rule for later rows until `pillargate` runs in CI** (the Architect's
carried condition C8). F8 is held in CI only by `powerdatagate`'s charge
bound; the measured median and launch count are not. So every later row
whose diff touches `SkirmishAI`'s build ladder or a superweapon def records
its two `pillargate --bind` verdict lines, the rate half and the first-launch
half, in its ADR or its PR. P8-52's reissued playtest brief cites this ADR,
as that row now says.

## Architect sign-off
Signed off with conditions by the Architect on 2026-10-03, reviewing the P8-18 integration branch with its audit follow-ups applied. Before-merge conditions C1 to C5 are met in the same pull request that lands this ADR (implemented in the row's lane as commit 815c74c, which the squash merge does not preserve). Carried conditions C6 (row P8-61 and P8-19's seismic damage), C7 (the single read predicate) and C8 (pillargate verdicts recorded until it runs in CI) are recorded in the tracker and in Consequences.
