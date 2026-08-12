# DEFECT: nine shared meshes wore the Directorate's team colour

Labels: `persona:art-pipeline` `gdd:doc16` `phase:P7` `owner:art-pipeline`
Found: 2026-08-12, auditing what remained after P7 closed
Severity: **high** (faction read inverted for one side, on nine of the things
they build most)
Confidence: **measured**, derived from the source and then from the exported
glTF bytes

> **RESOLVED 2026-08-12** in the same wave it was found. Nine meshes re-baked,
> verified at the artefact level, and the rule is now enforced at `join()`
> rather than written in a comment.

## The defect

Doc 16 line 15 states the law: *"COMMON hardware: field olive with ferrite-gold
marks."* A `com_` mesh is shared hardware, one mesh serving BOTH players, so its
team band cannot carry a faction's colour. `art/3d/builder.py` said so itself,
in a comment with the argument spelled out:

> Signal orange on a mesh both factions place would paint a Directorate stripe
> on a Sodality wall.

And then nine `com_` builders passed `'orange'` anyway:

| mesh | what a Sodality player was building |
|---|---|
| `com_barracks` | their barracks, in Directorate orange |
| `com_radar_uplink` | their radar |
| `com_emplacement` | their emplacement |
| `com_gate` | their gate |
| `com_airfield` | their airfield |
| `com_repair_vehicle` | their repair vehicle |
| `com_carrier` | their carrier |
| `com_flak_track` | their flak track |
| `com_strike_flyer` | their strike flyer |

`com_harvester` was the single one that obeyed the rule, which is why the
inconsistency survived: the correct case existed, so the rule looked kept.

The asymmetry is what makes it high severity rather than cosmetic. The
Directorate player sees their own colour everywhere and notices nothing. The
Sodality player never sees theirs on any of these nine, and the one place doc 16
reserves for team identity is telling them the wrong side owns it.

## The shape, which is the familiar one

**A rule that was written down and then not enforced.** This is the same class
P7 found around seventeen times in the sim, appearing here in the art pipeline:
the constant existed (`BARRIER_MARK`), the reasoning was recorded next to it,
and nothing checked that anybody used it. A comment is not a check.

## The fix

Not nine edits. Nine edits would leave the tenth to be authored wrong later.

1. `COMMON_MARK` names the rule for ALL shared hardware, not just barriers.
   `BARRIER_MARK` is kept as an alias so doc 22 and `wall-yaw-gate.py` still
   read in their own terms, but it is no longer a separate decision.
2. `team_band()` tags the part with the colour it was asked for.
3. **`assert_common_band()` refuses a faction mark on a `com_` mesh at
   `join()`**, the one funnel every model is assembled through. So the rule is
   enforced on the mesh actually being built, not on anyone re-reading the file.

Bite-tested, with controls, because a guard nobody proved is a guard nobody has:

| stage | expectation | result |
|---|---|---|
| 1 | `com_` mesh + orange band is REFUSED | bit correctly |
| 2 | `com_` mesh + teal band is REFUSED (the rule is about faction marks, not about orange) | bit correctly |
| 3 | control: `com_` mesh + ferrite is ACCEPTED | passed |
| 4 | control: `dir_` mesh KEEPS its orange, so the rule does not overreach | passed |

All 48 builders build with zero refusals.

## Measurement

Nine `.glb` files re-baked and all nine changed; no tenth file changed. Verified
by parsing the exported glTF material `baseColorFactor` values rather than by
re-reading the source: **zero `com_` meshes now carry a faction colour**, and all
nine carry ferrite. Structure is unchanged (same material counts, 0 textures
before and after, byte size moves by 4 bytes, consistent with a palette name
changing length).

Nothing in `/sim` or `/data` is touched, so no golden hash and no catalogue
checksum moves. This is art only.

## Found on the way, NOT fixed, and not caused by this change

- **`art/3d/export_glb.py` could not run on this machine at all.** It carried two
  absolute `/home/claude/...` paths from the container it was first written in,
  for both its import path and its output directory. Fixed here, because the
  re-bake needed it: paths now derive from `__file__`, it takes model names so a
  partial re-bake is possible, and it refuses an unknown name rather than
  silently exporting nothing.
- **The roster is in TWO pipeline states.** `com_harvester` and the wall variants
  are texture-baked (`*_baked` materials, `baseColorTexture`, 3 to 7 images);
  these nine and others are flat-material exports with no textures. That
  predates this change and is why the full roster was deliberately NOT
  re-exported: a blanket `export_glb.py` run would overwrite the baked meshes
  with flat ones. **Anyone re-baking must pass explicit model names until that
  is reconciled.** Worth its own wave.
- Infantry squads (`com_rifle_squad`, `com_rocket_squad`) carry no team band at
  all, against doc 31 lines 99 and 107. Not this defect, and it needs a design
  call about where the mark goes on a squad rather than a colour correction.
