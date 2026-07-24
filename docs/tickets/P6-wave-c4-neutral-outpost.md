# P6 Wave C4 delivery notes: the neutral Outpost

Closes the C4 row of the P6 campaign tracker under ADR-021 (ratified): the
capturable neutral income structure of GDD line 41, doc 22 P5-ECON-14's shape.
NEUTRAL hash impact, exactly as the ADR's design pass predicted against the
tracker's "regeneration" guess: no golden move, no save bump.

## Plan

labels: persona:p2 gdd:s4 phase:6 owner:sim-engineer + architect + game-designer
(Balance under A11 for the income rate)

ADR-021 decided everything; this wave is its implementation, byte for byte.

## The Outpost as built

**Struct type 13, EntityKind.Outpost (the reserved 17).** MaxStructType is 13;
DefaultStructureType, StructureCatalogue.TypeIdOf/KindOf and
data/buildings/com_outpost.yaml carry the def (cost 500, BuildTicks 0, hp 1000,
draw 0, sight 5, unarmed), the selftest round-trips it ("all 12 compiled
structure defs"). BuildTicks 0 keeps it out of every yard queue (the yard and
barrier precedent) and no sidebar item names it: map-placed only.

**Capture came free.** IsStructure gains Outpost, which is the whole capture
wiring: CaptureSystem's only ownership test (t.PlayerId == e.PlayerId) already
passes a neutral -1, and the Attack handler validates only alive-and-valid, so an
engineer ordered onto a neutral outpost walks in and flips it, consumed by the
act, exactly as against an enemy factory.

**The income is one Kind-gated branch in ProductionSystem.** While owned
(PlayerId >= 0), the outpost pays OutpostIncomePerSecond (15) on the
pre-increment tick's positive multiples of TicksPerSecond, the ADR-012 regrowth
schedule idiom, so a loaded save resumes on the same beat. GDD line 41's "+15
credits/tick" is read as 15 per SECOND per doc 22's units-error flag (15/tick at
15 Hz would be 225/s, roughly ten harvesters). Guarded on PlayerId >= 0 twice
over: a neutral outpost pays nobody and _credits[-1] is never indexed.

**An outpost is not hope.** VictorySystem's hope test excludes Outpost beside the
barrier exclusion: a player whose last possession is a captured income node is
still eliminated. (The existing PlayerId < 0 guard already protected the hope
span from a neutral owner.)

**Neutral inertness is inherited, verified not assumed.** Auto-acquire skips
t.PlayerId < 0, so no unit ever plinks at an unclaimed outpost (an explicit
Attack still can); FogSystem skips PlayerId < 0, so an uncaptured outpost reveals
nothing and starts revealing for its captor the tick it flips.

**Maps place it with the existing line.** `structure -1 13 X Y tag` parses today
(int.Parse has no lower bound); only the BuildWorld spawn switch gained an
Outpost arm. No map-format bump, the reason P5-ECON-14 was chosen over BD-22's
new grid char. No shipped map carries one yet: placing outposts on the four
skirmish maps is a map-design pass for tools + balance, filed below.

**The client (presentation only).** ModelLibrary maps kind 17 to the refinery
interim (an industrial silhouette); DressStructure already declines the team
strip for player < 0, so an unclaimed outpost renders unclaimed for free, and the
turret brown-out dim is already gated PlayerId is 0 or 1. Bespoke model owed to
art-pipeline.

## Why the goldens do not move

Every new behaviour is Kind == Outpost gated, and no golden scenario or shipped
map spawns one, so all branches are dead code at seed 2026; no new hashed Entity
field, no schema key, no save change (v7). The catalogue checksum moves because
the catalogue grew, which nothing pins. Proven by the byte-identical golden run
below, the ADR-019 pattern.

## The new gate

OutpostGate joins the battery (additive, standalone + Match stage, never a golden
scenario, the golden list stays 24). It proves, all at exit 0:

- An engineer captures a NEUTRAL outpost (PlayerId flips to the capturer, the
  engineer is consumed), through the untouched CaptureSystem.
- The captured outpost pays exactly 10 x 15 credits over a 150-tick window (any
  150 consecutive ticks contain exactly ten second-boundaries, so the assertion
  is phase-independent and exact).
- A neutral outpost pays nobody over 60 ticks, stays neutral, and takes zero
  damage from an armed enemy parked beside it (auto-acquire skips neutrals).
- A player whose ONLY possession is a captured outpost is eliminated; the
  opponent with a real structure wins.

## Verification (local, real evidence)

- outpostgate standalone exit 0, first run.
- Full battery `match 2026` exit 0 (selftest now round-tripping 13 units and 12
  structures, determinism 24/24, every scenario assertion, repairgate,
  outpostgate, lan 5/5).
- The exact CI golden check byte-identical across all 24 rows.
- Both Godot client builds 0 warnings (Debug and ExportRelease).

## Changed / Assumed / Needed next

**Changed.** Sim: MaxStructType 13, the type-13 def, IsStructure + Outpost, the
VictorySystem hope exclusion, OutpostIncomePerSecond and the ProductionSystem
income branch, SpawnOutpost, the MapLoader arm; DataLoader TypeIdOf/KindOf.
Data: com_outpost.yaml. Runner: OutpostGate plus wiring. Client: ModelLibrary
kind 17 interim. Docs: this file, the tracker and ledger (ADR-021 landed with
the design PR).

**Assumed.** 15/second is the ratified rate (doc 22's reading); Balance owns it
under A11. Cost 500 exists for the schema and the classic sell-a-captured-
building half-refund (250), never charged. The capture team strip not updating on
owner flip is a pre-existing client limitation shared with captured factories
(doc 18 N6), not this wave's regression.

**Needed next (from whom).** tools + balance: place outposts on the four
skirmish maps (a map-design pass; the fairness invariant in tools/mapgen.py
should mirror them under the 180-degree rotation). art-pipeline: the bespoke
com_outpost model and a selection-readout name. ai-engineer: teach the AI to
send an engineer at a neutral outpost (it ignores them today; the game is
unbroken).
