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

DR-03 and DR-04 remain open, and Q017's sequencing question is now answered by
having taken its own first candidate.

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
