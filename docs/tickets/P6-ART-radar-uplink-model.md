# TICKET-P6-ART-01 - the Radar Uplink's own model and icon

labels: persona:commander gdd:s7-86 phase:6 owner:art-pipeline

Raised by Wave B3 (ADR-008 clause 4), which made struct type 12 buildable.
No com_radar_uplink.glb exists, so ModelLibrary maps kind 13 to
sod_veil_projector.glb, the interim doc 23 s4.2 prescribes: its `dish`
child already spins under ScanRig, which reads as a scanning antenna for
free. The sidebar button also has no com_radar_uplink.png icon; MakeButton's
Exists guard tolerates the absence and flips on its own the moment the
sprite lands in ui/icons/.

Owed: a bespoke com_radar_uplink.glb through the Blender asset pipeline
(art/3d/builder.py conventions, including the orientation contract), with a
`dish` or equivalent rotating child so the ScanRig idiom keeps working, and
the matching sidebar icon. The visual should read as the eye of the base
(GDD s7 line 86 puts the radar minimap above the sidebar; this building is
its diegetic anchor) and must stay visually distinct from the Sodality veil
projector it currently borrows, which is a faction-signature building.

Acceptance: com_radar_uplink.glb and com_radar_uplink.png exist; the
ModelLibrary interim mapping is replaced with the real name; a live
skirmish shows the uplink distinct from a veil projector at a glance; the
existing offline dim wash still reads on the new meshes.

**Changed:** nothing yet; this is the owed-work record.
**Assumed:** the veil projector stand-in is acceptable until this lands
(doc 23's own recommendation).
**Needed next (from art-pipeline):** the model and icon as above.
