# ADR-052: dead weight accumulates, and the big map does not end
- Status: Ratified (two findings recorded, both refused for now)
- Date: 2026-08-02
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD pillar 2; TDD s6's entity budget; P7-10

## Context

Third wave running of the ADR-050 method: **ask what nothing asserts.** The
question this time was whether a long match degrades, because entity ids are
stable by construction - `Add` appends, death sets `Alive = false`, and a save, a
replay and a LAN peer all name entities by index - so **the list can never be
compacted** and every system walks all of it.

The existing load scenario measures 600 units standing still for 1000 ticks. That
is a snapshot. Nothing had ever measured accumulation over the length of a real
game.

## Finding 1: the list grows without bound while the living count stays flat

`churnprobe` on skirmish-07, the basin, whose starts are 231 cells apart
precisely so a crossing is a commitment, over 27000 ticks (30 minutes, the top of
GDD pillar 2's window):

```
   tick   entities   alive   dead   dead%   ms/1000   army0   army1   match
   4500        211     194     17      8%       863      16      15   running
   9000        269     183     86     31%      1270       8      11   running
  13500        343     203    140     40%       985      18      16   running
  18000        422     199    223     52%      1017      23       9   running
  22500        503     202    301     59%      1209      21      16   running
  27000        558     194    364     65%       652      13      16   running
```

**The living count is flat at about 200 and the list has grown to 558.** Two
thirds of what every system walks on every tick is a corpse, and the ratio climbs
linearly with match length.

On skirmish-01 it does not, and the reason is worth stating because it is why
this was invisible: that match **resolves at tick 13500** (seat 0 wins, about
fifteen minutes) and everything freezes. A short map hides the growth by ending.

## Finding 2: the big map does not resolve inside GDD pillar 2's window

GDD pillar 2: **"Games resolve in 15-30 minutes."** skirmish-07 is still
`running` at 27000 ticks with both armies healthy, 13 against 16.

`basingate` already plays this map for 20000 ticks and concludes "it is a match,
not a stalemate on open ground". **That is a different claim.** It measured
whether the commanders expand and fight, and they do; it never asked whether the
match ENDS. This is the third time in three waves that a defect was found beside
a gate that asks a neighbouring question.

## Decision: both refused for now, both recorded

### Finding 1, dead weight

The fix is a **free list**: reuse the slots of dead entities. It is deterministic
and therefore legitimate - every peer would reuse the same slot on the same tick -
but it changes what an entity id MEANS across time, and ids are the identity that
saves, replays and the LAN protocol are built on. A replay recorded before the
change would name different entities after it.

**Not taken, because it is not yet costing anything.** At 30 minutes the sim runs
about 1 ms per tick against TDD s6's 8 ms budget, with two thirds of the list
dead. The growth is linear rather than explosive, so the headroom is real.

Rejected alternative: **a periodic compaction pass.** It is worse than the free
list in the one way that matters, because it renumbers LIVING entities and would
invalidate every in-flight order, target and harvester assignment that names one.

#### The argument that would have to be overturned

1. **The per-tick budget is approached.** Re-run `churnprobe`; if a 30-minute
   match on the largest supported map crosses about half of TDD s6's 8 ms, the
   headroom has gone and the free list earns its replay-compatibility break.
2. **Matches routinely run far longer than 30 minutes**, which would push the
   dead ratio past two thirds. That is really finding 2, and fixing that removes
   most of this.
3. **A supported map is much larger than skirmish-07.** `sizeprobe` exists for
   exactly this question.

### Finding 2, the match that does not end

Not taken because **it is a balance question and the cheapest possible
information has not been gathered.** GDD pillar 2 is a design promise, and
whether skirmish-07 is too large, its commanders too passive, or its expansion
sites too generous is precisely what a playtest answers. Guessing at it by tuning
the AI would be inventing a balance change to satisfy a number.

#### The argument that would have to be overturned

1. **A playtest.** Four ADRs have now asked for one and this is the fifth; it is
   also the cheapest of these to satisfy.
2. **A deliberate decision that skirmish-07 is a "long game" map** and pillar 2's
   window applies to the standard maps only. That is a Producer amendment to the
   GDD or to the map's own brief, not an engineering row.
3. **The commander gains a closing behaviour** - massing rather than trickling
   waves - which is a real AI row and would want measuring against this probe.

## What ships

`churnprobe`, a non-asserting probe, taking a map name so both cases can be seen
side by side. Like `economyprobe` and `dockprobe` it exists so the next person to
ask "does a long match degrade" answers it in a minute with evidence.

It reports the living count beside the total deliberately: **the total alone
looks alarming and means nothing**, since a growing list with a growing
population is just a busy game. It is the flat living count next to the climbing
total that makes it dead weight.

No behaviour changed. All 24 goldens byte-identical, catalogue checksum unmoved,
all 18 local CI gates green.

## Consequences

Two things are now known that were not, and neither is a crisis: the sim carries
two thirds dead weight at the end of a long match and still runs at an eighth of
its budget, and the biggest map does not finish inside the window the GDD
promises.

The method's third payoff in three waves, and the pattern in all three is the
same: **the defect sat next to a gate that asked a neighbouring question.**
`basingate` asked whether skirmish-07 was a stalemate and not whether it ended;
the load scenario asked what 600 units cost and not what a match accumulates.
Asking the question one step to the side of an existing gate has found something
every time.
