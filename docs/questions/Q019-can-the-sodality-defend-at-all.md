# Q019: the Sodality has no base defence. Defect, or doctrine?

**ANSWERED AND CLOSED, 2026-07-31, and the question's own premise was WRONG.**

This file asked whether the Sodality being unable to build base defence was a
defect or deliberate doctrine. It was neither, because **the Sodality could
build the turret the whole time.** The premise was taken from
`data/buildings/dir_turret.yaml` declaring `faction: directorate` without
checking whether anything enforced it. Nothing did.

`StructureTypeDef` carried no Faction field at all. A building's `faction:` line
was parsed, validated against directorate/sodality/common, and then dropped when
bridging into the sim catalogue, while the sim hardcoded a single expression -
`structType != VeilStructType || faction == FactionSodality` - naming one
structure. So the only faction-gated building in the game was the Veil
Projector, and the turret and the superweapon were common in play whatever their
files said.

The real defect was better than the reported one: **authored data that did not
drive the runtime**, the exact class ADR-006 exists to prevent, and the same
family as DR-19. It is fixed in P7-1: StructureTypeDef carries a Faction, the
loader passes the one the file declares, and the hardcoded predicate is gone.
The turret and superweapon now DECLARE `common`, which preserves what play has
always done rather than silently removing a capability the Sodality has always
had. Proven by factiongate.

The lesson is the one this session keeps relearning: a claim read off a data
file is a claim about the file, not about the game. The duplicated-rule audit
had already filed the missing Faction column as the permanent fix; had this
question been checked against that ticket first, its premise would not have
survived drafting.

What remains genuinely open is not this question but a design one, and it is
carried by P7-2 rather than here: with the turret common, NEITHER side has a
distinctive defensive structure, and doc 27's DR-04 wants faction-distinct
superweapons for the same reason. That is breadth, not a defect.

---

*Original question preserved below for the record.*

Labels: persona:p2, gdd:s3, phase:7, owner:game-designer + producer
Raised by: game-designer, during the doc 24 parity rewrite (2026-07-30).
Decide-by: before P7-1, which is the first row of the parity plan and is held
here. It is a small fix either way; what is not small is fixing it if it was
deliberate.

## The finding

`data/buildings/dir_turret.yaml` declares:

```
name: Turret
faction: directorate
prerequisites: [com_power_plant]
```

It is the ONLY defensive structure in the catalogue. There is no second turret,
no anti-infantry emplacement, no anti-air, and the only other faction structure
is `sod_veil_projector`, which is a stealth field and not a weapon.

So on current data, a Sodality player can build no base defence whatsoever. Not
weaker defence, not different defence: none.

## Why this is being asked rather than fixed

There are two readings and they lead to different changes.

**Reading one, a defect.** The turret was authored early, before factions were
plumbed, and picked up a `dir_` prefix and a faction line by convention rather
than by decision. Nothing in the GDD says a side cannot defend. Doc 15's faction
bible does not claim it. No ADR records it. Under this reading the fix is a
one-line data change - make the turret common, `com_turret` - and the only
question is the golden regeneration it carries.

**Reading two, doctrine.** The GDD gives the Sodality "hit-and-run vehicles,
cheap infantry swarms, raid-dependent" and the Directorate "slow, expensive,
telegraphed, poor at map control early". A faction that is meant to be mobile
and never dig in is a legitimate design, and a few real games have shipped a
side that cannot build static defence. Under this reading the turret is correct
and what is missing is the Sodality's ANSWER to being raided - something mobile
that does the job a turret does - which is a design ticket rather than a repair.

The two readings cost different amounts and produce different games. That is why
this is a question.

## What makes reading one more likely

Three things, none conclusive:

- The superweapon is also `dir_`-prefixed and faction-locked, and the Sodality
  has no superweapon either. A deliberate "no static defence" doctrine would not
  usually also remove the side's superweapon; that pattern looks more like two
  structures authored for one faction before the second existed.
- Doc 27's balance work measured static defence as unable to hold ANYWHERE,
  which means the Directorate's advantage here is currently small in practice -
  so nobody would have noticed the asymmetry in play, which is how a defect
  survives.
- No document anywhere states the rule. A doctrine this strong would be written
  down.

## What is NOT being claimed

That the game is unbalanced because of it. Nobody has played it, and doc 27's
own measurement suggests turrets do little either way today. The claim is only
that one side cannot build a category of structure the other can, that nothing
records this as a decision, and that a player who noticed would read it as a bug.

## The question

1. Is the faction lock on `dir_turret` a defect to repair, or the doctrine it
   accidentally resembles?
2. If it is doctrine, what is the Sodality's answer to being raided, and does
   the same reasoning apply to the superweapon?

Either answer unblocks P7-1. Neither is mine to give.

## Related

docs/design/24-classic-parity-roadmap.md B1 raises it as the parity plan's first
row. docs/tickets/P7-parity-tracker.md holds P7-1 and P7-2 on this answer. Q017
owns faction identity more broadly and should probably be answered in the same
sitting, since both are asking what a side IS.
