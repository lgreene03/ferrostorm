# ADR-081: detection is a team asset, so a cloaked enemy any teammate has detected is a target
- Status: Ratified under decision D24b of docs/tickets/P8-formidable-tracker.md ("make `CanTarget` team-wide (row P8-55, its own ADR)"), taken by the orchestrator under the owner's standing authority of 2026-10-02. Drafted by the implementer agent on row P8-55. No golden moves (measured, all 25 byte-identical), so CLAUDE.md's Architect sign-off for a golden change does not apply; the one compatibility consequence, for TEAMED matches only, is recorded under "Compatibility" so that the Architect can call it in if it should have been routed.
- Date: 2026-10-14
- Deciders: orchestrator (owner's standing authority, D24b) + implementer agent; Architect agent + Luke by the template
- GDD/TDD feature served: GDD s9 ("custom lobbies up to 4v4"); ADR-038 (teams) section 2's detector clause; decision D24 and its correction of 2026-10-02 (P8-6); row P8-55; criterion F2 ("the screen tells the truth")

## Context

D24 decided that the client treats a cloaked enemy as detected when ANY seat on
the local player's team has its bit in `DetectedMask`, and P8-6 built exactly
that: an enemy Phantom Tank that only an ally's Sentinel Scout has found is drawn
on the player's screen under the detected tint. P8-6 also found that the sim did
not agree. `World.CanTarget` read the attacker's OWN bit alone:

    (t.DetectedMask & (1 << byPlayer)) != 0

so every gun the player owned refused that tank. An ordered Attack was cleared by
CombatSystem the tick it landed, and a squad standing beside it never acquired
it. P8-6 kept the screen honest by giving the pick a second, narrower rule (the
local seat's own bit), so the cursor offered Move over an enemy it had just drawn
as detected. That is "targets my units refuse to shoot" turned into "targets my
cursor refuses to offer", which is better, but it still leaves the player looking
at a tank the team has found and cannot touch.

D24b decided to close the gap in the sim: CanTarget becomes team-wide. Its
reason was that this is what D24 meant, and that in a match where nobody calls
`SetTeam` a team IS a seat, so the change is the pure generalisation it claims
and every golden should stay byte-identical. D24b reverses if a golden moves.

## Decision

1. **`CanTarget(byPlayer, t)` reads the attacker's TEAM.** A cloaked entity is
   targetable when it is uncloaked, when it has fired (RevealTicks, unchanged,
   which reveals it to everyone), or when a detector of ANY seat on the
   attacker's team has it in `DetectedMask`. All four readers follow without
   being touched: the guard leash's acquisition, attack-move's "is the ground
   clear", the explicit-target clear and auto-acquire.

2. **The team's bits are spelt once, in `World.TeamDetectionMask(player)`**, a
   public read returning one bit per seat on the player's team, its own
   included, in `DetectedMask`'s layout. It is DERIVED on every call from the
   team map rather than cached beside it. The loop runs only for a cloaked,
   unrevealed target (CanTarget's last clause), over at most eight seats, in seat
   order. A player outside the seat range (a neutral, -1) is on no seat's team,
   so its mask is empty, which is what `1 << -1` against a byte gave before.

3. **The commander's air census asks the same rule.** `SkirmishAI.SeesAircraft`
   (ADR-072) states that it applies "the same rule the sim's own targeting
   applies (World.CanTarget)", and it had copied the per-seat expression. It
   now reads `TeamDetectionMask`, so the claim stays true. No flyer in the
   catalogue cloaks today, so this is unobservable until one does.

4. **The client drops its local-seat pick rule.** `SkirmishLive.PickHostile`
   now takes exactly what is drawn: an enemy is pickable where it is drawn, and
   drawn where the team has detected it, which is now where the sim lets the
   seat shoot it. The client's team mask asks `World.TeamDetectionMask` rather
   than rebuilding it, so drawing, picking and targeting are one rule.

5. **What does NOT change, deliberately.**
   - `DetectedMask` keeps its meaning, a per-seat record of WHO detected the
     entity, written exactly as before. The widening is on the read side, so the
     hashed and saved state of every world, teamed or not, is unchanged, and the
     client can still tell "my ally's scout found it" from "mine did".
   - **Vision does not flow** (ADR-038 section 3). Detection is pooled; fog is
     not. An enemy only an ally can see is still under the player's own fog, is
     not drawn and is not pickable (P8-5).
   - Detectors still do not reveal a teammate's stealth (ADR-038 section 2,
     P7-8c), and the veil projector still hides only its owner's units.

## Alternatives rejected

**Leave CanTarget per seat and draw only what the seat itself detects.** This
would make the screen agree with the sim by undoing D24, showing the player less
than the team knows. D24 was decided, and the genre's team games treat detection
as a team asset; the gap was in the sim, so the sim is where it was closed.

**Widen the WRITE: have DetectionSystem set every teammate's bit.** In a
two-seat world this is also neutral, but in a teamed world it changes the hashed
and saved `DetectedMask` and erases who detected what, which the client reads to
tint and the harness reads to prove an ally's scout did the finding. Widening the
read changes one predicate and no state.

**Cache a team mask per player, filled by `SetTeam`.** A second copy of the team
map, to be kept in step by `SetTeam` and by the loader (which writes the team
array directly and never calls `SetTeam`). For a loop of at most eight steps that
runs only on cloaked targets, the copy costs more in correctness than it saves.

**Share vision as well.** A separate lever, decided against by ADR-038 section 3
and not reopened by D24b. It would change what a player sees everywhere rather
than whom they can shoot among what they already see.

## Hash and format

**All 25 goldens byte-identical, measured** (`golden 2026` against
sim/golden-hashes.txt, every line in order). This holds by construction, not by
luck: no golden scenario calls `SetTeam`, so every seat is a team of one and
`TeamDetectionMask(p)` is `1 << p`, the exact bit read before. The catalogue
checksum does not move (no /data changes), the save format does not move (no
state is added) and no wire command changes.

## Compatibility

The rule changes only for a seat with a teammate, which today means only EVEN
SIDES on skirmish-09, the one four-start map. In such a match a squad now
acquires and keeps a cloaked enemy its ally has detected. So:

- a replay of a TEAMED match recorded before this change, in which a teammate
  detected a cloaked enemy within reach of the other teammate's guns, diverges
  on playback; every free-for-all and two-seat replay plays back unchanged;
- a mixed-build LAN game under EVEN SIDES desyncs at the first such engagement
  rather than being refused up front, because neither the catalogue checksum nor
  the hello carries the rules (Q024 asks for a rules epoch).

## Proof

`teamdetectgate` (a standalone runner mode and a `match` battery stage, after
`teamgate`) plays the shipped skirmish-09 under EVEN SIDES with the opening hands
placed. On quiet ground near the centre, seat 0's two rifle squads stand two
cells from an enemy Phantom Tank (seat 1, holding fire so it never reveals
itself) that only seat 2's Sentinel Scout has detected. Seat 2 is seat 0's
teammate. The ordered squad holds fire, so only its Attack can make it shoot; the
other is left Aggressive, so only auto-acquire can. The control is the identical
fixture in a free-for-all, where seat 2 is no ally. Measured: teamed, the order is
held and fires 7 shots and the unordered squad fires 7 in 60 ticks (98 damage);
free-for-all, the order is dropped and both fire 0. The preconditions are asserted
in both worlds: `DetectedMask` is 4, seat 2's bit and not seat 0's.

Proved to bite: CanTarget restored to the attacker's own bit fails the teamed
check by name ("CanTarget is not team-wide (ADR-081, D24b) ... the order was
dropped the tick it landed and 0 shots were fired"); CanTarget accepting ANY
detector's bit fails the free-for-all control by name (the order held, 7 and 7
shots, 98 damage).

On the client, `inputgate/stealth-team` drives it from seat 1 of the teamed
scene (ally seat 3, enemy seat 2): the tank only the ally's scout detects is
drawn, the cursor over it offers Attack, a right click queues the Attack and the
sim holds it as the squad's target.

## Consequences

- The screen, the cursor and the guns agree in a team game: what the team has
  detected is drawn, offered and shot. P8-6's recorded half of D24 is delivered.
- A teammate's Sentinel Scout or Watch Post is now worth as much to its allies'
  guns as to its owner's, which makes one detector per side enough in a 2v2. That
  is a balance consequence of D24 rather than of this ADR, and it is unmeasured
  until somebody plays a 2v2; it reverses with D24 if a playtest finds it too
  strong.
- Any future reader of detection asks `TeamDetectionMask` or `CanTarget` rather
  than spelling `1 << seat`, or it reintroduces the disagreement this closes.
