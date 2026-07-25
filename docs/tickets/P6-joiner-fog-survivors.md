# TICKET-P6-JOINER-FOG: two hardcoded seats survived the C7b-ii sweep

Labels: persona:p3, gdd:s9, phase:6, owner:client-engineer, gdd:Q002

Status: **FOUND AND FIXED, 2026-07-25.** Found while auditing the client for the
ferrite-drain wave, not by a failing check, which is itself the finding.

## What was wrong

Two lines in the battle scene still read a literal seat after C7b-ii swept
ninety-three sites and CI grew a guard against exactly this:

```csharp
// the actor loop
node.Visible = v.PlayerId != 1 || _world.IsVisible(0, (int)v.X, (int)v.Y);
// the minimap dot feed
if (v.PlayerId == EnemyPlayerId && !_world.IsVisible(0, (int)v.X, (int)v.Y)) continue;
```

At seat 0 both are correct, **by luck**: the enemy really is player 1 and the
local player really is 0.

At seat 1, which is every LAN joiner, the first clause **inverts**:

- `v.PlayerId != 1` is FALSE for the joiner's own units, so their visibility fell
  through to `IsVisible(0, ...)` - the HOST'S vision. **A joiner's own army was
  drawn only where their opponent could see it.**
- `v.PlayerId != 1` is TRUE for every enemy unit, so the fog test was skipped
  entirely. **The host's whole army was drawn through the shroud.**

The minimap line has the seat-relative `EnemyPlayerId` on the ownership half but
still asked player 0's eyes, so a joiner's minimap showed enemy units wherever
the enemy could see its own units, which is everywhere. A maphack, delivered by
a spare zero.

## Why the existing checks did not catch it

This is the part worth keeping.

`FogRevealsOwnBase` has passed since the harness was built, and it was not
lying: **the shroud texture has always used `LocalPlayerId`** (`_fog.UpdateFrom
(_world, LocalPlayerId)`). The overlay was right. What was wrong was the
filtering of the actors drawn *underneath* it, and the dots drawn *beside* it,
which are three separate reads of "who can see this" that nothing forced to
agree.

The CI seat guard did not catch it either, because it looked for `PlayerId == 0`
and `PlayerId != 0`. Neither line says that. **"Player 0" is not the only way to
name the wrong seat; "player 1" names it just as wrongly**, and `IsVisible(0,`
names it without mentioning a seat at all.

## The fix

One predicate, `DrawnForLocalSeat(playerId, cx, cy)`, used by both call sites and
by a verification hook, so there is one definition of the rule rather than three
copies that agree until one of them does not.

The CI guard is widened to ban the literal seat in **both** directions and inside
`IsVisible` / `IsExplored`, with comment lines filtered out so the fix's own doc
comment can quote the bad expressions. A guard that forbids naming the bug is a
guard that gets the explanation deleted instead of the defect.

## Verification

Two harness checks, and they fail in **opposite directions** against the old
expression, which is what a single check would have missed:

```
FAIL  my own base is DRAWN for me (not gated on the other player's vision)
FAIL  an enemy in unseen fog is NOT drawn for me
```

Restoring the pre-fix expression turned both red; reverting restored green. The
widened CI grep was likewise proven against a copy carrying the reintroduced
line.

## The lesson, since this is the sixth of this shape

Every one of the six has been client-side, and every one was invisible from the
seat the developer happened to be sitting in. C7b-ii's sweep was thorough about
the pattern it was looking for and blind to the same bug wearing a different
literal. The durable fix is not a wider grep, it is that **three copies of one
rule became one**: `DrawnForLocalSeat` cannot disagree with itself.
