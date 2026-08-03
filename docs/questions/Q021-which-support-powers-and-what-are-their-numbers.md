# Q021: the five support powers are NAMED in the GDD. What are their numbers?

Owner: game-designer (+ balance for the numbers)
Raised by: P7-21 (ADR-062), 2026-08-03, on landing the machinery
Decide by: unset (not blocking; the machinery is inert until a power is authored)

## What is already written, which is more than expected

GDD s8 specifies the machinery and names no powers. **GDD s3 names five**, in the
faction identity sections, and that was nearly missed - doc 29 recorded support
powers as "nothing at all" against "GDD s8 promises 3 to 4 per faction" without
noting that s3 already says which:

> **Directorate** (s3 line 25): support powers are **surgical** (**orbital
> scan**, **precision strike**).
>
> **Sodality** (s3 line 30): support powers are **dirty tricks** (**radar
> jamming**, **decoy army**, **tunnel deployment**).

So the roster is two-thirds specified: five of the six-to-eight powers s8 asks
for are named, with a doctrine word each. **This is not an invitation to invent
a roster; it is a request for numbers.**

## The question, per power

Each needs the same three answers, and none of them is written anywhere:

1. **Which building unlocks it?** s8 says powers are "unlocked by structures", and
   P7-21 makes the structure the permission - so this choice also decides the
   power's counterplay, because killing that building removes the power.
2. **What is its magnitude?** A radius, a duration, a damage figure, a count.
3. **What is its charge?** P7-21 gives every power the same charge, derived as a
   third of the superweapon's. If powers should differ from each other, that is a
   design call.

| power | side | doctrine word | what it plainly does | what is missing |
|---|---|---|---|---|
| orbital scan | Directorate | surgical | reveals fog at a point | radius, how long the reveal lasts |
| precision strike | Directorate | surgical | damage at a point | damage, radius, and how it differs from a small superweapon |
| radar jamming | Sodality | dirty trick | blinds an enemy's radar | duration, and whether it is global or local |
| decoy army | Sodality | dirty trick | fake units | count, lifetime, and whether they can be shot |
| tunnel deployment | Sodality | dirty trick | move units somewhere | range, how many, and where they may arrive |

## Progress

- **orbital scan** - ANSWERED and shipped 2026-08-03 (ADR-063). Bastion; radius
  = the building's own sight; duration = the superweapon's warning window.
- **precision strike** - ANSWERED and shipped 2026-08-03 (ADR-064). Bastion,
  beside the scan and sharing its charge; damage = a third of the orbital
  cannon's; radius = the cannon's own core, one band, no falloff.

ADR-064 also removed the blocker the rest of this question would have hit: a
building now unlocks a LIST of powers, because the Directorate owns only three
exclusive buildings and one power each could never reach s8's "3-4 per faction".
The Sodality's five exclusive buildings can carry the three remaining tricks.

## Recommended first, and why

**Orbital scan.** It is the only one of the five whose effect needs no new
combat or unit machinery: the sim already has per-player fog (`IsVisible`,
`IsExplored`), so a reveal is a timed override of a bitset that already exists.
Its two numbers - radius and duration - are also the two least entangled with
balance, because a scan changes what a player KNOWS rather than what they can
kill.

**Least recommended first: decoy army**, which needs entities that look real to
one player and not another, and touches targeting, fog and the checksum at once.

## Note for whoever answers

`ApplySeismicCharge` (ADR-044 clause 4) is the precedent for the effect
functions: **its own function, not a widened shared one**, because the shared
`ApplyAreaDamage` is also the mine's and a radius parameter would have put every
mine one careless argument from changing shape. Expect each power to want its own
effect function for the same reason.

And the Veil Projector is **not** one of these. See ADR-062, which corrects
ADR-060 on that point: it is a persistent aura, already working, not a timed
power.
