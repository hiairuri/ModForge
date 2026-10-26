@tool
extends SceneTree

# Exports authentic game presentation assets as PNG, using the engine's own decoders.
#
# Some assets (card frames, banners) live in VRAM-compressed atlases stored as BC7,
# which a plain image decoder cannot read. Loading them through the engine gives access
# to its full texture pipeline, so this script mounts the game's pack and writes each
# requested resource out as an ordinary PNG that any renderer can display.
#
# Usage:
#   godot --headless --script export_assets.gd -- <game-pck> <out-dir> <res-path>...

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 3:
		printerr("usage: -- <game-pck> <out-dir> <res-path>...")
		quit(2)
		return

	var pck_path := args[0]
	var out_dir := args[1]
	var wanted := args.slice(2)

	if not ProjectSettings.load_resource_pack(pck_path, true):
		printerr("could not mount: ", pck_path)
		quit(3)
		return

	DirAccess.make_dir_recursive_absolute(out_dir)

	var failed := 0
	for res_path in wanted:
		if not await _export_one(res_path, out_dir):
			failed += 1

	quit(1 if failed > 0 else 0)


func _export_one(res_path: String, out_dir: String) -> bool:
	if not ResourceLoader.exists(res_path):
		printerr("missing: ", res_path)
		return false

	var resource := ResourceLoader.load(res_path, "", ResourceLoader.CACHE_MODE_REUSE)
	if resource == null:
		printerr("failed to load: ", res_path)
		return false

	# A sprite is either a texture already, or a resource that exposes one.
	var texture: Texture2D = null
	if resource is Texture2D:
		texture = resource
	elif resource.get("atlas") != null:
		texture = resource.get("atlas")

	if texture == null:
		printerr("no texture in: ", res_path)
		return false

	# texture.get_image() is unreliable for VRAM-compressed textures, so the atlas
	# image is loaded from its source path and cropped with Image operations.
	var image := _load_atlas_image(resource, texture)
	if image == null or image.is_empty():
		printerr("no image data in: ", res_path)
		return false

	# A compressed image cannot be cropped, so decompress before applying a region.
	if image.is_compressed():
		var error := image.decompress()
		if error != OK:
			# BC7 这类压缩纹理，Image.decompress() 也解不了（Error 15）。
			# 这时回退到用引擎的渲染管线把纹理画出来——它有完整的解码器。
			var rendered: Image = await _render_texture(resource, texture)
			if rendered != null:
				image = rendered
			else:
				printerr("could not decompress: ", res_path, " (", error, ")")
				return false

	# A region sprite must be cropped to the region it declares.
	if resource.get("region") != null:
		var region: Rect2 = resource.get("region")
		if region.size.x > 0.0 and region.size.y > 0.0:
			image = image.get_region(Rect2i(region))

	var name := res_path.get_file().get_basename()
	var out_path := out_dir.path_join(name + ".png")
	var save_error := image.save_png(out_path)
	if save_error != OK:
		printerr("save failed (", save_error, "): ", out_path)
		return false

	print("ok: ", out_path, " ", image.get_width(), "x", image.get_height())
	return true


# Loads the underlying atlas image for a sprite, so the pixels are real even when the
# texture itself is VRAM-compressed.
func _load_atlas_image(resource: Resource, texture: Texture2D) -> Image:
	if resource.get("atlas") != null:
		var atlas: Texture2D = resource.get("atlas")

		# In an exported build the atlas's source .png is replaced by an imported file,
		# so the sibling .import stub is read to find the file that holds the real pixels.
		# This also avoids an error for the missing raw .png.
		var path: String = atlas.resource_path
		if path != "":
			var resolved := _resolve_import(path)
			if resolved != "":
				var image := Image.load_from_file(resolved)
				if image != null and not image.is_empty():
					return image

		# Fall back to the texture itself, which works for the uncompressed case.
		return atlas.get_image()

	return texture.get_image()


# Reads "<path>.import" and returns the file it remaps to, or "" when there is none.
func _resolve_import(source_path: String) -> String:
	var stub_path := source_path + ".import"
	if not FileAccess.file_exists(stub_path):
		return ""

	var stub := FileAccess.open(stub_path, FileAccess.READ)
	if stub == null:
		return ""

	while not stub.eof_reached():
		var line := stub.get_line().strip_edges()
		if line.begins_with("path"):
			var start := line.find("\"res://")
			if start >= 0:
				var rest := line.substr(start + 1)
				var end := rest.find("\"")
				if end > 0:
					return rest.substr(0, end)

	return ""

# Draws the texture through the engine's render pipeline and reads the pixels back.
#
# Needed for BC7-compressed sprites: neither Image.load_from_file nor Image.decompress()
# can read them, but the renderer decodes them for display, so rendering to a viewport and
# copying the frame gives us the real pixels.
func _render_texture(resource: Resource, texture: Texture2D) -> Image:
	var size := Vector2i(texture.get_width(), texture.get_height())
	if size.x <= 0 or size.y <= 0:
		return null

	var viewport := SubViewport.new()
	viewport.size = size
	viewport.transparent_bg = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	root.add_child(viewport)

	var rect := TextureRect.new()
	rect.texture = texture
	rect.size = Vector2(size)
	if resource.get("region") != null:
		var region: Rect2 = resource.get("region")
		# Present the whole atlas, then crop afterwards.
		rect.stretch_mode = TextureRect.STRETCH_KEEP
	rect.position = Vector2.ZERO
	viewport.add_child(rect)

	await RenderingServer.frame_post_draw

	var image := viewport.get_texture().get_image()
	viewport.queue_free()

	if image == null or image.is_empty():
		return null

	# A region sprite still has to be cropped out of the atlas we just rendered.
	if resource.get("region") != null:
		var region: Rect2 = resource.get("region")
		if region.size.x > 0.0 and region.size.y > 0.0 and region.end.x <= image.get_width() and region.end.y <= image.get_height():
			image = image.get_region(Rect2i(region))

	return image
