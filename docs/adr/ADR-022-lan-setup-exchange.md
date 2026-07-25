# ADR-022: the LAN setup exchange

- Status: Ratified (Architect + netcode drafted 2026-07-25; ratified under
  Luke's standing directive to build out the C-series. Reserved on 2026-07-24 by
  the C7a wave, which found the gap while designing C7b)
- Date: 2026-07-25
- Deciders: Architect + netcode + Luke
- GDD/TDD feature served: GDD s9 mode 3 (multiplayer); docs/questions/Q002;
  P6 campaign tracker wave C7b, first slice

## Context

C7a shipped the non-blocking poll, the technical half of Q002's remainder. While
designing C7b, the battle-scene half, the recon found a hole nothing had noticed:
**the protocol has no way to tell a joiner what match it is joining.**

`LockstepClient` took its seed from its own caller. ADR-006's catalogue Check
guards /data, so two players running different unit stats are refused before tick
0, but nothing carries the seed, the map, or the factions. Two clients agreeing
on those was an article of faith arranged outside the protocol: fine for a soak
where one process starts both ends with the same literal, and impossible for a
real join, where the joiner knows only an IP address.

A joiner that builds a different world does not fail cleanly. It plays a
divergent match until the first order whose outcome differs, and then reports a
desync, which reads as a netcode bug rather than a lobby one.

## Decision

**The host's match setup rides in the Hello frame as a length-prefixed blob, and
the blob is OPAQUE to the net layer.**

1. **Opaque, deliberately.** `Relay` takes `byte[]? setup` and broadcasts it
   verbatim; `LockstepClient` exposes it as `Setup` and never inspects it.
   Ferrostorm.Net knows nothing about maps, factions, seeds or MatchSetup, and
   gains no reason to. The client layer, which already owns MatchSetup and its
   sidecar serialisation, encodes and decodes it. This keeps the net layer's
   surface exactly as small as it was.

2. **A length prefix, and a zero length is legal.** A relay started without a
   setup writes length 0, so every pre-ADR caller (the `lan` and `lanchaos`
   soaks, `LanSmoke`, `lanpoll`) is byte-compatible and unchanged. The client
   reads the tail defensively on length, so a short body yields an empty array
   rather than an exception.

3. **A joiner builds FROM the blob, not from a seed it was handed.**
   `LockstepClient` gains an optional `Func<byte[], World> worldFromSetup`. When
   supplied it is used instead of the seed factory, which is exactly what a
   joiner passes. The host keeps using the seed factory, because the host is the
   one who chose the seed.

4. **The Hello, not a new frame type.** The setup must arrive before the client
   builds its world, and the world must exist before the catalogue Check is sent;
   the Hello is the only frame that already sits in that window. A new frame type
   would need its own ordering rules for no gain.

## Alternatives rejected

**A new Setup frame between Hello and Check.** Cleaner to read in a packet
capture, but it adds a round trip and a new ordering constraint to a handshake
that already has a working sequence, to carry data the Hello can hold.

**Teach the net layer about MatchSetup.** Would let the relay validate the setup
and refuse an impossible one. Rejected because it drags map paths, faction ids
and campaign indices into Ferrostorm.Net, which today knows only frames, ticks
and hashes, and would have to change every time the client's setup grows a field.
The opaque blob costs one length prefix and keeps that boundary intact.

**Derive the seed from the catalogue checksum or a handshake hash.** Removes the
blob but makes the seed unchooseable, which loses "host a match on this map with
these factions" entirely.

## Consequences

Easier: a joiner can build the host's world from the connection alone, which is
the precondition for any real Host and Join screen. C7b's remaining work is now
pure client mechanics with no format decisions left in it.

Harder: nothing measurably. The blob is inert when absent, and the client layer
owns its encoding, which is where the setup already lives.

Hash impact: NEUTRAL. Ferrostorm.Net is outside the state hash entirely; no sim
file changed. All 24 goldens byte-identical, and the `lan`, `lanchaos` and
`lanpoll` gates pass unchanged, which is the compatibility claim demonstrated
rather than asserted.

Gates: a `lansetup` gate proves the host's setup reaches both clients in the
Hello and that a joiner handed a **deliberately wrong** seed still builds the
host's world exactly, running 120 ticks to identical hashes with no desync. It
also asserts the negative control, that the wrong seed genuinely builds a
different world, so the match is the exchange working rather than a coincidence
of two seeds agreeing.

## What this does NOT do

It does not make LAN playable. The battle scene still does not drive its frame
loop from the lockstep poll, `LocalPlayerId` is still hardcoded to 0 in roughly
forty places, and Host and Join are still disabled in the menu. Those remain
TICKET-P6-C7b, which now has no ADR-gated work left in it. Real two-machine
verification was always, and remains, a human step.

**POSTSCRIPT, 2026-07-25: every limitation named in the paragraph above has
since been lifted.** C7b-ii plumbed the seat through ninety-three sites (with a
CI guard now enforcing it), C7b-iii made the frame loop lockstep-driven, and
C7b-iv shipped the Host and Join lobby. C7c added a dropped-peer notice. The
paragraph is kept as written because it was true at ratification; only the
human two-machine session is still owed.
