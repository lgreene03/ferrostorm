# Doc 27: Design Review, 2026-07-25 - Gaps and the Features Worth Adding

Owner: game-designer + producer. Phase: 6. Reviews the shipped game against the
GDD (doc 02), the faction bible (doc 15) and the genre canon, and ends with a
prioritised register of gaps and candidate features.

**How this review was made, stated up front.** Nobody has played this build.
The evidence base is: the live /data numbers and the sim code they drive; the
balance simulator's engagement matrix and siege table (tools/Ferrostorm.Balance,
run 2026-07-25); the three mission files and their briefings; the AI's actual
decision rules read from SkirmishAI.cs; and a cited survey of the genre canon
(C&C 1995/RA96 retrospectives, the 2020 Remaster's QoL record, OpenRA's
modernisation set, current retro-RTS releases, and published design writing on
asymmetry, skirmish AI and victory conditions - the source list is at the foot).
Where this review says "measurably", a number from the repo's own
instrumentation backs it. Where judgement substitutes for play, it says so.
**The human playtest of build/macos/Ferrostorm.app remains the single most
valuable missing input to everything below.**

---

## Part 1: the designer's review, by the GDD's own pillars

The GDD names five pillars. They are the right rubric, because they are the
promises the game made to itself. Delivery against them is uneven in an
instructive way: the pillars about SYSTEMS are largely delivered, and the pillar
about IDENTITY is barely started.

### Pillar 1, "readable in one glance": largely delivered, art-bound

The counter-triangle is three legible weapons; the team-colour law is one
predicate; the brown-out, radar blackout, stances, capture and drain states all
have readouts; the outpost and ferrite fields explain themselves on inspection.
The residue is art-pipeline, already filed: the repair vehicle wears the MCV's
model, the outpost has a stand-in, and several sidebar icons are uncut. Nothing
new to add here; the design work is done and waiting on assets.

### Pillar 2, "fast, decisive, generous": delivered, with three convictions from the repo's own instrumentation

The economy is HOT. A harvester on a ten-cell run delivers roughly 2,150
credits per minute against a 600-credit tank, so one protected harvester
finances a tank every seventeen seconds. Games will resolve inside the GDD's
15-30 minute window almost by force: home clusters hold 48,000-72,000 credits
and strip in eight to twelve minutes, expansion pressure arrives on schedule,
and the superweapon recharges every 100 seconds forever, so a stalemate ends by
attrition strikes. Decisive is delivered.

Three convictions, all from the balance tool, none of which needs a playtest to
believe:

1. **The faction war is 0-6.** Sodality sweeps Directorate in AI-vs-AI across
   every seed, against a GDD target of a 48-52 per cent band. The tool marks
   this reporting-only pending human play, and that is right, but a shutout
   this clean seldom inverts under human hands.
2. **Massed rifle squads are close to a universal per-cost answer.** At equal
   credits they beat the howitzer, the phantom, the bulwark and everything else
   except the vanguard car (their designed counter, which only wins 60 per
   cent) and other infantry. This is Red Alert's tank-rush degeneracy inverted
   into a rifle rush, and the genre history says a single dominant per-cost
   blob is the thing players discover first.
3. **Static defence measurably cannot hold.** Every siege configuration in the
   tool's table ends BREACHED; the howitzer at range 9 outranges the turret at
   range 5 and shells over walls that buy seven per cent of a siege. The tool
   itself records that the "MUST NOT breach" expectation is measurably false
   since ADR-010, with the rebalance-or-withdraw decision parked in ADR-010's
   consequences. The classics' answer was a range-13-versus-range-5 artillery
   duel against defences WITH minimum ranges and repair; here there is one
   turret type, no repairing AI, and nothing between the wall (100 credits)
   and the turret (600).

On "generous": the comeback rule exists (rebuild an MCV and survive
elimination), but it is exercised by nobody. The AI never rebuilds an MCV to
survive; a player has no idle-anything key to notice their economy has died;
and the endgame research's sharpest point applies squarely: the loser's last
minutes are undesigned. The classics stumbled into a solution with the Fire
Sale (a beaten AI sells everything and throws one final wave), which reviewers
still cite as the reason mop-up ends with a bang. Ferrostorm's beaten AI
simply runs out of things quietly.

### Pillar 3, "asymmetry with personality": the least-delivered pillar, and the review's headline

Honestly measured: of 13 unit types, 6 are common, 5 Directorate, 2 Sodality;
of 12 structures, 11 are common. Both factions build the same base, mine the
same fields, place the same turret and fire the same superweapon on the same
timer. Sodality's identity is one mechanic (stealth) expressed three ways, and
it has no artillery, no heavy armour, no anti-infantry vehicle and no detector
of its own - a Sodality mirror match has no stealth counter beyond the
three-second firing reveal. The Directorate has no faction mechanic at all; it
is the common game plus more and bigger vehicles. Doc 15 is honest that
identity currently lives in "the specials" plus AI doctrine, and the AI
doctrine IS genuinely characterful (phantoms waging the shadow war on
harvesters, sentinels escorting) - but that means faction personality is
carried by the opponent's scripts, not by the player's own hands.

The design writing on asymmetry converges on two tests, and the shipped game
fails both. First, asymmetry should be THEMATIC and live WHERE THE GAME'S FOCUS
IS. This game's declared focus is the economy (pillar 4), and the factions'
economies are byte-identical. The GDD already knows the answer it wants:
centralised versus decentralised power, tough-expensive versus cheap-swarm
buildings, a raid-dependent economy for Sodality. None of it is built. Second,
"do not strip core tools": Sodality lacking any detector fails this test the
moment two Sodality players meet.

This is the review's principal recommendation: **before adding breadth (air,
more units, more missions), spend one wave making the two factions economically
and structurally distinct.** It is the pillar furthest behind, it is the one
the genre literature says matters most, and its cheapest expression is numbers
the /data files already hold: different plant sizes and costs, different
refinery economics, a Sodality detector answer. The register below breaks this
into steps and files the priority question, because which step first is a
designer's call.

### Pillar 4, "the economy is the battlefield": the best-delivered pillar

The bones are genuinely good. The harvester loop is hot enough that raiding it
matters more than army trades; the AI's phantom doctrine and sentinel escorts
make the harvester war real even against the computer; outposts add the
map-control income the GDD wanted; regrowth is tuned an order of magnitude
below extraction so expansion pressure is genuine; and the harvester load
readout finally makes the loop legible. Two gaps, both small and both now
visible because everything else works: there is no idle-harvester key (the
single most-used economy control in the genre), and the GDD's "refinery
includes one free harvester" promise is unimplemented - the AI's own build
ladder carries a documented workaround for the deadlock this causes, which is
the code telling us the design was right.

### Pillar 5, "modern hands, classic heart": quietly ahead of the canon

The surprise of this review. Against the 2020 Remaster's QoL record, Ferrostorm
already ships the two most-cited additions (production queues, camera zoom) AND
the one thing reviewers still ding the Remaster for refusing: attack-move.
Against OpenRA's de facto modernisation spec, it has attack-move, veterancy,
fog, stances (exceeding it: hold-fire, guard AND patrol), range rings on
placement, capturable tech structures and shift-queueing. Replays with
hash-verified playback exceed the canon entirely.

What is missing is the selection-and-camera layer, and it is nearly all
client-only work: no select-all-army key, no double-click or Ctrl-click
type-select, no camera bookmarks (GDD-promised), no control group 0, no idle
key, no attack-ground or force-fire, no minimap ping, no grid build hotkeys
(the sidebar is mouse-only). Individually small; together they are the
difference between hands that feel modern and hands that feel almost modern.

---

## Part 2: the reviewer's verdict

Reviewing what is on disk today, as a critic would receive it, with the honesty
that this reviewer has read the game rather than played it.

**The technology story would lead every review.** A deterministic, fixed-point,
zero-dependency lockstep sim with 24 cross-platform golden hashes, bit-exact
saves and replays, a headless client harness with 95 checks in CI, and working
LAN whose two ends provably build identical worlds - this is an engineering
posture most shipped RTS games never reach, and it is the moat. It is also,
today, better than the game it carries.

**The game on top is a strong vertical slice with a thin middle.** Minute one
to minute six is genuinely classic: the build cadence, the power curve bite,
the first raid. The middlegame is where the thinness shows: seven weapons
across thirteen units means armies feel samey; the mirror-base economies mean
faction choice changes your vehicle tab, not your plan; a known-dominant rifle
blob and defences that measurably cannot hold mean the discovered meta will be
shallow until the parked balance decisions are taken. The endgame ends
decisively (superweapon attrition guarantees it) but not memorably (no Fire
Sale, no surrender, no designed last minutes; Q015 already holds the
LAN-departure half of this).

**The campaign is three missions that are better than they have any right to
be.** The mix is exactly the canon shape - guided base opener, no-base commando
set piece, hold-the-line - and the briefings have real voice, with a mole
subplot threaded through all three that simply stops. Mission 2's "the wrench
is the objective" design (lose the engineer, lose the mission, now enforced) is
the best design in the game. A critic would call the campaign a proof of
concept and dock the score for its length, then admit they played all three
twice.

**The AI is honest, characterful and shallow.** It plays by the command rules,
raids with faction personality, defends its harvesters, expands once and takes
outposts - and it is omniscient about the map, never repairs a building, never
uses two of Sodality's three specials, and its three difficulty presets differ
only in wave size. The research is blunt that repair, harvester replacement,
expansion and visible reaction to raids are the four behaviours players
actually notice; it delivers two of four. "Lose convincingly" is not yet in its
vocabulary, and neither is losing with a bang.

**Verdict.** As an Early Access release today: the engine earns a 9, the game a
6.5, and the honest headline would be "the best-built RTS skeleton in years is
still waiting for its two factions to disagree about something". The
one-more-match hook - the harvester war plus outpost control - is real. What
sends a player to bed instead of one more match is that the second faction
plays like the first with different silhouettes.

---

## Part 3: the prioritised register

Effort: S under a day's wave, M one to three waves, L a campaign of waves.
Hash column: NEUTRAL means provably no golden moves; MOVES means goldens
regenerate and therefore an ADR plus Architect sign-off per the standing law.
Existing blocked items are referenced, not relitigated.

### Tier 1: the asymmetry package (the recommendation)

| Id | Item | Effort | Hash | Blocked on |
|---|---|---|---|---|
| DR-01 | **File and answer Q017**: which faction-identity step first (power economics, refinery economics, Sodality detection, faction superweapons) | S (the decision) | n/a | Luke + game-designer; Q017 filed with this review |
| DR-02 | Faction power economics: Directorate one big expensive plant, Sodality several small cheap ones (the GDD's own centralised/decentralised promise; mostly /data numbers plus a second plant def) | M | MOVES (openings change) | Q017, then an ADR; needs the Faction column on StructureTypeDef already filed as a schema wave |
| DR-03 | A Sodality stealth-detection answer (unit flag on an existing unit, or a cheap structure), closing the mirror-match hole and the "do not strip core tools" failure | S-M | NEUTRAL if a new def nothing spawns | Q017 sequencing; roster additions brush C9 (Luke's roster call) |
| DR-04 | Faction-distinct superweapons (GDD s8: Directorate single-point, Sodality area-denial that kills fields - the second one is the economy-warfare identity in one feature) | M-L | MOVES | ADR; art for the new effect |

### Tier 2: unblocked quality, mostly client-only

| Id | Item | Effort | Hash | Blocked on |
|---|---|---|---|---|
| DR-05 | Select-all-army key, double-click and Ctrl-click type-select, control group 0 | S | NEUTRAL | nothing |
| DR-06 | Idle-harvester key (and an idle-army variant), the genre's most-used economy control | S | NEUTRAL | nothing |
| DR-07 | Camera bookmarks F1-F4 (GDD s10 promise) | S | NEUTRAL | nothing |
| DR-08 | Grid build hotkeys for the sidebar (GDD s10 "grid hotkeys default") | M | NEUTRAL | nothing |
| DR-09 | Minimap ping (foundation for any future team play; solo it is a self-note) | S | NEUTRAL | nothing |

### Tier 3: the endgame package

| Id | Item | Effort | Hash | Blocked on |
|---|---|---|---|---|
| DR-10 | DELIVERED (TICKET-DR-10). AI Fire Sale: a beaten AI (no yard AND no MCV, strictly conservative) sells out and throws one last wave; an MCV in hand deploys instead. The branch lives in the formerly silent decapitated state, and the neutrality proof ran FIRST: 24 goldens byte-identical. Gated by firesalegate | M | NEUTRAL, PROVEN | nothing |
| DR-11 | Surrender in LAN plus AI capitulation offer | M | NEUTRAL (client + net) for surrender; capitulation is AI | Q015 (already filed) owns the result semantics; do not build ahead of it |
| DR-12 | The rifle-blob and static-defence convictions: promote the parked ADR-010 decision (rebalance defence or withdraw the expectation) and the per-cost review of TestRifle | M | MOVES | Balance + Game Designer co-sign (A11); the tool's rows stay visible every run meanwhile |

### Tier 4: AI depth (the four noticed behaviours)

| Id | Item | Effort | Hash | Blocked on |
|---|---|---|---|---|
| DR-13 | DELIVERED (ADR-026). AI repairs damaged structures: one additive block flips Repair on once per damage episode. Proven NOT neutral (the concern the row named) - the skirmish golden moved because that AI-vs-AI match batters a structure the AI now mends - so it shipped under a ratified pre-first-public-build regen, not as a neutral change. Pinned by airepairgate | S-M | REGEN (skirmish only) | ADR-026 + Luke sign-off (done) |
| DR-14 | DELIVERED in the sim (doc 28). The Easy-to-Brutal ladder, orthogonal to personality: difficulty owns the decision beat and the harvester headroom, Brutal's declared handicap lives in SETUP because a self-granting AI would desync its own replays. This row's "MOVES" prediction was WRONG and the diff proved it: Normal is the identity rung, so all 24 goldens are byte-identical. Pinned by difficultygate, and REACHABLE since the DR-14b follow-up wave: a DIFFICULTY picker with the handicap declared in the Brutal item, plumbed through the sidecar and the Hello (doc 28 s7) | M | NEUTRAL, MEASURED | the design note (doc 28, written) |
| DR-15 | AI scouting honesty: fog-filter its entity reads so information play (stealth, feints) works against it | L | MOVES | ADR; substantial redesign of its target selection |

### Tier 5: content breadth (deliberately after identity)

| Id | Item | Effort | Hash | Blocked on |
|---|---|---|---|---|
| DR-16 | Missions 4-6: continue the mole arc the briefings opened; keep the canon mix (one set piece per two builders) | M each | NEUTRAL (data + triggers) | Q012/Q016 first (win/loss semantics), then mission design |
| DR-17 | Naming: TestRifle, TestCannon, TestRocket persist in the shipped catalogue's weapon ids | S | NEUTRAL (display names live in /data; ids are internal) | nothing, but coordinate with any schema wave |
| DR-18 | A fifth and sixth skirmish map exercising the underused vocabulary (destroyable bridges appear on one map; outposts on two) | S-M each | NEUTRAL (new files) | map-design doc 26 invariants apply |

### Findings filed as defects rather than features

| Id | Finding | Disposition |
|---|---|---|
| DR-19 | SpawnHarvester hardcodes speed 1/5 and ignores the catalogue's authored speed 18 - the sim overriding its own /data, the exact class ADR-006 exists to prevent. Fixing it MOVES every harvester golden, so it needs the standing authorisation; until then the YAML number is a lie | needs ADR + regen authorisation; recorded here so it is not rediscovered |
| DR-20 | GameEventType.Captured is still consumed by nothing (surfaced in an earlier survey; ADR-021 silent on notification). The capture alert is a natural fifth alert but is a UX + audio call | already on the not-taken list; referenced for completeness |

### Explicitly out of scope, per standing decisions

C6b wall gates (ADR-005 clause 6), C8 multi-resource (Q014), C9 roster
(Luke's), mission 03's defeat path (Q016), harvester tempo (P5-ECON-04/05),
turtle doctrine (DEF-14), refinery free harvester (P5-ECON-13, though this
review notes the AI code itself argues for it), the ore heap and all model/icon
work (art-pipeline), BD-20 queue cap, the air layer (C5: ADR plus art), naval
and FMV (GDD amendments), crates and the map editor (Producer rule).

---

## Sources

Repo instrumentation: tools/Ferrostorm.Balance run of 2026-07-25 (engagement
matrix, siege table, faction war, tempo baseline); /data catalogue;
sim/Ferrostorm.Sim (World.cs, Combat.cs, SkirmishAI.cs, MapLoader.cs,
MissionRunner.cs); the four maps, three missions and three briefings.

Genre canon (external, cited in the research digest of 2026-07-25): C&C 1995
and RA96 retrospectives (Game Developer/Game Wisdom, SUPERJUMP, aarmstrong.org,
GameSpeak); the C&C Remastered Collection QoL record (Wikipedia, bit-tech,
Windows Central, TechRadar); OpenRA's modernisation set (openra.net, its wiki
and community guides); Tempest Rising, D.O.R.F. and Rusted Warfare reception;
asymmetric faction design writing (Callum McCole; Dustin Browder's GDC 2011
e-sport talk); the C&C GPL source AI analysis (AI and Games via Game
Developer); victory-condition design writing (Matchsticks for my Eyes, Wayward
Strategy). Full URLs live in the research digest retained in the session
record; the load-bearing claims above are each traceable to one of these.
