# 25. Visual Overhaul Roadmap: why it does not look as good, and the order to fix it

Status: IN EXECUTION. **Wave V0 SHIPPED and Wave V1 SHIPPED**, branch
`ticket/p6-visual-v0-v1`, delivery notes docs/tickets/P6-visual-v0-v1.md.
Waves V2, V3 and V4 remain as written below. LOOK-03 is NOT done: correcting
the three false ledger entries is a producer action and is still owed.
Date: 2026-07-19
Owner: design-review, for execution by client-engineer + art-pipeline + tools agents

---

## Execution record, added 2026-07-19 after V0 and V1 shipped

**The capture-verification rule is now in force, and it is enforceable.** A
visual ticket is not closeable without a before-and-after contact sheet at the
three reference cameras, and a change that is invisible at all three is a failed
ticket regardless of its numeric criterion. The harness that makes that
checkable is `tools/lookdev/capture.sh` and `tools/lookdev/contact.py`, both
committed. There is no longer any excuse for a wave shipping unseen.

**Five corrections to this document, all measured rather than argued.** They are
recorded here in place because a later reader will otherwise implement the
version that was wrong. Corrections 1 to 4 were found by wave V0/V1, correction
5 by wave V2.

1. **LOOK-01 clause 4's headless invocation does not work.** Godot's
   `--headless` selects the dummy rasterizer and renders nothing, so a headless
   capture writes a blank PNG. The harness runs windowed with `--audio-driver
   Dummy`, which is the flag that actually matters offscreen, plus
   `--fixed-fps 60`.
2. **LOOK-01 clause 2's thirty-frame wait is not enough.** The volumetric fog's
   temporal reprojection has not converged at thirty frames and the capture came
   back byte-different between runs about half the time. 240 frames converges.
   Separately, the fog's history is a property of every frame the PROCESS has
   drawn, so the harness runs one process per camera; shooting three cameras in
   one process and then returning to the first moved 16,592 pixels by up to
   64/255 with the scene frozen.
3. **V1-01's acceptance criterion "mean HSV saturation at CAM-A rises by at
   least 0.03 absolute" points the wrong way.** The day-one fog experiment
   measured mean HSV saturation COLLAPSING from 0.488 to 0.064 when the fog was
   removed, because the frame's apparent saturation was the fog's uniform blue
   cast rather than any chroma in the art. The metric that tracks what a viewer
   sees is hue span, which went from 18.5 degrees to 222.7. Restate the
   criterion against hue span.
4. **V1-03 clause 4 is moot and V1-07's candidate knob does not exist.** Under
   `AmbientSource.Sky` with `AmbientLightSkyContribution` at 1.0, which clause 3
   mandates, `AmbientLightEnergy` is inert: raising it from 0.4 to 1.0 moved
   mean open-ground luminance at CAM-A by 0.03 out of 255. The ambient level is
   set by `SkyTopColor`, `SkyHorizonColor` and `SkyEnergyMultiplier` and by
   nothing else. V1-03's own acceptance measurement is also unmeasurable as
   written: at a fixed -50 pitch no downward-facing hull face is visible at all.

5. **V2-01 clause 3 rests on a chip mask that does not work, and the broken
   mask is itself one of the largest visual defects in the project.** The
   clause says to drive the Metallic socket from "the existing pointiness chip
   mask", which assumes that mask isolates convex edges. It does not.
   `materials2.wmat` windowed `Geometry.Pointiness` between 0.535 and 0.595 on
   the stated theory that pointiness "clusters tightly around 0.5". Baked and
   measured across all 27 models, the flat-face value is 0.5725 on most of the
   common and Directorate roster, 0.6039 to 0.6118 on the infantry, the scout
   dish and the vanguard wheels, and 0.30 to 0.33 on the Sodality vehicles.
   Pointiness is normalised over the mesh, so its flat value is a property of
   the topology and no fixed window can serve a range that wide. A flat face at
   0.5725 sits 62 per cent of the way through the old window, so the mask read
   about 0.6 across entire flat panels. Share of texels inside or above the
   window: 97 per cent on com_factory, 99.9 on com_wall_post and the vanguard
   car body, 100 on the vanguard wheels and the scout dish. **The bare-metal
   chip colour (0.50, 0.53, 0.56) was therefore mixed over most of the surface
   of most of the roster rather than over its edges**, which is a large part of
   the pale chalky low-contrast read, and it is why the Sodality vehicles,
   whose pointiness happens to fall BELOW the window, measured as the darkest
   and least chalky models in the game. Section 1 attributed the whole chalky
   read to the metallic constant; the constant is real and is fixed, but it had
   company. V2 replaces the quantity rather than the window: the mask is now
   the angle between the bevel-shaded normal and the true geometric normal,
   which is exactly 1.0 on any flat face whatever the topology, and which
   measured a median of 0.00 to 0.043 on every object in the roster.

**What the day-one experiments proved.** At CAM-A, setting `VolumetricFogDensity`
to zero dropped mean frame luminance by 37.5/255, meaning the fog was ADDING
that much uniform light to every pixel, and opened the frame's hue span from
18.5 degrees to 222.7. Hiding the shroud plane was worth 7.5/255 and changed the
hue span not at all. Section 1's proportioning was right about the order and
understated the gap between the two.

**Two defects the fog cut UNCOVERED**, neither of them caused by it, both owed
onward and neither in scope for V1. The shroud image is one texel per map cell,
so with the fog no longer smoothing it the visible-to-explored boundary is now
plainly blocky at CAM-A and CAM-C. And the top eighteen to twenty-eight per cent
of every frame is off-map void: at a 75-degree FOV and a -50 pitch the far edge
of the frame is 99 metres away at CAM-A and 189 at CAM-B, and no shipped map is
that deep. The second is V3-01's business and strengthens its case.

**The project's first frame-time numbers**, at CAM-B with both bases in view at
1600x900 with V-Sync off, over 240 rendered frames: 10.27 ms before V1 and
9.86 ms after, at 117 draw calls either way. Section 5's prediction that V1 is
net negative in cost holds.
Inputs: six parallel lens audits (Godot Forward+ pipeline config; procedural asset and material quality; art direction and value structure; RTS readability at strategic camera distance; benchmarking against shipped Godot 4 titles; a literal audit of every captured render and the shipped bake data), each then attacked by three independent sceptics who re-measured the evidence rather than re-reading the prose. Judged against docs/design/16-visual-style.md, docs/design/20-visual-aaa-roadmap.md, docs/design/22-scale-and-colour-roadmap.md, docs/design/03-technical-design-document.md and docs/adr/ADR-001, at HEAD.

This document answers one sentence Luke wrote after looking at other Godot games: "i have looked at other godot games and the graphics seem much better." He is right, and this document is not a defence of the existing work. It is written to be executed by a cheaper model, so every ticket carries its full mechanical specification, exact values and a measurable acceptance criterion.

A note on method, because it changes how much of this to trust. Every auditor claim below survived an adversarial pass, and where a sceptic disproved an auditor the sceptic won. Several of the most confidently written proposals in the research would have shipped real damage, and one of them would have made the game roughly three times darker while being described as "the biggest single win". Section 6 is the ledger of what was dropped and why, and it is not decoration. Anyone tempted to reach past this document into the raw audits should read section 6 first.

A second note. Every load-bearing claim in section 1 was re-verified against the working tree while writing this, read-only, at HEAD. Where a claim is quoted from an audit and was not independently re-measured, it is marked UNVERIFIED in place.

---

## 1. The honest answer

The reason other Godot games look better is not that they have a technique Ferrostorm lacks. It is that somebody looked at their frames. Ferrostorm has never once iterated one scene to beauty and then rolled the result outward, because the project has no way to see its own output: `grep -rn "SavePng\|GetImage\|ViewportTexture\|save_png" game/ tools/` returns exactly one hit and it is a line in a README. There is no offscreen capture, no reference frame, no screenshot harness, no look-development scene. Every image in this repository is a Blender turntable render of an asset in isolation, lit by lineup.py's own studio rig, which tells you nothing about the game. Meanwhile the session ledger records three completed tasks that claim an "offscreen client capture" (items 23, 30 and 34). The project believes it has a feedback loop it does not have. Four visual waves, doc 20's Waves 1, 2, 4 and 4c, were specified, implemented, verified and signed off without anyone looking at a frame of the running client. That is the root cause, and it is a process defect rather than a rendering defect. Everything below is downstream of it.

The second finding is nearly as uncomfortable. The fix for a large part of this was already written, costed and ratified, and then nothing happened. docs/design/22-scale-and-colour-roadmap.md is marked "RATIFIED PLAN, implementation contract" and dated 2026-07-15. Its Wave C, at lines 904 to 1162, is titled "colour and readability" and contains seven tickets with exact values, a mandated landing order and capture-based acceptance criteria: the PAL chroma pass, the 8-bit emissive clamp fix, the scatter hue jitter that the doc itself calls "the cheapest win in the project", the light rig warm/cool spread, the shroud wash, the second team-colour location and the grading LUT. Verified at HEAD: `grep -n "Biome\|MineralTints\|Jitter(" game/scripts/BattlefieldView.cs` returns nothing at all. Not one Wave C ticket landed. Neither did Wave A.5, the biome ground. Wave A's map tickets shipped, and then every subsequent commit went to gameplay: the wall system, ADR-007 through ADR-012, doc 23's base systems, doc 24's parity work. Four gameplay waves queue-jumped the colour plan, and one of its tickets, C-07, is still blocked on Luke's own signature to the doc 16 amendment in doc 22 section 5, where it has sat since 15 July. So the honest answer to "why does it not look as good" includes "because the plan to fix it was written a month ago and never started, and two of its items are waiting on you".

The third finding is the one doc 22 missed, and it is the single largest measured defect in the codebase. **The metallic channel of every model in the game is a constant.** art/3d/builder.py line 20 routes every roster part through `materials2.wmat(name, rough=rough, metal=max(metal, 0.2))`, and art/3d/bake.py bakes the Principled node's Metallic default value as a uniform emission colour, so the blue channel of the packed ORM texture of all 27 shipped .glb files is the byte 51, which is 0.200, at the 5th percentile, the median and the 95th percentile alike. A million pixels storing one number. Nothing in Ferrostorm is metal and nothing is a dielectric; everything is twenty per cent metal, which is a value the metallic-roughness BRDF has no valid material for. It removes a fifth of the diffuse albedo and hands it to a specular lobe with a muddy F0, and that lobe reflects the environment, which is a procedural sky whose top colour is (0.015, 0.02, 0.032). So a fifth of every surface's response in the game is a reflection of a black sky. That is the chalky grey-plastic read, and it is two lines of Python. Alongside it, art/3d/bake.py lines 135 to 136 multiply ambient occlusion into the diffuse map at `k = 0.85`, the same AO is packed into the ORM red channel and exported as the glTF occlusion texture, and the environment then runs SSAO at intensity 2.5 and power 1.8. Occlusion is applied three times and the albedo map is permanently contaminated with a lighting term.

Then there are two multiplicative grey veils that not one of the six lenses costed. The volumetric fog runs at density 0.022 over a length of 110 with an albedo of (0.55, 0.62, 0.75), a bright blue-grey. An RTS has no near field: the camera pitch is fixed at -50 degrees, so the nearest thing on screen is at H divided by sin 50, which is 28.7 metres at the shipped start height of 22 and 54.8 metres at the maximum height of 42. Transmittance is therefore between 0.53 and 0.30, meaning that somewhere between forty-seven and seventy per cent of the radiance of every pixel in the frame is uniform in-scattered fog rather than the battlefield, and it is spatially uniform because in a top-down view everything sits at roughly the same depth. That destroys albedo contrast, specular contrast, shadow contrast and hue separation simultaneously, and it costs one float to test. On top of it, game/scripts/FogOfWar.cs line 52 floats an unshaded alpha plane painting every explored-but-unseen cell with (0.012, 0.018, 0.030) at alpha 0.38, and in a real match most of the visible ground is explored-but-unseen at any moment. Neither veil appears in any Blender render, which is precisely why four waves of analysis walked past both.

### The proportioning

Judged against what was actually measured, and stated as the share of the perceived gap rather than the share of the work:

- **Art direction and colour execution: 40 per cent.** This is the unexecuted Wave C plus the metallic constant plus the triple AO. The palette in doc 16 is good, the shipped bake destroys it, and the plan to restore it exists.
- **Engine and environment configuration: 25 per cent.** Higher than the benchmark lens's estimate of 15, because two of the items are uniform multiplicative veils over the whole frame rather than marginal effects. Ambient source set to a flat colour, a near-black sky feeding the specular environment, fog density, the shroud alpha, and a ground shader that silently opts out of anisotropic filtering.
- **Asset construction: 20 per cent.** One material archetype applied to everything, fourteen of twenty-seven models shared between both factions, one twelve-sided cylinder vocabulary. Real, but capped by the two above and by the camera below.
- **RTS camera distance: 15 per cent.** Camera3D never sets `Fov`, so it runs at Godot's default 75. At the shipped start height a vehicle is roughly twenty pixels across. This functions as a discount applied to everything else: it means texel-level investment has close to zero return, which is why the largest single block of proposed work in the research was cut.

The one-line version. The game is not under-rendered. It is under-measured, its ratified colour plan was never run, one texture channel stores the number 0.2 a million times, and more than half of every pixel is fog.

---

## 2. What is already good and must NOT be rebuilt

This section exists to stop a cheaper model spending money twice. Every item below was verified at HEAD.

**The environment resource is more complete than most shipped Godot 3D games bother with.** game/scripts/BattlefieldView.cs lines 22 to 89: AgX tonemapping at exposure 1.35 with grading enabled, SSAO with tuned radius, power, horizon and sharpness, SSIL, SSR at 56 steps, a hand-shaped five-band glow curve, volumetric fog with temporal reprojection, a procedural storm sky. Do not add post-processing. The naive diagnosis of "no post-processing" is wrong and expensive to re-test.

**The light rig is a real three-point rig.** BuildLightRig at lines 95 to 130 gives a warm key at energy 1.7, a cool fill and a cool rim, with a 4096 shadow atlas, soft filter quality 3, blend splits on and `LightAngularDistance` for softening. The warm-against-cool intent is already correctly expressed in code. It needs retuning, not replacing, and doc 22's C-10 already carries the exact replacement values.

**The PBR bake pipeline is wired up correctly.** art/3d/bake.py produces four maps per model, exports glTF occlusion properly through the "glTF Material Output" node group, carries `KHR_materials_emissive_strength`, and tiers resolution by class at 2048 for structures, 1024 for vehicles and 512 for infantry. Verified by parsing the glTF JSON: dir_cannon_tank.glb genuinely carries normal, occlusion, base colour, metallic-roughness and emissive textures. The bakes are present and plumbed. Their *content* is the problem. Do not rebuild the pipeline; change what goes into it.

**Texel density is broadly right and is not the problem.** Roughly 1024 pixels per unit for structures, 730 for vehicles, 1280 for infantry. A 1.75x spread. Raising texture resolution is not a fix and must not be attempted.

**Silhouettes are better than the "procedural means primitive" story suggests.** builder.py contains 190 box() and cyl() calls across 27 models; dir_cannon_tank carries roughly fifteen to twenty primitives including glacis, engine deck, panel inserts, tow hooks, both track assemblies, turret ring, bustle, mantlet, muzzle brake, hatch, antenna and headlights. Adding geometry is not the fix.

**The terrain system, the batching and the motion layer.** A real heightfield with central-difference normals and tangents, a three-layer splat shader, cliff strata, shore ramps, bridge piers, MultiMesh batching (MAP-01, shipped), area-scaled scatter density (MAP-03, shipped), a 192x128 map (MAP-04, shipped). Turret tracking, recoil, wheel spin, hull pitch, build-up rise tweens, death tumbles, per-weapon projectiles, impacts, scorch decals, burning wrecks, camera shake. Motion is the one channel of visual quality that survives intact to RTS distance, and it is the one channel this project genuinely nailed. Do not touch it.

**The 2D icon set is the best-looking asset in the repository.** art/contact-sheet.png measures 21 per cent desaturated pixels against 91 to 99 per cent for every 3D render. The icons hold the palette that the 3D pipeline loses. They are not a problem to fix; they are the proof that the art direction is sound and the pipeline is what breaks it.

**Anti-aliasing of polygon edges.** project.godot sets `msaa_3d=2`, which is 4x MSAA, with a 4096 shadow map at filter quality 3. Edge aliasing is not the complaint.

---

## 3. The look-development loop

This is the process fix, and it is the reason this document exists rather than another ticket list. It ships first, before any visual change, and no ticket in section 4 may be closed without it.

The failure mode this replaces is specific and is documented in the ledger. A wave is specified against a technique, implemented across the whole game, verified by "the build is clean and the hashes did not move", and signed off. Nobody sees the result. It happened four times. Doc 22's Wave C tickets already carry capture-based acceptance criteria, which was the right instinct, and they were unexecutable because the capture does not exist.

**The loop, defined concretely.**

1. **One fixed reference scene.** skirmish-03, the only committed map that actually exercises the terrain vocabulary of water, hills, ruins, fences and bridges. Loaded from a committed save so the state is identical every run, with both players' bases in frame, a mixed force of both factions, and a region of explored-but-unseen shroud visible so the fog-of-war veil is in shot. This is not negotiable: a scene that varies between captures cannot support a before-and-after comparison.

2. **Three fixed reference cameras**, captured every run, all at the shipped fixed pitch of -50 degrees. CAM-A at height 22, which is the height SkirmishLive actually starts the camera at and therefore the height the game is played at. CAM-B at height 42, the maximum zoom, which is the worst case for pixel budget. CAM-C at height 9, close on a single tank against ground, which is the only view in which material work can be judged at all. Frozen positions, written down, never adjusted, because the moment the camera moves between captures the comparison is worthless.

3. **The capture script.** A tools-side offscreen capture that loads the reference save, positions each camera, waits a fixed number of frames for the temporal effects to converge (volumetric fog reprojection and any future temporal AA need this, and a capture taken on frame 1 is a different image), calls `GetViewport().GetTexture().GetImage()` and writes a PNG per camera to a directory outside res://. Runs headless with `--audio-driver Dummy` per game/README-GODOT.md. Ticket LOOK-01 specifies it.

4. **The contact sheet.** A script that takes a "before" directory and an "after" directory and emits a single PNG with the three camera pairs stacked, plus a printed table of the measurements each ticket's acceptance criterion names: mean luminance, mean HSV saturation, the 5th and 95th luminance percentiles, and the hue span across sampled geometry. The numbers exist so that "it looks better" can be checked, and the image exists because the numbers are not the point. Ticket LOOK-02.

5. **The rule, which is the actual deliverable of this section.** A visual ticket ships only if the after-capture is visibly better than the before-capture at the three reference cameras, judged as an image. The acceptance metrics are a floor, not a pass: a change that satisfies its numeric criterion and makes the contact sheet worse is a failed ticket and is reverted. Correspondingly, a change that is invisible at all three cameras is a failed ticket even if it is technically correct, and this is the rule that would have killed most of doc 20's Wave 4.

6. **The iteration order, which is the part that has never been done.** Tune CAM-C, one tank, one ground patch, to something Luke says looks good, *before* rolling anything out to the other twenty-six models and the other five maps. Every wave in section 4 lands at CAM-C first, gets judged, and only then goes wide. The project has a comprehensive environment, full PBR bakes and a three-light rig and a flat-looking game because every wave so far rolled a technique across everything without ever tuning one thing to beauty.

**Two things to capture on day one, before any change lands**, because they are one-line experiments that will settle arguments six lenses could not: a capture with `VolumetricFogDensity` set to 0, and a capture with the shroud plane hidden. If either frame reads dramatically better, the priority order in section 4 is already validated; if neither does, the fog hypothesis in section 1 is wrong and V1-01 and V1-02 drop to the bottom. This costs ten minutes and it is the highest-information action available anywhere in this document.

---

## 4. The waves

Ordered by visual impact per unit of work, cheapest transformative things first. Every ticket below is client-side or art-side. Not one edits /sim. Labels follow the CLAUDE.md workflow convention.

Wave summary:

| Wave | What | Tickets | Sim risk | Bake required |
|---|---|---|---|---|
| V0 | The look-development loop | 3 | none | no |
| V1 | The veils and the free one-liners | 7 | none | no |
| V2 | The one bake session | 4 | none | yes, once |
| V3 | Camera, scale and the aliasing | 4 | none | no |
| V4 | Faction identity and first impression | 4 | none | no |

Twenty-two tickets, five waves. Doc 22's Wave C tickets C-01, C-05, C-06, C-07, C-08, C-10 and C-11 are **not restated here**. They are ratified and they stand. This document schedules them and folds two additional fixes into C-05's bake session; where a ticket below interacts with one, the interaction is named in place.

### Wave V0: the look-development loop

#### LOOK-01. Offscreen capture harness for the running client

- Labels: `persona:tools` `gdd:none` `phase:6` `owner:tools`
- Impact: CRITICAL | Effort: M | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: tools/capture/ (new), game/scripts/ (one temporary autoload, not committed to the scene tree)
- Spec:

1. Add a committed reference save of skirmish-03 with both bases, a mixed force and visible shroud, at tools/capture/reference-state.fsave. Produce it by playing to a stable state and saving; commit the file so every capture is byte-identical in state.
2. Add a capture autoload that takes a camera list, and for each entry sets `RtsCamera` position and lets the fixed -50 pitch stand, waits 30 rendered frames, then `GetViewport().GetTexture().GetImage().SavePng(path)`.
3. The three cameras, frozen: CAM-A at Y=22, CAM-B at Y=42, CAM-C at Y=9. Record the exact X and Z in the script as constants with a comment saying they must never change.
4. Invocation: headless, `--audio-driver Dummy`, output directory passed as an argument, per game/README-GODOT.md line 31's existing idiom.
5. The 30-frame wait is mandatory and must be commented as such: `VolumetricFogTemporalReprojectionEnabled` is true (BattlefieldView.cs:82) so a frame-1 capture is a different image from a converged one, and any future temporal AA makes this worse.
6. Do not add the autoload to the shipped scene tree and do not ship the capture path in a player build.

- Acceptance: running the harness twice in a row on an unchanged tree produces three PNGs each time that are byte-identical between runs. This determinism check is the whole point of the ticket and it must pass before anything downstream is trusted. sim golden-hash battery exits 0 (nothing in /sim is touched).

#### LOOK-02. Before-and-after contact sheet and the measurement table

- Labels: `persona:tools` `gdd:none` `phase:6` `owner:tools`
- Impact: CRITICAL | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: tools/capture/contact.py (new)
- Spec:

1. Takes two directories of captures and writes one PNG: three rows, before on the left and after on the right, each pair labelled with the camera name.
2. Prints a table per camera of mean luminance, mean HSV saturation, the 5th and 95th luminance percentiles, and the total hue span over 200 sampled pixels that land on unit geometry.
3. Percentiles are reported over the full frame with pure black excluded, and the exclusion is stated in the output, because the sceptic pass showed two independent audits reaching opposite conclusions about the same asset by silently disagreeing on their island mask.
4. Standard library plus Pillow only, consistent with the project's zero-dependency posture in /sim (this is tools-side, so Pillow is acceptable, but do not add a dependency to /sim or /game).

- Acceptance: run against two identical directories, every metric delta prints as exactly 0.000 and the contact sheet halves are visually identical. sim golden-hash battery exits 0.

#### LOOK-03. Record the capture-verification rule and correct the ledger

- Labels: `persona:producer` `gdd:none` `phase:6` `owner:producer`
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: docs/design/20-visual-aaa-roadmap.md, docs/design/22-scale-and-colour-roadmap.md, the session ledger
- Spec:

1. Record in both visual roadmaps that a visual ticket is not closeable without a before-and-after contact sheet attached, and that a change invisible at all three reference cameras is a failed ticket regardless of its numeric criterion.
2. Correct the ledger entries for items 23, 30 and 34, which record a completed "offscreen client capture" that no code in the repository can perform. State plainly that no capture was taken. This is not bookkeeping pedantry: the false entries are the reason nobody went looking for the missing harness for four waves.

- Acceptance: both docs carry the rule. The three ledger entries are corrected in place with a dated note. No code changes.

### Wave V1: the veils and the free one-liners

Everything in this wave is a value change or a one-word shader edit. No bake, no asset work, all of it reversible in one line, and it is where the largest change per unit of effort in the whole document sits. Land the whole wave as one change and judge it on one contact sheet, because several items are individually counterintuitive: raising the sky while lowering the fog will look wrong if either lands alone.

#### V1-01. Cut the volumetric fog density and inject GI into what remains

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: CRITICAL | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

At the shipped start height of 22 and a fixed -50 pitch, the minimum camera-to-ground distance is 28.7 metres, so `VolumetricFogDensity = 0.022f` (line 75) yields a transmittance of 0.53, and at maximum height 0.30. Between forty-seven and seventy per cent of every pixel is uniform in-scattered fog at albedo (0.55, 0.62, 0.75).

1. Line 75: `VolumetricFogDensity = 0.022f` becomes `0.010f`. At 28.7 metres that lifts transmittance from 0.53 to 0.75.
2. Line 76: `VolumetricFogAlbedo` (0.55f, 0.62f, 0.75f) becomes (0.46f, 0.50f, 0.58f). Less saturated and darker, so what fog remains reads as distance rather than as a scrim.
3. Line 78: `VolumetricFogLength` 110f stays. The length is not the problem; the density over the unavoidable minimum distance is.
4. Add `VolumetricFogGiInject = 0.6f`, currently unset and therefore 0, so the fog picks up scene light and produces coloured haze around explosions and the superweapon rather than a uniform tint.
5. Do not disable the fog. It is doing real atmospheric work at the far edge of the frame and the storm mood depends on it. This is a density change, not a deletion.

- Acceptance: contact sheet at all three cameras. At CAM-A the 95th-percentile luminance rises by at least 8/255 and the 5th-percentile luminance falls by at least 4/255, which is the frame regaining range at both ends rather than merely brightening. Mean HSV saturation at CAM-A rises by at least 0.03 absolute. Frame time at 1600x900 within 0.3 ms of baseline, and expected to improve slightly. sim golden-hash battery exits 0.

#### V1-02. Schedule doc 22's C-08, the explored-shroud wash

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/FogOfWar.cs
- Spec: Implement doc 22 ticket C-08 exactly as written at doc 22 lines 1104 to 1120. Line 52's `new Color(0.012f, 0.018f, 0.030f, 0.38f)` becomes `new Color(0.020f, 0.030f, 0.052f, 0.30f)`. Do not re-derive the values; they are ratified. Honour C-08's integration note about MAP-07's byte-array rewrite: if MAP-07 has landed, edit bytes 5, 8, 13, 77 rather than Colors, and the two must not disagree.

This document raises C-08's stated impact from LOW to HIGH. C-08 was written before anyone had established that most of the frame is explored-but-unseen in a real match, and a 38 per cent near-black overlay across most of the screen is a second uniform veil stacked on V1-01's. That is a re-rating, not a re-specification.

- Acceptance: C-08's own acceptance criteria at doc 22 line 1120, run through the LOOK-02 contact sheet, with the reference save's shroud region visible at CAM-A.

#### V1-03. Ambient source to Sky, and lift the sky so there is something to reflect

- Labels: `persona:client-engineer` `gdd:16` `phase:6` `owner:client-engineer`
- Impact: CRITICAL | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

Line 41 sets `AmbientLightSource = Godot.Environment.AmbientSource.Color` with a flat (0.34, 0.38, 0.46) at energy 0.4. A constant ambient arrives identically from every direction, so every surface not hit by the key receives the same value regardless of its normal, which is the literal definition of flat shading. Meanwhile `ReflectedLightSource` is already Sky (line 40), and the sky it reflects has a top colour of (0.015, 0.02, 0.032), so the specular environment of every surface in the game is black. These two must move together or each looks like a failure alone.

1. Line 41: `AmbientSource.Color` becomes `AmbientSource.Sky`.
2. Line 42: delete `AmbientLightColor`. It is unread under Sky and leaving it invites a future reader to tune a dead value.
3. Set `AmbientLightSkyContribution = 1.0f`.
4. Line 43: leave `AmbientLightEnergy` at 0.4f for this ticket. Do NOT drop it. Three separate audits proposed cutting ambient and fill energy in the same change as lifting the sky, and the sceptic pass established the frame is already dark, not bright: the shipped diffuse atlas has a median of linear 0.037. Retune ambient in V1-07 against a capture, once, after everything else in this wave has landed.
5. Line 29: `SkyTopColor` (0.015f, 0.02f, 0.032f) becomes (0.060f, 0.080f, 0.130f).
6. Line 30: `SkyHorizonColor` (0.10f, 0.085f, 0.075f) becomes (0.22f, 0.19f, 0.16f).
7. Line 32: `SkyEnergyMultiplier` 1.0f becomes 1.6f.
8. Do not touch `GroundBottomColor`, the AgX settings, or any Ssao, Ssil or Glow value in this ticket.

The storm mood survives this comfortably: AgX plus the existing contrast of 1.14 pulls it back down, and the sky is the only specular environment in the scene. This is the ticket that makes the already-shipped ORM bakes pay for themselves, and it is a prerequisite for judging V2-01.

- Acceptance: contact sheet. At CAM-C, sample an upward-facing hull face and a downward-facing hull face on the same tank in shadow: their luminance must now differ by at least 12/255, where pre-change they differ by under 3/255. That single measurement is the entire point of the ticket, because it is the difference between directional ambient and flat fill. Mean open-ground luminance at CAM-A stays inside 90 to 135 out of 255. sim golden-hash battery exits 0.

#### V1-04. Give the ground shader anisotropic filtering

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/shaders/ground_splat.gdshader
- Spec:

Verified at HEAD, lines 2 to 4: all three samplers are declared `filter_linear_mipmap`, which is trilinear and silently opts the ground out of the project-wide anisotropic setting entirely. At a fixed -50 pitch the ground is always at a grazing angle and always occupies most of the frame, which is the exact case anisotropic filtering exists for.

1. Change `filter_linear_mipmap` to `filter_linear_mipmap_anisotropic` on `noise_a`, `noise_b` and `detail_normal`.
2. Note in a comment that this is a shader-side hint and that the project setting in project.godot does not reach this shader, so both must be set.

Read this ticket alongside the drop record in section 6. One audit ranked raising the project-wide anisotropic key as its best sharpness-per-effort item in the entire list and justified it entirely by the ground, which this shader does not obey. The project key is still worth setting, in V1-05, but on its own it would have been a verified no-op shipped as a win.

- Acceptance: contact sheet at CAM-A and CAM-B. Crop the mid-distance third of the frame and compute Laplacian variance as a sharpness proxy; it must rise by at least 15 per cent. Visible check: the splat layer boundaries and cliff strata resolve in the middle distance rather than smearing. sim golden-hash battery exits 0.

#### V1-05. project.godot rendering keys: anisotropy, debanding, FXAA

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/project.godot
- Spec:

The `[rendering]` section sets six keys. Add three.

1. `textures/default_filters/anisotropic_filtering_level=4`. The enum is 0=Disabled, 1=2x, 2=4x, 3=8x, 4=16x, and the Godot 4 default is index 2, which is 4x. Two separate audits misstated this default in opposite directions, one as 2x and one as 8x, and one of them prescribed index 3 while describing it as 16x. The correct value for 16x is 4. This affects every `StandardMaterial3D` in the game, which is every unit and structure, and it does not reach the ground shader, which V1-04 handles.
2. `anti_aliasing/quality/use_debanding=true`. Off by default. With a dark sky and volumetric fog gradients this is the textbook banding case.
3. `anti_aliasing/quality/screen_space_aa=1`, which is FXAA. Keep `msaa_3d=2`. MSAA resolves polygon edges only and does nothing for the specular sparkle on normal-mapped metal hulls, which is the aliasing an RTS camera shows most. FXAA covers it cheaply. Do NOT enable TAA; see section 6.

- Acceptance: contact sheet. Banding check: sample a 200-pixel horizontal run across the sky gradient at CAM-B and count distinct luminance steps; it must at least double. Specular shimmer check is qualitative and belongs to the contact sheet judgement. Frame time within 0.4 ms of baseline. sim golden-hash battery exits 0.

#### V1-06. Disable SSR and bank the budget

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: LOW visually, MEDIUM as headroom | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

Lines 63 to 67 run screen-space reflections at 56 steps. SSR only reflects what is already on screen, and at a -50 pitch almost nothing is on screen to reflect onto the ground. Every terrain material in the file is roughness 0.85 to 1.0, and rough surfaces return nothing from SSR. The full cost is paid so that one water slab benefits.

1. Set `SsrEnabled = false`.
2. Leave the water material (line 455, metallic 0.85, roughness 0.08) alone. It will lose its screen-space reflection and keep its sky reflection, which at this camera is most of what it was showing anyway.
3. Record the measured frame-time saving in the ledger. It is the only cost reduction anywhere in this document and it pays for V1-01's GI inject and V1-05's FXAA.
4. If the contact sheet shows the water visibly worse, revert this single ticket rather than compensating elsewhere.

- Acceptance: contact sheet shows no visible regression at any of the three cameras. Frame time at 1600x900 improves measurably; record the number. sim golden-hash battery exits 0.

#### V1-07. The single ambient and fill retune, judged against a capture

- Labels: `persona:client-engineer` `gdd:16` `phase:6` `owner:client-engineer`
- Impact: MEDIUM | Effort: S | cheapModelSafe: false | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

TASTE REQUIRED. This ticket is deliberately last in the wave and deliberately vague on its final numbers, because it is the one place where the research disagreed with itself most sharply and where guessing is expensive.

Once V1-01 through V1-06 have landed, capture, then make at most one adjustment to the balance between key, fill, rim and ambient, and record the values in the ledger.

1. The candidate direction, if and only if the capture shows the shadow side of units washing out: `AmbientLightEnergy` (line 43) from 0.4f toward 0.30f, and `FillSky` energy (line 119) from 0.5f toward 0.35f. Under `AmbientSource.Sky` the ambient is now directional and doing work the flat fill used to fake, so some of the fill is genuinely redundant.
2. The opposing constraint, which must be honoured: the shipped diffuse atlas has a median of linear 0.037, so the frame is already dark. Do not cut ambient, fill and rim together, and do not cut them at all if the capture's 5th-percentile luminance is already below 12/255.
3. This ticket must not touch light *colours*. The warm and cool spread is doc 22's C-10 and it has exact ratified values; landing both in the same edit makes the contact sheet uninterpretable. Land C-10 separately, after this.
4. One adjustment, one capture, one ledger entry. If the first adjustment does not improve the contact sheet, revert to the V1-06 state and close the ticket as no-change.

- Acceptance: mean open-ground luminance at CAM-A inside 90 to 135 out of 255. Contact sheet judged better by Luke at CAM-C. The chosen values are recorded. sim golden-hash battery exits 0.

### Wave V2: the one bake session

A full Cycles bake of 27 models across four map types at 2048, 1024 and 512 is the most expensive single action anywhere in this document, and three separate audits each proposed it as their number-one action for three different and mutually incompatible reasons, none acknowledging the others. **It happens exactly once and it carries every fix simultaneously.** Doc 22 already mandated part of this ordering at line 908: C-06 must land with or before C-05, or the chroma C-05 restores is immediately clipped away by the 8-bit bake.

The session's contents, in landing order within the session: V2-01, V2-02, then doc 22's C-06, then doc 22's C-05, then V2-03. One `blender -b -P bake.py` run at the end.

#### V2-01. Make metallic binary and kill the 0.2 floor

- Labels: `persona:art-pipeline` `gdd:16` `phase:6` `owner:art-pipeline`
- Impact: CRITICAL | Effort: M | cheapModelSafe: true | touchesSim: false | client-only: yes (asset-only)
- Files: art/3d/builder.py, art/3d/materials2.py, art/3d/bake.py, game/assets/models/*.glb
- Spec:

This is the single largest measured defect in the codebase and doc 22 did not catch it. Verified: builder.py line 20 reads `return materials2.wmat(name, rough=rough, metal=max(metal, 0.2))`, and the ORM blue channel of the shipped bakes is the byte 51 at the 5th, 50th and 95th percentiles alike. Every surface in the game is exactly twenty per cent metal. The metallic-roughness BRDF has no valid material between roughly 0.05 and 0.9: mid-metallic darkens the diffuse albedo and simultaneously raises F0 into a muddy grey specular, which is the chalky plastic read.

1. builder.py line 20: remove the `max(metal, 0.2)` floor entirely, passing `metal=metal` through.
2. builder.py line 17: change the `mat()` default from `metal=0.15` to `metal=0.0`.
3. In materials2.wmat, drive the Metallic socket from a **map**, not a constant: painted armour, rubber, concrete and cloth are 0.0; the bare-metal chip mask, which is the existing pointiness-driven `mul` output, is 1.0. Metallic then becomes binary everywhere except the single transition pixel at a chip edge, which is the only place a blend is physically legitimate.
4. bake.py's value-pass path must bake the metallic **node output** rather than the Principled node's `default_value`. Read the file before editing: the emissive path already rewires through a Separate Color node off the ORM texture in the rebuilt material, so the existing code is not uniformly a default-value read, and an instruction written from the audit's description of it will not match what is there.
5. Assign metals deliberately across the palette: `gun`, `plate` and `gundark` bodies are painted steel, so metallic 0.0 with the chips at 1.0; `bone`, `olive`, `olived` and `cinder` are 0.0; the ferrite family keeps its emissive treatment untouched.
6. Do not change any geometry, UV, bake resolution or emissive value in this ticket.

- Acceptance: automated check on the re-exported ORM textures of all 27 models: the blue channel's 5th percentile must be below 0.05 and its 95th percentile above 0.90, that is, the histogram must be bimodal where it is currently a single spike at 0.200. Fewer than 8 per cent of texels sit between 0.10 and 0.85. Contact sheet at CAM-C: the tank reads as painted metal with specular response on its chipped edges. sim golden-hash battery exits 0, this being an asset-only change.

#### V2-02. Stop multiplying ambient occlusion into the diffuse, and pull SSAO back

- Labels: `persona:art-pipeline` `gdd:none` `phase:6` `owner:art-pipeline`
- Impact: CRITICAL | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: art/3d/bake.py, game/scripts/BattlefieldView.cs, game/assets/models/*.glb
- Spec:

Verified: bake.py lines 135 to 136 read `k = 0.85` then `out = d * (1.0 - k + k * a)`, so the exported diffuse map is literally the base colour with AO multiplied in at 85 per cent. The same AO is packed into the ORM red channel and exported as the glTF occlusion texture, which Godot wires into the material's AO slot automatically. The environment then applies SSAO at intensity 2.5 and power 1.8, against Godot's defaults of 2.0 and 1.5. Occlusion is applied three times and the albedo carries a lighting term that cannot be undone.

1. bake.py line 135: change `k = 0.85` to `k = 0.0`, and add a comment stating that AO belongs in the ORM red channel only, that baking lighting into base colour is not recoverable, and that the environment applies its own SSAO on top.
2. Preferably delete the multiply block outright rather than zeroing the constant, so a future reader cannot restore it by tuning a number.
3. Do NOT change `use_pass_direct` or `use_pass_indirect`. They are already both False, set before the loop at bake.py lines 110 to 111 with a comment reading "DIFFUSE: colour only, no lighting baked in". One audit prescribed setting them to False as its supporting fix; that would be a verified no-op shipped as a win, and it also means the audit's stated evidence, a 0.992 correlation between the AO channel and diffuse luminance, was a consequence of the line 136 multiply by construction rather than an independent discovery.
4. BattlefieldView.cs line 52: `SsaoIntensity = 2.5f` becomes `1.6f`. Line 53: `SsaoPower = 1.8f` becomes `1.4f`. The 2.5 was tuned to be visible against already-darkened crevices; with the double count removed it will crush.
5. Do not touch `SsaoRadius`, `SsaoDetail`, `SsaoHorizon`, `SsaoSharpness` or `SsaoLightAffect`, all of which are well tuned.

- Acceptance: automated check on the re-exported diffuse textures: mean relative luminance rises by at least 25 per cent across every model, and the correlation between the diffuse luminance and the ORM red channel falls below 0.55, where it is currently 0.992. Contact sheet at CAM-C: crevices read as shaded rather than as black holes, and panel interiors are visibly lighter. sim golden-hash battery exits 0.

#### V2-03. Widen roughness into a real range and give it a second octave

- Labels: `persona:art-pipeline` `gdd:16` `phase:6` `owner:art-pipeline`
- Impact: HIGH | Effort: M | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: art/3d/materials2.py, game/assets/models/*.glb
- Spec:

Measured on the shipped ORM green channel: 0.494 at the 5th percentile, 0.573 at the median, 0.639 at the 95th. A total spread of 0.145 across an entire model, from a single noise octave at scale 5.5, which is about five soft blobs across the whole asset. Visually that is a constant, so nothing in the game catches a highlight differently from anything else. This lands in the same session as V2-01 because binary metallic with constant roughness still reads wrong.

1. In the roughness chain at materials2.py lines 110 to 123, raise the grime multiplier from 0.25 to 0.45 and the base offset from `rough - 0.1` to `rough - 0.22`, which at the roster defaults yields roughly 0.48 to 0.93.
2. Add a second Noise node at Scale 12, Detail 6, mixed in at plus or minus 0.06 for medium blotching.
3. Add a third at Scale 220, Detail 8, mixed at plus or minus 0.04 for micro grain. Three octaves plus a real range is the difference between a shaded object and a surfaced one.
4. Feed `coord.outputs['Object']` into the Vector input of every noise node. They are currently unconnected and therefore default to Generated, which is bounding-box space, so a two-unit structure and a 0.4-unit infantryman receive the same number of grime blobs. Detail frequency currently scales with object size, which reads as small things being noisier than big things.
5. Rescale the noise after step 4, since object space changes what a given Scale means. Target roughly one grime feature per 0.4 world units.
6. Do not add greebles, rivets, welds or stencils. See section 6.

- Acceptance: the ORM green channel's 5th percentile falls below 0.42 and its 95th percentile rises above 0.86 on every model, a spread of at least 0.44 against the current 0.145. Frequency check: the standard deviation of roughness within a 32x32 texel window is at least 0.04, confirming the micro octave survived the bake. Contact sheet at CAM-C: the key light rakes across the hull rather than tinting it uniformly. sim golden-hash battery exits 0.

#### V2-04. Schedule doc 22's C-06 then C-05 inside this session

- Labels: `persona:art-pipeline` `gdd:16` `phase:6` `owner:art-pipeline`
- Impact: HIGH | Effort: L | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: art/3d/bake.py, art/3d/builder.py, art/3d/materials2.py, game/assets/models/*.glb, docs/design/16-visual-style.md, docs/design/20-visual-aaa-roadmap.md
- Spec:

Implement doc 22 tickets C-06 (doc 22 lines 987 to 1042) and then C-05 (lines 933 to 985), exactly as written, in that order, in the same Blender session as V2-01 through V2-03, with a single `blender -b -P bake.py` at the end.

Do not re-derive C-05's palette table. Verified at HEAD, art/3d/builder.py line 36 confirms C-06's premise: `bpy.data.images.new(img_name, size, size, alpha=False)` with no `float_buffer`, so the bake target is an 8-bit buffer and every emissive above 1.0 clips a channel, which on a saturated colour is a hue shift rather than a brightness cap. That is why the superweapon core bakes yellow instead of signal orange and the Sodality veil orb bakes cyan-white instead of corroded teal. C-06 is the reason the faction accent colours are missing from the frame and no lens found it.

**The warning that matters most in this document.** One audit proposed, as its single highest-ranked action, applying an sRGB-to-linear conversion to every PAL entry, on the theory that the palette is inflated between 1.26 and 6.6 times. Doc 22 line 942 already forbids this in terms, having done the arithmetic: rust would fall 62 per cent in relative luminance and the Sodality faction would disappear against the ground. The sceptic pass then measured the shipped atlas independently and found the median albedo is linear 0.037, roughly eight times darker than the palette, not brighter. Applying the conversion would have multiplied an already-dark asset by a further 0.37 and returned a visibly blacker game after the one expensive bake the project had budget for. C-05's chroma table is the correct fix: it raises saturation while holding relative luminance within about 15 per cent. **Do not substitute a colour-space conversion for it.**

- Acceptance: C-06's and C-05's own acceptance criteria, at doc 22 lines 1042 and 985 respectively, plus the V2-01 through V2-03 criteria, all verified against the single post-session bake. Additionally: the contact sheet at all three cameras compared against the pre-session capture, and mean relative luminance of every model's diffuse within 20 per cent of pre-session, which is C-05's own "nothing went dark" guard and is now also guarding against the drop recorded above. sim golden-hash battery exits 0.

### Wave V3: camera, scale and the aliasing

#### V3-01. Set the camera FOV to 50

- Labels: `persona:client-engineer` `gdd:11` `phase:6` `owner:client-engineer` `needs:game-designer`
- Impact: HIGH | Effort: S | cheapModelSafe: false | touchesSim: false | client-only: yes
- Files: game/scripts/RtsCamera.cs
- Spec:

Verified: `Camera3D.Fov` is never set anywhere in game/scripts or game/scenes/Battle3D.tscn, so it runs at Godot's default of 75 vertical. At the shipped start height of 22, a fixed -50 pitch and a 900-pixel viewport, that gives roughly 18 pixels per world unit, so a vehicle body of 0.78 by 1.0 units is about 15 pixels across. The wide default also produces perspective distortion that reads as amateurish on a fixed-pitch view.

1. In `_Ready`, set `Fov = 50f;` with a comment recording the derivation and the reason.
2. Do not change `MinHeight` or `MaxHeight` in this ticket.
3. Reconcile with doc 22's MAP-05, which adds a camera-height-driven `DirectionalShadowMaxDistance` to BuildLightRig. FOV and MAP-05 both change how much ground falls inside the shadow cascade. If MAP-05 has landed, re-derive its `Mathf.Max` against the new FOV and record the result; do not let either overwrite the other.
4. Verify the minimap frustum polyline still matches what the player sees. `RtsCamera` shares a 0.55 look-at offset with `Minimap.Refresh`, and a FOV change alters the frustum the minimap draws.

This is a **gameplay-visible change**, not a presentation tweak: it reduces how much battlefield the player can see at a given height, which is a design decision on maps whose ferrite spacing doc 22 tuned by measurement. It needs the Game Designer at a capture. It must not be bundled into an unattended batch and it must not be the first thing anyone tries. Two audits ranked it their number-one action; it is here, in wave three, because the whole of V1 is cheaper, reversible and addresses the specific word Luke used, which was that the graphics look worse, not that the units look small.

- Acceptance: contact sheet at all three cameras, judged by Luke and the Game Designer together. Measured: pixels per world unit at CAM-A rises from roughly 18 to roughly 27. The minimap frustum polyline matches the rendered view at both extremes of the zoom range. Ferrite fields on skirmish-01 through skirmish-04 remain findable without excessive scrolling, judged by the Game Designer. sim golden-hash battery exits 0.

#### V3-02. Make the depth of field honest or remove it

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: LOW | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/RtsCamera.cs
- Spec:

Verified, and this corrects two audits that contradicted each other. `DofBlurFarDistance = 55f` is set in `_Ready` at line 38 and then overwritten every single frame in `_Process` at line 100 by `Position.Y * 2.2f + 12f`, so the 55 is dead code. `DofBlurAmount` is 0.05, which is half Godot's default. One audit called this a permanently destroyed upper third of the frame and ranked killing it third; another called it invisible. The truth is in between and closer to the second: a mild veil at the far edge, not a destroyed frame. This is a low-priority tidy, not a cause.

1. Delete the dead `DofBlurFarDistance = 55f` initialiser at line 38, since it is overwritten on the first frame and only misleads.
2. Note that V3-01's FOV change pulls the top-of-frame ground substantially closer, so after V3-01 the existing `Position.Y * 2.2f + 12f` covers proportionally more of the frame. Re-judge on the contact sheet.
3. Either keep the effect with `DofBlurAmount` raised to 0.10f for a deliberate and visible tilt-shift, or set `DofBlurFarEnabled = false` and reclaim the cost. Choose on the contact sheet and record which, in one line, in the ledger. Do not leave it at a value too small to see and too costly to be free.
4. Do not enable near-field DOF under any circumstances. Units at the bottom of the frame are gameplay surface.

- Acceptance: contact sheet at CAM-A and CAM-B. Whichever branch is taken is recorded with its measured frame-time delta. sim golden-hash battery exits 0.

#### V3-03. Fade the ground detail normal with distance and drop its frequency

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: HIGH | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/shaders/ground_splat.gdshader
- Spec:

The detail normal is a runtime-generated 512-pixel four-octave noise texture sampled at `wpos.xz * 0.15`, which is one tile per 6.67 world metres. At the 28.7-metre minimum camera distance that tile covers roughly 71 screen pixels, an undersample of about seven times, with `NORMAL_MAP_DEPTH` up to 0.95 and no distance fade at all. Undersampled normal detail shimmers as the camera pans, and this is aliasing inside triangles, which MSAA provably cannot resolve. It is very likely the largest single contributor to the impression that the game looks noisy and cheap in motion.

1. Change the detail normal sample from `wpos.xz * 0.15` to `wpos.xz * 0.05`.
2. Change the gravel grain sample from `wpos.xz * 0.23` to `wpos.xz * 0.07`.
3. Add a distance fade and multiply `NORMAL_MAP_DEPTH` by it: `float ndfade = 1.0 - smoothstep(30.0, 90.0, length(VERTEX));`.
4. Do not change the splat layer logic, the layer colours, or the heightfield. The layer colours belong to doc 22's C-02 and C-04 and must not be touched here.
5. Note in a comment that the texture is generated at runtime by `BattlefieldView.GrainTex`, so the fix is the sample frequency and the fade, and the project-wide filtering setting is not the lever. V1-04's anisotropic hint is complementary and both are needed.

- Acceptance: capture two frames at CAM-A with the camera panned 4 world units apart and difference them; the mean absolute difference over ground pixels must fall by at least 40 per cent, which is the crawl measured directly. Mid-distance sharpness from V1-04 must not regress. sim golden-hash battery exits 0.

#### V3-04. Range-cull the sub-pixel scatter instead of deleting it

- Labels: `persona:client-engineer` `gdd:none` `phase:6` `owner:client-engineer`
- Impact: MEDIUM | Effort: S | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

2200 rubble instances at scale 0.05 to 0.21 units and 900 grass tufts at 0.16 units tall are between half a pixel and two pixels each at CAM-B. Sub-pixel MultiMesh boxes flicker in and out of sample coverage as the camera pans.

1. Set `VisibilityRangeEnd = 38f` and `VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self` on the rubble and tuft `MultiMeshInstance3D` nodes, so they exist only when the camera is close enough to resolve them.
2. Raise the minimum rubble instance scale from 0.05 to 0.12 units.
3. **Do NOT cut the instance counts.** MAP-03 shipped specifically to scale scatter density with map area because a 192x128 map read as bare ground, and one audit proposed a 60 to 70 per cent count cut that would directly undo it. Range culling achieves the visual goal without fighting a shipped decision, and it improves frame time rather than costing it.
4. Interaction with doc 22's C-01: C-01 adds per-instance hue jitter to these same MultiMeshes and is unaffected by a visibility range. Land either order.

- Acceptance: the pan-difference measurement from V3-03, restricted to scatter regions, falls by at least 50 per cent at CAM-B. At CAM-C the scatter is visually unchanged, which is the guard against having simply deleted content. Frame time at CAM-B improves; record the number. sim golden-hash battery exits 0.

### Wave V4: faction identity and the first impression

The highest-leverage work in this wave is not on the battlefield. Luke formed his judgement by looking at other Godot games, and most of what a player sees of a game before a unit ever moves is a menu and a HUD.

#### V4-01. Schedule doc 22's C-07 and unblock the doc 16 amendment

- Labels: `persona:client-engineer` `gdd:16` `phase:6` `owner:producer` `needs:luke`
- Impact: CRITICAL | Effort: M | cheapModelSafe: true | touchesSim: false | client-only: yes
- Files: docs/design/16-visual-style.md, game/scripts/BattlefieldView.cs, game/scripts/SkirmishLive.cs
- Spec:

C-07 is specified in full at doc 22 lines 1044 to 1102 and it is blocked on the doc 16 amendment written at doc 22 section 5, which has been waiting on Luke's signature since 15 July. This ticket is a producer action first and an engineering action second.

Verified, and it makes C-07's approach the correct one rather than merely a convenient one: parsing the material arrays of all 27 shipped .glb files shows that twenty of them carry exactly one material and one primitive, including every wall piece, both infantry squads, the construction yard, power plant, refinery, service depot, MCV, superweapon, turret, veil projector and both Sodality vehicles. **There is no per-surface material slot to override for team colour on most of the roster**, so any proposal to tint a unit's body at runtime requires a re-bake that splits materials. C-07 sidesteps this entirely by adding team-coloured geometry at runtime, a square footprint strip for structures and a widened ground ring for units, which needs no bake and no material slot. That is the right answer and it should not be re-litigated into a bake wave.

Also note what C-07 records at doc 22 line 1054: the one-place team-colour law in doc 16 is **already broken in shipped code**, because `DressMobile` adds a team ground ring on top of the model's baked band. The bible is already out of date with the game. That weakens the case for treating the amendment as a heavyweight decision and strengthens the case for signing it.

- Acceptance: the doc 16 amendment at doc 22 section 5 is signed. C-07's own acceptance criteria at doc 22 line 1102, run through the LOOK-02 contact sheet with both bases in frame. sim golden-hash battery exits 0.

#### V4-02. Schedule doc 22's C-01, C-10 and C-11

- Labels: `persona:client-engineer` `gdd:16` `phase:6` `owner:client-engineer`
- Impact: HIGH | Effort: M | cheapModelSafe: C-01 and C-10 yes, C-11 no | touchesSim: false | client-only: yes
- Files: game/scripts/BattlefieldView.cs
- Spec:

Implement doc 22's C-01 (scatter hue jitter, doc 22 lines 912 to 931), C-10 (the warm and cool light rig spread, lines 1122 to 1141) and C-11 (the split-tone grading LUT, lines 1143 to 1161) exactly as written. Doc 22 calls C-01 the cheapest win in the project and it has never been run.

1. Landing order within this ticket, per doc 22 line 908: C-01, then C-10, then C-11 last as a taste pass.
2. C-10 must land **after** V1-07, not with it, so the contact sheet can attribute the change. V1-07 moves light energies; C-10 moves light colours.
3. C-10 clause 4 says its ambient value moves into `Biome.Ambient` once doc 22's C-02 lands. C-02 has not landed. Apply C-10 clause 4 to `AmbientLightColor` directly, and note in the ledger that C-02 must pick it up when it lands.
4. C-11 clause 3 is a genuine kill switch: if Godot 4.7's `AdjustmentColorCorrection` does not produce a split-tone from a `GradientTexture1D`, delete the block and close as WONTFIX rather than substituting something else. Honour it.

- Acceptance: each of C-01, C-10 and C-11's own acceptance criteria at doc 22 lines 931, 1141 and 1161, each run through the LOOK-02 contact sheet. sim golden-hash battery exits 0.

#### V4-03. Theme the HUD and give the main menu a backdrop

- Labels: `persona:ux` `gdd:86` `phase:6` `owner:ux`
- Impact: HIGH | Effort: M | cheapModelSafe: false | touchesSim: false | client-only: yes
- Files: game/ui/ (new theme resource), game/scripts/MainMenu.cs, game/scripts/Sidebar.cs
- Spec:

UNVERIFIED in detail: this ticket rests on one audit's reading of MainMenu.cs and Sidebar.cs, which this document did not independently re-read. The claim is that the entire UI is built from per-widget `AddThemeColorOverride` calls with no `.tres` or `.theme` resource anywhere in game/, that the main menu is a flat ColorRect in cinder with a text title and eight text buttons and no art or motion, that there is no loading screen, and that the sidebar is flat `StyleBoxFlat` rectangles with 1-pixel borders and no gradients, corner radii or panel art. **Verify all five before implementing.**

If verified:

1. Create one `.theme` resource holding the palette, the two shipped fonts, the button styles, the panel styles and the corner radii, and migrate the per-widget overrides onto it. This is the prerequisite for every subsequent UI change costing minutes instead of hours.
2. Give the main menu a backdrop. The cheapest credible option, and the one that reuses shipped work rather than commissioning art, is a live 3D render of the reference scene behind the menu with a slow camera drift and the existing DOF turned up, which the capture harness from LOOK-01 has already proven can be driven programmatically.
3. Raise the sidebar icon size above the current 26 pixels and give the icon buttons real panel treatment. The best-looking asset in this project is art/contact-sheet.png and it is currently displayed at 26 pixels inside untextured grey boxes.
4. Add a loading screen. There is none anywhere in the project.
5. Do not redesign the sidebar's information architecture. Doc 23 section 4.3 already specifies a five-tab restructure and this ticket must not collide with it: theme and art only, layout unchanged.

- Acceptance: the theme resource exists and at least 80 per cent of `AddThemeColorOverride` call sites are gone. Screenshots of the menu, the loading screen and the in-match HUD, judged by Luke and the UX agent. sim golden-hash battery exits 0.

#### V4-04. Author real VFX textures for the combat effects

- Labels: `persona:art-pipeline` `gdd:none` `phase:6` `owner:art-pipeline`
- Impact: MEDIUM | Effort: M | cheapModelSafe: false | touchesSim: false | client-only: yes
- Files: game/scripts/CombatEffects.cs, game/assets/vfx/ (new)
- Spec:

UNVERIFIED in detail: one audit reports that every draw pass in CombatEffects.cs is an untextured primitive, specifically a bare 0.08 `QuadMesh` with a flat albedo for sparks, a `BoxMesh` for rockets, an 8-by-4 `SphereMesh` for howitzer shells, a `CapsuleMesh` for shells, and procedurally generated gradients for smoke and fire, with no authored VFX texture anywhere in the project. This document did not re-read the file. Verify before implementing. Note that five of six lenses listed the effects system in the "already good" column without opening it, which is itself a reason to check.

If verified:

1. Author a small set of tiling and sprite textures: a soft radial spark, a smoke puff with real internal structure, a fire wisp, an impact flash and a muzzle flare. These are procedural-generation candidates and do not require an artist.
2. Replace the flat albedo assignments with textured billboards on the existing particle system. Do not rebuild the particle system, the timings or the spawn logic, all of which are good.
3. Audit the tweens for linear interpolation. The order ring and the muzzle flash use `SetTrans` and `SetEase`; the majority are bare `TweenProperty` calls, which is linear, and linear easing is the animation equivalent of an untextured additive quad.
4. This is the one place where authored detail genuinely reads at RTS distance, because effects are bright, large in screen area and in motion, which is the opposite of the case argued against in section 6.

- Acceptance: contact sheet plus a short capture sequence during combat at CAM-A. No untextured additive quad remains in any draw pass. Frame time during a 40-unit engagement within 0.5 ms of baseline. sim golden-hash battery exits 0.

---

## 5. Performance and contract impact

### The contract, stated once and absolutely

**Every ticket in this document is client-side or art-side. Not one edits /sim.** That is not a happy accident, it is the constraint the whole plan was built inside, and any implementer who finds themselves editing a file under /sim has left the plan and must stop and report.

- **ADR-001, presentation-only client.** All of it holds. The camera, unit visual scale, materials, shaders, environment, bake pipeline, UI theme and VFX are presentation. Nothing here couples to simulation state beyond reading the snapshot the client already reads.
- **The golden hashes must stay byte-identical.** `sim/golden-hashes.txt` must not move for any ticket in this document, and every acceptance criterion above ends with the battery exiting 0. **A hash delta on any of these tickets is a defect, not a golden regeneration.** Changing a golden hash is a replay-compatibility break requiring an ADR and Architect sign-off per CLAUDE.md, and nothing in a visual roadmap can justify one.
- **The 15 Hz snapshot interpolation contract.** The client interpolates 15 Hz sim snapshots into per-frame transforms and drives turrets, wheels and hulls through tweened rigs. Nothing here changes the interpolation. This contract is also the specific reason TAA is refused in section 6: Godot's motion vectors are known-incomplete for exactly this class of interpolated and tweened motion.
- **The 600-unit budget.** TDD s6 targets 600 units and 200 structures at 60 fps on a GTX 1060-class GPU. Every wave below is measured against it.

### Frame time, per wave

- **V0.** Zero runtime cost. The capture harness is not in the shipped build.
- **V1.** Net negative, that is, it should get faster. V1-01 cuts fog density, V1-06 removes SSR at 56 steps. Against that, V1-05 adds FXAA and debanding, both cheap, and 16x anisotropic filtering, which is near-free on modern hardware. V1-03 changes the ambient source, which is not a cost change. **Must be measured, not assumed**, and the V1-06 saving must be recorded because it is the budget that funds V1-05.
- **V2.** Zero runtime cost. Asset content changes only, same texture count, same resolutions, same draw calls. The one-off cost is the Blender bake.
- **V3.** Net negative. V3-01's narrower FOV culls more geometry, and V3-04's visibility ranges remove thousands of MultiMesh instances at the zoom levels where they are invisible anyway. V3-03 is a shader arithmetic change at no cost.
- **V4.** Slightly positive. C-01 is zero cost by construction, per doc 22 line 929. C-11 is one texture fetch in the post pass. V4-04's textured billboards replace flat ones at effectively identical cost. V4-01's team strips add four small `BoxMesh` children per structure, which is a real draw-call increase bounded by the structure count and worth measuring on a full base.

### What must be measured, and against what

The hot path after MAP-01 batched the terrain into MultiMeshes is the shadow pass over those MultiMeshes at a 4096 atlas with soft filter quality 3. Several proposals in the research would have made that worse and none costed it; see section 6. Measurements required:

1. Frame time at 1600x900 at all three reference cameras, before V1 and after each wave. This is the baseline the project has never had.
2. Draw call count at CAM-B on skirmish-04, the 192x128 map, which is the worst case.
3. An AI-versus-AI match run to roughly 400 live units, with frame time sampled, after V1 and again after V4. The 600-unit budget is a TDD number and this document must not be the thing that quietly breaks it.
4. The V1-06 SSR saving and the V3-04 culling saving, recorded as explicit numbers, because they are the headroom that everything else spends.

**The measurement itself is a gap.** There is no frame-time harness in the repository, and no lens produced a single frame time, a draw call count or any GPU-side number. LOOK-01 should emit frame time alongside each capture; if that proves awkward, a separate ticket is warranted before V4, and it must exist before anyone proposes GI again.

---

## 6. What was dropped, and why

This section is not decoration. Several of the most confident proposals in the research would have cost real money and shipped real damage, and three of them were ranked number one by the audit that proposed them.

**Applying sRGB-to-linear conversion to the whole PAL palette. DROPPED, and it is the most dangerous proposal in the pack.** Ranked as "the biggest single win" by the art-direction lens, which computed that the palette is inflated between 1.26 and 6.6 times and prescribed dividing every entry. Two independent sceptics killed it. Doc 22 line 942 already forbids it by name, with the arithmetic: rust would fall 62 per cent in relative luminance and the Sodality faction would vanish against the ground. A sceptic then measured the shipped diffuse atlas and found a median of linear 0.037, which is roughly eight times *darker* than the palette, not brighter, so the proposed correction points the wrong way. It would have consumed the one expensive bake the project had budget for and returned a visibly blacker game. The lens's supporting measurement, that 88 to 98 per cent of pixels sit in a mid band, was taken from Blender turntable renders lit by lineup.py's own studio rig, which say nothing about the game's albedo. Superseded by doc 22's C-05 chroma table, scheduled as V2-04.

**SDFGI. DROPPED.** Ranked number one by the pipeline lens as "nearly free to adopt, roughly six lines", quoting the Godot docs' "start with SDFGI first as it requires the least amount of setup". That sentence is about setup, not cost; the same docs call SDFGI one of the most demanding GI techniques in the engine. Two other lenses and two sceptics opposed it. The substantive objections: the bounce sources are surfaces at linear 0.037 albedo under a sky at 0.015, so there is almost nothing to bounce until V1-03 and V2-02 land, at which point it is probably unnecessary; the terrain is a runtime-built ArrayMesh plus MultiMesh scatter rather than authored static geometry; and the camera pans constantly across maps up to 192x128, which is SDFGI's cascade-rescroll worst case. It would also be stacked on SSAO, SSIL, volumetric fog and a 4096 shadow atlas with no frame-time harness to catch the cost. If GI is ever wanted the correct target is LightmapGI, since the .glb.import files already carry `meshes/light_baking=1` and `lightmap_texel_size=0.2`, but it is not the first move and it is not in this document.

**TAA. DROPPED, and refused on contract grounds rather than taste.** Proposed as a top-five item by the pipeline lens, which explicitly argued against FXAA. The client interpolates 15 Hz snapshots into per-frame transforms and drives turrets, wheels and hulls through tweened rigs, and Godot's TAA motion vectors are documented as incomplete for exactly that class of motion, with open quality issues. The most likely outcome is ghosting on every moving unit and every projectile trail, which makes the game look cheaper in motion, which is the complaint. FXAA at `screen_space_aa=1` gets most of the specular coverage with none of the temporal risk, and it is what V1-05 ships.

**Raising the project-wide anisotropic key as the fix for ground blurring. DROPPED as specified, retained as a different ticket.** Ranked as "probably the best sharpness-per-effort item in the whole list" and justified entirely by the ground, which does not obey it: ground_splat.gdshader lines 2 to 4 declare all three samplers `filter_linear_mipmap`, opting out of the project setting. The lens also misstated the default as 2x and prescribed index 3 while calling it 16x; the default is index 2, which is 4x, and 16x is index 4. Split into V1-04, the shader hint, which is the fix that actually reaches the ground, and V1-05 clause 1 with the correct index, which reaches every `StandardMaterial3D`. Left as originally written it would have been implemented, verified as done, and changed nothing, which is corrosive to trust in the whole list.

**Setting `use_pass_direct` and `use_pass_indirect` to False in bake.py. DROPPED as a verified no-op.** Proposed as the fix for AO contaminating the diffuse. Both are already False, set at bake.py lines 110 to 111 with a comment saying so. The lens's supporting evidence, a 0.992 correlation between the AO channel and diffuse luminance, is a consequence of the line 136 multiply by construction, not an independent finding. The real defect is the `k = 0.85` multiply and V2-02 fixes that instead.

**Deleting the normal maps. DROPPED.** Proposed on the evidence that 46.4 per cent of their pixels are perfectly flat. That is what a model built from flat-panelled boxes should produce, and the non-flat remainder is the Bevel node's edge rounding, which is the only thing softening the low-poly silhouettes. The 950 KB per model it would reclaim is not the asset-size problem; embedded uncompressed textures are, and com_factory.glb is 22 MB because of `gltf/embedded_image_handling=3`. The patches of invalid unnormalised RGB on the track sections are a real defect and should be filed separately as a bake bug rather than used to justify deleting the whole map.

**Authoring greebles, rivets, weld beads, hazard stripes and stencilled unit designations into the normal and albedo bakes. DROPPED, and this is the largest single block of work cut.** Proposed independently by two lenses, one as its item 8 and one as its item 4, the latter describing it as "the real work and there is no shortcut". At the shipped start height a vehicle is roughly 15 pixels across, and 27 pixels after V3-01. Two other lenses independently established that texel-level investment has near-zero return at this camera, and micro-normal detail on an object that small does not read as detail, it reads as shimmer, which is the failure mode V3-03 exists to fix. It is also multi-day node-graph work plus a full re-bake, delivered into a frame that is currently more than half fog. This is precisely the shape of the four waves that already shipped and did not pay off, and doing more of it at higher cost is the least defensible spend available.

**Per-actor `SetSurfaceOverrideMaterial` for rim lighting or detail normals. DROPPED on performance grounds.** Proposed by three separate lenses. Applying an override per actor in `SyncActors` mints a unique material per instance, so at the 600-unit budget that is 600 unique materials and the loss of instancing on the most numerous objects in the frame. If a rim or detail term is ever wanted it must be one shared material per model applied once at load in ModelLibrary, and it is not in this document.

**Visually oversizing units at 1.6x for vehicles and 2.2x for infantry. DROPPED for now, not refused forever.** Presented as risk-free and presentation-only because it does not touch /sim. ADR-001-legal, but not free and not one line. `SkirmishLive.PickEntity` projects the cursor to the ground plane and picks the nearest entity within a hard-coded radius of 0.8 for enemies and 1.1 for ferrite, so 2.2x infantry means the drawn model is roughly three times the click target and the player clicks a visibly-hit unit and misses. It also needs a matching pass over selection rings, team rings, contact blobs, health-bar anchors, decal sizes, muzzle and projectile spawn offsets and the wall-drag ghosts, and the wall system ships chained one-cell segments placed edge to edge that would interpenetrate at any structure scale above 1.0. Do V3-01's FOV change first, measure, and revisit only if units still read too small.

**Raising `DirectionalShadowMaxDistance` from 90 to 200. DROPPED.** It would quarter near-shadow texel density on the same 4096 atlas and multiply shadow-pass geometry, which is the client's known hot path after MAP-01, thereby degrading the near-contact shadows that every lens agreed are the depth cue that survives at RTS distance. Doc 22's MAP-05 already derives shadow distance from camera height with a scaling formula; V3-01 reduces the far edge of the frame instead, which gets the coverage without the cost.

**ReflectionProbes. DROPPED.** Proposed as a follow-on to SDFGI. Godot has open issues on probe energy conservation, double-blending across overlapping probes and specular flicker, and the probe would be sourcing from the same near-black sky that V1-03 exists to fix. Revisit only after V1-03 and V2-01 have landed and only if the contact sheet shows structures still lacking a local specular environment.

**Cutting ferrite_cluster from 23,564 triangles and dropping the bake resolution tiers. DEFERRED, not dropped, and re-filed.** This is a performance and asset-size task wearing a visual costume; it will not change how the game looks. It is real (ferrite_cluster is the heaviest asset in the game and is scattered map-wide, and com_factory.glb is 22 MB from uncompressed embedded textures) and it should be filed as its own performance ticket rather than spending the visual budget.

**Cutting the scatter instance count by 60 to 70 per cent. DROPPED, replaced.** It would directly undo MAP-03, a completed ticket that exists specifically because a 192x128 map read as bare ground. V3-04 range-culls instead, achieving the anti-shimmer goal without fighting a shipped decision.

**Forking faction silhouettes and authoring three distinct material archetypes with insignia and unit numbering. DEFERRED to a future wave.** The problem is real: fourteen of twenty-seven models are shared `com_` assets, so both players' bases are pixel-identical, which is a readability defect as much as a looks defect. But there is no artist, so every version of this resolves to more procedural Blender scripting, which is exactly the effort Luke is saying did not pay off. V4-01's C-07 team strips address the readability half at a fraction of the cost. Revisit only after the whole of V1 through V4 has been judged on a contact sheet.

**Rewriting the terrain from scaled BoxMesh primitives to authored rock and cliff meshes. DEFERRED.** Same reasoning. The batching architecture already supports it, so the door is open, but it is asset work with no artist and it is capped by a finding no lens reached: doc 22 records that skirmish-01 and skirmish-02 contain zero water, hills, ruins, fences or bridges, and that the three missions are 99.0, 98.2 and 94.8 per cent bare open ground. Authoring better cliffs for maps that contain no cliffs is not the first move. The map-content work is doc 22's and it belongs there.

---

## Changed / Assumed / Needed next

**Changed.** Nothing in the repository. This document was produced entirely read-only, per the working constraint that another agent holds a branch in the tree. No file inside /Users/lgreene/project-ferrostorm was created, modified or deleted, no git state-changing command was run, and neither the Godot client nor the sim battery was executed. This file is written to the scratchpad for later copying into docs/design/ as 25-visual-overhaul-roadmap.md.

**Assumed.**

1. That doc 22 remains ratified and its Wave C tickets stand as written. This document schedules them rather than restating them, and folds two additional bake fixes into C-05's session. If doc 22 is superseded, V1-02, V2-04, V4-01 and V4-02 lose their specifications.
2. That the sceptic measurement of the shipped diffuse atlas, median linear 0.037 with 5th, 50th and 95th percentiles of 18, 52 and 141 in sRGB bytes, is correct. It was taken independently on the extracted maps and it contradicts one lens directly. It is load-bearing for the palette drop in section 6 and for V1-07's instruction not to darken. Re-measure once before V2 runs.
3. That `ground_splat.gdshader`'s `source_color` uniforms are converted from sRGB to linear by Godot 4.7. This was verified as present in the shader but the conversion behaviour was not measured, and doc 22's C-02 STEP 0 mandates measuring it and calls it the single most likely cause of the whole frame being dark. Every ground luminance figure quoted anywhere in the research is unverified until that measurement exists. **Do it as part of LOOK-01.**
4. That V4-03 and V4-04's descriptions of MainMenu.cs, Sidebar.cs and CombatEffects.cs are accurate. They are marked UNVERIFIED in place and both tickets open with an instruction to verify before implementing.
5. That the client's rendering path is what the player sees. Note the sceptic finding that game/scenes/Battle3D.tscn carries its own Sun and Fill while its Theater node runs `ReplayTheater`, which calls `BuildLightRig` and adds three more directionals, so the replay path renders under five directional lights totalling 4.55 energy. Skirmish.tscn is script-only and the live game is unaffected, but **any capture taken through the replay path is not the game**, and LOOK-01 must capture through the skirmish path.

**Needed next, and from whom.**

- **From Luke, and it is the cheapest thing on this list.** Sign the doc 16 amendment at doc 22 section 5. It has been waiting since 15 July, it blocks C-07 and therefore V4-01, and the one-place team-colour law it amends is already broken in shipped code without anyone writing it down. Also: fund V0 before anything else, and be available to judge one contact sheet at CAM-C, because the look-development loop only works if somebody with taste looks at the image.
- **From the Producer.** Rule on why Wave C was queue-jumped by four gameplay waves, and whether this document's V0 through V2 now take priority over the remaining gameplay work. Correct the three false ledger entries per LOOK-03.
- **From the Game Designer.** V3-01's FOV change is a gameplay-visible change to how much battlefield the player can see, on maps whose ferrite spacing doc 22 tuned by measurement. Judge it at a capture, with the Producer, before it lands.
- **From the Architect.** Confirm that no ticket in this document requires a golden regeneration, which is this document's own claim and should be checked rather than accepted. Rule on whether a frame-time harness is a prerequisite for wave V4, given that no GPU-side number exists anywhere in the project today.
- **From the Balance agent.** Nothing. This document contains no gameplay-number change, which is deliberate.
