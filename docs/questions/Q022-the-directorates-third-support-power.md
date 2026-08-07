# Q022: the Directorate is one support power short, and the GDD does not name it

Owner: game-designer
Raised by: P7-27 (ADR-068), 2026-08-03, on auditing P7 for completion
Decide by: unset (not blocking; it is the last parity gap in P7 and it needs a
name nobody has written)

> **ANSWERED AND CLOSED 2026-08-07 (Luke, Game Designer authority; ADR-070).**
> The asymmetry is RATIFIED, not filled: the Sodality's three surgical-versus-
> dirty-tricks split against the Directorate's two is intended, and GDD s3 - the
> more specific document, which names exactly two Directorate powers and three
> Sodality - is authoritative over s8's looser "3-4 per faction". GDD s8 line 71
> is amended so the specification and the game agree (the "Candidate directions"
> below record the third option, "Nothing at all", which was the one taken and is
> argued there as legitimate). The Directorate is paid for the shorter list in
> armour and the Bastion. `parityprobe` still measures 2 vs 3; that reading is now
> the intended state, not a shortfall. Overturned only by a playtest saying the
> asymmetry reads as a gap - then a named sixth power, derived as the other five
> were.

## The gap, measured

GDD s8 line 71: *"**3-4** minor support powers **per faction** on shorter timers,
unlocked by structures."*

`parityprobe`, derived from the catalogue:

| faction | support powers | s8 asks |
|---|---|---|
| Sodality | **3** | 3-4 |
| Directorate | **2** | 3-4 |

The Sodality satisfies the sentence. **The Directorate is one short.**

## Why this is a question and not a ticket

GDD s3 line 25 names exactly two Directorate powers - *"support powers are
surgical (**orbital scan**, **precision strike**)"* - and both ship (ADR-063,
ADR-064). GDD s3 line 30 names three for the Sodality, and all three ship
(ADR-065, ADR-066, ADR-067).

So the design names **five** powers, and s8 asks for **six to eight**. The
shortfall is not an implementation gap: **it is a power nobody has written down.**

Every one of the five shipped without a balance argument because every number in
them is derived from something already in the game. A sixth power cannot be
derived that way, because there is nothing to derive a *name* or a *purpose*
from. That makes it invention, and invention on a roster is a Game Designer's
call (C9), not an implementer's.

## What an answer needs

One sentence, in the shape s3 already uses. It needs three things, and the
machinery supplies the rest:

1. **A name and a doctrine word.** The Directorate's is "surgical" - both its
   existing powers are precise, low-collateral and information-or-damage at a
   point. A third should read as the same doctrine, or say why it does not.
2. **Which building unlocks it.** That choice is also its counterplay, since
   GDD s8's rule is "scout the structure, kill it" and ADR-062 makes the
   structure the permission. **Note the constraint**: the Directorate owns only
   three faction-exclusive buildings - the power plant, the Bastion and the
   orbital cannon - and the cannon cannot carry a power at all (it already uses
   `ChargeTicks` for its own cycle, ADR-063). The Bastion already carries both
   existing powers, and powers on one building **share its charge** (ADR-064).
   So a third power either joins the Bastion and makes that choice three ways,
   or goes on the power plant, or **wants a new Directorate building** - which is
   a roster addition and a bigger call.
3. **What it does**, at the level s3 gives: two or three words. The numbers can
   then be derived as all five others were.

## Candidate directions, none chosen

- **A repair or resupply pulse.** The Directorate's doctrine is materiel and
  staying power; a power that mends what it has fits "tough but expensive" and
  needs no new mechanic (`Repair` already exists as a command).
- **An artillery spotting or accuracy effect**, extending "surgical" into the
  Directorate's stated artillery strength.
- **Nothing at all.** GDD s8 says "3-4", and a faction with two well-made powers
  against another's three is a legitimate asymmetry if the Directorate is paid
  elsewhere - it already owns the heaviest armour and the only Bastion. **This is
  a real option and should be considered rather than assumed away**: the honest
  reading may be that s8's "3-4" was written before s3 named only two, and s3 is
  the more specific document.

## What would settle it

A Game Designer sentence, or a decision that the asymmetry stands. If the latter,
GDD s8 line 71 should be amended so the specification and the game agree, because
leaving a written "3-4" that the design deliberately does not meet is how the
stale claims P7-27 found got written in the first place.
