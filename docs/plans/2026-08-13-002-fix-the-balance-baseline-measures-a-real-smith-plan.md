---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
type: fix
created: 2026-08-13
---

# The balance baseline measures a real smith - Plan

## Goal Capsule

**Objective.** Every balance number this project has ever produced describes a smith who cannot
craft above **Fine**, because the measurement harness never plays the forge minigame. Replace the
single crippled baseline with **two** — a novice and a veteran — so the balance suite answers
"is this tuned for the people who actually play it?" instead of "is this consistent with an
artifact of the harness?"

**Product authority.** Owner, 2026-08-13, in this brainstorm: intent is **honest balance numbers**
(a measurement defect, not a player-facing one), calibrated against **two baselines, novice and
veteran** — balance must hold for both.

**Open blockers.** None. No GPU, no owner ear, no external dependency. The 53 existing balance
assertions will move; that re-baseline is the work, not a surprise.

---

## Problem Frame

**A human blacksmith is not capped.** A real craft carries a scored grade — the forge scorer, or a
puzzle scorer for alchemy/tanning/engineering — and reaches Superior or Masterwork on skill.
`QualityRoller.AutoCraftGrade = 550` fires **only** when every scorer returned null.

That covers exactly three things: the `BaselinePlayer` harness, heirloom crafts
(`HeirloomHandlers` calls `RollActive` with no grade at all), and any craft that somehow skips the
minigame. Blacksmith is the only profession with `ActiveCraft: true`.

**550 sits exactly on the Common/Fine seam** (`< 550 → Common`, `< 780 → Fine`), and the jitter
half-width is 25. So every auto-craft in the game's history resolved inside **[525, 575]** —
Common or Fine on a coin flip, and nothing else, ever. The `isAutoCraft` hard-cap at Superior a few
lines below is vacuous: the value it caps cannot be approached.

**Therefore the measurement, not the game, is what was broken.** The 2026-08-09 finding — *of 176
items equipped by living heroes at day 100 across 11 seeds: Common 140, Fine 36, Superior 0,
Masterwork 0* — measured the harness. It was read at the time as "the player's own work was never
better than shop stock," and that reading is wrong for a human player.

**The balance suite is green, and that is the problem.** 53 of 53 pass on `6177103` (measured
2026-08-13, 4m22s). They pass *against* a baseline that cannot exceed Fine, so they certify
consistency with a broken reference — not balance. **Superior and Masterwork gear has never been
exercised by a balance gate at all.**

**Already fixed, do not re-fix.** The ~90% craft refusal (PT26) is gone: `BaselinePlayer` now asks
`ActionLegality.IsLegal` rather than hand-rolling the material check, which had missed the
material-efficiency discount and the tier gate. The code comment records it in past tense. Task #77
still says "CONFIRMED" and is stale — close it.

---

## Product Contract

### Actors

| ID | Actor | Stake |
|---|---|---|
| A1 | The balance suite (`Category=Balance`) | Consumes the baselines; every gate it asserts is only as meaningful as they are |
| A2 | Whoever tunes the game next | Reads balance output to decide whether a lever moved the right thing |
| A3 | The player, indirectly | Never sees this change; is the person the numbers are supposed to be about |

### Requirements

| ID | Requirement |
|---|---|
| R1 | Two named baselines exist — **novice** and **veteran** — distinguished by the craft quality they achieve, and both drive the balance suite |
| R2 | The veteran reaches **Superior and Masterwork**; the novice stays mostly **Common/Fine**. Neither is a fixed grade: both produce a distribution across a campaign |
| R3 | A balance gate that holds for one baseline and fails for the other is a **finding, and must be visible as one** — not silently averaged away or resolved by loosening the gate |
| R4 | Both baselines are **deterministic**: same seed plus same baseline yields identical state (hard rule 5). Quality variation comes from the kernel's injected RNG stream, never from wall-clock, ambient state, or a second draw |
| R5 | The existing 53 assertions are **re-baselined against the new references**, and each moved number is recorded with its before/after — a moved gate is evidence, not noise |
| R6 | Auto-craft's grade stops being the harness's de-facto skill model. Whatever value survives for the genuine auto path (heirlooms) is chosen **on its own merits**, and the vacuous Superior hard-cap is either made reachable or removed |
| R7 | `docs/design/MAKERS-MARK.md`'s §11.8.2 record of the 2026-08-09 ruling is corrected: the measurement described the harness, and a human player was never subject to the cap |
| R8 | Sim purity holds — no Godot reference, no wall-clock, no transcendental `Math.*` in sim code (hard rule 4) |

### Acceptance Examples

| ID | Given | When | Then |
|---|---|---|---|
| AE1 | A 100-day campaign on the **veteran** baseline | Equipped-gear quality is censused at day 100 | Superior and Masterwork both appear; the distribution is not confined to two adjacent bands |
| AE2 | A 100-day campaign on the **novice** baseline | Same census | Mostly Common/Fine, with the shape a struggling player would produce — and it is a distribution, not a single band |
| AE3 | The same seed, the same baseline, run twice | States compared | Byte-identical (golden-replay contract) |
| AE4 | A balance gate that passes for the veteran and fails for the novice | The suite runs | The failure is attributed to a named baseline in the output, so the reader knows which player the game fails |
| AE5 | An heirloom forged from a fallen hero's gear | Quality is rolled | Resolves on a deliberate rule for the auto path, not on a constant that exists to model a player who is not there |

### Scope Boundaries

**In scope** — the harness, the balance assertions, the auto-craft constant's justification, and
the two docs that record the superseded finding.

**Out of scope, and each for a stated reason:**

- **The player-facing power curve.** Gear stats, level scaling and venue difficulty stay untouched. The finale gates currently pass; changing the curve *and* the ruler in one move would make both unreadable.
- **The forge minigame's own difficulty.** PT13 ("winnable but punishing") is a real, separate complaint about how the puzzle feels to play.
- **Making a human player's crafts better.** They were never capped. This wave changes no player-visible behaviour.
- **`RivalCatalog`'s flat-Common shop stock.** Whether the rival should sell better goods is a design question this measurement fix does not settle.

### Assumptions

| ID | Assumption | If wrong |
|---|---|---|
| AS1 | A synthetic performance grade can represent a player's minigame skill faithfully enough to calibrate balance — without simulating the minigame itself | The harness needs a real scorer driven by a scripted input trace, which is materially more work |
| AS2 | The 53 assertions can be re-baselined without their *intent* changing — only their numbers | Some gate was encoding the broken baseline as a design target and needs re-deciding, not re-numbering |
| AS3 | Two baselines are affordable in balance-suite runtime (currently 4m22s) | Reduce seed count per baseline, or run the veteran on the full sweep and the novice on a subset |

### Success Signals

- A tuning question can be answered with "it holds for the veteran but breaks the novice at day N" — a sentence that is impossible to say today.
- The gear-quality census over a campaign spans more than two adjacent bands.
- No balance number in the repo any longer traces back to a smith who cannot exceed Fine.

### Outstanding Questions

| ID | Question | Owner |
|---|---|---|
| OQ1 | Should the novice baseline be *bad at the minigame* or *inconsistent at it*? Different distributions, different failure modes the suite would catch | Implementation, informed by PT13's own finding that the puzzle punishes |
| OQ2 | Does the veteran also play the shop and camp verbs better, or is skill scoped to crafting only? Scoping to crafting keeps this wave narrow | Planning |
| OQ3 | What is the right auto-craft grade for heirlooms once it stops modelling the harness? | Planning; small, and R6 forces the question to be asked rather than inherited |

---

## Sources

- `sim/GameSim/Crafting/QualityRoller.cs` — `AutoCraftGrade`, the band table, the vacuous auto-craft cap
- `sim/GameSim/Crafting/CraftingHandlers.cs:160-176` — where a real grade comes from, and when it is null
- `sim/GameSim/Crafting/HeirloomHandlers.cs:130` — the one genuine auto-craft path in the played game
- `sim/GameSim/Harness/BaselinePlayer.cs:55-80` — the craft policy, and the PT26 fix recorded in past tense
- `sim/GameSim.Tests` `Category=Balance` — 53 passing on `6177103`, measured 2026-08-13
- `origin/fix/power-growth-reaches-the-finale` — the unmerged 2026-08-09 branch; its measurement is the evidence, its conclusion is the part this corrects
