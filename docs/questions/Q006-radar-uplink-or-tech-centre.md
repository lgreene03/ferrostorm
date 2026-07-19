# Q006: does the Radar Uplink absorb the Tech Centre role, or is a separate Tech Centre wanted?

Owner: game-designer
Raised by: ADR-009 drafting, 2026-07-17 (ordered by doc 22's BD-17 clause 7
and doc 23 s4.3, which both refused to let an implementer decide it silently)
Decide by: 2026-07-24

## The question

GDD line 47: "Both factions can build replacement MCVs at the Factory once a
Tech Centre exists." No Tech Centre exists anywhere in the project: not in
the GDD's own structure lists beyond that sentence, not in /data, not in the
sim. Meanwhile the Radar Uplink (struct type 12, compiled at World.cs:459,
authored at data/buildings/com_radar_uplink.yaml) is about to become the
tier-defining building: ADR-008 hangs the blackout off it and doc 23 s4.3
proposes `[com_radar_uplink]` as the prerequisite for the superweapon and the
future airfield. Does the Radar Uplink absorb the Tech Centre role GDD line
47 names, or does the design want a separate Tech Centre structure?

## Context

- The candidate resolution (doc 22 BD-17 clause 7, doc 23 s4.3): the radar
  absorbs the role, so com_mcv's prerequisite becomes `[com_radar_uplink]`.
  One building anchors radar, tier 2 and MCV replacement, the classic
  mid-game pivot, and no new structure is invented.
- The alternative: a separate Tech Centre, which honours GDD line 47
  literally, gives tier gating a building that does not also carry the
  blackout mechanic, and costs a new struct type (17 is next per ADR-008's
  amendment note), a new YAML, a model, a sidebar entry and every
  enumeration-site edit doc 23 s4.2 lists.
- Today com_mcv.yaml:21 reads `prerequisites: [com_factory]`, which under
  `produced_at: com_factory` is a tautology and says nothing; the comment at
  com_mcv.yaml:18-19 already flags this exact question.
- ADR-009's implementing ticket (TICKET-P5-PROD-04) authors the final unit
  prerequisite table and CANNOT merge the MCV's row until this is decided.
  The AI's `expansionDesired` gate (SkirmishAI.cs:127) also moves with the
  answer: whatever the MCV waits on, the AI must know to build it first or it
  saves 3500 credits forever.

## Options considered

1. Radar Uplink absorbs the role; com_mcv gains `[com_radar_uplink]`; the
   GDD's "Tech Centre" is read as a role, not a building. (Candidate per doc
   22 and doc 23. Cheapest, and the tier structure it creates matches the
   authored tiers in /data.)
2. A separate Tech Centre ships as its own structure with its own ADR-005
   amendment for the numbering; the radar stays a pure radar.
3. Defer: the MCV keeps a non-tautological interim prerequisite (for example
   `[com_service_depot]`) until the air ADR forces the tier question anyway.
   Recorded for completeness; it spends the decision twice.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** that GDD line 47's intent is "MCV replacement is tier-gated",
  not "a building literally named Tech Centre must exist".
- **Needed next (from the Game Designer):** pick 1 or 2 by the decide-by
  date; ADR-009's prerequisite table and the AI ladder edit both block on it.

## Status note, 2026-07-19 (P6 Wave B4)

STILL OPEN, and now costlier to answer. ADR-009's implementing wave shipped
the mechanically enforceable half and deliberately left this curation alone:
com_mcv.yaml still reads `prerequisites: [com_factory]`, which under
produced_at is the tautology this question exists to resolve. It is now
ENFORCED rather than carried, which changes nothing about play (it is
trivially satisfied at the factory the MCV comes out of) but does mean the
answer arrives as a behaviour change inside a golden regeneration rather than
as a free YAML edit.

The AI's `expansionDesired` gate was left reading the factory ON PURPOSE and
carries a comment binding it to this question, because gating on the data is
what keeps the commander from saving 3500 credits for an MCV it cannot buy.
Whichever option is chosen, that line moves in the same change as the
prerequisite, or the subtlest failure mode ADR-009 clause 7 names appears
immediately.

One piece of new evidence for the decision: the Radar Uplink is now a real
tier gate in the shipped tree (the superweapon waits behind it, and it waits
behind the factory), so option 1 would place the MCV on an anchor that already
carries mid-game weight rather than inventing one.
