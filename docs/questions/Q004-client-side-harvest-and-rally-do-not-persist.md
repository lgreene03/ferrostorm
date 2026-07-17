# Q004: harvester auto-resume and rally points live in the client, so they do not survive a save and would not replicate

Labels: persona:P2, gdd:s4, phase:1, owner:architect
Raised by: client-engineer + sim-engineer, during TICKET-P5-BD-01 (doc 22 Wave D, the hash-neutral subset).
Decide-by: 2026-08-05 (before the netcode work makes a second client real, and before the first public build fixes the replay format).

## Question

P5-ECON-07's auto-resume and BD-14's rally points are both client state, deliberately, because the sim-side versions are `touchesSim` and would have needed an ADR this session did not have. Both are now shipped and both carry the same limitation. Should they move into the sim, and if so, in which order and under whose ADR?

## What shipped, and why it is here rather than in /sim

**Auto-resume (P5-ECON-07).** A harvester that reaches `HarvestState.Idle` stays Idle forever: only a fresh `CommandType.Harvest` sets `HState`, and nothing in the sim issues one. The AI hides this by re-issuing Harvest every tick (SkirmishAI); the player had no equivalent, and the three ordinary ways to hit it are (a) ordering Harvest before the refinery exists, (b) every reachable field running dry before an expansion opens a new one, (c) the last refinery dying and being replaced.

The client now re-issues through the ordinary command path, rate-limited to one attempt per harvester per 15 ticks, mirroring `RetargetField`'s tie-break exactly so client and sim choose the same field.

**Rally (BD-14).** `_rally` is a client dictionary keyed by structure id. The sim now names the producing structure in `GameEvent.C`, which fixed the attribution (the client used to guess by position proximity), but the rally POINT itself is still client-side.

## The limitation, stated plainly

- **Neither survives save/load.** Save a match with three rallied factories and a working harvester fleet, load it, and the rally points are gone and every Idle harvester is parked until the player notices. The world resumes bit-exact; the player's intent does not.
- **Neither would replicate.** Under lockstep both clients would need identical `_rally` contents and identical `_manuallyStopped` sets to issue identical commands. They are not part of the command stream, the hash, or the save format, so two clients would diverge in what they ISSUE while agreeing perfectly on what they simulate. This is not a desync in the sim's sense; it is worse, because the sim would faithfully execute two different games.
- **Replays are safe today** only because a replay issues no client commands at all: the auto-resume is guarded on `_replay != null` and returns immediately. That guard is load-bearing.

## The honest fix, and its cost

For auto-resume: `HarvestSystem`'s ToRefinery branch (World.cs) already retries `FindNearestRefinery` and gives up permanently instead of parking a retry, and `RetargetField` already auto-reassigns dry fields. A sim-side fix is roughly five lines, fixes the AI's per-tick command flood for free (doc 22 P5-ECON-15), and survives the ADR-004 renderer swap without being re-implemented. It regenerates goldens.

For rally: a sim-side `SetRally` command extends the wire and replay format (doc 18 M5). Bigger, and a format break.

## Candidate resolutions

1. **Fold the auto-resume into the sim** in the same ADR and the same golden regeneration as the P5-ECON-04/05/15 economy batch (doc 22 section 4.3 already groups those three; the harvest retry belongs with them and would regenerate the same AI-driven rows).
2. **Take `SetRally` into the wire format** as its own ADR, sequenced with the netcode work rather than ahead of it.
3. **Accept both as client state** and record it as a known limitation in the save documentation. Defensible for a single-player game; not defensible once a second client exists.

The recommendation is 1 now and 2 with the netcode. What must not happen is the current state persisting silently into the netcode work, at which point the divergence is discovered from a bug report rather than from this file.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** that shipping the quality of life in the client today beats shipping nothing while an ADR waits, given the replay guard makes it safe for the shipped modes.
- **Needed next (from the Architect):** a ruling on 1 versus 3 for the auto-resume, and a sequencing call on 2 against the netcode plan.

## Resolution, 2026-07-17: the rally half is RESOLVED by ADR-007 (Wave B2)

Candidate 2 was taken, but NOT on this question's recommended sequencing.
Q004 recommended folding SetRally in with the netcode; doc 23's Wave 4
measurements inverted that (the spawn-occupancy fix bricks the factory
after eleven units unless an exit move lands first), so ADR-007 was
ratified and Wave B2 implemented it ahead of the netcode. As of commit
history on ticket/p6-wave-b2:

- `CommandType.SetRally = 16` is wire format; the client's `_rally`
  dictionary is deleted and the right-click issues the command on the
  ordinary path, which also closes SPAWN-D9 (player-0-only rally).
- RallyX/RallyY/HasRally/Departing are Entity state, hashed and saved
  (save format v4; the ADR carries a dated note on the v3-to-v4 shift).
  A save now preserves rally points BY THE SIM: this question's opening
  defect ("a save drops every rally point") is dead, and the spawngate
  asserts the round trip plus the resumed-battle marker.
- Replays and future netcode carry rally as ordinary commands; no client
  dictionary needs replicating.

**Still open, deliberately: the harvester auto-resume half.** ADR-007
explicitly declines to rule on it (its sim-side fold is candidate 1,
belongs with the doc 22 economy batch and that batch's own regeneration).
The client-side auto-resume with its load-bearing `_replay != null` guard
remains shipped behaviour, and this question stays open for that ruling
alone. One interaction survived the wave intact: a rally still beats
auto-harvest (a rallied factory's fresh harvester enrols as parked), now
keyed off the sim's HasRally rather than the deleted dictionary.
