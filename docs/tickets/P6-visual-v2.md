# TICKET-P6-VISUAL-V2: the one bake session, and the shroud the last wave uncovered

Labels: `persona:art-pipeline` `persona:client-engineer` `gdd:16` `phase:6`
`owner:art-pipeline`
Branch: `ticket/p6-visual-v2`
Spec: docs/design/25-visual-overhaul-roadmap.md wave V2, plus the shroud defect
filed as the first "needed next" in docs/tickets/P6-visual-v0-v1.md. Where this
work and doc 22 overlap, doc 22's ratified numbers win and are used unmodified.

---

## Plan comment, written before starting

**Approach.** Land the shroud first and alone, because it is the ugliest thing
in the frame, it is cheap, and it is the one change in this wave that can be
judged without a bake in front of it. Then the bake session as one edit to the
pipeline followed by one full re-bake, kept in its own commit so twenty-seven
large binaries are revertable in a single move. Then the exposure, last,
against captures, because the two bake fixes both brighten the frame and the
right exposure is not knowable until they have landed.

**Assumptions.** That the shroud plane is presentation only and that
`SkirmishLive.SyncActors` is the thing that actually hides enemy units, which
is verified in place rather than assumed. That doc 22 remains ratified. That
the V1-final tree at `main` is the correct "before" for every comparison in
this wave, so it is captured first and kept.

**Interfaces touched.** `FogOfWar.Init` and `FogOfWar.UpdateFrom`, presentation
only, no new read of simulation state. `BattlefieldView.BuildEnvironment`, the
SSAO pair and the tonemap exposure. `art/3d/builder.py`'s `mat()`,
`art/3d/materials2.py`'s `wmat()` metallic chain, `art/3d/bake.py`'s diffuse
composition and value-pass path. All twenty-seven `.glb` files. Nothing under
/sim.

---

## What shipped

### Part 1: the shroud (commit "Soften the shroud boundary")

`game/scripts/FogOfWar.cs`. The shroud plane is one texel per map cell, and
with the fog gone its bilinear upsample was interpolating a hard zero-to-0.30
alpha step across about thirty screen pixels, which is a visible staircase with
diamond facets. The filter was not the cause and neither was the resolution;
the field was. Per-cell opacity is now dilated over a 3x3 neighbourhood,
blurred by two passes of a separable three-tap box, and clamped back up to the
truth, after which the existing bilinear upsample has a smooth field to
interpolate.

It reveals nothing. The dilate is an erosion of the visible set, and the final
clamp makes it unconditional that every texel ends at least as opaque as the
sim says, so the ramp lives entirely on the visible side of the boundary: it
feathers inward, dimming the outer ring of ground the player may see, and never
lifts shroud off ground the player may not. Independently, the plane hides
nothing in the first place: enemy units are hidden by SkirmishLive.SyncActors
reading World.IsVisible directly, and the minimap dots apply the same gate. The
truth image is still built at one texel per cell, unsoftened, and is still what
the minimap reads; the plane samples a separate softened copy. The guarantee is
stated in a comment at the method.

**Looked at, all three cameras.** At CAM-A and CAM-C the hard diagonal polygons
that cut across the battlefield are gone and the visible-to-explored boundary
now reads as an edge of vision, a soft falloff rather than a row of squares. At
CAM-B, where the shroud region is a large part of the frame, the same. This was
the single ugliest element in the frame after V1 and it is simply gone. Nothing
regressed. It is also free: frame time went 9.863 to 9.816 ms because the
rebuild moved off a per-cell SetPixel loop.

### Part 2: the bake session (commits "Fix the bake..." and "Re-bake...")

Three defects fixed in `art/3d/builder.py`, `materials2.py`, `bake.py`, the
SSAO pair in `BattlefieldView.cs`, and all 27 `.glb` re-baked.

**V2-01, the metallic constant.** builder.mat floored every part at
max(metal, 0.2) and bake.py read the Principled Metallic default_value, so the
ORM blue channel of all 27 models was byte 51 at the 5th, 50th and 95th
percentiles alike, 99.96 per cent of texels in the invalid 0.10-to-0.85 band.
The floor is gone, mat and wmat default to 0.0, the value pass bakes the node
output, and Metallic is driven from the chip mask through a constant-
interpolation ramp so it is binary. After: p5 and p50 are 0.0, p95 is 1.0 on
most materials, mid-band mean 7.3 per cent.

**The fifth roadmap error, found here and corrected in doc 25 in place.** The
chip mask V2-01 clause 3 told me to reuse did not work. It windowed
Geometry.Pointiness at 0.535-0.595 on the theory that pointiness clusters at
0.5. Baked across the roster, the flat-face value is 0.5725 on most models,
0.60-0.61 on infantry and wheels, 0.30-0.33 on the Sodality vehicles: a flat
face at 0.5725 sits 62 per cent through the old window, so the bare-metal chip
colour was smeared across whole flat panels of most of the roster, and the
Sodality vehicles escaped only because their pointiness fell below the window.
That is a second, previously unfiled cause of the pale chalky read, and it is
why the Sodality tanks measured as the darkest models in the game. The mask is
replaced by the angle between the bevel-shaded normal and the true geometric
normal, which is exactly 1.0 on any flat face whatever the topology; its median
is 0.00-0.04 on every object. This correction is recorded in doc 25's
correction list as number 5.

**V2-02, the triple AO.** bake.py multiplied AO into the diffuse at k=0.85, the
same AO shipped in the ORM red channel, and SSAO ran on top. The multiply is
deleted outright. use_pass_direct/use_pass_indirect left False and untouched as
the roadmap requires. SsaoIntensity 2.5 to 1.6, SsaoPower 1.8 to 1.4. After:
diffuse-to-AO correlation 0.924 to -0.133 (criterion below 0.55, met), mean
diffuse linear luminance 0.132 to 0.260 (criterion +25 per cent, met at +97).

**One fix the bake needed that neither ticket named:** the Cycles DIFFUSE pass
returns the diffuse lobe, which is black at metallic 1.0, so the chipped-metal
base colour was baking black. Metallic is muted to 0 for the DIFFUSE bake only.

**Metrics summary, before to after, over all 38 materials:**

| | shipped | re-baked | criterion | met |
|---|---|---|---|---|
| metallic p5 | 0.200 | 0.000 | below 0.05 | yes |
| metallic p95 (median) | 0.200 | 1.000 | above 0.90 | yes on most |
| metallic mid-band % (mean) | 99.96 | 7.27 | below 8 | yes on mean |
| diffuse-vs-AO correlation | 0.924 | -0.133 | below 0.55 | yes |
| diffuse linear luminance | 0.132 | 0.260 | +25% | +97% |

The two soft misses, reported rather than hidden. p95 is below 0.90 on nine of
the 38 materials, but those are the low-edge parts (factory doors, the scout
dish, the vanguard wheels, ferrite crystal) where little metal should show; a
flat door with no bevelled edges correctly bakes to no metal. Mid-band exceeds
8 per cent on five structure materials (superweapon 17.9, a factory sub-
material 17.2, turret 15.4) because a 2048px structure has a long mask boundary
and the transition texels live there; the mean is 7.3.

**Looked at, CAM-C first.** The Sodality vehicle on the bridge goes from a dark
grey-olive chalky lump with no faction identity to a rust hull that reads as
its faction colour, with panels and gun barrels legible. The Directorate
vehicle goes from a flat chalky box to top-lit painted steel with distinct
turret faces, both track assemblies catching light along their top edges, and
the amber emissive at the front. The unit chromatic fraction at CAM-C nearly
tripled, 0.14 to 0.42, and unit saturation rose 0.108 to 0.156. The ruins at
CAM-A and CAM-C read as warmer, lighter tan blocks rather than untextured grey.
**Honest verdict: this is better, clearly, and it is better for the reason the
tickets predicted.** The one caveat, stated plainly: at the exposure the ground
band forces (below), the Sodality rust reads a touch high-key. That is a
saturation problem, not a brightness one, and it is exactly what doc 22's C-05
chroma pass exists to fix; it is out of scope here and must not be faked with
exposure.

### Part 3: the exposure retune (commit "Bring the exposure back down...")

`BattlefieldView.cs`, 2.9 to **2.85**, and the reason it comes down only that
far is the finding. V1 raised exposure to replace fog brightness; V2's bake was
expected to brighten the frame back and let this fall further. Measured, it did
not brighten the frame: the AO and specular fixes brighten the baked UNITS, not
the ground, because the ground is a runtime shader with no baked map, so
whole-frame CAM-A luminance actually fell (59.7 to 57.1). Exposure tracks the
ground, which is the knob's ratified constraint, and the new bake gives CAM-A
open-ground luminance 92.0 at 2.9, 90.0 at 2.8, 85.8 at 2.6, so below about 2.8
it breaks doc 22 C-10's 90-to-135 floor. Exposure is also the wrong tool for
the hot units regardless: AgX compresses them in its shoulder, so 2.9 to 2.6
moves them only 163 to 155. 2.85 lands CAM-A ground at 91.0, safely in band,
and trims the frame a touch. Recorded value: **TonemapExposure = 2.85f**, CAM-A
ground 91.0.

## Gates

- `~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c Release`: exit 0,
  run before and after.
- `git diff sim/` empty. `git diff sim/golden-hashes.txt` empty. No file under
  /sim opened for writing.
- `game/Ferrostorm.Game.csproj` builds clean in Debug and Release, zero
  warnings.
- Godot re-imported all 27 re-baked .glb cleanly, no loose textures extracted,
  import sidecars unchanged (still pin embedded uncompressed images). NOTE, and
  it is a real trap: the capture harness does NOT trigger a reimport, so a
  capture taken straight after a re-bake shows the OLD imported models. The
  first bake capture in this wave was invalid for exactly this reason; the fix
  is one `Godot --headless --path game --import` before capturing.
- Frame time CAM-B: 9.863 ms before, 9.766 ms after, 117 draw calls either way.
- User data untouched: saves, replays and settings.cfg hashed before and after,
  identical; only Godot's own engine logs differ.

## Frame-by-frame verdict

A change ships only if the after-capture is visibly better at the three
cameras. It is, at all three, and nothing in the wave was reverted. The shroud
softening removed the ugliest element in the frame; the bake gave the units
material and faction colour and gave the ground and ruins warmth; the exposure
sits at the lowest value the ratified band allows. The honest caveats are the
Sodality rust reading slightly high-key (C-05's job) and a handful of structure
materials retaining a wider metallic transition band than the 8 per cent target
(cosmetically invisible at RTS distance). Neither is a regression.

## Changed / Assumed / Needed next

**Changed.**

- `game/scripts/FogOfWar.cs`: shroud softened (dilate, blur, clamp-to-truth),
  presentation only.
- `art/3d/builder.py`, `art/3d/materials2.py`, `art/3d/bake.py`: metallic floor
  removed, metallic driven binary from a bevel-curvature chip mask baked as the
  node output, AO multiply into diffuse deleted, DIFFUSE bake mutes metallic.
- `game/scripts/BattlefieldView.cs`: SsaoIntensity 2.5 to 1.6, SsaoPower 1.8 to
  1.4, TonemapExposure 2.9 to 2.85.
- `game/assets/models/*.glb`: all 27 re-baked and re-exported.
- Docs: doc 25 correction 5 added and status updated, this file, the ledger
  line.
- Also committed: the missing `game/scripts/LookDev.cs.uid` that the V0/V1 wave
  omitted, so the tree is consistent with every other committed script.

**Assumed.**

1. That the bevel-curvature chip mask is an acceptable replacement for the
   pointiness window V2-01 named, since the named mask was measured not to work.
   It is a mask-construction change inside the same weathering material, not a
   new material archetype, so it stays inside V2-01's scope.
2. That 2.85 is the right exposure, chosen because the ratified ground band
   forbids lower and AgX makes exposure the wrong tool for the units. If C-10 or
   C-05 later change ground or unit luminance, this is the knob to revisit.
3. That taming the high-key Sodality rust is C-05's chroma work and must not be
   substituted with exposure or an albedo edit here.

**Needed next, and from whom.**

- **From whoever lands doc 22's C-05.** The re-baked albedo is now the honest
  albedo with no AO burned in and no false specular stealing a fifth of it, so
  C-05's chroma table now lands on a clean base. The Sodality rust reads a
  touch high-key at the band-forced exposure; C-05 raises saturation while
  holding luminance, which is the correct fix.
- **From the tools owner.** The capture harness should force a reimport (or
  document that a re-bake must be followed by `Godot --import`), because a
  capture taken against a stale import silently shows the old assets, which is
  exactly the "shipped unseen" failure the loop exists to end. This wave hit it
  once and caught it by checking the imported .scn timestamps.
- **From design-review, owner of doc 25.** V2-01 clause 3's chip-mask premise
  is corrected in place as correction 5. Confirm the correction stands.
- **From the client-engineer.** The off-map void at the top of frame is still
  V3-01's, untouched here.

