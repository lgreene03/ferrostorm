# TICKET-P6-DUP-AUDIT: the client's duplicated rules, audited in full

Labels: persona:p3, gdd:s9, phase:6, owner:client-engineer

Status: **AUDIT COMPLETE, 2026-07-25.** Wave 1 fixed three HIGH seat bugs.
**Wave 2 (same day) cleared the LAST HIGH and the four most player-visible
MEDIUMs: NO HIGH FINDINGS REMAIN.** What is left is filed below with severities.

## Why this audit happened

PR#24 fixed a bug where three separate places each answered "can the local
player see this entity?" and two had drifted to a hardcoded seat. The fix
collapsed them into one predicate. The obvious question was **how many more
rules are in that state**, so the whole client was read looking for one
conceptual rule with more than one implementation.

The answer was: a lot, and three of them were live HIGH-severity bugs of exactly
the same shape. This is now the seventh, eighth and ninth instance of
"client-side, correct at seat 0 by luck".

## FIXED in this wave

### 1. `ValidPlacement(0, ...)` while the command is issued as `LocalPlayerId`. HIGH

Three sites: `CanPlace`, the drag ghosts, `FindPlacementCell`.

`World.ValidPlacement(int player, ...)` uses its player argument to decide
**whose structures anchor the build radius** (`o.PlayerId != player`). So the
client asked "may player 0 build here" and then issued "player 1 builds here".

At seat 1 the placement ghost was green only inside the **host's** base and red
inside the joiner's own, and because the commit gates on the same predicate,
**a joiner could not place a structure anywhere at all.** Measured by the
harness: **0** placeable cells in their own base, **56** in the opponent's.

### 2. `_ownerBrownedOut` written seat-relative, read absolutely. HIGH

Written with slot 0 meaning "me" and slot 1 meaning "the opponent"; read at both
consumers by `v.PlayerId`. At seat 1 the two indices swap, so a joiner's turrets
greyed out and read `OFFLINE - LOW POWER` when the **host** browned out, and
stayed lit while the joiner's own grid collapsed and the sim quietly refused to
let them fire.

Now keyed by absolute player id on both the write and the read.

### 3. Team colour: seat-relative on the minimap, absolute on the battlefield. HIGH

`BattlefieldView` colours by player id and calls itself "the one-place
team-colour law"; the minimap dot feed held a rival copy keyed on "me versus
them". At seat 1 a joiner saw their own army **orange on the minimap and teal on
the battlefield**, and the enemy the reverse.

Collapsed into `BattlefieldView.MarkFor(player)`, used by the minimap and both
`Dress` functions. A player's colour is which player they **are**, not who is
looking, so both commanders can say "the orange tanks" and mean the same tanks.

### 4. ADR-008's brown-out threshold, four copies. MEDIUM, fixed opportunistically

The klaxon, the turret dim, the sidebar bar, and `World.AtLeast75`. Two of the
client copies carried comments asserting there was a single shared threshold.
There was not. `AtLeast75` is now **public** and the client calls it, so the
count is one across both projects and the comment is finally true.

## FIXED in wave 2 (the last HIGH, and the four most player-visible MEDIUMs)

### 5. The wall-drag path had no affordability test. HIGH, already divergent

`CanPlace` (single click) tested credits; `UpdateDragGhosts` (drag) tested only
the cap. A run drawn with 300 credits tinted **entirely green**, sent every
segment, and the sim silently dropped each one past the money while the readout
quoted a total it could not pay.

**Why it lasted:** the opening treasury is 8000 and a segment costs 100, which is
exactly the 80-segment cap. The money and the cap bite on the same segment at the
start of every match, and only diverge once the player has spent anything.

Both paths now go through `CanPlace(cell, type, aheadInRun)`, where `aheadInRun`
counts the segments landing before this one, since a barrier is charged as it
lands. At `aheadInRun = 0` the single-click behaviour is unchanged to the credit.
The readout names the money as a reason: `ONLY 5 AFFORDABLE - RUN TRUNCATED`.

**The commit is deliberately unchanged.** It still sends the whole run and lets
the sim decide, because a client-side filter would be a second opinion that
diverges the moment credits move between the draw and the release. The client
**predicts**; the sim **decides**. That is what keeps the prediction safe.

### 6. Combat effects ignored the visibility the actor loop computed. MEDIUM

`Live` gated only on the actor **existing**, and a fog-hidden enemy's actor does
exist, merely `Visible = false`. An unseen turret firing out of unexplored fog
drew its muzzle flash, tracer and impact: the fog hid the shooter and the effects
layer painted a bright arrow at it. Measured at **2 effect nodes** leaked per
shot.

`Live` now reads `node.Visible` rather than recomputing the rule, so the effects
layer consumes the actor loop's decision instead of forming a rival one. Audio is
deliberately not gated: a sound gives away no position.

### 7. `UnitNames` was missing the repair vehicle. MEDIUM, already divergent

ADR-019 added unit type 13 to the catalogue, the sidebar and the model library,
and not to the name table, so a completed repair vehicle toasted `UNIT DEPLOYED`
and read `UNIT`. Three lookup sites, **two carrying their own copy of the length
guard**, which is what turned a missing entry into a silent shrug in three
readouts rather than one obvious gap. Now one `UnitNameOf`.

### 8. `ReplayTheater` still held the pre-fix ferrite expression. MEDIUM

The exact defect P5-ECON-01 fixed on the live side, with the cap hardcoded at
12000. Now calls `SkirmishLive.FieldFullness`, so the default lives in one place.

## NOT FIXED, filed with severity

### MEDIUM

- **The cursor promises a harvest the click refuses.** `CursorFor`'s comment
  claims it runs the exact picks `IssueOrder` runs. The enemy and field picks do
  match; the **refinery precondition does not**, so with no refinery standing the
  cursor shows the harvest verb over every deposit and the click yields only a
  `NO REFINERY` toast.
- **An impact on a visible target is lost when the shooter is hidden.** Surfaced
  BY the wave-2 fog fix rather than by it: `SpawnAutocannonBurst` gates the
  tracer, the impact and the hit-pop on both ends, so a unit shot from fog no
  longer flinches. Correct for the tracer (it is a line between the two) and
  wrong for the impact, which happens on ground the player can see. Wants the
  attacker-gated and target-gated effects separated per spawner, which is a
  taste call per weapon family rather than a one-line change.
- **The tech tree is reimplemented client-side** (`OwnsStructType` +
  `PrereqsMet` against `World.HasPrereqs`). Identical today. The failure mode on
  drift is a lit button whose order the sim silently drops.
- **The Veil's Sodality gate is the literal type 7 in two projects**, while the
  *unit* faction gate reads the live catalogue. A second faction-specific
  structure would be offered to both sides and refused for one.
- **`IsBarrier` from the catalogue in one file, the literal 9 in the other.**
  ADR-005 reserves type 10 for a gate; on the day it lands `SkirmishLive` picks
  it up and `Sidebar` does not, so the gate button would queue at the yard and
  wait for a ready slot a barrier never fills.
- **The campaign allow-list is asked in six places**, and the **barrier path
  consults none of them** - a mission that disallows the wall is enforced only by
  the button being hidden, unlike every other structure.
- **`WeaponOfStruct` is a stale hardcoded table.** Its comment says it mirrors a
  hardcode in `World.SpawnTurret` that no longer exists: the sim now reads
  `WeaponId` from the catalogue. The placement ghost's range ring is drawn from
  the stale copy while the built structure uses the live value.
- **Sidebar button visibility computed twice**, at Init and per frame, and the
  per-frame copy only runs for items the Init copy did not already hide.
- **`Mobile` implemented four times**, one of them keyed on raw ints in
  `ReplayTheater`. All agree until an air kind is appended, at which point the
  inlined copies classify aircraft as structures and the `BASE UNDER ATTACK`
  klaxon fires whenever an aircraft is shot at.
- **The own supply and draw tally exists three times** in one file.

### LOW

Tool scenes (`LookDev`, `Main`) hold hardcoded-seat reads; `FindPlacementCell`
asks the geometry question without a struct type; struct type ids appear as bare
literals in three readouts; the repair rate `15 cr/s` is hand-precomputed in
three places; the sell refund halving is a client copy of a sim rule; the barrier
count is recomputed from the interpolated view rather than the sim; `Ralliable`
deliberately differs from the sim's `IsRallyable` (documented on both sides,
recorded here so the divergence is known rather than missed); the MCV and
engineer type ids are duplicated constants; `Lan.cs` identifies own mobiles by
speed rather than kind.

## Audited and found CLEAN

Worth recording so "audited" can be told from "not looked at": entity fog gating
(one predicate since PR#24), producer routing, rally point positions, formations,
catalogue costs and build times, and the minimap radar blackout.

## The standing lesson

Every instance of this defect class has been one conceptual rule with more than
one implementation, where the copies agreed until they did not, and where the
disagreement was invisible from the seat the developer was sitting in. The
durable fix is never a wider grep. **It is one implementation.**
