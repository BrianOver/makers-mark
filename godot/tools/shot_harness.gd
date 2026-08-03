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
	else:
		_settle = 320
	_ui = load("res://scenes/panels/main_ui.tscn").instantiate()
	root.add_child(_ui)
	if _quiet:
		_try_suppress_ambient_vfx() # in case a future refactor DOES build it synchronously

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
		elif _state == "ForgePanel":
			# U1 (painted-interiors plan): "Forge" now walks the player INTO the room instead
			# of opening the drawer directly (R1). This state bypasses the room and opens the
			# ForgePanel drawer straight by id, so a drawer-only receipt stays possible for
			# comparison against the room (same idiom as Demand/HeroCards above).
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
