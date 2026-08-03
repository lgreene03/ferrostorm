# ADR-067: the decoy army, and the assumption that it could not be built

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 line 30; GDD s8 lines 71-72; ADR-062 to ADR-066; Q021; P7-26

## Context

The last of GDD s3's five named support powers, and the one this project had
twice written down as probably unbuildable:

> entities that look real to one player and not another touch targeting, fog and
> the checksum at once ... if it needs per-viewer entity visibility that the sim
> cannot express, REFUSING it with the argument recorded is a legitimate and
> possibly correct outcome.

**That assumption was wrong, and it was wrong in an interesting way.**

## The decision that decided the row

A decoy does **not** need to be *rendered* differently to different players. It
needs to **be** a real entity that looks like a real unit and does nothing, so
that an observer cannot tell which army is which **until they shoot it**.

**The deception lives in the observer's uncertainty, not in the renderer.**

That is also how the benchmark games did it: their fakes were real objects with
no function, visible to everybody, indistinguishable until touched. Per-viewer
illusion is a modern reading of the words, not the 90s one, and the GDD's whole
brief is the classic games.

So there is no architecture to grow. `SpawnUnit` already takes `hp`, `armour`,
`weaponId`, `sightCells` and `unitType` as separate arguments, which is exactly
the shape needed: **the imitated unit's type and speed, one hit point, no weapon,
no eyes.**

Measuring the entity model is what showed this - the fifteenth time this phase
that measuring before inventing has changed the answer, and the second time it
has overturned a refusal that had not yet been written.

## The numbers, all derived

| | chosen | derivation, and what was rejected |
|---|---|---|
| how many | **one wave's worth** (`GetAiTuning(StandardId).WaveSize`) | The number this game already uses for *"an army worth attacking with"*, which is precisely what a decoy must look like. Fewer reads as a patrol; more reads as an army nobody could afford. |
| hit points | **1** | Not a chosen number but the smallest thing that can exist. *"Dies to the first shot that touches it"* is the defining property, and 1 is the only value that says it exactly. Rejected: a fraction of the real unit's hp, which makes a decoy a cheap unit rather than an empty one, and would need re-deriving every time a unit is rebalanced. |
| what it imitates | **the rifle squad** | The one thing both factions field in numbers from the opening minute - ADR-009 makes it the only unit either side can build until a wave stands. A decoy army of anything rarer would announce itself by being unaffordable. |
| speed | **the real unit's** | An army that cannot keep up is not a convincing army. |
| sight | **none** | A rule rather than a number, and the same trap tunnel deployment had: a power that quietly doubled as a scouting power would be two powers. A hollow imitation has no eyes. |

### The Shroud Nest carries it

The name is the argument - a shroud is deception - and it is cheap and early,
which suits a trick the Sodality's *"cheap infantry swarms"* doctrine wants in
the opening rather than the endgame.

Rejected: the generator, which is infrastructure and would put the trick on the
first building raised; the seismic charge, technically excluded like every
superweapon because it already uses `ChargeTicks`. The Watch Post and Veil
Projector already carry the other two tricks, and powers on one building share a
charge (ADR-064) - spreading the Sodality's three tricks across three buildings
keeps each one separately scoutable and killable, which is GDD s8's counterplay.

### It may be placed anywhere, including in fog

Deliberately unlike tunnel deployment, which requires vision. That power moves
**real** units and would otherwise be a free scout as well as a teleport; decoys
have no sight, so no such risk exists - and a fake army appearing in the fog for
an enemy to stumble into is the entire trick. The asymmetry is recorded here
rather than smoothed away.

## Hash and format

**All 24 goldens byte-identical, measured.** No golden scenario builds a Shroud
Nest, and the power holds no live state.

**Save format unchanged at v14** - decoys are ordinary entities, saved by the
entity block that already exists. **The catalogue checksum moves to
0xFA2DFE52192660BA**, because the Shroud Nest now grants a power.

## Proved to bite - after the gate itself had to be fixed twice

`decoyarmygate`, four stages, control first. It is the first power that
**creates** entities: the tunnel moves existing ones, and every other power
reveals, damages or blinds, so no other gate would notice decoys that could
shoot, that saw the map, or that were tough enough to be worth killing.

**The gate's first two versions were wrong in the same way, and the bite tests
found it.** It identified decoys by their hit points, and then by *"no weapon and
no eyes"*. Both are properties the gate itself asserts - so breaking the very
rule a later stage tested made the decoys **unfindable**, and the count stage
failed first with the wrong message:

> `decoy army: 0 decoys where a wave is 6`

...when the actual break was that decoys had sight.

**A gate whose stages can only fail in one order is one assertion wearing four
hats.** Fixed by identifying decoys by **provenance** - the entities the power
created, recorded across the firing step - which is independent of every property
asserted about them. Each break now reports its own failure:

- armed → *"a decoy carries weapon 2 ... it is a free army"*
- full health → *"a decoy has 100 hit points where a decoy has 1 ... a decoy that
  trades is an army"*
- sighted → *"a decoy has sight ... a power that quietly doubled as a scouting
  power would be two powers"*

Stage 2 asserts against the **derivation** (`WaveSize`) rather than the literal
6. Stage 4 kills a decoy with a real rifle squad rather than reading `MaxHp`,
because the claim is about what happens in a firefight.

After every revert the goldens were re-measured against the file and matched.

## Consequences

**Q021 is CLOSED. All five of GDD s3's named support powers now exist**, and GDD
s8's *"3-4 minor support powers per faction"* is satisfied for the Sodality (3)
and nearly for the Directorate (2).

Five powers have shipped without a single balance argument between them, because
every number in all five belongs to something else already in the game. That is
the whole method: **a derived number needs no defence, only a reader.**

And not one of them can tell you whether any of it is fun. Sixteen ADRs now.
