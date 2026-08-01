# ADR-047: two refineries, the first golden regeneration, and a gate that was measuring itself
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised golden-hash regeneration)
- GDD/TDD feature served: GDD s4's stated equilibrium; doc 24 C9's AI half; P7-7a

## Context

GDD s4 states the designed economy outright:

> **Design intent:** A player **floats at 2 refineries / 3 harvesters** on one
> base; expansion or raiding decides who out-produces whom.

The commander ran **one of each**. `TICKET-AI-03`'s rung reads
`refineryCount < cyCount`, one refinery per base, and it has held the AI at half
the designed economy since the ladder existed.

**ADR-041 measured this while refusing something else**, which is the part worth
keeping. That ADR refused a credit ceiling, and the probe it built to justify the
refusal found the opposite problem:

> The last two columns say **1 refinery and 1 harvester**, sustained across all
> 9000 ticks... **The design intent is not being met, and it is not being met in
> the direction opposite to the one P7-6 assumed.** The economy is not
> overflowing; it is undersized.

This row is the one that refusal pointed at.

## The measurement, before and after

`economyprobe`, unchanged, on the same 9000-tick AI-versus-AI match:

**Before**

```
   tick   credits0   credits1   refineries0   harvesters0
   1500       3329       4029             1             1
   3000       1939        720             1             1
   4500          0          2             1             1
   6000        650       1234             1             1
   7500         19        820             1             1
   9000          1       2172             1             1
```

**After**

```
   tick   credits0   credits1   refineries0   harvesters0
   1500       2350       3050             2             1
   3000       2870       3103             2             2
   4500       1992       3022             2             2
   6000       1292       1802             2             2
   7500       4018       1707             2             2
   9000       3122          0             2             2
```

Read the credit column rather than the refinery column. Before, the treasury
touches **0, 2, 19 and 1**: a commander spending every credit the moment it
arrives and never having any. After, it moves between roughly **1300 and 4000**.
That is the difference between a starved economy and GDD s4's float, and it is
the reason this row matters more than the count suggests.

## Decision

`RefineriesPerBase = 2`, a **compiled constant** rather than a `/data` rung knob.

Rejected: making it a difficulty rung value beside `harvesters_per_refinery`.
That would make "plays the economy the game is designed around" a difficulty
setting, when it is the game's own designed equilibrium and the same for every
commander. The ladder's knobs are what make one commander *better* than another;
this is what makes all of them play the game as written.

Charter A11 was checked and does not bind: this is **AI behaviour**, not a stat.
No unit cost, rate or hit point moved, and no `/data` file changed.

## The third harvester, and why it is NOT here

GDD s4 asks for **3** harvesters and the commander now settles at **2**. That gap
is left open deliberately, with its cause identified rather than guessed:

**The same GDD section says "Refinery: 2,000 credits, includes one free
harvester", and the sim has never implemented it.** `World.SpawnRefinery` creates
a refinery and nothing else. So the designed 3 is *two free, one bought*, and no
amount of tuning the AI's purchase target reproduces that honestly.

Two further reasons it is its own row rather than this one:

- **The blast radius is much wider.** Every scenario, map and mission that spawns
  a refinery would gain a unit, and it affects *players* exactly as much as the
  AI. Folding it in here would have made this wave's golden regeneration
  attributable to two causes at once, on the first regeneration this campaign
  has ever needed.
- **The knob cannot express it anyway.** `harvesters_per_refinery` is an integer
  and 3-at-2-refineries is a ratio of 1.5, so reaching 3 by tuning would mean
  changing the shape of the difficulty ladder's economy knob for a number that
  the free-harvester clause is supposed to supply.

## Hash and format

**FOUR goldens regenerated, and this is the first regeneration in sixteen rows.**
Measured, not assumed, and each is explicable by one cause:

| scenario | before | after |
|---|---|---|
| `skirmish` | `0x58706A353E6BAFB3` | `0x19228D6E6E605554` |
| `expansion` | `0xDDE867740E6BD1E7` | `0x762BE98AE6C0E86F` |
| `aisuper` | `0xADCBEA00230129DD` | `0x10456F2FE00DE33E` |
| `mission` | `0xA00E338F5F91C3C4` | `0x1E0F30CF25385501` |

**The other twenty are byte-identical**, and the split is exactly the one the
change predicts: all four movers run a `SkirmishAI` that builds an economy
(`mission` runs `SkirmishAI.Rusher`), and every scenario without a commander, or
whose commander never reaches the refinery rung, is untouched. The catalogue
checksum does not move; no definition changed.

**A process note, because it nearly went unnoticed.** The default battery passed
with the old hash file still in place - the golden comparison is a separate
runner mode that CI runs, not part of `dotnet run` with no arguments. Reading the
green battery as "no hashes moved" would have been exactly the silence-as-success
failure the operating rules warn about. The four movers were found by running
`golden` explicitly and diffing.

## What CI caught that the local battery did not

The first push went red on both platforms, on `campaignsave`, and the failure was
worth more than the fix.

**A deeper economy is slower to first blood.** Mission-01 is a camp-clearing
sprint, and the commander now spends 2000 credits on a second refinery before its
army. Measured: **scripted victory moved from tick 3688 to 4946, about 34 per
cent later.** `campaignsave` ran a 4500-tick horizon whose own comment said it
"covers scripted victory under garrison-era AI doctrine" - honest about being
tied to how the commander plays, and the commander changed.

The mission still **wins**, which is the question that mattered, and this fixture
drives the seat a human plays in the real campaign, so it is a test driver taking
longer rather than the campaign getting harder. The horizon is raised to 7000 with
the measurement recorded at the site. **Whether a rush personality should invest
in a second refinery at all is a genuine balance question, and it belongs to the
playtest rather than to a number quietly widened to make a gate green.**

**And the process lesson, which is the larger one.** CI runs eleven sim steps;
`dotnet run` with no arguments runs one of them. `golden`, `campaignsave`,
`saveload`, `replay`, `spectate`, `lanchaos` and the balance gate are all
separate modes. This wave was pushed after a green local battery and an explicit
`golden` check, and `campaignsave` was still missed - so "the battery is green"
was never the same claim as "CI will pass", and this row is the one that made the
difference visible. Every CI mode is now run locally before a push.

One observation from the balance gate, recorded rather than acted on: it reports
**"faction war: Directorate 0 - 6 Sodality"** with a PASS verdict, its own note
saying it is "blocked on human playtesting only". Six faction rows have landed
since that tool last had a human look at it. It is a reporting line rather than a
gate, and it is exactly the sort of thing a playtest exists to judge.

## The gate that was measuring itself

Worth recording in full, because it is a failure mode this project has not hit
before and it would have been invisible.

The first draft of `economyfloatgate` asserted:

```csharp
if (SkirmishAI.RefineriesPerBase != 2) return Fail(...);        // stage 1
if (s.Refineries < SkirmishAI.RefineriesPerBase) return Fail(...);  // stage 2
```

**Both were useless, in different ways.** Stage 1 compares a compile-time
constant to a literal, so the compiler folded it and emitted "unreachable code
detected" - a dead assertion that could never fail, and the build said so.

Stage 2 was worse for being alive: **it measures the commander against the same
constant that drives the commander.** Setting `RefineriesPerBase` back to 1 would
have made the AI build one refinery, and the gate would have compared one against
one and passed.

The target is now written in the gate as a literal sourced from GDD s4, and the
commander is measured against *that*. Proved by setting the constant to 1:

> `GDD s4 has a player float at 2 refineries on one base and this commander
> settled at 1 - TICKET-AI-03's one-per-base cap is back`

The general rule this earns: **a gate must assert against the specification, not
against the implementation's own constant.** Sharing a constant between the code
under test and the test of it makes the test follow the code wherever it goes.

## Consequences

The commander plays two-thirds of GDD s4's economy where it played one-third, and
the treasury floats instead of starving. `economyfloatgate` pins it and is proved
to bite. Full battery exit 0.

Also cleaned up: two unused constants in `factionsuperweapongate` that had been
emitting build warnings since P7-5b. Warnings left standing train a reader to
ignore warnings, which is how the unreachable-code one above would have been
missed.
