# ADR-028: the air layer
- Status: Ratified
- Date: 2026-07-31
- Deciders: Architect agent + Luke (under the standing P7 directive, "work on this goal until complete")
- GDD/TDD feature served: GDD line 55 (aircraft and the airfield); doc 24 Tier A1; P7-4

## Context

Air is the largest of the three whole systems doc 24 found missing, and the only
one still open. There are no aircraft, no airfield, and - the part that matters
most - no anti-air anywhere in the roster. Both benchmark games used air as a
third dimension of play: fast strike craft that ground defence cannot answer,
and the anti-air building that answers them.

The sim already anticipated it. `EntityKind.Airfield` has existed since ADR-009
clause 5, it is already in `IsStructure` and the producer set, and the skirmish
AI already lists it among its wave targets. Nothing fills it.

## Decision

### 1. Air is a property of the unit TYPE, not per-entity state

`UnitTypeDef` gains `Air`, defaulting false. Whether an entity flies is answered
by looking up its type in the catalogue, exactly as its speed and armour are.

The alternative - a hashed per-entity flag - would move all 24 goldens for zero
behavioural change, which is the cost ADR-012 refused for FerriteCap and
ADR-014 accepted only because it had to. A type-derived property costs a
dictionary lookup and moves nothing.

### 2. Aircraft move in straight lines and ignore terrain

Ground movement is a flow field over blocked cells. An aircraft does not consult
it: it steps toward its target directly, and no terrain, structure or unit
blocks it. It also does not participate in separation, and it does not block
cells for anything else.

This is gated on the type, so a world containing no aircraft executes the
identical code path it did before, which is what keeps the goldens still.

### 3. A weapon engages air IF AND ONLY IF it is an anti-air weapon

`WeaponDef` gains `AntiAir`, defaulting false, and the rule is an equality
rather than an implication: a ground weapon cannot reach a plane, AND a
dedicated anti-air weapon cannot shoot the ground.

Both directions are load-bearing, and the second was learned the hard way. This
clause was first written as "cannot hit air unless it says it can", which makes
an anti-air weapon one that can hit BOTH - and the gate caught the consequence
at once: the flak track was a better tank as well as the answer to aircraft,
which turns the counter into a straight upgrade and removes the decision the
whole layer exists to create.

All THREE target-selection paths ask one shared predicate - the explicit order,
the main auto-acquire scan, and the guard stance's leash scan. A first pass
guarded two of the three and shot a plane down with a rifle.

Defaulting false means every existing weapon - the tank cannon, the service
rifle, the rocket tube, the turret gun, the howitzer, the bulwark cannon, the
vanguard autocannon, the emplacement gun - is ground-only from the moment this
lands. That is deliberate and it is the whole reason air is interesting: an
aircraft that everything can shoot is just a fast tank.

### 4. Therefore anti-air ships in the SAME wave, and this ADR binds them

An air layer without an answer is not a feature, it is a dominant strategy. This
ADR is not satisfied by aircraft alone: the wave that adds a strike aircraft
must also add at least one anti-air answer, or it must not land. The gate is
required to prove both halves - that the aircraft is untouchable by ground
weapons, and that the anti-air answer kills it.

### 5. The Airfield produces aircraft, and gates them

Struct type 16, `EntityKind.Airfield`, prerequisite the radar uplink, and the
`produced_at` for every air unit. This reuses the ADR-009 producer routing
wholesale rather than inventing an air-specific path.

## Alternatives rejected

**A hashed per-entity Air flag.** Simpler to read at the point of use, and it
moves every golden for no behavioural gain. Rejected on ADR-012's precedent.

**Letting existing weapons hit air by default.** It would have made this wave
smaller by removing the need for an anti-air answer, and it would have made air
pointless: the distinguishing property of an aircraft is precisely that most of
the roster cannot touch it.

**Modelling ammunition and a return-to-pad reload cycle**, as the benchmarks
did. It is genuinely part of what makes air feel right, and it is deferred: it
needs a per-entity ammunition count, which is hashed state and a save bump, and
it is orthogonal to the question of whether an air layer exists at all. Recorded
here so it is not mistaken for an oversight.

**Aircraft that can be crushed, blocked or separated.** Rejected as incoherent:
they are not on the ground.

## Consequences

The pool gains a dimension it did not have, and gains it with an answer already
in place, so no faction is handed an unanswerable weapon.

Every existing weapon becomes explicitly ground-only. That is a real balance
statement and it is measured, not assumed: with no aircraft in any golden
scenario the change is byte-identical, and the gate proves the refusal directly.

The catalogue checksum moves, because new units and a new structure change the
catalogue by construction. Pre-existing saves and replays refuse, on the same
pre-first-public-build argument as P7-2 and P7-3.

What this ADR does NOT deliver, stated so the row is not read as finished:
aircraft have no reload cycle, the AI does not build or answer them, and no
bespoke art exists - the interim models are owed to art-pipeline, the ADR-019
precedent. The row is the air LAYER; making the AI fly is a separate question.
