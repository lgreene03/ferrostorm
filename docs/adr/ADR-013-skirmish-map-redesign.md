# ADR-013: Skirmish map redesign and regeneration

- Status: Proposed (authored by tools + game-designer under the owner's directive
  of 2026-07-20; awaiting Architect + Luke ratification, per CLAUDE.md the golden
  move requires it)
- Date: 2026-07-20
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD (doc 02) pillars 2 and 4 (games resolve in 15 to 30
  minutes; the economy is the battlefield); TDD (doc 03) pathfinding and the map
  format; design standard doc 26.

## Context

The owner's directive, verbatim in spirit: the skirmish maps look wrong. Rivers
are straight blocks rather than winding, the land is fully open, and there is no
design that pushes a player to choose the best place to set up. Seen directly in
the files: `data/maps/skirmish-01.fmap` was a straight vertical wall of blocked
cells at x=46..49 with two gaps and three ferrite cells; `skirmish-02.fmap` was a
scatter of straight rectangular blocked blocks with a single central ferrite
pile; `skirmish-04.fmap` was a ruler-straight water column at x=94..97. Only
`skirmish-03.fmap` was already authored with winding water, hills, ruins, fences
and bridges.

Two facts make this an ADR rather than a data edit. First, `skirmish-01.fmap` is
loaded by the gated `skirmish` golden scenario (Program.cs BuildSkirmishWorld),
so changing one of its cells changes that scenario's state hash, and per
CLAUDE.md changing a golden hash is a replay-compatibility break that requires an
ADR and Architect sign-off. Second, doc 22's MAP-02 note recorded that
skirmish-01's fair axis was the mirror x to 94-x because its starts shared a row;
this redesign instead adopts 180-degree rotation symmetry on every map, matching
the fairness invariant `tools/gen_skirmish_04.py` already enforces, which moves
skirmish-01's starts to a rotation pair. That is a deliberate reversal of the
earlier note and needs to be recorded.

## Decision

Redesign skirmish-01, skirmish-02 and skirmish-04 to the standard in doc 26, each
produced by a committed Python generator under a shared library
(`tools/mapgen.py`, `tools/gen_skirmish_01.py`, `tools/gen_skirmish_02.py`, and a
rewritten `tools/gen_skirmish_04.py`), and regenerate their `.fmap` files once
under this ADR's authority. Preserve skirmish-03 unchanged, because it already
meets the standard and is the frozen look-dev reference whose camera constants
and committed reference save are tuned to its exact geography.

The redesign is bound by these constraints, all proved in the generator before a
file is emitted:

1. 180-degree rotation symmetry about the map centre, cell by cell, for blocked
   cells, ferrite and bridges. Each start is a rotation image of the other.
2. A 9x9 open apron at each start so the Construction Yard and MCV fit.
3. Reachability: both starts reach every ferrite patch, the far start and every
   apron cell over the passable cells with bridges open, four-connected, the
   conservative model of the sim's flow field.
4. Load-bearing crossings: closing every bridge or pass disconnects the two
   starts, so a crossing is a decision, not decoration.
5. Ferrite fairness: identical Chebyshev distance profiles from each start; 20
   cells on the 96x64 maps, 60 on the 192x128 map.
6. Terrain density inside 8 to 10 percent.
7. The AI can still play: a full AI-vs-AI match on each map, in both faction
   matchups, in which both commanders build, harvest, produce, path across the
   crossings and fight to a result. A map where an army parks is widened until it
   flows.

## Alternatives rejected

**Hand-edit the map files.** Rejected because the fairness invariant is
mechanical over thousands of characters and a human cannot hold it. The whole
reason MAP-04 was generated rather than typed applies to every map, so the same
discipline is extended rather than abandoned.

**Redesign skirmish-03 as well, for a uniform four-map set.** Rejected because
skirmish-03 is the only committed map exercising the whole terrain vocabulary and
is the reference the look-dev harness is frozen against; its camera constants are
marked never to change and its reference save encodes a match on its exact
terrain. Redesigning it would break that harness and re-taking every reference
capture for no design gain, since it already meets the standard. It is kept as
the resource-contest member of the set and doc 26 records the option of bringing
it under the generator in a later ticket if visual uniformity is wanted.

**Keep skirmish-01's mirror axis to avoid moving its starts.** Rejected because
the brief makes 180-degree rotation the invariant on every map, and a horizontal
start pair cannot be rotation-symmetric on an even-height grid. Adopting rotation
uniformly is cleaner than carrying one map on a different fairness rule, and the
selftest fingerprint that pinned the old starts is updated in the same change.

## Consequences

**Easier.** Every map is now reproducible from a reviewed generator that proves
its own fairness, so a future edit is a script change with a proof attached
rather than a hand-edit taken on trust. The maps read as designed terrain and
give a reason to prefer one piece of ground over another, which is what the brief
asked for. A new `tools/lookdev/map_preview.py` renders any `.fmap` top-down
without Godot, so a layout can be looked at without the GPU.

**Harder.** skirmish-01's replay compatibility is broken once, so any replay or
saved match recorded against the old skirmish-01 will not reproduce. This is the
accepted cost of the regeneration and is confined to this one scenario.

**Hash impact.** This ADR authorises exactly one regeneration. It MOVES one
golden hash, `skirmish`, the only golden that loads a skirmish map. Regenerated
value: `0x165DE5ADA957AC53` (was `0xB7514D5EEA6DFFFD`). Every other scenario is
byte-identical, proven by regenerating the full golden set and diffing: the diff
is a single line. In particular skirmish-02 and skirmish-04 are loaded by no
golden scenario, so their redesign moves nothing, and the three mission goldens
(`mission`, `mission02`, `mission03`) and `capture` are untouched because they
load `data/missions/*.fmap`, which this change does not touch. The diff was
explained scenario by scenario in the delivery notes per the doc 23 section 6
discipline; a scenario whose map did not change did not move.

**Justified sim-code edit beyond the golden file.** The selftest in Program.cs
carried a byte-content fingerprint of skirmish-01 (248 blocked cells, three
ferrite fields including one at (47,31), starts (8,30) and (86,30), a specific
blocked-and-open probe pair). Those assertions are a fingerprint of the exact map
this ADR redesigns, so they are updated to the new census (548 blocked cells, 20
ferrite fields including one at (17,19), starts (9,9) and (86,54), probe on the
base-0 shoulder hill). This is the only sim-code change; `git diff sim/` shows it
and `golden-hashes.txt` and nothing else.

## Gates

The implementing wave must show, and did show: the full battery
`~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c Release` exits 0
against the regenerated goldens; the `golden` output byte-matches the committed
`golden-hashes.txt`; the golden diff is the single `skirmish` line; each
generator's own symmetry, reachability, crossing, fairness and density
assertions pass; an AI-vs-AI match on each redesigned map passes in both faction
matchups; and the game project builds in Debug and Release with zero warnings.
Cross-platform byte-for-byte determinism is proved by the determinism CI on
Windows and Linux.
