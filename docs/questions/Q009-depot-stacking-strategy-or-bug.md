# Q009: depot stacking: five overlapping depots heal 10 hp per tick. Strategy or bug?

Owner: game-designer (Balance consulted; any cap or rate change over 15 per
cent triggers the A11 co-sign)
Raised by: doc 23's repair audit (REP-D5), 2026-07-16; filed by ADR drafting
2026-07-17
Decide by: 2026-07-24

## The question

The Service Depot's repair pass runs once per depot per tick with no
per-target dedupe (World.cs:1761-1773): every powered depot within radius 4
of a damaged own unit heals it 2 hp for 1 credit, independently. Measured
(doc 23 REP-D5): one depot takes a tank from 100 to 120 in ten ticks for 10
credits; two overlapping depots take the same tank to 140 for 20. Rate and
cost both scale linearly with depot count, uncapped, so five overlapping
depots heal 10 hp per tick. At 1200 credits a depot (com_service_depot.yaml:4)
that is a real, purchasable forward-repair fortress. Is it a strategy or a
bug?

## Context

- Nobody has written it down anywhere: not in the YAML notes, not in the
  GDD (which never asked for a repair building at all; the depot and its
  aura are TICKET-P3-SIM-01's inventions), not in any ticket. It is
  currently a behaviour, not a decision.
- It silently invalidates any balance number derived from "2 hp per tick",
  which is the figure every repair-related readout and ticket quotes,
  including Wave 1's REP-04 readout ("REPAIRING 15 cr/s") one layer up.
- It is invisible to the player: no aura ring shipped until Wave 1's REP-07,
  and nothing anywhere shows stacked healing. A strategy the player cannot
  see is not yet a strategy.
- If it is a strategy: it wants a name, a YAML note, a visible stacked-aura
  cue, and a Balance pass on the credits-per-hp curve at depot counts 1 to 5
  (the per-tick spend also scales linearly and races the treasury).
- If it is a bug: the fix is a per-target dedupe (first depot in index order
  wins, deterministic), it MOVES golden hashes wherever two depots overlap
  in a scenario, and per CLAUDE.md it needs an ADR plus Architect sign-off,
  so the ruling here feeds an amendment to ADR-008's wave or its own small
  ADR rather than a quiet patch.
- Adjacent recorded asymmetry, decided elsewhere: the depot's power gate is
  base-wide, not depot-local (World.cs:1760, REP-D6), and structure repair
  ignores power entirely while the depot does not (PWR-D8). Doc 23 s4.6
  recommends documenting, not reconciling; they are listed here only so this
  question is not answered in ignorance of them.

## Options considered

1. Strategy: keep linear stacking, document it in com_service_depot.yaml's
   notes, give it a visible cue, and hand Balance the curve.
2. Bug: per-target dedupe, one depot's worth of healing regardless of
   overlap, ADR'd and regenerated with an explained diff.
3. Capped stacking (for example two depots' worth maximum): the middle
   ground; still a sim change, same ADR cost as option 2.

## Changed / Assumed / Needed next

- **Changed:** nothing. This is a question, not a decision.
- **Assumed:** the measured numbers from doc 23's probes hold at HEAD (the
  loop structure at World.cs:1761-1773 is unchanged and makes the linearity
  structural).
- **Needed next (from the Game Designer, with Balance):** a ruling by the
  decide-by date, and if 2 or 3, a note to the Architect so the hash cost
  lands inside a planned regeneration instead of alone.
