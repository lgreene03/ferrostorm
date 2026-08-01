# ADR-043: the Sodality can see, and a common stealth tool stops having a faction-locked counter
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD line 56; GDD s3; doc 24 C9; doc 27 DR-03; P7-5b, the second part of Q017

## Context

GDD line 56 is written as an absolute, and absolutes are checkable:

> Cloaked units decloak on firing and near detectors; detectors are visible and
> killable. **Every stealth tool has a public counter.**

It was true for one side. `dir_sentinel_scout` (unit type 6, `Detector: true`,
`Faction: FactionDirectorate`) is the **only** detector in the game, unit or
structure. `Entity.Detector` is written in exactly one place in the whole sim,
inside `SpawnUnit`, so no building could ever detect anything.

Against that, the Sodality owns every stealth tool in the game: the Shade
Raider, the Phantom Tank, the Infiltrator, the Saboteur, the Shadow Commando and
the Shroud Nest, plus the Veil Projector's area cloak. So the asymmetry ran
total in both directions - and the side with nothing to detect held the only
detector.

## The case that decided the shape, which Q017 did not name

Q017 justified DR-03 on the **mirror match**: Sodality against Sodality has no
answer to cloak beyond the firing reveal. That is true and it is the weaker half.

`com_mine` is **`faction: common`** and stealthed. Its own `/data` notes assert
GDD line 56 is satisfied:

> ...GDD line 56's requirement that every stealth tool carry a public counter is
> met by **a Sentinel Scout** revealing the field and anything at all shooting it

**A Sentinel Scout is Directorate-only.** So a common tool that either side can
lay had a counter only one side could build, and the file saying otherwise was
simply wrong for half the players. That is not a mirror-match edge case, and it
is what makes this a defect rather than a feature request.

## Decision

### 1. A STRUCTURE, not a unit, and the shape is the identity

`sod_watch_post` (struct type 21, `EntityKind.WatchPost`), Sodality-only,
`Detector: true`.

The alternatives, with why each lost:

- **A `Detector` flag on an existing Sodality unit.** The cheapest option and
  the one Q017 lists first. It fails on doctrine: **every Sodality unit is
  itself stealthed**, and a cloaked detector contradicts line 56's "detectors
  are visible and killable" in the same sentence that requires it. The Sodality's
  only uncloaked units are the *common* ones, and flagging one of those would
  hand the Directorate a second detector rather than give the Sodality a first.
- **A new Sodality detector unit**, mirroring the Sentinel Scout. Cheapest in
  machinery, and the weakest answer: it makes the two sides differ by a name.
  Q017's own complaint is that asymmetry must be thematic.
- **A structure**, which is what shipped. It needed real machinery
  (`StructureTypeDef.Detector`, and a spawner that writes `Entity.Detector`),
  and it buys a genuinely different *shape* of answer:

  > the Directorate **sweeps** - a scout car drives where it suspects
  > the Sodality **waits** - a post is planted where it predicts

  A building is also maximally "visible and killable", which is the property
  line 56 asks a detector to have.

### 2. Numbers set against the Sentinel Scout, because the pair is the point

| | Sentinel Scout (Directorate) | Watch Post (Sodality) |
|---|---|---|
| cost | 400 | **350** |
| hit points | 90 | **260** |
| sight / detection radius | 7 | **8** |
| mobile | **yes** | no |
| armed | no | no |

Cheaper, far harder to pick off, and one cell further-seeing, paid for by never
moving. Sight 8 against 7 is the one number where it wins outright and it has to:
a detector that cannot be repositioned must cover more ground to be worth
planting. Detection radius **is** `SightCells`, because the sim has never carried
a separate detection range and adding one would be a second number meaning
almost the same thing.

Behind the power plant (which since ADR-042 means *a power plant*, so the
Sodality's own generator unlocks it) and therefore available early, on purpose:
the stealth it answers comes out of the barracks, and a counter that arrives
after the tech tree is a counter to nothing.

### 3. One defect found beside it: the /data round-trip was blind to a faction

`StructureTypeDef.Equals` is hand-written, and it compared eleven fields and
**not `Faction`**. That comparison is what the selftest's `/data` round-trip
uses, so a building whose yaml declared one side and whose compiled reference
declared the other would have round-tripped **clean**.

ADR-042 moved `com_power_plant` to Directorate in both places one wave ago, and
**would have passed having moved it in only one.** `Faction` and `Detector` both
join the comparison here.

## Hash and format

**All 24 goldens byte-identical, measured.** Nothing spawns a Watch Post in any
golden scenario, and every existing structure def takes `Detector = false` by
default, so no fold changes value for anything that already existed.

**The catalogue checksum MOVES, 0x64768008B78985FB to 0x2CADF63D66912E62**, from
the new type and from folding `Detector` into every structure. The fold is not
optional: detection decides what a player can *see* and therefore what its units
may target, so two peers disagreeing would resolve the same firefight
differently while every stat in the game matched. That is the clearest case of
the ADR-032 clause yet, because the disagreement would not even be about a
number.

`EntityKind.WatchPost = 21` is additive and no save format changes.

## What this deliberately does NOT do

- **No art.** The Watch Post wears the veil projector's model, chosen because its
  `dish` child already spins under `ScanRig` and reads as a sensor. It is a 1x1
  building wearing a 2x2 model, which is visibly wrong rather than invisibly
  wrong. Bespoke model and icon owed.
- **The AI never builds one.** The commander's ladder yields seven common types
  and has never reached a faction building; a Sodality commander will therefore
  be blind to cloak exactly as it was. That matters more here than for most
  faction buildings, because this one is a *counter*, and it is a row of its own.
- **Detection is still per-entity and per-tick, unchanged.** No stickiness, no
  shared team detection (ADR-038 decided that vision does not flow), and no
  separate detection range.
- **Nothing about the Directorate changed.** It keeps the Sentinel Scout and
  gains nothing, which is the point: this row closes a hole rather than trading.

## Consequences

GDD line 56 is now true for both sides. `sodalitydetectorgate` (5 stages) pins
it, and three of them were **proved to bite** by reverting the fix and watching
the gate fail with the right message.

The measurement, which is the doctrine rather than a stat line:

```
cloaked raider past a Sodality turret - 150 hp with no post, 0 hp with one
```

Full battery exit 0; client harness PASS at 194 checks.

One process note worth keeping, because it cost a cycle and the selftest is what
caught it: a blind first-match string replace put `Detector: true` on the
**Shroud Nest** instead of the Watch Post. The `/data` round-trip named the
building, the field and both values in one line. Authored data that must
reproduce a compiled reference keeps paying for itself.
