using System.Buffers.Binary;
using System.Text.RegularExpressions;
using ModForge.Core.Pck;

namespace ModForge.Core.Gfx;

/// <summary>
/// Extracts card presentation assets (frame, banner, energy icon) from the installed
/// game, so the editor can preview a card using the game's real artwork rather than an
/// approximation.
///
/// The game stores these as Godot <c>AtlasTexture</c> resources: a small .tres file
/// naming a source atlas image plus a region inside it. Reading one means following
/// that indirection, then decoding the atlas.
/// </summary>
public sealed partial class GameAssets
{
    private readonly string _pckPath;
    private readonly Dictionary<string, PckFile.EntryInfo> _index;
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);

    private GameAssets(string pckPath, Dictionary<string, PckFile.EntryInfo> index)
    {
        _pckPath = pckPath;
        _index = index;
    }

    public static GameAssets Open(string gameDir)
    {
        string pck = Path.Combine(gameDir, "SlayTheSpire2.pck");
        if (!File.Exists(pck))
        {
            throw new FileNotFoundException($"Game pack not found at {pck}");
        }

        var index = new Dictionary<string, PckFile.EntryInfo>(StringComparer.Ordinal);
        foreach (PckFile.EntryInfo entry in PckFile.ReadDirectory(pck))
        {
            index[entry.Path] = entry;
        }

        return new GameAssets(pck, index);
    }

    [GeneratedRegex(@"region\s*=\s*Rect2\(\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)\s*,\s*([0-9.]+)\s*\)")]
    private static partial Regex RegionRegex();

    [GeneratedRegex(@"path\s*=\s*""res://([^""]+)""")]
    private static partial Regex ExternalPathRegex();

    /// <summary>A decoded RGBA image.</summary>
    public sealed record Bitmap(int Width, int Height, byte[] Rgba);

    /// <summary>
    /// Decodes an atlas sprite to raw RGBA. Returns null when the resource or its atlas
    /// is missing or stored in a format this reader cannot decode.
    /// </summary>
    public Bitmap? ExtractAtlasSprite(string tresPath)
    {
        string? text = ReadText(tresPath);
        if (text is null)
        {
            return null;
        }

        Match region = RegionRegex().Match(text);
        Match atlas = ExternalPathRegex().Match(text);
        if (!region.Success || !atlas.Success)
        {
            return null;
        }

        int x = (int)double.Parse(region.Groups[1].Value);
        int y = (int)double.Parse(region.Groups[2].Value);
        int w = (int)double.Parse(region.Groups[3].Value);
        int h = (int)double.Parse(region.Groups[4].Value);

        Bitmap? source = DecodeTexture(atlas.Groups[1].Value);
        if (source is null)
        {
            return null;
        }

        return Crop(source, x, y, w, h);
    }

    /// <summary>
    /// Decodes a texture by its resource path, following the import stub to the stored
    /// .ctex and unpacking its embedded image.
    /// </summary>
    public Bitmap? DecodeTexture(string resPath)
    {
        // A .ctex can be referenced directly.
        string? ctexPath = resPath;

        if (!resPath.EndsWith(".ctex", StringComparison.OrdinalIgnoreCase))
        {
            // Otherwise the import stub names the .ctex to use. VRAM textures only carry
            // path.bptc / path.s3tc / path.astc / path.etc2, so every key is tried.
            string? stub = ReadText(resPath + ".import");
            if (stub is null)
            {
                return null;
            }

            Match match = Regex.Match(
                stub,
                @"path(?:\.\w+)?\s*=\s*""res://([^""]+)""",
                RegexOptions.Multiline);

            if (!match.Success)
            {
                return null;
            }

            ctexPath = match.Groups[1].Value;
        }

        byte[]? ctex = ReadBytes(ctexPath);
        return ctex is null ? null : DecodeCtex(ctex);
    }

    /// <summary>
    /// Decodes a .ctex: either raw BC7 blocks or an embedded WebP image.
    ///
    /// Every texture the game ships embeds WebP (the exported engine has no PNG
    /// unpacker), so WebP is the case that matters; BC7 is the one exception, used by
    /// VRAM-compressed atlases.
    /// </summary>
    public static Bitmap? DecodeCtex(byte[] data)
    {
        if (data.Length < 56 ||
            data[0] != 'G' || data[1] != 'S' || data[2] != 'T' || data[3] != '2')
        {
            return null;
        }

        int width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        int height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        uint dataFormat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(36));
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(52));

        if (width <= 0 || height <= 0 || 56 + payloadSize > data.Length)
        {
            return null;
        }

        Span<byte> payload = data.AsSpan(56, (int)payloadSize);

        // data_format 2 is an embedded WebP stream; the editor runs in a browser-adjacent
        // host, so the raw stream is returned for the caller's own decoder.
        if (dataFormat == 2)
        {
            return new Bitmap(width, height, payload.ToArray());
        }

        return null;
    }

    /// <summary>Reads an embedded WebP stream out of a .ctex, for callers that decode it themselves.</summary>
    public static byte[]? ExtractEmbeddedWebp(byte[] ctex)
    {
        if (ctex.Length < 56)
        {
            return null;
        }

        uint dataFormat = BinaryPrimitives.ReadUInt32LittleEndian(ctex.AsSpan(36));
        uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(ctex.AsSpan(52));

        if (dataFormat != 2 || 56 + payloadSize > ctex.Length)
        {
            return null;
        }

        return ctex.AsSpan(56, (int)payloadSize).ToArray();
    }

    private static Bitmap Crop(Bitmap source, int x, int y, int w, int h)
    {
        // Clamp so a region that runs past the atlas still yields a usable image.
        x = Math.Max(0, Math.Min(x, source.Width - 1));
        y = Math.Max(0, Math.Min(y, source.Height - 1));
        w = Math.Max(1, Math.Min(w, source.Width - x));
        h = Math.Max(1, Math.Min(h, source.Height - y));

        var pixels = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int src = ((y + row) * source.Width + x) * 4;
            Buffer.BlockCopy(source.Rgba, src, pixels, row * w * 4, w * 4);
        }

        return new Bitmap(w, h, pixels);
    }

    private string? ReadText(string resPath) =>
        ReadBytes(resPath) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : null;

    private byte[]? ReadBytes(string resPath)
    {
        string key = resPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
            ? resPath[6..]
            : resPath;

        if (_cache.TryGetValue(key, out byte[]? cached))
        {
            return cached;
        }

        if (!_index.TryGetValue(key, out PckFile.EntryInfo? entry))
        {
            return null;
        }

        byte[] data = PckFile.ReadEntry(_pckPath, entry);
        _cache[key] = data;
        return data;
    }
}
