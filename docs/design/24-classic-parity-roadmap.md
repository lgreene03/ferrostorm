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
| Campaign missions | ~15 per side | ~14 per side | **3** |
| Simultaneous players | 4 | 8 | **2** |

The thirteen units are six shared (engineer, harvester, MCV, repair vehicle,
rifle squad, rocket squad), five Directorate and two Sodality. The thirteen
structures are ten shared, two Directorate and one Sodality.

## Tier A: whole systems that do not exist

These are not missing units. They are missing dimensions of play, and each
removes a category of decision rather than an item from a list.

**A1. The air layer.** No aircraft, no airfield or helipad, and therefore no
anti-air anywhere in the roster. Both benchmarks used air as a third dimension:
fast strike craft that ground defence cannot answer, and the anti-air building
that answers them. `EntityKind.Airfield` exists in the sim enum and the skirmish
AI already lists it among its wave targets, so the shape was anticipated and
never filled. Tracked as C5; needs an ADR and art.

**A2. Transport.** None of any kind - no armoured carrier, no transport
helicopter, no landing craft. This removes a family of play the benchmarks
leaned on: the engineer delivered under fire, the infantry force that arrives
somewhere unexpected, the retreat that saves an army. It is cheaper than A1
because it needs no new dimension, only a unit that carries.

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

P7-2 added the first of the missing legs: the Emplacement, anti-infantry,
cheap, and deliberately WORSE against armour than the turret is - measured at
32 ticks against a rifle squad where the turret takes 91, and 632 against
armour where the turret takes 143. Defence is now a choice rather than a
ladder. What remains true is the rest: ONE more type cannot express the
rock-paper-scissors the benchmarks built defence around, anti-infantry against
anti-armour against anti-air, and doc 27's balance work already measured that
static defence cannot hold. Neither side has a distinctive defensive structure.
That is P7-2, and it is breadth rather than a defect.

**B2. No storage, so the economy has no ceiling.** The benchmarks capped credits
and made silos a real decision, with overflow lost. There is no silo and no cap
here, so banking is free and there is never a reason not to hoard.

**B3. No support infantry.** No medic, no field mechanic, no scout animal. The
repair vehicle covers part of the mechanic's role; nothing covers the rest.

**B4. No hero unit.** Each benchmark had one, and it carried a disproportionate
share of that game's character.

**B5. No infiltration.** Ferrostorm has stealth and detection, which is half of
information warfare. It has nothing that steals, reveals, disables or denies: no
spy, no thief, no jamming structure. The Sodality's written identity is raiding
and economy denial, and the roster gives it no tool for the second half of that.

**B6. No mines or minelayer.**

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
superweapons) are the designed response, and all three are blocked on Q017.

## Tier D: content volume

**D1. Three campaign missions** against roughly fifteen per side in the
benchmarks, with the voice set still placeholder TTS pending the legal check.

**D2. Two player seats.** Every committed map declares exactly two starts and
`MapLoader.PlaceSkirmishStart` is hardcoded to two players, while GDD section 9
promises skirmish against one to seven opponents and custom lobbies up to four
against four, and the production plan lists 4v4 as in scope. This is the widest
single divergence between what ships and what is written down. It is a sim
change and needs Producer sign-off.

## Deliberately out of scope, so the comparison stays honest

Naval combat and full-motion-video briefings are GDD amendments needing Producer
sign-off, not tickets. Crates and a map editor are GDD-silent and need the same.
None is pursued under the current directive, and none is counted as a gap above
except A3, which is recorded only because skirmish-08 made its cost visible.

## Hash impact

Tier A and Tier B items are sim changes and every one of them moves goldens;
each needs its own ADR and a regeneration under the doc 23 section 6 discipline.
Tier C is largely `/data` and moves goldens through the catalogue. D1 is data and
triggers; D2 is a sim change. Nothing here is hash-neutral, which is itself the
argument for sequencing this work rather than picking it up piecemeal.

## Changed / Assumed / Needed next

**Changed.** This document, rewritten against the current tree.

**Assumed.** Nothing. Every count is from the roster or the sim's enums.

**Needed next, and from whom.** The ranked plan this analysis sets as the goal
lives in `docs/tickets/P7-parity-tracker.md`. Three decisions there are Luke's
and gate the rest: whether B1's faction-locked turret is a defect to fix now (it
reads as one), whether the air layer or transports lead Tier A, and whether
D2's player-count promise is kept or the GDD amended to match what shipped.
