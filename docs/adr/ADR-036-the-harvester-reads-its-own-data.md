# ADR-036: the harvester reads its own data, and six goldens move to let it
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised golden-hash regeneration)
- GDD/TDD feature served: CLAUDE.md's data convention; P7-1's defect class

## Context

`World.SpawnHarvester` is the oldest spawner in the file and it predates the
catalogue entirely. It hardcoded hit points, armour, sight and speed, and it
never stamped a `UnitType`.

It was found by `reachabilitygate`, which had to identify a produced harvester by
its `Kind` because the type stamp was missing. Had that gate matched by kind from
the start rather than by type, it would have been green and this would still be
here.

## Two consequences, and the second had been costing something all along

**No `UnitType`.** Every harvester in the game stood as type 0, so its authored
def could not be read back off the entity. `AtMaxAlive`, `IsAirborne` and the
client's name and model lookups were all blind to it, and one runner check had
already been written *around* the gap rather than against it.

**The speed diverged.** `Fix64.FromFraction(1, 5)` is 0.20.
`data/units/com_harvester.yaml` authors `speed: 18`, which is 0.18. Every
harvester in the game moved **eleven per cent faster than the file that is
supposed to define it**, which means every economy measurement this project has
ever taken was taken against a number nobody wrote down.

Hit points, armour and sight all happened to match, and that is exactly why this
survived so long: **a mostly-correct copy is harder to notice than a wrong one.**

This is P7-1's defect - authored data that is parsed, validated and then not used
- in the place it has sat longest, and it survived three waves this phase that
were explicitly about that defect class (weapons into `/data`, AI tuning into
`/data`, and `schemagate`). None of them looked at a spawner.

## Decision

`SpawnHarvester` reads `GetUnitType(HarvesterUnitType)` for speed, hit points,
armour, weapon and sight, and stamps the type. The **registered** def wins, not
the compiled one, which is the difference between data driving the runtime and
data mirroring it; `harvesterdatagate` proves that by registering a poisoned def
and asserting the spawn takes it.

## What moved, measured and explained

**Six of twenty-four goldens**: `aisuper`, `economy`, `expansion`, `mission`,
`mission03`, `skirmish`. Eighteen held.

That split is the argument that this is the right change rather than a broad
disturbance: **the six are exactly the scenarios where a harvester's speed can
affect the outcome**, and the eighteen that held are combat, stealth, walls,
pathing, veterancy and the rest, where no harvester earns anything.

**One scenario assertion also moved, and it is worth reading**, because it is the
only place the cost is legible in a unit a person can picture. `ScenarioEconomy`
asserted `4000 delivered plus 14 regrown`. It is now 13. Measured at the end of
the run, both fields are dead with 0 remaining, so **everything spawned still
reaches the refinery** - the delivered 4000 is unchanged. What moved is when: the
harvester arrives fractionally later, each field spends a slightly different span
below cap, and the regrowth accrual lands one unit short.

One unit of ferrite is the entire visible cost of eleven per cent of harvester
speed having been wrong since before the catalogue existed.

**Two absolute-hash pins in gates also had to be re-taken**, and they are
recorded here rather than quietly bumped because the pattern matters.
`multiseatgate` pinned a two-player skirmish-01 placement to prove P7-8a's
generalisation was inert; `lanaiseatsgate` pinned a no-commanders control to
prove the empty case stayed a pass-through. Both moved for a reason neither was
about, and both then **reported the failure in the wrong change's name**. The
values are re-measured and both messages now say "if you did not deliberately
change this, it is a regression; if you did, re-pin it" rather than naming a
cause they cannot know. An absolute pin is a good check with a bad error
message, and the fix is the message.

## Alternatives rejected

**Changing the yaml to 20 instead.** It makes the goldens hold and settles the
disagreement in favour of the copy rather than the source, which is precisely
backwards: `/data` is the authority by CLAUDE.md and by three ADRs this phase.

**Stamping the UnitType and leaving the hardcoded stats.** Fixes the blindness,
leaves the divergence, and leaves the next reader believing the numbers agree.

**Leaving it and documenting the divergence.** Considered, because six goldens is
a real cost. Rejected: a documented lie is still a lie, and every future balance
measurement would inherit it.

## Consequences

Six goldens regenerated with their cause named and the eighteen that held named
too. A replay or save recorded against any of the six will refuse, which is what
the hash is for and why this needed authorisation.

Every economy number this project has measured - harvest rates, the "float at 2
refineries / 3 harvesters" intent in GDD s4, the balance gate's figures - was
taken at 0.20 and is now taken at 0.18. **None of those figures were re-derived
here**, and that is the honest limit of this ADR: it makes the sim agree with its
data, and it does not tell anyone whether 0.18 is the right number. That is a
playtest.
