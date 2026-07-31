# The human-playtest harness

*2026-07-30 — why `godot/tests/HumanPlayer.cs` exists and the one rule it enforces.*

## The problem

531 engine tests were green while the game was unplayable. Owner's playtest, verbatim:

> "Forge mini game still doesn't work, shift doesn't do and space seems to actually be the bellows /
> i am incapable of creating anything - something is wrong lol"
> "depths menu is cut off still"
> "bounties menu is broke"

Every miss had the same shape: **the test drove a seam one layer below the thing that was broken.**

| What the test did | What it therefore could not see |
|---|---|
| `mg.ForgeStrike()` on an overlay never added to the tree | The overlay held no keyboard focus, so every key was dead |
| `button.EmitSignal(Pressed)` | `EmitSignal` does not move focus — **it passed against the broken build** |
| `_GetDragData()` directly | Real mouse-drag was broken |
| `RenderedText(root)` — collects text from hidden and off-screen nodes | Two panels were cut off at the window edge |
| Asserted immediately after `Build()` | The camera never followed the player (~500 tests missed it) |

The pattern is not carelessness. Each of those seams was chosen deliberately, for determinism, and each is correct for what it was originally written to check. The failure is that **no test existed at the layer above them**, so the seams were load-bearing for claims they could not support.

## The rule

> A test may only act through `Viewport.PushInput`, and may only read text that is visible **and** on screen.

Mechanically checkable, and enforced structurally rather than by discipline: `HumanPlayer` is constructed from a *viewport*, not from game nodes. It exposes no way to call a game method, emit a signal, or queue a sim action. A test written against it **cannot** commit any of the five mistakes above, because the dishonest path is not reachable from the object it was handed.

Make the honest path the only available path.

## What makes a click honest

`Click` subscribes to the target's `Pressed` signal, pushes a real motion → press → release at the control's centre, then asserts the signal actually fired.

That last step is the whole point. A button that is disabled, hidden, zero-sized, scrolled off screen, or **covered by another control** will not fire it. Reachability stops being something a test assumes and becomes something it proves — and the failure message names which of those it was.

## What it observes, and why not pixels

It reads laid-out geometry and visible text, not the rendered frame. Not a compromise — a constraint with a hard edge behind it:

**Pumping frames while any `SubViewport` is drawing hangs the gdUnit4 headless runner.** A hang is not a red test; it is a dead run that takes every `[RequireGodotRuntime]` suite with it and reports the surviving pure-.NET remainder as "Passed". That exact shape once turned 502 reported tests into 68. `HumanPlayer`'s constructor therefore calls `StopSubViewportsRendering` — in the constructor, not in each test, because the failure mode is a hang rather than a red test and no test should be able to forget it.

Input routing, layout, focus and physics picking all work with drawing off, which is everything this harness asserts on. Real-pixel checks stay with the windowed tools in `godot/scripts/tools/`.

## Three false alarms it produced first, and the fixes

A detector that cries wolf gets switched off, which would cost more than the bugs it finds. All three were bugs in the *harness*, found by not trusting its first output:

1. **Every panel reported off screen.** It was measuring mid-slide; the tell was fractional offsets like `646.7778`. Fixed with `WaitForLayout`, which pumps until the rect stops moving — never a guessed frame count. Also required `DrawerHost.CurrentContent`, because the drawer *host* is a full-rect Control that never moves and so reads as settled on frame one while the content is still animating.

2. **Every recipe row reported off screen.** Content below the fold of a `ScrollContainer` is reachable — the player scrolls. Now excused when it has an on-screen scrollable ancestor, and only then.

3. **A correctly-sized panel reported off screen** because it ended at x=1152.0026 in a 1152px window. Slide easing settles to a sub-pixel residual, so every enclosure question is now asked against `WindowRect` (the visible rect grown by a 1px tolerance), never the raw rect.

A fourth was a genuine correctness bug, not a tolerance issue: it reported the world's "Gate" nametag as 2px off screen in every panel. That node lives inside Town2D's `SubViewport`, whose rects are in a different coordinate space (and additionally scaled by `StretchShrink` and scrolled by a `Camera2D`) — comparing them to the window rect compares unrelated numbers. `Descendants` now stops at a `SubViewport` boundary. World-space visibility is a real concern with a real owner (`CameraFollowTests`, `Town2DSceneTests`); answering it in the wrong units only manufactures noise.

## Two more harness bugs, found by not trusting it

4. **Every panel reported as off screen, again**, this time because `WaitForLayout` watched only the content root's own rect. A container's `queue_sort` is deferred and nested containers cascade over several frames, so the outer panel reaches its final position while sections inside it are still moving. It now hashes the rects of the root **and every descendant** and waits for that to hold steady. Symptom of getting this wrong: rects that cannot coexist — siblings apparently overlapping, a VBox child sitting above one declared before it. I spent real time hunting a Shop layout bug that was a measurement taken too early.

5. **The whole HUD reported as unreachable in every panel.** Correct observation, wrong expectation: `DrawerHost` puts a click-catching veil over everything behind an open drawer, so the HUD genuinely is inert while a panel is open — that is what a modal is for. `ClickableButtons` now takes a scope, and the sweep passes the open panel's own subtree.

Also worth recording, because it burned two iterations: **node names are not identifiers here.** Panels rebuild their content on every refresh, so Godot's auto-generated names change (`@Button@1017` → `@Button@2041`), and `FindChild` does *pattern* matching on top of that. And instances are no better — the first click rebuilds the panel and frees every other instance in the batch. Position in a freshly-derived clickable list is the only handle that survives.

## Real bugs it found immediately

All invisible to the 531 existing tests, and all three are complaints the owner actually filed:

- **Depths panel demanded 724px inside a 600px drawer.** `DepthsPanel` built a `GridContainer` with a hardcoded `Columns = 2` and 360px tiles. A `Control` cannot lay out narrower than its combined minimum size, so the whole panel was forced past the drawer's right edge — where anchors cannot help and the vertical-only scroller cannot reach. Latent until a second venue (the Gloomwood) went live and gave it a second column to overflow with. Fixed by *deriving* the column count from the available width (`ColumnsThatFit`), so widening the drawer restores the two-column intent and the overflow is arithmetically impossible.

- **Demand panel demanded 630px.** One `StatChip` per mine floor in a plain `HBoxContainer`, whose minimum width is the sum of its children. Correct at three floors, broken at six — a time bomb, not a typo. Fixed with `SimPanel.AddWrappingRow` (`HFlowContainer`). **Any row whose children come from a loop must use it.**

- **The Shop's "Open Counter" button did nothing at all.** The most serious of the three, and the one nobody could have found by reading the code. `SimPanel` derives from `Control`, not `Container` — and a plain Control does not derive a minimum size from its children. `ShopPanel` nests `CounterPanel` above its shelf sections inside a `VBoxContainer`, so the VBox gave it **zero height**: it reserved no space, its own full-rect-anchored content overflowed that empty box, and the shelf `DropZone`s were laid out straight through it. Those drop-zones then swallowed every click aimed at the button underneath.

  Every property of every control involved was correct throughout. Only their positions *relative to each other* were wrong, and nothing else in the repo looks at that.

  Fixed by overriding `SimPanel._GetMinimumSize()` to report its content's needs, plus a `UpdateMinimumSize()` call at the end of `CounterPanel.Refresh` (a plain Control is never told when a child's minimum size changes). **Negative control verified:** disabling the override turns `EveryVisibleButton_ActuallyRespondsToARealClick` red.

  The generalisable lesson is in `OverlappingSiblings`, which now flags the *cause* rather than the symptom: a container child that reports no size while containing visible content. The pairwise-overlap check alone missed this case — the collapsed panel was filtered out by its own zero size — which is why the detector checks both.

### What the button census turned up

Not bugs I fixed, but facts worth a decision (day 1, Morning, fresh campaign):

| Panel | Buttons | Note |
|---|---|---|
| Forge | 74 (37 disabled) | Gating on day-1 poverty — expected |
| Shop | 2 | |
| Heroes | 7 | |
| **Tavern** | **0** | No controls whatsoever |
| **Bounties** | **1, disabled** | Cannot post a bounty on day 1, with no visible reason |
| Depths / Demand / HeroCards / Progress | 0 | Read-only surfaces — plausible by design |

The Bounties row is very likely the owner's *"i posted a bounty at the 'gate' but nothing happened?"* and *"bounties menu is broke"*: a panel whose single control is disabled and unexplained reads as broken rather than as gated. Tavern having no controls at all needs an answer — either recruiting is gated later (say so on screen) or it is unwired.

## What it asserts vs. what it reports

Hard failures — structural claims about whether the game is operable:

- No panel demands more width than the drawer gives it (`NoPanel_DemandsMoreWidthThanTheDrawerGivesIt`)
- No text is cut off with no way to reach it (`EveryPanel_FitsOnScreen`)
- No panel draws its controls on top of each other (`NoPanel_DrawsItsControlsOnTopOfEachOther`)
- Every visible button responds to a real click (`EveryVisibleButton_ActuallyRespondsToARealClick`)
- No phase leaves the player with nothing to do but ring the bell (`NoPhase_LeavesThePlayerWithNothingToDoButRingTheBell`)
- The sweep covers every registered panel (`ThePanelSweep_CoversEveryRegisteredPanel`)

The click sweep's non-vacuity guard is **per panel**, not a global count. Panels mutate as they are clicked — a purchase removes its row, a craft rebuilds the list — so found-vs-clicked has a legitimate gap, and chasing that number upward only produces baroque test logic. What matters is that no panel was skipped *entirely*, since that is what would hide a whole broken surface. A click that frees its own button counts as a success, not a failure: `ObjectDisposedException` is caught **before** `InvalidOperationException` because it derives from it.

Clicks aim at the centre of the control's **visible** part — its rect intersected with every clipping ancestor and the window. Godot clips input exactly as it clips drawing, so a card taller than the scroller holding it cannot be clicked at its geometric centre. Two of four hero cards failed that way, with nothing over them, which is what showed the *point* was wrong rather than the control.

That last one matters more than it looks. Without it, adding a tenth panel silently escapes every check above while the suite stays green — the same declare-it-then-forget-to-wire-it shape that already shipped a dormant ground-tile system and four invisible panel banners on this project.

Tuning values (craft grades, difficulty, timings) are **not** asserted here. They belong in telemetry, because a test that fails on a balance tweak gets weakened until it fails on nothing.

## Anti-fakery is a requirement, not advice

Every test added here must state how it can fail, and that must be verified by hand — the guard removed, the test observed going red. Two of this session's tests passed against the broken build on first write. A check that cannot fail is not a check.

## Still owed

- **The synthetic forge player.** A policy that plays the heat/shape loop the way a person does (pump, strike in rhythm, be imperfect) to answer "is this winnable and does it feel fair", rather than "does the API respond". The owner suggested a local LLM; a scripted policy with a seeded imperfection budget is the better tool — it is reproducible, needs no model, and the interesting output is a grade *distribution* over many seeded runs, which an LLM would only make noisier and slower to obtain.
- Extending the same treatment to the tanning frame, engineering bench, and alchemy brew puzzle.
- A drag verb built on `HumanPlayer.Drag` to replace the `_GetDragData()` seam in `RealDragOntoShelfTests`.
