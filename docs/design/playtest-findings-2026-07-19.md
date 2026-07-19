# Playtest findings — 2026-07-19 (SP-1 pilot: 3 personas × seed 2026 × 25 days + graphics capture)

First run of the SP-1 self-playtest harness (`docs/design/2026-07-19-flavorforge-erenshor-recommendations.md`
Part 3). Three LLM personas (min-maxer, cautious-casual, chaos-monkey) played `GameSim.Cli`
seed 2026 for 25 days each via the deterministic re-pipe loop; a graphics agent ran the engine
suite (123/123 green) and captured all 7 tabs. Every finding below reproduces with seed 2026 +
the referenced transcript (scratchpad archives; transcripts embedded in session logs — re-run
recipe: pipe the quoted commands into `GameSim.Cli --seed 2026`).

Severity: **P0** = core loop unreachable/misleading, **P1** = real defect, hurts play,
**P2** = polish/design question.

## P0 — CLI command surface

1. **H#/I# ID-format trap (all 3 personas, independently).** Every list displays ids as `H1`/`I12`,
   but every ID-taking command (`stock`, `price`, `unstock`, `send`, `buyore`, `recall`) parses
   only the bare number — `stock I12 50` and `recall H1` fail with the same generic
   `? unknown command` a nonsense string gets. Two personas concluded the whole shelf loop was
   *unimplemented* (14 failed attempts, chaos-monkey; min-maxer never stocked anything in 25
   days); cautious-casual found `stock 12 20` works only by brute-force syntax guessing.
   Repro: fresh game → `craft dagger copper` → `next` → `items` (shows `I12`) → `stock I12 50` →
   unknown command; `stock 12 50` → queued.
   **Fix direction (small, CLI-only):** accept `H#`/`I#` prefixed forms everywhere a bare id is
   parsed + emit distinct errors (unknown verb vs bad argument vs wrong phase).

2. **`send` unusable in practice.** Chaos-monkey failed with every plausible syntax including
   bare numerics during a live Camp phase with a held consumable. Even if some exact numeric form
   works, three experienced testers could not find it — same trap class as #1.

3. **buyore timing reads as broken.** The Evening Ledger prints its own suggested command
   (`buyore 1 iron 3`) but: (a) buying immediately in that same tick can be rejected
   (`No open ore offer` / `Only 2 iron offered; asked for 3` printed in the SAME tick that
   advertised 3); (b) the ledger `next` also rolls to Morning, so following the printed hint
   afterwards yields `REJECTED: BuyOreAction during Morning`; (c) chaos-monkey confirmed offers
   only become purchasable the FOLLOWING Evening. Whatever the intended rule is, the game
   actively teaches the failing pattern. Repro: fresh game → `next`×5 → `buyore 1 copper 1`.

## P1 — feedback + visibility defects

4. **Silent bounty lifecycle.** Escrow works, but: no command shows active bounties (`board` shows
   an unrelated depth leaderboard); resolution/refund is invisible (min-maxer's 20g escrow
   silently refunded by day 10, zero narration; chaos-monkey's floor-5 bounty sat invisible for
   25 days; a paid bounty is detectable only by diffing a hero's overnight gold).

5. **Queued-action feedback inconsistency.** `craft` echoes `queued:`; `buyore`/`stock`/`talent`
   are silent on entry (success and failure identical) and rejections surface a full phase later
   with no back-reference. Invalid `craft`/`bounty` args also echo `queued:` then reject later.

6. **Hero Level is always L1.** 25 days, floor-4 clears, 700g purses — every hero remains `L1`
   (all 3 personas). Either leveling is unimplemented or the display is unwired; roster showing a
   dead stat undermines the progression fantasy.

7. **Empty-state inconsistency.** `items`/`board`/`gossip` print NOTHING when empty
   (indistinguishable from a typo); `mats` prints a friendly empty line.

8. **Graphics (from tab captures, engine suite green):**
   - **Depths tab header renders one-character-per-line** ("T/H/E/ /M/I/N/E…") — the exact
     collapse class `LayoutTests` hunts; the `VenueTileSize` guard at `DepthsPanel.cs:35-38` is
     not holding.
   - **Hero roster cards never show name captions**: `UiKit.ArtRect`'s real-art success branch
     returns a bare TextureRect and drops the `caption` param; only the no-art fallback builds a
     Label. Real art = normal case → names effectively never render in the grid.
   - Narrow cards wrap item names mid-word ("Pine/Buckle/r", "Soldier/'s/Longs/word").
   - Console prose prints mojibake where glyph headers are used (codepage, CLI-only).

9. **Prose/log defects:** same kill fact printed 3× in one tick (inline + `KillingBlow:` +
   recap); dead hero still listed on `board` 20 days later; recall success message uses the same
   leading `?` glyph as errors; name pool collides hard (three "Cedany", two "Ulf" in one
   25-day run → ambiguous gossip); flavor templates visibly recycle by day ~15.

## P2 — balance/design observations (for Brian to rule on, not auto-fix)

10. **Day-10+ flatline (all 3 personas).** Veterans plateau at floor 3 forever (smart-continue
    competence cap?), never die again, bank unbounded gold (700g+ by day 25); every later recruit
    dies within 1–2 days under-equipped; player gold freezes for 16–20 straight days; 64–80% of
    a 25-day run is pure spectation. Even with the shelf loop working, tier-1 goods become
    irrelevant against hero wealth inflation.
11. **Talents are free** — all 8 nodes by day 4, no cost/budget → checklist, not decisions.
12. **Phase order and multi-party behavior are learn-by-poking** — nowhere stated; `day` silently
    skips that Evening's buy window; `ExpeditionDeep` unexplained.
13. **Charming + worth amplifying:** item combat-history surfacing via `gossip`/`items` (crafted
    items remember kills/saves even after sale) delighted the cautious persona — candidate for
    the living-world wave's bark/bubble surfacing.

## Disposition

- #1/#2/#5/#7 + `?`-glyph → **one CLI-UX fix PR** (parser prefixes + distinct errors +
  entry echoes + empty-state lines). No sim changes.
- #8 Depths collapse + captions + mid-word wrap → **one UI fix PR** (UiKit/DepthsPanel layout).
- #3/#4/#6/#9-13 → need design rulings (buyore window rule, bounty visibility surface, leveling,
  name pool size, balance curve) — queue for Brian; several map onto already-planned waves
  (living-world barks #13, Erenshor waves).
