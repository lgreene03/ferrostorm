# ADR-025: destroyable bridges

- Status: Ratified (Architect + game-designer + sim-engineer drafted 2026-07-25;
  ratified under Luke's standing directive of 2026-07-17, by the ADR-012
  precedent: a doc 24 ratification-gated sketch is formalised into an ADR and
  then built, which is exactly how ferrite regrowth shipped as wave B6)
- Date: 2026-07-25
- Deciders: Architect + game-designer + sim-engineer + Luke
- GDD/TDD feature served: doc 24's ratification-gated sketch (Phase C,
  "bridges are permanently open cells; making them destroyable is a map-format
  and passability change"); P6 campaign tracker wave C6, bridges half

## Context

The C6 row bundles two features, and the design pass found they have almost
nothing in common. This ADR takes the half that is shippable and records why the
other half is not.

**Bridges today are pure client dressing.** `'B'` in the map grid sets a Visual
cell and nothing else: MapLoader deliberately does NOT add it to the blocked
list, `MapData.Visual` is documented as "client-only terrain dressing, the sim
never reads this", and a bridge has no entity, no Hp, no hash presence and no
save representation. The sim does not know bridges exist.

**Doc 24's cost estimate for this is wrong, and correcting it is why the wave is
cheap.** It says bridges are "worth pairing with the gate work since both need
incremental flow-field repair". A gate needs incremental repair because it opens
and closes as units approach, turning a rare passability event into a per-tick
one. **A bridge dies once and stays dead.** That is a single passability change
per bridge per match, which is precisely the frequency the existing wholesale
`_flow.Clear()` in Block/UnblockFootprint was built to absorb. Bridges need no
new pathfinding machinery whatsoever. The false coupling to gates is the only
reason this looked expensive.

## Decision

A destroyable bridge is a **1x1 map-placed neutral structure with Hp**, spawned
from a **new grid character**, which blocks its cell when it dies.

1. **A new grid character, and `'B'` stays inert.** This is the load-bearing
   choice. skirmish-01 carries twelve rows of `'B'` cells and IS the map the
   `skirmish` golden loads, so promoting existing `'B'` cells to entities would
   add entities at tick 0 and move that golden immediately. With a new character,
   `'B'` keeps meaning "permanently open decorative crossing" and only maps that
   opt in gain destroyable ones. Unknown grid characters already throw, so a new
   one is purely additive and needs no map-format version bump (the ADR-021
   argument for the structure line).

2. **Placed on skirmish-04 only**, leaving skirmish-01 (the golden's map),
   skirmish-03 (the frozen look-dev reference) and every mission map untouched.
   This is the C4b rule verbatim, and no golden scenario loads skirmish-02 or
   skirmish-04.

3. **A new `EntityKind.Bridge = 18`** (append-only; Outpost = 17 was last) and a
   new struct type, reusing the Outpost's map-placed neutral machinery and the
   wall's 1x1 footprint machinery. Neutral owner (PlayerId -1), so auto-acquire
   ignores it exactly as it ignores a neutral outpost, and an explicit Attack
   still targets it, which is how a player brings one down deliberately.

4. **Death BLOCKS the cell, which is a new code path.** Every existing death path
   only ever unblocks. A bridge inverts that: alive means passable, dead means
   the cell is set blocked and the flow cache cleared, so routes re-form around
   the gap. This is the one genuinely new mechanic and it gets its own gate
   assertion.

5. **A bridge is not hope and not capturable**, mirroring the barrier and outpost
   exclusions: it is excluded from the VictorySystem hope test, from engineer
   capture, and from combat auto-acquisition.

6. **The severed-map problem is answered by the GENERATOR, not by the sim.**
   A rubbled bridge is a neutral blocker, so the DEF-05 breach path will not fire
   against it (that path requires an enemy-owned barrier), which means an
   attack-move across a fully severed river would go inert and the AI's waves
   would halt. Rather than widen the breach predicate to neutral blockers, which
   would touch a path every golden exercises, the map generator guarantees the
   problem cannot arise: **`tools/mapgen.py` gains a check that the two starts
   remain connected with EVERY destroyable bridge simultaneously rubbled.**
   skirmish-04 has three crossings, so making a strict subset destroyable
   satisfies it with margin. This reuses the same block-and-reflood machinery the
   outpost check already uses, and it turns a sim-level hazard into a generator
   invariant that fails loudly at authoring time.

## Alternatives rejected

**Promote the existing `'B'` cells to entities.** The obvious reading, and it
would make every existing bridge destroyable at once. Rejected because
skirmish-01 has twelve of them and is the `skirmish` golden's map, so it is an
immediate golden move for a feature that does not need one; and because it would
silently change three shipped maps' tactical shape without a design pass on any
of them.

**Widen the DEF-05 breach predicate to neutral blockers** so a severed river
self-heals. Tempting, and arguably correct in general, but it edits an
auto-acquisition and pursuit path that every golden exercises, to fix a case the
generator can make impossible. Deferred to its own ADR if a map ever genuinely
wants a severable crossing.

**Ship bridges together with gates, as doc 24 suggested.** Rejected on the
finding above: the pairing rests on a claim about incremental flow repair that is
true only for gates. Pairing them would hold a cheap, complete feature hostage to
an architecture wave.

## Gates: explicitly NOT authorised here

ADR-005 clause 6 deferred gates and, unusually, recorded its own revisit
condition: "if per-player flow fields are ever built for another reason, clause 6
is revisited with the blocker already gone." **That precondition is unmet.**
Passability is one global grid with no player dimension anywhere: FlowField.Build
takes no player, and the cache key is the target cell alone.

So a gate is not a dead-code wave like C2, C4 or C3b. It requires either
per-player flow fields (multiplying the cost of the single hottest deterministic
path in the sim) or a global auto-open approximation (which mutates the shared
grid per tick, needs the same incremental repair, and ships a tailgating
exploit). Either way its acceptance criterion cannot be "goldens byte-identical
by construction"; it must be measured, and an incremental repair that is not
bit-identical to a full rebuild both moves goldens and desyncs lockstep.

Gates therefore stay deferred, and TICKET-P6-C6b records that building them means
OVERRIDING clause 6 rather than satisfying it, which is a decision for Luke and
the Architect and not something a wave should assume.

## Consequences

Easier: skirmish-04's crossings become a real tactical object, which is the point
of a river map; the map generator gains a connectivity invariant that makes
severing impossible to author by accident.

Harder: one genuinely new code path (an entity death that BLOCKS rather than
unblocks) that must be gated; a save-format consideration for the new kind; and
the client needs an intact-versus-rubble visual, which can reuse existing wall or
rubble material rather than new art.

Hash impact: NEUTRAL. A new EntityKind and struct type that no golden scenario or
golden-covered map spawns is byte-identical dead code, the ADR-019/021/023
pattern. The new grid character appears only in skirmish-04, which no golden
loads. The `CatalogueChecksum` changes because the catalogue grew, which is a
save and replay compatibility matter and not a state hash, exactly as ADR-021
accepted.

Gates: a BridgeGate (additive, standalone plus a Match stage, never a golden
scenario) proving that a bridge is passable while alive, that destroying it
BLOCKS its cell and re-routes traffic around the gap, that auto-acquire ignores a
neutral bridge while an explicit Attack fells it, that it is not hope for
victory, and that a v-bumped save round-trips it. The generator's
connectivity-with-all-bridges-rubbled check is asserted at authoring time, and
mapgate already walks every committed map.
