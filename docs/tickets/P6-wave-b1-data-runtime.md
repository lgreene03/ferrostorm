# P6 Wave B1 delivery notes: /data becomes the runtime source

TICKET-P5-DATA-01 (doc 23 Wave 3), implementing ADR-006 as ratified. Plan
comment first (CLAUDE.md workflow rule 2), delivery notes and the standard
footer at the end.

## Plan

labels: persona:P4 gdd:TDD-s11 phase:6 owner:architect + client-engineer

Approach, in the ADR's own order of commitments.

The sim gains a catalogue checksum: FNV-1a in the sim's existing StateHash
idiom, computed over the canonicalised registered defs, never over file
bytes. Canonical means: unit types first, then structure types, each walked
in ascending type id (a sorted key list, because dictionary order must never
leak into an artefact), each def contributing every field in declaration
order, prerequisite lists length-prefixed. It is a property on World
(CatalogueChecksum) because it answers for THIS world's registered
catalogue, compiled or overridden, and it is deliberately not part of
ComputeStateHash: the ADR rejects hashing the catalogue into state because
that moves all 24 goldens for zero behavioural change.

The loader idiom becomes a callable. The runner's load path at
Program.cs:1582-1662 is the reference implementation; its walk (sorted
ordinal, register through TypeIdOf and ToTypeDef, a duplicate claim refused,
every compiled type demanded so a partial /data cannot silently mix the two
catalogues) moves into CatalogueFiles.RegisterAll in the sim's DataLoader.cs
so the client and the gate exercise one implementation. Errors are readable:
a missing directory says /data is missing and what was expected; a parse
failure names the file and carries the parser's line number; a missing file
names the compiled type it was meant to provide, because the compiled
catalogue is not a fallback (a silent fallback would resurrect the
two-catalogue ambiguity the ADR exists to end).

The client loads /data before tick 0. MapData.BuildWorld gains an optional
configure hook invoked after the World is constructed and before any spawn,
because mission maps spawn tagged units and structures whose stats are read
from the catalogue at spawn time; registering after BuildWorld would hand
mission content compiled stats under an edited /data. BuildStartingWorld
passes SkirmishLive.RegisterCatalogue (GameFiles.RepoRoot resolved, exactly
the pathing idiom the ADR names) in both its branches. Any failure lands in
a try/catch around the fallible half of _Ready: the message goes to a static
notice slot on MainMenu, the scene change back to the menu is deferred, and
a guard flag keeps _Process and _UnhandledInput quiet for the frames in
between. MainMenu shows the notice in the existing overlay chrome with an
UNDERSTOOD button; the menu behind it stays fully usable.

The save format goes to v3 in the established magic idiom: SaveMagicV3
follows V1 and V2, the catalogue checksum is written immediately after the
magic, and Load accepts all three. v1 and v2 have no checksum and their
absence means do not check, never refuse. Load gains an optional
registerCatalogue callback invoked while the loaded world's Tick is still 0,
honouring RegisterUnitType's own Tick != 0 guard rather than bypassing it;
the recorded checksum is then asserted against the world's, and a mismatch
refuses with a readable message naming both checksums. The client's
ResumeFromSave passes RegisterCatalogue, so a resumed save plays the
catalogue it was recorded against or does not play at all.

Replays: the .frep format is versioned by its header line, so the header
advances to v3 and the writer records a catalogue line when given a
checksum. The reader accepts v2 and v3; a missing catalogue line means do
not check. Replay.AssertCatalogueMatches carries the refusal so the client
and any future harness refuse identically; BeginPlayback calls it against
the freshly built world before a single tick plays back.

The LAN hello: Wire gains Check (client to relay, the client's checksum
after its world is built), Start and Refuse (relay to clients). The relay
gathers one Check per player before starting its pumps; all equal starts the
game, any difference sends Refuse carrying both values and the relay stands
down. LockstepClient sends its checksum inside the constructor and blocks
for the verdict, so a mismatch refuses before tick 0 by construction; the
refusal is an InvalidDataException naming both checksums. The lan, lanchaos
and in-process smoke paths all construct LockstepClient, so all three
exercise the handshake with equal checksums and must pass unchanged.

The gate: a new catrefuse mode, also run inside Match so the full battery
and CI exercise it. It proves the checksum is stable across worlds and
sensitive to a single def change; that two lockstep clients with one bumped
def are refused with both checksums named while the relay records a refusal
and no desync; that a save carrying a foreign checksum refuses with both
checksums named while the same bytes with a v2 magic and no checksum still
load; that a v3 replay round-trips its catalogue line, refuses a foreign
checksum and tolerates a v2 stream with none; that RegisterAll on the real
/data produces a checksum identical to the compiled catalogue's (the ADR's
hash-impact argument, asserted rather than assumed); and that the three
readable error shapes (missing /data, parse failure with file and line,
missing file for a compiled type) all land as messages.

The sidebar stops carrying prices. Its static tables held a second copy of
every cost, which under a runtime /data would show the player compiled
prices while the sim charged authored ones. The BuildItem cost column goes;
labels read the live catalogue through the existing delegate for structures
and a new unit-cost delegate beside the build-ticks and faction ones.

Assumptions. GameFiles.RepoRoot remains the development-build answer to
pathing; packaged distribution stays with the release ticket, per the ADR's
consequences section. The compiled catalogue remains the selftest's
round-trip truth and the zero-dependency default for harness callers that
never touch disk; nothing removes it. Hash impact none: the runner already
loads /data, the values are equal at HEAD by selftest guarantee, and the 24
goldens must be byte-identical, which the battery proves.

Interfaces touched: World (new property), World.Serialization (v3),
Replay (v3 + assert), DataLoader (CatalogueFiles), MapLoader (configure
hook), Lockstep (Wire/Relay/LockstepClient handshake), Program.cs (gate +
mode + ReplayCheck carries the checksum), SkirmishLive (registrar, startup
try/catch, recording/playback/resume wiring, verification hooks), Sidebar
(live prices), MainMenu (refusal notice).

## Delivery notes

Shipped against the ADR's decision and all three subsidiary commitments.

Commitment 1, the checksum. World.CatalogueChecksum is FNV-1a over the
canonicalised registered defs (sorted type ids, declaration-order fields,
length-prefixed prerequisite lists), computed by the sim and never over file
bytes. It rides the LAN hello (Wire Check/Start/Refuse; the LockstepClient
constructor blocks for the verdict, so a mismatch cannot reach tick 0 by
construction), the save format (v3, checksum after the magic) and the replay
format (v3 header, catalogue line). Refusal names both checksums on every
surface. v1/v2 saves and v2 replays carry no checksum and are never refused:
the catrefuse gate performs byte surgery on a v3 save to make a v2 one and
proves it loads under a foreign catalogue, and loads a hand-built v2 replay
the same way.

Commitment 2, failures as messages. The runner's loader idiom became
CatalogueFiles.RegisterAll in the sim, so the client and the gate walk ONE
implementation: missing /data says so and names the expected directories, a
malformed file is named with the parser's line, a missing file names the
compiled type it should provide, and a duplicate claim is refused. The
client wraps the fallible half of _Ready; any failure lands as a BATTLE
REFUSED overlay on a fully usable menu.

Commitment 3, the compiled catalogue demoted, not deleted. It remains the
selftest round-trip truth and the default for harness callers that never
touch disk. It is NOT the fallback for a broken /data: RegisterAll demands
every compiled type be authored, so a partial /data refuses rather than
silently mixing the two catalogues.

The client loads /data before tick 0 in BuildStartingWorld, resolved through
GameFiles.RepoRoot, via a configure hook on MapData.BuildWorld that runs
before ANY spawn (see finding 3). ResumeFromSave passes the registrar into
World.Load's tick-0 window (finding 4). BeginRecording stamps the checksum
into every fresh .frep; BeginPlayback asserts it before a tick re-simulates.
The sidebar's hardcoded price tables are gone; every price is a live
catalogue read (finding 2).

The gate: catrefuse, additive (not a golden scenario), run standalone and
inside the battery's match stage, covering the LAN-hello refusal with both
checksums named on both clients and no desync, the save and replay refusals,
the backwards-compatibility paths, the three readable error shapes, checksum
stability and sensitivity, and the ADR's hash-impact argument asserted: the
real /data registers to a catalogue whose checksum equals the compiled one
(0xF7ED8ABE932D6C46 at HEAD).

### Verification evidence

Full battery exit 0 (selftest, determinism, match including defence and
catrefuse gates, lan 5/5). Goldens: `git diff sim/golden-hashes.txt` empty
and a golden-mode diff against the committed file byte-identical, 24/24.
Also green standalone: spectate, replay, saveload, campaignsave, lanchaos,
and the balance gate. Purity and portability greps clean. Both client
builds (Debug and ExportRelease) at zero warnings; project.godot survives
byte-identical.

Offscreen client run: a temporary autoload (removed before commit, the
established pattern) drove the REAL menu, battle, save, replay and refusal
paths in two phases, 26/26 checks. Phase 1 ran with COPY-edited data
(dir_cannon_tank cost 600 to 650, com_power_plant 300 to 350): the live
skirmish's sidebar priced both edits (button text "650" and "350"), the live
sim's defs carried both, the live catalogue checksum differed from the
compiled one (proof the registration actually drove the match), and a plant
queued through the real sidebar path drained the treasury by EXACTLY 350
over its build and then stopped, the ADR's own acceptance made end to end.
The phase saved slot 4 (verified v3 on disk, carrying the live checksum) and
recorded a replay on the way out. Phase 2 ran with /data restored
byte-identical (`git diff data/` empty): the slot 4 save REFUSED with both
checksums named in the menu notice; the recorded replay REFUSED the same
way; a deliberately malformed YAML refused naming com_rifle_squad.yaml and
line 1; a hidden com_mcv.yaml refused naming compiled unit type 7; the menu
survived every refusal, dismissed each overlay through its own button, and
then started a clean battle whose button priced the pristine 600 and whose
live checksum matched the compiled reference. Slot 4 was confirmed empty
before use and cleaned after; the test replay was deleted; the user's own
saves and recordings were untouched.

### Findings

1. REAL BUG, caught by the new gate before it shipped: the first cut of the
   v3 loader kept the faction-array read conditioned on `magic ==
   SaveMagicV2`, so a v3 save silently misaligned its stream (the corrected
   condition is `magic != SaveMagicV1`). Every future format bump has to
   touch that line's condition or inherit the same bug; the catrefuse gate's
   round-trip now fails loudly if it regresses.
2. The sidebar's static tables carried a second copy of every price. Under
   a runtime /data the buttons would have shown compiled numbers while the
   sim charged authored ones. The cost column is deleted; prices read the
   live catalogue delegates.
3. MapData.BuildWorld spawns mission-tagged units and structures reading
   the catalogue AT SPAWN TIME, so registering /data after BuildWorld would
   hand mission content compiled stats under an edited /data. Hence the
   configure hook that runs before any spawn.
4. World.Load builds its world with the compiled catalogue and its restored
   Tick forbids later registration, so without the registrar callback a
   resumed save would SILENTLY play compiled values, resurrecting the
   two-catalogue ambiguity on the resume path. The callback runs inside the
   tick-0 window, honouring the sim's own guard.
5. Relay hardening forced by this wave: once BuildStartingWorld can throw
   (broken /data), a LanSmoke client's factory failure would have left the
   relay's handshake read throwing on a background thread, taking the whole
   client process down. The relay now stands down quietly when a client
   dies mid-hello.
6. The lan, lanchaos and in-process smoke paths all construct
   LockstepClient, so all three exercise the new hello implicitly; no gate
   text needed extending, and all passed unchanged.
7. Deliberately NOT taken: Main.cs (the debug loopback scene) still builds
   its hand-spawned world off the compiled catalogue. It is a harness-style
   caller, not a battle the menu can reach; wiring it is not this ticket's.

## Changed / Assumed / Needed next

Changed: sim/Ferrostorm.Sim (World.cs catalogue checksum;
World.Serialization.cs save v3 with registrar and refusal; Replay.cs v3
with catalogue line and AssertCatalogueMatches; DataLoader.cs
CatalogueFiles; MapLoader.cs configure hook), sim/Ferrostorm.Net/Lockstep.cs
(Check/Start/Refuse handshake, relay hardening, CatalogueRefused),
sim/Ferrostorm.Sim.Runner/Program.cs (catrefuse gate and mode, battery
wiring, ReplayCheck carries the checksum), game/scripts (SkirmishLive
registrar and startup refusal path, Sidebar live prices, MainMenu refusal
notice), this document, the campaign tracker and the ledger. Goldens
byte-identical; full battery exit 0; both client builds clean at zero
warnings; git diff data/ empty.

Assumed: GameFiles.RepoRoot stays the development-build pathing answer, with
packaged distribution left to the release ticket the ADR names. "The
runner's exact loader idiom" is read as the idiom made shared rather than
copied, so the gate and the client cannot drift. Slot 4 was a legitimate
scratch slot for verification because it was empty before and after.

Needed next (from whom): the packaged-build /data pathing decision from the
release ticket's owner when packaging starts (ADR-006 consequences); a
Main.cs decision from the client-engineer if the debug loopback scene should
also read /data (finding 7); Wave B2 (rally into the sim) may begin on this
base per the tracker's sequencing.
