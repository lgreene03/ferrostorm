# ADR-066: tunnel deployment, the power that needed nothing new

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 line 30; GDD s8 lines 71-72; ADR-060; ADR-062 to ADR-065; Q021; P7-25

## Context

GDD s3 line 30's second Sodality dirty trick: **tunnel deployment**. Like every
power in Q021 it arrives as a name and a doctrine word, with no numbers.

Measuring what the sim already had decided the whole design, which is the fourth
wave running that has been true. Everything it needs was already built:

| what the power needs | what already existed |
|---|---|
| a bound on how many travel | `CarrierCapacity` - one transport's worth |
| somewhere to put them down | `SpawnOffsets`, the producers' own ring, walked in its committed order by the transport unload |
| a reach around the mouth | `SightCells`, the derivation ADR-063 gave the scan |
| a counterplay | `IsVisible` |

**So it needed no new state, no save bump and no invented number.** That is why
it went before the decoy army rather than after.

## Decision

### The Veil Projector carries it

The building that hides things is where concealed transit belongs.

It also answers something ADR-060 measured and left open: **no commander ever
builds a Veil Projector**, because a persistent cloak aura was not worth 1500
credits on its own. This gives it a second reason to exist **without changing one
of its numbers** - which is the cheapest possible answer to that finding, and
notably not a balance change.

Rejected: the Shroud Nest, a gun with no relationship to movement; the generator,
which is infrastructure and would put a raiding tool on the first building
raised; the seismic charge, technically excluded like every superweapon because
it already uses `ChargeTicks` for its own cycle.

The Watch Post was also rejected, and for a reason worth recording: it already
carries radar jamming, and **powers on one building share its charge** (ADR-064).
Stacking a second trick there would have made the Sodality choose between
blinding and raiding on one timer, which is a real cost - but it would also have
concentrated two of its three tricks in one killable building, which is too much
counterplay in one place.

### Every number derived, and they are all somebody else's

- **Who travels**: whoever stands within the mouth's own `SightCells`. A building
  reaches as far as it can see - the same derivation ADR-063 gave the scan.
- **How many**: `CarrierCapacity`, one transport's worth. A tunnel that moved an
  unbounded army would not be a trick, it would be teleportation.
- **Where they land**: the producers' own `SpawnOffsets` ring, in its committed
  order, so two peers set the same units down in the same cells.
- **Where it may aim**: only ground the player can **see**.

### The sight rule is the counterplay, expressed as a rule rather than a number

GDD s8 asks that every power have counterplay. This one's is: **deny the Sodality
vision and you deny it the tunnel.**

It is checked **first**, so a blind aim spends the charge and moves nobody. That
is the honest reading of "you cannot tunnel where you cannot see", and it stops
the power quietly doubling as a free scout - which is what an implementation that
moved the units and then failed to reveal anything would have been.

Aircraft do not tunnel, which needs no argument beyond saying it. Harvesters do
not either: a tunnel is a raiding tool, and moving the economy through one is a
different power nobody designed.

### Arrivals STOP

A unit that kept the order it held before it travelled would turn round at the
far end and walk the whole map back to a destination that meant something only
where it used to stand. This is the failure the power's shape invites, and no
other gate in the project could see it.

## Hash and format

**One golden moves, measured: `veil 2026`**, from 0x8B806C541C3699FC to
0xA5D040C135D43B67. It is the only scenario that spawns a Veil Projector.

**Measured cause, and it is not a behaviour change**: `Add` seeds a support
power's charge at spawn (ADR-062), and `ChargeTicks` is hashed - so a Veil
Projector that now carries a power hashes differently the instant it exists. The
scenario's own assertion passes verbatim on its own terms: the cloak still hides
the rifle for 100 ticks, selling the plant still collapses the veil, and the
turret still opens fire. A mechanical hash move, the same class as ADR-015's
stance append.

Regenerated under the standing authorisation. **Save format unchanged at v14** -
the power holds no live state at all. **The catalogue checksum moves to
0xD69565C84EE1166E**, because the Veil now grants a power.

## Proved to bite, and what this gate catches that no other does

`tunneldeploymentgate`, five stages, **control first** - a unit left alone must
not move, or every stage below passes in a world where units wander.

This is **the first support power that moves entities**. Every other one reveals,
damages or blinds, and none of their gates would notice if units teleported to
the wrong cells, landed on top of each other, or marched back across the map.

- Sight rule removed → *"units travelled to ground the player could not SEE ...
  without it the power is also a free scout"*.
- Bound removed → *"8 units travelled where one transport's worth is 5 ... an
  unbounded tunnel is teleportation rather than a trick"*.
- Arrival stop removed → *"a unit that arrived through the tunnel walked away
  again ... marching back across the map"*.

Stage 4 asserts against the **derivation** (`CarrierCapacity`) rather than the
literal 5, so the bound and the rule that sets it cannot drift apart. Stage 3
first asserts the destination is genuinely dark, or it would prove nothing.

After every revert the goldens were re-measured against the file and matched.

## Consequences

**One power remains**: the decoy army, and it is the expensive one. Entities that
look real to one player and not another touch targeting, fog and the checksum at
once, and the sim has no per-viewer entity visibility. Refusing it with the
argument recorded is a legitimate outcome and may well be the right one.

Four powers have now shipped without a single balance argument between them,
because every number in all four is somebody else's number. Whether any of them
is *fun* remains the fifteenth ADR's unanswered question.
