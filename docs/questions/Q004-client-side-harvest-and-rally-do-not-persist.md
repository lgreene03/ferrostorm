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
