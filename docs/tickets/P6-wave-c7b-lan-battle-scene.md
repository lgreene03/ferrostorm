# TICKET-P6-C7b: the LAN battle scene (filed pending)

Labels: persona:p3, gdd:s9, phase:6, owner:netcode + client-engineer +
architect, gdd:Q002

Status: FILED, pending. Split out of C7 by the C7a wave, which shipped the
non-blocking `TryAdvanceTick` poll and its `lanpoll` chaos gate (the technical
question Q002's remainder posed). This ticket is the battle-scene integration
that makes two-machine play reachable from the menu. It is the widest client
change since Phase A, which is exactly why it is its own wave.

## The design (from the C7a recon, verified against the code)

- **The frame loop.** SkirmishLive holds a `LockstepClient? _net`. In the
  accumulator drain (the `while (_accumulator >= TickSeconds)` loop), when
  `_net != null`: submit `_pending` EXACTLY once per world tick (a
  `_lastSubmitTick` guard; the relay counts batches per tick and a resubmit
  corrupts the merge), then `TryAdvanceTick`; on false, break the drain and
  clamp `_accumulator` to about one TickSeconds so a recovered stall does not
  fast-forward in a burst; on true, run the existing post-step work (SnapshotNow,
  fog, effects, victory) against `_net.World`. Clamp `_renderTime` so it never
  runs past the last snapshot during a stall. Mirror `_net.DesyncNotified` into
  `NetSession.NoteDesync` each frame; the latched HUD notice already displays it.

- **LocalPlayerId.** SkirmishLive hardcodes the human as player 0 in roughly
  forty places (selection filters, fog update, power HUD, sidebar producers,
  MCV deploy, alerts, auto-harvest). Plumb a `LocalPlayerId` (default 0)
  through them; the wire side is already safe (SubmitCommands restamps ids)
  but the JOINER is player 1 and cannot select, see fog, or read power without
  this. The widest and most mechanical part of the diff, and the part where
  every miss is a subtle joiner-only bug: verify with a scripted match driven
  from the player-1 seat.

- **No AI, no mission.** `_enemy` and `_mission` stay null in a LAN match (two
  humans). `CanSave` gains `&& _net is null` (a .frep is a command stream from
  tick 0 and a save cannot be honestly resumed into a live lockstep session).
  Replay recording disabled in LAN for this wave (recording the merged stream
  is a later option, since RunOneTick already records restamped commands).
  Pause must NOT stall the lockstep: the escape menu in LAN does not set
  `_paused`; the client keeps submitting empty batches.

- **The setup exchange (the ADR).** The catalogue Check protects /data, but
  nothing exchanges seed and map: `LockstepClient` takes the seed from its own
  caller, so a joiner cannot build the identical world. The fix is a
  host-supplied setup blob on the Relay, appended to the Hello frame, which the
  joiner uses to build its MatchSetup. That is a WIRE-FORMAT change and takes
  the reserved ADR-022 (claimed in docs/adr/ADR-open-queue.md) before code.

- **The menu.** Enable HOST (spawn `Relay(2, fixedPort, IPAddress.Any)`
  in-process per doc 18 Phase D, plus a local LockstepClient to loopback) and
  JOIN (address field, dial the host), then load Skirmish with `_net` set and
  `NetSession.Active = true`.

## Acceptance

Machine-checkable: the existing lanpoll gate stays green; a new in-process
two-client scripted match through the REAL SkirmishLive code path (the
AutoStep=false offscreen-verification hooks exist for exactly this) runs to
identical final hashes with zero desyncs, driven from BOTH seats so the
LocalPlayerId plumbing is proven from the joiner's side; the full battery and
both client builds stay green; goldens byte-identical (Ferrostorm.Net and the
client are outside the state hash).

Needs a human: real two-machine play over a real network, which Q002 has always
said no in-process test can provide, and the feel of the stall behaviour (the
render-on-while-sim-waits under a laggy peer).

## Needed from whom

- **architect + netcode:** ADR-022 (the Hello setup exchange, a wire change)
  before any code.
- **client-engineer:** the SkirmishLive integration and LocalPlayerId plumbing.
- **Luke:** the two-machine session that closes Q002 for good.
