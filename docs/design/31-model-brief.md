# 31. Model brief: what every unit and building should look like

Date: 2026-08-03. A per-entry visual brief for all 42 catalogue entries, so that
the 22 owed models can be built without re-deciding the shape language each time.

**This document invents nothing it did not have to.** Doc 16 is the style bible
and its law is inherited verbatim; 20 entries already have an established shape
(sprite, shipped model, or both) and those are *recorded* here rather than
redesigned. Section 7 marks exactly which descriptions are new.

---

## 1. The law, inherited from doc 16

> Silhouette-first: **every unit must be identifiable as a 40-pixel blob.**
>
> The law: **team colour appears in exactly one place per silhouette (the
> band/slash), always.**

Palette, unchanged:

| | colour |
|---|---|
| Ground / cinder | `#16181a` |
| Plating / seams | `#232629` / `#2e3236` |
| Ferrite gold (the resource, and every trim) | `#c9a86a`, highlight `#e0c288` |
| **Directorate** gunmetal / plate / shadow | `#5b6770` / `#78848c` / `#3d454b` |
| Directorate team mark | signal orange `#e8762c` |
| **Sodality** rust / plate / shadow | `#8a4a34` / `#a35c40` / `#5c3122` |
| Sodality team mark | corroded teal `#4fb8a8` |
| Common hardware | field olive `#6e6a5e` with ferrite-gold marks |

### A discrepancy this brief must flag

Doc 16 requires the team mark **on the silhouette**. The client currently renders
team colour as a **ring on the ground** (`BattlefieldView.DressMobile`), and the
models carry none. So doc 16's law has never been implemented.

Doc 30 arrived at "move team colour onto the model" from research. It should be
recorded that **doc 16 required it all along** - this is not a new idea, it is an
unbuilt one. Every brief below therefore names a band/slash location, and those
locations are useless until the shader change lands.

## 2. The two shape languages

Doc 16 gives the words; doc 30's research gives the levers that survive at 40-60px.

**DIRECTORATE - the wall.** Slab-sided, symmetric, *issued*. Bilateral symmetry,
level tops, horizontal lines. Repeated modular forms so the roster looks
manufactured from one kit. Flat plated surfaces, few large planes, no clutter.
Uniform livery, insignia always in the same place. Wide, low, planted.

**SODALITY - the shadow.** Angular, asymmetric, welded-from-salvage. Broken or
tilted rooflines. Mismatched parts and visibly civilian chassis. Silhouette-
breaking additions on top: cargo, tarpaulins, aerials, exposed crew. Narrow, tall,
leaning. **Upgrades should show as added geometry** - the C&C: Generals GLA
precedent, where scavenged armour appears on the model.

**COMMON hardware** is field olive and reads as *neither*: utilitarian, boxy,
unglamorous. It is the stuff both sides inherited rather than built.

**Where the shape budget goes.** At a high angled camera you see the **top and
upper profile** - roofline, turret, mast, aerial, cargo - not the flanks. Spend
the identifying geometry there. Detail below roughly 10cm on a 3m vehicle is
invisible; a 2-3cm bevel is pure waste.

## 3. The derived gap

| | count |
|---|---|
| Catalogue entries | **42** (20 units, 22 buildings) |
| With an established sprite | 19 |
| With a bespoke `.glb` | 20 |
| **With neither - the true gap** | **22** |

The 22 match the owed-models count exactly, derived independently.

---

## 4. Units

Format: **silhouette at 40px** / team mark location / the one distinguishing
feature. `[E]` = an established shape already exists (a sprite in `art/sprites/`, a shipped
`.glb`, or both). `[N]` = new here, and one of the 22 owed models.

### Common (field olive, ferrite-gold marks)

**com_rifle_squad** `[E]` 200cr, 100hp
A **dot-cluster**, per doc 16. Four or five small upright figures in loose
formation, reading as one blob with texture rather than one object. Team band on
the shoulder plane - the only surface angled towards a high camera.

**com_rocket_squad** `[E]` 300cr, 80hp
The same dot-cluster, but each figure carries a **long diagonal** over the
shoulder. That diagonal is the entire read: rifle squads are vertical ticks,
rocket squads are ticks with a slash. Team band on the shoulder.

**com_engineer** `[E]` 500cr, 60hp
A **single** figure, not a cluster - immediately different from every other
infantry. Hunched, carrying a boxy case. Ferrite-gold hard hat: the one bright
point, so a player can find him in a crowd. No weapon silhouette at all.

**com_harvester** `[E]` 1400cr, 700hp
Doc 16: **a fat beetle with a gold hopper.** Wide, low, no turret. The hopper is
the read and it should visibly **fill** - ferrite gold rising as it loads is the
best free feedback in the game. Team band low on the flank.

**com_mcv** `[E]` 3000cr, 600hp
The **largest vehicle silhouette**. A slab on tracks, unarmed, with visible fold
seams that promise it unpacks. Reads as cargo, not weapon. Team slash across the
back plate.

**com_repair_vehicle** `[N]` 700cr, 300hp
Small, boxy, unarmed, with an **arm or crane folded on the roof** - the top
profile is the read, since it has no turret to distinguish it. Ferrite-gold tool
markings. Currently borrows the MCV mesh, which is far too big and reads wrong.

**com_carrier** `[N]` 600cr, 350hp
Unarmed transport for five. A **long flat deck** with low sides - the silhouette
of a thing that carries, deliberately empty on top so cargo reads when loaded.
Wider than it is tall. Team band on the cab.

**com_flak_track** `[N]` 550cr, 260hp
Light chassis with a **short fat barrel cluster angled upward**. The upward angle
is the read at distance: this is the only ground unit pointing at the sky. Open
mount, exposed gunner.

**com_strike_flyer** `[N]` 1100cr, 180hp
The only airborne unit. A **thin forward-swept dart**, small, no bulk anywhere.
Must read instantly as *not on the ground* - which the shadow gap does, so keep
the mesh spare. Team mark on the tail fin, the highest visible surface.

### Directorate (gunmetal, signal orange)

**dir_cannon_tank** `[E]` 600cr, 300hp
The **baseline slab**: symmetric, level-topped, a single centred barrel. Every
other Directorate vehicle is read against this one. Team band across the hull
front, square to the world.

**dir_bulwark_tank** `[E]` 1600cr, 550hp
Doc 16: **a slab.** The widest, lowest, heaviest silhouette in the game - "the
wall that walks". No visible weak points, no clutter, tracks as thick as the hull.
Should look like it costs 1600 credits from 40 pixels away. Team band low and wide.

**dir_howitzer** `[E]` 900cr, 160hp
Artillery: a **long barrel over a small hull**, with the barrel elevated so the
top profile is unmistakably a gun pointing far away. Visible recoil housing.
Fragile-looking, which is honest at 160hp.

**dir_sentinel_scout** `[E]` 400cr, 90hp
Light, fast, and the faction's **eyes** - a tall thin **mast or dish** above a
small hull. The mast is the read and it must clear the two-pixel threshold, so
make it taller than looks natural.

**dir_vanguard_car** `[E]` 450cr, 150hp
Wheeled, not tracked - the **only Directorate wheels**, which is the distinction
from the cannon tank at distance. Low open-topped body, small forward gun.

**dir_commando** `[N]` 1500cr, 200hp, one alive
A single figure like the engineer, but **armoured and upright** where the engineer
hunches. Larger than any other infantry - hero scale is permitted and expected.
Signal orange should be generous here: this is a unit the player must never lose
track of.

### Sodality (rust, corroded teal)

**sod_shade_raider** `[E]` 500cr, 150hp
Fast, stealthed, improvised. **Asymmetric** - weapon on one side only, salvage
strapped to the other. Low and leaning forward. Reads as a civilian vehicle that
has been made dangerous.

**sod_phantom_tank** `[E]` 900cr, 200hp
Doc 16: **a faceted wedge.** All flat angled planes, no curves, no clutter - the
stealth read. Narrow front, wide rear. When cloaked the mesh should still be
*present* as a distortion, not absent.

**sod_infiltrator** `[N]` 700cr, 90hp
Single stealthed figure. **Civilian silhouette** - no visible weapon, coat rather
than webbing. Should read as "a person", which is exactly what makes it
frightening among the Sodality's welded machines.

**sod_saboteur** `[N]` 600cr, 80hp
Same civilian read as the infiltrator, distinguished by a **satchel or charge
carried low** - the one geometric addition. The pair should be confusable at a
glance and separable on a second look, which is the doctrine.

**sod_shadow_commando** `[N]` 1500cr, 200hp, one alive
The Sodality hero. Upright and armoured like its Directorate twin, but
**asymmetric and hooded**, with mismatched plate. Corroded teal generous, same
rule: never lose track of it.

---

## 5. Buildings

Footprint `fp2` = 2×2, `fp1` = 1×1.

### Common infrastructure (field olive)

**com_construction_yard** `[E]` 3000cr, 3000hp, fp2
The **anchor of the base** and it should look it: the largest, heaviest footprint,
with a visible gantry or crane on top. Everything else is built from here, so it
reads as a factory that makes factories. Ferrite-gold trim on the gantry.

**com_refinery** `[E]` 2000cr, 2000hp, fp2
A **docking bay with a visible opening** on one side - the harvester goes *in*.
Tanks and pipework above. Ferrite gold on the pipes: this is where the resource
becomes credits, so the gold should be brightest here of anywhere.

**com_factory** `[E]` 2000cr, 1500hp, fp2
A wide shed with **large doors on one face** and a flat roof. Vehicles come out,
so the door must read at 40px. Roof plane deliberately plain - it is a workshop.

**com_barracks** `[N]` 500cr, 800hp, fp2
Smaller and cheaper than the factory, and must not be confused with it. **Pitched
roof** against the factory's flat one, small personnel door rather than a vehicle
door. Currently borrows the service depot, which is wrong in both size and read.

**com_power_plant** `[E]` 300cr, 150hp, fp2 *(Directorate-only since P7-5a)*
Doc 15's centralised grid: **fewer, bigger, juicier targets**. Cooling stacks or
a turbine housing on the roof - the top profile is the read. At 150hp it is the
most fragile building in the game and should look it: thin walls, exposed plant.

**com_service_depot** `[E]` 1200cr, 1000hp, fp2
A flat apron with a **repair gantry over it** - an open structure, not a sealed
box. That openness distinguishes it from the barracks and factory.

**com_radar_uplink** `[N]` 900cr, 1000hp, fp2
The **tallest thin structure** in the game: a mast or dish on a small base. Pure
vertical, which nothing else is. Ferrite-gold dish face. Currently borrows the
veil projector - a Sodality building standing in for a common one, which breaks
the faction read entirely.

**com_airfield** `[N]` 1800cr, 1100hp, fp2
A **flat pad with a control mast at one corner** - mostly horizontal, deliberately
low so the aircraft on it reads as the tall thing. Painted landing markings in
ferrite gold.

**com_outpost** `[N]` 500cr, 1000hp, fp2 *(neutral, capturable)*
Must read as **nobody's**: bleached, weathered, no faction plate, no team colour
at all until captured - at which point the band appears. A small blockhouse with a
flag mast. Currently borrows the refinery, which is badly misleading since it
implies an economy building.

**com_bridge** `[N]` 400cr, 800hp, fp1 *(map-placed, destroyable)*
Flat span, no superstructure, reads as terrain rather than building. Should look
*structural* - visible trusses underneath - so that felling it feels consequential.

### Defence (common)

**dir_turret** `[E]` 600cr, 400hp, fp2
Small base, **rotating gun on top**. The gun is the read; the base is almost
incidental. Common hardware despite the `dir_` id.

**com_emplacement** `[N]` 350cr, 300hp, fp2
Cheaper and lower than the turret: a **sandbagged ring** with a short weapon,
mostly horizontal. Anti-infantry, so it should look like a pit rather than a tower.

**com_wall** `[N]` 100cr, 500hp, fp1
A plain segment. **The one rule that matters: it must tile seamlessly** and read
as continuous from above, since players build them in runs.

**com_gate** `[N]` 200cr, 500hp, fp1
Visibly a wall segment **with a moving part** - a gap and two posts. The open and
shut states must be distinguishable at 40px, which means the moving element needs
to be large and to change the silhouette, not slide inside it.

**com_mine** `[N]` 400cr, 100hp, fp1
Small, low, mostly flush with the ground. Barely a silhouette by design. A single
ferrite-gold pressure plate is the only visible feature - and it should be visible
*only to its owner's player colour*, which is a shader question.

### Directorate structures

**dir_bastion** `[N]` 1400cr, 1600hp, fp2
The Directorate's own defence and its **toughest building per credit**. A squat
armoured blockhouse - thick sloped plate, a heavy weapon in an embrasure rather
than a turret on a pole. Should read as fortification, not as a gun emplacement.
Now also carries two support powers, so a **dish or antenna** on the roof is
earned. Currently borrows the turret, which badly under-reads 1400 credits.

**dir_superweapon** `[E]` 4000cr, 1200hp, fp2 *(orbital cannon)*
The largest and most vertical Directorate structure: a **cannon aimed upward**,
with the barrel as the whole silhouette. Charge state should be readable - ferrite
gold intensifying up the barrel as it fills is the obvious language, and it
matches the viewer's ferrite-seam glow.

### Sodality structures

**sod_generator** `[N]` 130cr, 70hp, fp1
Doc 15's **decentralised power: many small generators.** The cheapest and most
fragile thing in the game at 130cr/70hp. A single **salvaged drum** with cabling.
Players will build a dozen, so it must tile visually without becoming noise.

**sod_shroud_nest** `[N]` 400cr, 260hp, fp2
The Sodality's defence, and now the decoy-army building. A **lean-to of mismatched
plate** with a weapon poking through a gap - deliberately looks improvised and
temporary against the Bastion's permanence.

**sod_watch_post** `[N]` 350cr, 260hp, fp1
The detector, and **unarmed by design** (GDD line 56: detectors are visible and
killable). A **thin tower with a crow's nest** - tall, spindly, obviously fragile.
No weapon anywhere on the silhouette; that absence is the read. Now also the radar
jamming building, so a scavenged aerial array on top is earned.

**sod_veil_projector** `[E]` 1500cr, 900hp, fp2
Cloak field generator. A **dish or emitter aimed outward and up**, on a squat base.
Its effect radius is invisible, so the building should imply reach - the emitter
oversized relative to the base. Now also the tunnel building, so a **visible shaft
or hatch** at ground level is earned.

**sod_seismic_charge** `[N]` 4000cr, 1200hp, fp2 *(superweapon)*
The Sodality's superweapon and the deliberate opposite of the orbital cannon:
where that aims *up*, this **drives down**. A drill rig or piston over a shaft.
Charge readable the same way, ferrite gold descending rather than rising.

---

## 6. Rules that apply to everything

1. **Silhouette gate before any detail work.** Black the model out and view it at
   40px. If it is not identifiable, the shape has failed and no texture will
   recover it. Doc 30 §2 gives the protocol.
2. **One team-colour band or slash per silhouette**, as doc 16 requires - large
   and contiguous, on a surface facing the camera. Not a decal.
3. **No normal maps on units.** Doc 30 §3: a normal map cannot change a
   silhouette. AO into vertex colours instead.
4. **Detail budget goes on the top and upper profile.**
5. **Units are not to scale with buildings.** Infantry are scaled *up* so classes
   stay distinguishable; buildings are compressed so they do not occlude the units
   in front of them. Visual scale is decoupled from the sim's footprint and
   collision radius, which must never change to suit a mesh.
6. **Budgets** (doc 30 §7): infantry and light vehicles 300-900 tris, heavy
   vehicles 900-2,500, buildings 1,500-4,000.

## 7. What is inherited and what is invented

**Inherited, not invented:** the palette, the team-colour law and the four
silhouettes doc 16 names explicitly (bulwark = slab, phantom = faceted wedge,
harvester = fat beetle with gold hopper, squads = dot-clusters). The faction shape
languages are doc 16's words extended with doc 30's research. The centralised and
decentralised power grids are doc 15's.

**Recorded from existing art:** the 20 `[E]` entries have shapes already fixed by
`art/sprites/*.svg` and, for 20 of them, a shipped `.glb`. Those descriptions
*document* what exists so this brief is a complete reference. **If a description
contradicts the existing sprite, the sprite wins** and this document should be
corrected.

**Invented here:** the 22 `[N]` entries, and the specific band placements
throughout. These are proposals from the doctrine, the stats and the role, not
design decisions on file. **A Game Designer or Luke may overrule any of them**,
and the shape language in §2 matters more than any individual description.

**Deliberately not specified:** exact proportions, greebling, insignia design, and
anything that would be easier to judge from a blockout than to argue in prose.
