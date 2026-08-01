# 24. Classic parity: what the benchmark games have that Ferrostorm does not

Author: game-designer + producer agents. Originally written 2026-07-17 against
main ca481d3; **rewritten 2026-07-30 against the current tree.**

This document was rewritten rather than amended. The previous version carried
three stacked status banners telling the reader that most of its body was no
longer true, which is a document that has stopped being one. Everything below
describes the tree as it stands. Where a gap has been closed it has been
deleted rather than annotated.

Method: every count and every claim of absence below was taken from the working
tree - the `/data` roster, the sim's own enums, and the committed maps - and not
from memory.

## What already matches, so nobody rebuilds it

The harvester economy with regrowth; construction yard with build radius and MCV
deploy; per-producer queues with ready-and-place, parallel lanes and
cancel-refund; selling; structure and vehicle repair, including an AI that now
repairs its own buildings; walls; partial-power rules with brown-out and radar
blackout; engineer capture; neutral capturable outposts; stealth, detection and a
stealth-projector structure; veterancy with rank pips; a superweapon with charge,
launch detection and impact alerts; infantry crushing; attack-move; destroyable
bridges; fog of war; minimap with pings and jump-to-event.

And a set of things the benchmark games did NOT have, which this project should
stop treating as catching up: unit stances (hold-fire, guard, patrol),
formations, control groups and army-select keys, camera bookmarks, grid build
hotkeys, an idle-harvester key, a difficulty ladder held separate from opponent
personality, deterministic lockstep with hash-verified replays, and save/load
that round-trips a live match.

## The scale gap, measured

| | Benchmark 1995 | Benchmark 1996 | Ferrostorm today |
|---|---|---|---|
| Units | ~20 per side | ~30 per side | **13 total** |
| Structures | ~20 | ~25 | **13** |
| Defensive structures | 4 | 6 to 7 | **1** |
| Superweapons | 2 | 3 | **1** |
| Campaign missions | ~15 per side | ~14 per side | **6** |
| Simultaneous players | 4 | 8 | **2** |

The thirteen units are six shared (engineer, harvester, MCV, repair vehicle,
rifle squad, rocket squad), five Directorate and two Sodality. The thirteen
structures are ten shared, two Directorate and one Sodality.

## Tier A: whole systems that do not exist

These are not missing units. They are missing dimensions of play, and each
removes a category of decision rather than an item from a list.

**A1. The air layer. DELIVERED (P7-4, ADR-028).** The Airfield, the Strike
Flyer and the Flak Track landed together, because the ADR binds them: an air
layer without an answer is a dominant strategy rather than a feature. Aircraft
ignore terrain entirely and no ground weapon can touch them; the flak track
kills them and cannot shoot the ground. What is NOT delivered, so the row is not
read as finished: no reload cycle, the AI neither builds nor answers aircraft,
and the models are interim. The original gap read: Both benchmarks used air as a third dimension:
fast strike craft that ground defence cannot answer, and the anti-air building
that answers them. `EntityKind.Airfield` exists in the sim enum and the skirmish
AI already lists it among its wave targets, so the shape was anticipated and
never filled. Tracked as C5; needs an ADR and art.

**A2. Transport. DELIVERED (P7-3).** The Carrier: unarmed, carries five, and
what it carries is a data question rather than a hardcoded list - anything the
barracks produces, which includes the engineer, so the classic delivery gambit
is now possible. A carried unit is DESPAWNED rather than flagged, because a
live-but-skipped entity would have to be remembered by movement, combat,
separation, selection and drawing, and this phase has already been bitten three
times by exactly that kind of enumeration. A destroyed carrier takes its cargo
with it, so the hold is not somewhere to hide an army. Save format v9 carries
the hold; without it a player who saved with troops aboard would have loaded to
find them gone.

**A3. Naval.** Absent entirely, and out of scope by standing decision (below).
Recorded because skirmish-08 now puts two seas on a map with nothing on them, so
the cost of that decision is visible in a way it was not before.

## Tier B: roster holes that distort play

**B1. One defensive structure.** The turret is the only defence in the game.
Its file used to read `faction: directorate`, and this document's first draft
reported that as the Sodality being unable to defend at all. **That was wrong**,
and the correction is worth keeping: nothing enforced the line. StructureTypeDef
carried no Faction field, so a building's declared side was parsed, validated and
then discarded, while the sim hardcoded one expression naming the Veil Projector.
Both sides could always build the turret. P7-1 fixed the mechanism - the side now
comes from /data - and declared the turret and superweapon `common`, which
preserves what play always did.

P7-2b then gave each SIDE its own defence - the Directorate's Bastion (tough
and dear, GDD s3's "buildings are tough but expensive") and the Sodality's
Shroud Nest (cloaked, GDD s3's "cloaked units AND structures", decloaking when
it fires per the same section's stealth rule). It is the first row in the game
where a faction can build something the other cannot, beyond the Veil.

P7-2 added the first of the missing legs: the Emplacement, anti-infantry,
cheap, and deliberately WORSE against armour than the turret is - measured at
32 ticks against a rifle squad where the turret takes 91, and 632 against
armour where the turret takes 143. Defence is now a choice rather than a
ladder. What remains true is the rest: ONE more type cannot express the
rock-paper-scissors the benchmarks built defence around, anti-infantry against
anti-armour against anti-air, and doc 27's balance work already measured that
static defence cannot hold. Neither side has a distinctive defensive structure.
That is P7-2, and it is breadth rather than a defect.

**B2. No storage, so the economy has no ceiling. NOT TAKEN, and deliberately.**
GDD s4 specifies the economy in full - resource, harvester, refinery, design
intent, secondary income - and says nothing about storage, a cap or overflow. A
ceiling would change the "float at 2 refineries / 3 harvesters" intent the GDD
DOES specify, which makes it a GDD-silent mechanic in the same category as
crates and the map editor: Producer sign-off, not a judgement call. The benchmarks capped credits
and made silos a real decision, with overflow lost. There is no silo and no cap
here, so banking is free and there is never a reason not to hoard.

**B3. No support infantry.** No medic, no field mechanic, no scout animal. The
repair vehicle covers part of the mechanic's role; nothing covers the rest.
**Partly closed 2026-08-01** (P7-11a, ADR-030): the Sodality's Saboteur ships,
and with it a third state a building can be in - standing, rubble, and now
switched OFF. It is the sabotage half of the doctrine GDD line 30 describes,
of which the faction previously had only the theft half.

The rest of this entry is left open and should be read carefully, because it is
the weakest entry in this document. The medic, the field mechanic and the scout
animal are named HERE as absences and appear nowhere in the GDD. B3's own second
sentence concedes the repair vehicle already covers the mechanic's role. A gap
analysis noticing that a benchmark had something is not the same as this game's
design asking for it, and the remainder of B3 needs a Producer to say which of
those three, if any, this game actually wants before it is a ticket.

**B4. No hero unit.** Each benchmark had one, and it carried a disproportionate
share of that game's character. **NOT TAKEN, and blocked on the Producer**
(P7-11b). The GDD names both heroes - `Commando (hero, one at a time)` and
`Shadow Commando (hero)` - and gives neither an ability, stats nor a tier, and
"one at a time" has no machinery in the sim at all: there is no per-unit-type
build cap anywhere. Everything interesting about a hero would be invention.
Doc 23 has moreover already ruled on this exact GDD line, at s142 and again at
s599: it "mandates the Repair Vehicle exactly as much as it mandates the
Commando, which is to say it is a sample, not a system statement."

**B5. Infiltration: HALF DELIVERED (P7-7).** The Sodality's Infiltrator steals
a share of an enemy treasury and is consumed by the act - the credits MOVE
rather than appear, and the building is left unharmed and in enemy hands, so it
is a robbery rather than a second capture. GDD s7 also names a Saboteur
(disables buildings), which is not built. The original gap read: Ferrostorm has stealth and detection, which is half of
information warfare. It has nothing that steals, reveals, disables or denies: no
spy, no thief, no jamming structure. The Sodality's written identity is raiding
and economy denial, and the roster gives it no tool for the second half of that.

**B6. No mines or minelayer.** **NOT TAKEN, and blocked on the Producer**
(P7-11c). Worth stating what this one-line entry is actually asking for, because
the line badly understates it: the word "mine" appears nowhere in the GDD, in
any ADR, or anywhere in `sim/`. The damage half would be nearly free - splash
damage exists, `ApplyAreaDamage` exists, and the superweapon is a working
countdown precedent - but the TRIGGER is the entire feature, and a dormant,
invisible, proximity-fired entity that is owned but not targetable has no
precedent in this sim at any point. One line of gap analysis, one whole new
mechanic, and no design behind it.

**B7. One wall type at a flat 100 credits**, where the benchmarks tiered
barriers by cost and durability - and gates are still deferred (C6b).

## Tier C: faction identity, the thinnest part of the game

The Sodality has **two** unique units and **one** unique structure; the
Directorate has five and two. Sixteen of the twenty-six roster entries are
shared. In the benchmark games the two sides genuinely played differently. Here
most of the game is a common roster with a small garnish, and a player choosing
a side is choosing a handful of items rather than a doctrine.

Doc 27 reached the same conclusion from the other direction and called asymmetry
the least-delivered of the GDD's five pillars. Its DR-02, DR-03 and DR-04
(faction power economics, a Sodality detection answer, faction-distinct
superweapons) are the designed response.

**A third closed 2026-08-01** (P7-5a, ADR-042): DR-02 shipped, so the two sides
no longer share a power grid. The Directorate keeps one big fragile plant and the
Sodality builds three small generators for the same supply, which is GDD s3's own
"centralised" against "decentralised" written out in numbers. The doctrine is
pinned as behaviour rather than as a stat line: **one building lost darks a
Directorate base completely (100 to 0 supply) and costs the Sodality under half
its grid (120 to 80).** That is the first time in this game that the two
economies differ at all.

Two things about that row are worth carrying forward rather than leaving in the
ADR. It could not be built until a **prerequisite stopped naming a building and
started naming a capability** - five prerequisites name the power plant by type
id, so a Sodality player holding only generators would have satisfied none of
them and been stuck one rung deep forever. And reading GDD s8 for DR-04 turned up
a live defect worth more than the row: **any unit could delete an entire ferrite
field with one shot**, because a field has 1 hit point and the explicit-attack
branch was the only one in the sim that did not exclude fields. GDD s8 reserves
destroying a field to the Sodality seismic charge, so that identity could not
have shipped on top of it.

**Two thirds closed 2026-08-01** (P7-5b, ADR-043): DR-03 shipped, so GDD line
56's "every stealth tool has a public counter" is true for both sides rather than
one. The Sodality gets a Watch Post, a **structure** rather than a unit, because
every Sodality unit is itself cloaked and a cloaked detector contradicts the same
line that requires one. The shape is the identity: the Directorate **sweeps**
with a mobile scout, the Sodality **waits** behind a planted post.

The justification Q017 gave for DR-03 was the mirror match, and it turned out to
be the weaker half. **`com_mine` is faction COMMON and stealthed, and its own
`/data` notes claim line 56 is satisfied "by a Sentinel Scout revealing the
field" - a unit only the Directorate can build.** A common tool with a
faction-locked counter is a defect rather than a missing feature, and the file
asserting otherwise was wrong for half the players.

**Closed 2026-08-01** (P7-5c, ADR-044): DR-04 shipped, so the two sides no longer
fire the same superweapon on the same timer. The pair is identical in cost, build
time, power draw and charge and differs **only** in effect, which is what GDD s8's
"one superweapon per faction" asks for: the Directorate's orbital cannon is
unchanged, and the Sodality's seismic charge is wide, softer and **destroys the
resource fields under it**. Measured on one factory at ground zero, the cannon
deals 720 and the charge 280.

**All three of DR-02, DR-03 and DR-04 are now delivered**, so the asymmetry
pillar doc 27 called the least delivered of the GDD's five has the two sides
differing in their power grid, their answer to cloak and their superweapon. All
three landed with the 24 goldens byte-identical.

Two things this row FILED rather than fixed, both stated plainly because they are
the honest state of it. **The sim charges its superweapon 3.6 times faster than
GDD s8's "~6 minute"** (1500 ticks against 5400); that is refused under charter
A11 as a balance change needing co-sign, with its reversal conditions recorded.
And **a Sodality commander can no longer queue a superweapon at all**, because the
AI ladder names struct type 6 by number and that is now a Directorate building.

That last one generalised into a row of its own, and it is **mostly closed
2026-08-01** (P7-5d, ADR-045). The ladder now asks the catalogue for "the
superweapon I can build" and "the building of mine that reveals cloak" rather
than naming type ids, and a query with no answer returns 0 so the rung is skipped
instead of queueing a building the sim refuses - which is the failure mode that
had been silently costing a Sodality commander its superweapon entirely.

Measured: a Sodality commander reaches struct type 22, its seismic charge, where
reverting the fix leaves it with **type 0, none at all.**

What the commander still does NOT build, stated plainly because the row is
deliberately narrow: **the faction defences** (Bastion, Shroud Nest) and the
**Veil Projector**, both of which are balance additions rather than defects since
the common turret works for either side.

**And the aim closed 2026-08-01** (P7-5e, ADR-046). Three waves had built the
seismic charge, made the commander build one, and left it aiming with the attack
wave's scan - at the nearest enemy refinery, which spends the only thing that
weapon does that the orbital cannon cannot. It now hunts **the richest CLUSTER of
enemy ferrite**, scored by what the whole 6-cell blast would take rather than by
the single fattest field, and it refuses to deny ground nearer its own base than
the enemy's.

The defect underneath was one this campaign created. ADR-044 selected the effect
with a **type-id literal**, the instance-not-property mistake this phase has
corrected about fifteen times - and its second cost was subtler than usual: it
left the AI no question it could ask. A commander cannot know that struct type 22
is special, but it can ask whether its superweapon destroys fields. An authored
`destroys_fields:` key now answers that for both the impact site and the aim,
which is what a `/data` key driving the runtime is supposed to look like.

**And the commander's economy doubled, 2026-08-01** (P7-7a, ADR-047). GDD s4
states the designed equilibrium outright - "a player floats at 2 refineries / 3
harvesters on one base" - and TICKET-AI-03 had capped the AI at one refinery per
base since the ladder existed. ADR-041 measured that consequence while refusing a
credit ceiling and named it correctly: the economy was **undersized, not
overflowing**. The treasury went from touching **0, 2, 19 and 1** across a
9000-tick match to floating between **1300 and 4000**.

**This is the first row in sixteen to move a golden hash**, and four moved:
`skirmish`, `expansion`, `aisuper` and `mission`, which are exactly the four that
run a commander building an economy. The other twenty are byte-identical.

The third harvester is still owed and its cause is now identified rather than
guessed: **the same GDD section says a refinery "includes one free harvester" and
the sim has never implemented it**, so the designed three is two free plus one
bought.

**That row was then built, measured and REFUSED** (P7-7b, ADR-048), and the
refusal is worth more than the feature would have been. The commander did reach
GDD s4's 2 refineries / 3 harvesters. It then banked **38,823 credits** with the
match still running, left its opponent at nothing with no victory declared, and
**stopped being able to clear mission-01's camp at all** where ADR-047 had left it
winning at tick 4946.

The cause is not the free harvester. The army rung stands aside "while the yard
still wants a structure it cannot yet afford - infrastructure before army,
always", and **that rule has no termination condition.** Two economy rows in a
row lengthened the build ladder, so the commander spends longer and longer
building and a richer economy makes it *less* able to fight rather than more.

**That diagnosis was wrong, and the correction closed the row** (P7-7d,
ADR-049). `economyprobe` gained army columns, and they disproved it in a single
run: on main the commander goes 3, 6, 9, 12 units while its credits oscillate
between 1292 and 4018, and the seat that banked 38,823 had **the biggest army on
the board** at 22 units. It was never failing to spend - it was out-earning one
factory and one barracks, whose queues cap at two items each. A throughput
ceiling, not a build-order stall.

The real culprit was a **`+1 bought harvester` derived and shipped beside** the
GDD clause, and never isolated from it. On its own the free harvester makes the
commander **faster**: mission-01 clears its camp at tick **3462**, against a 3688
baseline and the 4946 that ADR-047 left it at. The `+1` was not even necessary -
the skirmish start already provides one harvester, so two purchased refineries
deliver two more and the commander reaches **2 refineries and 3 harvesters**, the
float GDD s4 specifies, with no derived addition at all.

**GDD s4's economy is now fully implemented.** The lesson banked, because it cost
a wave: ADR-048 shipped two changes together, measured the pair, and blamed the
written one. Two changes in a wave is one change too many when either could
explain the result.

**And the economy rows then exposed something older** (P7-8, ADR-050). The
tracker carried a filed row saying the commander clustered its Sodality
generators where a single plant would have gone. Measured, that was wrong in the
opposite direction: the generators were **strung in a chain from the yard to the
map corner**, twelve of them, because `TryFindPlacement` walked the entity list
backwards and anchored each new building on the most recently built one. A base
did not cluster, it **walked**.

Walking the list forwards anchors on the Construction Yard instead. Measured, the
furthest structure from a yard went from **31 cells to 5** for the Sodality and
from **11 to 4** for the Directorate - so the drift was always there, and DR-02
multiplied it two and a half times by having one side build twelve power
buildings where the other builds five.

The trade is recorded rather than hidden: a compact base takes **9 of 12** power
buildings inside one seismic blast against 5 of 12 before. ADR-042's claim was
about losing a SINGLE building and is untouched; whether the Sodality should
deliberately spread against area weapons is a balance question filed for the
playtest.

This one is worth remembering for what it says about coverage: **the defect was
invisible for the whole project until an economy row made one side build twelve
power buildings instead of one.** Nothing had ever asked what shape a base was.

**So the next wave went looking on purpose** (P7-9, ADR-051), and found another
sitting under three rows that had already shipped. GDD s4 says a refinery
"processes a load in 8 seconds" and that a player floats at 2 refineries; the
second is only a design if the first is a BUILDING rate. It is not.
`UnloadTicks`' own comment says "refinery processes a load in 8s" while the code
applies it **per harvester with no occupancy check**, so any number unload at
once. Measured: six harvesters unload five-at-a-time at one refinery, and **a
second refinery earns 0 per cent more at three harvesters and 1 per cent at
six.**

In this sim a refinery is a **licence to own more harvesters**, not a station -
and that, rather than throughput, is why ADR-047's second refinery improved the
treasury. Serialising the dock is refused for now with three reversal conditions,
because three economy rows have landed unplayed and this would be a five-fold cut
on top of them.

The method is the transferable part: **a defect that nothing tests does not
announce itself, it has to be gone looking for.** Three waves running have now
found one by asking what no gate asserts.

**The third** (P7-10b, ADR-052) asked whether a long match degrades, because
entity ids are stable by construction so the list can never be compacted and
every system walks all of it. On skirmish-07 over thirty minutes the list grows
**211 to 558 while the living count stays flat at about 200** - two thirds of
every per-tick walk is a corpse - and **the match is still running at the top of
GDD pillar 2's 15-to-30-minute window** with both armies healthy. skirmish-01
hides both by ending at tick 13500.

Both are refused with conditions rather than fixed: the sim still runs at about
1 ms per tick against TDD s6's 8 ms budget, and whether the basin is simply a
long-game map is a balance question for the playtest rather than something to
tune blind.

**And the pattern across all three is now unmistakable: each defect sat beside a
gate that asked a neighbouring question.** `basingate` plays skirmish-07 and asks
whether it is a stalemate, never whether it ends. The load scenario asks what 600
units cost, never what a match accumulates. `aitargetgate` asks where a wave is
aimed, never whether it arrives. Asking one step to the side of an existing gate
has found something every single time.

## Tier D: content volume

**D1. Six campaign missions** against roughly fifteen per side in the
benchmarks, with the voice set still placeholder TTS pending the legal check.
**Half closed 2026-08-01** (P7-9, ADR-029): missions 04 to 06 shipped, and the
gap is now six against fifteen rather than three.

What matters more than the count is that the three new ones are shapes the
campaign did not have. Missions 01 to 03 are a strike, a theft and a siege, all
three decided by what is left standing. **Ironhaul** (04) is decided by what
ARRIVES, which is the first non-destruction win condition in the game and the
mission the Carrier exists for. **Skyfall** (05) is the first mission where a
whole CATEGORY of unit is not optional: nothing in the player's starting force
can touch an aircraft, by ADR-028 clause 3, and the answer has to be built.
**Ashen Crown** (06) is the only mission that is a whole game, and it is where
the rest of P7 is put in front of the player at once - defence with a shape, a
contested sky, a treasury worth stealing.

They are also the first missions that are GENERATED rather than hand-typed, at
96x72, 112x80 and 128x96 against the old 64x48, and the first with the
decorative layer the skirmish pool got in P6 and the campaign never did.

Still open on this row: nine or so more missions to reach the benchmarks, the
voice set, and bringing missions 01 to 03 under the generator (P7-9a, deferred
because it moves two goldens).

**D2. Two player seats.** GDD section 9 promises skirmish against one to seven
opponents and custom lobbies up to four against four, and the production plan
lists 4v4 as in scope. This was called the widest single divergence between what
ships and what is written down, and the call was right.

**The ENGINE half is closed, 2026-08-01** (P7-8a, ADR-031): the sim plays
free-for-all with any number of seats, `PlaceSkirmishStart` places N, and the
client no longer infers a winner by flipping a seat number. All 24 goldens
byte-identical.

The survey that preceded it inverted the expected cost and is worth recording,
because this entry had assumed the sim was the hard part. It was not:
`VictorySystem`, the save format, the LAN relay and `SkirmishAI` were ALREADY
correct for N players. Three of the four two-player assumptions were in the
client, and the worst of them was silent - with three seats the victory banner
was exactly inverted, showing VICTORY to a player who had not won.

What remains, and it is most of the player-facing half:

- ~~**No shipped map can host a third player**~~ **(P7-8b, closed 2026-08-01).**
  `tools/mapgen.py` writes every feature as its full ORBIT under the map's
  symmetry group rather than as a 180-degree pair, and every fairness check runs
  over all starts. The pool's first four-player map is **skirmish-09, "Kilnmoor
  Quarters"**, 160x120, 9.15 per cent density, eight neutral outposts. The group
  is a double mirror rather than a quarter turn, because 90-degree rotation
  requires a square map and no map in the pool is square. Seats 0 and 1 are
  asserted to be the 180-degree pair, so a two-player game on it is exactly as
  fair as on any two-start map, which is what lets the menu offer it while the
  lobby still expresses only two seats. All ten previously committed maps
  re-generate byte-identically, which is the proof the refactor changed nothing.
- **The lobby cannot express a third seat.** `MatchSetup` carries one
  `OppFaction`, and both codecs that persist it are shaped for two.
- **4v4 is a separate project** (P7-8c, Producer-blocked). There is no team
  field, no alliance table and no `AreAllied` predicate anywhere in the sim;
  hostility is decided everywhere by "not me and not neutral". It touches
  targeting, splash friendly-fire, detection and fog sharing, victory and the
  AI, and it has no code to build on. Free-for-all was reachable from here and
  teams are not, which is why the row was split rather than half-delivered.

## Deliberately out of scope, so the comparison stays honest

Naval combat and full-motion-video briefings are GDD amendments needing Producer
sign-off, not tickets. Crates and a map editor are GDD-silent and need the same.
None is pursued under the current directive, and none is counted as a gap above
except A3, which is recorded only because skirmish-08 made its cost visible.

## Hash impact

Tier A and Tier B items are sim changes and every one of them moves goldens;
each needs its own ADR and a regeneration under the doc 23 section 6 discipline.
Tier C is largely `/data` and moves goldens through the catalogue. D2 is a sim
change. Nothing here is hash-neutral, which is itself the argument for
sequencing this work rather than picking it up piecemeal.

D1 was the exception and it was not predicted: missions 04 to 06 landed with all
24 goldens byte-identical and the catalogue checksum unmoved, because
`MissionRunner` state has always lived outside the world hash and the row added
no unit or building. The claim above that "nothing here is hash-neutral" was
right about every row it was written for and wrong about this one, which is
worth leaving visible rather than quietly editing away.

## Changed / Assumed / Needed next

**Changed.** This document, rewritten against the current tree.

**Assumed.** Nothing. Every count is from the roster or the sim's enums.

**Needed next, and from whom.** The ranked plan this analysis sets as the goal
lives in `docs/tickets/P7-parity-tracker.md`. Three decisions there are Luke's
and gate the rest: whether B1's faction-locked turret is a defect to fix now (it
reads as one), whether the air layer or transports lead Tier A, and whether
D2's player-count promise is kept or the GDD amended to match what shipped.
