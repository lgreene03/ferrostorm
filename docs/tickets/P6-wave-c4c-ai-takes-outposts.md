# P6 Wave C4c delivery notes: the AI takes the free income

Closes the largest remaining gap in the outpost mechanic, and the one C4 and C4b
both filed as owed: the AI ignored outposts entirely, so on a map carrying them
only a human ever captured one and the AI simply conceded the economy. NEUTRAL
hash impact: all 24 goldens byte-identical despite this being an AI behaviour
change.

## Plan

labels: persona:p2 gdd:s4 phase:6 owner:ai-engineer

The AI already knew engineers existed (it excludes unit type 11 from attack
waves) but never built one. Teaching it to capture is three small pieces: notice
a neutral outpost, buy an engineer, send it. The whole block must be inert on a
map with no outposts, because that is every golden scenario.

## What the AI does now

Once per decision beat, after the existing entity scan and the decapitation
guard, `NearestNeutralOutpost` finds the closest UNCAPTURED outpost to the AI's
home (ties to the lower entity id, the NearestField shape). If there is one:

- **With an engineer standing**, it issues an Attack order on the outpost. An
  Attack order on a structure IS the capture order: the engineer walks in and
  CaptureSystem consumes it on contact. Re-issued each beat, which is harmless
  (the same target) and self-healing if the walk is interrupted.
- **With no engineer**, it queues one at the barracks, gated on affording it and
  on `AlreadyQueued`, so it cannot spam an engineer every beat.

An outpost owned by anyone, including this player, is not a target: capture only
ever flips a neutral one, and re-taking an enemy's is a job for the army, not a
lone engineer. So the routine self-terminates once the map's outposts are taken.

## Why this AI change moves no golden

Three deliberate choices, in descending order of importance:

1. **The whole block is guarded on an outpost existing.**
   `NearestNeutralOutpost` returns -1 on a map with none, and every golden
   scenario is such a map: skirmish-01 (the map the `skirmish` golden loads) and
   the mission maps carry no outposts, which C4b kept true on purpose. So the
   block adds no command and reads no state that could reorder anything.
2. **The engineer tally is additive only.** The scan's `EntityKind.Unit` arm
   still counts an engineer toward `army` exactly as before; the new
   `ownEngineer` note sits INSIDE that same branch. Had the engineer been pulled
   out of the army count, wave timing would shift and every golden would move.
3. **No randomness, no dictionary iteration.** The new scans walk entities by
   index, like every other AI helper.

Proven, not asserted: all 24 goldens byte-identical; the five-seed determinism
suite 24/24 at 2026, 31337 and 900913 (the ADR-014 regression seed); and the
`skirmish` scenario reports the IDENTICAL match summary to before the change
("37 entities destroyed, treasuries 2/1398"). Independently, mapgate shows
skirmish-01 and skirmish-03 producing exactly 14 entities, the same as before,
while the two outpost maps now produce 17 and 18 (the engineers).

## The gate

mapgate's assertion is FLIPPED and strengthened. It used to assert every map
outpost stayed NEUTRAL, which was correct only while the AI ignored them; C4b
wrote that it would need updating the day the AI learned, and this is that day.
It now asserts that on any map carrying outposts the AI CAPTURES at least one
within the run, so the capture routine cannot rot to a no-op with every other
assertion still passing. Measured with margin: 2 of 2 on skirmish-02 and 3 of 4
on skirmish-04, deterministic at the gate's fixed seed.

## Verification (local, real evidence)

- mapgate exit 0: 6 outposts across 4 maps, 5 taken by an AI.
- Full battery `match 2026` exit 0.
- determinism 24/24 at seeds 2026, 31337 and 900913, exit 0 each.
- The exact CI golden check BYTE-IDENTICAL across all 24 rows.
- Both Godot client builds 0 warnings (Debug and ExportRelease).

## Changed / Assumed / Needed next

**Changed.** sim/Ferrostorm.Sim/SkirmishAI.cs: an `ownEngineer` note inside the
existing Unit arm, the outpost block after the decapitation guard, and the
`EngineerType` constant plus `NearestNeutralOutpost` and `AlreadyQueued`
helpers. Runner: mapgate's ownership assertion flipped to a capture assertion.

**Assumed.** Nearest-outpost-to-home is the right target choice for a first pass;
the AI sends one engineer at a time and does not escort it, so a contested
outpost will cost it engineers. Both are Balance and ai-engineer refinements that
need no format change.

**Needed next (from whom).** ai-engineer: escorting, and deciding whether to
re-take an outpost an enemy holds (today the army will attack it as an ordinary
enemy structure, which destroys rather than captures it - a design question worth
a decision). Balance: whether an AI that reliably banks 15/s per outpost makes
the outpost maps swingy, now measurable because the AI actually plays them.
