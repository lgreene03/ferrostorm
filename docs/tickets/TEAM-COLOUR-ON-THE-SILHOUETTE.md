# Team colour reaches the model: doc 16's law, finally applied

Labels: `persona:client-engineer` `gdd:doc16` `phase:P7` `owner:client-engineer`
Date: 2026-08-13
Status: **DONE for 16 of 48 models, and the 32 remaining are MEASURED, not
forgotten.** The client half is complete and needs no further change; what is
owed is an art-pipeline change, recorded below with exactly what unblocks it.

## The requirement, which is not new

Doc 16: *"team colour appears in exactly one place per silhouette (the
band/slash), always."*

It had never been implemented. The client rendered team colour **only on the
ground** - a ring under mobiles (`DressMobile`) and a square strip under
structures (`DressStructure`) - and every model's band was baked in a fixed
FACTION colour. So two seats of the same side were pixel-identical, and the one
place doc 16 reserves for team identity said nothing about who owned the thing.

Doc 31 s1 flagged this as a discrepancy and recorded the useful part: doc 30
arrived at "move team colour onto the model" from research, but **doc 16 had
required it all along**. This is an unbuilt requirement, not a new idea.

## What was built

`BattlefieldView.TintTeamBand(node, player)`, called from both `DressMobile` and
`DressStructure` - the two functions every entity already passes through, so
there is no third path to keep in step.

The band is **its own surface**, because Blender's `join()` gives every material
its own primitive. So the tint is a surface override: no shader, no mask, no
MultiMesh, no per-frame cost. The original material is **duplicated** rather than
replaced, so roughness, metallic and emission energy survive and only the two
colour channels move; building a fresh `StandardMaterial3D` would have quietly
flattened the surface response of every band in the game.

**Doc 30 s6 recommended `INSTANCE_CUSTOM`, and it was answering a different
question.** That advice assumes units are drawn through a MultiMesh. They are
not: MultiMesh in this client is terrain scatter (rubble, tufts, grass), and
units and buildings are per-entity nodes. With per-entity nodes a surface
override is both simpler and cheaper than packing a team index into four floats
and indexing a uniform array. The doc's advice was right for the architecture it
assumed; the measurement decided the design, as it has all phase.

### Identifying the band

By material name, which is **derived rather than decorative**: `builder.py`'s
`mat()` names every material `f"{name}_{emit}"`, and `team_band()` is the only
thing in the roster that emits at **1.2**. Measured across all 48 exported
models: **exactly 16 carry one match, and none carries two.** That is what makes
the name safe to key on.

The suffix is **parsed numerically, not string-matched**, because Godot's
importer is free to sanitise a resource name and `ferrite_1.2` arriving as
`ferrite_1_2` would silently match nothing. The failure mode of a silent
mismatch is a feature that is simply absent, which no crash announces. The
numeric compare is also what separates a band from `com_mine`'s `ferrite_0.9`
glow, which is not a team mark.

## Proved, control first

Client harness (`VerifyRunner`), which drives the real scene from seat 1:

| check | why it is there |
|---|---|
| control: an UNDRESSED model wears its authored colour | otherwise the rest asserts only that something was written |
| control: a texture-baked model has no band, and tinting is a no-op | the 32 without a band must not crash the draw path |
| seat 0's band is seat 0's colour | the feature |
| seat 1's band is seat 1's colour, same mesh | the feature, from the other seat |
| **the two differ** | the whole of doc 16's law in one line |
| a faction-exclusive model takes its OWNER's colour, not its faction's | the case a rule written as "recolour the common ones" would leave behind |
| a neutral model keeps its authored band | an outpost or bridge owns no side and must not be painted into one |

## The measurement that decided the scope

The roster is in **two pipeline states**, which the previous wave found and this
one had to plan around:

| state | count | can the band recolour? |
|---|---|---|
| flat export, one band surface | **16** | **yes, and does now** |
| texture-baked (`*_baked`, band is pixels in a `baseColorTexture`) | **27** | no - no surface override can reach it |
| flat, no band authored at all | 5 | nothing to recolour |

**Nothing regresses for the 27.** The ground ring and strip are untouched and
still carry team identity for every entity, exactly as before; the 16 gain the
silhouette band *in addition*. No player loses a mark, and no seat becomes
ambiguous.

## What is owed, and what unblocks it

`bake.py` folds every material into one atlas, so a baked model's band is pixels.
The fix is to leave the band on its own material slot, unbaked, so the exported
mesh keeps a band surface. **The client needs no change when that lands** - the
remaining 27 light up on their next export, because `TintTeamBand` keys on the
surface, not on a list of model names.

Deliberately NOT attempted here:

- **Re-baking 27 shipped models blind.** They were baked under a different
  Blender than the 5.1.2 on this machine, and `builder.py` itself records a 5.x
  change that "exploded every model" once. Re-baking them is a visual change that
  cannot be verified without seeing it, and the look-dev capture path needs an
  actively viewed session. That is a wave with a human in it.
- **Colour-keying the baked texture in a shader.** It would tint the lamps too
  (`glow_1.1` is emissive and is not a team mark), so it trades a missing feature
  for a wrong one.
