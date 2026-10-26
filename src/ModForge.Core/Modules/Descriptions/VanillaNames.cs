using ModForge.Core.Pck;

namespace ModForge.Core.Modules.Descriptions;

/// <summary>
/// 原版内容的显示名查询。
///
/// 生成描述时需要把 <c>CARD.STRIKE_IRONCLAD</c> 这样的模型 id 写成玩家认得的名字
/// （"打击" / "Strike"），这要去游戏自己的本地化表里查。表在 PCK 里且未压缩，
/// 所以直接按键读取，不需要加载整张表。
///
/// 这是可选能力：拿不到游戏目录时退回按 id 反推的可读名字，功能不受影响。
/// </summary>
public sealed class VanillaNames
{
    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.Ordinal);
    private readonly string? _gameDir;

    private static readonly string[] Tables = { "cards", "powers", "relics", "potions", "enchantments", "orbs" };

    public VanillaNames(string? gameDir)
    {
        _gameDir = gameDir;
    }

    /// <summary>某个模型 id 在某语言下的显示名；查不到时返回 null。</summary>
    public string? Lookup(string modelId, string language)
    {
        if (string.IsNullOrWhiteSpace(modelId) || _gameDir is null)
        {
            return null;
        }

        // 语言代码先规范化，接受 en / zh 这类简写。
        string? code = GameLanguages.Normalize(language);
        if (code is null)
        {
            return null;
        }

        string entry = modelId.Contains('.') ? modelId[(modelId.LastIndexOf('.') + 1)..] : modelId;

        Dictionary<string, string>? table = LoadTable(code);
        if (table is null)
        {
            return null;
        }

        return table.TryGetValue($"{entry}.title", out string? title) ? title : null;
    }

    /// <summary>按需读入某语言的几张表，读过一次就缓存。</summary>
    private Dictionary<string, string>? LoadTable(string language)
    {
        string key = language;

        if (_tables.TryGetValue(key, out Dictionary<string, string>? cached))
        {
            return cached;
        }

        try
        {
            string pck = Path.Combine(_gameDir!, "SlayTheSpire2.pck");
            if (!File.Exists(pck))
            {
                _tables[key] = new Dictionary<string, string>(StringComparer.Ordinal);
                return null;
            }

            // 只保留需要的表，避免把整包读进内存。
            var reader = new PckLocReader(pck);
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string table in Tables)
            {
                foreach ((string k, string v) in reader.LoadTable(language, table))
                {
                    // 卡与状态可能同名键，先到的保留；cards 在 Tables 里排在前面。
                    merged.TryAdd(k, v);
                }
            }

            _tables[key] = merged;
            return merged;
        }
        catch (Exception)
        {
            // 游戏目录不可读时静默降级，描述仍会用 id 反推的名字。
            _tables[key] = new Dictionary<string, string>(StringComparer.Ordinal);
            return null;
        }
    }
}
