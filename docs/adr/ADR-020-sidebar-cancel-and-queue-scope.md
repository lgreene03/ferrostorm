# ADR-020: the sidebar cancel, and the four-queue scope split

- Status: Ratified (Architect + game-designer + client-engineer drafted
  2026-07-24; ratified the same day under Luke's directive to implement out the
  C-series, the standing-directive ratification pattern the P6 tracker records)
- Date: 2026-07-24
- Deciders: Architect + game-designer + client-engineer + Luke
- GDD/TDD feature served: GDD line 45 (the four-queue sidebar); P6 campaign
  tracker wave C3; doc 24 Tier 2 (the four-queue promise doc 23 Wave 6 did not
  fully discharge)

## Context

GDD line 45 specifies, verbatim: "Two parallel building queues (structures /
defences) and two unit queues (infantry / vehicles) per production-structure
type, C&C3-style multi-queue with per-structure rally points." The C3 tracker row
titles the wave "Four-queue sidebar (GDD line 45 in full)" and guessed the hash
impact "likely neutral (client)".

The code read shows the guess is only half right, and splits the wave cleanly:

1. The client already has four tabs (Buildings, Defence, Infantry, Vehicles) and
   per-structure rally points already shipped under ADR-007, so those parts of
   line 45 are done.
2. Infantry and Vehicles already ARE two parallel queues: they read different
   producers (barracks and factory), which build simultaneously.
3. But Buildings and Defence share ONE serial queue: both tabs read the single
   Construction Yard queue, so a structure and a defence cannot build at once.
4. And, decisively, the client issues CancelProduce NOWHERE. The whole cancel and
   refund half of the production loop is missing: a player can queue but can never
   cancel a queued item or a ready-to-place structure. The sim has supported it
   all along (CancelProduce by queue index, with pay-as-you-build pro-rata
   refunds, plus the QueueContents read accessor the doc-18 M3 flagged, which has
   landed), but nothing in the client ever sends the command.

So line 45 splits into a client-only, hash-neutral part (the missing cancel, and
the queue reads that already exist) and a genuinely sim-side part (two parallel
structure queues on one yard entity).

## Decision

C3 delivers the client-only, hash-neutral part, and defers the sim-side part.

**Delivered now (client only, ADR-001 intact, no golden move).** Every sidebar
build item gains a right-click cancel that issues the existing CancelProduce:

- Right-click a unit button cancels the most recent queued unit of that type at
  the own producer that has it queued, refunding through the sim's
  pay-as-you-build rule (the head refunds pro-rata; a not-yet-started queued item
  was never charged, so it refunds nothing, which is correct).
- Right-click a structure button cancels the most recent queued structure of that
  type at the Construction Yard, or, when a structure of that type is READY to
  place, cancels the ready slot for a full refund. When a DIFFERENT structure is
  ready (which pauses the yard queue), the button declines, because the player
  cancels the ready structure by right-clicking its own button, not another.

This is the classic sidebar affordance, and it is the functional gap that
mattered: the production loop could not be undone at all. Infantry and vehicles
are confirmed already parallel; the queue readout (count badges plus a head
progress fill) already exists and is left as is.

**Deferred to TICKET-P6-C3b (a golden-move sim change).** True two parallel
structure/defence queues on one Construction Yard, so a structure and a defence
build simultaneously, cannot be done client-side. One yard entity holds one queue
and one BuildProgress head and one ready slot; two parallel queues need a second
head and a second ready slot, which is new hashed per-entity state that moves all
24 goldens and bumps the save format. That is a deliberate golden-move wave with
its own ADR and neutralisation proof, filed as C3b, not smuggled into a wave the
tracker itself expected to be client-only.

## Alternatives rejected

**Do the parallel structure/defence queues now as part of C3.** It is the literal
remainder of line 45, but it is a hashed-state sim change (second build head,
second ready slot, save bump, golden regeneration) bolted onto a wave scoped and
expected as client-only. Rejecting it here and filing C3b keeps C3 hash-neutral
and green, and gives the parallel-queue machinery the design pass and
neutralisation proof it deserves rather than a rushed bundle. This is the same
split ADR-018 used (ship the client-side slot layer, defer cohesive movement) and
ADR-015 used (ship stances, defer formations).

**A left-click-cycles / dedicated cancel button per item.** A separate cancel
control per queued item is more UI for the same command. Right-click is the genre
convention, needs no new widgets, and maps one-to-one onto the existing per-type
buttons and the CancelProduce-by-index command. Rejected as heavier for no gain.

## Consequences

Easier: the production loop can be undone, with correct refunds, for the first
time in the client; a ready-to-place structure can be cancelled; discoverability
is a tooltip on every build item. No sim change, so no golden move and no save
bump.

Harder, or rather deferred: buildings and defences still share one serial queue,
so line 45's parallel structure/defence promise is only half met until C3b lands
the sim change. Recorded here and in the tracker so the wave is not mistaken for
discharging line 45 in full.

Hash impact: NEUTRAL. No sim code changes; the client issues an existing,
gate-tested command (the spawngate and prodgate already prove CancelProduce
refunds exactly). All 24 goldens stay byte-identical and the save format stays v7.

Gates: no new sim gate, because there is no sim change; the cancel path issues the
CancelProduce the spawngate/prodgate refund assertions already cover. Machine
check is both client builds clean and the full battery and goldens unmoved. Needs
a human: whether right-click cancel feels right in the running client, and the
go/no-go on scheduling C3b's parallel-queue sim change.
