# TICKET-P6-DUP-AUDIT: the client's duplicated rules, audited in full

Labels: persona:p3, gdd:s9, phase:6, owner:client-engineer

Status: **AUDIT COMPLETE, 2026-07-25. Three HIGH findings FIXED in this wave;
the remainder are filed below, unfixed, with severities.**

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

## NOT FIXED, filed with severity

### HIGH

**The wall-drag path has no affordability test.** `CanPlace` (single click)
tests credits; `UpdateDragGhosts` (drag) tests only the cap. **They already
disagree**, in single player, today: draw a twenty-segment run with 300 credits
and every segment tints green, the whole run is sent, and the sim silently drops
each segment past the money. The readout quotes the total cost and never says it
cannot be paid. Wants the affordability clause plus a truncation notice in
`WallDragSummary`.

### MEDIUM

- **Combat effects ignore the visibility the actor loop computed.** `OnFired`
  and `OnDied` gate only on the actor node existing, and a fog-hidden enemy's
  node exists (it is merely `Visible = false`). **An unseen turret firing out of
  unexplored fog draws its muzzle flash and tracer**, revealing the position the
  fog exists to hide. Already divergent, visible in single player.
- **The cursor promises a harvest the click refuses.** `CursorFor`'s comment
  claims it runs the exact picks `IssueOrder` runs. The enemy and field picks do
  match; the **refinery precondition does not**, so with no refinery standing the
  cursor shows the harvest verb over every deposit and the click yields only a
  `NO REFINERY` toast.
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
- **`UnitNames` is missing the repair vehicle** (13 entries, unit type 13 added
  to the sidebar and model library but not here). **Already divergent**: a
  completed repair vehicle toasts `UNIT DEPLOYED` and reads `UNIT`.
- **`ReplayTheater` still has the pre-fix ferrite expression** with the
  hardcoded 12000, the exact defect P5-ECON-01 fixed in the live client.
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
