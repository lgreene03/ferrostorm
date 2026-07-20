# P6 Wave B6 delivery notes: ferrite fields regrow

Closes the B6 row of the P6 campaign tracker under ADR-012 (ratified), which
formalises doc 24's classic-parity finding that permanent depletion caps match
length and starves long sieges. One golden regeneration, authorised by the ADR's
hash-impact clause and explained row by row below. Plan comment first (CLAUDE.md
workflow rule 2), delivery notes and the standard footer at the end.

## Plan

labels: persona:economy gdd:s5 phase:6 owner:sim-engineer + architect (Balance under A11 for the numbers)

**The rule is the ADR's, exactly.** Each FerriteField regrows a fixed amount on a
fixed interval up to its spawn amount, deterministically, in entity-index order,
in a new small RegrowthSystem step ordered explicitly in the tick, never iterating
a dictionary. The load-bearing constraint is the own-amount rule: a field regrows
only while its own remaining amount is above zero, so a field stripped to zero is
dead ground forever and denial by exhaustion stays a real choice. The ADR's
rejected alternatives (spreading to neighbours, unlimited regrowth regardless of
remaining) are not implemented; the ratified own-amount rule is.

**The RegrowthSystem sits right after HarvestSystem.** The tick order is depletion
then replenish, the economy pair: HarvestSystem drains, RegrowthSystem tops up.
It is ordered explicitly in World.Step between HarvestSystem and ProductionSystem,
a list walk by entity index. Order among fields does not affect the result because
each field's regrowth reads only its own amount and cap, but the ADR mandates the
index walk and it is honoured.

**The interval is derived from the tick, not a stored counter.** Regrowth fires when
the tick is a positive multiple of the interval. RegrowthSystem runs before the
tick increments, so the pre-increment tick is tested and the first regrow lands at
tick 75, a full interval after tick 0. Because the tick is saved, regrowth resumes
correctly across save and load with no new hashed or serialized counter. This is the
cleanest reading of the ADR's "the interval counter must be part of hashed state or
derived from the tick".

**The numbers are /data with a compiled reference twin.** ADR-012 says the numbers
live in /data, schema-validated, on a field definition, defaulting to the placeholder.
Ferrite fields are map-placed via SpawnFerriteField rather than catalogue-driven, so
there is no unit or building file to extend; they get their own home,
data/fields/com_ferrite_field.yaml (regrow_amount 1, regrow_interval_ticks 75),
validated by data/schema.field.json. World.DefaultRegrowAmount and
DefaultRegrowIntervalTicks are the compiled reference twin the selftest proves the
file reproduces, exactly as the unit and structure catalogues work. The scenarios
and the battery build compiled worlds, so the goldens rest on the twin; the shipped
client loads the file through CatalogueFiles.RegisterFields so a Balance edit takes
effect rather than sitting decorative.

**The spawn amount travels on the entity, and is serialized but not hashed.** A field
regrows towards its spawn amount, which is per-instance (the map's standard deposit,
or a scenario's own amount), so it is new per-entity state, FerriteCap, set once at
spawn and never mutated. Like Sight it is serialized (save format v5) but deliberately
not part of ComputeStateHash: it is immutable spawn-time provenance, identical on every
client, and hashing it would move every golden including the no-ferrite scenarios the
ADR requires stay still. FerriteAmount, which regrowth mutates, is already hashed, so
regrowth is a hashed mutation and the desync coverage is complete.

Assumptions: the spawn amount is left where it already lives (a compiled constant on
map-placed fields and the scenario's own literal), not relocated into /data, because
moving it is out of scope for this wave and the ADR asks only for the regrow numbers
as data; the regrow config is not folded into CatalogueChecksum, matching how the
sibling ferrite number (the standard field amount) is treated and keeping the blast
radius off the save and replay refuse-gates, a documented small decision (see Needed
next); a pre-v5 save defaults the cap to its stored amount, treating the saved level
as the ceiling, which is the sane resume the ADR's save-compat clause names.

Interfaces touched: sim/Ferrostorm.Sim (World.cs: the RegrowthSystem, the Step order,
Entity.FerriteCap, ConfigureRegrowth and the compiled twin, the SpawnFerriteField cap,
the unhashed-cap note in ComputeStateHash; World.Serialization.cs: save format v5;
DataLoader.cs: FieldData, ParseField, CatalogueFiles.RegisterFields),
sim/Ferrostorm.Sim.Runner/Program.cs (the regrowthgate mode and its Match/dispatch
wiring, the selftest field round-trip, DowngradeSave's v5 handling, the ScenarioEconomy
surplus), game/scripts/SkirmishLive.cs (the client loads the field def), data (the field
definition and its schema), sim/golden-hashes.txt (the wave's one regeneration, every
row explained below), docs (the campaign tracker, the ledger, this file).

## Delivery notes

Shipped in three code commits in load-bearing order: the /data numbers (374c1a8), the
RegrowthSystem, loader and save v5 (a8f20ac), the gate and the one golden regeneration
(d7ff34c); docs ride last.

**Part 1, the numbers as data.** data/fields/com_ferrite_field.yaml carries
regrow_amount 1 and regrow_interval_ticks 75, validated by data/schema.field.json in
the same shape as the unit and structure schemas. World.DefaultRegrowAmount and
DefaultRegrowIntervalTicks are the compiled twin; the selftest walks /data/fields,
asserts the file reproduces the twin exactly, and runs it through RegisterFields, the
same round-trip discipline the two catalogues hold. The client's RegisterCatalogue
now calls RegisterFields on the same pre-tick-0 path, so the game runs the file, not a
silent copy of the twin.

**Part 2, the RegrowthSystem and save v5.** The system tops each below-cap, above-zero,
alive field up by the amount, clamped to the cap, on the tick-derived schedule, ordered
after HarvestSystem. FerriteCap rides on the entity, set at spawn, serialized after the
rally tail in save v5; a pre-v5 save loads with the cap defaulted to its stored amount.
DowngradeSave learns the v5 stream and strips the cap (and, for a pre-v4 target, the
rally tail) so the spawngate's downgrade proofs keep working. ComputeStateHash carries a
note that the cap is intentionally unhashed, next to Sight's established precedent.

**Part 3, the gate and the economy surplus.** The regrowthgate mode proves, additively
and outside the golden list, that a below-cap field recovers at exactly the placeholder
rate, a field at cap never overflows, a field stripped to zero stays dead across four
regrow intervals, and a v5 save round-trips regrowth and the cap with a v4 downgrade
resuming sanely. ScenarioEconomy's assertion moves from 4000 to 4014: the two fields
still deliver their full 4000 plus exactly 14 that regrew while they were alive and below
cap before exhaustion. It stays exact, not a band, because the harvest of these fixed
fields is deterministic across every seed (verified at 2026, 31337, 424242 and 777, all
4014).

**The regeneration, explained row by row.** Exactly five of the twenty-four goldens
move, and every one is a world whose ferrite is harvested below cap and runs long enough
to reach a regrow tick:

- economy (0xC878...DA7 to 0x8302...4D1): three harvesters strip two 2000-unit fields to
  exhaustion over 5000 ticks; the fields collect 14 units of regrowth while alive, which
  is harvested and delivered, so the credits and the final state both move.
- skirmish (0x165D...C53 to 0x4F62...346): the full AI-vs-AI match on skirmish-01, whose
  twenty 12000-unit fields are mined below cap over a long game; regrowth trickles into
  both economies and the final hash moves.
- expansion (0xB6DB...DC9 to 0x8863...280): the AI founds a second base at the rich field
  and mines it down; a below-cap field regrows, so the migrated economy moves.
- aisuper (0x6165...A37 to 0x7FBD...938): the AI harvests its 12000-unit field below cap
  to fund the superweapon; regrowth moves the run.
- mission (0x655E...D95 to 0x1E2A...FC3): mission-01's two fields are harvested below cap
  across the 3574-tick mission, so its final state moves.

The other nineteen rows are byte-identical. Sixteen contain no ferrite field at all and
cannot move. mission02 loads a map with zero ferrite cells and does not move. mission03
carries a single ferrite field but it is never harvested below its cap in the survival
mission, so regrowth is a no-op there and the row holds. capture has no ferrite and holds.

The neutralisation proof is decisive and matches the discipline of B2 through B4. Because
the cap is unhashed and regrowth is the only new state mutation, disabling the single
RegrowthSystem call on a scratch build (with the economy assertion temporarily returned
to 4000) regenerates all twenty-four rows byte-for-byte back to the pre-wave goldens. So
regrowth is the sole cause of every move, the five movers move only because of regrowth,
and the nineteen non-movers, no-ferrite scenarios included, are untouched by the cap,
the serialization and the data plumbing. Nothing moved unexplained; nothing explained
failed to move.

### Verification evidence

Full battery exit 0: the default no-args run (selftest with all three catalogue
round-trips including the new ferrite twin, determinism 24/24 double-run identical at
seed 2026, match with every scenario assertion plus the defence load gate, catrefuse,
spawngate, prodgate and the new regrowthgate, lan 5/5). The regrowthgate reported a
below-cap field recovering exactly 13 over 1000 ticks (1 per 75) with the harvest
sequence unchanged, a field at cap never overflowing, a stripped field staying dead
across 300 ticks and four regrow intervals, a v5 save round-tripping regrowth and the
cap bit-exact and resuming identically, and a v4 downgrade loading with the cap
defaulted to the stored amount hash-identically.

Standalone gates green: saveload (v5 save resumed to the uninterrupted final hash
bit-for-bit), campaignsave, replay (a 3000-tick AI match reproduced bit-exactly),
spectate, lanchaos, catrefuse (/data and the compiled catalogue agree on the same
checksum, unchanged), and the balance gate (VERDICT PASS; the reported 0-6 faction war
is the pre-existing TICKET-AI-05 note, not a regression).

AI-vs-AI resolves on a map with ferrite: the skirmish scenario ran the complete loop,
bases built, harvesting live, 33 entities destroyed, both treasuries positive (695/870),
and the match ended in bounded time; the replay gate ran a full 3000-tick AI match and
the five lan games each completed with zero desyncs. Regrowth changes the economy without
starving the AI or preventing the game from ending.

Goldens byte-identical against a fresh golden 2026 run, LF verified (the Console.Out.NewLine
guard, three header lines preserved, only the twenty-four data rows replaced). The sim
purity grep is clean (no float, double, System.Random or Godot in the sim library; no
engine reference outside game). Both Godot client builds succeed at zero warnings (Debug
and ExportRelease); project.godot is untouched. No user saves, replays or settings.cfg
were written: the gates use temporary files and in-memory streams only.

Q013 note: the nightly-soak determinism at seed 900913 fails on an unrelated crowd-pathing
convergence issue in ScenarioPathing that predates this wave (docs/questions/Q013). This
wave's determinism at seed 2026 passes, and the change touches only ferrite fields, so
900913's ScenarioPathing failure is Q013, not a B6 regression.

## Changed / Assumed / Needed next

**Changed:** sim/Ferrostorm.Sim (World.cs: RegrowthSystem and its Step slot after
HarvestSystem, Entity.FerriteCap, ConfigureRegrowth with the DefaultRegrowAmount and
DefaultRegrowIntervalTicks twin, the SpawnFerriteField cap, the unhashed-cap note in
ComputeStateHash; World.Serialization.cs: save format v5 carrying the cap, pre-v5
defaulting it to the stored amount; DataLoader.cs: FieldData, ParseField and
CatalogueFiles.RegisterFields), sim/Ferrostorm.Sim.Runner/Program.cs (the regrowthgate
mode wired into Match and the dispatch, the selftest field round-trip, DowngradeSave's
v5 handling, ScenarioEconomy's 4014 surplus), game/scripts/SkirmishLive.cs (RegisterFields
on the client's load path), data (fields/com_ferrite_field.yaml and schema.field.json),
sim/golden-hashes.txt (the wave's one regeneration, every moved row explained above and
proven by neutralisation), docs (the campaign tracker set to DONE, the phase-1 ledger
line, this file).

**Assumed:** the field's spawn amount stays where it already lives rather than moving into
/data, since the ADR asks only for the regrow numbers as data and relocating the deposit
is out of scope; the regrow config is not added to CatalogueChecksum, matching the sibling
standard-field-amount constant and keeping the save and replay refuse-gates off the change;
a pre-v5 save defaults the cap to its stored amount, the sane resume the ADR names; the
gate's recovery window and field sizes are chosen so the field never nears depletion, which
makes the differential rate proof exact; the economy surplus of 14 is pinned exactly rather
than as a band, because it is seed-independent.

**Needed next (from whom):** Balance (balance agent) owns the two numbers under A11 and can
tune data/fields/com_ferrite_field.yaml when the late-game economy is play-tested; the
selftest round-trip will hold them to the compiled twin, so a change there must move the
twin in lockstep. The Architect may wish to fold the regrow config into CatalogueChecksum
if regrow numbers ever diverge across a version boundary, so a save or replay made under
different numbers refuses before tick 0 rather than diverging at the first regrow tick;
today it is left out, consistent with the standard-field-amount constant, and noted here so
the decision is visible. The client (client-engineer) may want to surface a field's cap and
current amount in the selection panel now that a field is no longer a monotonic countdown.
