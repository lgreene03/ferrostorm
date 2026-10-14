# Sidebar icons: the two owed Sodality sprites, and three placeholders pointing at nothing

Labels: `persona:art-pipeline` `persona:client-engineer` `gdd:doc16` `phase:P7`
Date: 2026-08-13
Status: **DONE for what could be cut from existing art.** What remains needs a
2D sprite drawn, which is an art task and not an implementer's.

## How this icon set is actually made, which decided the whole wave

The icons are **not** renders of the `.glb` models. They are `sips -z 64 64`
downscales of the 96x96 stylised 2D sprites in `art/png/`. TICKET-P5-ALERT-02
recorded the method and the reason it was trusted: it reproduced the committed
`com_factory.png` **pixel-identically**, and *"a 3D render of the .glb would have
matched nothing, the set is stylised 2D sprites"*.

Doc 25 line 203 is why that matters rather than being trivia: *"the 2D icon set
is the best-looking asset in the repository ... they are the proof that the art
direction is sound and the pipeline is what breaks it."*

**The method was re-verified before use, the same way**: copying
`art/png/com_factory.png` and running `sips -z 64 64` reproduced the committed
icon with an **identical SHA-256**. So the two new icons are cut by a process
proved against a known-good output rather than by eye.

That is also why this wave does NOT close the icon gap: **17 icons against 42
build items is mostly a missing-SOURCE-ART problem, not a missing-step problem.**
Only 20 source sprites exist, and cutting an icon for something with no sprite
would mean inventing 2D art in a style doc 25 calls the best thing in the repo.

## 1. The two owed Sodality unit icons

`sod_phantom_tank` and `sod_shade_raider` had source sprites and no icon. They
have been owed since P6-wave-a, whose own notes list *"Sodality unit icons owed
to art-pipeline"*.

Cut to 64x64 RGBA, matching the set exactly. **No code change was needed**:
`MakeButton`'s `ResourceLoader.Exists` guard flips on its own, and
`PlaceholderIcon` already fell through to the id.

The effect is small and real: a Sodality player's factory page had two blank
buttons where a Directorate player's had none.

## 2. Three placeholder entries pointed at art that has never existed

`PlaceholderIcon` mapped `com_wall`, `com_mine` and `com_gate` to
**`com_wall_straight`**, and there is no `com_wall_straight.png` - nor any wall
sprite in `art/png` at all.

All three were **inert**, and provably so: the `Exists` guard failed on the
target exactly as it failed on the id, so a player saw the same iconless button
either way. Verified before removing them, for all four names:

| name | icon | source sprite |
|---|---|---|
| `com_wall_straight` | no | no |
| `com_wall` | no | no |
| `com_mine` | no | no |
| `com_gate` | no | no |

So removing them changes nothing on screen and stops the map claiming to supply
art it does not have.

**Why they survived: the symptom of a broken workaround is identical to the
symptom of no workaround.** A button with no icon looks the same whether the
placeholder is missing, wrong, or absent, so nothing could ever have told anyone.

## 3. The count in the comment was wrong, in the familiar way

The doc comment read *"all six are owed to art-pipeline"* while the switch held
**seven** entries. A hand-maintained count lagging the list it describes, sitting
inside the comment that documents the workaround - the same shape P7 found around
seventeen times, and the same shape the previous art wave found in `builder.py`.

The count is no longer written down. `Sidebar.PlaceholderIds` is the one list.

## The guard, so the class is closed

The client harness now derives from `PlaceholderIds` and asserts, for every
entry, that it **re-points** and that its target **resolves to art that exists**.
A fifth mapping pointing at a sprite nobody cut fails the harness instead of
quietly producing a blank button. Plus a check that the two new icons exist.

## Still owed, and it needs an artist rather than an implementer

Build items with no icon and no source sprite: the barracks, radar uplink,
airfield, emplacement, bastion, shroud nest, gate, mine, walls, carrier, flak
track, strike flyer, both commandos, the infiltrator, the saboteur, the
generator, the seismic charge and the watch post. Four of those wear a
placeholder today; the rest show a bare button.

Doc 25 line 613 also asks for the icons to be shown larger than the current 26
pixels, which is a UI change and independent of cutting more of them.
