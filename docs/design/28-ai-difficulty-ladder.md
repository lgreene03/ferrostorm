# Doc 28: the AI difficulty ladder

Owner: game-designer + ai-engineer. Phase: 6. Delivers doc 27 register row
DR-14. Authority: GDD (doc 02) line 76, which names the ladder as "Easy: no
cheats, slow; Normal: competent build orders; Hard: strong macro, honest
information; Brutal: resource handicap, clearly labelled as cheating". That
sentence is the whole brief, and this note only fills in the numbers and states
plainly where the promise is not yet fully kept.

## 1. The problem: difficulty and personality had been conflated

The only opponent knob a player could reach was `AiPreset`, which selects
Standard, Rusher or Turtle. Those three vary `waveSize` (6, 4 and 10) and
nothing else. Wave size changes the SHAPE of an opponent, not its strength: a
Turtle is not a harder Standard, it is a slower one that hits in bigger lumps,
and a Rusher is not an easier Standard. All three shared the identical decision
beat of 15 ticks, so the `actEvery` knob the code advertised as "the difficulty
knob" never actually varied in any shipped configuration.

The result was that the game shipped with no difficulty ladder at all, while
the GDD had promised four rungs since before the prototype. Doc 27 filed this
as DR-14 and named the remedy: difficulty should be economy and thinking speed,
which is the genre's honest ladder, rather than wave size.

## 2. The decision: two orthogonal axes

Personality and difficulty are now separate. Personality keeps `waveSize` and
answers "what kind of opponent is this". Difficulty owns the decision beat and
the economy and answers "how good is it". A player picks taste on one axis and
strength on the other, and the two compose freely.

## 3. The ladder

| Rung | Decision beat | Harvesters per refinery | Handicap | GDD line 76 clause served |
|------|---------------|-------------------------|----------|---------------------------|
| Easy | 30 ticks (the base beat doubled) | 1 | none | "no cheats, slow" |
| Normal | 15 ticks (the shipped beat) | 1 | none | "competent build orders" |
| Hard | 15 ticks | 2 | none | "strong macro, honest information" |
| Brutal | 10 ticks (two thirds of the base) | 2 | 5000 starting credits | "resource handicap, clearly labelled as cheating" |

The beat is expressed as a scaling of whatever beat the personality asked for,
not as a replacement, so the two axes cannot fight each other. A floor of one
tick guards the arithmetic, because a beat of zero would make the modulo throw.

Both knobs are honest in the sense the genre means. A slower commander is
genuinely worse because it reacts later, notices an idle harvester later and
answers a raid later, and it is worse without being told anything false about
the world. A second harvester per refinery is more mining rather than free
money: Hard pays the full 1400 credits for that harvester and has to keep it
alive, which is exactly the macro a strong human plays.

## 4. Normal is the identity rung, and that is load bearing

Every value on the Normal rung is the value the code used before the ladder
existed, so a Normal commander plays the identical match to the commander that
shipped. This was a design goal, not a coincidence. Doc 27 predicted DR-14
would move the goldens, and it does not: all 24 golden hashes are byte
identical across this change, measured rather than assumed, because the three
personality presets all sit on Normal and every golden scenario uses one of
them.

The claim is pinned twice over. The golden diff is the evidence, and
`difficultygate` restates it as a check by playing a full 1200 tick match with
the pre-ladder constructor and with an explicitly Normal one and demanding the
same state hash. A future edit that quietly changes the Normal rung therefore
fails on a named check rather than surfacing months later as a hash nobody can
account for.

## 5. Where the promise is not yet kept, stated plainly

GDD line 76 asks Hard for "honest information". The ladder does not deliver
that clause and cannot on its own, because the commander reads every entity in
the world at every rung, including entities it has no vision of. That is an
information advantage at Easy just as much as at Brutal, and it is the reason
stealth and feints do not work against the AI at all. Doc 27 filed it as DR-15,
which needs its own ADR and a substantial rework of target selection. Until
DR-15 lands, the word "honest" in the Hard rung means "no resource handicap"
and nothing stronger, and this note says so rather than letting the table imply
a promise the code does not keep.

## 6. Why Brutal's handicap lives in setup

`SkirmishAI` holds no privileged access and mutates nothing. It plays through
the same command interface as a human or the network layer, which is what makes
an AI match replayable and desync safe: a replay re-runs the bare command
stream with no AI attached at all. An AI that granted itself credits would
therefore desync every replay of its own match, and would do it silently.

Brutal's handicap is consequently not something the AI does. It is a starting
credit figure the ladder OFFERS to whatever builds the match, exposed as
`SkirmishAI.StartingCreditHandicap`, and applied as ordinary starting state
alongside the existing `StartCredits` setup value. This keeps the handicap
replay safe and, just as importantly, keeps it visible: a number in setup can
be shown to the player, which is what GDD line 76 requires when it says the
cheat must be clearly labelled.

That labelling is a requirement on any surface that offers Brutal. Whatever UI
eventually exposes the ladder must name the handicap on the rung, in the
player's own language, rather than presenting Brutal as merely a better
opponent.

## 7. Reachability, and what comes next

The ladder is delivered in the sim and proven by `difficultygate`, but no
player can select it yet: the client's menu still offers only the three
personalities. That split follows the precedent set by the neutral outpost,
which landed as a model in C4 and became reachable in C4b, and it keeps this
wave to one change with one proof.

The follow up wave owes: a difficulty picker in the skirmish menu carrying the
Brutal label required above, the setting plumbed through `MatchConfig` and the
saved setup (a save format revision), the LAN Hello blob, and the client
applying `StartingCreditHandicap` when it builds a Brutal match. None of it
touches the sim.
