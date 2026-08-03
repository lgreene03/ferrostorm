# ADR-058: the Radar Uplink absorbs the Tech Centre role, and GDD line 47 starts being enforced

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s5 line 47; ADR-009 clauses 2 and 7; Q006; P7-16

## Context

GDD s5 line 47:

> Both factions can build replacement MCVs at the Factory **once a Tech Centre
> exists.**

No Tech Centre exists anywhere in this project beyond that sentence. There is
no such building in `/data`, no `EntityKind`, no compiled def, nothing in the
sidebar. The clause had therefore never been enforced, and `com_mcv.yaml`
carried this instead:

```yaml
prerequisites: [com_factory]
```

Which is a **tautology**. The MCV's `produced_at` is the factory, so ADR-009
clause 2 already refuses the order anywhere else. A prerequisite naming the
building a unit comes out of can never refuse anything. Line 47 read like a
tier gate and gated nothing, and the file said so in a comment that had been
waiting for a decision since 2026-07-17 (Q006).

## Decision

**The Radar Uplink absorbs the Tech Centre role.** `com_mcv`'s prerequisite
becomes `[com_radar_uplink]`.

This is Q006's own recorded candidate, carried independently by doc 22 BD-17
clause 7 and doc 23 s4.3. The reasoning that decided it:

Line 47's intent is **"MCV replacement is tier-gated"**, not "a building
literally named Tech Centre must exist". The Radar Uplink is already the tier
gate this tree has: the superweapon, the airfield and five units all wait
behind it. Reading the sentence as a requirement for a *name* rather than for a
*property* would add a structure whose only job is to be a prerequisite.

That is the same correction P7 has now made about sixteen times, and it is
worth stating in the general form because it keeps paying: **read a rule as the
property it means rather than the instance it names.**

### Rejected: build a Tech Centre

A real `com_tech_centre`: new `EntityKind`, def, YAML, art, sidebar entry,
faction variants, AI ladder rung, and a place in the checksum.

Rejected on three grounds. It satisfies a **name** rather than a **need** - the
game already has a tier gate and would then have two, with nothing to say about
which units sit behind which. It is a roster addition, and roster additions are
C9, Luke's call, not a side effect of enforcing an existing sentence. And doc
29's roster analysis listed a Tech Centre as a missing building on exactly this
reasoning; **doc 29 was wrong to**, and is corrected in this row.

**What would have to be overturned to revisit this:** a design intent that the
radar and the tech tier are *different* tiers, with units that should be
available at one and not the other. Nothing in the GDD says that today. If it
is ever wanted, this ADR reverses by moving one prerequisite.

### Rejected: leave it as authored and change the GDD

Honest, and cheaper still - delete "once a Tech Centre exists" from line 47.
Rejected because tier-gating the MCV is *good*, and the sentence is the only
thing in the design that stops instant re-expansion after a yard kill. The
clause deserved enforcing, not deleting.

## The AI moves in the same change, and does not work yet

Q006 named the trap: *"whatever the MCV waits on, the AI must know to build it
first or it saves 3500 credits forever"*. `SkirmishAI`'s expansion gate used to
read `factory >= 0`, a **hand-kept copy** of the MCV's prerequisite, with a
comment binding whoever answered Q006 to move both together.

It now asks the data instead:

```csharp
bool canBuyMcv = w.HasPrereqs(_player, w.GetUnitType(World.McvUnitType).Prereqs);
```

Whatever the MCV waits on, the AI waits on the same thing, and there is no
longer a second copy to fall behind. That is strictly better than moving the
copy, which is what the old comment asked for.

**And it cannot currently be observed to bite. That is recorded here rather
than glossed, because ADR-055's rule is that a bite test which passes tells you
about the fixture.** Deleting `canBuyMcv` changed the gate's measured
behaviour not at all - tick for tick - and four successive fixtures failed to
find a state where it discriminates. The measured reason is a **separate
defect**: the army block is also gated on `wanted == 0`, and a commander that
cannot satisfy its yard ladder freezes completely before this guard is ever
consulted. Filed as `docs/tickets/DEFECT-AI-yard-loss-freeze.md`, with the
controlled measurement (same world, yard present: 9000 credits spent; yard
absent: **zero**, forever).

The guard is kept because it is correct, free and reads as the rule it means.
It is not claimed to be tested.

> **CORRECTED 2026-08-03 by ADR-059.** Two things above are wrong. The filed
> defect's DIAGNOSIS was wrong - `Act` returns at `if (cy < 0)` a hundred lines
> before the army block, into DR-10's deliberate last stand, and the measured
> silence was a fixture with no enemy structures rather than a freeze. And the
> claim that the guard cannot be observed to bite was too strong: a fifth state,
> the tier gate destroyed while the yard survives, does discriminate (guard on,
> 9 units; guard off, 10). The guard is reachable; it is still not carrying
> much. The real defect that state was hiding is ADR-059's: DR-10 fire-sales
> without ever asking whether it could BUY an MCV.

## Hash and format

**One golden moved, measured:** `expansion 2026`, `0x7A6AA4D3238DF294` ->
`0xCCF833DEB3E10B68`. The other 23 are byte-identical.

That is the expected and desired result, and Q006 predicted it in as many
words: *"the answer arrives as a behaviour change inside a golden regeneration
rather than as a free YAML edit"*. `expansion` is the one scenario whose whole
subject is the MCV. Measured cause of the move: the commander now reaches its
tech gate at tick 1860 and its MCV at tick 2379, a 519-tick (34 second) gap
where it previously bought the MCV as soon as it could afford one. The
expansion is **delayed and tier-gated, not blocked** - the scenario still
founds its second base, adds its refinery and mines the far field to zero.

Regenerated under the standing authorisation, which permits regeneration where
**measured** and carried by an ADR.

## Proved to bite, and for the right reason

`mcvtechgate`, four stages. Reverting the prerequisite to `[com_factory]`:

> `mcv tech: a player with a Factory but no tech gate must NOT be able to order
> an MCV ... a prerequisite naming the factory the MCV is produced AT is a
> tautology and gates nothing`

Stage 1 asserts the **property** (a factory alone refuses an MCV), not the type
id the answer picked, so it would pass unchanged had Q006 resolved to a
purpose-built Tech Centre. Stage 2 asserts the converse, because stage 1 alone
is satisfied by an MCV nothing can ever build.

## The defect stage 3 found on its first run

Stage 3 derives the list of produced-at tautologies and asserts the count.
`World.cs` had a hand-written comment naming them: *"the four ... (mcv,
howitzer, bulwark, phantom)"*. The derived count came back **four with the MCV
already removed** - so there were five, and the extra was `com_strike_flyer`,
authored by ADR-028's air layer as `Prereqs: [16], ProducedAt: 16` and never
added to the comment. The same comment also attributed them to Q007, which is
about where the **engineer** is built.

A comment documenting the hand-kept-list-lags-its-catalogue defect had itself
become an instance of it. The list is now derived; the remaining four are filed
as **Q020** for the Game Designer, and counted rather than quietly fixed,
because curating a unit's tier is not a tidy-up.

## Consequences

Q006 is **ANSWERED** after seventeen days. GDD s5 line 47 is enforced for the
first time. All 18 local CI gates green; client harness PASS.

Two things this leaves behind on purpose: **Q020**, the four remaining
tautologies, and **the yard-loss freeze**, which is the more serious of the two
and is the reason this row's AI guard cannot yet be proved.
