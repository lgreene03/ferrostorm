# Q024: a sim-rules epoch for replays and the LAN hello, because a code-only rules change is refused by neither

Owner: architect, with netcode
Raised by: the Architect's sign-off reviews of ADR-071 (P8-15, 2026-10-03) and
ADR-074 (P8-56, 2026-10-04), filed with ADR-074's before-merge condition C5
Decide by: before the first published build

Numbering: Q023 is the per-faction anti-air defence structure that tracker
decision D30 cites. It was reserved for that subject when this question was
filed, which is why this one is Q024, and it has since been filed with the
ADR-072 conditions (docs/questions/Q023-a-per-faction-anti-air-defence-structure.md).

## The problem

Two things guard against two machines simulating different rules, and both
compare the same single number: the catalogue checksum (ADR-006).

- **The replay header.** A `.frep` is `ferrostorm-replay v3`, the seed, the
  setup, a `catalogue` line, the command stream and the final hash
  (sim/Ferrostorm.Sim/Replay.cs). It carries no build or sim version.
  `Replay.AssertCatalogueMatches` refuses playback before tick 0 only when the
  recorded catalogue differs from the running one.
- **The lockstep hello.** Each client sends its `World.CatalogueChecksum` in a
  `Wire.Check` frame; the relay answers `Start` when every checksum agrees and
  `Refuse` when one differs (sim/Ferrostorm.Net/Lockstep.cs). It carries no
  build or sim version either.

The catalogue checksum moves only when a value in `/data` (or a number folded
into it) moves. **A change to what the sim's code does, with no catalogue value
moved, is invisible to both.** Three rows have now shipped such a change:

- **ADR-071's docking (P8-15).** A refinery docks from every face, and a walk
  onto a building routes to whichever face is open. Seven goldens moved; no
  Entity field, save version, wire change or `/data` change, so the catalogue
  checksum is untouched (ADR-071, "Format"). A replay recorded before it
  diverges under it wherever a harvester's route to its refinery changed, and
  nearly every match harvests.
- **ADR-074's boarding walk (P8-56).** A LoadTransport from out of reach now
  walks in and boards, or gives up at ADR-014's deadline, and the in-reach
  boarding clears the unit's orders. No golden moved and no catalogue value
  moved (ADR-074, "Compatibility").
- **ADR-075's oriented placement (P8-21).** The commander's placement scan is
  oriented to the map centre. Five commander goldens moved (four on the base
  the Architect reviewed, and `airanswer`, appended on main meanwhile); no
  catalogue value, save format or wire format moved (ADR-075,
  "Compatibility"). It adds a distinction the first two do not have: it
  changes which commands a commander CHOOSES, not a rule of the sim. A LAN
  game with a commander seat across it desyncs, because every peer generates
  that seat's commands locally (`LockstepClient.SetAiCommanders`); but a
  replay recorded before it plays back exactly, because a replay records the
  commander's commands and playback attaches no commander. So an epoch bumped
  on every golden move, which Option 1's CI tie below would enforce, would
  refuse replays that play back exactly: the epoch would need to tell a
  commander-only change, which splits the LAN hello but not the replay
  header, from a rules change, which splits both.

The comment on `LockstepClient.SetAiCommanders` (sim/Ferrostorm.Net/Lockstep.cs)
says "Two peers therefore provably run the same commander", because the
commander's tuning rides `World.CatalogueChecksum`, which the hello compares.
That holds only within one build: the commander's CODE is in no checksum, so
two builds either side of ADR-075 pass the hello with different commanders.

What happens today when the rules differ but the catalogues agree:

- **An old replay** re-simulates under the new rules from tick 0, the client
  shows the player a different match to its end, and only then does
  FinishPlayback report REPLAY DIVERGED with both hashes. Nothing told the
  player up front that this replay could not be shown.
- **A mixed-build LAN pair** passes the lobby, because the hello sees two equal
  checksums, and starts. It desyncs in play on the first tick the two rule sets
  write different hashed state; the relay compares state hashes every
  `HashInterval` (30 ticks, two seconds) and broadcasts the desync, so it is
  detected rather than silent, but the match is lost.

A rules change is refused up front today only by accident, when the same row
also happens to move a catalogue value. ADR-073 (P8-18) did, so every v3 replay
and every LAN peer from before it is refused; ADR-071 did not, and neither ADR-074 nor ADR-075 does. Whether
a build refuses an incompatible partner should not depend on whether its row
touched `/data`.

Saves are outside this question. A save is a starting state rather than a
recording: loading one under newer rules resumes a game rather than claiming to
reproduce one, so a rules change does not make it wrong in the way it makes a
replay wrong. Saves already carry and check the catalogue checksum
(World.Serialization.cs).

## Why it is cheap to leave today, and why it stops being cheap

There is no published build, so nobody outside this repository holds a replay or
plays a LAN game against an older build. TDD s9 describes a replay corpus
("curated replays re-simulated on every engine change; divergence = failed
build"), which protects the replay promise "within a major version", but no corpus
exists yet and nothing in the code knows which version a replay came from. From
the first published build onwards, a player's saved replays and a LAN game
between two players a patch apart are real, and every behaviour-changing row
becomes a silent break for them.

## Options

1. **A monotonic sim-rules epoch.** An integer constant in the sim (for example
   `World.SimRulesEpoch`), bumped by any row that changes what the sim does:
   every row whose goldens move, and every row whose ADR records a
   replay-compatibility change with goldens neutral, as ADR-074 does. It is
   written into the `.frep` header as a new line (a v4 header; a v3 stream,
   which has none, is read as "unknown, do not check" by ADR-006's own rule for
   a missing line) and carried in the hello's `Check` frame beside the catalogue
   checksum, refused on mismatch with both epochs named.
   - Costs: one wire change and one replay header bump, each itself a one-time
     incompatibility; and a discipline, because a human must bump the constant.
     The discipline can be half gated: CI can fail any change to
     sim/golden-hashes.txt that does not also change the epoch, which catches
     every golden-moving row. A behaviour change that moves no golden, ADR-074
     for one, still relies on the ADR review to ask for the bump.
   - A variant folds the epoch into `CatalogueChecksum` instead of adding a
     field. That reuses the existing refusal path with no wire or header change,
     but the refusal would then say "catalogue mismatch" when `/data` is
     identical, which sends the player to restore files that are not the
     problem, and every save would be refused across an epoch bump, which the
     paragraph on saves above argues it need not be.
2. **A hash of the sim assembly**, computed at start-up and compared in the
   header and the hello.
   - Costs: nothing to remember, but it refuses far more than it should. Any
     rebuild that changes the binary without changing behaviour (a refactor, a
     new log string, a compiler update, a Debug build against an exported
     Release one) refuses, so every patch would orphan every replay and split
     every LAN pool, behaviour change or not. It also needs byte-identical
     builds on Windows, Linux and macOS, which the project does not promise
     today and the golden-hash gate deliberately does not rely on: that gate
     compares behaviour, not binaries. And a refusal says only "different", not
     what changed.
3. **Status quo, with a documented policy.** Keep comparing only the catalogue;
   state in TDD s9 and the player-facing replay text that a replay plays only on
   the build that recorded it; optionally record the build identity in the
   header as information, never compared, so a DIVERGED verdict can say
   "recorded with a different build".
   - Costs: nothing to build. But a mixed-build LAN game still starts and still
     desyncs, a published replay corpus would rot silently between patches, and
     TDD s9's "within a major version" promise would need amending to match
     what the code does.

A fourth direction is named only to rule it out: keeping old rules runnable
behind the epoch so an old replay plays under its own rules. That keeps every
superseded behaviour alive in `/sim` for good, with a branch per change, and is a
much larger commitment than any row has asked for.

## Recommendation

**Option 1, as its own field rather than folded into the catalogue checksum,
landed before the first published build**, with the CI tie to
sim/golden-hashes.txt so that the common case cannot be forgotten. It refuses
exactly what changes behaviour, it is a constant and so agrees across platforms
by construction, and its one weakness, a forgotten bump on a golden-neutral
behaviour change, is the case an ADR already has to argue, so the ADR template
(docs/adr/ADR-000-template.md) can gain the one question "does this bump the
epoch?" in its Consequences section. Option 2 is rejected because it refuses on
binaries rather than behaviour. Option 3 is acceptable only until the first
published build, which is why that is the decide-by.

## What would settle it

An Architect decision with Netcode's agreement on the wire change, recorded as an
ADR that fixes the field, the header line, the hello frame, the refusal text and
the CI rule, and that names the rows which would have bumped the epoch so far
(ADR-071 and ADR-074 among them, and ADR-075 with the commander-only
distinction above decided one way or the other) so its first value has a
stated meaning.
