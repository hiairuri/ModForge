@tool
extends SceneTree

# Converts image files to WebP using the engine's own codecs.
#
# Godot is used here because it ships a reliable WebP encoder, which the game requires
# for any custom texture that is packed into a mod.
#
# Usage:
#   godot --headless --script convert_webp.gd -- <input1> <input2> ...

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

	# Card art is composited over the frame, so an alpha channel is wanted. WebP is a
	# lossless-capable format here, which keeps the artwork crisp at card size.
	if image.get_format() != Image.FORMAT_RGBA8:
		image.convert(Image.FORMAT_RGBA8)

	var output_path := input_path.get_basename() + ".webp"
	var error := image.save_webp(output_path, false)
	if error != OK:
		printerr("failed to write ", output_path, " (error ", error, ")")
		return false

	print("ok: ", output_path)
	return true
