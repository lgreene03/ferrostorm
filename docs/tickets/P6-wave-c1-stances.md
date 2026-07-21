# P6 Wave C1a delivery notes: unit command stances

Closes the C1a row of the P6 campaign tracker under ADR-015 (ratified), which
gives every unit a per-instance command STANCE governing three behaviours beside
today's default: hold-fire, guard and patrol. It resolves Q003 (doc 18's H hold
had no sim behaviour to bind to) and closes P4-PORT-01 (patrol and guard) from
the portability audit. Formations (P4-PORT-05) split to their own ticket,
TICKET-P6-C1b, filed alongside this wave exactly as ADR-015 scopes. One golden
regeneration, authorised by the ADR's hash-impact clause and proven mechanical
below. Plan comment first (CLAUDE.md workflow rule 2), delivery notes and the
standard footer at the end.

## Plan

labels: persona:p2 gdd:s10 phase:6 owner:sim-engineer + client-engineer + architect (Balance under A11 for the leash number)

**Built on the WIP, did not redo it.** The interrupted session's checkpoint
(the old a28d256) already carried the Stance enum, the six Entity fields, the
SetStance command and its handler, the CancelPositionalStance helper, the three
command-cancel call sites and the one CombatSystem hold-fire guard, and all of it
matched ADR-015. What it lacked was the StanceSystem body (so the branch did not
build), the hash of the six fields and the save v7 serialiser. Completing it was
the honest path; redoing correct work would have been waste. The WIP sim commit
was amended into one complete, buildable commit rather than layered on.

**The rule is the ADR's, exactly.** Stance : byte { Aggressive = 0, HoldFire = 1,
Guard = 2, Patrol = 3 }. Zero is the struct default, the neutral serialised value
and today's behaviour, so nothing changes until a SetStance sets a non-default
value, which no scenario, AI or save does. The six new fields (Stance, PostX/Y,
PatrolX/Y, PatrolOutbound) are appended after the ADR-014 no-progress tail in
declaration order, which is hash order and save order, never inserted.

## The stance layer as built

**Hold-fire is fire discipline and only that.** The single combat change is that
CombatSystem's auto-acquire branch (the else taken when ExplicitTarget < 0) now
runs only when the stance is not HoldFire. A HoldFire unit does not auto-acquire
and does not fire even with an enemy in weapon range; an explicit Attack order
still fires because the explicit-target branch is untouched, so Q003's "defends
only if explicitly ordered to attack" holds for free. It persists across a Move,
which is the whole of Q003's engineer walking past the sentry it must not wake.
That else-branch guard is also the load-bearing neutralisation point: for the
default Aggressive stance the condition is always true, so seed-2026 behaviour is
byte-identical to before this ADR.

**Guard is a leashed hold that engages and returns.** A new StanceSystem step,
ordered in World.Step immediately after OrderDispatchSystem and before
MovementSystem, finds each Guard unit the nearest targetable enemy within the
unit's own Sight of its POST (the leash, ties to lower id) and, if the unit is
armed, sets it as ExplicitTarget. That routes the engagement through the
battle-tested explicit-attack machinery, which closes to weapon range and fires
and is immune to the crowd-arrival shortcut, so even a short-range guard closes
properly. With no enemy in the leash the unit drops any standing target and walks
back to the post, settling within the four-cell crowd radius. The leash is the
unit's own Sight, not a new stat, so guard needs no schema change and no magic
constant; Balance may promote it to a dedicated leash stat later with no wire
change (the leash is read, never sent).

**Patrol is deterministic two-point cycling.** The SetStance handler kicks off
the outbound attack-move leg toward endpoint B (the ordered point, clamped as Move
does), with endpoint A pinned to the unit's live position. StanceSystem watches
for the leg to complete: AMove clears only at or near the endpoint with the area
clear, which is precisely "reached this endpoint", so on that tick it flips
PatrolOutbound, re-arms the no-progress backstop and attack-moves back. Engaging
anything met en route and resuming afterwards is inherited from the attack-move
machinery whole; patrol adds only the flip.

**The transitions.** One wire command, SetStance = 17, absolute rather than a
toggle so it is idempotent and replay-robust; the client turns H into "aggressive
if already hold-fire, else hold-fire". Only a Unit accepts it. A Move, PathMove,
AttackMove, Stop or Attack order cancels Guard and Patrol back to Aggressive (a
new activity supersedes a standing positional one) but PRESERVES HoldFire,
because fire discipline is a persisting preference and Q003's engineer needs it to
survive the very Move that carries it past the sentry.

**State, hash and save.** All six fields are HASHED (ComputeStateHash) and
SERIALIZED (save v7, SaveMagicV7 = 0x534C4137), following the ADR-007 rally
precedent for mutable per-entity command state: a stance changes what a unit does,
so two clients must agree on it exactly, unlike ADR-012's immutable-and-unhashed
FerriteCap. v1..v6 saves load with Stance defaulted to Aggressive and the position
fields zero, which is exactly what those saves meant, so old saves and replays
resume identically. The runner's DowngradeSave surgery was updated to read a v7
stream and strip the 34-byte stance tail, keeping the v6/v5/v4 downgrade tests
alive.

## The client controls (presentation only, ADR-001 intact)

Three rebindable actions join the input surface, defaulting to H hold-fire, G
guard and Q patrol (all free keys). Each is a presentation-only issue of the one
SetStance command; the client reads the live stance back for the readout and owns
none of the behaviour. Hold-fire is an absolute toggle (all-held releases to
aggressive, otherwise all to hold-fire). Guard sets a leashed hold in place,
carrying no point because the sim reads the post off the unit's live position.
Patrol arms the attack-move two-step: the key selects the order, the next left
click supplies the far point, with the attack cursor and attack-colour
acknowledgement because a patrol leg is an attack-move. The armed orders are
mutually exclusive and cancel restores the selection. The three actions are added
to project.godot's [input] block and to Settings.Bindable, the single list the
settings scene, the conflict detector and ApplyBinds all iterate, so they rebind
and clash-check like every other key. An own combat unit's readout now names its
live stance and the three stance keys from their live bindings (the SET-01 rule),
so a rebind never leaves the readout lying. The MCV keeps its deploy readout and
is not offered a stance.

## The golden regeneration, explained

Hashing a per-entity field shifts every scenario's fingerprint mechanically, so
ALL 24 seed-2026 goldens move together (the ADR-014 rally pattern, not ADR-012's
selective one). The neutralisation is proven twice by identity, both on scratch
builds reverted before the golden commit:

- **Behaviour is inert at seed 2026.** With the StanceSystem stepped inert and
  the hold-fire combat guard forced off but the six fields still hashed, the
  seed-2026 goldens are BYTE-IDENTICAL to the wave build across all 24 scenarios.
  This proves the stance behaviour never fires at seed 2026 (no scenario sets a
  non-default stance).
- **The move is the field-append alone.** Additionally un-hashing the six fields
  recovers the PRE-WAVE goldens BYTE-IDENTICAL across all 24. This proves the only
  thing that moved the wave goldens is the mechanical hashed-field append, with
  zero behavioural change at the golden seed.

Together: pre-wave equals (un-hashed + behaviour-off), the wave equals (hashed +
behaviour-off) because the behaviour never fires, and the sole difference between
them is the hashed-field append. One regeneration under ADR-015, cross-platform
proven by the determinism CI on Windows and Linux.

## The new gate

StanceGate joins the battery (additive, a standalone mode and a Match stage, never
a golden scenario, so the golden list stays 24 lines). It proves, all at exit 0:

- A hold-fire unit with an enemy two cells inside weapon range does NOT fire (enemy
  10/10 hp) while an identically-placed aggressive twin auto-acquires and kills it.
- A guard leaves its post to close on and kill an intruder inside its leash (nine
  cells out, well beyond weapon range), then returns within the crowd radius holding
  no target, its Guard stance intact across the cycle.
- A patrol cycles its two waypoints (endpoints pinned A=origin, B=ordered), flipping
  PatrolOutbound at least three times across 600 ticks and visiting near both ends.
- A v7 save round-trips all three stances and their geometry and resumes bit-exact;
  a v6 downgrade of an aggressive world loads every unit Aggressive, hash-identical.

## Verification (local, real evidence)

- Full battery `match 2026` exit 0 (selftest, determinism 24/24, every scenario
  assertion, defence load, catrefuse, spawngate, prodgate, regrowthgate, stancegate,
  lan). `match 900913` exit 0 too.
- The exact CI golden check byte-identical: `golden 2026` diffed against the
  committed sim/golden-hashes.txt minus comments, identical.
- Five-seed determinism suite all 24/24 double-run identical: 2026, 31337, 424242,
  777, and 900913. Seed 900913 (fixed by ADR-014's no-progress backstop) does NOT
  regress. The 20-game LAN soak completed with zero desyncs.
- saveload (v7, 14601 bytes) and campaignsave (v7) round-trip bit-exact; replay
  reproduced a 3000-tick AI match bit-exactly; spectate and lanchaos green.
- Both Godot client builds 0 warnings (Debug and ExportRelease). project.godot's
  [input] and other sections intact; no user save, replay or settings written.
  FogOfWar.cs and Minimap.cs untouched (a concurrent local session owns them).
- AI-vs-AI resolves normally at seed 2026 and 900913 (the AI does not use stances
  yet; the game is unbroken).

## Changed / Assumed / Needed next

**Changed.** Sim (commit 0562aaa): Stance enum, six Entity fields, SetStance
command and handler, StanceSystem, the one CombatSystem hold-fire guard,
CancelPositionalStance and its three call sites, the hash, save format v7, and the
runner's DowngradeSave surgery plus a StanceGate. Client (be3fd76): three [input]
actions and Settings.Bindable rows, ToggleHoldFire/IssueGuard/ArmPatrol/CommitPatrol
in SkirmishLive, the patrol-armed click and cancel paths, the patrol cursor, and
the stance readout. Goldens (a9d041a): all 24 hashes regenerated. Docs (this
commit): these notes, Q003 resolved, the ledger and the tracker, and the C1b
formations ticket filed.

**Assumed.** Guard's leash is the unit's own Sight per ADR-015, no schema change;
Balance owns any future promotion to a dedicated leash stat under A11, with no wire
change when they take it. H/G/Q are sensible free defaults, all rebindable.
Formations stay out of scope, deferred to C1b, honouring the ADR's split.

**Needed next (from whom).** Architect sign-off on the golden move (a golden change
is a replay-compatibility break requiring ADR + sign-off; ADR-015 is that ADR, and
the orchestrator verifies and merges). Balance may set a dedicated guard-leash stat
later (A11). The AI-engineer may teach the AI to adopt stances through the same
SetStance command it already uses for everything else, with no format change.
Game-designer and docs may close doc 18's "H hold" row now that it binds to a real
behaviour. C1b (formations, P4-PORT-05) is filed and pending.
