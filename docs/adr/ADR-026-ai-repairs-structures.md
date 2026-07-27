# ADR-026: the AI repairs its damaged structures
- Status: Ratified
- Date: 2026-07-27
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD pillar 5 "modern hands, classic heart"; doc 27 (design review) tier 4, row DR-13

## Context
Doc 27's design review named an AI that mends its damaged buildings as the
endgame's most-noticed absent behaviour. The Repair command has existed since
TICKET-P2-SIM-08 (a structure toggles repair for 2 hp per 1 credit per tick,
switching off at full health and stalling while broke) and past the opening the
commander has the credits, but SkirmishAI never issued it: a battered enemy base
simply decayed and stood, which reads as an AI that has given up.

Every recent AI behaviour shipped hash-neutral by being provably inert on the
golden maps (the outpost block, C4c; the fire sale, DR-10). Structure repair is
different in kind, because structure damage is not map-specific: it is what
combat does. The skirmish golden is a 5000-tick AI-vs-AI match that asserts
combat happened, so an AI structure is battered there and a repairing AI now
mends it. The change is fully deterministic (run-to-run identical on all 24
scenarios) but it MOVES the skirmish golden. Under the standing law a golden
move needs an ADR plus sign-off, which is what forces this decision now.

## Decision
SkirmishAI gains one additive block: for each own structure below maximum health
that is not already repairing, when the treasury can pay at least one tick, issue
Repair once. The command toggles and the sim runs the mend to completion unaided,
so the block's only job is to flip repair on once per damage episode; the
"not already repairing" guard is what stops it toggling a live repair back off on
the following beat, and the credit floor keeps it from issuing a mend it cannot
begin to pay.

The skirmish golden is regenerated from 0x0EA347F18E08EA43 to 0x9D396D315554D987.
Only that one line moves; the other 23 goldens are byte-identical, proven by the
compare. The regeneration is authorised as a pre-first-public-build change, the
window the golden file's own header reserves for exactly this: no shipped build
yet carries the old skirmish replay, so no player replay is broken.

## Alternatives rejected
Gate the repair so it provably cannot fire inside the skirmish golden (keeping
the hash neutral). Rejected as dishonest: the scenario is precisely where an AI
should repair, and contorting the rule to dodge one test would make the golden
lie about the behaviour it is meant to pin.

Repair only below a damage threshold, or only the Construction Yard. Rejected as
unmotivated complexity for a first delivery; repair is cheap and self-halting, so
mending any damaged structure whenever the credits exist is both correct classic
behaviour and the simplest rule that is right. A threshold can follow on evidence.

## Consequences
A beaten-down base now heals between waves, so the endgame reads as a live
opponent rather than an abandoned one, and sieges must out-damage repair rather
than merely out-range a static wall (the ADR-010 defence conversation gains a
second lever). The behaviour is pinned by a new standalone gate, airepairgate,
in the repairgate/firesalegate pattern (a Match-battery stage and a named mode,
never a golden scenario, so the golden list stays 24): it proves the AI mends a
damaged own structure at the sim's rate, never touches a healthy one, and never
toggles repair while broke. From the first public build onward the regenerated
skirmish hash is load-bearing like any other and a further move needs its own
sign-off.
