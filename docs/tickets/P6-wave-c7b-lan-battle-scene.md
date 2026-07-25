# TICKET-P6-C7b: the LAN battle scene (DELIVERED)

Labels: persona:p3, gdd:s9, phase:6, owner:netcode + client-engineer +
architect, gdd:Q002

Status: **DONE, 2026-07-25**, across four slices: C7b-i the setup exchange
(ADR-022), C7b-ii LocalPlayerId through the battle scene, C7b-iii the
lockstep-driven frame loop, C7b-iv the Host and Join lobby. The delivery notes
are at the foot of this file. Everything machine-checkable in the Acceptance
section below is now checked; the one item that was always a human's remains
one, and is the last thing standing between this and a closed Q002.

Originally: FILED, pending. Split out of C7 by the C7a wave, which shipped the
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
  before any code. DONE, ratified 2026-07-25.
- **client-engineer:** the SkirmishLive integration and LocalPlayerId plumbing.
  DONE (C7b-ii and C7b-iii).
- **Luke:** the two-machine session that closes Q002 for good. **STILL OPEN, and
  now the only thing outstanding.**

---

## Delivery notes: C7b-iv, the Host and Join lobby (2026-07-25)

The last mile. The scene has been lockstep-driven and proven since C7b-iii; what
remained was a screen that connects two of them.

### What ships

**`MatchSetupBlob`** (Lan.cs) is the encoding ADR-022 deliberately left to the
client layer, so `Ferrostorm.Net` still knows only frames, ticks and hashes.
Seven fields, version-tagged. The map travels **repo-relative**, which is the
point rather than an implementation detail: two machines have different repo
roots, and one may be a packaged build whose `/data` sits beside the executable,
so an absolute path is the one form guaranteed not to resolve at the other end.

**`LanLobby`** owns hosting and joining, and lives outside MainMenu so the
harness can drive it with no scene at all. `Host` opens `Relay(2, 47801,
IPAddress.Any, setup: blob)` and connects its own client to **loopback** (its own
client is on its own machine; dialling the LAN address would fail on a machine
that allows inbound but not hairpin). `Join` parses `address` or `address:port`,
resolves a name on the connect thread, and builds through `worldFromSetup`, so a
joiner reproduces the host's world from the connection alone.

**Everything connects off the main thread**, and this is the wave's load-bearing
constraint rather than a style choice. The relay accepts every player before it
sends a single Hello and the client constructor blocks reading it, so a host's
own client does not return until the joiner arrives, which may be never. The
harness found this in C7b-iii by deadlocking two clients built one after another
on one thread. The menu polls `State` from `_Process`.

**The host builds from a round-tripped setup**, not from its own object. The
joiner can only ever have what came through the blob, so a field the encoder
writes and the decoder drops would produce a world only the host has: a desync at
tick 0 that the host cannot reproduce alone. Running both seats through the same
codec makes any such loss appear identically on both machines.

**`Relay.Stop()`** is new and small. A host who opens a lobby and backs out
leaves `Run` blocked in `AcceptTcpClient`, and on the fixed port a LAN host must
use, that holds the port bound: host, back out, host again would refuse with
"address already in use". `Run`'s accept loop now treats a failed accept as a
stand-down instead of letting it escape as an unhandled exception on a
background thread.

### Three defects fixed on the way, all of them reachable only once LAN was

**Pause stalled the peer.** `_paused` halts the accumulator drain, and the drain
is the only thing that submits this client's batch, so pausing stopped the OTHER
player's world dead with nothing on their screen to explain it. In LAN the menu
now opens over a running battle. The ticket's design called this out; it had no
way to be tested until two scenes could play each other.

**The pause menu told a LAN player their live match was a replay** ("saving is
disabled during replay playback"), because `CanSave` gained a second reason and
the note still named the first.

**`ModeLine`** now says LAN and says the battle is still running, because a
player who assumes otherwise walks away from a live match.

### Verification: 12 new harness checks, 28 to 40

The lobby pair runs in-process, both ends real: the host waits rather than
starting alone, the relay seats them opposite, and the joiner ends up holding
**the host's map, seed, treasury and sides** having been told nothing but an
address and a port. The claim that matters is asserted directly: both lobbies
built the identical world **before tick 0**, which is the precondition every
later tick depends on.

The codec is checked separately, because a field the encoder writes and the
decoder does not read is a joiner-only divergence, and finding it there is the
difference between a one-line fix and a desync hunt. An absent blob is refused
rather than guessed at.

The pause check was **proven able to fail**: restoring `_paused = true`
unconditionally turned it red (`60 -> 60`), and reverting restored green.

Full battery exit 0, 24 goldens byte-identical, both client builds 0 warnings.

### What this does NOT do

It does not prove two machines can play. Nothing in this process can, and Q002
has said so from the day it was filed. What is now true is that every line of
code between a player and that session exists and is verified; the remaining step
is Luke, two machines and a network.

No dropped-peer handling. If one player quits, the other's world stops advancing
(lockstep has nothing to advance with) and the desync notice does not fire,
because nothing desynced. That is honest but unexplained, and it wants its own
wave: a relay-side disconnect notice and a client-side "the other commander has
left" latch. Filed here rather than smuggled into this one.
