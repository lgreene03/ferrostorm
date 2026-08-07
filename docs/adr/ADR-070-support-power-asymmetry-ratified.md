# ADR-070: the support-power roster is asymmetric by design, not one short
- Status: Ratified
- Date: 2026-08-07
- Deciders: Luke (Game Designer authority) + Architect agent
- GDD/TDD feature served: GDD s8 line 71; GDD s3 lines 25 and 30

## Context

P7-27's completion audit (ADR-068) measured the shipped support powers against
GDD s8 line 71, which read *"3-4 minor support powers per faction on shorter
timers"*. `parityprobe`, derived from the catalogue, found the Sodality carries
THREE (radar jamming, tunnel deployment, decoy army) and the Directorate TWO
(orbital scan, precision strike). The Sodality satisfies the sentence; the
Directorate is one short of its lower bound.

The shortfall is not an implementation gap. GDD s3 line 25 names exactly two
Directorate powers and calls the doctrine "surgical"; s3 line 30 names three
Sodality powers, the "dirty tricks". Every one of the five shipped without a
balance argument because its numbers derive from something already in the game.
A sixth power cannot be derived that way: there is no name or purpose to derive.
That makes it invention on a roster, a Game Designer's call (C9), and it was
filed as Q022 rather than taken by an implementer.

## Decision

The asymmetry stands. The Sodality's three against the Directorate's two is the
intended design, not a gap to fill. GDD s3, the more specific document, names the
roster precisely and is authoritative over s8's looser "3-4 per faction"; s8 line
71 is amended to state the asymmetry and to stop asserting a count the design
deliberately does not meet. No sixth power is invented.

The Directorate is paid for the shorter list elsewhere, in the heaviest armour on
the board and the only Bastion. The two doctrines are legible: surgical
information-and-damage at a point versus three cheaper acts of misdirection.

## Alternatives rejected

**Invent a sixth power (a repair pulse, an artillery-spotting effect, or a new
Directorate building to carry it).** Each is real design and would ship cleanly,
but every one is invention on a roster with no GDD sentence behind it, and the
constraint is tight: the Directorate owns only three faction-exclusive buildings,
the orbital cannon cannot carry a power (it uses ChargeTicks for its own cycle,
ADR-063), the power plant is the first building raised, and the Bastion already
holds both existing powers sharing one charge (ADR-064). So a sixth power either
overloads the Bastion three ways or wants a new building, a bigger call than the
gap justifies. Filling a "3-4" by inventing to the number is exactly how a
specification stops describing the game.

**Leave s8 unamended.** Rejected because a written "3-4" the game deliberately
does not meet is precisely the stale-claim class P7-27 spent a wave correcting
(doc 24 asserting three shipped features were missing). A specification that
disagrees with the game is a defect whichever side is right; here the game is
right, so the specification moves.

## Consequences

Q022 is closed. GDD s8 and the catalogue agree, and `parityprobe`'s 2-versus-3
reading is now the intended state a reader can check rather than a shortfall.
Nothing in `/sim`, `/data` or any hash changes: this ADR is documentation only.

We are committed to the asymmetry until a playtest says it reads as a shortfall
rather than a doctrine. That is the single overturning condition, and it is a
human verdict: the P7 playtest brief already asks whether each faction's kit
feels complete. If it is overturned, the sixth power is named first and its
numbers derived as the other five were, never invented to hit a count.
