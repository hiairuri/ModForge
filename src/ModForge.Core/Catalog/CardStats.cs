namespace ModForge.Core.Catalog;

/// <summary>
/// 一张原版卡牌的数值定义，从游戏程序集里读出来。
///
/// 为什么必须读程序集：卡牌的费用、类型、数值都不在本地化表里。描述文本写的是
/// <c>造成{Damage:diff()}点伤害。</c> 这样的占位符，真实数字编译进了 <c>sts2.dll</c>。
/// 想显示"打击造成 6 点伤害"、想把原版卡当模板导入，就得从字节码里把这些常量取出来。
/// </summary>
public sealed record CardStats
{
    /// <summary>本地化条目的键，例如 STRIKE_IRONCLAD。</summary>
    public required string Entry { get; init; }

    /// <summary>游戏里的类型名，例如 StrikeIronclad。</summary>
    public required string ClassName { get; init; }

    public required int EnergyCost { get; init; }

    /// <summary>费用是 X（打出时消耗全部能量）。</summary>
    public bool EnergyCostX { get; init; }

    /// <summary>attack / skill / power / status / curse / quest / none。</summary>
    public required string CardType { get; init; }

    /// <summary>basic / common / uncommon / rare / ancient / event / token / status / curse / quest / none。</summary>
    public required string Rarity { get; init; }

    /// <summary>self / any_enemy / all_enemies / random_enemy / …。</summary>
    public required string Target { get; init; }

    /// <summary>能否升级。</summary>
    public bool Upgradable { get; init; }

    /// <summary>卡面上声明的数值，例如 Damage=6。</summary>
    public IReadOnlyList<CardStatVar> Vars { get; init; } = Array.Empty<CardStatVar>();

    /// <summary>卡牌自带的关键词，例如 EXHAUST。</summary>
    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    /// <summary>升级带来的变化。</summary>
    public CardStatUpgrade Upgrade { get; init; } = CardStatUpgrade.Empty;

    /// <summary>牌池 / 角色，从卡图路径推断（ironclad、colorless…）。</summary>
    public string? Pool { get; init; }

    /// <summary>卡图文件名（不含扩展名）。</summary>
    public string? ArtStem { get; init; }

    /// <summary>
    /// 该卡的数值无法从字节码静态求出（例如"造成等于当前格挡的伤害"）。
    /// 这类卡不是读取失败，而是数值本来就取决于战况；界面要如实说明而不是显示 0。
    /// </summary>
    public bool HasComputedValues { get; init; }

    /// <summary>某个数值的名字对应的基础值，读不到时返回 null。</summary>
    public double? Var(string key) =>
        Vars.FirstOrDefault(v => string.Equals(v.Key, key, StringComparison.Ordinal))?.Value;
}

/// <summary>卡面声明的一个数值。</summary>
public sealed record CardStatVar(string Kind, string Key, double? Value);

/// <summary>升级带来的变化：费用增减、数值增减、关键词增删。</summary>
public sealed record CardStatUpgrade
{
    public static readonly CardStatUpgrade Empty = new();

    public int EnergyDelta { get; init; }

    public IReadOnlyDictionary<string, double> VarDeltas { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);

    public IReadOnlyList<string> AddedKeywords { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RemovedKeywords { get; init; } = Array.Empty<string>();

    /// <summary>升级后有意义的数值：基础值 + 增量。</summary>
    public double? UpgradedVar(CardStats card, string key)
    {
        double? baseValue = card.Var(key);

        if (baseValue is null)
        {
            return null;
        }

        return baseValue.Value + (VarDeltas.TryGetValue(key, out double delta) ? delta : 0);
    }
}

/// <summary>从游戏程序集读到的全部卡牌数据，按版本缓存。</summary>
public sealed record CardStatsTable
{
    public required string GameVersion { get; init; }

    public string? Commit { get; init; }

    public required IReadOnlyDictionary<string, CardStats> ByEntry { get; init; }

    public CardStats? Find(string entry) =>
        ByEntry.TryGetValue(entry, out CardStats? stats) ? stats : null;
}
