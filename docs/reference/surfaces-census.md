# Maker's Mark — the surfaces census

*Branched from `2a9c27ca`. Every claim carries a `file:line` into that tree. Line numbers drift as files change; symbol names drift far slower — grep the symbol if a line number misses.*

This document is an exhaustive census of the game's **surfaces**: every scene, screen, panel, HUD element, input binding, art/audio asset, and the wiring between them, plus the CLI as a second surface. The sim's rules are a sibling document's job; copy transcription is another sibling's. Where copy is quoted here it is only to identify an element.

Method notes:
- Reader counts come from `grep` over `godot/scripts/` (production client code). `godot/tests/` and `sim/` are counted separately where it matters.
- "0 readers" always names the grep pattern used.
- No status/shipped/TODO language appears here (repo rule 8). This describes what is in the tree at `2a9c27ca`.

---

## 1. Scene inventory — and the one structural fact

There are **14 `.tscn` files**, and every one of them is a single node with a script attached. **The entire UI tree is built in C# at runtime** — node trees live in `_Ready()`/`Build()`/`EnsureBuilt()` code, not in scene files. Reading `.tscn` files tells you almost nothing about this game; reading `BuildUi()`-style methods tells you everything.

| Scene | Root node | Script | Instantiated by | When it appears |
|---|---|---|---|---|
| `godot/scenes/new_game_select.tscn` | `Control "NewGameSelect"` | `scripts/NewGameSelect.cs` | `project.godot:14` (`run/main_scene`) | Boot; and via "Save & quit to title" (`MainUi.cs:470` `TitleScenePath`, `MainUi.cs:5760` `SaveAndReturnToTitle`) |
| `godot/scenes/panels/main_ui.tscn` | `Control "MainUi"` | `scripts/MainUi.cs` | `NewGameSelect.cs:57` (`MainScenePath`), swapped in by Continue or Begin | The whole game |
| `godot/scenes/panels/forge_panel.tscn` | `Control "Forge"` | `panels/ForgePanel.cs` | `MainUi.cs:4043` (`InstantiatePanel`) | Drawer, id "Forge" |
| `godot/scenes/panels/shop_panel.tscn` | `Control "Shop"` | `panels/ShopPanel.cs` | `MainUi.cs:4044` | Drawer, id "Shop" |
| `godot/scenes/panels/heroes_panel.tscn` | `Control "Heroes"` | `panels/HeroesPanel.cs` | `MainUi.cs:4045` | Drawer, id "Heroes" |
| `godot/scenes/panels/tavern_panel.tscn` | `Control "Tavern"` | `panels/TavernPanel.cs` | `MainUi.cs:4046` | Drawer, id "Tavern" |
| `godot/scenes/panels/depths_panel.tscn` | `Control "Depths"` | `panels/DepthsPanel.cs` | `MainUi.cs:4047` | Drawer, id "Depths" |
| `godot/scenes/panels/bounty_panel.tscn` | `Control "Bounties"` | `panels/BountyPanel.cs` | `MainUi.cs:4048` | Drawer, id "Bounties" |
| `godot/scenes/panels/demand_panel.tscn` | `Control "Demand"` | `panels/DemandPanel.cs` | `MainUi.cs:4049` | Drawer, id "Demand" |
| `godot/scenes/panels/hero_panel.tscn` | `Control "HeroPanel"` | `panels/HeroPanel.cs` | `MainUi.cs:4050` | Drawer, id "HeroCards" (HUD button labeled Renown) |
| `godot/scenes/panels/ledger_modal.tscn` | `Control "LedgerModal"` (starts `visible = false`) | `panels/LedgerModal.cs` | `MainUi.cs:4141` (`GD.Load<PackedScene>`) | Evening reveal / tray button |
| `godot/agentplaytest.tscn` | `Node "AgentPlaytest"` | `godot/scripts/tools/AgentPlaytest.cs` | dev only — `tools/agent-playtest.ps1` launches it by path | never in play |
| `godot/fullplaytest.tscn` | `Node "FullPlaytest"` | `godot/scripts/tools/FullPlaytest.cs` | dev only — launched by path, must run windowed (`FullPlaytest.cs:36`) | never in play |
| `godot/scenariowriter.tscn` | `Node "ScenarioWriter"` | `godot/scripts/tools/ScenarioWriter.cs` | dev only — gated on env var `ScenarioWriter.cs:20-25` | never in play |

Panels **not** backed by any scene (code-built `new`): `ProgressionPanel` (`MainUi.cs:4051`), `LessonsPanel` (`MainUi.cs:4052`), `MineWatch` (`MainUi.cs:4098`), `CompanionDock` (`MainUi.cs:4112`), `TabFade` (`MainUi.cs:4131`), `RaidForecastBoard` (`MainUi.cs:4150`), `CommissionBoard` (`MainUi.cs:4163`), `LegendsWall` (`MainUi.cs:4173`), `PledgePanel` (`MainUi.cs:4190`, new — see §7), `CampPanel` (`MainUi.cs:4199`), system menu (`_systemMenu`, `MainUi.cs:4209`, built by `BuildSystemMenu` at `MainUi.cs:5640`), `ObjectiveTracker` (`MainUi.cs:4224`), `TutorialFlow` (`MainUi.cs:4265`), `ScryingMirror` (`MainUi.cs:4308`), `PipDock` (`MainUi.cs:4314`), `TutorialOverlay` (`MainUi.cs:4326`), `MentorBanner` (`MainUi.cs:4340`), `BuildStamp` (`MainUi.cs:4428`), `CounterPanel` (owned by ShopPanel, embedded), `ProvenanceCard` (one per hosting panel), `TellingPanel` (new — owned by `LedgerModal`, not `MainUi`; constructed at `LedgerModal.cs:2049`, its only host — see §7). (`BestiaryPanel` was here too until #770 deleted it; `ChronicleScroll` and `AdventureTicker` were both here too; both classes are deleted — P2-MEMORY-14 and P2-MEMORY-12 — and neither's job left a code-built panel behind: see §7's `LegendsWall` row and §8 below.)

### project.godot facts (`godot/project.godot`)

- Main scene: `res://scenes/new_game_select.tscn` (`project.godot:14`).
- Window: 1152×648 explicit, **no stretch mode** — panels tuned without it (`project.godot:22-23`).
- **No `[input]` section and no autoloads.** Every input action is registered at runtime (`TownInput.cs:16-24`, `MinigameInput.cs:35-67`, `MainUi.cs:4218` `RegisterQuickTravelActions`) because `project.godot` is deny-listed for agents.
- Icon: `res://icon.svg` (`project.godot:12`).

---

## 2. Boot: the title screen (`NewGameSelect.cs`, 1,093 lines)

One centered wood card (`CardWidth = 600f`, `NewGameSelect.cs:195`) over a `SurfaceDeep` full-rect backdrop. Exactly one of four views is visible at a time: **title menu → picker → primer**, plus **settings**.

| Element | file:line | Shows / does |
|---|---|---|
| `Continue` button + blurb | `NewGameSelect.cs:426-457+` | From `CampaignSave.Peek()` envelope only (world not deserialized): profession name, day, phase via `PhaseVocab`, saved-at time. Absent when no valid save. Primary-styled. Pressed → `CampaignSave.TryLoad()` → `MainUi.AdapterOverride` → scene swap |
| `NewGame` | button that sets `_picker.Visible = true` (`OnNewGamePressed`, `:539+`) | → picker. Primary-styled only when no Continue row exists |
| `SettingsButton` | `:403` | → shared `SettingsPanel` instance |
| `Quit` | `:415-416` | `GetTree().Quit()` |
| Profession picker | around `:591-640` | One `Pick_{id}` button + blurb + "Your workshop: the {nametag}" note per `ProfessionRegistry.All` entry; shared starter-kit note; `PickerBack` (`:636`) |
| Primer ("Your first day") | around `:675-730` | `FantasyNote` (`:189`), `MainUi.PhaseLegend` verbatim, `ClockNote` built from `PhaseVocab.BellVerb` (`:175`), seed label/field, `Begin` (primary, `:725-726`), `Back` |
| Returning-smith choice | around `:822-868` | Visible on the primer only when `TutorialFlow.HasPriorProgress` (`:905`); two toggle buttons `RunCourse`/`SkipCourse` + note. Skip → `TutorialFlow.ResetForReturningSmith()` at Begin; otherwise `ResetForNewGame()` |

Begin also: clears the campaign save, sets `MainUi.FirstMorningBeatPending = true` on **both** branches so Bryn's cold-open fires on the next mount, builds `GameComposition.NewCampaign(seed, profession)`. **Seed source is no longer uniformly wall-clock.** For a returning smith, or any profession other than Blacksmith, `SeedSource()` still draws wall-clock (`Time.GetTicksUsec()`, `:64`) and the seed field shows/edits that number. But a **fresh save's first Blacksmith pick** (`!TutorialFlow.HasPriorProgress && professionId == BlacksmithId`, `OnProfessionPicked`, `:889`) pins a fixed constant, `WarrantSeed`, instead — the seed field is hidden (`:897-899`) and replaced by fictional framing text, `WarrantFictionName = "The Warrant — through day three, the Mine keeps no one."` (`:112`). Every other path (returning smith of any profession, or any non-Blacksmith fresh pick) keeps the wall-clock draw and the visible/editable seed field exactly as before. F11 works on this screen too, now via `_UnhandledKeyInput` (`:282-292`) rather than `_Input`.

---

## 3. The shell: `MainUi` (6,091 lines)

### 3.1 Layout regions

`MainUi` is a full-rect Control holding, in order (`BuildUi`, `MainUi.cs:3447+`):

1. **`Layout` VBox** (`:3466`): `HudHeader` (wood panel, two rows, `PanelContainer`, `:3475`) → `ToastBanner` (`:3983`, hidden unless a toast is live) → **`WorldSlot`** (ExpandFill, `:4011-4015`) containing `Town2D` full-rect (`Town`, added `:4018`) **and, since a visfix, `DrawerHost` too** (`Drawer`, added `:4078` — see below). The world sits **in layout flow** below the header — the header never occludes it. (An `AdventureTicker` row used to close this stack below `WorldSlot`; P2-MEMORY-12 deleted it, and `WorldSlot` now simply keeps the full remaining height instead of splitting it with the ticker.)
2. **`DrawerHost` moved inside `WorldSlot`, not a MainUi-root sibling.** A real, documented regression ("fix/visfix1", comment at `MainUi.cs:4067-4076`) found that a FullRect Control added as a MainUi-root sibling *after* `layout` paints over the header's own rows (measured: the action-slot pip row clipped from 5 to 3, the rejection toast cut off mid-sentence, both exactly at the drawer's left edge — because Town2D and DrawerHost both used to be later siblings of `layout`, and a later sibling paints over an earlier one). The fix parents both `Town2D` and `DrawerHost` **inside** `WorldSlot` instead of as MainUi's own siblings, so occlusion is structural (pinned by `HudBoundsTests.WorldRegion_NeverIntersects_TheHudHeader`) rather than merely unlikely.
3. **Remaining overlay siblings of `MainUi` itself** (draw above `Layout` in add order, `CompanionLayer` CanvasLayer 40 at `:4110-4111` with `CompanionDock`, `TabFade` CanvasLayer 100 at `:4131`, `LedgerModal` (`:4141-4149`), `RaidForecastBoard` (`:4150`), `CommissionBoard` (`:4163`), `LegendsWall` (`:4173`), **`PledgePanel`** (`:4190-4191`, new — took the row `BestiaryPanel` vacated when #770 deleted it), `CampPanel` (`:4199`), system menu (`:4209`), `ObjectiveTracker` top-right dock (`:4224+`), `TutorialFlow` dock below it (`:4265+`), `ScryingMirror` (`:4308`), `PipDock` (`:4314`), `TutorialOverlay` (`:4326`), `MentorBanner` (`:4340`), interact-prompt chip (`:4415+`), `BuildStamp` (`:4428-4429`).

`MineWatch` is constructed once (`:4098`) and parked inside `DepthsPanel` (`Depths.MountWatch(Watch)`, `:4101`); `ScryingMirror` borrows it while open — the "exactly one live SubViewport" constraint still holds.

### 3.2 HUD header row 1 — stat chips (`RefreshStatus`, `MainUi.cs:2432+`)

Rebuilt clear-then-compose on every tick. Wrapped in `StatChipsWrap` (`:3507`, fixed height, ClipContents) so its minimum width can never push the window wide. **P2-LONG-17 regrouped this row into three clusters and removed two of the original ten chips outright** — `RentChip` and `AssessmentChip` no longer exist anywhere in `MainUi.cs` (grep `RentChip`/`AssessmentChip` → 0 hits each). The doc's old flat ten-chip table is gone; the current row is:

**CalendarCluster** (`:2445`): `DayChip` (`:2450`, day number) · `PhaseChip` (`:2456-2475`, + tooltip `PhaseLegend`; `PhaseVocab.Display(state)`; width reserved against `PhaseVocab.AllLiveWords` so a phase-word change never shoves the row) · `ActChip` compact badge (`:2479-2503`, campaign act I/II/III, `ArcActRoman(state.Arc.Act)`).

**WealthHandsCluster** (`:2504-2536`): `GoldChip` (icon + big value, `BuildGoldChip`, `:2786`; bounce-pop on a player-shelf sale, `:2565-2571`, gated on `ItemSold{FromPlayerShop:true}` in `LastEvents`) · `HeroesChip` (`:2513-2519`, alive/total, shield glyph stands in for a party glyph) · `SlotPips` (`BuildSlotPips`, `:2816`, 5 pips lit = remaining action slots) · `StandingChips` (`BuildStandingChips`, `:2856`, one chip per faction with non-zero standing).

**DuesCluster** (`:2538-2562`): **`ConfidenceChip` only.** Doc comment at `:2538-2549` states the redesign explicitly: Rent is "demoted to a Morning-only line on the clock banner" (`RentMorningLine`, `MainUi.cs:3191`, folded into `UpdateClockLabel`'s tail at `:3131` — no chip, no pay verb); the Guild Assessment's heartbeat "now speaks through the guild assessor's own in-world caption (`Town2D.BuildAssessor`) instead of a bar chip" (see §4.2's new Assessor NPC). Confidence is the one gauge judged to never go quiet (it can still collapse the era), so it stayed and grew into the freed width as a full `NamedStatChip` with its word label restored (previously a compacted icon+value pill).

| Chip | file:line | Shows | Fed by |
|---|---|---|---|
| `DayChip` | `:2450` | day number | `state.Day` |
| `PhaseChip` | `:2456-2475` | Dawn/Prepare/Quest/Vigil/Deep Vigil/Night | `PhaseVocab.Display(state)` |
| `ActChip` | `:2479-2503` | campaign act I/II/III | `state.Arc.Act` |
| `GoldChip` | `:2786` (built), `:2513` (mounted) | gold; bounce-pop on a player-shelf sale | `state.Player.Gold`, `ItemSold{FromPlayerShop:true}` |
| `HeroesChip` | `:2513-2519` | alive/total | `state.Heroes` |
| `SlotPips` | `:2530`, built `:2816` | 5 pips, lit = remaining action slots | `state.ActionSlotsRemaining` vs `ActionBudget.SlotsPerDay` |
| `StandingChips` | `:2535`, built `:2856` | one chip per faction with non-zero standing | `state.Player.Standing` |
| `ConfidenceChip` | `:2554-2562` | town confidence % | `state.Rent.ConfidencePermille` |

Rent and guild dues are no longer HUD chips at all — read them via the clock-label tail line and the in-world assessor NPC's caption, respectively.

### 3.3 HUD header rows 2-3 — clock caption, timeline, verbs, books tray

**Now three header rows, not two** (a layout fix split what used to be one row): row 1 is the stat chips (§3.2); **row 2** is the `ClockLabel` caption alone, full-width (`MainUi.cs:3544`) — pulled out of `VerbCluster` onto its own line because a long sentence there ("Quest — the gate stands quiet today — nobody marching today bought anything off your shelf") was setting the whole row's minimum width past the window; **row 3** (`HudHeaderRow`, `:3542`) holds the three original zones — the day-timeline (left, ExpandFill), the primary verb cluster (center), the books tray (right) — 16px apart.

- **`DayTimeline`** (in `TimelineWrap`, `:3560-3568`): five phase pills in kernel order with current/past/future styling + a pulsing ember "waiting" dot (labels from `PhaseVocab`; dot shown when `Clock.AutoAdvance && Playing && Engaged`).
- `ClockLabel` caption (`:3544`; text logic `UpdateClockLabel` — phase name, hold explanation, departure omen, send-off sale beat, open-items badge, heroes-ready badge, and now also `RentMorningLine` (`:3131`, `RentMorningLine` defined `:3191`) — Rent's only remaining surface, a Morning-only tail line, no chip, no pay verb).
- **`VerbCluster`** (`:3580`): the **primary verb button `AdvancePhase`** (`:3592+`) — label is `PhaseVocab.BellVerb` ("Send them off"/"Snuff the lanterns"/"Hurry the day along"), or "Skip" in auto mode, or "Return to the vigil" when the vigil stop is armed with its slate closed; pressing during the raid span calls `Conductor.Hurry()`; at Morning it force-closes an open counter session first, with an honest toast.
- **`WatchButton`** ("👁 Watch", `:3706`): opens the Scrying Mirror; visible **only** during Expedition/Camp/ExpeditionDeep.
- **`AutoAdvance`** toggle (⏱, `:3718`, persists via `ClockSettings`), **`PlayPause`** (`:3730`), **`Speed`** 1×/2×/4× (`:3740`) — the latter two visible only while auto is on.
- **`Fullscreen`** (⛶ + shortcut badge, `:3755`; F11 also global in `_Input`).
- **`BellTray`** (`:3780`, rebuilt from `SimAdapter.PendingActions` via `RefreshBellTray`, `:2174`): one chip per bell-deferred action (`BuildBellTrayChip`, `:2197`) with a ✕ withdraw wired to `SimAdapter.Withdraw`; a failed withdraw toasts, never silent.
- **`BooksTray`** (`:3790+`): eight 28px icon-only buttons, full-sentence tooltips. Seven are gated by `SurfaceUnlocks` (§10.8); Lessons is deliberately ungated.

| Tray button | file:line | Opens | Gate (SurfaceUnlocks.cs) |
|---|---|---|---|
| `OpenLedger` (skull) | `MainUi.cs:3816` | `Ledger.ShowFor(LastCompletedDay)` | a party has departed |
| `OpenForecast` (depths) | `:3825` | `Forecast.ShowForTomorrow(state)` | first Evening reached |
| `OpenCommissions` (bounty) | `:3835` | `Commissions.ShowOpen(state)` | first `CommissionPosted` |
| `OpenLegends` (rune) | `:3843` | `Legends.ShowWall(state)` | first beat **or** first death |
| `OpenDemand` (gossip) | `:3855` | `OpenPanel("Demand")` | first `HeroPassedOnItem` |
| `OpenHeroCards` (shield, tooltip "Renown…") | `:3866` | `OpenPanel("HeroCards")` | first player-shop sale |
| `OpenProgress` (weapon) | `:3873` | `OpenPanel("Progress")` | first bounty paid |
| `OpenLessons` (armor glyph reused) | `:3943` | `OpenPanel("Lessons")` | none |

Gated buttons are greyed with the gate's reason as tooltip, never hidden; a one-line arrival toast fires the first tick a gate opens, suppressed when a rejection owns the strip.

Neither the Pledge modal (the assessor NPC, §4.2) nor the Telling (a Ledger beat-row button, §7) has a tray icon — both are reached only through their in-world/in-modal triggers, not the books tray.

### 3.4 Toast strip, interact prompt, build stamp

- **`ToastBanner`/`RejectionToast`** (`:3983-3992`): the one transient strip. Priority: refusal (friendly-phrased via `FriendlyRejection`, `:2906`) > narrator milestone text > `WorldNotice` (confidence collapse, climax, hero leaving, rival expansion, stipend) > cleared. 4s lifetime (`RejectionToastSeconds = 4.0`, `:52`), also reused for bell-queue acknowledgements (`ShowBellToast`, `:3432-3436`), gate-open arrivals, station flavor lines, no-target interact, party-returns notice.
- **`InteractPrompt`** chip (updated every frame, `UpdateInteractPrompt`, `:2352`): mirrors `Town.WorldInputNode.PromptText` verbatim ("E · Forge" or a flavor station's `HoverLine`), bottom-center, hugging its text, kept clear of the target's nametag (`InteractPromptWorldGap = 4f`, `:2314`).
- **`BuildStamp`** (`BuildStamp.cs`): dim top-left corner label from `res://assets/build_info.txt`, fallback "dev (unstamped)", CanvasLayer, never eats clicks.

### 3.5 Objective / tutorial docks

- **`ObjectiveTracker`** ("Today" card, `ObjectiveTracker.cs`): top-right, docked below the header's measured height (`UpdateObjectiveDock`, `MainUi.cs:2259`), content-height-fitted with viewport clamps. Advisor top pick + reason, expandable ranked list, tutorial checklist scroll, ✕ dismiss-confirm rows, ↻ re-ask button. Hidden while a drawer/modal owns the screen **except** when the tutorial is active with a drawer or room open, in which case it docks to the LEFT edge instead (`keepTutorialReadable`, `MainUi.cs:5981-5983`, `DockObjectiveHorizontally`).
- **`TutorialFlow` dock**: hosts only the earn-2nd-profession picker + quick-travel row; self-hides when neither is live. Stacked below the objective card, measured, clamped to the window, internally scrolling.

### 3.6 The clock, the conductor, the engaged latch

- **`PhaseClock`** (`PhaseClock.cs`): auto-advance **defaults OFF** (`:45`) — the day is bell-driven; a persisted opt-in restores the timed "Innkeeper's Clock" (`MainUi.cs:661-665`). Durations Morning 45s / Expedition 30s / Evening 45s, Camp+Deep borrow Morning's (`PhaseClock.cs:26-28`, `:68-77`). While `Engaged`, an expired timer holds at the boundary ("flows-but-waits", `:116-135`).
- **`RaidConductor`** (`RaidConductor.cs`): sequences Expedition→Camp→ExpeditionDeep as a show — beats `Idle/SendOff/VigilStop/DeepTick/Homecoming` (`:88-95`), pinned maxima SendOff 6s / empty beat 1s / deep show 3s / homecoming 12s (`:69-82`). `VigilStop` is timer-free and only ends via `ResolveVigil()` (`:288-296`), wired to the Camp slate's "Send them deeper" (`MainUi.cs:695-700`). Show timers stop dead while `Clock.Engaged` or the tutorial's Watch step is unanswered (`:204-218`); `Hurry()` (the player's own press) walks through the hold but never past `VigilStop` unseen (`:267-281`). Beat re-derived from sim state on every tick (`Resync` `:168-190`), including a resumed save parked at Camp (`:136-138`). Per frame, `MainUi._Process` drives exactly one of `Clock.Update` / `Conductor.Update` (`MainUi.cs:852-864`).
- **`UpdateEngaged`** (`MainUi.cs:5914`) is the one place the three distinct questions are answered: clock-hold (`Drawer.IsOpen || ModalOwnsTheScreen()`, `:4714-4715`, plus the day-1 craft→shelve pacing hold, folded in separately at `:5945-5949` so it never suppresses the objective chip or Town's world-input gate), world-input block (`Drawer.IsOpen || AnOverlayOwnsTheScreen()` — a walkable room does NOT block walking), and PiP suppression. **The overlay roster is no longer a hand-written array.** `OverlaySurfaces()` (`MainUi.cs:4768-4771`) now projects `SurfaceArbiter.Discover(GetTree())` — every constructor that calls `SurfaceArbiter.Claim(...)` with `SurfaceRegion.FullScreenModal` and `OwnsScreen: true` is automatically in this list, so a ninth surface can no longer omit itself the way a hand-written array once could (the array this replaced was missing `Chronicle`/`ChronicleScroll` before that class was deleted). The eight current `FullScreenModal` claims (`MainUi.cs`, `SurfaceArbiter.Claim` call sites): **Ledger** (`:4143`), **Forecast** (`:4152`), **Commissions** (`:4165`), **Legends** (`:4175`), **Pledge** (`:4192`, new), **Camp** (`:4201`), **SystemMenu** (`:4212`), **Mirror** (`:4310`) — `Bestiary` is gone from this list (the class is deleted, #770); `Pledge` fills the row it vacated. `ProvenanceCard`'s `SurfaceRegion.ChildModal` claim and `CompanionDock`'s `SurfaceRegion.HudDock` claim (`OwnsScreen: false`) are deliberately excluded from this projection — a nested card opening over its host must never read as the host releasing the screen.

### 3.7 The Evening reveal chain (Return Ritual)

Evening tick completes → `LastCompletedDay`/`_pendingLedgerDay` armed with an unscaled wall-clock gate; narrator trigger + loss count captured at the tick, not the reveal. When the gate elapses: `Ledger.ShowFor(day, ConsumeLedgerTip(), ConsumeFirstLossBlock())` (`MainUi.cs:1288-1289`), Bryn's loss voice line anchored at the Legends tray, the Proof-act voice anchored into the ledger's lead card, narrator line + death toll cue. Ledger close chains the `RaidForecastBoard` once per day-end, inheriting the resume-play intent. Every modal open pauses the clock and captures a resume latch; every close re-syncs the latch and fires any deferred mine-gate camera pan.

**On a night a hero died, the wake IS the evening (P2-PEOPLE-07), not a separate surface a player has to go find.** `LedgerModal.AddWakeLeads` (`LedgerModal.cs:820`) renders one lead card per `HeroDied` event that night — ahead of every other card in the grid, including the followed-item line — with a skull icon, "Fell on floor {N} — {cause}. Carrying {gear}.", an optional open-commission consequence line, and a "Sit the wake" button (`SitTheWake_{hero}`). Pressing it closes the Ledger and raises `SitTheWakeRequested` (`LedgerModal.cs:55`, `:857`), which `MainUi` wires (`MainUi.cs:4184`) straight into `LegendsWall.ShowActorPage(state, hero)` — the SAME "fallen's page" the Legends book's own index opens (§7's `LegendsWall` row), never a second one, going through the same `OpenGatedSurface("Legends", ...)` gate the tray button uses. The Ledger card grid, beyond the wake lead, now also composes (all `LedgerModal.cs` private methods, all "one shared fact, not one per hero card", plain/past-tense/no-verb-at-the-player copy): a camp's own receipt lines (`AddCampReceiptLines`, `:1109`, split so a camp that buried someone sits beside the wake lead and every other camp's receipt sits later), the followed item's own night (`AddFollowedItemLine`, `:903`), the narrator line (`AddNarratorLine`, `:919`), a gate-held streak line (`AddGateHeldStreakLine`, `:961`), rival-sale lines (`AddRivalSaleLines`, `:1007`, "the rival takes a name"), earmark/hold lines (`AddEarmarkLines`, `:1032`, "hold it for Torvald" payoff), and released-hold lines for a hold whose hero died (`AddReleasedHoldLines`, `:1070`, P2-PEOPLE-31 — "a hold for the dead is released at the wake").

### 3.8 System menu (pause)

Esc in the bare town opens it; Esc closes it first when open. Full-rect dim (no click-out) + centered wood card: `Resume` (primary), `SystemMenuSettings` (nested second `SettingsPanel` instance), `SaveQuitToTitle` (→ `SaveAndReturnToTitle`, `MainUi.cs:5760`), `QuitGame` (→ `SaveAndQuit`). The OS window ✕ / Alt+F4 is intercepted (`AutoAcceptQuit = false`, `:964`, `_Notification` `:1045-1051`) and saves before quitting. Autosave otherwise happens once per Evening tick, one rolling slot, deliberately anti-reroll.

---

## 4. The town (`Town2D.cs`, 2,913 lines + layout tables)

### 4.1 Canvas, camera, movement

- The world renders in a `SubViewportContainer` (`Stretch=true`, Nearest) wrapping a `SubViewport` with pixel snap + physics picking (`Town2D.cs:607-660`). Nominal canvas 640×360 (`:57-58`); the real canvas is window ÷ `CanvasShrink`, an integer derived so ~576px (36 tiles) of world is visible regardless of monitor (`:107`, `ShrinkFor` `:116`, re-derived on resize). Camera zoom is pinned 1 — StretchShrink is the only magnification dial.
- `Camera2D` follows the player every frame by assignment + built-in smoothing, clamped to the town rect (`Cam` built `:722`, `FollowPlayer` `:1532`). **Focus beats** are a timed borrow (`FocusOn` `:1624`): departure/return pans to the mine gate for 3.2s (`FocusOnMineGate` `:1641`, `MineGateFocusSeconds`), suppressed inside a room, deferred while any modal owns the screen. The tutorial re-ask still peeks briefly.
- **Player** (`PlayerController2D.cs`): `CharacterBody2D`, WASD at 90px/s (`Speed = 90f`, `:29`) + click-to-move seek (`MoveToTile` `:242`); real input cancels a seek. Art `player_smith` + `_step/_walk2/_walk4` frames, feet-origin, no nameplate by design.
- **World interact** (`WorldInput2D.cs`): per-physics-frame nearest-overlap scan (`_PhysicsProcess` `:59`), highlights the target, exposes `PromptText` (`:28`), E → `RaisePick()` (`:80-84`); E with nothing in range raises `NoTargetInteract` with an honest "Too far from the {name} — move closer." line (`:162`) toasted by MainUi (`MainUi.cs:4034`, `ShowBellToast`). Esc raises `CancelRequested` (`:92-95`).

### 4.2 The five buildings (`TownLayout2D.cs:220-227`)

64×44 tile grid, 16px tiles (`TileSize = 16`, `TownLayout2D.cs:21`; grid `GridWidth`/`GridHeight` `:133-135`). Venues: forge (18,26), market "Shop" (46,26), tavern (18,40), minegate "Mine Gate" (32,8), noticeboard "Bounties" (46,40) — SDXL exterior art. Cobble plaza + north road + door spurs (`PathRects` `:251+`); rally tile (32,14) (`RallyTile`, `:235`). Each `Building2D` (`Building2D.cs`) carries a sprite (feet-line origin), click `Area2D` (`Interact`, `:109`), blocking footprint (`FootprintHeightFraction = 0.6f`, `:52`, so the door row stays walkable), nametag label, `DoorAnchor` marker (`:113-115`), optional hover-line, optional "tell" glow for real-verb stations, highlight, and the tutorial pulse (`SetTutorialPulsing` `:346`).

Clicking (or E at) a building emits its lowercase key → `MainUi.OnTownBuildingClicked` (`MainUi.cs:5011`): a venue with an `InteriorLayout2D.Rooms` row (forge/market/tavern/minegate — all but noticeboard) **enters the walkable interior**; noticeboard opens the Bounties drawer; unknown keys fall through to "Town" (close drawer).

A sixth, standing `Building2D` sits in the open plaza, not one of the five venues: the **memorial wall** (`BuildMemorialWall`, `Town2D.cs:2350-2368`, fixed tile `TownLayout2D.MemorialWallTile = (54, 32)`), built once per campaign whether or not anyone has died. Its `Picked` event fires `MemorialWallClicked` → `MainUi.OnMemorialWallClicked` (`MainUi.cs:4996`) → `Legends.ShowWall(...)` — the same modal the tray button and the tavern's story wall open. It carries a physical lantern row, one per fallen hero (`RefreshMemorialWallLanterns`, `Town2D.cs:2381-2411`, reading `DramaState.Memorials.Count` directly — LAW 4, no second count), rebuilt only when that count changes; a deathless campaign shows the wall with zero lanterns rather than hiding it.

Clicking a wandering **hero** opens the Heroes drawer with that hero selected (`OnTownHeroClicked` `MainUi.cs:4980`) — this is the *only* way into the Heroes roster panel.

Two named, permanent world NPCs are `TownsfolkNpc2D` instances distinct from the four cosmetic villagers below:
- **The rival smith** (`BuildRivalSmith`, `Town2D.cs:2183-2206`) stands permanently at the market's door anchor, never errands away, not clickable. His caption is a fixed tagline until a hero dies wearing no player-marked gear, at which point `RefreshRivalAbsenceLine` (`:2213-2242`) speaks one line naming that hero, once, via `RivalPack`/`FlavorEngine` — "the shop the town would have if your hands didn't matter."
- **The assessor** (`BuildAssessor`, `Town2D.cs:2264-2296`) stands permanently at the noticeboard's door anchor and **is clickable** (`clickable: true`, `:2280` — the one townsfolk NPC the player can click). His `Picked` event fires `AssessorClicked` → `MainUi.OnAssessorClicked` (`MainUi.cs:4990`) → `Pledge.ShowPledge(...)` (§7). His caption is the live guild-dues reading in his own voice (`AssessorLine`, `Town2D.cs:2297-2301`), refreshed whenever the reading changes (`RefreshAssessorLine`) — this replaced a permanent HUD stat chip (`AssessmentChip`, now fully removed — grep `AssessmentChip` in `MainUi.cs` → 0 hits; §3.2's chip table must not list it).

### 4.3 Props, ambience, day tint

- Props (`TownLayout2D.Props`): well, 4 plaza lanterns, 12 perimeter trees (swaying, per-instance phase — `SwayingTreeSprite2D`), 2 crates, plus the "warm-hub" prop set (market crates, second flyer board, string lanterns, ore cart, forge salamander, laundry line, tavern cat) — all resampled offline to draw size. A former duplicate well was deleted per owner ruling.
- `AmbientLife2D` (`AmbientLife2D.cs`): chimney smoke (forge+tavern), dusk fireflies, per-lamp flicker, market awning sway, mine-mouth dust, noticeboard paper flutter, per-venue window glow anchored to each building's real sprite size — all phase-driven via `SetPhase`.
- `StationBreath2D.cs`: the idle-breath treatment for a *static* station sprite — a plain C# driver reusing `SpriteMotion` (the same idle-pose accumulator `HeroActor2D`/`TownsfolkNpc2D` use for walking actors, fed a zero velocity so only the breathing squash/stretch ever fires). Wired to exactly one station today: the mentor's (Bryn's) workshop station (`InteriorRoom2D.cs:205`, `_mentorBreath = new StationBreath2D(station.Sprite, phaseSeed: 0f)`) — no other station uses it.
- `DayPhaseTint` eases a full-canvas `CanvasModulate` per phase; a warm constant overrides it inside interiors.
- Forge FX: glow overlay + spark burst + steam plume near the forge door, driven by `ForgePanel` during the minigame.
- Hero actors (`HeroActor2D.cs`): one per alive hero (`ReconcileHeroes`, `Town2D.cs:759,773`), class body art with walk frames, nameplate, wander-drift + real errands to venue doors/corner tiles, rally→march-out→away→walk-in choreography driven by `Town2D.OnPhaseCompleted` (`:1202`) with an 8s minimum "show floor" before survivors re-emerge (`MinDelveShowSeconds = 8f`, `:293`). Townsfolk (`TownsfolkNpc2D.cs`): 4 purely cosmetic villagers (`TownsfolkHomeTiles.Length`, `TownLayout2D.cs:439`), never clickable by default (`BuildTownsfolk`, `Town2D.cs:2108-2163` never passes `clickable: true`) — the class itself gained an opt-in `clickable` constructor param (used only by the assessor above), so "purely cosmetic, never clickable" still holds for these four specifically. Tavern seating for present heroes with mood glyphs (`TavernLife2D.cs`); market-room customer choreography off real shop events (`MarketLife2D.cs`).

### 4.4 Walkable interiors (`InteriorLayout2D.cs`, `InteriorRoom2D.cs`)

Rooms are far-off "islands" in the same world. `EnterInterior` (`Town2D.cs:966`) teleports the player to the room's door, clamps the camera to the room rect, re-points interact scanning at the room's stations; exit is Esc or walking onto the door's `ExitZone` (`InteriorRoom2D.cs`, wired `Town2D.cs:1942-1946`) — both funnel through `ExitInterior` (`Town2D.cs:1008`).

Stations are `Building2D`s mounted at the town's flat Y-sort scope. A station's identity is data (`StationSpec` `InteriorLayout2D.cs:82-106`): a **real-verb** station carries `Action` (a drawer id or modal route) + `Verb` + `Copy` (toasted on press) + optional `Focus`; a **flavor** station carries `HoverLine` + `FlavorLine` (toasted on press — never a dead click). Routing: `MainUi.OnStationActivated` (`MainUi.cs:5181`) → Bryn's station speaks the current lesson; flavor toasts; real verbs go to `OnInteriorHotspotActivated` (`:5250`) — `"Bestiary"`→ removed (see §14), `"Legends"`→`Legends.ShowWall()`, `"Watch"`→`Mirror.ShowMirror()`, else `OpenPanel(action)` — then `ForgePanel.FocusSection(focus)`.

Station tables:
- **Workshop** ("forge" venue, composed per profession — `WorkshopRoomFor` `InteriorLayout2D.cs:296-311`, union of `WorkshopVocab.StationsFor` sets + Bryn's station appended unconditionally). Blacksmith: anvil→Forge/craft, furnace→Forge/foundry, bellows→Forge/craft (CombinesWith anvil), quench trough (flavor), material shelf→Forge/materials, finished-goods rack→Shop (`WorkshopVocab.cs:110-128`). Alchemist: cauldron/still→craft, reagent shelf→materials, potion rack→Shop, herb bundles (flavor) (`:141-149`). Engineer: bench/gear-rack/parts-crate/flywheel (`:159-165`). Tanner: scrape-frame/hide-rack/goods-rack/vats (`:175-181`). The workshop rebuilds on next entry when professions change (`Town2D.cs:1646-1666`); nametag/signboard follow the primary profession (`:1420`, `:1439-1458`).
- **Market**: sales counter→Shop ("Haggle"), wares shelf + curio shelf→Shop, ledger desk (flavor), stock crates (flavor) (`InteriorLayout2D.cs:190-201`).
- **Tavern**: hearth (flavor), bar→Tavern ("Order a Round", CombinesWith table-b), story wall→**Legends** modal, fireside table→Tavern ("Eavesdrop"), corner table→Tavern ("Swap Stories") (`:214-237`).
- **Gatehouse** (minegate): overlook→**Watch** (Scrying Mirror), muster board→Depths, bounty ledger→Bounties, gate winch (flavor) (`:252-260`).

---

## 5. Input census

All actions are runtime-registered; the InputMap is the union of three registrars.

| Action | Keys | Registered at | Consumed by |
|---|---|---|---|
| `move_up/down/left/right` | W/S/A/D + arrows | `TownInput.cs:18-21`, also `MinigameInput.cs:40-43` | `PlayerController2D`; minigame cursors |
| `interact` | E | `TownInput.cs:22` | `WorldInput2D._PhysicsProcess` (`WorldInput2D.cs:68-78`) |
| `cancel` | Escape | `TownInput.cs:23` | `WorldInput2D.cs:80-83`; plus raw-Escape handlers below |
| `forge_strike` | Space | `MinigameInput.cs:45` | `ForgeMinigame` |
| `bellows` | Shift | `:46` | `ForgeMinigame` (held) |
| `plunge` | Space/Enter/KpEnter | `:47` | `QuenchMinigame` |
| `confirm` | Enter/KpEnter | `:48` | minigame prompts |
| `scrape` | Space | `:49` | `TanningFrame` |
| `crank_stroke` | Space | `:50` | `EngineeringBench` |
| `pull_part` | Backspace/Delete | `:51` | `EngineeringBench` |
| `docket_toggle` | C | `:59` | `MainUi._UnhandledKeyInput` → `Docket.Toggle()` |
| `tutorial_reask` | R | `:66` | `MainUi.cs` → `ReaskTutorial` |
| `quicktravel_forge/shop/tavern/gate` | 1/2/3/4 | table `MainUi.cs:159-165`, registered `RegisterQuickTravelActions` `:5120-5133` | polled in `_Process` `:1430-1436`, gated inline on `Tutorial.QuickTravelUnlocked` |
| (raw key) F11 | F11 | none — matched raw | `MainUi._Input`; `NewGameSelect.cs` |

- **Escape ladder** (`MainUi._Input`, `:5359`): child overlays consume first (DrawerHost; every modal via `ModalEscape`); then the system menu closes itself; then the interior room exits; then, bare-town, Esc **opens** the system menu. `ModalEscape` usage: 18 files grep-hit (`grep -rl ModalEscape godot/scripts/`), of which 1 is the definition (`ui/ModalEscape.cs`) and 17 are consumers — `MainUi.cs`, the same 5 minigames, `SettingsPanel.cs` (under `ui/`), and 10 panels (`CampPanel`, `CommissionBoard`, `ForgePanel`, `LedgerModal`, `LegendsWall`, `ProvenanceCard`, `RaidForecastBoard`, `ScryingMirror`, and the two added since the last pass, `PledgePanel`/`TellingPanel`).
- `ShortcutMap` (`ShortcutMap.cs`) is the one registry that renders bindings (Settings legend + tooltips); key labels are read live off the InputMap so rebinds (SettingsPanel C3) show through. Rebinding persists via `UiSettings.ApplyPersistedBindingIfAny` (`TownInput.cs:41`, `MinigameInput.cs:108`).
- Mouse: building/hero/station click-picking via `Area2D` physics picking inside the SubViewport; drawer dim veil click-out closes (`DrawerHost.cs`); ShopPanel drag-and-drop shelf stocking (`ShopPanel.cs:858-1090`, `_GetDragData`/`_CanDropData`/`_DropData`); bounty poster drag (`BountyPanel.cs`, `PosterComposer`); everything else is buttons.

---

## 6. Drawer panels (`DrawerHost`, one at a time, right-anchored 600px)

`DrawerHost.cs`: registration `Register` (still 10 ids — Forge/Shop/Heroes/Tavern/Depths/Bounties/Demand/HeroCards/Progress/Lessons, `MainUi.cs:4081-4090`), `Open` replaces, dim-under click-out + Esc close, ~0.22s accumulated-delta slide. `MainUi.OpenPanel` (`MainUi.cs:4451`) is the single router: gate check, refresh-on-open, per-building entrance cue, `Tutorial.NotifyPanelOpened`, read-only-surface first-touch lesson for HeroCards/Depths/Heroes.

| Drawer id | Panel (file) | Shows | Player verbs (control names) |
|---|---|---|---|
| Forge | `ForgePanel.cs` (2,965) | Tab row Craft/Materials/Foundry (`:2523-2525`); craft view: feedback line, needs-row (`BuyMat_{key}` twin, `:1277`), material select (`MaterialSelect` `:2567`), modifier selects Oil/Rune/Fit, recipe cards ordered tier-then-id with art/stat chips/material chip (now also naming the marcher the item would arm, P2-PEOPLE-22), locked rows naming their gate talent, talent cards, docket button (`OpenDocketFromForge` `:2679`); materials view: vendor rows (one per `MaterialRegistry.PricedPool` key with qty stepper, `BuyMat_{key}` `:726`) and Foundry section (tier/coal/flux chips, `UpgradeForge` `:816`, `BuySupply_{coal,flux}` `:827`) | `Craft_{id}` (auto-craft, `:1036`), `WorkForge_{id}`/`Brew_{id}`/`Assemble_{id}`/`Scrape_{id}` (per active profession, `:1076`), `ForgeAnother_{id}` (repeat trace, `:1083`), `Masterwork_{id}` (`:1137`), `Commission_{id}` legendary, `Unlock_{node}`, `BuyMat_{key}`, `BuySupply_{key}`, `UpgradeForge`, `OpenDocketFromForge`. A repeat craft now also shows the pending batch-echo grade and uses-left on the recipe card (`CraftingHandlers.PendingEchoGrade`, `:1215-1218` — see §14.3, `BatchEcho` is no longer an invisible blind spot) |
| Shop | `ShopPanel.cs` (1,085) | "Who Would Buy This" (`:212`), "Your Shelf" cards + pass reasons (`:281`), "Unshelved Crafts" (`:459`), "Rival Shelf" read-only incl. a "Rival Edge" stat chip reading `RivalMarketSharePermille` (`:660-665`, P2-HONEST-17 — see §14.3); drag-and-drop stock/unstock; auto-suggested prices | `Unstock_{id}` (`:321`), `Reprice_{id}` via `PriceTag` (`:353`), `Provenance_{id}` History (`:356`, `:576`), `Present_{id}`/`Suggest_{id}` while a customer is at the counter (`:415-416`), `Stock_{id}` + `StockPrice_{id}` (`:543-556`) |
| — (embedded in Shop) | `CounterPanel.cs` (1,172) | Customer card w/ class icon + mood bucket, Interest/Patience/**Standing**/Round chips (`BuildMeters`, `:410-423` — the old Goodwill chip is gone; `CounterState.GoodwillPermille` is a per-session fleece-memory number nothing player-facing reads, P2-ONBOARD-09; `HeroChips.StandingChip` renders the same relationship band `HeroPanel`/`CampPanel` show instead), presented item, standing offer, `CustomerWalked` reasons, walk-away speech bubble, drawn `CounterDesk` (`:435`). New: when the shelf holds nothing the customer wants, the card offers a discoverable jump to the Forge (`OpenForgeRequested`, P2-PEOPLE-21 "Forge it — Torvald waits", forwarded by `ShopPanel.cs:97`) | `OpenCounter` (`:210`, Morning-only mirror), `CloseCounter` (`:238`), `Accept` (`:624`), `HoldFirm` (`:626`), `Counter` + `CounterPrice` CoinStack (`:659`) |
| Heroes | `HeroesPanel.cs` (618) | Portrait-grid roster (2-wide, class-tinted `PortraitFrame`s), detail pane: worn gear w/ mark tallies (`LedgerQuery.MarkTally` `:275`), item memories, needs signal, relationships | roster card click (selects), `Provenance_{gearId}` History (`:298`) — otherwise read-only |
| Tavern | `TavernPanel.cs` (988) | "TAVERN GOSSIP", "WORK THE ROOM — IN THE COMMON ROOM" w/ per-patron Pursue rows (commission / ore / arc scene), "THE HANDSHAKE" for the pursued thread — retitled "A WORD AT THE BAR" and built ABOVE the room when the pursued thread is an arc scene (P2-PEOPLE-01) — "OUT AT THE MINE" | `Pursue` (adapter-local selection), `Pursue_Scene_{hero}` + `SceneClose_{hero}` (arc scenes — queue NO action and change no sim field), `HandshakeAccept_{hero}`/`HandshakeDecline_{hero}` (commissions), `HandshakeBuy_{hero}` (ore), `TavernHistory_{hero}_{slot}` |
| Depths | `DepthsPanel.cs` (353) | The parked `MineWatch` strip (resting host) above a single venue tile holding the deepest-floor-per-hero board (`DramaState.DepthsBoard`) | none — read-only |
| Bounties | `BountyPanel.cs` (665) | "OPEN BOUNTIES" cards w/ `BountyJudged` sticky notes (`:94`), resolved judgments (`RenderJudgment` `:192`), "POST BOUNTY" form: `MineCrossSection` floor picker (`:69`), `CoinStack` reward, draggable `PosterComposer` (`:71`, `:493+`) | `PostBounty` or drag the poster onto the board |
| Demand | `DemandPanel.cs` (171) | "WHAT HEROES ARE PASSING ON" (`:50`), "OPEN COMMISSIONS" (`:69`), "DEPTH STALL — CALL TO ACTION" (`:97`), "BOUNTY BOARD" with per-floor minimums (`:124`) | none — read-only |
| HeroCards | `HeroPanel.cs` (373) | "HEROES" card list: class, standing band, summed deeds, deepest floor, XP + Veterancy (cosmetic), Venue (real `LadderRank` standing), needs/relationship chips | none — read-only |
| Progress | `ProgressionPanel.cs` (275) | Profession-switch header (checkboxes + `ConfirmProfessions`, bell-rider) above five ladder cards (Forge/Depth/Roster/Wealth/Chronicle) from `ProgressionSpineSystem.Compute` | `ConfirmProfessions` |
| Lessons | `LessonsPanel.cs` (291) | Every registry row's ShortLabel + TeachNote, chapter-numbered by act, plus every fired first-touch lesson — permanent, survives dismiss/complete | none — read-only |

---

## 7. Modal overlays (full-rect siblings above the drawer)

All pause the clock while visible and restore play state on close (visibility handlers `MainUi.cs:4488-4676`). All close via their own Close button and Esc (`ModalEscape`).

| Modal | file | Opened by | Shows |
|---|---|---|---|
| `LedgerModal` | `LedgerModal.cs` (2,131) | Return-Ritual auto-reveal; tray `OpenLedger` | Per-hero return cards (`LedgerQuery.ReturnCards`) in a wrapping grid: fate line, gold purse/earned chips, attribution beats (lead card sorted first — `LedgerCard_0`) with a per-beat "Ask how it happened." button (`AskHowItHappened_{beatId}`, `:1528`) into `TellingPanel` (P2-PROOF-07, see its own row below), warrant-save line, "ORE OFFERED" rows with `BuyOre_{hero}_{mat}` (`:1568-1578`, Evening-gated mirror of `OreMarketHandlers`), tutorial tip, first-loss block, narrator line, "THE RETELLING" (`RetellingHeader`, `:1749`), `CloseLedger` (`:2044`). **New: `AddWakeLeads` (`:820-865`, P2-PEOPLE-07 "death-night staging")** — on a night any hero died, one lead card per `HeroDied` event renders FIRST in the grid (`WakeLead_{heroId}`), before the ordinary return cards: fallen name, floor + cause, gear carried, any open commission the death orphaned, and a **"Sit the wake"** button (`SitTheWake_{heroId}`) that closes the Ledger and fires `SitTheWakeRequested` → `MainUi.cs:4184` → `Legends.ShowActorPage(state, hero)` — jumping straight to that hero's page in the Legends Wall (see that row below). "The wake IS the evening, not a wall a player has to go find." |
| `TellingPanel` (the Telling) | `TellingPanel.cs` (782) | The Ledger's own "Ask how it happened." button, its **only** host (constructed inside `LedgerModal.cs:2049`, never by `MainUi`) | P2-PROOF-02..07 (§11.15): link 4's counterfactual proof, staged rather than printed as one line. Replays the recorded fight round-by-round from a `TellingScript` (all arithmetic in `TellingQuery`; this panel only draws it), player-paced ("Continue" press, no timer, skippable via Close/Escape at any step). Four stages: `Framing` → `Factual` (mid-play) → `Fork` (desaturated hold, for the two shapes with a real counterfactual — `LethalSaveShape`/`PotionLifesaveShape`) → `Fall` (the held counterfactual death, same recorded rolls with the item removed, nothing rolled past the divergence) → `Verdict` (colour floods back, the mark stamps). Every number is a snap, never a tween — no engine tween anywhere in the class, so an HP label can only ever hold the exact recorded value. Plain `Control` tree of `UiKit.ArtRect` standees, not a lit `SubViewport` world the way `MineWatch` is |
| `RaidForecastBoard` | `RaidForecastBoard.cs` (724) | Day-end chain after Ledger; tray `OpenForecast` | One section per tomorrow's party (`RaidForecast.ForTomorrow`) now opens with a spoken line instead of a bare header: `MusterVoice.AnchorLine(party)` (`:162`, "the forecast gets a face", P2-MEMORY-20 — link 3, "no important information without a face", §11.7.3) gives the muster a person before the roster/target-floor/monster rows render, plus `MusterVoice.FollowedSendOffLine`/`BountySendOffLine` (`:91,170`) when a followed item or an active bounty is riding along; empty-slot asks with `ForgeOne_{hero}` / `TodoForge_{name}` jump buttons (`:333,499` → `OpenPanel("Forge")`), `ForecastClose` (`:244`). `MusterVoice.FollowedNightLine` also speaks into the Ledger's own night card (`LedgerModal.cs:910`) — the same voice, not a second one |
| `CampPanel` (winch-house slate) | `CampPanel.cs` (689) | Auto via `SyncCampModal` when a party parks; reopened by the bell while VigilStop is unanswered | Per-party card now opens with `PartyVoice.AnchorLine(state, party)` (`:308`, "the camp speaks first", P2-PEOPLE-15 — link 2, decision 6) — the party's lead hero (`InFlightExpedition.Party[0]`, the same hero the card's own control names already call "lead") speaking in first person ahead of the old bare "PARTY CAMPED" header — then camped heroes' HP, `PartyVoice.HealsLeft`/`YoursHealsLeft` (`:359-360`, "(of which yours: N)"), target floor, floors/monsters still ahead, rejections verbatim; runner fee mirror; flee-threshold urgency at ≤40% |
| — Camp verbs | | | `CampPick_{lead}` (select supply), `CampSend_{member}` (`SendSupplyAction`), `CampRecall_{lead}` (`RecallPartyAction`), "Send them deeper" (→ `SendDeeperRequested` → `Conductor.ResolveVigil`, `MainUi.cs:1002-1006`), "Forge something for them" (→ `OpenForgeRequested` → `OpenPanel("Forge")`, `:1008`), Hold (close) |
| `ScryingMirror` | `ScryingMirror.cs` (346) | `WatchButton` (`MainUi.cs:3706`), PiP body click, gatehouse overlook station | The borrowed `MineWatch` strip in a top band, party tabs, floor-progress line, time-stretched `JourneyFeed` beats with `ManifestLine_{item}_{party}` and `AttributionBeat_{item}_{floor}` provenance buttons, `MirrorClose` |
| `CommissionBoard` | `CommissionBoard.cs` (335) | tray `OpenCommissions` | One row per live commission (hero, slot, min quality, deadline, premium) with Accept/Decline; accepted rows show a status line; `CommissionClose` |
| `LegendsWall` | `LegendsWall.cs` (1,762) | tray `OpenLegends`; tavern story wall; the memorial wall building in the town plaza (§4.2, `MemorialWallClicked`); the Ledger's "Sit the wake" button on a death night (`ShowActorPage`, above); auto on `CampaignEnded`, opening straight to the bind page | A three-page book (P2-MEMORY-10 book shell — this REPLACED the old flat "THE FALLEN" + "DEPTHS RECORDS" sections, `LegendsWall.cs:304`), not a flat wall. **Index** (`RenderActorBook`, `:308-329`, header "WHO THE TOWN REMEMBERS"): every hero the town has a durable fact about — a memorial, a depths-board entry, or both (`ActorRows`, `:336-366`; fallen first, newest loss leading, then everyone else the depths board remembers, deepest first), each row tagged "fallen"/"floor N", opening `ShowActorPage`; plus "LEGENDARY GEAR" (`:1137`) and "STORIED GEAR" (`:1190`) sections with `Legend_{item}` buttons (`:1158`, `:1207`) opening `ShowItemPage`, and a "Bind the Book" row (`BindTheBook`, `:223`) opening the bind page. **Actor page** (`ShowActorPage`, `:382-461`): memorial line, **Honor** button (`HonorMemorialAction`, if unhonored, `:420-438`), the two wake verbs P2-PEOPLE-06 added — a grave-marker picker (`RenderMarkerRow`, `:469-498`, `SetMarker_{heroId}` → `PlaceGraveMarkerAction` over the exact legal candidates `FarewellHandlers` itself enforces) and a remembrance choice (`RenderRemembranceRow`, `:508-551`, → `ChooseRemembranceAction` over every event that truly names the hero, default pick sorted first; a chosen remembrance renders as fixed prose, never a re-askable button) — both self-extinguishing — plus **Reforge** rows (recipe + material `OptionButton`s, `ReforgeHeirloomAction`) and the depths record; a "the wall keeps what you'd have chosen" skip-cost line renders only while some wake fact is still open. **Item page** (`ShowItemPage`, `:584`): one item's `Item.History` prose, maker's mark, craft sub-scores — reuses `ProvenanceCard.RenderInto`/`TitleFor` as content, but as a book page, not a popup (P2-MEMORY-11 retired the nested `ProvenanceCard` popup this wall used to open — see that row below). **Bind page** (`ShowBindPage`/`RenderBindPage`, `:692,699`) composes the ending tallies from `ChronicleComposer`, day-stamped, reopenable any number of times, with an `ExportChronicleHtml` button (`:723`) writing a self-contained HTML file to `user://` — replaces the deleted `ChronicleScroll` (P2-MEMORY-14). `LegendsWallClose` (`:1304`) |
| `PledgePanel` (the pledge) | `PledgePanel.cs` (284) | Clicking the assessor NPC at the noticeboard (§4.2, `Town.AssessorClicked` → `MainUi.OnAssessorClicked` → `Pledge.ShowPledge(state)`) | P2-LONG-18 (§11.15): "the guild wall Voss keeps" — one row per player-marked, unworn, unsold, not-in-a-hero's-pack piece appraised at or above the current guild dues, each with its appraisal and a Pledge button. Pressing Pledge does **not** queue the action — it arms a confirm state on that one row showing `VossConfirmQuote` verbatim (a fixed cost-naming line, never softened to a one-time banner: "the cost named is the trade itself... said every time, in Voss's own words"), and only a second press, "Hand It Over," submits `PledgeDuesAction`. A separate, one-time first-touch lesson (`"the-pledge"`) explains the mechanic the first time the panel opens at all. Code-built, mirrors `CommissionBoard`'s idiom (dim backdrop, centered themed card) |
| `BestiaryPanel` | deleted (#770, P2-SCREEN-14: "the Bestiary gets no door, so it dies instead") | — | The row is kept as the record of why: its only opener was a hotspot no station ever named (see §14.2) |
| `ProvenanceCard` | `ProvenanceCard.cs` (342) | "History"/legend buttons in Shop/Heroes/Tavern/Mirror | One item's `Item.History` prose, maker's mark, craft sub-scores. (`LegendsWall` moved its own item rows onto book pages instead of this popup, P2-MEMORY-11 — it is no longer one of this card's hosts.) |
| System menu | `MainUi.cs:5640` (`BuildSystemMenu`) | Esc in bare town | Resume / Settings / Save & quit to title / Quit game |

`SettingsPanel` (`SettingsPanel.cs`, 695 — two instances: title screen + system menu, `:10-16`): `FullscreenToggle` (`:200`), `MuteToggle` (`:216`), `UiScaleSlider` (`:225-245`), four mixer faders (master/music/SFX/narrator — narrator's caption names the skipping-law cost, `:50-56`), controls rebinding rows `Rebind_{action}_Key` for 12 rebindable actions (`:84`, `:267-290`), `ResetBindingsToDefaults` (`:290`), read-only shortcut legend from `ShortcutMap` with quick-travel locked hints (`:301`), `SettingsBack` (`:316`). Persisted via `UiSettings` (`UiSettings.cs:39-190`: fullscreen, volumes, mute, UI scale, bindings).

---

## 8. Companion & ambient spectate surfaces

- **`CompanionDock`** ("Tomorrow at the Counter" docket, `CompanionDock.cs`): bottom-left companion on its own CanvasLayer 40 — deliberately NOT in `OverlaySurfaces()`/`SurfaceArbiter`'s `FullScreenModal` projection (its own claim declares `HudDock`, `OwnsScreen: false`, §3.6), so it never engages the clock, never blocks town input, and stays open through a running craft. Three ways in: `docket_toggle` (C), the Forge drawer's `OpenDocketFromForge` button (`MainUi.cs:2679`), and its own collapsed chip. First-open teaches via first-touch.
- **`PipDock`** (`PipDock.cs`): bottom-right journey dock, visible only during Expedition/Camp/Deep (slide in/out), titled "SCRYING MIRROR", latest revealed beat + party HP pips + party-cycle arrow + "Watch the delve ⤢" expand → Mirror. Suppressed while a drawer/modal owns the screen.
- **`AdventureTicker`** — deleted (P2-MEMORY-12, P2-OQ3). It used to be the single bottom-edge marquee voicing the quiet event tail. Its `FormatLine` switch moved into `LegendsWall` as the composer for the book's day pages (`RenderDayLog`/`ShowDayPage`/`DayLines`) — same lines, full campaign retention instead of a rolling 3-day window, queryable by day rather than ambient.
- **`MineWatch`** (`MineWatch.cs`, 1,830): the lit SubViewport strip — mine backdrop tiles, torch/campfire lights, walking hero figures (class walk frames), departure slate ("THE SEND-OFF"; empty-slate honest row), journey feed label, monster-slide + record-bark milestone flash, den-threat callouts. Live only while a party is underground; collapses to zero height otherwise; missing backdrop art collapses it permanently (`HasContent`). Hosts **`DelveStage`** (`DelveStage.cs`, 1,499): beat-driven combat overlay — floor chip, monster + honest HP bar (depletes by the sim's own `DelveBeat.DamageDealt` against `VenueDefinition.MonsterHp`), per-beat hero combat motion, damage numbers, kill poof, loot sparkle, quaff tint, proof flare, constitutional death-clouding (never a corpse, never an HP reveal).
- **Journey pipeline**: `JourneyStream` (phase→stage table, self-censored beats) → `JourneyFeed`/`JourneyPlayhead` (time-stretches one party's beats across the phase) → consumed by Mirror, PipDock, MineWatch; `DelveBeats` builds the animation-shaped beat list.

---

## 9. Audio

- **Bus graph** (`AudioBuses.cs:5-30`): Master (limited) → Music, Sfx (→ SfxLoop), Narrator — built in code, never `project.godot`.
- **`AudioDirector`** (`AudioDirector.cs`): 6-voice SFX pool, two crossfading music players (2.5s, `:38-43`), phase-keyed beds, per-category faders + mute from `UiSettings`, `MuteEnvVar` for dev tools (`DevToolAudio.cs`). Composed-track table replaces the synth bed for all five phases (`day-first-light`, `town-dusk`, `quest-wait`, `night-still` ×2, `:52-75`); `MusicBed` (`MusicBed.cs`) synthesizes fallback loops and remains the only Underground theme.
- **Cues** (`SfxLibrary.cs`): PanelOpen/PanelClose/Click/Coin/Shelve/CraftDone/Bell/BountyPost/PartyDepart/Rejected/HammerOnBeat/HammerOffBeat/Quench/Bellows(loop)/5 grade stings/5 building entrance cues/MemorialHonor/DeathToll. One cue per tick, worst news first (`MainUi.SoundTheTick`, `:4546`); immediate actions are deliberately bell-silent.
- **Narrator**: 7 triggers × 3–10 takes = 49 committed OGGs under `godot/assets/audio/narrator/` (confirmed still 49: act-advanced ×3, campaign-ending ×3, climax-reached ×3, death-epitaph ×10, killing-blow ×10, proven-save ×10, vigil-opening ×10) — `NarratorLines.AllAudioIds` walks the full set; un-voiced lines are an observable state (`NarratorRequest.Voiced`). Spoken text always also lands on screen (Ledger narrator line, Camp slate).

---

## 10. The tutorial presentation layer (rework-planning section)

This is the exact mechanism inventory of what the presentation layer can and cannot do today.

### 10.1 The chain (`TutorialFlow.cs`, 4,192 lines)

Eleven `TutorialStep`s in ten displayed slots (`:16-45`; BuyMaterial+Craft share slot 1): BuyMaterial, Craft, Shelve, PostBounty (MinDay 3, `:596-597`), WatchDeparture, LookIn, OpenCounter, Vigil, EveningClose, MeetHeroes (MinDay 3), Commission (MinDay 3, terminal). Grouped into five acts = the five links (`TutorialAct` `:155-162`: Mark, HandOff, Dark, **Proof — deliberately empty of rows** `:141-153`, Memory); card prefix is "{Act} · {pos}/{total}" (`:486-499`), never a global countdown.

Each row is one `TutorialStepDef` record (`:298-355`): DisplayIndex, Act, Anchor, MinDay, ShortLabel, TeachNote, `IsDone` (durable-fact predicate over EventLog/ActionLog/state), `AdvanceFrom`/`AdvancesTo` (transition graph), optional `AnchorExists`/`AnchorFallback` (conditional anchors — a conditional row with no fallback **throws**, `ResolveExistence` `:1017-1034`), optional `CanonicalAction` (the one `PlayerAction` whose `ActionLegality.IsLegal` verdict gates the step's copy, `:1465-1490`; Craft is judged on the slot dimension only, `:1484-1487`).

Advancement (`Advance` `:1862-1907`): a single forward pass over the registry per tick, cascading; anti-stranding sweeps (WatchDeparture fires from any day-1 step `:634-638`; EveningClose fires from an unanswered Vigil `:778`). UI-only steps advance via notify hooks: `NotifyMirrorOpened` (`:1952`), `NotifyPanelOpened` — Tavern or HeroCards (`:1965`), `NotifyCampCardShown` (`:2000`), `NotifyEnteredBuilding` arrival ratchet (`:2018-2037`), `NotifyLedgerOpened` (`:2607`). Unconditional close at `ChainBackstopDay = 8` (`:1943`).

Copy machinery: `TopSlotText`/`CopyFor` → `StepText` (`:1196-1297`, per-step copy quoting live control labels via `PhaseVocab.BellVerb` and MainUi tooltip constants); `WaitText` deferred variants keyed to the *actual* blocking gate — day, slots, phase, gold — never a blank card (`:1586-1707`); `GatingNote` short checklist reasons (`:1713-1769`, Vigil's is muster-honest via `MusterPlan.Compute` `:1567-1573`, `:1780-1781`); `GoTo` walk/arrive acknowledgement (`:1389-1427`, movement hint = "WASD" `:1374`). Profession-true workshop vocabulary is pushed in, never derived (`SetWorkshopVocab` `:1153-1163`; station-id substitution `:986-999`).

Persistence: `user://tutorial_flow.json` (`:420`, `PersistedData` `:3211-3291`) — Completed/Dismissed/Step, ledger tip, warrant beat, first-loss day, fleece beat, demand-board beat + armed day, proof-beat day + card-opened, FirstTouch fired map, visited-anchor ratchet, pending MentorBanner lines. `ResetForNewGame` deletes it (`:2999-3005`); `ResetForReturningSmith` writes Dismissed=true while carrying the fired-lesson set forward (`:3057-3068`).

### 10.2 Anchors — the pointing vocabulary

`TutorialAnchorKind` (`:58-127`): **None**, **Building** (venue key → `Building2D` sprite pulse), **Hud** (live control by `FindChild` name — throws if unresolved), **Station** (one station inside a room — same pulse mechanism), **PanelControl** (a named control scoped to one registered panel/modal's content root), **PanelSection** (a named container — tolerant of zero/one/many rows). Constructors at `:198-241`.

Aiming rules (`AimAnchor`, pure/static `:917-960`): a Station anchor points at its **building** until the player is inside the venue; a PanelControl/PanelSection anchor points at the surface's declared **way in** while the surface is closed (`TutorialSurfaceRegistry.WayInFor`), and **throws** for a surface declared way-in-less. Existence fallback resolves before aiming (`AnchorFor` `:907-908`).

### 10.3 `TutorialSurfaceRegistry` (`TutorialSurfaceRegistry.cs`)

The one roster of addressable surfaces (`Surfaces`, `:83-114+`): 10 drawer ids + Ledger/Commissions/Legends/Camp/Forecast/Mirror/Pip/Docket — **18 total**, not 20 (Bestiary was deleted at #770; **Chronicle was also retired**, P2-MEMORY-14, since `ChronicleScroll` no longer exists — the class doc names this explicitly: "P2-MEMORY-14 retired a third named row here, Chronicle"). Neither `Pledge` nor `TellingPanel` is registered here — the tutorial's pointing system cannot anchor to either surface today. Each registered surface has a content-root resolver and a declared `WayIn` anchor. **Surfaces declared to have no live way in**: Heroes (only a roaming hero click), Pip (ambient). Camp's way-in is the `AdvancePhase` bell (`:105`); Mirror's is `WatchButton` (`:109`); Docket's is a PanelControl inside Forge (`:114`).

### 10.4 `TutorialAnchorArbiter` (`TutorialAnchorArbiter.cs:42-61`)

One pure precedence rule for who owns the pulse each tick: **ForgeSpotlight** (ForgePanel's private banner) > **MentorBannerAnchor** (the shared banner's current line) > **ChainStep** (the pointed chain) > **LossRow** (dormant loss act → Legends tray) > None. Resolved in `MainUi.RefreshObjectiveLine` (`MainUi.cs:1418-1437`) with the open-surface id (`CurrentOpenSurfaceId` `:1475-1486`) so an anchor pointing into a visible modal aims at the control, not the way in.

### 10.5 `TutorialOverlay` (`TutorialOverlay.cs`, 679)

The pointing renderer. Warm-gold 3px pulsing outline for Hud/PanelControl/PanelSection targets (1.1s sine, `:66-74`, drawn `:621-638`); Building/Station targets pulse the sprite itself via `Building2D.SetTutorialPulsing` (scale + color breathe); other stations' "tell" glows are damped while a world anchor is live (`Town2D.SetWorldAnchorTellDamping` `Town2D.cs:738-747`). Eager resolution — an unresolvable anchor **throws**, never points at nothing (`:50-59`, throw sites `:305`, `:341`, `:351`). Extras: **off-camera edge marker** (a 20px triangle projected via `Town2D.WorldToScreen`, sharing the pulse's own scale/alpha, sliding vertically to clear the objective card — `KeepClearOf`, `:143`, `:213-245`, `:440-538`; silent when camera and target are on different room islands `:455-470`); **scroll-into-view** once per fresh target inside its ScrollContainer, clipped so the outline never floats outside the scroll (`:394-421`, `:562-614`); `ForceRefreshOnNextCall` restart for the player's re-ask (`:150-169`). Never a click target (MouseFilter Ignore, `:186`).

### 10.6 `ObjectiveTracker` + checklist

Described in §3.5. Checklist rows render ✓ done / ◆ current / ○ upcoming / **— skipped** ("didn't come up this time", the third honest state, `ObjectiveTracker.cs:459-496`, `TutorialFlow.Checklist` `:1790-1826`), "✓ Arrived" sub-tick, current row's TeachNote + GatingNote, auto-scroll to the current row's deepest line (`:531-544`, `:571-607`). Dismissal is a two-press confirm whose copy names the warrant cost only while it is still owed (`ShowDismissConfirm` `:391-395`, copy `TutorialFlow.DismissConfirmCopy` `:1318-1322`); Yes atomically submits `ConcludeApprenticeshipAction` + `Tutorial.Dismiss()` (`MainUi.cs:3470-3475`).

### 10.7 The voice: `MentorBanner`, `MentorVoice`, first-touch, stuck detection, act budget

- **`MentorBanner`** (`MentorBanner.cs`): the shared "Bryn speaks" surface — full-rect transparent root, centered 440px wood card, one "Got it" button, **no timer ever** (`:20-24`). Rank-ordered queue (Lesson < Act, `:49-58`), cap 4 with lowest-rank eviction (`:395`, `:397-448`), preempt inserts the displaced line at its rank's front (`:240-249`), anchors travel with lines (`CurrentAnchor` `:157`), full queue persisted through `TutorialFlow` (`SnapshotForPersistence`/`RestoreFromPersistence` `:321-365`).
- **`MentorVoice`** (`MentorVoice.cs`): pure attribution wrapper (`Speak`) + Bryn's workshop station (present in every profession's room) whose press speaks the current lesson (`MainUi.cs:4297-4301`); her lines are pinned never-imperative.
- **`FirstTouchLessons`** (`FirstTouchLessons.cs`): the generic once-ever-per-id engine (anti-nag pin, `:20-27`), persisted, consumed via `TutorialFlow.ConsumeFirstTouch` (`:2787-2796`). ~20 first-touch ids exist across MainUi (first-morning, read-only-surfaces, docket, quick-travel, second-profession, refusal-x3, idle-help), ForgePanel (material-ceiling, act1/act2, brew/assembly/tanning, talents, foundry-four-verbs, the-mark-read — the one preempting lesson, `ForgePanel.cs:2334-2357`), Shop/Commissions/Forecast/Legends/Progress dilemma lessons.
- **`StuckPlayerDetector`** (`StuckPlayerDetector.cs`): pure bookkeeping — 45s idle on one step offers that step's own teaching once (`MainUi.cs:66`, `:1275-1294`); the third identical friendly refusal promotes it from toast to banner (`:76`, `:1086-1090`).
- **Act-voice budget** (`TutorialFlow.cs:2177-2322`): at most **two** act-rank voices per night (`:2219`), fixed precedence HeroDeath > Proof > Graduation > WarrantEnded > ActAdvance > CommissionFulfilled > RankUp (`:2205-2214`), death excludes proof outright (`:2259-2270`); a loser stays un-consumed and re-arms the next day with its full one-night-one-day window (LossActRow `:2449-2475`, ProofBeatRow `:2625-2651`).

### 10.8 Surface gating (`SurfaceUnlocks.cs`)

Seven tray books open on durable player-caused facts (table §3.3), derived-never-persisted (`:17-22`), greyed-not-hidden (`:24-27`), monotonic (`:64-70`). The one hard pin: a gate may never hide a tutorial anchor — `ForcedOpenByAnchor` recognizes both Hud "Open{id}" and PanelControl panel-id anchors (`:146-151`, ORed in `MainUi.SurfaceEffectivelyOpen` `:1566-1568`).

### 10.9 What the presentation layer can and cannot do today

**Can:** point at a building, a station in a room, any uniquely-named live HUD control, a named control or section inside any of the **18** registered surfaces; aim at the way-in while the target is closed; fall back per-row when a target doesn't exist yet; show an off-camera direction marker; scroll a target into view; restate the current step on demand (R / ↻) with a camera peek; hold the raid span while a step is unanswered; teach once-ever on first touch, on idle, and on repeated refusal; persist every one-shot across quits; run a dormant post-chain act (loss, proof, warrant end, demand board, fleece) with a bounded voice budget.

**Cannot (by construction, with the receipts):** point at a dynamic per-entity control that does not exist yet except via PanelSection containers; point at a roaming hero sprite (`TutorialSurfaceRegistry.cs` — no anchor kind names one); open Pip/Heroes from closed (declared way-in-less); point at the **Pledge** or **Telling** panels at all (neither is registered in `TutorialSurfaceRegistry`); move the camera on its own (law 1 — only the player's re-ask peeks); show two banner lines at once (one slot + queue); render markup beyond stripping `**`; regress a step; or run more than one pointing overlay (one `TutorialOverlay`, one arbiter winner per tick).

---

## 11. Minigames (5 overlays inside the Forge drawer)

All are self-contained full-rect overlays built hidden at `ForgePanel.EnsureBuilt`, keyboard-claimed on open (`OpenedOverlay`), force-cancelled if the drawer hides (`_Notification`), logged open/done/cancel to `PlaytestLog`, and bound by the single-action contract: exactly one `CraftAction` on finish, nothing on cancel. (`ForgePanel.cs` grew 2,359 → 2,965 lines since the old base; none of the five minigame files themselves changed at all — same line counts, byte-identical structure.)

| Overlay | file | Act structure | Inputs |
|---|---|---|---|
| `ForgeMinigame` (Act 1, shaping) | `ForgeMinigame.cs` (1,501) | Steer the billet (X=shape, Y=heat) along the sim's own `ForgePath` polyline; strikes advance, bellows raise heat; required strikes fall with demonstrated accuracy (21→18 base/min, `:38-45`); hands off at the sim's forge-zone boundary via `ShapingDone` | `forge_strike` (Space), `bellows` (Shift hold / right-drag), Esc cancels |
| `QuenchMinigame` (Act 2) | `QuenchMinigame.cs` (482) | Heat falls on its own; one decisive `Plunge` inside the tier-narrowed band; auto-plunges at timeout — owns the one `CraftAction` (`:12-35`) | `plunge` (Space/Enter), Esc |
| `AlchemyBrewPuzzle` | `AlchemyBrewPuzzle.cs` (1,011) | Discrete pour-order puzzle, no clock, no `_Process`; sim scores the submitted `AlchemyReagentPuzzle` (`:13-30`) | cursor + `confirm`, undo, Esc |
| `EngineeringBench` | `EngineeringBench.cs` (998) | Clockless spatial part-seating against the sim schematic; reseat-is-free honored in the recorded fill order (`:13-33`) | cursor, `confirm`, `pull_part`, `crank_stroke`, Esc |
| `TanningFrame` | `TanningFrame.cs` (840) | Clockless coverage-with-restraint over the sim's patch grid (`:13-28`) | cursor, `scrape`, `confirm`, Esc |

Post-craft: the G1 result ceremony (grade stamp, star row, three sub-score pips, grade sting, 2s auto-dismiss or Skip/Esc), plus the "the mark, read" first-touch showing the item's actual `MakersMark` (`ForgePanel.cs:2912+`).

---

## 12. Theme, widget kit, art resolution

- **`GameTheme`** (`GameTheme.cs`): the palette (Void/Iron/Arcane/Coolant/Ember/Bone/Blood + roles), Silkscreen header font, font sizes, spacing 4/8/12/16, radii, `PanelStyle`/`PanelStyleWood` (the wood frame is `ui-frame-wood.png`, null-tolerant fallback), button styles, scrollbar styles. Built once and cascaded from the scene root.
- **`UiKit`** (`UiKit.cs`, 1,165 lines — grew from 820): `Card`, `Section` (returns a `SectionView`), **`Disclosure`** (a new collapsible-section widget, `:362`, with `DisclosureToggleName`/`DisclosureBodyFor` helpers — ShopPanel's "Who Would Buy This" and "Rival Shelf" both use it, §6), `StatChip`/`StatChipCompact`/`IconChip`, **`TrinketChips`** (`:478`, new), `ShortcutBadge`, `PortraitFrame`, `ArtRect` (themed fallback + caption on a manifest miss; misses logged once), `ListRow` (icon|name|price|owned|action with disabled-reason tooltips), `SceneBanner`, `DrawerHeader`, keyboard claim/reclaim helpers, `MakeButtonsMouseOnly`. `HeroChips.StandingChip` (`godot/scripts/ui/HeroChips.cs`, 98 lines, new) is a sibling widget outside this file specifically for the relationship-band chip `CounterPanel`/`HeroPanel`/`CampPanel` all now render identically (§6).
- **Art resolution ladder** (confirmed unchanged since the last pass — no commits touched any of these four files): `AssetCatalog` composes ids from sim concepts (`item-{recipe}`, `monster-{slug}`, `{venue}-backdrop/-entrance`, `hero-{classId}`, Sunken Crypt hyphen normalization `AssetCatalog.cs:40-50`) → `IconRegistry` loads by id with a manifest presence check (`art-manifest.json`, `IconRegistry.cs:12-25`) → `ArtVariants.Pick` selects deterministic per-entity variants from `-v{N}` pools → `TownAssets2D` adds loud placeholders (magenta border + missing-id pixel text + one log, `TownAssets2D.cs:7-17`). Hand-authored SVGs: 9 concept glyphs + 19 ore icons (`assets/icons/`), 6 hero figures (`assets/sprites/hero_{classId}.svg`, loaded `IconRegistry.cs:168`).
- A dedicated reachability census now guards this whole ladder from the sim side: `art/GameArt.Tests/AssetReachabilityTests.cs` (P2-SCREEN-13) — "does anything in the shipped game ever actually ASK for this id", distinct from provenance or resolution tests. Its own class doc cites this document's §14.1 finding about `town2d-player.png` by name as the gap it closes (that id was found by hand, not by any test, and sat orphaned for weeks).

---

## 13. Persistence, dev surfaces, and the CLI

### 13.1 Persistence files (all `user://`)

| File | Writer | Contents |
|---|---|---|
| campaign save (one rolling slot) | `CampaignSave.cs` (envelope + `SaveCodec` bytes, class at `:41`) | the sim world; autosaved each Evening (`CampaignSave.Save`, `MainUi.cs:1577`; also on both quit paths, `:5762`, `:5790`), saved on quit paths; every failure degrades to "no save" |
| `tutorial_flow.json` | `TutorialFlow.Save` (`:3818`) | chain + every once-ever flag + mentor queue (§10.1) |
| `clock_settings.json` | `MainUi.ClockSettings` (`:6036`) | auto-advance opt-in |
| UI settings | `UiSettings.cs` | fullscreen, 4 volumes, mute, UI scale, key rebinds |

### 13.2 Dev/observability surfaces (in the client, inert in normal play)

- `PlaytestLog` (`PlaytestLog.cs`): JSONL session log, opt-in via `MM_PLAYTEST_LOG` (`:67`); one row per tick with economy columns, plus action/decision/note trails with causes (`MainUi._pendingTickCause`, `:247`).
- `DecisionEvents` (`DecisionEvents.cs`): mirrors the sim's own typed reasons (pass/decision/bounty/walk/death/beat/DecisionExplained) into the session log.
- `EngineDistress` (`EngineDistress.cs`): in-memory capture of every push-warning/error; scanned by `EngineLogAnomalies`.
- Receipt/screenshot seams (env-gated, never fire in play): `TOWN_SHOT` (`MainUi.cs:1083-1103`), `SHOT_PROFESSION`/`SHOT_PROFESSION2` (`:499-515`), `SHOT_WATCH_FIGHT` staged fight (`:527`, `:906`), `Dev_QueueDay1TutorialLadder` bridge (`:1117`), `godot/tools/shot_harness.gd` + `play_harness.gd`.
- `AgentPlaytest` (`AgentPlaytest.cs`): the model-driven harness — writes a per-turn `state.json` digest (visible text, buttons + enabled state, value controls, location incl. overlay names via `MainUi.ActiveOverlayName`), accepts verbs press/move/key/set/wait/advance/stop.
- `FullPlaytest` (`FullPlaytest.cs`): five real-launch multi-day playthroughs with pixel-motion measurement; `ScenarioWriter`: manufactures a day-N save through the real `CampaignSave.Save`; `FrameCapture`, `ScreenObservation`, `DevToolAudio` (mute for tools).
- `play.bat` is the one launcher (staleness-gated); `edit.bat` opens the pinned editor.

### 13.3 The CLI (`sim/GameSim.Cli`, 6,470 lines total across all `.cs` files — grown from an earlier pass's 3,905; `Program.cs` alone is 1,766)

Interactive text play over the same `Tick(actions)` surface. Full command set:

- **Verbs**: craft (with explicit grade), profession, talent, buymat, stock, price, unstock, buyore, bounty, send, recall, accept-commission, decline-commission, honor-memorial, reforge-heirloom, upgrade-forge, buy-supply, masterwork, commission-legendary, counter open/present/suggest/close, haggle accept/hold/counter. No `pledge` verb exists, and no verb wraps `PlaceGraveMarkerAction`/`ChooseRemembranceAction` (the wake's own two verbs, §7).
- **Reads**: status, recipes, talents, mats, items, heroes, hero <name>, shelf, forecast|telegraph, board, demand, gossip, advice, progress|spine, modifiers, day, next, export.
- **Modes**: `batch` seed-sweep telemetry farm (`BatchRunner.cs`), `decisions` decision-surface logger (`DecisionLogger.cs`), `decisions play` scripted index-choice replay (`DecisionPlay.cs`), `Characterize` ladder measurement, `ConsequenceProbe` does-the-choice-matter probe.

**CLI can, client cannot:** craft with an arbitrary explicit grade (`craft … grade <0-1000>` — the client earns grades by hand); run batch/decision/consequence analysis; export chronicles; jump a whole day in one command (`day`).
**Client can, CLI cannot:** the five minigames (the CLI's grade parameter stands in); walking/proximity/camera; the tutorial layer (all of §10 is client-side); drag-and-drop; audio/narrator; **the guild pledge** (`PledgePanel` has no CLI counterpart); **the Telling's staged counterfactual proof** (no CLI equivalent to `TellingPanel`'s stage machine) and the death-clouded delve animation (the CLI prints beats as text); **the wake's grave-marker/remembrance choices** (no CLI verb for either); save/continue (the CLI has no `CampaignSave` caller — `grep CampaignSave sim/GameSim.Cli/` → 0 hits, re-confirmed).

---

## 14. THE WIRING AUDIT

### 14.1 Orphans

| Item | Evidence |
|---|---|
| **`godot/assets/art/town2d-player.png`** — committed PNG referenced by nothing | Re-run: grep `town2d-player` across `*.cs`/`*.py`/`*.md`/`*.ps1`/`*.tscn` → only its own `.import` file, plus a test, `art/GameArt.Tests/AssetReachabilityTests.cs` (P2-SCREEN-13, §12), that names this exact orphan by id in its own class doc as the worked example its coverage guard is built against. The player draws `player_smith[.png]` (`PlayerController2D.cs`) |
| **`town2d-tile-grass.png` / `town2d-tile-cobble.png` / `town2d-tile-path.png`** — committed, never drawn at runtime | Re-run: grep `town2d-tile` in `godot/scripts/` → 0 hits; the ground draws `town2d-ground-atlas`. The three tiles survive only as palette *sources* for `art/pipeline/gen-*-interior.py` and rows in `art-manifest.json` — pipeline inputs living in the shipped game directory |
| Everything else checks out | All narrator OGGs (49) and all hand-authored SVGs (34, §12) resolve through their respective registries; the minigame PNGs and the composed art families (`item-*`, `monster-*`/`{venue}-*`, `hero-*`, `town2d-hero-*`, `town2d-monster-*`, `town2d-townsfolk-*`, `props-*`/`town2d-prop-*`) remain reachable through `AssetCatalog`/`TownAssets2D`/`DelveStage`/`TownsfolkNpc2D`/`TownLayout2D.Props`, and are now also policed by the `AssetReachabilityTests` census (§12) rather than resting on a hand count. A precise total base-art-id count ("161") from an earlier pass is not re-asserted here — not independently re-derivable in this pass without a fuller asset sweep — but the two named orphans above are independently re-confirmed |

`HANDOFF.md` (repo root) — the doc an earlier pass flagged as stale — **no longer exists in the tree** (confirmed: `test -f HANDOFF.md` fails). The row that used to flag it here is removed; the file it pointed at is gone.

The three dev scenes (`agentplaytest/fullplaytest/scenariowriter.tscn`) are launched by path from `tools/*.ps1` — dev-only, not orphans.

### 14.2 Dead ends

| Item | Evidence |
|---|---|
| **The Bestiary was unreachable in real play — resolved by deletion (#770, P2-SCREEN-14).** The panel, its monster-portrait bestiary route, and `OnInteriorHotspotActivated("Bestiary")` are gone; no station in `InteriorLayout2D.Rooms` ever named Action "Bestiary" | confirmed gone: `godot/scripts/panels/BestiaryPanel.cs` does not exist |
| **The Heroes roster panel is unreachable while every hero is away or dead.** Its only door is clicking a wandering hero's sprite (`MainUi.OnTownHeroClicked`); `TutorialSurfaceRegistry.cs` still declares no other way in. During Expedition/Camp/Deep all party members are `Away` (invisible at the gate) — the roster/gear/provenance detail pane cannot be opened exactly while its subjects are underground. (HeroCards/Renown remains reachable but shows the digest, not gear/history.) | unchanged from an earlier pass, re-confirmed against current `TutorialSurfaceRegistry.cs` (§10.3) |
| `PledgePanel` has no reachability gap of the same shape: the assessor NPC (Voss) is a permanent, always-clickable town fixture, not gated on a phase, a room, or a hero's presence, so "the pledge" is always reachable — checked for the same class of defect on introduction | new surface, no dead end found |
| `PhaseChip`'s tooltip legend claims SendSupply/RecallParty availability the Camp modal owns — cosmetic-only duplication | not a dead end, listed for completeness |
| Flavor stations (quench trough, ledger desk, stock crates, hearth, herb bundles, flywheel, vats, gate winch) press to a toast, not an action | **by design** — "honest flavor"; not defects, but they are the only E-targets in rooms that do nothing |

### 14.3 Blind spots (sim decides it; no screen shows it)

| Sim state / event | Reader count (re-grepped fresh over `godot/scripts/`) | Note |
|---|---|---|
| **`GameState.RivalMarketSharePermille`** | **Re-evidenced, no longer 0.** 2 references, 1 real reader: `ShopPanel.cs:664` (`RivalEdgeGradient.For(state.RivalMarketSharePermille)`) drives the Rival Shelf's own "Rival Edge" gauge (P2-HONEST-17, §6). This resolves the open thread an earlier pass left on this exact row | The rival's competitive edge is now shown on the Rival Shelf, not just implied by its prices |
| **`MarketShareShifted`** event | 1 real reader, unchanged: `LegendsWall.cs` (`MarketShareShifted e when e.RivalGained =>`), the idle-day direction only. The claw-back direction (`RivalGained: false`) remains a deliberate exclusion, documented as a comment in the same switch | The idle-day half speaks once in the book's day pages ("You were not at the anvil today. The rival's stall was.") |
| **`TariffApplied`** event | 1 real reader, unchanged: `MainUi.cs` (`state.EventLog.OfType<TariffApplied>().Any()`), gating the one-time "the-tariff-fork" mentor line. The day-page exclusion is separate and still stands (`LegendsWall.cs`, comment block) | The per-purchase delta itself still never renders as a running line |
| **`PlayerState.BatchEcho`** | **Re-evidenced, no longer 0.** Now read in `ForgePanel.cs:1215-1218` (`CraftingHandlers.PendingEchoGrade(state.Player.BatchEcho, ...)`, `CraftingHandlers.BatchEchoCount - state.Player.BatchEcho!.Uses`), rendering an "echo uses left" counter on the affected recipe card (§6). This directly resolves the exact gap an earlier pass recorded on this row | A player can now see that their batch echo exists and how many uses remain |
| **`LootIncomeReceived`** | 0 direct godot readers, unchanged | Folded into `LedgerQuery.ReturnCards`' gold lines sim-side — the total renders; the typed event does not |
| **`PartyCampReport`** | **Re-evidenced, no longer 0.** Now a real code reader: `LedgerModal.cs` (`foreach (var report in dayEvents.OfType<PartyCampReport>())`), the source of `AddCampReceiptLines` (§3.7, P2-SCREEN-40) — the winch-house slate's own facts (where the party camped, how low each hero stood, the player's own checkpoint call) now reach the night card | This directly resolves the exact gap an earlier pass recorded on this row |
| `HeroDecisionExplained` | 8, unchanged | healthy — listed as the contrast case |
| `KillingItem` / `Hero.Pack` | 20 occurrences across 5 files / 7 occurrences, re-counted fresh (an earlier pass counted 15 / 9) | previously one-reader fields; still multi-surface |

### 14.4 Silent fallbacks (quiet degradation paths, with their loudness today)

| Path | Behavior | Loud? |
|---|---|---|
| `MineWatch` missing `mine-backdrop` | whole strip collapses **forever**, DepthsPanel renders as if the feature does not exist (`MineWatch.cs:67-70`) | quiet by design — documented, but a broken import shows nothing on screen and nothing in-game says why |
| `IconRegistry.Art` / `AssetCatalog` unknown id | returns null; `ArtRect` renders themed fallback + caption and warns once (`UiKit.cs:374-494`); `TownAssets2D` placeholder is magenta-bordered with the id baked in (`TownAssets2D.cs:7-17`) | loud since the #316-class fixes |
| `AudioDirector.For(this)` in panels | null-conditional — no director, no sound, no log (every cue site, including the newer `PledgePanel`/`TellingPanel`) | quiet; acceptable for tests, but a mis-mounted director in play would be silent |
| `TellingPanel` asked to stage a beat with no real counterfactual | never happens by construction — the "Ask how it happened." button is the ONE creation site (`LedgerModal.cs:1518-1529`) and renders only when `TellingPanel.FindResult` already returns a stageable result; a beat without one gets no button at all, never a disabled one | not a fallback path — the check happens before the button exists |
| Narrator line without a recording | plays nothing, text still renders; `NarratorRequest.Voiced` observable (`NarratorLines.cs:39-47`) | observable, not on-screen |
| `CampaignSave` corrupt/missing | Continue row absent; `TryLoad` failure logs and stays on title (`NewGameSelect.cs:403-409`) | logged |
| `tutorial_flow.json` / `clock_settings.json` corrupt | fail-soft to fresh defaults (`TutorialFlow.cs:2952-2955`, `MainUi.cs:5053-5061`) | quiet by design |
| Ground atlas missing | 2-tile flat-color fallback (`Town2D.cs:2105-2116`) | quiet, deliberate |
| `ShortcutMap`/`MinigameInput` unregistered action | renders "?" instead of a key label (`ShortcutMap.cs:160-179`, `MinigameInput.cs:80-91`) | visibly wrong, per design |
| `BuildStamp` unreadable build info | "dev (unstamped)" (`BuildStamp.cs:18-20`) | visible |

### 14.5 Scale & layout hazards

| Item | Evidence |
|---|---|
| Runtime scale knobs are pinned OFF for characters: `CharacterSpriteScale = 1.0` with a regression pin (`TownLayout2D.cs:57`, history of the 0.5 asymmetric-decimation defect `:34-46`) — art ships at draw size; props were resampled offline (`:150-158`) | the two historical scale-knob defects (#471, #487) are closed at the source |
| The objective card's right edge overhangs the window by 6px — measured, left deliberately (`MainUi.cs:3445-3451`) | known cosmetic |
| `HeaderBudgetPx = 175` pins the two-row header height (`MainUi.cs:110`); `StatChipsWrap`/`TimelineWrap`/`TickerWrap` exist because HBox/marquee minimum widths repeatedly inflated the whole layout past 1152px (`:2833-2861`, `:2869-2882`, `:3277-3294`) | the wrap pattern is the standing defense; any new header child added outside a wrap re-opens it |
| No stretch mode + fixed 1152×648 reference (`project.godot:16-25`): all layout tuning assumes this size; `Town2D.CanvasShrink` is the only resolution-aware piece (`Town2D.cs:104-114`) | UI at other window sizes relies on clamps, not design |
| The drawer claims a fixed 600 of 1152px (`DrawerHost.cs:46`); the objective card 320px (`ObjectiveTracker.cs:31`); MentorBanner card fixed 440px (`MentorBanner.cs:135`) | fixed widths, fine at reference size |
| Tray icon buttons need both a `CustomMinimumSize` and per-instance margin trims and an `icon_max_width` cap or they overflow the header — still exactly 8 tray buttons (§3.3); neither `PledgePanel` nor `TellingPanel` added a 9th (both are reached outside the books tray) | adding a real 9th tray book will re-fight this |
| The tutorial dock's position is measured off the objective card, clamped + internally scrolled (`MainUi.cs:1748-1771`) after two magic-offset regressions | derived, no longer a knob |

---

## 15. Unverified — worth checking

Questions only; no claims.

1. (Closed by #770 — the Bestiary panel is deleted rather than given a door.)
2. `PhaseClock` Camp/ExpeditionDeep "borrow MorningSeconds" (`PhaseClock.cs:73-76`) — with the RaidConductor owning those phases, can the borrowed 45s timer ever actually fire (auto mode, Conductor at `Idle` during Camp)? Is the fallback dead code or a real path? (Not re-investigated this pass.)
3. The `Heroes` drawer id and `HeroCards` id render overlapping information — is the roster's gear/provenance pane intended to stay hero-click-only, or should Renown grow a detail view so gear history is reachable during a raid?
4. `town2d-tile-*.png`: should pipeline palette sources live under `art/pipeline/sources/` instead of the shipped `godot/assets/art/`? (Their `.import` files mean Godot imports them on every fresh checkout.)
5. The interact-prompt chip centers on the window, not the world strip — with a drawer open (world input blocked) the chip hides via prompt-empty, but is there a frame where a station prompt renders under the drawer?
6. (Closed — `RivalMarketSharePermille` and `BatchEcho` both gained real client readers, §14.3: a Rival Edge gauge on the Shop's Rival Shelf, and an echo-uses-left counter on the Forge's recipe card. Neither was awaiting a surface; both now have one.)
7. `SettingsPanel` UI-scale slider: which surfaces have been eyeballed above 1.0 at 1152×648? The wrap/clamp pattern in §14.5 was tuned at scale 1.
8. Is `PledgePanel`'s complete absence from `TutorialSurfaceRegistry` and `SurfaceUnlocks` (§10.3, §10.8) — always-open, never gated, never pointed at by the tutorial layer — a deliberate choice (Voss's own permanent town presence is the teaching) or a gap the tutorial layer simply hasn't caught up to yet?
