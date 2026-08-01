# ADR-045: the commander builds its own side, and asks for a capability rather than a type id
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 and s9's skirmish AI; doc 24 C9's AI half; P7-5d

## Context

Three rows in a row split buildings the two sides used to share: ADR-042 the
power grid, ADR-043 the detector, ADR-044 the superweapon. Every one of them
landed with the goldens byte-identical and every one of them left the same thing
behind, which doc 24 now names as the largest remaining gap: **the AI builds none
of it.**

`SkirmishAI`'s ladder yields struct types 1, 3, 11, 2, 5, 12 and 6, all named as
literals. Two of those literals had quietly stopped meaning what they said.

### The defect this fixes was created one wave ago

**A Sodality commander could not queue a superweapon at all.** The rung reads
`!hasSuper && credits >= 4500 ? 6`, and ADR-044 made struct type 6 the
Directorate's orbital cannon. So a Sodality commander asked the yard for a
building the sim refuses it, got nothing, and asked again forever.

Measured, by reverting the fix and running the gate:

> `a Sodality commander must reach its seismic charge and this one finished with
> superweapon type 0 (0 means none at all)`

The ladder's own comment predicted this failure and named the wrong cause: it
warned that "the day ADR-009's prerequisites land it queues a superweapon it can
never build and stalls forever". Prerequisites were handled. **Faction arrived
instead**, through the same door.

### And the counter shipped last wave was unreachable

ADR-043 gave the Sodality a Watch Post because GDD line 56 requires every stealth
tool to have a public counter. No AI could build one, so a Sodality commander
stayed exactly as blind as before the counter existed.

That asymmetry was precise rather than approximate, and it is worth recording:
the **Directorate** commander has had eyes since TICKET-P3-FAC-04, whose unit
cycle builds a Sentinel Scout every sixth unit and calls it "eyes for the wall".
The AI's blindness mirrored the sim's own hole exactly, on the same side.

## Decision

### 1. The ladder asks for a CAPABILITY, not a type id

Two queries on `World`, both scanning the catalogue in ascending type id:

- `BuildableStructOfKind(player, kind)` - the buildable structure of that kind
  this player's side may build, or **0 for none**.
- `BuildableDetectorStruct(player)` - the buildable building that reveals cloak,
  or **0 for none**.

**Returning 0 rather than a best guess is the load-bearing part.** Every rung
that uses one checks for 0 first and skips, so a commander can never queue a
building it will be refused. That failure mode is silent and total: the yard is
asked for something it can never finish, and the ladder never advances past it.

This is the same correction P7 has now made about fourteen times, and the third
time in four waves that the answer was to read a rule as the property it means
rather than the instance it names.

### 2. A detector rung, placed where the Sodality's answer actually lives

The Directorate's answer to cloak is a **unit** and is already in its unit cycle.
The Sodality's is a **building**, so it belongs in the structure ladder, sitting
beside the turret as the other cheap defensive building.

`BuildableDetectorStruct` returns **0 for the Directorate**, so the rung cannot
fire for that side at all. That is the mechanism for hash neutrality rather than
a resemblance to it, and the gate asserts it directly rather than leaving it to
the hash file:

> `the Directorate must have NO detector building (its answer is the Sentinel
> Scout, a unit) ... the new ladder rung would fire for that side and every
> golden would move`

### 3. One detector, not a screen

The commander builds a single Watch Post. Rejected: matching the Directorate's
two Sentinel Scouts, and scaling with base count. A static detector covers one
approach, so one post is demonstrably not enough to cover a base - but "how many
and where" is a balance question in a game nobody has played, and one is the
smallest thing that turns a blind commander into a seeing one.

## What this deliberately does NOT do

Each is a real idea, and treating them as consequences of this row would smuggle
several balance decisions inside one defect fix.

- **The commander still builds the COMMON turret, not its faction defence.**
  Neither the Bastion nor the Shroud Nest is ever built. That is a genuine
  identity gap, and it is a balance change rather than a defect: the turret works
  for both sides, so nothing is stalled or unreachable in the way the superweapon
  was.
- **No Veil Projector.** The Sodality's area cloak is never built, so an AI
  Sodality never uses its own signature mechanic offensively.
- **No use of the difference.** A Sodality commander now owns a seismic charge
  and will fire it exactly as the orbital cannon is fired, at the nearest enemy
  refinery. It does not aim at ferrite fields, which is the whole point of that
  weapon. **This is the largest thing this row leaves undone** and it is a real
  design question: field-denial targeting is a different scan from
  structure-targeting.
- **No spreading of generators.** ADR-042 gave the Sodality a decentralised grid
  and the commander places its generators exactly where it would have placed one
  plant, so it gets the sprawl's cost without its resilience.

## Hash and format

**All 24 goldens byte-identical, measured.** Two independent reasons, and the
first is sufficient: every seat defaults to `FactionDirectorate`, and for a
Directorate commander `BuildableStructOfKind(..., Superweapon)` returns 6 and
`BuildableDetectorStruct` returns 0, so the ladder yields exactly what it always
did. **The catalogue checksum does not move either**, because no definition
changed - this row is entirely AI behaviour.

## Consequences

`aifactiongate` (3 stages) pins it, and both new rungs were proved to bite by
reverting them.

```
aifaction: superweapon reached - Directorate type 6, Sodality type 22
```

A Sodality commander now reaches its own superweapon, where before this row it
reached none at all, and ends a match able to see cloak. Full battery exit 0.

The honest summary of where the AI stands: it builds its own side's **economy,
eyes and superweapon**, and none of its own side's **defences or tricks**. That
is a much smaller gap than the one this row opened on, and it is still a gap.
