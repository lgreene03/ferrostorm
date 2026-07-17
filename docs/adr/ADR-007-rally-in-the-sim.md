# ADR-007: Rally points move into the simulation

- Status: Ratified (Architect authored 2026-07-17; ratified by Luke 2026-07-17 under the directive "design out and build all these", covering this ADR as drafted)
- Date: 2026-07-17
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD s5 line 45, "per-structure rally points";
  ADR-001's layering rule (the sim is the authority on gameplay, and a rally
  point is gameplay); resolves the rally half of
  docs/questions/Q004-client-side-harvest-and-rally-do-not-persist.md

## Context

Rally points live entirely in the client. `_rally` is a dictionary keyed by
structure id (SkirmishLive.cs:173), written by the order branch at
SkirmishLive.cs:2817-2819 and applied when a ProductionComplete event carrying
the producer id arrives (SkirmishLive.cs:1022). The sim has no rally notion of
any kind. Q004 states the consequences plainly and they have not improved with
age: a save drops every rally point while resuming the world bit-exact, and
under lockstep two clients would issue different command streams while
agreeing on every state hash, which is worse than a desync because the sim
would faithfully execute two different games. The client-side rally is also
issued for player 0 only (SkirmishLive.cs:2817), which is doc 23's SPAWN-D9:
minor today, critical the day netcode lands.

Q004's own recommendation was to sequence a sim-side SetRally with the netcode
work. Doc 23's Wave 4 measurements inverted that ordering, and the inversion
is the reason this ADR exists now rather than later. The spawn system has two
CRITICAL defects: every produced unit spawns on the same cell and stays there
(SPAWN-D1), and a unit completed while the spawn ring is blocked is deleted
with the credits gone and no event (SPAWN-D2; the pop at World.cs:1838 happens
before the spawn loop at :1840-1852 and there is no else branch). Both fixes
need a spawn-cell occupancy test. And the occupancy test is the most dangerous
change in the set if it lands before rally does. Two measured traps, recorded
here so the implementing ticket cannot miss them:

1. **The inverted-order trap.** A produced unit gets no exit move: it is
   created with target equal to position and Moving false, and nothing sends
   it anywhere unless a client rally happens to exist. `SpawnOffsets` has
   exactly eleven entries (World.cs:1717-1718). Add the occupancy test while
   units still never leave and the ring saturates permanently: measured on a
   patched copy, sixteen rifle squads queued at one factory with no rally
   produced eleven spawns and then a factory frozen at full BuildProgress for
   the remaining four hundred simulated seconds. Five paid units never
   appeared and the factory never produced again. The occupancy fix converts
   "units stack invisibly", which is ugly but production continues, into
   "production permanently dead", which is fatal. Rally first, occupancy
   second, and Wave 4's ticket order (SPAWN-03 before SPAWN-04) is
   load-bearing, not stylistic.

2. **The treasury-drain trap in the hold.** The correct occupancy design holds
   the factory at 100 per cent until a cell frees instead of popping the
   queue. But World.cs:1825-1826 zero BuildProgress AND BuildPaid at the top
   of the completion block, above the isCy branch and above anything the hold
   touches, so every held tick re-enters with BuildPaid 0 and the owed
   computation at World.cs:1818 recharges the FULL unit cost each tick.
   Measured: 40000 credits to zero for eleven units worth 2200, roughly 3000
   credits per second at 15 Hz, silent and permanent. The zeroing must move
   below the hold check, BuildPaid must stay intact while holding (or a
   cancel at that moment refunds nothing), and the runner must assert that a
   factory whose ring is fully blocked spends EXACTLY ZERO credits over 100
   held ticks. That assertion is the test that catches this.

## Decision

Rally becomes sim state, wire format, save format and hash input.

1. **Wire format.** `CommandType.SetRally = 16`, appended after
   `LaunchSuper = 15` (World.cs:20). The enum's unused hole at 1 is not
   reused. Replays are safe by construction: Replay.cs:32 writes the command
   type as a byte, and no existing recording contains a 16.
2. **State.** `Entity` gains `Fix64 RallyX, RallyY; bool HasRally; bool
   Departing;`, appended after FieldCloaked (World.cs:143), never inserted.
   `ApplyCommandCore` case SetRally accepts only a producing structure the
   commanding player owns (Factory or Construction Yard today; ADR-009's
   IsProducer widens the same predicate when the barracks lands, so the wire
   format does not change twice), clamps X and Y exactly as the Move case
   does (World.cs:800-801), and treats `AuxId == -1` as a clear.
3. **Hash.** The four fields are appended to `ComputeStateHash` after
   FieldCloaked (World.cs:1976). `StateHash.Add(bool)` and `Add(Fix64)` both
   exist (Determinism.cs:74-75).
4. **Save format v3**, on the Q001 v2 precedent (World.Serialization.cs:18-19):
   `SaveMagicV3 = 0x534C4133`; WriteEntity and ReadEntity append the four
   fields after FieldCloaked (World.Serialization.cs:161 and :184) in hash
   order. v1 and v2 saves still load, with no rally set and Departing false,
   which is exactly what those saves meant: rally was client state and was
   already lost on every save. `saveload` exiting 0 is the only proof the
   writer and reader agree, which is that file's own stated contract
   (World.Serialization.cs:5-9).
5. **Produced units exit toward the rally.** In ProductionSystem after the
   spawn (World.cs:1844-1847), if the producer HasRally, the new unit's
   TargetX/TargetY/Moving/UseFlow are set directly rather than synthesising a
   Command: the sim queues no commands of its own anywhere today, and
   inventing an internal command channel is a bigger change than this needs.
   Harvesters honour the rally too, matching the client's shipped
   auto-harvest precedent.
6. **The rally survives the crowd-arrival shortcut.** `Departing` is set on a
   rally spawn and cleared the tick the unit's cell differs from its spawn
   cell or the target is reached, and `&& !e.Departing` joins the guard at
   World.cs:1117-1119. Without it any rally within 4 cells of the spawn cell
   is a silent no-op (SPAWN-D3): the shortcut fires on the first movement
   tick. Units only; the shortcut is kind-gated on `EntityKind.Unit`, so
   harvesters never reach it and need no flag.
7. **No YAML.** A rally is per-instance runtime state, not a stat. Neither
   schema changes and nothing lands in /data.
8. **Client.** `_rally` (SkirmishLive.cs:173) and the ProductionComplete
   rally application (SkirmishLive.cs:1022) are deleted; the order branch at
   :2817-2819 sends SetRally instead of writing a dictionary, which closes
   SPAWN-D9 for free. `_rallyMarkers` and ForgetRally (SkirmishLive.cs:1427)
   stay, node lifetime being a client concern, but marker positions are
   driven from the sim's own RallyX/RallyY so the marker can never drift from
   the truth.

What this ADR deliberately does not resolve: Q004's other half, the harvester
auto-resume, stays client-side behind its load-bearing replay guard. Its
sim-side fold is Q004's candidate 1, belongs with the doc 22 economy batch
and that batch's own regeneration, and gets its own decision there. Q004
remains open for that ruling alone.

## Alternatives rejected

**Accept rally as client state and document it (Q004 resolution 3).**
Defensible for a single-player game, indefensible once a second client
exists, and no longer free either way: Wave 4's occupancy fix cannot land
safely before an exit move exists, so keeping rally in the client now blocks
the fix for two CRITICAL spawn defects. The cost of this option stopped being
zero the day the ring measurement was taken.

**Sequence SetRally with the netcode (Q004's own recommendation).** Overtaken
by measurement. Q004 was written before the inverted-order trap was known;
holding rally hostage to the netcode schedule holds the spawn fixes hostage
too. The netcode inherits a format that already contains SetRally, which is
strictly less work than retrofitting it.

**Synthesise a sim-side Command on spawn instead of writing movement fields.**
Rejected because the sim does not issue its own commands anywhere, and
starting to would create a second command source that replays and the wire
format would then have to reason about. Setting the fields is what the Move
handler would have done, without the machinery.

## Consequences

Easier: a save preserves the player's intent, not just the world's bits;
replays and future netcode carry rally in the ordinary command stream;
Wave 4's occupancy and hold work becomes safe to land; the rally marker
cannot lie.

Harder: this is a wire format break and a save format break in one change,
and it moves golden hashes, so it costs a full regeneration and this
sign-off. The client loses a small convenience: rally state is no longer
readable without a sim snapshot.

Committed to: the v3 magic with v1/v2 tolerant loads; the four fields in
hash order in both the hash and the save; the Departing guard; the SPAWN-04
zero-drain assertion and the exit-move-before-occupancy ordering recorded in
the Context, which the implementing tickets must not reorder.

Hash impact: MOVES golden hashes. Four new hashed Entity fields and a new
command type change the hash input and the wire format, and the exit move
changes every downstream position wherever a rally stands. One regeneration
shared with the rest of Wave 4 (SPAWN-03 + SPAWN-04 + SPAWN-05) per doc 23
section 6, on both platforms, every moved row explained. Changing a golden
hash requires an ADR plus Architect sign-off (CLAUDE.md); this ADR is that
sign-off once ratified.

Gates: TICKET-P5-SPAWN-03 implements this ADR; SPAWN-04 hard-depends on it
and carries the zero-drain assertion; SPAWN-05 batches into the same
regeneration. None of the three may start before ratification.
