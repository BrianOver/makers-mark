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
#   SHOT_OUT=<abs png path>  SHOT_STATE=<""|Forge|Shop|Tavern|Gate>
#   godot --path <godot dir> -s godot/tools/shot_harness.gd
# Empty SHOT_STATE captures the town; a venue key enters that interior through the
# production OnTownBuildingClicked path, then waits for the camera dolly to settle.
# main_ui.tscn self-seeds a deterministic SimAdapter (seed 2026) on _Ready.

extends SceneTree

var _frames := 0
var _ui: Node = null
var _entered := false
var _out := ""
var _state := ""
var _settle := 90

func _initialize() -> void:
	_out = OS.get_environment("SHOT_OUT")
	_state = OS.get_environment("SHOT_STATE")
	if _out == "":
		push_error("shot_harness: SHOT_OUT not set")
		quit(1)
		return
	# Entering an interior needs extra frames for the camera push-in ease to settle.
	_settle = 90 if _state == "" else 320
	_ui = load("res://scenes/panels/main_ui.tscn").instantiate()
	root.add_child(_ui)

func _process(_delta: float) -> bool:
	_frames += 1
	if _state != "" and not _entered and _frames == 60:
		# Same entry point the town uses on building arrival (private C# method reached
		# via the source-gen call() bridge).
		if _ui.has_method("OnTownBuildingClicked"):
			_ui.call("OnTownBuildingClicked", _state)
		_entered = true
	if _frames >= _settle:
		var img := root.get_texture().get_image()
		var err := img.save_png(_out)
		if err != OK:
			push_error("shot_harness: save_png failed: %d" % err)
		return true # quit the SceneTree main loop
	return false
