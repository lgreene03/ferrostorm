# TICKET-P6-ART-02 - the Barracks' own model and icon

labels: persona:commander gdd:s5-45 gdd:s7-86 phase:6 owner:art-pipeline

Raised by Wave B4 (ADR-009 clause 5), which made struct type 11 buildable and
turned it into one of the game's two unit producers. No com_barracks.glb
exists, so ModelLibrary maps kind 12 to com_service_depot.glb as the interim,
following the pattern TICKET-P6-ART-01 set for the radar. The depot was chosen
over the factory deliberately: the barracks split only pays off if a player can
tell the two PRODUCERS apart at a glance, and borrowing the factory's
silhouette would undo the readability the whole wave exists to create. The
sidebar button also has no com_barracks.png icon; MakeButton's Exists guard
tolerates the absence and flips on its own the moment the sprite lands in
ui/icons/.

Owed: a bespoke com_barracks.glb through the Blender asset pipeline
(art/3d/builder.py conventions, including the orientation contract), and the
matching sidebar icon. The building should read as infantry production at a
glance and from the top-down RTS camera in particular: a door or bay the
squads visibly come out of would also give the client's existing door-tween
rig (the one the factory already drives on ProductionComplete) something
honest to animate, which is free legibility. It must stay visually distinct
from the Service Depot it currently borrows AND from the Factory it stands
beside in every base.

Acceptance: com_barracks.glb and com_barracks.png exist; the ModelLibrary
interim mapping is replaced with the real name; a live skirmish shows the
barracks distinct from both the depot and the factory at a glance; if the
model carries named door children, the existing ActorRig door tween picks
them up with no client change.

**Changed:** nothing yet; this is the owed-work record.
**Assumed:** the service depot stand-in is acceptable until this lands, and
that "distinct from the factory" matters more than "distinct from the depot"
if the two goals ever conflict, because the factory is the building a player
must not confuse it with mid-match.
**Needed next (from art-pipeline):** the model and icon as above.
