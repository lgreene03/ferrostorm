# 29 - Roster gap analysis: what to add, what not to, and what is Luke's call

Date: 2026-08-02. Author: Architect agent.
Status: **analysis and proposal. Decides nothing.** Roster composition is C9,
which docs/design/27 records as Luke's call, and every name below is provisional.

Two inputs: a measured audit of every unit, building, weapon and mechanic in the
tree, and genre-design research into roster construction. Sources for the second
are cited inline. Nothing here proposes reproducing any existing game's content;
the research is used for design *patterns* only, and per Q011's method this
document names no franchise, faction or studio.

---

## 1. The reframe: the roster is not too small, it is LOPSIDED

The obvious reading of "20 units against a design doc asking for 12 per faction"
is that the roster is half-built. **That reading is wrong**, and correcting it
changes what should be built.

Nine of the twenty units are `faction: common`, so each side actually fields:

| | common | exclusive | **total available** |
|---|---|---|---|
| Directorate | 9 | 6 | **15** |
| Sodality | 9 | 5 | **14** |

Observed commercial rosters run **12 to 16 combat units per faction** (StarCraft
II 14 to 16 by race per [unitstatistics.com](https://unitstatistics.com/starcraft2/);
Tempest Rising's Veti at 7 vehicles, 6 infantry, 5 air, 5 specialists per
[PCGamesN](https://www.pcgamesn.com/tempest-rising/veti-multiplayer-test)).

**Both factions are already inside that band.** The count is fine. What is not
fine is which roles those counts cover.

---

## 2. The measured gaps, in severity order

### 2.1 The Sodality cannot siege. At all.

`dir_howitzer` is the **only** unit in the game carrying an `anti_building`
warhead. GDD s6 promises "artillery beats static defence" as one of four named
counters, and it is **Directorate-only**.

Genre research treats siege as a canonical role precisely because it is the
answer to static defence. A faction without it has no reply to a turret line
except to walk into it.

GDD s7 already names the missing unit: the Sodality's **Mobile Rocket Launcher**.

### 2.2 The Sodality owns no heavy armour, which breaks half the matrix

Armour class census across all twenty units: **none 8, light 8, heavy 4,
structure 0**. Of the four heavy units, only two fight, and **both are
Directorate** (`dir_cannon_tank`, `dir_bulwark_tank`).

Five of the ten weapons are `anti_armour`. Against the Sodality they have
**two targets in the entire game, and the Sodality supplies neither.** The
anti-armour half of GDD s6's counter matrix barely exists in a
Directorate-versus-Sodality game.

GDD s7 names the missing unit: the Sodality's mainline light tank.

### 2.3 Three whole systems are two entities wide

| System | Shipped | Missing |
|---|---|---|
| **Air** | 1 aircraft, 1 anti-air weapon on 1 unit | **no anti-air building anywhere** |
| **Splash** | 1 gun splashes (`wpn_howitzer`) | every other area effect is non-weapon |
| **Support powers** | **nothing at all** | GDD s8 promises 3 to 4 per faction |

Research names anti-air as *the* most commonly missed role, and notes air
advantage scales with map size because "the relative range of anti-air is
smaller" on large maps
([That's a Terrible Idea](http://www.thatsaterribleidea.com/2010/07/air-units-in-land-based-rtses.html)).
Ferrostorm's largest map has starts 269 cells apart.

Doc 24 B1 already names the missing leg: "anti-infantry against anti-armour
against **anti-air**". The first two ship as buildings; the third does not exist.

### 2.4 Tier 3 is empty and the heavy tank wins nothing

Tier 3 holds three units: one aircraft and two heroes. **No tier-3 vehicle
exists**, and the **1700 to 2999 credit band is completely empty** - exactly
where a capstone sits.

The unit billed as the heavy, `dir_bulwark_tank`, is tier **2** and the balance
tool measures it losing to cannon tanks, rifles, rockets, phantoms and scout
cars, with a 0% survivor mirror. A 1600-credit unit that loses to 200-credit
rifle squads.

Research explains the failure precisely: a capstone must be "a legible
alternative use of resources", **its counter must be the tempo attack rather
than another capstone**, and it must be visible while building so the commitment
is scoutable ([Supreme Commander Wiki, game ender](https://supcom.fandom.com/wiki/Game_ender)).

### 2.5 The Tech Centre is named in the GDD and does not exist

> **CORRECTED 2026-08-03 by ADR-058 (P7-16). This section was wrong**, and the
> correction is worth more than the section. It read GDD line 47 as requiring a
> building called a Tech Centre, and listed it as the fifth-priority missing
> building. Q006 - which this analysis did not consult, and should have - had
> already recorded the cheaper reading: line 47's intent is that MCV
> replacement is TIER-GATED, and the Radar Uplink is the tier gate the tree
> already has. The row shipped as one prerequisite change with no new building.
> **The Tech Centre is not a missing building and should not be built.** The
> lesson: check the open questions before filing a gap, because a gap already
> under discussion has usually been thought about harder than a fresh audit
> will manage.

GDD s5 line 47: "Both factions can build replacement MCVs at the Factory once a
**Tech Centre** exists." There is no Tech Centre. `com_mcv.yaml` names the hole
itself and leaves the prerequisite as `[com_factory]`, which is a tautology under
`produced_at`.

It is the only GDD-named building with no file, and it would give the tech tree
its **first two-deep prerequisite** - today no unit or building anywhere requires
more than one thing.

### 2.6 Eleven mechanics have exactly one user

Building stealth, the cloak field, capture, theft, sabotage, demolition,
transport, weapon splash, weapon min-range, mines, gates and their hysteresis,
field destruction, the outpost, the bridge.

This is not automatically a defect - a mechanic with one good user is fine. It
is listed because **the cheapest roster additions are second users of machinery
that already works and is already gated.**

---

## 3. What the research says NOT to build

### 3.1 Wall tiers: three independent lines of evidence say no

Doc 24 B7 flags "one wall type at a flat 100 credits, where the benchmarks tiered
barriers by cost and durability". **Three separate lines say tiering by
durability is the wrong fix.**

**Our own measurement.** The balance tool reports that against artillery a
gapped wall - the shape ADR-005 clause 6 intends - is worth **zero ticks**: the
yard falls on the same tick with it and without it, because the howitzer's range
9 beats the turret's range 5, so it parks outside and never touches masonry. A
*sealed* wall buys 235 ticks, 7% of the siege. **A tougher wall the enemy still
never shoots is still worth zero.**

**A shipped studio cut exactly this.** Age of Empires II designed a Fortified
Palisade Wall, doubling the cheap tier's hit points, and removed it from
multiplayer because "there was no good way to implement it without making the
game too defensive"
([AoE Wiki](https://ageofempires.fandom.com/wiki/Fortified_Palisade_Wall_(Age_of_Empires_II))).
It survives only in the scenario editor.

**The genre-current title spends its budget elsewhere.** Tempest Rising (2025)
ships **one wall tier plus a gate plus wall-mountable turrets**, with segments
auto-generating between placed endpoints
([Tempest Rising Wiki](https://tempestrising.wiki.gg/wiki/Concrete_Wall)).
Its complexity budget goes on attachment and placement ergonomics, not tiers.

And the deathball analysis adds a genre-level warning: strengthening static
defence "is only going to make deathballing a more attractive option while making
smaller raids less effective".

**What barrier tiering DID buy where it worked** was never durability - it was
**differing rules**. The classic distinction was one barrier that blocked
movement but not line of sight, and another that blocked both and resisted
crushing. The cheap tier's real function is a *tempo tool*: fast, uncommitted,
placeable mid-fight.

> **CONFIRMED AND CLOSED 2026-08-03 by ADR-061 (P7-19)**, which re-derived this
> rather than deferring to it - and found this section had understated its own
> case and misquoted one number. The decisive fact is not the measurement but
> that **the GDD contains no sentence about walls at all**. The measured figure
> is **229 ticks**, not 235. And the table's most interesting row is not the
> artillery one: against RIFLE SQUADS a gapped wall makes the yard fall 324
> ticks SOONER than no wall at all. The balance tool now carries a signed
> `Ticks bought` column so that is visible without arithmetic.

**Recommendation: refuse wall tiers as specified.** If a second barrier is
wanted, differentiate it by rule - a cheap, fast, low-HP barrier that blocks
infantry but not vehicles, placeable mid-fight - not by cost and hit points.

### 3.2 Do not add persistent stealth. Consider converting what exists.

The Sodality carries **five persistently-stealthed units**. Research is blunt
that persistent stealth is "very binary" - you built detectors or you lose - and,
worse, that it is **parasitic on the whole roster**: every faction now needs
detection, every unit must be evaluated for whether it detects, and detection
becomes a minigame disconnected from the core loop
([Wayward Strategy](https://waywardstrategy.com/2023/06/26/fixing-stealth-in-rts/)).
**One faction's identity mechanic taxes both factions' design budget**, which is
exactly what ADR-043 spent a wave paying for.

The recommended alternative is **positional stealth**: concealment broken on
attack, so stealth is a mobility and positioning tool rather than invulnerability.

**Ferrostorm is already most of the way there.** `RevealTicks = 45` on firing
means cloak already breaks for three seconds when a unit shoots. The remaining
distance to positional stealth is small, and it would let detection become
optional rather than mandatory - which is the stated design goal.

Not proposed here, because it would change the Sodality's identity and that is
squarely Luke's. Recorded because it is the cheapest large improvement available
and nobody has costed it.

---

## 4. Proposals, ranked

Ranked by evidence strength, not by appeal. Names are **provisional** and follow
the existing idiom (Directorate: martial and solid; Sodality: concealment and
insurgency). All are GDD-named slots except where marked.

### Tier 1 - fixes a measured structural hole

| # | Role | Side | Why | GDD slot |
|---|---|---|---|---|
| 1 | **Mobile artillery** | Sodality | The only `anti_building` weapon is Directorate-only, so one faction cannot answer static defence at all. Fixes GDD s6's named counter for both sides | **Mobile Rocket Launcher**, s7 |
| 2 | **Mainline tank** | Sodality | No Sodality heavy armour means five of ten weapons have two targets, both enemy. Restores the anti-armour half of the matrix | **Scorpion-slot light tank**, s7 |
| 3 | **Anti-air emplacement** | common | Doc 24 B1's missing third leg. No building can shoot an aircraft. Second user of `wpn_flak_gun`, which already exists | doc 24 B1 |

Those three are the whole of what I would call *required*. Each closes a hole
that is measurable today, and each is already written down somewhere.

### Tier 2 - completes a promise, larger work

| # | Item | Why |
|---|---|---|
| 4 | **Support-power machinery plus 2 powers per side** | GDD s8 promises 3 to 4 per faction and **no machinery exists**. Research gives a clear spec: anchor to a destroyable structure, visible countdown and target, a **second dependency** such as power so there are two ways to deny it, and cost the main resource so firing delays units ([Wayward Strategy](https://waywardstrategy.com/2021/01/14/ion-cannon-online-how-do-we-improve-support-powers-in-rts/)). The Sodality's written powers - jamming, decoys - are *positional*, which is what short-timer powers should be |
| 5 | ~~**Tech Centre**~~ | **WITHDRAWN 2026-08-03 (ADR-058).** Not a missing building: GDD line 47 wanted MCV replacement tier-gated, and the Radar Uplink absorbed the role. Shipped as one prerequisite change. See s2.5 |
| 6 | **Anti-infantry vehicle** | Sodality | GDD s6 promises "anti-infantry vehicles beat infantry" and only the Directorate has one |

### Tier 3 - only with a playtest behind it

| # | Item | Why it waits |
|---|---|---|
| 7 | **Tier-3 capstone per side** | The 1700-2999 band is empty and tier 3 has no vehicle. But `dir_bulwark_tank` already fails as a heavy, and adding a *bigger* heavy above a broken one repeats the mistake. **Fix the bulwark first, then decide whether a capstone is wanted** |
| 8 | **Second transport / air transport** | GDD's shared slot says "transport helicopter"; `com_carrier` is ground-only. Real gap, low urgency |
| 9 | **Grenadier, flame trooper, militia, rocket cell** | Four named infantry slots. **Lowest priority on purpose** - see the retirement test below |

---

## 5. The tests to apply before adding anything

Research supplies five, and two are free to run right now
([Wayward Strategy, Redundancy](https://waywardstrategy.com/2018/05/17/unit-design-clarity-of-roles-and-redundancy/)):

1. **The tooltip test.** Write one sentence per unit: what it beats, what beats
   it. If two sentences are near-identical, one unit should go. Identical tooltip
   language is the named symptom of roster bloat.
2. **The retirement test.** Add a unit; if an existing unit's usage drops
   materially, the overlap was too close. *"If you introduce a unit and another
   unit is no longer used by players then your new unit overlapped the old unit
   too closely."*
3. **The usage-share test.** Within a role, expect roughly equal shares.
4. **The sample-rate test.** A unit appearing in under ~1 game in 30 cannot be
   balanced from data ([Stardock](https://www.stardock.com/games/article/487949/dev-journal-the-challenge-of-balancing-an-rts)).
5. **The head test.** Can a player state the opposing roster unprompted?

**This is why the four named infantry slots rank last.** The Sodality's "Militia
Squad" and "Rocket Cell" are, functionally, `com_rifle_squad` and
`com_rocket_squad` with different flavour. Adding them would fail the tooltip
test on day one, and the policy worth adopting instead is the one a major studio
ran explicitly: **when a new unit goes in, an old one comes out**
([Game Developer, The Design of StarCraft II](https://www.gamedeveloper.com/business/the-design-of-i-starcraft-ii-i-)).

---

## 6. Two findings that are not roster work

**The damage matrix is still compiled.** `Combat.cs` carries GDD s6's percentage
table with its own comment calling the `/data` wiring "a Phase 2 ticket". It is
**the one combat number in the game that does not live in `/data`**, in a project
that gates that rule everywhere else. Research also notes a major studio re-cut
its entire matrix between base game and expansion - **treat the matrix as tunable
data, not as structure.**

**`dir_bulwark_tank` is a live balance defect, not a gap.** It loses every
matchup the balance tool runs. Research names the likely cause: "the counters to
the counters were too strong" is the best-documented single cause of a dominant
unit, and the fix there was strengthening intermediate units rather than nerfing
the offender.

---

## 7. What is Luke's call

Everything in section 4 is a roster decision (C9). Specifically owed:

1. **Are the three Tier-1 additions wanted?** They fix measured holes and are all
   already written in a design doc. This is the smallest useful yes.
2. **Should stealth become positional?** The cheapest large improvement
   available, and it changes the Sodality's identity.
3. **Is the roster budget "12 per faction" or "12 exclusive per faction"?** Both
   sides are already at 14-15 available. The answer decides whether section 4 is
   additions or replacements.
4. **Wall tiers: confirm the refusal.** Three independent lines say the
   durability version is the wrong fix. Recorded here so overturning it is a
   decision rather than an oversight.
