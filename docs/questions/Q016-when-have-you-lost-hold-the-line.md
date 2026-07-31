# Q016: when have you LOST mission 03, "Hold the Line"?

**ANSWERED AND CLOSED, 2026-08-01: option 1, and it needed a new trigger
condition rather than the obvious fix.** Decided under the standing P7 directive
rather than by the game-designer, because it was blocking P7-9. See "The answer"
at the foot of this file.

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


## The answer

**Option 1: you have lost when you hold nothing that counts as being in the
game** - which is to say, when the sim's own elimination predicate is true of
you.

Option 3 (the forward position specifically) is still the better mission and
still not mine to take: it changes what mission 03 IS, from a survival timer
into a hold objective. Option 2 needs a conjunction the trigger vocabulary does
not have. Option 1 is the one that invents nothing.

### The obvious fix does not work, and this is why it needed a condition

The tempting reading of option 1 is "just stop suppressing short game". It
cannot be done: **player 1 in mission 03 owns no structures at all.** It is a
wave-spawning attacker with nothing but the units the triggers give it. Turn
short game back on and the sim eliminates the ATTACKER on tick 0 and hands the
player an instant win. `rules noshortgame` is load-bearing, and this is the
general case rather than a quirk of one mission - missions 04 and 06 set it too.

So the mission has to be able to STATE its own defeat, and the vocabulary had
nothing that could. The new condition is `eliminated P`, and the one thing that
matters about it is that it does not restate the rule:

```
"eliminated" => !w.HasHope(I(cond[1])),
```

`World.HasHope` is the elimination predicate, extracted from inside
`VictorySystem` where it had been inlined, and now asked by both. A campaign
defeat therefore cannot drift from a skirmish one, and the ADR-005 clause 2
barrier exclusion and the ADR-021 outpost exclusion are inherited rather than
re-typed. Restating it would have been the eighth instance this phase of this
project's most common defect: a rule written twice and then only fixed once.

### Trigger order is load-bearing, exactly as this file warned

The defeat is written BELOW the win in every mission that has both. On the tick
where a player both survives to 4200 and loses their last building, they have
held the line. `MissionRunner` evaluates in file order and `DeclareWinner`
latches the first call, so file order is the tie-break - the same property
mission 02 depends on, now depended upon deliberately rather than by luck.

### What changed

- `World.HasHope(int)` and `World.IsHope(in Entity)`: the predicate, extracted.
- `MissionRunner`: the `eliminated P` condition.
- `mission-03.fmap`: `eliminated 0 -> the_line_broke`, `win 1`. The mission that
  ran forever now ends.
- Missions 04 and 06, written after this answer, use it from the start. Mission
  06 uses it in BOTH directions, so the finale owns its whole ending.
- `campaigngate` stages 3 and 4 assert it: for each noshortgame mission, erase
  the loser's holdings and require a declared winner and a message. The gate
  checks `ShortGameEnabled` is actually false first, so the test cannot pass
  vacuously.

**Measured neutral:** all 24 goldens byte-identical. `MissionRunner` state has
always lived outside the world hash.
