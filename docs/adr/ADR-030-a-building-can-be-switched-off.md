# ADR-030: a building can be switched off
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing P7 directive, "work down the tracker on my own judgement, deciding rather than asking")
- GDD/TDD feature served: GDD s7 line 64, "Saboteur (disables buildings)"; doc 24 B3; P7-11a

## Context

Every building in this game has been in one of two states since the first
prototype: standing, or rubble. The GDD names a unit whose entire purpose is a
third state, and it names it in the same breath and the same form as the
Infiltrator that shipped in P7-7: `Saboteur (disables buildings)`.

The row it arrived under, P7-11, bundled this with a hero unit and mines. Those
two are refused and the reasoning is in the tracker; the short version is that
the hero has a name and no ability and the project has already ruled the line is
a sample, and mines appear in no design document at all. The Saboteur is written
with its effect, which is the bar P7-7 cleared and P7-6 did not.

## Decision

### 1. "Disabled" is a side collection, not a field on the entity

`Dictionary<int, int>`, entity id to the tick it comes back.

A hashed per-entity field would move all 24 goldens for a feature no golden
scenario plays, which is the cost ADR-012 refused for FerriteCap and ADR-028
refused for the Air flag. The side collection is folded into the state hash
inside the per-entity loop, guarded:

```csharp
if (_disabledUntil.TryGetValue(e.Id, out int until)) h.Add(until);
```

**The guard is the whole trick.** An absent entry contributes zero bytes, so a
world with no saboteur in it hashes byte-identically to one compiled before
saboteurs existed. An unconditional `h.Add(0)` would have moved every golden.
This is the ADR-023 lane pattern and the P7-3 cargo pattern, used a third time,
and it is now the house style for optional per-entity state.

The prune predicate is the exact complement of the read guard - an entry goes
when its tick has passed or its entity is dead - because this project's most
expensive recurring defect is a rule stated twice and then fixed once. The prune
walks entity indices rather than the dictionary, so no unordered iteration
reaches the sim.

### 2. A disabled building is OFF, and the consequences are the EXISTING ones

Three effects, all of them a guard on code that already existed:

- It supplies and draws no power (`ComputePower`).
- It does not fire (the existing ADR-008 brown-out gate, joined as another
  clause of the same `if` rather than added as a second guard beside it).
- It does not produce, and its build lanes do not advance.

The first is the one that makes the unit interesting, and it was chosen
precisely because it invents no consequence: sabotage a power plant and the base
browns out through ADR-008's rules exactly as it would if the plant had been
shot. A player who understands power already understands what a Saboteur does.
Nothing new had to be explained to the player or written into the sim.

### 3. The effect is a third branch on the shared contact shape

`CaptureSystem` already carries the engineer's capture and the infiltrator's
theft on one shared shape: acquire an explicit target, walk to it, test the
same 1.75-cell reach, consume the actor. The Saboteur is a third EFFECT on that
shape and adds no pursuit, reach or consumption logic of its own.

Sabotage extends an existing disable by taking the maximum rather than
overwriting it, so a second saboteur cannot shorten the first one's work.

**450 ticks, thirty seconds, is my number and is the only invented figure here.**
It is long enough that a dark power plant actually costs the defender a fight
and short enough that it is a raid rather than a demolition. It belongs in
`/data` eventually; it is a named constant today.

### 4. It is NOT capture, and the event says so

`GameEventType.Sabotaged`, a new member, rather than reusing `Captured`.

This was a deliberate departure from the first draft of the brief, and it is
right: `SkirmishLive.cs` consumes `Captured` as an ownership change, firing the
"you lost it" alert and re-caching the owner. Raising it for a building that
never changed hands would tell the victim they had lost a building they still
own, which contradicts the exact distinction the unit exists to make.

That is not hypothetical. **The Infiltrator does precisely this today** - P7-7
reused `Captured` for a robbery - and it is now filed as P7-7a rather than left
to be discovered by a player. The enum is neither hashed nor saved, so a new
member costs nothing.

### 5. Save format v10

The disable is real state and a save that dropped it would resume a dark
building as a working one. v10 carries it.

Bumping the format found a live defect and a latent one. The live one: the v9
cargo block's reader test was `magic == SaveMagicV9`, an EQUALITY, so v10 would
have skipped a block the writer had written - the same shape of bug that v9's
own `hasBuildLanes` had when it landed, which was caught then and has now been
caught twice. Every version-feature test is a floor now. The latent one:
`DowngradeSave` would have emitted a silently corrupt stream for a v9 target,
dropping lanes, cargo and the checksum; no caller asked for one, so it had never
failed.

## Alternatives rejected

**A hashed `DisabledUntil` field on `Entity`.** Simplest to read, and it moves 24
goldens for a feature none of them play.

**Modelling the disable as damage, or as a temporary owner change.** Both reuse
existing machinery and both are lies: the building is neither hurt nor taken,
and the player's alerts would say so.

**Reusing `GameEventType.Captured`.** Cheaper by one enum member and it would
have shipped a false alert. See clause 4.

**Putting the duration in `/data` now.** Correct destination, and it needs a
schema key at a moment when `schema.unit.json` is already known to have drifted
from the loader (it declares `additionalProperties: false` and omits `air`).
Filed rather than compounded.

## Consequences

The Sodality gains the sabotage half of the doctrine GDD line 30 describes
("capture and sabotage tools"), of which it previously had only the theft half.

**All 24 goldens byte-identical, measured.** The catalogue checksum moves,
because a new unit changes it by construction, so pre-existing saves and
replays refuse on the same pre-first-public-build argument as P7-2, P7-3 and
P7-4. Save format v10.

What this ADR does NOT deliver, stated so the row is not read as finished: the
AI neither builds nor defends against a Saboteur, there is no client alert for
being sabotaged (only the sim event exists), the duration is compiled rather
than authored, and nobody has played against one.
