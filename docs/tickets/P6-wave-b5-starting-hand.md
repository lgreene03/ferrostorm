# P6 Wave B5 delivery notes: the starting hand enters the sim

Closes the B5 row of the P6 campaign tracker under ADR-011 (ratified), which
resolves docs/questions/Q005-starting-hand-lives-outside-the-sim.md. One golden
regeneration, authorised by the ADR's hash-impact clause and explained row by
row below, with the neutralisation and the missions-did-not-move proofs the
sim waves have held since B2. Plan comment first (CLAUDE.md workflow rule 2),
delivery notes and the standard footer at the end. This is the last Phase B
wave: with it landed, every B row (B1 through B6) is DONE and Phase B is
complete.

## Plan

labels: persona:commander gdd:s3(tdd) phase:6 owner:sim-engineer + client-engineer + architect (Balance co-sign on the centring, A11)

**The layer is the ADR's, exactly.** The opening hand every real skirmish begins
with (start credits, a construction yard per player at the map's start cells,
one harvester and three rifle squads per player, mirrored) moves out of the
client's SkirmishLive.BuildStartingWorld and into the sim's MapLoader layer, as
a builder on MapData that takes the treasury as a parameter (ADR-011 clause 1).
The runner's gated skirmish scenario calls the same builder with its unchanged
8000-credit treasury (clause 2), so from here on the golden covers the world
players actually play rather than a bare two-yard world nobody plays. The
mission branch is out of scope (clause 4) and is untouched.

**The CellCentre choice is decided in this same change (clause 3).** Moving the
hand into MapLoader forces a placement convention to be written either way. The
client authored the harvester and squads with Fix64.FromInt cell corners, while
every spawn path inside the sim uses Map.CellCentre. Carrying the corners into
the sim would silently decide for the corners, so centres are written here,
ending the corner-versus-centre split Q005 found. This is outcome-shifting, so
Balance co-signs: Q005's instrumented measurement moves the skirmish-04
idle-player defeat 550 ticks earlier. That shift is expected and authorised, a
report rather than a defect, and it is recorded for Balance below.

**The builder is faction-neutral.** The hand is common hardware only, so
PlaceSkirmishStart neither reads nor writes the sides. The runner leaves
factions at the default and the client sets the player's menu choice
(TICKET-P6-FACTION-01) before the call, so the entity-creation order and the
faction handling are unchanged from the branch this replaces.

Assumptions: the builder takes an already-built World and adds the hand to it,
rather than calling BuildWorld itself, so the client keeps its own BuildWorld
call with the /data catalogue registrar and the faction lines and the runner
keeps its bare two-argument BuildWorld; two-player skirmish only, exactly as the
branch it replaces was (Starts[0] and Starts[1] are indexed as before); the
treasury parameter is a long, matching World.GrantCredits, and both the client's
setup.StartCredits and the runner's 8000 pass through it.

Interfaces touched: sim/Ferrostorm.Sim (MapLoader.cs: the new PlaceSkirmishStart
on MapData), sim/Ferrostorm.Sim.Runner/Program.cs (BuildSkirmishWorld defers to
it), game/scripts/SkirmishLive.cs (BuildStartingWorld's skirmish branch shrinks
to the single call), sim/golden-hashes.txt (the wave's one regeneration), docs
(the campaign tracker, the phase-1 ledger, Q005's RESOLVED note, this file).

## Delivery notes

Shipped in three code commits in load-bearing order: the sim starting-hand path
and the CellCentre placement (a9e622f), the client deferring to it (49b279f),
the one golden regeneration (e814e10); docs ride last.

**Part 1, the sim starting-hand path and CellCentre.** MapData.PlaceSkirmishStart
grants both players the treasury it is handed, spawns a construction yard per
side at Starts[0] and Starts[1], and mirrors the classic opening force of one
harvester and three rifle squads each around those cells. The mobile units are
placed at Map.CellCentre, so a harvester lands at the .500 coordinates of every
other sim spawn rather than the client's old integer corners; the construction
yard is centred by its own spawn function as it always was. BuildSkirmishWorld in
the runner drops its inline credits and yards and calls PlaceSkirmishStart with
8000, so the gated scenario builds the real opening hand.

**Part 2, the client defers.** SkirmishLive.BuildStartingWorld's skirmish branch
sheds the ten lines that granted credits, spawned the two yards and authored the
hand, and calls the same MapData.PlaceSkirmishStart. The faction lines stay above
the call, unchanged: they are the player's menu choice and the hand is
faction-neutral, so the builder stays out of them. The mission branch is
untouched. The client's BuildStartingWorld is still the one static, scene-free
function that a fresh match, a replay playback and offscreen verification all
build the identical world from, because the shared builder is deterministic and
pure.

**Part 3, the one golden regeneration.** The skirmish golden moves from
0x4F6252B168468346 to 0x3CE6E400F07A3AA1 and no other row moves.

**The starting hand, proved offscreen.** A throwaway console referencing only the
sim library (no engine) built skirmish-01 through the shared PlaceSkirmishStart,
exactly as the client's BuildStartingWorld now does, and enumerated the tick-0
world. Each side received one construction yard, one harvester at 700 hp and
three rifle squads at 100 hp carrying weapon 2, over the map's twenty ferrite
fields, thirty entities in all. Player 0's harvester sat at (12.500, 11.500) and
its squads at (11.500, 7.500), (12.500, 7.500) and (13.500, 7.500), the .500
cell centres; player 1's were the mirror at (83.500, 56.500) and y 52.500. The
old client corners would have read (12.000, 11.000) and so on, so the centring
is visible in the coordinates. Because the client now calls this identical
method, a live skirmish still starts with two bases, a harvester and three
squads per side, at the right positions, and the battery's skirmish golden
scenario now exercises that exact opening hand, which is the whole point of the
wave.

**The regeneration, explained row by row.** Exactly one of the twenty-four
goldens moves:

- skirmish (0x4F62...346 to 0x3CE6...AA1): the full AI-vs-AI match on skirmish-01
  now begins from the real opening hand rather than a bare two-yard world. The
  gated scenario's world gains a harvester and three rifle squads per side and
  those units sit at cell centres instead of corners, so the final state moves.

The other twenty-three rows are byte-identical. Every micro-scenario (movement,
pathing, economy, combat, production, attackmove, construction, stealth,
veterancy, victory, expansion, artillery, superweapon, crush, aisuper, veil,
waypoints, depot, walls) builds its own bespoke forces and never touches the
skirmish builder, so none can move. The four mission-family rows (mission,
mission02, mission03, capture) build from map data and scripted triggers, also
outside the builder, and hold. Nothing moved unexplained; nothing explained
failed to move.

**The missions did not move, proven two ways.** The golden 2026 diff shows
mission, mission02, mission03 and capture unmoved, and git diff data/missions is
empty. The change touches no map file and no mission code path.

**The neutralisation proof.** On a scratch build reverting BuildSkirmishWorld to
the old no-starting-hand construction (the 8000 credits and the two yards only,
no hand), a fresh golden 2026 reverts all twenty-four rows byte-for-byte to the
pre-wave hashes, the skirmish row back to 0x4F6252B168468346. So the starting
hand plus the centring is the sole cause of the one moved row, and the
twenty-three non-movers, the missions included, are untouched by the change.

**Reports that shift, not gates.** The in-client two-client LanSmoke prints a new
final hash (its recorded pass value 0x4EC4DA95C8D7F31A dated from the
corner-placed hand); the runner's replay and saveload gates print new report
hashes (0x745067B91DD2F3D3 and 0x0ABC7189B58DE07A) because BuildSkirmishWorld
now seeds those round-trips with the hand, though each still proves its own
bit-exact internal consistency; and the balance journal's skirmish-04 tick
numbers move with the 550-tick shift. These are reports, not gates, exactly as
ADR-011's consequences anticipated.

**For Balance (A11 co-sign).** The centring is outcome-shifting and authorised.
Q005's instrumented measurement: centring the hand moves the skirmish-04
idle-player defeat from tick 4192 to tick 3642 and the starting-unit wipe from
tick 3458 to tick 2719, the same winner 550 ticks earlier. That is expected and
part of why the skirmish golden moves; it is recorded here for Balance rather
than treated as a defect, and the AI-vs-AI match still resolves in bounded time
on the shared world (below).

### Verification evidence

Full battery exit 0: the default no-args run (selftest with all three catalogue
round-trips, determinism 24/24 double-run identical at seed 2026 with the
skirmish scenario now at 0x3CE6E400F07A3AA1, match with every scenario assertion
plus the defence load gate at 1.673 ms/tick, catrefuse, spawngate, prodgate,
regrowthgate, lan 5/5). Zero FAIL lines and zero unhandled exceptions across the
run.

The exact CI golden check passes locally: golden 2026 diffed byte-for-byte
against the comment-stripped sim/golden-hashes.txt is identical. The sim purity
grep is clean (no float, double, System.Random or Godot in the sim library) and
the portability grep is clean (no engine reference in sim, tools or data).

Standalone gates green: replay (a 3000-tick AI match reproduced bit-exactly),
saveload (player 1's Sodality faction survived the round trip; the mid-match save
resumed to the uninterrupted final hash bit-for-bit), campaignsave (mission save
and message log resumed identically, its hash unchanged because it is a mission),
spectate, lanchaos (3 games, zero desyncs), and the balance gate (VERDICT PASS;
the reported 0-6 faction war is the pre-existing TICKET-AI-05 note, unchanged
from B6, which confirms the balance tool builds its own worlds and not through
the changed path).

AI-vs-AI resolves on the shared world: the skirmish scenario ran the complete
loop from the real opening hand, bases built, harvesting live, 37 entities
destroyed, treasuries 2 and 1398, and the match ended in bounded time; the replay
gate ran a full 3000-tick AI match bit-exactly and the five lan games each
completed with zero desyncs. The starting hand changes the opening without
starving the AI or preventing the game from ending.

Both Godot client builds succeed at zero warnings (Debug and ExportRelease);
project.godot is untouched. No user saves, replays or settings.cfg were written:
the gates use temporary files and in-memory streams only. game/scripts/FogOfWar.cs
and Minimap.cs were not touched (the concurrent local session owns them).

Q013 note: the nightly-soak determinism at seed 900913 fails on an unrelated
crowd-pathing convergence issue in ScenarioPathing that predates this wave
(docs/questions/Q013). The failure is thrown in ScenarioPathing before the run
ever reaches the skirmish scenario, and this wave touches only the skirmish
builder, which ScenarioPathing never calls, so 900913's ScenarioPathing failure
is Q013, not a B5 regression. This wave's determinism at seed 2026 passes 24/24.

## Changed / Assumed / Needed next

**Changed:** sim/Ferrostorm.Sim (MapLoader.cs: MapData.PlaceSkirmishStart, the
skirmish opening hand at cell centres), sim/Ferrostorm.Sim.Runner/Program.cs
(BuildSkirmishWorld defers to the shared builder with its unchanged 8000
treasury), game/scripts/SkirmishLive.cs (BuildStartingWorld's skirmish branch
shrinks to the single PlaceSkirmishStart call; the faction lines stay above it),
sim/golden-hashes.txt (the wave's one regeneration, the skirmish row, explained
above and proven by neutralisation), docs (the campaign tracker set to DONE with
Phase B noted complete, the phase-1 ledger line, Q005's RESOLVED note, this
file).

**Assumed:** the builder takes an already-built World and adds the hand rather
than calling BuildWorld itself, so the client keeps its catalogue registrar and
faction lines and the runner keeps its bare BuildWorld; two-player skirmish only,
exactly as the replaced branch was; the treasury parameter is a long matching
GrantCredits; the construction yard's own centring is left to its spawn function
as before, so only the mobile units carry the CellCentre change.

**Needed next (from whom):** Balance (balance agent) owns the acceptance of the
550-tick outcome shift under A11 and may wish to re-tune the opening once the
centred hand is play-tested; the shift is a report here, not a value to gate on.
The client-engineer may want to retire the two-client LanSmoke's recorded pass
value in the P5-SET-01 ledger prose, now that its printed hash has moved with the
hand; it is a report, not a gate, so it is left as historical prose. The Architect
may note that the skirmish golden now covers the shipped opening hand, so the next
inconsistency of the corner-versus-centre kind cannot hide, and that C1's unit
command layer inherits a sim-authored opening rather than a client-authored one.
