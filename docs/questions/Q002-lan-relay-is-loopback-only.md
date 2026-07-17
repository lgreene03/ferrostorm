# Q002: the LAN lockstep layer is loopback-only, so no two machines can play

Labels: persona:all, gdd:s11, phase:2-3, owner:netcode
Raised by: client-engineer, during TICKET-P5-SET-01 (settings, hotkeys and the LAN front door).
Decide-by: 2026-07-29 (before any further LAN client work, which is blocked on the answer, and before doc 18's LAN claim is repeated to anyone outside the project).

## Question

Should `Relay` and `LockstepClient` take a bind address and a host address, so a hosted game can be reached from another machine? And if so, does the client's real-time accumulator drive a blocking `AdvanceTick`, or does `AdvanceTick` grow a non-blocking poll?

Both halves are netcode calls. Neither was taken in TICKET-P5-SET-01, which shipped the host/join screen with HOST and JOIN disabled rather than guess.

## The finding

The lockstep layer cannot reach the network. It is not a bug in the protocol, which is sound and soaked; it is that both ends are pinned to loopback:

- `Relay`'s listener is constructed `new TcpListener(IPAddress.Loopback, port)` (Lockstep.cs:93). It accepts on 127.0.0.1 only, so no host on the LAN can connect to a hosted game.
- `LockstepClient`'s constructor takes `(int port, Func<ulong, World> worldFactory, ulong seed)` and calls `_tcp.Connect(IPAddress.Loopback, port)` (Lockstep.cs:239-241). There is no address parameter. A JOIN BY IP screen has nothing to dial.

Consequently the runner's `lan` mode is a **loopback soak, not a LAN soak**. It is a genuine test of the wire format, the per-tick merge, the command-delay scheduling and the hash comparison, and it passes 20 games with zero desyncs. It has never carried a packet between two machines, and it cannot.

## Why this matters beyond the code

docs/design/18-game-review-roadmap.md s1 describes the sim as having "a LAN lockstep layer soak-tested to zero desyncs". That sentence reads, to anyone who has not opened Lockstep.cs, as though LAN play works and only the UI is missing. The UI is now there (TICKET-P5-SET-01) and LAN play still does not work, because the distance was never the UI.

The roadmap's Phase D acceptance is honest about the test ("two in-process clients complete a scripted 1v1 with zero desyncs") and that test now passes from inside the client binary as well as from the runner, on the real match world rather than the synthetic fixture. But its "Needs a human: ... real two-machine LAN play" cannot be scheduled until this question is answered.

## Options

1. **Add the addresses.** `Relay(int playerCount, int port = 0, IPAddress? bind = null)` defaulting to `IPAddress.Loopback` so every existing caller is unchanged, and `LockstepClient(string host, int port, ...)` or an overload. Additive, small, and it cannot move a golden hash: nothing here is in the deterministic sim, and `sim/Ferrostorm.Net` is already outside `Ferrostorm.Sim`. It is still a change to a soaked file and it is netcode's to make, not the client's.
2. **Leave it loopback and say so.** Defensible if LAN is out of scope for the current phase. Then doc 18 s1's wording should be corrected to "a lockstep layer soak-tested over loopback", because the current sentence over-claims, and the LAN screen should keep saying what it says now.

## The second half, which is larger

Even with addresses, no networked match can be played until the battle scene's loop is lockstep-driven. `SkirmishLive` accumulates real time to 15 Hz and calls `World.Step` (SkirmishLive.RunOneTick). A lockstep client instead calls `SubmitCommands` then `AdvanceTick`, which **blocks** until the relay's merged batch for that tick arrives (`Monitor.Wait`, up to a 10 s timeout, Lockstep.cs:312-335). Blocking the frame on a socket is how a dropped packet becomes a frozen window. This needs either a non-blocking `TryAdvanceTick` that the accumulator can poll and skip on, or a client-side buffering scheme; both are design decisions with a feel cost, and both are netcode's.

## What TICKET-P5-SET-01 shipped in the meantime

- The host/join screen exists, with HOST and JOIN **disabled** and labelled with their own blocker rather than live and failing into a socket timeout.
- `LanSmoke` runs a relay and two `LockstepClient`s in-process over a real TCP socket on the real starting world, and reports pass or fail on the screen. Both worlds ended on 0x4EC4DA95C8D7F31A over 120 ticks with the relay silent.
- The HUD desync notice is wired to `NetSession` and proven to raise, waiting for the mode that drives it.

## Needed from whom

- **netcode:** the ruling on both halves above, and the ticket if the answer is option 1.
- **docs/design-review:** doc 18 s1's LAN sentence, either way.

## Resolution so far (2026-07-17)

The mechanical half is landed, in option 1's exact shape. `Relay` now takes
`(int playerCount, int port = 0, IPAddress? bind = null)` and `LockstepClient`
gains a trailing `IPAddress? address = null` on its constructor; both default
to loopback, so every existing caller (the runner's `lan` and `lanchaos`
gates, `LanSmoke`) compiles unchanged and runs the soak-tested configuration
byte for byte. The LAN screen's HOST and JOIN buttons now name the one
blocker that remains, doc 18 s1's sentence is corrected per this question,
and the full battery still exits 0 with the goldens untouched.

Still open, and still netcode's: the second half above. `AdvanceTick` blocks
on a `Monitor.Wait` for up to ten seconds and SkirmishLive's 15 Hz
accumulator cannot block the frame on a socket, so the battle scene needs a
non-blocking poll or a buffering scheme, both of which carry a feel cost.
Two-machine verification also remains open; no in-process test can provide
it. This question stays open for that half.
