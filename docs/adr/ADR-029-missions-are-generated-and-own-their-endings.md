# ADR-029: a mission is generated, states its own setup, and owns its endings
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing P7 directive, "work down the tracker on my own judgement, deciding rather than asking")
- GDD/TDD feature served: GDD s8 (campaign); doc 24 Tier D1; P7-9; answers Q012 and Q016

## Context

P7-9 is "campaign missions 4 to 6", filed in the tracker as **data only**. It is
not data only, and finding out why is most of what this decision records.

Three things blocked simply writing three files. Two were open questions (Q012,
whether elimination may win a scripted mission; Q016, when a mission that
suppresses the short-game rule is lost). The third was not written down
anywhere: **missions 01 to 03 are hand-typed 64x48 grids**, at a moment when the
skirmish pool had just been regenerated at 96x64 to 256x192 with a decorative
layer, on the explicit finding that the old maps were "not big and detailed
enough". Writing three more hand-typed missions would have shipped the same
complaint into the campaign.

## Decision

### 1. Mission maps are GENERATED, by the same library, in an asymmetric mode

Doc 26 section 4 says every map comes from a committed script and never by hand,
and gives the reason as the fairness invariant: 180-degree rotation symmetry,
proved cell by cell. That reason does not transfer. **A campaign mission is
asymmetric on purpose** - the player and the scripted enemy are not meant to be
evenly matched, and a mirrored mission would be a skirmish with dialogue.

The rule is kept and the reason is replaced. `Canvas` takes `symmetric=False`,
which drops the checks that are about fairness between two starts (rotation
symmetry, ferrite and outpost distance profiles, start separation) and keeps
every check that is about the map being playable at all: aprons open, density in
the 8 to 10 per cent band, and reachability. Two checks are ADDED, because a
mission has invariants a skirmish map does not:

- **Objective reachability.** Every cell the script sends the player to must be
  walkable from the start. A mission whose extraction field sits behind a sealed
  ridge is unwinnable and would only be discovered by playing it.
- **Entity placement.** Every `unit` and `structure` line is parsed back and its
  cells proved open. This is the thing a hand-typed mission gets wrong.

Both earned their place immediately. The placement check caught a phantom tank
standing on a ferrite patch in mission 06, and the crossings check - restated
for a mission as "close the crossings and the OBJECTIVE becomes unreachable" -
caught a river that was not a river: a sawtooth centre function that snapped
back fifteen cells at its period boundary, leaving a corridor straight through
itself. Neither would have been visible in the emitted file.

The single mode was chosen over a second `MissionCanvas` because a fork would
duplicate the flood fill, the density census and the emitter, and this project's
most common defect by a wide margin is a rule written twice and then fixed once.
The refactor's inertness is **measured, not argued**: all seven skirmish
generators were re-run and their output is byte-identical.

### 2. A mission declares its own setup in its own file

Missions 01 and 03 are set up by `switch (setup.MissionIndex)` in
`SkirmishLive.cs`, which grants credits and spawns a construction yard per
mission INDEX. That is a rule keyed on which mission this is rather than on what
the mission says, and it has the failure mode such rules always have: a new
mission needs an edit in C#, a matching one in the gate, and a mission whose
author forgot is silently a mission with no base.

It needs no new machinery to fix. A construction yard is `structure 0 4 CX CY`
and an opening grant is `trigger elapsed 0 -> grant 0 N`; the format has meant
both for as long as it has existed. Missions 04 to 06 say so themselves and need
no client case.

**Missions 01 and 03 are deliberately NOT migrated.** Moving their setup would
change entity spawn order and therefore two golden hashes, for a refactor with
no behavioural gain, in a wave that is already large. The divergence is recorded
in the tracker as its own row rather than left to be discovered.

### 3. `eliminated P` is a trigger condition, and it asks the sim's own predicate

Q016's answer in full is in that file. The part that belongs here is that the
condition does not restate the elimination rule:

```csharp
"eliminated" => !w.HasHope(I(cond[1])),
```

`World.HasHope` was extracted from inside `VictorySystem`, where the predicate
had been inlined, and both now ask it. A campaign defeat cannot drift from a
skirmish one, and the ADR-005 clause 2 barrier exclusion and the ADR-021 outpost
exclusion are inherited rather than re-typed.

Note what this does NOT do: it does not re-enable short game. `rules
noshortgame` stays load-bearing, because a wave-spawning attacker owns no
structures and would be eliminated on tick 0, handing the player an instant win.
The mission states its defeat while the sim stays quiet, which is the point.

### 4. Where a win and a defeat can both come true, FILE ORDER decides

`MissionRunner` evaluates triggers in file order and `DeclareWinner` latches the
first call. Every mission with both endings writes the win first, so a player
who survives to the timer on the same tick their last building falls has
survived. Mission 02 already depended on this property by luck; it is now
depended on deliberately and documented in the format's own comment.

## Alternatives rejected

**Hand-typing three more 64x48 missions.** Fastest, and it would have shipped
three more of the maps the design review had just rejected.

**A separate mission generator.** Avoids threading a flag through eight
mutators, and duplicates the proof machinery, which is how the two would drift.

**Relaxing doc 26's rule to "skirmish maps are generated".** Considered and
refused: it would license the next hand-typed mission. The rule is that a map is
generated; what varies is which invariants apply.

**Making the mission map format able to express a conjunction** ("no structures
AND no units"), which is Q016's option 2. A real format change for one mission's
sake, when the answer that invents nothing was available.

**Migrating missions 01 and 03 onto self-declared setup in this wave.** Correct,
and it moves two goldens for no behavioural gain. Deferred with a row, not
forgotten.

## Consequences

The campaign is six missions instead of three, and the three new ones are 96x72,
112x80 and 128x96 against the old 64x48, with the decorative layer the skirmish
pool got and the missions never did.

The trigger vocabulary gains one condition. `MissionRunner` state has always
lived outside the world hash, so **all 24 goldens are byte-identical and the
catalogue checksum does not move** - measured, and unusual for a P7 row.

`campaigngate` is the standing guard, and its first stage is the one worth
keeping longest: it reads `campaign.txt` the way the client sidebar does and
refuses ids that do not resolve. That header had already gone stale by six
structure types and six units before anyone noticed, because nothing read it
except a running client.

What this ADR does NOT deliver, stated so the row is not read as finished: the
missions are unplayed by a human, the AI still neither builds nor answers
aircraft (ADR-028's own caveat, which mission 05 is written around rather than
fixed by), and mission 04's gauntlet is tuned to nothing but the one AI-driven
run in the gate. Balance is a playtest, not a gate.
