# ADR-031: the engine counts seats
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (under the standing P7 directive, "work down the tracker on my own judgement, deciding rather than asking")
- GDD/TDD feature served: GDD s9 line 76, "Skirmish vs AI, 1-7 opponents"; doc 24 D2; P7-8a

## Context

Doc 24 calls D2 "the widest single divergence between what ships and what is
written down": GDD s9 promises skirmish against one to seven opponents and
custom lobbies up to four against four, and the game ships exactly two seats.

The row was blocked on a Producer decision phrased as "is D2's player-count
promise kept, or the GDD amended to match what shipped". It is decided here as
KEEP, and the reason is a distinction this phase has leaned on twice already:
GDD s9 writes its mode list unhedged, where line 62 writes the roster as
`**Directorate (sample):**`. P7-11b refused the Commando precisely because that
line is hedged. The same reading obliges the opposite answer here. Amending a
design document to match an incidental limitation is the weaker option when the
limitation was never designed, and this one was not: it is the residue of a
prototype that only ever needed two.

A survey of the tree changed the shape of the work substantially, and the
finding is worth recording because it inverted the expected cost. **The sim core
was already right.** `VictorySystem` is a last-one-standing rule over
`_players` with a per-seat announcement latch; the save format writes the seat
count and loops it; the LAN relay takes `playerCount` as a real parameter and
sizes every internal structure from it; and `SkirmishAI` holds only its own seat
and selects hostiles with "anyone who is not me and not neutral". None of that
needed touching. The two-player assumption lived in four places, and three of
them were in the client.

## Decision

### 1. GDD s9's two promises are two projects, and only one is taken here

**Free-for-all is reachable; teams are not.** There is no team field, no alliance
table and no `AreAllied` predicate anywhere in the sim. Hostility is decided
everywhere by the same test, "not me and not neutral". Introducing alliances
touches targeting, splash friendly-fire, detector sharing, fog sharing, victory
and the AI, and it has no code to build on at all.

So "1-7 opponents" (P7-8a, this ADR) and "4v4" (P7-8c, not taken and Producer-
blocked) are split. Shipping the first and calling D2 closed would be the more
flattering report and a false one.

### 2. The opening force leans towards the map CENTRE, not away from seat 0

This is the whole of why the change is hash-neutral, so it is the clause that
matters.

`PlaceSkirmishStart` mirrored the opening hand with `int side = p == 0 ? 1 : -1`
- player 0 lays out to the right, player 1 to the left. That is a binary, and a
binary has nothing to say about a seat 2. Read as a PROPERTY rather than as a
seat number, what it means is "lay the force out towards the middle of the map":

```csharp
int side = 2 * sx < Width ? 1 : -1;
```

On all eight committed skirmish maps start 0 sits left of centre and start 1
sits right of it, so this reproduces the old ternary **seat for seat on every map
that ships**, while being meaningful for a seat anywhere on a map with four or
eight of them. Verified against the maps before it was written, not after.

The three placement passes - every treasury, then every yard, then every opening
force - are deliberately left as three passes rather than folded into one loop
per seat. Spawn order is entity id order and entity ids are hashed, so the
rearrangement would play identically and hash differently, which is a replay
break for no gain.

Seats are walked over the world's seat range in ascending order and `Starts` is
only ever INDEXED, never iterated. Taking a `Dictionary` in its own order here
would be exactly the unordered-iteration determinism violation CLAUDE.md
forbids, and it would be invisible until it was not.

### 3. A seat count mismatch is a setup error that names both numbers

More seats than the map declares starts now fails before anything is spawned,
with a message giving both counts and the fix. It threw `KeyNotFoundException`
before, which names neither and reads as an engine fault rather than as "four
players were asked for on a two-player map". Spare starts are simply unused,
which is what lets an eight-start map host a three-player game.

### 4. The client reads the winner; it never infers one

The sim has always known the winner. The client reconstructed it, in three
places, by flipping a seat number:

```csharp
_winner = player == 0 ? 1 : 0;                                  // from the LOSER
if (_winner < 0 && _world.Winner >= 0) OnEliminated(_world.Winner == 0 ? 1 : 0);
```

With three seats this is not approximately wrong, it is exactly inverted: a
`Winner` of 2 calls `OnEliminated(0)`, which sets `_winner = 1`, so **player 1 is
shown VICTORY and the actual winner is shown DEFEAT**. No crash and no log, as
the last thing the match says.

`_winner` is now an absolute seat read straight off `World.Winner`, and a
separate `_matchOver` latch carries "is this match finished", which is what the
two were being conflated into. `OnEliminated` acts only when the eliminated seat
is the local one.

The related defect is that the client ended the match on the FIRST
`PlayerEliminated` event. The sim emits one per eliminated seat and keeps
playing until one is left; in a four-player game the first knockout ended
everybody's match. Another commander's elimination is now news, not an ending.

This class of bug has shipped in this file once before, is recorded in its own
comment at the site, and was caught then only by the headless harness at seat 1.
It was caught again the same way: three new harness checks assert that being
declared the winner reads VICTORY at seat 1, that being eliminated oneself reads
DEFEAT, and that another commander's elimination raises no banner and invents no
winner.

### 5. Seat colour is a table, and the CI guard stops teaching two players

`MarkFor` was `player == 0 ? DirectorateMark : SodalityMark`, so every seat above
0 rendered in one colour with no error: three enemies indistinguishable on the
battlefield and the minimap, looking entirely plausible. It is an eight-entry
table now, seats 0 and 1 byte-identical to the existing values.

The hardcoded-seat CI guard needed changing in the same wave, because **the guard
was part of the problem**: its remedy told authors to use `EnemyPlayerId`, which
IS `1 - LocalPlayerId`. Its regexes also keyed on the literals `[01]`, so
`PlayerId == 2` passed untouched, and the `== 0 ? 1 : 0` ternary form evaded it
entirely - which is precisely how the clause 4 defects survived a guard written
to catch them. Literals are `[0-9]+` now, a second grep catches the two
seat-flipping ternary shapes across all of `game/scripts/`, and the remedy no
longer recommends a two-player-only helper.

`EnemyPlayerId` is kept, because roughly ninety call sites use it and it remains
correct for the 1v1 case, but its declaration now records what it is.

## Alternatives rejected

**Amending the GDD to promise two players.** The honest option only if the limit
were designed. It was not.

**Deriving `side` from the seat's index modulo two, or from a facing declared
per start in the map format.** The first is the old binary wearing a hat. The
second is a format change to express something the map's geometry already says.

**Doing teams in the same wave.** It is a larger project than this one and would
have buried a hash-neutral change inside a hash-moving one.

**Fixing the client's inversion in a later wave.** Tempting, because no shipped
map declares more than two starts so the bug is not reachable from the menu
today. Rejected: the engine change is what makes it reachable, and shipping the
capability while leaving the code that misreports its result is how a latent
defect becomes a released one.

## Consequences

The engine plays free-for-all with any number of seats. **All 24 goldens are
byte-identical, measured**, and the gate asserts the neutrality directly rather
than leaving it to the golden file: a two-player skirmish-01 still hashes
`0x944F9440A28B59FB` at placement and `0x6DB7E79AAD62EFAD` 600 ticks on, values
taken from `main` before the change.

The client harness grew from 128 checks to 130.

What this does NOT deliver, stated so the row is not read as finished:

- **No shipped map can host a third player.** All eight declare exactly two
  starts, so every multi-seat map is new content (P7-8b). `tools/mapgen.py`
  assumes 180-degree rotation throughout - `rot()` itself, and the "a cell and
  its one image" pairing every mutator uses - so a fair four-start map needs the
  pair generalised to a symmetry ORBIT. Note that 90-degree rotation also
  requires a SQUARE map and none of the eight are square, so a double mirror is
  the likelier group. `data/maps/test-4seat.fmap` exists as a TEST FIXTURE only,
  carries no fairness proof, and is excluded from the menu's map list.
- **The lobby cannot express a third seat.** `MatchSetup` has one `OppFaction`
  field, commented "player 1", and both codecs that carry it (the save sidecar
  and the LAN blob) are shaped for two. The relay itself is already generic; the
  two call sites that pin it to `playerCount: 2` are a one-line change once the
  lobby can ask for more.
- **`SkirmishAI` picks the first enemy refinery in entity order, not the
  nearest.** With one opponent those are the same thing; with three, waves and
  superweapon strikes will preferentially hit whichever player sits earliest in
  the entity array, which looks like the AI inexplicably focusing one player. It
  is a quality defect, it is hash-moving, and it is left to its own wave.
- **`DetectedMask` is a `byte`, so eight seats is a hard ceiling** - exactly the
  GDD's maximum of you plus seven, with zero margin. A ninth seat shifts out of
  the byte and that player's detectors silently stop revealing stealth. It is
  hashed state, so widening it later is expensive. Recorded here rather than
  discovered later.
