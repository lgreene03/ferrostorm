# ADR-064: the precision strike, and the cardinality limit that blocked it

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Balance agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 line 25; GDD s8 lines 71-72; ADR-044; ADR-062; ADR-063; Q021; P7-23

## Context

Q021 holds GDD s3's five named support powers. ADR-063 shipped the first,
the orbital scan, on the Bastion. This row takes the **precision strike**,
completing the Directorate's surgical pair.

**And it could not be built.** ADR-062 put one `SupportPowerId` on the structure
def, which was right in shape and wrong in cardinality. Measured:

| side | faction-exclusive buildings |
|---|---|
| Directorate | **3** - power plant, Bastion, orbital cannon |
| Sodality | 5 |

Of the Directorate's three, the **orbital cannon cannot carry a power at all**
(ADR-063: it already uses `Entity.ChargeTicks` for its own cycle, and one entity
cannot hold two independent charges), and the **power plant is the first
building anyone raises**, so a power gated on it is gated on nothing.

That leaves **one usable building**. GDD s8 asks for **"3-4 minor support powers
per faction"**. One power per building could never satisfy the specification -
it caps the Directorate at one, and this row is the second.

So the row is two things that are really one: fix the cardinality, and prove it
with the power that needed it.

## Decision

### A building unlocks a LIST of powers

`SupportPowerId` becomes `SupportPowerIds`, an `int[]?` following the `Prereqs`
precedent exactly - null for none, so a building with no powers contributes
nothing to `Equals` or to the checksum fold. The command names the power in
`AuxId`, and a power the building does not grant is refused: **the list is the
permission**, exactly as the single id was.

### The powers on a building SHARE its charge

This is a design decision, not a shortcut for avoiding per-power state.

A shared charge makes the choice between a building's powers a **real one** - a
Bastion holds a scan **or** a strike ready, never both. That is a tactical
decision the player makes, and it is what "surgical" ought to feel like: one
platform, one shot ready at a time.

**Rejected: an independent charge per power.** It needs per-(entity, power)
state, a side collection with its own hash fold and save block, it moves
goldens - and it buys the player nothing except never having to choose.

**To overturn:** a design intent that powers on one building are unrelated
capabilities rather than uses of one platform. Nothing says that today, and if a
future building wants independent timers the answer is probably a second
building.

### The strike's numbers, both derived

| | chosen | derivation, and what was rejected |
|---|---|---|
| damage | **300** | A **third of the orbital cannon's 900** - the same ratio ADR-062 gave the charge, so a minor power does a third of the major one's damage on a third of its clock. One idea in two places. It lands below both calibration points (the cannon's 900 and the seismic charge's 350) without being told to, which is what "minor" has to mean. Rejected: half the cannon (450), which exceeds a *superweapon* and so is not minor at all; a tenth (90), a scratch against a 2000-hp refinery. |
| radius | **the cannon's own 1.5-cell core, one band, no falloff** | Derived from a measured number rather than picked. The cannon is 1.5/3 and the charge 3/6; this is the cannon's core and nothing outside it. **A ring of half damage would make it a smaller superweapon**, which is the one thing GDD s8 says a minor power is not. |

Naming `OrbitalCannonDamage` removed a bare `900` from its only site - the
calibration point needed a name before anything could be derived from it.

### It leaves ferrite fields alone, and that line is the Sodality's identity

GDD s8 gives *"destroys resource fields"* to the seismic charge **alone**, as its
economic-warfare flavour. A Directorate power that quietly did it too would take
that identity away without a word being written. Asserted, and proved to bite.

### Its own effect function, for the third time

`ApplyPrecisionStrike`, never a widened `ApplyAreaDamage` - ADR-044 clause 4's
argument unchanged: that function is shared with the **mine**, `minegate`
asserts its shape, and a radius parameter would put every mine in the game one
careless argument from changing.

Like the cannon, the charge and the mine it asks **no ownership question**: it
hits whatever stands under it, including the firing player's own. ADR-038's
splash rule, applied unchanged.

## Hash and format

**All 24 goldens byte-identical, measured** - twice, once after the cardinality
change and once after the strike. The charge stayed where it was
(`Entity.ChargeTicks`), which is precisely what the shared-charge decision bought:
an independent-charge design would have moved them.

Save format **unchanged at v13**. **The catalogue checksum moves to
0xA5544FFFAFAA8B5D**, because the def's power column changed shape and the
Bastion now grants two.

## Proved to bite, and what this gate catches that no other does

`precisionstrikegate`, six stages, **control first** - an unstruck refinery must
survive the same span at full health, or every stage below passes on a building
that was dying of something else.

`orbitalscangate` proves a power that **reveals** and asserts nothing about
damage; `supportpowergate` proves the machinery with a power that does nothing at
all. Neither can see a strike, and **nothing anywhere asserted that a building
may hold more than one power**.

- Field protection removed → *"a Directorate strike damaged a ferrite field ...
  would take that identity away"*.
- Core widened to 8 cells → *"a refinery 6 cells from the aim point lost 240
  health ... a ring of half damage would make it a smaller superweapon"*.
- Charge not spent on firing → *"the strike fired on the same charge the scan had
  just spent ... otherwise the choice between them is not a choice"*.

Stage 5 also asserts the strike **works again after recharging**, or a shared
charge would be indistinguishable from one power being disabled.

Stage 2 asserts against the **derivation** (a third of the cannon, through the
live `/data` damage matrix) rather than the literal 300, so the number and the
rule that produces it cannot drift apart.

After every revert the goldens were re-measured against the file and matched.

## Consequences

The Directorate's pair is complete. Q021's remaining three are the Sodality's
dirty tricks, and the cardinality fix means the **Watch Post, Shroud Nest, Veil
Projector and generator** are all now available to carry them - the Sodality has
five exclusive buildings and never had this problem, but it would have hit the
same wall at four powers.

Still nothing here says whether 300 damage in a 1.5-cell core, on a 500-tick
shared timer, is worth building a 1400-credit Bastion for. That is the
thirteenth ADR to want a playtest.
