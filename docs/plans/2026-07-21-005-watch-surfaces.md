# Watch-Surfaces — the Presentation Scheduler

Plan of record for the game's biggest de-risking: **can a pre-resolved deterministic raid be fun to WATCH?** Research basis: watchability report (paradox-of-suspense, Football Manager highlights, autobattlers, Omasse Grindcast, Orkin's law, Blaseball, RimWorld letter tiers).

## The insight
Suspense is **viewer-side**, not sim-side. A decided outcome, streamed with controlled information release + honest near-misses + a commentary layer, is indistinguishable from live. The risk was never determinism — it's presentation without characters, pacing, or stakes (the Swag & Sorcery trap). So we build a reveal machine.

## Why early / sequencing
Cheap, CLI-first, and it directly tests the premise on the loop that *already exists*. Can run parallel to Phase A/B. The **Presentation Scheduler abstraction must exist from day one** so every later surface (ticker, mirror, town ceremony) is just a renderer over one beat stream.

## Determinism
Scheduler is **pure and seedable**, `Harness`-style purity: `CombatLog → List<Beat>`. Same log + same seed = same broadcast (golden-testable). Any presentation-only jitter uses a **separate presentation RNG stream — never the sim kernel** (KTD2/KTD4 preserved). No sim change → no re-baseline.

## Units

### U-W1 — Presentation Scheduler (core)  [M]
- `Beat{revealTimeShopClock, tier(Ambient|Glance|PullFocus), telegraphLine, resolveLine, cameraHint, attributionRefs}`. Transform the deterministic combat log into a paced beat list.
- **Pacing rules (the scheduler's contract):**
  1. Map expedition-time → shop-time non-linearly: travel/quiet compresses to ticker one-liners; encounters dilate. Target: raid broadcast ≈ one craft cycle.
  2. Every tier-2+ beat = **telegraph → hold (1–3 s) → resolve**; hold scales with computed stakes (HP-% swing, kill potential).
  3. **Beat budget per raid:** ≤1 pull-focus + 4–6 glance + unlimited ambient. Highlights = scan log for max HP-swing / closest call / first use of a player-crafted item (FM highlight picker; trivial because the log is complete before broadcast).
  4. **No-leak invariant** (cardinal): UI state (rosters, ledger, inventory) mutates ONLY when its beat plays. Enforced in code, **tested**.
  5. Honest near-miss detection: flag beats where a hero crossed <15% HP or armor converted a lethal hit → long hold + dedicated bark. Never fabricate.
  6. Delivery jitter ±10–20% on ambient spacing (presentation RNG only).
- **Tests:** beat determinism (log+seed→beats); no-leak (no state read before its beat); budget caps; near-miss flagging.

### U-W2 — Raid Ticker (Grindcast descendant)  [M]  — the MVP surface
- Persistent feed, hero-voiced first-person + terse narrator. **Bark table keyed by event × personality × relationship-to-player-gear.** Color-tiered (gray/amber/red), scrollback, audio chime per tier (the multitasking channel — player's hands are on the forge).
- **Orkin's law:** if a bark doesn't say it, it didn't happen — every kill/save names the gear and, when it's the player's work, says so ("The Riverfang — Torvald's new blade — shears the ogre's arm").
- Ticker lines seed post-raid tavern gossip (feeds the Drama/Legend layers).
- CLI first, Godot HUD panel later.
- **Tests:** bark selection determinism; tier routing; attribution line present on kill/save beats.

### U-W3 — Return summary card  [S]
- Outcome + one auto-picked turning point rendered as 3 lines of prose + the attribution readout ("Your craftsmanship: 3 kills, 1 life saved — Sera, turn 41"). The thesis statement as a first-class field.

### U-W4 — Scrying Mirror (PiP → highlights)  [M]  — post-skeleton polish
- Always-on frame near the forge: idle shimmer + party silhouettes + one momentum rune (Dota win-graph collapsed to a glyph). Glance beats pulse + 2–3 s abstract vignette (dots/silhouettes over a mine map — **deliberately abstract; do NOT attempt full 3D combat choreography** — FM blobs > bad mocap). Pull-focus expands fullscreen telegraph-hold-resolve. Post-raid 20 s highlight replay.

### U-W5 — Town ambient + stakes  [M]  — post-skeleton
- Departure ceremony (party walks past shop with your gear visible), light in-raid town reflection, survivors walk back in (embodied reveal, not popup), broken gear returns to the counter, graves accrete. Optional departure prediction/wager (Blaseball-proven).

## Attention tiers (RimWorld letter stack, formalized)
- **Ambient** — scrolling ticker, no sound (flavor, travel, minor skirmish).
- **Glance** — soft chime + colored pulse; readable <2 s (injuries, durability, boss engage).
- **Pull-focus** — hard interrupt, forge minigame grace-pauses, mirror expands (death, first-kill record, party rout). Rare; if it fires twice a raid it's noise. **Never punish looking away** — everything recoverable via scrollback/recap.

## MVP watch-surface (skeleton)
U-W1 + U-W2 + U-W3 + 3 hero-voice bark variants per event type. This alone converts "spreadsheet scroll" into a broadcast. Defer mirror visuals, town ceremony, betting, replays — but the Scheduler exists from day one.

## Top mistakes (guardrails)
No outcome leak (any side channel kills all future suspense) · no fabricated drama (players audit vs summary) · no uniform pacing · named prose not numbers · bark the intelligence or it reads as dice · confident abstraction over bad 3D · ambient-first (respect the forging hands) · give the watcher skin in the raid · raids leave a trace in town.
