# Q014: does Ferrostorm want a second resource at all?

- Owner: Luke (with game-designer)
- Raised: 2026-07-25, by the C8 design pass
- Decide by: before wave C8 is built; nothing else is blocked on it
- Status: OPEN

## The question

Doc 21's P4-PORT-04 asks for multi-resource support and says "GDD decision
first". That decision has never been made. Should the game have a second
resource, and if so, is it a richer grade of the same crystal feeding one
treasury, or a genuinely separate currency?

## Why this is a question and not a ticket

Every other C-series wave implemented something the GDD already named: the
repair vehicle is GDD line 62, the capturable Outpost is line 41, the parallel
build lanes are line 45. Each decided HOW, never WHETHER.

The GDD names exactly ONE resource, in section 4: "Ferrite crystal fields.
Regrow slowly from seed nodes; fields near spawns are finite enough to force
expansion by minute ~8." There is no second resource in the GDD, in doc 26, or
anywhere in the design package. Doc 22's P5-ECON-12 proposes a design, but
explicitly as a recommendation pending this decision.

Adding a second resource changes a core economic pillar. CLAUDE.md requires
Producer sign-off for new units, factions or modes, and a resource is at least
that significant; rule 5 forbids resolving a conflict with the design package
silently, and "the GDD says one resource, the audit asks for two" is exactly
that conflict. So it is asked rather than assumed.

## What the engineering says, so the decision is informed

The design pass turned up one fact that makes the choice unusually clean, and it
is the reason this question is worth answering rather than shelving:

- **A richer grade feeding the ONE treasury is FREE.** It follows the pattern
  three waves have now proved (a new EntityKind that no golden scenario spawns is
  byte-identical dead code), needs no save-format bump, and keeps all 24 golden
  hashes untouched.
- **A separate currency is NOT.** The treasury is folded into the state hash
  inside a per-player loop that always runs, so a second counter moves all 24
  goldens by construction. There is no way to guard it, unlike the build-lane
  collection in ADR-023. On top of that it rewrites nineteen credit call sites
  and contradicts the single-pool tradition the economy is built on.

So the cheap option and the option doc 22 recommends on design grounds are the
same option. That is rare and worth knowing before choosing.

## The options

1. **No second resource.** Keep the single-crystal economy. Close P4-PORT-04 as
   "considered and declined", and correct doc 21's status row so it stops
   reading as an outstanding gap. Perfectly defensible: the current economy is
   coherent and nothing is broken.
2. **A richer grade, one treasury (ADR-024 as proposed).** A finite, non-regrowing
   rich patch placed away from the spawns, banking more credits per unit mined.
   Adds a second axis to map control with a clock on it. Hash-neutral.
3. **A separate currency.** The literal reading of P4-PORT-04. Costs a golden
   regeneration, an economy-wide rewrite, and a new sidebar readout, and it cuts
   against the GDD's economy. Not recommended, but it is the option that would
   most change how the game plays.

## Recommendation

Option 2 if a second resource is wanted at all, and option 1 is a genuinely fine
answer. Option 3 should be chosen only deliberately and with its cost accepted,
not drifted into.

If option 2: the ADR needs a NAME for the resource and a statement of what it is
FOR, neither of which exists anywhere in the design package. Doc 22's "Ferrite
Cache" is already spent on a different concept.

## Resolution

(unanswered)
