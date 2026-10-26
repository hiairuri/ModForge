using System.Buffers.Binary;

namespace ModForge.Core.Gfx;

/// <summary>
/// Writes Godot's compressed texture container (.ctex), so a mod can ship custom
/// card art inside its PCK instead of relying on loose files at runtime.
///
/// Layout, read from the engine's own loader (scene/resources/compressed_texture.cpp)
/// and verified byte-for-byte against the game's shipped portraits:
///
///   @0   "GST2"
///   @4   u32 version (1)
///   @8   u32 width
///   @12  u32 height
///   @16  u32 detect flags (0)
///   @20  u32 mipmap limit
///   @24  u32 reserved
///   @28  u32 reserved
///   @32  u32 reserved
///   @36  u32 data format: 1 = embedded PNG, 2 = embedded WEBP, 0 = raw image data
///   @40  u16 width
///   @42  u16 height
///   @44  u32 mipmap count
///   @48  u32 image format (the pixel format Godot converts to)
///   @52  u32 payload size
///   @56  payload
///
/// Only the WebP container is used for output. This was measured inside the shipping
/// game: a WebP-payload .ctex loads correctly, while a PNG-payload one is rejected,
/// because the exported template is built without the PNG unpacker. Every texture the
/// game itself ships is WebP for the same reason.
/// </summary>
public static class CtexWriter
{
    /// <summary>Container data format: a file inside the payload.</summary>
    private const uint DataFormatPng = 1;

    private const uint DataFormatWebp = 2;

    /// <summary>Godot Image::Format.RGBA8.</summary>
    private const uint FormatRgba8 = 5;

    private const uint Version = 1;

    /// <summary>
    /// Builds a .ctex from a PNG file's bytes. The PNG is embedded verbatim.
    /// </summary>
    public static byte[] FromPng(byte[] pngBytes)
    {
        (int width, int height) = ReadPngSize(pngBytes);
        return Build(pngBytes, DataFormatPng, width, height);
    }

    /// <summary>Builds a .ctex from a WebP file's bytes.</summary>
    public static byte[] FromWebp(byte[] webpBytes)
    {
        (int width, int height) = ReadWebpSize(webpBytes);
        return Build(webpBytes, DataFormatWebp, width, height);
    }

    /// <summary>
    /// Builds a .ctex usable by the shipping game. The input must be WebP, because the
    /// exported engine has no PNG unpacker (measured in game), so a PNG is reported as
    /// unsupported rather than written into art that would fail to load.
    /// </summary>
    public static byte[] BuildForGame(byte[] imageBytes)
    {
        if (IsWebp(imageBytes))
        {
            return FromWebp(imageBytes);
        }

        throw new NotSupportedException(
            "Card art must be a WebP image: the game's exported engine only ships a WebP " +
            "unpacker, so a PNG payload fails to load at runtime.");
    }

    /// <summary>
    /// Builds a .ctex for an image file. Returns null, with a reason, when the format is
    /// one the game cannot load.
    /// </summary>
    public static byte[]? FromFile(string path, out string? reason)
    {
        byte[] bytes = File.ReadAllBytes(path);
        reason = null;

        if (IsWebp(bytes))
        {
            return FromWebp(bytes);
        }

        if (IsPng(bytes))
        {
            reason = "PNG is not supported by the game's texture loader; convert the art to WebP.";
            return null;
        }

        reason = "unsupported image format; use WebP.";
        return null;
    }

    public static bool IsPng(byte[] data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    public static bool IsWebp(byte[] data) =>
        data.Length >= 12 &&
        data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
        data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';

    private static byte[] Build(byte[] payload, uint dataFormat, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Image dimensions must be positive.");
        }

        // The inner header stores dimensions as 16-bit values.
        if (width > ushort.MaxValue || height > ushort.MaxValue)
        {
            throw new ArgumentException($"Image is too large for this container: {width}x{height}.");
        }

        using var stream = new MemoryStream(56 + payload.Length);
        using var writer = new BinaryWriter(stream);

        writer.Write(new[] { (byte)'G', (byte)'S', (byte)'T', (byte)'2' });
        writer.Write(Version);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(0u);            // detect flags
        writer.Write(0xFFFFFFFFu);   // mipmap limit
        writer.Write(0u);            // reserved
        writer.Write(0u);            // reserved
        writer.Write(0u);            // reserved
        writer.Write(dataFormat);
        writer.Write((ushort)width);
        writer.Write((ushort)height);
        writer.Write(0u);            // mipmap count
        writer.Write(FormatRgba8);
        writer.Write((uint)payload.Length);
        writer.Write(payload);

        return stream.ToArray();
    }

    /// <summary>
    /// Reads dimensions from a PNG's IHDR chunk. Layout: 8-byte signature, then a
    /// length + "IHDR" + width (big-endian) + height (big-endian).
    /// </summary>
    public static (int Width, int Height) ReadPngSize(byte[] png)
    {
        if (!IsPng(png) || png.Length < 24)
        {
            throw new ArgumentException("Not a valid PNG file.");
        }

        if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
        {
            throw new ArgumentException("PNG is missing its IHDR chunk.");
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
    }

    /// <summary>
    /// Reads dimensions from a WebP header, handling the VP8, VP8L and VP8X variants.
    /// </summary>
    public static (int Width, int Height) ReadWebpSize(byte[] webp)
    {
        if (!IsWebp(webp) || webp.Length < 30)
        {
            throw new ArgumentException("Not a valid WebP file.");
        }

        string chunk = System.Text.Encoding.ASCII.GetString(webp, 12, 4);

        switch (chunk)
        {
            case "VP8X":
                // Canvas size is stored as (value - 1) in 24-bit little-endian fields.
                int w = webp[24] | (webp[25] << 8) | (webp[26] << 16);
                int h = webp[27] | (webp[28] << 8) | (webp[29] << 16);
                return (w + 1, h + 1);

            case "VP8L":
                // 14-bit width-1 and height-1 packed after the signature byte.
                int bits = webp[21] | (webp[22] << 8) | (webp[23] << 16) | (webp[24] << 24);
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);

            case "VP8 ":
                // Lossy: 16-bit dimensions after the 3-byte start code and 3-byte signature.
                int lw = (webp[26] | (webp[27] << 8)) & 0x3FFF;
                int lh = (webp[28] | (webp[29] << 8)) & 0x3FFF;
                return (lw, lh);

            default:
                throw new ArgumentException($"Unsupported WebP variant '{chunk}'.");
        }
    }
}
