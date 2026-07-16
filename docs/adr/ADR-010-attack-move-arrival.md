# ADR-010: Attack-move arrival, and what may end the stance
- Status: Proposed
- Date: 2026-07-17
- Deciders: Architect agent + Luke
- GDD/TDD feature served: GDD s6 line 53 (artillery beats static defence);
  TICKET-P2-UX-01 (attack-move); unblocks TICKET-P5-DEF-17's negative controls
  and TICKET-P5-DEF-14's acceptance test.
- Numbering note: drafted as ADR-007 until doc 23's prose reservations
  (007 rally, 008 power, 009 roster) were found; those reservations are now
  mirrored in ADR-open-queue.md so the next drafter greps them.

## Context

Attack-move does not prosecute a base. 3000 credits of rifle squads ordered to
attack-move at a fortified base kill both turrets, then park seven cells from a
full-hit-point Construction Yard and never touch it, at 73% army strength, for
as long as the fixture runs. The same rifles told to Attack the yard directly
raze it at tick 767 losing nothing, so the army is entirely capable of the job.
Attack-move simply stops trying.

The mechanism was measured rather than inferred, by instrumenting every site
that clears `AMove` and recording the tick and position at which each unit lost
the stance. **The result contradicts the diagnosis recorded in
docs/balance/2026-07-15-turtle-gate.md (journal step 5), and that matters,
because the wrong line was accused.** Sites below are cited at their pre-fix
coordinates in World.cs as of commit 3583157:

| site (baseline) | what it is | units it cancelled |
|---|---|---|
| World.cs:1488 | the stall-cancel, the accused line | **0** |
| World.cs:1203 | attack-move completion | 1 (the lead unit) |
| World.cs:1436 | arrival contagion | 10 (the rest of the army) |
| World.cs:937 | exact arrival in MoveTo | 0 |

All eleven survivors lost the stance inside a six-tick window (527 to 532). The
stall-cancel needs thirty-plus ticks of accumulation to fire and therefore
cannot produce a six-tick cascade. It never ran. Fixing it, as the journal
proposed, would have changed nothing.

What actually happens is a two-step collapse, and both steps are arithmetic:

1. **The lead unit completes its order two cells short of seeing the target.**
   The completion radius is 4 cells (`DistSq <= 16`). The ordered point sits 3
   cells from the yard centre. So the lead unit halts 7 cells from the yard.
   Rifle sight is 5. It sees nothing, declares "all clear", and ends the order.
   Measured: the lead unit stopped at 3.79 cells from the ordered point, 6.81
   from the yard. The closest any unit ever got was 6.81. They never saw the
   thing they were sent to kill.
2. **That stopped unit poisons the whole army.** Halting via the completion
   branch leaves `TargetX/Y` on the ordered point, which makes it a textbook
   arrival-contagion seed. Every unit behind it, marching to the same point,
   is told "the crowd has reached the destination, you have arrived too" and
   loses the stance within five ticks. None of them had arrived.

The general failure condition is `gap + 4 > sight`, where `gap` is the distance
from the ordered point to the target. For a rifle squad (sight 5) any ordered
point more than **one cell** off the target strands the army. This is why the
defect never showed up in a field battle, and why the existing `attackmove`
golden scenario is blind to it: its hash does not move under this fix.

A second, independent seed exists and was also measured: a unit that halts to
**fire** keeps `TargetX/Y` on its ordered point too, because the combat branch
stops it without retargeting. Three howitzers shelling turrets from range 9
stripped the stance from each other at tick 162, nineteen cells from the
objective. They finished the yard only because range 9 happened to cover where
they stopped. The same seed is what deadlocked the howitzer-mirror row of the
balance matrix (UNRESOLVED at the 200-second cap in the shipped 2026-07-15
report): two artillery lines froze each other before either landed a kill.

## Decision

One principle, applied to every site that ends the stance: **an arrival
inference requires an arrival.** Each of these heuristics concludes "you have
arrived, stop fighting"; each is sound only where an arrival actually occurred.

1. **Completion judges "all clear" from the ordered point, not from the unit's
   feet.** An attack-move may not complete while a targetable enemy stands
   within `Sight` of the point the unit was sent to. The predicate is a new
   helper, `EnemyNearAMovePoint`, evaluated lazily AT the completion sites
   rather than accumulated during the auto-acquire scan, for two reasons found
   in adversarial review of the first cut: (a) a flag set only inside the
   auto-acquire scan is silently false for a unit holding an `ExplicitTarget`
   (the breach path holds one while the stance is up), so such a unit could
   still complete beside a live enemy; evaluating at the site closes that
   path; (b) it moves the cost from one squared distance per enemy per tick to
   one scan per completion attempt. The helper mirrors auto-acquisition's
   filters exactly: barriers and ferrite fields are not "something to fight"
   (ADR-005 clause 2) and stealth follows CanTarget, so an unseen enemy cannot
   hold the stance open.
2. **The same rule guards exact arrival in MoveTo** (baseline World.cs:937).
   Landing exactly on a coordinate stops the walk; it only ends an attack-move
   if that coordinate is the ordered point and the helper agrees there is
   nothing to fight. Previously an exact landing released the stance
   unconditionally, including a landing on a pursuit target's coordinates.
3. **Arrival contagion requires the neighbour to have arrived**, expressed as
   `!o.AMove`. A unit still holding the stance and merely stopped is stopped to
   fight, not because it is there. A unit that has released the stance has
   genuinely finished its order, which is what makes it sound evidence that
   the crowd is at the destination. Contagion stays transitive (each newly
   settled ring seeds the next), so its reach is unchanged, and plain moves
   never set `AMove`, so their settling is bit-identical.
4. **The stall-cancel gives up the walk, not the fight**, unless it is within
   the crowd radius of the destination. Rim-lock means shoving achieves
   nothing; it does not mean the objective is abandoned. A jam nineteen cells
   short is traffic, and benching a unit there leaves it inert for the rest of
   the match while the crowd ahead moves on without it. Near the destination
   the two readings coincide and the heuristic keeps its original meaning, so
   it remains the genuine safety valve: a crowd pressed within 4 cells of an
   unreachable or garrisoned point settles unit by unit through it, and each
   settled unit is then a legitimate contagion seed under clause 3.

No `Entity` field is added, no `EntityKind` renumbered, no data value touched,
no new allocation per tick. The stance ends where it always ended: on arrival
with nothing left to fight, or on a `Stop`/replacement order.

Determinism review (adversarial, on the committed diff): the helper is a pure
existence test over the entity list with no tie-break; every mutable field the
fix reads or writes (`AMove`, `AMoveX/Y`, `TargetX/Y`, `Moving`, `StallTicks`)
is in `ComputeStateHash`; the one unhashed input is `Sight`, which is immutable
after spawn. **If sight upgrades ever ship, `Sight` must enter the hash or this
predicate desyncs invisibly** (guard comment left on the helper). The
`RevealTicks` mid-pass read order is inherited from the existing scan
unchanged.

## Alternatives rejected

**Fix the stall-cancel, as the journal proposed.** Rejected because it is not
the mechanism. Measured: it cancels zero units in the failing fixture. This is
the alternative the brief asked for, and the instrumentation is the reason it
is not the one being shipped.

**Accumulate the all-clear flag inside the auto-acquire scan** (the first cut
of this fix). Rejected on adversarial review: the scan is skipped entirely for
units holding an `ExplicitTarget`, so the flag under-reports exactly on the
breach-pursuit path this ADR cares about, and it charges every attack-mover
one extra squared distance per enemy per tick for a value that only matters at
the moment of completion.

**Add an `AMoveSettled` (or stance) field to `Entity`.** The honest modelling
fix: separate "stop moving" from "abandon the order" explicitly. Rejected for
now because it grows the state schema, the serialisation and the replay format
for a problem that `!o.AMove` already discriminates exactly, and because
ADR-005's "no Entity field is added" restraint is worth keeping until
something forces it. Revisit if a genuine hold/guard stance is ever specified.

**Shrink the completion radius from 4 cells to 1.** Would fix the rifle case by
letting the lead unit close enough to see the yard, and nothing else. It is a
magic number tuned to one sight value, it would re-break the moment a unit with
sight 4 existed, and it fights the crowd-settling the radius exists to provide.

**Require the ordered point to be reached exactly.** Fifteen squads cannot
occupy one cell; the crowd would grind against itself forever. This is
precisely what the 4-cell radius and the contagion were built to prevent.

## Consequences

**Golden hashes: 4 of 24 move.** This is the replay-compatibility break and the
reason this ADR exists. Measured in an isolated worktree pinned to 3583157 with
only this change applied, so the movement is attributable to it alone:

| scenario | from | to |
|---|---|---|
| skirmish | 0xDF844901214FD6C5 | 0xEC4FC36437301639 |
| aisuper | 0x81CE9819C0278CAA | 0xE86DAA751736FDC3 |
| mission | 0xE0CBE5C2BB60590E | 0x45910E8DA7751E7E |
| mission02 | 0x30A0832E24A23312 | 0x0A5E5EBE024042C7 |

The other 20 are byte-identical, including `attackmove`, `combat`, `artillery`,
`walls`, `capture`, `depot` and `pathing`. All four movers are AI-driven, which
is the expected blast radius: `SkirmishAI` is the main consumer of attack-move
(SkirmishAI.cs:259 and :341). `aisuper` and `mission02` move only under the
final (lazy) form of clause 1 plus clause 2, which is direct evidence the
review's ExplicitTarget hole fired in real scenarios and closing it was
substantive, not hygiene. **Do not regenerate until this ADR is ratified.**

**Full battery on the fixed sim, 16 of 16 exit 0:** replay reproduces a
3000-tick AI match bit-exactly; saveload and campaignsave resume hash-exact;
lan runs 20 of 20 two-client games with zero desyncs; lanchaos survives 60ms
jitter plus 5% stalls; spectate, bench, export and the six debug dumps all
clean. Determinism double-run identical on 18 of 18 scenarios.

**The AI can finish a base it has cracked open.** This is the point.

**The balance matrix changes in exactly five rows, all howitzer rows, no
winner flips.** The howitzer is the one unit whose field behaviour legitimately
changes: it pauses to fire at standoff range, and under the old contagion each
pause benched the units behind it. Headline: the howitzer-mirror row, the
shipped report's only UNRESOLVED, now resolves (howitzer wins at 30% survivor
value in 16.9s instead of a 200-second freeze). The others are second-decimal
timing shifts (cannon 12.5s to 12.3s, bulwark 24.1s to 24.3s, vanguard 9.2s to
9.9s) and rocket-vs-howitzer survivor value 80% to 90%.

**TICKET-P5-DEF-17's negative controls cannot be promoted, and the reason has
inverted.** The ticket wants "rifle and cannon MUST NOT breach". With
attack-move working they both now raze the fortified base (rifle t=2046 at 73%
retained, cannon t=1486 at 80%). Their previous "pass" was manufactured by the
artefact exactly as the journal suspected, but the conclusion is the opposite
of the one the journal hoped for: promoting these rows to hard-fails would now
make the gate **red**, because the balance claim itself is false. Massed small
arms genuinely do kill a fortified base at equal credits. This is journal item
2 ("two turrets lose to 3000 credits of rifle squads") arriving as a blocking
question rather than an observation. **Balance and Game Designer must decide
whether static defence is rebalanced or the ticket's expectation is
withdrawn.** The gate still exits 0 today because those rows are
reporting-only.

**The turtle gate's asserted row survives, and improves.** Artillery still
razes in every geometry, both turrets dead, 90% retained, 3 segments breached
on a sealed wall. The sealed-wall delay moves 235 to 229 ticks, still far
inside the 1200 bound. The howitzer razes the unwalled base at t=1248 rather
than t=3124, because it now advances instead of freezing at range: a 60%
faster siege from a change that touches no artillery number.

**One acceptance test breaks and needs a human decision (QA + Producer).** The
`mission` scenario now throws "winner declared while camp entities lived".
Diagnosed, not guessed: mission-01's objective is `trigger destroyed camp ->
win 0` over 3 structures **and 2 units**, but victory arrives from
`VictorySystem` eliminating player 1 the moment its last *structure* dies,
while a camp unit lives on 10 hp. The attackers now focus structures instead
of milling among the camp units, so the structures die first. The test's
invariant (winner implies all camp entities dead) was always a coincidence of
ordering, and the underlying issue is that mission-01 can be won without
completing its stated objective. That is a MissionRunner/VictorySystem
interaction this ADR deliberately does not fix; it is out of scope and is
reported rather than patched. **The gate is red until it is resolved, so this
ADR cannot land without that decision.** (`campaignsave`, which also runs
mission-01, reaches the scripted victory and passes; the assertion exists only
in the gate's ScenarioMission.)

**Not fixed here, and explicitly not this ADR's business.** The howitzer's
explicit-Attack death was re-measured and the recorded explanation is wrong:
it never enters its 3-cell dead zone. The closest it ever gets to the yard is
8.87 cells, just inside its range of 9. It dies because that standoff ring
sits on top of the turrets (x approximately 15.87 against turrets at 16 and
17) and `ExplicitTarget` lock stops it ever shooting back. That is a
target-lock question, not a MinRange one, and it deserves its own ticket.
