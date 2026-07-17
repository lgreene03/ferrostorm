# ADR-011: The starting hand enters the sim

- Status: Proposed (authored 2026-07-17; awaiting ratification by Luke)
- Date: 2026-07-17
- Deciders: Architect agent + Luke, plus Balance co-sign on the placement
  centring (outcome-shifting, measured in Q005 and restated below)
- GDD/TDD feature served: ADR-001's core principle, the sim is the authority
  on gameplay; TDD s3 (all match state lives in the deterministic core);
  resolves docs/questions/Q005-starting-hand-lives-outside-the-sim.md
- Numbering note: the open queue held ADR-011 for the Lua trigger sandbox (a
  Phase 3 decision with no draft). Per the queue's own ADR-004 precedent the
  number is taken here and the sandbox topic is re-queued as ADR-013 in
  ADR-open-queue.md in the same commit, so the reservation stays greppable.

## Context

The opening hand every real skirmish begins with is authored in client code
and is covered by no golden hash. Q005 found it; the facts below are
re-verified at HEAD (510af33).

- `SkirmishLive.BuildStartingWorld` (game/scripts/SkirmishLive.cs:398-436)
  grants the menu treasury, spawns two construction yards, then spawns the
  actual starting force: one harvester and three rifle squads per player,
  mirrored around the map's start cells.
- The runner's `BuildSkirmishWorld` (sim/Ferrostorm.Sim.Runner/
  Program.cs:464-476), which is what the gated `skirmish` scenario runs,
  grants 8000 credits a side and spawns two construction yards and NOTHING
  ELSE.
- `MapLoader.BuildWorld` deliberately places no starting forces; its own
  header says "the scenario places starting forces at Starts"
  (MapLoader.cs:146).

So the sim's own gate and the client disagree about what a skirmish start
IS, and the golden `skirmish` hash proves the determinism of a world no
player ever plays.

The hole has already hidden a real inconsistency. The client places its
starting units with `Fix64.FromInt` (cell corners, measured at exactly
(14.000, 18.000)), while every spawn path inside the sim uses
`Map.CellCentre` (MapLoader.cs:187-188 for map-authored units,
World.cs:1925-1926 for everything production builds, hence the .50
coordinates on every AI unit). Nothing caught the divergence because nothing
hashes the client's hand. And on ADR-001's own terms the hand sits in the
wrong layer: a starting force is gameplay, not presentation, and ADR-004's
renderer-swap plan turns any reimplementation drift into a desync between
clients rather than a cosmetic difference.

## Decision

1. **The skirmish starting hand moves into the sim's MapLoader layer.** The
   opening spread that `SkirmishLive.BuildStartingWorld` authors today
   (start credits, a construction yard per player at the map's start cells,
   one harvester and three rifle squads per player, mirrored) becomes a
   MapLoader-layer builder that takes the treasury as a parameter. The
   client's skirmish branch shrinks to a single call; the exact signature is
   the implementing ticket's, the layer is this ADR's.
2. **The gated `skirmish` scenario calls the same builder.** The runner
   keeps its 8000-credit treasury, so the golden's movement is attributable
   to the hand and the centring alone, and from then on the golden covers
   the world players actually play.
3. **Placement is `Map.CellCentre`, decided here, in the same change.**
   Moving the hand into MapLoader forces a placement convention to be
   written either way; carrying the client's `Fix64.FromInt` corners into
   the sim would silently decide FOR the corners. Centres are the convention
   of every other spawn path in the sim, so the inconsistency Q005 found
   ends here rather than being enshrined. This clause is the reason Balance
   co-signs (see Consequences).
4. **Mission starts are out of scope.** The mission branch already builds
   from map data and triggers; its two per-mission grants stay where they
   are and can follow through their own decision if it ever matters.

## Alternatives rejected

**Status quo.** The golden `skirmish` hash proves a world nobody plays,
which is a test-coverage hole in the one place this project is most careful,
and it has already cost something real: the corner-versus-centre
inconsistency hid in it undetected. Keeping the hand in the client also
keeps gameplay authored in the presentation layer, which ADR-001 exists to
forbid.

**The client stays authoritative and the runner mirrors a copy of the
hand.** Two sources of truth is the disease, not the cure. The mirror drifts
silently in exactly the way the corner/centre split already demonstrated,
every future client (ADR-004's renderer swap included) reimplements the hand
from scratch, and any divergence lands as a desync rather than a visible
bug.

## Consequences

**Hash impact: MOVES the `skirmish` golden hash.** This is a
replay-compatibility break under CLAUDE.md's hash law; this ADR, once
ratified, is the required ADR and Architect sign-off. One regeneration: the
gated scenario's world gains a harvester and three squads per side and its
units move to cell centres. The other 23 rows must be byte-identical in the
same diff, and the implementing ticket explains any row that moves beyond
`skirmish` rather than regenerating blanket (ADR-009's rule: a blanket
regeneration is a rejection).

**The centring is outcome-shifting, so Balance co-signs.** Q005's
instrumented measurements: centring the client's hand moves the skirmish-04
idle-player defeat from tick 4192 to tick 3642 and the starting-unit wipe
from 3458 to 2719. Same winner, 550 ticks earlier. That is a
balance-affecting change wearing the clothes of a consistency fix, and it
does not land on the Architect's signature alone.

**Q005's bundling warning, answered rather than ignored.** Q005 asked for
the move and the CellCentre fix NOT to be bundled, so the hash movement
could not pass as an unexamined side effect of a tidy-up. The centring
nevertheless must be decided in the same change, because clause 3's
observation stands: writing the hand into MapLoader chooses a convention
either way, so the choice cannot be deferred, only hidden. This ADR is the
examination Q005 wanted; the decision is explicit, measured, and co-signed
instead of incidental.

**Reporting values shift; goldens beyond `skirmish` do not.** The in-client
two-client smoke prints a new final hash (its recorded pass value
0x4EC4DA95C8D7F31A dates from the corner-placed hand), and measured tick
numbers quoted in balance journals move with the 550-tick shift. Those are
reports, not gates.

**What becomes easier.** The gate battery and the shipped game finally test
the same opening world; the client sheds gameplay authorship; a future
renderer swap inherits the hand for free; and the next inconsistency of the
corner/centre kind cannot hide, because the hand is hashed.

Gates: the implementing ticket must not start before ratification, and its
regeneration commit carries the before/after `skirmish` hash plus the
23-rows-identical diff as evidence.
