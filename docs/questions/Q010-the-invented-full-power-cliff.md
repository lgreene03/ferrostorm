# Q010: the sim invented a hard full-power cliff the GDD never names, and the GDD's one named power play is inverted

Owner: game-designer
Raised by: doc 23's power audit (PWR-D5, referred rather than filed as a
defect); filed by ADR-008 drafting, 2026-07-17
Decide by: 2026-07-24

## The question

GDD line 48 names exactly two power thresholds: below 100 per cent,
production slows linearly and radar goes dark; below 75 per cent, turrets go
offline. The sim ships a THIRD semantic the GDD never mentions, a hard cliff
at exactly 100 per cent, and applies it to three systems the GDD never
couples to power at all:

- the superweapon charge pauses the instant supply drops below draw
  (World.cs:1780, `supply[sp] >= draw[sp]`);
- the service depot stops mending in any brown-out (World.cs:1760);
- the veil projector's whole cloak field collapses in one (World.cs:1181).

Meanwhile the one power play GDD line 48 DOES name, "deliberate 'sell power
to sneak a superweapon' plays should be possible", is mechanically inverted
by the first of those cliffs: selling your plants to fund a superweapon
stops the superweapon. Which behaviour is intended?

## Context

- All three cliffs are deterministic, hashed behaviour with gate coverage
  (the superweapon boundary equality at Program.cs:833 and :840 is asserted
  on exactly the inclusive `>=`). Changing any of them MOVES golden hashes
  and needs an ADR plus Architect sign-off; that is why this files as a
  question feeding a decision, not a patch.
- ADR-008 makes the question sharper, deliberately and honestly: under its
  honest draws the opening base is EXACTLY 100 supply against 100 draw, so
  every one of these inclusive boundaries is the resting state of a fresh
  base. The cliff semantics stop being a corner case the moment those
  numbers land.
- The sell-to-sneak inversion has a coherent defence: pausing an unpowered
  superweapon is itself classic behaviour, and the GDD sentence may intend
  "sell power you can spare" rather than "a superweapon charges without
  power". But that is a reading, and doc 23's sceptic pass explicitly
  declined to rate the inversion as a defect because intent is the Game
  Designer's to state, not an auditor's to infer (doc 23 s3.1 PWR-D5 and
  s7 item 15).
- Whatever the ruling, it should be one semantic decision, not three
  accidents: the cliff currently exists because it was easy, in three
  systems nobody asked to couple to power, while the two couplings the GDD
  asked for arrive years later via ADR-008. Inheriting that shape without a
  decision is the thing this file exists to prevent.

## Options considered

1. Ratify the cliffs as design: three named systems require full power;
   sell-to-sneak is read as "spare capacity", the sentence in GDD line 48 is
   amended to say so, and the inversion is closed as intended behaviour.
   Zero code change, zero hash movement, one GDD edit.
2. Honour the letter of GDD line 48: the superweapon charge follows the
   linear-slowdown model (charges slower below 100 per cent instead of
   pausing), making sell-to-sneak genuinely possible. Depot and veil cliffs
   decided separately on their own merits. Moves hashes; wants its own small
   ADR or a berth in a planned regeneration.
3. Unify on AtLeast75: all power gates use the one threshold the sim and the
   bar already share (World.cs:1091, Sidebar.cs:323). Simplest mental model,
   biggest behaviour change, moves hashes.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** ADR-008's turret gate (a fourth coupling, at 75 per cent) is
  decided by that ADR regardless of the ruling here; this question owns only
  the three inherited cliffs and the sell-to-sneak sentence.
- **Needed next (from the Game Designer):** a ruling by the decide-by date,
  and if 2 or 3, the Architect schedules the hash movement; the GDD edit in
  option 1 needs Producer visibility since it amends source-of-truth
  number two.
