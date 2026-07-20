# TICKET-P6-VISUAL-V3: camera, scale and the aliasing

Labels: `persona:client-engineer` `gdd:11` `phase:6` `owner:client-engineer`
`needs:game-designer`
Branch: `ticket/p6-visual-v3`
Spec: doc 25 (visual overhaul roadmap), Wave V3, its four tickets V3-01 to
V3-04, plus section 6's deferred unit-oversizing item, which this wave takes now
that its precondition (the FOV change) has landed.

This wave fixes the two flaws the owner verified by eye and the aliasing the
roadmap scoped to it: the off-map void at the top of every frame, units that
read as small pale specks, and the sub-pixel shimmer of ground detail and
scatter. It is client-only. No file under `/sim` is touched, no golden hash
moves, and every visual is presentation the sim never reads.

---

## Plan comment, written before starting

**Approach.** Work the harness-verified way V0/V1/V2/V-TERRAIN established:
capture the three frozen reference frames on the untouched branch first, then
land one change at a time, look at the capture, and keep the change only if the
after-frame is a better image. Three logical commits in the order the roadmap
sets: the FOV that removes the void, then the unit up-scale, then the aliasing.
The FOV is the money change and the one the owner named, so it lands first and
CAM-A is captured before and after specifically to show the void gone.

**Assumptions.** That doc 25's Wave V3 binds and its ratified FOV value of 50
stands; that unit oversizing may be taken now because section 6 deferred it
behind the FOV change and a measurement rather than refusing it; that the
capture harness reuses the shipped `RtsCamera`, so a FOV set in `_Ready` flows
into every capture with no harness edit; and that anything the harness pins (the
viewport MSAA) needs a matching harness edit to be seen in a capture.

**Interfaces touched.** `game/scripts/RtsCamera.cs` (the lens and the depth of
field), `game/scripts/SkirmishLive.cs` (the mobile-actor scale and one stale
frustum comment), `game/scripts/BattlefieldView.cs` (the scatter visibility
range and the minimum rubble scale). No sim, no assets, no shaders: V3-03's
shader change was already delivered by the terrain wave (see below).

---

## What shipped, ticket by ticket

### V3-01, the FOV and the void (commit 1)

`Camera3D.Fov` was never set, so the camera ran at Godot's 75-degree default.
At the fixed -50 pitch that put the far edge of the frame roughly 99 metres away
at the play height of 22 and 189 at the max zoom of 42, beyond the depth of
every shipped map, so the top fifth to third of every frame was empty off-map
void. This was measured and named the biggest compositional flaw in V0/V1 and
re-handed to V3 by both V1 and V2.

Set `Fov = 50f` in `_Ready`, the roadmap's ratified value. Judged by eye at all
three cameras:

- **CAM-A (play height): the void is gone.** Where the before-frame had a dead
  black band across the top quarter and the map floated on it, the after-frame
  is filled top to bottom with grass, water, the base, ruins, fences and the far
  water body. This is the single most important image in the wave.
- **CAM-B (max zoom): the void is much reduced but not entirely gone.** The map
  fills roughly 70 per cent of the frame against about 45 before, and the void
  band is roughly halved. A residual band remains at the very top, and this is
  honest and inherent: at height 42 the frustum still overshoots this finite
  96x64 map's far edge even at 50 degrees, and killing it completely would need a
  much narrower lens that reads telephoto-flat at CAM-A and shows too little
  battlefield. 50 is the balance the roadmap chose and it holds.
- **CAM-C (close): the void band at the top is essentially gone** and the frame
  is battlefield throughout.

Measured: pixels per world unit at CAM-A rose from roughly 18 to roughly 27,
exactly the roadmap's figure, which is the readability half of this wave on its
own. Whole-frame at CAM-A: 5th-percentile luminance 9.8 to 29.0 (the black void
floor lifted), mean luminance 87.8 to 103.0, chromatic fraction 0.54 to 0.67
(the dark void replaced by colourful battlefield). This is a gameplay-visible
change: it shows less ground at a given height, so it needs the Game Designer at
a capture before it is considered final on the ferrite-spacing maps.

Two reconciliations the ticket names, both checked and both clear:

- **MAP-05 has not landed.** `BuildLightRig`'s `DirectionalShadowMaxDistance` is
  a plain constant 90 metres, not a height-driven `Mathf.Max`, so there is
  nothing to re-derive. The narrower frame only lets 90 metres cover more of it.
- **The minimap frustum tracks the lens automatically.** `SkirmishLive.GroundPoint`
  projects the viewport corners through the camera's own `ProjectRayNormal`, so
  the frustum trapezoid follows the new FOV with no edit; a narrower lens only
  steepens the corner rays, so they all still hit the ground. The stale "~37
  degree half-FOV" comment is corrected to 25. **`Minimap.cs` itself is not
  touched** (a separate session holds it for the shroud-boundary fix).

### V3-02, the depth of field (commit 1, with V3-01)

The far tilt-shift DOF is removed rather than kept. At 75 FOV its blur fell on
the off-map void and cost a little for nothing; with V3-01 that region is now
real battlefield, and softening the far units and structures the player must
read is the same mistake V3-02 forbids in the near field. This is the ticket's
"`DofBlurFarEnabled = false` and reclaim the cost" branch. The dead
`DofBlurFarDistance = 55f` initialiser (clause 1, overwritten every frame by the
old `_Process`) and the per-frame distance update that drove it both go with it.
Confirmed on the CAM-A after-capture: the far battlefield is crisp.

### Unit scale (commit 2)

Section 6 deferred oversizing units behind the FOV change and a measurement. The
FOV alone took a vehicle from about 15 pixels to about 27 at play height, but a
tank still read as a small pale shape, so this takes the deferred up-scale at a
modest **1.3** for mobile units only. Judged at CAM-C and CAM-A: a cannon tank
now reads as a chunky piece with a legible turret and barrel, with no clipping
into structures or other units and no visible wreckage.

**How sim scale is kept untouched, exactly.** The change is a single Godot
`Node3D.Scale` on the presentation actor in `SyncActors`. Nothing it scales is
sim state:

- `PickEntity` selects in world and sim space with hard-coded radii of 0.8 to
  1.4 world units; those are unchanged, so a unit clicks exactly where it always
  did. 1.3 is well below the 1.6-for-vehicles and 2.2-for-infantry the audit
  proposed against the old wide FOV, which were dropped in part because 2.2x
  infantry draws three times the click target; at 1.3 on top of the FOV the drawn
  silhouette stays close to the pick radius.
- `World.FootprintOf`, collision and passability are sim state and untouched, so
  a unit still occupies exactly its sim cell.
- The `.glb` meshes are untouched, so there is no re-bake and therefore no
  reimport step (the trap V2 hit).
- The scale is set once at creation and persists, because the per-frame actor
  update only ever writes `node.Position` and `node.Rotation`, never `Basis` or
  `Transform`, so interpolation, hull yaw, hull pitch, the idle bob and the death
  tumble all leave it intact.
- It is applied to **mobiles only**. Structures rise-tween to `Vector3.One`
  (which the scale would fight) and walls sit edge to edge (which would
  interpenetrate above 1.0), exactly section 6's warnings; ferrite drives its own
  scale from remaining yield. Health bars and rank pips are scene children, not
  actor children, so they keep their tuned world sizes.

### V3-03, the ground detail normal (delivered early by V-TERRAIN)

**No code change was needed: the terrain wave already implemented this.**
`game/shaders/ground_biome.gdshader` (which replaced the `ground_splat.gdshader`
the ticket names) carries the exact distance fade V3-03 specifies at lines 105 to
107: `float ndfade = 1.0 - smoothstep(30.0, 90.0, length(VERTEX));` multiplied
into `NORMAL_MAP_DEPTH`, with the detail normal sampled at a low `wpos.xz * 0.06`
frequency, and a comment citing "V3-03's lesson". V3-03's clause 2 (the separate
gravel-grain normal) is moot because the biome shader has no separate gravel
normal; the single detail-normal sample it does have is already low-frequency and
faded. Verified present; the ticket's `ground_splat.gdshader` file reference is
now stale and is noted in doc 25's status.

### V3-04, range-cull the scatter (commit 3)

The 2200 rubble flecks and 900 dead-straw tufts are half a pixel to two pixels
each at max zoom, where they only shimmer as the camera pans. Range-culled rather
than cut, because MAP-03 scaled their count up on purpose. Godot measures the
visibility range from the camera to the node's AABB centre (verified against the
4.7 source: `renderer_scene_cull.cpp` assigns the instance position to
`transformed_aabb.get_center()`), which for a map-spanning MultiMesh is the map
centre. The three frozen cameras sit roughly 25 metres from that centre at CAM-A
and CAM-C and 47 at CAM-B, so `VisibilityRangeEnd = 38` with a 6-metre `Self`
fade shows the field at play height and close in and hides it at max zoom. This
is the pattern the live grass already ships (`BuildGrass` uses End 40). The FOV
change does not move the cameras, so it does not disturb these distances.

Judged: at CAM-C the scatter is intact (the guard against having simply deleted
content), at CAM-B it is gone. The minimum rubble scale also rises from 0.05 to
0.12 so no instance is a sub-pixel fleck even inside the visible range.

### Anti-aliasing: MSAA considered, left at 4x

The task asked to consider the MSAA level and measure its cost. The roadmap's
"already good" section states 4x MSAA plus FXAA resolves edges and that edge
aliasing "is not the complaint", and my 4x captures show clean tank and ruin
edges even with the larger units. An 8x probe (temporarily pinning the harness
viewport, then reverted) measured **10.34 ms at CAM-B against 4x's 10.37**, that
is effectively free, but that number is misleading for the decision: this M4's
tile renderer resolves MSAA almost for nothing, whereas the TDD's min-spec is a
GTX 1060-class immediate-mode GPU where 8x is real cost the harness cannot
measure, and the 8x capture showed no visible edge improvement over 4x. So MSAA
stays at 4x, FXAA stays on for specular sparkle, and TAA stays refused on the
15 Hz-interpolation-and-tween contract grounds V1 established.

---

## Frame time, goldens, builds, determinism

- **Frame time at CAM-B (1600x900, V-Sync off, 240 frames):** 10.800 ms / 119
  draw calls before, **10.370 ms / 118 draw calls** after. Net negative as the
  roadmap predicted: the narrower FOV culls more geometry, V3-04 removes
  thousands of scatter instances at max zoom, and the DOF is gone. This is the
  clean CAM-B figure the terrain wave left owed under remote-desktop load; the
  machine was free enough this run (about 8 seconds per capture).
- **Capture determinism:** `tools/lookdev/verify-determinism.sh` PASS, all three
  cameras byte-identical across two runs. The 1.3 scale and the visibility range
  are constants, so no new nondeterminism was introduced.
- **Sim battery:** `~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c
  Release` exit 0, same LAN hashes as baseline. `git diff sim/` empty,
  `git diff sim/golden-hashes.txt` empty.
- **Builds:** game/ Debug and Release, 0 warnings, 0 errors, after every commit.
- **User data:** `saves/`, `replays/` and `settings.cfg` byte-identical before
  and after (backed up to the scratchpad and diffed; the harness writes only PNGs
  to the output directory).

---

## Changed / Assumed / Needed next

**Changed.**

- `game/scripts/RtsCamera.cs`: `Fov = 50f` set in `_Ready` with its derivation;
  the far DOF disabled and its dead initialiser and per-frame distance update
  removed.
- `game/scripts/SkirmishLive.cs`: the `UnitVisualScale` constant (1.3) and its
  application to mobile actors in `SyncActors`; the stale "~37 degree half-FOV"
  frustum comment corrected to 25.
- `game/scripts/BattlefieldView.cs`: `VisibilityRangeEnd`/`EndMargin`/`FadeMode`
  on the Rubble and Tufts MultiMeshes; the minimum rubble scale 0.05 to 0.12.
- Docs: this file, doc 25's status block (marking V3 shipped and V3-03 delivered
  early by V-TERRAIN), the ledger line.
- No change: `game/shaders/` (V3-03 already in `ground_biome.gdshader`),
  `game/project.godot` (MSAA left at 4x), `game/scripts/LookDev.cs` (the 8x probe
  was reverted; no diff), `game/scripts/Minimap.cs` and `game/scripts/FogOfWar.cs`
  (held by a separate session), and everything under `/sim` and `/data`.

**Assumed.**

1. That doc 25's ratified FOV of 50 is correct and that the residual CAM-B top
   void band at max zoom is acceptable, being inherent to framing a finite map at
   maximum height without a telephoto lens.
2. That a modest 1.3 mobile up-scale is the right amount on top of the FOV, and
   that scaling the whole presentation actor (so team and selection rings grow
   with the unit) is coherent at this factor while the fixed sim pick radius keeps
   clicks landing.
3. That leaving MSAA at 4x is correct, because the roadmap ratified it, the edges
   read clean, and the 8x cost is invisible on this machine but real on the
   min-spec target.

**Needed next, and from whom.**

- **From Luke and the Game Designer, together.** V3-01 is a gameplay-visible
  change to how much battlefield is seen at a given height, on maps whose ferrite
  spacing doc 22 tuned by measurement. Judge the three CAM captures and confirm
  ferrite fields on skirmish-01 through skirmish-04 stay findable without
  excessive scrolling. The single most important image is CAM-A before/after,
  showing the void gone.
- **From Luke.** Confirm the 1.3 unit scale reads right by eye, and whether
  infantry specifically want a touch more (they are the smallest class; a
  per-class factor is a one-line change if wanted).
- **From whoever revisits the camera.** If the residual CAM-B void band ever
  grates, the lever is a max-zoom-only FOV taper or a small `MaxHeight` cut, not a
  blanket narrower lens; both are out of this wave's scope and need the Game
  Designer.
