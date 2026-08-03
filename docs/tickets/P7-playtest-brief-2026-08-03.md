# Playtest brief, 2026-08-03 (all of P7)

The last brief was **2026-07-24** and covered Phase C. Everything in P7 has
shipped since, every wave signed off on "18 gates green, hashes measured", and
**nobody has played any of it.**

Seventeen ADRs now name a playtest as the thing that would settle them. This
brief exists because none of them made it easier to do one - which is a tooling
gap, not a design gap, and the tool that fixes it is a page telling you what to
play and what question each thing answers.

**Nothing below is a bug report.** Every item is a judgement a gate cannot make,
written down by the wave that owed it, with the ADR that is waiting on your
answer.

## How long this takes

Four matches, about **ninety minutes**. They are ordered so that stopping early
still answers something: match 1 alone settles the three economy ADRs, which are
the oldest debts.

Answers can go anywhere durable - this file, a new one, or a reply. What matters
is that the ADR gets its verdict, because each one records exactly what would
have to be true to overturn it.

---

## Match 1 - the economy, on skirmish-01

**Play a normal skirmish against Normal AI. Do not rush. Build the economy you
would build if nobody were watching.**

Three ADRs refused an economy change and all three said the same thing: measure
the player, not the AI.

- **ADR-041 (no credit ceiling / the silo, REFUSED).** Do you ever end up
  *banking* credits with nothing to spend them on? The refusal stands on "the
  economy is undersized, not overflowing". If you finish matches rich and idle,
  the refusal is wrong and the silo comes back.
- **ADR-051 (a refinery is not a bottleneck, REFUSED).** Does a second refinery
  feel like it buys you anything? The ADR refused adding a throughput limit until
  somebody checked whether the economy is *already* too fast.
- **ADR-047 / ADR-048 (two refineries, the free harvester).** GDD s4 says a
  player floats at 2 refineries and 3 harvesters. **Do you?** If your natural
  build is different, the number in the GDD is the thing that is wrong.

**One question to hold the whole match:** did money ever stop being the thing you
were thinking about? In this genre it should not.

---

## Match 2 - the long game, on skirmish-07 (Karsthollow Basin)

**The big map, 256x192, eight times a small one. Play it out to a result.**

- **ADR-052 (dead weight and the match that does not end, REFUSED).** The AI-vs-AI
  match on this map does not resolve inside GDD pillar 2's promised
  **15-30 minutes**. The ADR refused "fixing" it because it could not tell
  whether the basin is simply a long-game map or whether the promise is broken.
  **Time this match.** If it runs long and that feels *right* for a map this size,
  the basin is fine and pillar 2 wants a caveat. If it runs long and feels
  *slack*, the game has a resolution problem the tools cannot see.
- **ADR-044 (superweapon charge, REFUSED).** The sim charges a superweapon in
  **100 seconds**; GDD s8 says **~6 minutes**. That is a 3.6x discrepancy the ADR
  would not take unilaterally under charter A11. On a match this long you will see
  several. **Do they dominate?** If the answer is yes, the ADR's own overturning
  condition is met and the charge goes to 5400 ticks.

---

## Match 3 - the support powers, either faction, any map

**Five powers shipped in P7 and every one is derived from a number already in the
game. Not one of them has been felt.** Build the unlocking building and use it.

| power | building | ADR | the question |
|---|---|---|---|
| Orbital scan | Bastion | ADR-063 | A 5-second reveal. Is that a *scan* or a flicker? |
| Precision strike | Bastion | ADR-064 | 300 damage in a tight core, no falloff. Surgical, or just small? |
| Radar jamming | Watch Post | ADR-065 | ~11 seconds of blank minimap. Clever ambush, or does it read as an **interface bug**? |
| Tunnel deployment | Veil Projector | ADR-066 | Five units, only onto ground you can see. Useful, or fiddly? |
| Decoy army | Shroud Nest | ADR-067 | Six fake rifle squads that die to one hit. Do they fool *you*? |

**The one that matters most:** the Bastion carries **both** Directorate powers and
they **share one charge** (ADR-064) - you hold a scan *or* a strike, never both.
Does that choice feel good, or does it feel like a bug?

**And the one nobody can answer with a tool:** does being on the receiving end of
any of these feel like being outplayed, or like being cheated?

---

## Match 4 - the Directorate under pressure, on skirmish-02 or skirmish-04

**Play the Directorate. Let the AI get ahead of you.**

- **`BALANCE-bastion-value-vs-shroud-nest.md` (A11, needs your co-sign).** The
  Bastion costs **1400 behind a radar**; the Sodality's Shroud Nest costs **400
  behind a plant**. **Do you feel defenceless before the radar?** That is the
  whole question. Note the Bastion has since gained two support powers, which
  changes its value and is *not* an answer to the ticket.
- **ADR-059 (beaten means cannot rebuild).** Lose your Construction Yard while
  your factory still stands. The AI now **buys an MCV and rebuilds** instead of
  selling up. Watch it happen to the AI, or do it yourself. Does the comeback
  feel earned, or does it just prolong a lost match?
- **ADR-060 (each side defends with its own hardware).** The balance tool reports
  the faction war as **6-0 one way**, and **0-6 the other** if one ladder rung
  changes - and calls both a PASS. A war with no middle is not a balanced game.
  **Does one side feel stronger to play?**

---

## Found by the index, and added after this brief was first written

`tools/playtest-index.sh` was run against the draft above and immediately caught
three live decisions it had missed. That is the tool doing its job on its first
real use, and the omissions are recorded rather than quietly fixed - the point of
the check is that a brief written from memory *will* have holes.

- **ADR-061 (wall tiers, REFUSED)** - and this one was mine, written eight waves
  ago. Its third overturning condition is *"a playtest saying bases feel
  unfortifiable"*, and the ADR itself notes that no tool can report it and it is
  **the argument most likely to be right**. While you play match 4, ask it:
  **can you actually fortify a position, or does a wall just delay the
  inevitable?** Measured, a gapped wall buys nothing against artillery and
  against rifle squads it makes the yard fall *sooner*.
- **ADR-018 (formations)** - spacing is one tunable constant, and the ADR says a
  refinement is a client-only change *"if playtests show"* it is wrong. Move a
  group. Does the box read as a formation, or as units standing oddly apart?
- **Q015 (does a departure decide the match?)** - open since P6. If a LAN player
  leaves, should the survivor simply win? This is a rules judgement rather than a
  feel one, so it needs your answer more than it needs a match.

## Also open, if you have appetite

- **`dir_bulwark_tank`** loses every matchup the balance tool runs, at 1600
  credits. Build some. Are they worth it?
- **Q022** wants one sentence: the Directorate has 2 support powers against GDD
  s8's "3-4". Either name a third, or say the asymmetry is correct and s8 should
  be amended.
- **Q020** wants a tier decision on four units whose prerequisite gates nothing.

## Keeping this brief honest

`tools/playtest-index.sh` lists every ADR and ticket that names a playtest, and
flags any this brief does not mention. Run it before trusting the list above -
the last wave found doc 24 claiming three things were missing that had shipped,
and a brief is exactly the kind of document that goes stale the same way.
