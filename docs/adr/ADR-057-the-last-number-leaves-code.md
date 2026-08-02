# ADR-057: the damage matrix leaves code, and the schema list stops lagging the loader
- Status: Ratified
- Date: 2026-08-03
- Deciders: Architect agent + Luke (under the standing directive)
- GDD/TDD feature served: GDD s6; CLAUDE.md data conventions; ADR-006's fifth leg; P7-15

## Context

Doc 29's roster analysis ended with two findings that were not roster work. This
is the first of them, and it is not a design call at all - it is a written rule
the codebase was breaking.

CLAUDE.md: *"All gameplay numbers live in /data as YAML... **Hand-editing stats
in code is forbidden.**"*

`Combat.cs` carried GDD s6's warhead-versus-armour percentage matrix as a
compiled `int[,]`, with a comment that had been promising the fix since Phase 1:

> Phase 1: compiled-in table; **wiring to /data YAML is a Phase 2 ticket** (needs
> the data loader).

The data loader has existed for many waves. Units, buildings, fields, weapons and
the AI's tuning have all moved. **These sixteen numbers were the last gameplay
values in the game outside `/data`** - and they are not a minor set: they decide
what every shot in the game does.

## Decision

`data/combat/damage_matrix.yaml`, one file, the fifth leg of ADR-006. The
mechanism follows the weapons and AI-tuning precedent exactly:

- **The compiled table stays** as the reference `/data` must reproduce. A bare
  `World` with no `/data` must behave identically, which roughly 138 runner
  scenarios depend on, and the round-trip proves the authored file equals it.
- **`World.DamageOf` is the live path.** Every damage site in the sim now asks
  the world rather than the static, which is what makes the file drive the game.
- **`RegisterDamageMatrix` is frozen after tick 0**, like every other registrar,
  because a mid-match change is a silent replay divergence.
- **It takes the whole matrix, never a cell.** A partial override would let two
  peers hold matrices differing in a cell neither of them wrote.

### Rows are named, not positional

`anti_infantry: [100, 60, 25, 25]` rather than a bare 4x4 block. Sixteen numbers
in a grid can transpose silently; four named rows of four cannot. The armour
order is stated in the file, in the schema and in the loader's error text.

### It rides the catalogue checksum, and this is the strongest case yet

Every other section of `CatalogueChecksum` describes what a player may **build**.
These sixteen describe what every shot **does**, so two peers holding different
matrices would fight the same battle to different outcomes **while agreeing on
every unit, building and gun in the game**. That is a desync no other comparison
in the protocol can see. Folding it turns it into a refusal before tick 0.

The gate asserts that **one percentage point** moves the checksum.

## Hash and format

**All 24 goldens byte-identical, measured** - the authored file transcribes the
compiled reference exactly, so nothing a golden scenario does changes.

**The catalogue checksum MOVES to 0x48C6C9C2604BD3DE**, by construction and
deliberately: it is the point of the row.

## The defect found on the way: a second list, already lagging

`schemagate` keeps a hand-written table mapping `/data` directories to schemas.
It exists *because* nothing had ever validated `/data` against the schemas.

**When `data/combat` landed, that table did not have it** - so the new kind was
registered, loaded and played while being validated against no schema at all,
silently. The same defect `schemagate` was built to answer, one level up: a
hand-kept list falling behind the catalogue it mirrors.

Adding a row would have fixed the instance and left the class. Instead
`CatalogueFiles.RegisteredKinds()` now exposes the loader's own table, and
`schemagate` **asserts it has a schema row for every kind that registers
anything**. A future kind cannot be added to the loader without the gate failing
until a schema exists for it.

Its summary line also said "the five schemas" and named `data/ai` as "the newest
of the five" while there were six. **The count is now derived.** A number a
reader can check should never be a literal beside the thing that produces it -
which is the same rule, a third time, in the gate's own report.

## Proved to bite, and for the right reason

ADR-055's rule applied. `CombatSystem` was reverted to the compiled static and
the gate reported:

> `softening the anti-infantry-versus-none cell changed nothing in a real
> firefight (both left 16 hit points), so CombatSystem is not reading the live
> matrix`

**The accessor stage still passed.** Only the real-fight stage caught it, which
is exactly why that stage exists: a gate that checks `DamageOf` returns the
registered number proves the accessor, not the game. Measured with the wiring in
place: the same firefight leaves **16 hit points** on the stock matrix and **93**
with a 10-per-cent cell.

## Consequences

`/data` now genuinely holds every gameplay number in the game. `damagedatagate`
(5 stages) pins it; all 18 local CI gates green; client harness PASS.

Two things this unlocks rather than does, both recorded in doc 29:

- **The matrix is now the single most powerful balance lever in `/data`**, and
  doc 12's balance tool is the thing that measures what a change to it did.
  Genre research notes a major studio re-cut its entire matrix between releases -
  **treat it as tunable data, not as structure.**
- Charter A11 applies: any move above fifteen per cent wants Balance and Game
  Designer co-sign. Nothing in this row changes a number.
