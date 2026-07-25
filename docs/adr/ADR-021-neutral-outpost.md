# ADR-021: the neutral capturable Outpost

- Status: Ratified (Architect + game-designer drafted 2026-07-24 under Luke's
  directive to implement out the C-series; READY TO IMPLEMENT, design complete,
  not yet built at time of writing). Balance under A11 for the income rate.
  **BUILT AND SHIPPED 2026-07-25** across C4 (the sim mechanic), C4b (placed on
  skirmish-02 and -04), C4c (the AI captures them) and the VERDICT wave (the
  captured outpost is repainted for its new owner).
- Date: 2026-07-24
- Deciders: Architect + game-designer + Luke, Balance under A11
- GDD/TDD feature served: GDD line 41 (capturable neutral income structures);
  doc 22 P5-ECON-14; P6 campaign tracker wave C4

## Context

GDD section 4 line 41: "Secondary income: Capturable neutral 'Depot' structures
on the map grant +15 credits/tick, the map-control incentive, replacing oil
derricks in spirit." The name "Depot" is already spent (struct type 8
com_service_depot is EntityKind.ServiceDepot, a repair building), so doc 22's
P5-ECON-14 reserved EntityKind.Outpost = 17 for the income building. That enum
value exists (World.cs:49) but is entirely inert: no spawn, no system, no
catalogue entry, no map arm.

The code read (C4 design pass) establishes that this feature is hash-NEUTRAL, not
a golden regeneration as the tracker guessed:

- Capture already accepts neutral owners: CaptureSystem (World.cs:1752) excludes
  only `t.PlayerId == e.PlayerId`, which a neutral structure (PlayerId -1, the
  FerriteField convention) passes. The only gap is that IsStructure (World.cs:1412)
  does not list Outpost, so an outpost is not yet a capturable structure.
- The income reuses the existing `_credits` pool, which is already hashed
  (World.cs:2833). No new hashed Entity field is needed.
- No golden scenario or map spawns a neutral structure or an Outpost (the six
  .fmap files place only player 0/1 structures of types 1/2/3/5; the in-process
  scenarios spawn only player-owned entities plus FerriteFields). So every new
  Kind==Outpost branch is dead code in all 24 goldens, exactly like the ADR-019
  repair-vehicle branch, and the 24 goldens stay byte-identical.

## Decision

Add a neutral, map-placed, capturable Outpost that grants its owner passive credit
income, hash-neutral, proven by an additive OutpostGate (not a golden regeneration).

1. **A new structure type 13 (com_outpost), EntityKind.Outpost.** Struct type 13
   is the next free struct-type id (MaxStructType 12 -> 13; DefaultStructureType
   and StructureCatalogue.TypeIdOf/KindOf gain it), with data/buildings/com_outpost.yaml
   and the compiled twin the selftest round-trips, per ADR-006. Cost 0 and
   unbuildable (map-placed only): it never appears in the sidebar and no unit names
   it as a prerequisite. PowerDraw 0, so a captured income building never browns
   out the grid it funds. A modest Hp and a small Sight (map-control vision as a
   minor bonus alongside the income).

2. **IsStructure gains Outpost**, which makes it capturable through the existing
   engineer CaptureSystem with no capture-code change. This edit only changes the
   truth table for Outpost inputs, which no golden entity is, so it is hash-neutral.

3. **Income branch in ProductionSystem**, gated `e.Kind == EntityKind.Outpost`:
   while owned (`PlayerId >= 0`), grant `OutpostIncomePerTick` to the owner on the
   per-second tick (`Tick % TicksPerSecond == 0`), then `continue`. Guarded on
   PlayerId >= 0 so a neutral (uncaptured) outpost pays nothing and never indexes
   `_credits[-1]`. Rate: the GDD's "+15 credits/tick" at 15 Hz is 225/s (about ten
   harvesters), which doc 22 flags as "do not ship"; the ratified rate is 15 per
   SECOND (`Tick % 15 == 0`, +15), a real but not game-warping trickle, Balance to
   confirm under A11.

4. **VictorySystem excludes Outpost from hope**, the barrier precedent
   (World.cs:2737): a player whose only remaining building is a captured outpost is
   still eliminated, because an outpost is an income node, not a base. Hash-neutral
   (no golden has an outpost).

5. **MapLoader spawns it** via the existing `structure <player> <type> <x> <y>
   <tag>` line with player -1 and type 13; the line parser already accepts a
   negative player (int.Parse, no lower bound), so NO map-format bump and no new
   grammar (the reason P5-ECON-14 was chosen over BD-22's new grid char). Only the
   BuildWorld spawn switch gains an Outpost arm and the header comment a line.

6. **Client (presentation only).** ModelLibrary.KindModel gains kind 17 (interim:
   com_service_depot or com_refinery, a structure silhouette, over the power-plant
   default) and a struct-name label; a neutral owner renders with no team colour.
   Bespoke model owed to art-pipeline.

## Alternatives rejected

**Vision/radar instead of income.** Cheaper still and field-free, but a weaker
map-control incentive than the GDD's explicit income line, and income is what the
GDD asks for. Vision rides along free anyway via the small Sight.

**A forward production or rally point.** Needs IsProducer membership, a queue and
rally plumbing; much larger and off-spec.

**A new map grid character and format bump (BD-22).** P5-ECON-14 reuses the
existing structure line with a negative player, so no format version bump; BD-22's
new `D` char and v3 format are unnecessary. Rejected.

**A golden scenario with a 25th committed golden row (P5-ECON-14's literal
acceptance).** A cross-platform golden row would pin the outpost's determinism, but
it edits golden-hashes.txt. To keep the wave maximally hash-safe (the existing 24
byte-identical, no golden-file change at all) the behaviour is proven by an additive
OutpostGate instead, the RepairGate precedent. The mechanic is pure integer credit
addition and already-golden-covered capture, so its determinism is not in doubt; a
cross-platform golden row is a low-risk follow-up if later wanted.

## Consequences

Easier: GDD line 41's secondary-income map-control incentive exists; capture gains
a neutral target for free; the income and vision reuse existing pools and systems.

Harder, or owed: the bespoke com_outpost model and icon (interim is a stand-in
structure model); Balance confirms the 15/second rate under A11.

Hash impact: NEUTRAL. EntityKind.Outpost, all new branches Kind==Outpost gated,
income into the already-hashed `_credits`, no new hashed Entity field, and no
golden scenario or map spawns an outpost. All 24 golden hashes stay byte-identical;
the save format stays v7. Proven by an additive OutpostGate (capture flips a
neutral outpost to the capturer, an owned outpost trickles income at the rate, a
neutral one pays nothing, and a player left only a captured outpost is still
eliminated), never a golden regeneration.

Gates: an OutpostGate (additive, standalone mode + Match stage, never a golden
scenario, golden list stays 24). Machine check: the /data round-trip selftest
("all N compiled structure defs"), the full battery exit 0, the 24 goldens
byte-identical, both client builds clean. Needs a human: the income rate feel
(Balance, A11) and the bespoke model (art-pipeline).
