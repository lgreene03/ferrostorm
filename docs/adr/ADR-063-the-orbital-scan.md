# ADR-063: the orbital scan, whose every number is derived from something else

- Status: Ratified
- Date: 2026-08-03
- Deciders: Game Designer agent + Architect agent + Balance agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s3 line 25; GDD s8 lines 71-72; TDD line 11; ADR-062; Q021; P7-22

## Context

ADR-062 landed the support-power machinery with **no power having an effect**,
and filed Q021 asking for the numbers GDD s3's five named powers each need.
Q021 recommended the **orbital scan** first, on the grounds that it is the only
one whose effect needs no new combat or unit machinery: per-player fog already
exists.

GDD s3 line 25, the Directorate: *"support powers are **surgical** (**orbital
scan**, precision strike)"*. That is the whole specification - a name and a
doctrine word.

## Decision

Three choices were needed. **Every one of them is derived from a rule already in
the game rather than invented**, which is the through-line of this row and the
reason it needed no balance argument.

### 1. The Bastion unlocks it

| candidate | verdict |
|---|---|
| **Bastion** (17) | **chosen** |
| Orbital Cannon (6) | **technically impossible** |
| Directorate power plant (1) | rejected |

Only three buildings are Directorate-exclusive, and s3 makes the scan a
Directorate power, so the field was small.

The **Orbital Cannon** was the obvious thematic pairing - one orbital platform
doing a cheap frequent scan and a rare huge strike - and it is **excluded by a
hard technical fact**: the superweapon already uses `Entity.ChargeTicks` for its
own cycle, and one entity cannot hold two independent charges without new
per-entity state. That is a real reason, not a preference.

The **power plant** was rejected on two grounds: a plant calling down orbital
scans reads wrong, and it is the first building anyone raises, so a power gated
on it is not "unlocked by structures" in any meaningful sense.

The **Bastion** is the right answer on the design's own terms. It is the
Directorate's **eye** as much as its gun - `SightCells: 7` is the widest of any
building in the game - it sits behind the radar so the power is genuinely
tier-unlocked, and it is a **forward defensive hardpoint**, which makes GDD s8's
counterplay rule ("scout the structure, kill it") true by construction rather
than by arrangement.

**Noted rather than hidden:** this gives the Bastion utility it did not have, and
`BALANCE-bastion-value-vs-shroud-nest.md` is an open A11 ticket arguing the
Bastion is poor value. This row does **not** claim to answer that ticket - no
stat changed - but it is a relevant new fact for whoever does.

### 2. The radius is the unlocking building's own sight

Not a number of its own. `GetStructureType(e.StructType).SightCells`.

The scan shows **what that building's sensors would see, projected anywhere on
the map**. It explains itself, it needs no balance argument, and a future scan
building gets a radius the day it is authored. Change the Bastion's
`sight_range` and the scan changes with it, deliberately.

Rejected: a fixed 10 (the radar's sight, the largest in the game) - a second
number to keep in step with nothing; and the seismic charge's 6 - borrowed from
a weapon, which a scan is not.

### 3. The duration is the superweapon's warning window

`OrbitalScanRevealTicks = SuperweaponWarningTicks` = 75 ticks, five seconds.

**75 is already this game's answer to "how long does a player need to notice
something and act on it"** - it is written into the one mechanic whose entire
purpose is to give a victim that chance. A scan buys the Directorate exactly one
such window. That is what "surgical" means as a number.

Rejected: a fifth of the power's charge (100 ticks), derived from nothing about
seeing; and a reveal lasting the full charge, which stops being a scan and
becomes permanent vision on a cooldown.

Naming `SuperweaponWarningTicks` also removed a bare `75` from its only site.

### It marks the ground EXPLORED, not merely visible

A scan is intelligence you **keep**. A reveal whose ground reverts to unmapped
the moment it lapses is a torch, not a scan. Asserted in two stages.

## Hash and format

**All 24 goldens byte-identical, measured.** The live reveals live in a **side
collection folded only when non-empty**, so a world that never fired one - which
is every golden - hashes exactly as one compiled before scans existed. Fifth user
of the technique CLAUDE.md documents.

**Save format bumps to v13**, deliberately, carrying the live scans. A save taken
mid-scan that resumed with the ground dark is a **divergence**, not a missing
feature: what a player can see decides what its units acquire.

**The catalogue checksum moves to 0x905DDBBD71F7973D**, because the Bastion now
carries a power.

## Proved to bite, and what each gate catches that the others cannot

`orbitalscangate`, six stages, **control first**: stage 1 asserts the far corner
is dark *before* any scan, because every later stage would otherwise pass on a
map that was never fogged.

- Effect disabled → stage 2 fails, **and `supportpowergate` still passes**. That
  is the clean proof the two gates are complementary: the machinery gate would
  pass unchanged if firing a scan revealed nothing.
- Reveals never pruned → stage 4 fails: *"a scan that never lapses is permanent
  vision on a cooldown"*.
- Scans dropped on load → stage 6 fails naming both hashes, **while `saveload`
  passes throughout**, which is exactly what stage 6 exists for: `saveload` never
  fires a scan, so a v13 block written and never read would pass it silently.

After every revert the goldens were re-measured against the file and matched.

## The defect this row found in ADR-062, one wave old

`supportpowergate` stage 1 compared `SupportPowerChargeTicks` against
`SuperweaponChargeTicks` - **two compile-time constants**. The compiler proved
500 < 1500 at build time and **deleted the branch**, and said so in a CS0162
unreachable-code warning nobody read.

That is ADR-047's defect exactly - a gate that measures itself - shipped in the
gate that was supposed to be careful about it. It now **observes both timers from
a running world**, charging a superweapon and a support power side by side and
counting the ticks each takes to announce itself. Nothing there can be folded.

**The lesson worth keeping: a build warning is a gate telling you something.**
The unreachable-code warning was the whole diagnosis, printed on every build for
a wave.

## The second thing that lagged, caught exactly where its own comment predicted

The save-surgery helper pins the source format to a literal, and its comment
says so:

> Pinned to a literal version this helper breaks the moment the format moves,
> and it breaks in the BATTERY rather than here.

It did, for the fifth time. v13 is the source now, v12 became a legal target, and
the scan block is the new walk step. The comment was right and is left standing.

## Consequences

The first support power in the game does something. Four of GDD s3's five remain,
and Q021 holds them.

Nothing here tells anyone whether a five-second scan on a 500-tick timer is
**worth 1400 credits**, or whether being scanned feels like being outplayed or
like being cheated. That is a playtest, and it is the twelfth ADR to say so.
