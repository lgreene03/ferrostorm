# ADR-019: the repair vehicle - mobile field repair reusing the depot loop

- Status: Ratified (Architect + sim-engineer + game-designer drafted 2026-07-24;
  ratified the same day under Luke's directive to implement out the C-series,
  the standing-directive ratification pattern the P6 tracker records for the B
  and C waves; Balance under A11 for the stat block)
- Date: 2026-07-24
- Deciders: Architect + sim-engineer + game-designer + Luke, Balance under A11
- GDD/TDD feature served: GDD line 62 (the repair vehicle); P6 campaign tracker
  wave C2

## Context

The C series wants a repair vehicle, a classic RTS support unit that mends
friendly forces in the field. The design question the C2 row poses is whether
healing units is new sim behaviour, which would be a golden move, or reuses
something already shipping.

The code read settles it: healing friendly units already exists and is golden
covered. The Service Depot (EntityKind.ServiceDepot, World.cs ProductionSystem)
mends its owner's damaged units and harvesters within radius 4 for 2 HP per tick
at 1 credit per unit per tick, gated on base power. The loop reads only the
healer's own position, assumes nothing about the healer being a structure or
stationary, and is friendly-only by construction. It is the exact mechanic a
repair vehicle needs, already written, tested and hashed. Two other HP-raising
sites exist (elite veterancy self-repair, structure self-repair) but neither is a
targeted friendly heal. There is no negative-damage weapon path anywhere.

## Decision

The repair vehicle is EntityKind.Unit with a new unit type id 13, unarmed,
carrying no new per-entity state. Its behaviour is the Service Depot heal loop
run as a moving aura, added as a branch in ProductionSystem gated on
`e.Kind == EntityKind.Unit && e.UnitType == RepairVehicleType`, reusing the
depot's constants (radius `DepotRepairRadiusCells` = 4, 2 HP per tick, 1 credit
per unit per tick, stop when broke). Three deliberate departures from the depot,
each a design decision this ADR records:

1. **Not power-gated.** The depot skips its heal while supply is below draw; the
   repair vehicle does not. A field-repair unit that only works under base power
   coverage is pointless, because the whole reason to build a mobile healer is to
   sustain an army fighting away from the base. Its cost is its build price, its
   fragility (unarmed, light armour) and the per-unit credit drain, not a power
   tether.

2. **Repairs others, not itself.** The loop excludes the healer's own index
   (`u == i`), so a repair vehicle cannot mend itself. It needs a depot, another
   repair vehicle, or a retreat, which is the classic medic constraint and keeps
   a lone healer from being self-sustaining under fire.

3. **Mobile units only, as the depot has it.** The kind gate stays Unit or
   Harvester, so the vehicle mends vehicles and infantry and harvesters but not
   structures, which keep their own Repair toggle. This keeps the mechanic
   distinct from structure repair and the loop byte-identical to the depot's.

The rate and radius are hardcoded, reusing the depot's own constants, exactly as
the depot hardcodes them today. No /data schema key for repair power, no new
`Entity` field. The unit's stat block (cost, HP, speed, prerequisites) is /data
like every unit, in `data/units/com_repair_vehicle.yaml` with a compiled twin the
selftest round-trips: common faction, unarmed, produced at the factory, prereq
the Service Depot (so field repair unlocks behind the repair building it extends).

## Alternatives rejected

**A negative-damage heal weapon reusing CombatSystem.** Damage is a signed int
so a negative value would heal, but the acquire scan selects only enemies
(`t.PlayerId == e.PlayerId` is a continue), and the fire path assumes hostility
throughout (kill credit, Fired events, cloak reveal on fire, the death and
harvester-flee checks in the damage pass). Reusing it means an inverted predicate
in the hottest, most golden-sensitive loop in the sim for no gain over the
self-contained depot loop. Rejected.

**A per-unit hashed repair-power field so different vehicles heal at different
rates.** This would append a hashed `Entity` field, moving all 24 goldens
mechanically and bumping the save to v8, for a feature with exactly one healer
type and no design need for variable rates. It is machinery ahead of the
requirement (the ADR-015 discipline). If Balance ever wants variable rates it is
a future golden-move ADR; today one hardcoded constant, the depot's, keeps the
wave hash-neutral. Rejected.

**A new EntityKind for the repair vehicle.** Unnecessary: the depot heal loop
already handles EntityKind.Unit targets, and making the healer an ordinary Unit
means movement, selection, fog, combat and separation treat it correctly for
free, exactly as the MCV is a Unit (type 7) rather than its own kind. Rejected.

## Consequences

Easier: GDD line 62's repair vehicle exists; an army can be sustained in the
field; the mechanic reuses a proven, tested, hashed loop rather than inventing
one; the client gets a new buildable in the VEHICLES tab over the existing
production path with no new command.

Harder, or rather owed: the bespoke .glb model and sidebar icon do not exist yet.
The client falls back to a stand-in (the MCV model, a vehicle silhouette, over
the rifle-squad default) and the icon guard tolerates the missing sprite, the
same interim the barracks and radar uplink already use. Both are owed to
art-pipeline. Balance owns the stat block under A11.

Hash impact: NEUTRAL. No new EntityKind, no new hashed Entity field, no schema
change. The heal branch fires only for unit type 13, and no golden scenario at
seed 2026 spawns a type-13 unit, so existing entities never enter the branch and
execution is byte-identical: all 24 golden hashes stay put and the save format
stays v7. The catalogue checksum (ADR-006) changes because a unit was added, but
nothing pins it to a fixed value (catrefuse asserts /data and compiled agree, and
they still do), and no golden state hash reads it. This is the same neutralisation
ADR-015 relied on, without even the hashed-field append that moved its goldens.

Gates: the wave adds a RepairGate to the battery (additive, a standalone mode and
a Match stage, never a golden scenario, so the golden list stays 24). It proves,
all at exit 0: a repair vehicle mends a damaged friendly unit at 2 HP per tick
for 1 credit per tick and stops when whole; it mends a harvester too; it does NOT
mend an enemy unit, does NOT mend itself, and does NOT mend a structure; it heals
with no power infrastructure present at all (the not-power-gated decision, which
a depot in the same world would fail); and it stops healing the tick the treasury
empties. The full battery, five-seed determinism suite and LAN soak stay green and
the goldens stay byte-identical.
