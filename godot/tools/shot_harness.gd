# Visual-playtest capture harness (Track A, U1).
#
# Renders ONE game state to a PNG so an automated visual check (or a human/Claude)
# can SEE what the game actually draws — the gap property-only gdUnit tests can't
# cover (they never render a frame; a flat-2D interior passed them identically to
# a good one). Runs NON-headless on the GPU (windowed/minimized) — `--headless`
# uses the dummy driver and cannot produce a real frame (see
# docs/design/2026-07-24-visual-playtest-loop.md).
#
# Invoke (via tools/shoot.ps1, which adds a timeout+kill safety net):
#   SHOT_OUT=<abs png path>  SHOT_STATE=<""|Forge|Shop|Tavern|Gate|SendOff|...>  SHOT_QUIET=<""|1>
#   godot --path <godot dir> -s godot/tools/shot_harness.gd
# Empty SHOT_STATE captures the town; a venue key enters that interior through the
# production OnTownBuildingClicked path, then waits for the camera dolly to settle.
# U1 (painted-interiors plan): SHOT_STATE=Forge now walks the player INTO the walkable forge
# room (R1) — it drives the same production path, so this is automatic. SHOT_STATE=ForgePanel
# bypasses the room and opens the ForgePanel DRAWER directly by id, so a drawer-only receipt
# (the "before" half of the U1 before/after pair) stays reachable for comparison. SHOT_STATE=
# ForgeExit enters the room, then (frame 200) calls Town2D.ExitInterior() directly — the second
# required U1 receipt, proving the exit door returns the player outside.
# U3 (painted-interiors plan): ForgeShelf enters the room, then (frame 200) presses the Material
# Shelf station directly (Building2D.RaisePick — the same call a click/E-interact fires) — the
# receipt proving ForgePanel opens scrolled to its materials section. ForgeAnvil does the same for
# the Anvil station (scrolled to the recipe cards instead) — the comparison receipt proving the
# two land in visibly different places. ForgeFlavor does the same for the Bellows station — the
# receipt proving a flavor press shows a one-line toast and never opens a panel.
# Counter (BP-BUG-3) opens the Shop drawer, then presses the real "Open Counter" button --
# CounterPanel is nested inside ShopPanel and only exists while state.Counter is live, so it
# is NOT reachable as an OpenPanel id. Without this state the reported edge-clipping could
# not be photographed at all, which is how a fix for it shipped unverified.
# SendOff (U1, playtest-three plan) opens the Forge drawer, then presses the real
# AdvancePhase bell through its own signal — the receipt for "send them off with a drawer
# open" reachability fix, showing the resulting town view (drawer closed, camera on the
# gate, PiP dock docked).
# main_ui.tscn self-seeds a deterministic SimAdapter (seed 2026) on _Ready.
#
# U1 (world-and-interiors plan, docs/plans/2026-08-02-004): market/tavern/minegate grew rooms
# too, same as the forge -- so the same "PanelId + Panel" bypass-state idiom ForgePanel
# established is extended here for each: ShopPanel/TavernPanel/DepthsPanel open their drawer
# directly by id, bypassing the room, so the "before" half of each new venue's receipt pair
# stays reachable on the SAME build as the room "after" shot (SHOT_STATE=Shop/Tavern/Gate).
#
# SHOT_QUIET=1 (receipt.ps1's -Quiet): freezes AmbientLife2D -- chimney smoke, fireflies,
# lamp flicker, market awning sway, noticeboard paper flutter, mine dust -- as early as
# possible. That node accumulates real per-frame delta (not frame count) to drive its
# sine-wave decoration, and real per-frame delta is inherently jittery across separate
# process launches (OS/GPU scheduling), so two otherwise-identical captures drift apart by a
# small amount neither run's CODE caused. receipt.ps1's header quotes the measured floor
# with and without this flag. Scoped to AmbientLife2D specifically because it's reachable by
# name and self-contained (no sim/position coupling); tree sway and idle-character breathing
# are owned by other actors' code and NOT covered, so a residual floor can remain even with
# -Quiet.
#
# AmbientLife2D does not exist yet the instant `root.add_child(_ui)` returns -- MainUi builds
# the town (and wires AmbientLife2D) on a LATER frame, not synchronously during _Ready(), so
# a single find_child() call right after add_child() finds nothing (confirmed: it silently
# returned null when tried that way). _try_suppress_ambient_vfx() is instead polled once per
# frame from _process() until the node exists, then disables it on that same frame -- at most
# one frame of drift can accumulate before it's caught, versus the full ~90-320 frame settle
# window if suppression silently never engages at all.
#
# U2 (shell-and-audio plan, docs/plans/2026-08-02-005): SHOT_STATE=MineGateFocus calls
# Town2D.FocusOnMineGate() directly (rather than depending on a fresh seed-2026 campaign
# actually forming/sending a party on day 1, which it may not) -- the deterministic receipt for
# R1 ("the mine is off the screen at the top"), proving the gate is on screen once the header
# no longer occludes the world.
#
# U7 (world-and-interiors plan, KTD-3): SHOT_PROFESSION=<professionId> (read directly by
# MainUi.BuildDefaultAdapter in C#, not by this GDScript file) starts the self-seeded campaign
# with that profession instead of the default — e.g. SHOT_PROFESSION=alchemy plus
# SHOT_STATE=Forge captures an alchemist's workshop room instead of the blacksmith's. Unset
# (every normal launch) keeps the pre-U7 seed-only campaign byte-identical.
#
# U11 (world-and-interiors plan, "night is dark, dawn is dawn"): SHOT_STATE=Phase0..Phase4
# presses the REAL AdvancePhase bell N times (never an adapter/state injection seam) to land
# on phase N of the day's actual 5-phase cycle -- Morning/"Dawn"=0 -> Expedition/"Quest"=1 ->
# Camp/"Vigil"=2 -> ExpeditionDeep/"Deep Vigil"=3 -> Evening/"Night"=4 (GameKernel.Advance's
# own order -- Evening is the LAST phase before the next day's Morning, not a "dusk" stop).
# Settle is far longer than every other state (see _settle below) because DayPhaseTint EASES
# toward its target at 0.6/sec rather than snapping -- capturing right after the last press
# would show a mid-transition tint, not the phase's real one.
#
# U13 (world-and-interiors plan, "hero visuals, third round"): SHOT_STATE=HeroCandidateClosed/
# Mid/Open mounts one of the three motion-candidate striker leg poses
# (art/pipeline/gen-hero-candidates-r3.py) directly beside the player -- see
# _mount_hero_candidate below. Candidate-only: the textures load by raw res:// path, never
# through IconRegistry/AssetCatalog, so they cannot register a census row or otherwise enter the
# production resolution path. Used to render the "would a smoother stride read as more alive"
# receipt series for the owner's pick; no candidate here ships by default.
#
# P2-ONBOARD-05 (§11.15, "The Warrant ships"): SHOT_STATE=Primer and SHOT_STATE=WarrantFirstMorning
# are the only two states that do NOT mount main_ui.tscn -- the Warrant's pinned seed and its
# fiction name (NewGameSelect.WarrantFictionName) are decided entirely inside
# NewGameSelect.OnProfessionPicked, so proving either one on screen needs the REAL front door.
# Primer: New Game -> Pick_blacksmith, then captures the primer with the Warrant's name printed
# where a raw seed number used to be. WarrantFirstMorning: the same two presses, then Begin --
# the real GetTree().ChangeSceneToFile this fires is DEFERRED (the scene swap lands at idle time,
# not inside the press that requested it), so _ui is re-polled from root every frame past the
# press until the new MainUi is found, and the settle window is long enough for that swap plus
# MainUi's own boot sequence (which is what shows Bryn's cold-open beat) to land. Both force-clear
# user://tutorial_flow.json unconditionally at boot -- the SAME file SHOT_RESET_TUTORIAL=1 clears
# opt-in elsewhere in this script, except here it is not optional: a stale file from an earlier
# capture or a dev's own real play would make Ui.TutorialFlow.HasPriorProgress read true, and the
# receipt would silently show a raw seed instead of the name it exists to prove.

extends SceneTree

var _frames := 0
var _ui: Node = null
var _entered := false
var _out := ""
var _state := ""
var _quiet := false
var _ambient_suppressed := false
var _settle := 90

# Every state this harness actually implements a branch for, "" (plain town) and the Phase* family
# aside. Kept beside the branches it names, and asserted against SHOT_STATE at startup -- see
# _initialize.
const KNOWN_STATES := [
	"BellTray", "Bestiary", "BrynGreedyRule", "BrynRuleRevised", "Camp", "CommissionDilemma",
	"Chronicle", "Counter", "Demand",
	"DepthsPanel", "Docket",
	"ForgeAnvil", "ForgeAnvilEmpty", "ForgeExit", "ForgeFlavor", "ForgeLadder", "ForgePanel",
	"ForgeShelf", "ForgeTrinket", "GatedCounterEmptyShelf", "GateNight", "Graduation",
	"HeroCandidateOpen", "HeroCards",
	"HeroErrand", "HeroTrinket", "Ledger", "LedgerProvenance", "Lessons", "MemoryRow",
	"MineGateFocus", "Mirror",
	"OccupancyCorner", "Primer", "Provenance", "ReturnAtNight", "ReturnEmerge", "ReturnQuestEmpty",
	"SendOff", "ShopPanel", "ShopTrinket", "SplitLessons", "Storied", "StoriedCard",
	"StoriedRefusal", "SystemMenu", "TavernPanel",
	"TavernScene", "TavernSceneAtBar", "Telling",
	"TellingFall", "TellingFork", "TellingVerdict", "TownOverview", "TutorialLookIn",
	"TutorialOffCamera", "Watch", "WarrantFirstMorning",
]

# P2-SCREEN-09: the recipe id/talent node id sequence ForgeAnvilEmpty unlocks to drain the whole
# day's action budget WITHOUT spending a single copper — see the frame==260/300/340/380/420
# dispatch below for why this exact order (each node's one prerequisite, if any, is already
# unlocked by the time its own press fires).
const FORGE_ANVIL_EMPTY_UNLOCK_FRAMES := [260, 300, 340, 380, 420]
const FORGE_ANVIL_EMPTY_UNLOCK_IDS := [
	"keen-eye", "material-efficiency", "master-touch", "weapon-specialist", "material-mastery",
]

func _initialize() -> void:
	_out = OS.get_environment("SHOT_OUT")
	_state = OS.get_environment("SHOT_STATE")
	_quiet = OS.get_environment("SHOT_QUIET") == "1"
	if _out == "":
		push_error("shot_harness: SHOT_OUT not set")
		quit(1)
		return
	# U-T7-4: refuse an unrecognised state instead of photographing the plain town under its name.
	# Measured: `receipt.ps1 -State Docket` captured the town, wrote the file, and reported success --
	# so a brand new surface read as "looked at" while nobody had seen it. Phase* is a family
	# (Phase1..Phase5) and "" is the deliberate plain-town default, so both are allowed through.
	if _state != "" and not _state.begins_with("Phase") and not KNOWN_STATES.has(_state):
		push_error("shot_harness: SHOT_STATE='%s' is not a known state. Known: %s" % [_state, ", ".join(KNOWN_STATES)])
		quit(1)
		return
	# Entering an interior needs extra frames for the camera push-in ease to settle. Phase
	# captures need far more: DayPhaseTint's exponential ease converges at 0.6/sec, so a short
	# settle would show a mid-ease tint rather than the phase's real one (see the U11 note
	# above _initialize).
	if _state.begins_with("Phase"):
		_settle = 900
	elif _state == "GateNight":
		# U4 (world-and-interiors plan): DayPhaseTint's ease (see the U11 note above) needs the
		# same long convergence window Phase4 gets, PLUS room-entry settle time on top (the
		# camera push-in, entered later once the tint has actually landed -- see frame 920
		# below).
		_settle = 1200
	elif _state == "":
		_settle = 90
	elif _state == "ForgeAnvilEmpty":
		# P2-SCREEN-09: the anvil press lands at frame 200 (ForgeAnvil's own timing); the five
		# talent-unlock presses below run at 260/300/340/380/420, so 500 leaves ~80 frames (over
		# a second) past the last press for ForgePanel's own Refresh to settle before capture.
		_settle = 500
	elif _state == "HeroErrand":
		# U-T3-8 (register #150, "no hero/NPC walk animation"): the plain town default ("") only
		# settles 90 frames (1.5s) -- nowhere near enough for HeroActor2D's own id-seeded first-
		# errand stagger (2.0 + id*1.5s, i.e. 2-9.5s before a hero even LEAVES home) plus travel
		# time to a venue door. This state holds the plain town (no click/bell) for 900 frames
		# (15s) instead, so at least one of the six heroes is provably mid-errand -- away from its
		# home tile, walking a real path -- rather than standing frozen at Home, when the capture
		# is finally saved.
		_settle = 900
	elif _state == "TownOverview":
		# U-T3-3 (register #163, occupancy): a before/after receipt for "does the bigger 64x44 grid
		# read fuller or emptier" needs the WHOLE grid on screen at once, which the production 1x
		# camera zoom never shows (it only frames ~24x13.5 tiles around the player/follow target).
		# Held for 1800 frames (30s) -- long enough for several errand cooldown cycles (22s) to
		# fire across all ten wandering actors, so the capture isn't just catching everyone still
		# at home on a lucky/unlucky frame.
		_settle = 1800
	elif _state == "OccupancyCorner":
		# U-T3-3: the direct close-up receipt. Hero 5's own errand rotation seeds at
		# _errandRotation = heroId = 5, and the shared pool's index 5 (5 venue doors, then the
		# four TownsfolkHomeTiles in order) is TownsfolkHomeTiles[0] = tile (6,12) = world
		# (104,200) -- so hero 5's FIRST errand, with no rotation cycling needed, targets that
		# exact corner. Hero 5's own home (HeroHomeTiles[4] = tile (33,18) = world (536,296)) is
		# ~442.5px away; at ErrandWalkSpeed (110px/s) that is a ~4.0s walk. Hero 5's first-errand
		# cooldown is FirstErrandOffsetSeconds + 5*FirstErrandStaggerSeconds = 2.0 + 7.5 = 9.5s, so
		# the dwell window at the corner runs roughly t=13.5s to t=18.0s (ErrandDwellSeconds=4.5).
		# 950 frames (~15.8s at 60fps) sits comfortably inside that window with margin on both
		# sides for real per-frame jitter (this runs on the GPU, not headless -- see this file's
		# own SHOT_QUIET doc for why per-frame delta is never perfectly 1/60s here).
		_settle = 950
	elif _state == "LedgerProvenance":
		# P2-MEMORY-03/-17: the beat row (now carrying BOTH the channel clause and the presence
		# clause composed onto one line) sits past the fitted modal's own viewport height on a
		# fresh 1152x648 capture. LedgerScroll's real content height is not known until Godot's
		# own layout/text-shaping pass finishes settling (measured: the scrollbar's max is still 0
		# at frame 60, first non-zero at frame 90) -- so the scroll-down below waits for frame 100
		# (comfortably past that), and this settle leaves 50 more frames for it to take before
		# capture.
		_settle = 150
	elif _state == "BellTray":
		# U3 (loop-legibility plan, KTD-B): a plain HUD chip, no camera move -- but the
		# ack toast auto-clears after MainUi.RejectionToastSeconds (4s = 240 frames), so the
		# default 320-frame settle below would already show the tray chip WITHOUT the toast.
		# 90 (the plain-town default) lands comfortably inside the toast's window.
		_settle = 90
	elif _state == "Watch":
		# U-T5-9/10/11/12 (§11.14.7): MainUi.StageWatchFightReceipt (SHOT_WATCH_FIGHT, set by
		# shoot.ps1 for this state) already stages a resolved two-floor fight at DayPhase.Camp
		# before the scene even mounts -- no day-cycle navigation needed. The beat reveal itself is
		# forced instantly (MineWatch.RevealDelveBeatsForReceipt, called at frame 200 below) rather
		# than waited on -- see that method's own doc for why frame 200, not frame 0: the proof
		# flare is a real-time decay curve (2.6s) that keeps ticking regardless of pause state, so
		# revealing at mount time and capturing at a several-second settle would photograph it
		# already faded. 260 leaves 60 frames (~1s) of the flare/camera-lean still visibly live.
		_settle = 260
	elif _state == "MineGateFocus":
		# U2: armed at frame 60 below; the focus beat's own smoothing settles ~40 frames after
		# arming (measured), and the beat itself expires 3.2s (~192 frames) after arming, after
		# which the camera reverts to the player. 130 (70 frames past arming) lands comfortably
		# inside that window, past settle with margin, well short of expiry.
		_settle = 130
	elif _state == "ReturnQuestEmpty":
		# U10 (world-and-interiors plan, KTD-5): armed at frame 60 below (both bell presses land
		# in that one frame) -- this settle deliberately lands WELL INSIDE Town2D's
		# MinDelveShowSeconds (8s, ~480 real frames at 60fps) hold, after the party has had time to
		# actually march out to Away but long before the show floor could possibly have cleared.
		# The receipt this proves: while the HUD reads Quest/Vigil, the town is empty of party --
		# not a teleport-quick round trip.
		_settle = 240
	elif _state == "ReturnEmerge":
		# U10: the same double-bell-press as ReturnQuestEmpty, but settled far past
		# MinDelveShowSeconds (8s) plus march-out, landing WHILE the narrator toast
		# ("The party returns from...", MainUi.RejectionToastSeconds = 4s) is still on screen --
		# MainUi.OnPartyEmerging fires the toast at the exact instant the walk-in begins, so this
		# is calibrated (empirically, capturing early enough that the 4s window hasn't closed) to
		# land inside that beat, not after it has already faded.
		_settle = 900
	elif _state == "ReturnAtNight":
		# U10: four bell presses (not two) land the day at Evening/"Night" -- the survivor group
		# is already queued (and de-duplicated, Town2D._queuedReturnHeroIds) by the SECOND press,
		# so pressing on through Camp/ExpeditionDeep afterward does not re-queue it; it only
		# advances the PHASE (and therefore DayPhaseTint's target) while the SAME hold keeps
		# counting. This is the ORDINARY shape for any staged (Camp) return in real play --
		# ReturnSurvivors' second call site is OnPhaseCompleted(ExpeditionDeep), which always lands
		# on Evening -- so "does a returning party stay visible at Night" is not an edge case, it
		# is the common one. 950: DayPhaseTint's ease converging (Phase4/GateNight's own 900-1200
		# frame precedent) and the show floor clearing land in roughly the same window (both are
		# driven off the same real per-frame delta), so this also catches MainUi's narrator toast
		# (RejectionToastSeconds = 4s) still on screen, same as ReturnEmerge's own calibration.
		_settle = 950
	elif _state == "Storied":
		# M2b: the wall is opened synchronously at frame 0 (Dev_ShowLegendsWallLive) -- no camera
		# dolly, no scene change -- so the plain-town default settle is already generous.
		_settle = 90
	elif _state == "StoriedCard":
		# M2b: the storied row is pressed at frame 90 (see the dispatch below), once the wall has
		# settled; the card it opens is synchronous, so 150 leaves 60 frames of margin past it.
		_settle = 150
	elif _state == "StoriedRefusal":
		# M2b: the Shop drawer opens at frame 0, Open Counter is pressed at 90 (past its 0.22s
		# slide) and Present at 150 (past the counter session's own rebuild). 220 leaves 70 frames
		# past the refusal for the walk-away speech bubble to be laid out and drawn.
		_settle = 220
	elif _state == "Primer":
		# P2-ONBOARD-05: two synchronous button presses in one frame (see the _frames==60 dispatch
		# below) -- no camera dolly, no scene change -- so the plain-town default settle is already
		# generous.
		_settle = 90
	elif _state == "WarrantFirstMorning":
		# P2-ONBOARD-05: Begin's own GetTree().ChangeSceneToFile is DEFERRED (the scene swap lands
		# at idle time, not synchronously inside the press that requested it), so this needs real
		# settle time past the press for the swap AND MainUi's own boot sequence (which is what
		# shows Bryn's cold-open beat) to land -- longer than a camera ease, but nowhere near the
		# Phase*/GateNight family's multi-second tint convergence.
		_settle = 240
	else:
		_settle = 320
	# P2-ONBOARD-05 (§11.15): unconditional, not the opt-in SHOT_RESET_TUTORIAL=1 below -- these two
	# states exist specifically to prove Ui.TutorialFlow.HasPriorProgress reads false (a genuinely
	# fresh profile), so a stale user://tutorial_flow.json left by an earlier capture or a dev's own
	# real play on this machine would silently swap the receipt from "The Warrant" to a raw seed
	# number and prove nothing about the fiction name this unit ships.
	if _state == "Primer" or _state == "WarrantFirstMorning":
		var warrant_tutorial_save_path := "user://tutorial_flow.json"
		if FileAccess.file_exists(warrant_tutorial_save_path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(warrant_tutorial_save_path))
	# U5 (loop-legibility plan): SHOT_RESET_TUTORIAL=1 deletes the persisted
	# user://tutorial_flow.json BEFORE the scene mounts -- TutorialFlow.Load() reads whatever
	# that file says regardless of which fresh seed-2026 campaign SimAdapter just started, so a
	# machine that already has a Completed/Dismissed chain saved from an earlier session (this
	# file lives in the OS user-data folder, not the repo -- it survives across branches/
	# worktrees) would otherwise show the LIVE advisor instead of the tutorial for a receipt
	# that specifically wants to prove the tutorial's own overlay/checklist. Off by default --
	# zero effect on every other capture.
	if OS.get_environment("SHOT_RESET_TUTORIAL") == "1":
		var tutorial_save_path := "user://tutorial_flow.json"
		if FileAccess.file_exists(tutorial_save_path):
			DirAccess.remove_absolute(ProjectSettings.globalize_path(tutorial_save_path))
	# P2-ONBOARD-05: Primer/WarrantFirstMorning need the real front door -- see this file's own
	# class-doc note above for why main_ui.tscn cannot show either receipt.
	if _state == "Primer" or _state == "WarrantFirstMorning":
		_ui = load("res://scenes/new_game_select.tscn").instantiate()
	else:
		_ui = load("res://scenes/panels/main_ui.tscn").instantiate()
	root.add_child(_ui)
	if _quiet:
		_try_suppress_ambient_vfx() # in case a future refactor DOES build it synchronously

## U13 (world-and-interiors plan, KTD-7/U13): mounts one of the three motion-candidate striker
## frames (art/pipeline/gen-hero-candidates-r3.py) directly beside the player, at the SAME
## CharacterSpriteScale (0.5) every real actor uses, for an honest in-world scale comparison.
## Loaded by raw res:// path -- deliberately NOT through IconRegistry/AssetCatalog, so this
## candidate can never resolve through the production art path or register a census row (the
## PNGs live under res://assets/candidates/, a directory IconRegistry never looks at at all).
## This is receipt-only scaffolding: nothing production reads "HeroCandidate*" as a state, and
## no shipped script constructs a Sprite2D this way.
func _mount_hero_candidate(pose: String) -> void:
	var tex = load("res://assets/candidates/heroes-r3/striker-candidate-" + pose + ".png")
	if tex == null:
		push_error("shot_harness: no candidate texture for pose '%s'" % pose)
		return
	var wrapper := Node2D.new()
	wrapper.name = "HeroCandidateArt"
	wrapper.scale = Vector2(0.5, 0.5) # TownLayout2D.CharacterSpriteScale, mirrored here since
	# GDScript cannot call a C# static const directly -- kept in sync by the shared comment above.
	var sprite := Sprite2D.new()
	sprite.texture = tex
	sprite.centered = true
	sprite.offset = Vector2(0, -tex.get_height() / 2.0) # feet-at-origin, matching HeroActor2D's convention
	wrapper.add_child(sprite)
	var anchor_pos := Vector2(200, 200)
	var player = _ui.find_child("Player", true, false)
	if player:
		anchor_pos = player.position + Vector2(56, 0) # a few tiles clear of the player, never overlapping
	wrapper.position = anchor_pos
	var ysort = _ui.find_child("YSort", true, false)
	if ysort:
		ysort.add_child(wrapper)

func _try_suppress_ambient_vfx() -> void:
	# Node.PROCESS_MODE_DISABLED stops both AmbientLife2D's own _Process (the lamp/awning/
	# paper sine flicker) AND its CpuParticles2D children's internal simulation in one call --
	# no AmbientLife2D.cs edit needed, so that file's ownership is untouched.
	var ambient = _ui.find_child("AmbientLife2D", true, false)
	if ambient:
		ambient.process_mode = Node.PROCESS_MODE_DISABLED
		_ambient_suppressed = true

func _process(_delta: float) -> bool:
	_frames += 1
	if _quiet and not _ambient_suppressed:
		_try_suppress_ambient_vfx()
	# P2-ONBOARD-05: WarrantFirstMorning's Begin press (frame 60) fires a DEFERRED
	# GetTree().ChangeSceneToFile -- the swap lands at Godot's own idle time, not synchronously
	# inside that press -- so _ui (still the old NewGameSelect instance once the swap actually
	# happens) is re-polled from root every frame past the press until the new MainUi shows up.
	# Harmless once already swapped: find_child + an identity check is cheap, and re-running it
	# every frame costs nothing this script does not already spend elsewhere (Watch/SendOff/Mirror
	# above all poll similarly cheap node lookups on a schedule).
	#
	# ChangeSceneToFile normally frees the OLD current_scene for you -- but this harness never set
	# SceneTree.current_scene (it mounted NewGameSelect by hand, root.add_child, the same way every
	# other state here mounts main_ui.tscn), so ChangeSceneToFile has nothing registered to free and
	# just adds MainUi as a SIBLING, leaving the old NewGameSelect orphaned but still fully alive and
	# still drawing (measured: Begin/Back and the primer's own card bled through under the new
	# MainUi's HUD at the bottom edge). This frees it explicitly the moment the swap is detected.
	if _state == "WarrantFirstMorning" and _frames > 60:
		var warrant_swapped_ui = root.find_child("MainUi", true, false)
		if warrant_swapped_ui != null and warrant_swapped_ui != _ui:
			var warrant_stale_new_game_select = _ui
			_ui = warrant_swapped_ui
			if is_instance_valid(warrant_stale_new_game_select):
				warrant_stale_new_game_select.queue_free()
	if (_state == "TownOverview" or _state == "OccupancyCorner") and _frames == 65:
		# U-T3-3: Town2D.FollowPlayer() re-centers the camera on Player.GlobalPosition EVERY
		# real engine frame -- a direct or even a set_deferred Cam.GlobalPosition write from
		# THIS script was measured to still lose that race every single frame (this SceneTree's
		# own _process apparently runs before Town2D's, not after, contradicting an assumption
		# an older comment here made about _try_suppress_ambient_vfx's ordering). Moving the
		# PLAYER instead sidesteps the race entirely: FollowPlayer just keeps re-reading
		# wherever the player actually is, and nothing in this headless-input run ever moves
		# the player again once placed (no WASD is ever pumped here). One frame after the
		# other states' own _frames==60 dispatch, so it never collides with one.
		var player = _ui.find_child("Player", true, false)
		var cam = _ui.find_child("Cam", true, false)
		if player and cam:
			if _state == "TownOverview":
				# Fits the whole 64x44 grid (1024x704 world px) inside the 640x360 world
				# viewport with a small, even margin (measured empirically -- 0.3 left ~47%
				# grey letterbox per side).
				player.call("SpawnAt", Vector2(512, 352))
				cam.zoom = Vector2(0.5, 0.5)
			else:
				# Hero 5's own errand rotation seeds at _errandRotation = heroId = 5, and the
				# shared pool's index 5 (five venue doors, then the four TownsfolkHomeTiles in
				# order) is TownsfolkHomeTiles[0] = tile (6,12) = world (104, 200) -- hero 5's
				# FIRST errand, no rotation cycling needed, targets exactly this corner (see
				# the _settle branch above for the full timing derivation).
				player.call("SpawnAt", Vector2(104, 200))
				cam.zoom = Vector2(1.4, 1.4)
			cam.global_position = player.global_position
			cam.reset_smoothing()
	if _state != "" and not _entered and _frames == 60:
		if _state == "Bestiary":
			# The Bestiary modal opens from a tavern hotspot in-game; capture it directly by
			# finding the panel node and calling its public ShowAll (source-gen call() bridge).
			var b = _ui.find_child("BestiaryPanel", true, false)
			if b:
				b.call("ShowAll")
		elif _state == "Demand":
			# DemandPanel opens via the drawer (no in-world hotspot yet); reach it the
			# same way Bestiary does — the production OpenPanel path by id.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Demand")
		elif _state == "HeroCards":
			# Phase B Renown panel — drawer-hosted, opened by id.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "HeroCards")
		elif _state == "Lessons":
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Lessons")
		elif _state == "ForgePanel":
			# U1 (painted-interiors plan): "Forge" now walks the player INTO the room instead
			# of opening the drawer directly (R1). This state bypasses the room and opens the
			# ForgePanel drawer straight by id, so a drawer-only receipt stays possible for
			# comparison against the room (same idiom as Demand/HeroCards above).
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Forge")
		elif _state == "ForgeLadder":
			# Ladder-art receipt: ForgePanel lists recipes OrderBy(Tier).ThenBy(RecipeId), so the
			# six Tier 8-14 forward-ladder rows are always below the fold behind the Tier 1-3 ones
			# a fresh campaign opens on. Opening the drawer alone photographs a Buckler and proves
			# nothing about the ladder icons. The second beat (frame 120 below) scrolls the recipe
			# list to the end so those rows -- and their art -- are actually on screen.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Forge")
		elif _state == "ShopPanel":
			# U1 (world-and-interiors plan): "Shop" now walks the player INTO the market room
			# instead of opening the drawer directly (R1) -- same idiom as ForgePanel above,
			# so the drawer-only "before" shot stays reachable for the receipt pair.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Shop")
		elif _state == "Provenance":
			# P2-SCREEN-04: the nested-modal receipt -- a ProvenanceCard opened over a REAL
			# FullScreenModal host (Legends), proving the host's own screen ownership (clock held,
			# world input blocked, objective card + interact prompt hidden -- all already true the
			# instant Legends itself opens) survives the card opening on top of it. A fresh day-1
			# campaign has no Signed Work yet, so the dev bridge stamps one into a display-only
			# GameState copy and calls LegendsWall.ShowWall directly (zero sim mutation -- see the
			# bridge's own doc). Both ShowWall and the button press are synchronous, so no
			# settle-frame beat is needed the way Counter/SendOff/Mirror's drawer-slide idiom is.
			if _ui.has_method("Dev_ShowProvenanceCardOverLegends"):
				_ui.call("Dev_ShowProvenanceCardOverLegends")
				var legend_btn = _ui.find_child("Legend_*", true, false)
				if legend_btn:
					legend_btn.emit_signal("pressed")
				else:
					push_error("[shot] SHOT_STATE=Provenance could not find a Legend_* row button -- "
						+ "the shot below is a closed card and proves nothing about the nested-modal claim.")
			else:
				push_error("[shot] SHOT_STATE=Provenance could not reach Dev_ShowProvenanceCardOverLegends -- "
					+ "a capture of the plain town under this name would read as a look nobody took.")
				quit(1)
				return false
		elif _state == "Storied" or _state == "StoriedCard":
			# M2b: the storied-gear promotion, live. SHOT_STORIED=1 (see shoot.ps1) has already
			# planted the FACTS into the campaign -- a marked blade with four recorded deeds in
			# every hero's hands -- so nothing here is staged: the wall renders the real campaign
			# state, and whether any row appears at all is ShoppingAi's own threshold deciding.
			# Dev_ShowLegendsWallLive rather than the OpenLegends HUD button because the wall is a
			# gated surface a day-1 campaign has not unlocked; the wall itself is the production
			# ShowWall call either way. StoriedCard presses the storied row a beat later (the
			# _frames == 90 block below), once the wall has settled -- the same production
			# Button -> OnShowProvenance path a player clicking that row takes.
			if _ui.has_method("Dev_ShowLegendsWallLive"):
				_ui.call("Dev_ShowLegendsWallLive")
			else:
				push_error("[shot] SHOT_STATE=%s could not reach MainUi.Dev_ShowLegendsWallLive -- "
					% _state + "the shot below is the plain town and proves nothing about storied gear.")
				quit(1)
				return false
		elif _state == "StoriedRefusal":
			# M2b: the counter voicing the refusal. Opens the Shop drawer here; the real
			# "Open Counter" button and then the real "Present" button on the shelved Plain Blade
			# are pressed on later beats (the _frames == 90 / 150 blocks below), once each slide
			# has settled -- the same ordering (and the same reason) the Counter state uses.
			# Nothing about the outcome is staged: the customer the morning queue promotes is
			# wearing storied work, so ShoppingAi's own gate is what walks them away.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Shop")
		elif _state == "MemoryRow":
			# U32 (§11.14.14): the Memory act’s own row, live — a fresh day-1 campaign has no
			# legend item yet to render the wall’s "LEGENDARY GEAR" section against, so this
			# calls LegendsWall’s own shot-harness bridge (Dev_ShowWallWithMemoryRow) -- same
			# "stage a synthetic state, never mutate the live Adapter" idiom the Provenance
			# state above uses. Arms the row, opens the wall (which marks it Done inside
			# ShowWall’s own NotifyLegendsWallOpened call), and renders the live
			# "MemoryActRowNote" status line under the Legendary Gear section.
			var legends_memory = _ui.find_child("LegendsWall", true, false)
			if legends_memory and legends_memory.has_method("Dev_ShowWallWithMemoryRow"):
				legends_memory.call("Dev_ShowWallWithMemoryRow")
			else:
				push_error("[shot] SHOT_STATE=MemoryRow could not reach LegendsWall.Dev_ShowWallWithMemoryRow -- "
					+ "the shot below is the plain town and proves nothing about the Memory row.")
				quit(1)
				return false
		elif _state == "BrynGreedyRule":
			# P2-ONBOARD-07 (§11.15): beat 3, her rule, wrong on purpose -- the real production path
			# (MainUi.OnTownBuildingClicked, venue key "market"), the same one every other real
			# building-entry receipt in this file uses (Forge/Gate below). No dev bridge needed --
			# a fresh day-1 campaign has never walked to the Shop yet, so the first-touch fires for
			# real on this press.
			if _ui.has_method("OnTownBuildingClicked"):
				_ui.call("OnTownBuildingClicked", "Shop")
			else:
				push_error("[shot] SHOT_STATE=BrynGreedyRule could not reach OnTownBuildingClicked -- "
					+ "the shot below is the plain town and proves nothing about her rule.")
				quit(1)
				return false
		elif _state == "BrynRuleRevised":
			# P2-ONBOARD-07 (§11.15): "eating her rule" -- reaching this beat for real needs a
			# pinned counter close whose buyer's band has risen to Regular, or a fulfilled
			# commission, neither of which a fresh day-1 campaign has. MainUi.Dev_ShowRuleRevisedBeat
			# is the SAME "stage a synthetic state, never mutate the live Adapter" idiom
			# Dev_ShowProvenanceCardOverLegends/Dev_ShowWallWithMemoryRow above already use, and
			# returns whether the beat actually armed, so a silent no-op fails loud rather than
			# photographing an ordinary town.
			if _ui.has_method("Dev_ShowRuleRevisedBeat"):
				var rule_revised_armed: bool = _ui.call("Dev_ShowRuleRevisedBeat")
				if not rule_revised_armed:
					push_error("[shot] SHOT_STATE=BrynRuleRevised: Dev_ShowRuleRevisedBeat returned false -- "
						+ "the beat did not actually arm, and the shot below proves nothing.")
			else:
				push_error("[shot] SHOT_STATE=BrynRuleRevised could not reach MainUi.Dev_ShowRuleRevisedBeat -- "
					+ "the shot below is the plain town and proves nothing about the correction beat.")
				quit(1)
				return false
		elif _state == "Graduation":
			# U32 (§11.14.14): graduation becomes event-shaped -- the receipt for the course
			# ending on the FACT (the Memory row settling) rather than waiting on the day-8
			# backstop. Dev_GraduateViaMemoryRow arms the row, opens the wall (Done), then
			# re-advances so TutorialFlow.Advance’s own completion check actually fires --
			# returns whether TutorialFlow.Completed is now true, so a silent no-op fails loud
			# rather than photographing an ordinary town. The wall is closed again immediately
			# (real play never leaves it open) so the second beat’s bell press (frame 90 below)
			# lands on the live HUD, which is what actually shows the "quick-travel just
			# unlocked" lesson banner -- the same tick MainUi.ShowQuickTravelUnlockedLessonIfEarned
			# fires on in real play.
			var legends_graduate = _ui.find_child("LegendsWall", true, false)
			if legends_graduate and legends_graduate.has_method("Dev_GraduateViaMemoryRow"):
				var graduated: bool = legends_graduate.call("Dev_GraduateViaMemoryRow")
				if not graduated:
					push_error("[shot] SHOT_STATE=Graduation: Dev_GraduateViaMemoryRow returned false -- "
						+ "the course did not actually graduate, and the shot below proves nothing.")
				legends_graduate.call("Close")
			else:
				push_error("[shot] SHOT_STATE=Graduation could not reach LegendsWall.Dev_GraduateViaMemoryRow -- "
					+ "the shot below is the plain town and proves nothing about graduation.")
				quit(1)
				return false
		elif _state == "CommissionDilemma":
			# U27 (§11.14.14, dilemma #1): a fresh day-1 campaign has no open commission yet to
			# render the hold-or-sell fork against, so this calls CommissionBoard's own shot-
			# harness bridge (Dev_ShowSampleOpenCommission) -- same "stage a synthetic state,
			# never mutate the live Adapter" idiom the Provenance state above uses.
			var commissions = _ui.find_child("CommissionBoard", true, false)
			if commissions and commissions.has_method("Dev_ShowSampleOpenCommission"):
				commissions.call("Dev_ShowSampleOpenCommission")
			else:
				push_error("[shot] SHOT_STATE=CommissionDilemma could not reach CommissionBoard.Dev_ShowSampleOpenCommission -- "
					+ "the shot below is the plain town and proves nothing about the hold-or-sell fix.")
				quit(1)
				return false
		elif _state == "TavernPanel" or _state == "TavernScene" or _state == "TavernSceneAtBar":
			# P2-PEOPLE-01: TavernScene captures Act 1 -- the arc-scene row on Torvald's patron
			# card, beside the commission/ore rows it shares a mechanism with. TavernSceneAtBar
			# presses that row's own Pursue (frame 120 below) so the capture is the scene itself,
			# playing in the same section a handshake closes in. Both need SHOT_ARC_SCENE=1 on the
			# launching process (tools/shoot.ps1 sets it for these two states) -- that plants the
			# one FACT the scene requires, a player-marked piece in his hands, and the engine then
			# decides for itself whether to offer.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Tavern")
		elif _state == "DepthsPanel":
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Depths")
		elif _state == "ForgeTrinket":
			# P2-HONEST-11 visible half: open the Forge drawer here -- SHOT_PROFESSION=engineering
			# (set on the launching process, same contract as the "SHOT_PROFESSION=alchemy plus
			# SHOT_STATE=Forge" precedent in this file's own header) supplies the one starting
			# profession with a Trinket recipe (engineering-utility-multitool). The second beat
			# (frame 120 below) scrolls the recipe list down to that card specifically -- it sorts
			# (Tier, then RecipeId) well below this profession's Weapon/Shield/Armor Tier-1 cards,
			# same ensure_control_visible idiom as ForgeLadder above.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Forge")
		elif _state == "HeroTrinket":
			# P2-HONEST-11 visible half: open the Heroes drawer here; the second beat (frame 90
			# below) calls HeroesPanel's own dev bridge (Dev_ShowSampleTrinketGear) to equip a real
			# roster hero with a synthetic modifier-carrying Trinket, so the receipt proves the GEAR
			# row renders the modifier chip instead of the Atk/Def numbers CombatMath never reads
			# for that slot.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Heroes")
		elif _state == "ShopTrinket":
			# P2-HONEST-11 visible half: open the Shop drawer here; the second beat (frame 90
			# below) calls ShopPanel's own dev bridge (Dev_ShowSampleUnshelvedTrinket) to inject a
			# synthetic, unshelved, modifier-less Trinket, so the receipt proves the Unshelved
			# Crafts card renders the honesty-phrase fallback instead of a false Atk/Def number.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Shop")
		elif _state == "Camp":
			# Visual-check plan (2026-08-12): the winch-house slate (CampPanel, node name
			# "CampModal") has no in-world hotspot and no drawer id of its own -- MainUi
			# normally auto-opens it only when a party actually parks at DayPhase.Camp with a
			# non-empty InFlight. ShowModal is null-tolerant (renders "No party is camped below
			# the checkpoint." against a fresh seed-2026 day-1 mount, CampPanel.Render's own
			# empty-InFlight branch), so calling it directly here -- same call() bridge idiom as
			# Bestiary/Mirror above -- is enough to render the modal's own chrome (title, card
			# frame, close button) for a layout/theming check, without depending on RNG actually
			# parking a party this run.
			var camp = _ui.find_child("CampModal", true, false)
			if camp:
				camp.call("ShowModal")
		elif _state == "Chronicle":
			# P2-SCREEN-04: the campaign's ending ceremony fires exactly once per campaign,
			# automatically, off a real CampaignEnded event -- there is no player-pressable door to
			# it (TutorialSurfaceRegistry's own class doc), so this receipt uses the same dev-bridge
			# idiom Dev_QueueDay1BuyAndCraft already established: a MainUi method reachable through
			# this call() bridge, never called from real play. The receipt this state exists for:
			# before this unit the ending drew with the clock running, world input live, PiP
			# undimmed, and the objective card/interact prompt drawn over it -- OverlaySurfaces()
			# now derives Chronicle from the arbiter instead of a hand-written array missing it.
			if _ui.has_method("Dev_ShowChronicle"):
				_ui.call("Dev_ShowChronicle")
			else:
				push_error("[shot] SHOT_STATE=Chronicle could not reach Dev_ShowChronicle -- a "
					+ "capture of the plain town under this name would read as a look nobody took.")
				quit(1)
				return false
		elif _state == "ForgeExit":
			# U1 (painted-interiors plan): the second required receipt -- proves the exit
			# door returns the player OUTSIDE. Enters the room the normal way; the second
			# beat (frame 200 below) calls Town2D.ExitInterior() directly, mirroring how
			# SendOff/Mirror above drive their own second beat through a real signal/call.
			if _ui.has_method("OnTownBuildingClicked"):
				_ui.call("OnTownBuildingClicked", "Forge")
		elif _state == "ForgeShelf" or _state == "ForgeFlavor" or _state == "ForgeAnvil" or _state == "ForgeAnvilEmpty":
			# U3 (painted-interiors plan): enter the room the normal way; the second beat
			# (frame 200 below) presses the actual station -- shelf, bellows, or anvil.
			if _ui.has_method("OnTownBuildingClicked"):
				_ui.call("OnTownBuildingClicked", "Forge")
		elif _state.begins_with("Phase"):
			# U11: press the real bell (Button.Pressed -> MainUi's own handler -> AdvanceNow ->
			# SimAdapter.AdvancePhase, the exact player path -- never a state/adapter injection
			# seam) N times to walk the day's actual 5-phase cycle up to phase N. All N presses
			# land in this single frame; PhaseClock.AutoAdvance defaults OFF for a fresh
			# campaign, so nothing else advances the phase out from under this on the long
			# settle wait below.
			var presses = int(_state.substr(5))
			var bell = _ui.find_child("AdvancePhase", true, false)
			if bell:
				for _i in range(presses):
					bell.emit_signal("pressed")
		elif _state == "Docket":
			# U-T7-4 (register #149): the Companion Dock, expanded -- the screen the owner named as
			# the one he liked, and the host of the todo list. Opened through the dock's own public
			# Open() rather than a tray click, because the dock is not a drawer panel and has no
			# OpenPanel id; CompanionDockTests drives it the same way.
			# find_child, not _ui.get("Docket"): a C# public PROPERTY is not in the Godot property
			# list unless exported, so get() returns null here. The node's own name is stable
			# ("CompanionDock", set in its _Ready) and is what CompanionDockTests resolves too.
			var dock = _ui.find_child("CompanionDock", true, false)
			if dock and dock.has_method("Open"):
				dock.call("Open")
			else:
				push_error("[shot] SHOT_STATE=Docket could not reach the CompanionDock node -- a "
					+ "capture of the plain town under this name would read as a look nobody took.")
				quit(1)
				return false
		elif _state == "Counter":
			# BP-BUG-3: the counter panel was reported clipped on BOTH edges, and the clip fix
			# could never be photographed because no shot state reached an OPEN counter --
			# CounterPanel is NESTED inside ShopPanel and appears only when state.Counter is
			# live, so it is not an OpenPanel id the way Demand/HeroCards are. A fix nobody
			# can photograph is a fix nobody can check, and that is why this one never was.
			#
			# Open the Shop drawer here; the real "Open Counter" button is pressed a beat
			# later (the _frames == 90 block below) once the drawer's 0.22s slide-in has
			# settled -- the same ordering SendOff uses, for the same reason: pressing
			# mid-slide photographs a panel that is still moving.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Shop")
		elif _state == "SendOff":
			# U1 (playtest-three plan): the receipt for "clicked send them off with a drawer
			# open — where are the visuals?" Open the Forge drawer here; the AdvancePhase
			# bell itself is pressed a beat later (see the `_frames == 90` check below), once
			# the drawer's own 0.22s slide-in has settled, mirroring the real player order
			# (craft, THEN ring the bell) rather than pressing through mid-slide.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Forge")
		elif _state == "Ledger":
			# U7 (loop-legibility plan, R10): drive a whole day with zero player actions --
			# heroes muster and raid autonomously (A2), so day 1 produces a real return the
			# Ledger can show. Bell presses below (frames 90/120/150/180) walk the day's
			# current five phases (Morning/Expedition/Camp/ExpeditionDeep/Evening) the same
			# way SendOff/Mirror press the real bell button rather than faking a tick. The
			# reveal itself is real-time-gated (MainUi's Return Ritual, ReturnRitualDelaySeconds)
			# -- a frame-counted wait against a wall-clock gate is exactly the "frame count is
			# not a duration" trap, so frame 220 calls LedgerModal.ShowFor directly instead
			# (the same "call the panel's own public show method" idiom Mirror/Bestiary already
			# use above), once day 1 has fully rolled over.
			var bell_l = _ui.find_child("AdvancePhase", true, false)
			if bell_l:
				bell_l.emit_signal("pressed")
		elif _state == "LedgerProvenance":
			# P2-MEMORY-03/-17: the beat names its channel AND its presence -- the receipt for
			# the Evening Ledger's beat row composing both onto one line. Real combat RNG landing
			# a beat through a specific channel, and a real bounty acceptance moving a departure's
			# floor, are not guaranteed on a fresh seed, so this reaches the LedgerModal node
			# directly (its own dev bridge, not MainUi's -- LedgerModal is a scene root loaded by
			# MainUi, not a MainUi property Godot exposes to GDScript get(), same "find by stable
			# node name" idiom Docket/CompanionDock above already use) and calls its own
			# hand-built-state receipt method, mirroring Provenance's
			# Dev_ShowProvenanceCardOverLegends idiom.
			var ledger_modal = _ui.find_child("LedgerModal", true, false)
			if ledger_modal and ledger_modal.has_method("Dev_ShowLedgerWithProvenanceBeat"):
				ledger_modal.call("Dev_ShowLedgerWithProvenanceBeat")
			else:
				push_error("[shot] SHOT_STATE=LedgerProvenance could not reach "
					+ "LedgerModal.Dev_ShowLedgerWithProvenanceBeat -- the shot below is empty and "
					+ "proves nothing about the channel line.")
				quit(1)
				return false
		elif _state == "Telling" or _state == "TellingFork" or _state == "TellingFall" or _state == "TellingVerdict":
			# P2-PROOF: the Telling's own four-frame receipt (a factual round mid-play, the
			# desaturated fork, the held fall, the stamped verdict). Real combat RNG landing a
			# LethalSave beat with a multi-round fight is not guaranteed on a fresh seed, so this
			# reaches LedgerModal's own dev bridge (the same "find by stable node name, call its own
			# hand-built-state receipt method" idiom LedgerProvenance/Provenance already use) and
			# tells it which stage to land the panel on.
			var ledger_modal_t = _ui.find_child("LedgerModal", true, false)
			var telling_stage = "Factual"
			if _state == "TellingFork":
				telling_stage = "Fork"
			elif _state == "TellingFall":
				telling_stage = "Fall"
			elif _state == "TellingVerdict":
				telling_stage = "Verdict"
			if ledger_modal_t and ledger_modal_t.has_method("Dev_ShowTellingReceipt"):
				ledger_modal_t.call("Dev_ShowTellingReceipt", telling_stage)
			else:
				push_error("[shot] SHOT_STATE=%s could not reach LedgerModal.Dev_ShowTellingReceipt -- "
					% _state
					+ "the shot below is empty and proves nothing about the Telling.")
				quit(1)
				return false
		elif _state == "Mirror":
			# U1: the second required receipt -- the first proof any human has seen the
			# mirror render since it merged (#321). No drawer to open first; press the real
			# bell straight away so a party has actually departed by the time the Mirror
			# opens (see the frame==90 check below).
			var b2 = _ui.find_child("AdvancePhase", true, false)
			if b2:
				b2.emit_signal("pressed")
		elif _state == "BellTray":
			# U3 (loop-legibility plan, KTD-B): the receipt for the bell tray holding a
			# pending bell-rider chip + its withdraw control. No submit BUTTON exists yet
			# for UpgradeForge/CommissionLegendaryWork (their panels are other units' work);
			# SetProfessionsAction already has one real, wired production entry point --
			# MainUi.OnSecondProfessionPicked(professionId) (the U23 earn-2nd-profession
			# affordance) -- so this drives THAT directly, the same "call the real method,
			# skip the not-yet-reachable precondition" idiom MineGateFocus/Mirror use above.
			# A fresh seed-2026 campaign starts blacksmith-only, so "alchemy" is a genuine
			# second selection -- SimAdapter.Queue defers it (ActionTiming), which is exactly
			# the tray/ack-toast path this receipt exists to show.
			if _ui.has_method("OnSecondProfessionPicked"):
				_ui.call("OnSecondProfessionPicked", "alchemy")
		elif _state == "TutorialOffCamera":
			# The receipt for a pointer whose target the player CANNOT see. Buy + craft only, which
			# leaves Shelve current -- its anchor is the market BUILDING, 448px from the forge spawn
			# in a 640px viewport. The player is deliberately left standing at spawn: this photograph
			# is worthless if anything moves them or the camera toward the target, which is also
			# exactly the law-1 line an off-camera pointer must not cross.
			if _ui.has_method("Dev_QueueDay1BuyAndCraft"):
				_ui.call("Dev_QueueDay1BuyAndCraft")
		elif _state == "SplitLessons":
			# P2-SCREEN-07: the receipt for Bryn's three split lessons (slot-budget, station-press,
			# leaving-a-room) each landing in the Lessons book as SEPARATE cards rather than one
			# bolted-together paragraph. Buy+craft (Dev_QueueDay1BuyAndCraft) fires ItemCrafted --
			# TutorialFlow.Advance's own leaving-a-room hook -- then walking into the forge
			# (OnTownBuildingClicked, same production path a real click takes) fires
			# NotifyEnteredBuilding("forge"), which is slot-budget's and station-press's own hook.
			# All three ConsumeFirstTouch ids are fired by the time the second beat (frame 90 below)
			# opens the Lessons book.
			if _ui.has_method("Dev_QueueDay1BuyAndCraft"):
				_ui.call("Dev_QueueDay1BuyAndCraft")
			if _ui.has_method("OnTownBuildingClicked"):
				_ui.call("OnTownBuildingClicked", "Forge")
		elif _state == "GatedCounterEmptyShelf":
			# P2-SCREEN-08: the receipt for a gating note folded onto the card's ONE instruction line
			# rather than a second block. Buy+craft only (Dev_QueueDay1BuyAndCraft) -- deliberately
			# never stocks the shelf, so OpenCounter's own empty-shelf GatingNote case would stay
			# live once that step is current AND it is Morning again -- but reaching OpenCounter at
			# all requires the party to have already departed, which is necessarily past Morning
			# (the same TutorialRegistryConformanceTests
			# .AGatedStep_ShowsItsGatingNote_OutsideItsOwnWindow_NeverThePressNextAdvanceCopy sequence
			# below drives for real), and cycling the clock back around to the next Morning keeps
			# auto-opening a NEW party's own CampPanel/LedgerModal faster than it can be suppressed.
			# This state settles for the Morning-only phase-gate case instead (still a real,
			# non-trivial GatingNote fold, WaitText's own — see the frame-150 note below) rather than
			# chase the empty-shelf case through several more days of simulated auto-modals; that
			# exact case is proven instead by GatingFoldedIntoInstructionTests
			# .OpenCounterWithAnEmptyShelf_FoldsTheReason_OntoTheSameInstructionLine.
			if _ui.has_method("Dev_QueueDay1BuyAndCraft"):
				_ui.call("Dev_QueueDay1BuyAndCraft")
			var gatedBell = _ui.find_child("AdvancePhase", true, false)
			if gatedBell:
				gatedBell.emit_signal("pressed")
		elif _state == "TutorialLookIn":
			# U5 (loop-legibility plan): the receipt for LookIn's own HUD anchor
			# (WatchButton) -- queue the SAME day-1 ladder TutorialFlowTests.DriveDay1ToLookIn
			# drives for real in the engine suite (buy -> craft -> shelve -> post a bounty, all
			# four immediate per U1) via the dev-only call() bridge, then press the real bell
			# once; the SECOND press (frame 90 below) is what actually lands the party's own
			# departure, which is the fact that advances the chain to LookIn.
			if _ui.has_method("Dev_QueueDay1TutorialLadder"):
				_ui.call("Dev_QueueDay1TutorialLadder")
			var bell3 = _ui.find_child("AdvancePhase", true, false)
			if bell3:
				bell3.emit_signal("pressed")
		elif _state == "Watch":
			# §11.14.7: the fight is already staged at mount (see StageWatchFightReceipt).
			# MineWatch itself is mounted INSIDE the Depths drawer (Depths.MountWatch, MainUi's
			# own build-time wiring) -- not part of the always-visible HUD -- so it is not on
			# screen at all until that drawer opens, same "PanelId + Panel" bypass idiom
			# ForgePanel/ShopPanel/DepthsPanel above already use.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Depths")
			var watch_camp_modal = _ui.find_child("CampModal", true, false)
			if watch_camp_modal:
				watch_camp_modal.visible = false
			var watch_tutorial = _ui.find_child("TutorialOverlay", true, false)
			if watch_tutorial:
				watch_tutorial.visible = false
			# Wave E's first-touch lesson on the read-only surfaces fires from the same
			# OpenPanel("Depths") call above, and its banner covers the whole strip. It does not
			# appear on every run -- two clean builds captured this state with the drawer visible
			# and no banner at all -- so it is timing-dependent, which is worse than always: a
			# receipt for the most important screen in the game that is right most of the time is
			# a receipt nobody re-checks. Suppressed like the three above; the capture-time overlay
			# census (added in #592) is what caught it.
			var watch_mentor = _ui.find_child("MentorBanner", true, false)
			if watch_mentor:
				watch_mentor.visible = false
			# The departure slate ("THE SEND-OFF") is real MineWatch content, but it reports on
			# TODAY'S actual auto-formed party -- a different, unrelated departure from the
			# hand-staged floor 1/2 fight this receipt exists to show -- and it visually sits on
			# top of the backdrop/monster/HP-bar overlay this state is FOR. Suppressed here, same
			# "the modals that covered you are the thing to suppress" idiom as CampModal/
			# TutorialOverlay above.
			var watch_slate = _ui.find_child("DepartureSlate", true, false)
			if watch_slate:
				watch_slate.visible = false
		elif _state == "MineGateFocus":
			# U2 (shell-and-audio plan): the receipt for R1 -- "the mine is off the screen at
			# the top" -- proving the mine gate is reachable once the header no longer occludes
			# the world. Calls Town2D.FocusOnMineGate() directly (the SAME beat a real send-off
			# triggers, see MainUi.SoundTheTick) rather than depending on a fresh seed-2026
			# campaign actually forming a party on day 1 (it may not, depending on roster/gold
			# state) -- deterministic regardless of sim RNG, same call() bridge idiom as Mirror
			# above. Second beat below waits for the camera's own settle.
			var town0 = _ui.find_child("Town2D", true, false)
			if town0:
				town0.call("FocusOnMineGate", 3.2)
		elif _state == "GateNight":
			# U4 (world-and-interiors plan): the gatehouse's own warm InteriorWarmTint constant
			# (Town2D.cs, applied whenever InteriorActive) overrides the exterior's phase tint
			# entirely, so a receipt of the ROOM alone can never show a "dark phase" difference by
			# construction -- that override IS the feature being verified. What this state proves
			# instead: the room reads exactly as readable once the exterior has actually reached
			# Evening/"Night" (genuinely dark post-U11/#357, tint ~0.42) as it does at a fresh
			# Dawn. Same idiom as Phase4 above -- 4 presses in this one frame walk Morning ->
			# Expedition -> Camp -> ExpeditionDeep -> Evening -- except this state ALSO enters the
			# gatehouse afterward (frame 920, once the tint's ease has actually converged; see
			# _settle above for why the wait is so long).
			var bell = _ui.find_child("AdvancePhase", true, false)
			if bell:
				for _i in range(4):
					bell.emit_signal("pressed")
		elif _state == "SystemMenu":
			# U4 (shell-and-audio plan): the pause menu, U4's own new modal. OpenSystemMenu()/
			# CloseSystemMenu() are private C# (not reachable through the call() bridge), so this
			# drives the SAME real path Esc does at the engine level: set the found node's
			# built-in `visible` property (a genuine engine-side Control property, exposed to
			# every language) directly to true. That fires the real VisibilityChanged signal,
			# which runs the actual OnSystemMenuVisibilityChanged handler (pause the clock,
			# engage the latch, suppress world input) -- a fully production-wired state, not a
			# painted-on facsimile.
			var menu = _ui.find_child("SystemMenu", true, false)
			if menu:
				menu.visible = true
		elif _state == "HeroCandidateClosed" or _state == "HeroCandidateMid" or _state == "HeroCandidateOpen":
			# U13 (world-and-interiors plan): the motion-candidate receipt series -- three stills
			# of the striker's candidate leg poses (closed/mid/open, see
			# art/pipeline/gen-hero-candidates-r3.py) mounted beside the player at play scale.
			# Candidates only; nothing here is the production render path (see
			# _mount_hero_candidate's own doc).
			var pose = _state.substr(len("HeroCandidate")).to_lower()
			_mount_hero_candidate(pose)
		elif _state == "ReturnQuestEmpty" or _state == "ReturnEmerge":
			# U10 (world-and-interiors plan, KTD-5): the exact original bug's reproduction --
			# press the bell that ends Morning, then IMMEDIATELY press it again to end
			# Expedition, ZERO frames apart (the same synchronous-click shape
			# ExpeditionSystem.cs's unstaged resolve + the old immediate ReturnSurvivors snap
			# produced -- see Town2D.ReturnSurvivors' own doc). A fresh seed-2026 campaign's
			# day 1 is always unstaged: every starting hero's DeepestFloorReached is 0, so
			# day 1's target floor is always 1. What differs between the two states is only
			# how long this harness waits before capturing (see _settle above).
			var return_bell = _ui.find_child("AdvancePhase", true, false)
			if return_bell:
				return_bell.emit_signal("pressed")
				return_bell.emit_signal("pressed")
		elif _state == "HeroErrand":
			pass # U-T3-8: no click/bell -- just hold the plain town for the long settle above.
		elif _state == "TownOverview":
			pass # U-T3-3: no click/bell -- the player-teleport + zoom-out fires at frame 65 below.
		elif _state == "OccupancyCorner":
			pass # U-T3-3: no click/bell -- the player-teleport + zoom-in fires at frame 65 below.
		elif _state == "ReturnAtNight":
			# U10: same reproduction, but four presses (Morning -> Expedition -> Camp ->
			# ExpeditionDeep) land the day at Evening/"Night" -- see _settle above for why
			# pressing on through Camp/ExpeditionDeep does not re-queue or reset the hold.
			var night_bell = _ui.find_child("AdvancePhase", true, false)
			if night_bell:
				for _i in range(4):
					night_bell.emit_signal("pressed")
		elif _state == "Primer" or _state == "WarrantFirstMorning":
			# P2-ONBOARD-05: New Game -> Pick_blacksmith, both real Button.Pressed signals fired
			# synchronously (a C# event, never CONNECT_DEFERRED) -- so the primer is already showing
			# by the end of THIS frame, no second beat needed for the Primer receipt.
			var warrant_new_game = _ui.find_child("NewGame", true, false)
			if warrant_new_game:
				warrant_new_game.emit_signal("pressed")
			var warrant_pick_blacksmith = _ui.find_child("Pick_blacksmith", true, false)
			if warrant_pick_blacksmith:
				warrant_pick_blacksmith.emit_signal("pressed")
			else:
				push_error("[shot] SHOT_STATE=%s could not find Pick_blacksmith -- the shot below is the title menu and proves nothing about the Warrant." % _state)
			if _state == "WarrantFirstMorning":
				var warrant_begin = _ui.find_child("Begin", true, false)
				if warrant_begin:
					warrant_begin.emit_signal("pressed") # fires the deferred scene change -- see _process's own re-poll below
				else:
					push_error("[shot] SHOT_STATE=WarrantFirstMorning could not find Begin -- the shot below is the primer and proves nothing about the first morning.")
		elif _ui.has_method("OnTownBuildingClicked"):
			# Same entry point the town uses on building arrival (private C# method reached
			# via the source-gen call() bridge).
			_ui.call("OnTownBuildingClicked", _state)
		_entered = true
	# Watch's second beat: jump the delve overlay to floor 1's LethalSave-flared Exchange beat
	# (MineWatch.RevealDelveBeatsForReceipt's own doc explains why this fires at frame 200, 60
	# frames -- ~1s -- before the 260 settle above, instead of at mount time: the proof flare and
	# camera lean are real-time decay/ease curves, and firing them too early would have them fully
	# resolved by the time the PNG is actually captured).
	if _state == "Watch" and _frames == 200:
		var watch_reveal = _ui.find_child("MineWatch", true, false)
		if watch_reveal:
			watch_reveal.call("RevealDelveBeatsForReceipt", 3)
	# SendOff's second beat: press the real bell button through its own signal (the exact path
	# a player uses), a beat after the Forge drawer opened above -- this is what the departure
	# choreography (drawer close, camera pan, PiP dock) is actually reacting to.
	# Counter's second beat: the Shop drawer opened at frame 0 has settled, so press the real
	# "Open Counter" button CounterPanel.BuildClosedState builds -- the same production path a
	# player takes (Button -> OpenCounterAction -> Adapter.Queue), never a state injection. The
	# button is gated to Morning, which is where a fresh campaign starts, so it is live here.
	if _state == "Counter" and _frames == 90:
		var open_counter = _ui.find_child("OpenCounter", true, false)
		if open_counter:
			open_counter.emit_signal("pressed")
		else:
			# Loud, not silent: a shot that quietly photographs a CLOSED counter would read as
			# "the panel is fine" and is exactly how this bug survived being fixed once already.
			push_error("[shot] SHOT_STATE=Counter could not find the OpenCounter button -- "
				+ "the shot below is a CLOSED counter and proves nothing about the clip.")
	if _state == "SendOff" and _frames == 90:
		var bell = _ui.find_child("AdvancePhase", true, false)
		if bell:
			bell.emit_signal("pressed")
	# M2b, StoriedCard's second beat: the wall opened at frame 0 has settled, so press a real
	# storied row -- the same Button -> OnShowProvenance path a player clicking it takes. Loud on a
	# miss: a shot that quietly photographed a wall with no storied row would read as "the card is
	# fine" while proving nothing about the line this unit exists to add.
	if _state == "StoriedCard" and _frames == 90:
		var storied_row = _ui.find_child("Storied_*", true, false)
		if storied_row:
			storied_row.emit_signal("pressed")
		else:
			push_error("[shot] SHOT_STATE=StoriedCard could not find a Storied_* row button -- "
				+ "the shot below is a closed card and proves nothing about the storied line.")
	# M2b, StoriedRefusal's second and third beats: open the real counter, then present the real
	# shelved Plain Blade to whoever the morning queue promoted. Both are the production buttons
	# (Button -> OpenCounterAction / PresentItemAction -> Adapter.Queue), never a state injection.
	if _state == "StoriedRefusal" and _frames == 90:
		var open_counter_storied = _ui.find_child("OpenCounter", true, false)
		if open_counter_storied:
			open_counter_storied.emit_signal("pressed")
		else:
			push_error("[shot] SHOT_STATE=StoriedRefusal could not find the OpenCounter button -- "
				+ "the shot below is a CLOSED counter and no refusal can have been spoken.")
	if _state == "StoriedRefusal" and _frames == 150:
		var present_btn = _ui.find_child("Present_*", true, false)
		if present_btn:
			present_btn.emit_signal("pressed")
		else:
			push_error("[shot] SHOT_STATE=StoriedRefusal could not find a Present_* button -- "
				+ "nothing was ever offered, so the shot below proves nothing about the refusal.")
	# P2-PEOPLE-01, TavernSceneAtBar's second beat: the Tavern drawer opened at frame 0 has settled,
	# so press the arc-scene row's own Pursue -- the production path a player takes, the same one
	# the commission and ore rows beside it take. Loud on a miss: a shot that quietly photographed a
	# tavern with no scene in it would read as "the words are fine" while proving nothing at all.
	if _state == "TavernSceneAtBar" and _frames == 120:
		var pursue_scene = _ui.find_child("Pursue_Scene_1", true, false)
		if pursue_scene:
			pursue_scene.emit_signal("pressed")
		else:
			push_error("[shot] SHOT_STATE=TavernSceneAtBar could not find Pursue_Scene_1 -- either "
				+ "SHOT_ARC_SCENE is unset on the launching process or the scene did not offer, and "
				+ "the shot below proves nothing about Torvald's first scene.")
	# HeroTrinket/ShopTrinket's second beat: the drawer opened above (frame 60) has settled --
	# reach the panel INSIDE DrawerHost specifically (not a bare find_child("Heroes", ...) off the
	# whole tree, which would just as happily match Town2D's own "Heroes" Node2D root and silently
	# miss the dev bridge) and call its own dev bridge, same "hand-built GameState, zero sim
	# mutation" idiom as LedgerModal.Dev_ShowLedgerWithProvenanceBeat/
	# CommissionBoard.Dev_ShowSampleOpenCommission above.
	if _state == "HeroTrinket" and _frames == 90:
		var hero_trinket_drawer = _ui.find_child("DrawerHost", true, false)
		var hero_trinket_panel = hero_trinket_drawer.find_child("Heroes", true, false) if hero_trinket_drawer else null
		if hero_trinket_panel and hero_trinket_panel.has_method("Dev_ShowSampleTrinketGear"):
			hero_trinket_panel.call("Dev_ShowSampleTrinketGear")
		else:
			push_error("[shot] SHOT_STATE=HeroTrinket could not reach HeroesPanel.Dev_ShowSampleTrinketGear -- "
				+ "the shot below is a plain roster and proves nothing about the trinket chip fix.")
	if _state == "ShopTrinket" and _frames == 90:
		var shop_trinket_drawer = _ui.find_child("DrawerHost", true, false)
		var shop_trinket_panel = shop_trinket_drawer.find_child("Shop", true, false) if shop_trinket_drawer else null
		if shop_trinket_panel and shop_trinket_panel.has_method("Dev_ShowSampleUnshelvedTrinket"):
			shop_trinket_panel.call("Dev_ShowSampleUnshelvedTrinket")
		else:
			push_error("[shot] SHOT_STATE=ShopTrinket could not reach ShopPanel.Dev_ShowSampleUnshelvedTrinket -- "
				+ "the shot below is a plain shelf and proves nothing about the trinket chip fix.")
	# HeroTrinket/ShopTrinket's third beat: the dev bridge above (frame 90) has rebuilt the
	# panel's content -- scroll each panel's own content ScrollContainer (found scoped under
	# DrawerHost -> the panel's own root, same collision-avoidance reasoning as the frame-90
	# block above -- SimPanel.BuildScrollBody names EVERY panel's scroll body the generic
	# "Scroll", so a bare find_child("Scroll", ...) off the whole tree could just as easily
	# resolve a different registered panel's scroll node) down to the staged trinket card, which
	# sits below the fold on a fresh day-1 roster/shelf.
	if _state == "HeroTrinket" and _frames == 120:
		var hero_trinket_drawer2 = _ui.find_child("DrawerHost", true, false)
		var hero_trinket_panel2 = hero_trinket_drawer2.find_child("Heroes", true, false) if hero_trinket_drawer2 else null
		var hero_trinket_detail_scroll = hero_trinket_panel2.find_child("DetailScroll", true, false) if hero_trinket_panel2 else null
		var hero_trinket_provenance = hero_trinket_panel2.find_child("Provenance_90301", true, false) if hero_trinket_panel2 else null
		if hero_trinket_detail_scroll and hero_trinket_provenance:
			hero_trinket_detail_scroll.ensure_control_visible(hero_trinket_provenance)
		else:
			push_error("[shot] SHOT_STATE=HeroTrinket could not find DetailScroll/Provenance_90301 -- "
				+ "the shot below may not show the Trinket gear row.")
	if _state == "ShopTrinket" and _frames == 120:
		var shop_trinket_drawer2 = _ui.find_child("DrawerHost", true, false)
		var shop_trinket_panel2 = shop_trinket_drawer2.find_child("Shop", true, false) if shop_trinket_drawer2 else null
		var shop_trinket_content_scroll = shop_trinket_panel2.find_child("Scroll", true, false) if shop_trinket_panel2 else null
		var shop_trinket_card = shop_trinket_panel2.find_child("UnshelvedCard_90302", true, false) if shop_trinket_panel2 else null
		if shop_trinket_content_scroll and shop_trinket_card:
			shop_trinket_content_scroll.ensure_control_visible(shop_trinket_card)
		else:
			push_error("[shot] SHOT_STATE=ShopTrinket could not find Scroll/UnshelvedCard_90302 -- "
				+ "the shot below may not show the unshelved trinket card.")
	# Mirror's second beat: the bell above (frame 60) has landed a party in Expedition by now --
	# open the Scrying Mirror directly (ShowMirror is phase-ungated by design; see ScryingMirror
	# and MainUi's new Watch control) so the receipt shows real roll-call/"CARRYING YOUR WORK"
	# content, not an empty shell.
	if _state == "Mirror" and _frames == 90:
		var mirror = _ui.find_child("ScryingMirror", true, false)
		if mirror:
			mirror.call("ShowMirror")
	# TutorialLookIn's second beat: the bell above (frame 60) only landed Morning -> Expedition;
	# a party's own departure happens on Expedition's OWN tick, so this second press is what
	# actually advances TutorialFlow.Step to LookIn (see TutorialStepDef's own doc on the
	# day-1-unconditional muster row for why this is the "party departed" fact, not a click).
	if _state == "TutorialLookIn" and _frames == 90:
		var bell5 = _ui.find_child("AdvancePhase", true, false)
		if bell5:
			bell5.emit_signal("pressed")
	# Graduation's second beat: TutorialFlow.Completed already flipped true synchronously inside
	# the frame-60 dev bridge (a direct call, never routed through the HUD) -- pressing the real
	# bell here is what actually runs MainUi.RefreshHud, the SAME tick
	# ShowQuickTravelUnlockedLessonIfEarned fires the once-ever "quick-travel row just opened up
	# top" banner on in real play (mirrors TutorialCompleting_TeachesThatQuickTravelJustUnlocked's
	# own C# sequence: flip Completed off-HUD, then a real AdvancePhase is the tick that shows it).
	if _state == "Graduation" and _frames == 90:
		var bell6 = _ui.find_child("AdvancePhase", true, false)
		if bell6:
			bell6.emit_signal("pressed")
	# TutorialOffCamera's second beat (P2-SCREEN-10): this state's whole point is proving the
	# off-camera marker clears BOTH top-right docks, but the Tutorial dock never naturally shows
	# this early -- both its rows (second profession, quick travel) gate on eligibility a fresh
	# day-1 fixture has no reason to have earned. Forced here, capture-only, docked exactly where
	# MainUi.UpdateObjectiveDock docks it in real play: immediately below Objective's own live
	# bottom edge, with a real non-zero height. Direct rect writes on the two Nodes' own Godot
	# properties, never RefreshAffordances/RefreshHud -- either of those would immediately reset
	# Visible back to false from the real (ineligible) state, the same trap
	# OffCameraPointerTests.AnEasternTarget_PutsTheMarkerClearOfBothTopRightDocks documents on the
	# C# side (godot/tests/OffCameraPointerTests.cs).
	if _state == "TutorialOffCamera" and _frames == 90:
		var objective_dock = _ui.find_child("ObjectiveTracker", true, false)
		var tutorial_dock = _ui.find_child("TutorialFlow", true, false)
		var quick_travel_row = _ui.find_child("QuickTravelRow", true, false)
		if objective_dock and tutorial_dock:
			var obj_rect = objective_dock.get_global_rect()
			tutorial_dock.size = Vector2(obj_rect.size.x, 80.0)
			tutorial_dock.global_position = Vector2(obj_rect.position.x, obj_rect.end.y + 16.0)
			tutorial_dock.visible = true
			# Real content, not a fabricated label: QuickTravelRow's own venue buttons are built
			# unconditionally in TutorialFlow.Build regardless of eligibility -- only its own
			# Visible flag (normally gated on QuickTravelUnlocked) hides them. Revealing that one
			# flag shows the genuine row rather than an empty panel a viewer could mistake for a
			# rendering bug.
			if quick_travel_row:
				quick_travel_row.visible = true
		else:
			# Loud, not silent -- a shot that quietly photographs only ONE card would look like a
			# clean receipt and prove nothing about the defect this unit fixes (Counter's own
			# push_error above is the same idiom).
			push_error("[shot] SHOT_STATE=TutorialOffCamera could not find the Objective/Tutorial " +
				"docks -- the shot below is missing the second card this receipt exists to prove.")
	# SplitLessons' second beat: the workshop entry above (frame 60) has settled -- open the real
	# Lessons book (production OpenPanel path) so the receipt shows all three split lessons already
	# fired, as separate cards.
	if _state == "SplitLessons" and _frames == 90:
		if _ui.has_method("OpenPanel"):
			_ui.call("OpenPanel", "Lessons")
	# SplitLessons' third beat: the three split-lesson cards are the LAST thing LessonsPanel renders
	# (after all ten registry rows), so they sit well below the fold on open -- scroll the book's own
	# ScrollContainer (SimPanel.BuildScrollBody, node name "Scroll") to bring the first of the three
	# on screen.
	if _state == "SplitLessons" and _frames == 120:
		var lessonCard = _ui.find_child("Lesson_FirstTouch_slot-budget", true, false)
		if lessonCard:
			var scrollAncestor = lessonCard.get_parent()
			while scrollAncestor != null and not (scrollAncestor is ScrollContainer):
				scrollAncestor = scrollAncestor.get_parent()
			if scrollAncestor:
				scrollAncestor.ensure_control_visible(lessonCard)
	# GatedCounterEmptyShelf's second beat: the SAME Expedition -> Camp press
	# TutorialRegistryConformanceTests drives for real -- this is what actually lands the party's
	# departure and advances Step to LookIn. Opening the Mirror (LookIn's own taught affordance)
	# advances Step again, straight to OpenCounter -- Day 1, Phase Camp, exactly where that same
	# test parks it to check the Morning-only GatingNote fold.
	if _state == "GatedCounterEmptyShelf" and _frames == 90:
		var gatedBell2 = _ui.find_child("AdvancePhase", true, false)
		if gatedBell2:
			gatedBell2.emit_signal("pressed")
		var gatedMirror = _ui.find_child("ScryingMirror", true, false)
		if gatedMirror:
			gatedMirror.call("ShowMirror") # LookIn -> OpenCounter (the step, not the visible modal)
			gatedMirror.call("CloseMirror") # the modal itself must not linger and own the screen
	# GatedCounterEmptyShelf's third beat: the objective card renders the current step's own
	# instruction regardless of which drawer (if any) is open, so no further click is needed here
	# -- but the party's own return can auto-open CampPanel/LedgerModal along the way (events
	# entirely independent of the TUTORIAL's own Step, which never left OpenCounter --
	# CounterAnsweredAtLeastOnce was never satisfied), and either would otherwise cover the whole
	# screen. Suppressed the same way the "Watch" state above already suppresses CampModal for the
	# identical reason.
	if _state == "GatedCounterEmptyShelf" and _frames == 150:
		var gatedCampModal = _ui.find_child("CampModal", true, false)
		if gatedCampModal:
			gatedCampModal.visible = false
		var gatedLedger = _ui.find_child("LedgerModal", true, false)
		if gatedLedger:
			gatedLedger.visible = false
	# GateNight's second beat: the tint's ease has now converged (see _settle above) --
	# enter the gatehouse the same way every other venue receipt does
	# (OnTownBuildingClicked's "Gate" -> "minegate" mapping, MainUi.cs).
	if _state == "GateNight" and _frames == 920:
		if _ui.has_method("OnTownBuildingClicked"):
			_ui.call("OnTownBuildingClicked", "Gate")
	if _state == "ForgeExit" and _frames == 200:
		# The second beat: leave the room through the exit zone's own effect --
		# Town2D.ExitInterior -- a direct test seam, not a separate code path.
		var town = _ui.find_child("Town2D", true, false)
		if town:
			town.call("ExitInterior")
	if _state == "ForgeLadder" and _frames == 120:
		# Bring the LAST forward-ladder recipe row into view. Deliberately not "scroll to the
		# bottom": CraftScroll holds the talent list below the recipes, so scrolling to max_value
		# photographs Tier 3 Smithing unlock buttons and no recipe art at all (measured). Asking
		# the ScrollContainer to reveal a named card lands the ladder rows on screen with their
		# lower-tier siblings above, and keeps working as recipes are added either side of them.
		var craft_scroll = _ui.find_child("CraftScroll", true, false)
		var last_ladder_card = _ui.find_child("RecipeCard_emberglass-draught", true, false)
		if craft_scroll and last_ladder_card:
			craft_scroll.ensure_control_visible(last_ladder_card)
	if _state == "ForgeTrinket" and _frames == 120:
		# The trinket recipe (engineering-utility-multitool) sorts (Tier, then RecipeId) behind
		# this profession's Weapon/Shield/Armor Tier-1 cards, same "below the fold" shape
		# ForgeLadder's own block above exists for.
		var forge_trinket_scroll = _ui.find_child("CraftScroll", true, false)
		var forge_trinket_card = _ui.find_child("RecipeCard_engineering-utility-multitool", true, false)
		if forge_trinket_scroll and forge_trinket_card:
			forge_trinket_scroll.ensure_control_visible(forge_trinket_card)
		else:
			push_error("[shot] SHOT_STATE=ForgeTrinket could not find CraftScroll/RecipeCard_engineering-utility-multitool -- "
				+ "the shot below does not show the trinket recipe card. Was SHOT_PROFESSION=engineering set?")
	if _state == "ForgeShelf" and _frames == 200:
		# U3: the shelf station's own RaisePick -- the exact call a click/E-interact fires
		# (Building2D.Configure names each station node "Building_{key}").
		var shelf = _ui.find_child("Building_shelf", true, false)
		if shelf:
			shelf.call("RaisePick")
	if _state == "ForgeFlavor" and _frames == 200:
		# U3: the bellows station's own RaisePick -- proves the flavor toast, never a panel.
		var bellows = _ui.find_child("Building_bellows", true, false)
		if bellows:
			bellows.call("RaisePick")
	if (_state == "ForgeAnvil" or _state == "ForgeAnvilEmpty") and _frames == 200:
		# U3: the anvil station's own RaisePick -- the comparison receipt proving the shelf
		# scrolls somewhere DIFFERENT (materials) than the anvil does (craft/recipe cards).
		var anvil = _ui.find_child("Building_anvil", true, false)
		if anvil:
			anvil.call("RaisePick")
	# P2-SCREEN-09: five real Unlock_<id> button presses (never an adapter/state injection seam)
	# spend the whole day's action budget on cost-free talents -- zero gold, zero materials -- so
	# the SAME captured frame proves BOTH refusal shapes read honestly off one screen: recipe
	# cards refuse for missing materials (a fresh save always has none), and by the last press the
	# vendor/foundry/needs rows ALSO refuse for "No action slots left today".
	if _state == "ForgeAnvilEmpty":
		var unlock_idx = FORGE_ANVIL_EMPTY_UNLOCK_FRAMES.find(_frames)
		if unlock_idx != -1:
			var unlock_btn = _ui.find_child("Unlock_" + FORGE_ANVIL_EMPTY_UNLOCK_IDS[unlock_idx], true, false)
			if unlock_btn:
				unlock_btn.emit_signal("pressed")
			else:
				push_error("[shot] SHOT_STATE=ForgeAnvilEmpty could not find Unlock_%s at frame %d -- the captured frame will not actually be at zero action slots." % [FORGE_ANVIL_EMPTY_UNLOCK_IDS[unlock_idx], _frames])
	if _state == "LedgerProvenance" and _frames == 100:
		var ledger_scroll = _ui.find_child("LedgerScroll", true, false)
		if ledger_scroll:
			ledger_scroll.scroll_vertical = 9999
		else:
			push_error("[shot] SHOT_STATE=LedgerProvenance could not find LedgerScroll to scroll -- "
				+ "the beat row's second line will be clipped under the fold.")
	# Ledger's remaining beats: walk the rest of day 1's five phases (frame 60 above already
	# pressed the bell once, ending Morning) at the same 30-frame spacing SendOff/Mirror use,
	# then force the reveal open directly once day 1 has rolled over (see the elif above for
	# why this skips the real-time Return Ritual rather than waiting on it).
	if _state == "Ledger" and (_frames == 90 or _frames == 120 or _frames == 150 or _frames == 180):
		var ledger_bell = _ui.find_child("AdvancePhase", true, false)
		if ledger_bell:
			ledger_bell.emit_signal("pressed")
	if _state == "Ledger" and _frames == 220:
		# Press the REAL "OpenLedger" tray button (MainUi's Books Tray) rather than calling
		# LedgerModal.ShowFor directly -- this sidesteps BOTH the real-time Return Ritual wait
		# (a frame-counted wait against a wall-clock gate is exactly the "frame count is not a
		# duration" trap) AND any GDScript-call()-arity guesswork against a C# default
		# parameter, by driving the exact button a player clicks.
		var open_ledger = _ui.find_child("OpenLedger", true, false)
		if open_ledger:
			open_ledger.emit_signal("pressed")
	# ForgeAnvilEmpty's five talent-unlock presses (above) are a means to an end (draining the
	# action budget on zero gold/materials) -- Bryn's own first-touch teaching banner
	# ("ForgeMentorBanner", ForgePanel's own node) fires as a side effect of the FIRST talent
	# unlock and covers the whole panel, which is real content but not what THIS receipt is
	# about. Suppressed here, same "the modals that covered you are the thing to suppress" idiom
	# Watch's own CampModal/TutorialOverlay/MentorBanner/DepartureSlate suppression already uses.
	if _state == "ForgeAnvilEmpty" and _frames == _settle - 1:
		var forge_mentor = _ui.find_child("ForgeMentorBanner", true, false)
		if forge_mentor:
			forge_mentor.visible = false
	if _frames >= _settle:
		_warn_about_covering_overlays()
		var img := root.get_texture().get_image()
		var err := img.save_png(_out)
		if err != OK:
			push_error("shot_harness: save_png failed: %d" % err)
		return true # quit the SceneTree main loop
	return false


## Name every full-screen overlay still visible at the moment of capture.
##
## Each state above suppresses the modals that were in its way ON THE DAY IT WAS WRITTEN, by name.
## That list cannot know about a modal added later -- and one was: a Wave E first-touch lesson on
## the read-only surfaces fires when Depths opens, so the Watch receipt quietly became a photograph
## of a tutorial banner over an empty panel. Nothing failed. The PNG was produced, it was the right
## size, and it was wrong.
##
## So this does not suppress anything (a blanket hide would be its own silent lie -- it could hide
## the subject). It only says what is on top, loudly, on stderr. A receipt whose console output
## names a banner is a receipt somebody can catch; a receipt that says nothing cannot be.
func _warn_about_covering_overlays() -> void:
	# P2-ONBOARD-05: WarrantFirstMorning's own re-poll (_process, above) usually lands the swap to
	# the new MainUi well before this ever runs, but a slow swap on a loaded machine could still
	# leave _ui pointing at the old NewGameSelect instance, already queued_for_deletion by
	# ChangeSceneToFile -- is_instance_valid catches BOTH null and "freed or about to be," which a
	# bare null check does not.
	if not is_instance_valid(_ui):
		return
	var covering: Array[String] = []
	for node in _ui.find_children("*", "Control", true, false):
		var control := node as Control
		if control == null or not control.is_visible_in_tree():
			continue
		var n := String(control.name)
		if not (n.ends_with("Modal") or n.ends_with("Banner") or n.ends_with("Overlay") or n.ends_with("Slate")):
			continue
		var r := control.get_global_rect()
		if r.size.x >= 200.0 and r.size.y >= 60.0:
			covering.append("%s (%dx%d)" % [n, int(r.size.x), int(r.size.y)])
	if covering.size() > 0:
		printerr("shot_harness: state '%s' captured with these overlays still on screen: %s"
			% [_state, ", ".join(covering)])
