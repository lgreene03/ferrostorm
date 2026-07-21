# TICKET-P6-MAP-01: Skirmish map redesign (winding rivers, ridges, decision-forcing economy)

Labels: `persona:game-designer` `persona:tools` `gdd:02` `phase:6` `owner:tools`

Branch: `ticket/p6-map-redesign`
Spec: the owner's directive (2026-07-20), design standard doc 26, authority ADR-013.

## Plan comment, written before starting

**Approach.** Fix how the skirmish maps look and play: winding rivers with a few
bridge chokepoints instead of straight block barriers; terrain that carves the
open field into lanes and gives each base a defensible alcove; a safe home
ferrite patch plus a larger contested patch so expansion is a decision. Redesign
the three straight/open maps (skirmish-01, 02, 04) with committed Python
generators that prove 180-degree rotation symmetry, reachability, load-bearing
crossings, ferrite fairness and 8 to 10 per cent density. Preserve skirmish-03: it
already meets the standard and is the frozen look-dev reference. Prove each map
with a full AI-vs-AI match before committing, because a chokepoint the flow field
cannot path parks the army and is a broken map, not a hard one.

**Assumptions.** 180-degree rotation symmetry is the fairness invariant on every
map (brief), overriding doc 22 MAP-02's mirror-axis note for skirmish-01. Ferrite
budgets follow convention (20 cells small, 60 big). Only the `skirmish` golden
loads a skirmish map, so only it moves; missions load their own files and must
not move.

**Interfaces touched.** `data/maps/skirmish-01,02,04.fmap` (regenerated);
`tools/mapgen.py`, `tools/gen_skirmish_01.py`, `tools/gen_skirmish_02.py`,
`tools/gen_skirmish_04.py` (rewritten), `tools/lookdev/map_preview.py` (new);
`sim/golden-hashes.txt` (one line); `sim/Ferrostorm.Sim.Runner/Program.cs` (the
selftest fingerprint of skirmish-01 only). No sim logic changed.

## What shipped

**The generator library.** `tools/mapgen.py` holds the invariant once: a `Canvas`
that writes every feature with its rotation image and a `validate()` that proves
symmetry cell by cell, open aprons, reachability from both starts, load-bearing
crossings (closing them disconnects the starts), Chebyshev ferrite fairness and
density. Winding rivers are a sine centred on the map centre, closed under
rotation so integer rounding cannot bias a bank.

**skirmish-01, Serpentine Ford (96x64).** The old straight Spine wall replaced by
a river winding through three fords, base alcoves in opposite corners, ridge
spurs splitting the near bank into a central lane and a southern flank, ruins and
fences on the ford approaches, a safe 4-cell patch by each base and a contested
6-cell patch beside the central ford. Census: 548 blocked (8.92 per cent), 20
ferrite. Starts moved to the rotation pair (9,9) and (86,54).

**skirmish-02, Ironback Ridge (96x64).** The old scattered blocks replaced by one
winding ridgeline running corner to corner along the main diagonal, pierced by
three passes (left flank, central saddle, right flank), with base alcoves, a
forward knoll per land, rubble at the pass mouths and the contested patch at the
saddle. Census: 594 blocked (9.67 per cent), 20 ferrite. The three passes are
proved load-bearing. Starts (10,53) and (85,10), the opposite diagonal to
skirmish-01 so the two do not feel the same.

**skirmish-04, Tarnwater Crossing (192x128).** The straight water column replaced
by a meandering Tarnwater, keeping the MAP-04 constraints: three bridges, bank
bluffs, midfield ruins, a 60-cell economy as twelve clusters of five, 9x9 aprons,
density 9.72 per cent. Starts unchanged at (12,20) and (179,107).

**skirmish-03.** Deliberately unchanged; see ADR-013 and doc 26.

**Preview tool.** `tools/lookdev/map_preview.py` renders any `.fmap` top-down with
Pillow, no Godot and no GPU, for reading layout. Before/after captures are in the
delivery scratch under `maps/`.

## Gates

- Full battery `~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c Release`: exit 0.
- `golden 2026` output byte-matches the committed `sim/golden-hashes.txt`.
- Golden diff before/after: a single line, `skirmish` (0xB7514D5EEA6DFFFD to
  0x165DE5ADA957AC53). Missions and every other scenario byte-identical.
- AI-vs-AI acceptance match on skirmish-01, 02 and 04, both faction matchups
  (both Directorate, and Directorate vs Sodality): PASS. Both sides build 8 to 9
  structures, keep a harvester, produce units, path across the crossings and
  fight (24 to 38 deaths per match). No crossing needed widening. skirmish-03 run
  as a control also passes.
- Each generator's own assertions pass on emit.
- Game project `game/Ferrostorm.Game.csproj` builds Debug and Release: 0 warnings.
- `git diff sim/`: `golden-hashes.txt` and the Program.cs selftest fingerprint only.

## Changed / Assumed / Needed next

**Changed.** skirmish-01, skirmish-02, skirmish-04 regenerated as winding-river,
ridge-and-passes and big-theatre maps. New generator library and three
generators; skirmish-04 generator rewritten. New top-down preview tool. One
golden hash (`skirmish`) and the skirmish-01 selftest fingerprint updated. Design
standard doc 26 and ADR-013 authored.

**Assumed.** Rotation symmetry over mirror on every map (brief over doc 22
MAP-02). Ferrite and density conventions as above. skirmish-03 preserved.

**Needed next, and from whom.** Architect + Luke: ratify ADR-013 (the `skirmish`
golden move). (DONE 2026-07-20: ADR-013 ratified and the skirmish map redesign shipped.) Art-pipeline + client-engineer: a taste pass on the running client,
since the previews prove layout, not lighting and materials. Optional later
ticket: bring skirmish-03 under the generator, which would require regenerating
the look-dev reference save and re-taking its reference captures.
