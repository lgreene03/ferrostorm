# Q016: when have you LOST mission 03, "Hold the Line"?

Labels: persona:p1, gdd:s8, phase:6, owner:game-designer + producer
Raised by: client-engineer, while giving mission 02 a defeat path (the same wave).
Decide-by: before mission-03 acceptance tests are written, which is the same
deadline Q012 already carries for the same reason.

## Question

Mission 03 sets `rules noshortgame`, which disables the elimination rule, and
carries no `win 1` action. So a player who loses everything is neither defeated
nor able to win: the mission runs forever.

Mission 02, fixed in this wave, had the same hole and a **forced** answer. Its
objective is `owned prize 0`, the prize is taken by ENGINEER, and the map gives
exactly one. Lose the engineer and the objective is unreachable by construction,
so `destroyed wrench -> win 1` is not a design choice - it is the mission's own
logic stated out loud.

**Mission 03 has no forced answer, which is why it is a question and not a
commit.** Its objective is `elapsed 4200 -> win 0`: survive. Losing is therefore
whatever "you did not survive" means, and the map does not say.

## Why I did not just pick one

The candidates are not equivalent, and each implies a different mission:

1. **All player structures destroyed.** The closest thing to the sim's own
   elimination rule, restated explicitly because `noshortgame` suppressed it.
   But it means a player whose base is razed at tick 4100 loses, even with an
   army intact and a hundred ticks to run.
2. **All player structures AND units destroyed.** Total wipe. Generous, and it
   permits the degenerate finish where the last rifleman hides in a corner for
   two thousand ticks while the timer expires.
3. **The forward position specifically** - the two turrets in the gap at (26,22)
   and (26,26). Truest to the briefing ("The gap is yours. Keep it."), and the
   only option under which the mission is about the LINE rather than about
   survival. Also the harshest, and the easiest to lose to a lucky howitzer.

Option 3 is the one I would argue for on fiction, and that is exactly why I did
not take it: it changes what the mission IS, from a survival timer into a hold
objective, and that is a designer's call rather than a defect fix.

## The constraint any answer has to respect

The trigger vocabulary has **one condition per trigger** and no conjunction, and
one tag per entity (`MapLoader`, `p[5]`). So "structures gone AND units gone"
cannot be written as a single trigger today. Option 2 therefore needs either a
format change or a second tag on shared entities - worth knowing before it is
chosen.

Also relevant, and the trap mission 02 walked into: on a WIN the engineer is
CONSUMED, so `destroyed wrench` becomes true on the winning tick too. It is
harmless only because triggers evaluate in file order and `DeclareWinner`
latches the first call, so the `win 0` written above it lands first. Any defeat
trigger added to mission 03 needs the same check - if the victory condition can
destroy the tagged thing, order becomes load-bearing.

## Needed from whom

- **game-designer:** which of the three, or a fourth.
- **producer:** whether this is worth answering before the campaign is played at
  all. Mission 03 is currently *completable* (survive to 4200) and only fails to
  declare a LOSS, so it is a worse bug than mission 02's was in principle and a
  milder one in practice.

## Related

Q012 asks whether elimination is a legitimate mission win, and fork 2 of that
question ("a scripted mission's objective is the only win") explicitly requires
that "the player-loss path must be re-specified explicitly" - which is this
question for mission 03. Answer Q012 first if the two are taken together.
