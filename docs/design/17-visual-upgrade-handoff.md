# Visual Upgrade Handoff (priority 2)

Written 2026-07-12, end of the Godot-bring-up session, for whichever model
picks this up next (possibly a smaller/cheaper one - this document is
written to be followed mechanically, with exact commands and file paths, so
no judgement calls should be required beyond the creative modelling work
itself).

## Read this first: two files in the original plan do not exist

The task brief that started this session referenced `art/3d/hero.py` (a
detailed cannon tank study) and `art/3d/materials2.py` (procedural weathered
PBR materials) as already-written, mid-flight work. **They are not in the
repository.** They were created in a previous session's sandboxed container
under `/home/claude/...` and were never committed - the container's
filesystem did not persist. Do not search for them; write them from
scratch, using the guidance below. This also means the "render timed out"
note from the old brief is not a real signal about difficulty or expected
duration - that timeout was a container resource limit, not anything about
the actual render cost (see the Environment section: a comparable render on
this Mac took 2.4 seconds).

Two other files in `art/3d/` DO exist and DO work, but hardcode paths from
the old container and need a one-line fix before use:
- `art/3d/export_glb.py` line 2: `sys.path.insert(0, '/home/claude/b3d')`
  and line 5: `out = '/home/claude/project-ferrostorm/game/assets/models/'`
- `art/3d/battle-scene.py` line 3: `sys.path.insert(0, '/home/claude/b3d')`
  and line 5: `R = json.load(open('/tmp/ferrostorm-replay.json'))`

Fix by pointing at the real repo location, e.g. on this Mac:
`sys.path.insert(0, '/Users/lgreene/project-ferrostorm/art/3d')` and
`out = '/Users/lgreene/project-ferrostorm/game/assets/models/'`. Adjust
`/tmp/ferrostorm-replay.json` to wherever you export a replay to (see
Environment section for the export command).

## Environment (verified working this session, 2026-07-12)

- Blender is installed at `/Applications/Blender.app`, CLI symlinked at
  `/opt/homebrew/bin/blender`. Installed via `brew install --cask blender`
  with no sudo prompt (unlike the .NET SDK cask, which does need sudo and
  should be avoided - see game/README-GODOT.md for that story). Confirmed
  version: **Blender 5.1.2**, not the 4.x the original plan assumed.
- Headless invocation: `blender -b -P script.py`. Verified end to end this
  session: wrote a 20-line smoke-test script that imports
  `art/3d/builder.py`, calls `builder.scene_setup()`, builds one unit
  (`dir_cannon_tank`), and renders a 400x300 PNG. Total time: **2.4
  seconds**, no errors, no timeout. `builder.py` is fully compatible with
  Blender 5.1.2 as committed - no API changes needed (the AgX view
  transform name and `use_denoising` property the original plan flagged as
  version-sensitive both still work as documented).
- `.NET 8 SDK` is at `~/.dotnet` (user-local install, not the Homebrew
  cask - that one needs sudo and will hang non-interactively). Every shell
  in this session exports `PATH="$HOME/.dotnet:$PATH"` and
  `DOTNET_ROOT="$HOME/.dotnet"` before any `dotnet` command; do the same or
  add it to `~/.zshrc` (already done - check `tail ~/.zshrc` before
  re-adding).
- Godot 4.7 .NET is at `~/Applications/Godot_mono.app`. Not needed for this
  ticket except at the very end (step (c) below re-exports the .glb files,
  which then need to reimport cleanly in Godot - see the Bring-up
  Validated section of game/README-GODOT.md for known gotchas: headless
  `--audio-driver Dummy` to dodge a CoreAudio hang, `open -a` instead of a
  backgrounded shell to dodge App Nap freezing the render loop).
- To export a fresh replay for `battle-scene.py` to render from:
  `cd ~/project-ferrostorm && dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- export 2026 /tmp/ferrostorm-replay.json`
  (or wherever you point the script's `open()` call).

## Status update (2026-07-12, late session): part (a) done, builder.py fixed for Blender 5

Part (a) is complete: `art/3d/hero.py` and `art/3d/materials2.py` now exist
(written from scratch as instructed below) and render a side-by-side
blockout-vs-hero comparison to `art/3d/hero.png`. The hero cannon tank keeps
the blockout silhouette and adds exposed road wheels, track skirts, a
mantlet/sleeve/muzzle-brake gun assembly, commander hatch, antenna, panel
inserts, tow hooks, and procedural weathering. Iterate further if taste
demands, but the beats-the-blockout bar is met.

**Three Blender 5.x incompatibilities were found in the committed builder.py
and fixed this session. Without these fixes every model in the roster
renders exploded/disassembled under Blender 5.x, even though the same code
worked under the container's Blender 4.0:**

1. **Stale matrices at join.** Headless Blender 5 defers depsgraph
   evaluation: `rotation_euler` set after object creation is not yet in
   `matrix_world` when `bpy.ops.object.join()` bakes vertices, so every
   rotated part (gun barrels, wheels) joined unrotated. Fix:
   `bpy.context.view_layer.update()` at the top of `builder.join()`.
2. **`transform_apply` argument defaults.** `builder.box()` called
   `transform_apply(scale=True)`, but the unspecified `location`/`rotation`
   parameters DEFAULT TO TRUE, so it also reset each part's origin to world
   zero, making any post-creation rotation pivot around the world origin
   instead of the part itself. Fix: pass all three flags explicitly. A
   compensating `transform_apply(all)` was added after join so joined
   objects keep identity transforms (downstream code - battle-scene
   instancing, placement via `.location` - relies on origin-at-world-zero).
3. **Box size semantics.** `primitive_cube_add(size=1)` yields verts at
   ±0.5 under Blender 5, and `box()` scaled by `sx/2`, producing boxes at
   HALF their authored size while part locations stayed full-scale - every
   multi-part model exploded into separated pieces. Fix: scale by the full
   `(sx, sy, sz)`. The authored dimensions in every unit function only make
   sense at full size (e.g. hull 0.62 wide with tracks at ±0.36).

**Consequence for step (c):** the .glb files committed in
game/assets/models/ were exported under Blender 4.0 and are geometrically
correct. Re-exporting them with builder.py WITHOUT the three fixes above
would have shipped exploded models into the Godot client. With the fixes
they should re-export correctly, but verify one model visually in the
Godot editor (or via the offscreen-capture trick in game/README-GODOT.md)
before re-exporting all 20.

**Debugging tip that found all three bugs:** don't trust the rendered image
alone - add a probe that prints world-space vertex extents
(`matrix_world @ v.co` min/max per axis) for the joined object and compare
against the authored dimensions. hero.py has such probes (the `PROBE`
prints) - keep them; they cost nothing and catch regressions instantly.
Also beware: a camera looking straight down the barrels' axis made a
correctly-assembled tank look broken for two iterations - check the camera
before diagnosing geometry.

## The four-part plan, as originally scoped

(a) Hero asset: a detailed `dir_cannon_tank` study in Blender, iterated
    until it convincingly beats the current blockout (see
    `art/3d/asset-lineup.png` for the blockout baseline - gunmetal slabs,
    simple primitives, no surface detail). Render to `/tmp/hero.png` (or
    anywhere; the container path was a container-only convention),
    inspect, iterate.
(b) Apply the same detail level plus procedural weathering materials
    (`materials2.py`, to be written) across the full 20-model roster in
    `art/3d/builder.py`.
(c) BAKE the procedural materials to image textures. This step is not
    optional: glTF/.glb (the export format Godot consumes) cannot express
    Blender's procedural shader node graphs - only baked image textures
    survive the export. Use UV smart project, bake diffuse and roughness
    at approximately 1024px, then re-export every .glb via
    `art/3d/export_glb.py` (after the path fix above).
(d) Re-render the reference battle scene (`art/3d/battle-scene.py`, after
    the path fix above) with atmosphere: world mist or distance fog, plus
    a compositor glare pass on emissive surfaces (the ferrite clusters and
    ferrite-gold trim already use emission shaders in `builder.py`'s
    `mat()` helper - glare should pick them up for free once enabled).

## Concrete starting point for (a): what to reuse from builder.py

Do not start from a blank Blender file. `art/3d/builder.py` already has:
- `PAL` - the exact faction palette as RGBA tuples, matching
  `docs/design/16-visual-style.md` (Directorate gunmetal #5b6770/plate
  #78848c/orange #e8762c team mark; Sodality rust #8a4a34/plate
  #a35c40/teal #4fb8a8 team mark; common olive #6e6a5e; ferrite-gold
  #c9a86a/#e0c288). Reuse this dict; do not eyeball new colours.
- `mat(name, emit=0.0, rough=0.7, metal=0.15)` - creates/caches a
  Principled BSDF material from the palette. `materials2.py` should
  extend this, not replace it: add procedural weathering (noise-driven
  roughness/colour variation, edge wear via pointiness or bevel-based
  AO) as additional node groups feeding the same Base Color/Roughness
  inputs, keeping the function signature-compatible so `builder.py`'s
  existing calls keep working during the transition.
- `box()`, `cyl()`, `wedge()`, `join()` - the low-poly primitive builders
  every unit function composes. The hero cannon tank should still use
  these as its blockout pass, then add greebles/panel lines/surface
  breakup on top - do not reinvent primitive placement.
- `dir_cannon_tank()` (line ~71) - the existing blockout function to beat.
  Current geometry: a beveled hull box, two side-skirt boxes, a turret
  box, a cylindrical barrel, one orange team-colour band. Read this
  function first; the hero version is this same silhouette with detail
  added, not a redesign (silhouette-first is the design law in doc 16 -
  changing the recognisable shape is out of scope for this ticket).
- `scene_setup()` (line ~248) - lighting rig (two suns), world background,
  Cycles CPU render settings (72 samples, denoising off, AgX Medium High
  Contrast view transform). Call this before building/rendering anything;
  it also clears the scene, so call it once per script run, not per model.

## Definition of done

- `/tmp/hero.png` (or equivalent) shows a detailed cannon tank that is
  visibly more finished than `art/3d/asset-lineup.png`'s blockout version,
  while keeping the same recognisable silhouette (doc 16's silhouette-first
  law is non-negotiable - do not redesign the shape, only add surface
  detail and weathering).
- All 20 models in `builder.py`'s `BUILDERS` dict have been brought to the
  same detail/weathering level and re-exported as .glb via
  `export_glb.py` (path-fixed).
- Materials are baked to image textures before export, not left as
  procedural node graphs (verify by opening one exported .glb in a plain
  glTF viewer, or reimporting in Godot per game/README-GODOT.md, and
  confirming the surface detail survived - if it imports flat/grey, the
  bake step was skipped or failed).
- `art/3d/battle-scene.py` (path-fixed) re-rendered with fog/mist and
  emissive glare enabled, producing a new reference frame comparable to
  the existing `art/3d/battle-first-light.png` but visibly upgraded.
- Update `game/README-GODOT.md` and this doc's own ledger entry (see
  Working Conventions in the root `CLAUDE.md`: every session ends with a
  new entry in `docs/tickets/phase-1-backlog.md` and a new appendix in
  `docs/design/13-phase1-gate-review.md`) describing what changed and any
  new gotchas found, the same way session 18's entries describe this
  session's Godot bring-up and the Windows CI newline bug.

## Process notes from this session, likely to transfer

- Prefer running slow/headless render or build commands with
  `run_in_background: true` and poll for the output file rather than a
  blocking foreground call with a long timeout - this session used that
  pattern repeatedly (Godot capture screenshots, CI polling) and it kept
  each turn fast even when the underlying operation took 10-100+ seconds.
- When a headless GUI-adjacent process seems stuck at 0% CPU progress,
  suspect either macOS App Nap (unfocused window - relaunch via `open -a`)
  or a permission-prompt-shaped hang with no visible dialog (this session
  hit exactly this with Godot's CoreAudio init - fixed by
  `--audio-driver Dummy`). Blender's `-b` background mode has no window at
  all, so this class of problem is less likely here, but if a render hangs
  indefinitely with zero CPU growth, check `sample <pid> 2` (macOS tool)
  to see the actual stuck call stack rather than guessing.
- Before trusting any cross-platform CI gate, check what OS it failed on
  and whether earlier steps in the same job passed - this session's
  Windows CI failure looked like a determinism regression at first glance
  but was actually a `Console.WriteLine`/`Environment.NewLine` (\r\n on
  Windows) formatting bug in the Runner's `Golden()` method, not a real
  divergence; build, selftest, and double-run determinism all passed on
  Windows before the byte-diff step failed. Fixed at
  `sim/Ferrostorm.Sim.Runner/Program.cs`'s `Golden()` by forcing
  `Console.Out.NewLine = "\n"`. If any *new* CI step compares generated
  text output byte-for-byte against a committed file, apply the same fix
  proactively rather than waiting for Windows CI to catch it.
