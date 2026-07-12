# 08 - Art and Audio Cost Model

Version 0.1. Purpose: turn "art is the biggest spend" into a number with scenarios, so the go/no-go gates in doc 05 have real financial teeth. All figures in GBP, exclusive of VAT, based on typical 2025-26 freelance/outsourcing market rates for stylised low-to-mid-poly game art. Rates vary hugely by region and seniority; each line carries a low-high band. **Treat this as a planning model, not quotes - validate with 2-3 real quotes per category before the Alpha gate.**

---

## 1. Asset inventory (derived from GDD v0.1)

### 3D units (modelled, textured, rigged, animated, team-colour masked)
| Category | Count | Unit cost band | Subtotal band |
|---|---|---|---|
| Vehicles/aircraft (24 faction + 3 shared) | 27 | £350-£900 | £9.5k-£24.3k |
| Infantry squads (8 types, shared rig + anim set) | 8 | £450-£1,100 | £3.6k-£8.8k |
| Hero/commando units (bespoke anims) | 2 | £700-£1,500 | £1.4k-£3k |
| **Units subtotal** | **37** | | **£14.5k-£36k** |

### 3D structures (build-up animation, damage states, destruction)
| Category | Count | Unit cost band | Subtotal band |
|---|---|---|---|
| Faction structures (~14 each) | 28 | £300-£750 | £8.4k-£21k |
| Defences/turrets (animated) | 8 | £300-£700 | £2.4k-£5.6k |
| Neutral/tech structures | 6 | £250-£600 | £1.5k-£3.6k |
| **Structures subtotal** | **42** | | **£12.3k-£30.2k** |

### Environment
| Item | Scope | Band |
|---|---|---|
| Terrain tileset/biomes (3 biomes: temperate, arid, urban-ruin) | textures, props, doodads, resource fields | £6k-£15k |
| Map props/decals library | ~80 pieces | £2k-£5k |

### VFX
| Item | Scope | Band |
|---|---|---|
| Weapon/explosion/superweapon FX library | ~60 effects | £4k-£10k |

### 2D / UI / narrative
| Item | Scope | Band |
|---|---|---|
| UI kit (sidebar, HUD, menus, icons ×~120) | full game | £4k-£10k |
| Unit/building cameo icons | ~80 | £1.6k-£4k |
| Key art + Steam capsule set | marketing-critical, human artist | £1.5k-£4k |
| Motion-comic briefings (16 missions, ~6 panels each) | ~100 illustrated panels + light animation | £6k-£18k |
| Faction emblems, logo, style bible illustration support | | £1.5k-£3.5k |

### Audio
| Item | Scope | Band |
|---|---|---|
| Soundtrack (~45 min commissioned, streaming-safe licence) | £250-£700/finished minute | £11k-£31k |
| SFX library (weapons, UI, ambience; mix of licensed packs + custom) | | £3k-£8k |
| VO: 2 announcer voices + ~30 unit bark sets | session fees + usage | £4k-£12k |

## 2. Scenario totals

| Scenario | Approach | Total band |
|---|---|---|
| **Lean** | Low-band rates (Eastern Europe/SEA outsourcing, junior-mid freelancers), licensed SFX heavy, static briefing art, 30-min soundtrack, some AI-assisted concept/iteration (disclosed per Steam policy, final assets human-finished) | **£55k-£75k** |
| **Standard** | Mid-band, mixed seniority, motion comics as specced | **£90k-£130k** |
| **Premium** | High band, senior artists, extended soundtrack, animated briefings | **£150k-£210k** |

## 3. Reality check against revenue

Using the doc 07 comparable: a well-received niche classic RTS at ~£20-25 can plausibly do 20k-80k units in year one at indie scale (Tempest Rising's estimated ~$10M gross is the publisher-backed ceiling, not our planning case). At 30k units × £22 × ~65% after Steam cut/VAT/refunds ≈ **£430k gross margin** - Standard art spend is viable *if* the wishlist gate (doc 07 §5) is met. At 8k units ≈ £115k, only Lean survives. **Hence the rule: no art spend beyond style bible + vertical slice placeholder tier (~£8k-£12k) until the wishlist gate reads.**

## 4. Spend sequencing (matches doc 05 phases)

1. **Phase 1-2 (≤£12k):** style bible, one biome, ~6 units + 8 structures per faction at vertical-slice quality, temp music, key art draft. Everything else placeholder.
2. **Alpha gate:** read wishlists → pick Lean/Standard. Commission in batches with contract milestones; no full-roster commitments upfront.
3. **Beta:** briefings, final soundtrack, VO - the most cuttable items go last.

## 5. Cost-control levers (ranked by savings vs damage)

1. Squad-shared infantry rigs/anims (already assumed) - big saving, zero player-visible cost.
2. Modular structure kits per faction (shared bases + unique tops) - ~25% structure saving.
3. Motion comics → static art with pans - saves ~£8k, campaign feel suffers moderately.
4. 30-min looping soundtrack vs 45 - saves ~£5k-10k; music is a pillar for P1, cut last.
5. Third biome deferred to post-launch map pack - saves ~£4k, acceptable.

## 6. Contract requirements (every commission)

Written agreement covering: IP assignment (or exclusive perpetual licence), milestone payments, revision counts, source files delivered, credit terms, streaming-safe music licence, and a warranty that work is original and unencumbered. AI-assist usage disclosed both directions (their pipeline and ours) for the Steam disclosure log (agent A9 owns this).
