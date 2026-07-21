# Gate B rev.2 open questions — research + recommendations

**Date:** 2026-07-21
**Status:** research input for Brian's sign-off on the seven open questions in
`docs/design/2026-07-21-3d-playtest-redesign.md` §6 (Q1–Q7).
**Method:** each question researched against industry best practice (cited) and grounded in this
project's reality: solo hobby dev, Godot 4.6.3-stable .NET pin, presentation-only pivot (KTD2),
headless render-hang constraint (no CI pixels), existing `TOWN_SHOT` dev tool, P0/P1/P2 convention.

---

## Q1 — Comfort policy: auto-P0 for motion sickness; build camera options now?

**Decision at stake.** (a) Is "any motion-sickness report = auto-P0" the right severity policy?
(b) Should the slice ship user-facing camera settings (follow speed, distance) speculatively, or
only if the first run reports discomfort?

**What the research says.**
- Motion sickness is driven by *vection* — illusory self-motion creating a visual/vestibular
  mismatch. First-person free cameras with wide FOV are the high-risk class; fixed, evenly-moving
  raked/isometric-style cameras are consistently listed as the *sickness-friendly* end of the
  spectrum because they produce weak vection ([Kotaku expert
  round-up](https://kotaku.com/video-game-motion-sickness-expert-tips-call-of-duty-fps-1849147209),
  [EIP Gaming — accessible games for motion
  sickness](https://eip.gg/news/accessible-games-for-motion-sickness/), [vection factor study,
  PMC](https://pmc.ncbi.nlm.nih.gov/articles/PMC11618617/)). Our rig (fixed −50° pitch, FOV 45,
  no player-controlled rotation, no head-bob, no motion blur, no camera shake) already omits every
  major trigger the mitigation literature names ([Busseneau — alleviating motion
  sickness](https://nicolas.busseneau.fr/en/blog/2020/09/alleviating-motion-sickness-in-first-person-video-games)).
  The one residual risk is exactly what the redesign flags: follow-lerp "swim" (`1-exp(-5·dt)`),
  which is a *smoothness/judder* complaint far more often than a nausea one at this camera class.
- Accessibility standards ([Xbox Accessibility Guideline
  117](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/117),
  [Game Accessibility Guidelines full
  list](https://gameaccessibilityguidelines.com/full-list/)) do say players should eventually be
  able to adjust camera movement speed and related visual motion. But those are ship-quality
  guidelines for a public audience; nothing in them argues for building a settings UI before the
  first tester has reported a problem.

**Recommendation.**
- **(a) Keep auto-P0.** It costs nothing (the camera class makes a report unlikely), and encoding
  "comfort is never negotiable" now sets the precedent cheaply before the camera ever gets more
  dynamic (interiors, spectating cinematics in PR2+ are where risk actually enters).
- **(b) Do not build user-facing options now.** `CameraRig` already exposes `FollowSpeed`,
  `Distance`, `Pitch`, `FOV` as `[Export]`s — that is the correct solo-dev middle ground: if run 1
  reports swim/discomfort, the fix is a one-line tuning change or (worst case) snapping follow to
  physics ticks, not a settings screen. Log a **P2 backlog item**: "camera comfort options
  (follow speed / motion toggles) required before any public/itch build" so the accessibility
  guideline isn't lost, just deferred to the audience that needs it.

**Unlocks.** Card 5's comfort language stands as written; no new pre-flight work.

---

## Q2 — Performance floor + target machine + FPS overlay

**Decision at stake.** Confirm the fps floor (60 sustained / ≥45 dips), the measurement machine,
and whether to build an env-gated FPS overlay.

**What the research says.**
- 60 fps is the consensus PC target; 30 is "playable floor" territory reserved for slow turn-based
  or cinematic titles, and dips into the 45–60 band are generally tolerated in non-twitch genres
  ([GamingScan — best FPS for gaming](https://www.gamingscan.com/best-fps-gaming/),
  [fpscalculator — good FPS](https://fpscalculator.net/blog/game/good-fps-for-gaming/)). A low-poly
  Kenney town with a dozen actors is a trivially light 3D load — if this scene can't hold 60 on a
  real GPU, that's a defect signal (SubViewport misconfiguration, per-frame allocation, reconcile
  churn), not a hardware limitation. Setting the floor at 60 keeps that signal sharp.
- **No overlay needs to be built.** Godot has a **built-in debug performance overlay since 4.4**
  (F3 cycles hidden → compact FPS/frametime → detailed; also settable programmatically via
  `SceneTree.debug_overlay_mode`) — it works in exported/fullscreen projects with no editor
  attached ([godotengine PR #100829](https://github.com/godotengine/godot/pull/100829)). Our pin
  is 4.6.3-stable, so it's present. The default F3 binding requires zero code and zero
  `project.godot` edits (deny-list safe). The [`Performance`
  class](https://docs.godotengine.org/en/stable/classes/class_performance.html) and the
  [godot-debug-menu addon](https://github.com/godot-extended-libraries/godot-debug-menu) exist as
  escalation paths if per-event hitch attribution is ever needed — don't reach for them yet.

**Recommendation.** Confirm **sustained 60 / dips ≥45 / sustained <45 = P0** as proposed, measured
on **Brian's actual play machine at his actual resolution** (the only machine that matters for a
solo project; note GPU + resolution in the findings doc header so future runs are comparable).
**Do not build a custom overlay** — Card 5 says "press F3"; done. If a hitch needs attribution,
that's the moment to consider the debug-menu addon, as a finding-driven follow-up.

**Unlocks.** §3.1 setup line changes from "no in-game counter exists yet" to "use the built-in F3
overlay"; deletes a would-be dev-tool task from the backlog.

---

## Q3 — Discoverability bar: must the proximity prompt be noticed unprompted?

**Decision at stake.** Card 2 currently caps the axis at PARTIAL if the tester only ever clicks
and never notices the highlight + `E · <label>` prompt. Too strict?

**What the research says.**
- UX literature is unambiguous that an affordance nobody discovers is functionally absent —
  hidden affordances need *signifiers* (highlight, prompt) and are judged by whether users find
  them unaided; undiscovered ≠ working ([UXPin — affordances in UX
  design](https://www.uxpin.com/studio/blog/affordances-user-interaction/)). The proximity-prompt
  pattern itself ("walk near → contextual key prompt") is a well-established convention players
  transfer between games — e.g. it's a first-class engine primitive on Roblox precisely because
  it's the standard way to say "this object is interactive" ([Roblox proximity prompts
  docs](https://create.roblox.com/docs/ui/proximity-prompts)). A genre-literate PC player who walks
  a 3D avatar near a highlighted building with an `E · Forge` billboard *should* read it; if they
  don't, the signifier (contrast, size, placement, timing) is genuinely too weak — that's real
  signal, not an unfair bar.
- The redesign's own logic seals it: the prompt **is** the affordance the 3D pivot was built to
  deliver ("interaction is obvious" is a stated plan goal). If click-only were PASS, the gate
  would certify the pivot's headline feature while it silently failed.

**Recommendation.** **Keep the bar: click-only caps the axis at PARTIAL.** Two calibrations:
1. Keep "noticed by the second building" as the PASS line — first-building blindness is normal
   (attention is on movement); by the second approach the highlight has had two exposures.
2. Severity mapping stays as drafted: prompt-never-noticed = **P1** (discoverability collapse),
   not P0 — the click path keeps the game playable, so it's "real defect hurting play," which is
   exactly P1's definition.

Caveat that feeds Q5: with a warm tester, a *false PASS* is the risk (Brian already knows the
prompt exists). Discoverability conclusions from run 1 are only trustworthy from a cold tester.

**Unlocks.** Card 2 text unchanged; adds one line to the findings template: record *which
building* the prompt was first noticed at.

---

## Q4 — Golden-image diffing: invest now or defer?

**Decision at stake.** Automate TOWN_SHOT image comparison (perceptual-hash tolerance) now, or
keep screenshots human-glanced until the art stabilizes post-PR2?

**What the research says.**
- Golden-image testing pays off only when the rendering environment is pinned and the imagery is
  stable. Consumer GPUs are not pixel-deterministic — practitioners uniformly require fuzzy
  tolerances, and cross-GPU/driver output differs by small RGB deltas per pixel ([Aras
  Pranckevičius — testing graphics
  code](https://aras-p.info/blog/2011/06/17/testing-graphics-code-4-years-later/),
  [Playwright visual comparisons](https://playwright.dev/docs/test-snapshots)). Chromium's
  answer is a whole triage service (Gold) with *multiple approved baselines per test* —
  industrial-scale machinery a solo project cannot amortize
  ([Chromium GPU pixel testing with
  Gold](https://chromium.googlesource.com/chromium/src/+/HEAD/docs/gpu/gpu_pixel_testing_with_gold.md)).
- The dominant cost of goldens is **churn**: every intentional visual change invalidates
  baselines. This slice is the *start* of the town's visual life — interiors-in-3D, phase-tinted
  lighting, ambient FX, and venue theming are all approved-and-pending. Baselines cut now would
  thrash on every PR2+ wave, training everyone to rubber-stamp diffs — the failure mode that makes
  golden suites worse than nothing.
- Project-specific hard constraint: CI can never render the 3D town (headless render-hang rule),
  so goldens could only ever run on Brian's dev box — one machine, one GPU. That kills the main
  benefit (regression catching on someone else's change in CI) while keeping all the maintenance.

**Recommendation.** **Defer, with a named trigger.** Keep the TOWN_SHOT pack human/agent-glanced
against the §3.2 checklist and **archive each pack with its findings doc** (the redesign already
requires this) — archived packs *are* the baseline history, at zero tooling cost. Revisit
golden-image automation when both are true: (1) the deferred visual waves (interiors, lighting,
FX) have landed and the town's look is declared stable, and (2) a visual regression has actually
slipped past a human glance at least once. If adopted then: perceptual-hash with tolerance, dev-box
only, small curated shot list (default pose + one interior), never CI.

**Unlocks.** Nothing to build now; adds one sentence to §4.3 naming the revisit trigger.

---

## Q5 — Second tester: is n-of-1 acceptable?

**Decision at stake.** Gate B is one tester (Brian), and after run 1 he is never cold again.
Recruit one fresh human for discoverability-sensitive cards on major reruns?

**What the research says.**
- Nielsen's small-n research is the canonical grounding: qualitative usability testing with very
  few users finds the large majority of problems, with steep diminishing returns after ~5; the
  recommended spend is *more small iterated tests, not bigger tests*
  ([NN/g — how many test users](https://www.nngroup.com/articles/how-many-test-users/),
  [NN/g — 5 users: qual vs quant](https://www.nngroup.com/articles/5-test-users-qual-quant/)).
  Read for this project: n-of-1 is *defensible* for feel, comfort, performance, and spatial reads —
  one attentive tester surfaces most severe issues, and the gate iterates.
- The rule's published caveats are exactly where n-of-1 breaks here: it assumes each test user is
  *representative and naive for the task* ([Faulkner/critique lit — why five users aren't always
  enough](https://www.researchgate.net/publication/200553185_Why_and_when_five_test_users_aren't_enough)).
  Discoverability (Card 2) and first-read spatial comprehension (Card 4) are one-shot cognition
  measurements — they are *destroyed by prior exposure*, not merely weakened. Brian designed the
  layout and knows the prompt exists; his rerun data on those two cards is structurally invalid,
  regardless of how honest he is.

**Recommendation.** **Yes — one fresh tester, narrowly scoped.** Concretely:
- Run 1: Brian alone is fine (he is cold *enough* for a world he hasn't walked, and he's the
  acceptance authority).
- Any **major rerun** (post-fix re-gate, or after PR2+ visual waves): hand one fresh person a
  15-minute scripted protocol covering **Cards 2 and 4 only** — "find and enter the Forge, then
  the notice board, then the mine gate; then point at where heroes gather / leave / are
  remembered" — with a screen recording. No install burden beyond a zip; no rubric training
  needed, the recording is the data.
- Authority is unchanged: Brian scores the rubric and owns the verdict; the fresh recording is an
  *instrument* for the two axes his warmth invalidates. One person, once per major rerun, is the
  entire cost — cheap insurance against designing for the one player who already knows where the
  Forge is.

**Unlocks.** A half-page tester script appended to the redesign doc (write it when the first
rerun is scheduled, not before).

---

## Q6 — Gamepad/input scope for rev.2

**Decision at stake.** WASD + mouse only for rev.2, or sanity-check camera/interaction decisions
against controller support now?

**What the research says.**
- The established PC-dev guidance is: build a **remappable action abstraction** early, then add
  device mappings when the audience justifies them — the abstraction is the expensive part to
  retrofit, the mappings are cheap ([GameDev.net — what inputs to support for a PC
  game](https://gamedev.net/forums/topic/688465-what-inputs-to-support-for-pc-game-keyboard-controller-or-both/));
  Steam Input further backstops PC controller users by remapping onto whatever actions a title
  exposes ([PCGamingWiki — controller
  glossary](https://www.pcgamingwiki.com/wiki/Glossary:Controller)).
- This project already has the expensive part **by construction**: all input flows through
  Godot's InputMap actions registered in `TownInput.RegisterActions()` (`move_*`, `interact`,
  `cancel`). Adding a controller later is adding `JoypadMotion`/`JoypadButton` events to existing
  actions — a mapping change, not a redesign.
- Interaction-model audit for gamepad-friendliness, done now on paper: **proximity highlight +
  `interact` is already the gamepad-native pattern** (it exists industry-wide precisely because
  cursor-pointing is miserable on a stick). The only mouse-dependent features are click-to-move
  (redundant with WASD — a controller player simply walks) and direct hero-clicking (heroes are
  also reachable via the Heroes panel). Nothing in the current design paints us into a corner.

**Recommendation.** **WASD + mouse only for rev.2 — no gamepad card, no gamepad testing.** The
one thing to do now costs zero code: adopt the standing rule that *no future interaction may be
mouse-only unless it has a proximity/action-key equivalent* (today's design already complies).
Log controller mappings as a P2 backlog item for whenever a public build is contemplated. A solo
dev testing his own game on his own PC gains nothing from controller QA today.

**Unlocks.** One sentence in the redesign doc recording the "no mouse-only interactions" rule so
PR2+ agents inherit it.

---

## Q7 — Scope ledger sign-off (observed-not-gated deferrals)

**Decision at stake.** Confirm that interiors-in-3D, gate walk-out spectating, and ambient FX are
formally *observed-not-gated* this rev, so a PASS is honest about what it covers.

**What the research says.** This is standard test-scoping hygiene rather than a research
question: an acceptance verdict is only meaningful relative to an explicit in/out-of-scope
declaration, and silent scope shrinkage is how "PASS" quietly stops meaning anything (the same
logic behind known-limitations registers in any QA regime). The redesign already implements the
right mechanics — deferred pillars are recorded, observations about them are captured but cannot
fail the gate, and task cards get appended when PR2+ lands each pillar (§3.5). Two details make
the ledger fully honest:
1. **Deferral provenance:** each ledger row should cite *where* the deferral was approved (the
   3D-town spec's PR2+ split), so a future reader can distinguish "deferred by decision" from
   "forgotten."
2. **Observation escalation rule:** an observation logged against a deferred pillar can still be
   promoted to a real P0/P1 *if it breaks an in-scope axis* (e.g. the InteriorStage 2D seam
   glitching is in-scope via Card 3 even though interiors-in-3D are not). State this explicitly so
   the ledger can't be used to wave off a real defect.

**Recommendation.** **Sign off as drafted**, adding the two details above to §3.5. This is the
lowest-stakes question of the seven — it changes no behavior, only makes the PASS label precise.

**Unlocks.** Nothing; unblocks the findings-doc template.

---

## Decision table

| # | Question | Recommended answer | Owner confirm needed |
|---|---|---|---|
| Q1 | Comfort policy | Keep auto-P0; **no** user-facing camera options now (tune via existing `[Export]`s); P2 backlog item: comfort options before any public build | **Y** (policy) |
| Q2 | FPS floor / overlay / machine | 60 sustained, ≥45 dips, <45 sustained = P0; measured on Brian's play machine at native res; **use built-in F3 debug overlay (Godot ≥4.4) — build nothing** | **Y** (must name the machine) |
| Q3 | Discoverability bar | Keep it: prompt noticed unprompted by 2nd building = PASS; click-only = PARTIAL; never-noticed = P1; record which building first triggered notice | N (default stands unless overridden) |
| Q4 | Golden-image diffing | **Defer.** Archive TOWN_SHOT packs per findings doc as the baseline history; revisit only after PR2+ art stabilizes *and* a regression has slipped a human glance | N |
| Q5 | Second tester | Run 1: Brian alone. Major reruns: one fresh human, scripted 15-min protocol, **Cards 2+4 only**, screen recording; Brian keeps verdict authority | **Y** (needs a warm body) |
| Q6 | Gamepad scope | WASD + mouse only for rev.2; adopt "no mouse-only interactions without an action-key equivalent" rule; controller mappings = P2 backlog (InputMap makes it cheap later) | N |
| Q7 | Scope ledger | Sign off as drafted + add deferral provenance and the observation-escalation rule to §3.5 | **Y** (it's literally a sign-off) |

*Sources are linked inline per question. All recommendations respect the headless render-hang
constraint, the deny-list (no `project.godot` edits — the F3 overlay needs none), and KTD2 (every
recommendation is presentation-/process-side only).*

---

## DECISIONS — confirmed by Brian 2026-07-21

| # | Decision | Notes |
|---|----------|-------|
| Q1 | **Accept** — motion-sickness = auto-P0; no comfort-options UI this slice; camera tuned via `CameraRig` `[Export]`s; options-UI = P2 before any public build | |
| Q2 | **Accept** — 60 target / 45 floor / sustained <45 = P0 on the dev box (RTX 5080, Ryzen 9 7950X, 64 GB); use Godot 4.4+ built-in F3 overlay, build nothing, no `project.godot` edit | |
| Q3 | **Settled** (no objection) — strict discoverability bar: 2nd-building notice = PASS, click-only = PARTIAL, never-noticed = P1 | |
| Q4 | **Settled** (no objection) — defer golden-image tests; archive TOWN_SHOT packs as baseline; named revisit trigger | |
| Q5 | **Accept** — one fresh, never-exposed human runs the cold-cognition cards (navigation legibility, discoverability) on major reruns, 15-min scripted recording; Brian keeps verdict authority | **Open:** identify the tester |
| Q6 | **Settled** (no objection) — defer gamepad; WASD+mouse only + "no mouse-only interactions" rule; gamepad = P2 | |
| Q7 | **Sign off** — accept the Gate-B survives/dies/net-new ledger as definition of done, with deferral provenance + observation-escalation rule | |
