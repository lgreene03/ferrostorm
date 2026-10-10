# 19. Audio specification

Status: the unit acknowledgement section below is written and shipped (P8-41).
The rest of the game's audio is specified where it is built rather than here:
the announcer channel, the positional pool and the per-weapon reports in
`game/scripts/AudioDirector.cs`, `Announcer.cs` and `WeaponSounds.cs` with
P8-44's tracker row, the interim score in `art/audio/synth.py` with P8-46's,
and every sound effect's recipe in the docstring of its generator in
`synth.py`. This document collects sections as they are written; a later pass
may fold those in.

## Unit acknowledgements (barks)

### Why

A selection and an order were answered by a UI click and a short blip, the
same for a rifle squad as for a tank, so nothing told a player which units had
taken the click, and a click that landed on the wrong group sounded exactly
like one that landed on the right one (FEEL-01). A bark is the unit answering:
it says that the units the player meant heard the order, in a voice that says
what kind of units they are.

### Who barks

Only the local player's own units. `SkirmishLive.BarkFor` refuses any unit
whose owner is not `LocalPlayerId` before it asks the audio channel for
anything. An enemy that answered a click would be a maphack in sound, and in a
LAN match each peer's units answer on that peer's machine only. Buildings never
bark; a selected building keeps its click. A seat is never written as a
literal anywhere in this path.

### When

A bark answers one of three things. A selection is answered when a click picks
one of my units, when a drag catches any, when a double click or Ctrl click
selects every unit of a type on screen, when the army key, the idle harvester
key or a control group recalls a selection. A move is answered when a right
click sends units somewhere (a move, a harvest, a boarding) and when the repair
key sends damaged units to a depot. An attack is answered when a right click
orders an attack, a force attack or a contact unit's walk in, and when an
attack move or a patrol is committed. The stop, guard, hold fire, deploy and
unload keys keep their own acknowledgements and do not bark, because none of
them sends a unit somewhere new. An order that issued nothing is not answered,
the rule the order marker and its blip already follow.

### Which unit speaks for a group

One unit answers for a whole selection or order, never several at once: the
lowest numbered unit of the class the group holds most of, ties going to
infantry and then to vehicles, so ten tanks and a rifle squad answer as tanks.
For a click or a drag the group is what that gesture caught, so a shift click
that adds a squad to ten tanks is answered by the squad; for a recall or a
select all it is the whole selection. For an order the group is the units the
order actually went to, so a harvester left out of an attack move, or one
refused a harvest for want of a refinery, has no say in who answers. The
lowest number makes the choice stable from one click to the next, so the same
group answers in the same register.

### Classes

The class comes from what the sim says a unit is, never from its model. An
aircraft is a unit whose type flies (`World.IsAirborne`). Infantry is what the
Barracks produces, the sidebar's INFANTRY rule, asked of
`CombatEffects.DeathLookOf` itself rather than copied from it, so a death
effect and a bark can never disagree about what a unit is: a scout built at
the factory is a vehicle and an engineer is infantry. Every other mobile, the
harvester and the MCV included, is a vehicle.

### The set

Three variants for each order, class and faction, played in turn so that three
answers in a row never repeat:

| Dimension | Values | What it changes |
|-----------|--------|-----------------|
| order | select, move, attack | the contour |
| class | infantry, vehicle, aircraft | the register and the texture under it |
| faction | Directorate, Sodality | the timbre and the interval language |
| variant | 1, 2, 3 | which interval, and a seeded nudge to pitch and timing |

That is 54 files, `game/audio/barks/bark_<dir|sod>_<class>_<order>_<variant>.wav`,
16-bit 44.1 kHz mono like every other effect, from 215 to 331 ms long and
1,283,856 bytes in all. The faction is the owning player's, so a unit
captured or built from the other side's tree still answers in its owner's
voice.

### The sound

Barks are WORDLESS on purpose. There is no voice and no syllable in them,
nothing that could be heard as a phrase, so nothing here can echo the unit
lines of any other game, and no recorded voice is needed (recorded voice is
spend, decision D26). Each is a short radio style figure rendered by
`python3 art/audio/synth.py sfx` from its own seed (300 to 353, in the nesting
order of the table above), and a re-run renders the same bytes.

The order sets the contour. A selection rises, short then long, a question
answered. A move falls, going. An attack is three quick notes, the last a step
higher and accented. The class sets the register and what sits under the
notes: infantry high with a keying tick, vehicles low over an engine throb,
aircraft highest over a passing rush of air. The faction sets the timbre and
the intervals. The Directorate's is a clean odd harmonic radio tone framed by
squelch clicks, stepping by fourths, fifths and octaves, held and orderly. The
Sodality's is two detuned partials beating over a breath of noise, opened by a
muffled tap, gliding by minor thirds, tritones and semitones, the sound of a
salvaged set. The two timbres differ by about 4 dB in crest factor, so the set
is matched by loudness rather than by peak, and the factions answer at one
level: every bark is turned down from the -3 dBFS peak every effect shares to
-14 dBFS RMS, and never turned up. Four have a crest factor too high to reach
-14 without passing that peak, the Sodality's three aircraft moves and its
first infantry move, so they stay at the peak and read -14.2 to -14.9 dBFS
RMS, a difference no ear will find in a sound a third of a second long.

### The channel and the debounce

Barks play on a channel of their own, one `AudioStreamPlayer` on the Ui bus
(`AudioDirector.Bark`), so a bark never takes a UI click's voice and a click
never cuts a bark off, and the interface volume slider holds them. One player,
because two units answering at once is noise rather than news. They play at
-6 dB, under the announcer's -4.

THE DEBOUNCE. A bark asked for within 0.6 s of the last one that started is
dropped, never queued, because a late answer is worse than none. The window is
longer than any bark in the set, so a bark is never cut by one of its own kind,
and clicking as fast as a hand can gives at most one answer per window. One
exception, because selecting and then ordering at once is the commonest thing a
player does: an order's bark is debounced only against the last order's bark,
and cuts a selection's bark that is still sounding. So an order made the
instant after a selection is always answered, and a second order inside the
window is not. Decided under the owner's standing instruction; it reverses if
a playtest finds the answers too sparse in heavy micro (shorten the window) or
the order cutting the selection jarring (let the selection finish).

### The drag select

A drag that catches anything has its own sound, `ui_select_box`: a soft upward
brush of noise landing on a small dry tick, 150 ms, so a drag reads as a
gesture where a click reads as a tap, airier than the click so that dragging
every few seconds never tires. Its level is derived from the click's rather
than chosen: `ui_click` plays at -14 dB from a file at -19.9 dBFS RMS, and
`ui_select_box`, at -16.4 dBFS RMS, plays at -18 dB, half a decibel under the
click. A click plays only the click. A drag that catches a run of barrier gets
the sound and no bark, because a barrier has no voice, and a drag that catches
nothing is silent, as an order that issued nothing is.

### Verification

`barkgate` in the client harness (`game/scripts/VerifyRunner.cs`,
`RunBarkStages`), on the battle scene's own AudioDirector with its clock
frozen and stepped by hand, from seat 1: every bark and the drag-select sound
loaded; a click and two right clicks answered with the selection, move and
attack barks of my faction's infantry, the click with no drag-select sound;
an enemy unit answering nothing, whether clicked or asked directly; the
debounce on selections and orders and the variants in turn; a drag answered in
the class it caught most of with its own sound, an empty drag silent; each
class barking as itself; and, in a second scene with the two sides' factions
swapped, the same squad answering in its owner's new faction.
