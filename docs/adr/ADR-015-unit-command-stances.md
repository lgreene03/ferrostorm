# ADR-015: unit command stances - hold-fire, guard, patrol

- Status: Ratified (Architect + sim-engineer authored 2026-07-20; ratified by Luke under the standing directive "design out and build all these" of 2026-07-17, whose P6 campaign tracker names wave C1 "Unit command layer: hold-fire, guard, patrol, formations" and marks it for a regeneration; this ADR is that authority, mirroring how ADR-012 and ADR-014 cite the same directive)
- Date: 2026-07-20
- Deciders: Architect + sim-engineer + Luke, with Balance under A11 for the leash number
- GDD/TDD feature served: doc 18 Phase A order surface ("A attack-move, S stop, and H hold"); doc 21 UE5 portability audit gaps P4-PORT-01 (patrol + guard) and P4-PORT-05 (formations); resolves docs/questions/Q003-no-hold-stance-in-the-sim.md

## Context

The sim has no way to tell a unit not to shoot. Q003 established this with the
code read: auto-acquisition (World.cs CombatSystem) scans for an enemy already
inside weapon range and fires, and nothing suppresses it. A stopped unit holds
position and shoots whatever comes into range, which is hold-position but not
hold-fire. Q003's own worked example is a unit on a mission that must walk past
an enemy it must not wake: today it cannot, because the only fire discipline the
sim has is being unarmed. Q003 offered three options and flagged option 3
(hold-fire) as worth doing on its own merits regardless of the hotkey.

Two more gaps sit beside it. Doc 21's portability audit lists P4-PORT-01 (patrol
and guard commands) and P4-PORT-05 (formations) as genuine command-surface gaps
against the commercial-RTS spec, each with its intended shape: guard is a leashed
hold, patrol is waypoint cycling, formations is deterministic slot assignment on
group orders. The P6 campaign tracker folds all three plus hold-fire into wave C1
and marks the wave for one golden regeneration. TICKET-P5-SET-01 shipped A and S
but could not bind H, because H needs a sim stance and a sim stance is a golden
move that needs an ADR. This is that ADR.

## Decision

A per-unit STANCE enters the sim as hashed state, a wire command and save state,
governing three behaviours beside today's default. The default is AGGRESSIVE and
is exactly today's behaviour, so no unit changes unless something sets a
non-default stance, which no existing scenario, AI, or save does.

1. **The enum.** `Stance : byte { Aggressive = 0, HoldFire = 1, Guard = 2,
   Patrol = 3 }`. Zero is the default and the neutral serialised value, so an
   unset stance and a pre-v7 save both read Aggressive. Stances are mutually
   exclusive by construction (a unit is in exactly one), enforced by the command
   handler, so one byte carries the whole state.

2. **Hold-fire is fire discipline, and only that.** A HoldFire unit does not
   auto-acquire and does not fire, even with an enemy in weapon range. The single
   combat change is that CombatSystem's auto-acquire branch (the `else` taken when
   `ExplicitTarget < 0`) runs only when the stance is not HoldFire. An EXPLICIT
   Attack order still fires: the explicit-target branch is untouched, so
   "defends only if explicitly ordered to attack" (Q003) holds for free. Hold-fire
   PERSISTS across a Move order, which is the whole of Q003's engineer case: you
   set the unit to hold-fire, then walk it past the sentry, and it does not open
   fire on the way. Only its own command, or a Guard/Patrol command, changes it.

3. **Guard is a leashed hold that engages and returns.** A Guard unit holds a
   POST (its position when the guard order was given, `PostX/PostY`). Each tick a
   new StanceSystem step, ordered before movement, finds the nearest targetable
   enemy within the unit's own SIGHT of the post - the leash - and, if one
   exists and the unit is armed, sets it as the unit's ExplicitTarget. That routes
   the engagement through the battle-tested explicit-attack machinery, which closes
   to weapon range and fires and is immune to the crowd-arrival shortcut (the
   shortcut only fires with `ExplicitTarget < 0`), so even a short-range guard
   closes properly. When no enemy is within the leash the unit drops any standing
   target and returns to the post, settling within the four-cell crowd radius. The
   leash is the unit's own Sight rather than a new stat: guard engages what it can
   see from its post, which is data-driven per unit and needs no schema change and
   no magic constant. Balance may promote it to a dedicated leash stat later; the
   wire format does not change if they do (the leash is read, never sent).

4. **Patrol is deterministic two-point cycling.** A Patrol unit ping-pongs between
   endpoint A (`PostX/PostY`, its position when ordered) and endpoint B
   (`PatrolX/PatrolY`, the ordered point), attack-moving each leg so it engages
   anything met en route and resumes after. The order kicks off the outbound leg
   (an attack-move toward B, `PatrolOutbound = true`). StanceSystem watches for the
   leg to complete - `AMove` clears only at or near the destination with the area
   clear, which is exactly "reached this endpoint" - then flips `PatrolOutbound`,
   re-arms the no-progress backstop, and starts the return leg. This reuses the
   attack-move completion and pursuit machinery whole; patrol adds only the flip.
   Two waypoints, not more: the classic patrol mechanic is a ping-pong, it fits the
   entity struct without a side list, and multi-waypoint patrol via the shift-queue
   is a clean later extension that this format already leaves room for.

5. **The stance commands.** One wire command, `CommandType.SetStance = 17`,
   appended after `SetRally = 16` (the enum's unused hole at 1 stays unused).
   `AuxId` carries the target stance value; for Patrol, `X/Y` carry endpoint B, and
   the handler reads the unit's live position for the post/origin. Absolute, not a
   toggle: the client turns H into "set aggressive if already hold-fire, else set
   hold-fire", so the sim command is idempotent and replay-robust. Only a Unit
   accepts it (turrets, harvesters and structures never carry a non-default
   stance), which keeps the neutralisation clean. A Move, PathMove, AttackMove,
   Stop, Attack or Harvest order cancels Guard and Patrol back to Aggressive - a
   new activity supersedes a standing positional one - but PRESERVES HoldFire,
   because fire discipline is a persisting preference and Q003's engineer needs it
   to survive the very Move that carries it past the sentry.

6. **State, hash and save.** `Entity` gains `Stance Stance; Fix64 PostX, PostY;
   Fix64 PatrolX, PatrolY; bool PatrolOutbound;`, appended after the ADR-014
   no-progress tail, never inserted, in declaration order which is hash order and
   save order. All six are HASHED (ComputeStateHash) and SERIALIZED (save v7,
   `SaveMagicV7 = 0x534C4137`), following the ADR-007 rally precedent for mutable
   per-entity command state: a stance changes what a unit does, so two clients must
   agree on it exactly, unlike ADR-012's immutable-and-therefore-unhashed
   FerriteCap. v1..v6 saves load with Stance defaulted to Aggressive and the
   position fields zero, which is exactly what those saves meant (no stance
   existed), so old saves and replays resume identically.

7. **The system step.** `StanceSystem` runs immediately after OrderDispatchSystem
   and before MovementSystem, so a stance's movement decision takes effect the same
   tick. It is a no-op for Aggressive and HoldFire units (it touches only Guard and
   Patrol), which is what makes the golden move purely the hashed-field append.

8. **No YAML.** A stance is per-instance runtime state, not a stat. No schema
   changes and nothing lands in /data. The guard leash reads the existing Sight
   stat.

## Formations: split to C1b, not half-built here

Formations (P4-PORT-05) is the largest sub-feature and ships as its own ticket,
TICKET-P6-C1b, filed alongside this wave. The three stances are self-contained and
close Q003 and P4-PORT-01 cleanly; formations is a different problem - deterministic
slot assignment and a group-order layer above the per-unit command - that would
either bloat this wave or land half-built. The crowd-arrival settle (units freeze
within four cells of a shared destination) already gives massed orders a coherent
enough body for now; formations tightens that into assigned slots and is worth its
own design pass and its own regeneration. Shipping C1a (the three stances) and
filing C1b (formations) is the honest split the wave brief invites. The tracker row
becomes C1a DONE with C1b filed pending.

## Alternatives rejected

**Rule that Stop is hold and bind H to Stop (Q003 option 1).** Cheapest, but it
ships a second key doing what S already does while implying a stance system the sim
does not have - a promise the sim cannot keep, worse than an unbound key. And it
leaves the genuinely absent behaviour, a unit that does not auto-acquire, still
absent. Rejected because the design wants stances as a mechanic, which the
portability audit's P4-PORT-01 independently asks for.

**Hold-fire as a separate boolean rather than a stance value.** Cleaner in the
abstract (fire discipline is orthogonal to the guard/patrol activity), but the four
behaviours are mutually exclusive in practice and a single byte with explicit
transition rules is less state and less interaction surface than a bool plus an
activity enum. The transition rules (Move cancels Guard/Patrol, preserves HoldFire)
give the two-axis feel without the two fields.

**Guard sets movement directly instead of via ExplicitTarget.** Rejected: the
crowd-arrival shortcut stops a plain flow-move within four cells of its target, so a
short-range guard driving straight at an enemy would halt out of range and never
close. Routing guard pursuit through ExplicitTarget inherits the explicit-attack
path's close-to-weapon-range behaviour, which the shortcut deliberately exempts, so
guards of every range close correctly with no new movement code.

**Multi-waypoint patrol with a side list.** Deferred. A per-entity waypoint list
(the _orderQueues precedent) would carry three-plus waypoints and hash and serialise
like the order queues do, but two-point ping-pong is the classic mechanic, fits the
struct, and the shift-queue is the natural multi-waypoint extension when a design
need appears. Building the list now is machinery ahead of the requirement.

**Leave the stance fields unhashed (serialise only, like FerriteCap) to keep every
seed-2026 golden byte-identical.** Tempting, and with no scenario setting a stance
the fields are constant zero, but a stance is MUTABLE state that gates what a unit
does, so the rally precedent applies: mutable command state is hashed, for
desync-detection defence in depth. Serialisation alone would also fail the save/load
contract the day a design reads a mid-guard save without the field in the hash. We
accept moving all 24 goldens and prove the move is purely mechanical.

## Consequences

Easier: Q003's engineer-past-the-sentry case is expressible; doc 18's H hold binds
to a real behaviour; guard and patrol close P4-PORT-01; the command surface matches
the portable-RTS spec on all but formations; the AI can adopt stances later through
the same command it already uses for everything else, with no format change.

Harder: six new per-entity fields to carry in the hash and the save; save format is
v7 (v1..v6 load with stance defaulted to Aggressive); the golden hashes move and
cost this regeneration and sign-off. The client gains three controls and a stance
readout, all presentation-only over the one SetStance command (ADR-001 intact).

Hash impact: MOVES all 24 golden hashes. Like ADR-014's rally-pattern move and unlike
ADR-012's selective one, a per-entity HASHED field shifts every scenario's
fingerprint mechanically. The neutralisation proof is therefore by IDENTITY, not by
which rows move: on a scratch build with the fields still hashed but the StanceSystem
skipped and the hold-fire combat guard forced off (every unit treated as Aggressive),
the seed-2026 goldens are BYTE-IDENTICAL to the shipped build across all 24
scenarios. That proves the stance layer never fires at seed 2026 - no scenario sets a
non-default stance - so every golden move is the mechanical field-append alone, zero
behavioural change at the golden seed. One regeneration under this ADR, cross-platform
proven by the determinism CI on Windows and Linux, per the doc 23 section 6
discipline.

Gates: the implementing wave adds a StanceGate to the battery (a hold-fire unit with
an enemy in range does NOT fire while a normally-stanced unit in the same setup DOES;
a guard engages an intruder within its leash and returns to its post; a patrol cycles
its two waypoints; a v7 save round-trips stance and a v6 downgrade loads Aggressive
hash-identically). The full five-seed determinism suite plus the 20-game LAN soak exit
0; saveload and campaignsave round-trips prove the v7 serializer order.
