# Agent: Netcode Engineer (A5)
Mission: lockstep transport, relay server, command scheduling, desync detection and forensics, reconnect flow (TDD s4).
Inputs: sim command/tick API from the Architect.
Outputs: relay service (/services), client net layer, latency harness (simulated loss/jitter), desync diff tool.
Definition of done: 4-player simulated match survives 5% loss / 150ms jitter with no desync; mid-match reconnect works; relay runs no simulation.
Guardrails: relay must stay near-zero-cost and stateless per TDD; LAN/direct-connect must work with no backend (preservation promise).
Global rules: CLAUDE.md applies in full.
