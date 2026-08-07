# ADR-069: four produced-at tautology prerequisites are emptied, not tier-gated
- Status: Ratified
- Date: 2026-08-07
- Deciders: Luke (Game Designer authority) + Architect agent
- GDD/TDD feature served: GDD s5 (tech tree); Q020; ADR-058 (which raised it)

## Context

A unit's `prerequisites` list names structures that must stand before it can be
ordered; its `produced_at` names the structure it comes out of. When a
prerequisite names the unit's own `produced_at`, the clause can never refuse
anything, because the order is only accepted at that building in the first place.
It reads as a tier gate and is not one.

P7-16 (ADR-058) found four units carrying that shape while answering Q006 for the
MCV, and deliberately did not take them: curating a unit's tier is a balance and
pacing call reserved for the Game Designer, not a tidy-up a neighbouring row may
help itself to. It filed Q020 and left `mcvtechgate` stage 3 asserting the count
was exactly four, so a fifth could not appear unnoticed and the MCV could not
regress. The schema's own note already said "the producer IS a prerequisite; do
not repeat it in the prerequisites list", so the four defs were authored against
their own schema's advice.

The four:

| id | unit             | produced_at  | prerequisite (was) |
|----|------------------|--------------|--------------------|
| 8  | dir_howitzer     | com_factory  | com_factory        |
| 9  | sod_phantom_tank | com_factory  | com_factory        |
| 10 | dir_bulwark_tank | com_factory  | com_factory        |
| 15 | com_strike_flyer | com_airfield | com_airfield       |

## Decision

Empty all four. The reading taken is that a rule which never refuses is not a
tier the designer chose to keep; it is a fake gate that misleads a reader into
thinking the tier is gated. Emptied, the four units are available as soon as
their producer stands, which is exactly what the sim already did. Both the `/data`
yaml (`prerequisites: []`) and the compiled reference in `World.cs` are changed
together, so the bare-`World` reproduction still matches.

`mcvtechgate` stage 3's count is retired to ZERO and repurposed to guard the
CLOSED class: it now asserts no produced-at tautology exists at all, so any new
one, or the MCV regressing into the shape Q006 removed, fails by name. This is
strictly stronger than the magic-number count it replaced.

The heavy factory roster (howitzer, phantom, bulwark) is therefore NOT put behind
a real tier gate. That was the considered alternative and it is a legitimate
design, but it is a balance change dressed as a defect fix, and the honest reading
of a rule that never bites is to remove it rather than to make it bite for the
first time under cover of a clean-up.

## Alternatives rejected

**Tier-gate the three factory units behind the Radar Uplink, empty only the
strike flyer.** Arguable on pacing grounds (they are the expensive end of the
factory roster), and it is a real option. Rejected because it changes when a
commander can field its heaviest units, which moves AI goldens and is a balance
decision that wants a playtest number, not a rider on a tautology clean-up. The
strike flyer being the airfield's only unit makes an empty prerequisite there
almost unarguable, but splitting the answer by unit is precisely the tier
curation Q020 reserved, and the ratified reading is that none of the four was a
gate to begin with.

**Leave them as-is (the deferral P7-16 took).** Correct while the question was
open; the gate held the line. With the question answered there is no reason to
keep four rules that read as gates and are not, and every reason to remove a
recurring source of the "a rule keyed on an instance" confusion this project has
found around seventeen times.

## Consequences

Q020 is closed. `mcvtechgate` now proves a property (no tautology) rather than
counting instances, which cannot go stale the way the World.cs comment it
replaced did. Goldens are byte-identical across all 24 scenarios, measured: the
emptied clause never refused an order, so no command stream moved. The catalogue
checksum moved by construction, from `0xFA2DFE52192660BA` to `0xB4E6F043C4A872CC`,
because a prerequisite is part of the unit def folded into it; this is a
replay-compatibility bump of the pre-first-public-build kind, agreed here.

We are committed to these four units being available as soon as their producer
stands. Overturning that is a Game Designer tier decision like any other: name
the gate, move the prerequisite to it, update the compiled reference, and
`mcvtechgate` will then assert one tautology-free tree with a real gate in place
of a fake one.
