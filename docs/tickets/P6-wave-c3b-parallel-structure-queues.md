# TICKET-P6-C3b: parallel structure/defence queues (filed pending)

Labels: persona:p2, gdd:s5, phase:6, owner:architect + sim-engineer, gdd:line-45

Status: FILED, pending. Split out of C3 by ADR-020, which shipped the client-only
half of the four-queue sidebar (the missing right-click cancel/refund) and
deferred the genuinely sim-side half to this ticket rather than bundle a
golden-move change into a wave the tracker expected to be client-only.

## What it is

GDD line 45 wants "two parallel building queues (structures / defences)". Today
one Construction Yard holds ONE queue, one BuildProgress head and one ready slot
(World.cs ProductionSystem), so a structure and a defence cannot build at the same
time; the client's BUILDINGS and DEFENCE tabs both read that one yard queue. C3
(ADR-020) confirmed infantry and vehicles are already two parallel queues (they
read different producers) and delivered cancel/refund, but the parallel
structure/defence queues remain.

## Why it is its own wave (a golden move)

Two parallel queues on one yard entity need new hashed per-entity state: a second
queue, a second BuildProgress head, and a second ready slot (the current single
ready slot pauses the whole line, which is exactly the serialisation to remove).
That is:

- A hashed Entity tail append (a second progress/paid/ready set), which moves all
  24 goldens mechanically (the ADR-014/015 rally-pattern move) and needs the
  neutralisation-by-identity proof.
- A save-format bump (to v8, SaveMagicV8), the DowngradeSave tail surgery, and the
  hasX predicate wiring in World.Serialization.
- ProductionSystem advancing two heads with the under-power rate scaling applied
  to each, and CancelProduce learning which lane an index addresses.

None of that is client-side, so it could not ride C3.

## What it must decide (for the design pass, not now)

- **Lane model.** Whether the yard grows a fixed second lane (structures vs
  defences) or a general N-lane producer. Two lanes is the GDD's literal ask and
  the cheaper hashed footprint.
- **Ready slots.** One ready slot per lane, so a structure and a defence can both
  be waiting to place, with the client PLACE prompt disambiguating which. This is
  the main UX design point.
- **CancelProduce addressing.** The wire command currently carries one queue
  index; two lanes need the lane too (AuxId packing or a second field), a wire and
  replay-format consideration flagged for the ADR.
- **The neutralisation.** The second lane must be inert on every existing golden
  (no scenario uses it), so the append is mechanically neutral like ADR-015's
  stance tail. Prove by identity.

## Needed from whom

- **architect + game-designer:** an ADR (the lane model, ready-slot-per-lane,
  CancelProduce addressing, the hash and save cost) before any code.
- **sim-engineer:** the implementation once ratified, with its gate and one golden
  regeneration.

C3 (ADR-020) did not build any of this; the client cancel is self-contained and
complete without it.
