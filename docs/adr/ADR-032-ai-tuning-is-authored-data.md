# ADR-032: the AI's tuning is authored data, and it rides the catalogue checksum
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive to work the tracker on my own judgement)
- GDD/TDD feature served: GDD line 76 (the difficulty ladder); CLAUDE.md's data convention

## Context

`CLAUDE.md` states that all gameplay numbers live in `/data` as YAML and that
hand-editing stats in code is forbidden. Three waves have now found that
sentence to be partly aspirational: the schemas were never validated
(`schemagate`), `data/weapons/` was empty, and `data/ai/` was empty too.

`data/ai/` existing and holding nothing is the tell that someone intended this
and it never happened. The numbers it should hold are small and entirely
concrete: three personalities of two numbers each, and four difficulty rungs of
three.

## Decision

### 1. The tuning is catalogue data, registered on the World

The obvious shape - a static table inside `SkirmishAI` loaded once from disk -
is refused. The runner builds roughly 138 worlds in one process, and a mutable
process-global would let one scenario's tuning leak into another's. That is a
determinism hazard of exactly the kind this project exists to avoid.

So the tuning is registered on the `World`, frozen after tick 0, alongside
units, structures, fields and weapons. `SkirmishAI` still holds no world
reference: its constructor resolves the two rows once into the same three
integers it has always held, so the AI remains sim-adjacent and mutates nothing.

The compiled table survives as the reference a `/data` file must reproduce, the
`Combat.Weapons` precedent, which is what keeps a bare `World` with no `/data`
behaving exactly as before.

### 2. It MUST ride the catalogue checksum, and that is the whole safety argument

This is what makes this wave different from the weapons one, and it is the
clause worth remembering.

The AI's numbers being COMPILED is currently a safety property nobody wrote
down: two LAN peers agree on them by construction. Moving them to `/data`
destroys that property and creates a new desync vector, because two peers with
different `data/ai` files would issue different AI commands from an identical
world and neither would know.

`World.CatalogueChecksum` already rides the LAN Hello, saves and replays, and a
mismatch already refuses. Folding the AI tuning into it restores by mechanism
what compilation gave by accident. The gate proves the fold is real: the
checksum moves on **one unit of wave size** or **one credit of Brutal's
handicap**.

Stating the general rule, because the next authored kind will face it: **moving
a number from code into `/data` moves it from "agreed by construction" to
"agreed only if checked". Anything that can differ between two peers and change
the command stream must be in the checksum.**

### 3. The beat ratio is authored as a numerator and a denominator

The ladder scales the beat by 2, 1, or 2/3. Authoring 2/3 as anything
floating-point is forbidden in `/sim` and would be a determinism defect
regardless of the grep. Numerator and denominator keep the existing integer
arithmetic exactly, including its truncation: `15 * 2 / 3` is 10, not 10.0
rounded, and the `beat < 1` floor still clamps a beat of 1 rather than blanket
clamping everything.

The gate asserts the truncation and the floor separately, because a single
"floors to 1" check would pass on an implementation that clamped everything.

## Alternatives rejected

**A static table in `SkirmishAI`.** Simplest, and a process-global mutable in a
deterministic sim across 138 worlds per process.

**Leaving the tuning compiled.** Defensible, and it makes `CLAUDE.md`'s rule
false in a fourth place. The rule is either true or it should be amended, and
amending it to exclude the AI would be excluding the numbers a player most
plausibly wants to tune.

**Authoring the beat as a percentage or a tick count per rung.** A tick count
per rung would drop the interaction the ladder is built on: the rung SCALES the
personality's beat rather than replacing it, which is what lets taste and
strength be picked independently (DR-14b).

**Not folding it into the checksum**, on the grounds that AI state is never
hashed and replays re-run the command stream with no AI attached. True for
replays and false for LAN, where both peers run their own AI live. The narrower
argument would have been correct about the case it considered and wrong about
the one it did not.

## Consequences

`data/ai/` is no longer empty, and `/data` now holds every gameplay number the
project claims it does: units, structures, fields, weapons and the AI. The
`DataDirs` table added last wave turned `ai` from a known-non-catalogue row into
a real kind by changing one row, which is the fan-out that table exists to
prevent.

**All 24 goldens byte-identical, measured**, because the authored numbers
reproduce the compiled ones exactly and the gate asserts that field by field.
The catalogue checksum moves from `0x73326A3FF8AEA4D1` to `0x6BFA9B6EBB6946CF`,
so pre-existing saves and replays refuse, on the same pre-first-public-build
argument as P7-2, P7-3, P7-4, P7-11a and the weapons wave.

`schemagate` now walks five schemas: 45 definitions and 597 keys became 52 and
640.

What this does NOT deliver: the AI's BEHAVIOUR is untouched, and the numbers are
the ones that shipped. Whether Rusher's wave of four is the right four is a
balance question and a playtest, not a data format.
