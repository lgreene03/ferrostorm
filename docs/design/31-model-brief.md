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

Each entry gives a **physical description** - what the object actually is, for
someone modelling or drawing it - then *Reads as*, the 40-pixel silhouette and
team-mark placement. `[E]` = an established shape already exists (sprite, shipped
`.glb`, or both); the description records it. `[N]` = new, one of the 22 owed.

### Common hardware (field olive `#6e6a5e`, ferrite-gold marks)

Common kit is what both sides inherited rather than built: utilitarian, boxy,
slightly agricultural. Rounded-off corners from wear, no styling, visible bolts
and panel joins. It should look older than either faction's own hardware.

**com_rifle_squad** `[E]` 200cr, 100hp
Four or five infantry moving as a loose group. Bulky olive fatigues with a webbing
harness over the chest, a shallow domed helmet, and a stubby rifle carried across
the body. Deliberately anonymous - no faces, no insignia detail, silhouettes
slightly hunched forward as if advancing. The figures should be simple capsules
with squared shoulders rather than anatomically modelled people.
*Reads as:* a dot-cluster of vertical ticks. Team band across the shoulders.

**com_rocket_squad** `[E]` 300cr, 80hp
The same fatigues and helmets, but each figure carries a **long tube over one
shoulder**, angled up and back past the head. The tube is thick, cylindrical, with
a flared rear venturi and a bulbous warhead at the front. Slightly fewer figures
than a rifle squad, standing more upright to balance the weight.
*Reads as:* the same dot-cluster, but each tick now has a diagonal slash across
it. That diagonal is the whole distinction. Team band across the shoulders.

**com_engineer** `[E]` 500cr, 60hp
A **single** figure in olive coveralls rather than combat webbing, hunched forward
under the weight of a large rectangular toolcase carried in one hand. A
ferrite-gold hard hat is the one bright element on him. No weapon anywhere - empty
hands and a case, which is the point.
*Reads as:* one small tick with a gold dot on top, never a cluster. Team band on
the back of the coveralls.

**com_harvester** `[E]` 1400cr, 700hp
A wide, low, tracked machine with no turret and no weapon. The front third is a
downward-angled **intake maw** with visible cutting teeth, close to the ground.
Behind it sits a large open-topped **hopper** whose interior fills visibly with
ferrite gold as it works. The cab is a small armoured box offset to one side,
almost an afterthought. Heavy fenders over both tracks, caked and industrial.
*Reads as:* a fat beetle with a glowing gold back. Team band low on the flank.

**com_mcv** `[E]` 3000cr, 600hp
The largest vehicle in the game: a long slab-sided box on wide tracks, completely
unarmed. The body is visibly **segmented into folding sections** - deep seams,
heavy hinges, external locking clamps and folded-flat outrigger legs along the
lower edge. It should look like a building lying down. A small armoured cab at the
front, no windows to speak of.
*Reads as:* an oversized featureless brick on tracks. Team slash on the rear plate.

**com_repair_vehicle** `[N]` 700cr, 300hp
A small, boxy, unarmed tracked utility vehicle - roughly a third the size of the
MCV. The defining feature is a **folded articulated arm** lying along the roof,
ending in a claw or welding head, with hydraulic cylinders visible along its
length. Ferrite-gold hazard striping on the arm and around the rear deck. Toolboxes
and a spare drum strapped to the flanks.
*Reads as:* a small box with a bent stick on top. Team band on the cab side.
*Currently borrows the MCV mesh, which is several times too large.*

**com_carrier** `[N]` 600cr, 350hp
A long, low, unarmed transport with a **flat open deck** taking up two thirds of
its length and shallow drop-sides. The cab is a small forward box with a heavy
grille. The deck should read as deliberately empty when unloaded, with tie-down
rails and a fold-down rear ramp visible. Six wheels rather than tracks, giving it
a lighter, more civilian look than anything armed.
*Reads as:* a long flat plank with a lump at one end. Team band on the cab.

**com_flak_track** `[N]` 550cr, 260hp
A light half-tracked chassis with an **open-topped mount** at the rear carrying a
cluster of four short, fat barrels angled steeply upward. Ammunition boxes are
stacked around the mount, and an exposed gunner's seat and hand-cranked traverse
wheel sit beside it. The front is an ordinary truck cab. Everything about it says
converted rather than purpose-built.
*Reads as:* the only ground unit whose silhouette points at the sky. Team band on
the cab door.

**com_strike_flyer** `[N]` 1100cr, 180hp
A small single-seat aircraft with **forward-swept wings** and a slim tapering
fuselage. No bulk anywhere - a thin fuselage, a bubble canopy set well forward,
twin tail fins, and a single underslung rocket pod beneath each wing. It should
look fast and fragile.
*Reads as:* a thin dart with a shadow gap beneath it, which is what sells it as
airborne. Team mark on the tail fins, the highest visible surface.

### Directorate (gunmetal `#5b6770`, plate `#78848c`, signal orange `#e8762c`)

Directorate hardware is **issued**: every vehicle looks like it came off the same
production line. Flat welded plate, bilateral symmetry, level tops, hard right
angles, and no visible improvisation. Panel lines are regular and repeated.
Stencilled serial numbers in the same place on every hull. Clean, cold, and
slightly overbuilt.

**dir_cannon_tank** `[E]` 600cr, 300hp
The faction's baseline: a low, wide tracked tank with a **single sloped frontal
plate** and a squat hexagonal turret set centrally. One long smoothbore barrel with
a blunt muzzle brake. Track guards run the full length as unbroken plates, hiding
the running gear so the whole vehicle reads as one solid mass. No stowage, no
clutter, no curves anywhere.
*Reads as:* the reference slab with a barrel. Team band across the hull front.

**dir_bulwark_tank** `[E]` 1600cr, 550hp
The widest, lowest, heaviest thing on the field - "the wall that walks". A vast
flat-topped hull sitting on **two pairs of tracks per side**, with armour so thick
the track guards and hull merge into one continuous slab. The turret is barely a
turret: a low armoured hump with a short heavy barrel, set well back. Deep bolted
seams across the frontal plate. It should look immovable rather than fast.
*Reads as:* a slab, per doc 16 - the broadest silhouette in the game. Team band
low and wide across the front.

**dir_howitzer** `[E]` 900cr, 160hp
A small, thin-skinned tracked chassis almost entirely occupied by a **very long
barrel**, elevated well above the horizontal and overhanging the front by half the
vehicle's length again. A large recoil housing and recuperator sit above the
breech, and two hydraulic spades are folded at the rear. The crew compartment is
an open-backed box, obviously unarmoured.
*Reads as:* a small body under a long raised stick. Team band on the hull side,
below the barrel.

**dir_sentinel_scout** `[E]` 400cr, 90hp
A light four-wheeled scout car with a small faceted hull and no main gun. Its
defining feature is a **tall slender mast** rising from the rear deck, topped with
a flat dish or sensor paddle - taller than looks natural, because at distance the
mast is the entire identity. Small vision blocks, a light machine gun on a pintle.
*Reads as:* a small box with a thin vertical line above it. Team band on the
bonnet.

**dir_vanguard_car** `[E]` 450cr, 150hp
A fast **six-wheeled** armoured car with a low, open-topped welded body and a
sharply sloped nose. A small forward-firing autocannon sits in a shallow shielded
mount rather than a turret. Wheels are large and exposed with chunky treads - the
only Directorate vehicle where the running gear is visible, which is exactly what
separates it from the cannon tank at distance.
*Reads as:* a low wedge on visible round wheels. Team band on the nose slope.

**dir_commando** `[N]` 1500cr, 200hp, one alive
A single figure, noticeably **larger than any other infantry** - hero scale is
intended. Heavy segmented plate armour over the chest and shoulders, a full helmet
with a narrow visor slit, and a long rifle held across the body rather than
shouldered. Stands fully upright where line infantry hunch. Signal orange is
generous here: shoulder plates and helmet band both.
*Reads as:* one tall bright tick that stands out from every crowd.

### Sodality (rust `#8a4a34`, plate `#a35c40`, corroded teal `#4fb8a8`)

Sodality hardware is **welded from salvage**: mismatched plate of visibly different
ages, asymmetric mountings, tilted or broken rooflines, and things strapped on top
that break the silhouette. Rust streaks below every join. Where the Directorate has
stencils, the Sodality has hand-painted marks. Nothing looks like it came from the
same factory as the thing beside it.

**sod_shade_raider** `[E]` 500cr, 150hp
A fast, low, four-wheeled raider built on an obviously **civilian chassis** - a
pickup-like body with the roof cut away. A weapon is bolted to one side only:
asymmetry is the read. The other side carries strapped-on salvage - jerrycans, a
spare wheel, rolled tarpaulin. Improvised plate is welded over the cab in
mismatched sheets, leaving gaps.
*Reads as:* a lopsided low wedge, leaning forward. Team mark hand-painted on the
door panel.

**sod_phantom_tank** `[E]` 900cr, 200hp
The Sodality's one piece of precision engineering: an angular tracked tank built
entirely from **flat faceted planes**, no curves and no clutter anywhere. Narrow
sharply-pointed front widening to a broad rear. The weapon is a slim rocket box
rather than a barrel, low over the hull. Surfaces are matte and dark, with faint
teal seams that glow slightly when cloaked.
*Reads as:* a faceted wedge, per doc 16. Team mark as a thin slash along the hull
edge - minimal, since this thing is meant to disappear.

**sod_infiltrator** `[N]` 700cr, 90hp
A single figure in a long **civilian coat** over ordinary clothes, carrying no
visible weapon at all. Hands in pockets or at the sides. Head bare or lightly
hooded. The whole point is that it reads as a person rather than a soldier, which
is what makes it unnerving among welded machines.
*Reads as:* one plain tick with no hard edges and nothing carried.

**sod_saboteur** `[N]` 600cr, 80hp
Same civilian coat and posture as the infiltrator, distinguished by a **satchel
carried low at the hip**, bulky and square, with a strap across the chest. That
satchel is the only geometric addition. The two should be confusable at a glance
and separable on a second look - which is the doctrine, not an accident.
*Reads as:* the infiltrator's tick with a small box at waist height.

**sod_shadow_commando** `[N]` 1500cr, 200hp, one alive
The Sodality hero: a tall figure in **mismatched scavenged plate**, deliberately
asymmetric - a heavy pauldron on one shoulder only, a hood over a partial mask,
wrapped cloth at the forearms. Carries a long rifle. Where the Directorate commando
is issued and uniform, this one looks assembled from what was available.
*Reads as:* one tall lopsided tick with a hood. Corroded teal generous on the
pauldron and hood.

## 5. Buildings

Footprint `fp2` = 2x2, `fp1` = 1x1. Buildings are **compressed in height** relative
to reality so they do not occlude the units in front of them.

### Common infrastructure (field olive)

**com_construction_yard** `[E]` 3000cr, 3000hp, fp2
The anchor of the base: a wide, heavy industrial hall with a **gantry crane
spanning the roof**, its rails running the full width. The crane's trolley is
visible and ferrite-gold trimmed. Corrugated walls with a large roller shutter on
one face, external stairs and a railed walkway along one side. It should look like
the factory that builds factories.
*Reads as:* the largest footprint with a horizontal bar floating above it.

**com_refinery** `[E]` 2000cr, 2000hp, fp2
A processing plant built around a **docking bay with a wide open mouth** on one
side, deep enough that a harvester visibly enters it. Above sit two cylindrical
storage tanks and a tangle of pipework, with ferrite-gold running through the pipes
and pooling brightest at the outflow. A short chimney vents at the rear.
*Reads as:* a box with a dark opening and gold pipes above it - the brightest gold
on the map.

**com_factory** `[E]` 2000cr, 1500hp, fp2
A wide flat-roofed shed with **two large vehicle doors** on the front face and a
concrete apron in front of them. The roof carries ventilation louvres and nothing
else - deliberately plain, because it is a workshop. Steel-framed walls with
visible bracing, and a painted lane marking leading out from the doors.
*Reads as:* a plain flat box with two dark rectangles on one face.

**com_barracks** `[N]` 500cr, 800hp, fp2
Smaller and simpler than the factory, and must not be confused with it: a **pitched
roof** against the factory's flat one is the distinction. A single personnel-sized
door with a short covered porch, small square windows along one side, and a rack of
kit and a water butt against the wall.
*Reads as:* a small hut with a peaked roof.
*Currently borrows the service depot, which is both too large and flat-roofed.*

**com_power_plant** `[E]` 300cr, 150hp, fp2 *(Directorate-only)*
GDD s3's centralised grid, so it is a **big juicy target**: a squat turbine hall
with two broad **cooling stacks** rising from the roof, venting faint heat haze.
Thin walls, large louvred vents, exposed conduit running down the outside to
ground level. At 150hp it is the most fragile building in the game and should look
it - all plant and no armour.
*Reads as:* a low box with two thick chimneys, the tallest common silhouette.

**com_service_depot** `[E]` 1200cr, 1000hp, fp2
An **open** structure rather than a sealed box: a flat concrete apron under a
skeletal repair gantry, with a hoist on a rail and floodlights on posts at the
corners. Oil drums, a tool cart and a stack of track links sit around the edges.
The openness is what distinguishes it from the barracks and factory.
*Reads as:* a flat pad with an open frame over it - you can see through it.

**com_radar_uplink** `[N]` 900cr, 1000hp, fp2
The **tallest thin structure** in the game: a small windowless equipment blockhouse
with a lattice mast rising from it, carrying a large ferrite-gold dish angled
skyward. Guy wires run from the mast to ground anchors. Pure vertical emphasis,
which nothing else common has.
*Reads as:* a small base under a tall thin line with a dish on top.
*Currently borrows the Sodality veil projector, breaking the faction read.*

**com_airfield** `[N]` 1800cr, 1100hp, fp2
Deliberately **horizontal**: a large flat concrete pad with painted ferrite-gold
landing markings, a low fuel bowser at one edge, and a slim control mast with a
glazed cabin at one corner. Everything low so the aircraft standing on it reads as
the tall element.
*Reads as:* a flat marked rectangle with one thin post at a corner.

**com_outpost** `[N]` 500cr, 1000hp, fp2 *(neutral, capturable)*
Must read as **nobody's**: a weathered concrete blockhouse, bleached and stained,
with a flat roof, narrow slit windows and a bare flagpole with nothing on it. No
faction plate, no livery, no team colour at all - the flag and a painted band
appear only on capture.
*Reads as:* a pale, plain, colourless box with a bare pole.
*Currently borrows the refinery, which misleadingly implies an economy building.*

**com_bridge** `[N]` 400cr, 800hp, fp1 *(map-placed, destroyable)*
A flat roadway span with **visible truss girders beneath** and low kerb rails at
the sides. No superstructure above deck level, so it reads as terrain rather than
building. Concrete abutments at each end. The underside trusses matter: they are
what make felling it feel structural.
*Reads as:* a flat strip across a gap, dark underneath.

### Defence

**dir_turret** `[E]` 600cr, 400hp, fp2 *(common hardware)*
A small circular concrete base carrying a **rotating armoured turret** with a
single medium barrel. The base is almost incidental - low, plain, half-sunk. The
turret is the read: a faceted box that visibly tracks its target.
*Reads as:* a small dark disc with a gun on top that moves.

**com_emplacement** `[N]` 350cr, 300hp, fp2
Lower and cheaper than the turret: a **sandbagged ring** with a short-barrelled
automatic weapon on a pintle at its centre, and an ammunition box beside it. Mostly
horizontal, dug in rather than built up. Reads as a pit, not a tower.
*Reads as:* a low ring with a small stub in the middle.

**com_wall** `[N]` 100cr, 500hp, fp1
A plain reinforced concrete segment with a chamfered top edge and a visible
vertical joint at each end. **It must tile seamlessly** and read as a continuous
run from above - that is the only rule that matters. Light weathering at the base.
*Reads as:* an unbroken line when built in runs.

**com_gate** `[N]` 200cr, 500hp, fp1
Visibly a wall segment **with a moving part**: two heavy posts with a barred
sliding leaf between them, and a small mechanism housing on one post. Open and shut
must be distinguishable at 40px, so the leaf slides fully clear rather than
retracting inside the posts.
*Reads as:* a gap in the wall line that visibly opens and closes.

**com_mine** `[N]` 400cr, 100hp, fp1
Small, low, and almost flush with the ground - barely a silhouette by design. A
shallow disc with a single **ferrite-gold pressure plate** at its centre and a
scatter of disturbed earth around it. Visible to its owner, near-invisible to
everyone else.
*Reads as:* a faint gold dot on the ground, easily missed.

### Directorate structures

**dir_bastion** `[N]` 1400cr, 1600hp, fp2
The toughest building per credit and it must look it: a squat **armoured
blockhouse** with steeply sloped concrete flanks, a heavy weapon firing through a
narrow embrasure rather than sitting on a turret, and a stepped parapet along the
top. A small dish and antenna cluster on the roof, earned by its two support
powers. Reads as fortification, not as a gun on a pole.
*Reads as:* a wide sloped mass with a slot in the front and aerials above.
*Currently borrows the turret, which badly under-reads 1400 credits.*

**dir_superweapon** `[E]` 4000cr, 1200hp, fp2 *(orbital cannon)*
The most **vertical** Directorate structure: a massive segmented barrel angled
steeply skyward, mounted in a heavy ring cradle on a broad reinforced base. Cable
looms and coolant pipes run up the barrel's length. Ferrite gold intensifies up
the barrel as the charge fills, brightest at the muzzle just before firing.
*Reads as:* a huge diagonal line pointing up, with a glow that climbs it.

### Sodality structures

**sod_generator** `[N]` 130cr, 70hp, fp1
GDD s3's decentralised grid: the cheapest, flimsiest thing in the game. A single
**salvaged fuel drum** stood on end on a pallet, with a small motor bolted to the
top, a pull-cord, and cables trailing away across the ground. Rust streaks, dents,
faded painted markings from a previous life.
*Reads as:* a small rusty cylinder. Players build a dozen, so it must cluster
without becoming noise.

**sod_shroud_nest** `[N]` 400cr, 260hp, fp2
A **lean-to of mismatched plate** propped at an angle against a low frame, with a
weapon poking through a gap between sheets. Corrugated iron, salvaged vehicle
panels and mesh, all different ages. Sandbags at the base. Deliberately temporary
against the Bastion's permanence.
*Reads as:* a tilted asymmetric hump with something sticking out of it.

**sod_watch_post** `[N]` 350cr, 260hp, fp1
A **thin scaffold tower** with a small enclosed crow's nest at the top, reached by
an external ladder. Scavenged aerials and a small dish are lashed to the upper
frame. **Unarmed by design** (GDD line 56) - no weapon anywhere on the silhouette,
and that absence is the read. Spindly and obviously fragile.
*Reads as:* a tall thin lattice with a box on top and no gun.

**sod_veil_projector** `[E]` 1500cr, 900hp, fp2
A squat armoured base carrying an oversized **emitter dish** angled outward and
up, ringed by coil housings that pulse faint teal. The dish is deliberately large
relative to the base, because its effect radius is invisible and the building must
imply reach. A ground-level hatch and shaft head sit at one side, earned by tunnel
deployment.
*Reads as:* a low base under a big tilted dish, with a teal shimmer around it.

**sod_seismic_charge** `[N]` 4000cr, 1200hp, fp2 *(superweapon)*
The deliberate opposite of the orbital cannon: where that aims up, this **drives
down**. A heavy drill rig over a shaft head - a lattice derrick with a massive
piston hammer suspended in it, cables and counterweights on either side, and a
concrete collar around the borehole. Ferrite gold descends the shaft as it charges.
*Reads as:* a squat derrick over a dark hole, with a glow that sinks rather than
climbs.

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
