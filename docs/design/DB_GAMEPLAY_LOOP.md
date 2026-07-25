# Dungeon Bodega Simulator — Full Gameplay Loop & Systems Reference

**Game:** Dungeon Bodega Simulator — Alien Fruit (solo dev "moss"), released 2026-03-23, $9.99, Windows + Linux/SteamOS, Steam app 3458180 (free demo: app 4035470, also on itch.io).
**Reception:** Very Positive, ~98% of 260+ Steam reviews. 17 achievements. "No gen AI used" badge.
**Genre profile (Steam tags):** Shop Keeper, Farming Sim, First-Person, Physics, Resource Management, Cozy, Life Sim, Crafting, Immersive Sim, Dark Fantasy, Retro/Old School 3D.

**Premise:** Elm Myrkwater, a recently laid-off adventurer, is gifted a run-down bodega inside a dungeon by a distant uncle. Turn the "cold prison" into a welcoming supply stop for adventurers and dungeon locals, save up for **One Last Gig**, and along the way help Elm get over his imposter syndrome and work up the courage to return to adventuring. Story is delivered through letters and NPC interactions.

**Structure:** 24 in-game days, ~3–4 hour main story. Day/night cycle with **no clock pressure** — the day advances when the player chooses. **Infinite Mode** unlocks after completing the story. The demo covers the first 6 days plus its own endless mode.

Tags: **[C]** = confirmed from store page, dev site, itch page, patch notes, press, or player reviews. **[I]** = inferred; verify in the demo before building.

---

## 1. The core loop — one in-game day

```
WAKE → FARM CHORES → PRODUCTION (brew / forge) → ORDER SUPPLIES → STOCK SHELVES
     → OPEN & SERVE → CLOSE → SLIME CARE → (optional: DUNGEON DELVE / SOCIAL) → SLEEP
```

Player reviews describe the loop as "ordering, stocking, slime caretaking, blacksmithing, cat petting" with a meditative, chill pace [C]. Ordering of activities within a day is flexible because there's no timer [C]; the sequence above is the natural flow [I].

### 1a. Farming
- Plant, water, harvest. Demo: 3 crops + 1 secret crop; full game: **12+ unique crop types** [C] (older marketing said 8+; 12+ is the later demo-page figure).
- Crops are the base input for everything: sold raw, processed into items/products, brewed into potions [C].
- Growth appears to be day-gated (harvest cycles across sleeps) [I].

### 1b. Production — three value tiers [C]
1. **Process** crops into supplies/products for adventurers.
2. **Brew** potions and elixirs — "significant gold," the flagship money-maker. Brewing has its own station and (per dev) needed a dedicated tutorial, so it's a multi-step interaction, not one click [C].
3. **Forge/blacksmith** expensive weapons and tools — the high-capital, high-price tier [C].

### 1c. Supplies ordering [C]
- A **supplies ordering form / order panel** exists — you buy stock you can't grow (the panel lists items by name per a patch note). This is the wholesale channel that complements farming.
- Lead time / delivery mechanics unknown [I — verify].

### 1d. Stocking & selling
- First-person, physics-based item handling; you physically place stock on shelves [C from tags + genre].
- Customers: friendly adventurers, dungeon dwellers/creatures, locals, returning regulars, and **former co-workers** from Elm's adventuring days [C].
- **Daily demand rotation:** each day has different demands you can adapt to for more gold [C, dev site]. This is the strategic layer — read tomorrow's demand, plant/brew/order accordingly.
- Checkout interaction (register vs. hand-to-hand) unknown [I — verify in demo].
- New customer types unlock as you improve and upgrade the bodega [C].

### 1e. Slimes [C]
- Pet dungeon slimes: feedable, pettable.
- **Economic function:** slimes eat unused/junk items and convert them into potions — the game's waste-disposal → production loop. "Not picky but quite hungry" = a recurring upkeep sink.
- **Breeding:** raise and breed slimes to unlock **special items** you can sell [C, itch page]. So slimes are also a collection/genetics mini-system with exclusive inventory as the reward.
- A "How do I get rid of unwanted items?" forum thread confirms slimes are the intended junk sink [C].

### 1f. The bodega cat [C]
- Pettable. Mechanically light, tonally essential.

---

## 2. Progression systems (across the 24 days)

- **Gold** — spent on upgrades, expansion, and supply orders; accumulating the One Last Gig fund is the story win condition [C].
- **Player level** — a leveling track exists and gates content alongside gold [C, from a player review noting "many things being locked between player level and gold"]. XP source unknown (likely sales/quests) [I].
- **Upgrades** — plentiful; player chooses what to upgrade to optimize the shop; upgrades also unlock new customer types [C].
- **Relationships** — befriend regulars; dialogue and letters carry the storyline; the narrative is skippable but present, with a "heartfelt message" (imposter syndrome → courage arc) [C].
- **Quests** — full game advertises quests beyond the demo [C].
- **The dungeon mystery** — a second, parallel loop: explore beneath the shop, solve puzzles, uncover secrets [C]:
  - At least one gate requires **3 blue keys / 3 completed puzzles** to open a door [C, patch notes].
  - A maze section exists (achievement: "Scary Maze Survivor") [C].
  - Other achievements point to books/library puzzles ("Books Open Many Doors") and a revelation ("Ancient Mysteries Revealed") [C].
  - Solving the central mystery is tied to finishing the story and unlocking Infinite Mode [C, press].
  - Reviewers call the puzzles "neither too easy nor too hard" and note "secrets to uncover" beyond the critical path [C].

---

## 3. Modes & extras

- **Story mode:** 24 days, ~3–4 h [C].
- **Infinite Mode:** post-story endless play [C].
- **Demo:** first 6 days + endless continuation, 3+1 crops [C].
- **Twitch integration:** chatters appear in-game as customers; anyone in chat can pet the bodega cat [C]. If replicating: a lightweight IRC/EventSub client that maps chat events to customer spawns and one interaction verb.

---

## 4. Reception notes — what worked and what didn't

Worth encoding into any clone/homage:

**Praised [C]:**
- The chill, meditative loop; freedom in how to play "without being overwhelming."
- A real storyline with heart layered on a genre that usually has none — reviewers repeatedly cite this as the differentiator ("a shop simulator with heart," "not just copy and paste").
- Music and retro visuals.
- Worthwhile-feeling progression; plenty of upgrades *plus* a reason beyond upgrades to keep playing.

**Criticized [C]:**
- Text needed proofreading at launch (since patched).
- **Under-utilized mechanics:** because content is gated by player level and gold within a short 24-day story, a reviewer notes you can plausibly finish **without ever touching slimes or smithing**. Design lesson: in a short game, every major system needs a mandatory story touchpoint or it becomes optional decoration.
- Launch bugs around the blue-key puzzle chain (soft-locks, since patched with auto-open fallback + debug-menu recovery) [C]. Lesson: puzzle-gate state needs to be resilient across saves; ship a recovery path.

---

## 5. Design read — why the loop works

1. **Two loops, opposite moods.** Cozy shopkeeping above, dark-fantasy puzzle dungeon below. Each is the palate cleanser for the other; the "Dark Fantasy + Cozy" tag pair is the identity.
2. **No timers anywhere.** The day ends on the player's command. All pressure is strategic (what to grow/stock for tomorrow's demand), never mechanical (reflexes/speed).
3. **Demand rotation is the whole strategy layer.** It forces diversification across crops/potions/forged goods instead of a single optimal product, and it's why ordering + farming + brewing all stay relevant daily.
4. **The slime is a genius component:** pet (emotional attachment) + garbage disposal (solves the genre's junk-inventory problem) + breeding metagame (exclusive sellables). Three systems in one cute object.
5. **Short and dense.** 24 days means the pacing curve is authored day-by-day — letters, unlocks, and puzzle progress are scripted against the calendar, not emergent.
6. **Story as retention.** The imposter-syndrome arc gives a reason to finish that upgrades alone don't.

---

## 6. Handoff notes for Claude Code

Assuming the goal is a game *in the style of* DBS (mechanics aren't protectable; names, characters, art, dialogue, and music are — don't lift them):

**Vertical slice (build first):**
- One shop room + grow plot, first-person, simple physics pickup/place.
- 4 crops, 1 brew station (2-ingredient recipes), shelf stocking, hand-to-hand checkout.
- 6-day scripted demand rotation + one letter per morning (story stub).
- Gold + 3 upgrades (extra plot, extra shelf, brew station tier 2).
- Sleep-to-advance; no clocks.
- Phase 2: slimes (feed → potion output), ordering form, forge, dungeon room with one 3-key puzzle gate. Phase 3: breeding, Infinite Mode, Twitch.

**Architecture:**
- **Data-driven everything:** crops, recipes, items, customers, the per-day demand table, letters, and unlock gates live in JSON/YAML under `data/`, schema-validated at load. The 24-day pacing script is a data file.
- **Deterministic, headless-runnable sim core:** seeded PRNG, fixed timestep, sim separated from presentation. CI balance sweep: assert a naive strategy survives the calendar and a competent one hits the story gold target with margin.
- **Resilient gate state** (per the blue-key lesson): puzzle/quest flags stored as idempotent facts, re-derived on load ("if all 3 puzzles done → door open"), plus a debug recovery menu from day one.
- **Save format versioned** from the first build — the real game had to ship save-migration fixes.
- **Dual gating rule:** every major system (slimes, forge) gets at least one mandatory story beat so it can't be skipped entirely — direct fix for the game's main criticism.
- **Engine:** Godot 4 fits retro-PSX 3D + physics interaction well; keep the sim layer engine-agnostic.

**Verify in the demo before locking designs [all currently [I]]:** checkout interaction, price-setting (whether prices are player-set at all), order form lead times, crop growth timing, XP source, and how demand is communicated to the player.
