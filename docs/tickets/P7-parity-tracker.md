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
| P7-8d | The lobby seats what the map declares: N commanders, one per non-local seat | D2 | - | goldens NEUTRAL, measured; NO sidecar or wire format change, because the seat count is DERIVED from the map rather than stored | **DONE** - `SeatsFor(map)`; client harness 133 -> 138 checks |
| P7-8c | Teams and alliances: GDD s9's "custom lobbies up to 4v4" | D2 | **PRODUCER.** Not a sub-task of P7-8 but a second project of comparable size | MOVES | **NOT TAKEN** - there is no team field, no alliance table and no `AreAllied` predicate anywhere in the sim; hostility is decided everywhere by "not me and not neutral". It touches targeting, splash friendly-fire, detection and fog sharing, victory and the AI, and it has NO code to build on |
| P7-9 | Campaign missions 4 to 6 | D1 | ~~Q012/Q016~~ - both ANSWERED and closed under the standing directive | goldens NEUTRAL and checksum UNMOVED, measured (unusual for P7: `MissionRunner` state has always been outside the world hash) | **DONE** - ADR-029; campaigngate (5 stages) |
| P7-9a | Bring missions 01 to 03 under the generator, and onto self-declared setup, retiring `switch (setup.MissionIndex)` in SkirmishLive.cs | D1 | - | MOVES (two goldens: entity spawn order changes) | pending - **created by P7-9, not inherited.** Missions 04 to 06 declare their own yard and credits in the fmap; 01 and 03 still get theirs from a per-mission case in C#. Two mechanisms is the duplication this phase keeps finding, and the only reason it was left is that fixing it moves goldens for no behavioural gain |
| P7-10 | Wall tiers and gates | B7 | C6b: Luke must override ADR-005 clause 6 | MOVES | pending |
| P7-11 | ~~Hero unit, mines, support infantry~~ - **SPLIT, 2026-08-01.** One row bundling one thing that is written, one the project has already ruled is a sample, and one that appears in no design document at all | B3/B4/B6 | - | - | **SPLIT** - see "What P7-11 turned out to be" below |
| P7-11a | The Sodality's Saboteur: temporarily disables a building | B3 | - (GDD s7 names the unit AND its effect; only the duration is my call) | goldens NEUTRAL, measured; catalogue checksum MOVES; save format v10 | **DONE** - ADR-030; saboteurgate (6 stages) |
| P7-7a | **Client defect carried by P7-7:** the Infiltrator's theft raised `GameEventType.Captured`, which the client reads as an ownership CHANGE, so robbing a building told its owner "STRUCTURE LOST TO CAPTURE" about a building they still held, klaxon and all | - | - | goldens NEUTRAL, measured (the event enum is neither hashed nor saved) | **DONE** - `GameEventType.Robbed`; infiltratorgate gained the stage that catches it (proved by restoring the defect and watching it fail); client harness 130 -> 133 checks |
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
`data/schema.unit.json` declared `"additionalProperties": false` and did not
list the `air` key that `com_strike_flyer.yaml` authors and `DataLoader` reads.
**FIXED 2026-08-01**, and the fix is the guard rather than the key.

CLAUDE.md says gameplay numbers "live in /data as YAML **validated against**
/data/schema.unit.json". The first half was true and the second half was not:
the schemas declare `additionalProperties: false` and **nothing anywhere read
them**. There is no JSON-schema validator in the tree, so they were
documentation, and documentation drifts. It had already drifted for four waves,
and under the schema as written `com_strike_flyer.yaml` was invalid the whole
time.

`schemagate` is that sentence enforced: 36 authored definitions and 540 keys
checked against the three schemas every build. It checks the DATA against the
SCHEMA rather than trying to prove the loader and schema agree statically,
because a key the loader reads that nothing authors is harmless, while a key a
file authors that the schema forbids is either a typo the loader is silently
ignoring or a schema that has fallen behind. Both branches were proved to bite
by breaking them.

**And a second gap it surfaced: `data/weapons/` was EMPTY.** Every weapon number
in the game lived compiled in `Combat.cs`, which contradicted "all gameplay
numbers live in /data" as plainly as the unenforced schema did. **CLOSED
2026-08-01**: nine authored files, `data/schema.weapon.json`, and a fourth
directory in schemagate's walk (36 definitions and 540 keys became 45 and 597).

The part that mattered was not authoring the files. It was making them **drive
the runtime**. Writing the yaml while leaving `Combat.Weapons.Get(id)`
authoritative would have reproduced P7-1's defect exactly - authored data that
is parsed, validated and then dropped while the sim uses a hardcoded rule - and
it would have looked complete. `World` now holds a registered weapon table, the
runtime call sites read it, and weapons are folded into the catalogue checksum.

`weapondatagate` makes that mechanical rather than promised. Its second stage
registers a ten-cell gun where the compiled table says five, and asserts a
TURRET dealt damage at seven cells: a mobile shooter would have walked into
compiled range and passed the stage for the wrong reason, which is the kind of
false pass that has cost this project several waves. Measured 840 damage against
the control's 0, and proved to bite by reverting one call site.

Goldens byte-identical, because the transcription is exact and stage 1 asserts
it field by field. The catalogue checksum moved from `0x374FDD8212234CB2` to
`0x73326A3FF8AEA4D1`, which is expected: a new catalogue section changes it by
construction, on the same pre-first-public-build argument as P7-2/3/4 and P7-11a.

**That fan-out was the next wave, and it is CLOSED.** `CatalogueFiles.Register*`
was a per-kind opt-in, so `RegisterWeapons` had to be added beside all nine
`RegisterFields` sites plus the client's.

The name was the tell: **`RegisterAll` did not register all**, and had not since
fields were added. A caller who forgot a kind got a world with a partial
catalogue and NO error, silently falling back to the compiled defaults. The
recurring shape again, a rule keyed on an instance.

`RegisterAll(world, dataRoot)` is now the single honest entry point, and the
three-argument one is renamed `RegisterUnitsAndStructures` because that is what
it does. Thirty-two calls across eleven clusters became eleven.

**The guard is the point, not the tidying.** One table lists every `/data`
subdirectory as either a catalogue kind with its registrar or a known
non-catalogue, and both the registration loop and the guard read that ONE table
so they cannot drift. An unrecognised directory is refused by name:

> unrecognised /data directory 'zzz_probe'. Every directory under /data is either
> a catalogue kind this loader registers or one recorded as holding no defs; an
> unknown one would be authored, validated and then silently ignored.

So the next `/data` kind cannot be silently forgotten in one of ten places.

Checksum measured unchanged at `0x73326A3FF8AEA4D1` before and after, and the
gate asserts it three ways at once: the single call, the old per-kind sequence
run explicitly beside it, and a bare compiled world all agree.

One deliberate narrowing, recorded because it is a real semantic change: a
`/data` present but missing `weapons` now throws where it once silently compared
a partial checksum. That case cannot arise in this repo, and refusing it is
exactly the point of the wave.

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

## What P7-8d turned out to be

The obvious shape was to grow `MatchSetup` per-seat fields and version both
codecs that carry it, the save sidecar and the LAN blob. That is a compatibility
break and a migration, and it turned out to be unnecessary.

**The seat count does not need to be stored, because it is derivable from an
input the sidecar already names.** `SeatsFor(map)` reads the map's start count,
so a save or a replay written before multi-seat existed rebuilds the identical
world with no format version and no migration. Every map but skirmish-09
declares two starts, so every existing match is unchanged by construction.

The ceiling of 8 in that function is not a taste call: `Entity.DetectedMask` is
a byte, so a ninth seat shifts out of it and that player's detectors would
silently stop revealing stealth. ADR-031 recorded that ceiling; this enforces it,
which turns a silent wrong answer into a map that seats eight.

**One defect this created and caught before it shipped.** LAN seats exactly two
humans and builds its relay with `playerCount: 2`, but the world's seat count
now comes from the map. A LAN match on skirmish-09 would have seated two humans
and left two bases with NO controller: they would never act, and VictorySystem
would refuse to end the match until somebody walked over and razed them. A match
that cannot finish. It is refused loudly in `Lan.BuildFrom` now, and lifting the
refusal needs LAN seat negotiation, which is its own piece of work.

**And one the harness caught.** The opponent-faction rule was written as
"alternate between the player's pick and the opponent's pick", which reads
sensibly and is wrong: both default to the same faction, so all three opponents
came out identical. Alternating between the two FACTIONS holds whatever the two
menu picks happen to be.

~~Still not done, and deliberately: there is no opponent-count control in the
menu.~~ **Done as P7-8e, 2026-08-01.** I called the picker "menu work with no
new capability behind it" and that was wrong in one specific way worth
recording: filling every seat the map declares means a player who wants a DUEL
on skirmish-09 cannot have one. P7-8d did not add a capability, it removed a
choice, and GDD s9's wording is "1-7 opponents", which is a choice.

`MatchSetup.Seats` carries it, optional in the sidecar exactly as
`ai_difficulty` is, so zero means "fill the map" and every sidecar written
before the field resumes against the opposition it actually played. No format
version and no migration, the same trick P7-8d used to avoid one.

**The map remains the ceiling and always wins.** A hand-edited or corrupt
sidecar asking for nine seats on a four-start map gets four rather than a
`PlaceSkirmishStart` refusal, which is the "a corrupt sidecar must not take the
menu down with it" posture the difficulty rung already takes. Asserted both
ways in the harness, which went 138 to 145 checks.

The control's RANGE comes from the selected map, because a count the map cannot
seat is not an option, it is a crash waiting for a player to find it. On a
two-start map that leaves exactly one option, which is correct rather than a
special case, and the control says so instead of hiding.

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
