# P6 Wave C3 delivery notes: the sidebar cancel (four-queue, part)

Closes the client-only half of the C3 row of the P6 campaign tracker under
ADR-020 (ratified): right-click cancel and refund on every sidebar build item, the
production-loop half the client never had. NEUTRAL hash impact, no sim change. The
literal GDD-45 remainder (two parallel structure/defence queues) is filed as C3b.

## Plan

labels: persona:p2 gdd:s5 phase:6 owner:client-engineer + game-designer

The design pass (ADR-020) read GDD line 45 against the code and split the wave.
Line 45 wants two parallel building queues (structures/defences), two unit queues
(infantry/vehicles), per producer, with rally points. Already done: four tabs,
per-structure rally (ADR-007), and infantry/vehicles ARE two parallel queues (they
read different producers). Missing and client-fixable: the client issued
CancelProduce NOWHERE, so a player could queue but never cancel a queued item or a
ready-to-place structure. Missing and sim-side: buildings and defences share one
serial yard queue (deferred to C3b, a golden move).

## What C3 built (client only, ADR-001 intact)

Right-click on any sidebar build item cancels one of that type, through the sim's
existing CancelProduce (pay-as-you-build refund):

- **Units**: `SkirmishLive.CancelUnit` finds the own producer of the unit's
  produced_at that actually has the unit queued and cancels its most recent one
  (`LastQueueIndexOf` reads the queue from the back). The head refunds pro-rata; a
  not-yet-started queued item was never charged, so it refunds nothing, which is
  correct pay-as-you-build behaviour.
- **Structures**: `SkirmishLive.CancelStructure` cancels at the yard. If a
  structure of that type is READY to place, it cancels the ready slot for a full
  refund (the sim's ready-first rule); if a DIFFERENT structure is ready (which
  pauses the queue), it declines, because the player cancels the ready one by
  right-clicking its own button. Otherwise it cancels the most recent queued one
  of that type. Barriers have no queue (bought and placed outright), so they get
  no cancel.

The wiring is one new optional `onCancel` on `Sidebar.MakeButton`, a right-click
`GuiInput` handler that calls it, and a tooltip on every build item advertising
"Left-click: build   Right-click: cancel / refund". The two button call sites
(units, non-barrier structures) pass the cancel delegate; nothing else changed.

## Why the goldens do not move

No sim code changed: `git diff sim/` is empty. The client issues an existing,
gate-tested command (CancelProduce), whose refund correctness the spawngate and
prodgate already prove. All 24 goldens stay byte-identical and the save format
stays v7.

## Verification (local, real evidence)

- Both Godot client builds 0 warnings (Debug and ExportRelease).
- `git diff sim/` empty; the exact CI golden check byte-identical across all 24
  rows; full battery `match 2026` exit 0 (a function of the unchanged sim, run to
  honour the standing law rather than assume it).

Machine-checkable acceptance is the build plus the sim's existing CancelProduce
refund gates. Needs a human: whether right-click cancel feels right in the running
client (the classic-genre expectation), and the go/no-go on scheduling C3b.

## Changed / Assumed / Needed next

**Changed.** Client: `Sidebar.MakeButton` gains an optional onCancel wired to a
right-click GuiInput plus a tooltip; the unit and non-barrier structure buttons
pass it; `SkirmishLive` gains `CancelUnit`, `CancelStructure` and
`LastQueueIndexOf`. Docs: ADR-020, this file, the C3b ticket, the tracker and
ledger.

**Assumed.** Right-click is the cancel affordance (genre convention), cancelling
the MOST RECENT of a type. The ready-first rule for structures is the sim's, and
declining when a different structure is ready is the least surprising client
behaviour.

**Needed next (from whom).** architect + sim-engineer: C3b, the two parallel
structure/defence queues on one yard (a golden-move sim change, filed pending in
docs/tickets/P6-wave-c3b-parallel-structure-queues.md). A per-item queue strip
with per-item progress is a possible later client polish, not needed for the
cancel.
