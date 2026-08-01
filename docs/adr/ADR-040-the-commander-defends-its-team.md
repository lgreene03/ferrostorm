# ADR-040: co-operation means defending the team, and nothing else yet
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s9's 4v4, the AI half; P7-8i

## Context

ADR-038 and ADR-039 made team play real and reachable. Both ended by naming the
same remaining gap: **the AI does not know it has allies.** Two commanders on one
side refrain from shooting each other, because `IsEnemyOf` handles that, and
co-operate in no other way.

"Co-operate" is not a specification, so the first job was deciding what it means.

## The constraint that shaped the answer

`SkirmishAI` instances are independent. Each holds its own seat and nothing else,
mutates nothing, and has no channel to another commander. That is deliberate and
load-bearing: it is what makes an AI match replayable, because a replay re-runs
the bare command stream with no AI attached.

So co-operation cannot be negotiated. It has to be **derived from world state**,
which every commander already reads and which is identical on every machine.
That rules out the obvious ideas (agree a target, divide the map, request help)
and points at the ones where shared ground truth is enough.

## Decision

**Co-operation means defending the team.** One predicate.

The threat-response scan asked "is one of MY things being walked on". It now asks
"is one of MY SIDE'S things being walked on". That is the whole change, and it is
one line because the P7-8g refactor had already separated ownership from
hostility: the question this site asks is *whose ground is this*, and the answer
widened from a seat to a side.

With the default team map every seat is its own team, so `IsAlliedTo` reduces to
`PlayerId == _player` and the behaviour is byte-identical to what shipped. That
is the mechanism for hash neutrality rather than a resemblance to it, and all 24
goldens are unmoved.

### Measured as a behaviour, not asserted as a predicate

The gate runs one fixture twice: an enemy overruns seat 1's base while seat 0's
garrison idles nearby with nothing of its own at risk. **Allied: 6 orders sent to
defend it. Not allied: 0.** The difference is the assertion.

A stage that ran only the allied case would pass on a commander that charges at
everything, which is why the control is there and why the numbers are reported
rather than a boolean.

## What co-operation deliberately does NOT mean yet

Each of these is a real idea and each is a separate decision. Recorded so that
"the AI co-operates" is not read as more than it is:

- **Shared targeting.** Two allies pick waves independently, so they will often
  hit different bases. Coordinating that needs either a shared choice derived
  from world state (possible, and fiddly to make stable) or a channel between
  commanders (which would break the no-shared-state property above).
- **Economy courtesy.** Allied harvesters compete for the same nearest field, and
  nothing stops two allies expanding onto the same patch.
- **Tech or resource sharing.** ADR-038 already decided tech does not flow; this
  does not revisit it.
- **Formation or timing.** No notion of attacking together.

The smallest coherent version was chosen on purpose. Defending an ally is the one
behaviour that is unambiguous, needs no negotiation, and is visible in play within
seconds of it mattering. Everything above is a balance question in a game nobody
has played.

## Consequences

Two AI commanders on a side now come to each other's aid. **All 24 goldens
byte-identical; `lan`, `lanchaos` and the full battery green.**

What this does NOT deliver, stated plainly: an ally that is losing will still be
allowed to lose, because the responder only reacts to an intruder inside its own
guard radius of a team structure. A commander will not cross the map to help. That
is a range decision rather than a policy one, and it is a number in the same scan
if it ever wants changing.
