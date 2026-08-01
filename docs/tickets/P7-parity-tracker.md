# P7 parity tracker: close the gap to the benchmark games

Authority: Luke, 2026-07-30, "update that documentation and set that as a goal",
against the rewritten parity analysis in docs/design/24-classic-parity-roadmap.md.
That document is the ANALYSIS; this file is the PLAN, and it is the resume point
if a session dies.

**This phase is not like P6.** Every P6 wave could be made hash-neutral or
carried a single sanctioned regeneration, so waves could be picked up in any
order. Nothing in P7 is hash-neutral: every row below is a sim or catalogue
change that moves goldens. That is the argument for sequencing rather than
picking items off, and it is why the ordering column exists.

Standing law per wave, unchanged from P6: full battery exit 0, goldens measured
and not assumed, an ADR where a hash moves, a gate that proves the behaviour,
tracker updated, PR with green CI on both platforms.

## Ordering principle

Rows are ordered by PLAY IMPACT per unit of work, not by list position in doc 24.
A defect that makes one faction unable to defend outranks a feature nobody has
asked for. Whole missing systems outrank roster breadth, because a missing
system removes a category of decision while a missing unit removes an option.

| # | Row | Doc 24 | Blocked on | Hash | Status |
|---|-----|--------|-----------|------|--------|
| P7-1 | A building's faction comes from /data instead of a hardcoded name (the reported "Sodality cannot defend" was a WRONG premise: nothing enforced the field) | B1 | - | **NEUTRAL, measured** | **DONE** - factiongate; checksum and 24 goldens unmoved |
| P7-2 | Defensive variety: the Emplacement, the anti-infantry leg, so defence is a CHOICE rather than a ladder | B1 | - | goldens NEUTRAL; catalogue checksum MOVES (a new building changes it by construction) | **DONE** - emplacementgate; 32 ticks vs infantry against the turret's 91, 632 vs armour against its 143 |
| P7-2b | A distinctive defence per SIDE: the Directorate's Bastion and the Sodality's Shroud Nest, both from WRITTEN GDD s3 doctrine | B1/C | - (no new doctrine invented, so Q017 is untouched) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - factiondefencegate (4 stages incl. the cloak and its decloak-on-firing) |
| P7-3 | Transports: the Carrier, the first unit that exists to move OTHER units | A2 | - | goldens NEUTRAL; catalogue checksum MOVES; save format v9 | **DONE** - transportgate (6 stages incl. save round-trip and cargo dying with its carrier) |
| P7-4 | The air layer: Airfield, Strike Flyer, Flak Track | A1 | ADR-028 (ratified under the standing directive) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - airgate (5 stages, both halves of clause 4); no reload cycle and the AI does not fly, both stated in the ADR |
| P7-5 | Faction identity: DR-02/03/04 as one package rather than three tickets | C | **Q017 (Luke's roster call)** | MOVES | pending - **first open row, and it needs a human** |
| P7-6 | Storage and a credit ceiling (silo) | B2 | **PRODUCER: GDD-SILENT.** GDD s4 specifies the economy in full and never mentions storage, a cap or overflow, and a ceiling would change the "float at 2 refineries / 3 harvesters" intent it DOES specify. Same category as crates and the map editor | MOVES | **NOT TAKEN** - I put this row on the list treating it as mine; it is not |
| P7-7 | Infiltration: the Sodality's Infiltrator, from GDD s7's named roster | B5 | - (the unit is written; only the 20 per cent share is my call) | goldens NEUTRAL; catalogue checksum MOVES | **DONE** - infiltratorgate (4 stages incl. conservation and an engineer regression check) |
| P7-8 | ~~More than two player seats~~ - **SPLIT, 2026-08-01.** GDD s9 makes TWO promises of very different sizes and one row cannot hold both | D2 | - | - | **SPLIT**, see "What P7-8 turned out to be" |
| P7-8a | The engine becomes N-player, free-for-all: GDD s9's "skirmish vs AI, 1-7 opponents" | D2 | - (written unhedged as a mode spec, unlike the "(sample)" roster lines - so it is a promise to keep, not a design to invent) | goldens NEUTRAL, measured, and asserted IN the gate rather than left to the golden file | **DONE** - ADR-031; multiseatgate (7 stages); client harness 128 -> 130 checks |
| P7-8b | Maps that can HOST more than two: the mapgen symmetry group, and the first multi-start map | D2 | - | no hash; all 10 committed maps re-generate BYTE-IDENTICAL, which is the inertness proof | **DONE** - `mirror2` orbit group; skirmish-09 "Kilnmoor Quarters" 160x120, four starts, 9.15% density; multiseatgate stage 7 |
| P7-8c | Teams and alliances: GDD s9's "custom lobbies up to 4v4" | D2 | **PRODUCER.** Not a sub-task of P7-8 but a second project of comparable size | MOVES | **NOT TAKEN** - there is no team field, no alliance table and no `AreAllied` predicate anywhere in the sim; hostility is decided everywhere by "not me and not neutral". It touches targeting, splash friendly-fire, detection and fog sharing, victory and the AI, and it has NO code to build on |
| P7-9 | Campaign missions 4 to 6 | D1 | ~~Q012/Q016~~ - both ANSWERED and closed under the standing directive | goldens NEUTRAL and checksum UNMOVED, measured (unusual for P7: `MissionRunner` state has always been outside the world hash) | **DONE** - ADR-029; campaigngate (5 stages) |
| P7-9a | Bring missions 01 to 03 under the generator, and onto self-declared setup, retiring `switch (setup.MissionIndex)` in SkirmishLive.cs | D1 | - | MOVES (two goldens: entity spawn order changes) | pending - **created by P7-9, not inherited.** Missions 04 to 06 declare their own yard and credits in the fmap; 01 and 03 still get theirs from a per-mission case in C#. Two mechanisms is the duplication this phase keeps finding, and the only reason it was left is that fixing it moves goldens for no behavioural gain |
| P7-10 | Wall tiers and gates | B7 | C6b: Luke must override ADR-005 clause 6 | MOVES | pending |
| P7-11 | ~~Hero unit, mines, support infantry~~ - **SPLIT, 2026-08-01.** One row bundling one thing that is written, one the project has already ruled is a sample, and one that appears in no design document at all | B3/B4/B6 | - | - | **SPLIT** - see "What P7-11 turned out to be" below |
| P7-11a | The Sodality's Saboteur: temporarily disables a building | B3 | - (GDD s7 names the unit AND its effect; only the duration is my call) | goldens NEUTRAL, measured; catalogue checksum MOVES; save format v10 | **DONE** - ADR-030; saboteurgate (6 stages) |
| P7-7a | **Client defect carried by P7-7:** the Infiltrator's theft raises `GameEventType.Captured`, which `SkirmishLive.cs:1715` consumes as an ownership CHANGE - it fires the "you lost it" alert and re-caches the owner from `ev.B`. So robbing a building announces to the victim that they have lost a building they still own | - | - | client only, no hash | pending - **found while building P7-11a, and it is mine.** Not fixed in passing because the right fix is a distinct event AND a robbery alert in the client, which needs the Godot harness. The Saboteur deliberately does NOT repeat it: it raises `Sabotaged` rather than reusing `Captured` |
| P7-11b | Hero unit (Commando / Shadow Commando) | B4 | **PRODUCER.** Named in GDD s7, but with no ability, no stats, and a "one at a time" qualifier that has no machinery anywhere in the sim. Doc 23 s142 has ALREADY ruled on this line: "The GDD mandates the Repair Vehicle exactly as much as it mandates the Commando, which is to say it is a sample, not a system statement" | MOVES | **NOT TAKEN** |
| P7-11c | Mines and a minelayer | B6 | **PRODUCER.** The word does not appear in the GDD, in any ADR, or anywhere in `sim/`. B6 is a one-line entry in doc 24 sitting on top of an entire new mechanic | MOVES | **NOT TAKEN** |

Out of scope until a GDD amendment with Producer sign-off: naval, FMV briefings,
crates, a map editor. Recorded in doc 24 so the comparison stays honest.

## What P7-1 turned out to be

It was filed as a defect - the Sodality unable to build base defence - and the
premise was wrong. Nothing enforced `faction:` on a building at all: the field
was parsed, validated, and dropped, and the sim hardcoded one expression naming
the Veil. Both sides could always build the turret.

The real defect was better and is now fixed: authored data that did not drive
the runtime, the ADR-006 class. StructureTypeDef carries a Faction, the loader
passes what the file declares, the hardcoded predicate is gone, and the turret
and superweapon declare `common` - preserving what play always did rather than
silently taking a capability away. Neutral: catalogue checksum and all 24
goldens unmoved, because no golden scenario plays a Sodality commander building
a Directorate building.

Recorded because the lesson generalises: **a claim read off a data file is a
claim about the file, not about the game.** The duplicated-rule audit had
already filed the missing Faction column as the permanent fix; checking there
first would have caught the premise before it was written down.

Left undone deliberately: `dir_turret` and `dir_superweapon` keep their ids
although they are now `common`, which contradicts the repo's own prefix
convention. Renaming them cascades into art (art/png/dir_turret.png,
art/sprites/dir_turret.svg and the model library key), so it is a wave of its
own rather than a rider on this one.

## What P7-9 turned out to be

Filed as **data only**. It was not, and the reason generalises.

Three things stood between the ticket and three new files. Two were the open
questions the row already named. The third was in nobody's ticket: missions 01
to 03 are hand-typed 64x48 grids, written before doc 26 existed, at a moment
when the skirmish pool had just been regenerated at 96x64 to 256x192 with a
decorative layer on the finding that the old maps were "not big and detailed
enough". Three more hand-typed missions would have shipped that same complaint
into the campaign, and no row said so.

Writing them properly then surfaced three defects that had been sitting in the
tree, all of the same shape - **a rule keyed on an instance where it should key
on a property**, now the ninth, tenth and eleventh instances this phase:

- `MapLoader`'s structure switch had no arm for Emplacement, Airfield, Bastion
  or Shroud Nest. Those kinds have been spawnable, buildable and GATED for
  weeks; a map simply could not place one. The switch's own comment records
  this happening before (PROD-D7, the service depot).
- `campaign.txt`'s id legend had gone stale by six structure types and six
  units, because nothing read it except a running client's sidebar.
- `SkirmishLive.cs` sets missions up with `switch (setup.MissionIndex)`, so a
  new mission needs a C# edit or it silently has no base.

The first two are fixed and now guarded in CI by campaigngate stage 1, which
loads every mission the manifest names and refuses ids that do not resolve. The
third is P7-9a above, deliberately deferred because fixing it moves goldens.

**The lesson worth keeping:** a row estimated as "data only" was carrying three
code defects, and none of them were found by reading the tickets. They were
found by trying to use the feature end to end for the first time.

## What P7-11 turned out to be

Three rows in a trenchcoat, and only one of them is mine to take. The bar is the
one P7-7 set and P7-6 was refused by: **is the thing written down, with what it
does, or would I be inventing it?**

- **The Saboteur is written**, in the same GDD line and the same form as the
  Infiltrator I shipped in P7-7: `Saboteur (disables buildings)` (GDD s7,
  line 64). Unit named, faction assigned, effect stated. Only the duration is a
  judgement call, exactly as only the Infiltrator's 20 per cent share was. Taken,
  as P7-11a.

- **The hero is named and nothing else.** GDD line 62 gives `Commando (hero, one
  at a time)` and line 64 `Shadow Commando (hero)`. No ability, no stats, no tier,
  and "one at a time" has no machinery in the sim at all - there is no per-unit-
  type build cap anywhere, only ADR-005's `MaxBarriersPerPlayer`. Every
  interesting thing about a hero would be invention. And the project has already
  ruled on this exact line, in doc 23 at 142 and again at 599: the GDD "mandates
  the Repair Vehicle exactly as much as it mandates the Commando, which is to
  say it is a sample, not a system statement." Refusing it here is consistency
  with a decision already taken, not caution.

- **Mines are written nowhere.** Not in the GDD, not in ADR-005, not in any ADR,
  and nothing resembling the mechanic exists in `sim/`: no dormant entity, no
  proximity trigger, no hidden-but-not-stealthed state. The damage half would be
  nearly free (splash exists, `ApplyAreaDamage` exists, the superweapon is a
  countdown precedent) and the TRIGGER is the whole feature. B6 is one line of
  text in doc 24 with no design behind it.

Doc 24's B3 also names a medic, a field mechanic and a scout animal - as
ABSENCES. None appears in the GDD, and B3's own text concedes the repair vehicle
already covers the mechanic's role. A gap analysis noticing something is missing
is not the same as a design document asking for it, and this row is where that
distinction has to be made rather than blurred.

**Also worth recording, because it was found looking for something else:**
`data/schema.unit.json` declares `"additionalProperties": false` and does not
list the `air` key that `com_strike_flyer.yaml` authors and `DataLoader` reads.
There is no runtime JSON-schema validator in `sim/`, so the schema is
documentation that has silently drifted from the loader. Filed rather than
fixed in passing.

## What P7-8 turned out to be

The row assumed the sim was the hard part. **It was not.** `VictorySystem` is
already a last-one-standing rule over N seats with a per-seat announcement
latch; the save format already writes the seat count and loops it; the LAN relay
already takes `playerCount` as a real parameter and sizes everything from it;
and `SkirmishAI` already holds only its own seat and picks hostiles with "anyone
who is not me and not neutral". Three of the four two-player assumptions were in
the CLIENT.

The worst was silent and inverted the result. The client reconstructed the
winner by flipping a seat number, in three places, including deriving it from
the LOSER (`_winner = player == 0 ? 1 : 0`). With three seats a `Winner` of 2
called `OnEliminated(0)`, which set `_winner = 1`: **player 1 shown VICTORY and
the actual winner shown DEFEAT**, no crash, no log, as the last thing the match
says. A second defect ended everybody's match on the FIRST elimination event,
when the sim emits one per seat and plays on.

Two things about that are worth keeping:

1. **The CI guard meant to prevent exactly this could not see it.** Its regexes
   keyed on the literals `[01]`, so `PlayerId == 2` passed untouched, the
   `== 0 ? 1 : 0` ternary form evaded it entirely, and its remedy message
   actively recommended `EnemyPlayerId`, which IS `1 - LocalPlayerId`. A guard
   that teaches the assumption it is guarding against has to be changed in the
   same wave as the code, or it pulls new work straight back into the old shape.
2. **The headless client harness caught it, again.** The file's own comment
   records this class shipping once before and being found only by driving the
   client from seat 1. It was found the same way this time. That harness has now
   caught eleven defects the sim battery is structurally blind to.

**And one hard ceiling found while looking for something else:** `DetectedMask`
is a `byte`, so eight seats is the limit - exactly GDD s9's maximum of you plus
seven, with zero margin. A ninth seat shifts out of the byte and that player's
detectors silently stop revealing stealth. It is hashed state, so widening it
later is expensive. Recorded in ADR-031 rather than left to be discovered.

## What P7-8b turned out to be

A refactor from a PAIR to an ORBIT, and the orbit found coverage the pair never
had. `mapgen.py` wrote every feature as "a cell and its one 180-degree image",
and `validate()` compared `starts[0]` against `starts[1]` seven times over. Both
are now the general form: a feature is written as its whole orbit under the
map's symmetry group, and every fairness check runs over all starts.

Two decisions inside it:

- **`mirror2` (double mirror), not a quarter turn.** 90-degree rotation requires
  a SQUARE map and not one of the nine maps in the pool is square, so adopting
  it would have meant the first four-player map also being the first square one.
  The double mirror works on any rectangle.
- **Seats 0 and 1 must be the 180-degree pair**, asserted by the generator.
  `rot180` is a member of the `mirror2` group, so ordering the starts this way
  makes a TWO-player game on a four-start map exactly as fair as on any existing
  two-start map. That is what lets skirmish-09 be offered in the menu today,
  while the lobby still only expresses two seats.

**The generalisation was tested by breaking it**, five ways, each refused with a
specific message. The one worth keeping: closing every crossing but leaving one
pass unrecorded was caught with "starts 0 and 2 stay connected" - **a pair the
old `starts[0]` versus `starts[1]` check never looked at**. The refactor did not
merely tolerate more starts, it closed a hole that existed at two.

Two honest costs of the group, both documented in doc 26 rather than left to be
found: a feature sitting on a mirror axis cannot wander across it without being
reflected into two features, so the dykes vary in width rather than position;
and decoration is placed as an orbit too, so the four quarters cannot be dressed
differently the way skirmish-02 distinguishes its two lands. The Kiln is the
map's only landmark.

One thing deliberately NOT done: the default start-separation floor,
`int(0.7 * max(w, h))`, cannot be met by any four-quadrant layout, because it
exceeds `min(w, h)` on anything wider than 1.43:1 while a four-quadrant map's
closest pair faces across the SHORT axis. skirmish-09 passes an explicit
`min_separation` with its reasoning. Making the default aware of the seat count
is the right fix and would change what the floor means for two-start maps, so it
is a wave of its own.

## What this phase does NOT do

It does not chase the unit counts in doc 24's table. Thirteen units against
twenty or thirty is a real gap, but parity by headcount is the wrong target: the
benchmarks' rosters carried duplicated roles, and this project has spent P6
building things they never had. The goal is that no CATEGORY of decision is
missing, not that the lists are the same length.

## Prerequisites carried from P6

These block rows above and are not P7's to solve:

- **Q017**, the faction-identity sequencing question, blocks P7-5.
- **Q014**, the second-resource question, is unrelated to parity and stays in P6.
- **ADR-027**, the crowd-aware movement decision, blocks nothing here directly
  but distorts every AI-vs-AI measurement any of these rows would be judged by,
  so it should be answered before P7-2's balance work.
- ~~**Q012/Q016**, win and loss semantics, block P7-9.~~ Both answered and closed
  2026-08-01 under the standing directive, having passed their decide-by dates.
  Q012 took fork 3 (elimination and a scripted objective are both wins); Q016
  took option 1 (you have lost when you hold nothing that counts), which needed
  a new `eliminated P` trigger condition because the obvious fix - stop
  suppressing short game - hands the player an instant win against an attacker
  that owns no buildings.

## Changed / Assumed / Needed next

**Changed.** New file: the plan doc 24's analysis asks for.

**Assumed.** That "set that as a goal" means the parity gap becomes the project's
next phase after P6's remaining decision-gated rows, not that P6 is abandoned.
P6's tracker stays authoritative for its own rows.

**Needed next, and from whom.** Luke, three decisions, in this order: is P7-1 a
defect or intent; does the air layer or transport lead Tier A; and is D2's
player-count promise kept or the GDD amended to match what shipped. The first
unblocks a small fix immediately; the other two decide the shape of the phase.
