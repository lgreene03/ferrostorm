# ADR-037: a mine is a structure you place
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: doc 24 B6; P7-11c

## Context

Doc 24 B6 is one line: "No mines or minelayer." The word appears nowhere in the
GDD, in any ADR, or anywhere in `sim/`. This was refused three times as pure
invention, and the refusal named the reason precisely: the damage half was nearly
free, because splash, `ApplyAreaDamage` and the superweapon's countdown all
exist, and **the trigger was the entire feature**.

So the design below is mine. Alternatives are recorded beside each choice.

## Decision

### 1. A mine is a STRUCTURE you place, which is why almost nothing is new

The single decision that shapes the rest. Modelled as a structure, a mine
inherits the `BuildStructure` and `PlaceStructure` command path, ownership,
credit cost, prerequisites, the catalogue, the save format and
`reachabilitygate`'s coverage - which is why that gate's count moved from 14
buildable structure types to 15 without being told about mines.

Rejected: a new command type and a minelayer vehicle. It is the shape the
benchmarks used, and it would have meant a new command, a new unit ability path,
and a placement rule that duplicates the one `PlaceStructure` already enforces.

### 2. Hidden by the EXISTING stealth flag, so the counter is inherited

The mine sets the same `Stealth` flag a Phantom Tank sets. That is not a
shortcut, it is the point: GDD line 56 requires every stealth tool to have a
public counter, and reusing the flag means the detector rule already **is** that
counter. A Sentinel Scout reveals a minefield, and a revealed mine can be shot.

Rejected: a separate invisibility for mines. It would need its own counter
invented, and the one already written would not apply.

### 3. It does not block its footprint, and that is a correctness requirement

Every other structure calls `BlockFootprint`. A mine must not, and the reason is
sharper than "a mine is not a wall": **the flow field is shared ground truth**, so
a blocking mine would leak its own position to the enemy's pathing while
cloaked. An invisible thing that units visibly walk around is not invisible.

The implementation found a second consequence worth recording. `ValidPlacement`
skips structure cells as already-blocked, which a mine's are not, so a 2x2
building may legally be sited over a live mine. Had the death path unblocked the
cell on detonation, it would have cleared a cell that building occupies and units
would have pathed into a solid. So skipping BOTH the block and the unblock is
required for the map to stay coherent, not merely for symmetry.

`ValidPlacement` is deliberately left alone: refusing a placement over an enemy
mine would leak its position through the refusal, which is the same class of leak
as blocking.

### 4. Aircraft do not set it off, and this is the FOURTH path to need saying so

The first draft triggered on any enemy unit, so a Strike Flyer passing overhead
detonated a ground charge.

ADR-028 clause 2 says an aircraft is not on the ground: it ignores terrain,
blocks no cell, takes no part in separation. A buried charge is the same category
of thing it is not touching. And a mine that downs a flyer would be a second
anti-air answer, which ADR-028 clause 4 makes deliberately scarce.

**ADR-028 records its own first pass guarding two of three target-selection paths
and shooting an aircraft down with a rifle. This is the fourth path, and it was
missed again.** That is now three misses across four paths, which says the
omission is structural rather than careless: nothing in the codebase makes
"is this target airborne" a question a new path has to answer. Worth a future
row; noted rather than solved here.

### 5. Scan-then-apply, and consume before the blast

Both are determinism decisions and both are recorded in the code.

Apply-as-you-go lets a lower-indexed mine's blast kill the trigger a
higher-indexed one was waiting for, so whether the second fires would depend on
entity order. Marking every triggered mine dead before any damage lands is the
same argument one level down: a mine is a structure, and `ApplyAreaDamage` would
otherwise let mine A destroy mine B outright.

The gate asserts a walker between two mines loses 640, both charges in full,
which is what proves neither absorbed the other.

An untriggered mine caught in a blast dies without detonating. Proximity is the
only trigger; chain reactions are not a feature here.

### 6. The numbers, which are mine

Cost 400, trigger radius 1.5 cells, damage 400 through `ApplyAreaDamage` (so full
inside 1.5 and half out to 3), prerequisite the radar uplink, faction common,
`max_alive` 20.

The cap is not flavour: the trigger is a per-tick scan, and without a cap a
player can carpet a map and grow that cost without bound. Twenty is enough for
two or three approaches and not enough to fence a base.

Damage is a compiled constant rather than authored, and the reason is worth
recording because it looks like an inconsistency: authoring it as a weapon and
hanging it off `weapon_ids` would put a non-zero `WeaponId` on the entity, and
`CombatSystem` makes any armed structure auto-acquire and fire. The mine would
shoot people instead of waiting for them. It follows the superweapon's literal
and `DemolitionDamage` instead.

## Consequences

**All 24 goldens byte-identical, measured**: no golden places a mine and the
per-tick scan is guarded to cost nothing when none exists. The catalogue checksum
moves from `0x60DEB79B9DE8C0AD` to `0xCB3170B590433275`.

`StructureTypeDef` gains `MaxAlive`, mirroring the unit column added for the
hero, and the cap is enforced at BOTH the queue and the placement - P7-11b proved
a single point insufficient, and refusing only at placement would let a player at
the cap sink 400 credits into a ready slot that can never be spent.

**A latent trap found and NOT closed**: the sidebar's UNIT list is derived from
the catalogue, but its two STRUCTURE lists are still hand-kept arrays. The mine
needed a hand-added entry. That is the defect that made seven units unbuildable,
still live for buildings. It is guarded rather than fixed: the client harness
asserts every registered structure carries a button, so the next building fails
CI rather than shipping unreachable. Deriving the list properly is a row of its
own.

What this does NOT deliver: no bespoke art, the AI neither lays mines nor avoids
them, and nobody has played against one, so 400 credits and 400 damage are two
numbers chosen by argument.
