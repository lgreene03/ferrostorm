# Playtest brief, 2026-07-24

The last real playtest was 2026-07-16. Since then the whole of Phase B's tail and
all of Phase C so far have shipped, every wave signed off on "builds clean, hashes
unmoved". That phrase is almost exactly the root cause doc 25's V0 wave diagnosed
("nobody had ever looked at a frame"), so this brief exists to get eyes back on
the game rather than add to the pile.

Nothing below is a bug report. These are the judgement calls the gates cannot
make, each one written down by the wave that owed it.

## Play this

**Map: skirmish-02 (Ironback Ridge).** It is the one map that now carries
everything new at once: neutral outposts, and the whole C-series command surface.
skirmish-04 (Tarnwater Crossing) is the second choice and carries two outpost
pairs instead of one. skirmish-01 and skirmish-03 have NO outposts by design.

## What to try, and the question each answers

**1. Outposts (C4, C4b, C4c, ADR-021).** There is a neutral building in your land
and another in the enemy's. Click it: the readout should say it is unclaimed,
that it pays 15 cr/s to its owner, and that an engineer claims it. Build an
engineer at the barracks and walk it in.
- Does 15 credits per second feel worth the detour, or is it noise? (Balance owns
  this number under A11; it is one constant.)
- Is the outpost in a sensible place, or is it either free or unreachable?
- The AI now goes for these too. Does it beat you to it, and does that feel fair?
- It currently renders as a REFINERY (interim model). Is that too confusing to
  ship, or acceptable until art-pipeline cuts one?

**2. Formations (C1b, ADR-018).** Select several combat units and right-click a
destination. They should arrive in a forward-facing box rather than a blob.
- Does the box read as a formation, or as units standing oddly apart?
- Spacing is one tunable constant. Too tight, too loose?
- Attack-move also forms up. Does that help or does it break the charge?

**3. Sidebar cancel (C3, ADR-020).** RIGHT-CLICK any build item to cancel one of
that type and get the refund. Before this wave the client could queue but never
cancel, at all.
- Is right-click discoverable? The tooltip says so, but does anyone read it?
- Cancelling a ready-to-place structure refunds in full. Clear enough?

**4. Repair vehicle (C2, ADR-019).** Build one (factory, needs a Service Depot
first). It mends nearby damaged units in the field, 2 hp/tick for 1 cr/tick each,
and it does NOT need base power.
- Is it worth 700 credits, or would you always buy another tank?
- It also renders as a stand-in model (the MCV). Confusing?

**5. Unit stances (C1a, ADR-015), shipped 2026-07-21 and never played.** H
hold-fire, G guard, Q patrol on selected units.
- Does hold-fire actually let you walk a unit past a sentry?
- Is guard's leash (the unit's own sight) the right size?

## Known interim states, so they are not reported as bugs

- The Outpost and the Repair Vehicle both use stand-in models (refinery and MCV);
  bespoke models are owed to art-pipeline.
- The Outpost has no sidebar icon; it is never buildable, so it has no button.
- ~~An outpost captured from the enemy keeps its old team strip until the actor
  is rebuilt.~~ **FIXED 2026-07-25 (VERDICT wave): captured structures, and
  claimed neutral outposts, are now repainted for their new owner.**
- Two-machine LAN is REACHABLE as of 2026-07-25 (C7b-i..iv): HOST and JOIN are live, the frame loop is lockstep-driven, and a departing peer is announced. The two-machine session itself is still the outstanding human step.

## What I would most like an answer to

If only one thing gets judged: **the outpost income rate and placement**. It is
the newest mechanic, it is the one with a number nobody has ever felt, and it is
the cheapest to change (one constant and a generator line, both re-proved by the
gates for free).
