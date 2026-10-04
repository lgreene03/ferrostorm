# Q023: does each faction want an anti-air defence structure?

Owner: game-designer
Raised by: decision D30 of docs/tickets/P8-formidable-tracker.md (2026-10-02),
which cited this file as filed. It was not, and the Architect's retrospective
review of ADR-072 (2026-10-03) found the dangling reference. Filed 2026-10-04.
Decide by: after the first playtest that raids a base with Strike Flyers (the
air check in docs/tickets/P7-playtest-brief-2026-08-03.md, or its P8
successor). Not blocking P8: D30 keeps the status quo until then.

## The question

GDD s6 line 55: *"Limited-count strike aircraft (airfield-slot model) plus a
transport helicopter each. Air is a scalpel, not an army."*

The GDD names the aircraft and says nothing about what answers them from the
ground. ADR-028 shipped the air layer with one answer, the common Flak Track,
a mobile unit. **No structure in the game can shoot an aircraft**: ADR-028
clause 3 makes every weapon either ground-only or air-only, and every defence
building carries a ground weapon (the common turret and emplacement, the
Directorate Bastion and the Sodality Shroud Nest). So a base can be fortified
against everything except air.

Does each faction want an anti-air defence structure? If so, one common
building or one per faction?

## Context

**What answers air today.** The Flak Track: 550 credits, produced at the
factory, behind the Radar Uplink, and armed with a weapon that cannot shoot the
ground (ADR-028 clause 3, which was written that way because a flak track that
could also fight tanks was a straight upgrade rather than a counter). Nothing
else. ADR-028's own context says the benchmark games used "the anti-air
building that answers" strike craft, and the wave that shipped air shipped the
mobile half only.

**What D30 decided for P8.** No anti-air structure is built in P8, because the
commander's flak doctrine closes the exploit that made air urgent. AI-01
measured three Strike Flyers killing 5 to 9 of a commander's harvesters with
none shot down; under ADR-072 the same raid is shot down within about 400 ticks
at a cost of at most one harvester (aiairgate, binding), and the new golden
`airanswer` hashes a commander building Flak Tracks and shooting a flyer down.

**What that leaves open.**

- **The commander does not fly** (D10), so in single player only the AI is ever
  raided from the air. A human meets enemy air only in a LAN match, and answers
  it only with Flak Tracks.
- **An air rush before the radar has no answer at all.** ADR-072 measured a
  raid at t=1200, before the commander owned a radar: it lost every harvester,
  because a radar, a factory slot and a 130-tick build cannot be had in time. A
  human is in the same position.
- **The answer has a price paid in the ground war**: four Flak Tracks (2200
  credits) and whatever production they cancelled (ADR-072 Consequences).
- **Every faction defence is ground-only**, and P7-18 (ADR-060) made each side
  defend with its own hardware. A per-faction anti-air building would extend
  that asymmetry; a common one would not.

**Why this is a question and not a ticket.** A new structure is a roster
addition. It needs a name, a doctrine and a place in the tech tree that no
document has written, which makes it the Game Designer's call, with Producer
sign-off for new content (CLAUDE.md, Scope).

## Options

1. **No anti-air structure (the status quo, D30).** Air stays a scalpel that a
   mobile answer meets, and defending against it is a choice of army
   composition rather than of base layout. No roster, art, AI or balance work.
   The cost: a base cannot be fortified against air, which departs from the
   genre's convention, and an air rush before the radar stays unanswerable.
2. **One common anti-air emplacement**, beside the common turret, behind the
   Radar Uplink as the Flak Track is, its numbers derived from the Flak Track's
   own weapon by the method that let the support powers ship (CLAUDE.md, Data
   conventions). The smallest addition that gives a static answer: one def, one
   AI ladder rung, one balance surface. The cost: both factions defend against
   air identically, against the asymmetry ADR-060 built.
3. **One per faction**, matching the faction defences: a Directorate hardened
   site and a Sodality structure in its deceptive idiom. The most identity, and
   the most work: two defs, two pieces of art, two ladder rungs and a balance
   pass, and Producer sign-off for the roster growth.
4. **An anti-air mode on an existing faction defence.** Rejected in advance as
   the weakest option: ADR-028 clause 3's equality rule means a building that
   shoots both would need two weapons, and a structure carries one, so this is
   more machinery than option 2 for less clarity.

## Recommendation

**Keep option 1 for P8, and decide between 1 and 2 on the playtest's air check.**
The brief now asks a player to raid a Normal commander three times with three
Strike Flyers once its radar stands, and to try an air rush against a human
base. Take option 2 if the rush on a human base before the radar is found to be
unanswerable, or if a player says a base cannot be defended against air with
what exists. Take option 3 only if the Game Designer wants faction identity in
air defence as well, since it costs twice option 2 and needs Producer sign-off.

The reason to prefer 2 over 3 first: a static answer is the gap the playtest
can find, and one common building closes it with one derived def. Faction
flavour can follow once the static answer is known to be wanted.

## What would settle it

A Game Designer sentence after the playtest: "no anti-air structure", "one
common emplacement" or "one per faction", with the doctrine word for each if
option 3. If option 1 stands, D30 and this file record why and nothing else
changes.
