# ADR-074: a boarding walk closes to reach, boards on arrival, and gives up by ADR-014's rule
- Status: Proposed (row P8-56, drafted by the implementer agent; for Architect sign-off, routed by the orchestrator). Not self-ratified, because it changes what a recorded LoadTransport command does.
- Date: 2026-10-04
- Deciders: Architect agent + Luke
- GDD/TDD feature served: P7-3's Carrier (docs/tickets/P7-parity-tracker.md), whose LoadTransport walks a squad that is out of reach; TDD s1 ("determinism is the product": replays and desync-free play), TDD s4's per-tick hash exchange, and TDD s9's replay compatibility promise; row P8-56 of docs/tickets/P8-formidable-tracker.md, found by P8-7.

## Context

**The defect.** LoadTransport boards a squad that stands within two cells of its own Carrier. From farther away it started a flow walk towards the Carrier with no explicit target and no attack-move, and left the re-issue to whoever gave the order. StepToward's crowd-arrival rule ends any such walk within four cells of its destination, outside the two-cell reach, so the squad settled short and never boarded, however often the order was re-sent. P8-7 measured it from 3, 4, 6 and 10 cells: nothing boarded in 300 ticks. The client's re-send could not help, because each re-send started the same walk again.

**What 26cdee1 did about it.** It marked the walk so that crowd arrival no longer ends it, and added a `BoardingSystem` that re-issues the order to the walker each tick: board in reach, follow the Carrier out of reach, lapse if the Carrier dies, changes hands or fills. That closed the defect, and it also left three things this ADR settles.

1. **A walk to a Carrier the squad cannot reach never ended.** A walled-off Carrier gives StepToward no route, so the walk stops inside movement every tick, and BoardingSystem restarted it every tick. The order outlived every movement backstop for the rest of the match.
2. **The two boarding paths left different residue.** BoardingSystem cleared ExplicitTarget before boarding and the in-reach command path did not, so a squad that boarded while holding an attack target, an attack-move or a Guard post despawned with that order still in its hashed fields.
3. **It changes what a command does**, so a replay recorded before it, or a LAN peer running the build before it, does not reproduce a match in which that command appears. No golden contains one, so no golden moved, but it is still a replay-compatibility change, which CLAUDE.md puts under an ADR.

## Decision

### 1. The walk is marked in ExplicitTarget's negative range

`World.BoardingWalk(c)` is `-2 - c`: ExplicitTarget holds "walking to board own Carrier c". Minus one keeps its meaning (no order to finish) and zero and above keeps its meaning (an attack target). Every reader that asks whether a unit has an attack target asks only whether the value is negative, so combat, capture, the ADR-071 dock route and the commander see a boarding walker exactly as they saw the plain walk it replaces. Three places must tell the walks apart, and each asks by name (`BoardingCarrierOf`): StepToward's crowd-arrival rule, which now settles only a walk whose ExplicitTarget is exactly -1, so a boarding walk closes to its two-cell reach as an attack closes to weapon range; `BoardingSystem`; and SeparationSystem's ADR-014 backstop (clause 3 below).

The point of the encoding is that it adds **no new hashed state and no new saved state**. ExplicitTarget is already hashed and already saved, so the save format does not change, and a save written before this ADR never holds a value below -1, so it loads unchanged and plays exactly as before.

### 2. Board() is the one place a boarding happens, and it ends every order

Both paths call `Board()`, and Board now writes the boarded unit's whole order state: not alive, not moving, ExplicitTarget -1, no attack-move, and any Guard or Patrol post cancelled (HoldFire stays, as it does across a Move: it is fire discipline, not an order). The walk path arrived in that state already, apart from its own walk marker; the in-reach path now arrives there too, so the despawned entity is the same whichever path boarded it. Because every boarding passes through Board, an event or counter that a boarding must raise is a one-line change there.

### 3. The walk gives up by ADR-014's rule

`BoardingSystem` keeps ADR-014's watchdog for the walk, in the two fields every entity already carries: `NearestApproachSq` is the walker's closest approach to its Carrier, measured to the Carrier where it stands this tick, and `NoProgressTicks` counts ticks since that last improved. The order's own handler zeroes both, as a Move does, so the walk's first tick seeds the watchdog. A walker that has not bettered its nearest approach for `NoProgressDeadline` ticks (210, fourteen seconds) **lapses to a plain stop**: ExplicitTarget -1, not moving, both counters re-armed, which is exactly the bench ADR-014 applies to any other walk, with the order dropped as well, so nothing restarts it.

For that walk, BoardingSystem is the counters' only writer: SeparationSystem's ADR-014 block skips a unit whose ExplicitTarget names a boarding walk. Counted in both places, the counter would advance twice a tick against two different references, and a bench in SeparationSystem alone would be undone the same tick by BoardingSystem re-asserting the walk. That is not hypothetical: with the skip removed, the chase stage below never gives up, and its engineer boards at tick 256, after the Carrier parks.

The leaky `StallTicks` net is left alone. It fires in the ordinary crowd round a Carrier, where the boarders ahead are about to despawn and clear the way, so for a boarding walk it pauses the walk for a tick and BoardingSystem resumes it. Only the monotone deadline is terminal.

## Alternatives rejected

**A side collection (a map from walker to Carrier).** The usual way this project carries optional per-entity state without moving the goldens: an entry folded into the hash only when present. It was rejected because it would be new hashed state and new saved state, so a save format bump and a new hash fold with its prune and its ordering rules, for information an existing hashed and saved field can hold exactly. Every guarded side collection in this sim (ADR-023's second lane, the gate and sabotage sets) needed a prune whose predicate must match its guard, and this needs none.

**Store the Carrier id itself, ExplicitTarget = c.** The simplest encoding, and wrong. An explicit Attack on your own Carrier already means exactly that value: CanTarget is a stealth test, not a hostility test, so a force-fire on your own transport is a legal order today, and it must go on meaning a force-fire rather than silently becoming a boarding. The negative range is the only part of the field no existing order uses.

**Refuse a LoadTransport whose Carrier is unreachable, on the order's tick.** It would need a flow query in the command handler, and it would be wrong whenever the route opens later (a gate opens, a bridge is repaired, the Carrier drives out). Giving up by the watchdog treats the boarding walk as every other walk is treated.

**Re-arm the watchdog whenever the Carrier moves.** It would stop a squad slower than its Carrier from giving up while the Carrier is being driven away (clause 3's consequence, below), but it reopens the defect for a Carrier that keeps moving where the squad cannot reach it, a patrolling one for instance, and the attack pursuit, which chases a moving target with the same machinery, does not re-arm either. Recorded as the reversal below rather than taken.

**Make StallTicks terminal for a boarding walk too.** Rejected because it fires in ordinary crowding: five squads ordered onto one Carrier press on each other at the door, and the ones behind could abandon a boarding the crowd was about to admit. That is reasoned from what the net measures, not measured.

## Compatibility

**What changes for a recorded command.** A LoadTransport whose squad is out of reach used to end in a squad standing about four cells off; it now ends aboard, or stopped after the deadline. A LoadTransport whose squad is in reach while holding an attack target, an attack-move or a Guard or Patrol post now despawns it without that order. Nothing else changes: no catalogue value moves, so `World.CatalogueChecksum` is unchanged, and no Entity field, save version or wire command is added.

**An old replay.** A .frep carries the seed, the setup, the catalogue checksum and the command stream, with a final hash, and no build or sim version. `Replay.AssertCatalogueMatches` refuses playback before tick 0 only on a catalogue mismatch, and this ADR moves no catalogue value, so **an old replay is not refused**. If it contains an out-of-reach LoadTransport, playback re-simulates a different match from the tick that command lands (the squad walks in and boards rather than standing off), shows that different match to the end, and only then reports it: the client's FinishPlayback banner reads REPLAY DIVERGED with both hashes. The client's playback is the only consumer of a stored replay; the runner's `replay` check records and replays a stream within one build, so it cannot see this. If it contains only the in-reach case, the difference is confined to hashed fields of an entity that has already despawned, which the order-reading systems skip by testing Alive first; the playback should look the same and still end DIVERGED. That expectation is reasoned, not measured. There is no checked-in replay corpus (TDD s9 describes one; none exists yet), so no stored replay is known to break.

**A mixed-build LAN game.** The lockstep hello is a catalogue handshake (`Wire.Check`, ADR-006): the relay refuses the game before tick 0 only when two catalogue checksums differ, and it carries no build or sim version. **So a mixed-build pair is not refused; the game starts.** The first out-of-reach LoadTransport on either side diverges the two states on the tick it lands, because ExplicitTarget is hashed and the two builds write different values into it. The relay compares state hashes every `HashInterval` ticks (30, two seconds) and broadcasts a desync on mismatch, so the divergence is **detected within 30 ticks of that command, not silent**, and the client latches its desync notice. The in-reach case diverges the same way, through the despawned entity's hashed fields. A game in which no squad is ordered onto a Carrier stays in step: every other changed line (the crowd-arrival test, IsBusy, the SeparationSystem skip, BoardingSystem) behaves as before unless some ExplicitTarget holds a value below -1, and only an out-of-reach LoadTransport writes one.

**Saves.** The format is unchanged, a save written before this ADR never holds a value below -1, and `transportgate` stage 12 shows a save taken mid-walk resuming on the same tick and hashing alike.

**Golden hashes.** Byte-identical, 24 of 24, ordered diff empty, measured at seed 2026: no golden scenario, commander or mission issues LoadTransport.

So no existing mechanism refuses either case up front. Both are caught after the fact, the replay at its end and the LAN game within two seconds of the first divergent command. That is the same trade every earlier sim behaviour change made before this project has a published build to be compatible with; a build or sim version in the hello and the .frep header is what would turn either into a refusal, and that is a separate decision no row has asked for.

## The proof

`transportgate` (in `match`, so CI runs it) gains three stages beside P8-56's eight, every boarding order issued once.

- **Walled off (stage 15).** A Carrier inside a sealed ring of blocked cells three out from its own, a squad ten cells away: the order lapses after **211 ticks**, which is the 210-tick deadline plus the tick that seeds the watchdog, never before the deadline; the squad is alive, stopped, nothing aboard, and is still stopped, with no order and unmoved, 210 ticks later. Before this ADR, the squad still held the walk (target -2, moving) at the 211-tick bound, and still held it 421 ticks after the order, having never moved.
- **Outrun (stage 16).** An engineer three cells behind a Carrier that is driving away down an open row, faster than an engineer walks: the order lapses after **211 ticks**, the engineer 7.22 cells behind and the Carrier still driving. With SeparationSystem's skip removed, it never lapses, and the engineer boards at tick 256 after the Carrier parks.
- **Two paths, one state (stage 17).** A squad on Guard engaging a hostile, and a squad attack-moving, each ordered aboard in reach, and a squad walking in from six cells: all three despawned entities hold the same order state (alive false, moving false, target -1, no attack-move, Aggressive, no post). Before this ADR the Guard squad despawned holding target 1, stance Guard and its post (21, 20). Removing one of Board's three clears at a time fails this stage alone, each on the field it clears: without the attack-move clear the attack-mover despawned with attack-move set; without the stance cancel the Guard squad kept stance Guard and its post; without the ExplicitTarget clear the Guard squad held target 1 against the walker's -2.

## What reverses it

- **The give-up's chase consequence** reverses if a playtest finds squads abandoning a boarding because their Carrier drove away from them for longer than fourteen seconds. The remedy is the rejected alternative above: re-arm the watchdog while the Carrier moves, with a separate bound so that a Carrier that moves forever cannot make the walk endless.
- **The deadline** reverses with ADR-014's own: a boarding walk is benched on that constant, not a copy of it.
- **The encoding** reverses if any reader appears that needs the Carrier of a boarding walk to survive the walk ending, or if a second kind of order wants the negative range. Then a side collection becomes cheaper than sharing the field, and it takes a save format bump and a fresh measurement of the goldens.
- **Board's order clearing** reverses only with a reason for a despawned unit to keep an order, which nothing has today: an unloaded unit is a new entity built from its hold record, not the old one revived.

## Consequences

**Easier.** A squad ordered aboard from anywhere boards with the order given once, and an order that cannot be carried out ends rather than running for the rest of the match. Every boarding passes through one function, so the Boarded event P8-10 adds (decision D34) is wired there in one line.

**Harder.** ExplicitTarget now has three meanings by range, and a new reader of it must ask the right question: "is there an attack target" is `>= 0`, "is there any order to finish" is `!= -1`, and "is this a boarding walk" is `BoardingCarrierOf`. The give-up couples the boarding walk to ADR-014's constant.

**Committed to.** Replays and LAN peers from before this ADR do not reproduce a match in which a squad was ordered onto a Carrier from out of reach, or boarded in reach while holding an order, and neither is refused up front: the replay reports DIVERGED at its end and the LAN relay flags a desync within 30 ticks.
