# ADR-038: teams, and the four things being allied does NOT imply
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD s9, "custom lobbies up to 4v4"; doc 24 D2; P7-8c

## Context

GDD s9 promises 4v4 and the sim had no team concept at all. This was refused
twice as a second project of comparable size to everything else in P7-8, and
that was the right call at the time: hostility was answered inline in about forty
places, and adding teams by editing forty sites is how the air layer was handled,
which went wrong three times in four.

The preceding wave made it tractable. `World.IsEnemyOf` is now the single
expression that answers "is this an enemy", and the inventory it produced is the
reason this ADR can be short: **41 sites, 9 hostility, 32 ownership.**

## Decision

### 1. A team is a per-player id, defaulting to the player's own

Every seat starts on its own team, so **a free-for-all is unchanged by
construction**. That is not a convenience, it is the mechanism that keeps all 24
goldens byte-identical, and the gate asserts it directly: with no `SetTeam` call,
three seats are mutually hostile by the predicate and in all three firefights.

`SetTeam` is settable before tick 0 and refused after, matching the catalogue
registrars. (Worth recording: `SetFaction`, which the brief pointed at as the
model, does **not** actually guard tick 0 - it is a bare array write. The
registrars were matched instead, and `SetFaction` is left as a separate finding.)

### 2. Teams change four things

- **`IsEnemyOf`**, and everything routed through it follows for free: target
  acquisition, the guard leash, the mine trigger, the AI's scans.
- **Victory** counts living TEAMS, not players. A player with no hope is still
  eliminated and announced individually; the match continues while a teammate
  stands.
- **Contact effects** (capture, theft, sabotage, demolition) become "not mine AND
  not allied", so an engineer cannot take a teammate's refinery. The neutral
  outpost case is untouched, because a neutral is nobody's ally and capturing one
  is ADR-021's whole point.
- **Detectors** stop bothering to reveal a teammate's stealth units.

### 3. And FOUR things being allied deliberately does not imply

Each of these is a separate design lever, and treating them as consequences of
alliance would be smuggling four decisions inside one. Each is left unchanged
with a comment at the site saying it is a decision:

- **Tech does not flow.** An ally's radar does not satisfy your prerequisite;
  each player builds their own tree.
- **Vision does not flow.** Allies do not share fog.
- **The veil projector** hides its owner's units, not an ally's.
- **Splash still hurts allies.** `ApplyAreaDamage` already hits friend and foe
  alike including your own units, so an ally in your howitzer's splash takes it
  exactly as your own squad does. Nothing changed here, and that IS the decision.

The conservative direction is deliberate: each of these can be turned on later
with a one-line change and a gate, and each would be a real balance shift. Turning
them all on at once, unmeasured, in a game nobody has played, would be four
untested claims wearing one feature's name.

## Hash and format

**All 24 goldens byte-identical**, because the default team map is the identity
and every changed expression reduces to what it was. The fold into the state hash
is guarded so the default case adds zero bytes, the ADR-023 lane pattern.

**The save could not be guarded the same way, and the asymmetry is worth stating
because it looks like an inconsistency.** A hash is an accumulator, so an
unexecuted fold costs nothing and no reader has to find its place. A save is a
positional byte stream, so a conditional field needs a discriminator, and the
discriminator is itself new bytes buying nothing. Decisively, there was nothing
to keep still: the goldens are state hashes and no golden scenario saves. So v11
writes the team per seat unconditionally, and v1 to v10 load with everyone on
their own team, which is exactly what those saves meant.

The catalogue checksum does **not** move: teams are match setup, like factions,
not authored numbers.

## One client defect fixed with it

`World.Winner` is a player id, and the sim names the last standing seat of the
winning team. The client compared it to `LocalPlayerId`, so **the winner's own
teammate would have seen a DEFEAT banner**.

Nothing could reach it today, since no lobby path calls `SetTeam`. It is fixed
now rather than when a 4v4 lobby lands, precisely because that is the same shape
as the seat inversion the headless harness caught twice: a comparison that is
right at one seat by luck.

**And it is NOT harness-covered, which is worth stating plainly rather than
leaving to be assumed.** I tried: the check hung the Verify scene, because
`SetTeam` is refused after tick 0 by design and the harness world has already
ticked long before the banner checks run. So a harness check would need either a
guard-bypassing hook, which weakens the tick-0 rule the sim relies on, or a
second scene built teamed from the start, which is real work.

The fix therefore rests on `teamgate` proving the sim names the winning team's
last standing seat, plus reasoning about one comparison. That is weaker evidence
than this project usually accepts for a client rule, and it is recorded as such:
**whoever wires the 4v4 lobby should add the scene-level check at the same
time**, because that is the moment the code becomes reachable and the moment a
teamed scene exists to test it in.

## Consequences

The sim plays team games. Nothing else does: **no lobby can express a team**, so
this is reachable only from a gate today. That is the honest state of the row and
it is the next thing owed - GDD s9's "up to 4v4" needs `MatchSetup` to carry a
team per seat, and the LAN blob with it.

What this does NOT deliver: the AI does not know it has allies, so two AI seats
on one team will not co-operate, merely refrain from shooting each other. That is
a real gap and it is squarely a design question rather than a defect.
