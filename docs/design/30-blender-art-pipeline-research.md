# 30. Designing with Blender: research against this project's actual pipeline

Date: 2026-08-03. Four parallel research threads (Blender→Godot glTF, low-poly RTS
readability, procedural bpy versus geometry nodes, Godot rendering at RTS scale),
then every checkable claim tested against this repository rather than accepted.

**Read section 2 first.** The single most valuable finding is not about Blender.

---

## 1. Ground truth, measured before any research landed

| | measured |
|---|---|
| Pipeline | ~2,100 lines of procedural `bpy` (`art/3d/`), colour-managed palette, Cycles bake, glTF export |
| Models shipped | 27 `.glb` in `game/assets/models/` |
| Catalogue entries | 42 (20 units, 22 buildings) |
| **Bespoke models owed** | **22**, currently borrowing documented interim meshes via `ModelLibrary.cs` |
| Client rendering | Already `MultiMesh`, Godot **4.7**, Forward+ |
| Team colour | **A ground ring only. It never reaches the model.** |
| Mesh sharing | `com_` (shared) meshes outnumber faction-specific ones roughly 3:2 |
| Bake device | **Not pinned.** `bake.py` sets samples, never `--cycles-device` |
| Art pipeline in CI | **No.** The only ungated part of this project |

## 2. The finding that matters most: two factions, one silhouette

At the RTS camera a unit is roughly 40 to 60 pixels tall. This project's team
colour is a ring on the ground beneath the unit, and the majority of meshes are
shared between the Directorate and the Sodality.

**So at gameplay distance, most units of both sides are the same object with a
different circle under them.**

Every practitioner source treats this as the readability problem, not a polish
item. Tempest Rising's concept artist describes giving each faction "its own
shape language so players could identify units at a glance, even from a high
camera angle"; Iron Harvest's team holds the *class* silhouette constant across
factions and varies the faction language, so a player learns "that shape is
anti-tank" once and "that visual language is the enemy" once, rather than an N×2
matrix. Halo Wars' art blog is blunter about the failure mode: get the lighting
and contrast wrong and small units become "unreadable black blobs", and "player
color and unit recognition go out the door".

This is also the cheapest thing to fix, because it is mostly a *shader and
palette* change plus targeted geometry, not 22 new models.

### What the sources say to do

- **Team colour must be large contiguous areas on the model** - a whole panel, a
  roof plane, a flag - not a decal. At 40-60px a small marking is invisible.
- **Add one emissive team-colour element per unit.** StarCraft II's art tools do
  exactly this ("Team Color Add" in the emissive slot, alpha controlling how much
  shows). A self-lit element survives shadow, night and low contrast, which is
  precisely the black-blob failure above.
- **Use premultiplied addition, never a lerp.** This is the one correctness bug
  worth naming. If you store white in the tinted region and lerp, bilinear
  filtering blends between two gradients and produces bright fringes at every
  boundary. Store *black* (albedo already multiplied by the inverse mask) and add:
  `albedo = col.rgb + mask.r * team_colour.rgb`. At our pixel size the mask
  boundary is one or two pixels wide, so this artefact is proportionally far worse
  for us than for a third-person game. (Ben Golus, *The Team Color Problem*.)
- **Faction identity must not rest on hue alone.** Tempest Rising hit exactly this
  when red units became unreadable on a red-tinted field.

### Doctrine beyond colour, which we are unusually well placed to use

GDD s3 already gives the two sides opposed doctrines - a conventional superpower
and an insurgent network. C&C: Generals is the canonical reference for the second
half: the GLA "piece units and equipment together in outdated designs", and it was
the first game in the series where upgrades **showed up visibly on the models**.

The levers that survive at 40-60px:

| Directorate (conventional) | Sodality (improvised) |
|---|---|
| Bilateral symmetry, level tops, horizontal lines | Asymmetry as a rule, broken or tilted rooflines |
| Repeated modular forms - a manufactured kit | Mismatched parts, visibly civilian chassis |
| Uniform livery, consistent insignia placement | No livery; colour carried by painted patches and flags |
| Standardised proportions per class | Silhouette-breaking additions on top: cargo, tarpaulins, aerials |
| Drilled, even animation cadence | Looser, jittery, uneven cadence |

**The differentiating shape budget goes on the TOP and upper profile**, because at
a high angled camera that is what you see - roofline, turret, mast, aerial - not
the flanks.

## 3. Stop baking normal maps for units

`bake.py` currently bakes diffuse (AO multiplied in), **tangent normals from a
bevel pass**, and packed ORM. The normal bake is poor value at this camera and the
argument is decisive:

**A normal map cannot change a silhouette.** Polycount's wiki states it plainly:
"the silhouette of the game asset is determined by the low-poly geometry alone."
Since silhouette *is* the readability mechanism at 40-60px, the map buys nothing
on the axis that matters. Worse, at ~128 texels across a 60px unit you sample low
mips where tangent perturbations average towards flat, and whatever survives
aliases into shimmer under a moving camera.

**Keep the AO. Change how it is stored.** AO is low-frequency, so resolution
barely matters. Beyond All Reason - a shipping RTS - bakes vertex AO straight into
vertex colours with clamp/bias/gain controls, plus a separate ground "AO plate".
That costs no texture memory, no sampler, survives any mip level, and gives the
ground-contact darkening that stops units looking like they float.

**What to spend the saved pipeline time on instead:** a controlled key light with
a floor on ambient so nothing crushes to black, a rim light to separate units from
terrain, and a deliberately desaturated lower-contrast terrain so units read as
the bright objects. Halo Wars' account is that sun direction, inclination, ambient
level and shadow darkness were the decisive variables - not surface detail. Note
their related finding that terrain occupies up to 70-100% of an RTS screen, which
makes the ground's value and saturation a *unit-art* decision.

Keep normal maps, if anywhere, for **buildings only**, which are larger on screen
and static.

## 4. Determinism: the art pipeline is the one ungated part of this project

CLAUDE.md says determinism is the project's law. The sim has 18 gates and
cross-platform golden hashes. The art pipeline has neither, and it has a known
non-determinism:

**Cycles produces different images on CPU versus GPU with identical settings**
(Blender issues #101561, #89351), partly because the light tree is disabled on
some GPUs; GPU baking specifically has a history of scan-line artefacts (#T72237,
#T59286). `bake.py` does not pin a device, so a bake on a GPU machine and one on a
CPU machine can differ and nothing would detect it.

Recommended, in order of value:

1. **Pin `--cycles-device CPU`** in `bake.py`. Hosted runners have no GPU anyway,
   and CPU is the only device that gives a stable hash. Real cost: CPU baking is
   slower, and a local GPU bake will no longer match.
2. **Add `*.blend` to `.gitignore`.** Zero are tracked today, which is the strong
   position - the .blend format is explicitly documented as not byte-stable across
   platforms or releases, so treat them as disposable intermediates. One saved file
   committed by accident undoes this silently.
3. **Hash the outputs, not the inputs.** Blender is the compiler and it is not
   hermetic - the glTF exporter even writes its own version into `asset.generator`,
   so an exporter bump changes the bytes. A checked-in manifest of `.glb` hashes is
   the only thing that detects drift.
4. **A `.glb` assertion step.** A `.glb` is JSON plus a binary chunk, so it can be
   parsed in Python and asserted on: axis, extents, `COLOR_0` presence, and
   `baseColorFactor` against the palette. This is the art-side equivalent of the
   golden-hash gate, and it is cheap.

## 5. What this project already gets right

Worth recording, because generic Blender advice would tell you to change things
that are correct here.

- **The sRGB→linear conversion is already right.** `builder.py`'s `srgb()`
  implements exactly the EOTF the research prescribes, and doc 22 C-05 documents
  precisely the bug the sources warn about - sRGB fractions written into linear
  Base Color sockets, rendering everything desaturated. That was diagnosed and
  fixed before this research existed.
- **Script generation over .blend files is the strong choice**, not a compromise.
  It gives line-level diffs, review, blame, bisect and a rebuildable tag. Every
  .blend workflow gives up merging entirely and resorts to file locking. It is also
  the reason an AI assistant can work on the art at all.
- **Material names are deterministic** (`f"{name}_{emit}"`, never `Material.001`),
  which is what keeps Godot's external-material link from breaking on reimport.
- **The client is already on `MultiMesh`, Godot 4.7, Forward+**, so it is past the
  4.4 physics-interpolation/MultiMesh bug (#108058) and on the architecture that
  scales.
- **`-vcol` is not used anywhere**, which avoids a real trap: that suffix sets
  `FLAG_SRGB_VERTEX_COLOR`, and glTF `COLOR_0` is *linear*, so it double-decodes.

## 6. Godot rendering: what to do when team colour moves onto the model

The client already uses MultiMesh. The relevant question is how per-unit colour is
delivered, and there is a clear answer.

**Use `INSTANCE_CUSTOM`, not per-unit materials.** One mesh, one material, one
shader, one draw call. Pack the whole visual state into the four floats already
being uploaded:

```
.r = team index      .g = damage / build state
.b = animation phase .a = spare (selection, stealth alpha)
```

Then index a small `uniform vec4 team_colours[8]` in the shader and mask the tint
so only faction panels recolour. Separate materials per faction also work at two
factions but fragment batching and foreclose more colours later.

Two API traps worth knowing:
- **`use_colors` / `use_custom_data` must be set while `instance_count` is 0.** Set
  them afterwards and they are silently ignored.
- **Set `custom_aabb`** covering the playable area if transforms are rewritten each
  frame, or every write triggers a costly AABB recalculation.

**Shadows are the real cost, not triangles.** With the default 4 PSSM splits an
object visible in all of them is rendered *five times*. An RTS camera has a
bounded depth range, so `SHADOW_ORTHOGONAL` or 2 splits plus a tightened
`directional_shadow_max_distance` is the single biggest available win.

**Automatic mesh LOD is near-worthless here** and that is fine - it is
error-bounded edge collapse, and a 300-1,500 triangle unit has little to collapse
before the silhouette breaks. Leave it on, budget nothing for it. Note all
instances in one MultiMesh share one LOD level chosen from the nearest instance,
so it would do nothing across a map-spanning MultiMesh anyway.

## 7. Polygon budgets, for the 22 owed models

Historical ranges, with the honest caveat that most are community-sourced rather
than studio documents:

| | budget |
|---|---|
| Warcraft III massed units | 400-700 tris (heroes 900-1600) |
| StarCraft II typical | 1,000-2,000 |
| 0 A.D. base human | 500-800 before kit; buildings 1,000-3,000 |

**Suggested for Ferrostorm:** infantry and light vehicles 300-900, heavy vehicles
900-2,500, buildings 1,500-4,000 with the top reserved for two or three landmark
structures per faction.

The load-bearing point is that a Unity RTS test found the bottleneck at ~125
animated units was **animation and draw-call submission, not triangles** - 186k
triangles is trivial for any modern GPU. Budget effort at instancing and skinning
cost, not at shaving polygons. Since our sim is separate and units are visual-only,
we are already in the good case.

**Texture sizing:** at 40-60px a unit is served by 64-128 texels across its height.
Allowing one zoom step, **128×128 to 256×256 per unit is the honest ceiling** -
which argues for one shared atlas per faction rather than per-unit sheets.

## 8. Procedural bpy versus geometry nodes

**Keep the bpy scripts.** Geometry nodes is genuinely better at instancing and
scatter, and two classic objections died recently (custom split normals arrived in
4.5; 5.x made large graphs tractable). But for this job:

- `bmesh.ops` needs no UI context, which is exactly what you want in `--background`.
  `bpy.ops` operators raise on context and need `temp_override()`.
- Palette lookup, UV atlas placement, naming conventions, LOD budgets and
  validation asserts are trivial in Python and awkward-to-impossible as a graph.
- **The graph lives inside a binary .blend.** There is no first-class text form,
  which forfeits the version-control position that is this pipeline's main asset.
- glTF export of geometry-nodes instances is **still labelled experimental** in the
  Blender 5.2 manual, with hard constraints and no material variation.

If a sub-problem ever wants nodes (greebling, wear variation), author the node
group *in Python* so it stays in git.

**Headless CI is well-trodden**: `blender -b -P script.py --factory-startup`, with
`--factory-startup` essential so user preferences cannot leak in. The `gpu` module
is unavailable for drawing in background mode, so anything touching viewport GPU
rendering will not survive.

## 9. Recommended order of work

1. **Move team colour onto the models** (section 2). Biggest readability gain per
   hour, mostly shader and palette, and it makes the 22 interim stand-ins less
   damaging in the meantime because at least the *sides* will read apart.
2. **Pin the bake device and add `*.blend` to `.gitignore`** (section 4). Minutes
   of work; closes a real determinism hole in a project whose law is determinism.
3. **Drop the normal bake for units, move AO to vertex colours** (section 3). Makes
   every subsequent model cheaper to produce.
4. **Add the `.glb` assertion step** (section 4). The art-side golden gate.
5. **Then work the 22 owed models**, silhouette-first, with the doctrine table in
   section 2 as the brief and section 7 as the budget.

## 10. Uncertainty, flagged honestly

- Polygon budgets for Warcraft III and StarCraft II are community reverse
  engineering, not studio documents. The 0 A.D. figures are from an open-source
  project's own forum and its authors admit some models break them.
- No first-party benchmark exists for MultiMesh versus MeshInstance3D at RTS unit
  counts in Godot 4. The widely repeated "10,000 instances = 1 draw call" figures
  are arithmetic illustrations, not measurements. **If a number matters for a gate,
  measure it in this project.**
- The researcher could not read `docs.blender.org` or `polycount.com` directly
  (403/Cloudflare); some quotations come from search-index excerpts.
- No source states an RTS pixel-height threshold directly. The 40-60px figure and
  the texel arithmetic in section 7 are derived from cited texel-density rules, not
  quoted.
- Whether Godot's glTF importer supports Draco is unconfirmed. Do not enable it
  without testing.

**Primary sources**: Godot 4.5/4.6 documentation (importing 3D scenes, node type
customization, MultiMesh, mesh LOD, lights and shadows, optimizing 3D performance);
Khronos glTF 2.0 specification and `glTF-Blender-IO`; Blender 5.2 manual and
release notes; Ben Golus *The Team Color Problem*; Tempest Rising concept-art
interview (vsquad.art); KING Art Iron Harvest devblog; Ensemble Studios Halo Wars
dev blog; Beyond All Reason `OBJ2S3O`; Adrian Courrèges' Supreme Commander graphics
study; Polycount wiki and threads; 0 A.D. art design documents.
