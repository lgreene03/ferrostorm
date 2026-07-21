# TICKET-P5-SPAWN-03: the player gets a Deploy control

labels: persona:commander gdd:s5 phase:5 owner:client-engineer
found: 2026-07-16, during Wave 1 verification (doc 23)
severity: major (a shipped unit is unusable by a human), confidence: certain

## The finding

The client never issues `CommandType.Deploy`. A grep across game/scripts/
returns no caller; the only issuers in the repo are SkirmishAI and the gate
runner. A human player can build the 3000-credit MCV from the sidebar, drive
it anywhere on the map, and has no key, button or context action that unpacks
it. The sim's deploy rule (World.cs:874-891, clear-area test and yard spawn)
is correct and gate-covered; the AI uses it every match. Only the player
cannot.

Wave 1's TICKET-P5-SPAWN-02 shipped the "DEPLOY BLOCKED - CLEAR THE AREA"
toast, so the feedback half exists and is hook-proven. This ticket is the
control half.

## The design

A new InputMap action `deploy` (suggested default D, checked against the
19-action set by the settings scene's conflict detector, which is the
mechanism TICKET-P5-SET-01 built for exactly this). In HandleKeyAction, on a
selection containing at least one own MCV (UnitType 7), issue Deploy for each
selected MCV. The SPAWN-02 watcher already reports the blocked case; the
success case is self-evident on screen. Additionally, double-click on a
selected MCV should deploy, mirroring the classic idiom, and the single-MCV
selection readout should advertise the key the way the repair readout does.

Client-only, hash-neutral: Deploy is an ordinary player-equivalent command in
the existing stream, exactly like the orders the shipped auto-resume and
Wave 1's REP-02 already issue. Replays record it as any other order.

Acceptance: build an MCV in a live skirmish, drive it away, press the key,
and a second construction yard stands where it was; the settings scene lists
and rebinds the action; a blocked deploy says why (SPAWN-02's toast) and a
clear one succeeds.

## Plan comment (client-engineer, 2026-07-16)

Approach. Client-only plus one project.godot [input] entry; zero sim edits.
(1) A `deploy` action on keycode 68 (D), appended to the [input] block in the
exact serialisation shape of the existing nineteen; verified unbound first by
grepping the block for keycode 68. (2) `("deploy", "DEPLOY")` added to
Settings.Bindable, which is the single hand-written list the settings scene,
the conflict detector and ApplyBinds all iterate, so listing, rebinding and
refusal come free. (3) In HandleKeyAction, a deploy branch that issues
CommandType.Deploy for every own MCV in the selection through IssueDeploy,
the SPAWN-02 hook, which becomes the one shipped issue path; the branch does
not consume the press when the selection holds no MCV. (4) A double-click
case in _UnhandledInput, tested before the generic left case: the first
click of the pair has already selected the vehicle through FinishSelect, the
second press issues instead of starting a drag, and a non-MCV double-click
falls through and behaves exactly as two single clicks always have. (5) The
single-MCV selection readout advertises the live binding via
Settings.KeyName(Settings.BindOf("deploy")), the SET-01 rule. The persistent
hint bar is deliberately not touched: it is crowded and deploy is contextual.

Assumptions. MCV is UnitType 7 (confirmed against World's compiled unit
catalogue, com_mcv, and the sim's own Deploy guard). The SPAWN-02 watcher
arms in RunOneTick on any player-0 Deploy in the applied command stream, not
on a particular issue path, so both new controls inherit the blocked toast
without further wiring (confirmed by reading the arming loop). Deploy is
hash-neutral: an ordinary player-equivalent command in the existing stream.

Interfaces touched. game/project.godot [input]; Settings.Bindable;
SkirmishLive.HandleKeyAction, _UnhandledInput, SelectionSummary, IssueDeploy
and the SPAWN-02 watcher's stale "nothing issues a Deploy" comment.
docs/tickets/phase-1-backlog.md is another agent's live edit at the time of
writing, so the ledger entry is owed rather than written here.

## Delivered (client-engineer, 2026-07-16)

As planned, all five parts. Verified against a real Godot 4.7 offscreen run
(headless, dummy audio, scene-direct) through a temporary autoload driving
the shipped input paths and removed before commit: 31 assertions, all green.
The run proved the acceptance in full: D on a selected MCV issued exactly
one Deploy through the pending stream and a second construction yard stood
one tick later; D with no MCV selected issued nothing and was not consumed;
a footprint-blocked deploy left the MCV alive, founded nothing, and raised
SPAWN-02's "DEPLOY BLOCKED - CLEAR THE AREA" toast from the new key path; a
real press-release-press(double) sequence on the MCV's screen position
selected it and then deployed it, while the same sequence on the yard issued
nothing; the settings scene listed DEPLOY on D, refused R by naming REPAIR,
rebound onto J, after which D was dead, the readout said "J deploy", J
deployed, and a fourth yard stood; the binding was then restored to D and
the user settings file came through byte-identical. Both client
configurations build clean; full battery exit 0; git diff sim/ empty.

One adjacent finding, reported not fixed: SnapshotInterpolator.TrySample's
doc comment says entities absent from either bracketing snapshot "render at
the snapshot where they exist", but the loop iterates only the earlier
bracket's array, so a newborn entity reaches the view one tick late (deaths
behave as documented). Invisible in play at 15 Hz; it cost the offscreen
probe an extra settle tick after each scripted spawn. Sim-adjacent file, so
even the comment fix is left to a sim-owned ticket.

Owed: the phase-1-backlog.md ledger entry (file was another agent's live
edit for the whole session), and doc 23 / SET-01 prose that says "19
actions" where the rebindable surface is now 21.
