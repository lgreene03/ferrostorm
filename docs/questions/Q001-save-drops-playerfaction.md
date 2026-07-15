# Q001: World.Save drops _playerFaction, which ComputeStateHash hashes

Labels: persona:all, gdd:s6, phase:1, owner:sim-engineer
Raised by: client-engineer, during TICKET-P5-SAVE-01 (save/load and replays reach the player).
Decide-by: 2026-07-22 (before any further save-format work, and before the first public build, after which a save-format change is a compatibility break).

## Question

`World.Save` does not write `_playerFaction`, but `ComputeStateHash` hashes it. Should the field go into the save format (a format change, and the honest fix), or is the client's re-apply-from-map workaround the intended design?

## The defect

- `ComputeStateHash` hashes the faction for every player: `h.Add(_playerFaction[p]);` (World.cs:1776).
- `World.Save` writes, per player, only `_credits[p]`, `_eliminatedAnnounced[p]` and `_explored[p]`. The faction is never written.
- `World.Load` builds `new World(0, mw, mh, players)`, whose constructor sets `_playerFaction = new byte[players]` with the comment "everyone Directorate until told otherwise" (World.cs:216). Nothing tells it otherwise.

So for any map that declares a faction, `World.Load(save)` returns a world whose state hash differs from the world that was saved. This directly contradicts the save contract stated at the top of World.Serialization.cs: "loading reconstructs a World whose state hash equals the saved one".

**It is not cosmetic.** Faction gates what a player may build:
- World.cs:745: only Sodality may deploy the Veil Projector (`_playerFaction[c.PlayerId] != FactionSodality`).
- World.cs:789: production is refused when `GetUnitType(c.AuxId).Faction != _playerFaction[c.PlayerId]` ("not your side's hardware").

A loaded save therefore silently reverts every player to Directorate and lets them build the wrong side's hardware.

## Why both gates miss it

The `saveload` gate uses `BuildSkirmishWorld`, and the `campaignsave` gate uses `data/missions/mission-01.fmap`. Neither declares a faction, so `_playerFaction` is `[0, 0]` in both and the dropped field round-trips as 0 by luck. `grep -n faction data/maps/*.fmap data/missions/*.fmap` returns lines only in `mission-03.fmap` (`faction 0 0`, `faction 1 1`).

The gate hole is the more important half of this report: the field was always droppable, and no scenario could see it.

## Evidence

Measured offscreen through the client during TICKET-P5-SAVE-01, saving mission 3 at tick 500:

```
live world hash          = 0x820B9B21E28CAE21
bare World.Load hash     = 0x6DDA34B730F02E78     <- mismatch
map factions             = [0,0],[1,1]
after re-applying the map's factions = 0x820B9B21E28CAE21   <- exact match
```

Every other hashed field round-trips: entity count, all 49 hashed entity fields, `_rng.State`, Tick, Winner, ShortGameEnabled were compared field by field and are identical. The faction byte is the entire difference.

## Options considered

1. **Write `_playerFaction` in `World.Save` / read it in `World.Load`** (recommended). A few bytes per player, and it makes the file self-describing rather than dependent on its map. Costs a save-format change; existing save files are pre-public and disposable. Would not move any golden hash (`ComputeStateHash` is untouched), but that claim must be re-proved by the sim-engineer, not assumed from this ticket.
2. **Re-apply the faction from the map on load** (what the client does today, SkirmishLive.ResumeFromSave). Sound only because a faction is map content: `SetFaction` is called from exactly one place in the sim, `MapLoader.BuildWorld` (MapLoader.cs:153), and nothing mutates it mid-match. It is the same move the sim already endorses for mission tags ("the mission is rebuilt from the same map, then this state is restored on top", MissionRunner.cs:28-30). **It stops being correct the moment any feature lets a player change faction during a match**, and it leaves every non-client caller of `World.Save`/`World.Load` still broken.
3. Do nothing. Rejected: the save contract is stated in the sim's own header comment and is currently false.

The client took option 2 because a save-format change is not a client ticket's to make, and because CLAUDE.md forbids expanding a ticket's scope silently. Option 1 is still believed to be the right answer.

## Needed from the sim-engineer

1. A ruling on option 1 vs option 2, and if option 1, deletion of the client workaround in `SkirmishLive.ResumeFromSave` along with the assertion in the verification probe that pins the defect.
2. **Regardless of the ruling: close the gate hole.** Either extend the `campaignsave` gate to a map that declares factions, or give the `saveload` gate a `SetFaction` call before it saves. A field that no scenario can see is a field that can be dropped again.
3. A check of whether any other hashed-but-unsaved field exists. This one was found by accident, from the client, by a ticket that was not looking for it; the same method (diff every hashed field across a round trip) would find the rest.
