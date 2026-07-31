# P7 parity tracker: close the gap to the benchmark games

Authority: Luke, 2026-07-30, "update that documentation and set that as a goal",
against the rewritten parity analysis in docs/design/24-classic-parity-roadmap.md.
That document is the ANALYSIS; this file is the PLAN, and it is the resume point
if a session dies.

**This phase is not like P6.** Every P6 wave could be made hash-neutral or
carried a single sanctioned regeneration, so waves could be picked up in any
order. Nothing in P7 is hash-neutral: every row below is a sim or catalogue
change that moves goldens. That is the argument for sequencing rather than
picking items off, and it is why the ordering column exists.

Standing law per wave, unchanged from P6: full battery exit 0, goldens measured
and not assumed, an ADR where a hash moves, a gate that proves the behaviour,
tracker updated, PR with green CI on both platforms.

## Ordering principle

Rows are ordered by PLAY IMPACT per unit of work, not by list position in doc 24.
A defect that makes one faction unable to defend outranks a feature nobody has
asked for. Whole missing systems outrank roster breadth, because a missing
system removes a category of decision while a missing unit removes an option.

| # | Row | Doc 24 | Blocked on | Hash | Status |
|---|-----|--------|-----------|------|--------|
| P7-1 | A building's faction comes from /data instead of a hardcoded name (the reported "Sodality cannot defend" was a WRONG premise: nothing enforced the field) | B1 | - | **NEUTRAL, measured** | **DONE** - factiongate; checksum and 24 goldens unmoved |
| P7-2 | Defensive variety: the Emplacement, the anti-infantry leg, so defence is a CHOICE rather than a ladder | B1 | - | goldens NEUTRAL; catalogue checksum MOVES (a new building changes it by construction) | **DONE** - emplacementgate; 32 ticks vs infantry against the turret's 91, 632 vs armour against its 143 |
| P7-2b | A distinctive defence per SIDE: the Directorate's Bastion and the Sodality's Shroud Nest, both from WRITTEN GDD s3 doctrine | B1/C | - (no new doctrine invented, so Q017 is untouched) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - factiondefencegate (4 stages incl. the cloak and its decloak-on-firing) |
| P7-3 | Transports: the Carrier, the first unit that exists to move OTHER units | A2 | - | goldens NEUTRAL; catalogue checksum MOVES; save format v9 | **DONE** - transportgate (6 stages incl. save round-trip and cargo dying with its carrier) |
| P7-4 | The air layer: Airfield, Strike Flyer, Flak Track | A1 | ADR-028 (ratified under the standing directive) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - airgate (5 stages, both halves of clause 4); no reload cycle and the AI does not fly, both stated in the ADR |
| P7-5 | Faction identity: DR-02/03/04 as one package rather than three tickets | C | **Q017 (Luke's roster call)** | MOVES | pending - **first open row, and it needs a human** |
| P7-6 | Storage and a credit ceiling (silo) | B2 | **PRODUCER: GDD-SILENT.** GDD s4 specifies the economy in full and never mentions storage, a cap or overflow, and a ceiling would change the "float at 2 refineries / 3 harvesters" intent it DOES specify. Same category as crates and the map editor | MOVES | **NOT TAKEN** - I put this row on the list treating it as mine; it is not |
| P7-7 | Infiltration: the Sodality's Infiltrator, from GDD s7's named roster | B5 | - (the unit is written; only the 20 per cent share is my call) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - infiltratorgate (4 stages incl. conservation and an engineer regression check) |
| P7-8 | More than two player seats | D2 | **Producer sign-off**: sim change to PlaceSkirmishStart plus multi-start maps | MOVES | pending |
| P7-9 | Campaign missions 4 to 6 | D1 | Q012/Q016 (win/loss semantics) first | data only, MOVES | pending |
| P7-10 | Wall tiers and gates | B7 | C6b: Luke must override ADR-005 clause 6 | MOVES | pending |
| P7-11 | Hero unit, mines, support infantry | B3/B4/B6 | Producer: roster additions | MOVES | pending |

Out of scope until a GDD amendment with Producer sign-off: naval, FMV briefings,
crates, a map editor. Recorded in doc 24 so the comparison stays honest.

## What P7-1 turned out to be

It was filed as a defect - the Sodality unable to build base defence - and the
premise was wrong. Nothing enforced `faction:` on a building at all: the field
was parsed, validated, and dropped, and the sim hardcoded one expression naming
the Veil. Both sides could always build the turret.

The real defect was better and is now fixed: authored data that did not drive
the runtime, the ADR-006 class. StructureTypeDef carries a Faction, the loader
passes what the file declares, the hardcoded predicate is gone, and the turret
and superweapon declare `common` - preserving what play always did rather than
silently taking a capability away. Neutral: catalogue checksum and all 24
goldens unmoved, because no golden scenario plays a Sodality commander building
a Directorate building.

Recorded because the lesson generalises: **a claim read off a data file is a
claim about the file, not about the game.** The duplicated-rule audit had
already filed the missing Faction column as the permanent fix; checking there
first would have caught the premise before it was written down.

Left undone deliberately: `dir_turret` and `dir_superweapon` keep their ids
although they are now `common`, which contradicts the repo's own prefix
convention. Renaming them cascades into art (art/png/dir_turret.png,
art/sprites/dir_turret.svg and the model library key), so it is a wave of its
own rather than a rider on this one.

## What this phase does NOT do

It does not chase the unit counts in doc 24's table. Thirteen units against
twenty or thirty is a real gap, but parity by headcount is the wrong target: the
benchmarks' rosters carried duplicated roles, and this project has spent P6
building things they never had. The goal is that no CATEGORY of decision is
missing, not that the lists are the same length.

## Prerequisites carried from P6

These block rows above and are not P7's to solve:

- **Q017**, the faction-identity sequencing question, blocks P7-5.
- **Q014**, the second-resource question, is unrelated to parity and stays in P6.
- **ADR-027**, the crowd-aware movement decision, blocks nothing here directly
  but distorts every AI-vs-AI measurement any of these rows would be judged by,
  so it should be answered before P7-2's balance work.
- **Q012/Q016**, win and loss semantics, block P7-9.

## Changed / Assumed / Needed next

**Changed.** New file: the plan doc 24's analysis asks for.

**Assumed.** That "set that as a goal" means the parity gap becomes the project's
next phase after P6's remaining decision-gated rows, not that P6 is abandoned.
P6's tracker stays authoritative for its own rows.

**Needed next, and from whom.** Luke, three decisions, in this order: is P7-1 a
defect or intent; does the air layer or transport lead Tier A; and is D2's
player-count promise kept or the GDD amended to match what shipped. The first
unblocks a small fix immediately; the other two decide the shape of the phase.
