# ADR-065: radar jamming, the first power whose effect lands on a player

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Client Engineer agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 line 30; GDD s8 lines 71-72; GDD line 48; ADR-008; ADR-062 to ADR-064; Q021; P7-24

## Context

Q021's last three powers are the Sodality's dirty tricks. GDD s3 line 30:
*"Support powers are **dirty tricks** (**radar jamming**, decoy army, tunnel
deployment)"*.

**Radar jamming has real machinery to attach to, and that is why it goes first.**
ADR-008 already ties the minimap to the Radar Uplink, and the client already
carries the whole blackout: a blanked minimap reading RADAR OFFLINE, a toast, a
placeholder VO line, an alert cue and a ping at the uplink. All of it hangs off
one predicate:

```csharp
bool radarLive = hasUplink && supply >= draw;
```

A jam is the third term. Nothing else needed inventing.

Measured first, and it mattered: **the radar has no other mechanical effect in
the sim.** It is a prerequisite with the largest sight in the game, and the
blackout is the only thing that reads it. So jamming means exactly what the
blackout means, and nothing more had to be designed.

## Decision

### The jam lives in the SIM, and the client asks it

`World.IsRadarJammed(player)`, public, folded into the state hash, carried in the
save. The client's predicate becomes:

```csharp
bool radarLive = hasUplink && supply >= draw && !_world.IsRadarJammed(LocalPlayerId);
```

**This is the load-bearing architectural decision.** A blackout the client worked
out for itself would differ between two LAN peers reading the same command
stream - and it would differ in exactly the direction this project's
hardcoded-seat guard and its client harness exist to catch. `LocalPlayerId`, so a
joiner sees its own jam rather than the host's.

### The Watch Post carries it

The Sodality's **sensor** building is where sensor warfare belongs: my eyes on,
your eyes off, from one structure.

It also makes GDD s8's counterplay rule true **by construction rather than by
arrangement**. The Watch Post is unarmed, visible and killable by design - GDD
line 56 says detectors must be - so *"scout the structure, kill it"* needs no
arranging at all.

Rejected: the Veil Projector, which is about hiding your own units rather than
blinding an enemy's map, and which is a persistent aura with no charge (ADR-062);
the Shroud Nest, a gun; the seismic charge, technically excluded like every
superweapon because it already uses `ChargeTicks` for its own cycle.

### It blinds every hostile player, and takes no aim point

A radar is not somewhere you aim. The command's X/Y are **unused by this power**,
which is honest rather than untidy: a jam has no ground zero.

Re-jamming **refreshes** rather than stacks - two jams do not blind anyone twice
as hard - and allies are never caught. ADR-038's splash rule deliberately does
*not* apply, because this is not splash: it is an effect on a **player**, and a
trick that blinded your own team would be played by nobody.

### The duration is derived: a third of its own charge

`RadarJamTicks = SupportPowerChargeTicks / 3` = 166 ticks, about eleven seconds.

The **third** is this project's established ratio, used twice already for the
same reason: ADR-062 gave a support power a third of the superweapon's charge,
ADR-064 gave the precision strike a third of the orbital cannon's damage.
Applying it one level down gives a **duty cycle a reader can check**: a jammed
player is blind at most a third of the time, even against a commander who fires
it the instant it recharges. The gate asserts that bound as a game rule rather
than asserting the constant.

Rejected: **the orbital scan's 75-tick reveal**, which is tempting for the
symmetry - one power gives sight, the other takes it - and is too short to be a
trick; five seconds of blank minimap is a flicker. Rejected: **the full charge**,
which is not a jam at all but a permanent blackout, since the power recharges
exactly as it lapses.

## Hash and format

**All 24 goldens byte-identical, measured.** The deadlines are folded only while
a jam is live, so a world that never fired one hashes as one compiled before jams
existed.

**Save format bumps to v14**, deliberately, carrying one deadline per player. A
save taken mid-jam that resumed with the victim's minimap back is a divergence,
since the sim's own hash folds the deadline.

**The catalogue checksum moves to 0xD4F4248474EFA6E7**, because the Watch Post
now grants a power.

## Proved to bite, and what this gate catches that no other does

`radarjamminggate`, five stages, **control first** - nobody is jammed before
anything fires one, or every stage below would pass in a world where
`IsRadarJammed` simply returned true.

Every support-power gate so far asserts an effect on the **map**: a reveal, or
damage at a point. This is the first power whose effect is on a **player**, and
the first whose result the client reads back out of the sim. Neither
`orbitalscangate` nor `precisionstrikegate` can see a jam, and neither would
notice if `IsRadarJammed` always returned false.

- Ally exclusion removed → *"the jam blinded its OWN owner - a dirty trick nobody
  would play"*.
- Deadline never compared to the tick → *"still jammed 171 ticks after firing - a
  jam that never lifts is not a trick, it is deleting the enemy's minimap"*.
- v14 block parsed and dropped → the stage fails naming both hashes, **while
  `saveload` passes throughout** - the same blind spot ADR-063 found for scans,
  and the reason each power carries its own save stage.

After every revert the goldens were re-measured against the file and matched.

## The save-surgery helper lagged for the SIXTH time

Its own comment predicts this every time, and was right again:

> Pinned to a literal version this helper breaks the moment the format moves,
> and it breaks in the BATTERY rather than here.

v14 is the source now, v13 became a legal target, and the jam block is the new
walk step. Six formats, six catches, always in the battery and never in the
helper. **That comment has earned its place**: it is a hand-maintained lag that
announces itself, which is the rarest and most useful kind.

## Consequences

Two of the Sodality's three tricks remain: **tunnel deployment**, then **decoy
army** last - it needs entities that look real to one player and not another,
touching targeting, fog and the checksum at once.

This row also gives the project its first support power whose value is almost
entirely psychological. Whether eleven seconds of blank minimap reads as a
clever ambush or as an interface bug is not a thing any gate here can answer,
and it is the fourteenth ADR to say so.
