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

*(Delivery notes follow below, written as each part landed.)*
