# Balance report - 2026-07-15 (counter triangle)
Army value 3000 credits/side, seeds [11, 22, 33], cap 3000 ticks.

| Matchup | Winner (3 seeds) | Avg survivor value | Avg resolution |
|---|---|---|---|
| cannon_tank vs cannon_tank | cannon_tank/cannon_tank/cannon_tank | 60% | 16.7s |
| cannon_tank vs rifle_squad | cannon_tank/cannon_tank/cannon_tank | 80% | 35.1s |
| cannon_tank vs rocket_squad | rocket_squad/rocket_squad/rocket_squad | 90% | 11.9s |
| cannon_tank vs howitzer | cannon_tank/cannon_tank/cannon_tank | 100% | 12.5s |
| cannon_tank vs phantom_tank | cannon_tank/cannon_tank/cannon_tank | 100% | 10.6s |
| cannon_tank vs bulwark_tank | cannon_tank/cannon_tank/cannon_tank | 80% | 10.5s |
| cannon_tank vs vanguard_car | cannon_tank/cannon_tank/cannon_tank | 100% | 12.2s |
| rifle_squad vs rifle_squad | rifle_squad/rifle_squad/rifle_squad | 53% | 12.6s |
| rifle_squad vs rocket_squad | rifle_squad/rifle_squad/rifle_squad | 80% | 11.0s |
| rifle_squad vs howitzer | rifle_squad/rifle_squad/rifle_squad | 100% | 11.5s |
| rifle_squad vs phantom_tank | rifle_squad/rifle_squad/rifle_squad | 100% | 12.6s |
| rifle_squad vs bulwark_tank | rifle_squad/rifle_squad/rifle_squad | 80% | 23.5s |
| rifle_squad vs vanguard_car | vanguard_car/vanguard_car/vanguard_car | 60% | 10.8s |
| rocket_squad vs rocket_squad | rocket_squad/rocket_squad/rocket_squad | 40% | 14.0s |
| rocket_squad vs howitzer | rocket_squad/rocket_squad/rocket_squad | 80% | 9.1s |
| rocket_squad vs phantom_tank | rocket_squad/rocket_squad/rocket_squad | 100% | 8.3s |
| rocket_squad vs bulwark_tank | rocket_squad/rocket_squad/rocket_squad | 100% | 7.7s |
| rocket_squad vs vanguard_car | rocket_squad/rocket_squad/rocket_squad | 40% | 8.6s |
| howitzer vs howitzer | UNRESOLVED/UNRESOLVED/UNRESOLVED | 0% | 200.0s |
| howitzer vs phantom_tank | phantom_tank/phantom_tank/phantom_tank | 90% | 12.6s |
| howitzer vs bulwark_tank | bulwark_tank/bulwark_tank/bulwark_tank | 53% | 24.1s |
| howitzer vs vanguard_car | vanguard_car/vanguard_car/vanguard_car | 90% | 9.2s |
| phantom_tank vs phantom_tank | phantom_tank/phantom_tank/phantom_tank | 30% | 13.9s |
| phantom_tank vs bulwark_tank | phantom_tank/phantom_tank/phantom_tank | 60% | 10.9s |
| phantom_tank vs vanguard_car | vanguard_car/vanguard_car/vanguard_car | 75% | 10.8s |
| bulwark_tank vs bulwark_tank | bulwark_tank/bulwark_tank/bulwark_tank | 0% | 18.0s |
| bulwark_tank vs vanguard_car | vanguard_car/vanguard_car/vanguard_car | 45% | 20.5s |
| vanguard_car vs vanguard_car | vanguard_car/vanguard_car/vanguard_car | 45% | 12.2s |

## Static defence: does a walled base fall to a siege army?

Fortification: 1 Construction Yard, 1 power plant (supply 200 >= draw 140), 2 turrets, 12 wall segments ("none" is the unwalled control). Besieger: 3000 credits of one type, attack-move, cap 6000 ticks.

| Besieger | Wall | Segments lost | Turrets killed | Yard razed | Army retained | Verdict |
|---|---|---|---|---|---|---|
| howitzer | none | 0 | 2/2 | t=3124 | 90% | BREACHED |
| howitzer | gapped | 0 | 2/2 | t=3124 | 90% | BREACHED |
| howitzer | sealed | 3 | 2/2 | t=3359 | 90% | BREACHED |
| rifle_squad | none | 0 | 2/2 | no | 73% | held |
| rifle_squad | gapped | 0 | 2/2 | no | 86% | held |
| rifle_squad | sealed | 8 | 2/2 | no | 73% | held |
| cannon_tank | none | 0 | 2/2 | no | 80% | held |
| cannon_tank | gapped | 0 | 2/2 | no | 40% | held |
| cannon_tank | sealed | 5 | 2/2 | no | 60% | held |

| Artillery siege | Unwalled control | Gapped wall | Sealed wall |
|---|---|---|---|
| Yard razed at tick | 3124 | 3124 | 3359 |
| Ticks bought by the wall | - | 0 | 235 |

FINDING, not a caveat: against artillery a gapped wall - the shape ADR-005 clause 6
actually intends - is worth ZERO. The yard falls on the same tick with it and without
it, because the howitzer's range 9 beats the turret's range 5, so it parks outside the
wall and shells over the top and never has a reason to touch masonry. A sealed wall
buys 235 ticks, 7% of the siege. "Artillery beats static defence" holds, but it holds
independently of barriers, so this section polices the turret's range and the wall's
hit points, and cannot police much else. See docs/balance/2026-07-15-turtle-gate.md.

Rifle and cannon rows are REPORTING ONLY, and the reason is a finding rather than a
hedge: neither the ticket's "zero segments destroyed" nor a yard-razed test measures
what it appears to. Segments lost is set by geometry, not by warhead - nothing at all
shoots a gapped wall, and against a sealed one massed rifles out-chew the howitzer
(8 segments to 3). Yard-razed inverts under a different order: told to Attack the yard
directly, rifles raze it at t=767 losing nothing while the howitzer walks into its own
3-cell dead zone and dies to the last unit. Under attack-move the negative controls
hold only because the stall-cancel at World.cs:1488 drops AMove for good and parks them
short of the yard. Gating on that would gate an artefact. These rows are recorded every
run so the trend is visible; promoting them to blocking is gated on attack-move
prosecuting a base to the finish, per the faction-war precedent below.

## Cost efficiency: hit points per credit

| Thing | Hit points | Cost | Hp per credit |
|---|---|---|---|
| wall segment | 500 | 100 | 5.00 |
| turret | 400 | 600 | 0.66 |
| cannon tank | 300 | 600 | 0.50 |

## Harvester tempo (standard opening, 3000 ticks): 8400 credits

## VERDICT: PASS - matchups match design intent within bounds.

## Faction war: Directorate vs Sodality (6 seeds, 7000 ticks)

| seed | result | decided at |
|---|---|---|
| 3001 | Sodality | adjudicated |
| 3002 | Sodality | adjudicated |
| 3003 | Sodality | adjudicated |
| 3004 | Sodality | adjudicated |
| 3005 | Sodality | adjudicated |
| 3006 | Sodality | adjudicated |

Directorate 0 - 6 Sodality
## Journal: TICKET-P5-DEF-17, the turtle gate

Design intent: turn GDD s6 line 53 ("artillery beats static defence") and
ADR-005's stated mitigation for the turtling risk into a gate that runs on
every push, so "walls must not make turtling unbeatable" stops being a
paragraph nobody can check.

The gate as shipped asserts three things and reports the rest. What follows
is the working, one dial at a time, including the two dials that had to be
abandoned because the measurement did not support them.

1. First cut, exactly as the ticket specifies: 64x64 map, a 12-segment wall
   line "in front of" the turrets, three attackers at 3000 credits each,
   verdicts on segments-destroyed. Result: segments destroyed is 0 for every
   attacker, in every seed. Nothing shoots a wall on an open map. Diagnosis,
   by reading rather than guessing: auto-acquisition skips barriers (ADR-005
   clause 2, World.cs:1131), so the only paths to a dead segment are an
   explicit Attack order or the DEF-05 breach, and breach needs the route to
   be severed. A 12-segment line on a 64-wide map severs nothing: everyone
   walks around it. The howitzer clause ("at least one wall segment
   destroyed") is unsatisfiable here.

2. Splash was the obvious escape - the howitzer alone carries splash 1.5, so
   perhaps shells landing on a turret would take the wall with them and give
   artillery a wall-kill nothing else can match. It cannot, and this is
   arithmetic rather than opinion: a turret centre sits at anchor+1, a wall
   centre at anchor+0.5, so the nearest a legal wall centre can get to a 2x2
   turret centre is distSq 2.5, against a splash radius squared of 2.25. A
   shell on a turret NEVER touches a wall flush against it. Proved by sweeping
   every legal cell in the neighbourhood before relying on it.

3. Sealed the map into a 64x12 corridor so the wall genuinely seals, which is
   the only geometry where breach fires. Now the howitzer breaches: 3 segments,
   both turrets, 90% retained. But the rifle clause inverts hard - 15 rifle
   squads destroy EIGHT segments, more than the howitzer's three. That is not
   a surprise once measured: breach is unit-agnostic, so segments-destroyed
   measures aggregate dps against Structure, and 15 squads x 5.6 dps out-chew
   3 howitzers. The ticket's own arithmetic already said so and the conflict
   went unnoticed: 89 seconds per segment against a 200-second cap means one
   squad breaches with 111 seconds to spare.
   DIAL ABANDONED: segments-destroyed cannot separate artillery from small
   arms, in either geometry. Unsealed it is 0 for everyone; sealed it favours
   the rifles. It is a function of geometry, not of warhead.

4. Switched the discriminator to the thing the design claim is actually about
   - does the base fall. Clean separation on the first run: the howitzer razes
   the Construction Yard in every geometry retaining 90%, while rifles and
   cannons leave it at 3000/3000 forever, even at 9000 ticks. The mechanism is
   principled (AntiBuilding is 100% against Structure, AntiInfantry 25%,
   AntiArmour 50%), so this looked like the answer.

5. CONTROL RUN, and it destroyed step 4. Ordered the same armies to Attack the
   yard directly instead of attack-moving at it: rifles raze it at t=767
   losing NOTHING, cannons at t=831 losing nothing, and the howitzer fails
   outright and dies to the last unit, because explicit-Attack pursuit walks it
   inside its own 3-cell dead zone where it stands helpless under turret fire.
   Total inversion. So rifles can raze the yard perfectly well; under
   attack-move they simply stop trying, because the stall-cancel at
   World.cs:1488 drops AMove permanently when they jam up, parking them ~7
   cells short at full strength.
   DIAL ABANDONED: yard-razed is order-dependent, not a balance law. Gating on
   it would have gated the pathing artefact, and the gate would have gone red
   the day attack-move was improved.

6. THE CONTROL THAT SHOULD HAVE BEEN RUN FIRST: the same fortification with no
   wall at all. Artillery razes the unwalled base at t=3124. It razes the
   GAPPED walled base at t=3124. Identical. The wall is worth exactly zero
   ticks against artillery, because range 9 beats the turret's range 5, so the
   howitzer parks outside and shells over the top and never has a reason to
   approach. A sealed wall buys 235 ticks, 7% of the siege.
   This is the finding that matters, and it re-scoped the gate: a gate written
   only on "artillery razes the walled base" would pass unchanged if barriers
   were deleted from the game entirely. It does not measure walls. It cannot.

7. Final shape. Assert the wall's own contribution, differenced against the
   unwalled control so map size, army speed and turret dps cancel out and what
   is left is the wall and nothing else: a sealed wall may buy the turtle at
   most 1200 ticks. Both ends measured, not guessed - the shipped 500hp wall
   buys 235, a wall regressed to 5000hp buys 1936, and 1200 is the round number
   between them. Plus the ADR's literal claim (artillery razes the walled base,
   both turrets dead, >30% army retained, and on a sealed wall at least one
   segment breached so the DEF-05 path stays alive), plus the hp-per-credit
   bound from clause 3.

Gate bites, proved in both directions rather than trusted:
- wall hp 500 -> 5000 in a scratch build: sealed delay 235 -> 1936, over the
  1200 bound, tool exits 1. NOTE: this fires on the DELAY bound, not on the
  raze itself - at 10x hit points artillery still gets through at t=5060. A
  gate written on raze-within-cap alone would NOT have bitten, which is how
  the delay bound came to exist.
- wall cost 100 -> 10: 50.00 hp per credit against the 8.0 bound, tool exits 1.
- Both regressions reverted; git diff --stat sim/ is empty and all 24 golden
  hashes are byte-identical.
- Two consecutive runs are byte-identical apart from the date line.

Deviations from the ticket, all needing Balance + Game Designer co-sign
(CLAUDE.md A11), none of them silent:
- expectedBreachers is NOT the specified three-verdict hard-fail table. Only
  the howitzer is asserted. The rifle and cannon rows are reporting-only, on
  the faction-war precedent already in this tool, because their apparent
  "MUST NOT breach" pass is manufactured by the attack-move stall-cancel
  (step 5). Promoting them to blocking is gated on attack-move prosecuting a
  base to the finish.
- The wall-segment terms of the rifle and cannon verdicts are dropped as
  unmeasurable (steps 1-3).
- SiegeTicks is 6000, not the matrix's 3000. The shipped siege completes at
  3124-3359, so a 3000 cap fails the BASELINE. The cap is not doing the
  discriminating work: the negative controls do not flip at 9000 either.
- The seeds {11, 22, 33} are kept per spec, but this fixture draws no random
  numbers, so they agree by construction. Cross-seed agreement is asserted
  anyway - it costs nothing and trips if RNG ever leaks into the siege path.
- Turret hp-per-credit prints 0.66, not the ticket's 0.67: integer maths
  (400*100/600 truncates), kept deliberately over floating point.

NEEDS A HUMAN, and these are design questions, not tuning ones:
1. A gapped wall does nothing whatsoever against artillery. Is a barrier that
   only ever delays the attacker who was already going to lose (7% against a
   sealed wall, 0% against a gapped one) the feature that was wanted? The
   turtling risk ADR-005 mitigates does not appear to exist for artillery, so
   the mitigation is measuring something that is not threatening it.
2. Two turrets (1200 credits) lose to 3000 credits of rifle squads while
   killing 2 of 15 squads. Static defence is weak against everything at equal
   credits, not just against artillery. The turret, not the wall, is the number
   that looks wrong - the wall's hit points are not what makes turtling work or
   fail.
3. Attack-move does not prosecute a base to the finish. It kills what it bumps
   into and goes permanently inert (World.cs:1488), leaving an intact
   Construction Yard 7 cells away and a full-strength army standing still. The
   2026-07-14 journal's claim that "attack-move hunts to the finish" holds for
   field battles and not for bases. This affects the AI's waves, and DEF-14's
   Turtle-versus-Standard acceptance test leans on it.
