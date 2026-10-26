using ModForge.Core.Model;

namespace ModForge.Core.Modules.Cards;

/// <summary>
/// 一张卡的**固有属性**——"这张牌本身是什么"，与它有哪些效果无关。
///
/// 收成一个模块是因为这些东西散在四处各判一遍：模型（<see cref="CardDef"/>）、生成器
/// （构造器 / 关键词 / X 费 / 升级）、校验器、界面表单。散着改一处就会漏掉几处，
/// 而且漏掉的表现往往是"填了没反应"——最难发现的那种。
///
/// 这是 <see cref="CardDef"/> 的**视图**，不是新的存档形状：工程 JSON 不变，
/// 所以既有工程照旧能用。视图的价值在于把"默认值归一到一处"（例如星辰费用
/// <c>-1</c> 表示"没有星辰费用"）与"哪些属性真的会被生成"集中表达。
/// </summary>
/// <param name="Id">标识（决定类名与本地化条目）。</param>
/// <param name="EnergyCost">能量费用。X 费时这个值不参与生成（构造器传 0）。</param>
/// <param name="StarCost">星辰费用；<c>-1</c> 表示没有星辰费用（游戏的约定）。</param>
/// <param name="UpgradeCostDelta">升级时能量费用的变化量（增量语义，原版"痛击"是 -2）。</param>
/// <param name="UpgradeStarDelta">升级时星辰费用的变化量（增量语义）。</param>
/// <param name="EnergyCostX">能量费用是 X。</param>
/// <param name="StarCostX">星辰费用是 X。</param>
/// <param name="Keywords">关键词（消耗 / 虚无 / 固有 / 保留 / 奇巧 / 不能打出 / 永恒）。</param>
/// <param name="Type">卡牌类型。</param>
/// <param name="Rarity">稀有度。</param>
/// <param name="Target">目标类型。</param>
/// <param name="ReplayCount">这张牌自己的重放次数（0 = 不重放）。</param>
/// <param name="MaxUpgradeLevel">最多能升几级。</param>
/// <param name="Multiplayer">按玩家人数的限制（界面上叫「多人游戏卡牌」）。</param>
public sealed record CardCore(
    string Id,
    int EnergyCost,
    int StarCost,
    int UpgradeCostDelta,
    int UpgradeStarDelta,
    bool EnergyCostX,
    bool StarCostX,
    IReadOnlyList<CardKeyword> Keywords,
    CardType Type,
    CardRarity Rarity,
    CardTargetType Target,
    int ReplayCount,
    int MaxUpgradeLevel,
    CardMultiplayerConstraint Multiplayer)
{
    /// <summary>有没有星辰费用。X 星时由 <see cref="StarCostX"/> 表达，不看数值。</summary>
    public bool HasStarCost => !StarCostX && StarCost >= 0;

    /// <summary>构造器里给游戏传的能量费用：X 费时传 0（费用由打出时的消耗量决定）。</summary>
    public int ConstructorEnergyCost => EnergyCostX ? 0 : EnergyCost;

    /// <summary>从卡牌定义读出固有属性，顺带把默认值归一（例如 X 与数值互斥时的取值）。</summary>
    public static CardCore From(CardDef card) => new(
        Id: card.Id,
        EnergyCost: card.EnergyCost,
        StarCost: card.StarCost,
        UpgradeCostDelta: card.UpgradeCostDelta,
        UpgradeStarDelta: card.UpgradeStarDelta,
        EnergyCostX: card.EnergyCostX,
        StarCostX: card.StarCostX,
        Keywords: card.Keywords,
        Type: card.Type,
        Rarity: card.Rarity,
        Target: card.Target,
        ReplayCount: card.ReplayCount,
        MaxUpgradeLevel: card.MaxUpgradeLevel,
        Multiplayer: card.Multiplayer);

    /// <summary>有没有升级时要改的**费用**（能量或星辰）。</summary>
    public bool HasCostUpgrade => UpgradeCostDelta != 0 || UpgradeStarDelta != 0;
}
