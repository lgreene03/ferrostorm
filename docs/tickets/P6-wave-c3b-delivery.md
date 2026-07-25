# P6 Wave C3b delivery notes: parallel build lanes

Closes the C3b row under ADR-023 (ratified) and completes GDD line 45, whose
client half shipped as C3 (ADR-020). NEUTRAL hash impact, overturning the C3b
ticket's own assumption; save format v8, which costs no goldens.

## The design pass overturned the filed assumption, for the third time

TICKET-P6-C3b assumed a second build head is new hashed per-entity state and so
costs a golden regeneration plus a save bump. The regeneration half is wrong, and
the two costs are separable. Three facts carry it, all verified in code:

1. **SkirmishAI is strictly serial.** It only queues at the yard when
   `QueueLength(cy) == 0` and nothing is ready, so it never creates a second
   concurrent order on any golden it drives.
2. **The one scripted defence order in a golden lands in an idle yard.** The
   construction scenario's turret is queued when the queue and ready slot are
   both empty.
3. **A guarded non-empty hash fold is already shipped and golden-covered**
   (`_orderQueues` folds nothing when empty).

So a lane reached only by OVERFLOW is dead code on every golden.

## The overflow rule is the whole wave

A queued structure goes to lane 1 whenever lane 1 is fully idle, and to lane 2
only when lane 1 is busy. Lane selection is by OCCUPANCY, never by category.

Routing by category instead ("defences always lane 2"), which is the obvious
reading of GDD line 45, moves goldens twice over: the construction scenario's
turret leaves lane 1, AND `QueueLength(cy)` would read 0 while a turret built in
lane 2, so the AI would order extra buildings and every AI-driven golden would
diverge BEHAVIOURALLY rather than mechanically. Overflow gives the player the
same observable thing (order a defence while a structure builds and both now
build at once) in exactly the case no golden contains.

## As built

**The lane is a pruned side collection, never an Entity tail.**
`Dictionary<int, BuildLane>` keyed by yard id; a lane carries its own queue,
Progress, Paid and Ready. An Entity append would be hashed for every entity in
the game and would move all 24 goldens mechanically, to give a second line to the
one kind that uses it.

**The prune rule is load-bearing.** A lane entry is removed the instant it goes
inert (`BuildLane.Inert`: nothing queued, building, or waiting to place). The
prune predicate and the hash guard are the SAME expression on purpose, so "an
entry exists" provably means "this lane gates behaviour". That equivalence is
what makes the guarded fold sound rather than merely convenient, and the gate
asserts it directly.

**The hash fold is guarded and entity-scoped**, sitting inside the entity loop
right after the existing per-producer queue fold rather than as a second
top-level block, so two adjacent variable-length untagged folds can never present
an ambiguous int sequence.

**A separate advance pass.** `AdvanceBuildLanes` runs at the end of
ProductionSystem rather than as a branch inside the main producer loop: that loop
exits through half a dozen `continue`s and threading a second head through them
would risk perturbing lane-1 behaviour, the one thing that must stay identical.
It returns immediately when no lane exists, which is every golden.

**Save v8, costing no goldens.** The lane block is appended before the trailer
behind `SaveMagicV8`, with v8 added to EVERY earlier tail predicate (the
regression the serialiser's comments repeatedly warn about). `ComputeStateHash`
never reads the magic, so the version bump moves nothing. `DowngradeSave` now
takes a v8 stream and walks the queue blocks so it can DROP the lane block; its
old blanket tail copy would have carried v8-only bytes into a v7 file. It refuses
v8 as a target rather than half-copying.

**CancelProduce addresses a lane with no wire bump.** The lane rides in
`World.LaneFlag` (bit 20) of the existing AuxId, with lane 0 unchanged, so every
command and replay written before this ADR decodes identically.

**The client reads both lanes** and never guesses, because the sim decides the
lane at order time: counts sum across lanes, head progress comes from whichever
lane holds the type at its head, one PLACE prompt works through lane 1's ready
then lane 2's, the tab badge sums both, cancel prefers the later (lane 2) order,
and the tab is disabled only when BOTH ready slots are full. Shipping the sim
half alone would have repeated the outpost mistake: a feature live and invisible.

## The new gate

LaneGate (additive, standalone plus a Match stage, never a golden scenario):

- An order into an IDLE yard stays in lane 1 and creates NO lane entry. This is
  the neutrality rule itself, asserted rather than trusted.
- A second order at a BUSY yard overflows, and both lanes show progress
  simultaneously.
- The lanes hold independent ready slots; cancelling lane 2's refunds its full
  cost and leaves lane 1 untouched.
- A used-then-emptied lane hashes IDENTICALLY to a world that never had one
  (equal tick counts on both sides, or the comparison would only measure the
  tick counter).
- A v8 save round-trips both lanes bit-exact and resumes identically; a v7
  downgrade of a lane-free world loads hash-identically.

## Verification (local, real evidence)

- lanegate exit 0; full battery `match 2026` exit 0 with lanegate in it.
- The exact CI golden check BYTE-IDENTICAL across all 24 rows.
- determinism 24/24 exit 0 at seeds 2026, 31337 and 900913.
- saveload, campaignsave, stancegate, spawngate and prodgate all pass standalone
  (the format-sensitive gates, including stancegate's v7 downgrade).
- Both Godot client builds 0 warnings (Debug and ExportRelease).

## Changed / Assumed / Needed next

**Changed.** Sim: `BuildLane` and `_lanes` with LaneOf/PruneLane/LaneContents/
LaneState, the overflow routing in BuildStructure, lane-aware PlaceStructure and
CancelProduce, `AdvanceBuildLanes`, the guarded hash fold, save v8. Runner:
DowngradeSave walks the tail blocks; a LaneGate. Client: Sidebar reads both lanes
(counts, head progress, PLACE, badge, disable rule); SkirmishLive feeds the lane
and routes cancels. Docs: ADR-023, this file, the tracker and ledger.

**Assumed.** Exactly two lanes, per GDD line 45; N lanes are deferred until
something asks. One PLACE prompt working through both slots rather than two
competing widgets. Cancel prefers the later order.

**Needed next (from whom).** Game designer: whether the yard should show two
visible lines rather than merged counts, which is the honest UI question a
playtest answers. The AI still never overflows (it is serial by construction), so
only a human uses the second lane today; teaching it to would be an ai-engineer
wave and, because it would then reach lane 2 in AI-driven scenarios, it would
need its own neutrality argument.
