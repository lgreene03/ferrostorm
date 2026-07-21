# P6 Wave A delivery notes: the four classic-parity client tickets

Doc 24 Tier 3, executed as one wave under the P6 campaign tracker. All four
tickets are client-only and hash-neutral; the acceptance for the wave is
`git diff sim/` empty, the full battery exit 0 and the goldens byte-identical.
Plan comments first (CLAUDE.md workflow rule 2), delivery notes and the
standard footer at the end.

## TICKET-P6-FACTION-01 plan

labels: persona:commander gdd:s4 phase:6 owner:client-engineer

Approach. A FACTION row (DIRECTORATE / SODALITY) joins the skirmish menu
above THEATRE, in the exact Row() idiom the three existing rows use, because
the classic genre makes the side the first choice. The choice rides
MatchConfig into MatchSetup as two fields, Faction and OppFaction, and
BuildStartingWorld applies World.SetFaction for both players immediately
after map.BuildWorld and before any spawn. Two fields rather than one,
deliberately: every replay and save sidecar written before this ticket
describes a match in which BOTH players were Directorate (the World
constructor default), so a missing faction field must decode to that legacy
pairing or every old recording re-simulates a different world and reports
DIVERGED. A fresh menu skirmish sets OppFaction to the other side, per doc
24; a sidecar-less scene-direct launch keeps the legacy default and so
changes nothing.

The sidebar: the Units table gains the two Sodality entries the sim has
carried since P3 (sod_shade_raider 5, sod_phantom_tank 9) and every unit
button takes faction-gated visibility mirroring World.cs's own Produce test
(Faction == FactionCommon or == player 0's faction), exactly as the Wave 1
veil button mirrors the BuildStructure test. Hidden, not greyed; the sim's
checks remain the authority and are untouched. Structures need no new gate:
the sim faction-locks only the Veil Projector, and its button already reads
FactionOf(0).

Replays: the .frep format carries only seed, setup name and the command
stream; faction rides in the JSON sidecar beside it (MatchMeta), which is
client-side metadata, so the fix is small and client-only and is taken here
rather than filed. Saves: the .fsav has carried the faction array since
format v2 (Q001), and ResumeFromSave no longer re-applies from the map, so a
Sodality save resumes Sodality through the sim's own round-trip; the sidecar
faction only has to rebuild the pre-load world and the sidebar reads the
LOADED world's faction because BuildHud runs after ResumeFromSave.

Assumptions. Doc 24's "a Directorate skirmish is byte-for-byte today's game"
is read as the client path and the goldens being unchanged; the sim trace of
a NEW menu skirmish necessarily differs from yesterday's, because yesterday
the AI was Directorate by default and today the opponent takes the other
side, which is the doc's own design. Interfaces touched: MainMenu,
MatchConfig, MatchSetup, MatchMeta, SkirmishLive.BuildStartingWorld,
Sidebar.Init.

## TICKET-P6-MUSIC-01 plan

labels: persona:commander gdd:s7 phase:6 owner:audio + client-engineer

Approach. Two new generators in art/audio/synth.py's own idiom: music_calm,
an understated 64 second drone bed (low root-and-fifth sines, slow partial
movement, filtered air), and music_combat, an additive percussion and
tension layer of the same length so the pair stays bar-aligned when both
loop. Loop continuity uses the file's own established idiom, the
ambient_wind tail-into-head equal-power crossfade, because a drone that
edge-fades to zero dips audibly at the loop point; the combat layer's hits
decay before the end and take edge_fade, so both loop click-free, and the
generator asserts the boundary discontinuity is small rather than trusting
the recipe.

Client: AudioBuses gains a Music bus beside Sfx, Ui and Ambient;
AudioDirector gains a music player pair on it (calm bed at a fixed
understated level, combat layer crossfaded in by a 0..1 intensity, smoothed
per frame so the swell is musical rather than a gate). SkirmishLive owns the
intensity signal: any Fired event whose attacker or target belongs to player
0 sets it to 1, and it decays linearly over about 5 seconds of frame time.
A MUSIC slider joins the settings scene beside the existing four, in the
exact SET-01 Slider() idiom, persisted as audio/music.

Interfaces touched: synth.py, AudioBuses, Settings, SettingsScene,
AudioDirector, SkirmishLive.

## TICKET-P6-VO-01 plan

labels: persona:commander gdd:s7 phase:6 owner:audio + client-engineer + legal-review

Approach. art/audio/make_vo.sh generates the clip set with one macOS `say`
line per clip (voice Daniel, en_GB, matching the project's British voice)
to AIFF, then afconvert to the project wav format (16-bit 44.1 kHz mono,
verified against the committed assets with afinfo), into game/audio/vo/.
AudioDirector's loader learns to scan that one subdirectory. The clips are
PLACEHOLDER pending the legal-review check doc 24 records; the script is
the mitigation, one command to re-voice the set.

Nine lines per doc 24 (silos omitted, no silo system): construction
complete, unit ready, unit lost, base under attack, harvester under attack,
low power, superweapon launch detected, mission accomplished, mission
failed. Each wires into the EXISTING event and alert site ALONGSIDE its
toast and cue, never instead (the base-attack klaxon stays and the line
plays with it), through one PlayVo helper carrying a per-line cooldown map
in the alert-cooldown idiom, so "unit lost" says it once per window however
many walls fell. Played on the Ui bus.

Interfaces touched: make_vo.sh (new), AudioDirector, SkirmishLive.

## TICKET-P6-CURSOR-01 plan

labels: persona:commander gdd:s7 phase:6 owner:client-engineer + ux

Approach. art/cursors/gen_cursors.py, stdlib only (struct + zlib PNG
writer, supersampled rasteriser), draws eight 32x32 cursors into
game/ui/cursors/ in doc 16 palette tokens: ferrite gold and bone with a
cinder outline, and the invalid strike in the health-bar red the HUD
already owns. Shapes: select arrow, four-arrow move, crosshair attack,
pickaxe harvest, door enter, spanner repair, banknote sell, struck-circle
invalid.

Client: SkirmishLive resolves one CursorFor decision per frame from state
it already computes: placement mode asks the ghost's own CanPlace (invalid
over blocked ground), armed attack-move shows the crosshair, the pending
sell and repair confirmation windows show their cursors while live, an
enemy under the cursor with mobiles selected shows attack (or the door
when the selection is all engineers over a structure, the capture verb), a
ferrite field with a harvester selected shows harvest, open ground with
mobiles selected shows move, and everything else shows the select arrow,
so the OS cursor never appears in a live battle. Menu scenes are untouched
and _ExitTree restores the default. Input.SetCustomMouseCursor is called
only when the resolved cursor CHANGES, with the hotspot at the arrow tip
or the shape centre.

Interfaces touched: gen_cursors.py (new), SkirmishLive.

## Delivery notes

All four tickets shipped in one wave. Offscreen verification: a temporary
autoload (removed before commit, the established pattern) drove the REAL
menu path, battle scene and settings scene through their shipping code and
verification hooks. 42 checks, 42 passed, including: a Sodality skirmish
started from the real FACTION row showed the Sodality roster and BUILT a
shade raider through the sidebar and factory queue; a hand-crafted
wrong-faction Produce reached the sim and was refused; a Sodality save
resumed Sodality; a Sodality replay re-simulated onto its recorded hash; the
Directorate default path showed the unchanged Directorate roster; the calm
bed played at battle start with the combat layer silent, snapped to full
intensity when fire involved player 0, swelled to -11 dB and decayed back to
silence in about five seconds of peace; all nine VO clips loaded and
"construction complete" was found ON a live UI player carrying the right
stream; "unit lost" spoke once for two deaths in one window; the cursor
resolver answered Attack over an enemy with a combat selection, Harvest over
ferrite with a harvester, Invalid over blocked placement and Select over a
legal cell. Screenshots captured: the faction menu row, a Sodality sidebar,
and the attack cursor in a battle frame.

Findings recorded on the way through:

1. Replays did NOT carry faction. The .frep format carries only seed, setup
   name and commands; the fix rides in the JSON sidecar (MatchMeta), which
   is client-side metadata, so it was taken here rather than filed. Two
   sidecar fields (faction, opp_faction) rather than one, because every
   pre-P6 sidecar means the legacy both-Directorate pairing and must decode
   to it; the harness proved an old-shape sidecar's absence default and a
   new Sodality recording both re-simulate onto their recorded hashes.
2. A NEW Directorate skirmish now faces a Sodality opponent (doc 24's
   "opponent defaults to the other side"), so its sim trace differs from
   yesterday's mirror match: the AI plays its Sodality doctrine. The
   player-facing Directorate path (roster, sidebar, own hardware) is
   unchanged, the goldens are untouched, and old recordings still verify.
   If the mirror match is wanted back as an option, that is an OPPOSITION
   row extension, filed here rather than smuggled in.
3. The Sodality unit buttons cite icon names sod_shade_raider and
   sod_phantom_tank; the sprites are not cut yet, so those two buttons are
   text-only until the art/png downscale pass mints them (the MakeButton
   Exists guard tolerates this by design). The veil icon precedent (sips
   downscale of the reference sprite) is the one to follow.
4. The team-colour constants BattlefieldView.DirectorateMark and
   SodalityMark are PLAYER SLOT colours (0 and 1), not faction colours, and
   read slightly misnamed now that either faction can hold either slot.
   Purely cosmetic naming; left alone deliberately.
5. The harness's first run found the combat fixture racing the opponent AI:
   an idle enemy squad teleported beside the target was ordered home by its
   own commander before firing. Fixture pinned with an explicit target;
   worth remembering for future scripted battle checks.

## Changed / Assumed / Needed next

Changed: game/scripts (MainMenu, GameFiles, SkirmishLive, Sidebar,
Settings, SettingsScene, AudioDirector), art/audio/synth.py (two music
beds), art/audio/make_vo.sh (new), art/cursors/gen_cursors.py (new),
game/audio/music_calm.wav + music_combat.wav, game/audio/vo/ (nine clips),
game/ui/cursors/ (eight cursors), this document, the campaign tracker and
the ledger. Zero sim/ edits; goldens byte-identical; full battery exit 0;
both client builds clean with zero warnings.

Assumed: doc 24's "byte-for-byte" acceptance means the client path and the
goldens, not the sim trace of a match against an opponent who now plays the
other side (finding 2). The VO wording is original phrasing of generic
battlefield reports; the voice itself is placeholder TTS.

Needed next (from whom): legal-review's check on TTS redistribution before
any public build ships the VO set (doc 24 caveat); the two Sodality icon
sprites from art-pipeline (finding 3; DONE: sod_shade_raider and sod_phantom_tank sprites delivered); a composed score to replace the
placeholder beds when the audio direction is ready (doc 24 says the system
is the ticket, not the destination).
