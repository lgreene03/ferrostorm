# TICKET-P6-VISUAL-TERRAIN: grass, trees, water, sand, rocks, proper texture

Labels: `persona:art-pipeline` `persona:client-engineer` `gdd:16` `phase:6`
`owner:client-engineer`
Branch: `ticket/p6-visual-terrain`
Spec: doc 25 (visual overhaul, its ABSOLUTE contract), and doc 22 Wave A.5 /
Wave C's ratified per-map biome-palette intent, which this wave SUBSUMES and
delivers the terrain half of. The owner asked for this directly, in these
words: "i want grass, trees, water, sand, rocks, proper texture".

Where this work and doc 22 overlap it follows doc 22's ratified intent: a base
biome per map plus within-map variation, deterministic placement seeded off the
map. Doc 22's Wave A.5 "biome ground" was ratified and never started; this wave
is the ground, vegetation and water half of it. It does NOT implement doc 22's
Wave C unit-chroma tickets (C-01, C-05 etc.), which are the roster's colour and
belong to the bake waves, not to terrain.

---

## Plan comment, written before starting

**Approach.** Build the harness-verified way, exactly as V0/V1/V2 did: capture
the baseline first, change the ground shader, look, then water, look, then the
vegetation and the new meshes, look, and only ship each once the CAM-C capture
is genuinely better. Tune at CAM-C (grass, a tree, the shore and a rock in one
frame) before confirming it holds at CAM-A and CAM-B. Everything is presentation
and every placement is seeded off the map, byte-identical every run, because the
look-dev harness demands byte-identical captures and the golden hashes are
SIM-only and must not move.

**Assumptions.**

1. That skirmish-03 is the temperate showcase, because it is the only committed
   map with water and it is the scene the harness and the owner judge. Its base
   biome is temperate (green), driven off a single map property (does the map
   contain water) so no new map metadata and no sim change is needed: a map with
   water is temperate, a map without is arid. skirmish-01 and skirmish-02, which
   doc 25 records as bare open ground with no water, therefore stay arid, which
   is correct.
2. That procedural meshes built in C#, in `BattlefieldView`, are the right idiom
   for the trees and rock formations rather than the Blender pipeline. Every
   existing terrain form in this project (hills, ruins, fences, rock scatter,
   cliff strata, bridges) is already a procedural Godot primitive built in this
   file; the Blender pipeline is for the 27 UNIT models. Building trees and rock
   formations the same way keeps them inside the deterministic seeded-scatter
   architecture, needs no re-bake and no Godot reimport, and touches none of the
   unit `.glb` files. doc 25's own "deferred" list (section 6) blesses this:
   "Rewriting the terrain from scaled BoxMesh primitives to authored rock and
   cliff meshes. DEFERRED. The batching architecture already supports it." This
   wave stays inside the batching architecture.
3. That wind sway is driven by a `_Process`-advanced uniform (as `TickWater`
   already drives the water scroll), not by shader `TIME`. Both are legal and
   presentation-only, but a `_Process` uniform freezes when the harness pauses
   the tree, which is what makes two captures byte-identical; `TIME` keeps
   advancing while paused and would depend on the exact rendered-frame count.
4. That decorative vegetation and rock formations are presentation only and the
   simulation's passability is untouched. See the hard line below.

**Interfaces touched.** `BattlefieldView.BuildTerrain`, `BuildScatter`,
`BuildEnvironment` (untouched here), `TickWater` (renamed intent: now advances a
wind/wave phase and pushes it to the ground, grass and water shaders). New
shaders `game/shaders/ground_biome.gdshader` (replaces `ground_splat` as the
ground material), `game/shaders/water.gdshader`, `game/shaders/grass.gdshader`.
Nothing under `/sim`, nothing that reads or writes simulation state, no change to
`blockedCells` or the `visual` legend, no change to the 15 Hz snapshot contract.

---

## The hard line on passability, and how zero change is guaranteed

Vegetation and rock formations are PRESENTATION ONLY. The simulation decides
passability from the map (`MapLoader`, `/sim`), and this wave does not read,
write or influence it. Concretely:

- Every new visual (biome ground, grass, trees, rock formations, water) is built
  inside `BattlefieldView.BuildTerrain` / `BuildScatter`, which are handed
  `blockedCells` and the `visual` legend by value and only ever READ them. No
  cell is added to or removed from the blocked set. There is no code path from
  this wave to `World`, `MapData` or any sim type.
- Trees and rock formations are clustered on cells that are ALREADY blocked or
  are non-play edges: water-adjacent banks, map edges, hill ('h') and ruin ('r')
  surrounds, and the ridge/blocked ('#') feet. They are never gated on the sim,
  so a decorative tree may sometimes sit on a passable open cell and a unit will
  visually walk through it; doc 25 and the ticket brief accept that for this
  pass. Preferring blocked and edge cells makes the clash rare.
- The guarantee is mechanical, not by inspection: `git diff sim/` is empty and
  `git diff sim/golden-hashes.txt` is empty, and the full determinism battery
  exits 0. Those three facts are the proof.

---

## Design brief

### The biome model

A base biome per map, plus within-map variation driven by height, macro noise
and proximity to water. It is expressed once, deterministically, as a **biome
control map**: an RGBA image generated on the CPU in `BuildTerrain` at one texel
per map cell from the map's own data (blocked set, water cells, undulation
height, and fixed-seed macro noise). Its channels are the blend weights the
ground shader and the vegetation placement both read, so the ground, the grass,
the trees and the rocks all agree on where each biome is:

- **R = grass weight.** Low, flat, open, temperate ground.
- **G = sand weight.** Beaches in a band around water, and dry arid patches.
- **B = rock weight.** High ground, macro rocky outcrops, and the feet of hills,
  ruins and blocked ridges.
- **A = wetness.** A shoreline band (strong in the cell touching water, fading
  over three cells) that darkens the ground to wet sand and lowers its roughness.

Base biome is chosen from the map with no new metadata: a map that contains any
water cell is TEMPERATE (grass-dominant open ground, sand only at the beaches and
dry patches, rock on the high ground); a map with no water is ARID (sand-and-rock
dominant, grass only in sheltered low pockets). skirmish-03 has a river, so it is
temperate. The control map is generated byte-identically from the map every run.

### How each of the five is achieved

1. **GRASS.** Two things. The ground shader paints grassy ground a muted,
   variegated green (multi-octave noise on albedo AND roughness, a large-scale
   hue drift between yellow-green and blue-green so it is never flat), and a new
   `Grass` MultiMesh instances real crossed-blade clumps on top of it. Clumps are
   placed only where the biome grass weight is high, thinned by a density noise,
   and stopped at sand, rock and water. Per-instance colour variation gives three
   grass species tints. A vertex-shader wind sway (`grass.gdshader`, offset by a
   `_Process`-advanced wind phase, weighted by blade height so the tips move and
   the roots do not) makes it live. Grass is range-culled so it exists only when
   the camera is close enough to resolve it, which keeps it off the CAM-B pixel
   budget where it would be sub-pixel shimmer.

2. **TREES.** A procedural low-poly tree mesh built in C# (a tapered bark trunk
   plus an irregular three-lobe green canopy, two surfaces on one `ArrayMesh`),
   instanced as one `Tree` MultiMesh. Trees are clustered into copses seeded on
   the land cells that border water and on the map edges, not sprinkled
   uniformly, so they read as woodland along the river and the frame. Per-instance
   scale, yaw and canopy tint give variety from one mesh. The canopy sways in the
   same wind. Justified by capture at RTS distance rather than modelled in
   isolation.

3. **WATER.** The dark slab becomes a real water surface: `water.gdshader`, a
   depth-aware transparent surface that reads the scene depth behind it, so it is
   a light shallow teal where it meets the shore and a deep blue-green in the
   channel; a Fresnel brightening at grazing angles; two scrolling ripple normals
   for a moving surface; and a scrolling foam line where the water is shallow
   against the land. The basin geometry (the per-cell sunken slabs) is unchanged;
   only the material is replaced.

4. **SAND.** A distinct warm sand biome in the ground shader: fine-grain warm
   albedo (its own noise scale, finer than rock), high roughness, a gentle detail
   normal. It dominates the beach band around water and the dry arid patches, and
   it is clearly separate from the cool grey-brown rock and the green grass. A wet
   darkening band (the wetness channel) makes the sand read damp right at the
   waterline.

5. **ROCKS.** Actual rock formations: a new `RockFormation` MultiMesh of larger
   irregular multi-lobe boulders (0.5 to 1.4 units) with real silhouette, placed
   on the rock-biome cells and the feet of hills, ruins and ridges. Their material
   is noise-driven grey-brown albedo AND roughness with a detail normal and, per
   the V2 lesson, metallic 0 (no constant metallic, no chalky specular). They are
   distinct from the existing sub-pixel debris scatter, which stays as pebbles.

6. **PROPER TEXTURE.** Every new surface carries real spatial variation: the
   ground biome shader runs macro plus detail noise on both albedo and roughness
   with a detail normal, the water has moving normals and depth variation, the
   grass and rock and tree materials all carry noise albedo and roughness. No new
   surface is a flat colour.

### Determinism and passability guarantees (restated for the record)

- All placement is seeded (`System.Random` with fixed seeds, per-cell hashes,
  fixed-seed `FastNoiseLite`) and reads only the map. Byte-identical every run.
- Wind and wave motion is a `_Process`-advanced uniform that freezes on pause, so
  the harness captures are byte-identical between runs (verified with
  `tools/lookdev/verify-determinism.sh`).
- No sim file is touched; `git diff sim/` and `git diff sim/golden-hashes.txt`
  are empty; the battery exits 0.

---

## What shipped

Five logical commits: the biome ground shader, the water, the grass, the trees
and rock formations, then the docs. Each was captured at CAM-C and judged as an
image before it landed, the discipline V0/V1/V2 established.

### 1. The biome ground (commit "Drive the ground from a biome control map")

`BuildBiomeMap` generates an RGBA control map from the map alone: R grass, G
sand, B rock, A wetness, one texel per cell, lightly blurred. Base biome is
temperate if the map has water, arid if not. Sand forms beaches in a band around
water and dry patches by macro noise; rock forms on the feet of hills, ruins and
ridges and by macro outcrop noise; grass fills the rest of a temperate map.
`ground_biome.gdshader` blends the three with multi-octave world-space noise on
albedo AND roughness, a distance-faded detail normal, and a large-scale grass
hue drift, all sampled in world space so the dust-capped mesa slabs pick up the
same biome as the ground. `BiomeAt` exposes the field to the vegetation and rock
placement. The old single-arid `ground_splat.gdshader` is removed.

Looked at, CAM-C and CAM-A: the flat tan frame became green grassy flats with
warm sand beaches at the shoreline and real spatial variation, where before it
was one arid material carrying only noise.

### 2. The water (commit "Make the river read as water")

`water.gdshader` replaces the dark opaque slab: depth colour read from the scene
depth behind the surface (shallow teal at the bank, deep blue-green in the
channel), a fresnel edge, two scrolling ripple normals, and a scrolling shore
foam line. Transparent queue, which SSR being off (V1-06) now allows for free.
The per-cell slabs became one seamless `BuildWaterSurface` mesh because the old
1.02-overlap slabs double-blended into a grid of dark seams under a transparent
material; the sunken basin the shader reads its depth from is untouched.

Looked at, CAM-C: the river went from a near-black hole to a blue-green water
surface with a foam shoreline, depth gradient and sun sparkle.

### 3. The grass (commit "Instance real grass on the grassy biome, with wind")

A `Grass` MultiMesh of three-way crossed blade cards, alpha-cut from a
procedurally drawn blade texture, instanced only where the biome grass weight is
high, thinned by a density noise, stopped at sand, rock, water and blocked cells.
Three species tints jittered per instance. `grass.gdshader` sways the blades in
the wind, weighted by blade height, driven by the `_Process`-advanced
`wind_phase`. Range-culled (`VisibilityRangeEnd` 40) so it is full at play height
and gone by max zoom where a blade is sub-pixel. The dead-straw tufts are gated
off grassy ground so they now dress the dry, sandy and rocky biome.

Looked at, CAM-A and CAM-C: a green grassy meadow reads across the grassy biome,
thinning at the sandy banks and stopping at the water.

### 4. Trees and rock formations (commit "Add procedural tree copses and rock formations")

`BuildTrees`: a procedural low-poly tree (bark trunk plus a three-lobe canopy,
two surfaces on one mesh), MultiMesh-instanced and CLUSTERED into copses seeded
on the land cells that border water and on the map edges, so it reads as woodland
along the river and the frame. Per-instance scale, yaw and canopy tint;
`foliage.gdshader` gives the canopy noise-varied albedo and roughness and a
gentle wind sway. `BuildRockFormations`: larger irregular boulders (a low-poly
blob warped by deterministic per-vertex noise for a real lumpy silhouette) on the
rocky biome, cliff feet and a few at the water banks, with a noise-driven
grey-brown albedo AND roughness, a detail normal, and metallic 0 (the V2 lesson,
no chalky specular). Distinct from the sub-pixel debris scatter.

**Shadows OFF on both, and this was measured, not assumed.** With the canopies
casting into the 4096 shadow atlas, CAM-B (whole map, every copse in the cascade
at once) collapsed to about 1.5 fps: the exact "cost more than they show" case the
brief names. At RTS distance the tree's own form and the SSAO contact darkening
carry it. The first rock capture baked the boulders too dark; the albedo was
lightened to a grey-brown that catches the warm key.

## Frame time

Measured at CAM-B, 1600x900, V-Sync off, 240 timed frames, the harness's own
number.

| | frame time | draw calls |
|---|---|---|
| baseline (branch point) | 10.017 ms | 117 |
| biome ground + water + grass | 10.55 ms | 116 |
| + trees + rock formations (final) | _see note_ | |

Note on the final measurement: the machine was under heavy remote-desktop
(Jump Desktop) load for the back half of this wave, which saturates the
WindowServer and starves the windowed offscreen render, so the heaviest render
(CAM-B, full map) could not be timed cleanly at the end. The grass field is the
only heavy addition and it is range-culled off CAM-B entirely; the trees (a few
hundred opaque instances) and rock formations (a few hundred, shadows off) add a
small opaque draw cost. The final CAM-B figure is recorded in the ledger once a
clean measurement is taken.

## Gates

- `~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c Release`: exit 0.
  Full battery green (selftest, determinism, walls, LAN 5/5 no desync, defence
  load 1.548 ms/tick).
- `git diff sim/` empty. `git diff sim/golden-hashes.txt` empty. No file under
  /sim was opened for writing.
- `game/Ferrostorm.Game.csproj` builds clean in Debug and Release, 0 warnings, 0
  errors.
- User data untouched: saves, replays and settings.cfg hashed before and after.

## Changed / Assumed / Needed next

**Changed.**

- New: `game/shaders/ground_biome.gdshader`, `game/shaders/water.gdshader`,
  `game/shaders/grass.gdshader`, `game/shaders/foliage.gdshader`.
- Removed: `game/shaders/ground_splat.gdshader` (replaced by the biome shader).
- `game/scripts/BattlefieldView.cs`: the biome control map and `BiomeAt`, the
  biome ground wiring, the seamless water surface and its shader, the grass, tree
  and rock-formation builders and their mesh helpers, and `TickWater` now
  advancing a wind/wave phase into the animated shaders.
- `game/project.godot`: a stale comment renamed to `ground_biome.gdshader`.
- Docs: this file, doc 25's status block and wave table, the ledger line.

**Assumed.**

1. That a map with water is temperate and a map without is arid, so no new map
   metadata and no sim change is needed. skirmish-03 (river) is the temperate
   showcase; the dry maps stay arid.
2. That procedural C# meshes are the right idiom for trees and rock formations,
   as they are for every other terrain form in this file, rather than the Blender
   pipeline (which is for the 27 unit models). This touches no `.glb` and needs
   no re-bake or reimport.
3. That wind and wave motion driven by a `_Process` uniform (frozen on the
   harness pause) is preferable to shader `TIME` for harness determinism, both
   being legal presentation-only motion.
4. That decorative vegetation and rock formations are presentation only and may
   sometimes overlap an open play cell a unit walks through, which the brief
   accepts; they are clustered onto banks, edges and blocked-cell feet to make
   that rare, and never gate the sim.

**Needed next, and from whom.**

- **From Luke.** Look at the three named CAM captures and say whether the terrain
  sings, because the loop only works if somebody with taste looks at the image.
- **From the client-engineer.** A clean CAM-B frame-time measurement with trees
  and rocks in frame, once the machine is not under remote-desktop GPU load, to
  confirm the final figure against the 16 ms budget; record it in the ledger.
- **From whoever lands doc 22's Wave C.** The unit-chroma tickets (C-01, C-05,
  C-10, C-11) are still owed; this wave deliberately did the terrain only.
