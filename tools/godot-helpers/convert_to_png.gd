@tool
extends SceneTree

# Converts image files to PNG using the engine's own codecs.
#
# Used for the mod cover (About/Preview.png), which is written under a fixed name —
# a file whose extension says PNG should actually be a PNG, otherwise whoever opens it
# sees a broken image.
#
# Usage:
#   godot --headless --script convert_to_png.gd -- <input1> <input2> ...

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		printerr("usage: -- <input-file> [more-input-files...]")
		quit(2)
		return

	var failed := 0
	for input_path in args:
		if not _convert_one(input_path):
			failed += 1

	quit(1 if failed > 0 else 0)


func _convert_one(input_path: String) -> bool:
	var image := Image.load_from_file(input_path)
	if image == null or image.is_empty():
		printerr("could not open image: ", input_path)
		return false

	# A cover may carry transparency in some authoring tools; keep it.
	if image.get_format() != Image.FORMAT_RGBA8:
		image.convert(Image.FORMAT_RGBA8)

	var output_path := input_path.get_basename() + ".png"
	var error := image.save_png(output_path)
	if error != OK:
		printerr("failed to write ", output_path, " (error ", error, ")")
		return false

	print("ok: ", output_path)
	return true
