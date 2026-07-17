# ADR-006: /data becomes the runtime source of gameplay numbers

- Status: Ratified (Architect authored 2026-07-17; ratified by Luke 2026-07-17 under the directive "design out and build all these", covering this ADR as drafted)
- Date: 2026-07-17
- Deciders: Architect agent + Luke
- GDD/TDD feature served: TDD s11 (data-driven definitions); docs/design/01
  line 35, the modder persona's primary need, "Data-driven unit definitions in
  plain text"; CLAUDE.md data conventions ("All gameplay numbers live in /data
  as YAML... Hand-editing stats in code is forbidden")

## Context

The shipped client never loads /data. Every caller of the DataLoader path
lives in the gate harness (sim/Ferrostorm.Sim.Runner/Program.cs:1582-1662);
`grep -rn "DataLoader|RegisterUnitType|RegisterStructureType" game/scripts/`
returns one comment. A live match is built by `BuildStartingWorld`
(game/scripts/SkirmishLive.cs), which registers nothing, so it runs off the
compiled catalogue: `_unitTypes` (World.cs:289-308) and `SeedStructureTypes()`
(World.cs:385). Editing a YAML file changes the gate battery's opinion of the
game and changes nothing any player experiences.

The consequence is structural, not cosmetic. CLAUDE.md's data convention is in
force in the test harness and not in the product. Every balance change today is
a synchronised two-place edit (YAML and compiled catalogue), which the selftest
round-trip enforces but which defeats the point of the YAML. The repair
vehicle, the barracks tech tree, and every felt stat change queue behind this
decision (doc 23, Wave 3, TICKET-P5-DATA-01).

This was found by audit on 2026-07-16, adversarially verified, and it also
explains an oddity in the project's history: the BD-06 "structure catalogue
into /data" work was hash-neutral precisely because nothing at runtime read it.

## Decision

The client loads /data before tick 0, exactly as the runner does. In
`BuildStartingWorld`, before any spawn, the catalogue is registered from
/data/units and /data/buildings resolved through the existing
`GameFiles.RepoRoot` idiom (GameFiles.cs:35-38, which exists for precisely
this). `RegisterUnitType` already throws if `Tick != 0`, which is the correct
guard and is kept.

Three subsidiary commitments, priced rather than hoped:

1. **The desync surface is closed before it opens.** The catalogue is
   deliberately not hashed (World.cs:283-284), so two LAN clients with
   different YAML would agree on every state hash while playing different
   games. Mitigation: a catalogue checksum (FNV-1a over the canonicalised
   registered defs, computed by the sim, not over file bytes) exchanged in the
   LAN hello and asserted before tick 0; a mismatch refuses the game with a
   readable message naming both checksums. A new gate scenario proves a
   mismatched catalogue refuses rather than desyncs. Replays and saves record
   the checksum for the same reason and refuse with the same message.

2. **File IO failures land as messages, not crashes.** A missing directory, a
   malformed YAML or a schema violation at client start fails into a readable
   error naming the file and line, and the menu stays usable. A shipped build
   without /data present must say so, not throw FormatException.

3. **The compiled catalogue remains, demoted to reference.** It stays as the
   selftest's round-trip truth (the guarantee that /data and the compiled
   defaults agree at HEAD) and as the sim's zero-dependency fallback for
   harness callers that never touch disk. It stops being what players play.

## Alternatives rejected

**(b) Demote /data to a reference surface and amend CLAUDE.md.** Cheaper and
honest: one scoping sentence in CLAUDE.md, no new failure modes, no desync
surface. Rejected because it forecloses a shipped feature to avoid a solvable
problem: it permanently costs the modder persona the thing docs/design/01
names as their primary need, converts every future balance patch into a code
change followed by a rebuild, and quietly repeals the project's own data
convention rather than enforcing it. The checksum mitigation in the decision
is a bounded, testable answer to the one real risk (b) avoids.

**Loading /data lazily or per-scene rather than before tick 0.** Rejected
because `RegisterUnitType` correctly refuses registration after tick 0, and
any later loading reintroduces the two-catalogue ambiguity this ADR exists to
end.

**Hashing the catalogue into ComputeStateHash instead of a lobby checksum.**
Rejected because it moves all 24 golden hashes for zero behavioural change,
which is a replay-compatibility break bought for nothing the lobby checksum
does not already provide.

## Consequences

Easier: balance patches become YAML edits that players feel; the repair
vehicle and the tech tree (doc 23 Waves 3 and 6) unblock; the modder persona
gets its primary need; the gate battery and the shipped game finally test the
same object.

Harder: the client gains file IO, a parse-error surface, and a startup
dependency on the repo layout that packaged builds must answer (the ADR
accepts `GameFiles.RepoRoot` for the development build and leaves packaged
distribution pathing to the release ticket that already owns it); LAN gains a
pre-game handshake step.

Committed to: the catalogue checksum in the LAN hello, the save format and
replays; the refuse-with-message behaviour on any mismatch; a gate scenario
for the mismatch path; readable startup errors for missing or malformed data.

Hash impact: NONE at adoption. The runner already loads /data and the values
are equal today by selftest guarantee, so registering the same values in the
client moves nothing. The 24 goldens must be byte-identical after the
implementing ticket lands, and that is its acceptance criterion.

Gates: TICKET-P5-DATA-01 (doc 23 Wave 3) implements this ADR. Ratification
unblocks it; the ticket must not start before ratification.
