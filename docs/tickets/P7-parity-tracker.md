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
| P7-9a | ~~and onto self-declared setup, retiring `switch (setup.MissionIndex)`~~ **DONE** (ADR-034). Still owed: bring missions 01 to 03 under the GENERATOR, which is content rather than a defect | D1 | - | ONE golden regenerated (`mission`), measured; `mission03` was predicted to move and did not, for a reason worth reading | **PART DONE** - campaigngate proves every mission's setup comes from its own file |
| P7-10 | Wall tiers and gates | B7 | C6b: Luke must override ADR-005 clause 6 | MOVES | pending |
| P7-11 | ~~Hero unit, mines, support infantry~~ - **SPLIT, 2026-08-01.** One row bundling one thing that is written, one the project has already ruled is a sample, and one that appears in no design document at all | B3/B4/B6 | - | - | **SPLIT** - see "What P7-11 turned out to be" below |
| P7-11a | The Sodality's Saboteur: temporarily disables a building | B3 | - (GDD s7 names the unit AND its effect; only the duration is my call) | goldens NEUTRAL, measured; catalogue checksum MOVES; save format v10 | **DONE** - ADR-030; saboteurgate (6 stages) |
| P7-7a | **Client defect carried by P7-7:** the Infiltrator's theft raised `GameEventType.Captured`, which the client reads as an ownership CHANGE, so robbing a building told its owner "STRUCTURE LOST TO CAPTURE" about a building they still held, klaxon and all | - | - | goldens NEUTRAL, measured (the event enum is neither hashed nor saved) | **DONE** - `GameEventType.Robbed`; infiltratorgate gained the stage that catches it (proved by restoring the defect and watching it fail); client harness 130 -> 133 checks |
| P7-11b | Hero unit: Commando and Shadow Commando | B4 | - (authorised 2026-08-01; the design is INVENTED and recorded reversibly in ADR-035) | goldens NEUTRAL, measured; catalogue checksum MOVES | **DONE** - ADR-035; herogate (7 stages incl. the no-cap control that protects the goldens) |
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
that cannot finish. It was refused loudly in `Lan.BuildFrom`, and I said lifting
the refusal needs LAN seat negotiation. **Lifted by P7-8f, and it needed no
negotiation at all**: the spare seats are played by commanders that each peer
generates locally and folds into the same tick, which is safe because the
commander is deterministic and its tuning rides the catalogue checksum the hello
already refuses on. `lanaiseatsgate` measures it, including that a peer running a
different commander is caught rather than played on.

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

## /data finally holds what the project says it holds

Four consecutive waves, each one surfaced by the last, and worth reading as one
thing because the shape repeats: **a claim that was true when written and
quietly false since.**

1. `schemagate` found the schemas were never validated by anything at all, and
   `schema.unit.json` had been four waves behind the loader on the `air` key.
2. Which surfaced that `data/weapons/` was EMPTY, every weapon number compiled.
3. Which surfaced that `RegisterAll` did not register all, and had not since
   fields were added.
4. Which surfaced that `data/ai/` was empty too (ADR-032).

`/data` now holds every gameplay number the project claims it does: units,
structures, fields, weapons and the AI's tuning. `schemagate` walks five
schemas, 52 definitions and 640 keys.

**The lesson that generalises, and it is the one the whole session keeps
producing:** every one of these was a rule keyed on an instance rather than a
property, or a sentence that documented an intention rather than a mechanism.
None was found by reading a ticket. Each was found by trying to use the thing
end to end and noticing the claim did not hold.

**And one genuinely new rule, from ADR-032 clause 2**, because the next authored
kind will face it: moving a number from code into `/data` moves it from "agreed
by construction" to "agreed only if checked". The AI's numbers being compiled
was an unwritten safety property - two LAN peers agreed on them because they
could not disagree. Anything that can differ between peers and change the
command stream must be in the catalogue checksum. The gate proves the fold is
real by moving the checksum on one unit of wave size.

## The harness gap ADR-033 left, closed

ADR-033 recorded honestly that the client-side rule about WHICH seats get
commanders rested on reasoning and a blob round-trip check, not on the harness,
whose LAN stage built a two-seat world. That gap is closed.

Two things now hold it. The lobby stage runs a second time on **skirmish-09
across a real socket**, and asserts the joiner took the host's seat count, that
both peers built a FOUR-seat world, and that the two are byte-identical before
tick 0. The two-seat stage could not see this class at all: on a two-start map
both sides answer 2 whatever either believes, so a peer that DISAGREES about how
many seats exist is invisible there.

And `LanCommandedSeats` is asserted **from seat 1**, which is the seat this
harness drives and the seat where the old rule's absence shows: "every seat that
is not the local one" read from seat 1 returns seat 0, the human on the other end
of the socket, and would have handed Brutal's handicap to a person.

The method exists at all because the rule was **two loops sharing a bound** - one
building commanders, one granting the handicap. They are the same rule, and the
two drifting apart would be a desync that reads correct on either machine alone,
which is the exact species of defect that preceded it.

Client harness 146 to 153 checks. Also corrected a message from the previous wave
that contradicted itself, reporting "came back 3, not the host's 3" on success.

## The AI aimed at whoever spawned first

Fixed 2026-08-01, and it is the clearest example this phase produced of a defect
that no amount of testing was going to find.

`SkirmishAI` picked the enemy REFINERY as "the first one in entity order", and
that pick beats the nearest-production-structure one at both use sites, so it
decided where every wave and every superweapon went. With ONE opponent, first
and nearest are the same refinery. With three, it means the commander attacks
whichever player happens to sit earliest in the entity array, for the whole
match, **deterministically and reproducibly** - which is precisely why it would
never have been reported as a bug. An AI attacking someone always looks like
intended behaviour.

It is now nearest by the same measure the structure pick uses.

**Measured NEUTRAL: no golden moved.** That is the point rather than a relief -
no golden scenario distinguishes the two rules, so nothing existing proved the
old behaviour and nothing existing would have proved the new. `aitargetgate`
spawns a FAR enemy refinery BEFORE a near one and asserts the first wave goes
near, with both still standing so it is a choice between live targets. Proved to
bite by restoring the old line.

**One thing the gate got wrong first, worth keeping**: it asserted that EVERY
wave order went to the near refinery, and read 17 of 34 as a failure. It was the
commander correctly moving on: the near refinery had fallen, so the far one WAS
then the nearest. A gate demanding every order go to one place asserts that the
AI never finishes anything. The claim is about the FIRST wave.

## The hero, and the two defects adding it exposed

P7-11b is the first row this phase where the design is INVENTED rather than
implemented, and ADR-035 records the alternatives beside each choice so
overturning one is an edit rather than an excavation. The three that matter:
demolition is DAMAGE rather than deletion, so the hit-point column still decides
who dies; the hero SURVIVES its own act where the other three contact units are
consumed, which is what gives "one at a time" something to protect; and "one at
a time" is BUILT, as a general `max_alive` column that is a no-op at 0.

**Adding a fourth effect to a method that already had three is what exposed what
the existing three assumed**, and both findings are worse than the feature:

- **`UnitTypeDef.Air` was in neither `Equals` nor `CatalogueChecksum`**, since
  ADR-028. A drifting `air:` key was invisible to the /data round-trip selftest
  AND to the LAN desync guard, so two peers could disagree about which units FLY
  while every unit, building and gun matched. Worse than the usual case: ADR-028
  clause 3 makes engagement an equality between a weapon's anti-air flag and its
  target's airborne one, so the peers would disagree about what can be SHOT.
- **The Infiltrator crashed on a neutral outpost.** `CanBeActedOn` admits one
  deliberately (capturing a neutral outpost is ADR-021's feature) and the theft
  branch then indexed `_credits[-1]`. An index-out-of-range reachable by
  right-clicking an outpost, latent since P7-7, proved by removing the guard and
  watching the gate throw.

## Seven units were unbuildable, and one still is

Found 2026-08-01 while giving the Infiltrator and the heroes sidebar buttons.
The panel had a **hand-maintained thirteen-entry table** against a catalogue of
twenty, so the Carrier, the Strike Flyer, the Flak Track, the Infiltrator, the
Saboteur and both heroes had no button at all. **P7-3 and P7-4 were reported as
DONE with their units unreachable by any player.**

The list is derived from the catalogue now, and the refactor is provably inert:
all thirteen hand-written labels are EXACTLY what stripping the faction prefix
and upper-casing produces, with zero mismatches. Nineteen buttons where there
were thirteen.

**The same defect had a second instance**, which the first one's own comment
predicted. `SkirmishLive` carried another thirteen-entry name table with a
length guard returning "UNIT" past its end, so every unit from 14 up read as
"UNIT" in the selection readout and in every toast. That comment records the
table falling behind once already and being fixed BY ADDING ENTRIES, which
treats the symptom: a hand-maintained list of something the catalogue already
knows will fall behind again, and the length guard is what makes it silent. One
derivation now, shared, throwing on an unknown type rather than shrugging.

**FIXED 2026-08-01, the wave after.** `IsProducer` admits the Airfield, the
sidebar has an AIRCRAFT tab, and `airgate` gained the stage it should always have
had: one that ORDERS a flyer rather than spawning it. **All 20 units now carry a
button.** Measured neutral: no golden stands an airfield and the queue fold is
`TryGetValue`-guarded, so an empty one contributes nothing.

Both comments were to-do notes that outlived what they waited for.
`IsProducer` said "the Airfield joins when it exists (it is a slot-model producer
and waits on the air-layer ADR)"; the sidebar said "Four, not five - AIRCRAFT
waits for the air ADR with the airfield it would build". ADR-028 shipped both and
nobody came back to either. **A to-do in a comment is invisible to every gate in
the project**, which is the reusable lesson: the note was accurate, prominent, and
did nothing.

"Slot-model producer" is left UNBUILT rather than invented - aircraft occupying
pads, with capacity limiting how many fly, is a real design and is not what this
fixed. The Airfield queues like every other producer, the smallest thing that
makes the tier reachable.

**The original finding, kept for the record:**
`World.IsProducer` is `Factory or ConstructionYard or Barracks`. **The Airfield
is not in it.** `Produce` breaks on that predicate before reading anything else,
so **the Strike Flyer cannot be built by anybody, in any mode, and never could
be.** ADR-028 shipped an air layer whose aircraft is unreachable.

`airgate` never caught it because it spawns flyers with `SpawnUnit` directly and
never ORDERS one - the same shape as P7-7a, where a gate proved the sim's
behaviour and said nothing about what the game does. The sidebar wave therefore
ships 19 buttons rather than 20, deliberately: a button the sim silently drops
would break the one property that panel guarantees. Fixing `IsProducer` is a sim
change touching the four sites that predicate's comment names, including a
queue-hash fold, so it is its own wave and it is next.

## Reachability, proved systematically rather than row by row

Three defects this phase were one shape: something existed in the sim and no
player could reach it. Seven units had no button; the Strike Flyer had no
producer at all; a robbery announced itself as a capture. **All three passed
every gate, because the gates CONSTRUCTED the outcome instead of asking for it.**
`airgate` spawned its flyers with `SpawnUnit` and was green for months over an
aircraft nobody could build.

`reachabilitygate` is the systemic guard. It orders **every** registered unit
with a real `Produce` command at a producer the ordering player has BUILT, and
every buildable structure with `BuildStructure` then `PlaceStructure`, from one
spawned Construction Yard per player and nothing else. **20 of 20 units and 14 of
14 buildable structures.** The three excluded types are checked BOTH ways: named
here with a reason, and confirmed to have no build time in `/data`, so a fourth
map-placed building has to be documented rather than absorbed.

It found nothing further unreachable, which is the answer to "is there more of
this" and is worth as much as a finding would have been.

**The tech tree closes in three rounds from a bare yard with no authored build
order**, which is a stronger statement about the tree than the gate set out to
make.

**And it found a different defect of the same family.** `World.SpawnHarvester`
predates the catalogue and ignores it: it never sets `UnitType`, so every
harvester in the game stands as type 0 and its authored def cannot be read back
off the entity - `AtMaxAlive`, `IsAirborne` and the client's name and model
lookups are all blind to it. It also hardcodes hp, armour, sight and speed, and
**the speed diverges: 1/5 in code against `speed: 18` in `com_harvester.yaml`, so
every harvester moves at 0.20 where the data says 0.18.** P7-1's defect exactly,
in the oldest spawner in the file. Fixing it moves every golden with a harvester
in it, so it is its own wave and it is next.

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
