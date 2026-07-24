# P6 Wave C2 delivery notes: the repair vehicle

Closes the C2 row of the P6 campaign tracker under ADR-019 (ratified), which adds
a mobile field-repair unit (GDD line 62). NEUTRAL hash impact: no golden
regeneration, no save-format bump. Plan comment first (CLAUDE.md workflow rule 2),
delivery notes and the standard footer at the end.

## Plan

labels: persona:p2 gdd:s6 phase:6 owner:sim-engineer + client-engineer +
game-designer (Balance under A11 for the stat block)

The design pass (ADR-019) found that healing friendly units already exists and is
golden covered: the Service Depot mends own units and harvesters in radius 4 for
2 HP per tick at 1 credit per unit per tick, gated on power. A repair vehicle is
the MOBILE version of that mechanic, so the honest build reuses the depot loop
rather than inventing a heal system. That keeps the wave hash-neutral: the new
behaviour fires only for a new unit type id, which no golden scenario spawns.

## The repair vehicle as built

**A new unit type, not a new kind.** Unit type 13 (`World.RepairVehicleType`),
EntityKind.Unit, unarmed, exactly as the MCV is a Unit (type 7) with special
behaviour keyed on its id. Common faction, produced at the factory, prereq the
Service Depot (struct type 8), so field repair unlocks behind the repair building
it extends. Stat block in `data/units/com_repair_vehicle.yaml` with a compiled
twin the selftest round-trips (now "all 13 compiled unit defs").

**The heal is the depot loop as a moving aura.** A branch in ProductionSystem,
gated on `e.Kind == EntityKind.Unit && e.UnitType == RepairVehicleType`, runs the
depot's heal loop with the depot's own constants (radius 4, 2 HP/tick, 1
credit/unit/tick, halt when broke). Three deliberate departures from the depot,
each an ADR-019 decision:

- **Not power-gated.** A field repairer must work away from base power, so the
  depot's `supply < draw` skip is dropped. Its cost is its build price, its
  fragility and the per-unit credit drain.
- **Excludes itself** (`u == i`): it mends others, not itself.
- **Mobile units only** (Unit or Harvester), like the depot; structures keep
  their own Repair toggle.

**The client (presentation only).** The repair vehicle joins the sidebar's
VEHICLES tab (tab membership derives from produced_at). No bespoke model or icon
exists yet: it takes the MCV model as its interim (a vehicle silhouette, over the
rifle-squad default), and the sidebar icon guard tolerates the uncut sprite, the
same interim the barracks and radar uplink already use. Both owed to art-pipeline.

## Why the goldens do not move

The heal branch fires only for unit type 13, and no golden scenario at seed 2026
spawns a type-13 unit, so every existing entity skips the branch and execution is
byte-identical. No new EntityKind, no new hashed Entity field, no schema key, so
there is nothing to append to the hash or the save. Save format stays v7. The
catalogue checksum (ADR-006) changes because a unit was added, but nothing pins it
to a fixed value (catrefuse asserts /data and compiled agree, and they still do)
and no golden state hash reads it. Proven below by the byte-identical golden run.

## The new gate

RepairGate joins the battery (additive, a standalone mode and a Match stage, never
a golden scenario, so the golden list stays 24). It proves, all at exit 0:

- A repair vehicle fully mends a friendly unit at 2 HP/tick for exactly 1
  credit/tick, in a world with NO power infrastructure at all (the not-power-gated
  decision; a Service Depot in the same setup would heal nothing).
- It does NOT mend itself (a damaged medic stays damaged), does NOT mend an enemy,
  and does NOT mend a structure (proven at zero distance, so it is the kind gate,
  not range).
- It mends a harvester to full.
- 6 credits buy exactly 6 heal-ticks (+12 HP) then healing stops, treasury at 0.

## Verification (local, real evidence)

- Full battery `match 2026` exit 0 (selftest with the 13-unit round-trip,
  determinism 24/24, every scenario assertion, defence load, catrefuse, spawngate,
  prodgate, regrowthgate, stancegate, repairgate, lan 5/5).
- The exact CI golden check byte-identical: `golden 2026` diffed against the
  committed sim/golden-hashes.txt minus comments, identical across all 24 rows.
- LAN soak hashes unchanged from the C1b baseline (the LAN worlds spawn no type-13
  unit), confirming no behaviour drift.
- Both Godot client builds 0 warnings (Debug and ExportRelease).

## Changed / Assumed / Needed next

**Changed.** Sim: `World.RepairVehicleType = 13`, the compiled `_unitTypes[13]`
def, the ProductionSystem heal branch, and `UnitCatalogue.TypeIdOf` gains
com_repair_vehicle. Data: `data/units/com_repair_vehicle.yaml`. Client:
Sidebar.Units gains the repair vehicle, ModelLibrary maps type 13 to the MCV
interim. Runner: RepairGate plus its battery and mode wiring. Docs: ADR-019, this
file, the tracker and ledger.

**Assumed.** The stat block (cost 700, hp 300, speed 0.20, prereq depot) is a
sensible support unit; Balance owns it under A11. The heal rate and radius reuse
the depot's constants; promoting them to per-unit /data would be a future
golden-move ADR only if variable rates are ever wanted (the ADR-015 discipline).

**Needed next (from whom).** art-pipeline: the bespoke com_repair_vehicle .glb
model and sidebar icon (interim is the MCV model and no icon). Balance: tune the
stat block under A11. The AI does not build the repair vehicle yet (like stances,
the game is unbroken); the ai-engineer may teach it to through the same Produce
command with no format change.
