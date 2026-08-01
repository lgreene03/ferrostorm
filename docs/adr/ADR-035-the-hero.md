# ADR-035: the hero, and what "one at a time" costs to mean
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD s7 lines 62 and 64; doc 24 B4; P7-11b

## Context

**This is the first row in this phase where the design is invented rather than
implemented, and that difference is the whole reason this ADR is long.**

Everything before it was either something the GDD wrote down (the Infiltrator,
the Saboteur, the air layer) or something demonstrably broken (the seat
inversion, the peer-dependent handicap). Both have an external standard to be
wrong against. The hero has none. GDD s7 names it twice:

- line 62: `Commando (hero, one at a time)`
- line 64: `Shadow Commando (hero)`

and gives no ability, no stats, no tier. Doc 23 has moreover ruled that line 62
is "a sample, not a system statement", which is why P7-11b was refused three
times before authorisation.

So what follows is mine. The alternatives are recorded beside each choice so
that overturning one is an edit rather than an excavation.

## Decision

### 1. The ability is DEMOLITION on contact, and it is damage rather than deletion

A fourth effect on `CaptureSystem`'s shared contact shape, beside the engineer's
capture, the Infiltrator's theft and the Saboteur's disable.

**It applies 1000 damage through the ordinary damage path, not instant removal.**
That single choice is what makes the hero a decision rather than a guillotine:
a 150-hit-point power plant, an 800 barracks and the 900 veil projector die on
one visit; the 1600 Bastion, the 1500 factory and the 2000 refinery are badly
hurt and still standing. Armour class applies, the repair vehicle can answer it,
deaths make rubble, and the usual events fire. Nothing new was invented to
express it.

Rejected: instant removal. It is simpler, it is what "demolition" sounds like,
and it makes every building equal to every other building, which throws away the
entire hit-point column the game already has.

### 2. The hero SURVIVES, where the other three contact units are consumed

The engineer, the Infiltrator and the Saboteur are all spent by their act. The
hero is not.

A consumed hero is an expensive engineer. Surviving is what makes it a unit you
protect across a match, and it is what gives "one at a time" something to
protect. Its `ExplicitTarget` is cleared instead, so demolishing again means
being ordered again: that is the pacing limit, and it is deliberately not a
cooldown, because a cooldown is per-entity hashed state for a rule that walking
already expresses.

### 3. "One at a time" is BUILT, and generally

This is the one concrete thing the GDD does say about the hero, and there was no
machinery for it anywhere: the only per-player limit in the sim is
`MaxBarriersPerPlayer`. Shipping "the hero" while glossing the only specified
property would have been the dishonest half of the row.

`max_alive` is a unit-def column, authored in `/data`, **0 meaning unlimited**
and 1 for both heroes. It is enforced where a unit is QUEUED and again where
production COMPLETES, because a player could otherwise queue two while none
stands. The count is of living units of that type owned by that player.

Written as a general cap rather than a hero special case, so the next unit that
wants one is a data change. **It is a complete no-op at 0**, which is every
other unit, and that is what keeps all 24 goldens byte-identical - asserted
directly by a gate stage that produces six of an uncapped unit.

The completion-side behaviour is worth naming: a second hero already paid for is
HELD at full progress rather than refunded or discarded, and released on the
tick the standing one dies. Measured at 901 ticks in the gate. A refund would be
a different and defensible choice; holding was chosen because the player asked
for a hero and will get one.

### 4. The pair differs by exactly one property

`dir_commando` and `sod_shadow_commando` are the same unit authored twice,
differing in `faction` and in `stealth`. The Sodality's is cloaked, which is GDD
line 30's identity split, and it inherits decloak-on-firing and detector
vulnerability from the existing entity-level rules with no new machinery. The
P7-2b Bastion and Shroud Nest precedent, applied again.

`veterancy_enabled: false` for both. A rank-3 unit self-heals (GDD line 54), and
a self-healing hero that also demolishes buildings snowballs. The hero is
already the top of the ladder.

## Two pre-existing defects this wave found, neither its subject

**`UnitTypeDef.Air` was in neither `Equals` nor `CatalogueChecksum`**, from
ADR-028 until now. Both omissions were silent and the second was dangerous: a
drifting `air:` key was invisible to the `/data` round-trip selftest AND to the
LAN desync guard, so two peers could disagree about which units FLY while every
unit, building and gun still matched. Worse than the usual case, because ADR-028
clause 3 makes engagement an equality between a weapon's anti-air flag and its
target's airborne one, so the peers would disagree about what can even be shot.
Exactly the failure ADR-032 clause 2 names, in a field that predates the rule.
Both folds fixed.

**The Infiltrator crashed on a neutral outpost.** `CanBeActedOn` admits one
deliberately, because capturing a neutral outpost is ADR-021's whole feature,
and the theft branch then indexed `_credits[-1]`. An index-out-of-range
reachable by right-clicking an outpost, latent since P7-7. It now takes nothing,
mints nothing and does not consume the actor, and a gate stage proves it by
throwing when the guard is removed.

Both were found by adding a fourth effect to a method that already had three:
the act of generalising is what exposed what the existing branches assumed.

## Consequences

**All 24 goldens byte-identical, measured.** No golden spawns a type-19 or
type-20 unit, and `max_alive: 0` changes no existing path.

The catalogue checksum moves twice over: new units, a new weapon, a new def
column, and the `Air` fold that should have been there since ADR-028. Pre-existing
saves and replays refuse, on the pre-first-public-build argument.

What this does NOT deliver, stated so the row is not read as finished: **the
heroes have no sidebar button**, so they are unreachable in the client - the
same state the Infiltrator and Saboteur are already in, and a client wave of its
own. There is no bespoke art. The AI neither builds one nor recognises the
threat. And nobody has played against one, so 1500 credits, 200 hit points and
1000 damage are three numbers chosen by argument rather than by play.
