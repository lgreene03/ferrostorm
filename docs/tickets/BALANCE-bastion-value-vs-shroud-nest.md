# BALANCE: the Bastion is poor value against the Shroud Nest

Labels: `persona:balance` `gdd:s3` `phase:P7` `owner:balance + game-designer`
Found: 2026-08-03, P7-18 (ADR-060), as a side effect of making the AI build them
Charter: **A11** - needs Balance and Game Designer co-sign, or a playtest
Confidence: **measured**, by a controlled comparison on the balance tool

## The numbers

| defence | faction | cost | prerequisite | HP | weapon |
|---|---|---|---|---|---|
| Bastion (t17) | Directorate | **1400** | **Radar Uplink** | 1600 | turret gun |
| Shroud Nest (t18) | Sodality | **400** | Power Plant | 260 | emplacement gun |
| common turret (t5) | either | 600 | Power Plant | 400 | turret gun |

Three and a half times the price, and a **tier** later, for each side's answer
to the same problem.

## The measurement

P7-18 added a ladder rung so each commander builds its own defence. With no
affordability gate, both sides buy it out of the opening budget:

| ladder | faction war (balance tool) |
|---|---|
| before the row | Directorate **6 - 0** Sodality |
| rung, no affordability gate | Directorate **0 - 6** Sodality |
| rung gated on cost + 1500 (shipped) | Directorate **6 - 0** Sodality |

One ladder rung, no stat changed, and the war flips completely. The Directorate
pays 1,400 at a worse moment and loses every match; the Sodality pays 400 early
and wins every match.

ADR-060 shipped the affordability gate, which is independently correct ("buy
your signature defence from surplus, not instead of your army", the rule the
radar's 1500 and the superweapon's 4500 already state). **That masks this
problem rather than solving it.**

## Why this is not P7-18's to fix

It is a stat change, so charter A11 applies: Balance and Game Designer co-sign.
And the tool that measured it is not a fair judge of the answer - it already
reports **6 - 0 in one direction and calls that a PASS**, so it can tell you the
sign of a change but not whether the result is good. A war that reads 6-0 either
way is not a balanced game; it is a metric with no middle.

## What the design says

GDD s3 doctrine: the Directorate's buildings are *"tough but expensive"*, the
Sodality has *"cloaked units and structures"*. So the Bastion being dearer and
tougher is **correct by design** - `FactionDefenceGate` (P7-2b) asserts exactly
that ratio on purpose, and it should not simply be cheapened.

The question is therefore not "is the Bastion too expensive" but **"is 1400
behind a radar the right expression of tough-but-expensive, when the alternative
is 400 behind a plant?"** The tier gap may matter more than the price gap: the
Sodality has a defence from its first power plant and the Directorate has none
of its own until the radar.

## Candidate directions, none chosen

1. **Move the Bastion's prerequisite off the radar** to the factory or plant, so
   the timing gap closes while the price gap - the doctrine - stays. The
   smallest change, and it targets the difference that looks load-bearing.
2. **Reprice.** Straightforward and it dilutes the doctrine A11 exists to
   protect.
3. **Leave it and accept that the sides defend at different times**, which is a
   legitimate asymmetry if the Sodality pays for it elsewhere. Cheapest, and it
   needs a playtest to tell whether it feels like identity or like a handicap.

Direction 1 is the recommendation, on the grounds that it changes timing rather
than doctrine.

## What would settle it

A playtest, which is the thing this project has been blocked on for eight ADRs.
Specifically: does a Directorate player feel defenceless before the radar?
