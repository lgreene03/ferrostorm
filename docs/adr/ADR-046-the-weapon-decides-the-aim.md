# ADR-046: what the weapon IS decides where it goes
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s8 line 70; doc 24 C9's AI half; P7-5e

## Context

Three waves built the Sodality's seismic charge and then failed to use it.
ADR-044 gave it the effect GDD s8 specifies - "wide, lower-damage area denial
**that also destroys resource fields** - economic warfare flavour". ADR-045 made
the commander build one. And the commander went on aiming it with the scan it
uses for attack waves: **hit the nearest enemy refinery**.

That is not a small miss. Destroying resource fields is the only thing this
weapon does that the orbital cannon cannot, so aiming it at a building spends
4000 credits and six minutes of charge on a strictly worse orbital cannon.

## The defect underneath it, which this campaign created

ADR-044 selected the effect with `e.StructType == SeismicChargeStructType` - **a
literal**. That is the instance-not-property defect this phase has now corrected
about fifteen times, and that instance was mine, from two waves ago.

It had a second cost that was less obvious than the usual one: **it left the AI
no question it could ask.** A commander cannot reasonably be expected to know
that struct type 22 is special. What it can ask is "does my superweapon destroy
fields", and nothing in the catalogue could answer that.

## Decision

### 1. `StructureTypeDef.DestroysFields`, authored in /data

One boolean, authored in `sod_seismic_charge.yaml` as `destroys_fields: true`,
folded into `CatalogueChecksum`, and read in **two** places:

- the impact site chooses the effect by it, replacing the type-id literal;
- the AI aims by it.

The second reader is the point. A rule that only one site consults can be a
literal without anyone noticing; the moment a second, quite different site needs
the same question, the literal stops being adequate.

Rejected: a `World.SuperweaponDestroysFields(structType)` helper returning
`structType == SeismicChargeStructType`. It reads better at the call sites and
changes nothing - the knowledge is still compiled in, and `/data` still cannot
express a second denial weapon.

### 2. A different scan, not a different target in the same scan

The wave scan answers "where is the nearest enemy refinery". Field denial asks
"which patch of ground, denied, costs the enemy the most ferrite". Three rules,
each a decision:

**Score by CLUSTER, not by richest field.** `ApplySeismicCharge` kills every
field within 6 cells, so a tight group of three ordinary fields is worth more
than one fat isolated one. The gate makes the wrong answer the tempting one: a
single 9000 field against three 5000s together, and a single-richest aim takes
the 9000.

**Only their ground.** A field is a candidate only if it is nearer an enemy
structure than my own base. Fields are neutral and carry no owner, so proximity
is the only honest proxy for "theirs" - and without the rule the commander would
happily deny the patch its own harvesters are working, which the gate holds it
to by offering 60000 ferrite beside its own yard against 4000 beside theirs.

**Ties by lowest entity index**, which a strictly-greater comparison over an
ascending walk gives by construction. Stated because it is load-bearing for
determinism rather than incidental.

### 3. It still fires when there is nothing worth denying

No qualifying field falls back to the ordinary refinery aim. A denial weapon that
banks a charged superweapon forever because the map has no enemy ferrite is worse
than one that settles for a building.

## Hash and format

**All 24 goldens byte-identical, measured.** The new branch is reached only by a
commander whose superweapon destroys fields, which is the Sodality's alone, and
every golden is played by Directorate seats.

**The catalogue checksum MOVES, 0xD2B80B9B8E87A2CA to 0xDBC4C027FB1EAB73**, from
the new authored field. It is not optional: it decides what a launch does, so two
peers disagreeing would watch the same strike take the map's economy apart on one
machine and not the other.

## What this deliberately does NOT do

- **No timing judgement.** The commander fires the moment the weapon is charged,
  exactly as before. It does not wait for harvesters to be on the field, and
  "when" is a much harder question than "where".
- **No coordination with the attack.** The denial strike is not combined with a
  wave, so the enemy is free to rebuild its economy unharassed.
- **The Directorate learns nothing.** Its aim is untouched, which is correct: an
  orbital cannon has no reason to prefer ferrite.
- **It does not avoid its own units.** Neither superweapon ever has, and ADR-038
  recorded that splash hits friend and foe alike as a decision. A denial strike
  on their ground is unlikely to catch my army, but nothing prevents it.

## Consequences

`seismicaimgate` (5 stages) pins this, and two were proved to bite - including
one that specifically distinguishes a def question from a type-id one, by
registering the same building with the flag off and watching the aim return to
the refinery.

```
seismicaim: one 9000 field alone against three 5000 together - the charge went to (70, 70), the cluster
```

Full battery exit 0.

The Sodality's superweapon now does the thing it was built for. What remains of
the AI's faction gap is narrower than it was: it builds the common turret rather
than its faction defence, never builds a Veil Projector, and clusters its
generators where a single plant would have gone.
