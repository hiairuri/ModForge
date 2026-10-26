using ModForge.Core.Pck;

namespace ModForge.Core.Gfx;

/// <summary>一个可选的卡面来源。</summary>
public sealed record CardArtSource(
    /// <summary>"local"（编辑器 cardart 文件夹）或 "vanilla"（游戏本体）。</summary>
    string Kind,

    /// <summary>局部来源是文件名；原版是 "pool/stem"。</summary>
    string Id,

    /// <summary>界面显示名。</summary>
    string Display,

    /// <summary>角色或分组，用于筛选。</summary>
    string Group,

    /// <summary>取图的相对地址。</summary>
    string Url);

/// <summary>
/// 可选的卡面素材，来自两处：
///
///   编辑器根目录的 cardart 文件夹 —— 用户自己放进去的图片
///   游戏本体                      —— 原版卡图，从 PCK 里取出
///
/// 原版卡图在 PCK 里是 .ctex（内含 WebP），取出时不需要解码像素，直接把内嵌的 WebP 流
/// 交给浏览器即可——浏览器原生支持 WebP。
/// </summary>
public sealed class CardArtLibrary
{
    /// <summary>支持放进 cardart 文件夹的图片格式。</summary>
    private static readonly string[] LocalExtensions = { ".png", ".webp", ".jpg", ".jpeg", ".gif" };

    private readonly string _gameDir;

    /// <summary>素材库目录；未配置时为 null（此时本地来源为空列表）。</summary>
    private readonly string? _localDir;

    private Dictionary<string, PckFile.EntryInfo>? _pckIndex;
    private readonly Dictionary<string, Dictionary<string, string>> _namesByLanguage = new(StringComparer.Ordinal);

    public CardArtLibrary(string gameDir, string? localDir)
    {
        _gameDir = gameDir;
        _localDir = string.IsNullOrWhiteSpace(localDir) ? null : localDir;
    }

    /// <summary>素材库的绝对路径，界面上会显示出来，方便用户去放图。未配置时为 null。</summary>
    public string? LocalDirectory => _localDir;

    /// <summary>素材库文件夹里可用的图片。</summary>
    public IReadOnlyList<CardArtSource> ListLocal()
    {
        if (_localDir is null || !Directory.Exists(_localDir))
        {
            return Array.Empty<CardArtSource>();
        }

        return Directory.EnumerateFiles(_localDir, "*", SearchOption.AllDirectories)
            .Where(f => LocalExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                string rel = Path.GetRelativePath(_localDir, f).Replace('\\', '/');
                return new CardArtSource(
                    Kind: "local",
                    Id: rel,
                    Display: Path.GetFileNameWithoutExtension(f),
                    Group: Path.GetDirectoryName(rel)?.Replace('\\', '/') is { Length: > 0 } dir ? dir : "—",
                    Url: $"/api/art/local/{Uri.EscapeDataString(rel)}");
            })
            .ToList();
    }

    /// <summary>local 文件夹里某个文件的磁盘路径；不在允许范围内时返回 null。</summary>
    public string? ResolveLocalPath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || _localDir is null)
        {
            return null;
        }

        // 只接受文件夹内的相对路径，避免越权读取。
        string full = Path.GetFullPath(Path.Combine(_localDir, id.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(_localDir);

        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return File.Exists(full) ? full : null;
    }

    /// <summary>原版卡图列表，按角色分组。</summary>
    public IReadOnlyList<CardArtSource> ListVanilla(string language)
    {
        var result = new List<CardArtSource>();
        Dictionary<string, string> names = LoadNames(language);

        foreach ((string path, PckFile.EntryInfo _) in LoadIndex())
        {
            // 形如 images/packed/card_portraits/<pool>/<stem>.png.import
            if (!path.StartsWith("images/packed/card_portraits/", StringComparison.Ordinal) ||
                !path.EndsWith(".png.import", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = path.Split('/');

            // 顶层还有 beta.png / ancient_beta.png 这类没有角色目录的，跳过。
            if (parts.Length < 5 || parts[3].Length == 0)
            {
                continue;
            }

            string pool = parts[3];
            string stem = Path.GetFileName(parts[^1]).Replace(".png.import", "");

            // beta 变体单独一个目录，避免和正式卡图重名。
            if (parts.Length >= 6 && parts[4] == "beta")
            {
                continue;
            }

            // 卡图和卡牌不是一一对应的：卡图按美术命名，有些名字在卡表里查不到。
            // 查到就用官方卡名，查不到就把文件名整理成可读形式。
            string entry = stem.ToUpperInvariant();
            string display = names.TryGetValue($"{entry}.title", out string? title)
                ? title
                : Humanize(stem);

            result.Add(new CardArtSource(
                Kind: "vanilla",
                Id: $"{pool}/{stem}",
                Display: display,
                Group: pool,
                Url: $"/api/art/vanilla/{Uri.EscapeDataString(pool)}/{Uri.EscapeDataString(stem)}"));
        }

        return result
            .OrderBy(r => r.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Display, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 取出原版卡图的图片字节（内嵌的 WebP 流）。
    ///
    /// 不解码像素：PCK 里的 .ctex 已经把 WebP 原样存着，直接取出来浏览器就能显示。
    /// </summary>
    public byte[]? ReadVanillaImage(string pool, string stem)
    {
        string stubPath = $"images/packed/card_portraits/{pool}/{stem}.png.import";

        if (!LoadIndex().TryGetValue(stubPath, out PckFile.EntryInfo? stub))
        {
            return null;
        }

        byte[] stubText = PckFile.ReadEntry(PckPath(), stub);
        string stubString = System.Text.Encoding.UTF8.GetString(stubText);

        // import 存根里的 path="res://....ctex" 才是真正的图片文件。
        string? ctexPath = ExtractValue(stubString, "path=\"res://", "\"");
        if (ctexPath is null || !LoadIndex().TryGetValue(ctexPath, out PckFile.EntryInfo? ctex))
        {
            return null;
        }

        byte[] ctexBytes = PckFile.ReadEntry(PckPath(), ctex);
        return ExtractEmbeddedWebp(ctexBytes);
    }

    /// <summary>把原版卡图另存为可用文件，返回其路径（供卡牌引用）。</summary>
    public string? MaterializeVanillaImage(string pool, string stem, string outputDir)
    {
        byte[]? webp = ReadVanillaImage(pool, stem);
        if (webp is null)
        {
            return null;
        }

        Directory.CreateDirectory(outputDir);
        string outPath = Path.Combine(outputDir, $"{pool}_{stem}.webp");
        File.WriteAllBytes(outPath, webp);
        return outPath;
    }

    /// <summary>.ctex 开头是 GST2，data_format=2 表示内嵌 WebP。</summary>
    private static byte[]? ExtractEmbeddedWebp(byte[] ctex)
    {
        if (ctex.Length < 56 ||
            ctex[0] != 'G' || ctex[1] != 'S' || ctex[2] != 'T' || ctex[3] != '2')
        {
            return null;
        }

        uint dataFormat = BitConverter.ToUInt32(ctex, 36);
        uint payloadSize = BitConverter.ToUInt32(ctex, 52);

        // 1 是内嵌 PNG（导出的引擎没有 PNG 解码器，所以游戏不用它），2 才是 WebP。
        if (dataFormat != 2 || 56 + payloadSize > ctex.Length)
        {
            return null;
        }

        return ctex.AsSpan(56, (int)payloadSize).ToArray();
    }

    /// <summary>
    /// 把下划线文件名整理成可读形式：bandage_up -> Bandage Up。
    /// 卡表里查不到名称时用它，比原始文件名易读。
    /// </summary>
    private static string Humanize(string stem)
    {
        string[] words = stem.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);

        return words.Length == 0
            ? stem
            : string.Join(' ', words.Select(w =>
                char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : string.Empty)));
    }

    private static string? ExtractValue(string text, string prefix, string suffix)
    {
        int start = text.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        int end = text.IndexOf(suffix, start, StringComparison.Ordinal);
        return end > start ? text[start..end] : null;
    }

    private string PckPath() => Path.Combine(_gameDir, "SlayTheSpire2.pck");

    /// <summary>PCK 目录表，读过一次就缓存（很大，不要重复读）。</summary>
    private Dictionary<string, PckFile.EntryInfo> LoadIndex()
    {
        if (_pckIndex is not null)
        {
            return _pckIndex;
        }

        var index = new Dictionary<string, PckFile.EntryInfo>(StringComparer.Ordinal);

        try
        {
            string pck = PckPath();
            if (File.Exists(pck))
            {
                foreach (PckFile.EntryInfo entry in PckFile.ReadDirectory(pck))
                {
                    index[entry.Path] = entry;
                }
            }
        }
        catch (Exception)
        {
            // 读不到就当作没有原版素材，界面仍可用。
        }

        _pckIndex = index;
        return index;
    }

    /// <summary>
    /// 原版卡名，用于在列表里显示可读名称而不是文件名。按语言缓存，切换语言时会重新读。
    /// </summary>
    private Dictionary<string, string> LoadNames(string language)
    {
        string code = GameLanguages.Normalize(language) ?? GameLanguages.Fallback;

        if (_namesByLanguage.TryGetValue(code, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            string pck = PckPath();
            if (File.Exists(pck))
            {
                var reader = new PckLocReader(pck);
                foreach ((string k, string v) in reader.LoadTable(code, "cards"))
                {
                    names[k] = v;
                }
            }
        }
        catch (Exception)
        {
            // 名称只是显示用，读不到就退回文件名。
        }

        _namesByLanguage[code] = names;
        return names;
    }
}
