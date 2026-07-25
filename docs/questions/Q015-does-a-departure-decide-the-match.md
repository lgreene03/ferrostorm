# Q015: when a LAN player leaves, does the survivor WIN?

Labels: persona:p3, gdd:s9, phase:6, owner:game-designer + producer
Raised by: client-engineer, during P6 wave C7c (dropped-peer handling).
Decide-by: before LAN is put in front of anyone who might rage-quit, which is
the first time it matters and is therefore the first real playtest.

## Question

A LAN match ends when a player closes the game. C7c makes that **legible** - the
survivor is told the other commander has left and that the battle cannot
continue. It does **not** decide the match, because nothing authorises deciding
it.

Should a departure award the survivor a victory?

## Why this is not mine to decide

Neither the GDD nor the TDD mentions disconnects, quits, forfeits or concessions
anywhere. Grepping both documents for the whole family returns nothing.

The genre convention is obvious enough - in the classics, an opponent who quits
hands you the win - but "obvious" is exactly the argument that would have
justified inventing the Outpost's income rate, the brown-out threshold, or the
barrier cap, each of which is a ratified number in this project precisely
because someone wrote it down rather than assuming it.

A match RESULT is not presentation. It is the thing the whole simulation exists
to produce.

## What C7c shipped instead

The honest subset, which needed no ruling:

- The relay tells the survivors when a player's connection ends (wire msgType 9).
- `LockstepClient.PeerLeft` latches it, held apart from `DesyncNotified`.
- The HUD says `THE OTHER COMMANDER HAS LEFT / the battle cannot continue; stand
  down from the operations menu`, which names the one action still available.
- The notice deliberately does **not** say "the result is void", because nothing
  diverged. Saying so would be a lie about their match.

The player is no longer staring at a frozen game with no explanation, and no
result has been invented.

## The options

1. **Award the survivor the win.** Genre-conventional. Needs a decision on what
   the sim does, since `World.Winner` is driven by elimination and a departure
   eliminates nobody: either the client latches a victory outside the sim (which
   makes the result a client-side fact, and replays would not reproduce it), or
   the sim gains a concede command (hashed state, a golden question).
2. **Award nothing, as now.** The match simply ends. Honest, no invention, and
   arguably correct for a game with no ladder or persistent record to protect.
3. **Award the win only when the departure is unambiguous** (a clean quit
   through the menu, not a dropped connection), and end without a result on a
   drop. The most faithful to intent and the most work: it needs a "leaving"
   message distinct from a socket dying.

## Needed from whom

- **game-designer + producer:** the ruling, and an ADR if the answer is 1 or 3
  since both put a new fact into the match record.
- **Luke:** whether this matters before the first LAN session at all. If two
  people playing in one room is the only near-term audience, option 2 may be
  right indefinitely.
