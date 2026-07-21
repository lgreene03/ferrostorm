# Q003: doc 18 asks for an H hold hotkey and the sim has no stance to bind it to

Labels: persona:p2, gdd:s10, phase:2-3, owner:architect, sim-engineer
Raised by: client-engineer, during TICKET-P5-SET-01 (settings, hotkeys and the LAN front door).
Decide-by: 2026-07-29 (before the next client control-scheme ticket, which would otherwise re-open the same question).

## Question

Does Ferrostorm want a unit stance system (hold position / guard / aggressive, or some subset), or is the roadmap's "H hold" satisfied by the Stop order the sim already has?

TICKET-P5-SET-01 shipped A attack-move and S stop and **did not** bind H. This is the reason, and the ruling is the Architect's because the honest answer is a sim change.

## What doc 18 asks for

Phase A, on the order surface: "the full order surface as context-sensitive right-click ... plus A attack-move, S stop, and H hold". Two of the three are now shipped. The third has nothing to bind to.

## The finding: a stopped unit in this sim ALREADY holds position

Read together, three pieces of `World.cs` say that hold-position is the sim's default behaviour and not a separate stance:

- **Auto-acquisition never pursues.** A unit with no explicit target and no attack-move scans for enemies already inside its weapon range and fires (World.cs:1116-1137). It sets `Moving` nowhere in that branch. It will not chase a target that steps out of range, and it will not walk toward one that is out of range to begin with.
- **Only an explicit Attack order pursues.** `e.ExplicitTarget >= 0` with the target out of range sets `e.Moving = true; e.UseFlow = true` and closes (World.cs:1109-1114).
- **Only attack-move hunts.** `e.AMove` additionally tracks the nearest enemy within SIGHT and closes on it (World.cs:1135, 1167-1173).

And `CommandType.Stop` clears exactly the four fields that could make a unit move: `e.Moving = false; e.HState = HarvestState.Idle; e.ExplicitTarget = -1; e.AMove = false;` (World.cs:638-640).

So a stopped unit stands where it is and shoots whatever comes into range. That is the textbook definition of hold position. In a sim whose default stance were "guard" (chase an attacker a short distance and return), H would mean something; here there is no chase to suppress.

## Why H was not bound anyway

Binding H to `CommandType.Stop` would ship a second key doing exactly what S does, while implying to the player that a stance system exists. That is worse than an unbound key: it is a promise the sim does not keep. The player presses H, the unit does what S does, and the difference the player was told about is invisible because it is not there.

Giving H distinct behaviour means adding a stance field to `Entity`, hashing it in `ComputeStateHash`, serialising it in `World.Save`, and branching the combat system on it. That is a **golden-hash move requiring an ADR and Luke's sign-off** (CLAUDE.md, and the standing rule that regeneration needs both). TICKET-P5-SET-01 had neither and was explicitly told to prefer the client-side solution where an honest one exists. There is no honest client-side hold: the client cannot make the sim's units behave differently without the sim.

## Options

1. **Rule that Stop is hold, and drop H from doc 18's Phase A list.** Cheapest, and defensible on the evidence above. The client's hint already reads "S stop"; nothing changes.
2. **Add a stance system** (a sim ticket, an ADR, a golden regeneration, and a Balance pass, because "hold fire" and "aggressive" change what fights happen). Worth it only if the design wants stances as a mechanic rather than as a hotkey to fill a row.
3. **Add hold-fire only** (a unit that does not auto-acquire at all), which is the one genuinely absent behaviour of the three: there is currently no way to tell a unit not to shoot. Smaller than a full stance system, still a sim change with the same ADR and sign-off cost.

Option 3 is worth considering on its own merits regardless of the H hotkey: an engineer walking past an enemy it must not wake is a real scenario the sim cannot express today.

## Needed from whom

- **architect:** the ruling, and the ADR if it is option 2 or 3.
- **game-designer:** whether stances are a mechanic this game wants (option 2/3) or a hotkey looking for a job (option 1).
- **docs/design-review:** doc 18 Phase A's "H hold", either way.

## RESOLVED (2026-07-21, ADR-015, P6 Wave C1a)

Ruled **option 2, the fuller shape**: a per-unit stance system, which subsumes
option 3 (hold-fire) rather than shipping it alone. The Architect ratified
ADR-015 under Luke's standing "design out and build all these" directive, and
P6 Wave C1a built it (commits 0562aaa sim, be3fd76 client, a9d041a goldens;
delivery notes docs/tickets/P6-wave-c1-stances.md).

The engineer-past-the-sentry case this question named is now expressible.
Hold-fire is exactly option 3 as scoped here: a HoldFire unit skips the
auto-acquire branch this question located (the else taken when ExplicitTarget < 0,
the modern line of the old World.cs:1116-1137 read) and does not fire even with an
enemy in range, while an explicit Attack order still fires ("defends only if
explicitly ordered to attack"). It persists across a Move, so the engineer is set
to hold-fire and then walked past the sentry it must not wake, which is the whole
of the worked example. Guard and patrol landed beside it, closing doc 21's
P4-PORT-01. **Doc 18's "H hold" now binds to a real behaviour** and can leave the
unbound state P5-SET-01 left it in: the client defaults H to a hold-fire toggle
(absolute, so the wire command stays idempotent).

The golden cost this question flagged was paid honestly: a per-entity hashed
stance field moved all 24 goldens mechanically, proven by identity that the move
is the field-append alone with zero behavioural change at seed 2026. No
behaviour changed for any existing unit, because the default stance is Aggressive
and is exactly today's auto-acquire.
