# ADR-061: wall tiers refused, and the column that should have shown why

- Status: Ratified (a REFUSAL, with the overturning argument recorded)
- Date: 2026-08-03
- Deciders: Balance agent + Game Designer agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: doc 24 B7; ADR-005; doc 29 s3.1; P7-19

## Context

Doc 24 B7 flags *"one wall type at a flat 100 credits, where the benchmarks
tiered barriers by cost and durability"*. Doc 29 s3.1 already recommended
refusing it on three lines of evidence.

**This row re-derived the refusal rather than deferring to it**, because doc 29
has been wrong before in exactly this way: it filed the Tech Centre as a missing
building when Q006 had already recorded a cheaper answer (ADR-058), and ADR-048's
refusal was itself wrong and had to be corrected by ADR-049. A refusal on file is
not evidence; it is a claim, and this one rests on a number that can be measured.

## The first fact, and it is decisive

**The GDD says nothing about walls. Not one sentence, not one word.** Searched
in full: zero occurrences.

So there is no design intent to implement here. Every property a tier would have
- its cost, its hit points, what it blocks, when it unlocks - would be invented,
and invented on top of a feature that already works: ADR-005 shipped barriers and
P7-10 shipped gates. "Check what is written in the GDD before inventing" has paid
ten times in this phase, and this is the case it describes exactly.

That alone is sufficient. The measurement below is what makes the refusal
*informative* rather than merely cautious.

## The measurement, re-run rather than cited

The balance tool's siege fixture, measured fresh:

| Besieger | Wall | Yard razed | **Ticks bought** | Army retained |
|---|---|---|---|---|
| howitzer | none | t=1248 | - | 90% |
| howitzer | gapped | t=1248 | **0** | 90% |
| howitzer | sealed | t=1477 | +229 | 90% |
| rifle_squad | none | t=2046 | - | 73% |
| rifle_squad | gapped | t=1722 | **-324** | 86% |
| rifle_squad | sealed | t=3438 | +1392 | 73% |
| cannon_tank | none | t=1486 | - | 80% |
| cannon_tank | gapped | t=2048 | +562 | 40% |
| cannon_tank | sealed | t=2040 | +554 | 60% |

**A wall's value is dominated by TOPOLOGY and by the attacker's weapon, not by
durability.** Sealed versus gapped swings the result by hundreds or thousands of
ticks; against artillery a gapped wall is worth exactly **zero**, because the
howitzer's range 9 beats the turret's range 5 and it never touches masonry.

A tier that adds hit points changes the variable the data says matters least. A
tougher wall the enemy still never shoots is still worth nothing.

Two corrections to the record while re-measuring. Doc 29 quotes **235 ticks** for
the sealed-versus-artillery case; it measures **229** today. The siege fixture
draws no random numbers and runs no AI commander, so this is drift from some
earlier change rather than noise, and 229 is the number now. And doc 29 called
the gapped-artillery result "worth zero ticks", which is right, but it is only
the second most interesting row in the table.

## The finding the table was hiding, and the column that now shows it

**Against rifle squads, a gapped wall makes the yard fall SOONER than no wall at
all: 1722 against 2046, a loss of 324 ticks.** The defender's fortification
helps the attacker.

This has been printed in this report for many waves, in plain numbers, unseen -
because seeing it meant subtracting two non-adjacent rows in your head. Only the
howitzer had a derived "ticks bought" figure, in a separate summary table below.

So this row adds a **`Ticks bought` column to every besieger**, signed. That is
the phase's own rule applied literally: *when a table is confusing, add a column,
not a theory.* It is the whole code change in this wave.

**Hypothesised cause, EXPLICITLY UNVERIFIED.** Army retained rises from 73% to
86% with the gap, so the doorway appears to be *safer* than the open field: the
attackers funnel through a two-cell corridor at the edge of both turrets' range
rather than crossing an open front under fire, and arrive sooner and more
intact. The fixture's comment asserts "a two-cell doorway the turrets cover", and
the geometry makes that marginal - the turrets sit at range 4.5 and 5.0 from the
doorway cells against a turret range of 5.

**This is a hypothesis with supporting evidence, not a diagnosed cause**, and it
is recorded as such because this phase has twice paid for treating a measured
symptom as a measured cause (ADR-059). Settling it needs one more measurement:
where the attackers actually die in each shape. It is also quite possibly a
property of *this fixture's geometry* rather than of the game.

**Not filed as a defect**, for that reason, and because a wall that funnels
attackers into a killing corridor is a legitimate design property - it is only a
defect if the corridor is not actually covered.

## Decision

**Wall tiers are REFUSED.** No second barrier tiered by cost and durability.

### What would have to be overturned to take this row

Any one of these, and the refusal should be revisited:

1. **A GDD sentence about walls.** There is currently none. If the design
   acquires an intent for barriers, that intent - not benchmark parity - is what
   should be built.
2. **The measurement changing.** If a gapped wall ever starts buying meaningful
   time against artillery, durability becomes a lever worth pulling. The new
   column is where that would show up.
3. **A playtest saying bases feel unfortifiable.** No tool can report this, and
   it is the argument most likely to be right.

### Rejected: tier by DURABILITY (the row as specified)

Rejected on the measurement above, and on two external precedents that doc 29
gathered and this row re-checked as still standing. Age of Empires II built a
Fortified Palisade Wall, doubling the cheap tier's hit points, and **removed it
from multiplayer** because there was no good way to implement it without making
the game too defensive. Tempest Rising (2025), the genre-current title, ships one
wall tier plus a gate plus wall-mountable turrets and spends its complexity
budget on attachment and placement ergonomics instead.

### The alternative, recorded and NOT taken here: tier by RULE

Where barrier tiering worked in the genre, it bought **differing rules** rather
than durability - one barrier blocking movement but not line of sight, another
blocking both. The cheap tier's real function is a *tempo tool*: fast,
uncommitted, placeable mid-fight.

If a second barrier is ever wanted, that is the shape to build: a cheap, fast,
low-HP barrier that blocks infantry but not vehicles. **Recorded as a direction,
not adopted**, because it would still be invention with no GDD sentence behind
it, and because point 1 above should come first.

## Hash and format

**All 24 goldens byte-identical, measured.** The only code change is a derived
column in a reporting tool, plus the removal of four unused constants; nothing in
`/sim` moved.

## No gate, deliberately, and this is the project's own rule

The method says: decide gate-versus-probe by whether the property is
**correctness** (assert) or **balance** (report). A wall's worth in ticks is
balance. Asserting it would freeze a number that ought to move when the game is
tuned, and would make the tool's report a tautology.

The correctness half is already asserted and stays: the tool hard-fails if
artillery cannot raze a walled base (GDD s6 line 53), if the siege is pyrrhic, if
a turret is left standing, or if a sealed base falls without a single segment
breached. And the seeds-agree tripwire still guards determinism.

So the deliverable that carries this refusal is the **column**, not a gate: the
next person to look at that table will see `-324` without doing arithmetic.

## The tidy-up this row also owes

P7-16 left four unused constants in `McvTechGate` under a comment claiming one of
them, `TechGate`, was *"the one place a reader must look to see what Tech Centre
resolved to"*. Nothing used it; the stages spawn the tier gate through
`SpawnRadarUplink`. **The comment described code that was not there.** Deleted
rather than wired up - the answer to Q006 belongs in ADR-058 and in
`com_mcv.yaml`, and a gate should assert behaviour rather than restate a decision
in a constant. The build now emits zero warnings.

## Consequences

Doc 24 B7 is **closed as refused** rather than left open, with the overturning
conditions above. The largest genuine gap left in P7 is now GDD s8's support
powers, which - unlike walls - the design does specify.
