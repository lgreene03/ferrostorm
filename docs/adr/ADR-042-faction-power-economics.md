# ADR-042: the two sides stop sharing a power grid, and a prerequisite becomes a capability
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD s3 (both factions' identity mechanics), GDD pillar 3; doc 24 C9; doc 27 DR-02; P7-5, and the first part of Q017

## Context

Q017 asks which identity step comes first, and records the honest measurement
that made it urgent: **11 of 12 structures common, both sides building the same
base, mining the same fields and firing the same superweapon on the same timer.**
The GDD's third pillar promises factions that "differ in *how they think*", and
the two economies were byte-identical.

DR-02 is the candidate the question itself calls "the most thematic option and
the one the GDD already designed in prose", and that is not a flourish. GDD s3
writes both halves:

> Directorate: power grid is **centralised** (fewer, bigger power plants =
> juicier targets)
>
> Sodality: **decentralised power** (many small generators)

So nothing about the shape of this row is invented. Only the numbers are mine.

## Decision

### 1. Each side gets its own plant, and the Directorate's numbers do not move

`com_power_plant` (type 1) becomes Directorate-only. **Not one of its numbers
changed**, because 100 supply behind 150 hit points already *was* "fewer, bigger,
juicier": the whole base on one fragile building. The row did not need a
rebalance of that side, it needed the other side to stop sharing it.

`sod_generator` (type 20) is new, and every number is set **against** the plant
rather than chosen on its own, because the identity is the comparison. Three
generators are the unit of comparison, being what it takes to beat one plant:

| | Directorate, 1 plant | Sodality, 3 generators |
|---|---|---|
| supply | 100 | 120 |
| credits | 300 | 390 |
| credits per power | **3.00** | 3.25 |
| total hit points | 150 | **210** |
| build ticks | **100** | 135 |
| footprint | 2x2 | 3 x 1x1 |
| sight | 4 | 3 |

**The trade has to run both ways or it is not a trade.** Centralised buys power
more cheaply per credit, which is the upside "bigger" must earn; decentralised
takes more total damage to remove, which is the upside "many" must earn. The gate
asserts both directions as ratios rather than literals, so a balance pass can
move the numbers without silently turning one side's identity into a reskin.

Sight 3 against 4 is the one deliberate anti-synergy: a cheap generator that saw
as far as the plant would be a cheap watchtower, which is the single axis on
which this could have been strictly better rather than a choice.

### 2. A prerequisite is a CAPABILITY, and the row is impossible without it

This is the part worth keeping, and it is the enabling change rather than a tidy-up.

`HasPrereqs` asked `o.StructType == ids[r]` - **an instance**. Five prerequisites
in the tree name type 1. So the moment the sides stopped sharing type 1, a
Sodality player holding three generators would satisfy **no prerequisite in the
game** and could build a generator and nothing else, ever. Not a balance problem:
a dead end, and the row cannot ship without answering it.

A prerequisite is now satisfied by any owned structure of the same **Kind** as
the type named, and the authored id is an *exemplar of the capability* rather
than the only thing that provides it. "You need a power plant", not "you need
building number one".

That is the same correction P7 has now made about a dozen times - read a rule as
the property it means rather than the instance it names - and it is measured
hash-neutral, because no two structure types share a Kind that anything requires
today. The one Kind with two types is Emplacement (15 and 18), which nothing
takes as a prerequisite. When something does, the Shroud Nest satisfying an
Emplacement requirement is the intended reading rather than a leak, and that is
recorded here so it is a decision rather than a surprise.

### 3. Three defects found on the way, two of them latent and one live

**The placement switch was keyed on `EntityKind`, and Kind stopped identifying a
building.** A Sodality player who ordered a generator got a **Directorate plant**,
at the generator's price, silently. `EntityKind.Emplacement` has carried the same
collision since P7-2b and answers it by asking `c.AuxId`; the power plant case
did not. Found by `reachabilitygate`, not by reading, and pinned by a gate stage
proved to bite.

**`StructureTypeDef.Faction` was not in `CatalogueChecksum`.** Its unit twin has
been folded since the roster existed; this one was never added when the field
arrived in P7-1, which is precisely when it started deciding whether a
`BuildStructure` is accepted. Two peers holding different `/data` could disagree
about which side may build the Bastion while every number in the game matched,
and the protocol would see nothing. Latent because both peers load the same
`/data` - **and not latent from here**, because this row makes "may I build a
power plant" faction-dependent for the first time.

**A ferrite field could be destroyed by one rifle shot.** Recorded in full below,
because it is the one that was live.

## The ferrite field defect, which is DR-04's precondition

Found while reading GDD s8 for the *next* row rather than this one. GDD s8 gives
destroying a resource field to one superweapon on one side, as that faction's
economic-warfare identity:

> Sodality seismic charge (wide, lower-damage area denial **that also destroys
> resource fields** - economic warfare flavour)

Anything else that can do it takes that identity away before it ships, and
something else could do it: **anything at all.**

Every other system in the sim excludes fields by hand - auto-acquire, splash,
area damage, the guard leash, `EnemyNearAMovePoint`. The explicit `Attack` branch
did not, by design, because it "asks no hostility question at all". A field has
**1 hit point**. So one rifle shot deleted an entire field and every credit left
in it, permanently, since regrowth skips dead fields.

It is unreachable from the sidebar, and **that is not a defence**: it is
reachable from a LAN peer's command stream, and a rule that is safe only because
the local UI declines to send it is the exact shape this project has been caught
by three times. Fixed here rather than with DR-04, because the sim should agree
with written doctrine whether or not the seismic charge ever lands.

## Hash and format

**All 24 goldens byte-identical, measured**, and the tracker predicted this row
would move them. The mechanism rather than a resemblance to it: every seat
defaults to `FactionDirectorate`, the Directorate plant is type 1 with unchanged
numbers, and `SpawnPowerPlant` defaults to type 1, so every existing caller and
every golden scenario spawns exactly the building it always did. The AI picks its
own side's plant through one new expression that returns type 1 for a Directorate
commander, which is every commander in every golden.

**The catalogue checksum MOVES, 0x2495D0E393438B38 to 0x64768008B78985FB**, from
two causes that are both intended: a new structure type, and the `Faction` fold
above. No save format change; teams and factions are match setup.

## What this deliberately does NOT do

- **No rename.** `com_power_plant` is Directorate-only while keeping a `com_`
  prefix, which is a cosmetic mismatch. It is recorded rather than hidden: the
  prefixes are legacy labels and the `faction:` key is the truth, and
  `dir_turret` and `dir_superweapon` are `FactionCommon` and mismatch in the
  opposite direction. A rename touches the sidebar icon table and six prerequisite
  lists, and would bury this row's diff. Filed as tidy-up.
- **No art.** The generator wears the plant's model and icon, as the Bastion
  wears the turret's. A 1x1 building rendered as a 2x2 plant is owed work.
- **The AI does not exploit the difference.** A Sodality commander builds
  generators instead of a plant and is otherwise the same commander. It does not
  spread them, and it does not raid a Directorate plant knowing that one kill
  darks a base. Both are real ideas and both are separate rows.
- **Nothing else about the two factions changed.** DR-03 (a Sodality detector,
  which GDD line 56's "every stealth tool has a public counter" requires and the
  Sodality does not have) and DR-04 (the faction superweapons) are the rest of
  Q017 and are not in this row.

## Consequences

The two sides no longer play the same economy, which is the first substantive
answer to Q017 and to pillar 3. `factionpowergate` (5 stages) and
`ferritefieldgate` (3 stages) pin it; three of the five power stages and the
ferrite stage were each **proved to bite** by reverting the fix and watching the
gate fail with the right message.

The measurement the gate reports, which is the doctrine rather than a stat line:

```
one building lost - Directorate 100 -> 0 supply, Sodality 120 -> 80 across 3 generators
```

Full battery exit 0; client harness 189 to 194 checks, PASS.
