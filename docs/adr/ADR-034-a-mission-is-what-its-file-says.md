# ADR-034: a mission is entirely what its file says, and one golden moves to make it so
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised golden-hash regeneration for the remaining P7 rows)
- GDD/TDD feature served: GDD s8 (campaign); P7-9a

## Context

ADR-029 gave missions 04 to 06 the ability to declare their own construction
yard and opening treasury in their `.fmap` files, using a `structure 0 4` line
and an `elapsed 0 -> grant` trigger, both of which the format had always been
able to express.

It deliberately did NOT migrate missions 01 and 03, which got theirs from
`switch (setup.MissionIndex)` in `SkirmishLive.cs` and from two hand-copied
lines in the runner's gated scenarios. The reason recorded at the time was
honest and is now spent: migrating them moves golden hashes, and that was not
authorised.

The result was two mechanisms for one thing, kept in step by hand. That is the
duplication this phase has found in nine other places, and it has the same
failure mode: a rule keyed on WHICH mission this is rather than on what the
mission says, so every new mission needed a C# edit in two files, and a mission
whose author forgot was silently a mission with no base.

## Decision

### 1. Missions 01 and 03 declare their own setup; the switch and its copies go

Mission 01 gains `structure 0 4 6 22` and `trigger elapsed 0 -> grant 0 5000`.
Mission 03 gains only the grant, because it already declares a whole base and
the treasury was the last thing the switch still supplied for it.

`switch (setup.MissionIndex)` is deleted, as are the two setup lines in
`ScenarioMission` and `ScenarioMission03`. The client and the runner now build
the same world from the same source, rather than from two copies of one rule.

**A mission is now entirely what its file says**, which is what makes adding one
a data change rather than a data change plus two code edits.

### 2. Exactly one golden moves, and it is measured rather than predicted

`mission` moves from `0x979BBF17F84F6FF7` to `0xAC7083B4E1A485E2`. The cause is
plain: the construction yard is now spawned while the map's entity lines are
read rather than afterwards, so it takes a different entity id, and entity ids
are hashed.

**`mission03` does NOT move, and the reason is worth recording** because it was
predicted to. Its 4000 credits shifted from before tick 0 to an `elapsed 0`
trigger, which fires after the first `Step` rather than before it, so the
commander sees zero credits on its opening beat. That changes nothing, because
mission 03's map declares a power plant, a refinery, a factory and two turrets
and **no construction yard** - so the commander has no yard to build from on any
tick, and when the credits arrive is immaterial. The neutrality is explicable,
not luck, and checking why was worth more than accepting it.

This is the discipline the remaining P7 rows will follow now that regeneration
is permitted: measure which hashes move, explain each one that does AND each one
that was expected to and did not, and regenerate only those.

### 3. The guard is a property of the DATA, not a grep for the switch

`campaigngate` gained a stage asserting that four missions declare a
construction yard and at least five declare an opening grant, read from the
parsed map and its parsed triggers.

Deliberately not a grep for `switch (setup.MissionIndex)`: the switch could
return under another name, or as an `if`, and a grep would miss it. What the
check actually cares about is that a mission which needs a base has one in its
file, and that is what it reads. Proved to bite by deleting mission 01's yard
line, which fails naming the removed case.

## Alternatives rejected

**Leaving the two mechanisms.** Free, and it leaves the trap that the next
mission author walks into.

**Migrating the maps to the generator in the same wave.** P7-9a's row bundles
that, and it is content rather than a defect: it would move the same hash again
for a different reason and make the regeneration harder to reason about. Split
out, and still owed.

**Keeping the runner's setup lines "so the scenario is self-contained".** That is
the argument that produced the duplication. A scenario reading the same file the
game reads is more self-contained, not less.

## Consequences

One golden regenerated, with its cause named. Twenty-three unchanged.
`campaignsave`, `saveload` and `replay` pass, and the full battery is green.

A replay or campaign save recorded against the old `mission` hash will refuse,
which is the point of the hash and the reason this needed authorisation. On the
pre-first-public-build argument that has covered every catalogue move this
phase, that cost is acceptable.

What this does NOT deliver: missions 01 to 03 are still hand-typed 64x48 grids
while 04 to 06 are generated, which is the other half of the P7-9a row and is
still owed.
