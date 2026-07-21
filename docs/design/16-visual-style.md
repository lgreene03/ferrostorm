# Ferrostorm Visual Style Guide

## The world's materials decide the palette
Cooled cinder (#16181a) is the ground of every screen; plating (#232629) and
seams (#2e3236) build the interface from the same foundry. Ferrite gold
(#c9a86a, highlight #e0c288) is the light of this world - the resource, the
instrument trim, the thing everyone is fighting over. Text is unbleached
document bone (#d6d2c4).

## Two visual languages, one law
DIRECTORATE - the wall: slab-sided, symmetric, issued. Gunmetal #5b6770,
plate #78848c, shadow #3d454b. Team mark: signal orange #e8762c.
SODALITY - the shadow: angular, asymmetric, welded-from-salvage. Rust
#8a4a34, plate #a35c40, shadow #5c3122. Team mark: corroded teal #4fb8a8.
COMMON hardware: field olive #6e6a5e with ferrite-gold marks.
The law: team colour appears in exactly one place per silhouette (the
band/slash), always. Silhouette-first: every unit must be identifiable as a
40-pixel blob - the bulwark is a slab, the phantom a faceted wedge, the
harvester a fat beetle with a gold hopper, squads are dot-clusters.

## Reference implementations
art/sprites/*.svg (20 sprites, rendered PNGs in art/png/, contact sheet at
art/contact-sheet.png) and the match viewer (tools/viewer/) share one shape
language; the viewer's signature is the ferrite-seam glow draining with the
deposit. Blender/contractor work later should treat these as the style bible:
stylised low-poly, silhouettes and team-colour law as above.
