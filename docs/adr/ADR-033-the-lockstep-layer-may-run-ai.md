# ADR-033: the lockstep layer may run AI, and a seat without a peer gets a commander
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive to work the tracker on my own judgement)
- GDD/TDD feature served: GDD s9 (skirmish and multiplayer); P7-8f

## Context

P7-8a to P7-8e made the game play free-for-all at any seat count, gave the pool a
four-start map, and let a player choose how many opponents to face. LAN was left
out: `LanLobby.BuildFrom` refuses any map seating more than two, because a LAN
match seats exactly two humans and the spare seats would have **no controller at
all**. They would never act, and `VictorySystem` would refuse to end the match
until somebody walked over and razed them. A match that cannot finish.

That refusal was correct when written and is a limitation, not a design. This
lifts it.

## Decision

### 1. A seat with no peer gets an AI commander, generated LOCALLY by every peer

AI seats cannot submit through the relay: the relay counts one batch per player
per tick and its players are peers. So each peer generates the AI seats' commands
itself and applies them to the same tick.

That is sound because three things hold together, and it is worth naming all
three because removing any one breaks it:

- both peers hold an identical world at tick T, which is the lockstep guarantee;
- `SkirmishAI` is deterministic and reads only world state, holding no privileged
  access and mutating nothing;
- **its tuning is authored data folded into `CatalogueChecksum` (ADR-032)**,
  which the LAN Hello already compares and already refuses a mismatch on.

The third is the one that was not true a wave ago. While the AI's numbers were
compiled, two peers agreed on them by construction; the moment they moved to
`/data` that became "agreed only if checked", and the checksum is the check. This
decision rests on that one, and would have been unsafe before it.

### 2. One helper owns "what a tick contains, then step"

`TryAdvanceTick` and `AdvanceTick` both pulled the merged batch and stepped, with
duplicated hash-reporting tails. A rule stated twice and fixed once is this
repository's most expensive recurring defect, and adding AI to only one of two
step paths would be a desync that appears solely under whichever drive the other
gate uses.

So both now call one private helper. Order inside it is load-bearing and stated
as such: **the merged relay batch first, then each commander in ascending seat
order.** Commanders are sorted at the setter rather than trusted to arrive
sorted, and attaching after tick 0 is refused.

With no commanders attached the helper takes a separate plain `Step(merged)`
branch, so the empty case is unchanged as a code fact rather than as an argument.
That case is every existing LAN match.

### 3. The safety net is proved, not assumed

The gate's fourth stage gives one peer a commander on a different rung and
asserts the relay's hash comparison **catches** it. A stage that only ever proves
agreement cannot distinguish a working detector from a silent one.

Establishing that it bites needed care and the method is worth recording:
removing an assertion can never make a stage fail, so the falsification is to
neutralise the DIVERGENCE and check the stage then fails for want of a desync.
Stage 1 is the matching control, with identical commanders agreeing over 900
ticks.

## Two defects this wave surfaced, neither of them its subject

**The Brutal handicap was peer-dependent.** The offline rule grants it to "every
seat that is not the local one", and `LocalPlayerId` differs BETWEEN THE PEERS:
the host would grant it to seat 1 and the joiner to seat 0, so the two worlds
part company before tick 0 while each machine's rule reads correct in isolation.
The CI hardcoded-seat guard cannot catch this, because `seat != LocalPlayerId` is
exactly the shape that guard wants to see. The LAN arm grants it to the COMMANDED
seats instead, which is peer-independent by construction and is also the honest
answer.

**The LAN setup blob did not carry the seat count.** `MatchSetupBlob` held eight
fields and not `Seats`, so a joiner decoded zero, which `SeatsFor` reads as "fill
the map". On a two-start map both sides answer 2 and the omission was invisible.
The moment the refusal lifts, a host asking for two seats on skirmish-09 builds a
two-seat world against the joiner's four-seat one, which is not a desync at the
first order: it is two peers that never shared tick 0. Blob version 2 to 3.

Both were latent behind the refusal this wave removed. Neither would have been
found by reading the code, because each is locally correct.

## Alternatives rejected

**Keeping the refusal.** Honest, and it permanently excludes the only multi-seat
map from the only mode where a second human can play.

**Having the host generate AI commands and relay them.** Simpler to reason about,
and it adds a round trip of latency to every AI order and makes the host
authoritative over sim content in a design whose whole premise is that nobody is.

**Ignoring the host's seat choice in LAN and always filling the map.** Avoids the
blob bump, and silently overrides a control the host screen already offers.

## Consequences

A LAN match may use any map in the pool. Two humans on skirmish-09 play against
two AI commanders that both machines run identically.

**All 24 goldens byte-identical.** `lan 20` and `lanchaos` complete with zero
desyncs, and the no-commanders scenario still hashes the figure it produced
before commanders existed.

What this does NOT deliver: the client-side rule about which seats get commanders
is covered by reasoning and a blob round-trip check, not by the client harness,
whose LAN acceptance stage builds a two-seat world on skirmish-02. Extending it
to two real four-seat scenes is a piece of work of its own. And nobody has played
a LAN match on a four-seat map, which is the same caveat every wave this phase
has carried.
