# Maker's Mark — the surfaces census

*Branched from `28fd0452`. Every claim carries a `file:line` into that tree. Line numbers drift as files change; symbol names drift far slower — grep the symbol if a line number misses.*

This document is an exhaustive census of the game's **surfaces**: every scene, screen, panel, HUD element, input binding, art/audio asset, and the wiring between them, plus the CLI as a second surface. The sim's rules are a sibling document's job; copy transcription is another sibling's. Where copy is quoted here it is only to identify an element.

Method notes:
- Reader counts come from `grep` over `godot/scripts/` (production client code). `godot/tests/` and `sim/` are counted separately where it matters.
- "0 readers" always names the grep pattern used.
- No status/shipped/TODO language appears here (repo rule 8). This describes what is in the tree at `28fd0452`.

---

## 1. Scene inventory — and the one structural fact

There are **14 `.tscn` files**, and every one of them is a single node with a script attached. **The entire UI tree is built in C# at runtime** — node trees live in `_Ready()`/`Build()`/`EnsureBuilt()` code, not in scene files. Reading `.tscn` files tells you almost nothing about this game; reading `BuildUi()`-style methods tells you everything.

| Scene | Root node | Script | Instantiated by | When it appears |
|---|---|---|---|---|
| `godot/scenes/new_game_select.tscn` | `Control "NewGameSelect"` | `scripts/NewGameSelect.cs` | `project.godot:14` (`run/main_scene`) | Boot; and via "Save & quit to title" (`MainUi.cs:455` `TitleScenePath`, `MainUi.cs:4805` `SaveAndReturnToTitle`) |
| `godot/scenes/panels/main_ui.tscn` | `Control "MainUi"` | `scripts/MainUi.cs` | `NewGameSelect.cs:45` (`MainScenePath`), swapped in by Continue (`NewGameSelect.cs:401`) or Begin (`NewGameSelect.cs:722`) | The whole game |
| `godot/scenes/panels/forge_panel.tscn` | `Control "Forge"` | `panels/ForgePanel.cs` | `MainUi.cs:3263` (`InstantiatePanel`) | Drawer, id "Forge" |
| `godot/scenes/panels/shop_panel.tscn` | `Control "Shop"` | `panels/ShopPanel.cs` | `MainUi.cs:3264` | Drawer, id "Shop" |
| `godot/scenes/panels/heroes_panel.tscn` | `Control "Heroes"` | `panels/HeroesPanel.cs` | `MainUi.cs:3265` | Drawer, id "Heroes" |
| `godot/scenes/panels/tavern_panel.tscn` | `Control "Tavern"` | `panels/TavernPanel.cs` | `MainUi.cs:3266` | Drawer, id "Tavern" |
| `godot/scenes/panels/depths_panel.tscn` | `Control "Depths"` | `panels/DepthsPanel.cs` | `MainUi.cs:3267` | Drawer, id "Depths" |
| `godot/scenes/panels/bounty_panel.tscn` | `Control "Bounties"` | `panels/BountyPanel.cs` | `MainUi.cs:3268` | Drawer, id "Bounties" |
| `godot/scenes/panels/demand_panel.tscn` | `Control "Demand"` | `panels/DemandPanel.cs` | `MainUi.cs:3269` | Drawer, id "Demand" |
| `godot/scenes/panels/hero_panel.tscn` | `Control "HeroPanel"` | `panels/HeroPanel.cs` | `MainUi.cs:3270` | Drawer, id "HeroCards" (HUD button labeled Renown) |
| `godot/scenes/panels/ledger_modal.tscn` | `Control "LedgerModal"` (starts `visible = false`) | `panels/LedgerModal.cs` | `MainUi.cs:3367` (`GD.Load<PackedScene>`) | Evening reveal / tray button |
| `godot/agentplaytest.tscn` | `Node "AgentPlaytest"` | `tools/AgentPlaytest.cs` | dev only — `tools/agent-playtest.ps1` launches it by path | never in play |
| `godot/fullplaytest.tscn` | `Node "FullPlaytest"` | `tools/FullPlaytest.cs` | dev only — launched by path, must run windowed (`FullPlaytest.cs:36`) | never in play |
| `godot/scenariowriter.tscn` | `Node "ScenarioWriter"` | `tools/ScenarioWriter.cs` | dev only — gated on env var `ScenarioWriter.cs:20-25` | never in play |

Panels **not** backed by any scene (code-built `new`): `ProgressionPanel` (`MainUi.cs:3271`), `LessonsPanel` (`MainUi.cs:3272`), `RaidForecastBoard` (`MainUi.cs:3375`), `BestiaryPanel` (`MainUi.cs:3385`), `ChronicleScroll` (`MainUi.cs:3393`), `CommissionBoard` (`MainUi.cs:3400`), `LegendsWall` (`MainUi.cs:3409`), `CampPanel` (`MainUi.cs:3417`), system menu (`MainUi.cs:3426`, built at `MainUi.cs:4685`), `ScryingMirror` (`MainUi.cs:3518`), `PipDock` (`MainUi.cs:3523`), `MineWatch` (`MainUi.cs:3325`), `CompanionDock` (`MainUi.cs:3338`), `ObjectiveTracker` (`MainUi.cs:3440`), `TutorialFlow` (`MainUi.cs:3481`), `TutorialOverlay` (`MainUi.cs:3535`), `MentorBanner` (`MainUi.cs:3549`), `AdventureTicker` (`MainUi.cs:3295`), `TabFade` (`MainUi.cs:3357`), `BuildStamp` (`MainUi.cs:3630`), `CounterPanel` (owned by ShopPanel, `CounterPanel.cs:14-21` embedded), `ProvenanceCard` (one per hosting panel, `ProvenanceCard.cs:16-23`).

### project.godot facts (`godot/project.godot`)

- Main scene: `res://scenes/new_game_select.tscn` (`project.godot:14`).
- Window: 1152×648 explicit, **no stretch mode** — panels tuned without it (`project.godot:16-25`).
- **No `[input]` section and no autoloads.** Every input action is registered at runtime (`TownInput.cs:16-24`, `MinigameInput.cs:35-67`, `MainUi.cs:4218` `RegisterQuickTravelActions`) because `project.godot` is deny-listed for agents.
- Icon: `res://icon.svg` (`project.godot:12`).

---

## 2. Boot: the title screen (`NewGameSelect.cs`, 779 lines)

One centered wood card (600px, `NewGameSelect.cs:110`) over a `SurfaceDeep` full-rect backdrop (`:182-189`). Exactly one of four views is visible at a time (`:228-245`): **title menu → picker → primer**, plus **settings**.

| Element | file:line | Shows / does |
|---|---|---|
| `Continue` button + blurb | `NewGameSelect.cs:322-376` | From `CampaignSave.Peek()` envelope only (world not deserialized): profession name, day, phase via `PhaseVocab`, saved-at time. Absent when no valid save. Primary-styled. Pressed → `CampaignSave.TryLoad()` → `MainUi.AdapterOverride` → scene swap (`:401-422`) |
| `NewGame` | `:262-281` | → picker. Primary-styled only when no Continue row exists (`:273-278`) |
| `SettingsButton` | `:283-290` | → shared `SettingsPanel` instance (`:241-245`) |
| `Quit` | `:295-302` | `GetTree().Quit()` |
| Profession picker | `:443-526` | One `Pick_{id}` button + blurb + "Your workshop: the {nametag}" note per `ProfessionRegistry.All` entry (`:461-502`); shared starter-kit note (`:506-514`); `PickerBack` (`:516`) |
| Primer ("Your first day") | `:537-620` | `FantasyNote` (`:104`), `MainUi.PhaseLegend` verbatim (`:564-569`), `ClockNote` built from `PhaseVocab.BellVerb` (`:94-97`), seed label (`:580`), `Begin` (primary, `:597-608`), `Back` (`:610-617`) |
| Returning-smith choice | `:629-691` | Visible on the primer only when `TutorialFlow.HasPriorProgress` (`:702`); two toggle buttons `RunCourse`/`SkipCourse` + note. Skip → `TutorialFlow.ResetForReturningSmith()` at Begin (`:742-745`); otherwise `ResetForNewGame()` (`:754`) |

Begin also: clears the campaign save (`:735`), sets `MainUi.FirstMorningBeatPending = true` on **both** branches (`:765`) so Bryn's cold-open fires on the next mount, builds `GameComposition.NewCampaign(seed, profession)` (`:767`). Seed source is wall-clock, drawn once per pick (`:51`, `:696`) — the seed displayed is the seed shipped. F11 works on this screen too (`:164-174`).

---

## 3. The shell: `MainUi` (5,088 lines)

### 3.1 Layout regions

`MainUi` is a full-rect Control holding, in order (`BuildUi`, `MainUi.cs:2781-3638`):

1. **`Layout` VBox** (`:2800`): `HudHeader` (wood panel, two rows, `:2809-3204`) → `ToastBanner` (`:3209`, hidden unless a toast is live) → **`WorldSlot`** (ExpandFill, `:3232`) containing `Town2D` full-rect (`:3239-3241`) → `TickerWrap` (28px, `:3288-3298`) containing `AdventureTicker`. The world sits **in layout flow** below the header — the header never occludes it (`:2791-2799`).
2. **Overlay siblings** (draw above the layout in add order): `DrawerHost` (`:3305`), `CompanionLayer` CanvasLayer 40 with `CompanionDock` (`:3336-3339`), `TabFade` CanvasLayer 100 (`:3357`), `LedgerModal` (`:3367`), `RaidForecastBoard` (`:3375`), `BestiaryPanel` (`:3385`), `ChronicleScroll` (`:3393`), `CommissionBoard` (`:3400`), `LegendsWall` (`:3409`), `CampPanel` (`:3417`), system menu (`:3426`), `ObjectiveTracker` top-right dock (`:3440-3455`), `TutorialFlow` dock below it (`:3481-3508`), `ScryingMirror` (`:3518`), `PipDock` (`:3523`), `TutorialOverlay` (`:3535`), `MentorBanner` (`:3549`), interact-prompt chip (`:3616-3625`), `BuildStamp` CanvasLayer 5 (`:3630`).

`MineWatch` is constructed once (`:3325`) and parked inside `DepthsPanel` (`:3328`); `ScryingMirror` borrows it while open (`MainUi.cs:4618`, returned at `:4625`) — the "exactly one live SubViewport" constraint (`MainUi.cs:329-336`).

### 3.2 HUD header row 1 — stat chips (`RefreshStatus`, `MainUi.cs:1840-1967`)

Rebuilt clear-then-compose on every tick. Wrapped in `StatChipsWrap` (fixed 68px height, ClipContents, `:2839-2861`) so its minimum width can never push the window wide.

| Chip | file:line | Shows | Fed by |
|---|---|---|---|
| `DayChip` | `MainUi.cs:1858` | day number | `state.Day` |
| `PhaseChip` (+ tooltip `PhaseLegend` `:2099-2104`) | `:1866-1868` | Dawn/Prepare/Quest/Vigil/Deep Vigil/Night | `PhaseVocab.Display(state)` (`PhaseVocab.cs:27-45`; "Prepare" only while a counter session is open) |
| `ActChip` (compact badge) | `:1873-1876` | campaign act I/II/III/Fin | `state.Arc.Act` |
| `GoldChip` (icon + big value) | `:1883-1885`, built `:2181-2199` | gold; bounce-pop on a player-shelf sale (`:1963-1966`, animated `:959-968`) | `state.Player.Gold`, `ItemSold{FromPlayerShop:true}` in `LastEvents` |
| `HeroesChip` | `:1891-1895` | alive/total (shield glyph stands in — no party glyph exists, `:1888-1890`) | `state.Heroes` |
| `SlotPips` | `:1899`, built `:2207-2230` | 5 pips, lit = remaining action slots | `state.ActionSlotsRemaining` vs `ActionBudget.SlotsPerDay` |
| `StandingChips` | `:1904`, built `:2247-2275` | one chip per faction with non-zero standing, ore icon + value | `state.Player.Standing`, `FactionRegistry.All` |
| `RentChip` | `:1918-1928` | `{days}d·{gold}g`, tone escalates ≤3d/≤1d/missed | `state.Rent` |
| `AssessmentChip` | `:1932-1944` | guild dues countdown (gossip glyph stands in) | `state.Assessment` |
| `ConfidenceChip` | `:1948-1958` | town confidence % (rune glyph stands in) | `state.Rent.ConfidencePermille` |

### 3.3 HUD header row 2 — timeline, verbs, books tray (`:2865-3204`)

- **`DayTimeline`** (in `TimelineWrap`, min width 280 `MainUi.cs:131`): five phase pills in kernel order with current/past/future styling + a pulsing ember "waiting" dot (`ObjectiveTracker.cs:679-901`; labels from `PhaseVocab`, `:685-692`; dot shown when `Clock.AutoAdvance && Playing && Engaged`, `MainUi.cs:1825`).
- **`VerbCluster`**: `ClockLabel` caption (`:2896`; text logic `UpdateClockLabel` `:2426-2526` — phase name, hold explanation "the day waits on you" `:2491-2494`, departure omen `:2575-2581`, send-off sale beat `:2600-2623`, open-items badge `:2557-2571`, heroes-ready badge `:2544-2553`); the **primary verb button `AdvancePhase`** (`:2910-3010`) — label is `PhaseVocab.BellVerb` ("Send them off"/"Snuff the lanterns"/"Hurry the day along"), or "Skip" in auto mode, or "Return to the vigil" when the vigil stop is armed with its slate closed (`:2475-2483`; press handler reopens `Camp.ShowModal()` `:2920-2933`); pressing during the raid span calls `Conductor.Hurry()` (`:2941-2954`); at Morning it force-closes an open counter session first, with an honest toast (`:2956-3006`).
- **`WatchButton`** ("👁 Watch", `:3022-3028`): opens the Scrying Mirror; visible **only** during Expedition/Camp/ExpeditionDeep (`:2441`).
- **`AutoAdvance`** toggle (⏱, `:3034-3045`, persists via `ClockSettings` `:5033-5087`), **`PlayPause`** (`:3047`), **`Speed`** 1×/2×/4× (`:3056`) — the latter two visible only while auto is on (`:2435-2436`).
- **`Fullscreen`** (⛶ + shortcut badge, `:3069-3088`; F11 also global in `_Input` `:4406-4411`).
- **`BellTray`** (`:3096-3098`, rebuilt from `SimAdapter.PendingActions` `:1640-1652`): one chip per bell-deferred action with a ✕ withdraw wired to `SimAdapter.Withdraw` (`:1663-1699`; a failed withdraw toasts, never silent `:1691-1695`).
- **`BooksTray`** (`:3106-3204`): eight 28px icon-only buttons (icons capped at 22px, `:4064`, `:4085`), full-sentence tooltips. Seven are gated by `SurfaceUnlocks` (§10.8); Lessons is deliberately ungated (`:3195-3199`).

| Tray button | file:line | Opens | Gate (SurfaceUnlocks.cs:71-106) |
|---|---|---|---|
| `OpenLedger` (skull) | `MainUi.cs:3131-3136` | `Ledger.ShowFor(LastCompletedDay)` | a party has departed (`SurfaceUnlocks.cs:73`) |
| `OpenForecast` (depths) | `:3140-3145` | `Forecast.ShowForTomorrow(state)` | first Evening reached (`:81`) |
| `OpenCommissions` (bounty) | `:3151-3154` | `Commissions.ShowOpen(state)` | first `CommissionPosted` (`:87`) |
| `OpenLegends` (rune) | `:3158-3163` | `Legends.ShowWall(state)` | first beat **or** first death (`:99`) |
| `OpenDemand` (gossip) | `:3170-3175` | `OpenPanel("Demand")` | first `HeroPassedOnItem` (`:90`) |
| `OpenHeroCards` (shield, tooltip "Renown…" `:2117`) | `:3182-3185` | `OpenPanel("HeroCards")` | first player-shop sale (`:84`) |
| `OpenProgress` (weapon) | `:3188-3193` | `OpenPanel("Progress")` | first bounty paid (`:104`) |
| `OpenLessons` (armor glyph reused) | `:3200-3204` | `OpenPanel("Lessons")` | none |

Gated buttons are greyed with the gate's reason as tooltip, never hidden (`:1574-1598`); a one-line arrival toast fires the first tick a gate opens (`:1593-1597`), suppressed when a rejection owns the strip.

### 3.4 Toast strip, interact prompt, build stamp

- **`ToastBanner`/`RejectionToast`** (`:3209-3218`): the one transient strip. Priority: refusal (friendly-phrased via `FriendlyRejection` `:2297-2387`, catch-all names the action `:2401-2412`) > narrator milestone text > `WorldNotice` (confidence collapse, climax, hero leaving, rival expansion, stipend — `:2035-2071`) > cleared (`:1107-1135`). 4s lifetime (`:51`), also reused for bell-queue acknowledgements (`ShowBellToast` `:2774-2779`), gate-open arrivals, station flavor lines, no-target interact, party-returns notice (`:3859`).
- **`InteractPrompt`** chip (`:3616-3625`, updated every frame `:1795-1820`): mirrors `Town.WorldInputNode.PromptText` verbatim ("E · Forge" or a flavor station's `HoverLine`), bottom-center, hugging its text, kept 28px+16px above the ticker (`:1778`).
- **`BuildStamp`** (`BuildStamp.cs:5-25`): dim top-left corner label from `res://assets/build_info.txt`, fallback "dev (unstamped)", CanvasLayer 5, never eats clicks.

### 3.5 Objective / tutorial docks

- **`ObjectiveTracker`** ("Today" card, 320px wide `ObjectiveTracker.cs:31`): top-right, docked below the header's measured height (`UpdateObjectiveDock` `MainUi.cs:1725-1772`), content-height-fitted with viewport clamps. Advisor top pick + reason (2-line clamp `ObjectiveTracker.cs:39`; tutorial text **never** ellipsized — unclamped 3-line budget `:73`, `:345-355`), expandable ranked list (`:211-218`, `:365-381`), tutorial checklist scroll (75px window `:107`), ✕ dismiss-confirm rows (`:242-266`), ↻ re-ask button (`:231-235`). Hidden while a drawer/modal owns the screen **except** when the tutorial is active with a drawer or room open, in which case it docks to the LEFT edge instead (`MainUi.cs:5003-5005`, `DockObjectiveHorizontally` `:4903-4918`).
- **`TutorialFlow` dock**: hosts only the earn-2nd-profession picker + quick-travel row (`TutorialFlow.cs:1080-1136`); self-hides when neither is live (`:2861`). Stacked below the objective card, measured, clamped to the window, internally scrolling (`MainUi.cs:1748-1771`).

### 3.6 The clock, the conductor, the engaged latch

- **`PhaseClock`** (`PhaseClock.cs`): auto-advance **defaults OFF** (`:45`) — the day is bell-driven; a persisted opt-in restores the timed "Innkeeper's Clock" (`MainUi.cs:661-665`). Durations Morning 45s / Expedition 30s / Evening 45s, Camp+Deep borrow Morning's (`PhaseClock.cs:26-28`, `:68-77`). While `Engaged`, an expired timer holds at the boundary ("flows-but-waits", `:116-135`).
- **`RaidConductor`** (`RaidConductor.cs`): sequences Expedition→Camp→ExpeditionDeep as a show — beats `Idle/SendOff/VigilStop/DeepTick/Homecoming` (`:88-95`), pinned maxima SendOff 6s / empty beat 1s / deep show 3s / homecoming 12s (`:69-82`). `VigilStop` is timer-free and only ends via `ResolveVigil()` (`:288-296`), wired to the Camp slate's "Send them deeper" (`MainUi.cs:695-700`). Show timers stop dead while `Clock.Engaged` or the tutorial's Watch step is unanswered (`:204-218`); `Hurry()` (the player's own press) walks through the hold but never past `VigilStop` unseen (`:267-281`). Beat re-derived from sim state on every tick (`Resync` `:168-190`), including a resumed save parked at Camp (`:136-138`). Per frame, `MainUi._Process` drives exactly one of `Clock.Update` / `Conductor.Update` (`MainUi.cs:852-864`).
- **`UpdateEngaged`** (`MainUi.cs:4932-5022`) is the one place the three distinct questions are answered: clock-hold (`Drawer.IsOpen || ModalOwnsTheScreen()`, plus the day-1 craft→shelve pacing hold `:4880-4893`), world-input block (`Drawer.IsOpen || AnOverlayOwnsTheScreen()` — a walkable room does NOT block walking, `:4963-4964`, history at `:4940-4962`), and PiP suppression (`:5011`). The overlay roster is one list, `OverlaySurfaces()` (`:3929-3939`): Ledger, Camp, Mirror, Forecast, Bestiary, Commissions, Legends, SystemMenu.

### 3.7 The Evening reveal chain (Return Ritual)

Evening tick completes → `LastCompletedDay`/`_pendingLedgerDay` armed with a 3.0s unscaled wall-clock gate (`MainUi.cs:44`, `:1210-1241`); narrator trigger + loss count captured at the tick, not the reveal (`:1223-1237`). When the gate elapses (`:874-941`): `Ledger.ShowFor(day, ConsumeLedgerTip(), ConsumeFirstLossBlock())`, Bryn's loss voice line anchored at the Legends tray (`:894-899`), the Proof-act voice anchored into the ledger's lead card (`:907-912`), narrator line + death toll cue (`:917-939`). Ledger close chains the `RaidForecastBoard` once per day-end (`:4501-4510`), inheriting the resume-play intent. Every modal open pauses the clock and captures a resume latch; every close re-syncs the latch and fires any deferred mine-gate camera pan (`:4488-4676`).

### 3.8 System menu (pause)

Esc in the bare town opens it (`_Input` ladder step 4, `:4434-4438`); Esc closes it first when open (`:4420-4425`). Full-rect dim (no click-out, `:4694-4697`) + centered wood card: `Resume` (primary), `SystemMenuSettings` (nested second `SettingsPanel` instance `:4767-4781`), `SaveQuitToTitle` (`:4756-4761` → `:4805-4824`), `QuitGame` (`:4763-4765` → `SaveAndQuit` `:4833-4845`). The OS window ✕ / Alt+F4 is intercepted (`AutoAcceptQuit=false` `:656`, `_Notification` `:733-746`) and saves before quitting. Autosave otherwise happens once per Evening tick, one rolling slot, deliberately anti-reroll (`:1144-1147`).

---

## 4. The town (`Town2D.cs`, 2,118 lines + layout tables)

### 4.1 Canvas, camera, movement

- The world renders in a `SubViewportContainer` (`Stretch=true`, Nearest) wrapping a `SubViewport` with pixel snap + physics picking (`Town2D.cs:426-478`). Nominal canvas 640×360 (`:54-55`); the real canvas is window ÷ `CanvasShrink`, an integer derived so ~576px (36 tiles) of world is visible regardless of monitor (`:82`, `ShrinkFor` `:113-114`, re-derived on resize `:480`, `:1223-1242`). Camera zoom is pinned 1 — StretchShrink is the only magnification dial (`:64`).
- `Camera2D` follows the player every frame by assignment + built-in smoothing, clamped to the town rect (`:536-549`, `FollowPlayer` `:1244-1264`). **Focus beats** are a timed borrow (`FocusOn` `:1282-1291`): departure/return pans to the mine gate for 3.2s (`:125`, `FocusOnMineGate` `:1299-1318` — centers the building, not its doorstep), suppressed inside a room (`:1301-1304`), deferred while any modal owns the screen (`MainUi.cs:3827-3833`, `:3869-3876`). The tutorial re-ask peeks 1.6s (`MainUi.cs:2694`, `:2752-2755`).
- **Player** (`PlayerController2D.cs`): `CharacterBody2D`, WASD at 90px/s (`:29`) + click-to-move seek (`MoveToTile`); real input cancels a seek (`:6-10`). Art `player_smith` + `_step/_walk2/_walk4` frames, feet-origin, no nameplate by design (`:22-26`).
- **World interact** (`WorldInput2D.cs`): per-physics-frame nearest-overlap scan (`:59-84`), highlights the target, exposes `PromptText` (`:28`), E → `RaisePick()`; E with nothing in range raises `NoTargetInteract` with an honest "Too far from the {name}" line (`:44`, `:145-151`) toasted by MainUi (`MainUi.cs:3254`). Esc raises `CancelRequested` (`:80-83`).

### 4.2 The five buildings (`TownLayout2D.cs:209-219`)

64×44 tile grid, 16px tiles (`:124-126`, `:20`). Venues: forge (18,26), market "Shop" (46,26), tavern (18,40), minegate "Mine Gate" (32,8), noticeboard "Bounties" (46,40) — SDXL exterior art set with real PNG footprints listed at `:186-190`. Cobble plaza + north road + door spurs (`PathRects` `:242-256`); rally tile (32,14) (`:226`). Each `Building2D` (`Building2D.cs`) carries a sprite (feet-line origin), click `Area2D`, blocking footprint (60% height so the door row stays walkable, `:52-54`), nametag label, `DoorAnchor` marker, optional hover-line, optional "tell" glow for real-verb stations (`:16-26`), highlight, and the tutorial pulse (scale + color breathe, `:28-35`).

Clicking (or E at) a building emits its lowercase key → `MainUi.OnTownBuildingClicked` (`MainUi.cs:4146-4195`): a venue with an `InteriorLayout2D.Rooms` row (forge/market/tavern/minegate — all but noticeboard) **enters the walkable interior**; noticeboard opens the Bounties drawer; unknown keys fall through to "Town" (close drawer).

Clicking a wandering **hero** opens the Heroes drawer with that hero selected (`OnTownHeroClicked` `MainUi.cs:4127-4131`) — this is the *only* way into the Heroes roster panel (`TutorialSurfaceRegistry.cs:93-96`).

### 4.3 Props, ambience, day tint

- Props (`TownLayout2D.Props` `:275-341`): well, 4 plaza lanterns, 12 perimeter trees (swaying, per-instance phase — `SwayingTreeSprite2D`, `Town2D.cs:1519-1526`), 2 crates, plus 8 "warm-hub" props (market crates, second flyer board, string lanterns, ore cart, forge salamander, laundry line, tavern cat) — all resampled offline to draw size (`:150-158`). A former duplicate well was deleted per owner ruling (`:334-340`).
- `AmbientLife2D` (`AmbientLife2D.cs`): chimney smoke (forge+tavern), dusk fireflies, per-lamp flicker, market awning sway, mine-mouth dust, noticeboard paper flutter, per-venue window glow anchored to each building's real sprite size — all phase-driven via `SetPhase` (`Town2D.cs:1859-1917`, `:1036`).
- `DayPhaseTint` eases a full-canvas `CanvasModulate` per phase; a warm constant overrides it inside interiors (`Town2D.cs:210`, `:1024-1031`).
- Forge FX: glow overlay + spark burst + steam plume near the forge door, driven by `ForgePanel` during the minigame (`Town2D.cs:1828-1848`, `:612-661`; consumers `ForgePanel.cs:313-339`, `:1509-1526`).
- Hero actors (`HeroActor2D.cs`): one per alive hero (`ReconcileHeroes` `Town2D.cs:895-934`), class body art with walk frames, nameplate, wander-drift + real errands to venue doors/corner tiles (`:915-918`, `TownLayout2D.cs:371-396`), rally→march-out→away→walk-in choreography driven by `Town2D.OnPhaseCompleted` (`:953-978`) with an 8s minimum "show floor" before survivors re-emerge (`:259`, `:1109-1171`). Townsfolk (`TownsfolkNpc2D.cs`): 4 purely cosmetic villagers, never clickable, dedicated civilian body variants (`Town2D.cs:1708-1764`). Tavern seating for present heroes with mood glyphs (`TavernLife2D.cs`, wired `Town2D.cs:1775-1815`); market-room customer choreography off real shop events (`MarketLife2D.cs`, mounted `Town2D.cs:1604-1609`).

### 4.4 Walkable interiors (`InteriorLayout2D.cs`, `InteriorRoom2D.cs`)

Rooms are far-off "islands" in the same world (+2048px X, stacked +512 Y apart, `InteriorLayout2D.cs:110-141`). `EnterInterior` teleports the player to the room's door, clamps the camera to the room rect, re-points interact scanning at the room's stations (`Town2D.cs:779-813`); exit is Esc (MainUi ladder step 3, `MainUi.cs:4443-4452`) or walking onto the door's `ExitZone` (`Town2D.cs:1590-1596`) — both funnel through `ExitInterior` (`:821-843`).

Stations are `Building2D`s mounted at the town's flat Y-sort scope (`InteriorRoom2D.cs:16-25`). A station's identity is data (`StationSpec` `InteriorLayout2D.cs:81-92`): a **real-verb** station carries `Action` (a drawer id or modal route) + `Verb` + `Copy` (toasted on press) + optional `Focus`; a **flavor** station carries `HoverLine` + `FlavorLine` (toasted on press — never a dead click). Routing: `MainUi.OnStationActivated` (`MainUi.cs:4281-4321`) → Bryn's station speaks the current lesson (`:4297-4301`); flavor toasts (`:4303-4308`); real verbs go to `OnInteriorHotspotActivated` (`:4331-4362`) — `"Bestiary"`→`Bestiary.ShowAll()`, `"Legends"`→`Legends.ShowWall()`, `"Watch"`→`Mirror.ShowMirror()`, else `OpenPanel(action)` — then `ForgePanel.FocusSection(focus)` (`:4313-4316`).

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
| `docket_toggle` | C | `:59` | `MainUi._UnhandledKeyInput` `MainUi.cs:4469-4473` → `Docket.Toggle()` |
| `tutorial_reask` | R | `:66` | `MainUi.cs:4480-4484` → `ReaskTutorial` |
| `quicktravel_forge/shop/tavern/gate` | 1/2/3/4 | `MainUi.cs:143-149`, `:4218-4230` | polled in `_Process` `:999-1008`, inert until `Tutorial.QuickTravelUnlocked` |
| (raw key) F11 | F11 | none — matched raw | `MainUi._Input` `:4406-4411`; `NewGameSelect.cs:164-174` |

- **Escape ladder** (`MainUi._Input` doc `:4373-4400`, code `:4401-4453`): child overlays consume first (DrawerHost `DrawerHost.cs:336`; every modal via `ModalEscape`, used by 16 files — grep `ModalEscape` → MainUi, 5 minigames, 10 panels); then the system menu closes itself; then the interior room exits; then, bare-town, Esc **opens** the system menu.
- `ShortcutMap` (`ShortcutMap.cs:62-119`) is the one registry that renders bindings (Settings legend + tooltips); key labels are read live off the InputMap (`:136-149`) so rebinds (SettingsPanel C3, `SettingsPanel.cs:267-290`) show through. Rebinding persists via `UiSettings.ApplyPersistedBindingIfAny` (`TownInput.cs:41`, `MinigameInput.cs:108`).
- Mouse: building/hero/station click-picking via `Area2D` physics picking inside the SubViewport (`Town2D.cs:473`); drawer dim veil click-out closes (`DrawerHost.cs:22-28`); ShopPanel drag-and-drop shelf stocking (`ShopPanel.cs:43-56` doc); bounty poster drag (`BountyPanel.cs:20-33`); everything else is buttons.

---

## 6. Drawer panels (`DrawerHost`, one at a time, right-anchored 600px)

`DrawerHost.cs`: registration `Register` (10 ids, `MainUi.cs:3308-3317`), `Open` replaces, dim-under click-out + Esc close, 0.22s accumulated-delta slide (`DrawerHost.cs:46-49`), header strip with humanized title + ✕ (`:37-42`). `MainUi.OpenPanel` (`MainUi.cs:3653-3728`) is the single router: gate check, refresh-on-open, per-building entrance cue (`:3959-3967`), `Tutorial.NotifyPanelOpened`, read-only-surface first-touch lesson for HeroCards/Depths/Heroes (`:3710-3713`).

| Drawer id | Panel (file) | Shows | Player verbs (control names) |
|---|---|---|---|
| Forge | `ForgePanel.cs` (2,359) | Tab row Craft/Materials/Foundry (`:1955-1975`); craft view: feedback line, needs-row (`BuyMat_{key}` twin, `:1063-1079`), material select (`MaterialSelect` `:2003-2011`), modifier selects Oil/Rune/Fit (`:2040-2045`), recipe cards ordered tier-then-id (`:784`) with art/stat chips/material chip, locked rows naming their gate talent (`:803-822`), talent cards (`:1019-1053`), docket button (`:2084-2086`); materials view: vendor rows (one per `MaterialRegistry.PricedPool` key with qty stepper, `:640-673`) and Foundry section (tier/coal/flux chips, `UpgradeForge`, `BuySupply_{coal,flux}`, `:679-752`) | `Craft_{id}` (auto-craft), `WorkForge_{id}`/`Brew_{id}`/`Assemble_{id}`/`Scrape_{id}` (per active profession, `:888-939`), `ForgeAnother_{id}` (repeat trace, `:933-938`), `Masterwork_{id}` (`:986-988`), `Commission_{id}` legendary (`:1013-1016`), `Unlock_{node}` (`:1040-1051`), `BuyMat_{key}`, `BuySupply_{key}`, `UpgradeForge`, `OpenDocketFromForge` |
| Shop | `ShopPanel.cs` (814) | "Who Would Buy This" (`:217`), "Your Shelf" cards + pass reasons (`:274`), "Unshelved Crafts" (`:398`), "Rival Shelf" read-only (`:502`); drag-and-drop stock/unstock; auto-suggested prices (`:57-66`) | `Unstock_{id}` (`:314`), `Reprice_{id}` via `PriceTag` (`:336`), `Provenance_{id}` History (`:339`, `:496`), `Present_{id}`/`Suggest_{id}` while a customer is at the counter (`:354-355`), `Stock_{id}` + `StockPrice_{id}` (`:476`) |
| — (embedded in Shop) | `CounterPanel.cs` (977) | Customer card w/ class icon + mood bucket, Interest/Patience/Goodwill/Round chips, presented item, standing offer, `CustomerWalked` reasons, walk-away speech bubble (`:569`), drawn `CounterDesk` (`:264`) | `OpenCounter` (`:97`, Morning-only mirror), `CloseCounter` (`:125`), `Accept` (`:442`), `HoldFirm` (`:445`), `Counter` + `CounterPrice` CoinStack (`:484-492`) |
| Heroes | `HeroesPanel.cs` (516) | Portrait-grid roster (2-wide, class-tinted `PortraitFrame`s), detail pane: worn gear w/ mark tallies (`LedgerQuery.MarkTally`), item memories, needs signal, relationships | roster card click (selects), `Provenance_{gearId}` History (`:249`) — otherwise read-only |
| Tavern | `TavernPanel.cs` (765) | "TAVERN GOSSIP" (`:153`), "WORK THE ROOM — IN THE COMMON ROOM" (`:186`) w/ per-patron Pursue rows, "THE HANDSHAKE" (`:323`) for the pursued thread, "OUT AT THE MINE" (`:606`) | `Pursue` (`:292`, adapter-local selection), `HandshakeAccept_{hero}`/`HandshakeDecline_{hero}` (commissions, `:368-377`), `HandshakeBuy_{hero}` (ore, `:407`), `TavernHistory_{hero}_{slot}` (`:575`) |
| Depths | `DepthsPanel.cs` | The parked `MineWatch` strip (resting host, `:33-41`) above a single venue tile holding the deepest-floor-per-hero board (`DramaState.DepthsBoard`) | none — read-only |
| Bounties | `BountyPanel.cs` (665) | "OPEN BOUNTIES" cards w/ `BountyJudged` sticky notes (`:94`), resolved judgments (`:131`), "POST BOUNTY" form (`:242`): `MineCrossSection` floor picker (`BountyFloor`), `CoinStack` reward (`BountyReward`), draggable `PosterComposer` | `PostBounty` (`:289`) or drag the poster onto the board |
| Demand | `DemandPanel.cs` | "WHAT HEROES ARE PASSING ON" (`:49`), "OPEN COMMISSIONS" (`:68`), "DEPTH STALL — CALL TO ACTION" (`:96`), "BOUNTY BOARD" with per-floor minimums (`:123`) | none — read-only |
| HeroCards | `HeroPanel.cs` | "HEROES" card list (`:86`): class, standing band, summed deeds, deepest floor, XP + display rank, needs/relationship chips | none — read-only |
| Progress | `ProgressionPanel.cs` | Profession-switch header (checkboxes + `ConfirmProfessions` `:180`, bell-rider) above five ladder cards (Forge/Depth/Roster/Wealth/Chronicle) from `ProgressionSpineSystem.Compute` | `ConfirmProfessions` |
| Lessons | `LessonsPanel.cs` | Every registry row's ShortLabel + TeachNote, chapter-numbered by act, plus every fired first-touch lesson (id→title table `:28-35`) — permanent, survives dismiss/complete | none — read-only |

---

## 7. Modal overlays (full-rect siblings above the drawer)

All pause the clock while visible and restore play state on close (visibility handlers `MainUi.cs:4488-4676`). All close via their own Close button and Esc (`ModalEscape`).

| Modal | file | Opened by | Shows |
|---|---|---|---|
| `LedgerModal` | `LedgerModal.cs` (934) | Return-Ritual auto-reveal (`MainUi.cs:888`); tray `OpenLedger` | Per-hero return cards (`LedgerQuery.ReturnCards`) in a wrapping grid (`:259`): fate line, gold purse/earned chips (`:512-515`), attribution beats (lead card sorted first — `LedgerCard_0`), warrant-save line (`:552`), "ORE OFFERED" rows with `BuyOre_{hero}_{mat}` (`:559-568`, Evening-gated mirror of `OreMarketHandlers`), tutorial tip (`:415`), first-loss block (`:292`), narrator line (`:399`), "THE RETELLING" collapsed to 8 lines + "Full tale" toggle (`:32-36`, `:684`, `:714`), `CloseLedger` (`:932`) |
| `RaidForecastBoard` | `RaidForecastBoard.cs` (556) | Day-end chain after Ledger (`MainUi.cs:4501-4510`); tray `OpenForecast` | One section per tomorrow's party (`RaidForecast.ForTomorrow`): roster, target floor, monsters en route, empty-slot asks with `ForgeOne_{hero}` / `TodoForge_{name}` jump buttons (`:333`, `:499` → `OpenPanel("Forge")` `MainUi.cs:3382`), `ForecastClose` (`:244`) |
| `CampPanel` (winch-house slate) | `CampPanel.cs` (457) | Auto via `SyncCampModal` when a party parks (`MainUi.cs:1330-1362`); reopened by the bell while VigilStop is unanswered (`:2920-2933`) | Per-party card: camped heroes' HP, heals-left "(of which yours: N)", target floor, floors/monsters still ahead (venue data), rejections verbatim; runner fee mirror (`CampPanel.cs:50-55`); flee-threshold urgency at ≤40% (`:61`) |
| — Camp verbs | | | `CampPick_{lead}` (select supply), `CampSend_{member}` (`SendSupplyAction`), `CampRecall_{lead}` (`RecallPartyAction`), "Send them deeper" (→ `SendDeeperRequested` → `Conductor.ResolveVigil`, `MainUi.cs:695-700`), "Forge something for them" (→ `OpenForgeRequested` → `OpenPanel("Forge")`, `:704`), Hold (close) |
| `ScryingMirror` | `ScryingMirror.cs` | `WatchButton` (`MainUi.cs:3027`), PiP body click (`:3526`), gatehouse overlook station (`:4355-4359`) | The borrowed `MineWatch` strip in a top band (`:319`), party tabs (`:322`), floor-progress line (`:326`), time-stretched `JourneyFeed` beats with `ManifestLine_{item}_{party}` and `AttributionBeat_{item}_{floor}` provenance buttons (`:210`, `:243`), `MirrorClose` (`:338`) |
| `CommissionBoard` | `CommissionBoard.cs` | tray `OpenCommissions` | One row per live commission (hero, slot, min quality, deadline, premium) with Accept/Decline; accepted rows show a status line; `CommissionClose` (`:246`) |
| `LegendsWall` | `LegendsWall.cs` (560) | tray `OpenLegends`; tavern story wall (`MainUi.cs:4343-4347`) | "THE FALLEN" memorials (`FallenSection` `:135`) with per-memorial **Honor** buttons (`HonorMemorialAction`) and **Reforge** rows (recipe + material `OptionButton`s, `ReforgeHeirloomAction`, gate mirror `:44-50`), depths records, legend items ≥ famous-beat threshold or Signed with `Legend_{item}` provenance buttons (`:432`), `LegendsWallClose` (`:504`) |
| `BestiaryPanel` | `BestiaryPanel.cs` | **only** `OnInteriorHotspotActivated("Bestiary")` (`MainUi.cs:4335-4339`) — see wiring audit §14.2 | Every registered venue's per-floor monster (Emberfall included though dormant), lit portrait w/ breathe/hover, name/stat card, `BestiaryClose` (`:296`) |
| `ChronicleScroll` | `ChronicleScroll.cs` | Auto on `CampaignEnded` (`MainUi.cs:1151-1158`) | The ending tallies rendered from the event itself, staged line-by-line (0.45s/line, `:30-33`), `CloseChronicle` (`:215`); never halts the kernel |
| `ProvenanceCard` | `ProvenanceCard.cs` | "History"/legend buttons in Shop/Heroes/Tavern/Mirror/LegendsWall | One item's `Item.History` prose, maker's mark, craft sub-scores |
| System menu | `MainUi.cs:4685-4784` | Esc in bare town | Resume / Settings / Save & quit to title / Quit game |

`SettingsPanel` (`SettingsPanel.cs`, 695 — two instances: title screen + system menu, `:10-16`): `FullscreenToggle` (`:200`), `MuteToggle` (`:216`), `UiScaleSlider` (`:225-245`), four mixer faders (master/music/SFX/narrator — narrator's caption names the skipping-law cost, `:50-56`), controls rebinding rows `Rebind_{action}_Key` for 12 rebindable actions (`:84`, `:267-290`), `ResetBindingsToDefaults` (`:290`), read-only shortcut legend from `ShortcutMap` with quick-travel locked hints (`:301`), `SettingsBack` (`:316`). Persisted via `UiSettings` (`UiSettings.cs:39-190`: fullscreen, volumes, mute, UI scale, bindings).

---

## 8. Companion & ambient spectate surfaces

- **`CompanionDock`** ("Tomorrow at the Counter" docket, `CompanionDock.cs`): bottom-left companion on its own CanvasLayer 40 — deliberately NOT in `OverlaySurfaces()`, so it never engages the clock, never blocks town input, and stays open through a running craft (`:34-50`). Three ways in: `docket_toggle` (C), the Forge drawer's `OpenDocketFromForge` button (`MainUi.cs:3352`), and its own collapsed chip. "Forge one" inside it jumps to the Forge (`:3343`). First-open teaches via first-touch (`:3349`, `ShowDocketLesson` `:2656-2662`).
- **`PipDock`** (`PipDock.cs`): bottom-right 300×96 journey dock, visible only during Expedition/Camp/Deep (slide in/out, `:12-19`), titled "SCRYING MIRROR", latest revealed beat + party HP pips + party-cycle arrow + "Watch the delve ⤢" expand → Mirror (`:22-29`). Suppressed while a drawer/modal owns the screen (`MainUi.cs:5011`).
- **`AdventureTicker`** (`AdventureTicker.cs`): the single bottom-edge marquee, 48px/s, 3-day retention (`:26-32`), fed only freshly-stamped tick events (KTD5-safe by construction, `:9-18`). Voices the quiet event tail (recruits, commissions expiring, rank-ups, rent/assessment lines, incidents, bounty collections — `:170-250`); deliberate exclusions documented at `:252-263` (SupplyDelivered, MarketShareShifted, TariffApplied).
- **`MineWatch`** (`MineWatch.cs`, 1,667): the lit SubViewport strip (1024×260 design, `:82-86`) — mine backdrop tiles, torch/campfire lights, walking hero figures (class walk frames), departure slate ("THE SEND-OFF", `:553`; empty-slate honest row `:1055-1067`), journey feed label, monster-slide + record-bark milestone flash (forced visible across the phase gate, `:48-66`), den-threat callouts. Live only while a party is underground; collapses to zero height otherwise; missing backdrop art collapses it permanently (`HasContent`, `:67-70`). Hosts **`DelveStage`** (`DelveStage.cs`, 1,440): beat-driven combat overlay — floor chip, monster + honest HP bar (depletes by the sim's own `DelveBeat.DamageDealt` against `VenueDefinition.MonsterHp`, `:51-62`), per-beat hero combat motion, damage numbers, kill poof, loot sparkle, quaff tint, proof flare, constitutional death-clouding (never a corpse, never an HP reveal, `:63-70`).
- **Journey pipeline**: `JourneyStream` (phase→stage table, self-censored beats, `JourneyStream.cs:7-28`) → `JourneyFeed`/`JourneyPlayhead` (time-stretches one party's beats across the phase, `JourneyFeed.cs:9-14`) → consumed by Mirror, PipDock, MineWatch; `DelveBeats` (`DelveBeats.cs:8-27`) builds the animation-shaped beat list.

---

## 9. Audio

- **Bus graph** (`AudioBuses.cs:5-30`): Master (limited) → Music, Sfx (→ SfxLoop), Narrator — built in code, never `project.godot`.
- **`AudioDirector`** (`AudioDirector.cs`): 6-voice SFX pool, two crossfading music players (2.5s, `:38-43`), phase-keyed beds, per-category faders + mute from `UiSettings`, `MuteEnvVar` for dev tools (`DevToolAudio.cs`). Composed-track table replaces the synth bed for all five phases (`day-first-light`, `town-dusk`, `quest-wait`, `night-still` ×2, `:52-75`); `MusicBed` (`MusicBed.cs`) synthesizes fallback loops and remains the only Underground theme.
- **Cues** (`SfxLibrary.cs:9-157`): PanelOpen/PanelClose/Click/Coin/Shelve/CraftDone/Bell/BountyPost/PartyDepart/Rejected/HammerOnBeat/HammerOffBeat/Quench/Bellows(loop)/5 grade stings/5 building entrance cues/MemorialHonor/DeathToll. One cue per tick, worst news first (`MainUi.SoundTheTick` `:3736-3777`); immediate actions are deliberately bell-silent (`:3756-3771`).
- **Narrator**: 7 triggers × 3–10 takes = 49 committed OGGs under `godot/assets/audio/narrator/` (census: act-advanced ×3, campaign-ending ×3, climax-reached ×3, death-epitaph ×10, killing-blow ×10, proven-save ×10, vigil-opening ×10) — `NarratorLines.AllAudioIds` walks the full set (`NarratorLines.cs:28-37`); un-voiced lines are an observable state (`NarratorRequest.Voiced`, `:39-47`). Spoken text always also lands on screen (Ledger narrator line `LedgerModal.cs:399`, Camp slate `MainUi.cs:1355-1359`).

---

## 10. The tutorial presentation layer (rework-planning section)

This is the exact mechanism inventory of what the presentation layer can and cannot do today.

### 10.1 The chain (`TutorialFlow.cs`, 3,292 lines)

Eleven `TutorialStep`s in ten displayed slots (`:16-45`; BuyMaterial+Craft share slot 1): BuyMaterial, Craft, Shelve, PostBounty (MinDay 3, `:596-597`), WatchDeparture, LookIn, OpenCounter, Vigil, EveningClose, MeetHeroes (MinDay 3), Commission (MinDay 3, terminal). Grouped into five acts = the five links (`TutorialAct` `:155-162`: Mark, HandOff, Dark, **Proof — deliberately empty of rows** `:141-153`, Memory); card prefix is "{Act} · {pos}/{total}" (`:486-499`), never a global countdown.

Each row is one `TutorialStepDef` record (`:298-355`): DisplayIndex, Act, Anchor, MinDay, ShortLabel, TeachNote, `IsDone` (durable-fact predicate over EventLog/ActionLog/state), `AdvanceFrom`/`AdvancesTo` (transition graph), optional `AnchorExists`/`AnchorFallback` (conditional anchors — a conditional row with no fallback **throws**, `ResolveExistence` `:1017-1034`), optional `CanonicalAction` (the one `PlayerAction` whose `ActionLegality.IsLegal` verdict gates the step's copy, `:1465-1490`; Craft is judged on the slot dimension only, `:1484-1487`).

Advancement (`Advance` `:1862-1907`): a single forward pass over the registry per tick, cascading; anti-stranding sweeps (WatchDeparture fires from any day-1 step `:634-638`; EveningClose fires from an unanswered Vigil `:778`). UI-only steps advance via notify hooks: `NotifyMirrorOpened` (`:1952`), `NotifyPanelOpened` — Tavern or HeroCards (`:1965`), `NotifyCampCardShown` (`:2000`), `NotifyEnteredBuilding` arrival ratchet (`:2018-2037`), `NotifyLedgerOpened` (`:2607`). Unconditional close at `ChainBackstopDay = 8` (`:1943`).

Copy machinery: `TopSlotText`/`CopyFor` → `StepText` (`:1196-1297`, per-step copy quoting live control labels via `PhaseVocab.BellVerb` and MainUi tooltip constants); `WaitText` deferred variants keyed to the *actual* blocking gate — day, slots, phase, gold — never a blank card (`:1586-1707`); `GatingNote` short checklist reasons (`:1713-1769`, Vigil's is muster-honest via `MusterPlan.Compute` `:1567-1573`, `:1780-1781`); `GoTo` walk/arrive acknowledgement (`:1389-1427`, movement hint = "WASD" `:1374`). Profession-true workshop vocabulary is pushed in, never derived (`SetWorkshopVocab` `:1153-1163`; station-id substitution `:986-999`).

Persistence: `user://tutorial_flow.json` (`:420`, `PersistedData` `:3211-3291`) — Completed/Dismissed/Step, ledger tip, warrant beat, first-loss day, fleece beat, demand-board beat + armed day, proof-beat day + card-opened, FirstTouch fired map, visited-anchor ratchet, pending MentorBanner lines. `ResetForNewGame` deletes it (`:2999-3005`); `ResetForReturningSmith` writes Dismissed=true while carrying the fired-lesson set forward (`:3057-3068`).

### 10.2 Anchors — the pointing vocabulary

`TutorialAnchorKind` (`:58-127`): **None**, **Building** (venue key → `Building2D` sprite pulse), **Hud** (live control by `FindChild` name — throws if unresolved), **Station** (one station inside a room — same pulse mechanism), **PanelControl** (a named control scoped to one registered panel/modal's content root), **PanelSection** (a named container — tolerant of zero/one/many rows). Constructors at `:198-241`.

Aiming rules (`AimAnchor`, pure/static `:917-960`): a Station anchor points at its **building** until the player is inside the venue; a PanelControl/PanelSection anchor points at the surface's declared **way in** while the surface is closed (`TutorialSurfaceRegistry.WayInFor`), and **throws** for a surface declared way-in-less. Existence fallback resolves before aiming (`AnchorFor` `:907-908`).

### 10.3 `TutorialSurfaceRegistry` (`TutorialSurfaceRegistry.cs`)

The one roster of addressable surfaces (`Surfaces` `:89-123`): 10 drawer ids + Ledger/Commissions/Legends/Camp/Forecast/Mirror/Bestiary/Chronicle/Pip/Docket, each with a content-root resolver and a declared `WayIn` anchor. **Four surfaces are declared to have no live way in** (`:42-62`): Heroes (only a roaming hero click), Bestiary (its tavern hotspot is not wired — class doc `:48-52`), Chronicle (auto-only), Pip (ambient). Camp's way-in is the `AdvancePhase` bell (reopen path, `:107-111`); Mirror's is `WatchButton` (`:113-115`); Docket's is a PanelControl inside Forge (`:119-122`).

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

**Can:** point at a building, a station in a room, any uniquely-named live HUD control, a named control or section inside any of the 20 registered surfaces; aim at the way-in while the target is closed; fall back per-row when a target doesn't exist yet; show an off-camera direction marker; scroll a target into view; restate the current step on demand (R / ↻) with a camera peek; hold the raid span while a step is unanswered; teach once-ever on first touch, on idle, and on repeated refusal; persist every one-shot across quits; run a dormant post-chain act (loss, proof, warrant end, demand board, fleece) with a bounded voice budget.

**Cannot (by construction, with the receipts):** point at a dynamic per-entity control that does not exist yet except via PanelSection containers (`TutorialAnchorKind` doc `:93-126`); point at a roaming hero sprite (`TutorialSurfaceRegistry.cs:93-96` — no anchor kind names one); open Bestiary/Chronicle/Pip/Heroes from closed (declared way-in-less, `:42-62`); move the camera on its own (law 1 — only the player's re-ask peeks, `MainUi.cs:2705-2711`); show two banner lines at once (one slot + queue, `MentorBanner.cs:20-24`); render markup beyond stripping `**` (`ObjectiveTracker.Plain` `:637-644`, `MentorBanner.cs:224-231`); regress a step (`TutorialFlow.cs:846-848`); or run more than one pointing overlay (one `TutorialOverlay`, one arbiter winner per tick).

---

## 11. Minigames (5 overlays inside the Forge drawer)

All are self-contained full-rect overlays built hidden at `ForgePanel.EnsureBuilt` (`ForgePanel.cs:2090-2128`), keyboard-claimed on open (`OpenedOverlay` `:1230`), force-cancelled if the drawer hides (`_Notification` `:1246-1277`), logged open/done/cancel to `PlaytestLog` (`:1293-1301`), and bound by the single-action contract: exactly one `CraftAction` on finish, nothing on cancel.

| Overlay | file | Act structure | Inputs |
|---|---|---|---|
| `ForgeMinigame` (Act 1, shaping) | `ForgeMinigame.cs` (1,501) | Steer the billet (X=shape, Y=heat) along the sim's own `ForgePath` polyline; strikes advance, bellows raise heat; required strikes fall with demonstrated accuracy (21→18 base/min, `:38-45`); hands off at the sim's forge-zone boundary via `ShapingDone` | `forge_strike` (Space), `bellows` (Shift hold / right-drag), Esc cancels |
| `QuenchMinigame` (Act 2) | `QuenchMinigame.cs` (482) | Heat falls on its own; one decisive `Plunge` inside the tier-narrowed band; auto-plunges at timeout — owns the one `CraftAction` (`:12-35`) | `plunge` (Space/Enter), Esc |
| `AlchemyBrewPuzzle` | `AlchemyBrewPuzzle.cs` (1,011) | Discrete pour-order puzzle, no clock, no `_Process`; sim scores the submitted `AlchemyReagentPuzzle` (`:13-30`) | cursor + `confirm`, undo, Esc |
| `EngineeringBench` | `EngineeringBench.cs` (998) | Clockless spatial part-seating against the sim schematic; reseat-is-free honored in the recorded fill order (`:13-33`) | cursor, `confirm`, `pull_part`, `crank_stroke`, Esc |
| `TanningFrame` | `TanningFrame.cs` (840) | Clockless coverage-with-restraint over the sim's patch grid (`:13-28`) | cursor, `scrape`, `confirm`, Esc |

Post-craft: the G1 result ceremony (grade stamp, star row, three sub-score pips, grade sting, 2s auto-dismiss or Skip/Esc — `ForgePanel.cs:1565-1614`, non-blocking backdrop `:2139-2152`), plus the "the mark, read" first-touch showing the item's actual `MakersMark` (`:2334-2352`).

---

## 12. Theme, widget kit, art resolution

- **`GameTheme`** (`GameTheme.cs`): the palette (Void/Iron/Arcane/Coolant/Ember/Bone/Blood + roles `:65-125`), Silkscreen header font (`:56`), font sizes (16 body/legibility floor, 20 HUD value, 22 header, `:131-166`), spacing 4/8/12/16, radii, `PanelStyle`/`PanelStyleWood` (the wood frame is `ui-frame-wood.png`, null-tolerant fallback), button styles, scrollbar styles (`:198-360`). Built once and cascaded from the scene root (`MainUi.cs:669`, `NewGameSelect.cs:147`).
- **`UiKit`** (`UiKit.cs`, 820): `Card`, `Section` (title-derived names for PanelSection anchors, `:206-237`), `StatChip`/`StatChipCompact`/`IconChip`, `ShortcutBadge`, `PortraitFrame`, `ArtRect` (themed fallback + caption on a manifest miss; misses logged once, `:374-494`), `ListRow` (icon|name|price|owned|action with disabled-reason tooltips), `SceneBanner`, `DrawerHeader`, keyboard claim/reclaim helpers, `MakeButtonsMouseOnly`.
- **Art resolution ladder**: `AssetCatalog` composes ids from sim concepts (`item-{recipe}`, `monster-{slug}`, `{venue}-backdrop/-entrance`, `hero-{classId}`, Sunken Crypt hyphen normalization `AssetCatalog.cs:40-50`) → `IconRegistry` loads by id with a manifest presence check (`art-manifest.json`, `IconRegistry.cs:12-25`) → `ArtVariants.Pick` selects deterministic per-entity variants from `-v{N}` pools → `TownAssets2D` adds loud placeholders (magenta border + missing-id pixel text + one log, `TownAssets2D.cs:7-17`). Hand-authored SVGs: 9 concept glyphs + 19 ore icons (`assets/icons/`), 6 hero figures (`assets/sprites/hero_{classId}.svg`, loaded `IconRegistry.cs:168`).

---

## 13. Persistence, dev surfaces, and the CLI

### 13.1 Persistence files (all `user://`)

| File | Writer | Contents |
|---|---|---|
| campaign save (one rolling slot) | `CampaignSave.cs` (envelope + `SaveCodec` bytes, `:14-41`) | the sim world; autosaved each Evening (`MainUi.cs:1144-1147`), saved on quit paths; every failure degrades to "no save" |
| `tutorial_flow.json` | `TutorialFlow.Save` (`:2958-2976`) | chain + every once-ever flag + mentor queue (§10.1) |
| `clock_settings.json` | `MainUi.ClockSettings` (`:5033-5087`) | auto-advance opt-in |
| UI settings | `UiSettings.cs` | fullscreen, 4 volumes, mute, UI scale, key rebinds |

### 13.2 Dev/observability surfaces (in the client, inert in normal play)

- `PlaytestLog` (`PlaytestLog.cs`): JSONL session log, opt-in via `MM_PLAYTEST_LOG` (launchers set it; `:22-25`); one row per tick with economy columns, plus action/decision/note trails with causes (`MainUi._pendingTickCause` `:221-231`).
- `DecisionEvents` (`DecisionEvents.cs`): mirrors the sim's own typed reasons (pass/decision/bounty/walk/death/beat/DecisionExplained) into the session log (`:6-30`).
- `EngineDistress` (`EngineDistress.cs`): in-memory capture of every push-warning/error (`:6-20`); scanned by `EngineLogAnomalies`.
- Receipt/screenshot seams (env-gated, never fire in play): `TOWN_SHOT` (`MainUi.cs:775-793`), `SHOT_PROFESSION`/`SHOT_PROFESSION2` (`:489-503`), `SHOT_WATCH_FIGHT` staged fight (`:512-616`), `Dev_QueueDay1TutorialLadder` bridge (`:805-826`), `godot/tools/shot_harness.gd` + `play_harness.gd`.
- `AgentPlaytest` (`AgentPlaytest.cs`): the model-driven harness — writes a per-turn `state.json` digest (visible text, buttons + enabled state, value controls, location incl. overlay names via `MainUi.ActiveOverlayName` `:3947`), accepts verbs press/move/key/set/wait/advance/stop (`:362-372`).
- `FullPlaytest` (`FullPlaytest.cs`): five real-launch multi-day playthroughs with pixel-motion measurement (`:14-33`); `ScenarioWriter`: manufactures a day-N save through the real `CampaignSave.Save` (`:9-30`); `FrameCapture`, `ScreenObservation`, `DevToolAudio` (mute for tools).
- `play.bat` is the one launcher (staleness-gated); `edit.bat` opens the pinned editor.

### 13.3 The CLI (`sim/GameSim.Cli`, 3,905 lines)

Interactive text play over the same `Tick(actions)` surface (`Program.cs:14-16`). Full command set (help text `Program.cs:183-236`; dispatch `:242-1007`):

- **Verbs**: craft (with explicit grade), profession, talent, buymat, stock, price, unstock, buyore, bounty, send, recall, accept-commission, decline-commission, honor-memorial, reforge-heirloom, upgrade-forge, buy-supply, masterwork, commission-legendary, counter open/present/suggest/close, haggle accept/hold/counter.
- **Reads**: status, recipes, talents, mats, items, heroes, hero <name>, shelf, forecast|telegraph, board, demand, gossip, advice, progress|spine, modifiers, day, next, export.
- **Modes**: `batch` seed-sweep telemetry farm (`BatchRunner.cs`), `decisions` decision-surface logger (`DecisionLogger.cs`), `decisions play` scripted index-choice replay (`DecisionPlay.cs`), `Characterize` ladder measurement, `ConsequenceProbe` does-the-choice-matter probe.

**CLI can, client cannot:** craft with an arbitrary explicit grade (`craft … grade <0-1000>` — the client earns grades by hand); run batch/decision/consequence analysis; export chronicles; jump a whole day in one command (`day`).
**Client can, CLI cannot:** the five minigames (the CLI's grade parameter stands in); walking/proximity/camera; the tutorial layer (all of §10 is client-side); drag-and-drop; audio/narrator; the counterfactual proof *flare* and death-clouded delve animation (the CLI prints beats as text); save/continue (the CLI has no `CampaignSave` caller — grep `CampaignSave` in `sim/GameSim.Cli/` → 0 hits).

---

## 14. THE WIRING AUDIT

### 14.1 Orphans

| Item | Evidence |
|---|---|
| **`godot/assets/art/town2d-player.png`** — committed PNG referenced by nothing | grep `town2d-player` across all `*.cs`, `*.py`, `*.md`, `*.ps1`, `*.tscn` at `28fd0452` → only its own `.import` file. The player draws `player_smith[.png]` (`PlayerController2D.cs:37`) |
| **`town2d-tile-grass.png` / `town2d-tile-cobble.png` / `town2d-tile-path.png`** — committed, never drawn at runtime | grep `town2d-tile` in `godot/scripts/` → 0 hits; the ground draws `town2d-ground-atlas` (`Town2D.cs:2084`). The three tiles survive only as palette *sources* for `art/pipeline/gen-*-interior.py` and rows in `art-manifest.json:305-307` — pipeline inputs living in the shipped game directory |
| **`HANDOFF.md` (repo root)** — a 2026-07-18 handoff doc asserting a world-state git has long superseded | not a surface, but it is doc-rule-8 material a future session may obey; flagged for the owner, not deleted here (outside `docs/reference/` scope) |
| Everything else checks out | All 161 base art ids (after stripping `-v{N}`/`_step`/`_walk2`/`_walk4` variants consumed by `ArtVariants`/`SpriteMotion`) except the four above are reachable: literal mention, or a composed family (`item-*` ← `AssetCatalog.cs:27`, `monster-*`/`{venue}-*` ← `:33-39`, `hero-*`, `town2d-hero-*` ← `TownAssets2D.cs:160`, `town2d-monster-*` ← `DelveStage.cs:717`, `town2d-townsfolk-*` ← `TownsfolkNpc2D.cs:216`, `props-*`/`town2d-prop-*` ← `TownLayout2D.Props`). All 9 minigame PNGs load (`AlchemyBrewPuzzle.cs:979-981`, `ForgeMinigame.cs:1454-1458`). All 49 narrator OGGs are enumerated by `NarratorLines.AllAudioIds`. All 34 SVGs resolve through `IconRegistry` (9 glyphs + 19 `ore_*` + 6 `hero_*`) |

The three dev scenes (`agentplaytest/fullplaytest/scenariowriter.tscn`) are launched by path from `tools/*.ps1` — dev-only, not orphans.

### 14.2 Dead ends

| Item | Evidence |
|---|---|
| **The Bestiary is unreachable in real play.** The panel, its art (16 monster portraits incl. all 5 Emberfall floors), and its `MainUi` route all exist, but the only opener is `OnInteriorHotspotActivated("Bestiary")` (`MainUi.cs:4335-4339`) and **no station in `InteriorLayout2D.Rooms` names Action "Bestiary"** (grep `"Bestiary"` in `InteriorLayout2D.cs` → doc comment only, `:31`). The tavern's stations route to Tavern/Legends. Known and recorded in-code (`TutorialSurfaceRegistry.cs:48-52`: "unreachable since the pre-2.5D pivot") — which also means **the dormant Emberfall Foundry's only player-facing preview is unreachable** (`BestiaryPanel.cs:31-39`) |
| **The Heroes roster panel is unreachable while every hero is away or dead.** Its only door is clicking a wandering hero's sprite (`MainUi.cs:4127-4131`; `TutorialSurfaceRegistry.cs:93-96` declares no other way in). During Expedition/Camp/Deep all party members are `Away` (invisible at the gate) — the roster/gear/provenance detail pane cannot be opened exactly while its subjects are underground. (HeroCards/Renown remains reachable but shows the digest, not gear/history.) |
| `PhaseChip`'s tooltip legend claims SendSupply/RecallParty availability the Camp modal owns — cosmetic-only duplication pinned by `PhaseVocabTests` (`MainUi.cs:2093-2098`) | not a dead end, listed for completeness |
| Flavor stations (quench trough, ledger desk, stock crates, hearth, herb bundles, flywheel, vats, gate winch) press to a toast, not an action | **by design** — "honest flavor", `InteriorLayout2D.cs:36-45`; not defects, but they are the only E-targets in rooms that do nothing |

### 14.3 Blind spots (sim decides it; no screen shows it)

| Sim state / event | Reader count (grep over `godot/scripts/`) | Note |
|---|---|---|
| **`GameState.RivalMarketSharePermille`** (`World.cs:250`) | **0** (grep `RivalMarketSharePermille` → contracts + sim systems only; also 0 in `sim/GameSim.Cli/`) | The rival's competitive edge — raised by idle days, discounts rival stock — is computed daily and shown **nowhere**. The rival shelf shows prices, never the edge |
| **`MarketShareShifted`** event (`Events.cs:214`) | **0 code readers** (2 hits, both comments: `AdventureTicker.cs:255` deliberate exclusion, `HeroPanel.cs:60`) | With the field above, the whole market-share mechanism is invisible |
| **`TariffApplied`** event (`Events.cs:130`) | **0 code readers** (1 hit, the deliberate-exclusion comment `AdventureTicker.cs:257-263`) | The faction discount is voiced via the ore offer's price + `FactionStandingShifted`; the per-purchase delta itself never renders |
| **`PlayerState.BatchEcho`** (`Player.cs:17`, `:44`) | **0** in godot and CLI (grep `BatchEcho`; sim readers: `CraftingHandlers`, `ForgeTierHandlers`) | Repeat-craft echo state that biases quality — invisible; a player cannot see that their batch echo exists or expires |
| **`LootIncomeReceived`** (`Events.cs:102`) | **0 direct** godot readers | Folded into `LedgerQuery.ReturnCards`' gold lines sim-side — the total renders; the typed event does not |
| **`PartyCampReport`** (`Events.cs:151`) | **0 code readers** in godot (1 hit = doc comment `MineWatch.cs:42`) | `CampPanel` reads live `InFlight` instead; the event's own narration reaches only the CLI (`CampNarration` — `Program.cs`) |
| `HeroDecisionExplained` | 8 (HeroesPanel/HeroPanel/log) | healthy — listed as the contrast case |
| Formerly-flagged `KillingItem` / `Hero.Pack` | 15 / 9 | previously one-reader fields; now multi-surface |

### 14.4 Silent fallbacks (quiet degradation paths, with their loudness today)

| Path | Behavior | Loud? |
|---|---|---|
| `MineWatch` missing `mine-backdrop` | whole strip collapses **forever**, DepthsPanel renders as if the feature does not exist (`MineWatch.cs:67-70`) | quiet by design — documented, but a broken import shows nothing on screen and nothing in-game says why |
| `IconRegistry.Art` / `AssetCatalog` unknown id | returns null; `ArtRect` renders themed fallback + caption and warns once (`UiKit.cs:374-494`); `TownAssets2D` placeholder is magenta-bordered with the id baked in (`TownAssets2D.cs:7-17`) | loud since the #316-class fixes |
| `AudioDirector.For(this)` in panels | null-conditional — no director, no sound, no log (e.g. `ForgePanel.cs:1509`, and every cue site) | quiet; acceptable for tests, but a mis-mounted director in play would be silent |
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
| Tray icon buttons need both a `CustomMinimumSize` and per-instance margin trims and an `icon_max_width` cap or they overflow the header (`MainUi.cs:4020-4089`) — the 8th icon (Lessons) is what last pushed it | adding a 9th tray book will re-fight this |
| The tutorial dock's position is measured off the objective card, clamped + internally scrolled (`MainUi.cs:1748-1771`) after two magic-offset regressions | derived, no longer a knob |

---

## 15. Unverified — worth checking

Questions only; no claims.

1. Does anything ever call `BestiaryPanel.ShowAll()` in a shipped input path this census missed (a harness bridge aside)? If not, is the intended fix the tavern station slice 2 that `TutorialSurfaceRegistry.cs:48-52` describes?
2. `PhaseClock` Camp/ExpeditionDeep "borrow MorningSeconds" (`PhaseClock.cs:73-76`) — with the RaidConductor owning those phases, can the borrowed 45s timer ever actually fire (auto mode, Conductor at `Idle` during Camp)? Is the fallback dead code or a real path?
3. The `Heroes` drawer id and `HeroCards` id render overlapping information — is the roster's gear/provenance pane intended to stay hero-click-only, or should Renown grow a detail view so gear history is reachable during a raid?
4. `town2d-tile-*.png`: should pipeline palette sources live under `art/pipeline/sources/` instead of the shipped `godot/assets/art/`? (Their `.import` files mean Godot imports them on every fresh checkout.)
5. The interact-prompt chip centers on the window, not the world strip — with a drawer open (world input blocked) the chip hides via prompt-empty, but is there a frame where a station prompt renders under the drawer?
6. `RivalMarketSharePermille` / `BatchEcho`: are these deliberately invisible (telegraphed via rival prices and repeat-craft grades) or awaiting surfaces? Nothing in copy names either mechanism.
7. `SettingsPanel` UI-scale slider: which surfaces have been eyeballed above 1.0 at 1152×648? The wrap/clamp pattern in §14.5 was tuned at scale 1.
