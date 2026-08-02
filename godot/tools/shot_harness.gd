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
	# Entering an interior needs extra frames for the camera push-in ease to settle.
	_settle = 90 if _state == "" else 320
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
		elif _state == "SendOff":
			# U1 (playtest-three plan): the receipt for "clicked send them off with a drawer
			# open — where are the visuals?" Open the Forge drawer here; the AdvancePhase
			# bell itself is pressed a beat later (see the `_frames == 90` check below), once
			# the drawer's own 0.22s slide-in has settled, mirroring the real player order
			# (craft, THEN ring the bell) rather than pressing through mid-slide.
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", "Forge")
		elif _state == "Mirror":
			# U1: the second required receipt -- the first proof any human has seen the
			# mirror render since it merged (#321). No drawer to open first; press the real
			# bell straight away so a party has actually departed by the time the Mirror
			# opens (see the frame==90 check below).
			var b2 = _ui.find_child("AdvancePhase", true, false)
			if b2:
				b2.emit_signal("pressed")
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
	if _frames >= _settle:
		var img := root.get_texture().get_image()
		var err := img.save_png(_out)
		if err != OK:
			push_error("shot_harness: save_png failed: %d" % err)
		return true # quit the SceneTree main loop
	return false
