# ADR-024: a second resource type

- Status: **Proposed.** Deliberately NOT ratified under the standing directive,
  unlike ADR-019/021/023. Those implemented features the GDD already named; this
  one would AUTHOR a design the GDD does not have. See "Why this one is not
  self-ratified" below. Blocked on docs/questions/Q014.
- Date: 2026-07-25
- Deciders: Luke (the GDD decision) + game-designer + Architect; sim-engineer to
  implement once ratified
- GDD/TDD feature served: doc 21 P4-PORT-04; doc 22 P5-ECON-12 (which specifies
  it); P6 campaign tracker wave C8

## Why this one is not self-ratified

Every C-series wave so far cited explicit GDD authority: the repair vehicle is
GDD line 62, the capturable income Outpost is line 41, the parallel build lanes
are line 45. Each ADR decided HOW, never WHETHER.

C8 is different. The GDD names exactly one resource, in section 4: "Ferrite
crystal fields. Regrow slowly from seed nodes; fields near spawns are finite
enough to force expansion by minute ~8." There is no second resource anywhere in
the GDD, doc 26, or the design package. Doc 21's own ticket says so plainly:
"per-type credit pools or exchange rates; **GDD decision first**."

Adding a second resource changes a core economic pillar. CLAUDE.md's scope rule
requires Producer sign-off for new units, factions or modes, and a new resource
is at least that significant; rule 5 forbids silently resolving a conflict with
the design package, and "the GDD says one resource, the audit asks for two" is
exactly such a conflict. So this ADR proposes and Q014 asks. It does not decide.

## The decision this proposes

**A second, richer grade of the same crystal, feeding the ONE treasury at a
higher yield. Not a separate currency.**

1. **Yield, not pools.** A rich field's ore banks more credits per unit removed;
   there is no second currency, no second counter, no second sidebar readout.
2. **`EntityKind.RichField = 18`**, the next free value in the append-only enum.
   No new hashed Entity field: it reuses `FerriteAmount` and `FerriteCap`, and
   the yield is a constant keyed on Kind. **No save-format bump.**
3. **The Loading branch takes a BRANCH, not a universal multiply.** Writing
   `take * yield / 100` for every field is arithmetically identical at yield 100
   for the values involved, but writing `if (Kind == FerriteField) ... else ...`
   makes the neutrality structural rather than an arithmetic argument, which is
   the standard this repo holds itself to.
4. **Every other sim edit is an OR-widening** of an existing
   `Kind == FerriteField` test into an `IsField(Kind)` helper (eight sim sites,
   three in SkirmishAI). Widening a predicate by a disjunct that is false for
   every entity in every golden cannot change any golden's evaluation.
5. **Rich fields do NOT regrow.** The regrowth filter stays ferrite-only, so the
   rich field is genuinely finite and contesting it is a real decision with a
   clock on it. This is a design choice, not a technical constraint, and it is
   the one most worth arguing about.
6. **A new map grid character**, plus a parallel `MapData.RichFields` list rather
   than a type channel on `Fields` (which would touch the selftest's fingerprint
   for no gain). Unknown grid characters already throw, so a new one is purely
   additive: a new loader reads every existing map unchanged. **No map-format
   version bump**, the same argument ADR-021 made for the structure line.
7. **Placed on skirmish-02 and skirmish-04 only**, leaving skirmish-01 (the map
   the `skirmish` golden loads), skirmish-03 (the frozen look-dev reference) and
   every mission map alone. This is verbatim the C4b pattern and it is what keeps
   the wave hash-neutral in practice rather than only in principle.

## The fact that decides the design

**Separate currency pools cannot be made hash-neutral.** `ComputeStateHash`
folds the treasury inside a per-player loop that always runs for every player on
every scenario; a second counter inserts an extra value into that stream
unconditionally, moving all 24 goldens by construction. There is no guarded-fold
escape of the kind ADR-023 used for the build lanes, because a currency array is
never absent.

Yield into the single treasury touches that loop not at all.

So the option that is cheap and the option doc 22 recommends on design grounds
are the same option, which is the happiest possible shape for a decision. Doc 22
reached "adopt yield, reject pools" from the single-credit-pool heritage of the
genre; the hash arrives at it independently.

## Alternatives rejected (subject to the Q014 answer)

**Separate currency pools.** The literal reading of P4-PORT-04's "per-type credit
pools". Rejected on two independent grounds: it contradicts the single-treasury
tradition the GDD's economy is built on, and it is an unavoidable 24-golden
regeneration plus an economy-wide rewrite of nineteen credit call sites.

**Two new Entity fields (doc 22's shape: a field type id and a per-entity
yield).** More flexible, and it would let Balance tune a field's richness per
instance from the map. Rejected for the first wave because it costs a save-format
bump and per-entity state for a value that is constant per kind; promoting the
constant to per-entity state later is additive and needs no rework.

**Doing nothing.** Defensible, and the honest default if Q014 comes back "one
resource is the design". The single-resource economy is coherent, and doc 22's
own framing is that the second grade sharpens map control rather than fixing a
defect. This ADR does not assume the answer is yes.

## Consequences if ratified

Easier: map control gains a second axis (a finite rich patch worth contesting on
a clock); doc 21's P4-PORT-04 closes; the loader stops being single-resource by
construction.

Harder: roughly seventeen client guards currently test
`Kind == EntityKind.FerriteField` individually, and each one missed is a distinct
visual or input defect (a selectable, shootable, health-barred, fog-hidden rich
field). The wave's real risk is there, not in the sim. A single exported
`IsField` predicate that every site routes through is the mitigation and should
be treated as mandatory rather than tidy.

Note a pre-existing defect the survey turned up, filed but unfixed: ferrite
fields never visibly drain in live play, because `ViewEntity` carries no amount
and `SpawnFerriteField` sets `Hp = 1`, so the client's drain scale is a constant
(doc 22 P5-ECON-01). A rich field would inherit that. Worth fixing in the same
wave since it is the same code.

Hash impact if ratified as proposed: NEUTRAL, by the ADR-019/021/023 pattern
(a new Kind no golden spawns) plus the C4b placement rule (absent from every
golden-covered map). The AI widenings are inert for the same reason C4c's outpost
capture was, and carry the same proof obligation: the five-seed determinism suite
plus an identical `skirmish` scenario summary.

Gates if ratified: an additive `multires` mode (standalone plus a Match stage,
never a golden scenario) asserting that a harvester banks the yield multiple
while the field decrements by the RAW amount, and that a ferrite field still
banks exactly one for one; plus the existing mapgate extended to the new maps.
