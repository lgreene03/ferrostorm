# ADR-055: the harness was right by luck, in the thing whose job is to catch that
- Status: Ratified
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: C7b's seat correctness; doc 24 C9; P7-13

## Context

Sixth outing of the ADR-050 method, and the largest find since ADR-050 itself.

The client harness exists because the sim battery is structurally blind to the
panel: it drives the real battle scene **from seat 1**, so any rule written as
"me versus the other one" that happens to be right at seat 0 fails there. It has
caught an inverted victory banner, a capture alert fired for a robbery, and a
Brutal handicap granted to the wrong seat on each peer.

The question one step to the side: **what has it not been asked about, in the
sixteen waves since?**

## Finding 1: the building faction gate was never checked

`Sidebar.StructButtonVisible` has existed since P5 as a test hook. **The harness
has never called it once.** Its unit-side twin `UnitButtonVisible` carries this
comment:

> TICKET-P6-FACTION-01: **the unit-side twin of StructButtonVisible**, for the
> same reason - visibility IS the faction gate, so it is what a test must read.

So the hook was built for the building side, the unit check was written citing it
as precedent, and **the building check never was** - while six faction-locked
buildings shipped (Bastion, Shroud Nest, Veil Projector, the Sodality generator,
the Watch Post, the seismic charge) and two common ones became Directorate-only.

## Finding 2, which is worse: every faction check was VACUOUS

The building check was written, it passed, and then it was **bite-tested by
breaking the rule the way it would really break** - `FactionOf(0)` instead of
`FactionOf(LocalPlayerId)`, the exact "right at seat 0" defect this harness
exists for.

**Every check still passed.**

The reason is the finding. `VerifyRunner` sets the map and the AI preset and
**never set the factions**, so both seats defaulted to 0. With both seats
Directorate, a gate reading seat 0 and a gate reading the local seat return the
same answer, and **a rule keyed on the wrong seat reads correct.**

That makes the pre-existing **unit** check vacuous too, and it has shipped that
way since TICKET-P6-FACTION-01.

**This harness's own defect shape, turned on itself: right at seat 0 by luck, in
the thing whose entire job is to catch right-at-seat-0-by-luck.**

## Decision

Three changes, and the order matters because the third is what makes the first
two mean anything.

1. **`Sidebar.StructFixedGateForTest`**, the structure twin of
   `UnitFixedGateForTest`. Separate from `StructButtonVisible` for the reason the
   unit twin records: the live prerequisite half hides almost everything at tick
   0 and would mask a faction gate that had stopped binding.

2. **The building faction check**, mirroring the unit one including its control -
   a building of the other side must be gated out, genuinely invisible, and
   refused by the sim, so the panel and the sim agree rather than both being
   wrong.

3. **The fixture now gives the two seats DIFFERENT factions**, and a check
   asserts it:

   > `the two seats hold DIFFERENT factions (1 and 0), without which every
   > faction-gate check above passes whatever seat the rule reads`

   Measured which way round they land rather than assumed. Which seat gets which
   does not matter; that they differ is the whole point.

With the fixture fixed, the same bite test now reports **8 buildings disagree**
plus both controls failing.

## What this says about bite tests

Every gate this session has been "proved to bite". This one passed its first bite
test **while being useless**, and the only reason that surfaced is that the bite
test was run at all rather than assumed.

The transferable rule: **a bite test that passes when you break the rule is
telling you about the FIXTURE, not the rule.** The instinct on seeing "I broke it
and the check still passed" is to doubt the break; the right move is to ask what
in the fixture makes the broken and unbroken versions indistinguishable.

## Consequences

Client harness **194 to 201 checks**, and seven of those are new coverage while
the pre-existing unit check stops being vacuous - so the real gain is larger than
the count.

No sim behaviour changed: all 24 goldens byte-identical, catalogue checksum
unmoved, all 18 local CI gates green, harness PASS.

Ledger for the method across six outings: **four defects, two guarantees.** And
the fourth defect was in the test suite rather than the game, which is the first
time the method has turned on the tooling and will not be the last place worth
looking.
