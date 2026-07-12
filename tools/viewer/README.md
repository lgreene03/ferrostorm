# Ferrostorm match viewer

A standalone HTML war-room that replays exported matches - the game's first
window, no engine required.

Build a fresh record and viewer:
  dotnet run --project sim/Ferrostorm.Sim.Runner -c Release -- export 2026 replay.json
  python3 -c "print(open('tools/viewer/viewer-template.html').read().replace('__REPLAY_JSON__', open('replay.json').read()))" > ferrostorm-viewer.html

Or drag any export onto an open viewer to load it. Space plays, arrows step,
1x-20x transport speeds, scrub bar. The ferrite seams dim as they are mined -
watch the economy drain out of the terrain itself.
