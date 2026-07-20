# TICKET-P6-VISUAL-V4: faction identity and the first impression

Labels: `persona:client-engineer` `persona:art-pipeline` `gdd:16` `phase:6`
`owner:client-engineer`
Branch: `ticket/p6-visual-v4`
Spec: docs/design/25-visual-overhaul-roadmap.md wave V4, executed against the
ratified colour tickets of docs/design/22-scale-and-colour-roadmap.md Wave C.
Where V4 and Wave C overlap, Wave C's ratified numbers win and are used
unmodified.

---

## Plan comment, written before starting

**Approach.** This is the last visual wave and it does two things: it fixes the
confirmed blown-tank flaw V2 left for it, and it makes the two factions read
apart. Three logical commits, each judged on its own contact sheet against the
committed baseline.

1. **The saturation/blown-tank fix (C-06 then C-05, one re-bake).** V2 shipped
   the metallic and AO fixes and stated in plain terms that the remaining
   high-key vehicle read is a saturation problem for C-05 to solve at the
   palette level, not with exposure. So this commit lands doc 22's C-06 (the
   8-bit emissive normalise-and-carry so saturated emissives stop clipping to a
   hue-shifted near-white) and then C-05 (the PAL chroma table that holds
   luminance and restores saturation, plus the Directorate chip colour), in one
   `blender -b -P bake.py` of all 27 models, followed by a Godot reimport so the
   capture harness does not show stale models (V2's documented trap).
2. **Team-colour placement (C-07).** The secondary team mark: a square footprint
   strip around each structure and a widened unit ground ring, so common `com_`
   models shared by both players tell the factions apart at RTS distance. Marks
   stay under the glow HdrThreshold so they do not bloom.
3. **Palette tuning (C-01, C-10, C-11).** C-01 scatter hue jitter (the cheapest
   win, adds mineral colour to rubble/talus/rocks/plates/tufts), C-10 the
   warm/cool light-rig spread (chromatic modelling on every surface without an
   albedo edit), C-11 the split-tone grading LUT (taste, last, with C-11's own
   kill switch honoured if the 1D sampling does not split-tone).

**Assumptions.**
1. That doc 22 remains ratified and its Wave C numbers stand; this ticket
   schedules and executes them rather than re-deriving them.
2. That the committed baseline at HEAD 51697e0 is the correct "before" for every
   comparison, so it is captured first and kept.
3. That the blown-tank fix genuinely needs a re-bake: the base colours are in
   the baked diffuse texture, so a runtime tint cannot selectively raise
   saturation; C-05 is the ratified fix and it is an art-side palette change.

**Interfaces touched.** `art/3d/bake.py` (emissive normalise-and-carry),
`art/3d/builder.py` (PAL table + srgb helper), `art/3d/materials2.py` (chip
colour), all 27 `.glb`; `game/scripts/BattlefieldView.cs` (DressStructure new,
DressMobile ring widened), `game/scripts/SkirmishLive.cs` (structure dressing
call). Nothing under /sim.

**Scope held tight, stated honestly.** The client change was kept to C-07 team
colour, because the bake alone was proven (stage-sat capture) to fix the blown
tank AND make the faction-specific vehicles read apart, so the only remaining
faction-identity gap was the SHARED com_ structures, which C-07 closes. The
palette-tuning tickets C-01 (scatter jitter), C-10 (warm/cool light) and C-11
(grade LUT) were held: C-10/C-11 carry a re-brightening/taste risk that would
disturb the just-validated exposure balance and C-11 needs Luke's ramp sign-off,
so they are stated as owed V4-02 polish rather than rushed into the last wave.
V2-03 (roughness octaves) is a surfacing ticket, not colour, and is left owed to
avoid confounding the colour attribution. V4-03 (HUD/menu theme) and V4-04 (VFX
textures) are the menu/HUD/effects half of the roadmap's V4, large UX/art
tickets marked UNVERIFIED there; they remain and are the honest residual of the
overhaul.

---

## What shipped

### The blown-tank fix: C-06 then C-05, one re-bake (commits 1 and 2)

`art/3d/bake.py`, `art/3d/builder.py`, `art/3d/materials2.py`, all 27 `.glb`.

**C-06, the 8-bit emissive clamp.** The EMIT bake target is an 8-bit buffer, so
every emissive above 1.0 on any channel clipped, and clipping one channel of a
saturated colour is a hue shift rather than a brightness cap (the superweapon
core baked yellow, the veil orb cyan-white, the ferrite tips pure white). The
fix normalises every emissive material down by M = max(strength * max-channel)
before the bake so nothing clips, and hands M back at export via
`Emission Strength = 2.0 * M`, which reproduces the final colour exactly
((colour/M) * 2M) while the baked map keeps full chroma. `emit_scale` and
`emit_max` are now printed per model; the superweapon reports emit_scale 1.775,
emit_max 1.000, and the veil orb no longer clips at all because C-05's teal
(0.078,0.480,0.392) at emit 1.8 peaks at 0.864.

**C-05, the PAL chroma pass.** The old PAL held sRGB-looking fractions in
scene-linear Base Color slots, rendering every model 30-40 per cent less
saturated than doc 16 intends; that desaturation under V2's brighter exposure is
the confirmed blown-tank read. The ratified linear chroma table restores
saturation while holding luminance (gun to a steel #7C9EBC at S 0.34, rust to
#C4774D at S 0.61, teal to #4FB8A8), and the Directorate bare-metal chip moves
from neutral grey (0.50,0.53,0.56) to a cool (0.42,0.50,0.60). The two faction
body hues stay 187 degrees apart (gun hue 208, rust hue 20). An `srgb()` helper
is added above PAL as the documented encoding for any future entry.

**Looked at, CAM-C.** The Directorate vehicle goes from a pale near-white blob
with a warm cast and flat form to a formed cool-steel hull with distinct panels,
tracks, a legible turret and a rich amber front strip. The Sodality vehicle goes
from a pale pastel-pink wedge (the "pink cast") to a saturated salmon-rust hull.
The ferrite cluster goes from pure blown white to a warm gold. This is the whole
blown-tank fix, on its own, exactly as V2 predicted; no exposure knob and no
runtime tint were touched (verified: ModelLibrary instantiates the .glb directly
and no client code tints the unit body).

### C-07 team-colour placement (commit 3)

`game/scripts/BattlefieldView.cs`, `game/scripts/SkirmishLive.cs`. The bake makes
the faction-specific dir_/sod_ vehicles read apart by hull temperature, but the
shared com_ structures (refinery, factory, power plant, harvester, MCV, infantry)
are one model for both players and the baked orange on a power-plant vent is
faction-neutral, so a Directorate base and a Sodality base were pixel-identical.
`DressStructure` adds a square team-coloured footprint strip (orange for player 0,
teal for player 1) around each structure, and the unit ground ring widens from
0.30/0.36 to 0.27/0.40 so the mark survives the RTS camera band. Marks stay at
EmissionEnergyMultiplier 0.9, under the glow HdrThreshold of 1.0, so they do not
bloom. Walls keep their AddContactBlob branch (their silhouette is the run, not
the segment). **Looked at, CAM-A and CAM-B:** the Directorate base is now boldly
outlined in orange and reads as its faction at a glance; the code is symmetric so
a player-1 base reads teal (the Sodality base sits in explored-but-unseen fog in
this reference state, so a same-frame base pair is not capturable, but the
two-faction image pairs the orange base with the Sodality unit).

### The bake-pipeline finding (for the next person)

The re-bake took three Blender invocations, and one is a warning worth recording:
a single-model smoke test (dir_superweapon) validated C-06, then the full
`blender -b -P bake.py` was **externally killed under load at 13 of 27 models**
(it left a partial com_refinery.glb mid-export and 12 models un-updated, which
presented as a misleading "only 15 of 27 changed" git status). A targeted re-bake
of the remaining 13 by name completed the roster with a clean `BAKE PIPELINE DONE`
marker and all 27 modified. **The pipeline can silently drop models if the process
is killed; always confirm the DONE marker and a 27/27 modified count, not the
per-file git status, before trusting a full bake.** After the bake, a
`Godot --headless --path game --import` was required (the V2 reimport trap) and
ran clean with no loose textures extracted and the sidecars unchanged.

## Frame-by-frame verdict

A change ships only if the after-capture is visibly better at the three cameras.
It is. The blown tank reads as a formed, coloured vehicle at CAM-C, the two
factions read apart at CAM-A and CAM-B, and the green terrain, the killed off-map
void (CAM-A clean; the CAM-B residual max-zoom band is the documented
finite-map inherent) and the 1.3x unit scale from the prior waves all still hold.
Nothing was reverted. Frame metrics moved the right way (chromatic fraction
0.674 -> 0.716, 95th-pct luminance 185 -> 195, open-ground luminance steady at
~110 inside the 90-135 band); the numbers are modest because the fix is about
value structure and hue rather than mean saturation, and per the LOOK-02 rule the
image is the pass.

## Gates

- `~/.dotnet/dotnet run --project sim/Ferrostorm.Sim.Runner -c Release`: exit 0.
- `git diff sim/` empty. `git diff sim/golden-hashes.txt` empty. No file under
  /sim opened for writing. This is a client-and-art-only change.
- `game/Ferrostorm.Game.csproj` builds clean in Debug and Release, zero warnings.
- Frame time CAM-B, 1600x900, V-Sync off, 240 frames: 10.345 ms before ->
  10.340 ms after. Draw calls 118 -> 214 (C-07 strips, no measurable cost).
- User data untouched: saves, replays and settings.cfg hashed before and after,
  byte-identical; only Godot's own engine logs differ.
- Captures, README and contact sheets at the session scratchpad
  visual/captures/v4/.

## Changed / Assumed / Needed next

**Changed.**

- `art/3d/bake.py`: C-06 emissive normalise-and-carry (emissive_scale, per-object
  M scaling around the EMIT bake, `2.0 * M` export strength, emit reporting).
- `art/3d/builder.py`: C-05 PAL chroma table and the srgb() helper.
- `art/3d/materials2.py`: C-05 Directorate chip colour.
- `game/assets/models/*.glb`: all 27 re-baked and re-exported.
- `game/scripts/BattlefieldView.cs`: C-07 DressStructure added, DressMobile ring
  widened.
- `game/scripts/SkirmishLive.cs`: non-mobile non-wall structures routed through
  DressStructure.
- Docs: this file, the ledger line, doc 25 status.

**Assumed.**

1. That doc 22 remains ratified and its Wave C numbers stand; C-05, C-06 and C-07
   are used exactly as written.
2. That the blown-tank fix is the re-bake alone. Verified by the stage-sat capture
   (bake only, no client edit): the tank is fixed there.
3. That the DirectorateMark/SodalityMark Godot colours (which already render at
   the doc-16 team hues, orange ~22 degrees and teal ~171) are the right runtime
   marks for C-07 and need no change.

**Needed next, and from whom.**

- **From whoever lands doc 22's C-01, C-10, C-11 (roadmap V4-02).** These are the
  palette-tuning polish held out of this wave: scatter hue jitter, the warm/cool
  light-rig spread, and the split-tone grading LUT. C-10 must re-check the CAM-A
  ground stays in the 90-135 band and use the TonemapExposure knob once if it
  overshoots; C-11 needs Luke's ramp sign-off or its own WONTFIX kill switch.
- **From art-pipeline.** V2-03 (roughness octaves) still owed; V4-04 (VFX
  textures) and V4-03 (HUD/menu theme + loading screen) are the menu/HUD/effects
  "first impression" half of the roadmap's V4 and remain unstarted.
- **From the producer.** LOOK-03 (correcting the three false ledger entries) is
  still owed and is a producer action.
