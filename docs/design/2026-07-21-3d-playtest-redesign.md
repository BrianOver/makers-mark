# Gate B redesign — visual/embodiment playtest for the true-3D town hub

**Date:** 2026-07-21
**Status:** proposal for Brian's sign-off
**Supersedes:** the gate (b) scope described in `docs/plans/2026-07-19-002-feat-world-rework-plan.md`
(Success Criteria, two-gate acceptance) *for the visual pillars only*. Gate (a) — the GameSim.Cli
naive-LLM-persona comprehension probe — is **unchanged and already PASSED**
(`playtest-findings-2026-07-20-gate-a-rerun.md`); this doc does not touch it.
**Applies to:** the 3D town hub slice (`docs/superpowers/specs/2026-07-20-3d-town-hub-design.md`,
plan `docs/superpowers/plans/2026-07-20-3d-town-hub.md`), implemented in `godot/scripts/town3d/`.

---

## 1. Why Gate B must be redesigned, not re-run

The old Gate B was written for a 2D town: Y-sorted painted sprites, a click-a-sprite interaction
model, a flat camera that could not be wrong, and zero navigation (the whole town was one screen).
Its implicit rubric — "does the collage look coherent, is everything clickable, do interiors stage" —
mostly evaluates things the 3D slice deleted or made trivial, and evaluates **nothing** the 3D slice
made risky. A true-3D world introduces failure classes a 2D playtest has no vocabulary for:

- **Spatial navigation** — the player can now be lost, stuck on a collider, or off the navmesh.
- **Camera legibility** — a fixed ~50° pitch rig can hide doors behind roofs, clip on follow, or
  make the far edge of town unreadable.
- **Depth/grounding reads** — "nothing floats" was the pivot's founding directive; in 3D it becomes
  shadow contact, footprint scale, and horizon coherence, none of which a 2D check ever measured.
- **Proximity discoverability** — interaction moved from "click the sprite you can see" to "walk
  near, watch for highlight + `E · Forge` prompt." That is a *learned* affordance and can fail silently.
- **Comfort** — camera lerp (`1-exp(-5·dt)`), FOV 45, follow smoothing: motion sickness and
  swim/judder are now possible defects. 2D had no such axis.
- **Performance** — a full Node3D world inside a `SubViewport` under a 2D HUD is a real frame-budget
  question the 2D collage never posed.
- **Label3D readability** — hero/building name tags are now world-space text at a raked camera
  angle, not HUD labels.

Re-running the old checklist would green-light a build that could still be nauseating, unreadable,
and 20 fps. Hence: redesign.

## 2. Survives / dies / net-new

### Survives (carried forward verbatim)

| Element | Why it survives |
|---|---|
| **Brian as the named acceptance authority** for visual/embodiment | Unchanged from the world-rework plan. Gate B is irreducibly a human gate at its core (see §4). |
| **P0/P1/P2 severity convention** | Shared vocabulary with all three findings docs; keep it (3D anchor examples in §3.4). |
| **Findings land as `docs/design/playtest-findings-<date>.md`** with repro + screenshots | The pipeline into the "playtest-findings fix session" bootstrap works; don't break it. |
| **Finding-by-finding diff against the prior findings doc** | The 2026-07-19 gate-b screenshots *caused* this pivot; the redesigned run must explicitly disposition each visual finding (floating sprites, placeholder rect player, stretched interior, menu sizing). |
| **Seed 2026** as the shared reference world | Determinism gives Gate B the same repro guarantee Gate A enjoys — every finding quotes seed + steps. |
| **Menu-sizing checks** (objective tracker width, timeline separation, HUD clip) | Still 2D HUD concerns, now also test-pinned (`MenuSizingTests.cs`) — Gate B just eyeballs that the pins match reality on a real window. |
| **Drawer/panel routing checks** ("interacting opens the right panel") | The panel system was reused unchanged; the *trigger* changed, the *destination* didn't. |

### Dies (delete from the protocol)

| Element | Why it dies |
|---|---|
| Y-sort correctness checks (walk in front of / behind buildings by sort order) | Y-sort is gone; real 3D depth replaces it. The *question* survives as "occlusion reads correctly" but the 2D mechanism-specific checks are meaningless. |
| Feet-anchoring / floating-sprite checks against painted PNG baselines | No painted town sprites exist anymore. Grounding is re-asked in 3D terms (§3.3 axis 1). |
| Click-a-sprite hit-target checks (Area2D picking, sprite hitboxes) | Replaced by proximity + camera-ray; the old `Area2dPickingSpikeTests` era is deleted code. |
| Painted-interior staging fidelity ("stretched painting") checks | Custom-painted art is paused by directive. Interiors keep the 2D `InteriorStage` overlay this slice — check the *seam*, not the paintings (§3.3 axis 3). |
| Duplicate-representation sweep (wireframe town over painted town) | The stacked-towns defect class died with the 2D scene graph in T8's atomic cutover. |
| Live-spectating pillars (gate walk-out cinematics, PiP over town) as *gating* checks | Explicitly deferred to PR2+ by the approved spec. Carried as **out-of-scope pending**, not silently dropped (§3.5). |

### Net-new for 3D

1. **Navigation legibility** — can a first-time player get anywhere on purpose; do WASD and
   click-to-move cooperate; can you get stuck.
2. **Embodiment/movement feel** — acceleration, turn rate, walk-then-open pacing (KTD12), avatar
   facing, ground contact.
3. **Camera legibility + comfort** — does the fixed-angle rig ever hide the thing you need; does
   follow smoothing induce swim; motion-sickness screen.
4. **Depth/grounding reads** — buildings sit on the ground, characters cast/receive plausible
   contact shadows, scale is coherent, nothing z-fights.
5. **Proximity-interaction discoverability** — is highlight + `E · <label>` learnable in the first
   minute without being told; single-nearest-target rule feels right; click path (walk-then-open)
   and E path both discoverable.
6. **Label3D readability** — hero names and building labels legible at the fixed pitch, at both
   near and far camera range, over bright and dark backgrounds.
7. **Performance under the SubViewport** — frame rate with full hero roster wandering + HUD +
   drawer open, on Brian's actual machine.
8. **The 3D↔2D seam** — drawer/interior opens over the 3D world: world input correctly gated off
   (`SetWorldInputEnabled(false)`), no click-through, avatar restored to the door on interior exit,
   no visible viewport stretch artifacts on resize.
9. **The bridge question** — does the 3D layer *help or hurt* the comprehension Gate A measures?
   (Handled as a rubric axis, §3.3 axis 4 — not by re-running Gate A personas.)

## 3. The redesigned Gate-B protocol

### 3.1 Instrument

Gate B rev.2 is **one human tester (Brian), one rubric-scored session of ~45–60 minutes**, on the
real target machine with a real GPU, preceded by an automated pre-flight (§3.2) that must be green
before the human session starts. Wasting the human gate on defects a machine can catch is the main
process failure this redesign eliminates.

Structure the human session as **five task cards** (below), played *cold* — Brian should not
re-read the spec beforehand; the point is whether the world explains itself. Think-aloud notes or a
short screen recording (any capture tool; not committed) turn impressions into findings.

**Setup:** fresh build from the candidate branch; `dotnet build Game.sln`; launch
`"$GODOT_BIN" --path godot` (4.6.3-stable .NET only — engine pin); new game, seed 2026, default
window, then once maximized/full-screen. Enable the FPS counter for card 5
(no in-game counter exists yet — see Open Question Q2).

### 3.2 Automated pre-flight (blocking; no human time until green)

1. **Engine suite green:** `dotnet test godot/tests --settings .runsettings` — includes all
   `*3D*` suites + `MenuSizingTests` (current inventory in §4.1).
2. **Fast lane green** (no sim impact expected, but it's the repo's floor).
3. **TOWN_SHOT capture pack:** launch with `TOWN_SHOT=<path>` (the env-gated dev tool in
   `MainUi.MaybeScreenshotAndQuit`) to get a real-GPU render of town + HUD, at two window sizes.
   An agent (or Brian at a glance) checks the pack against the **screenshot checklist**:
   - no building or character floating above / sunk into the ground plane;
   - no primitive-fallback box where a Kenney mesh was expected (fallback is legal but must be flagged);
   - all Label3Ds render (non-empty, not clipped by geometry at the default camera pose);
   - OBJECTIVE tracker at full width, timeline separated, no HUD element off-screen;
   - memorial plot visible and stone count matches the seeded world's dead-hero count.
   Any checklist miss is triaged *before* the session; a P0-class miss cancels it.

### 3.3 Task cards + rubric

Score each axis **PASS / PARTIAL / FAIL** — deliberately the same trichotomy as Gate A's
UNDERSTOOD/PARTIAL/FAILED, so the two gates read side-by-side in one acceptance table.

**Card 1 — First 90 seconds (Embodiment).** New game, no instructions. Walk with WASD; walk with
click; mix them mid-path.
- *PASS:* control feels immediate and predictable; avatar faces travel direction; WASD cleanly
  cancels a click-move; walk-then-open never teleports; avatar never leaves the ground plane.
- *FAIL anchors:* input feels laggy/floaty; avatar slides without animation intent; click-move
  fights WASD; avatar clips through a wall or pops above ground.

**Card 2 — "Go to the Forge, craft something" (Navigation legibility + interaction
discoverability).** From spawn, find and enter the Forge with zero prompting, then repeat for the
notice board (Bounties) and the mine gate.
- *PASS:* every target found in under ~30 s each; the highlight + `E · <label>` affordance is
  noticed and used *unprompted* by the second building; both E and click-to-open get used
  naturally; player never gets stuck on a collider or walks off a navigable dead end.
- *PARTIAL:* found everything but only ever by clicking (proximity prompt never noticed), or one
  target needed wandering >60 s.
- *FAIL anchors:* any building unreachable/unfindable; prompt never noticed at all; stuck states
  requiring a restart; camera hides a door with no recovery.

**Card 3 — The seam (drawer/interior over 3D).** Open Forge panel → craft → close; enter an
interior → exit; resize the window with a drawer open; try to click the world *through* an open
drawer.
- *PASS:* world input is dead while any drawer/interior/modal is open; exit returns the avatar to
  the door anchor; no stretch/scaling artifacts in the SubViewport on resize; menus stay sized
  (the three 2026-07-20 menu defects stay fixed at both window sizes).
- *FAIL anchors:* click-through moves the avatar behind a drawer; interior exit strands the avatar;
  HUD or viewport visibly distorts on resize.

**Card 4 — Read the town (Spatial comprehension — the Gate-A bridge).** Without opening any panel:
point at where heroes gather, where they will leave for the Mine, where dead heroes are remembered,
and name what each building is for. Then advance a day and watch the town act out a phase change.
- *PASS:* town layout alone communicates the loop's geography (forge → shelf/market → board →
  gate → memorial); hero name tags are readable at the default camera without approaching; phase
  choreography (rally/walk-out/return) is *noticed* and read correctly as "they're leaving for the
  mine."
- *PARTIAL:* geography reads but name tags need walking up to; choreography seen but misread.
- *FAIL anchors:* the town reads as decoration — player still thinks of the game as menus; labels
  illegible at the fixed pitch; the 3D layer actively confuses something the CLI made clear.
  **A FAIL here is the "3D hurt comprehension" alarm** and triggers a design conversation, not
  just a defect fix.

**Card 5 — Comfort + performance soak (Performance-comfort).** 10 continuous minutes of normal
play: walk everywhere, keep the clock running, open/close drawers, let a full expedition cycle run
with the maximum alive-hero roster wandering.
- *PASS:* sustained ≥ 60 fps on the target machine (Open Question Q2 sets the machine + floor);
  no hitching on drawer open, phase tick, or hero reconcile; **zero motion-sickness or eye-strain
  report** from the camera follow/lerp; camera never clips into geometry.
- *PARTIAL:* isolated hitches (< 3 per 10 min) or fps dips ≥ 45; mild "swimmy" camera comment.
- *FAIL anchors:* sustained < 45 fps; any real nausea/discomfort report (auto-P0 — comfort is
  never negotiable); repeatable hitch tied to a specific event.

### 3.4 Severity scale (consistent with existing convention, 3D anchors)

- **P0** — embodiment loop unreachable or misleading, or physically unacceptable: player can get
  irrecoverably stuck; any building unreachable/unopenable; click-through breaks the input gate;
  motion sickness/discomfort reported; sustained sub-45-fps on target hardware; interior exit
  strands the avatar.
- **P1** — real defect hurting play: prompt/highlight unnoticeable so discoverability collapses to
  click-only; Label3D illegible at default camera; camera hides a door with awkward recovery;
  visible floating/sinking geometry; hitching tied to a common event; a menu-sizing regression.
- **P2** — polish/design: fallback primitives where meshes should be; shadow/scale incoherence
  that doesn't block reads; camera feel tuning; label styling; choreography readability tweaks.

### 3.5 Verdict + scope ledger

Verdict is per-axis (5 axes) + overall, same PASS / CONDITIONAL FAIL / FAIL shape as Gate A's
docs. **Gate B rev.2 passes when all five axes are PASS and no P0/P1 is open.** P2s never gate.

The findings doc must carry a **scope ledger**: interiors-in-3D, gate walk-out spectating, ambient
FX, phase-tinted lighting, and per-venue theming are *deferred by the approved spec* — the run
records observations about them but cannot fail the gate on them. When PR2+ lands those pillars,
they get task cards appended here; the gate grows with the surface.

## 4. Automated vs human — the honest split

Hard constraint first, because it bounds every claim below: **pumping frames while a 3D
`SubViewport` renders hangs the headless gdUnit runner** (learned in T3, encoded as the plan's
mandatory 3D-test rule). Every automated 3D test therefore either (a) asserts properties
synchronously after `Build()` with no frame pump, or (b) sets
`town.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled` immediately after mount
and pumps *physics* only. Consequence: **CI can never see a rendered pixel of the 3D town.** Any
check whose truth lives in the framebuffer — grounding *appearance*, label *legibility*, shadow
contact, fps, comfort — is out of headless reach by construction, and this doc refuses to claim it.

### 4.1 Already automated (exists in `godot/tests/` today — pre-flight rides on these)

- Viewport contract: `PhysicsObjectPicking` on, camera current (`Town3DSceneTests`).
- WASD moves the body; click walks-then-opens **never instant** (KTD12); ground-Y invariant; WASD
  cancels click-move; disabled world input ignores clicks (`PlayerController3DTests`).
- Proximity enter → active target + highlight + prompt text, clear on exit; interact raises the
  right `BuildingClicked` key; fallback-material highlight toggle (`Building3DInteractionTests`).
- Hero reconcile matches alive/dead; memorial stone added; pick raises `HeroClicked`; wander
  state machine deterministic; Evening never revives a dead actor (`HeroActor3DTests`).
- Camera rig push-in/release convergence; zero-delta NaN guard (`CameraRigTests`).
- Objective width + timeline separation (`MenuSizingTests`).

### 4.2 Automate next (cheap, render-free, would have caught real defect classes)

1. **Nav reachability sweep (highest value).** After the sync bake, for every `Building3D`:
   `NavigationServer3D.MapGetPath(map, spawn, DoorAnchorGlobal, true)` returns a non-empty path
   whose endpoint is within `TargetDesiredDistance` of the anchor. Property-only, no frame pump
   post-bake. Kills "unreachable building" P0s in CI forever, including after any future layout
   reshuffle.
2. **Analytic label-legibility floor.** No rendering needed: project each `Label3D` position
   through the camera's actual transform/FOV at the default follow pose
   (`Camera3D.UnprojectPosition` on two points one text-height apart) and assert the on-screen
   text height ≥ N px at reference resolution, and that `PixelSize`/billboard/outline settings
   match a pinned profile. This converts "readable at the fixed angle" from pure eyeball into
   eyeball-confirms-a-pinned-floor.
3. **Camera pose invariants.** Pitch/Distance/FOV pinned; assert the follow target offset never
   places the near plane inside the player capsule; assert `Far` covers the town's bounding box.
4. **Interaction-zone geometry sanity.** Every `DoorAnchor` lies inside its building's interact
   `Area3D` volume and *outside* its footprint collider; interact `Area3D` has
   `CollisionLayer=2, CollisionMask=4` (the fixed vocabulary); exactly one active target when two
   zones overlap (nearest-wins pinned with two adjacent buildings).
5. **Seam gating pin at the MainUi level.** With a drawer open, `WorldInputNode.Enabled == false`
   and a synthetic click does not move `Player` (physics-pump with render disabled); interior exit
   restores `Player.GlobalPosition` to `DoorAnchor(venue)`.
6. **Reconcile-scale smoke.** Build with a late-game state (max roster + many memorials), assert
   `HeroActorCount`/`MemorialStoneCount`, and — as a weak perf proxy only — a capped physics-pump
   completes; never claim this measures frame rate.

### 4.3 Semi-automated (real GPU, human-glanced — the TOWN_SHOT lane)

`TOWN_SHOT=<path>` renders ~90 real frames on the local GPU and saves viewport PNGs. This is the
**only** automated path to actual pixels. Use it for the §3.2 screenshot checklist (agent-checkable
in part: label presence, HUD bounds), and archive the pack with each findings doc as the visual
diff baseline for the *next* run. It runs on a dev box, never in CI. Full golden-image diffing is
deliberately **not** proposed yet (Open Question Q4) — GPU/driver nondeterminism makes it a
maintenance tax the slice hasn't earned.

### 4.4 Irreducibly human (the five task cards exist for these)

- Movement/camera **feel**: acceleration, turn smoothing, follow lerp comfort, motion sickness.
- Whether grounding/depth/scale *looks* right (contact shadows, silhouette, coherence).
- Whether the proximity affordance is *noticed and learned* unprompted — discoverability is a
  cognition claim; only a cold human proves it.
- Aesthetic legibility beyond the pinned floor (contrast against sky/roofs, visual noise).
- Real sustained frame rate + hitching on target hardware.
- The Card-4 bridge judgment: does the 3D town make the game *mean more*.

**Ratio check:** of the nine net-new 3D concerns in §2, six get meaningful automated pins
(§4.1–4.2), one gets a semi-automated lane (§4.3), and the human session concentrates on feel,
comfort, discoverability-as-cognition, and pixels — exactly what a machine cannot certify headless.

## 5. How Gate A and Gate B compose

The split survives intact — they measure orthogonal pillars and must never be merged:

| | **Gate A** (unchanged) | **Gate B rev.2** (this doc) |
|---|---|---|
| Instrument | 3 naive LLM personas, `GameSim.Cli`, seed 2026, blind | Brian, Godot client, seed 2026, cold task cards |
| Measures | Sim-side comprehension: professions, heroes, progression | Embodiment, navigation, discoverability, spatial reads, comfort/perf |
| Engine-agnostic? | Yes — text only; survives *any* render pivot (proved: 2D→3D cost it nothing) | No — bound to the Godot surface, versioned with it |
| Status | **PASS** (2026-07-20 rerun) | Pending — first rev.2 run gates the 3D slice |

Rules of composition:

1. **Sequenced, not parallel:** Gate A gates the sim/CLI surface and re-runs only when sim rules or
   CLI feedback change. The 3D pivot is presentation-only (KTD2: zero sim change), so the Gate-A
   PASS **carries** — do not burn persona runs on a render change.
2. **Gate B assumes Gate A.** Card 4 deliberately probes the *overlap*: the sim loop is proven
   comprehensible in text, so if Brian misreads the town spatially, the defect is in the 3D
   presentation by elimination. That is the clean experimental design the split buys us — keep it.
3. **Findings cross-reference, never cross-file:** a Gate-B finding that turns out to be a sim
   defect (e.g., a choreography beat missing because an event never fires) is re-filed against the
   sim and may trigger a Gate-A-relevant fix; the gates stay separate documents.
4. **U24 acceptance = Gate A PASS (done) + Gate B rev.2 PASS (pending).** The acceptance table in
   the findings doc shows both, per-axis, in the shared PASS/PARTIAL/FAIL vocabulary.

## 6. Open questions / decisions for Brian

- **Q1 — Comfort policy.** Proposal: any motion-sickness report is auto-P0 (§3.4). Also: should
  the slice expose camera settings (follow speed, distance) as user options *now*, or only if the
  first run reports discomfort? My lean: don't build options speculatively; let the run decide.
- **Q2 — Performance floor + target machine.** Proposed: sustained 60 fps, dips ≥ 45 tolerable,
  measured on your actual play machine at your actual resolution. Confirm the machine, and whether
  we should add a debug FPS overlay (env-gated like TOWN_SHOT) so Card 5 has a number instead of a
  feeling.
- **Q3 — Discoverability bar.** Card 2 currently PASSes only if the proximity prompt is noticed
  *unprompted* by the second building. Too strict? The click path alone technically works — but my
  position is the prompt IS the affordance the pivot was for; click-only should cap the axis at
  PARTIAL.
- **Q4 — Golden-image diffing.** Invest in automated TOWN_SHOT image comparison (perceptual-hash
  tolerance) now, or keep screenshots human-glanced until the town art stabilizes post-PR2? My
  lean: defer — the town's look will churn through interiors/lighting waves and goldens would
  thrash.
- **Q5 — Second tester.** Gate B has an n-of-1 discoverability problem: after run 1, you are never
  cold again. Recruit one fresh human (or accept a scripted screen-recording protocol you hand to
  a friend) for Card 2/Card 4 on major reruns? Cheap insurance against designing for the one
  player who already knows where the Forge is.
- **Q6 — Gamepad/input scope.** WASD + mouse only for rev.2, or is controller support near enough
  on the roadmap that camera/interaction decisions should be sanity-checked against it now?
- **Q7 — Scope ledger sign-off.** Confirm that interiors-in-3D, walk-out spectating, and ambient
  FX are formally *observed-not-gated* this rev (§3.5), so a PASS is honest about what it covers.

---

*Automated pre-flight inventory (§4.1) verified against `godot/tests/` in worktree
`3d-town-slice` at time of writing; §4.2 items are proposed, not yet built. All engine-test claims
respect the headless render-hang rule (plan §Global Constraints, memory: disable
`RenderTargetUpdateMode` before pumping physics; property-only otherwise).*
