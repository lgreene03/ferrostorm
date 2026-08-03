# ADR-068: the completion audit, and the roadmap that had gone stale

- Status: Ratified
- Date: 2026-08-03
- Deciders: Architect agent + Producer agent + Luke (under the standing directive)
- GDD/TDD feature served: doc 24 tiers A-D; GDD s8 line 71; P7-27

## Context

Q021 closed with ADR-067 and the support-power arc finished. The instruction for
this wave was to re-read the tracker and doc 24 and choose the next row **on my
own judgement**, or, if the list was exhausted, to say so plainly and say what
P7 would need to be declared complete.

The tracker is, in substance, exhausted. Of 46 rows, all but a handful are DONE;
the remainder are **refusals with recorded conditions** (P7-6 the silo, P7-7b,
P7-9, P7-10b) or administrative **splits** that were superseded by their halves.

The one row still reading "PART DONE" - P7-10, whose open half was wall tiers -
was closed by ADR-061 in P7-19 and nobody updated the row.

That was the first sign, and looking harder found worse.

## The finding: doc 24's Tier B was wrong in three places at once

Measured against the catalogue rather than read:

| doc 24 said | measured |
|---|---|
| **B1** "the turret is the only defence in the game" | **12** buildings on the defence tab - 2 Directorate-only, 4 Sodality-only |
| **B4** "No hero unit" | **2** - `dir_commando` and `sod_shadow_commando`, both capped at one alive |
| **B6** "No mines or minelayer. **NOT TAKEN**, and blocked on the Producer" | `com_mine` ships, and **`minegate` asserts its shape** - ADR-044 clause 4 later cited that very gate as its reason for a separate effect function |

Every one of those had been delivered by a wave that then failed to update the
document describing the gap it closed.

**This is the phase's signature defect, in the phase's own analysis of record.**
Around eighteen times now P7 has found a hand-maintained artefact lagging the
thing it describes - a comment naming four tautologies when there were five, a
schema list missing a registered kind, a save-surgery helper pinned to a version
six formats behind, a question number filed twice. This is the same shape, in the
document that decides what work remains.

**And the project already knew.** The tracker's own header says to prefer it
*"over any prose in the design docs, several of which lag by whole waves"*. That
is a defect written down as a warning instead of turned into a check. Worse, a
warning is only useful to a reader who already suspects - and doc 24 is exactly
the document a reader consults *because* they do not know.

## Decision

### `parityprobe`: the tier claims, derived

A non-asserting probe that derives doc 24's countable claims from the catalogue:
defence buildings by faction, hero units by their MaxAlive cap, mine-laying
buildings, support powers per faction, roster sizes, detectors, superweapons.

A reader comparing doc 24's prose to its output sees the lag immediately, and
nobody has to remember to look. **A derived list beats a hand-written one** - for
the nineteenth time in this phase, now applied to the roadmap itself.

**Non-asserting, deliberately.** Every number is a *count* of what the catalogue
holds, and counts are this project's definition of a balance question rather than
a correctness one (ADR-061's gate-versus-probe rule). A gate pinning "there are
12 defence buildings" would fail the day somebody authors a thirteenth, which is
not a defect. The three corrected entries are prose, not assertions, and the
honest instrument for prose is a report a human reads.

### The stale entries are corrected in place, with the original kept

B1, B4 and B6 now carry a measured correction with the original claim beneath it,
because in this project the correction has repeatedly been worth more than the
claim (doc 29's Tech Centre, ADR-048's refusal, ADR-060's Veil aura, and the
decoy army's supposed impossibility all read that way).

## What P7 needs to be declared complete

Measured, not estimated. **One parity gap remains, and it is not implementable.**

`parityprobe`: the Sodality has **3** support powers and the Directorate **2**,
against GDD s8's *"3-4 per faction"*. GDD s3 names five powers in total and all
five ship. **The sixth does not exist as a design** - it has no name, no doctrine
word and no purpose written anywhere.

Every one of the five shipped without a balance argument because every number in
them is derived from something already in the game. **A sixth cannot be derived,
because there is nothing to derive a name from.** That is invention on a roster,
which is C9 and a Game Designer's. Filed as **Q022**, with the constraint that
the Directorate owns only three exclusive buildings, one of which cannot carry a
power and one of which already carries both - so a third power either makes the
Bastion a three-way choice, sits on the power plant, or wants a new building.

Q022 also records the option that should not be assumed away: **that the
asymmetry is correct**, and s8's "3-4" was written before s3 named only two. If
so, s8 should be amended, because a written specification the design deliberately
does not meet is precisely how the stale claims above got written.

Everything else on the P7 list is DONE or REFUSED with its overturning argument
recorded.

**So P7 is one Game Designer sentence and one playtest from complete.**

## Hash and format

**All 24 goldens byte-identical, measured.** Nothing in `/sim` changed: this row
adds a reporting mode and edits documents.

## Consequences

The remaining blocker is unchanged and is now the only one. Sixteen ADRs have
asked for a playtest; this is the seventeenth, and it is the one that matters
most, because P7's remaining risk is no longer *"is it built"* but *"is it any
good"*.

Five support powers, two superweapons, twelve defence buildings, two heroes, six
campaign missions, four-player free-for-all, LAN, replay and save all work, and
**nobody has played any of it.**
