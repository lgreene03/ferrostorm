# ADR-044: two superweapons, and the one number in GDD s8 I am refusing to change
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD s8 line 70; doc 24 C9; doc 27 DR-04; P7-5c, the last part of Q017

## Context

GDD s8 line 70 writes both sides precisely, which is why very little here is
invented:

> One superweapon per faction, ~6 minute charge, global map ping and audio
> warning at launch: **Directorate orbital cannon (huge single-point damage),
> Sodality seismic charge (wide, lower-damage area denial that also destroys
> resource fields - economic warfare flavour).**

The game had **one** superweapon, `FactionCommon`, and both sides built it and
fired it on the same timer. Q017's own measurement named this: "both factions
... fire the same superweapon on the same timer."

Its precondition shipped one wave ago. ADR-042 closed the hole that let *any
unit* delete a ferrite field with one shot, so "destroys resource fields" can be
this weapon's identity rather than a thing everything could already do.

## Decision

### 1. The Directorate's numbers do not move, again

Struct type 6 becomes Directorate-only with **not one number changed**. 900 Omni
inside a 1.5-cell core already *was* "huge single-point damage". This is the
third row running where the fix was to stop the other side sharing something
rather than to rebalance it.

### 2. The pair is identical except in effect, deliberately

Same 4000 credits, same 600 build ticks, same 150 power draw, same radar
prerequisite, same charge. GDD s8 gives both sides "one superweapon per faction"
on the same terms, so **the two are meant to be the same decision with different
consequences.** A cheaper or faster one would make the choice about price. The
gate asserts the parity directly, so a balance pass cannot drift them apart
without meeting that argument.

### 3. It shares `EntityKind.Superweapon`, and only the impact branches

Charging, the ready alert, the `LaunchSuper` command and the five seconds of
warning are all keyed on the kind and all of it applies unchanged. One `if` at
the impact site is the entire behavioural difference.

**And that immediately re-created P7-5a's defect**, which is worth recording
because it is now twice: the placement switch is keyed on `EntityKind`, so a
Sodality player ordering a seismic charge was handed an **orbital cannon**. It
was pre-empted here rather than discovered, and the gate stage that pins it was
proved to bite by reverting the fix:

> `a Sodality player ordered a seismic charge and got structure type 6 - the
> placement switch is keyed on Kind and the two superweapons share one, which is
> P7-5a's defect arriving a second time`

### 4. A separate effect function, NOT a widened `ApplyAreaDamage`

This is the load-bearing engineering decision. `ApplyAreaDamage` is **shared with
the mine detonation**, and its 1.5/3-cell shape is asserted by `minegate` and by
the artillery and superweapon scenarios. Adding a radius parameter would have put
every mine in the game one careless argument away from changing shape.

`ApplySeismicCharge` is its own function, and the gate's last stage asserts a
mine still cannot reach 5 cells, so the sharing is proved intact rather than
assumed.

### 5. The three invented numbers, with their alternatives

GDD s8 gives adjectives, not values.

| | chosen | why, and what lost |
|---|---|---|
| damage | **350** (cannon: 900) | "lower-damage". Measured on one factory at ground zero: **280 against the cannon's 720.** Rejected 600 as too close to read as a different weapon; rejected 150 as area denial that denies nothing. |
| radius | **3 inner / 6 outer** (cannon: 1.5 / 3) | "wide". Exactly double, so four times the area for under half the damage - a ratio a reader can hold, rather than two unrelated numbers. |
| fields | **destroyed outright** | "destroys" is the written word. Rejected draining a percentage: a half-emptied field is a slower version of harvesting it, not denial. |

Like the cannon and the mine it asks **no ownership question**: it hits the
firing player's own units and allies, and destroys whatever fields lie under it
including ground the launcher was mining. That is ADR-038's splash rule applied
unchanged, and a weapon that spared its owner's ground would make area denial
free.

## The one number in GDD s8 I am REFUSING to change

GDD s8 says **"~6 minute charge"**. Six minutes at 15 ticks per second is **5400
ticks**. `SpawnSuperweapon` defaults to **1500 ticks**, which is 100 seconds.

**The sim charges its superweapon 3.6 times faster than the GDD specifies.** That
is a real discrepancy against written doctrine, measured rather than estimated,
and I am not taking it.

### The argument that would have to be overturned

Charter A11: *"Any stat change >15% requires Balance + Game Designer co-sign."*
1500 to 5400 is +260%, and it is not a cosmetic number: the superweapon's charge
is the clock the whole late game is paced against, and `aisuper` (a golden
scenario) fires at tick 1186 and would simply stop firing. So this is a balance
decision with a golden regeneration attached, and it is exactly the class the
charter reserves.

It becomes takeable when any of these is true:

1. **Balance and Game Designer co-sign the change**, which is the charter's own
   route and the cleanest.
2. **A playtest finds 100-second superweapons dominate the 15-to-30-minute game
   GDD pillar 2 promises.** This is the likeliest, and it needs the playtest that
   every wave keeps arriving at.
3. **The GDD's "~6 minute" is amended** to match the sim, which is a Producer
   edit to s8 rather than an engineering row. Worth considering on its merits:
   the sim's number has been played against by nobody, but so has the GDD's.

Recording it is the point. Before this wave nothing anywhere noted that the sim
and the GDD disagreed by a factor of 3.6.

## Hash and format

**All 24 goldens byte-identical, measured**, for the mechanism ADR-042 used:
every seat defaults to Directorate, type 6 is unchanged, and `SpawnSuperweapon`
defaults to it, so every golden scenario builds and fires exactly what it always
did.

**Catalogue checksum MOVES, 0x2CADF63D66912E62 to 0xD2B80B9B8E87A2CA**, from the
new type. No save format change; `EntityKind` gains nothing because the pair
shares one.

## What this deliberately does NOT do

- **No new art or audio.** The seismic charge fires the same
  `SuperweaponImpact` event and wears the cannon's model, so a wide seismic blast
  currently looks exactly like a pinpoint orbital strike. GDD s8 also promises a
  "global map ping and audio warning" *per weapon*; both get the shared one.
  Owed to art and audio, and it is the largest gap this row leaves.
- **The AI never builds either.** Its ladder reaches struct type 6 by number
  (`!hasSuper && credits >= 4500 ? 6`), which is now a building a Sodality
  commander cannot build - so a Sodality AI will queue nothing there. **That is a
  live defect this row creates**, it is filed rather than fixed, and it is the
  same shape as the plant fix in ADR-042 clause 1.
- **No rename.** `dir_superweapon` keeps its id, so its sidebar button reads
  SUPERWEAPON while the Sodality's reads SEISMIC CHARGE. With two of them in the
  game that is not readable at a glance, and the rename is filed: the label
  derives from the **id**, so renaming the `name:` key alone would have created a
  fresh mismatch rather than fixing one.

## Consequences

Q017 is fully answered. `factionsuperweapongate` (6 stages) pins this, and two of
them were proved to bite.

```
a field under ground zero - destroyed by the seismic charge, untouched by the orbital cannon
one factory at ground zero - orbital cannon 720 damage, seismic charge 280
```

Full battery exit 0; client harness PASS.

One correction worth keeping, because it is the wave's own instance of the defect
this phase keeps finding: the gate's first draft asserted that the orbital cannon
**one-shots a factory** and the seismic charge does not. It does not - 900 Omni
against Structure armour is 720, and a factory has 1500 hit points. The assertion
was wrong about the game rather than about the row, and the summary line it fed
had to be rewritten too. **A gate that states a claim the measurement does not
support is a hand-maintained copy lagging its source**, which is the thing this
phase has now found roughly sixteen times.
