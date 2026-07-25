# Q017: the asymmetry pillar is the least delivered - which identity step first?

Labels: persona:p1, gdd:s3, phase:6, owner:game-designer + producer
Raised by: the design review of 2026-07-25 (docs/design/27), as its principal
recommendation.
Decide-by: before any further content breadth (new missions, maps, units), on
the review's argument that identity is worth more than volume right now.

## The finding

Honestly measured, the shipped asymmetry is: 6 of 13 units common, 5
Directorate, 2 Sodality; 11 of 12 structures common. Both factions build the
same base, mine the same fields, and fire the same superweapon on the same
timer. Sodality is one mechanic (stealth) expressed three ways, with no
detector of its own, so a Sodality mirror has no stealth answer beyond the
firing reveal. The Directorate has no faction mechanic at all. The GDD's
pillar 3 promises factions that "differ in how they think"; the genre design
writing says asymmetry must be thematic and must live where the game's focus
is, which this game declares to be the economy - and the two economies are
byte-identical.

## The question

Which identity step is taken first? The candidates, from doc 27's register:

1. **Power economics (DR-02).** Directorate: one large, expensive, efficient
   plant (a juicy target - the GDD's own words). Sodality: several small cheap
   generators (resilient, sprawling). Mostly /data numbers plus one new def
   each; touches the power curve every opening is tuned around, so it MOVES
   goldens and needs an ADR. The most thematic option and the one the GDD
   already designed in prose.
2. **A Sodality detection answer (DR-03).** Closes the mirror-match hole and
   the "do not strip core tools" failure. Smallest option; a new def nothing
   spawns is hash-neutral by the standing precedent. Brushes C9 (the roster is
   Luke's call).
3. **Faction superweapons (DR-04).** The Sodality seismic charge that destroys
   ferrite fields is economy-warfare identity in a single feature and the GDD
   already specifies it. Larger: new sim behaviour, an effect, an ADR.
4. **Refinery/harvester economics per faction.** Not yet specified anywhere;
   would need design from scratch; listed for completeness.

## What this question is NOT

It is not C9 (which specific units fill the roster) and it is not C8
(multi-resource, Q014). It is the sequencing decision: which of the above gets
the next design-and-build wave, or whether breadth (missions, maps) is
preferred over identity despite the review's argument.

## Needed from whom

- **producer + game-designer (Luke):** pick a first step, or reject the
  sequencing argument.
- **architect:** the ADR when steps 1 or 3 are picked (both move goldens).
