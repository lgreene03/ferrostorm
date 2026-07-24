# P6 Wave C4b delivery notes: outposts on the maps, and the map gate

Closes the item C4 filed as owed: "tools + balance place outposts on the
skirmish maps". Until this wave the Outpost shipped under ADR-021 but NO map
placed one, so the whole income mechanic was unreachable in a real match, live
code that no player could ever see. NEUTRAL hash impact.

## Plan

labels: persona:p2 gdd:s4 phase:6 owner:tools + balance + qa

Two halves: place the outposts through the committed generators (hand-editing a
.fmap is forbidden by its own header), and build the map validation harness that
proves every committed map still loads and plays, which doc 18 Phase D asked for
and never got.

## Which maps, and why only two

- **skirmish-02 (Ironback Ridge)**: one pair, at (20,20) and its rotation image
  (74,42), one in each land, forward on the western lane.
- **skirmish-04 (Tarnwater Crossing)**: two pairs, at (66,40)/(124,86) on the
  western approach to the central ford (so holding the ford and holding its
  income are one decision) and (30,70)/(160,56) back in the southern lane as a
  quieter node an expansion picks up. A 192x128 theatre carries two.
- **skirmish-01 is deliberately UNTOUCHED**: it is the map the `skirmish` golden
  scenario loads, so an entity added to it moves that hash. Placing outposts
  there is a legitimate one-line golden regeneration and is left as its own
  deliberate wave rather than smuggled into a map pass.
- **skirmish-03 is deliberately UNTOUCHED**: it is the frozen look-dev reference
  whose camera constants must never move (ADR-013).

So the feature is reachable on half the skirmish maps at zero golden risk, which
is the honest split; the remaining half is a named, costed follow-up.

## The generator support (tools/mapgen.py)

`Canvas.outpost(ax, ay)` places an outpost and its rotation image, taking the
TOP-LEFT anchor of the 2x2 footprint (the anchor `World.SpawnOutpost` takes).
Outposts are held APART from the grid, because an outpost is an ENTITY, not
terrain: the sim blocks its own footprint when it spawns, so writing it into the
grid would block those cells twice and inflate the density. `emit` writes
`structure -1 13 <ax> <ay>` lines after the grid, the mission-map convention;
player -1 is neutral and the loader already parses a negative player, so there is
no map-format bump.

Placement asserts the footprint (and its mirror) is wholly open, outside every
base apron (a base must not start owning one) and clear of the load-bearing
crossings (a 2x2 outpost standing in a pass would part-seal it). `validate()`
gains an eighth check that blocks every outpost footprint and RE-RUNS the whole
reachability proof with them standing, so an outpost that walled off a field or
a lane fails the generator rather than a match.

**A real bug the fairness check caught, worth recording.** The first cut measured
the Chebyshev fairness profile from each start to the outpost ANCHOR, copying the
ferrite rule, and it failed. It was right to fail: ferrite is single cells and a
180-rotation maps a cell onto a cell, but the rotation of a 2x2 block maps its
top-left anchor onto the rotated block's BOTTOM-RIGHT, so anchor distances differ
by one between the starts even under perfect symmetry. The fix measures to the
footprint CENTRE, in doubled integers so it stays exact, which is what the sim
itself uses (`FootprintCentre`) and the only measure that rotates cleanly.

## The new gate: mapgate

The map validation harness doc 18 Phase D asked for, now owed twice over by
ADR-021. `MapLoader` THROWS on a struct type with no spawn arm, and NO golden
scenario loads skirmish-02 or skirmish-04, so an unguarded map file can break the
shipped game while every golden stays green. mapgate walks EVERY committed map in
sorted order (directory order must not leak into a gate), loads it with the /data
catalogue registered before tick 0 (ADR-006), places the real opening hand, and
plays 1500 ticks of AI-vs-AI on it. It asserts the map loads, does not throw
in play, produced something (so the AI can actually play it), and that every
outpost the map declares stands and is still NEUTRAL, since no AI captures one
yet. That last assertion is the end-to-end proof of the MAP path that outpostgate
cannot give, because outpostgate spawns its outposts directly.

Additive, standalone mode plus a Match battery stage, never a golden scenario, so
the golden list stays 24.

## Verification (local, real evidence)

- Both generators re-run clean: every symmetry, density, reachability, crossing
  and fairness check passes, INCLUDING the new reachability-with-outposts-
  standing proof. Terrain is provably untouched: skirmish-02 stays 594 blocked
  (9.67%) and skirmish-04 stays 2390 (9.72%), the figures the map-redesign wave
  recorded, and `git diff data/maps` is SIX ADDED LINES and nothing else.
- mapgate exit 0: all 4 maps load and play, 6 neutral outposts stood.
- Full battery `match 2026` exit 0 with mapgate in it.
- The exact CI golden check BYTE-IDENTICAL across all 24 rows (skirmish-01 and
  skirmish-03 were not touched).
- Both Godot client builds 0 warnings (Debug and ExportRelease).

## Changed / Assumed / Needed next

**Changed.** tools/mapgen.py (the outpost mutator, the eighth validate check, the
structure lines in emit, the report line), tools/gen_skirmish_02.py and
gen_skirmish_04.py (the placements), the two regenerated .fmap files, and a
MapGate in the runner plus its battery and mode wiring.

**Assumed.** One pair on a 96x64 map and two on a 192x128 one is the right
density of income nodes; the specific cells are a first pass. Balance and the
Game Designer own both under A11, and moving one is a generator edit plus a
regenerate, with the fairness and reachability proofs re-run for free.

**Needed next (from whom).** Balance + game-designer: play them and move them if
they sit wrong (that is a taste call no gate can make). tools: the deliberate
one-line golden regeneration if outposts are wanted on skirmish-01. ai-engineer:
the AI still ignores outposts entirely, so today only a human ever captures one -
the single largest remaining gap in the mechanic, and mapgate's neutral-outpost
assertion is written so it will need updating the day the AI learns to capture.
