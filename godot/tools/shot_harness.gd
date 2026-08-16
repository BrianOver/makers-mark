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

extends SceneTree

var _frames := 0
var _ui: Node = null
var _entered := false
var _out := ""
var _state := ""
var _quiet := false
var _ambient_suppressed := false
var _settle := 90

func _initialize() -> void:
	_out = OS.get_environment("SHOT_OUT")
	_state = OS.get_environment("SHOT_STATE")
	_quiet = OS.get_environment("SHOT_QUIET") == "1"
	if _out == "":
		push_error("shot_harness: SHOT_OUT not set")
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
	elif _state == "BellTray":
		# U3 (loop-legibility plan, KTD-B): a plain HUD chip, no camera move -- but the
		# ack toast auto-clears after MainUi.RejectionToastSeconds (4s = 240 frames), so the
		# default 320-frame settle below would already show the tray chip WITHOUT the toast.
		# 90 (the plain-town default) lands comfortably inside the toast's window.
		_settle = 90
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
	else:
		_settle = 320
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
		elif _state == "TavernPanel":
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Tavern")
		elif _state == "DepthsPanel":
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Depths")
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
		elif _state == "ForgeExit":
			# U1 (painted-interiors plan): the second required receipt -- proves the exit
			# door returns the player OUTSIDE. Enters the room the normal way; the second
			# beat (frame 200 below) calls Town2D.ExitInterior() directly, mirroring how
			# SendOff/Mirror above drive their own second beat through a real signal/call.
			if _ui.has_method("OnTownBuildingClicked"):
				_ui.call("OnTownBuildingClicked", "Forge")
		elif _state == "ForgeShelf" or _state == "ForgeFlavor" or _state == "ForgeAnvil":
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
		elif _state == "ReturnAtNight":
			# U10: same reproduction, but four presses (Morning -> Expedition -> Camp ->
			# ExpeditionDeep) land the day at Evening/"Night" -- see _settle above for why
			# pressing on through Camp/ExpeditionDeep does not re-queue or reset the hold.
			var night_bell = _ui.find_child("AdvancePhase", true, false)
			if night_bell:
				for _i in range(4):
					night_bell.emit_signal("pressed")
		elif _ui.has_method("OnTownBuildingClicked"):
			# Same entry point the town uses on building arrival (private C# method reached
			# via the source-gen call() bridge).
			_ui.call("OnTownBuildingClicked", _state)
		_entered = true
	# SendOff's second beat: press the real bell button through its own signal (the exact path
	# a player uses), a beat after the Forge drawer opened above -- this is what the departure
	# choreography (drawer close, camera pan, PiP dock) is actually reacting to.
	if _state == "SendOff" and _frames == 90:
		var bell = _ui.find_child("AdvancePhase", true, false)
		if bell:
			bell.emit_signal("pressed")
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
	if _state == "ForgeAnvil" and _frames == 200:
		# U3: the anvil station's own RaisePick -- the comparison receipt proving the shelf
		# scrolls somewhere DIFFERENT (materials) than the anvil does (craft/recipe cards).
		var anvil = _ui.find_child("Building_anvil", true, false)
		if anvil:
			anvil.call("RaisePick")
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
	if _frames >= _settle:
		var img := root.get_texture().get_image()
		var err := img.save_png(_out)
		if err != OK:
			push_error("shot_harness: save_png failed: %d" % err)
		return true # quit the SceneTree main loop
	return false
