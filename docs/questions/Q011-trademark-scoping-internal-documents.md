# Q011: the Legal rule says "anywhere" and the internal design documents break it. Scoping amendment or edits?

Owner: legal-review + Luke
Raised by: doc 23 section 7's referred legal finding (2026-07-16); filed by
ADR drafting, 2026-07-17, with the inventory re-verified by grep at HEAD
Decide by: 2026-07-24
**Status: CLOSED 2026-10-02 by decision D31** (docs/tickets/P8-formidable-tracker.md,
taken under the owner's standing authority of 2026-10-02; implemented by row P8-2)

## Decision (D31)

Option 3's hybrid shape, with the allow-list cut down to the documents whose
purpose is the marks themselves. The prohibition is absolute for everything
that ships or speaks to the player and for every working design document, with
ONE narrow exception: a document whose purpose is clearance, legal analysis or
competitor research, because a mark cannot be cleared, or a competitor
assessed, without being named.

- **The exception is a file, `tools/legal-allowlist.txt`**: one repo-relative
  path per line with its reason. It holds two documents, doc 07 (competitor
  research) and doc 09 (trademark clearance). The check refuses an entry
  outside docs/, an entry that does not exist, and an entry that no longer
  names anything, so the list can neither grow quietly nor rot.
- **Everything else was reworded rather than listed.** The GDD lines below
  (and two more the check found, the audio line and a build-radius
  resolution) now use the approved formulation; so do docs 00,
  01, 04, 05, 10, 13, 18, 20, 22, 27, 30 and 31, ADR-020's quotation of the
  GDD, the phase 1 backlog and art/audio/synth.py. Doc 00 section 5 now states
  the same rule and points at the canonical list instead of repeating it,
  which answers this file's closing request that the two say the same thing.
  Doc 10 is a clearance report, but its one usage was an analogy, so it was
  reworded and is not listed. The historical reports option 3 would have
  excepted were reworded the same way. This file names nothing, by its own
  method, so it needs no exception either.
- **Enforced by `tools/legalgrep.sh`**, run by the banned-tokens job in
  .github/workflows/determinism.yml and by tools/ci-local.sh. It fails on any
  protected name over game/, art/, data/, sim/, docs/ and the README outside
  the allowlist, and on the ten retired announcer phrasings wherever a player
  hears or reads them (the VO generator, data/, and string literals in
  game/scripts). Its patterns live in tools/legalgrep-patterns.txt, outside
  every scanned tree, so the check never matches its own source.
- **The two WIP files** the inventory warned about (doc 22 and the phase 1
  backlog) were edited. The warning dates from 2026-07-17; both are history
  now that P6 and P7 have closed, and the edits touch only the protected
  usages.
- **Not decided here, still legal-review's judgement call:** the slot
  shorthands this file flags below under the trade-dress clause (GDD lines 62
  and 64 and the code comment it cites). They are not on the name list and the
  check does not look for them.
- **CLAUDE.md's Legal bullet is restated** (2026-10-02, by the orchestrator under the
  owner's standing authority, since the implementing agent rightly declined to edit
  CLAUDE.md on another agent's instruction). It now names the allowlist and the check,
  so CLAUDE.md and doc 00 section 5 say the same thing.

NOTE ON METHOD: this file names no names. Every occurrence below is cited by
file and line only, because the rule under discussion forbids the terms in
any repository file and this file is a repository file. Readers should open
the coordinates, not reproduce them.

## The question

CLAUDE.md line 13 bans a specific list of franchise, faction, resource,
studio and publisher names "anywhere", under a header (line 12) that says "no
exceptions, no exemptions for placeholders/tests/comments". doc 00 line 52
states the same ban scoped to "the product". Internal design documents use
the listed names in quantity, while the product trees are clean. The two
formulations disagree and the repository currently obeys the narrower one.
The decision owed: a scoping amendment to CLAUDE.md that distinguishes
internal working documents from the product, or edits to the documents to
bring them under the rule as written.

## The verified inventory (grep, case-insensitive, listed terms only, 2026-07-17)

Clean, zero hits: sim/, game/, data/. The product complies today.

One hit outside docs/: art/audio/synth.py:7, a code comment whose sentence
instructs AVOIDING resemblance to the named material. A guardrail usage, but
a listed term in a non-docs file, so the "anywhere" rule catches it.

docs/ files carrying listed terms (file level; the load-bearing lines called
out where doc 23 already itemised them):

- docs/design/02-game-design-document.md, lines 24, 29, 45, 83 and 92. This
  is the sharpest case: the GDD is source-of-truth number two, so the rule
  is being broken by the document that outranks every other. Line 92's
  usage sits inside a sentence instructing the team to AVOID resembling the
  named brands: a guardrail, not an appropriation, and rewritable to
  describe the palettes to avoid without naming their owners (doc 23 s7's
  nuance).
- docs/design/18-game-review-roadmap.md, lines 50 and 126 (doc 23 listed
  only 126; line 50 is a second hit found by this verification).
- docs/design/00-project-overview.md, 01-personas-and-stakeholders.md,
  04-agent-team.md, 13-phase1-gate-review.md, 20-visual-aaa-roadmap.md:
  shorthand and comparison usage.
- docs/design/07-competitive-analysis.md and
  docs/design/09-title-and-trademark.md: documents whose FUNCTION is naming
  competitors and clearing marks; an elision rule arguably destroys their
  purpose.
- docs/design/22-scale-and-colour-roadmap.md and
  docs/tickets/phase-1-backlog.md: on the inventory, and both are live WIP
  of another session, so any edit option must coordinate with that work
  rather than touch them unilaterally.

Adjacent judgement call, not on the literal list: GDD lines 62 and 64 carry
hyphenated slot shorthands referencing classic unit names, and the same
shorthand appears in a code comment at sim/Ferrostorm.Sim/SkirmishAI.cs:175,
inside sim/. Not caught by the name list; arguably caught by CLAUDE.md's
broader trade-dress clause. Doc 23 s7 asks that this be labelled a judgement
call rather than asserted alongside the certain hits, and it is.

## Why it needs deciding rather than tolerating

Every honest document that quotes the GDD has to elide it: doc 23 did, and
ADR-007 through ADR-009 did. That is a standing tax on precision, and the
alternative failure is worse: a contributor who reads CLAUDE.md's "anywhere"
literally and mass-edits twelve documents, including two live WIP files and
the clearance report whose job is to name marks, would be enforcing the rule
and damaging the repository in the same commit. The rule should say what it
means so that neither the tax nor the mass-edit happens by default.

## Options considered

1. **Scoping amendment to CLAUDE.md.** The ban stays absolute for the
   product: sim/, game/, data/, art/, asset names, player-facing text,
   marketing copy, commit messages and all NEW documents; existing internal
   analysis documents may name marks where naming is the analytical point
   (docs 07 and 09 expressly). One sentence plus a file allow-list. Cheapest;
   matches doc 00 line 52's existing scope; leaves the GDD's shorthand usage
   (lines 24, 29, 45, 83) standing, which still forces elision on anyone
   quoting those lines.
2. **Edits.** Bring every document under the rule as written: rewrite the
   GDD's five lines and doc 18's two to describe without naming, rewrite GDD
   line 92 and art/audio/synth.py:7 in guardrail-preserving form, leave docs
   07 and 09 as the sole sanctioned exceptions (or edit them too and accept
   the loss), and coordinate the two WIP files with their owning session.
   Most consistent; most work; the GDD edit needs Producer visibility since
   it amends source-of-truth number two.
3. **Hybrid (doc 23's implied shape).** Amendment for the analysis documents
   (07, 09, and the historical reports 10 and 13); edits where the usage is
   mere shorthand (GDD lines 24, 29, 45, 83; doc 18 lines 50 and 126; the
   overview, personas, agent-team and visual-roadmap hits); guardrail
   rewrite for GDD line 92 and art/audio/synth.py:7; an explicit ruling on
   the slot shorthands (GDD 62 and 64, SkirmishAI.cs:175) under the
   trade-dress clause.

## Changed / Assumed / Needed next

- **Changed (2026-07-17):** nothing. This was a question, not a decision.
- **Changed (2026-10-02, P8-2):** closed by D31 as recorded above; the
  inventory below the decision is kept as the record of what was found.
- **Assumed:** the grep inventory above was complete for the LISTED terms at
  HEAD (fdff459); it is superseded by tools/legalgrep.sh, which re-derives it
  on every push. The trade-dress judgement calls are flagged but not
  enumerated exhaustively, because judging resemblance is exactly the call
  being requested.
- **Needed next:** from legal-review, a ruling on the slot shorthands if one is wanted.
