# ADR-008: Power gets teeth: the turret gate, honest draws and the radar blackout

- Status: Ratified (Architect authored 2026-07-17; ratified by Luke 2026-07-17 under the directive "design out and build all these", covering this ADR as drafted)
- Date: 2026-07-17
- Deciders: Architect agent + Luke (plus Balance + Game Designer A11 co-sign
  on the draw numbers, recorded below)
- GDD/TDD feature served: GDD s5 line 48, the entire power spec in one
  sentence; GDD s7 line 86 (the radar minimap); doc 00 line 24, "Power as a
  system", the pillar that names power plants as soft targets creating attack
  incentives

## Context

GDD line 48 asks for five things: the bar, linear production slowdown below
100 per cent, radar dark below 100 per cent, turrets offline below 75 per
cent, and the deliberate sell-power-to-sneak-a-superweapon play. The game
ships two of five. The bar is done and honest in integer maths
(Sidebar.cs:311, :323). The linear slowdown is done and proven
(World.cs:1804-1806, pinned at 75 ticks powered and 150 unpowered). The other
three clauses are missing, and one is inverted: World.cs:1780 pauses the
superweapon charge the instant supply drops below draw, so selling plants to
fund a superweapon stops the superweapon (referred to the Game Designer as
Q010, not silently redesigned here).

The critical gap is the turret. CombatSystem contained no reference to power
in any form, proven by execution: an unpowered turret and a powered turret
each dealt exactly 200 damage. Plant-sniping, the pillar doc 00 line 24 names,
buys the attacker a production slowdown and nothing else, and the sidebar
turns the bar red and pulses it at exactly the threshold where GDD s5 says the
guns die (Sidebar.cs:323) while the guns do not die. The client tells the
truth about the design and lies about the game.

Wave 2 (commit 147f03b) staged the machinery for this decision deliberately
and hash-neutrally, and it is all verified at HEAD. `ComputePower`
(World.cs:1071) is the one tally, called per SYSTEM rather than per Step,
which is load-bearing (World.cs:1062-1070): CombatSystem can destroy a plant
on the tick ProductionSystem then reads, and the shipped behaviour is that
production sees the post-combat total. `AtLeast75` (World.cs:1091) is the
divisionless boundary the bar already uses. A third tally now runs at the top
of CombatSystem (World.cs:1287-1289) and feeds nothing: the spans exist so
the turret gate lands as one guard line, and the comment at :1282-1286
already pins where in the tick it samples. The Radar Uplink is compiled
(struct type 12, World.cs:459: cost 900, build 150 ticks, hp 1000, draw 80,
sight 10) and authored (data/buildings/com_radar_uplink.yaml) and
deliberately unreachable: no sidebar button (Sidebar.cs:18-37 lists eight
entries, types 11 and 12 absent), no spawn method, no MapLoader arm
(MapLoader.cs:163-179 throws on it), and no IsStructure membership
(World.cs:972-975).

Meanwhile power costs nothing where it is actually played: the Construction
Yard draws 0 (com_construction_yard.yaml:8) and the refinery draws 0
(com_refinery.yaml:8), so the opening base draws 40 against a single
300-credit plant supplying 100. Power is a rounding error on the build order
at one end and consequence-free at the other. The model grew where it was
easy, in three full-power cliffs the GDD never asked for (Q010), and stalled
in the two clauses it did.

## Decision

One ADR, one wave, one regeneration. The three parts below are not
independently valuable and must not be split: a turret gate against a
trivially satisfied total does nothing, and expensive power that gates
nothing is a tax.

1. **Turrets go offline below 75 per cent.** The guard is inserted
   immediately after the real CombatSystem guard, `if (!e.Alive ||
   e.WeaponId == 0) continue;` at World.cs:1301, and ABOVE the cooldown
   decrement at :1302, deliberately: a dead turret does not reload.

   ```
   if (e.Kind == EntityKind.Turret && !AtLeast75(combatSupply[e.PlayerId], combatDraw[e.PlayerId]))
   { _entities[i] = e; continue; }
   ```

   Turret kind only: the GDD says defensive turrets, and doc 22's emplacement
   and bastion join the guard by kind when they land. The boundary is
   inclusive and asserted: supply 15 against draw 20 is exactly 75 per cent
   and FIRES; supply 14 does not. Divisionless AtLeast75, not a truncating
   percentage.

2. **The snapshot semantic is pinned: the turret gate reads PRE-combat
   power.** Both semantics are deterministic and they hash differently, so
   the choice is format-significant and is made here rather than inherited.
   Pre-combat wins for three reasons. First, it is what the staged code
   already commits to in writing: the CombatSystem tally runs before damage
   is applied (World.cs:1282-1286), so a plant destroyed on tick N disables
   its turrets on tick N+1. Second, the one-tick lag is more readable than a
   turret dying on the same tick as its plant. Third, ProductionSystem keeps
   its own post-combat tally (World.cs:1727-1731) under the per-system rule
   (World.cs:1062-1070); the two systems sampling different instants is
   shipped, priced reality, not an accident.

3. **Honest draws (BD-07, rebased; A11 co-sign REQUIRED).**
   com_construction_yard 0 to 20, com_refinery 0 to 40, dir_superweapon 100
   to 150. Unchanged: factory 40, turret 20, depot 30, veil projector 60,
   barracks 20, radar 80, wall 0, plant supply 100 at 300 credits. The
   resulting curve: the opening base draws 100 against a 100-supply plant, so
   the first plant is mandatory; two refineries plus four turrets is 220,
   three plants; adding the superweapon reaches 370, four plants. A 300-credit
   line item becomes a roughly 1200-credit commitment, which is what gives
   clause 1 teeth. Stated honestly: the opening base is then EXACTLY 100
   supply against 100 draw, sitting on the inclusive boundary of the three
   full-power cliffs (Q010) and of AtLeast75 simultaneously; any further draw
   tips a fresh base into brown-out before its second plant finishes. That is
   a zero-margin design and it must be explicitly accepted or explicitly
   re-tuned by Balance and the Game Designer, not discovered in a play-test.
   Both changed draws are far past CLAUDE.md's 15 per cent line, so this
   clause does not merge without the Balance + Game Designer co-sign.
   `DefaultStructureType` updates in lockstep with the YAML: required, not
   forbidden, because the compiled table is the round-trip reference the
   selftest asserts the files reproduce. The stale notes go with it:
   com_refinery.yaml and com_construction_yard.yaml document the zero draws
   as deliberate, and dir_turret.yaml:16's claim that the draw "buys nothing
   back today" stops being true the day clause 1 lands.
   The AI survives these numbers, traced by hand rather than assumed: its
   ladder (SkirmishAI.cs:94-101) hits supply 100 against draw 100, the
   `supply < draw + 40` rung at :97 fires, a second plant is queued, and it
   settles at 400 against 270. It does not brown-out lock.

4. **The radar blackout, tied to the Radar Uplink.** The building becomes
   reachable: SpawnRadarUplink, the PlaceStructure arm, the MapLoader arm,
   the sidebar button, and `EntityKind.RadarUplink` added to IsStructure
   (World.cs:972-975). That last one is the silent killer: omit it and sell,
   repair, capture, rubble-unblock, placement adjacency and the victory test
   all break with no compile error. The blackout itself is client-side, at
   the minimap refresh (SkirmishLive.cs:1222, which has no guard today): the
   minimap blanks to RADAR OFFLINE unless a living Radar Uplink stands and
   supply covers draw, GDD line 48's below-100 clause. Minimap pings still
   render while blanked, deliberately: blanking the base-under-attack ping
   would make the blackout a stealth nerf to the alert system. Two decisions
   are recorded rather than smuggled: the blackout binds only the human,
   because SkirmishAI reads `w.Entities` with no visibility filter,
   consistent with the AI already ignoring fog entirely; and the day-one
   opening experience (every shipped map and mission starts radarless) is
   Q008's call, which blocks the implementing ticket, not this ADR. The AI
   ladder gains the radar before the superweapon with a hasRadar flag;
   without it the AI queues a superweapon it can never build once ADR-009's
   prerequisites land, and stalls forever.

5. **The walls-gate amendment lands in the same change as the gate.** This is
   the trap this ADR exists to put in writing. The `walls` battery's PHASE G
   (Program.cs:1408-1441) spawns five wall segments and one turret for
   player 1 (:1415-1416) and no plant at all: supply 0 against draw 20 is 0
   per cent, so clause 1 kills that turret for the whole phase. The assertion
   at :1440-1441, that the howitzer takes nothing back, then passes because
   the turret is dead, not because it is out-ranged, and the report string at
   :1485 goes on printing a sentence that is no longer true. The comment at
   :1408-1411 calls this "the assertion that keeps turtling beatable" (GDD s6
   line 53). And the golden hash will NOT move: the turret is 10.5 cells from
   the gun, never acquires, never fires, its Cooldown stays 0, so the early
   continue produces a bit-identical entity. The golden check cannot catch
   this; only a human reading PHASE G can. The amendment, in the same commit
   as the gate: give player 1 a plant in worldG so the howitzer result is
   re-proven against a POWERED turret, and add a power assertion in the same
   battery proving a turret without power is offline BECAUSE of power: an
   in-range unpowered turret that visibly withholds fire, alongside the
   15-against-20 boundary assertion from clause 1. The other scenario surgery
   is enumerated, not forecast: ScenarioVeil (Program.cs:955) gives player 1
   a turret while the only plant belongs to player 0 (:956), so the baseline
   assertion at :963 throws, the good failure; fix with a plant for player 1.
   ScenarioStealth (Program.cs:575) runs its turret for 100 ticks before the
   plant arrives at :586, so its "undetected raider was shot" assertion keeps
   PASSING for the wrong reason; fix with a plant above :575, keeping :586
   for Rule 2's geometry.

6. **ScenarioSuperweapon's boundary test survives unloosened.** Under the new
   draws the scenario's draw is 170 (superweapon 150 plus the yard at :814
   now drawing 20), not 150, and selling plant1 (:833) leaves 100 against
   170: the charge pauses and the assertion at :840 throws. The equality
   cannot be restored by recomputing constants, because plants supply 100
   each and supply can never equal 170. The escape is the nullable supply
   override at World.cs:567: one plant spawned with `supply: 70` lands the
   post-sale total at exactly 170 and the boundary test survives intact. No
   assertion is deleted or loosened anywhere in this wave; a reviewer diffs
   Program.cs to confirm exactly that.

7. **Enumerate the blast radius; two scenarios must not move.**
   ScenarioProduction (Program.cs:195: a plant and a factory, no yard, no
   refinery) and ScenarioDepot (Program.cs:1214) are provably immune to the
   draw changes; their golden rows must be byte-identical after this wave and
   a diff that moves them is a defect. Every SpawnTurret site is re-read by a
   human with the owner's supply against draw recorded per tick range, hash
   movement or not: Program.cs:575, :955, :1416, :1766 (the bench rig
   alternates turrets and plants per player, probably fine, verify rather
   than assume), :2220 (a debug entry point, not a golden), and
   MapLoader.cs:171 (a live path; the shipped .fmap files carry no
   structures today). Diffing goldens is NOT sufficient acceptance for this
   wave; clause 5 is the proof of why.

## Amendment note to ADR-005 (numbering and loop bounds)

ADR-005 is ratified and is not edited; the amendment is recorded here.

- The struct numbering reservation extends: 9 wall (shipped), 10 gate
  (RESERVED, untouched), 11 barracks and 12 radar uplink (taken by Wave 2,
  compiled at World.cs:455 and :459), then 13 airfield, 14 emplacement,
  15 bastion, 16 outpost (reserved, uncompiled). **Struct type 12 does not
  collide with the reserved 10**, confirmed at HEAD: `GateStructType = 10`
  (World.cs:499) still has no def and no file, FootprintOf still
  special-cases it (World.cs:504), and both enumeration loops skip it
  explicitly (the seeding loop at World.cs:477-479 and the selftest
  authorship loop, both bounded by `World.MaxStructType = 12` at
  World.cs:471). The loop bounds and the gate-skip are PART of the
  reservation: raising a bound without the skip fails the gate, and adding a
  type without raising the bound ships an unbuildable building with no error.
- ADR-005's consequences paragraph counts "all 23 existing golden hashes"
  (ADR-005 line 85). Stale: sim/golden-hashes.txt holds 24 (the depot and
  walls rows landed after ratification). The correct count at HEAD is 24 and
  this note is the correction of record.

## Alternatives rejected

**Post-combat power for the turret gate (ProductionSystem's semantics).**
Rejected. It requires either a second tally after the damage pass, a fourth
O(n) sweep bought so that a turret can die less readably on the same tick as
its plant, or hoisting one shared tally into Step, which is the one thing the
Wave 2 refactor loudly forbids because it moves every golden for zero
behavioural gain. The staged tally already samples pre-combat and says so in
writing; choosing post-combat now would mean restructuring shipped, pinned
code to buy a worse-reading result.

**Gate every armed structure, not just turrets.** The GDD says defensive
turrets. The superweapon already has its own charge gate, and widening the
clause by analogy is inventing design that the emplacement and bastion
tickets can decide with their own numbers in hand.

**Ship the turret gate without the draws, or the draws without the gate.**
Each alone is inert or punitive; together they are the feature. Splitting
them also splits one replay-compatibility break into two.

**Blind the AI too while landing the blackout.** The AI has no fog reading to
gate; inventing a visibility filter for it is a real project (and an AI
fairness decision), not a rider on a building ticket. The one-sided blackout
is recorded as the shipped stance in clause 4 and can be revisited on its own
merits.

## Consequences

Easier: plant-sniping becomes the pillar doc 00 promises; the power bar stops
lying (PWR-D4 closes structurally, since sim and bar share AtLeast75); the
radar minimap finally has a building behind it; Q010's sell-to-sneak question
becomes answerable against a power model that matters.

Harder: the client gains blackout state and a RADAR OFFLINE surface; the
four .fmap files and three mission scripts inherit Q008's decision; the
runner needs surgery in five scenarios; and the opening base sits at zero
power margin until Balance rules on it.

Committed to: the pre-combat semantic; the inclusive 75 per cent boundary
with its assertion; the walls PHASE G amendment in the same commit as the
gate; the human re-read of every SpawnTurret site as acceptance; the A11
co-sign before the draws merge; Q008 decided before the blackout ships.

Hash impact: MOVES golden hashes. One regeneration for the whole of Wave 5
(PWR-03 + PWR-04 + PWR-05) per doc 23 section 6, on both platforms. Expect
stealth and veil movement at minimum; production and depot must NOT move;
walls will not move despite being the scenario most affected, which is
exactly why its amendment is bound into the same change. Every moved row
explained with the tick at which the brown-out occurs; an unexplained hash
change is a defect, not a rebase. Changing a golden hash requires an ADR plus
Architect sign-off (CLAUDE.md); this ADR is that sign-off once ratified.

Gates: TICKET-P5-PWR-03, PWR-04 and PWR-05 (doc 23 Wave 5). PWR-04 depends on
PWR-02, which landed in Wave 2 (commit 147f03b), and on PWR-03. Ratification
unblocks the wave; none of the three tickets may start before it, and PWR-05
additionally waits on Q008.

## Implementation notes (dated, Wave B3, 2026-07-18)

Implemented as ratified in P6 Wave B3 (branch ticket/p6-wave-b3; delivery
notes in docs/tickets/P6-wave-b3-power-gets-teeth.md). Two findings of
record, neither a deviation from the decision itself:

1. **Clause 7's parenthetical on MapLoader.cs is half wrong, and the
   mandated human re-read is what caught it.** "The shipped .fmap files
   carry no structures today" is true of the four skirmish maps and FALSE
   of the three mission files. mission-01's camp turret is powered (100
   supply against 60 draw) and mission-03's turrets sit at 83 per cent
   (100 against 120 under the new refinery draw, above the gate, below
   full production). mission-02's camp turret drew 20 against a supply of
   ZERO: clause 1 would have silenced the compound's only gun for the
   whole mission, hash-invisibly - the walls-trap shape again, exactly why
   this ADR made the re-read the acceptance criterion rather than the
   golden diff. mission-02.fmap gained a plant for the camp in the same
   change as the gate.

2. **Q008 sequencing under the ratifying directive.** This ADR gates the
   blackout on Q008; the P6 campaign tracker's authority (Luke's directive
   of 2026-07-17, which also ratified this ADR) lists the radar blackout
   in Wave B3's scope, and the wave instruction of 2026-07-17 assigns
   ALERT-02's "radar goes dark" clause to the wave explicitly. The
   blackout therefore shipped with the day-one rule as clause 4 wrote it
   (every map and mission opens radarless); the CURATION half of Q008 -
   whether missions grant or script radar coverage - remains open with the
   Game Designer inside its decide-by date, recorded on Q008 itself.
