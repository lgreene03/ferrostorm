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
