# Automated PLAYTHROUGH capture (not a single static frame).
#
# Loads main_ui.tscn (self-seeds a deterministic SimAdapter, seed 2026), then PLAYS the game
# forward by pressing the production "AdvancePhase" phase-bell button repeatedly — the exact same
# path a human clicking "Send them off" / "Skip" drives — pumping frames between presses so the UI,
# clock, ticker, and 3D town animate. Captures a PNG at several day milestones plus a final Renown
# panel shot showing heroes that have actually raided/ranked/died over the run.
#
# Runs NON-headless (windowed/minimized, GPU) via tools/play_shoot.ps1 (timeout+kill safety net).
#   PLAY_OUT=<abs dir>  godot --path <godot dir> -s godot/tools/play_harness.gd

extends SceneTree

var _ui: Node = null
var _frames := 0
var _advances := 0
var _out := ""
# [advances_reached, filename, optional_panel_id]
var _shots := [
	[8, "play_day2_town", ""],
	[20, "play_day4_town", ""],
	[30, "play_day6_town", ""],
	[30, "play_day6_renown", "HeroCards"],
	[30, "play_day6_demand", "Demand"],
]
var _shot_idx := 0
var _settle_after_panel := -1
var _pending_shot := ""

func _initialize() -> void:
	_out = OS.get_environment("PLAY_OUT")
	if _out == "":
		push_error("play_harness: PLAY_OUT not set")
		quit(1)
		return
	_ui = load("res://scenes/panels/main_ui.tscn").instantiate()
	root.add_child(_ui)

func _capture(name: String) -> void:
	var img := root.get_texture().get_image()
	img.save_png(_out + "/" + name + ".png")

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames < 90:
		return false # initial settle / seed

	# A panel shot was requested: open it, wait a few frames for the drawer slide, then capture.
	if _settle_after_panel >= 0:
		if _frames >= _settle_after_panel:
			_capture(_pending_shot)
			_settle_after_panel = -1
			_pending_shot = ""
			if _shot_idx >= _shots.size():
				return true # done
		return false

	# Advance one phase every 10 frames (lets each phase's reveal + dolly settle).
	if _frames % 10 == 0 and _advances < 32:
		var btn = _ui.find_child("AdvancePhase", true, false)
		if btn:
			btn.emit_signal("pressed")
		_advances += 1

	# Fire the next milestone shot once we've advanced far enough.
	if _shot_idx < _shots.size() and _advances >= _shots[_shot_idx][0]:
		var entry = _shots[_shot_idx]
		_shot_idx += 1
		var panel_id: String = entry[2]
		if panel_id == "":
			_capture(entry[1]) # town shot, capture immediately
			if _shot_idx >= _shots.size():
				return true
		else:
			if _ui.has_method("OpenPanel"):
				_ui.call("OpenPanel", panel_id)
			_pending_shot = entry[1]
			_settle_after_panel = _frames + 30 # let the drawer slide + panel refresh
	return false
