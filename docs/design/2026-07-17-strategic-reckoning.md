# Maker's Mark — Strategic Reckoning

## 1. Where we actually are

You have built an excellent *engine* and almost no *game*. The deterministic sim spine is real and rare-quality: ~6,300 lines, 14 modules, ~400 tests, all five roadmap cores merged and gated by a balance sim. But every core is proven by exactly **one** reference — 1 profession (Blacksmith), 3 melee roles and zero casters, 1 venue (Mine), 1 faction — and two of those cores ("core != live": frozen venue rotation, dormant faction surcharge) don't even fully execute in play. The only visual game is hand-authored placeholder SVG in a bare Control-tree town; the entire 2.5D direction is paper, and the Godot client structurally *cannot render* the lighting model you committed to. **Honest split: mechanism ~85-90% of the v1 spine, but playable-game-as-a-player-experiences-it ~15%, and the seen/felt/watchable game ~5%.** And the single most important thing — whether watching this loop is *fun* — has never once been tested against a human.

## 2. The core tension

**You are optimizing the part of game-making that is measurable (tests, determinism, pipelines, design docs) to avoid the part that is subjective and load-bearing (is it fun to watch, and does it look like anything).** The strategic question underneath everything: *Is the moment-to-moment loop actually entertaining — and can a deterministic, pre-resolved sim ever feel like a raid you want to watch, rather than a spreadsheet that scrolls?* Every downstream decision (breadth vs. graphics vs. more cores) is premature until that is answered. All five lenses converged here independently.

## 3. What needs to change (ranked)

1. **Redefine "done" from "tests green" to "I watched or played it."** — The reward signal is currently an engineer's, not a game-maker's; nothing on screen ever surprises or delights, which is exactly what burns out a solo game dev.
2. **Validate fun before adding anything.** — You have never playtested the CLI for real; if one profession/one venue is already thin, the lever is depth-of-loop, not breadth — and adding content onto an unfun loop is the most expensive possible mistake.
3. **Confront the determinism-vs-tension conflict head-on.** — Expeditions resolve at departure and reveal on return, so you read a settled result; spectator fun needs live resolution. Streaming the already-computed combat log tick-by-tick preserves determinism *and* restores drama — cheap, and it targets the exact reason watch-loops work.
4. **Stop building infrastructure ahead of validated need — especially the art contract.** — The GameArt spec/registry/conformance harness solves *large-inventory parallel fan-out*, a problem your own `fanout-strategy.md` says does not exist at this scale ("one lane, one tiller, not N parallel workers"). It has produced zero in-engine pixels and gates nothing shippable.
5. **Resolve the orphaned `docs/graphics-2.5d` branch and reconcile scope authority.** — A full session's uncommitted work (art scaffold + 2 docs + a deny-listed `Game.sln` edit) will rot into merge/decision debt; and the 5-phase roadmap vs. the 11-pillar master catalog must be declared executable-authority vs. idea-backlog, or "are we done?" stays permanently unanswerable.

## 4. Recommended path

**Prioritize proving the loop is fun and visible — in that order — before any content breadth or art industrialization. Take the "prove the unproven path" position, not "add more of the proven one."**

Concretely, the next arc:

- **First, a brutal fun spike (costs nothing).** Play `GameSim.Cli` for 30-60 minutes and answer in writing: *do I want to keep watching?* This gates everything. If the answer is no, no amount of Tanning professions or lit buildings saves it.
- **Then attack the see-it loop, not more infra:** (a) add a CLI autoplay/watch mode that runs a campaign and narrates beats live — including streaming the combat log tick-by-tick to test whether live reveal fixes the determinism-deadens-tension problem; (b) de-risk the render path with the graphics doc's *own* pilot — migrate **one** building to Sprite2D + CanvasModulate + one Light2D + one Laigter normal map from *existing* flat art. Prove sim → Godot → lit visual **once**, end-to-end, before generating a single new asset.
- **Lock the engine order before any scene surgery:** land the Godot 4.7.1 upgrade as an isolated infra task *first*, then do the Control→Sprite2D migration once against the target engine — not twice on an engine you plan to abandon.
- **Commit or shelve `docs/graphics-2.5d` this week.** Keep the cheap reusable core (`asset-style-spec.md`, the null-tolerant IconRegistry binding). Park the GameArt conformance contract as premature — right pattern, wrong time — do not expand it until real breadth arrives. Sanction the `Game.sln` edit, gitignore bin/obj, delete the `GodotClient.csproj.old*` orphans and retire `tools/AssetGen`, and flag the ComfyUI/SDXL/LFS platform dependency per org rules.

**Pause:** all new design docs, the 9-agent design workflow, the art fan-out machinery, and the hard caster/Necromancer/multi-venue work (those depend on unbuilt Wave-2 cores and must never be dispatched as "data").

**Only after the loop is validated fun and renders once:** do exactly one Wave-1 content add-on (Tanning) to re-prove the modular claim on something the game actually gains from — then alternate content and visual slices in short sessions.

Rationale: the sim spine is banked and safe. The two things that can actually kill this project — the loop isn't fun, and the visual path doesn't work — are precisely the two things never tested. Spend the next hours there. Breadth and art infrastructure are real eventual lanes, but investing in them now is building the back half of the bridge while the front half is unproven.

## 5. Open strategic questions for you

1. **Is your hobby the *game* or the *machinery*?** If building the deterministic engine and pipelines is itself the fun, most of this critique inverts and the current path is legitimate. Be honest — it changes everything.
2. **What is your "fun" hypothesis, in one sentence?** When you picture someone enjoying this, are they *watching* a raid unfold, *optimizing* a supply economy, or *savoring* attribution proof ("my sword mattered")? These want different games, and you haven't committed to one.
3. **Can a fully pre-resolved, deterministic outcome be fun to watch — and if not, are you willing to stream/reveal it live to fake the tension?** This is the premise's single biggest risk. Your answer decides whether the inverted-MMO idea survives contact with a player.
4. **What is the real breadth ceiling and timeline?** The vision (9 professions + full class roster + venues + economy + drama, plus the 11-pillar catalog) is a self-declared multi-year solo arc. Is that the actual target, or is a tight 2-3 profession / 2 venue slice the real "done"? "How far are we" is unanswerable until you pick.