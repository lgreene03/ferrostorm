# TICKET-P6-DUP-AUDIT: the client's duplicated rules, audited in full

Labels: persona:p3, gdd:s9, phase:6, owner:client-engineer

Status: **CLOSED, 2026-07-25. Every finding is fixed or has a stated reason for
standing.** Wave 1 took three HIGH seat bugs, wave 2 the last HIGH and the four
most player-visible MEDIUMs, wave 3 the entire remainder. Nine of the findings
were live bugs; the rest were loaded guns - correct today, wrong on the day
someone appended an air kind, authored a second faction-specific building, or
changed a rate.

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

## WAVE 3: the remainder, cleared

Every MEDIUM and every actionable LOW. What is left below is a short list of
things that are deliberately as they are, recorded so nobody re-files them.

### Behaviour

**9. The cursor promised a harvest the click refused.** `CursorFor`'s header
claims it "runs the exact picks `IssueOrder` runs". The refinery precondition
was missing, so with no refinery standing the cursor showed the harvest verb
over every deposit and the click yielded a `NO REFINERY` toast. It now makes the
same `HasLiveRefinery` call the order path makes.

**10. An impact on a visible target was lost when the shooter was hidden.**
Surfaced by wave 2's own fog fix. A shot has two ends and the fog can hide
either, so the rule is now per EFFECT rather than per event:

| effect | gated on |
|---|---|
| muzzle flash, smoke, report | the SHOOTER |
| tracer, shell, rocket flight | BOTH ends |
| impact, sparks, flinch | the TARGET |

Conflating them was wrong in both directions: drawing everything gave the
shooter away, and drawing nothing meant a unit shot from the dark took damage
without reacting at all. `OnFired` and `SpawnAutocannonBurst` both split now.

**11. The campaign allow-list was asked in six places and the barrier path
consulted none of them.** A wall does not queue at the yard, so `QueueStructure`'s
check never saw it and a mission that disallowed struct type 9 was enforced by a
hidden button alone. One `StructureAllowed` / `UnitAllowed` pair now, and
`EnterPlacement` asks it, so the wall is refused at the command like everything
else.

### The sim is the authority

**12. The tech tree was reimplemented client-side.** `World.HasPrereqs` is public
and the sidebar calls it; the client's `OwnsStructType` and `PrereqsMet` are
gone. The old pair carried a comment claiming it was "what keeps the panel and
the gate from disagreeing" - an aspiration, since two implementations agree only
until one is edited, and the failure mode is a lit button whose order the sim
silently drops.

**13. The Veil's Sodality gate was the literal 7 in two projects.**
`World.StructureAllowedForFaction` is public, named `VeilStructType`, and both
the sim's `BuildStructure` and the sidebar call it. The permanent fix is a
`Faction` column on `StructureTypeDef` to match `UnitTypeDef`, which is a /data
schema change and therefore its own wave with its own catalogue-checksum
argument; this collapses two copies to one at no hash cost in the meantime.

**14. `IsBarrier` was the catalogue in one file and the literal 9 in the other.**
The sidebar now asks the catalogue's `Kind`, so ADR-005 clause 6's reserved gate
type is classified correctly on the day it lands instead of queueing at a yard
and waiting for a ready slot a barrier never fills.

**15. `WeaponOfStruct` was a stale hardcoded table.** Its comment said it mirrored
a hardcode in `World.SpawnTurret` that no longer exists - ADR-006 moved the
weapon onto `StructureTypeDef`. It reads the live catalogue now.

**16. The barrier cap counted the interpolated view.** `World.CountBarriers` is
public; the view trails the sim by up to eight ticks, which at the cap boundary
let a segment tint green and be refused on arrival.

**17. The repair rate was hand-multiplied in three readouts.** `15 cr/s` is now
derived from `World.RepairCreditsPerTick * World.TicksPerSecond`, both named in
the sim and used by its own charging loop.

**18. The MCV and engineer ids were bare literals** across the deploy gate, the
victory-hope test, three AI branches and two client files, plus two private
constants naming the same numbers in two projects. `World.McvUnitType` and
`World.EngineerUnitType` now.

### Client-internal duplicates

**19. `Mobile` existed four times**, one keyed on raw ints in `ReplayTheater`.
All agreed only because `Unit` is 0 and `Harvester` is 1. One predicate now, and
the base-under-attack alert calls it rather than inlining its negation - that
copy is what would have classified an air kind as a structure and fired the
klaxon whenever an aircraft was shot at.

**20. The own supply-and-draw tally existed three times** in one file, feeding
two DIFFERENT sim rules (the 75 per cent brown-out and the depot's
supply-covers-draw), which is what makes an accidental copy-paste between them
quiet. One `OwnPower`.

**21. Sidebar visibility was computed twice**, at Init and per frame, with the
per-frame copy unable to reach items Init had already hidden. One
`FixedGatesAllow` pair.

**22. The wall-run refund was `walls * type-9-cost / 2`.** Now summed per segment
from each one's own catalogue cost, which is both what the sim actually pays
under integer division and correct for a second barrier type.

**23. `FindPlacementCell` asked the geometry question without a struct type**, so
it tested type 0's footprint and handed the answer to a caller placing a real
building. The type flows through.

**24. Bare struct ids in readouts** are named (`ServiceDepotStructType`).

## Deliberately left, with reasons

- **`Ralliable` differs from the sim's `IsRallyable`** (the client excludes the
  Construction Yard). Intentional, documented at length on both sides. Recorded
  here so the divergence stays known rather than being "fixed" by someone
  reading only one end.
- **`RepairStalled` approximates the sim's charging order.** Its own header says
  so: the depot drain interleaves, making the prediction optimistic. A faithful
  version would mean replaying the sim's entity loop client-side each frame,
  which is a worse trade than an honest approximation that says it is one.
- **`LookDev` and `Main` read player 0.** Neither has a seat concept: LookDev
  photographs a fixed one-sided world and `Main` is single-player loopback and
  says so. Both now carry a comment stating this, so a future sweep can tell
  design from defect. This is why CI's hardcoded-seat guard covers
  `SkirmishLive.cs` specifically.
- **A `Faction` column on `StructureTypeDef`** is the permanent fix for finding
  13 and is a /data schema change; it wants its own wave.

## Audited and found CLEAN

Worth recording so "audited" can be told from "not looked at": entity fog gating
(one predicate since PR#24), producer routing, rally point positions, formations,
catalogue costs and build times, and the minimap radar blackout.

## The standing lesson

Every instance of this defect class has been one conceptual rule with more than
one implementation, where the copies agreed until they did not, and where the
disagreement was invisible from the seat the developer was sitting in. The
durable fix is never a wider grep. **It is one implementation.**

Nine of these were live bugs. The rest were loaded guns: correct today, wrong on
the day someone appended an air kind, authored a second faction-specific
building, or changed a rate. The audit is complete; what it leaves behind is a
client where the rules that matter are each written once.
