# ADR-054: the save holds at scale, and a gate that is honest about what it adds
- Status: Ratified
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: TDD s6's entity budget; P7-12

## Context

Fifth outing of the ADR-050 method, second consecutive clean result.

`saveload` proves a save round-trips on `BuildSkirmishWorld` - a small,
hand-built world. **Nothing had ever saved a world at the scale a real match
reaches**, and `churnprobe` (ADR-052) showed 558 entities is reachable on
skirmish-07.

The question one step to the side of `saveload`: does the format hold at
**scale**? A save that works at 30 entities and fails at 300 loses a player's
campaign, and it would fail on exactly the saves worth keeping - the long ones -
while every existing gate passed.

## The measurement

A real match on skirmish-07, saved at tick 4500 and resumed:

> **226 entities, 81,215 bytes, loaded hash-exact, resumed to the uninterrupted
> final hash bit-for-bit.**

**No defect.** The format holds.

## The honest part: what this gate does NOT add

Proved to bite by dropping the harvester's `Carry` from the writer. The gate
failed as intended:

> `save scale: loaded hash 0xB88DE422104BF846 != saved 0x02AF799B2DFD7CB7 at 226
> entities`

**And so did `saveload`**, on the same defect, on its small world. So for a
dropped field this gate adds nothing.

That prompted the right follow-up question rather than a shrug: what *would* only
fail at scale? A narrow-typed count or index - a `byte` entity count capping at
255, a 16-bit id. Checked, and **every narrow write in the format is an enum**
(`Kind`, `Armour`, `HState`, `Stance`, `Type`), bounded by its own definition
rather than by population. Ids and counts are `int32`.

**So there is no current defect class that needs this gate.** That is worth
stating plainly, because the alternative is a gate whose value is assumed.

## Decision

**Ship it in the battery anyway, and cheaply.**

The cost was the deciding factor rather than an afterthought. The first draft ran
9000 ticks three times over - record, reference, resume - and that is real battery
time for coverage that is nearly a duplicate. Cut to 4500 ticks to *reach* the
scale plus 500 to prove the resume, because **divergence from a serialisation
slip shows on the first tick that reads the dropped field, not eventually.** The
whole gate now runs in **4.5 seconds** and still exercises 226 entities and 81 KB.

At that price the argument for keeping it is easy, and it is not "it might catch
something":

1. **It establishes a number nobody had.** 226 entities is 81 KB, so
   `churnprobe`'s 558-entity world is roughly 200 KB. Save size had never been
   measured at all.
2. **It guards format changes yet to come.** "No current bug class needs it" is
   exactly the argument that leaves a hole for the next narrow type somebody
   adds, and the format has gained twelve versions so far.
3. **The failure it guards is silent and catastrophic**, which is the standing
   reason this project gates things rather than reasoning about them.

Rejected: **keeping it as an on-demand mode outside the battery.** A gate nobody
runs is a gate that rots, and at 4.5 seconds there is no saving worth having.

## On two clean results in a row

ADR-053 found nothing and bought a guarantee. This one found nothing, bought a
smaller guarantee, and **found that its own marginal coverage is thin** - which is
a third kind of outcome and worth naming.

The method is not producing weaker results because it is running out; it is
producing them because the obvious questions have now been asked. The useful
discipline when that happens is the one applied here: **do not let a gate ship on
the assumption that it is valuable. Measure what it catches that an existing gate
does not, and if the answer is "nothing today", say so and justify it on the
future rather than pretending.**

## Consequences

`savescalegate` in the battery at 4.5 seconds, proved to bite. No behaviour
changed: all 24 goldens byte-identical, catalogue checksum unmoved, all 18 local
CI gates green.

Ledger for the method across five outings: **three defects, two guarantees** - and
one honest note that a guarantee can be worth less than it looks.
