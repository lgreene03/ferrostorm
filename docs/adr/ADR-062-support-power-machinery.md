# ADR-062: support-power machinery, where the structure IS the permission

- Status: Ratified
- Date: 2026-08-03
- Deciders: Architect agent + Game Designer agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s8 line 71 and 72; GDD s3 lines 25 and 30; TDD line 11; ADR-044; P7-21

## Context

GDD s8's second sentence had **no machinery in the sim at all** - the largest
genuine feature gap left in P7:

> 3-4 minor support powers per faction on **shorter timers**, **unlocked by
> structures**.
>
> Design rule: every power has **counterplay (spread out, scout the structure,
> kill it)**.

Two sentences, and between them they specify the machinery almost completely
while naming no power. So the machinery is implementation and the roster is a
Game Designer's - which is how this row is split.

## Decision

### The power lives on the STRUCTURE DEF, and that is the whole design

`StructureTypeDef` gains `SupportPowerId` (0 = none). Not a per-player list of
unlocked abilities, not a new `EntityKind`, not a type-id table.

This is what makes s8's own counterplay rule **fall out of the data model
instead of needing to be arranged**: the permission *is* the building, so killing
the building removes the power with no bookkeeping that can get out of step. The
gate's counterplay stage passes because `ApplyCommand` already refuses a dead
entity's orders, not because anything was written to make it pass.

Asked as a property, never as a type id - the correction this phase has made
about seventeen times.

### The charge is DERIVED from the superweapon's, and stays derived

GDD s8 says "shorter timers". Shorter **than what**? The superweapon is the only
other timer in that sentence, so it is the bound.

```csharp
public const int SuperweaponChargeTicks  = 1500;
public const int SupportPowerChargeDivisor = 3;
public const int SupportPowerChargeTicks = SuperweaponChargeTicks / SupportPowerChargeDivisor;
```

Expressed as a **fraction rather than an absolute**, so "shorter" is true *by
construction*. **This matters because of ADR-044.** That ADR refused moving the
superweapon's charge to GDD's ~6 minutes (5400 ticks) as an A11 balance call, and
that refusal stands. If it is ever taken, support powers scale with it and s8 is
still honoured without anyone remembering to look here.

A **third**, chosen the way ADR-044 chose the seismic charge's radius of exactly
twice - a ratio a reader can hold. Rejected: a half, too close to read as a
different class of thing; a fifth, which at 300 ticks is twenty seconds and makes
a "power" into a cooldown.

Naming `SuperweaponChargeTicks` also removed a **hand-duplicated constant**: 1500
was written bare at two sites, `SpawnSuperweapon`'s default and the recharge.
Same value, so nothing moved.

### A separate command, NOT a widened LaunchSuper

`UseSupportPower` is its own command type, applying ADR-044 clause 4's argument a
second time. `LaunchSuper` is shared by two superweapons whose warning, strike
delay and impact are asserted by three gates and two goldens; adding a branch
would put both one careless condition from changing.

It also has **no strike delay and no global warning**, which is the design rather
than a simplification. GDD s8 gives the ping and the five seconds to the
superweapon *specifically*, and a dirty trick the victim is warned about has no
surprise left to trade on.

### It rides the catalogue checksum

`SupportPowerId` decides whether a command is **accepted** and what it does, so
two peers holding different answers would watch one machine's power fire and the
other's refuse, from the same command stream, while every stat in the game
matched. Folded beside the other decision-carrying columns.

**The catalogue checksum MOVES to 0x42CE3A6F39C31A9C** (from
0x48C6C9C2604BD3DE), by construction and deliberately.

### NO POWER HAS AN EFFECT YET, deliberately

This is the honest state of the row and it is a decision, not an omission.

**GDD s3 names five powers** - Directorate "surgical (orbital scan, precision
strike)", Sodality "dirty tricks (radar jamming, decoy army, tunnel
deployment)" - which is more than expected and was nearly missed: doc 29 recorded
support powers as "nothing at all" without noticing s3 already says which. But
**not one of them has a radius, a duration or a damage figure written anywhere.**

So the roster is filed as **Q021** with the three numbers each power needs, and
each arrives in its own wave with its own ADR. The gate proves the machinery by
registering a power on a carrier building **in its own world only**, so the
shipped catalogue acquires no ability nobody designed.

## Hash and format

**All 24 goldens byte-identical, measured.** Nothing in the shipped catalogue
carries a power, so the charge pass and the command are inert. Save format
**unchanged at v12**: the charge reuses `Entity.ChargeTicks`, which is already
serialised and already hashed, so there is no new per-entity state.

## The implementation failed its own first measurement

Expected by now, and the finding is worth more than the fix. The first version
let a **freshly built power building fire on the tick it landed**, because
`ChargeTicks` defaults to 0 and 0 means ready. A timer you can skip by rebuilding
the building is not a timer.

Fixed in `Add`, the one funnel every spawn passes through - there are two dozen
`BlockFootprint` sites and no shared structure helper, so any other choice would
have been the hand-duplicated shape this phase keeps finding. Keyed on the def
carrying a power, so it is a no-op for every building in the shipped catalogue.

## Proved to bite, and the second bite test found a redundancy

`supportpowergate`, four stages. **What it catches that no existing gate does:**
`aisupergate` and the two superweapon scenarios all assert the *superweapon*
path, and none can see a minor power - the command they exercise is deliberately
not the one this row adds.

Removing the charge check:

> `support power: a power fired while still charging - the timer decides nothing`

Removing the `Alive` guard, the counterplay clause - **and this is the one worth
recording, because it passed**:

> the gate went green with the rule apparently broken.

ADR-055's rule applied: the fixture was telling me about itself. Measured rather
than reasoned about, the cause is that **`Alive` is checked twice** - once in
`ApplyCommand` and again in `ApplyCommandCore` - and I had broken the redundant
inner copy. With **both** removed the stage fails correctly:

> `support power: the unlocking structure was destroyed and its power still
> fired ... a power that outlives its building has none`

So the counterplay is defence-in-depth, which was not known before this row and
is now recorded. Stage 3 carries its **control first** (a charged power on a
living structure must fire), or it would be satisfied by a power that never works
at all - ADR-059 stage 2's rule.

After both reverts the goldens were re-measured against the file and matched.

## The correction this row owes ADR-060

ADR-060 excluded the Veil Projector from the defence ladder for having no weapon,
and said it "belongs with support machinery". **That was speculation and it is
wrong.**

Measured: the Veil Projector already works, and it is a **persistent aura** -
powered projectors cloak nearby friendly mobile units every tick, and a brown-out
drops the veil. It has no charge, no cooldown and no activation. Making it a
timed power would be a *design change to a working building*, not an
implementation of GDD s8.

The real gap ADR-060 found is the one it measured: **no commander ever builds
one**. That is a ladder question, not a support-power question, and it stays
open.

## Consequences

GDD s8's machinery exists. The next wave authors a power, and Q021 recommends
**orbital scan** first - the only one of the five whose effect needs no new
combat or unit machinery, since per-player fog already exists.

Nothing here can be judged by a gate. A support power's whole value is how it
feels to be on either end of, and that is a playtest.
