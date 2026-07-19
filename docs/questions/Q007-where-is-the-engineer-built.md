# Q007: where is the engineer built once the barracks exists?

Owner: game-designer
Raised by: ADR-009 drafting, 2026-07-17 (doc 23 s4.3 explicitly declined to
assert textual support the GDD does not give)
Decide by: 2026-07-24

## The question

When ADR-009's barracks split lands, every unit produces at exactly one
building. The engineer has to produce somewhere, and GDD line 60 does not
say where: "Shared: Engineer (capture), Harvester, MCV, Transport
helicopter." That is a shared roster line, not an infantry line, and it
groups the engineer with the harvester and the MCV, both of which go to the
Factory. Does the engineer build at the Barracks or at the Factory?

## Context

- The genre idiom builds engineers at the infantry building, and that is the
  recommendation doc 23 s4.3 records, while insisting it be decided here
  rather than read into GDD line 60.
- The data already carries the candidate: com_engineer.yaml:19 reads
  `produced_at: com_barracks` (authored in Wave 2, commit 147f03b). Nothing
  branches on the field yet, so reversing it is a one-line YAML edit with no
  hash cost UNTIL TICKET-P5-PROD-04 lands. After PROD-04 the same edit is a
  behaviour change inside a golden regeneration. That is why the decide-by
  matters: the cheap window closes when the implementing ticket merges.
- Gameplay stakes, stated so the choice is informed: at the Barracks, an
  engineer rush needs only power plus a 500-credit building, the classic
  cheese and the classic counterplay; at the Factory, engineers arrive later
  (factory needs a refinery under ADR-009's structure tree) and capture play
  becomes a mid-game tool rather than an opening gambit.
- The engineer is unit type 11; struct type 11 is the barracks. Different
  namespaces, no collision (the campaign.txt header gains a note saying so
  per ADR-009 clause 8), but any discussion of "11" in this area should say
  which namespace it means.

## Options considered

1. Barracks (`produced_at: com_barracks`, the current authored value): genre
   idiom, early capture play, gives the 500-credit barracks another reason
   to exist. Prerequisites stay `[]`.
2. Factory (`produced_at: com_factory`): reads GDD line 60's grouping as
   intent, delays engineer availability behind the refinery-then-factory
   chain, no rush.
3. Barracks with a prerequisite (for example `[com_refinery]`): keeps the
   build site idiomatic but delays the rush. A tuning middle ground the
   prerequisite system makes free to express.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** that the shipped YAML value is a candidate, not a decision;
  it was authored to exercise the carried field, and Wave 2's commit made no
  design claim for it.
- **Needed next (from the Game Designer):** pick an option by the decide-by
  date; TICKET-P5-PROD-04's final unit table blocks on it (ADR-009 clause 5).

## Status note, 2026-07-19 (P6 Wave B4)

STILL OPEN, and THE CHEAP WINDOW HAS NOW CLOSED, exactly as the Context above
predicted it would. ADR-009's implementing wave enforces produced_at, so
com_engineer.yaml's authored `produced_at: com_barracks` is live: the engineer
comes out of the barracks, appears under the sidebar's INFANTRY tab, and a
factory refuses to build it. Reversing that is still one YAML line, but it is
now a behaviour change inside a golden regeneration rather than a free edit,
and it would move the engineer's sidebar tab with it.

What shipped is option 1, and it shipped because it was the authored value and
the mechanically enforceable reading, NOT because this question was answered.
If the Game Designer prefers option 2 or option 3, the wave that applies it
regenerates the goldens under ADR-009's existing sign-off; nothing about this
wave forecloses the choice.

Play-relevant consequence now live, for the decision: an engineer rush needs
power plus a 500-credit barracks and nothing else, because the barracks sits
early in the tree with only the power plant behind it. That is the classic
cheese and the classic counterplay the Context describes, and it is now the
shipped behaviour rather than a hypothetical.
