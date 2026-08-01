# ADR-041: no credit ceiling, and the measurement that decided it
- Status: Ratified
- Date: 2026-08-01
- Deciders: Architect agent + Luke (who authorised the design calls previously refused)
- GDD/TDD feature served: GDD s4; doc 24 B2; P7-6

## Context

P7-6 asks for a silo and a credit ceiling. It was refused twice as GDD-silent and
Producer-owned; it is now mine to decide, and the answer is still no, but for a
better reason than before.

GDD s4 specifies the economy in full - the resource, the harvester, the refinery,
the secondary income - and **never mentions storage, a cap or overflow.** What it
does specify is an intent:

> A player floats at 2 refineries / 3 harvesters on one base; expansion or
> raiding decides who out-produces whom.

**"Floats" is the word that decides this.** The economy GDD s4 describes is a
flow, not a stockpile. A ceiling is a rule about a stockpile, so it is not a
missing piece of that design, it is a different design.

## The measurement

Refusing on a reading alone would be an opinion. `economyprobe` measures the
thing GDD s4 actually claims, which nothing had ever checked: it runs a real
AI-versus-AI match on skirmish-01 for 9000 ticks and reports the treasury.

```
   tick   credits0   credits1   refineries0   harvesters0
   1500       3329       4029             1             1
   3000       1939        720             1             1
   4500          0          2             1             1
   6000        650       1234             1             1
   7500         19        820             1             1
   9000          1       2172             1             1
```

**The treasury does not run away. It oscillates near zero**, because credits are
spent as fast as they are earned. That is precisely the float GDD s4 describes,
and it means a ceiling would constrain something that never approaches a limit.

A silo would be machinery, a build option, a schema key, a hash fold and a gate,
all to make a decision about a stockpile that does not exist.

## Decision

**No credit ceiling and no silo.**

### The argument that would have to be overturned

Recorded so this is reversible rather than merely refused. A ceiling earns its
place if any of these becomes true:

1. **The treasury runs away.** Re-run `economyprobe`. If credits climb
   monotonically into the tens of thousands, banking is real and a silo makes it
   a decision.
2. **The GDD changes its intent from a float to a stockpile.** That is a Producer
   amendment to s4, not an engineering row.
3. **A human playtest finds late-game banking that the AI does not exhibit.** The
   probe measures two commanders that spend continuously; a human who turtles may
   bank in a way this cannot see. **This is the likeliest of the three, and it is
   the reason the refusal is written as reversible rather than final.**

## What the measurement found instead, which matters more

The last two columns say **1 refinery and 1 harvester**, sustained across all
9000 ticks. GDD s4 specifies floating at **two and three**.

**The design intent is not being met, and it is not being met in the direction
opposite to the one P7-6 assumed.** The economy is not overflowing; it is
undersized. A silo would have been machinery added to a problem that is the
reverse of the real one.

The fix is the commander's economy behaviour, not storage: it builds one refinery
and one harvester and stops. That is a row of its own, it is measurable by the
probe that found it, and it is filed rather than fixed here because this wave's
question was whether a ceiling is wanted.

## Consequences

P7-6 is closed as REFUSED with its reversal conditions stated. No code changes
beyond the probe, so no hash moves and nothing is at risk.

`economyprobe` stays as a mode. It asserts nothing, because balance is a playtest
and a hard threshold here would be a number invented to pass itself. It exists so
that the next person to ask "should we have a silo" can answer it in thirty
seconds with evidence rather than an argument.
