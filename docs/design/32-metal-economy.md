# 32: The metal economy

**Status: PROPOSED, 2026-10-04.** This proposes an answer to Q014, prompted by the owner's message about mining different metals. Nothing in it takes effect until the owner confirms the reading set out in Decision 1. If its ADR is then ratified, it supersedes ADR-024 (multi-resource) and doc 22's P5-ECON-12. Owner of the design: game-designer. Owner of the engineering plan: architect. GDD sections touched: s3, s4, s5, s8. Proposed phase: P9, after P8 closes.

**File name.** The brief that commissioned this asked for `docs/design/30-metal-economy.md`. Numbers 30 and 31 are already taken (`30-blender-art-pipeline-research.md`, `31-model-brief.md`), so this is `docs/design/32-metal-economy.md`.

**Grounding note.** Every engineering claim below was read in this worktree on 2026-10-03 and 2026-10-04. The last read was on `claude/p8-30-longmatchperf` at `f05757d`, which includes P8-18. Symbols in `World.cs` moved by up to a hundred lines between reads, so sites are named by symbol first. A line number is a hint for finding the site again, never an address to edit. Every price, build time and hit point quoted from `/data` is today's value. P8-32 is scheduled to change some of them (it proposes the Bulwark at 1400). Every derived figure here is therefore recomputed from the values that stand when P9 starts, and the stages in the engineering plan fail until it is.

## Summary

Ferrite is still harvested by trucks and still pays for everything, so on every map that ships today the game is exactly the one P8 is tuning. A metals map adds lodes of two strategic metals: Tungsten at each side's forward expansion and Titanium on the contested centre line. An engineer that reaches a free lode founds a Pithead on it, and the Pithead mines one ingot every ten seconds into its owner's stock. Each metal pays for one kind of hardware on both sides: Tungsten for the heavy tier (the Bulwark Tank, the Howitzer and the Phantom Tank) and Titanium, whose lodes open six minutes into the match, for the two superweapons. A metal-priced item costs less Ferrite plus a few ingots, and the ingots are taken whole when the order is given; an order that cannot be paid in metal is refused at once, while infantry, the main battle line, aircraft, every defence and every detector cost no metal at all. A Pithead can be destroyed, captured, sabotaged, robbed or sold, but the lode beneath it is never lost, so the fight is over who holds the ground.

## The metals

| Material | Where it is found | What it is for | Scarcity | Regrowth |
|---|---|---|---|---|
| **Ferrite** (the structural metal, as shipped) | Crystal fields on `F` map cells at every start and at expansions, 12000 to a field (`MapData.StandardFieldAmount`) | Everything. It is banked as credits, and every price is still a credit price. On a metals map it pays the larger share of a metal-priced item and all of everything else. | The bulk resource: plentiful early, finite near the spawns, and thinning so that players must expand (GDD s4) | Regrows while any remains (ADR-012), reworked by P8-19's seed stock. This document does not change it. |
| **Tungsten** | One lode at each seat's forward expansion, never inside any start's construction radius (`World.CyBuildRadius`, 7) | Dense, hard and heat-proof parts: penetrators, shell bodies and rocket nozzles. It prices the heavy tier of both factions: the Bulwark Tank (3), the Howitzer (2) and the Phantom Tank (2). | One lode per seat. A Pithead mines 6 ingots a minute, which is less than one factory spends while it builds heavy-tier hardware without a break. | The lode never runs out. The Pithead can be lost, and the lode can be founded again the moment it falls. |
| **Titanium** | Contested ground only: two lodes on the centre line of a two-seat map, each the same path distance from both starts; never at a start or an expansion. Lodes can be founded only from tick 5400 (6:00). | Casings that keep their strength at high temperature: the Orbital Cannon's launch hardware and the Seismic Charge's bore casing (4 each). | Two lodes on a two-seat map, opening at 6:00 | As Tungsten |

**Derived, not chosen.** Five numbers in that table come from numbers already in the game. This is the project's method for numbers that need no defence.

- **The Pithead is the Outpost you found yourself.** It has the Outpost's price, hit points and sight (500 credits, 1000 hp, sight 5).
- **One ingot is worth what an Outpost pays in the same time.** A Pithead yields every 150 ticks, the Outpost pays 15 credits a second (`World.OutpostIncomePerSecond`, which H3 moves into `/data`), and ten seconds of that is 150 credits. The value is authored per metal as `ingot_value: 150`. A stage holds it equal to the Outpost's income times the metal's interval divided by `World.TicksPerSecond` (15). Editing a metal's interval therefore fails the stage loudly, instead of silently repricing every item in that metal.
- **Each item's metal is the number of ingots one Pithead mines while the item is being built**, rounded up: `metal = ceil(build_time_ticks / yield_interval_ticks)`. The Bulwark builds in 350 ticks and takes 3. The Howitzer builds in 200 and the Phantom Tank in 210, and each takes 2. Each superweapon builds in 600 and takes 4.
- **Titanium opens at one superweapon charge.** `opens_at_tick: 5400` is the superweapons' own `charge_ticks`, the same figure D33 uses for the commander's purchase floor. A stage holds it equal to the largest `charge_ticks` of any def priced in Titanium.

**Which items carry metal.** One editorial rule decides it, and it reads price tier and production class, never an ability. **Tungsten prices the heavy tier: every factory combat vehicle priced at 900 credits or more that no law below exempts.** That selects exactly the Bulwark (1600), the Howitzer (900) and the Phantom Tank (900). **Titanium prices the superweapons.** A runner stage holds the authored set to this rule. A later balance change that moves a price across 900 then fails the stage until someone decides, instead of quietly adding or removing a metal.

**Why two metals, and why they follow roles.** A metal priced only on one faction's exclusive hardware was considered and refused. Apart from its generator and its superweapon, everything exclusive to the Sodality is stealthed, cloaking or detecting:

- the Phantom Tank, Shade Raider, Saboteur, Infiltrator and shadow commando are all `stealth: true`;
- the Veil Projector field-cloaks friendly units;
- the Shroud Nest is a stealthed defence;
- the Watch Post is a detector.

A metal for the Sodality alone would therefore in practice be a metal for concealment, which the originality rules forbid. Metals assigned by role and spent by both factions avoid that. Two strategic metals rather than three also means fewer counters to read, fewer lodes to place and balance, fewer weights for the commander to get right and fewer art assets. Decision 2 records how a third metal could join later.

**Where the idea comes from.** The starting idea was metals with distinct properties. The design grounds it in real metallurgy: each metal does in the game what it does in industry. Its lineage is the genre's multi-resource economies, in which scarce materials gate particular hardware.

## How it plays

### Harvest and founding

Ferrite does not change at all. The harvester, the refinery, `HarvestSystem`, `RetargetField`, regrowth and the eleven behavioural sites in `World.cs` that test `EntityKind.FerriteField` all stay exactly as they are.

**Metals are mined by a building.** A lode is static map data, declared by a line in the map file, and it is not an entity. It cannot be targeted, damaged, drained or destroyed. So the class of defect ADR-042 found in 1 hp neutral deposits cannot arise for it, and neither can kill credit or alert spam from shooting a neutral.

**An engineer founds the Pithead.** An engineer ordered onto a free lode walks to it and **founds a Pithead**: the engineer is consumed and becomes the building, as an MCV becomes a construction yard through `CommandType.Deploy`. The precedent is exact:

- `com_construction_yard` is priced at 3000 with a build time of 0, because it is the `com_mcv` (3000) unpacked;
- `com_pithead` is priced at 500 with a build time of 0, because it is the `com_engineer` (500) set to work.

A Pithead draws no power, as the Outpost draws none, so a lode far from the grid still works.

**What may stand on a lode.** Neither existing placement test is right for a lode:

- `ValidFoundation`, the MCV's test, refuses any living unit on the footprint and ignores structures;
- `ValidPlacement` refuses any entity at all, including a buried mine.

Copied as it is, the MCV rule would let a 200-credit rifle squad, a cloaked Shade Raider or an invisible mine parked on a lode refuse every founding with no visible reason. It would also found a Pithead on top of an enemy mine. Founding therefore has its own predicate:

- Blocked terrain refuses, and so does any structure.
- A lode's footprint refuses the placement of every structure except a Pithead, so no mine, wall or turret can ever stand on one.
- Every unit on the footprint, whoever owns it, is moved to the nearest free cell by a ring scan in ascending order. No unit can deny a lode, and no refusal ever reveals that a cloaked unit is standing there.
- Founding is refused on an occupied lode and, for Titanium, before tick 5400.

**How a Pithead pays.** A standing Pithead pays its owner one ingot of its lode's metal on the Outpost's own schedule idiom (`Tick % yield_interval_ticks == 0`), so a loaded save resumes on the same beat. The metal is read from the lode under the Pithead's anchor, so the Pithead carries no new state. The yield is keyed on the def's `placed_on_lode` property.

The Pithead gets its own entity kind rather than borrowing `EntityKind.Outpost`. Every existing test of that kind would otherwise need a carve-out: the credit trickle, the commander's neutral-Outpost errand, `mapgate`'s capture count and the client's credits-per-second readout. A missed carve-out is how a structure ends up paying twice.

**What can happen to a Pithead.** Five things, and each is an existing verb:

- **Destroyed:** the lode is free again, for anyone, at once. Verified: the hero's demolition deals `DamageOf(DemolitionDamage, Warhead.AntiBuilding, t.Armour)`. The damage matrix gives `anti_building` 100 per cent against structure armour (`data/combat/damage_matrix.yaml`), so a demolition does 1000 damage against the Pithead's 1000 hit points. One visit from either faction's hero therefore kills a Pithead, exactly as it already kills an Outpost (Decision 16).
- **Captured** by an enemy engineer (`CaptureSystem` already acts on any enemy structure): it pays the captor.
- **Sabotaged** by a Saboteur: it pays nothing for the 450-tick window (`World.SabotageDurationTicks`), which costs the owner three ingots.
- **Robbed** by an Infiltrator: the robbery takes a fifth of each metal stock as well as a fifth of the credits.
- **Sold** by its owner: 250 credits back, and the lode is free.

**A Pithead anchors placement exactly as an Outpost does.** `ValidPlacement` accepts as an anchor any owned structure that is not a wall or a gate (`IsBarrier` is `Wall or Gate`). A captured Outpost therefore anchors placement today, and one rule for both is easier to teach than an exception. Lodes are few and fixed, so a Pithead cannot be chained into creep. Being able to raise a turret and a detector at a lode also suits the Directorate's identity as the wall. Decision 7 records the fallback if fortifying lodes turns out to decide matches.

**Why a founded Pithead, and not trucks hauling typed ore.** The owner's words are that mining different metals around the map is the resource gathering, and both models honour them. The Pithead does better on all three tests:

- **Readability.** A lode is either worked or it is not, the headframe is a large silhouette, and the counter says what it is worth. Typed truck cargo would add a second axis of readouts, and a mixed-load rule, to the most numerous vehicle in the game.
- **AI competence.** The commander already buys engineers and walks them to Outposts (`SkirmishAI.NearestNeutralOutpost`), so founding is the same errand with a different destination. Typed hauling would need a new allocation model across the five field-choice sites and would pull harvesters off Ferrite.
- **Cost.** It leaves `HarvestSystem` untouched, which is the hot path every golden runs and the one P8-19 is reworking. It needs no typed cargo and adds no 1 hp neutral entity.

Ferrite is gathered by truck and the metals by extractor. If the playtest finds Pitheads too passive, a later step can make a Pithead fill a hopper that a harvester hauls home, at the typed-cargo price. Every other part of this design carries over.

### Carry and bank

Metal has no carrier. A Pithead delivers straight into its owner's stock, and trucks carry only Ferrite. Each seat keeps one whole-number stock per metal beside its credit treasury. A stock has no cap, which extends ADR-041's refusal of a ceiling. Every stock starts at zero, so the first ingot of every match comes from a lode.

### Spend

Every item keeps its authored `cost` as its full value, and a metal-priced item names one metal and an amount.

- Where that metal is **active** (the map declares at least one lode of it), the item costs its Ferrite share plus the ingots. The Ferrite share is `cost` minus `ingot_value` times `metal`.
- Where the metal is not active, the item costs its full `cost` in Ferrite, exactly as today.

On a map with Tungsten lodes the Bulwark reads 1150 and 3 Tungsten; on every map that ships today it reads 1600. Because `cost` never changes, every reader of it stays correct: the balance tool's "equal credits fielded" budgets, P8-32's and P8-33's verdicts, and the commander's priority arithmetic.

The ingots are taken **whole when the order is given**, at `Produce` and at `BuildStructure`, and the order is refused if the stock is short. A refused order never enters the queue, which is what stops a queue head waiting forever on a metal its owner does not mine. The Ferrite share drains as the item builds, exactly as credits do today.

Refunds follow from that one rule. Every charge and refund goes through one pricing helper, because any refund that paid the full `cost` in credits would launder ingots into credits at 150 each. These are all the paths, from `CancelProduce`, `RefundPendingOnSell`, the sell case, `CaptureSystem` and death:

- **Cancelling** returns the order's ingots in full on all six cancel paths: the lane's ready slot, the lane's head, a queued lane order, the yard's ready slot, a queue head and a queued order. The queued order and the queued lane order refund nothing today because nothing has been paid yet, so under charge-at-order they are the paths most likely to be missed. Credits already paid return as today.
- **Selling** a building returns half its Ferrite share in credits and half its ingots, rounded down, matching today's `sold.Cost / 2`.
- **Selling a producer** also returns the ingots of every order waiting in its queues and lanes, because each paid its metal when it was ordered. `RefundPendingOnSell` owes queued items nothing today, which stops being true for metal. Its ready slots pay the Ferrite share and return the ingots.
- **Capture** refunds the pre-capture owner on the same rules as a sale's pending refunds, credits and ingots alike. Today capture refunds nothing and leaves the second lane to the captor (H4 below), so H4 lands first and metal inherits a correct path.
- **A producer that is destroyed** loses the ingots in its queue, just as it loses the credits already drained into `BuildPaid`.

### The treasury model

There is one credit treasury per seat, unchanged in every respect. It is still hashed unconditionally, saved, shown, granted by missions, carried in `MatchMeta`, agreed in the LAN setup's `StartCredits` and set by the TREASURY picker. Beside it sits one stock per metal. Nothing converts between them in either direction, and there is no market. A conversion rate would fold the metals back into one currency, and selling metal for credits would bring back ADR-024's richer grade by the back door.

### The laws

Seven laws say where metal may never go. They make the design readable and keep the counters honest. The data loader enforces each one by a property it can read, so a later edit cannot quietly break one.

1. **Nothing that earns, founds or powers costs metal:** harvester, refinery, MCV, construction yard, engineer, power plant, generator and the Pithead itself.
2. **Nothing that detects, defends or counters costs metal:** every def with `detector: true`, every def with an anti-air weapon, the radar uplink (P8-17's anti-air tier), walls, gates, mines, the turret, the emplacement and each side's own defence.
3. **Infantry, both heroes and the main battle line carry no metal:** every barracks unit, and every factory vehicle priced under 900 (the Cannon Tank, the Shade Raider, the Vanguard Car, the Flak Track, the Carrier and the Repair Vehicle).
4. **Aircraft and the airfield carry no metal,** so air play is the same on every map and never waits for Titanium's opening.
5. **Each item takes at most one metal.**
6. **Where a metal has no lode, everything priced in it costs its full Ferrite price.**
7. **Metal is a material spent at a producer.** It never fuels, charges or strengthens anything, and support powers cost none. Any def carrying `support_powers` carries no metal, which keeps the Bastion, the Veil Projector, the Shroud Nest and the Watch Post free of it by construction.

Five defs carry metal under the selection rule and these laws: `dir_bulwark_tank`, `dir_howitzer` and `sod_phantom_tank` in Tungsten, and `dir_superweapon` and `sod_seismic_charge` in Titanium.

## Faction asymmetry

Supply is symmetric, and so is the role map: both factions mine both metals and spend both. What differs is how far each doctrine depends on them, and how each side raids a Pithead.

**Dependence.**

- **The Directorate, "the wall",** puts two of its heavy hitters on Tungsten: the Bulwark and the Howitzer. A starved Directorate loses its breakthrough tank and its splash answer to infantry blobs. It fights on with rifles, rockets, Cannon Tanks, Vanguard Cars, its commando and every defence.
- **The Sodality, "the shadow",** puts one item on Tungsten, the Phantom Tank. In today's commander cycle, though, the Phantom is the only unit the Sodality factory builds once its line holds (anti-air and economy orders aside). A starved Sodality therefore loses its factory's main line. It fights on with Shade Raiders, its infantry, its stealth specialists, its hero and every defence.

Neither side is ever locked out of an army, and both can found again the moment an engineer reaches a free lode. Both superweapons take Titanium, so every seat must take the centre once it opens to reach its own.

**Value.** Each ingot spent saves its owner 150 credits of Ferrite, so a Pithead is worth more to whichever side can spend everything it mines. While a producer runs continuously at full power, one Pithead cannot keep up with either faction's heavy tier (see the table in the next section), so both sides extract its full value. That stops being true when a commander's cycle spends less of its factory time on heavy hardware. P8-28 will change exactly that, by adding the Shade Raider and the Vanguard Car to the cycles.

This is measured rather than argued. `metalprobe` prints each faction's ingot value spent per minute on `metals-01`, and P9's balance row (M7.2) requires the two factions to sit within 15 per cent of each other. If they do not, the remedy is a data change to which items carry metal, recorded as an exception to the selection rule.

**The map.** Each seat's own expansion holds one Tungsten lode. One Pithead mines 6 ingots a minute, against the 7.7 to 9 that one factory spends while building heavy-tier hardware without a break. A seat that wants a second heavy producer therefore needs a second lode, and on a two-seat map the only other Tungsten lode lies in the enemy's expansion. Taking it is both income and denial. Titanium opens at 6:00 on the centre line, where both seats must go for their superweapon. A seat that loses both centre lodes can win one back with an engineer and an escort.

**Raiding.**

- **The Directorate raids loudly:** armour rolls over a Pithead, and its commando's demolition kills one outright.
- **The Sodality raids quietly:** its shadow commando's demolition kills a Pithead outright while stealthed (`ContactEffectOf` gives demolition to both heroes), a Saboteur switches one off for thirty seconds, and an Infiltrator takes a fifth of each stock.

That is three quiet tools against none. It fits the factions' identities, but it is a real tilt. The Directorate's answers are:

- its detection, which costs no metal under law 2;
- fortifying the lode, which a Pithead's anchoring allows. A turret and a Sentinel Scout at a lode turn a quiet raid into a loud one.

`metalprobe` prints Pithead losses per seat by cause (demolition, sabotage, capture, other), and the playtest asks whether the player on the receiving end finds it fair. Decision 16 records the fallback.

## What the player sees

Classic first: on a map with no lodes the screen is exactly today's.

**Counters.**

- On a metals map, the status line and the sidebar header gain one counter for each active metal, after credits. Each shows the metal's shape, its name in words and a whole number ("TUNGSTEN 12", "TITANIUM 3"), with a tooltip giving the yield rate and how many Pitheads pay it.
- Both factions spend both metals, so there is never a counter for a metal the player cannot use. A metal with no lode on the map has no counter at all.
- Before Titanium opens, its counter reads "TITANIUM: OPENS 6:00".
- The chemical symbols (W, Ti) appear only as a secondary mark in tooltips. A symbol means nothing to a player who is not a chemist, and W reads as a compass point.
- `Sidebar.Refresh` takes the stocks as well as the credits.

**Prices.**

- A metal-priced button shows a split price, "1150 + 3" with the Tungsten shape, but only where the metal is active.
- Its tooltip also gives the full value ("WORTH 1600"), so a unit reads the same on every map.
- The button greys when either share is short, and the tooltip says which share and why ("NEEDS 3 TUNGSTEN: FOUND A PITHEAD ON A TUNGSTEN LODE").
- A refused order goes through P8-42's single refusal path with its reason.
- The affordability tests in `Sidebar.cs` and the wall-run and repair predictors in `SkirmishLive.cs` read the split price.

**Founding.** With an engineer selected, a free lode takes a "found" cursor, and a right click sends the engineer to found on it. An occupied lode shows whose Pithead stands there and refuses with "LODE TAKEN". A Titanium lode before 6:00 refuses with "LODE OPENS IN 2:13". The tooltip on a selected Pithead says that it anchors building like an Outpost.

**First encounter.** The first time a player starts a metals map, one routine line explains the whole system: "THIS THEATRE HAS LODES: SEND AN ENGINEER TO FOUND A PITHEAD. TUNGSTEN BUILDS HEAVY GUNS; TITANIUM, OPEN FROM 6:00, BUILDS SUPERWEAPONS." It is shown once per player and remembered in the client's settings, never in the sim.

**The battlefield.**

- Lodes are low rock outcrops veined in the metal's tone. They are visible once explored and plainly not Ferrite crystal.
- A Pithead is a mining headframe whose wheel turns only while it is paying. It carries the team band in the one place doc 16 allows.
- A selected Pithead reads "TUNGSTEN PITHEAD: 6 PER MINUTE" or "SABOTAGED: 23 S", and a selected lode reads "FREE TUNGSTEN LODE".
- The map picker's runtime thumbnail draws open, blocked, water and Ferrite cells today, and will draw lodes too.

**Colour and shape.** Doc 16's law that Ferrite gold is THE resource colour, and also the neutral mark, stays true. The hue wheel is spoken for (`BattlefieldView.SeatMarks` uses orange, teal, cobalt, magenta, leaf green, crimson, violet and citrine). The two metals are therefore told apart by **shape first and value second**, never by hue:

- Tungsten is a squat block in dark graphite, and Titanium an upward chevron in a cool mid grey.
- Every lode and every Pithead also carries a light outline that contrasts with the ground whatever the metal's value. Without it, doc 16's near-black battlefield ground would swallow the dark Tungsten, which is the lode that matters most.
- On the minimap the lodes are a square and a triangle, told apart by shape alone with no glyph, because a glyph a few pixels wide does not read. Founded lodes are ringed.
- Look-dev proves all of this under simulated colour-vision deficiencies before any art is commissioned.
- Doc 16 gains a short paragraph stating the shape-and-value law.
- New colour tokens follow doc 16's naming and do not reuse the word Cinder.

**Alerts.** Alerts go through P8-10's `AlertService` and use the game's own idiom ("HARVESTER IS TAKING FIRE", "STRUCTURE LOST TO CAPTURE"):

- PITHEAD FOUNDED (routine);
- PITHEAD IS TAKING FIRE, PITHEAD LOST and PITHEAD LOST TO CAPTURE (urgent);
- TITANIUM LODES OPEN (notice, for every seat, computed by the client from the tick and the registry);
- "TUNGSTEN STOLEN" and "TITANIUM STOLEN", beside the existing CREDITS STOLEN toast.

Each new alert gets a synthesised cue (`art/audio/synth.py`) and a voice line in the game's own words (`art/audio/make_vo.sh`), which the legal check reads. Until P8-58 lands, a LAN client consumes no events at all, so these alerts are missing in LAN exactly as every other alert is. The map picker gains a METALS badge. The player-facing vocabulary for metal is the short list fixed in doc 09 section 7: mine, found, lode, Pithead, ingot, stock, spend, open.

## How the AI plays it

**Where a commander could get stuck.** Reading `SkirmishAI.cs` shows three paths by which a commander could wedge:

1. **The unit cycle.** `_produced++` runs only inside the branch that issues a `Produce`, and that branch is entered only when `w.Credits(_player) >= cost`. Today a metal order would be issued blind, refused by the sim, and the slot would advance. Once the affordability test also asks about metal, a short slot is never issued and `_produced` never moves. The cycle then stops on that slot, and the barracks stops with it, because the unit type is chosen from `_produced`.
2. **The structure ladder.** A refused `BuildStructure` never clears `wanted`, and production stands aside while `wanted != 0`. This is the stall the comment on the faction-defence rung warns about. Under the laws, the only rung that can carry metal is the superweapon.
3. **A pay-as-you-build hold at a queue head.** This cannot occur for metal, because metal is charged when the order is given.

The commander's work comes in four steps, and the first is deliberately one change:

1. **No wedge (one change).**
   - A `CanAfford(world, def)` helper replaces the ten `w.Credits(_player) >= cost` gates and asks the sim's own pricing helper. On a map with no lodes it is today's comparison character for character, which the goldens prove.
   - In the same change, a unit slot short of metal **substitutes** the producer's dearest metal-free unit of the commander's own faction (the Cannon Tank for the Directorate, the Shade Raider for the Sodality) and advances `_produced` as usual.
   - The superweapon rung **skips rather than holds** while Titanium is short or not yet open, the way the faction-defence rung skips unmet prerequisites.
   - Landing `CanAfford` without substitution would stop the cycle, as described above, so the two cannot be split.
2. **Founding.**
   - The engineer errand becomes a scored choice between neutral Outposts and free lodes.
   - A Tungsten lode weighs `lode_weight` (3). A Titanium lode weighs the same, but only once it is open and the commander could buy a superweapon (radar tier and D33's floor). An Outpost weighs `outpost_weight` (2).
   - The score is the weight divided by the squared distance plus one. Scores are compared by cross-multiplication in integer arithmetic, walking candidates in ascending order, with the lower index winning a tie.
   - With no lodes on the map every candidate is an Outpost, and the choice reduces exactly to today's nearest Outpost.
   - The engineer cap is `metal_engineer_cap` (2) on a metals map, and one at Easy.
   - An escort is staged on a ring three cells off the lode, never on it.
3. **Spending and Titanium.**
   - When a Tungsten stock holds at least `metal_spend_ratio` (2) times the dearest Tungsten item's metal (2 times 3, so 6 ingots), the cycle prefers heavy-tier hardware, so ingots are spent rather than banked. The threshold is measured in ingots, not credits.
   - From Titanium's opening, and subject to D33, the commander claims a Titanium lode. P8-23's staging point is set on the ring round it, so a wave arrives with or before the engineer.
4. **Recapture and denial, Hard and Brutal only.**
   - An enemy Pithead on Tungsten or Titanium joins the wave's target preferences, and an engineer follows once no enemy army stands within eight cells.
   - Once P8-28 adds saboteur raids, the Sodality's saboteurs prefer Pitheads.

The knobs (`lode_weight`, `outpost_weight`, `metal_engineer_cap`, `metal_spend_ratio`) live in `data/ai`, with schema entries and compiled references. Each is added in the step that first reads it, and `aituninggate` proves that the registered values drive the commander.

**Does metal bind?** Metal binds when a producer runs continuously at full power on heavy-tier hardware. The figures below are ceilings computed from today's build times. They stop holding under low power (production falls linearly to 50 per cent in `ProductionSystem`) or when credits rather than metal limit the producer; in either case one Pithead outruns demand.

| A producer running continuously on | Metal | Consumption | Against one Pithead (6 a minute) |
|---|---|---|---|
| Bulwark Tanks | Tungsten | 3 per 350 ticks, 7.7 a minute | 129 per cent |
| Howitzers | Tungsten | 2 per 200 ticks, 9 a minute | 150 per cent |
| Phantom Tanks | Tungsten | 2 per 210 ticks, 8.6 a minute | 143 per cent |
| A superweapon | Titanium | 4 per purchase | an access gate, not a rate |

Rounding up in the derivation guarantees the first three rows. The commander's own figures are not tabled here. They depend on the cycle's composition, which P8-22 (more producers) and P8-28 (the Shade Raider and Vanguard Car join the cycles) will both change before P9 starts. `metalprobe` measures them on the commander as it then stands.

**Brutal.** The TDD promises Brutal "a labelled resource multiplier" (s8), delivered today as a 5000-credit start (`AiTuning.StartingCreditHandicap`). On a metals map that handicap already stretches further, because heavy-tier items cost less Ferrite, so Brutal gets no metal handicap. Decision 17 records the fallback.

**How competence is proved.** A new on-demand `metalprobe`, shaped like `ladderprobe`, runs every personality and both factions, in both orientations, on every metals map. It asserts:

- no wedge: no 900-tick window in which a producer stands, credits cover its cheapest metal-free unit, and no `Produce` is accepted. This includes a scenario with a seat starved of Tungsten from the start;
- the first Tungsten Pithead is founded by a measured tick;
- at least one heavy-tier unit is fielded;
- a superweapon is queued only when its Titanium is held;
- a unit parked on a lode, whether own, enemy or cloaked, never blocks a founding.

It also prints:

- each seat's ingot production against consumption;
- each faction's ingot value spent per minute;
- the tick at which each seat first holds 4 Titanium;
- Pithead losses by cause.

Until the AI steps land, metals maps are offered to human seats only: the lobby disables AI seats on them, and `inputgate/map-picker` checks it.

## Maps

**Format.**

- A lode is a post-grid section line, `lode <metal_id> <ax> <ay>`, whose coordinates are the Pithead footprint's anchor. It sits beside the `structure` and `trigger` lines that `MapLoader` already parses.
- An older build reading a metals map throws on the unknown section ("unknown section"), so the failure is loud.
- The grid, the `F` character and `MapData.Fields` are untouched, and no grid character is spent.
- Lodes parse into a parallel list, `MapData.Lodes`, on ADR-025's parallel-list precedent.
- The parser refuses a footprint that is out of bounds or on blocked ground, one that overlaps an `F` cell or another lode, and a metal id the registry does not hold.
- A lode is open, passable ground until a Pithead stands on it.

**Placement grammar**, to be written into doc 26.

- **Two-seat map:** one Tungsten lode at each seat's forward expansion, four to six cells from a Ferrite field so one expansion covers both; two Titanium lodes on the centre line, each the same path distance from both starts; nothing inside any start's construction radius; the map's own start symmetry throughout.
- **Four-seat map:** one Tungsten lode at each seat's expansion, and one Titanium lode between each pair of neighbours, equidistant from both.

`mapgate` checks the grammar:

- equal path distance from every start to its nearest lode of each metal;
- no lode inside a construction radius;
- reachability between all starts with every lode occupied, so a Pithead can never seal a choke.

**Which maps, and when.**

- **No lode goes on any map that ships today.** `skirmish-01` feeds the skirmish and commander goldens and the mission maps feed the mission goldens, so they never carry one. That single rule keeps all 24 goldens and every P8 baseline unchanged.
- **The metals maps are new files named outside the `skirmish-*.fmap` glob:** `metals-01.fmap` (two seats) and later `metals-02.fmap` (four seats), on the precedent of `test-4seat.fmap`. Each is written by a generator built on `tools/mapgen.py`, as the shipped maps are.
- **The name matters,** because three things glob `skirmish-*.fmap`:
  - the runner's `MeasureMaps`;
  - the client's `MapCatalogue`;
  - P8-40's `inputgate/map-picker` stage (`VerifyRunner.RunMapPickerStages`), which asserts that the picker lists exactly those maps "and no test fixture".

  A map in that series would silently join every P8 sweep. The client's catalogue gains a second glob for the picker, and the picker stage moves to the two-glob pool in the same change (M6.5).
- **`mapgate` walks every `*.fmap`,** so it plays a metals map the day it is committed, as a Ferrite-only match until the commander learns to found. It gains a lode assertion once the AI steps land.
- **Converting a shipped map**, or putting lodes into a mission, is a later decision with its own ADR and a measured regeneration.

**LAN.** Peers agree on a map by its path alone: `MatchSetupBlob` (version 4, `Lan.cs`) carries `MapPath` and nothing that identifies the file's content. Lodes make that gap more expensive. H5 therefore adds a content hash of the parsed map to the setup blob as version 5, under ADR-022's bump rule. The hash covers the grid and section lines rather than raw bytes, so a line-ending or comment difference between checkouts cannot refuse a match. A joiner whose map differs is refused in the lobby. Lodes cannot be agreed through the catalogue checksum instead, for the reason given under the engineering plan.

## Engineering plan

### What the hash, the save and the checksum allow

**Stocks.** The treasury is folded unconditionally: `ComputeStateHash` adds `_credits[p]` for every seat in its per-player loop, and the save writes every seat's credits. A second pool folded the same way would move all 24 goldens. That is the claim Q014 made, and the ADR-024 line in `docs/adr/ADR-open-queue.md` repeats it ("cannot be guarded"). The code has since grown the precedent that refutes it:

- in the same per-player loop, a seat's team is folded as `(p, team)` only when it differs from the identity;
- a few lines above it, the per-seat radar-jam array is folded only while a deadline lies in the future.

Stocks are folded the same way: `(p, metal, value)`, tagged, and only when the value is non-zero. A world in which nobody holds metal hashes byte-identically, and that covers every golden and every match on a shipped map.

**Pitheads and founding.** Pitheads are ordinary structure entities and hash like any other; none exists on a shipped map. An engineer walking to found remembers its lode in a side collection (engineer id to lode index). The collection is folded only when an entry is present, on the cargo and sabotage precedent.

**Lodes are not in the catalogue checksum, and cannot be.** `World.Load` registers the catalogue and compares `world.CatalogueChecksum` against the recorded value immediately after constructing the world (`World.Serialization.cs`, the `registerCatalogue?.Invoke(world)` line). That happens before the blocked grid, the players, the entities or any later block is read. A checksum that included the lode list, or a section guarded on "the world has a lode", would see no lodes at check time and refuse every metals-map save. Lodes are therefore treated as static map data, like the blocked grid, which is not hashed. The save carries them in v15, read beside the blocked grid after the check, and LAN peers agree on them through H5's map content hash.

**The catalogue.** The metals catalogue folds into one appended section of `CatalogueChecksum`.

- The registry, the Pithead def and the AI knobs fold unconditionally.
- A def's metal columns fold `(def id, metal, amount)` only when the amount is non-zero. This is sound for a different reason from the map case: the whole catalogue is present when the checksum is compared, so two sides that disagree about a column always disagree in bytes.
- The checksum therefore moves at each step that adds catalogue content: M1.1, M2.2, M3, M5.2 and M5.3.
- Each move refuses saves and replays recorded before it. That is the pre-public trade ADR-069, ADR-072 and ADR-073 already took, and each move is recorded with its old and new value in the P9 tracker, as P8 rows record theirs.
- No golden moves because of it, because the catalogue checksum is not part of the state hash.
- Adding the Pithead def also raises `World.MaxStructType` from 22 to 23 and changes the structure count the checksum folds. Both are part of M2.2's single move.

**Saves** bump once, to v15 at M1.4. The v15 save carries the stocks, the lode list and a founding block that is written empty until M2.3 fills it. Reserving the block in M1.4 means a v15 save written between M1.4 and M2.3 has the same layout M2.3 reads. That matters because the version predicates in `World.Serialization.cs` are explicit lists (`magic is SaveMagicV8 or ...`) that cannot be patched silently. A v14 save still loads, with zero stocks and no lodes, provided every predicate is widened as a floor. The file's own comments record two past misreads where one predicate missed a new version.

**What moves if the stock guard is refused.** If the Architect refuses the guarded stock fold, stocks fold unconditionally and all 24 goldens move once, which needs an ADR and Architect sign-off. With the guard accepted, nothing in P9 moves a golden.

**The founding command.** Founding reuses `CommandType.Deploy`, with `EntityId` the engineer and `AuxId` the lode's index in `MapData.Lodes` order; X and Y are unused. The Deploy case accepts MCVs only and never reads `AuxId` (`World.cs`, the Deploy case). An engineer Deploy was therefore always refused, every recorded replay keeps its meaning, and F1's verb count does not change. Founding raises a new `Founded` event, never `Deployed`, because the client reads `Deployed` as "own MCV deployed".

**Events.** `GameEvent` carries A, B and C, and `Robbed` already puts the stolen credits in C, so per-metal theft needs its own event. M2.6 adds two hash-neutral event types:

- `Founded`: A is the Pithead, B the lode index;
- `MetalStolen`: A is the robbed structure, B the metal's registry index, C the amount. It is raised beside the `Robbed` of the same robbery, which still names the thief.

PITHEAD LOST and PITHEAD LOST TO CAPTURE need nothing new: the client reads the existing `Died` and `Captured` events by kind. F15's event-coverage check fails any type that is neither consumed nor listed. M2.6 therefore lists both new types as KNOWN-MISSING in `eventgate`'s table, and M6.5 consumes them and removes the lines.

### The steps

One change per step, and no step advances until its gate is green. "Goldens" means `golden 2026` diffed empty against `sim/golden-hashes.txt`, with `tools/ci-local.sh` green, at every step. CLAUDE.md records that predicted neutrality has been wrong before, so it is measured every time. The H rows are defects that land in P8 whatever happens to metals.

| Step | Change | Goldens | Save | Map format | Catalogue checksum | Schema | Gate that proves it |
|---|---|---|---|---|---|---|---|
| H1 (P8, inside P8-19) | Fold the regrowth values into `CatalogueChecksum` | neutral, measured | none | none | moves, inside P8-19's planned move | none | Two worlds differing only in `regrow_amount` differ in checksum; a save across the change is refused |
| H2 (P8) | `RegisterFields` refuses an unknown field id by name instead of skipping it | neutral | none | none | none | none | A planted `data/fields` file with another id fails the load, naming the file |
| H3 (P8, batched with H1) | Outpost income into `com_outpost.yaml` (`income_per_second: 15`), read from the def | neutral (no golden spawns an Outpost) | none | none | moves, with H1 | structure: `income_per_second` | A registered 30 pays 30 a second; `outpostgate` and `mapgate` green |
| H4 (P8, new defect row) | Capture of a producer refunds the PRE-capture owner on `RefundPendingOnSell`'s rules (`BuildPaid`, the lane's paid amount, both ready slots), clears the lane and zeroes progress | measured; the `capture` golden is the one at risk | none | none | none | none | A stage first reproduces the defect (a captured yard hands the captor a paid lane and a finished lane-2 building, and the captor's next head starts discounted), then passes; exact refund to the victim |
| H5 (P8 hygiene; required before any metals map is offered over LAN) | Map content hash of the parsed map in `MatchSetupBlob`, version 4 to 5 | client only | none | none | none | none | A `tools/verify-client.sh` stage: a joiner whose map differs under the same path is refused in the lobby, and a copy differing only in line endings is accepted |
| G0 | Governance (see "Sequencing against P8") | none | none | none | none | none | Owner confirmation recorded as D35; Producer sign-off; ADR claimed in the open queue |
| M1.1 | `data/metals` registry (`com_tungsten`, `com_titanium`: id, name, symbol, `yield_interval_ticks: 150`, `ingot_value: 150`, `opens_at_tick` 0 and 5400), schema, `CatalogueFiles.DataDirs` row, compiled reference, appended checksum section | neutral | none | none | MOVES | new `schema.metal.json` | Selftest proves the files equal the compiled reference; the unknown-directory guard accepts the row; the `ingot_value` derivation stage |
| M1.2 | Per-seat stocks, `Metal(p, m)`, `GrantMetal`, guarded hash fold | neutral | none | none | unchanged | none | A grant of 1 changes the hash and returning to 0 restores it; double-run determinism |
| M1.3 | `MapData.Lodes`, the `lode` section, validation, `World.ConfigureLodes` before tick 0, `MetalActive(m)`; lode footprints refuse every structure placement | neutral | none | additive section line | unchanged (lodes are never in it) | none | Parse selftests (valid, unknown metal, overlap, blocked, out of bounds); every shipped map loads; a mine or turret on a lode is refused |
| M1.4 | Save v15: stocks, lode list read after the catalogue check, empty founding block reserved for M2.3; every predicate widened as a floor | neutral | v14 to v15 | none | unchanged | none | `saveload` and `campaignsave` green; a committed v14 fixture loads; a lode world with non-zero stocks saves and RELOADS on an identical per-tick hash; this step's v15 fixture committed for M2.3 |
| M1.5 | Flat keys `metal` and `metal_cost` on unit and structure defs; loader refusals including the seven laws; columns folded only when non-zero; no value authored | neutral | none | none | unchanged | unit and structure: `metal`, `metal_cost` | `metaldatagate` refusals (one key without the other, unknown metal, each law broken); authoring a column moves the checksum and removing it restores it |
| M1.6 | One pricing helper; whole-metal charge and refusal at `Produce` and `BuildStructure`; both `ProductionSystem` lanes drain the Ferrite share | neutral | none | none | unchanged | none | A test def costing 2 is refused at 1 and accepted at 2; on a lode-free map it costs `cost`; a metals-map Bulwark drains exactly 1150 |
| M1.7 | Every refund path: the six cancel paths, sell, `RefundPendingOnSell`'s ready slots and queued orders, capture (on H4's path) and destruction | neutral | none | none | unchanged | none | Exact-return stage per path; a seeded 200-sequence buy, cancel, sell and capture fuzz with two assertions: order then cancel restores credits and every stock exactly, and credits plus `ingot_value` times ingots never rises |
| M2.1 | `EntityKind.Pithead = 22` (append only; 18 to 21 are Bridge, Mine, Gate and WatchPost, so ADR-024's reservation of 18 is stale); `IsStructure`; the `IsHope` exclusion beside Outpost and Mine | neutral | none | none | unchanged | none | A lone Pithead does not keep a seat alive |
| M2.2 | `com_pithead` at structure id 23 (`MaxStructType` 22 to 23): cost 500, build 0, hp 1000, sight 5, no power, `placed_on_lode: true`, `founded_by: com_engineer`; refused at `BuildStructure` and absent from the yard's sidebar | neutral | none | none | MOVES | structure: `placed_on_lode`, `founded_by` | Selftest transcription; `reachabilitygate` and `mcvtechgate` record it as founded, not built; `schemagate` reads the new keys; a yard order for it is refused |
| M2.3 | Founding: engineer `Deploy` with the lode index; the founding predicate (terrain and structures refuse; units of any owner moved by the ring scan; occupied and not-yet-open lodes refuse); out of reach, the engineer walks with an explicit destination that closes to reach (P8-56's fix for boarding), remembered in the guarded side collection | neutral | v15's reserved block now written | none | unchanged | none | `metalgate` founds from 1, 4 and 10 cells; with an own, an enemy and a cloaked enemy unit on the footprint (each moved, founding succeeds); before 5400 on Titanium refused; occupied refused; an engineer `Deploy` on a lode-free map refused exactly as before; M1.4's v15 fixture loads |
| M2.4 | Yield keyed on `placed_on_lode`, metal read from the lode, on its interval, nothing while `IsDisabled`; capture pays the captor; destruction frees the lode | neutral | none | none | unchanged | none | Exactly 1 ingot per 150 ticks; sabotage costs exactly 3; capture; destroy then found again; a registered interval of 50 changes the yield |
| M2.5 | Theft takes a fifth of each stock, rounded down | neutral (`capture` golden measured) | none | none | unchanged | none | Robbing 10 Tungsten moves exactly 2; a robbery with zero stocks is exactly today's |
| M2.6 | `Founded` and `MetalStolen` events; listed KNOWN-MISSING in `eventgate`'s table | neutral (events are not hashed) | none | none | unchanged | none | A runner stage asserts each is raised exactly once per act; `eventgate/coverage` green |
| M3 | Author `metal` and `metal_cost` on the five defs | neutral | none | none | MOVES | none | Balance tool full output byte-identical; every `ladderprobe` line byte-identical; the selection-rule stage; the derivation stages (metal and `opens_at_tick`); identity stage (on every shipped map, the catalogue with metal columns stripped gives the same per-tick hash); A11 co-sign |
| M4 | `metals-01.fmap` from a new generator on `tools/mapgen.py`; lode grammar in `mapgate` | neutral | none | first map using the section | unchanged | none | `mapgate` green including the grammar; every `MeasureMaps` list byte-identical |
| M5.1 | No wedge: `CanAfford`, substitution and the superweapon rung's skip, as one change | neutral, measured | none | none | unchanged | none | Goldens; every `ladderprobe` line byte-identical; `metalprobe`'s no-wedge stage, including the Tungsten-starved seat |
| M5.2 | Founding: the scored errand, the engineer cap, the staging ring; knobs `lode_weight`, `outpost_weight`, `metal_engineer_cap` | neutral, measured | none | none | MOVES | ai: three keys | Goldens; `ladderprobe` byte-identical; `aituninggate`; `metalprobe`'s founding stages, including a unit parked on the lode |
| M5.3 | Spending preference and the Titanium objective; knob `metal_spend_ratio` | neutral, measured | none | none | MOVES | ai: one key | Goldens; `ladderprobe` byte-identical; `aituninggate`; `metalprobe`'s superweapon stage |
| M5.4 | Recapture and denial at Hard and Brutal; Sodality saboteurs prefer Pitheads | neutral, measured | none | none | unchanged | none | Goldens; `ladderprobe` byte-identical; `metalprobe` |
| M6.1 | Client: counters | client only | none | none | none | none | `tools/verify-client.sh` from seat 1: counters absent on `skirmish-01`, present on `metals-01`, Titanium's reading OPENS before 5400 |
| M6.2 | Client: split price, WORTH tooltip, refusal reasons through P8-42's path | client only | none | none | none | none | The Bulwark button reads 1150 + 3, greys at 2, enables at 3 |
| M6.3 | Client: the found gesture, LODE TAKEN and LODE OPENS IN; `inputgate`'s Deploy stage gains the engineer | client only | none | none | none | none | A right click founds from seat 1; both refusals shown |
| M6.4 | Client: lode and Pithead visuals (an Outpost mesh variant until A1), readouts, light outline, minimap shapes, picker thumbnail | client only | none | none | none | none | `verify-client`; look-dev captures under simulated colour-vision deficiencies |
| M6.5 | Client: alerts consuming `Founded` and `MetalStolen` (KNOWN-MISSING lines removed), cues and voice lines, the picker's second glob and METALS badge, `inputgate/map-picker` moved to the two-glob pool with AI seats disabled on metals maps until M5 | client only | none | none | none | none | `eventgate` stages for both events; legal check; seat guard |
| M6.6 | Client: the first-encounter line | client only | none | none | none | none | Shown once on the first metals map, never on a shipped map |
| A1 | Art: two lode outcrops and an animated headframe through the doc 30 pipeline, entries in doc 31, ingot and shape icons | none | none | none | none | none | Look-dev sign-off |
| M7.1 | `metals-02.fmap` (four seats) | neutral | none | none | none | none | `mapgate` including the four-seat grammar |
| M7.2 | P9 balance row: a `metals-01` faction war in `tools/Ferrostorm.Balance`, `metalprobe` parity, `laddergate` on `metals-01` | neutral | none | none | none | none | Faction ingot value spent within 15 per cent; each faction wins at least 40 per cent of cells; A11 co-sign |
| M7.3 | The metals playtest | none | none | none | none | none | Playtest record answering the questions below |
| M7.4 | GDD s4 and s5, docs 15, 16, 26 and 31 amended on ratification | none | none | none | none | none | Legal check |
| M7.5 | P9 milestone gate | none | none | none | none | none | Producer, QA, Legal and Luke sign off (CLAUDE.md workflow item 6) |

**Size.** P9 is 32 rows (G0 to M7.5) against P8's 61, plus the five H rows in P8. Most P9 rows are narrower than a P8 row, but P9 is a phase, not a ticket. This size goes to the owner with the governance question rather than being discovered later.

**Files touched.**

- Sim: `World.cs`, `World.Serialization.cs`, `MapLoader.cs`, `DataLoader.cs`, `SkirmishAI.cs`, `AiTuning.cs`.
- Runner: `Program.cs` (`metalgate` in `match` and binding from M1.6; `metaldatagate`; `metalprobe` on demand; selftests).
- Data: `data/metals/*`, `data/schema.metal.json`, `data/schema.unit.json`, `data/schema.structure.json`, `data/schema.ai.json`, `data/buildings/com_pithead.yaml`, `data/buildings/com_outpost.yaml`, five unit and building files, `data/ai`.
- Maps: the new `.fmap` files and their generators.
- Client: `Sidebar.cs`, `SkirmishLive.cs`, `ModelLibrary.cs` (kind 22), `BattlefieldView.cs`, `Minimap.cs`, `AlertService.cs`, `MapCatalogue.cs`, `MainMenu.cs`, `Lan.cs` (H5 only), `VerifyRunner.cs`.
- Audio: `art/audio/make_vo.sh`, `art/audio/synth.py`.
- Untouched:
  - `HarvestSystem`, `RetargetField` and every `FerriteField` guard;
  - `Replay.cs`;
  - `MissionRunner.cs` (a `metal` condition and a `grantmetal` action wait for the first mission that teaches metals);
  - the balance tool's pricing, because `cost` stays the full value.

## Sequencing against P8

**Now, documents only.** The owner's message is the trigger for this proposal, not yet a decision, and it does not by itself reverse D17. One tracker amendment is made before P8-12 lands, so that P8-12 does not write a closure the owner may be about to contradict. P8-12's "close ... Q014 (D17)" becomes "hold Q014 open pending the owner's confirmation of doc 32; D17's Q014 half is not executed until then". Nothing else in the tracker changes yet, and this document is committed as PROPOSED.

**On the owner's confirmation (G0).**

- **D35** records the direction in the owner's own words, using a verbatim extract that names no source: *"mining different metals around the map is the resource gathering and different metals are for different things."* It then records, labelled as the recommendation taken under the standing instruction, that this document is the way to build it, with its reversal conditions. D35 is the next free number after D34 ("Boarding and unloading get events of their own"). Re-check it at commit time and use the number actually taken everywhere, including the patterns comment and doc 09's heading.
- **D17 splits.** The silo half stays refused with its reason unchanged. The Q014 half is answered by D35: a metal economy, scheduled as P9.
- **P8-12** records Q014 as answered by D35 and points to doc 32.
- **"Deliberately not in P8"** keeps "a second resource (Q014)", because P9 follows P8. Its reason changes from "refused" to "scheduled as P9 (D35)".
- **Producer sign-off** for one new structure (the Pithead, taking the count from 22 to 23) and for metals maps is recorded with D35, as CLAUDE.md's Scope rule requires.
- **A P9 tracker**, `docs/tickets/P9-metals-tracker.md`, holds the steps above as rows with their hash columns. Its status table is authoritative for metals.
- **Records.**
  - Q014's Resolution is written.
  - ADR-024, its line in `docs/adr/ADR-open-queue.md` and doc 22's P5-ECON-12 are marked superseded.
  - The ADR is claimed in `ADR-open-queue.md` in the same commit that drafts it, as that file's numbering law requires (ADR-074 today, unless P8-19 claims it first).
  - Doc 09 gains section 7.
- **Owner-only edits.**
  - CLAUDE.md's Legal rule must name the second property before the new patterns can land. The patterns file names that rule as its authority, and an agent brief cannot authorise a CLAUDE.md edit.
  - In the same change, the patterns file's header and F14's wording ("the banned franchise names") widen to "each property named in CLAUDE.md's Legal rule".
  - This edit protects every future document whatever the answer to Decision 1, so it can be made now.
  - The phase-gate status in CLAUDE.md gains P9 on confirmation.

**Defect fixes that land in P8 regardless.** H1 to H5 above land in P8, because each is a defect today whether or not metals ship.

- **H1 is medium severity.** The regrowth values are loaded from `data/fields` but are not in `CatalogueChecksum`. Two LAN peers with different field files therefore pass the setup check and then desync, because `FerriteAmount` is hashed.
- **H4 is medium severity too.** A captor inherits the victim's paid lane progress and any finished lane-2 building, including faction-gated ones such as the seismic charge, because the faction check runs only at `BuildStructure`. The stale `BuildPaid` also discounts the captor's next head. This comes from a review of `CaptureSystem`, which removes `_queues` and `_orderQueues` but never `_lanes`, and it is reproduced by a failing stage before the fix.

Three more findings go to the rows that own them:

- **P8-24 / D12.** The stalemate rule ends a match after 2700 ticks in which no seat "deals damage, changes credits or completes production". A captured Outpost changes credits every second, so as worded the rule can never fire while anyone holds one. The row should read "changes any treasury or stock other than by a passive trickle (Outpost income, and later Pithead yield)". This is a defect in P8's own terms, not a metals change.
- **D29 / tower creep.** `ValidPlacement` lets any owned structure that is not a wall or gate anchor placement, and that includes the mine (`IsBarrier` is `Wall or Gate`). Twenty mines can therefore walk an anchor a long way from a base. Low severity, medium confidence: every placement goes through `ValidPlacement`, but I did not trace whether anything else limits mine placement. It belongs in D29's measure-first list.
- **Optional row.** The harvester's capacity, load rate and unload time move from compiled constants into `com_harvester.yaml`.

**P8 proceeds unchanged.** Every remaining P8 row, P8-32 and P8-33 included, measures the shipped pool, where no metal exists. F5's income, F7's field count, F10's and F11's equal-credit budgets, Brutal's 5000 credits and every commander golden keep their meaning, and no F criterion moves. P8-42 and P8-45 may leave room for a metal refusal reason and two small counters, at no cost.

**P9's place.** P9 starts after P8-52 closes P8. That ordering has three benefits:

- its AI steps hook into the commander that P8-20 to P8-28 build, rather than one those rows are about to rewrite;
- its balance row measures `metals-01` on top of P8-32's numbers;
- nothing in P9 can confound a P8 row's attribution.

An earlier start is possible for M1 to M3 only, which are sim-only and golden-neutral on the shipped pool, and only with the owner's explicit sign-off recorded in the tracker. The reasons for caution are that those steps would interleave catalogue checksum moves (M1.1, M2.2, M3) with P8 rows that record their own, and would collide textually with the AI rows in `World.cs`.

**P9's done condition:**

- a metals match plays to a result against every personality, from both seats, with no wedge;
- `metalprobe` is green;
- M7.2's parity bar is met;
- look-dev legibility is signed off;
- the metals playtest is answered;
- M7.5's milestone gate is signed.

## Decisions to take now

Each is a recommendation, taken under the standing instruction once the owner confirms Decision 1, with the condition that would reverse it. The H rows and the P8-24 wording are defects and are taken now.

1. **Gathering: engineers found Pitheads on permanent lodes beside Ferrite, which stays truck-harvested.** This is the one question for the owner: did he mean metals added beside Ferrite or replacing it, and does a built extractor satisfy "mining"? Reverses if he meant trucks hauling metal; the hopper-and-haul step is the recorded next move, and the stocks, prices, AI helper and UI all carry over. Also reverses if he meant replacing Ferrite, which would reprice everything and move at least the four commander goldens plus economy, production, construction, capture, depot and the mission goldens.
2. **The set: Ferrite plus Tungsten and Titanium, assigned by role, with both factions spending both.** Reverses if the Legal review finds a role too close. Also reverses if the playtest wants more variety; in that case a third metal joins only if it prices hardware of both factions by a physical property, passes the originality rules and holds M7.2's parity bar.
3. **Treasury: credits unchanged, one stock per metal, no cap, no conversion, every stock starting at zero.** Reverses if a hoarded stock decides matches (add a cap), or if metal is routinely banked unspent (costs too low or yield too fast).
4. **Pricing.** `cost` stays the full value. The Ferrite share is `cost` minus `ingot_value` times `metal` where the metal is active. `metal = ceil(build_time_ticks / yield_interval_ticks)`. `ingot_value` is authored and held to the Outpost derivation. At today's values the Ferrite shares fall by 33 per cent (Howitzer, Phantom Tank), 28 per cent (Bulwark) and 15 per cent (both superweapons). Three of the five therefore exceed charter A11's 15 per cent line, and the Balance and Game Designer co-sign applies. Reverses if that co-sign prefers metal on top of the full price, at the cost of an equal-value definition for metals maps.
5. **Charging.** Whole ingots are taken when ordered, and the order is refused if short. They are returned in full on all six cancel paths and at half on sell. Queued ingots are returned on sell, refunded to the pre-capture owner on capture, and lost with a destroyed producer. Reverses if players find two payment rhythms confusing even with the tooltip.
6. **Activation per metal, from the map's lodes.** No reversal is foreseen; it is a correctness rule as much as a hashing one.
7. **A Pithead anchors placement exactly as an Outpost does.** Reverses if `metalprobe` or the playtest finds fortified lodes deciding matches. The fallback lets a Pithead anchor walls and gates only, at the radius walls already use; that cannot creep, because a barrier anchors nothing but barriers (ADR-005 clause 4).
8. **No lode at any start; Titanium only on contested ground, opening at one superweapon charge (5400 ticks).** Reverses if new players cannot find metal before the middle game; the fallback moves expansion lodes closer, never into a start. Also reverses if the opening reads as arbitrary; the fallback drops the floor and takes Decision 9's price-based figure instead.
9. **Titanium's superweapon price is the derived 4.** With the floor, a seat cannot bank Titanium before 6:00. Four ingots is 600 ticks of one Pithead, so briefly holding one centre lode is enough, and losing both is not a permanent lockout. Reverses if the playtest finds the centre uncontested once a superweapon stands; the fallback is a recorded exception at the price-based 7 (a quarter of 4000, divided by 150, rounded up).
10. **The selection rule and the seven laws, enforced by the loader and a stage.** Reverses only with a written reason per item, recorded in the ADR.
11. **Metals maps are named outside `skirmish-*`, kept out of P8's measurement pool, and closed to AI seats until M5 lands.** Reverses at P9's close, map by map, with `seatfairgate` and `ladderprobe` re-baselined.
12. **The guarded stock fold; lodes kept out of the catalogue checksum; H5's map content hash for LAN; catalogue checksum moves accepted at five steps.** Reverses if the Architect prefers an unconditional stock fold, at the cost stated under "What moves if the stock guard is refused".
13. **Sabotage pauses a Pithead.** The same row checks whether a sabotaged Outpost still pays, and if it does, makes it stop, so one rule covers both; `outpostgate` measures it. Reverses if the playtest finds sabotage one-sided.
14. **Theft takes a fifth of each stock.** Reverses if the playtest finds the Sodality's theft one-sided.
15. **The founding predicate: terrain and structures refuse, units of any owner are moved, and no structure but a Pithead may stand on a lode.** Reverses if moving an enemy unit proves exploitable, for example to pull a unit out of a defensive spot. The fallback refuses founding while an enemy unit that the founder's team can see stands on the lode, and moves the rest.
16. **The Pithead keeps the Outpost's 1000 hit points, so one demolition from either hero kills it, as it already kills an Outpost.** The reasons are consistency and cheap recovery: the lode is never lost, and founding again costs one 500-credit engineer. Reverses if `metalprobe`'s losses by cause or the playtest show the shadow commando's quiet kill deciding metals matches. The fallback is a recorded exception at 1200 hit points, the figure both superweapons carry, so one demolition leaves it standing.
17. **Brutal gets no metal handicap.** Reverses if `laddergate` on `metals-01` shows Brutal beating Hard in under 70 per cent of decided games. The fallback is a `starting_metal_handicap` knob in `data/ai` granting Brutal one Bulwark's metal (3 Tungsten) at the start.
18. **Numbering: this document is doc 32, its ADR takes the next free number claimed in the open queue, and its decision is D35 or the next free.** No reversal; numbering only.

## Risks

| Severity | Confidence | Risk | Mitigation |
|---|---|---|---|
| High | Medium | The owner may mean trucks hauling metal, or replacing Ferrite rather than adding to it | Decision 1 is asked before anything but documents changes; only P8-12's wording moves now |
| High | High | Two economies double the balance surface, and every F criterion measures the shipped pool only | P9 runs after P8 with its own done condition, balance row and milestone gate; metals maps stay out of P8's pool |
| High if it happens | Medium | Ingots laundered into credits, or silently lost, through a refund path (the four ready-slot refunds pay the full `cost` today; two cancel paths refund nothing today) | One pricing helper for every charge and refund; M1.7's per-path stage and both fuzz assertions, binding in CI |
| Medium | High | The catalogue checksum moves at five P9 steps, refusing older saves and replays each time | Pre-public, recorded per step; P9 starts after P8, so no P8 row's recorded checksum is disturbed |
| Medium | High | The Sodality has three quiet ways to hurt a Pithead and the Directorate none | Detection and lode fortification as answers; losses by cause in `metalprobe`; Decision 16's fallback |
| Medium | Medium | Heavy-tier demand differs by faction once P8-28 changes the cycles | M7.2's parity bar on ingot value spent; the remedy is a data change with a recorded exception |
| Medium | Medium | The founding walk: P8-56 found that a walk with no explicit target halts within four cells and never reaches a two-cell reach | M2.3 closes to reach the way an attack order does, the fix P8-56 prescribes; reuse P8-56's fix if it lands first |
| Medium | Medium | Engineers (60 hp) die on the way, and the commander keeps one at a time today, so its metals play looks weak until M5.2 | Metals maps human-only until M5; escort on the staging ring |
| Medium | Medium | LAN peers agree on a map by path alone | H5 is required before any metals map is offered over LAN |
| Medium | Medium | A classic player must learn split prices, two payment rhythms and a timed lode | Full value in every tooltip; the first-encounter line; refusal reasons; playtest questions 2, 9 and 10 |
| Medium | Medium | Tungsten's set includes the Phantom Tank, a stealthed unit, by tier | The rule never reads stealth and both factions' items are in the set; first item on the Legal checklist |
| Low to medium | Medium | Two greys told apart by shape and value on a dark battlefield; both names begin with T | Shapes first, words on every counter, a light outline on every lode; look-dev under simulated deficiencies; playtest question 12 |
| Low | High | The ingot's value is coupled to Outpost income, so an A11 change to the Outpost moves every Ferrite share on metals maps | Intended; the derivation stage makes it loud |
| Low | High | The selection and derivation stages are tripwires: P8-32's price and build-time changes fail them until the metal is re-derived or an exception recorded | The A11 workflow names the stages |
| Low | Medium | Losing queued ingots with a destroyed producer may feel punitive | Playtest question; consistent with lost `BuildPaid` |
| Low | Medium | Metal alerts are absent in LAN until P8-58 lands | Inherited from P8-58, which owns the fix |
| Unverified | | Whether any catalogue query in the AI or the client picks up a new structure def unasked (sidebar, `mcvtechgate`, the commander's ladder) | M2.2's byte-compares and the sidebar check |
| Unverified | | The review's reading that `AdvanceBuildLanes` charges the new owner and `PlaceStructure` finds a lane's ready building by ownership alone | H4 reproduces it with a failing stage before fixing |
| Process | High | The owner's confirmation and CLAUDE.md edits, Producer sign-off for a new structure and maps, A11's co-sign and the P9 milestone gate are all needed | G0 lists each, and nothing in M1 starts without them |

## Playtest questions

1. After one match on `metals-01`, can a first-time player say what Tungsten and Titanium are for without opening a tooltip?
2. Did the first-encounter line explain enough to found a first Pithead without help?
3. Are the counters read before a button greys, or only after? Do they earn their place on the screen?
4. When is the first Pithead founded, and does founding feel like a decision or a chore?
5. Are engineers bought, escorted and fought over, and does "the engineer that got through" happen?
6. Does each side go for the Tungsten lode in the other's expansion, and does anyone found a Pithead purely to deny one?
7. Does metal bind before credits do, and at roughly what minute? In the late game, when Ferrite is thin, does metal decide more than it should?
8. When a player loses every Tungsten Pithead, do they feel slowed and forced to adapt, or locked out? Ask it of both factions.
9. Is "ingots when you order, Ferrite as it builds" understood, and is the split price "1150 + 3" with "WORTH 1600" clear?
10. Does Titanium opening at 6:00 read as a natural flashpoint or an arbitrary timer? Do the centre lodes produce fights, and does needing them make the superweapon feel earned or merely later?
11. On the receiving end, does a Pithead demolished by an unseen hero, switched off by a Saboteur or robbed by an Infiltrator feel clever or unfair?
12. Can colour-blind players, and players on a small screen, tell Tungsten, Titanium and Ferrite apart at a glance on the battlefield, the minimap and the sidebar? Is TUNGSTEN ever misread as TITANIUM?
13. Is metal ever banked unspent for long?
14. Does fortifying a lode with turrets make the game better, or turn the centre into a siege nobody can break?
15. Does the commander's metals play read as competent: does it found, claim Titanium, substitute when short, and retake what it loses?
16. Does losing ingots queued at a destroyed factory feel fair?
17. Asked of a player who has not seen these notes: did anything about the metals remind you of another game, book or film, and if so, which element?

## Originality rules

These are the positive rules every metals proposal follows. The Legal agent checks them at milestone sign-off, together with the checklist kept in doc 09 section 7.

1. **Metals are materials.** They are spent at a producer as part of a price. Nothing is fuelled, charged, primed or strengthened by a metal, no unit carries or consumes metal on its person, and a building that carries a support power costs no metal.
2. **Each metal's role is named by a physical property it has in real industry** (dense and hard; strong at high temperature). The hardware that takes it follows from that property and from its price tier, never from an ability.
3. **The set is small, uneven and unpaired:** one structural material and two strategic metals, with no opposites, no axes, no grid and no pairing of a metal with its alloy. Each item takes at most one metal.
4. **No metal is tied to concealing or revealing, and no metal's item set may be defined by stealth or detection.**
5. **Every metal prices hardware of both factions wherever both field that role, and both factions mine every metal.** Any engineer founds on any lode, and specialists are buildings and vehicles, never people.
6. **The visual language is industrial and geological:** rock outcrops, headframes with a turning wheel, ore hoppers, and ingots shown as bars, never as coins. A metal's icon is its name, its chemical symbol, an ingot silhouette or a plain geometric shape, never a bespoke alchemical or sigil-style symbol, and any icon art goes on the Legal checklist.
7. **Player-facing words for metal come from the vocabulary fixed in doc 09 section 7.** The legal check enforces the coined terms and the Legal agent's checklist covers the rest. No player-facing text calls Titanium the rarest or most precious metal.
8. **Design documents describe the lineage actually used:** real metallurgy and the genre's multi-resource economies. Public copy keeps the approved line, "inspired by the classic RTS games of the 90s". No commit message, PR description or journal entry for this work names or describes any source beyond "metals with distinct properties", because the CI grep reads `docs/` (the journal included) but never a commit message.

## Changed / Assumed / Needed next

**Changed.** This document only. It is a proposal: no code, data, map, tracker or ADR has been edited.

**Assumed.**

- The owner's message means metals added beside Ferrite, and a built extractor satisfies "mining". Both are asked once, as Decision 1, before anything else moves.
- The Architect accepts the guarded stock fold; the cost of refusal is stated.
- P8-56 or M2.3 gives a founding walk that reaches its lode.
- P8-19 carries H1 and H3 in its checksum move.
- P9 starts after P8-52, unless the owner signs off an earlier start for M1 to M3.
- Every line number is a hint, because the checkout moved during the reading, and every derived price is recomputed from the values standing when P9 starts.

**Needed next, and from whom.**

- **From the owner (Luke):** an answer to Decision 1; the CLAUDE.md Legal rule widening (useful now, whatever the answer) and the phase-gate line on confirmation; authority to amend GDD s4; a view on P9's size.
- **From the implementer, now:** P8-12's one-line amendment before P8-12 starts, and the H1 to H5 rows plus the P8-24 wording in P8.
- **From the Producer:** sign-off for the Pithead and for metals maps, recorded with D35.
- **From the Architect:** the ADR's ruling on the guarded stock fold, the founding command's encoding and the catalogue checksum moves.
- **From Balance and the Game Designer:** A11's co-sign on the Ferrite shares at M3, and M7.2's verdict.
- **From the Legal agent:** review of the patterns and of doc 09 section 7's checklist, with Tungsten's inclusion of the Phantom Tank as the first item.
