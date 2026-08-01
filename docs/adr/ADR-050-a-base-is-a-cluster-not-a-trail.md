# ADR-050: a base is a cluster, not a trail
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: doc 24 C9's AI half; P7-8

## Context

The tracker carried a filed row saying the commander "places Sodality generators
exactly where it would have put one plant, so it gets decentralisation's cost
without its resilience". **I filed that by reading the code and it was wrong.**

Measured, the generators are not clustered. They are strung in a chain marching
off the map:

```
Sodality power buildings: (12,30) (10,31) (7,28) (4,25) (0,19) (2,16)
                          (3,13)  (0,10)  (2,7)  (0,4)  (0,1)  (3,0)
```

Twelve generators walking from the yard to the map corner. That is the second
time in three waves that a row filed from reading the code was disproved by
measuring it, and it is why the measurement came first this time.

## The defect

`SkirmishAI.TryFindPlacement` walked the entity list **backwards**:

```csharp
for (int i = w.Entities.Count - 1; i >= 0; i--)
```

so the anchor was the **most recently built** structure, and every new building
ringed off the last one. Each placement moved the frontier a little further out,
and the base walked.

**This was not created by DR-02, it was multiplied by it.** Measured with the
fix reverted:

| | structures | furthest from a yard |
|---|---|---|
| Directorate | 13 | **11 cells** |
| Sodality | 21 | **31 cells** |

The Directorate drifts too. It only looked acceptable because a 100-supply plant
means it builds five where the Sodality, on 40-supply generators, builds twelve -
so the same per-building drift compounds two and a half times as far.

## Decision

**Walk the entity list forwards.** One direction, one line.

Forwards, the first eligible structure is the Construction Yard, so buildings
ring outward from the base and the base stays a base:

| | structures | furthest from a yard |
|---|---|---|
| Directorate | 13 | **4 cells** |
| Sodality | 21 | **5 cells** |

**Founding a second base still works**, which was the obvious risk and was
checked rather than assumed: the rings around the first yard fill up and the loop
falls through to the next one. The `expansion` scenario still buys its MCV,
founds its second base and migrates its economy, mining the far field to 0.

### The bound in the gate, and where it comes from

There is no GDD line about base shape, so `maxFromYard` is a design default. It
is **two `World.CyBuildRadius`** rather than a bare number, so the claim reads as
"a base is about two yard-radii across" and can be checked against the game's own
rule for how far a building may sit from what anchors it.

Rejected alternatives:

- **A literal cell count.** Passes identically and tells a reader nothing about
  why that number and not another.
- **A bounding-box diagonal against map size.** Scales with the map, which sounds
  better and is worse: it would let a base sprawl further simply for being on a
  bigger map, and the pathological chain measured 31 cells on a 96x64 map, close
  enough to half the short dimension to make the assertion nearly vacuous.
- **Asserting against the commander's own placement constants.** That is the trap
  ADR-047 fell into: a gate sharing a constant with the code under test follows
  the code wherever it goes.

## The trade this makes, stated because it is real

**A compact base is easier to hit with one area weapon.** Measured on the same
fixture, the count of power buildings inside a single seismic-charge blast
(6 cells) went from **5 of 12 to 9 of 12**.

That reads as a loss for ADR-042's decentralised Sodality grid, and it is worth
being precise about what that ADR actually claimed:

> no single kill takes more than a THIRD of the supply

**That claim is untouched.** It is about losing one building, and losing one
generator still costs a third of the grid. ADR-042's gate asserts single-loss
resilience and still passes.

What compaction does cost is resilience to **area** damage, which ADR-042 never
claimed and no gate asserts. Whether the Sodality should deliberately spread its
generators against superweapons and artillery is a genuine design question - and
it is a balance question in a game nobody has played, so it is **filed rather
than decided**. Spreading a base on purpose is also not free: a trail of
buildings across open ground is a trail nobody can defend, which is the
behaviour this row exists to remove.

## Hash and format

**Four goldens regenerated, measured** - the same four as ADR-047 and ADR-049,
and for the same reason: they are the scenarios whose commander builds a base.

| scenario | before | after |
|---|---|---|
| `skirmish` | `0x946956C509A353B8` | `0xDCD6A3480A8E0E22` |
| `expansion` | `0x1E49D8E5E01F6376` | `0x7A6AA4D3238DF294` |
| `aisuper` | `0x2BEF8B6865D0C25D` | `0x9CC770F9029970FD` |
| `mission` | `0x15BEBB861BD37F1A` | `0x3DB6A9D39E31617C` |

The other twenty are byte-identical and the catalogue checksum is unmoved.

## Consequences

Commanders build bases rather than trails. `baseshapegate` pins it for both
sides and was proved to bite at 31 cells against a bound of 14.

The finding worth carrying: **this defect was invisible for the whole project
until an economy row made one side build twelve power buildings instead of one.**
The drift was always there at 11 cells and nothing measured it, because nothing
had ever asked what shape a base was. A gate now does.
