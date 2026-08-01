# ADR-039: the lobby can express a team, and the gap ADR-038 admitted is closed
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s9, "custom lobbies up to 4v4"; P7-8h

## Context

ADR-038 gave the sim teams and ended by stating the honest limit: **no lobby path
calls `SetTeam`, so team play was reachable only from a gate.**

That is exactly the shape this phase has spent five waves removing - an aircraft
with no producer, seven units with no button, a hero and a Saboteur nobody could
build. Shipping teams and moving on would have created a sixth instance of the
defect I had just spent those waves arguing against.

## Decision

### 1. A team MODE, not per-seat assignment

`FREE FOR ALL` (every seat on its own team) and `EVEN SIDES`
(`SetTeam(p, p % 2)`, so seats 0 and 2 face 1 and 3).

Rejected: a per-seat team picker. It is the general answer, it needs a row of
controls that only mean anything on the single four-start map the pool has, and
it can be added later without invalidating this. A mode expresses GDD s9's 4v4
with one field.

**FREE FOR ALL calls `SetTeam` not at all.** That is the mechanism for hash
neutrality rather than a resemblance to it: the default team map is already the
identity, so the untaken branch cannot differ from what shipped.

On a two-start map the two modes are **measured** identical, both hashing
`0x0E3B3689A8833245`, and that is a harness check rather than an argument in this
document.

### 2. Carried like `Seats`, except in LAN

The sidecar takes it optionally with 0 meaning free-for-all, so every sidecar
written before this loads as the match it actually was. No format version, no
migration.

**The LAN blob had to bump, v3 to v4**, and the asymmetry is the same one ADR-038
recorded for saves: both peers must agree on it *before tick 0* or they build
different worlds, which is not a desync but two games that never shared a start.

A consequence worth knowing rather than discovering: the relay seats peers by
arrival, so on a four-seat map the two humans take seats 0 and 1, `EVEN SIDES`
puts them on opposite sides, and each is allied with a commander at seat 2 or 3.
That is a genuinely good 2v2 and it falls out of the seating rule rather than
being designed.

### 3. The gap ADR-038 admitted is now closed

ADR-038 recorded that the client's team-victory banner was **not** harness-covered,
because `SetTeam` is refused after tick 0 and a mid-run call hung the Verify
scene. It named the condition for fixing that: a teamed scene to test in.

A team mode in `MatchSetup` is that condition. The harness now builds a **second
real scene, teamed from the start**, and asserts that the winner's TEAMMATE reads
VICTORY, with an enemy control proving the pair discriminates rather than both
moving together. Proved to bite by reverting the comparison:

> `FAIL  my TEAMMATE being named the winner reads as VICTORY at seat 1 ("DEFEAT")`

**This is the part worth keeping.** ADR-038 could have said the rule was covered
and nobody would have checked. Writing down that it was not is what made it
obvious one wave later that the fix had become cheap.

## Consequences

Team play is reachable from the menu and over LAN. Client harness 174 to 189
checks. **All 24 goldens byte-identical and the catalogue checksum unmoved.**

One trap banked rather than fixed: `LaunchNetBattle` copies the lobby's setup into
`MatchConfig` field by field and copies neither `Seats` nor `TeamMode`. It is
harmless today because the LAN scene discards its own build in favour of the
lobby's, so the blob is the only carrier that matters - but it is a trap for
whoever next reads `_setup` in a LAN branch, and it is the third time this phase
that a hand-maintained copy of something has been found lagging its source.

What this does NOT deliver: **the AI still does not know it has allies.** Two
commanders on one side will refrain from shooting each other and co-operate in no
other way. That is a design question rather than a defect, and it is the largest
remaining thing between this and a 4v4 anyone would enjoy.
