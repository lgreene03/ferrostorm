# /game

The Godot 4.7 .NET client. Presentation only (ADR-001): it renders the sim and
sends commands, and it never simulates. `float` and `System.Random` are fine
here precisely because nothing here feeds back into the world.

- `scripts/SkirmishLive.cs` is the battle scene and by far the largest file.
- `scripts/MainMenu.cs` holds skirmish setup, the campaign and load browsers,
  and the LAN lobby.
- `scripts/VerifyRunner.cs` is the headless harness (see below).
- `Ferrostorm.Game.csproj` is the one project permitted to use nuget.org, via
  `game/NuGet.config`, because Godot.NET.Sdk lives there. The repo root clears
  every package source for the offline-first sim.

## Run the harness before you push a client change

```
./tools/verify-client.sh          # GODOT=/path/to/Godot if not at the default
```

It boots the REAL battle scene headless, drives it **from the joiner's seat**
(player 1) and asserts on what it does. CI runs it on every push, so a failure
blocks the merge either way, but locally it takes about a minute.

Driving it from seat 1 is deliberate and load-bearing. The recurring defect in
this client - **ten instances so far** - is a rule asked about the wrong player:
code that reads "player 0" meaning "me", which is correct for a single-player
host by luck and exactly inverted for a LAN joiner. Every one of the ten was
invisible from the seat the developer was sitting in. CI greps for the literal
seat as a second line of defence.

**Every client wave adds a check here.** If a change cannot be checked, that is
worth knowing before it ships rather than after: this client shipped four
separate features that looked implemented and were entirely dead.

## Two client rules worth knowing before you edit

- **The client PREDICTS and the sim DECIDES.** Ghosts, readouts and previews may
  anticipate an outcome; command paths must not filter on that prediction. A
  client-side filter is a second opinion that diverges the moment state changes
  between the prediction and the command.
- **One rule, one implementation.** If the client needs to know something the
  sim already knows, call the sim (see /sim's README). The duplicated-rule audit
  found nine live bugs of exactly this shape.
