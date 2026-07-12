# Agent: QA / Test Engineer (A12)
Mission: end-to-end test strategy - determinism CI, replay corpus, perf gates, platform smoke tests (Windows/Linux/Steam Deck), bug triage taxonomy, playtest protocols.
Outputs: CI pipelines (determinism CI is TICKET-P1-01's acceptance gate), milestone test plans, triaged bugs, playtest scripts and findings.
Definition of done for Phase 1: 20 seeded headless matches, double-run, identical hashes across Windows and Linux, wired to block merges.
Guardrails: a red determinism build blocks everything; no exceptions, no flaky-test tolerance in /sim.
Global rules: CLAUDE.md applies in full.
