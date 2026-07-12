# 11 - Control Scheme Specification v1 (TICKET-P1-12)

Owner: UX agent. Consumer: client engineer (A6). Status: accepted for vertical slice implementation. Serves GDD s10 and personas P1 (classic hands) and P2 (modern hands). Both schemes selectable at first boot and in settings; everything remappable.

## Scheme A: Classic
- Left click: select. Left click on ground with selection: move. Left click on enemy: attack. Left click on ally/own: select instead (drag box to multi-select).
- Right click: deselect all.
- No modern smart-cast; sidebar clicks place/queue as in the classics.

## Scheme B: Modern (default candidate, pending Q4 A/B in beta)
- Left click / drag: select / box select. Right click: context order (move / attack / harvest / enter).
- A + left click: attack-move. S: stop. H: hold position.

## Shared bindings (both schemes)
- Ctrl+0..9 assign control group; 0..9 select; double-tap centres camera; Shift+number adds to group; Alt+number steals into group.
- F1-F4 camera bookmarks (Ctrl+F1-F4 to set). Space: jump to last alert. Tab: cycle sidebar tabs. E: select all military on screen; Ctrl+E: everywhere.
- Shift-queue waypoints for any order. Rally points set per production structure by selecting it and right/left-clicking per scheme.
- Grid build hotkeys: QWERTY rows map to sidebar slots (Q row = structures tab, A row = units tab), displayed on the sidebar buttons.

## Interaction rules
- Selection cap: none (Q1 resolution); mixed selections order all, UI shows dominant type.
- Order feedback: instant local acknowledgement (bark + marker) regardless of lockstep command delay (TDD s4); the sim acts on schedule.
- Every QoL binding must pass the pillar-5 test: automates hands, never strategy.

## Acceptance criteria for implementation
Remap UI persists across updates; conflicts detected at bind time; both schemes complete the tutorial script without touching the mouse settings; colourblind-safe selection markers.
