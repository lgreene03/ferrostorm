# ADR-060: each side defends with its own hardware, bought from surplus

- Status: Ratified
- Date: 2026-08-03
- Deciders: AI Engineer agent + Balance agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s5 faction identity; GDD s3 doctrine; ADR-045 (P7-5d); P7-18

## Context

P7-2b shipped the faction defences - the Directorate's **Bastion** and the
Sodality's **Shroud Nest** - and `FactionDefenceGate` proved the catalogue was
right: each side can build its own and not the other's, and the stats express
GDD s3 doctrine rather than being two turrets with different price tags.

**Nothing checked that a commander ever built one.** Measured, both sides put up
exactly one **common turret** and stopped:

| commander | defence structures built in 12,000 ticks with 60,000 credits |
|---|---|
| Directorate | `t5` Turret x1. No Bastion. |
| Sodality | `t5` Turret x1. No Shroud Nest, no Veil Projector. |

So a Sodality base and a Directorate base were **defensively identical**, and
the two buildings P7-2b shipped were dead hardware sitting in the catalogue and
the sidebar. This is exactly what ADR-045 found one rung further down the same
ladder for the superweapon and the Watch Post, and the cause is the same shape:
the rung read `!hasTurret ? 5`, **the last hardcoded type id in the ladder**,
after P7-5d converted the plant, detector and superweapon rungs to capability
queries and left this one.

Doc 24 had recorded these as *"balance additions rather than defects, since the
common turret works for either side"*. That judgement is respected and narrowed
rather than overturned: the common turret does work, which is why this row
**adds** rather than swaps.

## Decision

A new ladder rung, after the common turret, asking `BuildableFactionDefence`.

The query is **derived from the catalogue**, not a list of type ids, because a
hand-written list has failed this project repeatedly (P7-16 found one wrong since
ADR-028). Three conditions, each meaning something checkable:

- the **Defence tab**, the catalogue's own statement of role;
- an actual **weapon**, which excludes the Veil Projector;
- **faction-exclusive**, since the common turret is the rung above and this one
  exists to say what the sides do differently.

A third faction defence would be picked up the day it is authored.

### The Veil Projector is excluded, deliberately

It sits on the Defence tab and has **no weapon**: it is a cloak field, not a
gun. A ladder rung asking for defence must not answer an attack by buying a
cloak. Gate stage 3 asserts this, so the query cannot drift into "anything filed
under Defence".

That leaves the Veil Projector still unbuilt by any commander, which is
**correct for this row and wrong for the game**. It is support machinery, and it
belongs with GDD s8's support powers - the largest remaining feature gap - not
smuggled in under a defence rung. Filed, not fixed.

### It is bought from SURPLUS, and this is the part that was measured into shape

The rung is gated on `Credits >= Cost + 1500`, matching the radar's and
superweapon's existing affordability thresholds.

**Without that gate the row flipped the faction war completely.** Measured on
the balance tool:

| ladder | faction war |
|---|---|
| before this row | Directorate **6 - 0** Sodality |
| rung with no affordability gate | Directorate **0 - 6** Sodality |
| rung gated on cost + 1500 (shipped) | Directorate **6 - 0** Sodality |

The cause is not subtle: the Directorate's Bastion costs **1400 and waits behind
the radar**, the Sodality's Shroud Nest costs **400 and needs only a plant**. Buy
both unconditionally out of the opening budget and the Directorate pays three
and a half times as much, at a worse time, and loses every match.

The threshold is principled rather than tuned to a number - "buy your signature
defence from surplus, not instead of your army" is the same rule the radar's
1500 and the superweapon's 4500 already state - but it was **the measurement
that revealed it was needed**, and that is recorded here rather than presented as
foresight. It is load-bearing, not decorative: the `mission` golden differs
between the gated and ungated versions, so it binds in real play.

### Rejected: replace the common turret with the faction defence

Rejected. The common turret is anti-armour and cheap; neither faction defence is
either of those things. Swapping them is a balance change to the opening dressed
as a fix, and it would delete the counter-triangle a base needs. **Gate stage 2
is the control that stops this row quietly becoming that change**, and it was
proved by removing the turret rung and watching it fail.

**To overturn:** a design decision that the common turret is redundant once a
side has its own defence. Nothing says that today.

### Rejected: build several, or rebuild when destroyed

The rung stops at one, like every other rung in this ladder. Defence density is
a balance question, and this row is about identity. **To overturn:** a playtest
showing one faction defence is decorative.

## Hash and format

**Four goldens move, measured:** `skirmish`, `expansion`, `aisuper`, `mission` -
every AI-driven scenario, and no others. The commander now buys one more
building, so this was expected rather than discovered.

| scenario | from | to |
|---|---|---|
| skirmish | 0xDCD6A3480A8E0E22 | 0x2DC6B7CC141FC20A |
| expansion | 0xCCF833DEB3E10B68 | 0xEECA2D1C61A23359 |
| aisuper | 0x9CC770F9029970FD | 0x6A39F0D6EFA0B8BC |
| mission | 0x3DB6A9D39E31617C | 0x6D491D77B5C4FD6D |

Regenerated under the standing authorisation. Every scenario assertion still
passes on its own terms - the skirmish still runs its full loop, the expansion
still founds and mines its second base, the superweapon still charges and fires.

## Proved to bite, and for the right reason

`aidefenceladdergate`, three stages. **Not a duplicate of `FactionDefenceGate`**,
and ADR-054's rule asks every new gate to say what it catches that an existing
one does not: that gate asserts the catalogue and passed throughout, because it
was right - the hardware existed. This one asserts the commander builds it, which
nothing checked and which was false for both sides.

Disabling the rung:

> `faction defence: a faction 0 commander with sixty thousand credits and twelve
> thousand ticks never built its own defence (struct type 17)`

Removing the common turret rung instead, to test the control:

> `faction defence: a faction 0 commander stopped building the COMMON turret.
> This row ADDS a side's own defence after it`

No stage names the Bastion or the Shroud Nest, so a third faction defence would
not need the gate rewritten.

After both bite tests were reverted, the goldens were re-measured and matched
the regenerated file exactly - which is the check that proves a revert left
nothing behind.

## The balance finding this row exposes, filed not fixed

**The Bastion is poor value against the Shroud Nest**, and the evidence is the
0-6 flip above: making both commanders buy their own defence unconditionally
loses the Directorate every match. 1,400 credits behind a radar against 400
behind a plant is a three-and-a-half-fold difference in price and a tier
difference in timing, for defences meant to be each side's answer to the same
problem.

Charter A11 territory, and **not this row's to fix**: it is a stat change, it
needs Balance and Game Designer co-sign, and the honest test is a playtest rather
than a tool that already reports 6-0 in one direction and calls it a pass. Filed
as `docs/tickets/BALANCE-bastion-value-vs-shroud-nest.md`.

## Consequences

The last hardcoded type id leaves the AI ladder; every rung is now a capability
query. A Sodality base and a Directorate base no longer look the same.

Two things left behind on purpose: the **Veil Projector**, which belongs with
GDD s8's support machinery, and the **Bastion's value**, which belongs to a
human.
