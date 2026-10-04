# 09 - Title Shortlist and Trademark Clearance Plan

Version 0.1. Purpose: get to a cleared, ownable name before the Steam page exists. I am not a lawyer and this is not legal advice; it is a working process to run, with a solicitor check at the end for the winning candidate.

---

## 1. Urgent finding: the placeholder must go

The working title "Ironharvest" collides directly with **Iron Harvest** (King Art Games, 2020) - an existing, well-known RTS. It cannot survive even as an internal placeholder because placeholders leak into file names, repos, and screenshots. **Effective immediately, internal codename: Project FERROSTORM** (codename only, not a launch-title candidate by default).

## 2. Naming criteria

A candidate must be:
1. **Distinctive** - coined or unusual word combinations clear trademarks far more easily than descriptive terms ("Iron", "War", "Command", "Conquer", "Storm" are radioactive: crowded, weak, and some invite EA-adjacent confusion).
2. **Clear of games** - no existing game, mod, or studio with the same or confusingly similar name.
3. **Registrable** - plausibly clear in trademark class 9 (downloadable game software) and class 41 (online game services) in UK, EU, and US.
4. **Ownable online** - .com or credible variant, Steam search not swamped, handles available.
5. **Evocative of the fantasy** - industrial war economy, factions, the Cinder resource.
6. **Pronounceable and memorable** for streamers saying it aloud (P6 persona).

## 3. Candidate shortlist (generated, NOT yet cleared)

| # | Candidate | Rationale | First-pass risk notes |
|---|---|---|---|
| 1 | **Ferrostorm** | Names the resource + evokes frontlines; coined compound | Industrial term exists generically; check music/band uses |
| 2 | **Ferrostorm** | Coined, martial, faction-neutral | "Storm" suffix is crowded in games; check similar marks |
| 3 | **Sundered Doctrine** | Evokes two-faction schism + military doctrine mechanics | Check tabletop/wargame space; "Doctrine" used in strategy titles |
| 4 | **Crucible Protocol** | Industrial + techno-military | "Crucible" appears in several games; compound may still clear |
| 5 | **Ashfall Command** | Post-industrial battlefield tone | "Ashfall" used by an announced game - verify status; "Command" invites genre confusion arguments |
| 6 | **Molten Concord** | The Cinder lore + ironic faction "concord" | Likely most distinctive; check pronunciation appeal |
| 7 | **(withdrawn: UK slang concern)** | Candidate withdrawn | Term unusable in the UK market |
| 8 | **Directorate: Cinder Wars** (series-style) | Faction-led branding enables sequels | Two-part names are weaker marks; sub-brand risk |

Generate a second batch after audience testing; do not fall in love with #1 on the list.

## 4. Clearance process (run per candidate, cheapest checks first)

**Stage A - knockout searches (free, ~30 min each):**
1. Steam store search + SteamDB (exact and partial matches, including delisted titles).
2. General web + itch.io + mobile stores + BoardGameGeek (board/card games count for confusion).
3. UK IPO trademark search, EUIPO eSearch, USPTO TESS - classes 9, 41 - exact and obvious-variant searches.
4. Domain (.com/.gg) and handle availability (YouTube, X/Bluesky, Discord vanity, Twitch).

**Stage B - survivors (top 2-3):**
5. Wider identical/similar-mark search across classes for famous-mark conflicts.
6. Audience test: say-it-aloud test with 10-20 target players (Discord poll); memorability at 24 hours.
7. Register domains + handles defensively for finalists (~£50 total; cheap insurance).

**Stage C - winner:**
8. Solicitor/trademark attorney clearance opinion (budget £300-£800).
9. File UK trademark first (classes 9 + 41, ~£170 + £50/extra class via IPO), EUIPO next (~€850) once the game is definitely proceeding past the Alpha gate; US filing can wait for launch commitment.
10. Record the decision as ADR-000 and purge every old placeholder from repo, docs, and assets (Legal agent A15 sweep).

## 5. Timing

- Stage A for all eight candidates: this month (agent-executable except judgement calls).
- Stage B: before the vertical slice starts (the slice's builds and captures will carry the name).
- Stage C filing trigger: Alpha gate pass (don't spend registration money on a project that might stop at the fun-gate).

## 6. Defensive notes

- Never use "Command & Conquer", "C&C", "Red Alert", "Tiberium", "Westwood" in the title, subtitle, Steam tags we control, or store copy. "Inspired by the classic RTS games of the 90s" is the approved formulation (doc 00 §5).
- Trademark the logo/wordmark later as a separate filing only if budget allows post-launch; the word mark is the priority.

## 7. Second property: the metal economy

**7.1 What the patterns guard against.**

The metal economy (doc 32, PROPOSED; its decision becomes D35 once the owner confirms it) was proposed after the owner, in October 2026, named Brandon Sanderson's Mistborn novels as a starting idea. That is the only reason the series is named in this repository, and this section records only what clearance needs.

There is no licence route. Dragonsteel's licensing policy (updated 20 August 2026) turns down digital gaming inquiries. MISTBORN is a live US mark covering electronic games (Reg. 4852186, class 28, renewed October 2025), alongside Reg. 4756692 for the books.

The bare idea that different metals do different things is free (US Copyright Office Circular 33; Nova v Mazooma [2007] EWCA Civ 219). An expressive combination, however, is protected even when renamed (Spry Fox v Lolapps, W.D. Wash. 2012). Doc 32 is built to stay clear of the series' combinations:

- the sixteen-metal table of base metals and alloys, paired as pulls and pushes acting on the self or the world in four groups;
- the loop of swallowing a metal and consuming it as a personal reserve that powers an ability;
- one person who uses every metal set against single-metal specialists;
- the signature imagery: lines drawn to nearby metal, firing and riding coins, deadly nightly mists, falling ash, spiked inquisitors, and the round chart of metal glyphs.

The name patterns in tools/legalgrep-patterns.txt catch the coined terms: the series, universe and company names, the three magic systems and their practitioner titles, the coined metals, peoples, places, gods, characters and book titles. The phrase patterns stop player-facing text from burning or flaring a metal, pushing or pulling on metal or coins, or calling a metal stock a reserve or a vial.

UK and EU registrations of MISTBORN are unchecked. A metal theme also moves the title nearer the author's brand space: DRAGONSTEEL and STORMLIGHT are live marks, and Ferrostorm's Latin root also begins one of the series' coined system names, Feruchemy. The title is therefore re-checked under section 4 if the theme ships.

**7.2 Player-facing vocabulary.** Anything a player reads or hears about metal uses only these words: mine, found, lode, Pithead, ingot, stock, spend and open, plus the real metal names Ferrite, Tungsten and Titanium. Never use reserve, vial, burn, flare, push or pull for metal, and never call Titanium "rarest" or "most precious".

**7.3 Human checklist at Legal sign-off.** Some words cannot be banned by the grep without breaking ordinary text, so a person checks every player-facing string and asset name for them: Thug, Seeker, Soother, Smoker, Rioter, Pulser, Slider, Augur, Oracle, Leecher, Brute, Sentry, Archivist, Spinner, Connector, Pinnacle, Skimmer, Sparker, Gasper, Inquisitor, Survivor, Preservation, Ruin, Harmony, Investiture, Vin, Ham, Breeze, Spook, Marsh, Wax and Wayne. The same check covers the themes mists, ashfall, savant, Snapping, Compounding, tapping and "lost metal". The design items, in this order:

1. Tungsten's item set includes the Phantom Tank, a stealthed unit, by price tier alone, and no metal may be tied to concealment or detection.
2. Titanium, the contested climax metal, keeps a strictly industrial justification.
3. Every metal icon and any glyph art is a name, a chemical symbol, an ingot silhouette or a plain shape, and ingots are bars, never coins.
4. The Pithead's art and every new voice line.

**7.4 Process.** No commit message, PR description or docs/JOURNAL.md entry for the metals work names or describes the series. The legal check reads docs/ but never a commit message. This section stays minimal and factual, and is for legal review if counsel is ever involved.
